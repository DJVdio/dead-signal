namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ReadBookSet.cs / HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 角色**专属效果（authored perk）** 地基：不做通用技能系统，每角色一套手写 perk（升级条件/效果各不同）。
// 本轮落地首个样板——**诺蒂·书虫**：读得快，读得越多越快，满级还带动全营。数值皆 draft，待 Sim/用户调。

/// <summary>
/// 单个幸存者的专属效果状态容器（纯逻辑，无 Godot 依赖）。**可扩展**：未来别的角色挂别的 perk，
/// 各自加一个可空成员即可（无 perk 者该成员为 <c>null</c>，读速合成时按 ×1.0 处理）。
/// 本轮只挂 <see cref="Bookworm"/>（诺蒂）。W3 由 Pawn 持有本对象并在阅读结算时喂时间。
/// </summary>
public sealed class SurvivorPerks
{
    /// <summary>书虫专属效果（仅诺蒂持有；其余角色为 <c>null</c>，即无此 perk）。</summary>
    public BookwormPerk? Bookworm { get; private set; }

    /// <summary>把本 pawn 变成书虫（赋予专属效果，起始 L1）。W3 建角时对诺蒂调用一次。</summary>
    public void GrantBookworm() => Bookworm ??= new BookwormPerk();

    /// <summary>
    /// 本 pawn 是否为南丁格尔（护士三级专属效果的**身份标记**）。效果规则/等级/计数皆走静态
    /// <see cref="NightingalePerk"/> + <c>StoryFlags</c>（[SPEC-B13-补2] 计数持久化走旗标，RadioMainline 回复日先例），
    /// 本处只标"这人是不是南丁格尔"（供 <c>CampMain</c> 判施术者/找营地护士）。其余角色恒 false。
    /// </summary>
    public bool IsNightingale { get; private set; }

    /// <summary>把本 pawn 标记为南丁格尔（赋予护士三级专属效果身份）。建角时对南丁格尔调用一次（<c>Pawn.Create</c> 按名授予）。</summary>
    public void GrantNightingale() => IsNightingale = true;

    /// <summary>
    /// 本 pawn 作为读者的**自身**读速加成（加法，非倍率）：持有书虫按其等级取（0.25/0.50/0.50），否则 0（无 perk 零影响）。
    /// 丧尸/非诺蒂无 perk → 0。全营加成不含在此，另由 <see cref="ReadingSpeed.Effective"/> 汇总加进来。
    /// </summary>
    public double SelfReadingSpeedBonus => Bookworm?.ReadingSpeedBonus ?? 0.0;

    /// <summary>
    /// 本 pawn **贡献给全营**的读速加成（供上层汇总所有营员的贡献，再加到每个读者头上）：
    /// 持有 L3 书虫 → 0.25，否则 0。丧尸/非诺蒂 → 0。
    /// </summary>
    public double CampWideReadingSpeedBonus => Bookworm?.CampWideReadingSpeedBonus ?? 0.0;
}

/// <summary>
/// **诺蒂·书虫**专属效果（纯逻辑）：靠累计阅读时间（游戏内小时）跨阈值升级 1→2→3。
/// 各级自身读速加成（加法）L1=+25% / L2=+50% / L3=+50%（L3 自身与 L2 相同——L3 的升级点是**多发一个全营 +25%**，不是自身再涨）；
/// 满级(L3)额外贡献全营 +25% 读速，作用到全营所有人**含诺蒂自己**（故诺蒂 L3 加起来对自己 = 50%+25% = 75%）。
/// 所有阈值/加成均为 <c>draft</c>——形态已锁，绝对数值待 Sim 拉表与用户拍板。
/// </summary>
public sealed class BookwormPerk
{
    // draft：升级阈值（累计阅读小时，游戏内）。参考文档 48h 量级，L2→L3 再翻倍余量。
    /// <summary>升到 L2 所需累计阅读小时（draft）。</summary>
    public const double Level2ThresholdHours = 48;
    /// <summary>升到 L3 所需累计阅读小时（draft）。</summary>
    public const double Level3ThresholdHours = 120;

    /// <summary>当前等级（1..3；持有者天生至少 L1）。</summary>
    public int Level { get; private set; } = 1;

