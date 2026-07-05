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
}

/// <summary>护甲单层。数据驱动 POCO。</summary>
public sealed class ArmorLayer
{
    public string Name { get; init; } = "";

    /// <summary>对锐器的防御值。设计口径：锐防普遍约为钝防两倍，板甲更高。</summary>
    public double SharpDefense { get; init; }

    /// <summary>对钝器的防御值。</summary>
    public double BluntDefense { get; init; }

    /// <summary>重量。重量惩罚（攻速/移速）本期不结算，字段留给后续。</summary>
    /// TODO(重量): 结算攻速/移速惩罚。
    public double Weight { get; init; }

    public ArmorSlot Slot { get; init; }

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

    public BodyPartCategory Category { get; init; }

    /// <summary>父部位名（null = 根，如躯干）。切除本部位时其所有后代一并失去。</summary>
    public string? Parent { get; init; }

    /// <summary>震荡可作用于此部位（脑部相关：头/眼/面/颈上部 + 躯干）。</summary>
    public bool ConcussionProne => Region is BodyRegion.Head or BodyRegion.Eye or BodyRegion.Face or BodyRegion.Torso;
}
