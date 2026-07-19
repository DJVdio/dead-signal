using System.Text.Json.Serialization;

namespace DeadSignal.Combat;

/// <summary>伤害类型。锐器命中可在结算中转为钝器；钝器天然保留穿透。</summary>
public enum DamageType
{
    Sharp,
    Blunt,
}

/// <summary>护甲所属层，决定从外到内的物理叠放顺序（值越小越靠外）。</summary>
public enum ArmorSlot
{
    /// <summary>装甲层（最外，如板甲）。</summary>
    Plate = 0,

    /// <summary>外套层（中间，如皮甲/外衣）。</summary>
    Outer = 1,

    /// <summary>贴身层（最内，如布衣）。</summary>
    Skin = 2,
}

/// <summary>
/// 武器。数据驱动 POCO，字段全部来自设计文档第 5 节。
/// 数值为原型期拟定，最终由蒙特卡洛模拟器拉表微调。
/// </summary>
public sealed class Weapon
{
    public string Name { get; init; } = "";

    /// <summary>玩家可见的一行风味描述（黑色幽默；空串=无）。仅供 UI 展示，不参与战斗结算。</summary>
    public string Description { get; init; } = "";

    /// <summary>伤害区间下限（含）。全程小数运算。</summary>
    public double DamageMin { get; init; }

    /// <summary>伤害区间上限（含）。</summary>
    public double DamageMax { get; init; }

    /// <summary>穿透力。降低防方可 roll 的防御上限，具体值见 Wiki 配置表。</summary>
    public double Penetration { get; init; }

    public DamageType DamageType { get; init; }

    // ---- 流血轴（[T53]）：武器影响它**造成的伤口**流得多快。中性默认 ⇒ 既有武器零漂移。----

    /// <summary>
    /// 这把武器造成的伤口的**流血速率乘数**；具体值与改装效果见 Wiki 配置表。
    ///
    /// <para>
    /// 它是**伤口**的属性，由造成伤口的武器在建档时写入（<see cref="Body.RegisterBleed(string, double)"/>），
    /// 与**受害者**侧的体质倍率 <see cref="Body.BleedRateMultiplier"/> **正交相乘** ——
    /// 「谁在砍」和「谁在流血」是两件事，各自一根轴。
    /// </para>
    /// <para>
    /// 🔴 <b>零漂移</b>：中性默认值下，且 <see cref="Body.TickBleed"/> 里 Σ(乘数) **逐位等于**旧的伤口计数
    /// ⇒ 既有武器的结算路径逐字节不变。
    /// </para>
    /// </summary>
    public double BleedRateMultiplier { get; init; } = 1.0;

    /// <summary>
    /// 造成伤害时**额外**引发一处「小流血」的概率；具体值与改装效果见 Wiki 配置表。
    /// <para>
    /// 🔴 [T58]「小流血」**现在有了正式语义**：它就是 <see cref="BleedModel.BleedSeverity.Small"/> 这一级
    /// （旧的速率减半普通伤口口径**已退役**）。
    /// ⇒ 钉子扎出的口子会和别的小流血**按同一套规则合并**（同一处扎两下 ⇒ 中流血）。
    /// </para>
    ///
    /// <para>
    /// 这条**独立于锐器流血**：钝器（棍棒）本来一处伤口都不会造成（流血资格要求锐器抵达），
    /// 钉子强化正是要给钝器开一个**小口子**。
    /// </para>
    /// <para>
    /// 🔴 <b>零漂移</b>：<see cref="CombatEffectResolver.Apply"/> 里以 <c>&gt; 0</c> 为**前置短路**条件 ——
    /// 值为 0（＝所有既有武器）时**一次随机都不抽**，随机流不错位。护栏见
    /// <c>WeaponBleedAxisTests.Zero_drift_no_rng_consumed_when_bleed_on_hit_is_zero</c>。
    /// </para>
    /// </summary>
    public double BleedOnHitChance { get; init; }

    /// <summary>true = 双手武器；false = 单手。</summary>
    public bool TwoHanded { get; init; }

    /// <summary>可双持标记（如手枪+匕首）。单持不惩罚；双持惩罚见 <see cref="DualWield"/>。</summary>
    public bool CanDualWield { get; init; }