    /// <summary>当前累计阅读时间（游戏内小时）。跨夜持久，只增不减。</summary>
    public double AccumulatedReadingHours { get; private set; }

    /// <summary>本人当前自身读速加成（加法，按等级；draft）。</summary>
    public double ReadingSpeedBonus => BonusForLevel(Level);

    /// <summary>满级(L3)时贡献给全营的读速加成，否则 0（draft）。</summary>
    public double CampWideReadingSpeedBonus => Level >= 3 ? CampWideBonusAtMax : 0.0;

    // draft：L3 满级全营读速加成幅度。
    private const double CampWideBonusAtMax = 0.25;

    /// <summary>某等级的自身读速加成（加法：L1=+0.25 / L2=+0.50 / L3=+0.50，L3 与 L2 同；越界按最近级钳制）。draft。</summary>
    public static double BonusForLevel(int level) => level switch
    {
        <= 1 => 0.25,
        _ => 0.50, // L2、L3 自身加成相同（L3 升级点在全营加成，不在自身）
    };

    /// <summary>
    /// 累加阅读时间并按阈值升级（可一次跨多级）。返回本次是否发生了升级。
    /// 已满级(L3)后再读只累计小时、不再升级、返回 <c>false</c>。
    /// </summary>
    public bool AddReadingTime(double hours)
    {
        if (hours <= 0) return false;
        AccumulatedReadingHours += hours;

        int newLevel = Level;
        if (AccumulatedReadingHours >= Level3ThresholdHours) newLevel = 3;
        else if (AccumulatedReadingHours >= Level2ThresholdHours) newLevel = 2;

        if (newLevel > Level)
        {
            Level = newLevel;
            return true;
        }
        return false;
    }
}

/// <summary>
/// **南丁格尔·护士三级专属效果**（纯逻辑·authored perk，[SPEC-B13-补]/[SPEC-B13-补2] 用户拍板，替换早前"固定+15"草案）。
/// 三级效果（用户原话，数值**非拟定**）：
///   · 1级：她本人手术基础点数 15→30（仅她施术时生效，她死即失）；
///   · 2级：卫生意识让床铺更干净，全营感染率 −15%（需她**在营存活**维持，不在营/死亡即失效）；
///   · 3级：卫生意识深入人心，全营手术基础点 +5、全营感染率**再** −10%——**永续遗产**（她死/离营依旧生效，知识已传承）；
///     3级在她存活时与 2级叠加（感染合计 −25%）。
/// 升级轴（[SPEC-B13-补2] 用户拍板）＝她本人**执行过的手术台数**（成败都计、重做每次计）：入队即 L1，累计
/// <see cref="Level2ThresholdSurgeries"/> 台→L2，累计 <see cref="Level3ThresholdSurgeries"/> 台→L3。**台数阈值拟定待调**。
///
/// 设计澄清：这**不复活**已删的**通用**医疗技能系统（那是人人一档的等级）；本 perk 是**单角色 authored 效果**，
/// 与诺蒂"书虫"、道格"羁绊"同型。手术侧走"per-surgeon 手术基础点数"（PerformSurgery 的 surgeryBasePoints 参数化），
/// 感染侧走"营地感染率乘子"（TickDay 的 infectionChanceMultiplier）。
/// L3 的永续遗产由营地层置永久旗标（<c>NurseRecruit.L3LegacyFlag</c>）承载——她死/离营后仍读，故本类的营地级查询
/// 收 <c>l3LegacyActive</c> 布尔（＝该旗标），而非依赖她的存活状态。
///
/// **无实例状态**：等级由她本人累计手术台数派生（<see cref="EvaluateLevel"/>），台数**持久化在 StoryFlags**
/// （[SPEC-B13-补2] "字符串承载整数"，RadioMainline 回复日 <c>ReplyDayKey</c> 同款）——与她的 Pawn 生命周期解耦，
/// 她死后计数天然冻结（不再有她施术→不再 RecordSurgery），L3 遗产旗标照旧永续。身份标记＝<c>SurvivorPerks.IsNightingale</c>。
/// </summary>
public static class NightingalePerk
{
    // —— 升级阈值（[SPEC-B13-补2]，台数=她本人执行手术数；拟定待调）——
    /// <summary>升到 L2 所需她本人累计手术台数（拟定待调）。</summary>
    public const int Level2ThresholdSurgeries = 3;
    /// <summary>升到 L3 所需她本人累计手术台数（拟定待调）。</summary>
    public const int Level3ThresholdSurgeries = 8;

