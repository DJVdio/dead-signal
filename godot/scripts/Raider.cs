using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat; // IRandomSource / SystemRandomSource（战术 AI 的随机走可注入随机源）
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 劫掠者：携武器的人类敌人 AI 单位。侦测/追击/丢失/游荡的自主 <see cref="Think"/> 借鉴
/// <see cref="Zombie"/>，但两点不同：①人类昼夜皆活动，不受夜间休眠门约束；②攻击对象不写死"打幸存者"，
/// 而由**可运行时替换的候选池 <see cref="SetTargetProvider"/> + <see cref="Factions.IsHostile"/>** 裁定择敌。
///
/// 克莉丝汀战斗时就是一个 Raider 实例：起手 <c>Faction=Raider</c> 打幸存者；反水由编排层（块D）
/// 运行时 <see cref="Actor.SetFaction"/>(Survivor) + <see cref="SetTargetProvider"/>(劫掠者池) 整套改边——
/// 本类只负责让这两个运行时改动即刻生效，不含触发/生成/抉择编排（那是块D/C）。
///
/// 攻击复用 <see cref="Actor"/> 既有路径：远程走锥形散布弹道（<c>IsRanged</c>+FireProjectile），
/// 近战走 ReceiveAttack；命中/友伤由弹道层经 <see cref="Factions.IsHostile"/> 裁定，本类不自造战斗。
/// </summary>
public sealed partial class Raider : Actor
{
    // 白昼满档视距 R0（作日间正面警觉，比丧尸略大：主动进攻方）。昼夜视锥与环境光参数以 Wiki 配置为准；
    // 夜间仍沿用与丧尸相同的光照收紧规则。
    private const float BaseSightRange = 300f;

    /// <summary>劫掠者是 AI，听得见动静就会挪过去看看（用户拍板：「丧尸和劫掠者都听得到」）。</summary>
    protected override bool RespondsToNoise => true;

    // 战斗时掏火把（用户口径：被发现正式开战后才拿出光源，潜行阶段不持光）。火把强度读 LightSource/Wiki 配置。
    // 效果：进入战斗态→SetCarriedLight(火把强度)：①自照亮提升本体局部光照→ConeFor 视锥变大（夜战视野恢复）；
    // ②成持光信标→他人 ExposedCone 读本体 CarriedLightIntensity 放大对己视距（暴露代价对己生效）。脱战即收火把归 0。
    private static readonly float TorchLightIntensity =
        LightSource.Find(LightSource.TorchKey)?.Intensity ?? 0.7f;

    /// <summary>
    /// 当前劫掠者是否有一盏可投影到场上的火把。消费层（营地/探索关 LightField）只读此快照，
    /// 不需要窥探 Raider 的目标与姿态私有状态。
    /// </summary>
    public PlacedLight? ActiveTorchLight
    {
        get
        {
            if (CarriedLightIntensity <= 0f || LightSource.Find(LightSource.TorchKey) is not { } torch)
            {
                return null;
            }

            return new PlacedLight(GlobalPosition.X, GlobalPosition.Y, torch);
        }
    }

    private Rect2 _wanderBounds;
    private Func<IEnumerable<Actor>>? _targetProvider;
    private double _wanderTimer;
    private BreachController? _breach; // 门关闭后砸围栏/大门破防（袭营时由 CampMain 注入）
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>战斗日志/检视显示名（克莉丝汀等具名劫掠者用）；默认"劫掠者"。</summary>
    public string DisplayName { get; private set; } = "劫掠者";

    /// <summary>
    /// 工厂：仿 <see cref="Zombie.Create"/> 风格，供块D 脚本化生成。
    /// </summary>
    /// <param name="wanderBounds">无目标时的游荡巡场范围（cartesian）。</param>
    /// <param name="targetProvider">择敌候选池（返回可能的攻击对象；本类再按 IsHostile+侦测半径挑最近敌对者）。</param>
    /// <param name="usePistol">true=手枪远程；false=匕首近战。<paramref name="weapon"/> 非空时本参数被忽略。</param>
    /// <param name="displayName">战斗日志显示名（克莉丝汀传其名）。</param>
    /// <param name="weapon">
    /// 点名持械（如金手指帮守备的短剑）。<c>null</c> ⇒ 回落 <paramref name="usePistol"/> 的手枪/匕首二选一
    /// （劫掠者的两种常规手牌）。给什么武器 <b>就等于给了什么战利品</b>（<see cref="CorpseLoot"/> 必掉零掷骰）——
    /// 这是经济决定，不是随手参数。
    /// </param>
    /// <param name="outfit">
    /// 点名着装（如斯图尔特庄园那个披甲的：皮甲 + 军用头盔 + 皮夹克…）。<c>null</c> ⇒ 回落默认的
    /// <c>SurvivorArmor</c>（皮夹克 + 长袖布衣两层，<b>既有行为逐位不变</b>）。
    /// <para>🔴 <b>穿什么＝掉什么</b>（<see cref="CorpseLoot.Strip"/> 零掷骰必掉）——所以这同样是<b>经济决定</b>：
    /// 用户对斯图尔特家族庄园的原话是「这个调查点<b>最富裕的地方是劫掠者们的装备和衣服</b>」，
    /// 那句话就落在这个参数上。远远看见那个穿皮甲的，那就是<b>一副皮甲在那儿走着</b>——
    /// 值不值得为它冒险，玩家自己算。</para>
    /// </param>
    public static Raider Create(
        Rect2 wanderBounds,
        Func<IEnumerable<Actor>> targetProvider,
        bool usePistol = true,
        string displayName = "劫掠者",
        Weapon? weapon = null,
        IReadOnlyList<ArmorLayer>? outfit = null,
        IRandomSource? visualRng = null)
    {
        var r = new Raider
        {
            DisplayName = displayName,
            BodyColor = new Color(0.72f, 0.26f, 0.22f), // 暗红：与幸存者（自定义色）、丧尸（绿）一眼区分
            VisualModelIndex = EnemyVisualModels.Pick(visualRng ?? new SystemRandomSource()),
            _wanderBounds = wanderBounds,
            _targetProvider = targetProvider,
        };
        r.Faction = Faction.Raider;
        r.Radius = 12f;
        r.MoveSpeed = 92f;
        r.Body = CombatData.NewHumanoidBody();
        r.DefenderArmor = outfit ?? CombatData.SurvivorArmor(); // 默认：人类＝皮夹克 + 长袖布衣两层
        r.Arm(weapon ?? (usePistol ? CombatData.Pistol() : CombatData.Dagger()));
        return r;
    }

    /// <summary>交战距离：远程（空间交战参数，当前值以 Wiki 配置为准）。</summary>
    private const float RangedEngageRange = 240f;

    /// <summary>交战距离：近战。</summary>
    private const float MeleeEngageRange = 26f;

