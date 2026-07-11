using System;
using System.Collections.Generic;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

// 阵营枚举 Faction 与敌对矩阵 Factions.IsHostile 见 Factions.cs（纯 C#、可入单测）。

/// <summary>
/// 幸存者与丧尸的共同基类：带寻路的躯体，能被结算伤害。
/// 逻辑/物理/寻路全在 cartesian 平面（本节点挂不可见 LogicLayer 下、自身不作视觉）；
/// 视觉由 <see cref="ActorSprite"/> 承担——_Ready 时在 <c>iso_layer</c> group 下挂一个程序化人形，
/// 每帧 <c>Iso.Project(GlobalPosition)</c> 定位、读本 Actor 暴露的数据
/// （<see cref="FacingAngle"/>/<see cref="HealthFraction"/>/<see cref="Selected"/>/<see cref="BodyTint"/>）绘制。
/// 这是伪等距解耦：物理留 top-down cartesian，渲染投到 iso（PZ 做法）。
/// 移动走 NavigationAgent2D 寻路（开避障）+ CharacterBody2D 物理碰撞（撞墙 + 角色互撞）。
/// </summary>
public abstract partial class Actor : CharacterBody2D
{
    // ---- 基础属性 ----
    public Faction Faction { get; protected set; }
    public float Radius { get; protected set; } = 12f;

    /// <summary>防御方躯体（细部位 HP/切除/损毁/失血）。由子类工厂在 Create 时装配。</summary>
    protected Body Body = null!;

    /// <summary>非战斗致死标记（如饿死）：绕过 Body 战斗死亡表示，走统一死亡路径。</summary>
    private bool _nonCombatDead;

    /// <summary>存活 = 躯体未死（致死部位归零/斩首/开膛/失血致死皆算死）且未被非战斗死因（饿死）判亡。</summary>
    public bool Alive => Body is { IsDead: false } && !_nonCombatDead;

    /// <summary>
    /// 饥饿对战斗能力的惩罚净值 0~1（1=完全丧失）。基类（丧尸等无饥饿单位）恒为 0；
    /// <see cref="Pawn"/> 覆盖返回其饥饿刻度惩罚，在攻速/移速消费点与残疾惩罚经
    /// <see cref="HungerState.CombineCapability"/> 合并（不覆盖、不改残疾数学）。
    /// </summary>
    protected virtual double HungerAbilityPenalty => 0.0;

    /// <summary>
    /// 非战斗致死（如饿死）：标记本 Actor 已亡并走统一死亡路径（触发 Died 事件 + QueueFree），
    /// 不写 Body 的战斗死亡状态（那是战斗引擎语义）。已死则幂等无操作。
    /// </summary>
    protected void KillNonCombat()
    {
        if (!Alive)
        {
            return;
        }
        _nonCombatDead = true;
        Die();
    }

    protected Color BodyColor = Colors.White;
    protected float MoveSpeed = 90f;
    protected virtual bool CanAct => true;

    /// <summary>
    /// 当前持握态，供攻速/误差角消费（<see cref="GripCombat"/>）。<see cref="Pawn"/> 走其 <see cref="Pawn.Grip"/>
    /// （左右手持械推导）；其余单位（丧尸/劫掠者等无持械模型）恒 <see cref="GripMode.OneHanded"/>（系数 ×1.0，零回归）。
    /// 说明：Pawn 已非虚暴露 public Grip 且其文件为只读边界，无法沿 HungerAbilityPenalty 的虚钩子路径（基类同名虚属性会被
    /// Pawn 的 public 隐藏而非覆盖），故此处以类型判定就地取用，行为与「Pawn override 返回 Grip」完全等价。
    /// </summary>
    protected GripMode ActiveGrip => this is Pawn pawn ? pawn.Grip : GripMode.OneHanded;

    // ---- 供 ActorSprite 读取的视觉数据（Actor 自身不再绘制人形）----
    /// <summary>面朝方向（cartesian 弧度）。移动时=速度方向；攻击时=指向目标；空闲保持上一次。
    /// ActorSprite 会把它经 <c>Iso.Project</c> 转到 iso 屏幕空间再平滑旋转（自由角度、无离散帧）。</summary>
    public float FacingAngle { get; private set; }

    /// <summary>人形主色调（丧尸昼夜会改 <see cref="BodyColor"/>，sprite 每帧取用自动跟随）。</summary>
    public Color BodyTint => BodyColor;

    /// <summary>是否远程持械（sprite 据此画枪管指示 vs 近战短刃）。</summary>
    public bool RangedArmed => IsRanged;