    /// <summary>true = 远程武器（有弹道误差角）；false = 近战（必中，无误差角）。</summary>
    public bool IsRanged { get; init; }

    /// <summary>
    /// 远程基础误差角（度）。向以准星为轴、半角为此值的锥内均匀采样一个偏转方向；越小越准。
    /// 近战忽略此字段。拟定待调（如手枪 3°、冲锋枪 6°、步枪 2°、狙击 0.5°）。
    /// </summary>
    public double BaseSpreadDegrees { get; init; }

    /// <summary>
    /// 出手间隔（秒/次）。攻速 = 1/间隔。双持攻速系数见 <see cref="DualWield"/>。
    /// 弹道飞行/时序由实时层消费，引擎只提供数值。拟定待调。
    /// </summary>
    public double AttackInterval { get; init; }

    // ---- 连发（枪械攻击模型：冷却→射击→冷却→射击；一次"射击"= BurstCount 发）----

    /// <summary>
    /// 连发数：一次"射击"连续打出的弹数。默认 1=单发；冲锋枪=3（三连发）。
    /// 每发独立锥形采样/命中/伤害 roll；<see cref="AttackInterval"/> 语义为**连发之后的冷却**
    /// （冷却→整轮连发→冷却→整轮连发）。拟定待调。
    /// </summary>
    public int BurstCount { get; init; } = 1;

    /// <summary>
    /// 连发内每弹间隔（秒），仅 <see cref="BurstCount"/> &gt; 1 时有意义。冷却在整轮连发之后才开始。拟定待调。
    /// </summary>
    public double BurstInterval { get; init; }

    // ---- 多弹丸（霰弹：一次击发同时打出 N 颗弹丸）----

    /// <summary>
    /// 弹丸数：<b>一发</b>子弹里同时打出的独立弹丸数。默认 1 = 单弹丸（全部既有武器，行为与随机流均不变）；
    /// 自制霰弹枪 = 8（用户拍板）。
    /// <para>
    /// 与 <see cref="BurstCount"/> 正交：连发是"一次射击打出 N <b>发</b>、有时间间隔"，
    /// 弹丸是"一发之内 N 颗<b>同时</b>飞出、无时间间隔"。
    /// </para>
    /// <para>
    /// 语义是<b>「单独计算」</b>（用户原话）：每颗弹丸各自独立选命中部位、独立过护甲三段判定、独立结算伤害与效果
    /// ——**不是**一次伤害 ×N。故一枪可以同时中头、胸、左臂，且每颗各自被挡下/半伤/全伤。
    /// 见 <see cref="CombatResolver.ResolveVolley"/>。
    /// </para>
    /// 空间侧（每颗弹丸各自的锥形散射方向）由 Godot 实时层逐颗调 <see cref="Ballistics.SampleDeflectionDegrees"/>；
    /// 引擎层不涉几何。
    /// </summary>
    public int PelletCount { get; init; } = 1;

    // ---- 弹丸飞行速度（[T68] 空间层弹道飞行轴；仅远程武器有意义）----

    /// <summary>
    /// 这把武器射出的弹丸的**飞行速度**（世界单位/秒）。默认沿用旧的全局弹道配置
    /// 全局常量的等效值 ⇒ <b>不填此字段的武器飞速与旧行为逐字节一致</b>（既有基线零漂移）。
    /// <para>
    /// 🔴 <b>零漂移（结构性证明）</b>：飞速是**空间层弹道飞行**属性——弹丸在 Godot 实时层沿直线匀速飞、
    /// 每物理步位移 = 飞速 × delta。<see cref="CombatResolver"/> / <c>Duel</c> / <see cref="Ballistics"/> 锥形采样
    /// **不建模弹丸飞行时间**（CLAUDE.md 明载 Sim 白送贴脸、不建模弹道），故本字段**不被任何 Sim 结算入口读取**，
    /// 对既有武器×护甲的 Sim 输出零影响。消费点唯一：Godot 侧 <c>Projectile._PhysicsProcess</c>。
    /// </para>
    /// <para>
    /// [T68] 此前飞速是全局常量、非逐武器字段，导致《弓与箭之道》与后续弓弩改装的飞速效果
    /// 都卡在这轴上无法落地。改为逐武器字段后：射手侧的书加成由 <see cref="Archery.Combine"/> 连乘进有效武器的飞速
    /// （<see cref="Archery.BookFlightSpeedMult"/>）；下游改装的飞速乘子由 <c>WeaponMod</c> 的 <c>WeaponStat.FlightSpeed</c>
    /// 走同一条乘算通路。同轴一律**乘算**（CLAUDE.md 铁律）。
    /// </para>
    /// </summary>
    public double FlightSpeed { get; init; } = 560.0;

