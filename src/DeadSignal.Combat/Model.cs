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

    /// <summary>可双持标记（如手枪+匕首）。副手命中惩罚本期不实现。</summary>
    /// TODO(双持): 实现副手命中惩罚数值。
    public bool CanDualWield { get; init; }
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

/// <summary>
/// 身体部位。命中按体积权重随机分配（瞄准指令改变权重）。
/// 本期先落地"部位命中分配"接口 + 胸部单部位；细部位（左右眼/鼻/下巴等）后续填数据。
/// </summary>
public sealed class BodyPart
{
    public string Name { get; init; } = "";

    /// <summary>体积权重，用于命中分配。</summary>
    public double VolumeWeight { get; init; }
}
