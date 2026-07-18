namespace DeadSignal.Combat;

/// <summary>
/// 一个部位上那**唯一**一处出血（[T58] 三级流血）。
/// <para><see cref="Severity"/>＝口子多大（小/中/大，合并后的结果）；
/// <see cref="RateMultiplier"/>＝造成它的武器让它流得多快（锯齿剑刃 1.4）。</para>
/// </summary>
public readonly record struct BleedWound(BleedModel.BleedSeverity Severity, double RateMultiplier);

/// <summary>出血伤口的致命性分级（按受伤部位）。</summary>
public enum BleedTier
{
    /// <summary>微小（指/趾/眼/面/耳）：擦伤级，战后自愈，战斗内几乎不失血、绝不致死。</summary>
    Micro,

    /// <summary>轻微（手/脚）：会失血、会让人虚弱，但**不致命**（战后只溃烂感染，需手术）。</summary>
    Minor,

    /// <summary>致命（躯干/头/颈/手臂/大腿等大部位）：深伤口，短时间内可放干致死。</summary>
    Lethal,
}

/// <summary>
/// 出血的部位分级模型 —— **战斗内失血与战后伤病建档共用的单一事实源**。
///
/// <para>
/// 用户口径：「流血应当是短时间内危险致死的，多个严重流血伤口可能会导致这场战斗还没打出胜负就流血致死了」；
/// 同时小伤口（手/脚/指）**不致命**，只溃烂感染。二者合起来 = **有梯度的致命性**：
/// 打中要害才放血放到死，划破手指不会。
/// </para>
///
/// <para>
/// 此前引擎的战斗内失血是**无梯度**的（每处伤口一律同速、且能把人放干），
/// 与 Godot 战后伤病系统 <c>HealthMapping</c> 已有的三层梯度（自愈/非致命/致命失血）对不上 ——
/// 一道手指划伤能把人流血流死。本类把那套分级下沉到引擎，两边现在读同一个函数。
/// </para>
///
/// 数值全部**拟定待调**。
/// </summary>
public static class BleedModel
{
    // ================= 【T58】三级流血（用户拍板）=================
    // 每个部位最多存在一处出血；新增出血即时合并并封顶。分级阈值与流速权重以 Wiki 配置表为准。

    /// <summary>一处出血的等级；枚举序用于合并，具体阈值与权重见 Wiki 配置表。</summary>
    public enum BleedSeverity
    {
        /// <summary>小流血：任何锐器进了肉就至少是这一级（不看伤害大小）。</summary>
        Small = 1,

        /// <summary>中流血：单次伤害超过 Wiki 配置的中流血阈值。</summary>
        Medium = 2,

        /// <summary>大流血：单次伤害超过 Wiki 配置的大流血阈值。**封顶级，再挨打也不会更高。**</summary>
        Large = 3,
    }

    /// <summary>中流血门槛：单次伤害占该部位最大生命值的比例，见 Wiki 配置表。</summary>
    public const double MediumThreshold = 0.30;

    /// <summary>大流血门槛，见 Wiki 配置表。</summary>
    public const double LargeThreshold = 0.60;

    /// <summary>
    /// 一次命中在该部位造成的出血等级。<paramref name="damage"/> 是**真正进到肉里**的伤害
    /// （护甲结算之后、且必须是**以锐器抵达**——钝伤不流血，由调用方门控）。
    /// </summary>
    public static BleedSeverity SeverityOf(double damage, double partMaxHp)
    {
        if (partMaxHp <= 0)
        {
            return BleedSeverity.Large; // 部位上限已被磨没：任何进肉伤害都按最狠算
        }

        double ratio = damage / partMaxHp;
        if (ratio > LargeThreshold)
        {
            return BleedSeverity.Large;
        }

        return ratio > MediumThreshold ? BleedSeverity.Medium : BleedSeverity.Small;
    }

    /// <summary>
    /// 合并两处出血（[T58] 用户的四条规则，**一行代码就是全部**）：
    /// 小(1)+小(1)=2=中 ✓；中(2)+中(2)=4→封顶 3=大 ✓；小(1)+中(2)=3=大 ✓；大+任何 ≥4→封顶=大 ✓。
    /// <para>把等级定义成 1/2/3 的"点数"后，用户那四条规则**恰好就是 <c>min(3, a+b)</c>** —— 不是巧合地凑出来的，
    /// 是这四条规则本身就唯一确定了这个加法（它同时也定死了用户没明说的"大+小 = 大"）。</para>
    /// </summary>
    public static BleedSeverity Merge(BleedSeverity a, BleedSeverity b)
        => (BleedSeverity)Math.Min((int)BleedSeverity.Large, (int)a + (int)b);