    // ---- 弹药（枪必须有后勤代价：强，但打不起）----

    /// <summary>
    /// 所需弹药类型（<see cref="AmmoKeys"/> 之一：子弹/霰弹/箭矢）。
    /// <b>默认空串 = 不消耗弹药</b> —— 全部近战武器、爪击/撕咬、以及 <see cref="MeleeProfile"/> 派生的枪托近战
    /// 均不填此字段，行为与随机流一律不变（既有基线零漂移）。
    /// <para>
    /// 设计意图（用户拍板）：把枪的碾压从"平衡问题"变成<b>资源管理问题</b>。
    /// 连发与穿透以 Wiki 配置为准；每次扣动扳机的弹药消耗由连发数派生，子弹靠搜刮/制作取得。
    /// <b>不削数值，用后勤代价平衡。</b>
    /// </para>
    /// </summary>
    public string AmmoKey { get; init; } = "";

    /// <summary>本武器是否消耗弹药（<see cref="AmmoKey"/> 非空）。近战恒 <c>false</c>。</summary>
    [JsonIgnore] // 计算属性：从 AmmoKey 派生，不入 weapons.json（否则序列化噪声、反序列化也读不回 get-only）。
    public bool UsesAmmo => !string.IsNullOrEmpty(AmmoKey);

    /// <summary>
    /// 一次"射击"消耗的弹药数 = 连发数（打几发就扣几发）。不吃弹药的武器恒 0。
    /// <para>
    /// <b>由 <see cref="BurstCount"/> 派生而非另填一个字段</b>：两者永远不会不同步，且这正是物理事实——
    /// 打出几发就消耗对应数量的弹壳。这也是弹药系统真正的平衡杠杆：
    /// 越强的枪每次扣扳机越贵。
    /// </para>
    /// <para>
    /// <b><see cref="PelletCount"/> 不参与相乘</b>：多弹丸武器一发壳内同时飞出多颗弹丸（各自独立判定），
    /// 但消耗的是一发对应弹药，不按弹丸数重复扣除。
    /// </para>
    /// </summary>
    [JsonIgnore] // 计算属性：由 BurstCount 派生，不入 weapons.json。
    public int AmmoPerAttack => UsesAmmo ? Math.Max(1, BurstCount) : 0;

    // ---- 噪音（潜行：响声会把敌人引过来）----

    /// <summary>
    /// 开火/挥击的噪音半径（世界单位）。<b>默认 0 = 完全无声</b>（近战武器、天生武器不填此字段，
    /// 行为与随机流一律不变 —— <b>既有 Sim 基线零漂移</b>：Sim 是纯引擎对决、无空间层，根本不跑噪音）。
    /// <para>
    /// 语义：每次攻击在攻击者位置发出一次噪音。半径内**当前没有攻击目标**的**敌对** Actor
    /// （丧尸 + 劫掠者，用户拍板全量版）会走过去**侦查一次**——不是直接锁定你，是"听见动静、过来看看"。
    /// 空间侧的广播与寻路归 Godot 实时层（<c>Actor.EmitNoise</c>），引擎只出这个数据字段与
    /// <c>NoiseLogic</c> 的纯判定函数。
    /// </para>
    /// <para>
    /// <b>设计意图（用户拍板）</b>：「弓箭可以设计成潜行武器，但是也会发出一定声响」——
    /// 弓<b>不是无声</b>，只是声音小。目标手感是<b>「你能悄悄干掉一个，但干不掉一群」</b>。
    /// 故梯度锚在丧尸感知配置上：弓的噪音与嗅觉范围、枪的噪音与视距关系均以 Wiki 配置表为准。
    /// </para>
    /// <b>噪音不吃墙遮挡</b>（声音会绕、会穿——与吃遮挡的视线/气味不同）。数值拟定待调。
    /// </summary>
    public double NoiseRadius { get; init; }

