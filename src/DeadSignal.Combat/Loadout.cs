namespace DeadSignal.Combat;

/// <summary>负重分档（用户拍板的三档 + 硬上限）。</summary>
public enum LoadoutTier
{
    /// <summary>低于 Wiki 配置的免罚线：无影响，自由行动。</summary>
    Unencumbered,

    /// <summary>处于 Wiki 配置的轻度区间：受到轻度 debuff。</summary>
    Encumbered,

    /// <summary>处于 Wiki 配置的重度区间：移速 debuff 加重并拖慢出手。</summary>
    Strained,

    /// <summary>
    /// 超过 Wiki 配置的硬上限：**不允许**（拾取处直接拦）。
    /// 正常玩法不可达；只在**上限中途下降**（关内断手/饿掉一档/狗跑了）时，已背在身上的东西会落进这一档——
    /// 东西不会凭空消失，但你会被拖到几乎走不动。
    /// </summary>
    Overloaded,
}

/// <summary>
/// 负重上限与分档惩罚（纯函数）。具体阈值、曲线与能力乘子以 Wiki 配置表为准。
/// <para>
/// [T45] 这本账里装的是**一个人身上的全部重量**：左右手的武器 + 11 槽护甲（消费层 <c>GearWeight</c>）+ 他分摊到的战利品。
/// 所以"把枪改装得很强"的代价是——**它吃掉你的搜刮余量**（改装后的重量由 Wiki 配置表决定）。
/// <b>不是"出门就减速"</b>：普通配置出门离免罚线还远得很，是**你搜的东西**把你推过线的。
/// 见 <see cref="BaseCarryLimitKg"/> 上方的余量表。
/// </para>
/// <para>
/// 上限是**硬的**（<see cref="CanCarry"/>／消费层 <c>ExpeditionBag</c> 在拾取处拦截）——装不下就是拿不走，
/// 而不是"超重了慢慢挪"。硬上限才制造取舍；软惩罚负责让"背得多"本身有代价。
/// </para>
/// <para>
/// <b>阈值是上限的比例，不是写死的公斤数</b>（见 <see cref="FreeRatio"/> 与 <see cref="StrainRatio"/>）：
/// 这样任何抬高上限的乘子都会把**三档整体上浮**，而不只是把终点线往后挪——
/// 他"从小帮祖母打理农庄"体现在每一档都比别人扛得住。反过来，残缺/饥饿把上限乘小，三档也一起收紧。
/// </para>
/// 本项目**没有"力量/体力"属性**（铁律：能力只由 authored 专属效果 + 读过的书承载），故 <see cref="CarryLimit"/>
/// 的基数对所有人一视同仁。曲线参数皆**拟定待调**；引擎只出数值，作用于移速/出手间隔由消费层消费。
/// </summary>
public static class Loadout
{
    // 装备、武器改装与负重阈值均由 Wiki/消费层配置提供；本类只保留分档计算规则。

    /// <summary>硬上限：不能超过 Wiki 配置的上限。装备也算在里面。</summary>
    public const double BaseCarryLimitKg = 80.0;

    /// <summary>
    /// 无影响线：低于 Wiki 配置的免罚阈值自由行动。
    /// </summary>
    public const double FreeThresholdKg = 30.0;

    /// <summary>加重线：达到 Wiki 配置的重度阈值后 debuff 加重。</summary>
    public const double StrainThresholdKg = 50.0;

    // ---- 比例化（随上限乘子整体伸缩）----

    /// <summary>无影响线占上限的比例，按当前配置推导。</summary>
    public const double FreeRatio = FreeThresholdKg / BaseCarryLimitKg;

    /// <summary>加重线占上限的比例，按当前配置推导。</summary>
    public const double StrainRatio = StrainThresholdKg / BaseCarryLimitKg;

    // ---- debuff 曲线：分段线性关系由 Wiki 配置表决定 ----

    /// <summary>轻度档末的移速乘子，具体值见 Wiki 配置表。</summary>
    public const double SpeedAtStrain = 0.80;

    /// <summary>
    /// 重度档末的移速乘子，具体值见 Wiki 配置表。
    /// </summary>
    public const double SpeedAtLimit = 0.20;

    /// <summary>
    /// 轻度档末的出手间隔乘子，具体值见 Wiki 配置表。
    /// </summary>
    public const double AttackSpeedAtStrain = 0.80;

    /// <summary>重度档末的出手间隔乘子，具体值见 Wiki 配置表。</summary>
    public const double AttackSpeedAtLimit = 0.50;

    /// <summary>超上限后的额外移速斜率，具体值见 Wiki 配置表。</summary>
    public const double OverloadSlope = 0.80;

    /// <summary>速度乘子下限，具体值见 Wiki 配置表。</summary>
    public const double MinMultiplier = 0.10;

    /// <summary>
    /// 一个人的负重上限（kg）＝ <see cref="BaseCarryLimitKg"/> × 承载能力 × authored 专属乘子。
    /// </summary>
    /// <param name="carryCapability">
    /// 承载能力：断手/饿肚子背不动。消费层直接喂 <c>Pawn.OperationCapability</c>
    /// （＝<c>HungerState.CombineCapability(残疾操作惩罚, 饥饿惩罚)</c>，与战斗出手间隔同源口径），不另造一套数学。
    /// 用户只拍了三档公斤数、没提残缺——按**乘算通则**接在这里：断一只手 → 上限（连同三档阈值）对折。
    /// </param>
    /// <param name="capacityMultiplier">
    /// authored 专属效果乘子（默认值代表无加成）。具体角色效果见 Wiki 配置表。
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
    /// 按 Wiki 配置的免罚线、轻度区间、重度区间与超限斜率计算。
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
    /// 出手间隔乘子（低于基线代表攻速变慢）。按 Wiki 配置的两段线性曲线计算。
    /// <para>
    /// ⚠️ **旧口径「只有重度档才罚攻速」已被用户推翻**：
    /// 现在攻速和移速一样，从免罚线起就开始掉；两者的恶化速度由 Wiki 配置决定：
    /// <b>负重压垮的首先是你的腿，不是你的手</b>。
    /// </para>
    /// <para>
    /// 刻意**不碰工时/操作能力**——那是残缺与饥饿的地盘（<c>Pawn.OperationCapability</c>）。
    /// 负重已经通过 <see cref="CarryLimit"/> 的 carryCapability 与它们相乘过一次，再扣一遍就是双重惩罚。
    /// </para>
    /// </summary>
    public static double AttackSpeedMultiplier(double totalWeight, double carryLimit)
    {
        double ratio = Ratio(totalWeight, carryLimit);

        if (ratio <= FreeRatio)
        {
            return 1.0;
        }

        if (ratio <= StrainRatio)
        {
            double t = (ratio - FreeRatio) / (StrainRatio - FreeRatio); // 0..1
            return 1.0 - t * (1.0 - AttackSpeedAtStrain);
        }

        if (ratio <= 1.0)
        {
            double t = (ratio - StrainRatio) / (1.0 - StrainRatio); // 0..1
            return AttackSpeedAtStrain - t * (AttackSpeedAtStrain - AttackSpeedAtLimit);
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
