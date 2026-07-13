namespace DeadSignal.Combat;

/// <summary>负重分档（用户拍板的三档 + 硬上限）。</summary>
public enum LoadoutTier
{
    /// <summary>&lt; 30kg：无影响，自由行动。</summary>
    Unencumbered,

    /// <summary>30 ~ 50kg：轻度 debuff（移速）。</summary>
    Encumbered,

    /// <summary>50 ~ 80kg：重度 debuff（移速加重 + 开始拖慢出手）。</summary>
    Strained,

    /// <summary>
    /// &gt; 80kg：**不允许**（硬上限，拾取处直接拦）。
    /// 正常玩法不可达；只在**上限中途下降**（关内断手/饿掉一档/狗跑了）时，已背在身上的东西会落进这一档——
    /// 东西不会凭空消失，但你会被拖到几乎走不动。
    /// </summary>
    Overloaded,
}

/// <summary>
/// 负重上限与分档惩罚（纯函数）。**用户口径**：30kg 以下无影响、30~50kg 有 debuff、50~80kg debuff 加重、**不能超过 80kg**。
/// <para>
/// 上限是**硬的**（<see cref="CanCarry"/>／消费层 <c>ExpeditionBag</c> 在拾取处拦截）——装不下就是拿不走，
/// 而不是"超重了慢慢挪"。硬上限才制造取舍；软惩罚负责让"背得多"本身有代价。
/// </para>
/// <para>
/// <b>阈值是上限的比例，不是写死的公斤数</b>（30/80＝<see cref="FreeRatio"/>、50/80＝<see cref="StrainRatio"/>）：
/// 这样任何抬高上限的乘子（山姆的 ×1.15、全营 ×1.03）都会把**三档整体上浮**，而不只是把终点线往后挪——
/// 他"从小帮祖母打理农庄"体现在每一档都比别人扛得住。反过来，残缺/饥饿把上限乘小，三档也一起收紧。
/// </para>
/// 本项目**没有"力量/体力"属性**（铁律：能力只由 authored 专属效果 + 读过的书承载），故 <see cref="CarryLimit"/>
/// 的基数对所有人一视同仁。曲线参数皆**拟定待调**；引擎只出数值，作用于移速/出手间隔由消费层消费。
/// </summary>
public static class Loadout
{
    // ---- 用户拍板的三个公斤数（基准人：无残缺、不饿、无专属加成）----

    /// <summary>硬上限：**不能超过 80kg**（用户原话）。</summary>
    public const double BaseCarryLimitKg = 80.0;

    /// <summary>无影响线：30kg 以下自由行动。</summary>
    public const double FreeThresholdKg = 30.0;

    /// <summary>加重线：50kg 起 debuff 加重。</summary>
    public const double StrainThresholdKg = 50.0;

    // ---- 比例化（随上限乘子整体伸缩）----

    /// <summary>无影响线占上限的比例（30/80 = 0.375）。</summary>
    public const double FreeRatio = FreeThresholdKg / BaseCarryLimitKg;

    /// <summary>加重线占上限的比例（50/80 = 0.625）。</summary>
    public const double StrainRatio = StrainThresholdKg / BaseCarryLimitKg;

    // ---- debuff 曲线（拟定待调）----

    /// <summary>轻度档末（＝加重线，基准人 50kg）的移速乘子：负重行军，累但还跑得动。</summary>
    public const double SpeedAtStrain = 0.85;

    /// <summary>重度档末（＝满上限，基准人 80kg）的移速乘子：走得动，但**逃不掉**——贪心的代价。</summary>
    public const double SpeedAtLimit = 0.55;

    /// <summary>满上限时的出手间隔乘子（攻速降到 85%）。轻度档**不罚攻速**——背 30kg 挥剑没什么影响。</summary>
    public const double AttackSpeedAtLimit = 0.85;

    /// <summary>超上限（正常不可达）每超 100% 额外扣的移速斜率（陡峭）。</summary>
    public const double OverloadSlope = 0.80;

    /// <summary>速度乘子下限（再重也不至于完全钉死在地上）。</summary>
    public const double MinMultiplier = 0.10;

