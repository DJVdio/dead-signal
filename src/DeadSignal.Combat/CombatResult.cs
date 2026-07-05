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

    /// <summary>取整后作用到部位的伤害：穿透所有层时向上取整、最低 1；中途终止为 0。</summary>
    public int FinalDamage { get; init; }

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
}