    /// <summary>
    /// 枪托贴脸砸人的噪音（钝器量级 ≈ 棍棒）。**枪本体的大噪音不继承到枪托**——砸不是打，没有枪声；
    /// 但砸人也绝不是哑剧，故不是 0。由 <see cref="MeleeProfile"/> 写入。拟定待调。
    /// </summary>
    public const double StockMeleeNoise = 110;

    // ---- 砸墙（对营地结构：围栏/大门/门板）----

    /// <summary>
    /// **砸墙系数**：这把武器打**结构**（围栏/大门/门板，非血肉）时，伤害相对其平均伤害的倍率。
    /// 砸墙每击伤害 = 「砸墙有效武器」平均伤害 × 本系数（规则见 <c>StructureDamage</c>，消费层纯逻辑）。
    /// <para>
    /// <b>为什么需要它</b>：墙没有护甲、没有部位、不吃穿透——它只有一个血条。锐器那套「切开血肉」的数值
    /// 打在木头铁皮上毫无意义（拿匕首捅墙本来就该没用），而锤子恰恰相反。此系数就是「这把家伙对付**死物**
    /// 有多好使」，与它对付**活物**的伤害是两回事。
    /// </para>
    /// <para>
    /// <b>枪械</b>：本系数作用于 <see cref="MeleeProfile"/>（枪托）的伤害与节奏，**不是子弹伤害**——
    /// 子弹打不穿承重墙，抡枪托砸门才是真事。故枪的这一格填的是「抡枪托的效率」。
    /// </para>
    /// <para>
    /// <b>弓弩</b>：没有枪托可抡，系数作用于箭伤且取全表最低——射箭砸墙是全游戏最徒劳的行为。
    /// </para>
    /// <para>
    /// <c>null</c> = 未填，按伤害类型兜底（钝器/锐器各一档缺省值，见 <c>StructureDamage.DefaultBluntFactor</c>）——
    /// 新武器忘填不会变成"砸墙零伤害"。数值拟定待调，用户在 <b>本地 wiki 数值表</b>（<c>docs/wiki</c>）
    /// 『武器表』的「砸墙系数」列调 —— <b>wiki 是数值唯一设计源，代码向它看齐</b>
    /// （旧的 <c>docs/weapons-calc.xlsx</c> 已删除退役，见 <c>Archery</c> 的 [DECISION] impl-archery-redo）。
    /// </para>
    /// </summary>
    public double? StructureFactor { get; init; }

    // ---- 远程射程与射程内衰减（仅远程武器填；近战留 null=无射程模型）----

    /// <summary>
    /// 最大射程（世界单位）。>MaxRange 不可开火（<see cref="Ballistics.RangedDamageFactor"/> 返 0）。
    /// null = 无射程模型（近战/未设，恒满伤、无射程约束）。每把远程曲线不同。拟定待调。
    /// </summary>
    public double? MaxRange { get; init; }

    /// <summary>
    /// 满伤射程（世界单位）。distance ≤ FalloffStart 时衰减系数 = 1（满伤）；
    /// 之后线性降到 <see cref="MaxRange"/> 处的 <see cref="FalloffFloor"/>。null 视为 0（自枪口即衰减）。拟定待调。
    /// </summary>
    public double? FalloffStart { get; init; }

    /// <summary>
    /// 射程末端（MaxRange 处）的伤害下限系数，(0,1]（如 0.5 = 最远处半伤）。
    /// null 视为 1.0（不衰减，射程内恒满伤直到 MaxRange 外截断）。拟定待调。
    /// </summary>
    public double? FalloffFloor { get; init; }

    // ---- 枪托近战 profile（仅远程武器填；贴脸时供 Godot 空间层调用的近战版数值）----

    /// <summary>枪托近战伤害下限（钝击）。仅远程武器填；null 视为无近战 profile。拟定待调。</summary>
    public double? StockMeleeDamageMin { get; init; }

    /// <summary>枪托近战伤害上限（钝击）。null 视为无近战 profile。拟定待调。</summary>
    public double? StockMeleeDamageMax { get; init; }

