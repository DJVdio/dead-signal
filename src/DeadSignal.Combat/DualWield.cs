namespace DeadSignal.Combat;

/// <summary>
/// 持握三态（互斥）。用户拍板口径：
/// 单手持单武器 = 基线；<b>双手持一把武器 = 同样是基线（无攻速加成）</b>；双持两把单手武器 = 攻速×0.70（远程另 +误差角）。
/// 一个战斗单位任一时刻只处于一种持握态，故用单一枚举表达。
/// <para><b>双手握不再给攻速加成</b>（旧 <c>TwoHandedSpeedBonus</c> ×1.15 已按用户口径删除）：双手武器的代价
/// 是<b>装备约束</b>（<see cref="Weapon.TwoHanded"/>：占满两只手、不能同时持光源或第二把武器），
/// 它的回报体现在武器自身的伤害/穿透数值上，<b>不再</b>额外附送攻速。</para>
/// </summary>
public enum GripMode
{
    /// <summary>单手持单武器：基线，无系数。</summary>
    OneHanded,

    /// <summary>双手持一把武器（含双手武器，或单手武器改双手握）：<b>无攻速加成</b>，与单手同基线。</summary>
    TwoHanded,

    /// <summary>双持两把单手武器：攻速×0.70（远程另有误差角 ×1.25）。</summary>
    DualWield,
}

/// <summary>
/// 持握系数（纯函数）。用户口径：
/// - 双手持一把：<b>无加成</b>，攻速与单手相同（短剑可单可双、消防斧只能双手——双手都不再有攻速回报）。
/// - 双持两把单手武器：两手各自按自身武器攻速独立出手、两手都吃惩罚——近战双持攻速 70%，远程双持攻速 70% 且误差角 +25%。
/// - 单手持单武器：基线不惩罚。
/// <para>⇒ 持握态里<b>只剩双持这一个减速项</b>，没有任何持握能把攻速抬到基线之上。</para>
/// 实际出手时序（两手各自节奏）由实时防御战 tick 循环消费，本引擎只提供系数。系数为**拟定待调**。
/// </summary>
public static class DualWield
{
    /// <summary>双持攻速系数（速度乘子）：0.7 = 慢到七成。</summary>
    public const double AttackSpeedFactor = 0.70;

    /// <summary>远程双持误差角系数：×1.25。</summary>
    public const double RangedSpreadFactor = 1.25;

    /// <summary>持握态对应的攻速乘子（次/秒方向）：双持 ×0.70（更慢）；单手与<b>双手</b>同为 1.0（双手无加成）。</summary>
    public static double GripSpeedFactor(GripMode grip) => grip switch
    {
        GripMode.DualWield => AttackSpeedFactor,
        _ => 1.0,
    };

    /// <summary>按持握态算有效出手间隔（秒/次）：间隔 = 基础 / 攻速乘子（双持变长；单手与双手均不变）。</summary>
    public static double EffectiveGripInterval(double baseInterval, GripMode grip) =>
        baseInterval / GripSpeedFactor(grip);

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
