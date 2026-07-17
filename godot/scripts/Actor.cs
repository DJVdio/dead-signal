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

    // ———————————— 🔴 [T45·负重激活] 负重 debuff 的两个消费点 ————————————
    //
    // 修复前：`Loadout.SpeedMultiplier` / `AttackSpeedMultiplier` 在引擎里算得好好的，
    // **全仓没有任何游戏代码消费它们**（除了 CarryWeight.cs 自己和单测）——移速是常数
    // `Pawn.MoveSpeed = 95f`，出手间隔只看残缺×饥饿×持握。⇒ **负重 debuff 是死代码**。
    // 用户要的「一出门就进入负重 debuff」在这一层是**物理上不可能发生**的。
    //
    // 现在：<c>CampMain.SyncExpeditionLoad</c> 把逐人的 <c>MemberLoad</c> 灌进 <c>Pawn.SetCarryLoad</c>，
    // 下面两个字段就是它落到实处的地方——移动链乘算它，出手间隔除以它。
    // **基类恒 1.0**（丧尸/劫掠者/狗/商人没有负重账）⇒ 对非 Pawn 单位逐位零回归。

    /// <summary>
    /// 负重移速乘子（1.0 = 无罚）。<b>乘算</b>进移动能力链（残缺 × 饥饿 × 骨折 × 战斗减速 × 家具 × <b>负重</b>）——
    /// 不是从总数里减百分点（CLAUDE.md 铁律：百分比一律乘算）。营地内 / 非探索队恒 1.0。
    /// </summary>
    protected double CarryLoadSpeedMult = 1.0;

    /// <summary>
    /// 负重出手间隔乘子（1.0 = 无罚，&lt;1 = 攻速变慢）。有效间隔 <b>÷</b> 它（乘子 0.85 ⇒ 间隔 ×1.176）。
    /// **从免罚线（30kg）起就线性掉**（50kg −20%、80kg −50%），见 <see cref="Loadout.AttackSpeedMultiplier"/>。
    /// </summary>
    protected double CarryLoadAttackSpeedMult = 1.0;

    // ---- authored 移速乘子（皮特·青春期田径队：L1 1.15× / L2 1.25× / L3 1.30×）----
    /// <summary>
    /// authored 专属移速乘子（1.0 = 无加成）。<b>乘算</b>进移动能力链（残缺 × 饥饿 × 骨折 × 战斗减速 × 家具 × 负重 × <b>authored</b>），
    /// 与负重槽同一注入式口径。<b>null（未注入）= 恒 1.0</b> ⇒ 丧尸/劫掠者/狗/商人及非皮特幸存者逐位零回归。
    /// 皮特由 <c>CampMain.AddActor</c> 注入一个读实时等级的 lambda（<c>PetePerk.MoveSpeedMultiplier</c>），
    /// 故升级即时生效、无缓存可失效（同山姆减伤 lambda 口径）。
    /// </summary>
    private System.Func<double>? _authoredMoveSpeedMult;

    /// <summary>注入 authored 移速乘子 lambda（皮特按等级现算）。null 清除（＝1.0，零回归）。</summary>
    public void SetAuthoredMoveSpeedMult(System.Func<double>? f) => _authoredMoveSpeedMult = f;

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

    /// <summary>
    /// 本单位当前用于攻击的武器（只读公开）。
    /// <para>
    /// <b>存在理由：让"玩家破坏"和"AI 砸墙"共用同一处真源。</b> <c>Raider</c>/<c>Zombie</c> 是子类，
    /// 能直接拿 <see cref="AttackWeapon"/> 喂 <see cref="StructureDamage.PerHit"/>；而玩家侧的破坏由
    /// <c>CampMain</c>（非子类）驱动，够不着 protected 字段。若为此另找一个"玩家的武器"入口
    /// （如 <c>Pawn.PrimaryWeapon</c>），两条路就会漂移——那正是"别给玩家开后门"要防的事。
    /// 故在此开一个只读口，<b>两边喂进 StructureDamage 的是同一个 Weapon</b>。
    /// </para>
    /// </summary>
    public Weapon CurrentAttackWeapon => AttackWeapon;
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

    /// <summary>
    /// 此刻身上穿着的护甲层（只读）。倒下时 <see cref="CorpseYard"/> 据此摆出尸体的战利品
    /// （<see cref="CorpseLoot.Strip"/>：<b>穿什么扒什么，零掷骰、不折损</b>；腐皮等天生层永不掉）。
    /// </summary>
    public IReadOnlyList<ArmorLayer> WornArmor => DefenderArmor;

    /// <summary>
    /// 此刻<b>手里握着</b>的武器（只读）。倒下时 <see cref="CorpseYard"/> 据此把它们原样放进尸体
    /// （用户拍板：「敌人掉武器的，他的武器直接落在他的可搜刮尸体里」）。
    ///
    /// <para>基类给的是<b>唯一那把攻击武器</b>——丧尸/劫掠者/狗都只有一把，够用。
    /// <see cref="Pawn"/> 会覆写它去读 <see cref="WeaponLoadout.HeldWeapons"/>，因为只有幸存者能<b>双持</b>
    /// （左右手各一把 ⇒ 该掉两把）。</para>
    ///
    /// <para><b>这里不过滤天生武器</b>：丧尸的爪击、狗的撕咬、空手的拳脚都会照常出现在本列表里——
    /// "什么算得上是能拿走的武器"是<b>掉落规则</b>，归 <see cref="CorpseLoot.IsSalvageable(Weapon)"/> 判
    /// （判据＝按名回查得到）。战斗层只如实报告"他手里是什么"，不替掉落层做判断。</para>
    /// </summary>
    public virtual IReadOnlyList<Weapon> HeldWeapons =>
        AttackWeapon is null ? Array.Empty<Weapon>() : new[] { AttackWeapon };

    private double _attackTimer;

    /// <summary>震荡硬打断剩余时长（秒）；&gt;0 时不能出手、移速×ConcussionMoveSlowFactor。见 <see cref="ReceiveAttack"/>。</summary>
    private double _concussionTimer;

    /// <summary>震荡抗性剩余时长（秒，覆盖打断+首轮重走冷却）；&gt;0 时再次被震荡的触发概率×ConcussionResistFactor（防死锁）。</summary>
    private double _concussionResistTimer;

    /// <summary>
    /// 命中减速剩余时长（秒，通用/RimWorld stagger 式）；&gt;0 时移速×StaggerSpeedMult。
    /// 任何攻击命中即触发（无论破防与否），只降移速——不打断出手、不清冷却（区别于震荡）。
    /// 重复命中取 <c>Max</c> 刷新时长、不叠加幅度。见 <see cref="ReceiveAttack"/>。
    /// </summary>
    private double _staggerTimer;

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

    // ---- 视野隐藏（批次4）：视野外不揭示。ActorSprite/状态条挂 iso_layer（非本体子节点），故隐 Actor 隐不掉它们，
    //      须单独切其 Visible。延迟挂载期间先记状态，挂载完成时补应用。----
    private bool _visualHidden;

    public event Action<Actor>? Died;

    /// <summary>
    /// <b>任何</b>单位倒下（丧尸/劫掠者/幸存者/布鲁斯/商人，不分阵营、不分场景）。与逐实例的
    /// <see cref="Died"/> 并行触发，语义完全相同——存在的理由只有一个：<see cref="CorpseYard"/> 要在
    /// <b>一处</b>接住所有死亡去落尸体、掷战利品，而实例事件的订阅散落在每个生成点
    /// （CampMain 里丧尸/夜袭者/教学劫掠者/克莉丝汀/商人各订各的），逐个补挂既易漏又要动一堆代码。
    /// <para>
    /// ⚠️ <b>静态事件：订阅方必须在 <c>_ExitTree</c> 退订</b>，否则换场景后被回收的旧 CorpseYard 仍会被调用。
    /// （同 <c>Raider.Escaped</c> 的既有静态事件形态。）
    /// </para>
    /// </summary>
    public static event Action<Actor>? AnyDied;

    /// <summary>
    /// <b>任何</b>单位挨了一下（含致命的那一下）。与逐子类的 <see cref="OnDamaged"/> 钩子并行，
    /// 存在的理由是**营地层要在一处接住"挨打"这件事**——目前的订阅方是
    /// <b>逐件搜刮</b>（<see cref="LootSession"/>）：正在翻箱倒柜的人一挨打就撒手，
    /// 已经掏出来的东西归你，正在掏的那件掉回箱子里。
    /// <para>⚠️ <b>静态事件：订阅方必须在 <c>_ExitTree</c> 退订</b>（同 <see cref="AnyDied"/>）。</para>
    /// </summary>
    public static event Action<Actor>? AnyDamaged;

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
    /// 层5 = <b>围栏</b>（网格状，用户口径：「围栏中间有网格空洞」）。**刻意与墙层 3 分开**，因为围栏的
    /// 阻挡是<b>部分的</b>：
    ///  - <b>挡移动</b> ✓ —— 故一切 Actor 的物理 mask 都含本层（同墙）。
    ///  - <b>不挡视线</b> ✓ —— <see cref="VisionOcclusion.WallMask"/> 只含层3，射线不查本层 ⇒ 看得穿网格。
    ///  - <b>不挡弹道</b> ✓ —— <c>Projectile.HitMask</c>=0b1111 只覆盖层1~4 ⇒ 子弹从网格空洞穿过去。
    ///  - <b>挡近战</b> ✓ —— 但这条**碰撞体拦不住**（围栏才 22px 厚，丧尸 24px 的够到距离跨得过去），
    ///    故由 <see cref="CoverLogic.MeleeBlocked"/> 在出手前显式拦（用户拍板"不允许隔着围栏近战"）。
    /// <b>营地大门不在本层</b>——门是实心的，仍在墙层3，照常挡视线挡弹道。
    /// </summary>
    private const uint LayerFence = 0b1_0000u;

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
                CollisionMask = LayerWall | LayerFence | LayerSurvivor;
                break;
            case Faction.Zombie:
                CollisionLayer = LayerZombie;
                CollisionMask = LayerWall | LayerFence | LayerZombie;
                break;
            case Faction.Raider:
                CollisionLayer = LayerRaider;
                CollisionMask = LayerWall | LayerFence | LayerRaider;
                break;
            case Faction.Neutral:
                // 中立方（神秘商人等）：不占任何阵营层（谁都不会撞进它、也不被挤开），只避墙自行走位。
                // 站定停留即可被右键前往交互（走的是容器 Rect 命中，非物理碰撞），无需参与阵营分离。
                CollisionLayer = 0u;
                CollisionMask = LayerWall | LayerFence;
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

    // ---- 导航路径读取（表现层专用：移动路径线 PathOverlay）----
    // 只**读** NavigationAgent2D 已算好并缓存的当前路径，不触发寻路、不改任何移动状态。
    // 因此画出来的就是他真会走的那条（门关着 → 导航改道 → 这里读到的就是绕远的那条）。

    /// <summary>当前是否有一条「还没走完」的导航路径（有移动/攻击逼近意图 且 未到终点）。</summary>
    public bool HasNavPath =>
        _agent != null
        && (_moveTarget.HasValue || _attackTarget != null)
        && NavigationServer2D.MapGetIterationId(_agent.GetNavigationMap()) != 0
        && !_agent.IsNavigationFinished();

    /// <summary>当前导航路径点（cartesian，含已走过的点；配 <see cref="NavPathIndex"/> 取剩余段）。无路径 → 空数组。</summary>
    public Vector2[] NavPathCart() => _agent == null ? System.Array.Empty<Vector2>() : _agent.GetCurrentNavigationPath();

    /// <summary>当前推进到的路径点下标（= 下一个要去的点）。</summary>
    public int NavPathIndex => _agent?.GetCurrentNavigationPathIndex() ?? 0;

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

        // 命中减速随时间衰减（只作用移速，不门控出手）。
        if (_staggerTimer > 0)
        {
            _staggerTimer -= delta;
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
                //
                // ⚠ 弓弩必须按**有效武器**（弓 ⊗ 箭 ⊗ 书）的射程判，不能按弓的裸射程——否则停下的距离与
                // TryAttack 真正的开火门（走的是有效武器）对不上：搭木箭（射程 ×0.75）时会停在射不到的地方
                // 干站着，读过《弓与箭之道》（×1.10）时又白白多走十步。枪与近战走的是同一个 AttackWeapon，行为不变。
                bool inEngage = IsRanged
                    ? Ballistics.InRange(EffectiveRangeDistance(dist), ResolveRangedShot().Shot)
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
        // 战斗移动减速：震荡硬打断 ×0.1（−90%，重）与命中减速 ×0.6（−40%，通用/RimWorld stagger 式）。
        // 二者并存则乘算，但整体封底 ConcussionMoveSlowFactor（0.1）——命中减速不再把已处震荡的移速进一步压低
        // （震荡是更重的效果，减速叠在其上无意义；封底口径拟定待确认，见 §5）。单独命中减速时 = ×0.6（不触底）。
        double combatSlow = 1.0;
        if (_concussionTimer > 0)
        {
            combatSlow *= CombatEffectCfg.ConcussionMoveSlowFactor;
        }
        if (_staggerTimer > 0)
        {
            combatSlow *= CombatEffectCfg.StaggerSpeedMult;
        }
        mobility *= System.Math.Max(combatSlow, CombatEffectCfg.ConcussionMoveSlowFactor);

        // 家具减速：踩在可跨越家具（椅子/床/柜子…）上 → ×0.75（用户拍板 −25%）。**乘算**进本链，
        // 不是从总数里减 25 个百分点：断腿(×0.7)的人跨椅子 = 0.7 × 0.75 = 0.525，不是加算的 0.45。
        // 无场（探索关）⇒ ×1.0，零回归。作业台（工作台/改装台/烹饪台）是实心的、压根站不上去，不在场里。
        if (Slowdowns is { } slow)
        {
            mobility *= slow.MultiplierAt(ToNumerics(GlobalPosition));
        }

        // 🔴 [T45] 负重减速：背着自己的枪、甲和战利品跑不快。**乘算**进本链（铁律），与残缺/骨折/家具彼此独立叠乘：
        // 断腿(×0.7) 的人背着满改装步枪(×0.97) = 0.679，不是加算的 0.67。基类恒 1.0 ⇒ 丧尸/劫掠者零回归。
        // 修复前这一行不存在 ⇒ Loadout.SpeedMultiplier 算出来的数字**没有任何人读它**。
        mobility *= CarryLoadSpeedMult;

        // authored 移速乘子（皮特青春期田径队 1.15/1.25/1.30×）：**乘算**进本链，与残缺/骨折/家具/负重彼此独立叠乘。
        // 未注入（非皮特）⇒ null ⇒ 不乘 ⇒ 零回归。皮特断腿(×0.7)背包(×0.97)仍照乘：0.7×0.97×1.30，不是加算。
        if (_authoredMoveSpeedMult is { } authoredMove)
        {
            mobility *= authoredMove();
        }

        Vector2 desired = mobility > 0 ? dir * MoveSpeed * (float)mobility : Vector2.Zero;
        // 把期望速度交给避障；OnVelocityComputed 收到安全速度后再 MoveAndSlide。
        _agent.Velocity = desired;
    }

    /// <summary>
    /// 避障算出的安全速度回调：真正驱动位移（<see cref="NavigationAgent2D.AvoidanceEnabled"/> 恒 true，
    /// 故这是全类**唯一**真实产生位移的地方——别处的 <c>MoveAndSlide()</c> 前一行都是 <c>Velocity = Zero</c>）。
    /// <para>
    /// 走路噪音就挂在这里，且量的是 <b>MoveAndSlide 之后的真实位移</b>而非期望速度 ——
    /// 顶着墙推是走不动的，走不动就不该有脚步声。
    /// </para>
    /// </summary>
    private void OnVelocityComputed(Vector2 safeVelocity)
    {
        if (!Alive)
        {
            return;
        }
        Velocity = safeVelocity;
        Vector2 before = GlobalPosition;
        MoveAndSlide();
        AccumulateFootstepNoise(GlobalPosition - before);
    }

    /// <summary>
    /// 头顶状态图标条（<see cref="StatusIconStrip"/>）的只读快照来源。基类只出 Body/武器/护甲
    /// （敌人等无伤病档单位）；<see cref="Pawn"/> 覆写以并入伤病集（感染常驻可见）。名字/饥饿对状态图标无关，省略。
    /// </summary>
    protected virtual PawnInspection BuildStatusInspection() =>
        PawnInspection.FromBody(Body, AttackWeapon, DefenderArmor, "");

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
            _statusStrip.Bind(this, BuildStatusInspection);

            // 补应用挂载前累积的视野隐藏态。
            if (_visualHidden)
            {
                _sprite.Visible = false;
                _statusStrip.Visible = false;
            }
        }
        else if (_spriteRetries == 120)
        {
            GD.PushWarning(
                "[ActorSprite] 未找到 'iso_layer' group，人形未挂载" +
                "（worktree 独立构建/合并前属正常，合并后由 B1 提供该 group）。");
        }
    }

    /// <summary>
    /// 视野隐藏（批次4，视野外不揭示）：切 iso_layer 上的 <see cref="ActorSprite"/> + 状态条可见性（本体 <see cref="Node.Visible"/>
    /// 隐不掉它们，因它们非本体子节点）。物理/AI/战斗照常——只是"没被看见时不渲染"。挂载前调用会在挂载完成时补应用。
    /// </summary>
    public void SetVisualHidden(bool hidden)
    {
        _visualHidden = hidden;
        if (_sprite != null && IsInstanceValid(_sprite))
            _sprite.Visible = !hidden;
        if (_statusStrip != null && IsInstanceValid(_statusStrip))
            _statusStrip.Visible = !hidden;
    }

    /// <summary>
    /// 当前有效出手间隔 = 基础冷却 / (操作能力 × 持握攻速系数)。操作能力 = 残疾×饥饿 经
    /// <see cref="HungerState.CombineCapability"/> 合并（与 <c>TryAttack</c> 计时器赋值同源，不改那套乘法）；
    /// 再乘 <see cref="ActiveGrip"/> 的持握系数（双持 0.70→更长；单手与双手均不变——双手无攻速加成）。供 wind-up 重置冷却复用。
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
        double interval = GripCombat.EffectiveInterval(baseCooldown ?? AttackCooldown, operation, ActiveGrip);

        // [A4] 书→近战攻速被动（消费层乘子汇总，见 MeleeBookEffect）：读过《进阶木匠技术》持消防斧 ⇒ 攻速 +8%（间隔 ×1/1.08）。
        // 仅 Pawn 有 ReadBookSet（IsBookRead 对非 Pawn 恒 false）⇒ 丧尸等零回归。武器名走当前生效武器；base 武器 Sim 读不到本乘子。
        interval *= AttackWeapon is null ? 1.0
            : MeleeBookEffect.AttackIntervalMultiplier(AttackWeapon.Name, IsBookRead);

        // 🔴 [T45] 负重罚攻速：**从免罚线（30kg）起就线性掉**（50kg −20%、80kg −50%）。攻速乘子 m ⇒ **间隔 ÷ m**（0.50 ⇒ 间隔 ×2.0）。
        // 刻意**不**并进上面的 operation 乘法——`Loadout` 明确写了负重不碰操作能力（那是残缺与饥饿的地盘，
        // 且负重上限本身已经乘过一遍操作能力，再扣一次就是双重惩罚）。故独立除在最后一层。
        // 修复前这一层不存在 ⇒ Loadout.AttackSpeedMultiplier 是死代码。基类恒 1.0 ⇒ 非 Pawn 单位零回归。
        return interval / System.Math.Max(CarryLoadAttackSpeedMult, Loadout.MinMultiplier);
    }

    private void TryAttack(Actor target)
    {
        // 震荡硬打断期：不能出手（冷却计时照常推进，打断结束后仍需走完重置进去的满冷却）。
        if (_attackTimer > 0 || _concussionTimer > 0)
        {
            return;
        }

        // 本次射击的**有效远程武器**与**该扣的弹药键**。弓/弩必须先解出"搭的是哪种箭"——
        // 箭会改写伤害/穿透/射程/冷却/散布（Archery.Combine），故下面的射程门、冷却、弹道一律用
        // 这把有效武器，而不是弓的裸数值。枪则原样（shot == AttackWeapon）。
        (Weapon shot, string ammoKey) = ResolveRangedShot();

        // 远程武器出手前先定"打什么"：
        // ①贴脸（≤PointBlankRange）→ 改钝击近战（必中、无误差角、低伤），不开火。用哪把由 Unarmed.MeleeFor 定：
        //   **枪 → 枪托**（MeleeProfile，慢而重，行为不变）；**弓/弩 → 拳脚**（用户拍板：「空手和持弓近战都视作
        //   空手近战，造成钝伤」——弓不是钝器，没有"抡弓砸人"这种形态，贴脸时你能用的只有自己的手）；
        // ②目标超出武器 MaxRange → 本次不出手也不消耗冷却（正常由 _PhysicsProcess 的 MaxRange 交战门先挡下、寻路逼近，此为兜底）；
        // ③弹药耗尽 → 开不了火，退化为枪托钝击（枪变烧火棍）；弓弩没枪托可抡 → 隔着距离的这一下根本打不出来
        //   （凑到贴脸就走①上拳头）。
        Weapon weapon = shot;
        bool fireRanged = IsRanged;
        int rounds = 1;  // 本次射击实际打出的发数（连发数，可能被余弹夹紧）；近战恒 1。
        if (IsRanged)
        {
            float dist = GlobalPosition.DistanceTo(target.GlobalPosition);
            if (!Ballistics.InRange(EffectiveRangeDistance(dist), shot))
            {
                return;
            }
            if (dist <= PointBlankRange + Radius + target.Radius)
            {
                weapon = Unarmed.MeleeFor(shot);   // 枪→枪托；弓/弩→拳脚
                fireRanged = false;
            }

            if (fireRanged)
            {
                // 弹药门（判定走引擎纯函数，实扣在下面真正提交出手时才做——同 CraftingLogic 出判定、
                // 调用方去 InventoryStore 实扣的分工）。不吃弹药的武器恒过门、扣 0，既有行为零回归。
                ShotPlan plan = AmmoLogic.PlanShot(shot, AmmoOnHand(shot, ammoKey));
                if (!plan.CanFire)
                {
                    AnnounceDry(shot);

                    // 打空了：能抡枪托就抡（复用贴脸枪托 profile——空枪至少还是根棍子）；
                    // 没枪托可抡的远程武器（弓/弩）则这一下根本打不出来，也不进冷却。
                    if (shot.MeleeProfile() is not { } emptyGunStock)
                    {
                        return;
                    }
                    weapon = emptyGunStock;
                    fireRanged = false;
                }
                else
                {
                    _dryAnnounced = false;   // 补上弹了 → 下次打空可以再报一次
                    rounds = plan.RoundsFired;
                }
            }
        }

        // 不允许隔着围栏近战（用户拍板）：围栏有网格空洞 ⇒ 看得穿、射得穿，但**捅不过去**。
        // 光靠碰撞体拦不住——围栏才 22px 厚，而丧尸的够到距离是 24+13+12=49px，跨得过去（现状就是
        // 丧尸能隔着栅栏咬到墙内的你）。故在出手前用线段-矩形几何显式拦掉。
        // 覆盖一切近战：本身近战武器、贴脸枪托、空枪抡棍（fireRanged=false 的全部路径）。远程不拦（能射穿）。
        // 拦下时**不进冷却**（这一下压根没打出来），下帧再判——玩家走开/围栏被啃穿后自然恢复。
        // 这也堵死了"站在安全的墙后拿长矛慢慢捅"的免费杀戮机器，把两难留给玩家：开枪(吃25%+烧子弹+引怪)
        // / 开门出去打(放弃防线) / 不管(墙被啃穿)。
        if (!fireRanged && Covers is { } meleeCovers
            && meleeCovers.MeleeBlockedBetween(ToNumerics(GlobalPosition), ToNumerics(target.GlobalPosition)))
        {
            return;
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
        // 跨度按**实发数** rounds 算（而非 BurstCount）：余弹只够单发时，冷却也相应短一截。
        if (fireRanged && rounds > 1)
        {
            _attackTimer += (rounds - 1) * System.Math.Max(0, shot.BurstInterval);
        }

        if (fireRanged)
        {
            // 实扣弹药：打几发扣几发（霰弹的 8 颗弹丸在同一发壳里，不乘弹药）。
            // 扣的是 ammoKey——弓/弩扣的是**选中那种箭**的具体键，而非它的类别键（库存里没有类别键那种东西）。
            SpendAmmo(shot, ammoKey, rounds);
            FireProjectile(target, rounds, shot, ammoKey);
        }
        else
        {
            // 贴脸枪托 / 空枪抡棍 / 本就近战武器：必中近战结算（枪托为上面派生的 MeleeProfile，不吃弹药）。
            target.ReceiveAttack(this, weapon, Combat);

            // 近战噪音（用户拍板：近战「会有一定的噪音」）：挥砍、闷响、扭打——砍人不是哑剧。
            // 半径按武器走（匕首 90 最静 … 破甲锤 155 最响），一律 **> 弓弩(≤70)**：这正是弓存在的意义,
            // 远远一箭放倒，好过凑上去砍出一堆动静把邻居全招来。爪击/撕咬同样有声（见 WeaponTable）。
            EmitNoise(weapon);
        }

        // [T47] 这一下**真的打出去了** —— 通知子类（消耗型改装靠它掉次数：锋刃研磨砍三下就磨没了）。
        // 传的是**手里那把武器**（AttackWeapon），不是本次派生出来的枪托/拳脚 profile：
        // 磨损记在"这把短剑"上，不是记在"这一下用的是哪种打法"上。
        OnAttackDelivered(AttackWeapon);
    }

    /// <summary>
    /// **一次攻击真的打出去了**（远程已开火 / 近战已结算）。默认空实现 ⇒ 丧尸/劫掠者行为**零变化**。
    /// <para>
    /// [T47] 存在的理由：消耗型改装（锋刃研磨 = 攻击三次后失去）要按"打了几下"计数。
    /// 只有玩家角色（<c>Pawn</c>）覆写它 —— 敌人没有库存、没有改装，也就没有磨损这回事。
    /// </para>
    /// <para>
    /// ⚠️ <b>钩子放在这里而不是各分支里</b>：射击与近战两条路都汇到这一点，加一个钩子就够；
    /// 分头挂两个，早晚会有人只改其中一个。被打断/超出射程/弹药不足的那些 <c>return</c> 都在这之前，
    /// 所以**没打出去的攻击不会掉次数**（这正是要的）。
    /// </para>
    /// </summary>
    protected virtual void OnAttackDelivered(Weapon used)
    {
    }

    // ---- 弹药（批次18）：枪必须消耗子弹，打空退化为枪托近战 ----

    /// <summary>
    /// 本单位的弹药来源。默认 <see cref="UnlimitedAmmoSource"/> —— 丧尸/劫掠者等**无库存模型**的单位
    /// 恒可开火、不扣弹（既有行为零回归）；玩家幸存者由营地层换成 <see cref="InventoryAmmoSource"/>
    /// （吃营地共享库存）。敌方的弹药以战利品形式回流给玩家，而非模拟他们的弹匣。
    /// </summary>
    public IAmmoSource Ammo { get; set; } = UnlimitedAmmoSource.Instance;

    /// <summary>
    /// 解出本次射击的**有效远程武器**与**该扣的弹药键**。
    /// <list type="bullet">
    /// <item><b>枪</b>：武器原样返回，弹药键 = <see cref="Weapon.AmmoKey"/>（子弹/霰弹是 1 键 : 1 材料）。</item>
    /// <item><b>弓/弩</b>：箭是「1 类别 : N 材料」——<see cref="Weapon.AmmoKey"/> 只是**类别键**（库存里根本没这种东西）。
    /// 故先从库存挑一种打得出来的箭（<see cref="Archery.PickCheapestAvailable"/>：**优先烧最差的**，好箭留着），
    /// 再由 <see cref="Archery.Combine"/> 算出「弓 ⊗ 箭」的有效武器（箭改写伤害/穿透/射程/冷却/散布）。
    /// 一支箭都没有 → 返回空键 → <see cref="AmmoLogic.PlanShot"/> 拿到余量 0 → 打不出来
    /// （弓弩没有枪托近战 profile，空弦就只能挨打——潜行武器该付的代价）。</item>
    /// </list>
    /// </summary>
    private (Weapon Shot, string AmmoKey) ResolveRangedShot()
    {
        if (!IsRanged)
        {
            return (AttackWeapon, "");
        }

        if (!Archery.UsesArrows(AttackWeapon))
        {
            return (AttackWeapon, AttackWeapon.AmmoKey);
        }

        ArrowDef? arrow = Archery.PickCheapestAvailable(Ammo.Count);
        return arrow is null
            ? (AttackWeapon, "")                                    // 箭壶空了
            : (Archery.Combine(AttackWeapon, arrow, HasReadArcheryBook), arrow.Key);
    }

    /// <summary>
    /// 本射手**本人**读完《弓与箭之道》没有——读过则其弓弩 射程 ×1.10、散布 ×0.90、攻速 ×1.02
    /// （<see cref="Archery.Combine"/> 里与箭的同轴系数**连乘**）。判据＝射手自己的已读书集，
    /// 与箭矢回收率 25%→50%（<see cref="Projectile"/>）同一条口径：**书的加成属于人，不属于箭，也不属于营地**。
    /// 丧尸/劫掠者不是 <see cref="Pawn"/>，恒 <c>false</c>（既有行为零回归）。
    /// </summary>
    private bool HasReadArcheryBook =>
        this is Pawn pawn && pawn.HasReadBook(BookLibrary.WayOfBowAndArrowId);

    /// <summary>
    /// 本单位**本人**是否读过某书（判据＝其 <see cref="Pawn.HasReadBook"/>）。喂给 <see cref="MeleeBookEffect"/> 等消费层书被动。
    /// 丧尸/劫掠者不是 <see cref="Pawn"/>，恒 <c>false</c>（既有行为零回归）。同 <see cref="HasReadArcheryBook"/> 的"书属于人"口径。
    /// </summary>
    private bool IsBookRead(string bookId) => this is Pawn pawn && pawn.HasReadBook(bookId);

    /// <summary>该弹药键的当前余量。不吃弹药的武器、或吃弹药但一支箭都没有（空键）→ 0。</summary>
    private int AmmoOnHand(Weapon weapon, string ammoKey)
        => weapon.UsesAmmo && !string.IsNullOrEmpty(ammoKey) ? Ammo.Count(ammoKey) : 0;

    /// <summary>已就"打空了"报过一次——避免每次抡枪托都刷一行飘字。补上弹后复位。</summary>
    private bool _dryAnnounced;

    /// <summary>
    /// "打空了"的一次性反馈：**只在打空的那一刻报一次**（此后一直空着也不再刷屏，补上弹后才复位）。
    /// 这是引擎里真实存在的状态（余弹 = 0），不是发明出来的手感文案。
    /// 玩家单位才提示——丧尸/劫掠者用无限弹药源，本就走不到这条路。
    /// </summary>
    private void AnnounceDry(Weapon weapon)
    {
        if (_dryAnnounced || this is not Pawn || !IsInstanceValid(this))
        {
            return;
        }
        _dryAnnounced = true;

        string text = weapon.HasMeleeProfile ? "打空了！只能抡枪托" : "没箭了！";
        FloatingText.Spawn(
            GetParent(),
            GlobalPosition + new Vector2(0, -Radius - 10),
            text,
            new Color(0.85f, 0.82f, 0.55f));   // 暗黄：警示但非伤害色（不与钝伤黄/锐伤红抢读）
    }

    /// <summary>实扣 <paramref name="rounds"/> 发弹药；不吃弹药的武器无操作。</summary>
    private void SpendAmmo(Weapon weapon, string ammoKey, int rounds)
    {
        if (weapon.UsesAmmo && !string.IsNullOrEmpty(ammoKey))
        {
            Ammo.Spend(ammoKey, rounds);
        }
    }

    private void FireProjectile(Actor target, int rounds, Weapon weapon, string ammoKey)
    {
        // 一次"射击"= rounds 发（= BurstCount 被余弹夹紧后的实发数，默认 1）。首发立即打出，
        // 其余 BurstInterval 间隔后逐发补上。每发独立锥采样、独立重新瞄准目标当前位置（连发跟枪）。
        // 冷却在 TryAttack 已含连发跨度；弹药亦已在 TryAttack 一次性扣清。
        // weapon = 有效武器（弓弩为「弓 ⊗ 箭」），在此捕获，避免连发途中换枪/换箭串味。
        FireOneRound(target, weapon, ammoKey);

        int burst = System.Math.Max(1, rounds);
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

                FireOneRound(target, weapon, ammoKey);
            };
        }
    }

    /// <param name="ammoKey">本发打出去的弹药具体键（弓弩＝选中那种箭）。传给弹丸供**箭矢回收**判定；枪弹一次性，用不上。</param>
    private void FireOneRound(Actor target, Weapon weapon, string ammoKey)
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

        // 子弹最大飞行距离对齐武器 MaxRange × 岗位射程倍率（开火判定同源；超此距离衰减归 0）。
        // 无射程模型的远程武器（罕见）回落到旧的 AttackRange*1.25 兜底（同样吃岗位倍率）。
        float maxDist = (float)((weapon.MaxRange ?? (AttackRange * 1.25)) * _postRangeMultiplier);

        // 多弹丸（霰弹 PelletCount=8）：一次击发同时打出 N 颗**各自独立**的弹丸——
        // 每颗在同一个误差角锥内**各自**采样一个方向（故扩散角越大、弹丸铺得越开），
        // 各自成为一枚独立 Projectile 独立飞行、独立碰撞、独立结算（撞到谁就对谁走一次完整
        // ReceiveAttack → 独立选部位 + 独立过护甲三段判定）。
        // 这正是引擎侧 CombatResolver.ResolveVolley 的空间版：两条路径同一语义（N 次独立判定），
        // 且**不会叠乘**——空间层的"独立"由 N 枚弹丸天然给出，故此处逐颗调单发结算即可。
        // 弹丸可分别命中不同目标（贴脸打一群丧尸时尤其明显），也可分别撞墙落空。
        // PelletCount 默认 1 → 恰好一枚，与改造前逐行等价（既有武器零回归）。
        int pellets = System.Math.Max(1, weapon.PelletCount);
        for (int p = 0; p < pellets; p++)
        {
            double shotDeg = Combat.SampleShotDirectionDegrees(aimDeg, spread);
            Vector2 dir = Vector2.FromAngle(Mathf.DegToRad((float)shotDeg));
            Projectile.Spawn(GetParent(), GlobalPosition + dir * (Radius + 2f), dir,
                maxDist, weapon, Combat, this, _postRangeMultiplier, ammoKey);
        }

        // 开火噪音：这一枪（或这一箭）响了，半径内闲着的敌人会过来看看。近战/无声武器 NoiseRadius=0 → 直接返回。
        EmitNoise(weapon);
    }

    // ---- 开火噪音（潜行：响声把敌人引过来）----

    /// <summary>
    /// 用这把武器发出一次攻击噪音（开火 or 挥砍，同一个口子）。半径 = <see cref="Weapon.NoiseRadius"/>。
    /// <para>
    /// 枪的贴脸 <see cref="Weapon.MeleeProfile"/> 走的是 <see cref="Weapon.StockMeleeNoise"/>（近战量级，
    /// 不是枪声）——抡枪托砸人有动静，但不会像开枪那样把半条街拽过来。
    /// </para>
    /// </summary>
    private void EmitNoise(Weapon weapon)
        => EmitNoiseAt(NoiseLogic.NoiseOf(weapon), NoiseKind.Combat, RatNoiseSource.WeaponAttack);

    /// <summary>
    /// [T61] 本单位的**动作噪音半径乘子**（脚步/开门/撬锁/静默拆除按此缩；**战斗/开枪/破坏不吃它**）。
    /// 基类恒 1.0（零回归——所有既有单位的噪音半径一个数都不变）；耗子在 <see cref="Pawn"/> 覆盖成 0.60。
    /// <para>
    /// 🔴 <b>为什么不能拿 <see cref="NoiseKind"/> 当开关（别"顺手简化"）</b>：那个枚举的语义轴是**"分不分阵营"**，
    /// 不是**"是不是战斗"** —— 开门(100)/撬锁(30)/静默拆除(35) 现在**全都归 <see cref="NoiseKind.Combat"/>**。
    /// 若按枚举分，耗子的开门声会**静默地不减**。故乘子的适用与否由 <see cref="RatPerk.AppliesToActionNoise"/>
    /// 按 <see cref="RatNoiseSource"/>（一条**正交的**分类轴）裁定，**每个 emitter 必须点名自己的来源**。
    /// </para>
    /// </summary>
    protected virtual double ActionNoiseScale => 1.0;

    /// <summary>
    /// **通用噪音事件**：在本单位位置发出一次半径为 <paramref name="radius"/> 的噪音。半径内、
    /// **当前没有攻击目标**的**敌对** Actor（丧尸 + 劫掠者，用户拍板全量版）走过去侦查一次。
    /// 判定走纯逻辑 <see cref="NoiseLogic.ShouldInvestigateSquared"/>，本方法只负责空间侧的遍历与派发。
    /// <para>
    /// 全部四类噪音源（走路 / 挥砍 / 开火 / 砸门破防）都汇到这里，**只有半径不同**。
    /// </para>
    /// <para>
    /// <b>零回归</b>：<paramref name="radius"/> ≤ 0 → 第一行即返回，一个听者也不惊动。
    /// </para>
    /// <para>
    /// <b>听者从场景树取</b>（同一父节点下的 Actor），不需要注入候选池 —— 与 <see cref="Projectile.Spawn"/>
    /// 挂到 <c>GetParent()</c> 同一口径，故不必改 CampMain/关卡层的任何接线。
    /// </para>
    /// <para>
    /// <b>性能</b>：本方法是**走路噪音的热路径**（每个 Actor 每走一个步幅就来一次，见 <see cref="AccumulateFootstepNoise"/>），
    /// 故整条路径**一次开方都不做**（<c>DistanceSquaredTo</c> + 平方比较），且把便宜的 bool 短路排在距离计算之前。
    /// <b>战斗噪音不分阵营（听者候选池变大）并不会让这条热路径变贵</b>：真正的热路径是**脚步**，
    /// 而脚步是 <see cref="NoiseKind.Movement"/>，仍然分阵营；且 <c>HasActiveTarget</c> 早退照旧
    /// （围攻时全场都在追人，绝大多数听者在那一行就筛掉了）。
    /// </para>
    /// <b>噪音不吃墙遮挡</b>：声音会绕会穿，与吃遮挡的视线/气味不同——隔堵墙开枪，对面照样听得见。
    /// </summary>
    /// <param name="kind">
    /// 噪音类别，决定**敌对那一条查不查**（用户拍板：战斗声不分阵营，脚步声分）。见 <see cref="NoiseKind"/>。
    /// </param>
    /// <param name="source">
    /// [T61] 噪音**来源**（<see cref="RatNoiseSource"/>，与 <paramref name="kind"/> **正交**）——
    /// 决定本次噪音吃不吃 <see cref="ActionNoiseScale"/>（耗子的"脚步和动作轻不可闻"）。
    /// **每个 emitter 必须点名**，不许省。非耗子单位恒 ×1.0（零回归）。
    /// </param>
    private void EmitNoiseAt(double radius, NoiseKind kind, RatNoiseSource source)
    {
        if (radius <= 0)
        {
            return; // 无声：零回归
        }

        // [T61] 耗子：脚步/开门/撬锁/静默拆除 ×0.60；战斗/开枪/破坏原样（用户明确排除）。
        // 非耗子 ActionNoiseScale ≡ 1.0 ⇒ 既有单位的噪音半径逐字节不变。
        if (RatPerk.AppliesToActionNoise(source))
        {
            radius *= ActionNoiseScale;
            if (radius <= 0)
            {
                return; // 乘到 0（若将来有"完全无声"的动作）：同无声，零听者
            }
        }

        Node? parent = GetParent();
        if (parent is null)
        {
            return;
        }

        Vector2 origin = GlobalPosition;
        foreach (Node n in parent.GetChildren())
        {
            if (n is not Actor listener || listener == this)
            {
                continue;
            }

            // 纯性能早退（**不是**第二份判定真源）：这三个 bool 是全部条件里最便宜的，先挡一道，
            // 省掉后面那次距离平方与阵营查表。RespondsToNoise 放最前面——它一刀切掉全部玩家单位，
            // 且围攻时全场丧尸都在追人（HasActiveTarget=true），绝大多数听者在这几行就筛掉了。
            // ⚠️ 这里**故意不含"敌对"那一条**——敌对是否生效取决于 kind（战斗噪音不查敌对），
            // 那个分支的权威在 ShouldInvestigateSquared 里，早退不许替它做主。
            // 语义与下面完全一致——它仍是判定权威，这里只是提前问了它其中三问。
            if (!listener.RespondsToNoise || !listener.Alive || listener.HasActiveTarget)
            {
                continue;
            }

            if (!NoiseLogic.ShouldInvestigateSquared(
                    kind: kind,
                    listenerRespondsToNoise: listener.RespondsToNoise,
                    listenerAlive: listener.Alive,
                    listenerHasTarget: listener.HasActiveTarget,
                    hostileToSource: Factions.IsHostile(listener.Faction, Faction),
                    distanceSquared: origin.DistanceSquaredTo(listener.GlobalPosition),
                    noiseRadius: radius))
            {
                continue;
            }
            listener.HearNoise(origin);
        }
    }

    // ---- 走路噪音（用户拍板：「走路会有较小的噪音」）----

    /// <summary>累计未发声的位移（跨帧攒着，攒满一个步幅就响一声）。见 <see cref="AccumulateFootstepNoise"/>。</summary>
    private double _footstepAccum;

    /// <summary>
    /// 本单位的脚步噪音半径。默认人类量级（<see cref="NoiseLogic.WalkNoiseRadius"/> = 40，
    /// **刻意压在丧尸嗅觉半径 70 以下**：否则玩家一迈腿就把周围全招来，地图寸步难行）。
    /// <see cref="Dog"/> 覆写为更轻的值（软肉垫，几乎无声）。
    /// </summary>
    protected virtual double FootstepNoiseRadius => NoiseLogic.WalkNoiseRadius;

    /// <summary>
    /// 脚步噪音：把本帧真实走过的距离攒进 <see cref="_footstepAccum"/>，**攒满一个步幅才响一声**。
    /// <para>
    /// <b>为什么按位移节流、而不是每帧广播</b>：走路是**持续行为**且**每个 Actor 都在走**（玩家 + 队友 + 全场敌人），
    /// 每帧广播就是每秒 60×N 次 O(N) 遍历 = O(N²) 灾难。按**累计位移**节流后，一个 Actor 最快也只能
    /// 每 <see cref="NoiseLogic.StrideDistance"/>(48px) 响一次 —— 以人类移速 95px/s 计约 **2 次/秒**。
    /// </para>
    /// <para>
    /// 按<b>位移</b>而非按<b>时间</b>节流还顺手买一送三：站着不动完全不发声；被减速/负重时脚步自动变稀；
    /// 移速快的（狗 150px/s）脚步自动变密。都是免费的正确物理直觉。
    /// </para>
    /// <para>
    /// <b>谁发脚步声</b>：**所有 Actor，无一例外**（玩家/队友/丧尸/劫掠者/狗），代码里没有任何身份特例。
    /// "丧尸听不听得见丧尸的脚步"这个问题**不需要专门回答**——<see cref="NoiseLogic.ShouldInvestigateSquared"/>
    /// 的「与噪音源敌对」那一条天然兜住了：丧尸同阵营 ⇒ 互不惊动；但**劫掠者听得见丧尸的脚步**，
    /// 丧尸也听得见劫掠者的（三方敌对矩阵）。零特例代码，语义自洽。
    /// </para>
    /// </summary>
    private void AccumulateFootstepNoise(Vector2 movedDelta)
    {
        double radius = FootstepNoiseRadius;
        if (radius <= 0)
        {
            return;
        }
        if (NoiseLogic.StrideDue(ref _footstepAccum, movedDelta.Length(), NoiseLogic.StrideDistance))
        {
            // 脚步 = **移动噪音（分阵营）**：丧尸不会被彼此的脚步声吸引成一坨（抱团震荡护栏）。
            EmitNoiseAt(radius, NoiseKind.Movement, RatNoiseSource.Footstep);
        }
    }

    /// <summary>
    /// **砸门/砸围栏/砸墙（破防）**发出的噪音，由 <see cref="BreachController"/> 在每次真正砸中结构时调用。
    /// 那是抡着家伙砸木头铁皮的动静，理应比任何一次挥砍都大（<see cref="NoiseLogic.BreachNoiseRadius"/> = 180）。
    /// <para>
    /// <b>门系统落地后，这里成了取舍的一端</b>：一扇锁着的门，撬（<see cref="EmitLockpickNoise"/>，30，安静但慢）
    /// 还是砸（180，快但把半条街招来）。<b>丧尸没得选——它不会开门，只会砸</b>（用户拍板）。
    /// </para>
    /// </summary>
    public void EmitBreachNoise() => EmitNoiseAt(NoiseLogic.BreachNoiseRadius, NoiseKind.Combat, RatNoiseSource.Breach);

    /// <summary>
    /// **开门**发出的噪音（<see cref="NoiseLogic.DoorNoiseRadius"/> = 100，近战量级：一扇旧门被推开的吱呀与碰撞）。
    /// 由 <see cref="CampMain"/>（玩家开门）与 <see cref="Raider"/>（劫掠者路过顺手开门）调用。
    /// <para>
    /// <b>只有这一个值，没有第二档</b>——用户拍板「玩家开门只有一种动作，不分轻推和踹开」。
    /// 半径经 <see cref="DoorLogic.NoiseOfOpening"/> 取，该函数刻意不收力度参数，从签名上挡住两档化。
    /// </para>
    /// <para>开门声（100）压过丧尸嗅觉（70）：你推开一扇门，附近闲逛的东西**听得见**。这正是撬锁存在的意义。</para>
    /// </summary>
    public void EmitDoorNoise() => EmitNoiseAt(DoorLogic.NoiseOfOpening(), NoiseKind.Combat, RatNoiseSource.DoorOpen);

    /// <summary>
    /// **撬锁**发出的噪音（<see cref="NoiseLogic.LockpickNoiseRadius"/> = 30，<b>全表最轻</b>，比走路 40 还轻）。
    /// <para>
    /// 30 &lt; 丧尸嗅觉 70 ⇒ <b>撬锁本身惊动不了任何东西</b>。这是该机制存在的全部理由：撬锁若能招来东西，
    /// 它就只是"慢速版砸门"，没人会选它。<b>撬锁买的是寂静，付的是时间</b>（坚固锁期望 32 秒 + 3 根铁丝）。
    /// </para>
    /// </summary>
    public void EmitLockpickNoise() => EmitNoiseAt(NoiseLogic.LockpickNoiseRadius, NoiseKind.Combat, RatNoiseSource.Lockpick);

    /// <summary>
    /// 本单位会不会开门。<b>丧尸恒 false</b>（用户拍板：不会开门，只会砸——门对它就是一堵墙，走破防系统）；
    /// <b>狗恒 false</b>（<see cref="Dog"/> 覆写：没有手。它是 <see cref="Faction.Survivor"/> 阵营，
    /// 光看阵营会被误判成能开门，故 <see cref="DoorLogic.CanOperateDoors"/> 把"是不是动物"单列一维）。
    /// 规则在 <see cref="DoorLogic"/>，此处只做转发。
    /// </summary>
    public virtual bool CanOperateDoors => DoorLogic.CanOperateDoors(Faction, isAnimal: false);

    /// <summary>
    /// 本单位是否**由噪音驱动**（即：是不是 AI）。**默认 false —— 玩家操控的单位（<see cref="Pawn"/>、
    /// <see cref="Dog"/>）以及中立的 <see cref="Merchant"/> 一律不响应噪音**；只有 <see cref="Zombie"/> /
    /// <see cref="Raider"/> 覆写为 true。
    /// <para>
    /// ⚠️ <b>这是硬安全阀，不是平衡项</b>。<see cref="HearNoise"/> 走的是 <see cref="CommandMoveTo"/> ——
    /// 那正是**玩家下达移动指令的同一条通道**。若不挡住玩家单位，任何一只丧尸只要走过幸存者 40px 内
    /// （<see cref="NoiseLogic.WalkNoiseRadius"/>），那个没在攻击的幸存者就会**自己朝丧尸走过去**，
    /// 玩家的操作被无声覆盖（玩家只会觉得"我的人中邪了"）。走路噪音把这个坑从偶发变成持续灾难。
    /// </para>
    /// <para>
    /// 这也正好落实用户拍板的原话：「<b>丧尸和劫掠者</b>都听得到」—— 用户点名的就是这两类 AI，
    /// <b>从没说过玩家的人会被声音牵着走</b>。玩家有自己的眼睛和耳朵。
    /// </para>
    /// </summary>
    protected virtual bool RespondsToNoise => false;

    /// <summary>
    /// 听见噪音：走过去侦查一次。**复用既有的「最后目击点」通道**（<see cref="UpdatePerception"/> 里
    /// 「丢失视野 → 前往最后目击点」那条路），不新造 AI 状态机、不改寻路 ——
    /// 到点/超时后由子类的游荡逻辑照常接管。
    /// <para>
    /// 只有 <see cref="RespondsToNoise"/> 为 true 的 AI 才会走到这里（<see cref="EmitNoiseAt"/> 已挡）。
    /// </para>
    /// </summary>
    protected virtual void HearNoise(Vector2 pos)
    {
        _lastSeenPos = pos;
        CommandMoveTo(pos);
    }

    /// <summary>
    /// 主动发一次噪音（子类用；如劫掠者<b>呼喊增援</b>）。⚠️ 这是唤醒<b>已经在场</b>的 AI 的唯一正道——
    /// 噪音<b>不生成任何新单位</b>，它只是让半径内闲着的人走过来看。
    /// </summary>
    protected void EmitNoise(double radius, NoiseKind kind, RatNoiseSource source = RatNoiseSource.WeaponAttack)
        => EmitNoiseAt(radius, kind, source);

    /// <summary>设置朝向（弧度）。哨兵在岗时用它把视野锥钉在岗位朝向上——玩家要绕的就是它。</summary>
    protected void SetFacing(float radians) => FacingAngle = radians;

    // ---- 守卫岗位加成（D 守卫防御战）：只读取岗位属性作用到战斗参数，不改既有攻防逻辑 ----
    private const float GuardBaseSightRadius = 200f; // 守卫巡防基础锁敌半径下限（拟定待调）
    // 岗位射程倍率（仅远程守卫，作用到有效开火射程 MaxRange；近战恒 1）。默认 1=非守卫/未上岗，零回归。
    private float _postRangeMultiplier = 1f;
    private float _postSightMultiplier = 1f; // 岗位视野倍率（作用到 GuardSightRadius；默认 1）
    private float _postBlockChance;          // 哨塔围栏远程抵挡几率（默认 0=不抵挡）
    private bool _firstStrikeAvailable;

    // ---- 半身掩体（桌子/椅子/沙袋）：远程 25% 整发无效 ----
    /// <summary>
    /// 当前关卡的半身掩体场（<b>场级唯一</b>）。<c>null</c> = 本关无掩体系统 ⇒ <see cref="ReceiveAttack"/> 里整块短路、
    /// **不掷点、不动随机流**（Sim/单测/未接线关卡零漂移）。
    ///
    /// <para><b>为何是 static</b>：掩体对<b>一切 Actor 双向对称</b>生效（玩家/幸存者/丧尸/劫掠者/狗都躲得、也都躲得过），
    /// 而 <c>ConfigurePerception</c> 那套逐 spawn 注入只覆盖敌人——漏一个就不对称了。掩体场又是场级唯一的，
    /// 故照 <see cref="CombatFeed"/> 的 static 约定走。</para>
    ///
    /// <para><b>生命周期（务必遵守）</b>：关卡建场时赋值，<b>换关/场景退场时置回 null</b>（<c>_ExitTree</c>），
    /// 否则下一关会残留上一关的掩体矩形。</para>
    /// </summary>
    public static CoverField? Covers { get; set; }

    /// <summary>
    /// 场上的<b>家具减速场</b>（可跨越家具的占地 + 移速乘子，见 <see cref="FurnitureTraversal"/>）。
    /// <c>null</c> = 没有场（探索关等）⇒ 谁都不减速，<b>零回归</b>。
    ///
    /// <para><b>为何是 static</b>：跟 <see cref="Covers"/> 一模一样的理由 —— 用户拍板「跨过家具减 25% 移速」
    /// 时<b>没有限定玩家</b>，所以它必须对<b>一切 Actor 双向对称</b>生效：丧尸、劫掠者、布鲁斯跨过你的柜子，
    /// 和你跨过它一样慢。挂在 Actor 基类的移速链上是<b>结构性</b>保证 —— <c>Pawn</c> / <c>Zombie</c> /
    /// <c>Raider</c> / <c>Dog</c> 全都是 Actor，<b>想给谁开后门，得先把它从 Actor 里摘出去</b>。</para>
    ///
    /// <para><b>生命周期（务必遵守）</b>：营地建场时赋值，<b>退场时置回 null</b>，否则下一关会残留上一关的家具矩形。</para>
    /// </summary>
    public static TraversalField? Slowdowns { get; set; }

    /// <summary>Godot 坐标 → System.Numerics（<see cref="CoverLogic"/> 等纯逻辑零 Godot 依赖，用后者的 Vector2）。</summary>
    private static System.Numerics.Vector2 ToNumerics(Vector2 v) => new(v.X, v.Y);

    /// <summary>
    /// 本单位与 <paramref name="target"/> 之间是否<b>隔着围栏</b>（⇒ 近战打不出去，见 <c>TryAttack</c> 的门）。
    ///
    /// <para><b>给 AI 用</b>（HANDOFF → impl-zombie-wall）：丧尸贴着围栏时，距离上"够得着"墙内的人，但这一下
    /// 会被拦掉。若 AI 仍认为自己在正常近战，它会贴着栅栏空挥、站着发呆。**够不着人 ⇒ 应转去砸围栏**
    /// （<c>BreachController</c> 的既有分支），本方法就是那个"够不着"的判据。</para>
    /// </summary>
    public bool MeleeBlockedByFence(Actor target) =>
        Covers is { } c && c.MeleeBlockedBetween(ToNumerics(GlobalPosition), ToNumerics(target.GlobalPosition));

    /// <summary>是否有存活的攻击目标（供 CampMain 巡防锁敌判定是否需补目标）。</summary>
    public bool HasActiveTarget => CurrentAttackTarget is { Alive: true };

    /// <summary>
    /// <b>近战被围栏卡死</b>：我是近战单位、有活着的目标、且中间隔着围栏 ⇒ <b>这一下永远打不出去</b>。
    ///
    /// <para>
    /// 袭营 AI 拿它当"够不着人"的判据喂给 <c>BreachLogic.ShouldBreach</c>：成立就转去<b>啃挡在中间的围栏</b>，
    /// 而不是站在栏外一下一下空挥（<see cref="MeleeBlockedByFence"/> 的类注说的就是这个 bug）。
    /// </para>
    /// <para>
    /// <b>只对近战成立</b>（<see cref="IsRanged"/> 为假）：围栏射得穿——持枪的劫掠者面前有栏也该继续开枪，
    /// 不该丢下枪去砸墙。
    /// </para>
    /// </summary>
    public bool MeleeStalledByFence =>
        !IsRanged && CurrentAttackTarget is { Alive: true } t && MeleeBlockedByFence(t);

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
        if (IsRanged)
        {
            // 弓/弩：先手同样要先搭上箭（有效武器 = 弓 ⊗ 箭）。
            (Weapon shot, string ammoKey) = ResolveRangedShot();

            // 弹药门：打空了就先手不出来——**也不消耗先手标记**（同射程外的口径：没打成就不算用掉）。
            ShotPlan plan = AmmoLogic.PlanShot(shot, AmmoOnHand(shot, ammoKey));
            if (!plan.CanFire)
            {
                return false;
            }
            _firstStrikeAvailable = false;
            SpendAmmo(shot, ammoKey, plan.RoundsFired);
            FireProjectile(target, plan.RoundsFired, shot, ammoKey);
        }
        else
        {
            _firstStrikeAvailable = false;
            target.ReceiveAttack(this, AttackWeapon, Combat);
        }
        return true;
    }

    /// <summary>
    /// 承受攻击前的闪避判定：返回 true 则整次攻击被躲开（不结算伤害/效果/表现，也不消耗攻击方冷却——冷却在攻击方侧起算）。
    /// 基类恒 <c>false</c>（无闪避轴，零回归）；高闪避单位（<see cref="Dog"/> 布鲁斯）覆盖按闪避概率掷免。
    /// 随机走引擎注入的 <see cref="IRandomSource"/>（<paramref name="rng"/>=<c>combat.Rng</c>，可复现）。
    /// </summary>
    protected virtual bool EvadeIncoming(IRandomSource rng) => false;

    // ---- 专属效果承伤乘子（synergy-wiring 注入，批次5 道格&布鲁斯；null=1.0 零回归）----
    /// <summary>本单位承伤额外系数（道格&布鲁斯 3 级光环：相依为命受伤 ×0.90）。返回 ≥0 乘子。</summary>
    private Func<double>? _incomingDamageFactor;

    /// <summary>注入承伤系数（3 级光环减伤）。null 清除。</summary>
    public void SetIncomingDamageFactor(Func<double>? f) => _incomingDamageFactor = f;

    // ---- 专属效果**护甲后**减伤（山姆 1 级"比常人耐揍"−10%；null=0 零回归）----
    /// <summary>
    /// 本单位的**护甲后**乘算减伤比例（0..1，山姆 1 级 = 0.10）。**与上面的 <see cref="_incomingDamageFactor"/> 不是同一层**：
    /// 那个折进 <c>damageFactor</c> → 缩放**武器伤害区间**，是**护甲之前**（甲随后照常吃缩小后的伤害）；
    /// 本条走 <c>CombatData.ResolveHit(…, incomingDamageReduction:)</c> → <c>CombatResolver.Resolve</c>，
    /// 在**护甲三段判定之后**乘算（甲先吃，穿透剩下的伤害再 ×0.9）。山姆的口径是后者（"更耐揍"＝肉更硬，不是让对方武器变钝）。
    /// </summary>
    private Func<double>? _incomingDamageReduction;

    /// <summary>注入护甲后减伤比例（山姆 1 级）。null 清除（＝0，零回归）。</summary>
    public void SetIncomingDamageReduction(Func<double>? f) => _incomingDamageReduction = f;

    /// <summary>
    /// [T69] 护手挡格保护的"持械手"部位集（手掌 + 五指）。
    /// <para>🔴 <b>拟定：惯用手＝右手</b>——零依赖战斗引擎（<c>CombatResolver</c>）不带"哪只手"这条轴，
    /// 而本集合是 <see cref="Actor"/> 基类的 <b>static</b>（丧尸/劫掠者等非 <see cref="Pawn"/> 单位根本没有持械模型），
    /// 故统一按惯用手（右）取一套。</para>
    /// <para>⚠ <b>别按"装备层还没落实左右手"理解</b>：装备层早已区分——<c>WeaponLoadout.LeftHand/RightHand</c>
    /// （WeaponLoadout.cs:22-26），且 <c>Pawn.WeaponInHand(Hand)</c>（Pawn.cs:232）已公开可读。要改成读实际持械手，
    /// 缺的只是"把这个 static 集合换成按 Pawn 实例查 loadout"，不是去造左右手模型。
    /// （纯逻辑 <see cref="WeaponModDefense.HandGuardNegates"/> 只认"命中是不是持械手"、不关心哪只，故它无需改动。）</para>
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> WeaponHandParts = new()
    {
        HumanBody.RightHand,
        HumanBody.RightThumb, HumanBody.RightIndex, HumanBody.RightMiddle, HumanBody.RightRing, HumanBody.RightPinky,
    };

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
        // 闪避：高闪避单位（布鲁斯）命中前掷免整次攻击（整次躲开、不结算伤害/效果/表现）。基类恒不闪避（零回归）。
        // 现引擎无闪避轴，按用户口径以最小侵入落在 Godot 层承伤入口；随机走引擎注入的 IRandomSource（可复现）。
        if (EvadeIncoming(combat.Rng))
        {
            return;
        }
        // 哨塔围栏抵挡：命中在哨塔驻守的守卫的**远程**攻击，按岗位抵挡几率整发打在围栏上、对角色完全无效
        // （非减伤、整发免掉；掷可注入随机源）。近战（枪托/暗哨近战/首发近战）传 ranged=false，不走此门。
        if (ranged && GuardPostMath.RangedBlocked(_postBlockChance, combat.Rng))
        {
            return;
        }
        // 半身掩体（桌子/椅子/沙袋）：躲在其后挨的**远程**攻击，按 25% 整发判无效——用户口径「这一下射中了人，
        // 但是判定 25% 几率无效」（不做子弹与掩体的物理碰撞）。**方向性**：只有掩体落在射击者与我的连线上才算
        // （绕后 → 掩体白躲，这正是包抄的意义）；**双向对称**：本门对一切 Actor 生效——你打躲在桌后的劫掠者，
        // 一样吃 25%。近战不吃（贴身砍你，桌子挡不住）。
        // **零漂移**：Covers 未接线（Sim/单测/无掩体关卡）或本次无掩体保护（chance=0）→ 短路，**不掷点、不动随机流**。
        if (ranged && attacker is not null && Covers is { } coverField)
        {
            float coverChance = coverField.ChanceFor(ToNumerics(attacker.GlobalPosition), ToNumerics(GlobalPosition));
            if (CoverLogic.Negates(ranged: true, coverChance, combat.Rng))
            {
                // 打中了，但没伤到：不结算伤害/效果/减速，只出"掩体挡下"飘字（区别于落空的静默）。
                CombatFeed.PublishCoverNegated(attacker, this);
                return;
            }
        }
        // [T69] 弩盾：举着装了弩盾的弩时，来自**正面 120°**（半角 60°）的**远程**攻击 25% 整发无效。
        // 与半身掩体同层（整发否决、不结算），方向按"射手在我正面锥内"判（绕到侧后 → 挡不住）。
        // 零漂移：无弩盾（chance ≤ 0）或近战 ⇒ WeaponModDefense.FrontalRangedNegates 短路、不掷点、不动随机流。
        if (ranged && attacker is not null && AttackWeapon is not null)
        {
            double shieldChance = ModdedWeaponRegistry.FrontalRangedNegateChanceOf(AttackWeapon.Name);
            double coneHalf = ModdedWeaponRegistry.FrontalNegateHalfAngleDegOf(AttackWeapon.Name);
            if (WeaponModDefense.FrontalRangedNegates(
                    ranged: true, shieldChance, coneHalf,
                    ToNumerics(GlobalPosition), FacingUnit(FacingAngle), ToNumerics(attacker.GlobalPosition), combat.Rng))
            {
                // 正面挡下：不结算伤害/效果/减速，只出"掩体挡下"飘字（复用同一表现，语义＝"这块弩盾替你挨了"）。
                CombatFeed.PublishCoverNegated(attacker, this);
                return;
            }
        }
        // 伤害/流血/切除/致死已在此调用内施加到 Body；下方仅发布到表现总线（飘字②③④各自订阅）。
        // damageFactor：远程距离衰减系数（近战/贴脸枪托传默认 1.0，不衰减）。
        // 光环承伤乘子（批次5 道格&布鲁斯 3 级：相依为命受伤 ×0.90）：折进 damageFactor 一并结算
        // （近战/首发/弹道统一经此入口）。null 注入回落 ×1.0（零回归）。
        if (_incomingDamageFactor is { } inMult)
            damageFactor *= inMult();
        // 震荡抗性：本单位处抗性窗内（吃过震荡、打断+首轮冷却未走完）→ 再次震荡触发概率打折（防死锁）。
        double concResist = _concussionResistTimer > 0 ? CombatEffectCfg.ConcussionResistFactor : 1.0;
        // 护甲后减伤（山姆 1 级"比常人耐揍"）：不折进 damageFactor（那是护甲前），单独喂进结算，甲吃完才乘。null → 0（零回归）。
        double postArmorReduction = _incomingDamageReduction is { } red ? red() : 0.0;
        // [T69] 护手挡格：持械手（含手指）被选为受击部位时，按几率整发否决。几率来自防御方（this）手里那把改装武器；
        // 判定必须落在**选部位之后**，故连同"持械手部位集"喂进 ResolveHit（承伤入口这里只知整次攻击、不知打哪个部位）。
        // 零漂移：无护手挡格 ⇒ chance 0 ⇒ ResolveHit 里短路、不掷点。
        double handGuardChance = AttackWeapon is null ? 0.0 : ModdedWeaponRegistry.HandGuardNegateChanceOf(AttackWeapon.Name);
        AttackOutcome hit = combat.ResolveHit(weapon, DefenderArmor, Body, damageFactor, concResist, postArmorReduction,
            handGuardChance, handGuardChance > 0 ? WeaponHandParts : null);
        if (hit.Concussed)
        {
            ApplyConcussion(hit.ConcussionSeconds);
        }
        // 命中减速（通用）：命中即施加（无论破防与否，含被甲完全挡下 hit.Blocked）——远程/近战/枪托同款。
        // 重复命中取 Max 刷新时长、不叠加幅度（RimWorld stagger）。围栏整发抵挡的远程在上方已 return，不到此处，故不减速。
        if (hit.StaggerSeconds > 0)
        {
            _staggerTimer = System.Math.Max(_staggerTimer, hit.StaggerSeconds);
        }

        CombatFeed.Publish(attacker, this, hit);

        // 挨打广播：先于死亡判定发，故**致命的那一下也算挨打**（正在搜刮的人被一口咬死，
        // 搜刮会话得当场收掉，别留着悬在一具尸体上）。见 AnyDamaged 的注释。
        AnyDamaged?.Invoke(this);

        if (!Alive)
        {
            Die();
            return;
        }

        OnDamaged(attacker); // AI 战术钩子（劫掠者：挨打 = 被压制 → 缩回掩体）。基类空实现，零回归。
    }

    /// <summary>
    /// 本单位挨了一下（**且没死**）。给 AI 子类的战术钩子——目前只有 <see cref="Raider"/> 用它做
    /// 「被压制 → 缩回掩体」。基类空实现（丧尸/幸存者不响应，行为零变化）。
    /// </summary>
    /// <param name="attacker">攻击方；环境伤害/无源时为 null。</param>
    protected virtual void OnDamaged(Actor? attacker) { }

    /// <summary>
    /// 当前攻击冷却剩余（秒；≤0 = 随时能开火）。供 AI 战术判定「枪快好了没」——
    /// 劫掠者据此决定<b>什么时候从掩体后探头</b>（<c>RaiderTactics.PhaseFor</c>）。
    /// </summary>
    protected double AttackCooldownRemaining => _attackTimer;

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
        // AnyDied 先于 Died：尸体/战利品在**任何**死亡订阅方跑起来之前就已落定，
        // 避免某个订阅方（如全灭判定）中途弹面板/换场景，把落尸挤掉。
        AnyDied?.Invoke(this);
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

    // ---- 感知（批次4 光照视野）：锥形 + 局部光照 + 遮挡 raycast，取代旧半径式侦测 ----
    // 视锥由观察者所处局部光照定（暗→短窄，走 VisionLogic.ConeFor）；对持光目标按暴露代价放大视距
    // （黑暗中的光源=显眼目标）；再对落在锥内的候选补墙层 raycast 判遮挡。潜行（背后/暗处/绕墙）由此自然涌现。
    // 三个依赖经 ConfigurePerception 注入，皆可空——未接线时回落（仅环境光锥 / 无暴露 / 自打墙层 raycast），
    // 使 worktree/光源系统未就绪的敌人仍走正确的锥形+环境光+遮挡感知，只是暂不吃动态光源与暴露代价。

    /// <summary>某点合成局部光照 L∈[0,1]（环境光与光源按距离衰减取 max，由 light-items 的 LightField 组合）。</summary>
    private Func<Vector2, float>? _localLightAt;
    /// <summary>目标当前持光强度 0~1（0=未持光；由 HeldLightState/LightProfile 提供），供暴露代价。</summary>
    private Func<Actor, float>? _carriedIntensityOf;
    /// <summary>观察者→目标是否被墙遮挡（复用 vision-render 公共工具；null 则本类自 raycast 墙层）。</summary>
    private Func<Vector2, Vector2, bool>? _sightOccluded;

    /// <summary>
    /// 本单位自身携带的光源强度 0~1（如劫掠者战斗时掏火把）；0=未持光。既提升自身视野（自照亮=光源中心满强度，
    /// 折进 <see cref="LocalLightAt"/> 的观察者局部光照），又使自己成为暴露信标（他人 <see cref="ExposedCone"/> 读此值放大对己视距）。
    /// </summary>
    private float _selfLightIntensity;
    /// <summary>本单位当前携带光源强度 0~1（供他人算暴露代价；0=未持光）。</summary>
    public float CarriedLightIntensity => _selfLightIntensity;
    /// <summary>设置/清除自身携带光源强度（劫掠者战斗态掏/收火把）。</summary>
    protected void SetCarriedLight(float intensity) => _selfLightIntensity = Mathf.Clamp(intensity, 0f, 1f);

    /// <summary>
    /// 短程全向"嗅觉"兜底半径（丧尸用；0=无，默认）。视距/半角看不见、但目标在此半径内且未被墙隔断（同房间）→仍感知。
    /// 补锥形视野"侧后死角"被无脑绕过的漏洞（用户拍板加）。是否穿墙=否（复用 <see cref="SightBlocked"/> 判同房间，待用户确认）。
    /// </summary>
    protected virtual float SmellRadius => 0f;

    /// <summary>感知节流计时（raycast 贵，按 <see cref="PerceiveInterval"/> 重算目标获取/丢失；移动/破防仍每帧）。</summary>
    private double _perceiveTimer;
    protected const double PerceiveInterval = 0.2; // 拟定待调，对齐批次2 UpdateRaid 节流量级
    /// <summary>丢失视野时的最后目击点（走过去侦查一次，到点/超时恢复游荡）。</summary>
    private Vector2? _lastSeenPos;

    /// <summary>
    /// 注入感知依赖（敌人生成处调用）。三者皆可空：null 时分别回落「仅环境光」「无暴露加成」「自打墙层 raycast」。
    /// </summary>
    public void ConfigurePerception(
        Func<Vector2, float>? localLightAt = null,
        Func<Actor, float>? carriedIntensityOf = null,
        Func<Vector2, Vector2, bool>? sightOccluded = null)
    {
        _localLightAt = localLightAt;
        _carriedIntensityOf = carriedIntensityOf;
        _sightOccluded = sightOccluded;
    }

    /// <summary>当前相位环境光（室内无窗恒暗标记暂按 false，室内标记待关卡层接入）。Clock 缺失回落白昼。</summary>
    private float AmbientNow()
        => VisionLogic.AmbientLight(Clock is null ? DayPhase.DayExplore : Clock.CurrentPhase, indoorsDark: false);

    /// <summary>
    /// 观察者所处局部光照 L：注入的 LightField 组合（否则仅环境光）与自身携带光源（自照亮，光源中心=满强度）取 max。
    /// 仅以本单位自身位置调用（PerceiveNearest/CanPerceive 传 GlobalPosition），故自持光折入即"站在自己火把下"的满强度，语义正确。
    /// </summary>
    private float LocalLightAt(Vector2 pos)
    {
        float baseLight = _localLightAt?.Invoke(pos) ?? AmbientNow();
        return _selfLightIntensity > baseLight ? _selfLightIntensity : baseLight;
    }

    /// <summary>观察者→目标视线是否被墙遮挡：注入工具优先，否则复用 vision-render 的共用遮挡工具。</summary>
    private bool SightBlocked(Vector2 from, Vector2 to)
        => _sightOccluded?.Invoke(from, to) ?? SelfSightBlocked(from, to);

    // 复用 vision-render 的 VisionOcclusion（批次4 遮挡「唯一权威来源」：遮暗渲染与敌方感知同口径，墙层 0b0100），
    // 不自造 raycast。无物理空间（未入树/worktree）→ 不误判为遮挡。
    private bool SelfSightBlocked(Vector2 from, Vector2 to)
    {
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        return space is not null && VisionOcclusion.IsOccluded(space, from, to);
    }

    // VisionLogic 零 Godot 依赖，坐标用 System.Numerics.Vector2；此处转换。
    private static System.Numerics.Vector2 Sn(Vector2 v) => new(v.X, v.Y);
    private static System.Numerics.Vector2 FacingUnit(float rad) => new(MathF.Cos(rad), MathF.Sin(rad));

    /// <summary>对持光目标按暴露代价放大视距（角度不变）：黑暗中持光=更远被发现。未持光/白昼=原锥。</summary>
    private VisionLogic.VisionCone ExposedCone(VisionLogic.VisionCone cone, float ambient, Actor target)
    {
        // 注入的持光查询优先（survivor 手持光走 HeldLightState）；否则回落目标自身携带光强（劫掠者战时火把等）。
        float carried = _carriedIntensityOf?.Invoke(target) ?? target.CarriedLightIntensity;
        if (carried <= 0f)
        {
            return cone;
        }
        float mult = VisionLogic.ExposureRangeMultiplier(ambient, carried);
        return new VisionLogic.VisionCone(cone.Range * mult, cone.HalfAngleDeg);
    }

    /// <summary>
    /// 锥形+光照+遮挡感知：从候选中挑本单位当前**能真正看见**的最近者。候选筛选（阵营/存活）由调用方在传入前完成。
    /// <paramref name="baseRange"/>=本单位白昼满档视距 R0。先廉价锥检（视距+半角）过滤，仅对锥内候选补 raycast，省开销。
    /// </summary>
    protected Actor? PerceiveNearest(IEnumerable<Actor> candidates, float baseRange)
    {
        float ambient = AmbientNow();
        VisionLogic.VisionCone cone = VisionLogic.ConeFor(LocalLightAt(GlobalPosition), baseRange);
        System.Numerics.Vector2 obs = Sn(GlobalPosition);
        System.Numerics.Vector2 facing = FacingUnit(FacingAngle);
        float smell = SmellRadius;

        Actor? best = null;
        float bestDist = float.MaxValue;
        foreach (Actor c in candidates)
        {
            if (!c.Alive)
            {
                continue;
            }
            float d = GlobalPosition.DistanceTo(c.GlobalPosition);
            VisionLogic.VisionCone effCone = ExposedCone(cone, ambient, c);
            bool inCone = VisionLogic.CanSee(obs, facing, Sn(c.GlobalPosition), effCone, occluded: false);
            bool inSmell = smell > 0f && d <= smell; // 全向嗅觉：无视半角，仅受半径约束
            if (!inCone && !inSmell)
            {
                continue; // 视锥外且嗅觉外 → 免 raycast
            }
            if (SightBlocked(GlobalPosition, c.GlobalPosition))
            {
                continue; // 墙隔断视线与气味（同房间才感知）
            }
            if (d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return best;
    }

    /// <summary>本单位当前是否仍能看见指定目标（追击维护/丢失判定）：同 <see cref="PerceiveNearest"/> 的单目标版。</summary>
    protected bool CanPerceive(Actor target, float baseRange)
    {
        if (target is not { Alive: true })
        {
            return false;
        }
        float ambient = AmbientNow();
        float d = GlobalPosition.DistanceTo(target.GlobalPosition);
        VisionLogic.VisionCone cone = ExposedCone(VisionLogic.ConeFor(LocalLightAt(GlobalPosition), baseRange), ambient, target);
        bool inCone = VisionLogic.CanSee(Sn(GlobalPosition), FacingUnit(FacingAngle), Sn(target.GlobalPosition), cone, occluded: false);
        bool inSmell = SmellRadius > 0f && d <= SmellRadius;
        if (!inCone && !inSmell)
        {
            return false; // 视锥外且嗅觉外 → 免 raycast
        }
        return !SightBlocked(GlobalPosition, target.GlobalPosition); // 墙隔断=同房间外，视线与气味皆断
    }

    /// <summary>感知节流闸：每 <see cref="PerceiveInterval"/> 放行一次重算（返回 true）；其间返回 false（维持现指令）。</summary>
    protected bool PerceptionDue(double delta)
    {
        _perceiveTimer -= delta;
        if (_perceiveTimer > 0)
        {
            return false;
        }
        _perceiveTimer = PerceiveInterval;
        return true;
    }

    /// <summary>
    /// 目标获取/丢失维护（节流帧调）：仍看得见现目标→刷新最后目击点；看不见（出锥/遮挡/进暗/超视距）→放弃并
    /// 走向最后目击点侦查一次；无目标→从候选里重新侦测最近可见者并 <see cref="CommandAttack"/>。走既有指令通道，
    /// 破防/基类移动照旧消费。<paramref name="candidates"/> 由子类给出（丧尸=幸存者池；劫掠者=IsHostile 过滤后的敌对池）。
    /// </summary>
    protected void UpdatePerception(IEnumerable<Actor> candidates, float baseRange)
    {
        Actor? cur = CurrentAttackTarget;
        if (cur is { Alive: true })
        {
            if (CanPerceive(cur, baseRange))
            {
                _lastSeenPos = cur.GlobalPosition;
                return;
            }
            CancelOrders(); // 丢失视野：放弃追击
            if (_lastSeenPos is { } p)
            {
                CommandMoveTo(p); // 走向最后目击点侦查（到点/超时后由子类游荡接管）
                _lastSeenPos = null;
            }
            return;
        }

        Actor? found = PerceiveNearest(candidates, baseRange);
        if (found != null)
        {
            CommandAttack(found);
            _lastSeenPos = found.GlobalPosition;
        }
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
