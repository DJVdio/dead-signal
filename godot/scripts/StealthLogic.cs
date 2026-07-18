using System;

namespace DeadSignal.Godot;

/// <summary>
/// 实时探索的潜行/噪音纯逻辑。
///
/// <para>夜防的服饰潜行表是 authored 数据；本类只把它投影到探索层使用的倍率，
/// 不让夜防结算反过来依赖 Godot 空间对象。</para>
/// </summary>
public static class StealthLogic
{
    /// <summary>掩体系数中性值：无覆盖时返回 1.0（零效果）。</summary>
    public const double NeutralCoverCoefficient = 0.0;

    private const double MinMultiplier = 0.1;
    private const double MaxMultiplier = 4.0;

    /// <summary>
    /// 把既有服饰潜行值转换为行动噪音/被发现距离倍率。
    /// 正值代表更安静、更难被看见，负值代表更显眼、更吵；中性值为 1。
    /// </summary>
    public static double EquipmentNoiseMultiplier(double apparelStealthScore)
    {
        if (double.IsNaN(apparelStealthScore) || double.IsInfinity(apparelStealthScore))
        {
            return 1.0;
        }

        double denominator = Math.Max(0.25, 1.0 + apparelStealthScore);
        return Clamp(1.0 / denominator);
    }

    /// <summary>别名：服饰对探索潜行距离的倍率与噪音投影保持同一单一事实源。</summary>
    public static double EquipmentStealthMultiplier(double apparelStealthScore)
        => EquipmentNoiseMultiplier(apparelStealthScore);

    /// <summary>
    /// 负重越慢，动作噪音半径越大。移速乘子 1.0 为中性；超载的最低移速按 0.1 封顶，
    /// 防止出现无穷大噪音。
    /// </summary>
    public static double LoadNoiseMultiplier(double movementSpeedMultiplier)
    {
        if (double.IsNaN(movementSpeedMultiplier) || double.IsInfinity(movementSpeedMultiplier))
        {
            return 1.0;
        }

        return 1.0 / Math.Max(MinMultiplier, movementSpeedMultiplier);
    }

    /// <summary>行动噪音的完整消费链：真实服饰效果 × 负重效果。</summary>
    public static double ActionNoiseMultiplier(double apparelStealthScore, double movementSpeedMultiplier)
        => Clamp(EquipmentNoiseMultiplier(apparelStealthScore) * LoadNoiseMultiplier(movementSpeedMultiplier));

    /// <summary>
    /// 被发现视距倍率。耗子 L3 的黑暗效果只在环境光低于白天满光时启用；服饰效果始终生效。
    /// </summary>
    public static double DetectionRangeMultiplier(
        float ambientLight,
        double apparelStealthScore,
        double darknessStealthBonus)
    {
        double multiplier = EquipmentStealthMultiplier(apparelStealthScore);
        if (!float.IsNaN(ambientLight) && ambientLight < 0.999f && darknessStealthBonus > 0.0)
        {
            multiplier *= 1.0 / (1.0 + darknessStealthBonus);
        }

        return Clamp(multiplier);
    }

    /// <summary>
    /// 耗子 L3 破隐先手伤害倍率。调用方传入当前是否仍未被敌方感知；一旦攻击，
    /// 消费方应立即把自身标记为已破隐，避免同一 Pawn 重复吃加成。
    /// </summary>
    public static double AmbushDamageMultiplier(
        bool isRat,
        int ratLevel,
        bool undetected,
        double ambushDamageBonus)
        => isRat && ratLevel >= 3 && undetected
            ? Math.Max(0.0, 1.0 + ambushDamageBonus)
            : 1.0;

    /// <summary>
    /// 综合潜行评级（实时探索层）：消费<b>服装、负重、黑暗、掩体</b>四条轴。
    /// 返回值 ≤1 = 更难被发现/更安静，≥1 = 更容易被发现/更吵闹。
    /// 中性值 1.0。所有轴按百分比乘算规则连乘（见 AGENTS.md）。
    /// </summary>
    /// <param name="apparelStealthScore">服饰潜行值合计；正=安静，负=吵闹。</param>
    /// <param name="movementSpeedMultiplier">移速乘子（<c>CarryLoadSpeedMult</c>）；1.0=中性，&lt;1=超载。</param>
    /// <param name="ambientLight">环境光 [0,1]；越低越暗。</param>
    /// <param name="darknessStealthBonus">黑暗隐匿加成（耗子 L3=0.5）；无加成传 0。</param>
    /// <param name="coverCoefficient">掩体系数 [0,1]；0=无掩体，1=完全遮蔽。</param>
    public static double StealthRating(
        double apparelStealthScore,
        double movementSpeedMultiplier,
        float ambientLight,
        double darknessStealthBonus,
        double coverCoefficient)
    {
        return Clamp(
            EquipmentNoiseMultiplier(apparelStealthScore)
            * LoadNoiseMultiplier(movementSpeedMultiplier)
            * DarknessFactor(ambientLight, darknessStealthBonus)
            * CoverFactor(coverCoefficient));
    }

    /// <summary>
    /// 黑暗隐匿因子 [0,1]：环境光越低 + bonus 越大 → 返回值越小（更难被发现）。
    /// 白昼（ambientLight ≈ 1）或无 bonus → 1.0（零回归）。
    /// </summary>
    public static double DarknessFactor(float ambientLight, double darknessStealthBonus)
    {
        if (float.IsNaN(ambientLight) || ambientLight >= 0.999f || darknessStealthBonus <= 0.0)
            return 1.0;
        return 1.0 / (1.0 + darknessStealthBonus);
    }

    /// <summary>
    /// 掩体系数 → 侦测倍率。
    /// <paramref name="coverCoefficient"/>=0 → 1.0（无掩体，零回归）；
    /// =1 → 0.5（完全遮蔽，侦测距离减半）。
    /// 同为 1/(1+x) 形式，与黑暗隐匿保持一致的乘算模式。
    /// </summary>
    public static double CoverFactor(double coverCoefficient)
    {
        double cc = Math.Clamp(coverCoefficient, 0.0, 1.0);
        if (cc <= 0.0)
            return 1.0;
        return 1.0 / (1.0 + cc);
    }

    /// <summary>
    /// 负重因子：别名，与 <see cref="LoadNoiseMultiplier"/> 同一模式。
    /// 超载越慢 → 返回值越大（更容易被察觉）。
    /// </summary>
    public static double LoadFactor(double movementSpeedMultiplier)
        => LoadNoiseMultiplier(movementSpeedMultiplier);

    private static double Clamp(double value)
        => Math.Clamp(value, MinMultiplier, MaxMultiplier);
}
