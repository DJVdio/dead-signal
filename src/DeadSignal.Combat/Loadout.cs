namespace DeadSignal.Combat;

/// <summary>负重分段。</summary>
public enum LoadoutTier
{
    /// <summary>0~50% 上限：无惩罚。</summary>
    Unencumbered,

    /// <summary>50~100% 上限：线性惩罚攻速/移速。</summary>
    Encumbered,

    /// <summary>&gt;100% 上限：陡峭惩罚。</summary>
    Overloaded,
}

/// <summary>
/// 负重阈值分段惩罚（纯函数）。用户口径：负重上限内 0~50% 无惩罚、50~100% 线性惩罚、&gt;100% 陡峭惩罚。
/// 曲线参数与上限体力换算均**拟定待调**。引擎只算出惩罚系数，实际作用于攻速/移速由时间系统消费。
/// </summary>
public static class Loadout
{
    /// <summary>无惩罚阈值（占上限比例）。</summary>
    public const double FreeThreshold = 0.50;

    /// <summary>满负重（100% 上限）时的速度乘子（拟定待调）。</summary>
    public const double PenaltyAtCapacity = 0.70;

    /// <summary>超载区每超 100% 上限额外扣的速度乘子斜率（拟定待调，陡峭）。</summary>
    public const double OverloadSlope = 0.80;

    /// <summary>速度乘子下限（再重也不至于完全不能动）。</summary>
    public const double MinMultiplier = 0.10;

    /// <summary>由体力属性换算负重上限（拟定待调）。</summary>
    public const double CapacityPerStrength = 6.0;

    public static double CapacityFromStrength(double strength) => strength * CapacityPerStrength;

    public static LoadoutTier TierOf(double totalWeight, double capacity)
    {
        double ratio = Ratio(totalWeight, capacity);
        if (ratio <= FreeThreshold)
        {
            return LoadoutTier.Unencumbered;
        }

        return ratio <= 1.0 ? LoadoutTier.Encumbered : LoadoutTier.Overloaded;
    }

    /// <summary>
    /// 攻速/移速速度乘子（1.0 = 无惩罚）。
    /// 0~50%: 1.0；50~100%: 从 1.0 线性降到 <see cref="PenaltyAtCapacity"/>；
    /// &gt;100%: 从 PenaltyAtCapacity 以 <see cref="OverloadSlope"/> 陡降，下限 <see cref="MinMultiplier"/>。
    /// </summary>
    public static double SpeedMultiplier(double totalWeight, double capacity)
    {
        double ratio = Ratio(totalWeight, capacity);

        if (ratio <= FreeThreshold)
        {
            return 1.0;
        }

        if (ratio <= 1.0)
        {
            double t = (ratio - FreeThreshold) / (1.0 - FreeThreshold); // 0..1
            return 1.0 - t * (1.0 - PenaltyAtCapacity);
        }

        double over = ratio - 1.0;
        return Math.Max(MinMultiplier, PenaltyAtCapacity - over * OverloadSlope);
    }

    private static double Ratio(double totalWeight, double capacity)
    {
        if (capacity <= 0)
        {
            return totalWeight > 0 ? double.PositiveInfinity : 0;
        }

        return Math.Max(0, totalWeight) / capacity;
    }
}