    // —— 三级效果数值（[SPEC-B13-补] 用户原话，**非拟定，勿标待调**）——
    /// <summary>常人手术基础点数（原 <c>HealthConditionSet.SurgeryBasePoints</c>）。</summary>
    public const int DefaultSurgeryBasePoints = 15;
    /// <summary>1级：南丁格尔本人手术基础点数（15→30）。</summary>
    public const int NightingaleSurgeryBasePoints = 30;
    /// <summary>3级：全营手术基础点 +5（永续遗产）。</summary>
    public const int CampSurgeryBaseBonus = 5;
    /// <summary>2级：全营感染率 −15%（她在营存活时生效）。</summary>
    public const double Level2InfectionReduction = 0.15;
    /// <summary>3级：全营感染率再 −10%（永续遗产）。</summary>
    public const double Level3InfectionReduction = 0.10;

    /// <summary>她本人累计手术台数的持久化旗标 key（字符串承载整数，RadioMainline 回复日先例）。</summary>
    public const string SurgeryCountFlag = "nightingale_surgery_count";

    /// <summary>读她本人累计已执行手术台数（未设置/不可解析 → 0）。</summary>
    public static int SurgeriesPerformed(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(SurgeryCountFlag), out int n) ? n : 0;

    /// <summary>
    /// 记一台**她本人执行**的手术（成败都计；门槛未过/重做冷却未真正施术者不计——由调用方按 SurgeryStatus 过滤）：
    /// 累计台数 +1、写回 <see cref="SurgeryCountFlag"/>，返回**新台数**。调用方据 <see cref="EvaluateLevel"/> 判是否达 L3 并置永续遗产旗标。
    /// </summary>
    public static int RecordSurgery(StoryFlags flags)
    {
        int n = SurgeriesPerformed(flags) + 1;
        flags.Set(SurgeryCountFlag, n.ToString());
        return n;
    }

    /// <summary>由累计手术台数派生等级（入队即 L1；≥<see cref="Level2ThresholdSurgeries"/>→L2；≥<see cref="Level3ThresholdSurgeries"/>→L3）。</summary>
    public static int EvaluateLevel(int surgeriesPerformed)
        => surgeriesPerformed >= Level3ThresholdSurgeries ? 3
         : surgeriesPerformed >= Level2ThresholdSurgeries ? 2
         : 1;

    /// <summary>由 StoryFlags 直接取她当前等级（读台数→派生）。</summary>
    public static int LevelOf(StoryFlags flags) => EvaluateLevel(SurgeriesPerformed(flags));

    /// <summary>
    /// 某台手术的**基础点数**（per-surgeon）：施术者是南丁格尔→<see cref="NightingaleSurgeryBasePoints"/>(30)、常人→
    /// <see cref="DefaultSurgeryBasePoints"/>(15)；再叠加 L3 全营遗产 <see cref="CampSurgeryBaseBonus"/>(+5，若 <paramref name="l3LegacyActive"/>)。
    /// 供 <c>CampMain.OnSurgeryRequested</c> 喂 <c>PerformSurgery(surgeryBasePoints:…)</c>。纯静态、可脱实例（遗产在她死后仍算）。
    /// </summary>
    public static int SurgeryBasePoints(bool surgeonIsNightingale, bool l3LegacyActive)
        => (surgeonIsNightingale ? NightingaleSurgeryBasePoints : DefaultSurgeryBasePoints)
           + (l3LegacyActive ? CampSurgeryBaseBonus : 0);