    /// <summary>枪托近战出手间隔（秒/次）。null 时回落到远程 <see cref="AttackInterval"/>。拟定待调。</summary>
    public double? StockMeleeInterval { get; init; }

    /// <summary>枪托近战穿透（低）。null 视为 0。拟定待调。</summary>
    public double? StockMeleePenetration { get; init; }

    /// <summary>
    /// 这把枪的枪托近战噪音半径。<c>null</c> ⇒ 回落到全局 <see cref="StockMeleeNoise"/>（钝器量级）——
    /// 故**不填的武器行为完全不变**（零回归）。
    /// <para>
    /// 分枪型的理由：不同枪型的枪身质量不同，动静不是一回事。按枪身质量分档，具体值见 Wiki 配置表。
    /// </para>
    /// </summary>
    public double? StockMeleeNoiseRadius { get; init; }

    /// <summary>
    /// 枪托近战的伤害类型。**默认 <see cref="DamageType.Blunt"/>**（拿枪当棍子抡）⇒ 不填的武器行为完全不变（零回归）。
    /// <para>
    /// <b>它存在的唯一理由是「近战型改装」</b>：枪口挂了刺刀、枪托绑了利刃之后，贴脸打出来的**不再是钝击**。
    /// 此前这件事被表达成"<c>Weapon</c> 之外再旁挂一个覆盖对象"，而库存里的一件武器**只存一个名字**
    /// （<c>Item.RefKey</c>）—— 旁挂的东西一入库就丢了，刺刀于是成了纯装饰。把伤害类型收进 <see cref="Weapon"/> 自己
    /// 之后，"改装后的枪"就**无损地仍是一把普通 <see cref="Weapon"/>**，能入库、能装备、能存档、能进结算。
    /// </para>
    /// </summary>
    public DamageType StockMeleeDamageType { get; init; } = DamageType.Blunt;

    /// <summary>是否具备枪托近战 profile（远程武器且填了伤害上限）。近战武器恒为 false。</summary>
    [JsonIgnore] // 计算属性：由 IsRanged + StockMeleeDamageMax 派生，不入 weapons.json。
    public bool HasMeleeProfile => IsRanged && StockMeleeDamageMax.HasValue;

    /// <summary>
    /// 派生这把远程武器的"枪托贴脸"近战版：必中（<see cref="IsRanged"/>=false，无误差角）、伤害/穿透低、攻速慢，
    /// 伤害类型取 <see cref="StockMeleeDamageType"/>（默认钝击；装了刺刀/利爪型改装的枪为锐击），
    /// 单双手语义沿用本武器 <see cref="TwoHanded"/>。供 Godot 空间层贴脸判定时调用（判定本身在 Godot，不在引擎层）。
    /// 无 profile 时返回 null。
    /// </summary>
    public Weapon? MeleeProfile()
    {
        if (!HasMeleeProfile)
        {
            return null;
        }

        // AmmoKey **不复制**（空枪仍能抡枪托）。
        // NoiseRadius **不继承枪本体的大噪音**（砸不是打，没有枪声），但也**不是 0**——
        // 抡枪托砸人显然有动静，按近战量级给 <see cref="StockMeleeNoise"/>。
        return new Weapon
        {
            Name = Name + "（枪托）",
            DamageMin = StockMeleeDamageMin ?? 0,
            DamageMax = StockMeleeDamageMax!.Value,
            Penetration = StockMeleePenetration ?? 0,
            DamageType = StockMeleeDamageType,
            NoiseRadius = StockMeleeNoiseRadius ?? StockMeleeNoise,
            TwoHanded = TwoHanded,
            IsRanged = false,
            AttackInterval = StockMeleeInterval ?? AttackInterval,
        };
    }
}

/// <summary>护甲单层。数据驱动 POCO。</summary>
public sealed class ArmorLayer
{
    public string Name { get; init; } = "";

    /// <summary>玩家可见的一行风味描述（黑色幽默；空串=无）。仅供 UI 展示，不参与战斗结算。</summary>
    public string Description { get; init; } = "";

    /// <summary>对锐器的防御值。设计口径：锐防普遍约为钝防两倍，板甲更高。</summary>
    public double SharpDefense { get; init; }