    /// <summary>
    /// 持械：射程 / 冷却 / 远程与否<b>全部由武器派生</b>，不再按"是不是手枪"分叉写两遍。
    /// 冷却读 <c>WeaponTable</c> 的权威间隔，敌我同一套节奏；具体武器数值以 Wiki 配置为准。
    /// <para>⚠️ 远程一律按 <see cref="RangedEngageRange"/> 交战——够用是因为敌人手上只会有短枪。
    /// 日后真要给敌人长枪（步枪/狙击），这里得改成按 <c>Weapon.MaxRange</c> 派生，否则会沿用当前敌方交战距离。</para>
    /// </summary>
    private void Arm(Weapon w)
    {
        AttackWeapon = w;
        AttackCooldown = w.AttackInterval;
        IsRanged = w.IsRanged;                                          // 远程走锥形散布弹道（误差角来自 BaseSpreadDegrees）
        AttackRange = w.IsRanged ? RangedEngageRange : MeleeEngageRange;
    }

    /// <summary>
    /// 预置伤情（金手指帮守备："刚经历完异常战斗，大家的状态都不是巅峰"）。
    /// <para>
    /// 走 <see cref="GoldfingerGang.ApplyInjuries"/> —— 与 Sim 校准 harness <b>同一个函数</b>，
    /// 免得"算出来的敌人"和"打到的敌人"是两拨人。只碰部位 HP 与骨折，<b>不登记出血</b>
    /// （否则他们会在玩家赶到前自己流血流死）。
    /// </para>
    /// </summary>
    public void ApplyInjury(GangInjury injury) => GoldfingerGang.ApplyInjuries(Body, injury);

    protected override void OnReady()
    {
        _rng.Randomize();
        Live.Add(this); // 同伴池/小队序号的唯一来源（战术 AI 用）
        // 掩体重搜**错峰**：起始相位随机，避免全队在同一帧一起开搜（性能红线：搜索是本 AI 唯一的射线热点）。
        _coverTimer = _rng.RandfRange(0f, (float)Tactics.CoverRecomputeInterval);
    }

    public override void _ExitTree() => Live.Remove(this);

    /// <summary>
    /// 运行时替换择敌候选池（克莉丝汀反水编排用）：只换"从哪群 Actor 里挑敌人"，不动攻防逻辑。
    /// 与 <see cref="Actor.SetFaction"/> 配合即可整套改边——但即便只切阵营不换池，<see cref="FindNearestHostile"/>
    /// 也会因 IsHostile 结果翻转而自动改打对象。节点未入树时调用也安全。
    /// </summary>
    public void SetTargetProvider(Func<IEnumerable<Actor>> provider) => _targetProvider = provider;

    /// <summary>
    /// 袭营时注入破防能力（门关闭后到不了营内目标→走到最近围栏/大门前砸墙）。委托由 CampMain 提供
    /// （找最近结构 / 对结构施伤，结构类型不外泄）；<paramref name="campCenter"/> 作无目标时的可达性探测点。
    /// <para>
    /// 砸墙伤害与节奏<b>由他手上的武器派生</b>（<see cref="StructureDamage"/>），不再是独立写死的伤害常数。
    /// <b>持枪者砸墙 = 抡枪托</b>（<see cref="Weapon.MeleeProfile"/>：伤害/节奏都取枪托 profile）——子弹打不穿承重墙，
    /// 但枪托是块钝的金属。这条同时修好了旧实现的不自洽：伤害同为常数、节奏却取自武器，导致
    /// **历史/非配置源**：旧报告曾比较手枪、匕首的破墙伤害与节奏；当前武器与枪托数值统一以 Wiki 配置为准。
    /// </para>
    /// </summary>
    public void ConfigureBreach(
        BreachController.FindNearestStructure find,
        BreachController.DamageNearestStructure damage,
        Vector2 campCenter,
        BreachController.SuppressBreach? suppress = null,
        BreachController.ReleaseBreachSlot? release = null)
    {
        // TryCivilizedEntry 作"抡家伙之前的文明解法"注入：被阻隔时
        //   ① 先试开能开的门 → ② 再试**安静入侵**（撬锁 / 轻声拆围栏）→ ③ 都不行才砸。
        // 这正是劫掠者与丧尸的分野（丧尸不注入这一项 ⇒ 只会直接砸）。
        // 攻击位名额（BreachSlots）对两者一视同仁——一扇门前站得下几个人，跟站的是死人还是活人无关。
        _breach = new BreachController(find, damage, campCenter,
            StructureDamage.PerHit(AttackWeapon), StructureDamage.Interval(AttackWeapon),
            attackReach: 34f + Radius, standoff: Radius + 8f,
            suppress, TryCivilizedEntry, release);
    }

    /// <summary>
    /// 注入「开门」能力（用户拍板：<b>劫掠者会正常开门</b>——不像丧尸只会砸）。
    /// <para>
    /// 委托由 <c>CampMain</c> 提供：<paramref name="findDoor"/> 找身边最近的**能开的门**（关着、没锁、够得着），
    /// <paramref name="openDoor"/> 真去开（切碰撞层 + 摘导航洞 + 重烘焙 + 发开门噪音；噪音数值以 Wiki 配置为准）。门的类型不外泄。
    /// </para>
    /// </summary>
    public void ConfigureDoors(FindOpenableDoor findDoor, OpenDoor openDoor)
    {
        _findDoor = findDoor;
        _openDoor = openDoor;
    }

    /// <summary>找 <paramref name="from"/> 附近 <paramref name="radius"/> 内最近的**能开的门**（关着 + 没锁），输出其位置。</summary>
    public delegate bool FindOpenableDoor(Vector2 from, float radius, out Vector2 doorPos);

    /// <summary>把 <paramref name="doorPos"/> 处的门推开（切碰撞/导航 + 发开门噪音）。返回是否真开了。</summary>
    public delegate bool OpenDoor(Vector2 doorPos, Actor opener);

    private FindOpenableDoor? _findDoor;
    private OpenDoor? _openDoor;

    /// <summary>够得着门、可以伸手去推的距离（≈近战边缘）。</summary>
    private const float DoorReach = 30f;

    /// <summary>门外找可开的门的搜索半径（与破防的 SearchRadius 同量级）。</summary>
    private const float DoorSearchRadius = 320f;

