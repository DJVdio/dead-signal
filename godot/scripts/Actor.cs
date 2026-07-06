using System;
using System.Collections.Generic;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

public enum Faction
{
    Survivor,
    Zombie,
}

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

    /// <summary>存活 = 躯体未死（致死部位归零/斩首/开膛/失血致死皆算死）。</summary>
    public bool Alive => Body is { IsDead: false };

    protected Color BodyColor = Colors.White;
    protected float MoveSpeed = 90f;
    protected virtual bool CanAct => true;

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
    protected IReadOnlyList<ArmorLayer> DefenderArmor = Array.Empty<ArmorLayer>();

    private double _attackTimer;

    // ---- 指令目标 ----
    private Vector2? _moveTarget;
    private Actor? _attackTarget;

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

        // 碰撞层：幸存者层1(0b0001)/丧尸层2(0b0010)/墙层3(0b0100)。既作点选命中区，也作物理体。
        // 互撞（用户反馈#4：角色不再穿透）：
        //   幸存者 mask = 墙 + 幸存者 → 幸存者之间会互挤开、仍可穿过丧尸（近战靠射程结算、不必贴身）。
        //   丧尸   mask = 墙 + 丧尸   → 丧尸之间不再重叠堆成一坨（拟定默认；靠避障免互顶死）。
        // 碰撞形状保持 cartesian 圆（不改菱形/椭圆）——伪等距下物理仍 top-down，PZ 标准做法。
        if (Faction == Faction.Survivor)
        {
            CollisionLayer = 0b0001u;
            CollisionMask = 0b0101u;
        }
        else
        {
            CollisionLayer = 0b0010u;
            CollisionMask = 0b0110u;
        }

        OnReady();
    }

    /// <summary>子类初始化钩子（在基类节点搭好后调用）。</summary>
    protected virtual void OnReady() { }

    // ---- 指令接口 ----

    public void CommandMoveTo(Vector2 worldPos)
    {
        _attackTarget = null;
        _moveTarget = worldPos;
        _agent.TargetPosition = worldPos;
    }

    public void CommandAttack(Actor target)
    {
        if (target == this || !target.Alive)
        {
            return;
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
                if (dist <= AttackRange + Radius + tgt.Radius)
                {
                    Vector2 aim = tgt.GlobalPosition - GlobalPosition;
                    if (aim != Vector2.Zero)
                    {
                        FacingAngle = aim.Angle(); // 停下开打：面朝目标
                    }
                    Velocity = Vector2.Zero;
                    MoveAndSlide();
                    TryAttack(tgt);
                    return;
                }
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
        double mobility = 1.0 - Body.DisabilityModifiers.MobilityPenalty;
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

    private void TryAttack(Actor target)
    {
        if (_attackTimer > 0)
        {
            return;
        }
        // 残疾操作惩罚：操作能力 = 1 − OperationPenalty（断手/断指净值，实时重算）。对齐 Duel.EffectiveInterval 口径：
        // 有效间隔 = 基础 / 操作能力（惩罚 0.5 → 间隔翻倍）；能力 ≤0（惩罚 ≥1，如断双手）→ 无法出手，跳过本次攻击、
        // 计时器不动（避免除零变 NaN/负值），下帧再判。
        double operation = 1.0 - Body.DisabilityModifiers.OperationPenalty;
        if (operation <= 0)
        {
            return;
        }
        _attackTimer = AttackCooldown / operation;

        if (IsRanged)
        {
            FireProjectile(target);
        }
        else
        {
            target.ReceiveAttack(AttackWeapon, Combat);
        }
    }

    private void FireProjectile(Actor target)
    {
        Vector2 toTarget = target.GlobalPosition - GlobalPosition;
        if (toTarget == Vector2.Zero)
        {
            return;
        }

        // 误差角锥采样：以指向目标的准星方向为轴，在武器基础误差角内随机一个射击方向。
        // 命中判定交给弹道实时层（路径首个碰撞体，含撞墙落空）——引擎只出纯函数采样方向。
        double aimDeg = Mathf.RadToDeg(toTarget.Angle());
        double shotDeg = Combat.SampleShotDirectionDegrees(aimDeg, AttackWeapon.BaseSpreadDegrees);
        Vector2 dir = Vector2.FromAngle(Mathf.DegToRad((float)shotDeg));

        Projectile.Spawn(GetParent(), GlobalPosition + dir * (Radius + 2f), dir,
            AttackRange * 1.25f, AttackWeapon, Combat, this);
    }

    // ---- 守卫岗位加成（D 守卫防御战）：只读取岗位属性作用到战斗参数，不改既有攻防逻辑 ----
    private const float GuardBaseSightRadius = 200f; // 守卫巡防基础锁敌半径下限（拟定待调）
    private float _postEngageBonus; // 岗位有效交战距离加成（哨塔射程 + 屋顶视野，均延长开火距离）
    private bool _firstStrikeAvailable;

    /// <summary>是否有存活的攻击目标（供 CampMain 巡防锁敌判定是否需补目标）。</summary>
    public bool HasActiveTarget => CurrentAttackTarget is { Alive: true };

    /// <summary>
    /// 守卫有效锁敌半径 = max(基础下限, 有效射程 AttackRange)。屋顶平台把视野折进射程后，
    /// 锁敌半径随之覆盖延长的开火距离，确保远射目标能被锁定并真正开火（而非锁到却打不着）。
    /// </summary>
    public float GuardSightRadius => Mathf.Max(GuardBaseSightRadius, AttackRange);

    /// <summary>上岗：把岗位属性叠加到本 Actor 的战斗参数。哨塔射程 + 屋顶视野均折进有效交战距离；暗哨挂首发。</summary>
    public void ApplyGuardPost(GuardPostStats stats)
    {
        ClearGuardPost();
        _postEngageBonus = stats.EngageRangeBonus; // = RangeBonus + SightBonus
        AttackRange += _postEngageBonus;           // 登高远射：视野也真正延长开火距离
        _firstStrikeAvailable = stats.FirstStrike; // 暗哨：首发一击
    }

    /// <summary>下岗：撤销岗位加成，恢复原始战斗参数。</summary>
    public void ClearGuardPost()
    {
        AttackRange -= _postEngageBonus;
        _postEngageBonus = 0;
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
        float reach = AttackRange + Radius + target.Radius;
        if (GlobalPosition.DistanceTo(target.GlobalPosition) > reach)
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
            target.ReceiveAttack(AttackWeapon, Combat);
        }
        return true;
    }

    /// <summary>作为防御方承受一次攻击：用自身护甲跑逐层结算 + 效果结算，施加到自身躯体。近战与子弹共用。</summary>
    public void ReceiveAttack(Weapon weapon, CombatEngine combat)
    {
        if (!Alive)
        {
            return;
        }
        // 伤害/流血/切除/致死已在此调用内施加到 Body；下方仅发布到表现总线（飘字②③④各自订阅）。
        AttackOutcome hit = combat.ResolveHit(weapon, DefenderArmor, Body);
        CombatFeed.Publish(this, hit);

        if (!Alive)
        {
            Die();
        }
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