    /// <summary>对钝器的防御值。</summary>
    public double BluntDefense { get; init; }

    /// <summary>
    /// 重量。<b>已接线</b>：消费层 <c>ItemRegistry.ArmorRoster</c> 从本字段投影出护甲重量（不复制数值），
    /// 经 <c>CarryWeight</c> 汇总进负重，由 <see cref="Loadout"/> 的 debuff 曲线出攻速/移速乘子
    /// （<c>Loadout.AttackSpeedMultiplier</c> / <c>SpeedMultiplier</c>），最终落到 <c>Pawn</c>/<c>Actor</c>。
    /// <para>引擎本身<b>不</b>在 <see cref="CombatResolver"/> 里读它 —— 负重惩罚属消费层的实时能力轴，
    /// 引擎只出数值。故本字段不参与 Sim 结算路径（结构性零漂移）。</para>
    /// </summary>
    public double Weight { get; init; }

    public ArmorSlot Slot { get; init; }

    /// <summary>
    /// 覆盖的具体身体部位名集合（<see cref="BodyPart.Name"/>，如"左手"）。粒度到具体部位——
    /// 因 <see cref="BodyRegion"/>/<see cref="BodyMacroRegion"/> 不分左右，区域级无法表达"仅左手/仅右手"，
    /// 故护甲覆盖以部位名表达（支持左右分、断肢分槽）。
    /// <c>null</c> = 覆盖全部位（向后兼容：现有护甲不填即全覆盖，行为不变）。
    /// 局部护甲（如左手套仅覆盖左手及其手指）才显式给出子集；命中部位不在集合内则该层不参与结算。
    /// 手部/脚部护甲应连带该手/脚的手指/脚趾（用 <see cref="HumanBody.SubtreeNames"/> 展开子树）。
    /// <para>
    /// 序列化为字符串数组；反序列化materialize为 <see cref="HashSet{T}"/>——STJ 不能实例化 <see cref="IReadOnlySet{T}"/>，
    /// 数值外置到 armor.json 后须经 <see cref="ReadOnlyStringSetJsonConverter"/> 才能读回（见该类说明）。
    /// </para>
    /// </summary>
    [JsonConverter(typeof(ReadOnlyStringSetJsonConverter))]
    public IReadOnlySet<string>? CoversParts { get; init; }

    /// <summary>本层是否覆盖该具体部位（<see cref="CoversParts"/> 为 null 时恒真=全覆盖）。</summary>
    public bool Covers(BodyPart part) =>
        CoversParts is null || CoversParts.Contains(part.Name);

    /// <summary>取该伤害类型下适用的防御值。</summary>
    public double DefenseFor(DamageType type) =>
        type == DamageType.Sharp ? SharpDefense : BluntDefense;
}

/// <summary>
/// 四肢（骨折以**肢**为单位，[SPEC-FRAC-LIMB]）。用户拍板：「软组织不会骨折，只有四肢会」；
/// 「手指脚趾也算，但会直接视作上肢骨折或下肢骨折，因此一个人身上最多有四处骨折」。
/// 手臂/手/手指 → 上肢；大腿/小腿/脚/脚趾 → 下肢；左右由部位名前缀「左/右」判定。
/// 软组织（胸/腹/头/眼/面/耳/颈）无所属肢 ⇒ 永不骨折。
/// </summary>
public enum Limb
{
    LeftUpperLimb,
    RightUpperLimb,
    LeftLowerLimb,
    RightLowerLimb,
}

/// <summary>肢的显示名/侧别/近端代表部位等辅助（骨折走肢级判定与展示）。</summary>
public static class Limbs
{
    /// <summary>肢的中文显示名（战报「骨折:右上肢」、健康档 BodyPart 键、存档均用它）。</summary>
    public static string DisplayName(this Limb limb) => limb switch
    {
        Limb.LeftUpperLimb => "左上肢",
        Limb.RightUpperLimb => "右上肢",
        Limb.LeftLowerLimb => "左下肢",
        Limb.RightLowerLimb => "右下肢",
        _ => limb.ToString(),
    };

    public static bool IsUpper(this Limb limb) => limb is Limb.LeftUpperLimb or Limb.RightUpperLimb;