    /// <summary>
    /// <b>劫掠者开门</b>（用户拍板：「劫掠者会正常开门」）。插在破防**之前**——这个顺序是全部关键：
    /// <b>能开的门就开，开不了的（锁着的 / 没有门的墙）才砸。</b>
    /// <para>
    /// 若顺序反了（破防在前），劫掠者会把每一扇关着的门都砸开——那它和丧尸就没有任何区别了，
    /// "劫掠者会开门"这条口径也就白写了。
    /// </para>
    /// <para>
    /// <b>锁着的门劫掠者怎么办</b> → 见 <see cref="TryQuietIntrusion"/>：<b>他会撬</b>。
    /// （⚠️ 这条是<b>用户后来推翻的旧口径</b>。旧版写着"不给 AI 做撬锁、让它砸"——那恰恰制造了一个致命漏洞：
    /// 劫掠者只会砸门 ⇒ 一砸就把玩家吵醒 ⇒ <b>玩家的最优解变成根本不派守夜人</b>。见 <see cref="IntrusionLogic"/>。）
    /// </para>
    /// 接管本帧（走向门 / 推门）返回 true，调用方应 return；无门可开返回 false，交回破防/追击。
    /// </summary>
    private bool TryOpenDoor()
    {
        if (_findDoor == null || _openDoor == null || !CanOperateDoors)
        {
            return false;
        }
        if (!_findDoor(GlobalPosition, DoorSearchRadius, out Vector2 doorPos))
        {
            return false; // 附近没有能开的门 → 交回破防（去砸围栏/锁着的门）
        }

        if (GlobalPosition.DistanceTo(doorPos) > DoorReach + Radius)
        {
            CommandMoveTo(doorPos); // 够不着：先走到门前
            return true;
        }

        CancelOrders();              // 到门口：站定推门
        _openDoor(doorPos, this);    // 门开了 → 导航重烘焙 + 开门噪音（数值以 Wiki 配置为准，附近闲逛的丧尸听得见）
        return true;
    }

    // ════════════════════════════════════════════════════════════════════════
    //  安静入侵（用户口径：「劫掠者会花一段时间撬锁，或者轻声拆除围栏进入，
    //  以避免玩家不派任何岗哨只等着敌人砸门」）—— **反退化设计**。
    //  判定全在纯逻辑 IntrusionLogic；这里只做空间执行：走过去、蹲下、计时、发作业噪音、完成时落地；噪音数值以 Wiki 配置为准。
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>找最近的可安静入侵目标（锁着的门 / 一格围栏）。<paramref name="isDoor"/>=false 即围栏格。</summary>
    public delegate bool FindQuietTarget(
        Vector2 from, float radius,
        out Vector2 point, out bool isDoor, out LockTier lockTier, out StructureTier fenceTier);

    /// <summary>把 <paramref name="doorPos"/> 的锁撬开并推门（CampMain 内部走 DoorLogic.TryPick 掷判）。返回是否撬开了。</summary>
    public delegate bool PickDoorLock(Vector2 doorPos, Actor picker);

    /// <summary>把 <paramref name="point"/> 那一格围栏<b>整格抹掉</b>（施加满血伤害 → 复用既有摧毁链路 ⇒ 一个真的洞）。</summary>
    public delegate bool DismantleFence(Vector2 point, Actor who);

    /// <summary>天亮前还剩多少秒（时间紧迫就没法慢慢来）。缺省 → 回落"夜里还长"。</summary>
    public delegate double SecondsUntilDawn();

    private FindQuietTarget? _findQuiet;
    private PickDoorLock? _pickLock;
    private DismantleFence? _dismantle;
    private SecondsUntilDawn? _untilDawn;

    /// <summary>安静作业进度（秒）与它锁定的那个目标。</summary>
    private double _quietProgress;
    private Vector2 _quietPoint;
    private bool _quietIsDoor;
    private int _failedPicks;

    /// <summary>身上带的铁丝（撬锁工具）。撬断一根少一根；没了就只能拆围栏或砸。数量以 Wiki 配置为准。</summary>
    public int Lockpicks { get; private set; } = 2;

    /// <summary>入侵参数（当前值以 Wiki 配置为准）。</summary>
    private static IntrusionParams Intrusion => IntrusionParams.Default;

    /// <summary>
    /// 注入<b>安静入侵</b>能力（袭营时由 CampMain 注入）：找目标 / 撬锁 / 拆围栏 / 问天什么时候亮。
    /// 不注入 ⇒ 这个劫掠者只会开门和砸（旧行为，零回归）。
    /// </summary>
    public void ConfigureQuietIntrusion(
        FindQuietTarget find, PickDoorLock pick, DismantleFence dismantle, SecondsUntilDawn? untilDawn = null)
    {
        _findQuiet = find;
        _pickLock = pick;
        _dismantle = dismantle;
        _untilDawn = untilDawn;
    }

    /// <summary>
    /// 抡锤子之前的全部文明解法，按<b>从安静到吵闹</b>的顺序试：
    /// ① 能开的门就开 → ② <b>安静入侵</b>（撬锁 / 轻声拆围栏）→ ③ 都不行，返回 false 交回破防（砸，噪音以 Wiki 配置为准）。
    /// </summary>
    private bool TryCivilizedEntry() => TryOpenDoor() || TryQuietIntrusion();

    /// <summary>
    /// <b>安静入侵</b>：蹲在门口撬锁，或者蹲在围栏边上一点一点拆。
    /// <para>
    /// 噪音、丧尸听觉阈值与入侵耗时均以 Wiki 配置为准；安静入侵应比走路/砸墙更难被察觉，但会付出时间代价。
    /// <b>那段时间就是留给你的守夜人发现他的窗口</b> —— 你没派人，就没有窗口。
    /// </para>
    /// 接管本帧返回 true；不该/不能安静入侵返回 false（交回破防去砸）。
    /// </summary>
    private bool TryQuietIntrusion()
    {
        if (_findQuiet is null || _pickLock is null || _dismantle is null || !CanOperateDoors)
        {
            return false;
        }
        if (!_findQuiet(GlobalPosition, DoorSearchRadius, out Vector2 point, out bool isDoor,
                        out LockTier lockTier, out StructureTier fenceTier))
        {
            _quietProgress = 0;
            return false; // 附近没有锁着的门、也没有围栏格 → 交回破防
        }

        var s = new IntrusionSituation
        {
            // 「已经被发现」= 我这会儿正跟人交火。都打起来了还蹲那儿撬锁 = 送死。
            Detected = _enemy is { Alive: true },
            HasLockpicks = Lockpicks > 0,
            LockedDoorNearby = isDoor,
            DoorLock = lockTier,
            FenceNearby = !isDoor,
            FenceTier = fenceTier,
            SecondsUntilDawn = _untilDawn?.Invoke() ?? 600.0, // 未注入 → 按"夜里还长"，不构成时间压力
            FailedPickAttempts = _failedPicks,
        };

        IntrusionMethod method = IntrusionLogic.Choose(s, Intrusion);
        if (method is IntrusionMethod.Bash or IntrusionMethod.None)
        {
            _quietProgress = 0;
            return false; // 被发现了 / 撬断了 / 来不及了 → 交回破防（砸，把全场招来）
        }

        // 换目标了 → 进度清零（不能撬一半跑去拆围栏还接着算）
        if (_quietProgress > 0 && (_quietIsDoor != isDoor || _quietPoint.DistanceTo(point) > 24f))
        {
            _quietProgress = 0;
        }
        _quietPoint = point;
        _quietIsDoor = isDoor;

        // 够不着：先走过去（走路噪音与作业噪音的相对关系以 Wiki 配置为准）
        if (GlobalPosition.DistanceTo(point) > DoorReach + Radius)
        {
            CommandMoveTo(point);
            return true;
        }

        // 蹲下作业。⚠️ 每一"拍"发一次作业噪音——这是他唯一的破绽，具体噪音数值以 Wiki 配置为准。
        CancelOrders();
        double needed = method == IntrusionMethod.PickLock
            ? DoorLogic.PickSeconds(lockTier)
            : IntrusionLogic.DismantleSeconds(fenceTier, Intrusion);

        double before = _quietProgress;
        _quietProgress += QuietTickDelta;
        if (Mathf.FloorToInt(before / QuietNoiseTick) != Mathf.FloorToInt(_quietProgress / QuietNoiseTick))
        {
            EmitNoise(IntrusionLogic.QuietNoiseRadius, NoiseKind.Combat);
        }

        if (_quietProgress < needed)
        {
            return true; // 还在撬 / 还在拆
        }

        _quietProgress = 0;
        if (method == IntrusionMethod.PickLock)
        {
            // 撬一次锁：成败都花掉了那几秒（DoorLogic 的口径）。断了就少一根铁丝。
            if (!_pickLock(point, this))
            {
                _failedPicks++;
                Lockpicks = Math.Max(0, Lockpicks - 1);
                GD.Print($"[劫掠者] {DisplayName} 撬断了一根铁丝（第 {_failedPicks} 次），还剩 {Lockpicks} 根。");
            }
            return true;
        }

        // 拆完一格围栏 = 一个货真价实的 100px 的洞（复用既有摧毁链路，不另造"洞"的概念）。
        _dismantle(point, this);
        GD.Print($"[劫掠者] {DisplayName} 悄悄拆开了一格围栏——营地上多了个洞，没有一个人听见。");
        return true;
    }

