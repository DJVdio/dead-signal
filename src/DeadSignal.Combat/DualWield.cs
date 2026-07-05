namespace DeadSignal.Combat;

/// <summary>
/// 双持系数（纯函数）。用户口径：两手各自按自身武器攻速独立出手，两手都吃惩罚。
/// 近战双持：攻速为 70%。远程双持：攻速为 70% 且误差角增加 25%。单持不惩罚。
/// 实际出手时序（两手各自节奏）由实时防御战 tick 循环消费，本引擎只提供系数。
/// 系数为**拟定待调**。
/// </summary>
public static class DualWield
{
    /// <summary>双持攻速系数（速度乘子）：0.7 = 慢到七成。</summary>
    public const double AttackSpeedFactor = 0.70;

    /// <summary>远程双持误差角系数：×1.25。</summary>
    public const double RangedSpreadFactor = 1.25;

    /// <summary>有效攻速（次/秒）：双持 ×0.7，单持不变。</summary>
    public static double EffectiveAttackRate(double baseRate, bool dualWielding) =>
        dualWielding ? baseRate * AttackSpeedFactor : baseRate;

    /// <summary>有效出手间隔（秒/次）：攻速降为 0.7 即间隔 ÷0.7（变长）。</summary>
    public static double EffectiveAttackInterval(double baseInterval, bool dualWielding) =>
        dualWielding ? baseInterval / AttackSpeedFactor : baseInterval;

    /// <summary>有效误差角（度）：远程双持 ×1.25；近战或单持不变（近战无误差角）。</summary>
    public static double EffectiveSpreadDegrees(double baseSpread, bool dualWielding, bool ranged) =>
        dualWielding && ranged ? baseSpread * RangedSpreadFactor : baseSpread;
}