    public static bool IsLower(this Limb limb) => limb is Limb.LeftLowerLimb or Limb.RightLowerLimb;

    /// <summary>
    /// 该肢的**近端代表部位名**（上肢=手臂、下肢=大腿）：既是健康档/角色面板展示锚，
    /// 也是「整肢畸形封顶致残」的截除落点（截近端 ⇒ 树形连带远端全失）。
    /// </summary>
    public static string RepresentativePart(this Limb limb) => limb switch
    {
        Limb.LeftUpperLimb => HumanBody.LeftArm,
        Limb.RightUpperLimb => HumanBody.RightArm,
        Limb.LeftLowerLimb => HumanBody.LeftLeg,
        Limb.RightLowerLimb => HumanBody.RightLeg,
        _ => "",
    };

    /// <summary>把肢的显示名解析回枚举；非肢名返回 null。</summary>
    public static Limb? FromDisplayName(string name) => name switch
    {
        "左上肢" => Limb.LeftUpperLimb,
        "右上肢" => Limb.RightUpperLimb,
        "左下肢" => Limb.LeftLowerLimb,
        "右下肢" => Limb.RightLowerLimb,
        _ => null,
    };
}

/// <summary>身体区域，用于效果适用范围判定（如震荡仅头/躯干）。</summary>
public enum BodyRegion
{
    Head,
    Neck,
    Torso,
    Arm,
    Hand,
    Leg,
    Foot,
    Eye,
    Face,

    /// <summary>耳（头部细部位，归零仅毁容、无系统后果）。</summary>
    Ear,

    /// <summary>手指（手部细部位，切除按"该手累计操作惩罚"结算）。</summary>
    Finger,

    /// <summary>脚趾（脚部细部位，切除按"该脚累计移动惩罚"结算）。</summary>
    Toe,
}

/// <summary>
/// 两级命中判定的"大区域"层：先按大区域体积权重选区域，再在区域内按子部位体积权重选子部位。
/// 每个 <see cref="BodyPart"/> 归属一个大区域；大区域体积权重 = 其成员子部位体积权重之和。
/// </summary>
public enum BodyMacroRegion
{
    /// <summary>躯干（枚举默认值；未显式标注大区域的部位归此）。</summary>
    Torso = 0,
    Neck,
    Head,
    Arm,
    Hand,
    Leg,
    Foot,
}

/// <summary>假肢等级。恢复比例见 <see cref="Prosthetic.RestoreRatio"/>（相对单肢能力）。</summary>
public enum ProstheticGrade
{
    /// <summary>木制假肢：恢复比例见 Wiki 配置表。</summary>
    Wooden,

    /// <summary>简易假肢：恢复比例见 Wiki 配置表。</summary>
    Simple,

    /// <summary>仿生假肢：恢复比例见 Wiki 配置表。</summary>
    Bionic,
}

/// <summary>
/// 假肢数据模型。装在被切除肢体的空槽位上（取代该部位），假肢无 HP、不可再被切除（暂定）。
/// 恢复是**相对单肢能力**的比例；具体比例见 Wiki 配置表。
/// </summary>
public sealed class Prosthetic
{
    public string Name { get; init; } = "";

    public ProstheticGrade Grade { get; init; }

    /// <summary>取代的肢体区域（<see cref="BodyRegion.Hand"/> 恢复操作能力 / <see cref="BodyRegion.Leg"/> 恢复移动能力）。</summary>
    public BodyRegion ReplacesRegion { get; init; }

    /// <summary>恢复比例，相对于单肢能力；具体值见 Wiki 配置表。</summary>
    public double RestoreRatio { get; init; }

    /// <summary>按等级构造假肢，等级对应的恢复比例见 Wiki 配置表。</summary>
    public static Prosthetic OfGrade(ProstheticGrade grade, BodyRegion replaces, string? name = null)
    {
        double ratio = grade switch
        {
            ProstheticGrade.Wooden => 0.25,
            ProstheticGrade.Simple => 0.50,
            ProstheticGrade.Bionic => 0.75,
            _ => 0.0,
        };
        return new Prosthetic
        {
            Name = name ?? grade.ToString(),
            Grade = grade,
            ReplacesRegion = replaces,
            RestoreRatio = ratio,
        };
    }
}