    /// <summary>安静作业的推进步长（秒/帧，按 60fps 的物理帧；吃缩放 delta 由调用处传，此处用固定步长近似）。</summary>
    private double QuietTickDelta => _quietTickDelta;
    private double _quietTickDelta;

    /// <summary>按配置间隔发一次作业噪音（不必每帧发，省广播）。</summary>
    private const double QuietNoiseTick = 1.0;

    protected override void Think(double delta)
    {
        _quietTickDelta = delta; // 安静作业按**已缩放**的 delta 推进（暂停冻结、加速档更快，与游戏时间同步）

        // 逃出场的劫掠者：已经不在这场仗里了，什么都不做（见 TryEscape）。
        if (HasEscaped)
        {
            return;
        }

        TickTacticTimers(delta);

        // 目标获取/丢失 + 战术决策：raycast 贵，走基类的 0.2s 节流闸；其间维持现指令（基类照常逼近/射击/近战/移动）。
        // 战术快照与姿态**只在节流帧重算**（0.2s，5Hz）：小队规模/序号/敌人视锥/敌我数是"局面"，不是"每帧"的东西。
        // 执行层（下面）每帧只把坐标刷新一遍（record struct 的 with = 栈上拷贝），不再重跑任何遍历。
        if (PerceptionDue(delta))
        {
            UpdateThreatPicture();
            _situation = BuildSituation();
            _stance = RaiderTactics.DecideStance(_situation, Tactics);
            TryCallReinforcements();
        }

        // 战斗态掏/收火把：有存活目标=已开战→点火把（自照亮回视野+成暴露信标）；脱战→收火把归 0。
        // 光源场消费 ActiveTorchLight，把火把半径内的局部光照与玩家遮暗一起更新。
        bool wantsTorch = RaiderTorchLogic.ShouldCarryTorch(
            hasLiveEnemy: _enemy is { Alive: true },
            retreating: _stance == RaiderStance.Retreat);
        SetCarriedLight(wantsTorch ? TorchLightIntensity : 0f);

        // 撤退压过一切（含破防）：命都不要了，还砸什么墙。
        if (_stance == RaiderStance.Retreat)
        {
            ExecuteRetreat();
            return;
        }

        // 破防：每帧真 delta（TryBreach 内部自带 0.3s 可达节流）。被围栏/大门阻隔则走到最近结构前砸墙；接管本帧则不再追击/游荡。
        if (_breach != null && _breach.TryBreach(this, delta, _enemy?.GlobalPosition))
        {
            return;
        }

        // 有敌人：按战术姿态行动（包抄 / 掩体 / 正面）。
        if (_enemy is { Alive: true })
        {
            ExecuteCombat(delta);
            return;
        }

        // 岗哨（敌营站岗的）：不游荡。在岗站定面朝岗位朝向 / 听见动静去看一眼 / 看完回岗。
        if (_post is not null)
        {
            ThinkAsSentry(delta);
            return;
        }

        // 丢失视野后走向最后目击点侦查一次（复用既有 _lastKnown 通道，不新造状态机）。
        if (_lastKnown is { } seen)
        {
            _lastKnown = null;
            CommandMoveTo(seen);
            _wanderTimer = _rng.RandfRange(2.5f, 5.0f);
            return;
        }

        // 游荡巡场：到点或超时换一个随机目标点。
        _wanderTimer -= delta;
        if (!HasMoveOrder || _wanderTimer <= 0)
        {
            _wanderTimer = _rng.RandfRange(2.5f, 5.0f);
            var p = new Vector2(
                _rng.RandfRange(_wanderBounds.Position.X, _wanderBounds.End.X),
                _rng.RandfRange(_wanderBounds.Position.Y, _wanderBounds.End.Y));
            CommandMoveTo(p);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  敌营岗哨（用户口径：「敌人营地也会有类似幸存者营地的岗哨」）
    //  最小可用版：固定岗位 + 固定朝向 · 被噪音唤醒去查看 · 查看完回岗。
    //  巡逻路线 / 换班 / 警戒等级 = 不做（遗留）。
    // ════════════════════════════════════════════════════════════════════════

    private SentryPost? _post;
    private SentrySweep _sweep = SentrySweep.Default;
    private double _sentryClock;        // 扫视的时间轴（吃已缩放 delta：暂停冻结、加速档更快，与游戏时间同步）
    private Vector2? _investigate;      // 要去查看的点（噪音源 / 最后目击点）
    private double _investigateElapsed;

    /// <summary>哨兵参数（暂用默认；与战术参数一并以 Wiki 配置为准）。</summary>
    private static SentryParams Sentry => SentryParams.Default;

    /// <summary>
    /// 把这个劫掠者变成<b>哨兵</b>：钉在 <paramref name="post"/>、<b>不再游荡</b>，
    /// 在岗时绕着 <paramref name="facingRadians"/> <b>有规律地左右扫视</b>（<paramref name="sweep"/>）。
    /// <para>
    /// ⚠️ <paramref name="facingRadians"/> 是<b>扫视的中心</b>，不是唯一朝向 —— 哨兵不是雕像。
    /// 扫视<b>周期固定、可观察、可预测</b>：玩家能蹲着数拍子、算准他背对你的那几秒窗口再动
    /// （端点停顿就是那个"现在动"的信号）。<b>绝不随机转头</b>——那只会逼玩家读档。
    /// </para>
    /// <para>
    /// 唯一的随机是<b>初始相位</b>：生成时掷一次（走可注入的 <see cref="IRandomSource"/>），此后永不再变。
    /// 它只是让全场哨兵不至于像仪仗队一样整齐划一地转头；每个哨兵<b>自己</b>仍然是完全规律的。
    /// </para>
    /// </summary>
    /// <param name="sweep">扫视档位（<see cref="SentrySweep.Alert"/> 警觉 / <see cref="SentrySweep.Slack"/> 懈怠）；null = 警觉。</param>
    public void ConfigureSentry(Vector2 post, float facingRadians, SentrySweep? sweep = null)
    {
        _sweep = sweep ?? SentrySweep.Default;
        double phase = SentryLogic.RollSweepPhase(_tacticRng, _sweep);
        _post = new SentryPost(Sn(post), facingRadians, phase);
        _sentryClock = 0;
        SetFacing(SentryLogic.SweepFacing(_post.Value, 0, _sweep));
    }

    /// <summary>
    /// 听见噪音：**记进哨兵自己的查看通道**（基类那条写的是它的私有 _lastSeenPos，我这套感知不读它）。
    /// 走的仍是既有噪音通道 —— 只是让哨兵知道"该去看哪儿、看了多久"，以便**看完能回岗**。
    /// </summary>
    protected override void HearNoise(Vector2 pos)
    {
        base.HearNoise(pos);
        _investigate = pos;
        _investigateElapsed = 0;
        _lastKnown ??= pos; // 非哨兵的普通劫掠者也复用同一条侦查通道
    }

    private void ThinkAsSentry(double delta)
    {
        SentryPost post = _post!.Value;
        _sentryClock += delta; // 扫视的时间轴（吃缩放后的 delta ⇒ 暂停时头也停住、加速档扫得更快）

        // 丢失视野的最后目击点也算"要去看的地方"（与噪音同一条通道）。
        if (_lastKnown is { } seen)
        {
            _investigate = seen;
            _investigateElapsed = 0;
            _lastKnown = null;
        }

        if (_investigate is not null)
        {
            _investigateElapsed += delta;
        }

        SentryAction action = SentryLogic.DecideIdle(
            Sn(GlobalPosition), post,
            _investigate is { } iv ? Sn(iv) : null,
            _investigateElapsed, Sentry);

        // 朝向的**唯一真源**在纯逻辑（在岗 ⇒ 按扫视规律转；离岗 ⇒ 不干预、由行进方向定）。
        // 这条规则不能散写在这里——否则"扫视是有规律的"就没有测试能抓住它。
        // 每帧只是一次取模 + 一次线性插值（无三角函数），N 个哨兵可忽略。
        // 视野锥自动跟转：Actor.PerceiveNearest / CanPerceive 都是**实时读 FacingAngle** 构造视锥，
        // 所以头转到哪儿、锥就扫到哪儿 —— 玩家绕的是真锥，不是画出来的装饰。
        SetFacing(SentryLogic.FacingFor(action, post, FacingAngle, _sentryClock, _sweep));

        switch (action)
        {
            case SentryAction.Investigate:
                Vector2 target = _investigate!.Value;
                if (GlobalPosition.DistanceTo(target) <= Sentry.PostArrivalTolerance)
                {
                    _investigate = null; // 到了，什么都没有 → 下一帧转 ReturnToPost
                    return;
                }
                CommandMoveTo(target);
                return;

            case SentryAction.ReturnToPost:
                _investigate = null;
                CommandMoveTo(Gd(post.Position));
                return;

            default: // HoldPost：站定（朝向已在上面由 SentryLogic.FacingFor 钉回岗位朝向）
                _investigate = null;
                CancelOrders();
                return;
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  战术 AI（包抄 / 找掩体 / 撤退 / 呼叫增援）
    //  ⚠️ 判定全在纯逻辑 RaiderTactics（Link 进单测）；本节只做**空间执行**：采样、raycast、下寻路指令。
    //  ⚠️ 丧尸不走这一套（它就该直线冲上来——那是本作有意为之的根本区别）。
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>战术参数（数据驱动，读 res://data/raider_tactics.json）。</summary>
    private static RaiderTacticsParams Tactics => RaiderTacticsData.Params;

    /// <summary>
    /// 场上活着的劫掠者注册表：<b>同伴池 + 小队序号</b>的唯一来源。
    /// 不必让 CampMain 额外注入队友池（它只给了敌对池）；入树登记、离树注销，N ≤ 个位数。
    /// </summary>
    private static readonly List<Raider> Live = new();

    /// <summary>
    /// <b>逃跑者身份钩子</b>（只出机制，不写剧情）：某个劫掠者活着逃出了地图。
    /// 剧情层日后可订阅它来做"以后再来报复"——本类不含任何剧情内容。
    /// </summary>
    public static event Action<RaiderEscape>? Escaped;

    /// <summary>本单位已逃出场（活着走掉的，不是死了）。已逃者不再参战。</summary>
    public bool HasEscaped { get; private set; }

    private TacticalSituation _situation;  // 局面快照（0.2s 一刷；执行层每帧只 with 刷坐标，不重跑遍历）
    private Actor? _enemy;                 // 自维护的感知目标（不走基类 UpdatePerception：那一版数不出"敌人有几个"）
    private Vector2? _lastKnown;           // 最后目击点（丢失视野后走过去看一眼）
    private int _visibleHostiles;
    private RaiderStance _stance = RaiderStance.Engage;

    private bool _retreatCommitted;        // 一旦开逃就逃到底
    private Vector2? _escapePoint;

    private Vector2? _coverPoint;          // 掩体（缓存；敌人挪远了才重搜）
    private Vector2? _peekPoint;           // 掩体对应的探头射击位
    private Vector2 _coverEnemyPos;
    private double _coverTimer;            // 掩体重搜节流（错峰起始，防全队同帧搜）

    private Vector2? _flankPoint;          // 包抄落点（缓存）
    private Vector2 _flankEnemyPos;
    private bool _flankDone;

    private double _suppressedTimer;       // 挨打后缩回掩体的剩余时长
    private double _callCooldown;          // 呼叫增援冷却
    private double _sinceLastShot = 999;   // 上次开火距今（枪声还在替我喊人吗）
    private double _prevCooldown;          // 用于识别"刚开了一枪"（冷却陡增 = 出手了）

    private readonly IRandomSource _tacticRng = new SystemRandomSource();

    /// <summary>
    /// 本单位的有效交战射程：远程按武器 <c>MaxRange</c>（那是远程开火距离的权威口径，非 AttackRange），
    /// 近战/未填射程模型按 <c>AttackRange</c>。掩体与包抄的落点半径都据此定——绕过去/躲进去<b>正好能打着人</b>。
    /// </summary>
    private float TacticalRange =>
        IsRanged && AttackWeapon.MaxRange is double max ? (float)max : AttackRange;

    /// <summary>挨打即被压制 → 缩回掩体（基类钩子；丧尸/幸存者不响应）。</summary>
    protected override void OnDamaged(Actor? attacker)
    {
        _suppressedTimer = Tactics.SuppressedHunkerTime;
        _flankDone = true;   // 已经在挨枪了，就别再绕路了——先找掩体/还击
    }

    private void TickTacticTimers(double delta)
    {
        if (_suppressedTimer > 0) _suppressedTimer -= delta;
        if (_callCooldown > 0) _callCooldown -= delta;
        if (_coverTimer > 0) _coverTimer -= delta;
        _sinceLastShot += delta;

        // "刚开了一枪"检测：攻击冷却只会在真的出手时陡增（基类 TryAttack 里重置）。免改基类的开火事件。
        double cd = AttackCooldownRemaining;
        if (cd > _prevCooldown + 0.01)
        {
            _sinceLastShot = 0;
        }
        _prevCooldown = cd;
    }

    /// <summary>
    /// 一趟感知遍历同时得出「最近的可见敌人」与「看得见几个敌人」（寡不敌众判定要后者）。
    /// raycast 预算与基类 <see cref="Actor.PerceiveNearest"/> <b>完全相同</b>：<see cref="Actor.CanPerceive"/>
    /// 内部先做廉价锥检（视距+半角）、只对锥内候选补一条射线。
    /// </summary>
    private void UpdateThreatPicture()
    {
        Actor? nearest = null;
        float bestD = float.MaxValue;
        int count = 0;

        foreach (Actor c in HostileCandidates())
        {
            if (!CanPerceive(c, BaseSightRange))
            {
                continue;
            }
            count++;
            float d = GlobalPosition.DistanceTo(c.GlobalPosition);
            if (d < bestD)
            {
                bestD = d;
                nearest = c;
            }
        }

        _visibleHostiles = count;

        if (nearest != null)
        {
            if (!ReferenceEquals(nearest, _enemy))
            {
                InvalidateTacticalPoints(); // 换了目标：旧掩体/包抄点作废
                _flankDone = false;
            }
            _enemy = nearest;
            _lastKnown = nearest.GlobalPosition;
            return;
        }

        if (_enemy != null)
        {
            _enemy = null;              // 丢失视野（出锥/遮挡/进暗/超视距）→ _lastKnown 保留，Think 里走过去侦查
            InvalidateTacticalPoints();
            _flankDone = false;
        }
    }

    private void InvalidateTacticalPoints()
    {
        _coverPoint = null;
        _peekPoint = null;
        _flankPoint = null;
    }

    /// <summary>场上活着的同阵营同伴（不含自己）。</summary>
    private IEnumerable<Raider> Allies()
        => Live.Where(r => !ReferenceEquals(r, this)
                        && IsInstanceValid(r) && r.Alive && !r.HasEscaped && r.Faction == Faction);

    /// <summary>
    /// 小队序号：在「本队按实例 id 排序」里的下标。<b>0 号顶正面吸引火力，其余人包抄</b>——
    /// 这就是"别全部走同一条路"。按 id 排序保证它是稳定的（同一个人一直当 0 号，不会每帧换人）。
    /// </summary>
    private int SquadIndex()
    {
        ulong me = GetInstanceId();
        int idx = 0;
        foreach (Raider r in Allies())
        {
            if (r.GetInstanceId() < me)
            {
                idx++;
            }
        }
        return idx;
    }

    /// <summary>
    /// 组装战术输入快照。敌人的<b>视野锥</b>（包抄要绕的那个东西）按视野系统同一套规则现算：
    /// <c>VisionLogic.ConeFor(环境光, R0)</c> —— 夜里锥又短又窄，所以<b>夜里更容易包抄</b>，这是自然涌现的。
    /// （劫掠者是在**估计**你能看到哪儿，不读你的内部状态；故用环境光而非你脚下的精确局部光照。）
    /// </summary>
    private TacticalSituation BuildSituation()
    {
        int allies = Allies().Count();
        bool hasEnemy = _enemy is { Alive: true };

        var cone = new VisionLogic.VisionCone(VisionLogic.BaseRange, VisionLogic.DayHalfAngleDeg);
        Vector2 enemyPos = GlobalPosition;
        Vector2 enemyFacing = Vector2.Right;

        if (hasEnemy)
        {
            float ambient = VisionLogic.AmbientLight(
                Clock is null ? DayPhase.DayExplore : Clock.CurrentPhase, indoorsDark: false);
            cone = VisionLogic.ConeFor(ambient, VisionLogic.BaseRange);
            enemyPos = _enemy!.GlobalPosition;
            enemyFacing = Vector2.FromAngle(_enemy.FacingAngle);
        }

        return new TacticalSituation
        {
            Self = Sn(GlobalPosition),
            HealthFraction = HealthFraction,
            HasVisibleEnemy = hasEnemy,
            EnemyPos = Sn(enemyPos),
            EnemyFacing = Sn(enemyFacing),
            EnemyConeHalfAngleDeg = cone.HalfAngleDeg,
            EnemyConeRange = cone.Range,
            VisibleHostiles = _visibleHostiles,
            AlliesAlive = allies,
            SquadIndex = SquadIndex(),
            SquadSize = allies + 1,
            WeaponRange = TacticalRange,
            IsRanged = IsRanged,
            RetreatCommitted = _retreatCommitted,
            FlankDone = _flankDone,
        };
    }

    // ─────────────────────── 战术①② 包抄 + 掩体 ───────────────────────

    private void ExecuteCombat(double delta)
    {
        // 复用节流帧的局面快照，只把两个坐标刷成当帧真值（到位判定/绕锥判定要用实时位置；
        // 小队规模、敌人视锥这些"局面量"不必每帧重算——那正是上一版每帧跑 LINQ 的性能坑）。
        TacticalSituation s = _situation with
        {
            Self = Sn(GlobalPosition),
            EnemyPos = Sn(_enemy!.GlobalPosition),
        };

        if (_stance == RaiderStance.Flank && ExecuteFlank(s))
        {
            return; // 还在绕路
        }

        if (_stance == RaiderStance.TakeCover && ExecuteCover(delta, s))
        {
            return; // 在掩体后（缩着 / 探头）
        }

        // 正面交战（含：包抄已到位、找不到掩体、近战、敌人已贴脸）→ 交基类逼近+开火。
        CommandAttack(_enemy!);
    }

    /// <summary>
    /// <b>包抄</b>：绕出目标视野锥。落点由纯逻辑 <see cref="RaiderTactics.FlankPoint"/> 出
    /// （敌人朝向 ± (视锥半角 + 余量 + 抖动)，半径 = 自己射程 × 系数）——绕的正是视野系统里那个**真**视野锥。
    /// 返回 true=本帧在绕路（接管）；false=已到位/已在锥外 → 交回正面交战。
    /// </summary>
    private bool ExecuteFlank(in TacticalSituation s)
    {
        Vector2 enemyPos = _enemy!.GlobalPosition;

        // 缓存失效：敌人挪远了 → 重算落点（纯三角函数，不打射线，便宜）。
        if (_flankPoint is null || enemyPos.DistanceTo(_flankEnemyPos) > Tactics.FlankEnemyMoveInvalidate)
        {
            _flankPoint = Gd(RaiderTactics.FlankPoint(s, Tactics, _tacticRng));
            _flankEnemyPos = enemyPos;
        }

        // 到位 或 已经绕出锥外（目标转身了/它自己走开了）→ 收工，转打。
        bool arrived = GlobalPosition.DistanceTo(_flankPoint.Value) <= Tactics.FlankArrivalTolerance;
        if (arrived || !RaiderTactics.IsInEnemyCone(s))
        {
            _flankDone = true;
            _flankPoint = null;
            return false;
        }

        CommandMoveTo(_flankPoint.Value);
        return true;
    }

    /// <summary>
    /// <b>找掩体</b>：躲到能**断掉目标视线**的位置。"断视线"不是我自己编的判据——
    /// 用的就是视野系统那条唯一权威的遮挡射线 <see cref="VisionOcclusion.IsOccluded"/>（墙层 0b0100，
    /// 与玩家的遮暗渲染 <c>VisionMask</c> 同源）：<b>敌人 → 候选点被墙挡住 = 那就是掩体</b>。
    /// 开火时探头、被压制时缩回。返回 true=本帧被掩体行为接管。
    /// </summary>
    private bool ExecuteCover(double delta, in TacticalSituation s)
    {
        Vector2 enemyPos = _enemy!.GlobalPosition;
        RefreshCover(enemyPos, s);

        if (_coverPoint is not { } cover)
        {
            return false; // 附近没掩体 → 回落正面交战
        }

        CoverPhase phase = RaiderTactics.PhaseFor(AttackCooldownRemaining, _suppressedTimer, Tactics);
        Vector2 goal = phase == CoverPhase.Peek && _peekPoint is { } peek ? peek : cover;

        if (GlobalPosition.DistanceTo(goal) > ArrivalTolerance)
        {
            CommandMoveTo(goal); // 还没到位：跑过去（缩回 or 探出）
            return true;
        }

        if (phase == CoverPhase.Peek)
        {
            CommandAttack(_enemy!); // 探头到位：开火（基类在射程内会停下打——掩体选点已保证在射程内）
            return true;
        }

        CancelOrders(); // 缩在掩体后：站定不动，目标看不见我，我也不开火
        return true;
    }

    /// <summary>
    /// 掩体搜索（**性能热点，三重节流**）：①只在 <see cref="RaiderTacticsParams.CoverSearchRadius"/> 内采样
    /// 固定 12 个点，<b>绝不做全图搜索</b>；②结果缓存，只在「没掩体」或「敌人挪出
    /// <see cref="RaiderTacticsParams.CoverEnemyMoveInvalidate"/>」时重搜；③重搜之间强制间隔
    /// <see cref="RaiderTacticsParams.CoverRecomputeInterval"/> 秒，且各单位起始相位随机错峰（不会全队同帧开搜）。
    /// </summary>
    private void RefreshCover(Vector2 enemyPos, in TacticalSituation s)
    {
        bool stale = _coverPoint is null
                  || enemyPos.DistanceTo(_coverEnemyPos) > Tactics.CoverEnemyMoveInvalidate;
        if (!stale || _coverTimer > 0)
        {
            return;
        }
        _coverTimer = Tactics.CoverRecomputeInterval;
        _coverEnemyPos = enemyPos;

        RaiderTacticsParams p = Tactics;
        System.Numerics.Vector2[] probes = RaiderTactics.SampleCoverProbes(Sn(GlobalPosition), p);
        var candidates = new List<CoverCandidate>(probes.Length);

        foreach (System.Numerics.Vector2 probe in probes)
        {
            Vector2 pt = Gd(probe);
            // ①「敌人看不看得见这个点」——掩体的**定义**就在这一行（视野系统的遮挡权威源）。
            bool breaksSight = SightBlockedBetween(enemyPos, pt);
            if (!breaksSight)
            {
                candidates.Add(new CoverCandidate(probe, false, false)); // 省掉第二条射线：不是掩体，可达与否都不重要
                continue;
            }
            // ② 走不走得过去（直线不撞墙的廉价近似；真寻路交给 NavigationAgent）。
            bool reachable = !SightBlockedBetween(GlobalPosition, pt);
            candidates.Add(new CoverCandidate(probe, true, reachable));
        }

        System.Numerics.Vector2? pick =
            RaiderTactics.SelectCover(Sn(GlobalPosition), Sn(enemyPos), TacticalRange, candidates, p);

        if (pick is null)
        {
            _coverPoint = null;
            _peekPoint = null;
            return;
        }

        _coverPoint = Gd(pick.Value);
        _peekPoint = FindPeekSpot(_coverPoint.Value, enemyPos, p);
    }

    /// <summary>
    /// 探头位：从掩体朝敌人方向探出去，直到<b>真的能看见敌人</b>（否则探了个寂寞，子弹打在墙上）。
    /// 逐级加码 1×/2×/3× PeekOffset，最多 3 条射线，且只在<b>选定掩体那一刻</b>算一次（不是每帧）。
    /// 三级都探不出来 → 回落掩体点本身（基类照常开火；下个重搜周期会换个掩体）。
    /// </summary>
    private Vector2 FindPeekSpot(Vector2 cover, Vector2 enemyPos, RaiderTacticsParams p)
    {
        for (int step = 1; step <= 3; step++)
        {
            var stepped = p with { PeekOffset = p.PeekOffset * step };
            Vector2 candidate = Gd(RaiderTactics.PeekPosition(Sn(cover), Sn(enemyPos), stepped));
            if (!SightBlockedBetween(candidate, enemyPos))
            {
                return candidate;
            }
        }
        return cover;
    }

    // ─────────────────────── 战术③ 撤退 ───────────────────────

    /// <summary>
    /// <b>撤退</b>：真的逃（跑向背向威胁的地图边缘），不是原地转圈。逃到边缘 → 触发
    /// <see cref="Escaped"/> 身份钩子并退出这场仗（活着走掉的人，日后可能再回来——那是剧情层的事）。
    /// </summary>
    private void ExecuteRetreat()
    {
        _retreatCommitted = true; // 逃了就逃到底：不给"跑两步觉得又行了"的回头机会
        InvalidateTacticalPoints();

        Vector2 threat = _enemy?.GlobalPosition ?? _lastKnown ?? GlobalPosition;

        if (_escapePoint is null)
        {
            System.Numerics.Vector2? pick = RaiderTactics.SelectEscape(
                Sn(GlobalPosition), Sn(threat), EscapeExits(), Tactics);
            if (pick is null)
            {
                return; // 无出口可逃（游荡范围退化）→ 站着不动也好过原地转圈
            }
            _escapePoint = Gd(pick.Value);
        }

        if (GlobalPosition.DistanceTo(_escapePoint.Value) <= EscapeArrivalTolerance)
        {
            TryEscape();
            return;
        }
        CommandMoveTo(_escapePoint.Value);
    }

    /// <summary>逃跑出口：游荡范围的四条边中点（＝这张图的"外面"）。空间层数据，纯逻辑只管挑哪个。</summary>
    private System.Numerics.Vector2[] EscapeExits()
    {
        Rect2 b = _wanderBounds;
        if (b.Size == Vector2.Zero)
        {
            return Array.Empty<System.Numerics.Vector2>();
        }
        Vector2 c = b.GetCenter();
        return new[]
        {
            Sn(new Vector2(b.Position.X, c.Y)), // 西
            Sn(new Vector2(b.End.X, c.Y)),      // 东
            Sn(new Vector2(c.X, b.Position.Y)), // 北
            Sn(new Vector2(c.X, b.End.Y)),      // 南
        };
    }

    /// <summary>
    /// 逃出场：发出<b>逃跑者身份钩子</b>（谁、第几天、剩几成血），此后不再参战。
    /// <b>只做机制，不写剧情</b>——"以后再来报复"由订阅方（阵营/剧情层）自己写。
    /// ⚠️ 刻意<b>不</b>走 <c>KillNonCombat</c>：那会触发 <c>Died</c> 事件（订阅方按"被打死"处理，
    /// 克莉丝汀甚至会走死亡剧情）。<b>逃走 ≠ 死了</b>。
    /// </summary>
    private void TryEscape()
    {
        if (HasEscaped)
        {
            return;
        }
        HasEscaped = true;
        CancelOrders();
        SetCarriedLight(0f);
        Escaped?.Invoke(new RaiderEscape(DisplayName, Clock?.Day ?? 0, HealthFraction));
        GD.Print($"[劫掠者] {DisplayName} 逃出了地图（剩 {HealthFraction:P0} 血）。");
    }

    // ─────────────────────── 战术④ 呼叫增援 ───────────────────────

    /// <summary>
    /// <b>呼叫增援</b>。⚠️ 大部分增援是<b>免费</b>的：战斗噪音<b>不分阵营</b>（<see cref="NoiseKind.Combat"/>），
    /// 劫掠者一开枪（噪音半径以 Wiki 配置为准），半径内所有<b>闲着的</b> AI 已经被噪音系统 <c>CommandMoveTo</c> 过来了——
    /// <b>枪声本身就是呼叫</b>，不必也不该再造一套。
    /// <para>
    /// 显式呼叫只补噪音盖不住的两个缺口（判定见 <see cref="RaiderTactics.ShouldCallReinforcements"/>）：
    /// <b>①拿匕首的</b>（近战噪音较小）、<b>②还没开枪的</b>（潜行接近/包抄途中）。
    /// </para>
    /// <para>
    /// 且它<b>不新造 AI 状态机</b>：走的就是既有的「最后目击点 + <c>CommandMoveTo</c>」通道，
    /// 与噪音侦查、丢失视野侦查是同一条路——只是把队友引向<b>敌人</b>（而非枪响处），这比噪音更有用。
    /// </para>
    /// </summary>
    private void TryCallReinforcements()
    {
        if (_enemy is not { Alive: true })
        {
            return;
        }

        RaiderTacticsParams p = Tactics;

        // 只**数**一下呼喊半径内还有几个闲着的同伙（喊了也没人来就别喊，白费一个冷却——
        // 何况呼喊不分阵营，白喊一嗓子只会把丧尸招来）。这里只读不写：不生成任何单位。
        int alliesAlive = 0;
        int idleInRange = 0;
        foreach (Raider r in Allies())
        {
            alliesAlive++;
            if (!r.HasActiveTarget && r.GlobalPosition.DistanceTo(GlobalPosition) <= p.CallRadius)
            {
                idleInRange++;
            }
        }

        var s = new ReinforceSituation
        {
            EnemySpotted = true,
            VisibleHostiles = _visibleHostiles,
            AlliesAlive = alliesAlive,
            IdleAlliesInRange = idleInRange,
            CallCooldownRemaining = _callCooldown,
            WeaponNoiseRadius = AttackWeapon.NoiseRadius,
            SinceLastShot = _sinceLastShot,
        };

        if (!RaiderTactics.ShouldCallReinforcements(s, p))
        {
            return;
        }

        _callCooldown = p.CallCooldown;

        // 【呼喊 = 一次噪音，不是刷怪】走既有的 EmitNoise 通道发一个大半径战斗噪音；半径以 Wiki 配置为准。
        // 噪音系统（NoiseLogic + Actor.HearNoise）会把半径内**当前没有目标**的 AI CommandMoveTo 过来——
        // 它们**本来就在场上**。这里没有、也不可能有任何 new/Create/Spawn：
        //   ⚠️ 凭空刷敌人会毁掉噪音系统的全部意义（玩家控制噪音就白费了），本作绝不这么干。
        // 代价：战斗噪音**不分阵营** ⇒ 喊人也把丧尸招来了。这是取舍，不是 bug。
        EmitNoise(p.ShoutNoiseRadius, NoiseKind.Combat);
    }

    // ─────────────────────── 空间层小工具 ───────────────────────

    /// <summary>到点容差（掩体/探头位）。</summary>
    private const float ArrivalTolerance = 14f;
    /// <summary>到出口容差（逃出地图）。</summary>
    private const float EscapeArrivalTolerance = 24f;

    /// <summary>
    /// 两点之间视线是否被墙断掉——**视野系统的唯一权威遮挡源**（与玩家遮暗渲染 VisionMask 同一条射线、同一个墙层）。
    /// 掩体判定（"敌人看不见这个点"）与可达近似（"我走得过去"）都用它。
    /// </summary>
    private bool SightBlockedBetween(Vector2 from, Vector2 to)
    {
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        return space is not null && VisionOcclusion.IsOccluded(space, from, to);
    }

    // 纯逻辑用 System.Numerics.Vector2（零 Godot 依赖），此处转换。
    private static System.Numerics.Vector2 Sn(Vector2 v) => new(v.X, v.Y);
    private static Vector2 Gd(System.Numerics.Vector2 v) => new(v.X, v.Y);

    /// <summary>
    /// 候选敌对池：<see cref="_targetProvider"/> 输出经 <see cref="Factions.IsHostile"/> + 存活过滤
    /// （敌我一律走 IsHostile，不散写阵营相等比较）。感知锥/遮挡由基类 <see cref="Actor.CanPerceive"/> 再裁。
    /// </summary>
    private IEnumerable<Actor> HostileCandidates()
        => _targetProvider is null
            ? Array.Empty<Actor>()
            : _targetProvider().Where(a => a.Alive && Factions.IsHostile(Faction, a.Faction));
}
