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

    /// <summary>穿透力，0~1。降低防方可 roll 的防御上限。</summary>
    public double Penetration { get; init; }

    public DamageType DamageType { get; init; }

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

    /// <summary>是否具备枪托近战 profile（远程武器且填了伤害上限）。近战武器恒为 false。</summary>
    public bool HasMeleeProfile => IsRanged && StockMeleeDamageMax.HasValue;

    /// <summary>
    /// 派生这把远程武器的"枪托贴脸"近战版：钝击、必中（<see cref="IsRanged"/>=false，无误差角）、伤害/穿透低、攻速慢，
    /// 单双手语义沿用本武器 <see cref="TwoHanded"/>。供 Godot 空间层贴脸判定时调用（判定本身在 Godot，不在引擎层）。
    /// 无 profile 时返回 null。
    /// </summary>
    public Weapon? MeleeProfile()
    {
        if (!HasMeleeProfile)
        {
            return null;
        }

        return new Weapon
        {
            Name = Name + "（枪托）",
            DamageMin = StockMeleeDamageMin ?? 0,
            DamageMax = StockMeleeDamageMax!.Value,
            Penetration = StockMeleePenetration ?? 0,
            DamageType = DamageType.Blunt,
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

    /// <summary>重量。重量惩罚（攻速/移速）本期不结算，字段留给后续。</summary>
    /// TODO(重量): 结算攻速/移速惩罚。
    public double Weight { get; init; }

    public ArmorSlot Slot { get; init; }

    /// <summary>
    /// 覆盖的具体身体部位名集合（<see cref="BodyPart.Name"/>，如"左手"）。粒度到具体部位——
    /// 因 <see cref="BodyRegion"/>/<see cref="BodyMacroRegion"/> 不分左右，区域级无法表达"仅左手/仅右手"，
    /// 故护甲覆盖以部位名表达（支持左右分、断肢分槽）。
    /// <c>null</c> = 覆盖全部位（向后兼容：现有护甲不填即全覆盖，行为不变）。
    /// 局部护甲（如左手套仅覆盖左手及其手指）才显式给出子集；命中部位不在集合内则该层不参与结算。
    /// 手部/脚部护甲应连带该手/脚的手指/脚趾（用 <see cref="HumanBody.SubtreeNames"/> 展开子树）。
    /// </summary>
    public IReadOnlySet<string>? CoversParts { get; init; }

    /// <summary>本层是否覆盖该具体部位（<see cref="CoversParts"/> 为 null 时恒真=全覆盖）。</summary>
    public bool Covers(BodyPart part) =>
        CoversParts is null || CoversParts.Contains(part.Name);

    /// <summary>取该伤害类型下适用的防御值。</summary>
    public double DefenseFor(DamageType type) =>
        type == DamageType.Sharp ? SharpDefense : BluntDefense;
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
    /// <summary>木制假肢：恢复单肢能力的 25%。</summary>
    Wooden,

    /// <summary>简易假肢：恢复单肢能力的 50%。</summary>
    Simple,

    /// <summary>仿生假肢：恢复单肢能力的 75%。</summary>
    Bionic,
}

/// <summary>
/// 假肢数据模型。装在被切除肢体的空槽位上（取代该部位），假肢无 HP、不可再被切除（暂定）。
/// 恢复是**相对单肢能力**的比例：单肢 = 全局能力的 50%，恢复值（占全局）= <see cref="RestoreRatio"/> × 50%。
/// </summary>
public sealed class Prosthetic
{
    public string Name { get; init; } = "";

    public ProstheticGrade Grade { get; init; }

    /// <summary>取代的肢体区域（<see cref="BodyRegion.Hand"/> 恢复操作能力 / <see cref="BodyRegion.Leg"/> 恢复移动能力）。</summary>
    public BodyRegion ReplacesRegion { get; init; }

    /// <summary>恢复比例，相对于单肢能力（手=50%，腿=50%）。值域 0.0~1.0。木 0.25 / 简易 0.5 / 仿生 0.75。</summary>
    public double RestoreRatio { get; init; }

    /// <summary>按等级构造假肢（等级→恢复比例：木 0.25 / 简易 0.5 / 仿生 0.75，拟定待调）。</summary>
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
/// 能力惩罚（残疾净值）。值域 0.0~1.0：0 = 无惩罚，1.0 = 完全丧失该能力。
/// 由 <see cref="Body.RecalculatePenalties"/> 依"切除部位 + 假肢"重算净值。
/// 操作能力：影响攻速/生产/开锁修理等精细操作；移动能力：影响移速/闪避走位。
/// </summary>
public sealed class DisabilityModifiers
{
    /// <summary>操作能力惩罚，0.0~1.0（一只手 -50%，两手 -100%）。</summary>
    public double OperationPenalty { get; set; }

    /// <summary>移动能力惩罚，0.0~1.0（一条腿 -50%，两腿 -100%）。</summary>
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
/// 每部位独立 HP；<see cref="Parent"/> 组成树形，用于切除连带（切上臂→连带手）。
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
}