    /// <summary>
    /// 全营**感染率乘子**：2级 −15%（<paramref name="nurseAliveInCamp"/> 且 <paramref name="nurseLevel"/>≥2）
    /// + 3级 −10%（<paramref name="l3LegacyActive"/>，永续遗产，她死/离营仍生效）。减免走**加法**（用户口径"合计 −25%"）：
    /// 存活 L3 = ×(1−0.15−0.10)=×0.75；死后/离营仅遗产 = ×0.90；仅 L2 存活 = ×0.85；无 = ×1.0。
    /// 供 <c>CampMain.AdvanceSurvivorsHealthDay</c> 喂各幸存者 <c>TickDay(infectionChanceMultiplier:…)</c>。纯静态、可脱实例。
    /// </summary>
    public static double CampInfectionMultiplier(int nurseLevel, bool nurseAliveInCamp, bool l3LegacyActive)
    {
        double reduction = 0.0;
        if (nurseAliveInCamp && nurseLevel >= 2) reduction += Level2InfectionReduction;
        if (l3LegacyActive) reduction += Level3InfectionReduction;
        return 1.0 - reduction;
    }
}

/// <summary>
/// 有效读速合成（纯函数，无状态）：自身与全营加成走**加法**，座位系数在最外层乘。
/// 公式：<c>有效读速 = 基础 × (1 + 自身level加成 + 全营perk加成汇总) × (有座 1.0 / 无座 0.9)</c>。
/// 全营加成汇总由上层遍历全体营员的 <see cref="SurvivorPerks.CampWideReadingSpeedBonus"/> 求和得到，
/// **含 L3 书虫本人**（故诺蒂 L3 有座 = 1+0.50+0.25 = ×1.75；普通人在其营地有座 = 1+0+0.25 = ×1.25）。座位惩罚为 draft。
/// </summary>
public static class ReadingSpeed
{
    // draft：无座位阅读惩罚（-10%）。
    /// <summary>无座位时的读速乘子（draft，整体 -10%）。</summary>
    public const double NoSeatMultiplier = 0.9;

    // draft：未读完前置书时的读速乘子（读得极慢、耗时 5 倍，但不禁止）。系数拟定待调。
    /// <summary>书籍前置链：读者尚未读完某书前置书时，读该书的读速乘子（draft，×0.2＝耗时 5 倍）。</summary>
    public const double MissingPrerequisiteMultiplier = 0.2;

    /// <summary>
    /// 书籍前置链系数：<paramref name="book"/> 无前置、或前置已被读者读完 → 1.0；否则 <see cref="MissingPrerequisiteMultiplier"/>。
    /// <paramref name="isBookComplete"/> 判某 book id 是否已被本读者读完（个人已读集，如 <c>Pawn.HasReadBook</c>）。未读前置**不禁止**阅读，只减速。
    /// </summary>
    public static double PrerequisiteFactor(BookData book, System.Func<string, bool> isBookComplete)
    {
        if (book is null) throw new System.ArgumentNullException(nameof(book));
        if (isBookComplete is null) throw new System.ArgumentNullException(nameof(isBookComplete));
        if (book.PrerequisiteBookId is null) return 1.0;
        return isBookComplete(book.PrerequisiteBookId) ? 1.0 : MissingPrerequisiteMultiplier;
    }

    /// <summary>
    /// 合成有效读速。
    /// </summary>
    /// <param name="baseSpeed">基础读速（每游戏内小时推进多少书本进度，量纲由上层定，通常 1.0）。</param>
    /// <param name="selfBonus">读者自身 perk 加成（加法，无 perk = 0，见 <see cref="SurvivorPerks.SelfReadingSpeedBonus"/>）。</param>
    /// <param name="hasSeat">是否有座位（无座施加 <see cref="NoSeatMultiplier"/> 惩罚）。</param>
    /// <param name="campWideBonusSum">全营 perk 加成汇总（各 L3 书虫 0.25 之和，含持有者本人，无则 0）。</param>
    /// <param name="prerequisiteFactor">
    /// 前置链系数（<see cref="PrerequisiteFactor"/>，无前置/前置已读 = 1.0，未读前置 = <see cref="MissingPrerequisiteMultiplier"/>）。
    /// 作独立乘子并入，故减速只影响耗时、不改 <see cref="ReadingProgress.IsComplete"/> 的读满阈值（仍是 <see cref="BookData.ReadHours"/>）。
    /// </param>
    public static double Effective(double baseSpeed, double selfBonus, bool hasSeat, double campWideBonusSum,
        double prerequisiteFactor = 1.0)
        => baseSpeed
           * (1.0 + selfBonus + campWideBonusSum)
           * (hasSeat ? 1.0 : NoSeatMultiplier)
           * prerequisiteFactor;
}
