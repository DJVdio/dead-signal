namespace DeadSignal.Combat;

/// <summary>单层结算的三段判定结果。</summary>
public enum LayerOutcome
{
    /// <summary>攻 ≥ 防：全额伤害，穿入下一层。</summary>
    Full,

    /// <summary>防/2 ≤ 攻 &lt; 防：半额伤害（锐器转钝、穿透归零）。</summary>
    Half,

    /// <summary>攻 &lt; 防/2：无伤害，结算终止。</summary>
    Blocked,
}

/// <summary>逐层结算明细，供战报叙事与调试。</summary>
public sealed class LayerResolution
{
    public int LayerIndex { get; init; }
    public string LayerName { get; init; } = "";

    public double AttackRoll { get; init; }
    public double DefenseRoll { get; init; }

    /// <summary>本层实际适用的防御值（按当时伤害类型选锐防/钝防）。</summary>
    public double ApplicableDefense { get; init; }

    /// <summary>本层结算时攻方所用的穿透（锐转钝后为 0）。</summary>
    public double PenetrationUsed { get; init; }

    public DamageType DamageTypeBefore { get; init; }
    public DamageType DamageTypeAfter { get; init; }

    /// <summary>本层是否发生锐器→钝器转换。</summary>
    public bool ConvertedToBlunt { get; init; }

    public LayerOutcome Outcome { get; init; }

    /// <summary>本层结算后向下一层/部位传递的伤害值（小数；被挡为 0）。</summary>
    public double DamageAfterLayer { get; init; }
}

/// <summary>一次完整命中结算的结果。</summary>
public sealed class CombatResult
{
    public BodyPart HitPart { get; init; } = null!;

    /// <summary>最终作用到部位时的伤害类型（可能已由锐转钝）。</summary>
    public DamageType FinalDamageType { get; init; }

    /// <summary>作用到部位前的小数伤害。被挡终止为 0。</summary>
    public double RawDamage { get; init; }

    /// <summary>作用到部位的**小数**伤害（[SPEC-B14-补6 伤害不取整]）：穿透所有层时保留小数、命中最低 0.01；中途终止为 0。</summary>
    public double FinalDamage { get; init; }

    /// <summary>是否在某层被挡而终止（未穿透全部护甲）。</summary>
    public bool Terminated { get; init; }

    /// <summary>逐层明细（被挡层含在内，作为最后一条）。无甲直击时为空。</summary>
    public IReadOnlyList<LayerResolution> Layers { get; init; } = Array.Empty<LayerResolution>();

    /// <summary>穿透（Full 或 Half）的层数。无甲为 0。</summary>
    public int LayersPenetrated { get; init; }

    /// <summary>
    /// 攻方本次的初始武器伤害 roll（打最外层那一发的原始力量值；无甲时即直击 roll）。
    /// 供震荡判定使用——契合"钝器隔甲生效"，与最终穿透伤害解耦。
    /// </summary>
    public double InitialAttackRoll { get; init; }

    /// <summary>
    /// 本次命中的最终三段判定结果：取<b>最外那一层</b>的结局——被挡下(Blocked)看的就是挡下它的那层；
    /// 半伤/全伤取第一层的结局（后续层是"穿进去之后"的事）。无甲直击恒为 <see cref="LayerOutcome.Full"/>。
    /// 供多弹丸统计（挡下率/进肉率）与战报读数用。
    /// </summary>
    public LayerOutcome Outcome() =>
        Terminated ? LayerOutcome.Blocked
        : Layers.Count == 0 ? LayerOutcome.Full
        : Layers[0].Outcome;
}

/// <summary>
/// 一次<b>齐射</b>（一发多弹丸）的汇总结果。<see cref="Weapon.PelletCount"/> = 1 时退化为单元素列表
/// （既有单弹丸武器语义不变）。
///
/// 每个 <see cref="CombatResult"/> 是一颗弹丸的<b>完整独立</b>判定链结果（自己的命中部位、自己的逐层结算）——
/// 「8 颗弹丸单独计算」（用户原话）在数据结构上就长这样：8 个彼此无关的 CombatResult，而不是一个乘了 8 的伤害。
/// </summary>
public sealed class VolleyResult
{
    /// <summary>逐颗弹丸的独立结算结果（按发射顺序）。单弹丸武器只有 1 个元素。</summary>
    public IReadOnlyList<CombatResult> Pellets { get; init; } = Array.Empty<CombatResult>();

    /// <summary>进肉的弹丸数（穿透了全部护甲层，含半伤）。</summary>
    public int LandedCount => Pellets.Count(p => !p.Terminated);

    /// <summary>被护甲挡下的弹丸数（<see cref="CombatResult.Terminated"/>）。</summary>
    public int BlockedCount => Pellets.Count(p => p.Terminated);

    /// <summary>本次齐射作用到身体的总伤害（各颗 <see cref="CombatResult.FinalDamage"/> 之和；被挡的记 0）。</summary>
    public double TotalDamage => Pellets.Sum(p => p.FinalDamage);

    /// <summary>各颗弹丸命中的部位名（可重复——两颗都中胸就出现两次）。</summary>
    public IReadOnlyList<string> HitParts => Pellets.Select(p => p.HitPart.Name).ToList();
}