    /// <summary>
    /// 各级出血的流速权重，乘以 <see cref="Body.BleedRatePerWound"/>。
    /// 具体权重与调参依据以 Wiki 配置表及历史仿真报告为准。
    /// </summary>
    public static double SeverityRateOf(BleedSeverity s) => s switch
    {
        BleedSeverity.Small => 0.3,
        BleedSeverity.Medium => 1.0,
        BleedSeverity.Large => 3.0,
        _ => 1.0,
    };

    /// <summary>战后伤病建档时，各级出血对应的初始严重度；具体映射以 Wiki 配置表为准。</summary>
    public static double ConditionSeverityOf(BleedSeverity s) => s switch
    {
        BleedSeverity.Small => 0.25,
        BleedSeverity.Medium => 0.45,
        BleedSeverity.Large => 0.70,
        _ => 0.35,
    };

    // ================= 流血口径：**实机与 Sim 的单一事实源** =================
    // Sim 与 Godot 共读本类常量；历史校准数字只保留在 research 报告，当前配置以 Wiki 为准。

    /// <summary>
    /// 储血量上限的**单一事实源**。<see cref="Body"/> 的构造默认值与
    /// <c>DuelConfig.BloodMax</c> **都读这一个常量** —— 实机与 Sim 不可能再漂开。
    /// </summary>
    public const double DefaultBloodMax = 100;

    /// <summary>
    /// 每处伤口每秒失血量的**单一事实源**。<see cref="Body.BleedRatePerWound"/> 的字段默认值与
    /// <c>DuelConfig.BleedRatePerWound</c> **都读这一个常量**。
    /// </summary>
    public const double DefaultBleedRatePerWound = 0.55;

    /// <summary>
    /// 休养时每昼夜恢复的血量，由储血上限与 Wiki 配置的恢复周期推导。
    /// 只在伤口止住（手术缝合）后回血。
    /// </summary>
    public const double BloodRegenPerRestDay = DefaultBloodMax / FullBloodRefillDays;

    /// <summary>休养回血从零回满所需的昼夜数，见 Wiki 配置表。</summary>
    public const double FullBloodRefillDays = 7.0;

    /// <summary>丧尸受害者侧的失血流速倍率，具体值见 Wiki 配置表。</summary>
    public const double ZombieBleedRateMultiplier = 1.0 / 3.0;

    /// <summary>非致命伤口的失血下限（占储血上限的比例），具体值见 Wiki 配置表。</summary>
    public const double NonLethalBloodFloorRatio = 0.5;

    /// <summary>该部位出血的分级。部位表查不到 → 按致命（从狠，与 <c>HealthMapping</c> 旧口径一致）。</summary>
    public static BleedTier TierOf(Body body, string part)
        => body.Parts.TryGetValue(part, out BodyPart? p) ? TierOf(p) : BleedTier.Lethal;

    /// <inheritdoc cref="TierOf(Body, string)"/>
    public static BleedTier TierOf(BodyPart part) => part.Region switch
    {
        BodyRegion.Finger or BodyRegion.Toe or BodyRegion.Eye or BodyRegion.Face or BodyRegion.Ear
            => BleedTier.Micro,
        BodyRegion.Hand or BodyRegion.Foot => BleedTier.Minor,
        _ => BleedTier.Lethal, // 躯干/头/颈/手臂/大腿 等大部位
    };

    /// <summary>该部位的出血是否为致命失血（拖久会放干）。</summary>
    public static bool IsLethalPart(Body body, string part) => TierOf(body, part) == BleedTier.Lethal;

    /// <summary>该部位的出血是否为微小伤（战后自愈，不需手术）。</summary>
    public static bool IsMicroPart(Body body, string part) => TierOf(body, part) == BleedTier.Micro;

    /// <summary>
    /// 各**部位**分级的流速权重（× <see cref="Body.BleedRatePerWound"/>，拟定待调）。
    /// 与 <see cref="SeverityRateOf"/> **正交相乘**：等级＝「口子多大」、部位权重＝「砍在哪」。
    /// </summary>
    public static double RateWeightOf(BleedTier tier) => tier switch
    {
        BleedTier.Lethal => 1.0,
        BleedTier.Minor => 0.5,
        BleedTier.Micro => 0.2,
        _ => 1.0,
    };

    /// <summary>
    /// 一处出血伤口的**实际**分级：断口（部位已被切除/损毁）一律按致命 —— 手掌被划一刀不致命，
    /// 但整只手被砍下来的断口是会把人放干的。**微小部位除外**（断一根手指不该流血流死）。
    /// 致命性由已持久化的状态（部位是否 <see cref="Body.IsGone"/>）推导，故不需要给存档加字段。
    /// </summary>
    public static BleedTier WoundTierOf(Body body, string part)
    {
        var tier = TierOf(body, part);
        if (tier == BleedTier.Micro)
        {
            return BleedTier.Micro; // 断指仍是断指
        }

        return body.IsGone(part) ? BleedTier.Lethal : tier;
    }
}