    /// <summary>血量比：取"部位血量比"与"储血比"的较小值，两条致死轴任一走低都反映。</summary>
    public float HealthFraction =>
        Body is null ? 1f : (float)Mathf.Clamp(Mathf.Min(PartHealthRatio(), Body.BloodRatio), 0, 1);

    // ---- 战斗（作为防御方的护甲；作为攻击方的武器 + 手感参数） ----
    protected Weapon AttackWeapon = null!;
    protected float AttackRange = 32f;
    protected double AttackCooldown = 1.0;
    /// <summary>远程武器：攻击时发射锥形散布弹道子弹，而非近战瞬时结算。</summary>
    protected bool IsRanged;
    /// <summary>
    /// 远程武器"贴脸"阈值（cartesian 像素，拟定待调）：目标 body 边缘进此间隙内改用枪托近战钝击而非开火。
    /// 采边缘间隙口径（+双方半径，与本文件其余交战判定一致）——确保近战敌(丧尸/劫掠者贴到约边缘 24~26px)贴身时可靠切枪托。
    /// </summary>
    private const float PointBlankRange = 40f;
    protected IReadOnlyList<ArmorLayer> DefenderArmor = Array.Empty<ArmorLayer>();

    private double _attackTimer;

    /// <summary>震荡硬打断剩余时长（秒）；&gt;0 时不能出手、移速×ConcussionMoveSlowFactor。见 <see cref="ReceiveAttack"/>。</summary>
    private double _concussionTimer;

    /// <summary>震荡抗性剩余时长（秒，覆盖打断+首轮重走冷却）；&gt;0 时再次被震荡的触发概率×ConcussionResistFactor（防死锁）。</summary>
    private double _concussionResistTimer;

    /// <summary>震荡/骨折效果参数（与引擎结算同源=EffectConfig.Default，实时消费点只读）。</summary>
    private static readonly EffectConfig CombatEffectCfg = EffectConfig.Default();

    // ---- 指令目标 ----
    private Vector2? _moveTarget;
    private Actor? _attackTarget;

    /// <summary>
    /// 防走A：自上次进入稳定攻击（在射程内停下开打）以来是否移动过——玩家给移动令、或实际位移逼近目标都算。
    /// 攻击指令的冷却重置决策（<see cref="AttackCommandState.ShouldResetCooldown"/>）据此判定「移动后再攻击→重新 wind-up」。
    /// 在稳定攻击帧清零，逼近/移动令置位，故右键狂点同一目标（stationary 攻击态）不会误判为移动。
    /// </summary>
    private bool _movedSinceCommand;

    // ---- 依赖（由 Main 注入） ----
    protected CombatEngine Combat = null!;
    protected GameClock Clock = null!;

    private NavigationAgent2D _agent = null!;

    // ---- 视觉 sprite 挂载（挂到 iso_layer group 下；B1 并发提供该 group，worktree 独测可能缺失）----
    private ActorSprite? _sprite;
    private int _spriteRetries;

    // ---- 头顶状态图标条（E④）：与 sprite 同挂 iso_layer（Actor 本体在不可见 LogicLayer，做子节点会不可见）----
    private StatusIconStrip? _statusStrip;

    public event Action<Actor>? Died;

    public void Inject(CombatEngine combat, GameClock clock)
    {
        Combat = combat;
        Clock = clock;
    }