    /// <summary>
    /// 一个人的负重上限（kg）＝ <see cref="BaseCarryLimitKg"/> × 承载能力 × authored 专属乘子。
    /// </summary>
    /// <param name="carryCapability">
    /// 承载能力 0~1：断手/饿肚子背不动。消费层直接喂 <c>Pawn.OperationCapability</c>
    /// （＝<c>HungerState.CombineCapability(残疾操作惩罚, 饥饿惩罚)</c>，与战斗出手间隔同源口径），不另造一套数学。
    /// 用户只拍了三档公斤数、没提残缺——按**乘算通则**接在这里：断一只手 → 上限（连同三档阈值）对折。
    /// </param>
    /// <param name="capacityMultiplier">
    /// authored 专属效果乘子（默认 1.0＝无加成）。现阶段唯一来源是**山姆"英雄风范"**：
    /// L2 他自己 ×1.15、L3 全营 ×1.03，山姆本人两者**连乘** ×1.15×1.03（≠ 加算的 ×1.18）。
    /// 负数按 0 钳制。
    /// </param>
    public static double CarryLimit(double carryCapability = 1.0, double capacityMultiplier = 1.0)
        => BaseCarryLimitKg * Math.Clamp(carryCapability, 0.0, 1.0) * Math.Max(0, capacityMultiplier);

    /// <summary>此人的"无影响线"（kg）——上限的 <see cref="FreeRatio"/>；山姆的线比别人高。</summary>
    public static double FreeThresholdFor(double carryLimit) => Math.Max(0, carryLimit) * FreeRatio;

    /// <summary>此人的"加重线"（kg）——上限的 <see cref="StrainRatio"/>。</summary>
    public static double StrainThresholdFor(double carryLimit) => Math.Max(0, carryLimit) * StrainRatio;

    /// <summary>背得动吗（**硬上限**：超过就是拿不走）。</summary>
    public static bool CanCarry(double totalWeight, double carryLimit)
        => totalWeight <= carryLimit + 1e-9;

    public static LoadoutTier TierOf(double totalWeight, double carryLimit)
    {
        double ratio = Ratio(totalWeight, carryLimit);
        if (ratio <= FreeRatio)
        {
            return LoadoutTier.Unencumbered;
        }

        if (ratio <= StrainRatio)
        {
            return LoadoutTier.Encumbered;
        }

        return ratio <= 1.0 ? LoadoutTier.Strained : LoadoutTier.Overloaded;
    }

    /// <summary>
    /// 移速乘子（1.0 = 无惩罚）。
    /// &lt;30kg: 1.0；30~50kg: 线性降到 <see cref="SpeedAtStrain"/>；50~80kg: 更陡地降到 <see cref="SpeedAtLimit"/>；
    /// &gt;80kg（硬上限外，仅上限中途下降时可达）：以 <see cref="OverloadSlope"/> 陡降，下限 <see cref="MinMultiplier"/>。
    /// </summary>
    public static double SpeedMultiplier(double totalWeight, double carryLimit)
    {
        double ratio = Ratio(totalWeight, carryLimit);

        if (ratio <= FreeRatio)
        {
            return 1.0;
        }

        if (ratio <= StrainRatio)
        {
            double t = (ratio - FreeRatio) / (StrainRatio - FreeRatio); // 0..1
            return 1.0 - t * (1.0 - SpeedAtStrain);
        }

        if (ratio <= 1.0)
        {
            double t = (ratio - StrainRatio) / (1.0 - StrainRatio); // 0..1
            return SpeedAtStrain - t * (SpeedAtStrain - SpeedAtLimit);
        }

        double over = ratio - 1.0;
        return Math.Max(MinMultiplier, SpeedAtLimit - over * OverloadSlope);
    }

    /// <summary>
    /// 出手间隔乘子（1.0 = 无惩罚，&lt;1 = 攻速变慢）。**只有重度档才罚**：
    /// 50kg 以下挥剑没什么影响；越过加重线后身体被拖住，出手明显变形，满上限时降到 <see cref="AttackSpeedAtLimit"/>。
    /// <para>
    /// 刻意**不碰工时/操作能力**——那是残缺与饥饿的地盘（<c>Pawn.OperationCapability</c>）。
    /// 负重已经通过 <see cref="CarryLimit"/> 的 carryCapability 与它们相乘过一次，再扣一遍就是双重惩罚。
    /// </para>
    /// </summary>
    public static double AttackSpeedMultiplier(double totalWeight, double carryLimit)
    {
        double ratio = Ratio(totalWeight, carryLimit);

        if (ratio <= StrainRatio)
        {
            return 1.0;
        }

        if (ratio <= 1.0)
        {
            double t = (ratio - StrainRatio) / (1.0 - StrainRatio);
            return 1.0 - t * (1.0 - AttackSpeedAtLimit);
        }

        double over = ratio - 1.0;
        return Math.Max(MinMultiplier, AttackSpeedAtLimit - over * OverloadSlope);
    }

    private static double Ratio(double totalWeight, double carryLimit)
    {
        if (carryLimit <= 0)
        {
            return totalWeight > 0 ? double.PositiveInfinity : 0;
        }

        return Math.Max(0, totalWeight) / carryLimit;
    }
}