/// <summary>
/// 能力惩罚（残疾净值）；具体范围与数值见 Wiki 配置表。
/// 由 <see cref="Body.RecalculatePenalties"/> 依"切除部位 + 假肢"重算净值。
/// 操作能力：影响攻速/生产/开锁修理等精细操作；移动能力：影响移速/闪避走位。
/// </summary>
public sealed class DisabilityModifiers
{
    /// <summary>操作能力惩罚；具体比例见 Wiki 配置表。</summary>
    public double OperationPenalty { get; set; }

    /// <summary>移动能力惩罚；具体比例见 Wiki 配置表。</summary>
    public double MobilityPenalty { get; set; }
}

/// <summary>
/// 部位归零后果分类（用户口径）：
/// 头/颈/躯干归零致死；四肢归零致残；眼归零致盲；其余（鼻/下巴等）仅毁容、无系统性后果。
/// </summary>
public enum BodyPartCategory
{
    /// <summary>致死部位：归零 = 角色死亡。</summary>
    Vital,

    /// <summary>致残部位：归零 = 该肢体失能。</summary>
    Limb,

    /// <summary>致盲部位：眼，归零 = 该眼失明。</summary>
    Eye,

    /// <summary>次要部位：归零无系统性后果（仅叙事/毁容）。</summary>
    Minor,
}

/// <summary>
/// 身体部位定义（不可变模板，数据驱动）。命中按体积权重随机分配（瞄准指令改变权重）。
/// 每部位独立 HP；<see cref="Parent"/> 组成树形，用于切除连带（切手臂→连带手）。
/// 细部位表见 <see cref="HumanBody"/>，HP/权重均"拟定待调"。
/// </summary>
public sealed class BodyPart
{
    public string Name { get; init; } = "";

    /// <summary>体积权重，用于命中分配。拟定待调。</summary>
    public double VolumeWeight { get; init; }

    /// <summary>部位最大 HP。拟定待调（参考 CDDA/RimWorld 量级）。</summary>
    public double MaxHp { get; init; }

    public BodyRegion Region { get; init; }

    /// <summary>所属大区域（两级命中判定第一级）。默认 <see cref="BodyMacroRegion.Torso"/>。</summary>
    public BodyMacroRegion MacroRegion { get; init; }

    public BodyPartCategory Category { get; init; }

    /// <summary>父部位名（null = 根，如躯干）。切除本部位时其所有后代一并失去。</summary>
    public string? Parent { get; init; }

    /// <summary>震荡可作用于此部位（脑部相关：头/眼/面/颈上部 + 躯干）。</summary>
    public bool ConcussionProne => Region is BodyRegion.Head or BodyRegion.Eye or BodyRegion.Face or BodyRegion.Torso;

    /// <summary>
    /// 本部位所属的**四肢**（骨折以肢为单位，[SPEC-FRAC-LIMB]）：手臂/手/手指 → 上肢，
    /// 大腿/小腿/脚/脚趾 → 下肢；左右由部位名前缀「左/右」判定（四肢部位名恒带侧别前缀）。
    /// 软组织（胸/腹/头/眼/面/耳/颈等非四肢部位）无所属肢 ⇒ <c>null</c> ⇒ **永不骨折**。
    /// </summary>
    public Limb? FractureLimb
    {
        get
        {
            bool upper = Region is BodyRegion.Arm or BodyRegion.Hand or BodyRegion.Finger;
            bool lower = Region is BodyRegion.Leg or BodyRegion.Foot or BodyRegion.Toe;
            if (!upper && !lower)
            {
                return null;
            }

            bool left = Name.StartsWith('左');
            bool right = Name.StartsWith('右');
            if (left == right)
            {
                return null; // 四肢部位名恒带「左/右」前缀；无侧别者视作无肢（防御，正常不发生）
            }

            if (upper)
            {
                return left ? Limb.LeftUpperLimb : Limb.RightUpperLimb;
            }

            return left ? Limb.LeftLowerLimb : Limb.RightLowerLimb;
        }
    }

    /// <summary>骨折可作用于此部位（＝有所属肢；软组织不可骨折）。形似 <see cref="ConcussionProne"/> 的部位门。</summary>
    public bool FractureProne => FractureLimb is not null;
}