    public override void _Ready()
    {
        // 碰撞体（撞墙）。
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = Radius } };
        AddChild(shape);

        _agent = new NavigationAgent2D
        {
            PathDesiredDistance = 4f,
            TargetDesiredDistance = 6f,
            // 角色互撞后 RimWorld 群体右键会互顶死——开 RVO 避障做局部分离缓解。
            AvoidanceEnabled = true,
            Radius = Radius + 2f,           // 分离半径（略大于碰撞圆）
            NeighborDistance = 64f,
            MaxNeighbors = 8,
            TimeHorizonAgents = 1.0f,
        };
        AddChild(_agent);
        // 避障会算出安全速度经此信号回吐，我们据此 MoveAndSlide（未连信号则 SetVelocity 无效）。
        _agent.VelocityComputed += OnVelocityComputed;

        ApplyFactionCollision();

        OnReady();
    }

    // 碰撞层位：幸存者层1(0b0001)/丧尸层2(0b0010)/墙层3(0b0100)/劫掠者层4(0b1000)。
    // 既作点选命中区，也作物理体。命中（弹道）靠 Projectile 的射线 HitMask 覆盖全阵营层 + 墙，
    // 敌我由 Factions.IsHostile 裁定；这里的物理 mask 只管"移动分离"（同阵营互挤开、穿过异阵营）。
    private const uint LayerSurvivor = 0b0001u;
    private const uint LayerZombie = 0b0010u;
    private const uint LayerWall = 0b0100u;
    private const uint LayerRaider = 0b1000u;

    /// <summary>
    /// 按当前 <see cref="Faction"/> 设定碰撞层与 mask。<c>_Ready</c> 与运行时 <see cref="SetFaction"/> 共用，
    /// 保证切阵营后碰撞立即生效。物理 mask 口径（用户反馈#4：角色不穿透）：mask = 墙 + 本阵营层
    /// → 同阵营互挤开、不重叠堆一坨，仍可穿过异阵营（近战靠射程结算、不必贴身）。碰撞形状仍 cartesian 圆（PZ 做法）。
    /// </summary>
    private void ApplyFactionCollision()
    {
        switch (Faction)
        {
            case Faction.Survivor:
                CollisionLayer = LayerSurvivor;
                CollisionMask = LayerWall | LayerSurvivor; // 0b0101
                break;
            case Faction.Zombie:
                CollisionLayer = LayerZombie;
                CollisionMask = LayerWall | LayerZombie;   // 0b0110
                break;
            case Faction.Raider:
                CollisionLayer = LayerRaider;
                CollisionMask = LayerWall | LayerRaider;   // 0b1100
                break;
        }
    }

    /// <summary>
    /// 运行时改阵营（如克莉丝汀反水）：改字段并**立即重设碰撞层/mask**，使新的敌我分离即刻生效。
    /// 命中/友伤走 <see cref="Factions.IsHostile"/>，无需缓存。节点未入树（_Ready 前）调用也安全——属性可预设，
    /// _Ready 会再走一次 <see cref="ApplyFactionCollision"/>。
    /// </summary>
    public void SetFaction(Faction faction)
    {
        Faction = faction;
        ApplyFactionCollision();
    }

    /// <summary>子类初始化钩子（在基类节点搭好后调用）。</summary>
    protected virtual void OnReady() { }

    // ---- 指令接口 ----

    public void CommandMoveTo(Vector2 worldPos)
    {
        _attackTarget = null;
        _moveTarget = worldPos;
        _movedSinceCommand = true; // 玩家给移动令：下次攻击须重新 wind-up
        _agent.TargetPosition = worldPos;
    }

    public void CommandAttack(Actor target)
    {
        if (target == this || !target.Alive)
        {
            return;
        }

        // 防走A：决定本次攻击指令是否重置冷却（先冷却再打第一下）。
        //  非攻击态 / 切换目标 / 移动过 → 重置为满有效间隔（重新 wind-up）；
        //  已在攻击态 + 同一目标 + 未移动（右键狂点同一目标）→ 保持冷却，忽略重复指令，避免一直卡冷却。
        bool isAttacking = _attackTarget != null;
        bool sameTarget = ReferenceEquals(_attackTarget, target);
        if (AttackCommandState.ShouldResetCooldown(isAttacking, sameTarget, _movedSinceCommand))
        {
            _attackTimer = EffectiveAttackInterval();
            _movedSinceCommand = false; // 已把「移动过」折进这次 wind-up，清零等待下次稳定攻击/移动再判
        }
        _attackTarget = target;
        _moveTarget = null;
    }

    /// <summary>撤销一切指令（丧尸白天休眠时用）。</summary>
    public void CancelOrders()
    {
        _attackTarget = null;
        _moveTarget = null;
        Velocity = Vector2.Zero;
    }

    protected Actor? CurrentAttackTarget => _attackTarget;
    protected bool HasMoveOrder => _moveTarget.HasValue;

    // ---- 每帧决策：子类填 AI（丧尸游荡/追击；幸存者留空由玩家指令驱动） ----
    protected virtual void Think(double delta) { }

    public override void _PhysicsProcess(double delta)
    {
        // 视觉 sprite 延迟挂载：iso_layer group 可能尚未就位（B1 并发挂载/worktree 独测缺失）——
        // 每物理帧重试，成功即停；到上限只告警不崩（防御，属正常）。
        if (_sprite == null && _spriteRetries < 120)
        {
            TryAttachSprite();
        }

        if (!Alive)
        {
            return;
        }

        // 持续失血推进（吃已缩放 delta：暂停冻结、加速档更快）。可能因失血致死。
        if (Body.BleedingWoundCount > 0)
        {
            Body.TickBleed(delta);
            if (!Alive)
            {
                Die();
                return;
            }
        }

        Think(delta);

        if (!CanAct)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }

        if (_attackTimer > 0)
        {
            _attackTimer -= delta;
        }

        // 震荡硬打断/抗性窗随时间衰减（打断期不能出手由 TryAttack 门控，移速惩罚在移动消费点叠乘）。
        if (_concussionTimer > 0)
        {
            _concussionTimer -= delta;
        }

        if (_concussionResistTimer > 0)
        {
            _concussionResistTimer -= delta;
        }

        // 有攻击目标：目标死了则清空；在射程内停下开打，否则寻路逼近。
        if (_attackTarget is { } tgt)
        {
            if (!tgt.Alive)
            {
                _attackTarget = null;
            }
            else
            {
                float dist = GlobalPosition.DistanceTo(tgt.GlobalPosition);
                // 交战/停下距离：远程按武器 MaxRange 判（进射程即停下开火，贴脸时 TryAttack 再切枪托近战）；
                // 近战按有效射程边缘（AttackRange 含守卫岗位加成）。远程武器的开火距离权威口径 = MaxRange，非 AttackRange。
                bool inEngage = IsRanged
                    ? Ballistics.InRange(EffectiveRangeDistance(dist), AttackWeapon)
                    : dist <= AttackRange + Radius + tgt.Radius;
                if (inEngage)
                {
                    Vector2 aim = tgt.GlobalPosition - GlobalPosition;
                    if (aim != Vector2.Zero)
                    {
                        FacingAngle = aim.Angle(); // 停下开打：面朝目标
                    }
                    Velocity = Vector2.Zero;
                    MoveAndSlide();
                    _movedSinceCommand = false; // 在射程内停下开打：稳定攻击态，重置「移动过」基线（右键狂点不再误判）
                    TryAttack(tgt);
                    return;
                }
                _movedSinceCommand = true; // 出射程、寻路逼近目标：位移了，下次攻击须重新 wind-up
                _agent.TargetPosition = tgt.GlobalPosition;
            }
        }

        // 寻路移动。
        if (NavigationServer2D.MapGetIterationId(_agent.GetNavigationMap()) == 0)
        {
            return;
        }
        if (_attackTarget == null && !_moveTarget.HasValue)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }
        if (_agent.IsNavigationFinished())
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            _moveTarget = null;
            return;
        }

        Vector2 next = _agent.GetNextPathPosition();
        Vector2 dir = GlobalPosition.DirectionTo(next);
        if (dir != Vector2.Zero)
        {
            FacingAngle = dir.Angle(); // 移动：面朝前进方向
        }
        // 残疾移动惩罚：移动能力 = 1 − MobilityPenalty（断腿/断趾净值，Body.RecalculatePenalties 实时重算）。
        // 惩罚 0.5 → 半速；能力 ≤0（惩罚 ≥1，如断双腿）→ 完全无法移动（期望速度归零）。
        // 再乘饥饿因子：有效能力 = (1−残疾) × (1−饥饿)，饿越狠移动越慢（丧尸 HungerAbilityPenalty=0，等价原样）。
        double mobility = HungerState.CombineCapability(
            Body.DisabilityModifiers.MobilityPenalty, HungerAbilityPenalty);
        // 腿/脚骨折 → 移动能力×0.7（未治疗）/×0.85（已治疗，用户口径）；多处乘算叠加、锁下限。与残疾/饥饿相互独立叠乘。
        mobility *= Body.LegFractureMobilityFactor(
            CombatEffectCfg.LegFractureMobilityMult, CombatEffectCfg.LegFractureHealedMobilityMult,
            CombatEffectCfg.FractureCapabilityFloor);
        // 震荡硬打断期 → 移速×0.1（−90%，限时，用户口径）。
        if (_concussionTimer > 0)
        {
            mobility *= CombatEffectCfg.ConcussionMoveSlowFactor;
        }

        Vector2 desired = mobility > 0 ? dir * MoveSpeed * (float)mobility : Vector2.Zero;
        // 把期望速度交给避障；OnVelocityComputed 收到安全速度后再 MoveAndSlide。
        _agent.Velocity = desired;
    }

    /// <summary>避障算出的安全速度回调：真正驱动位移。未开避障时不会触发。</summary>
    private void OnVelocityComputed(Vector2 safeVelocity)
    {
        if (!Alive)
        {
            return;
        }
        Velocity = safeVelocity;
        MoveAndSlide();
    }

    /// <summary>在 iso_layer group 下挂人形 sprite；group 未就位则记一次重试。</summary>
    private void TryAttachSprite()
    {
        _spriteRetries++;
        if (GetTree().GetFirstNodeInGroup("iso_layer") is Node2D layer)
        {
            _sprite = new ActorSprite();
            layer.AddChild(_sprite);
            _sprite.Bind(this);

            // 头顶状态图标条（E④）：只读自身 Body 拍出的 PawnInspection 快照——就地捕获受保护的
            // Body/武器/护甲构造委托（strip 拿不到可变引擎对象，改不坏战斗）。名字/饥饿对状态图标无关，省略。
            _statusStrip = new StatusIconStrip();
            layer.AddChild(_statusStrip);
            _statusStrip.Bind(this, () => PawnInspection.FromBody(Body, AttackWeapon, DefenderArmor, ""));
        }
        else if (_spriteRetries == 120)
        {
            GD.PushWarning(
                "[ActorSprite] 未找到 'iso_layer' group，人形未挂载" +
                "（worktree 独立构建/合并前属正常，合并后由 B1 提供该 group）。");
        }
    }

    /// <summary>
    /// 当前有效出手间隔 = 基础冷却 / (操作能力 × 持握攻速系数)。操作能力 = 残疾×饥饿 经
    /// <see cref="HungerState.CombineCapability"/> 合并（与 <c>TryAttack</c> 计时器赋值同源，不改那套乘法）；
    /// 再乘 <see cref="ActiveGrip"/> 的持握系数（双手 1.15→更短、双持 0.70→更长、单手不变）。供 wind-up 重置冷却复用。
    /// 操作能力 ≤0（断双手等无法出手）时回落基础冷却保持正值——此时 TryAttack 本就会跳过出手。
    /// <paramref name="baseCooldown"/> 为空时用 <see cref="AttackCooldown"/>（=主手武器间隔，wind-up 用）；
    /// TryAttack 出手时传入**当前生效武器**间隔（贴脸枪托则为枪托 StockMeleeInterval），使冷却随实际打出的武器走。
    /// </summary>
    private double EffectiveAttackInterval(double? baseCooldown = null)
    {
        double operation = HungerState.CombineCapability(
            Body.DisabilityModifiers.OperationPenalty, HungerAbilityPenalty);
        // 手部骨折 → 操作能力×0.7（未治疗）/×0.85（已治疗，含攻速，持久，用户口径）；多处乘算叠加、锁下限。对齐 Duel.EffectiveInterval。
        operation *= Body.HandFractureOperationFactor(
            CombatEffectCfg.HandFractureOperationMult, CombatEffectCfg.HandFractureHealedOperationMult,
            CombatEffectCfg.FractureCapabilityFloor);
        return GripCombat.EffectiveInterval(baseCooldown ?? AttackCooldown, operation, ActiveGrip);
    }

    private void TryAttack(Actor target)
    {
        // 震荡硬打断期：不能出手（冷却计时照常推进，打断结束后仍需走完重置进去的满冷却）。
        if (_attackTimer > 0 || _concussionTimer > 0)
        {
            return;
        }

        // 远程武器出手前先定"打什么"：
        // ①贴脸（≤PointBlankRange）→ 改枪托钝击近战（MeleeProfile：必中、无误差角、低伤慢速），不开火；
        // ②目标超出武器 MaxRange → 本次不出手也不消耗冷却（正常由 _PhysicsProcess 的 MaxRange 交战门先挡下、寻路逼近，此为兜底）。
        Weapon weapon = AttackWeapon;
        bool fireRanged = IsRanged;
        if (IsRanged)
        {
            float dist = GlobalPosition.DistanceTo(target.GlobalPosition);
            if (!Ballistics.InRange(EffectiveRangeDistance(dist), AttackWeapon))
            {
                return;
            }
            if (dist <= PointBlankRange + Radius + target.Radius && AttackWeapon.MeleeProfile() is { } stock)
            {
                weapon = stock;
                fireRanged = false;
            }
        }

        // 残疾操作惩罚：操作能力 = 1 − OperationPenalty（断手/断指净值，实时重算）。对齐 Duel.EffectiveInterval 口径：
        // 有效间隔 = 基础 / 操作能力（惩罚 0.5 → 间隔翻倍）；能力 ≤0（惩罚 ≥1，如断双手）→ 无法出手，跳过本次攻击、
        // 计时器不动（避免除零变 NaN/负值），下帧再判。
        // 再乘饥饿因子：有效能力 = (1−残疾) × (1−饥饿)，饿越狠出手越慢（丧尸 HungerAbilityPenalty=0，等价原样）。
        double operation = HungerState.CombineCapability(
            Body.DisabilityModifiers.OperationPenalty, HungerAbilityPenalty);
        if (operation <= 0)
        {
            return;
        }
        // 冷却随当前生效武器（远程/近战/贴脸枪托）的间隔走，读 WeaponTable 权威值，而非旧的硬编码手感常量。
        _attackTimer = EffectiveAttackInterval(weapon.AttackInterval);
        // 连发模型：冷却在整轮连发之后才起算——把连发跨度补进本次冷却，下一轮不与本轮连发重叠。
        if (fireRanged && AttackWeapon.BurstCount > 1)
        {
            _attackTimer += (AttackWeapon.BurstCount - 1) * System.Math.Max(0, AttackWeapon.BurstInterval);
        }

        if (fireRanged)
        {
            FireProjectile(target);
        }
        else
        {
            // 贴脸枪托或本就近战武器：必中近战结算（枪托为上面派生的 MeleeProfile）。
            target.ReceiveAttack(this, weapon, Combat);
        }
    }

    private void FireProjectile(Actor target)
    {
        // 一次"射击"= BurstCount 发（默认 1）。首发立即打出，其余 BurstInterval 间隔后逐发补上。
        // 每发独立锥采样、独立重新瞄准目标当前位置（连发跟枪）。冷却在 TryAttack 已含连发跨度。
        Weapon weapon = AttackWeapon;   // 捕获当前武器，避免连发途中换枪串味
        FireOneRound(target, weapon);

        int burst = System.Math.Max(1, weapon.BurstCount);
        if (burst <= 1)
        {
            return;
        }

        double gap = System.Math.Max(0, weapon.BurstInterval);
        for (int k = 1; k < burst; k++)
        {
            SceneTreeTimer t = GetTree().CreateTimer(gap * k);
            t.Timeout += () =>
            {
                // 连发途中射手/目标可能已死或被释放：逐发校验，失效则中止本发。
                if (!IsInstanceValid(this) || Body.IsDead || !IsInstanceValid(target))
                {
                    return;
                }

                FireOneRound(target, weapon);
            };
        }
    }

    private void FireOneRound(Actor target, Weapon weapon)
    {
        Vector2 toTarget = target.GlobalPosition - GlobalPosition;
        if (toTarget == Vector2.Zero)
        {
            return;
        }

        // 误差角锥采样：以指向目标的准星方向为轴，在武器基础误差角内随机一个射击方向。
        // 命中判定交给弹道实时层（路径首个碰撞体，含撞墙落空）——引擎只出纯函数采样方向。
        // 双持两把单手武器时误差角 ×1.25（远程双持代价）；单手/双手一把不变。近战不经此路径。
        double spread = GripCombat.EffectiveSpreadDegrees(weapon.BaseSpreadDegrees, ActiveGrip);
        double aimDeg = Mathf.RadToDeg(toTarget.Angle());
        double shotDeg = Combat.SampleShotDirectionDegrees(aimDeg, spread);
        Vector2 dir = Vector2.FromAngle(Mathf.DegToRad((float)shotDeg));

        // 子弹最大飞行距离对齐武器 MaxRange × 岗位射程倍率（开火判定同源；超此距离衰减归 0）。
        // 无射程模型的远程武器（罕见）回落到旧的 AttackRange*1.25 兜底（同样吃岗位倍率）。
        float maxDist = (float)((weapon.MaxRange ?? (AttackRange * 1.25)) * _postRangeMultiplier);
        Projectile.Spawn(GetParent(), GlobalPosition + dir * (Radius + 2f), dir,
            maxDist, weapon, Combat, this, _postRangeMultiplier);
    }

    // ---- 守卫岗位加成（D 守卫防御战）：只读取岗位属性作用到战斗参数，不改既有攻防逻辑 ----
    private const float GuardBaseSightRadius = 200f; // 守卫巡防基础锁敌半径下限（拟定待调）
    // 岗位射程倍率（仅远程守卫，作用到有效开火射程 MaxRange；近战恒 1）。默认 1=非守卫/未上岗，零回归。
    private float _postRangeMultiplier = 1f;
    private float _postSightMultiplier = 1f; // 岗位视野倍率（作用到 GuardSightRadius；默认 1）
    private float _postBlockChance;          // 哨塔围栏远程抵挡几率（默认 0=不抵挡）
    private bool _firstStrikeAvailable;

    /// <summary>是否有存活的攻击目标（供 CampMain 巡防锁敌判定是否需补目标）。</summary>
    public bool HasActiveTarget => CurrentAttackTarget is { Alive: true };

    /// <summary>
    /// 岗位射程倍率下的等效距离（distance / 倍率）：把实际距离压回武器原生射程曲线，据此复用
    /// <see cref="Ballistics"/> 的开火/衰减判定而无需改引擎。非守卫/近战倍率=1 → 原样（零回归）。
    /// </summary>
    private double EffectiveRangeDistance(double dist) =>
        GuardPostMath.EffectiveRangeDistance(dist, _postRangeMultiplier);

    /// <summary>
    /// 守卫有效锁敌半径 = max(基础下限, 本单位有效开火距离) × 岗位视野倍率。有效开火距离：远程取
    /// MaxRange×岗位射程倍率、近战取 AttackRange——确保远射目标能被锁定并真正开火（而非锁到却打不着）。
    /// </summary>
    public float GuardSightRadius
    {
        get
        {
            float reach = IsRanged && AttackWeapon.MaxRange is double mr && mr > 0
                ? (float)(mr * _postRangeMultiplier)
                : AttackRange;
            return GuardPostMath.EffectiveSight(Mathf.Max(GuardBaseSightRadius, reach), _postSightMultiplier);
        }
    }

    /// <summary>
    /// 上岗：把岗位属性叠加到本 Actor 的战斗参数。射程倍率仅作用于远程守卫（近战无 MaxRange 开火模型，
    /// 不加射程）；视野倍率全适用；哨塔挂围栏远程抵挡；暗哨挂首发。近战守卫射程不变、仅视野随巡防半径 ×倍率。
    /// </summary>
    public void ApplyGuardPost(GuardPostStats stats)
    {
        ClearGuardPost();
        _postRangeMultiplier = IsRanged ? stats.RangeMultiplier : 1f;
        _postSightMultiplier = stats.SightMultiplier;
        _postBlockChance = stats.BlockChance;
        _firstStrikeAvailable = stats.FirstStrike;
    }

    /// <summary>下岗：撤销岗位加成，恢复原始战斗参数。</summary>
    public void ClearGuardPost()
    {
        _postRangeMultiplier = 1f;
        _postSightMultiplier = 1f;
        _postBlockChance = 0f;
        _firstStrikeAvailable = false;
    }

    /// <summary>
    /// 暗哨首发：驻守后对首个来袭目标立即无冷却打一击（一次性，打完清标记）。
    /// **只在目标进入武器射程内才触发**（口径对齐 <see cref="_PhysicsProcess"/> 的命中判定），
    /// 射程外不凭空先手、也不消耗首发标记。复用既有 <see cref="FireProjectile"/>/<see cref="ReceiveAttack"/>，不改战斗规则。返回是否触发。
    /// </summary>
    public bool TryFirstStrike(Actor target)
    {
        if (!_firstStrikeAvailable || !Alive || target is not { Alive: true })
        {
            return false;
        }
        // 射程门：超出武器可及距离则不触发（也不消耗标记），等目标真正逼近再打这记先手。
        // reach 对齐武器射程——远程用 MaxRange×岗位射程倍率（中心距，同 Ballistics.InRange 口径，不叠半径）；
        // 近战用 AttackRange 边缘（叠双方半径，同近战交战门口径）。
        double reachBase = GuardPostMath.FirstStrikeReach(
            IsRanged, AttackWeapon.MaxRange, AttackRange, _postRangeMultiplier);
        double limit = IsRanged ? reachBase : reachBase + Radius + target.Radius;
        if (GlobalPosition.DistanceTo(target.GlobalPosition) > limit)
        {
            return false;
        }
        _firstStrikeAvailable = false;
        if (IsRanged)
        {
            FireProjectile(target);
        }
        else
        {
            target.ReceiveAttack(this, AttackWeapon, Combat);
        }
        return true;
    }

    /// <summary>
    /// 作为防御方承受一次攻击：用自身护甲跑逐层结算 + 效果结算，施加到自身躯体。近战与子弹共用。
    /// <paramref name="attacker"/> 为攻击方（用于战斗日志归属），可为 <c>null</c>（环境伤害/无源）。
    /// </summary>
    public void ReceiveAttack(Actor? attacker, Weapon weapon, CombatEngine combat, double damageFactor = 1.0,
        bool ranged = false)
    {
        if (!Alive)
        {
            return;
        }
        // 哨塔围栏抵挡：命中在哨塔驻守的守卫的**远程**攻击，按岗位抵挡几率整发打在围栏上、对角色完全无效
        // （非减伤、整发免掉；掷可注入随机源）。近战（枪托/暗哨近战/首发近战）传 ranged=false，不走此门。
        if (ranged && GuardPostMath.RangedBlocked(_postBlockChance, combat.Rng))
        {
            return;
        }
        // 伤害/流血/切除/致死已在此调用内施加到 Body；下方仅发布到表现总线（飘字②③④各自订阅）。
        // damageFactor：远程距离衰减系数（近战/贴脸枪托传默认 1.0，不衰减）。
        // 震荡抗性：本单位处抗性窗内（吃过震荡、打断+首轮冷却未走完）→ 再次震荡触发概率打折（防死锁）。
        double concResist = _concussionResistTimer > 0 ? CombatEffectCfg.ConcussionResistFactor : 1.0;
        AttackOutcome hit = combat.ResolveHit(weapon, DefenderArmor, Body, damageFactor, concResist);
        if (hit.Concussed)
        {
            ApplyConcussion(hit.ConcussionSeconds);
        }

        CombatFeed.Publish(attacker, this, hit);

        if (!Alive)
        {
            Die();
        }
    }

    /// <summary>
    /// 受一次震荡（重制口径）：进入 <paramref name="durationSeconds"/> 秒硬打断（期间 TryAttack 门控不出手、移速×0.1），
    /// 并把已积累的冷却清零——重置为「打断时长 + 一个满有效冷却」，使打断结束后从零重走完整冷却（对齐引擎）；
    /// 抗性窗覆盖「打断 + 首轮冷却」，期间再次被震荡触发概率打折（防死锁）。多次震荡取更长者、不缩短已有窗。
    /// </summary>
    private void ApplyConcussion(double durationSeconds)
    {
        double dur = System.Math.Max(0, durationSeconds);
        _concussionTimer = System.Math.Max(_concussionTimer, dur);
        double fullCooldown = EffectiveAttackInterval();
        if (IsRanged && AttackWeapon.BurstCount > 1)
        {
            fullCooldown += (AttackWeapon.BurstCount - 1) * System.Math.Max(0, AttackWeapon.BurstInterval);
        }

        // 打断期计时器照常衰减：设「打断+满冷却」→ 打断走完时恰好剩一个满冷却（=从零重走）。
        _attackTimer = System.Math.Max(_attackTimer, dur + fullCooldown);
        _concussionResistTimer = System.Math.Max(_concussionResistTimer, dur + fullCooldown);
    }

    private void Die()
    {
        Died?.Invoke(this);
        QueueFree();
    }

    // ---- 表现 ----
    /// <summary>是否被玩家选中（sprite 据此画选中环）。</summary>
    public bool Selected { get; set; }

    public bool SleepingVisual { get; private set; }

    public void SetSleeping(bool sleeping)
    {
        SleepingVisual = sleeping;
        if (sleeping)
        {
            CancelOrders();
            Velocity = Vector2.Zero;
        }
    }

    public bool IsNavigationFinished()
    {
        return _agent == null || _agent.IsNavigationFinished();
    }

    /// <summary>
    /// 只读寻路可达性查询：从本单位当前位置到 <paramref name="worldPos"/> 是否存在能抵达（路径终点贴近目标）
    /// 的导航路径。**不改变本单位的寻路目标/避障状态**（区别于设 TargetPosition 再读 IsTargetReachable，
    /// 那会污染当前指令）——纯粹向 NavigationServer 查一次路。供袭营 AI 判断「被围栏/大门阻隔、需先砸墙破防」。
    /// 导航图尚未就绪时返回 <c>true</c>（不误判为阻隔，避免开局帧敌人凭空砸墙）。
    /// </summary>
    public bool CanReach(Vector2 worldPos, float tolerance = 40f)
    {
        if (_agent == null)
        {
            return true;
        }
        Rid map = _agent.GetNavigationMap();
        if (!map.IsValid || NavigationServer2D.MapGetIterationId(map) == 0)
        {
            return true;
        }
        Vector2[] path = NavigationServer2D.MapGetPath(map, GlobalPosition, worldPos, true);
        return path.Length > 0 && path[^1].DistanceTo(worldPos) <= tolerance;
    }

    // 人形/血条/选中环已全部移交 ActorSprite（iso 层、YSort）。本节点在不可见 LogicLayer 下，
    // 不再 _Draw；sprite 每帧读上面暴露的数据自绘。

    /// <summary>当前各部位 HP 之和 / 模板 MaxHp 之和（分母用模板上限，条长不受磨损影响而失真）。</summary>
    private double PartHealthRatio()
    {
        double cur = 0, max = 0;
        foreach (BodyPart p in Body.Parts.Values)
        {
            cur += Body.HpOf(p.Name);
            max += p.MaxHp;
        }
        return max > 0 ? cur / max : 0;
    }
}
