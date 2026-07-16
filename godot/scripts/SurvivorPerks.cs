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

    /// <summary>
    /// 本 pawn 是否为山姆（"英雄风范"三级专属效果的**身份标记**）。效果规则/等级皆走静态 <see cref="SamPerk"/>——
    /// 他的等级**不存在实例状态**，由**实时营地人数**当场派生（见 <see cref="SamPerk.EvaluateLevel"/>），
    /// 故本处只标"这人是不是山姆"（供上层判"该给谁减伤/负重"、判"营地里山姆还活着吗"）。其余角色恒 false。
    /// </summary>
    public bool IsSam { get; private set; }

    /// <summary>把本 pawn 标记为山姆（赋予"英雄风范"专属效果身份）。建角时对山姆调用一次（<c>Pawn.Create</c> 按名授予）。</summary>
    public void GrantSam() => IsSam = true;

    /// <summary>
    /// 本 pawn 是否为**耗子**（下水道招募的无名幸存者，[T61]）。同南丁格尔的形态：效果规则/等级/计数皆走静态
    /// <see cref="RatPerk"/> + <c>StoryFlags</c>（计数持久化走旗标 <see cref="RatPerk.ScavengeCountFlag"/>），
    /// 本处只标"这人是不是耗子"。其余角色恒 false。
    /// </summary>
    public bool IsRat { get; private set; }

    /// <summary>把本 pawn 标记为耗子（赋予其三级专属效果身份）。招募入队时调用一次。</summary>
    public void GrantRat() => IsRat = true;

    /// <summary>
    /// 本 pawn 是否为**皮特**（青春期大男孩，曾校田径队；authored 三级专属效果）。同南丁格尔/耗子/山姆的形态：
    /// 效果规则/等级/计数皆走静态 <see cref="PetePerk"/> + <c>StoryFlags</c>（升级计数持久化走旗标
    /// <see cref="PetePerk.HungerStreakFlag"/> / <see cref="PetePerk.Level2ReachedFlag"/> / <see cref="PetePerk.DepartureCountFlag"/>），
    /// 本处只标"这人是不是皮特"（供 <c>CampMain</c> 判移速加成/操作光环/闪避/饥饿掉 2）。其余角色恒 false。
    /// </summary>
    public bool IsPete { get; private set; }

    /// <summary>把本 pawn 标记为皮特（赋予其三级专属效果身份）。建角时对皮特调用一次（<c>Pawn.Create</c> 按名授予）。</summary>
    public void GrantPete() => IsPete = true;
}

/// <summary>
/// **诺蒂·书虫**专属效果（纯逻辑）：靠累计阅读时间（游戏内小时）跨阈值升级 1→2→3。
/// 各级自身读速加成（加法）L1=+25% / L2=+50% / L3=+50%（L3 自身与 L2 相同——L3 的升级点是**多发一个全营 +25%**，不是自身再涨）；
/// 满级(L3)额外贡献全营 +25% 读速，作用到全营所有人**含诺蒂自己**（故诺蒂 L3 加起来对自己 = 50%+25% = 75%）。
/// 所有阈值/加成均为 <c>draft</c>——形态已锁，绝对数值待 Sim 拉表与用户拍板。
/// </summary>
public sealed class BookwormPerk
{
    // draft：升级阈值（累计阅读小时，游戏内）。参考文档 48h 量级，L2→L3 再翻倍余量。数值外置 perks.json。
    /// <summary>升到 L2 所需累计阅读小时（draft）。</summary>
    public static double Level2ThresholdHours => GameConfigCatalog.Section<PerkConfig>().BookwormLevel2ThresholdHours;
    /// <summary>升到 L3 所需累计阅读小时（draft）。</summary>
    public static double Level3ThresholdHours => GameConfigCatalog.Section<PerkConfig>().BookwormLevel3ThresholdHours;

    /// <summary>当前等级（1..3；持有者天生至少 L1）。</summary>
    public int Level { get; private set; } = 1;

    /// <summary>当前累计阅读时间（游戏内小时）。跨夜持久，只增不减。</summary>
    public double AccumulatedReadingHours { get; private set; }

    /// <summary>本人当前自身读速加成（加法，按等级；draft）。</summary>
    public double ReadingSpeedBonus => BonusForLevel(Level);

    /// <summary>满级(L3)时贡献给全营的读速加成，否则 0（draft）。</summary>
    public double CampWideReadingSpeedBonus => Level >= 3 ? CampWideBonusAtMax : 0.0;

    // draft：L3 满级全营读速加成幅度。数值外置 perks.json。
    /// <summary>L3 满级贡献给全营的读速加成幅度（draft）。</summary>
    public static double CampWideBonusAtMax => GameConfigCatalog.Section<PerkConfig>().BookwormCampWideBonusAtMax;

    /// <summary>某等级的自身读速加成（加法：L1=+0.25 / L2=+0.50 / L3=+0.50，L3 与 L2 同；越界按最近级钳制）。draft，数值外置 perks.json。</summary>
    public static double BonusForLevel(int level) => level switch
    {
        <= 1 => GameConfigCatalog.Section<PerkConfig>().BookwormSelfBonusL1,
        _ => GameConfigCatalog.Section<PerkConfig>().BookwormSelfBonusL2Plus, // L2、L3 自身加成相同（L3 升级点在全营加成，不在自身）
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
    // —— 升级阈值（[SPEC-B13-补2]，台数=她本人执行手术数；拟定待调）。数值外置 perks.json ——
    /// <summary>升到 L2 所需她本人累计手术台数（拟定待调）。</summary>
    public static int Level2ThresholdSurgeries => GameConfigCatalog.Section<PerkConfig>().NightingaleLevel2ThresholdSurgeries;
    /// <summary>升到 L3 所需她本人累计手术台数（拟定待调）。</summary>
    public static int Level3ThresholdSurgeries => GameConfigCatalog.Section<PerkConfig>().NightingaleLevel3ThresholdSurgeries;

    // —— 三级效果数值（[SPEC-B13-补] 用户原话，**非拟定，勿标待调**）。数值外置 perks.json ——
    /// <summary>常人手术基础点数（原 <c>HealthConditionSet.SurgeryBasePoints</c>）。</summary>
    public static int DefaultSurgeryBasePoints => GameConfigCatalog.Section<PerkConfig>().NightingaleDefaultSurgeryBasePoints;
    /// <summary>1级：南丁格尔本人手术基础点数（15→30）。</summary>
    public static int NightingaleSurgeryBasePoints => GameConfigCatalog.Section<PerkConfig>().NightingaleSurgeryBasePoints;
    /// <summary>3级：全营手术基础点 +5（永续遗产）。</summary>
    public static int CampSurgeryBaseBonus => GameConfigCatalog.Section<PerkConfig>().NightingaleCampSurgeryBaseBonus;
    /// <summary>
    /// 2级：全营感染率 <b>−15%</b>（她在营存活时生效）。
    /// <para>⚠️ 这个数**来回改过两轮**，别再"顺手改回去"：
    /// 初版 −15% →（T21 用户在数值表上手改<b>下调</b>）−10% →（T59 用户在 wiki 上<b>又调回</b>）−15%。
    /// 以表为准（表赢代码）。</para>
    /// 数值外置 perks.json（<c>NightingaleLevel2InfectionReduction</c>）。
    /// </summary>
    public static double Level2InfectionReduction => GameConfigCatalog.Section<PerkConfig>().NightingaleLevel2InfectionReduction;
    /// <summary>
    /// 3级：全营感染率再 <b>−10%</b>（永续遗产）。
    /// <para>⚠️ 同上，来回两轮：初版 −10% →（T21 下调）−5% →（T59 又调回）−10%。</para>
    /// 数值外置 perks.json（<c>NightingaleLevel3InfectionReduction</c>）。
    /// </summary>
    public static double Level3InfectionReduction => GameConfigCatalog.Section<PerkConfig>().NightingaleLevel3InfectionReduction;

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
    /// × 3级 −10%（<paramref name="l3LegacyActive"/>，永续遗产，她死/离营仍生效）。
    ///
    /// <para>🔴 <b>两级减免走乘算，不是加法</b>（CLAUDE.md 铁律：「百分比加成一律乘算，禁止加算」）。
    /// T59 之前这里是 <c>reduction += …; return 1 − reduction;</c> —— 一条**加算残留**，
    /// 它会让减免可以线性堆到 100%（堆够就"永不感染"）；乘算则永远逼近而不触及 0。
    /// 存活 L3 = ×(1−0.15)×(1−0.10) = <b>×0.765</b>（加算会给出 0.75，差的不只是这 0.015，而是方向）；
    /// 死后/离营仅遗产 = ×0.90；仅 L2 存活 = ×0.85；无 = ×1.0。</para>
    /// 供 <c>CampMain.AdvanceSurvivorsHealthDay</c> 喂各幸存者 <c>TickDay(infectionChanceMultiplier:…)</c>。纯静态、可脱实例。
    ///
    /// ✅ <b>[DECISION·已裁·感染重做] 轴的归属＝预防轴（用户拍板）</b>：本乘子作用在<b>预防轴</b>（<c>infectionChanceMultiplier</c>＝"会不会感染"的几率，
    /// ×0.765 连乘进 <see cref="HealthConditionSet.TickDay"/> 的每伤口感染几率链），**不是**恶化速率轴。
    /// 与山姆 L3 的<b>速率轴</b> <see cref="SamPerk.CampInfectionWorsenMultiplier"/>（"感染条涨多快"）显式正交、互不叠加同轴。
    /// 护栏见 <c>NurseRecruitTests.NightingaleInfectionPerk_ActsOnPreventionAxis_NotProgressionAxis</c>。
    /// </summary>
    public static double CampInfectionMultiplier(int nurseLevel, bool nurseAliveInCamp, bool l3LegacyActive)
    {
        double multiplier = 1.0;
        if (nurseAliveInCamp && nurseLevel >= 2) multiplier *= 1.0 - Level2InfectionReduction;
        if (l3LegacyActive) multiplier *= 1.0 - Level3InfectionReduction;
        return multiplier;
    }
}

/// <summary>
/// **山姆·"英雄风范"**（纯逻辑·authored perk，用户口径原话拍板，数值**非拟定**）。
///
/// 三级效果（累进：升级不丢下级效果，同诺蒂 L3 保留 L2 自身加成、南丁格尔 L3 与 L2 叠加）：
///   · 1级：他从小身强体壮、性格坚韧，比常人耐揍 —— **他收到的伤害 −10%**（只他自己）。
///   · 2级：从小吃苦耐劳帮祖母打理农庄 —— **他的负重 +15%**（只他自己）。
///   · 3级：他散发英雄风范、影响周边的人 —— 只要**山姆还活着**，营地**所有人**（含他自己）
///     **负重 +3%、干活效率 +3%、身体恢复速度 +3%、感染条上升速度 −3%**。
///
/// **⚠ 与既有 perk 的关键差异：本 perk 的等级「可倒退」。**
/// 诺蒂靠累计阅读时长、道格靠共同存活天数 —— 二者的升级轴都是**单调累计量**，只增不减；
/// 南丁格尔靠累计手术台数，同样单调（她死后计数天然冻结）。
/// 山姆的升级轴是**营地当前人数**（3 人 → L2、6 人 → L3）——这是个**会跌的实时量**：死了人，
/// 光环就退回去（用户原话："如果营地人数减少，山姆的技能会倒退"）。
///
/// **倒退是怎么"免费"得到的**：本类**无实例状态**（与 <see cref="NightingalePerk"/> 同为静态类），
/// 等级不是"存下来再推进"的字段，而是每次查询时由当前人数**当场派生**（<see cref="EvaluateLevel"/> 是纯函数）。
/// 没有需要"回滚"的存量，也就没有倒退逻辑——人数一变，下一次查询自然给出新等级，
/// "人死的那一刻光环就退"由调用方在人数变动后重新查询即可满足。单调 perk（<see cref="BookwormPerk"/> 的
/// 累计字段）与可倒退 perk（本类的纯派生）就这样共存于同一框架：**框架只要求"按条件结算 authored 效果"，
/// 从不要求条件必须单调**。
///
/// **营地人数口径**：**活着的、在营地的人类**——**狗（布鲁斯）不算人**（主 agent 裁决；本类 API 只收一个
/// 人数 int，无从得知狗的存在，口径由调用方保证）。山姆本人计入这个人数（"营地 3 人时到达二级"含他）。
/// **山姆死 → 等级归 0**（<see cref="EvaluateLevel"/> 的 <c>samAlive</c>），一切效果含全营光环即刻消失。
///
/// <para><b>⚠ [通则·用户拍板] 所有百分比加成一律「乘算」，作用于当前实际值，绝不加算。</b>
/// 即 <c>最终值 = 当前实际值 × 1.03</c>，而非 <c>当前实际值 + 基准值 × 0.03</c>。
/// <b>理由（用户原话）</b>：加算会导致"**没手的人也有 3% 操作能力**"的怪事——手全没了的人操作能力应该是 0，
/// 加算会让他凭空有 3%。乘算下 <c>0 × 1.03 = 0</c>，**残疾就是残疾**，加成不会把残缺的代价凭空补偿掉。
/// 本类六项加成全部照此：伤害 ×0.90、负重 ×1.15 / ×1.03、操作能力 ×1.03、恢复 ×1.03、感染恶化 ×0.97；
/// **多项并存时连乘**（山姆自己的负重 = 1.15 × 1.03 = ×1.1845，不是加算的 ×1.18）。</para>
/// </summary>
public static class SamPerk
{
    /// <summary>山姆的姓名（<c>Pawn.Create</c> 按此名授予 <see cref="SurvivorPerks.GrantSam"/>，同诺蒂/南丁格尔按名授予先例）。</summary>
    public const string SamName = "山姆";

    // —— 升级阈值（用户原话："营地 3 人时到达二级，6 人时到达三级"，非拟定）。数值外置 perks.json ——
    /// <summary>升到 L2 所需的营地人数（活着的在营人类，含山姆）。</summary>
    public static int Level2CampPopulation => GameConfigCatalog.Section<PerkConfig>().SamLevel2CampPopulation;
    /// <summary>升到 L3 所需的营地人数（活着的在营人类，含山姆）。</summary>
    public static int Level3CampPopulation => GameConfigCatalog.Section<PerkConfig>().SamLevel3CampPopulation;

    // —— 三级效果数值（用户原话，**非拟定，勿标待调**）。数值外置 perks.json ——
    /// <summary>1级：他收到的伤害 −10%（乘算减伤，作用在**护甲结算之后**，见 <c>CombatResolver.Resolve</c> 的 incomingDamageReduction）。</summary>
    public static double Level1DamageReduction => GameConfigCatalog.Section<PerkConfig>().SamLevel1DamageReduction;
    /// <summary>2级：他的负重 +15%（作用于负重上限，见 <c>Loadout.CapacityFromStrength</c> 的 capacityMultiplier）。</summary>
    public static double Level2CarryBonus => GameConfigCatalog.Section<PerkConfig>().SamLevel2CarryBonus;
    /// <summary>3级光环：全营负重 +3%。</summary>
    public static double AuraCarryBonus => GameConfigCatalog.Section<PerkConfig>().SamAuraCarryBonus;
    /// <summary>3级光环：全营干活效率 +3%（＝耗时缩短——制作 / 建造 / 搜刮等一切花时间的行为，见 <c>CraftingJob</c>/<c>RubbleSite</c>）。</summary>
    public static double AuraWorkSpeedBonus => GameConfigCatalog.Section<PerkConfig>().SamAuraWorkSpeedBonus;
    /// <summary>3级光环：全营身体恢复速度 +3%（术后流血/骨折的逐日愈合，见 <c>HealthConditionSet.TickDay</c> 的 healSpeedMultiplier）。</summary>
    public static double AuraHealSpeedBonus => GameConfigCatalog.Section<PerkConfig>().SamAuraHealSpeedBonus;
    /// <summary>3级光环：全营感染条上升速度 −3%（感染恶化速率，见 <c>HealthConditionSet.AdvanceInfectionRace</c> 的 campWorsenMultiplier）。</summary>
    public static double AuraInfectionWorsenReduction => GameConfigCatalog.Section<PerkConfig>().SamAuraInfectionWorsenReduction;

    /// <summary>
    /// **可倒退的等级派生**（纯函数，无记忆）：由**当前**营地人数与山姆存活状态当场算出等级。
    /// 山姆死/不在营 → <b>0</b>（无等级、无任何效果，含全营光环）；否则 ≥<see cref="Level3CampPopulation"/>→3、
    /// ≥<see cref="Level2CampPopulation"/>→2、其余→1（他在营即至少 L1）。
    /// 人数跌回阈值以下时本函数直接返回更低的级——**这就是"倒退"的全部实现**，无需回滚任何存量。
    /// </summary>
    /// <param name="campPopulation">当前营地**活着的、在营的人类**数（含山姆本人；狗不计入，由调用方保证口径）。</param>
    /// <param name="samAlive">山姆当前是否还活着且在营（光环的硬前提，用户原话"只要山姆还活着"）。</param>
    public static int EvaluateLevel(int campPopulation, bool samAlive)
    {
        if (!samAlive) return 0;
        if (campPopulation >= Level3CampPopulation) return 3;
        if (campPopulation >= Level2CampPopulation) return 2;
        return 1;
    }

    /// <summary>
    /// 某角色**受到伤害**的减免比例（0=无减免）：仅山姆本人、且他有等级（≥L1，即活着）→ <see cref="Level1DamageReduction"/>。
    /// 1级效果**在 2/3 级依然保留**（等级累进）。喂给 <c>CombatResolver.Resolve(…, incomingDamageReduction:)</c>，
    /// 在**护甲结算之后**乘算（护甲先吃，剩下的伤害再 ×0.9）。其余角色恒 0 → 引擎行为与既有完全一致。
    /// </summary>
    public static double IncomingDamageReduction(int samLevel, bool isSam)
        => isSam && samLevel >= 1 ? Level1DamageReduction : 0.0;

    /// <summary>
    /// 某角色的**负重上限乘子**（1.0=无加成）：多项加成**连乘**（[通则] 百分比加成一律乘算，见类注释）——
    ///   · 山姆自己、L2 起：×(1+<see cref="Level2CarryBonus"/>)＝×1.15（他自己的体格）；
    ///   · 全营（含山姆）、L3 起：×(1+<see cref="AuraCarryBonus"/>)＝×1.03（他给全营的光环）。
    /// 故**山姆在 L3 两者连乘** = 1.15 × 1.03 = **×1.1845**（~~旧加算口径 ×1.18 已作废~~）；其他人 L3 = ×1.03。
    /// 喂给 <c>Loadout.CapacityFromStrength(strength, capacityMultiplier:)</c>，在那里再乘上他的基础负重能力
    /// —— 所以负重能力本身为 0 的人（若日后有此状态）加成后仍是 0。
    /// </summary>
    public static double CarryCapacityMultiplier(int samLevel, bool isSam)
    {
        double mult = 1.0;
        if (isSam && samLevel >= 2) mult *= 1.0 + Level2CarryBonus;
        if (samLevel >= 3) mult *= 1.0 + AuraCarryBonus; // 光环及于全营，含山姆本人
        return mult;
    }

    /// <summary>
    /// 全营**干活效率（操作能力）乘子**（1.0=无光环）：L3 → ×(1+<see cref="AuraWorkSpeedBonus"/>)＝×1.03。
    /// "干活"＝**一切需要花时间的行为**（用户澄清：制作 + 搜刮 + 建造全算）。
    /// 这是个**纯乘子**，必须乘到**当前实际的操作能力**上（见 <see cref="OperationCapabilityWithAura"/>），
    /// 而不是加到基准值上。山姆一死 → 等级 0 → 乘子回 1.0。
    /// </summary>
    public static double CampWorkSpeedMultiplier(int samLevel)
        => samLevel >= 3 ? 1.0 + AuraWorkSpeedBonus : 1.0;

    /// <summary>
    /// **[通则·乘算] 把 3 级光环施加到某人当前的操作能力上**：<c>当前实际操作能力 × 1.03</c>。
    ///
    /// <para><b>为什么必须是乘算（用户原话）</b>：加算会导致"**没手的人也有 3% 操作能力**"的怪事——
    /// 一个手全没了的人操作能力应该是 <b>0</b>，加算 <c>0 + 3%</c> 会让他凭空能干活，荒谬。
    /// 乘算下 <c>0 × 1.03 = 0</c>，**残疾就是残疾**，百分比加成不会凭空补偿残缺的代价。</para>
    ///
    /// <para>这条尤其咬合山姆自己：他**左手缺小拇指与无名指**（authored 设定），基础操作能力已被
    /// <c>Body.DisabilityModifiers.OperationPenalty</c>（−7%/指）打到 0.86。他给全营的 3% 光环，
    /// 对他自己也只能在**这个折损后的基数**上乘（0.86 × 1.03 = 0.8858，而非加算的 0.89）——
    /// **英雄有代价，代价不该被自己的光环抹掉**。</para>
    /// </summary>
    /// <param name="baseOperationCapability">该角色**当前实际**操作能力 0..1（残疾 × 饥饿 × 骨折已折算完，见 <c>Pawn.OperationCapability</c>）。</param>
    /// <param name="samLevel">山姆当前等级（<see cref="EvaluateLevel"/>；未到 L3 或山姆已死 → 原值返回）。</param>
    public static double OperationCapabilityWithAura(double baseOperationCapability, int samLevel)
        => baseOperationCapability * CampWorkSpeedMultiplier(samLevel);

    /// <summary>
    /// 全营**身体恢复速度乘子**（1.0=无光环）：L3 → ×(1+<see cref="AuraHealSpeedBonus"/>)。
    /// 作用于术后流血/骨折的逐日愈合量，喂给 <c>HealthConditionSet.TickDay(…, healSpeedMultiplier:)</c>。
    /// 与"睡床 / 玫瑰果茶"那条**加算百分点**的轴（extraHealBonusPct）是**正交两轴**：那条改恢复效率的点数，
    /// 本条是最终愈合量的乘子（用户口径是"恢复速度 +3%"＝速度的百分比，不是效率点数 +3 点）。
    /// </summary>
    public static double CampHealSpeedMultiplier(int samLevel)
        => samLevel >= 3 ? 1.0 + AuraHealSpeedBonus : 1.0;

    /// <summary>
    /// 全营**感染条上升速度乘子**（1.0=无光环）：L3 → ×(1−<see cref="AuraInfectionWorsenReduction"/>)＝×0.97。
    /// 喂给 <c>HealthConditionSet.AdvanceInfectionRace(…, campWorsenMultiplier:)</c>，与用药的
    /// <c>Medicine.WorsenMultiplier</c> 是**两个独立乘子**（药压得多、光环再压一点，互不吞没）。
    /// 与南丁格尔的 <c>CampInfectionMultiplier</c> 亦正交：那个压"会不会感染"(几率)，本条压"感染条涨多快"(速率)。
    /// </summary>
    public static double CampInfectionWorsenMultiplier(int samLevel)
        => samLevel >= 3 ? 1.0 - AuraInfectionWorsenReduction : 1.0;
}

/// <summary>
/// 有效读速合成（纯函数，无状态）：§2 通则①<b>全乘算</b>——自身 perk / 每个 L3 书虫的全营贡献 / 每件穿戴品读速效果
/// 各作<b>独立乘子</b>连乘，座位/前置系数亦乘。
/// 公式：<c>有效读速 = 基础 × (1 + 自身level加成) × ∏(1 + 各L3书虫全营贡献) × ∏(穿戴品读速效果乘子) × (有座 1.0 / 无座 0.9) × 前置系数</c>。
/// <para>
/// 全营乘子由上层遍历全体营员对 <see cref="SurvivorPerks.CampWideReadingSpeedBonus"/> 逐个 <c>×(1+贡献)</c> 连乘得到，
/// <b>含 L3 书虫本人</b>（故诺蒂 L3 有座 = 1.50 × 1.25 = <b>×1.875</b>；普通人在其营地有座 = 1.0 × 1.25 = ×1.25）。
/// 穿戴品乘子由 <see cref="ApparelCatalog.ApparelEffectMultiplier"/> 对读者穿戴品的 <see cref="EquipEffectKind.ReadingSpeed"/>
/// 效果连乘得到（无效果 = 1.0，如平光眼镜 = ×1.05）。座位惩罚为 draft。
/// </para>
/// <para>
/// 🔴 [加算残留整改·诺蒂读速] 旧式 <c>(1 + 自身 + 全营)</c> 是加算，违反 §2 通则①（会让"没能力的人"凭空得加成、
/// 且多个来源线性叠加而非独立作用）。本次改全乘算：诺蒂 L3 由旧 ×1.75 → ×1.875、双书虫由 ×1.5 → ×1.5625。
/// </para>
/// </summary>
public static class ReadingSpeed
{
    // draft：无座位阅读惩罚（-10%）。数值外置 perks.json。
    /// <summary>无座位时的读速乘子（draft，整体 -10%）。</summary>
    public static double NoSeatMultiplier => GameConfigCatalog.Section<PerkConfig>().ReadingNoSeatMultiplier;

    // draft：未读完前置书时的读速乘子（读得极慢、耗时 5 倍，但不禁止）。系数拟定待调，数值外置 perks.json。
    /// <summary>书籍前置链：读者尚未读完某书前置书时，读该书的读速乘子（draft，×0.2＝耗时 5 倍）。</summary>
    public static double MissingPrerequisiteMultiplier => GameConfigCatalog.Section<PerkConfig>().ReadingMissingPrerequisiteMultiplier;

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
    /// <param name="selfBonus">读者自身 perk 加成（作单因子 <c>1+selfBonus</c>，无 perk = 0，见 <see cref="SurvivorPerks.SelfReadingSpeedBonus"/>）。</param>
    /// <param name="hasSeat">是否有座位（无座施加 <see cref="NoSeatMultiplier"/> 惩罚）。</param>
    /// <param name="campWideMult">
    /// 全营 perk 加成<b>乘子</b>（各 L3 书虫 <c>(1+0.25)</c> 之<b>积</b>，含持有者本人，无书虫 = 1.0）。
    /// 🔴 由调用方连乘算好——本参数已是乘子（≥1.0），不是旧的加成之和。
    /// </param>
    /// <param name="apparelMult">
    /// 读者穿戴品读速效果<b>乘子</b>（各件 <see cref="EquipEffectKind.ReadingSpeed"/> 效果之积，无 = 1.0，如平光眼镜 = 1.05）。
    /// 由 <see cref="ApparelCatalog.ApparelEffectMultiplier"/> 对读者已穿戴品算得。
    /// </param>
    /// <param name="prerequisiteFactor">
    /// 前置链系数（<see cref="PrerequisiteFactor"/>，无前置/前置已读 = 1.0，未读前置 = <see cref="MissingPrerequisiteMultiplier"/>）。
    /// 作独立乘子并入，故减速只影响耗时、不改 <see cref="ReadingProgress.IsComplete"/> 的读满阈值（仍是 <see cref="BookData.ReadHours"/>）。
    /// </param>
    public static double Effective(double baseSpeed, double selfBonus, bool hasSeat, double campWideMult,
        double apparelMult = 1.0, double prerequisiteFactor = 1.0)
        => baseSpeed
           * (1.0 + selfBonus)   // 自身 perk 单因子（诺蒂 L3 = ×1.50）
           * campWideMult        // ∏(1 + 各 L3 书虫全营贡献)，调用方已连乘（无 = 1.0）
           * apparelMult         // ∏(穿戴品读速效果乘子)（无 = 1.0，平光眼镜 = ×1.05）
           * (hasSeat ? 1.0 : NoSeatMultiplier)
           * prerequisiteFactor;
}

/// <summary>
/// **耗子**专属效果（[T61] 下水道招募的无名幸存者；authored，数值为**用户原话**，非拟定、勿标待调）。
/// <para>
/// 形态照抄 <see cref="NightingalePerk"/>：**无实例状态**，等级由**累计搜出的物品件数**派生
/// （<see cref="EvaluateLevel"/>），件数持久化在 <c>StoryFlags</c>（<see cref="ScavengeCountFlag"/>，
/// "字符串承载整数"，同 <c>nightingale_surgery_count</c>）⇒ <b>存档天然覆盖，不加 SaveData 字段、不撞版本号</b>。
/// 身份标记 = <see cref="SurvivorPerks.IsRat"/>。
/// </para>
/// <para>
/// <b>「一件」的口径</b>：一件 = 藏物清单里的**一个 <c>LootItem</c> 条目**（见 <c>LootSession</c> 头注释：
/// 「8 发子弹是一堆、**一次转出**」）—— <b>不按数量/重量/价值</b>。故 75/250 件 = 75/250 个条目，
/// 计数源 = <c>LootSession</c> 每转出一件就 <see cref="RecordScavenged"/> 一次。
/// </para>
/// </summary>
public static class RatPerk
{
    /// <summary>她的名字（用户原话：「**没有名字，叫"耗子"**」）。</summary>
    public const string RatName = "耗子";

    // —— 升级阈值（用户原话：「搜刮寻找物品，累计转出来 75 件物品升到二级，250 件升到三级」）。数值外置 perks.json ——
    /// <summary>升到 L2 所需累计搜出件数（**用户原话 75**，非拟定）。</summary>
    public static int Level2ThresholdItems => GameConfigCatalog.Section<PerkConfig>().RatLevel2ThresholdItems;
    /// <summary>升到 L3 所需累计搜出件数（**用户原话 250**，非拟定）。</summary>
    public static int Level3ThresholdItems => GameConfigCatalog.Section<PerkConfig>().RatLevel3ThresholdItems;

    /// <summary>她累计搜出件数的持久化旗标 key（字符串承载整数，同南丁格尔的手术台数）。</summary>
    public const string ScavengeCountFlag = "rat_scavenge_count";

    // —— L1（用户原话：「耗子的脚步和动作轻不可闻，声音减少 40%。（战斗、开枪、破坏这些不减少），
    //     耗子的翻找搜刮速度 +50%」）——
    /// <summary>
    /// L1：她的**动作噪音半径乘子** = 0.60（＝"声音减少 40%"）。
    /// <para>
    /// 🔴 <b>这里绝不能拿 <see cref="NoiseKind"/> 当开关，别"顺手简化"</b>：那个枚举的语义轴是
    /// **"分不分阵营"**，不是 **"是不是战斗"** —— <b>开门(100) / 撬锁(30) / 静默拆除(35) 现在全都归
    /// <see cref="NoiseKind.Combat"/></b>。若按枚举分，耗子的**开门声会静默地不减**（而用户排除的只有
    /// 「战斗、开枪、破坏」，开门/撬锁属于"动作"，该减）。
    /// </para>
    /// <para>
    /// ⇒ <b>按用户原话的语义逐个 emitter 点名</b>（见 <see cref="AppliesToActionNoise"/>）：<br/>
    /// <b>减</b>：脚步 / 开门 / 撬锁 / 静默拆除（＝"脚步和动作"）。<br/>
    /// <b>不减</b>：武器攻击噪音（战斗、开枪）/ 砸门破防（破坏）。
    /// </para>
    /// 数值外置 perks.json（<c>RatLevel1ActionNoiseMultiplier</c>）。
    /// </summary>
    public static double Level1ActionNoiseMultiplier => GameConfigCatalog.Section<PerkConfig>().RatLevel1ActionNoiseMultiplier;

    /// <summary>L1：翻找搜刮速度 **+50%**（用户原话）。数值外置 perks.json。</summary>
    public static double Level1LootSpeedBonus => GameConfigCatalog.Section<PerkConfig>().RatLevel1LootSpeedBonus;

    // —— L2（用户原话：「在下水道捡垃圾是耗子的生存秘诀，耗子翻找物品的速度**再 +100%**，
    //     并且她翻找东西**不会产生任何噪音**」）——
    /// <summary>L2：翻找搜刮速度**再** +100%（用户原话「再 +100%」）。数值外置 perks.json。</summary>
    public static double Level2LootSpeedBonus => GameConfigCatalog.Section<PerkConfig>().RatLevel2LootSpeedBonus;

    // —— L3（[T61 挂起] 见下方 TODO：两条都是**引擎新轴**，主 agent 裁决为「与今日其余新轴统一立项」——
    //     常量先落，**不接线**，护栏测试钉死"未接线"）——
    /// <summary>
    /// L3：「黑暗带来的**隐匿点** +40%」（用户原话）。
    /// <para>
    /// 🔴 <b>[T61 挂起 · 未接线]</b>：「隐匿点」这个标量目前**只存在于 <see cref="NightWatchContest.ComputeStealth"/>**
    /// （营地夜袭对抗，**潜行方恒为劫掠者、警戒方恒为我方守卫**）⇒ <b>玩家 pawn 在探索关根本没有隐匿分这个量</b>。
    /// 探索关里"黑暗难被发现"走的是**另一条形态**：视锥几何（暗 ⇒ 观察者视锥缩窄 <c>VisionLogic.ConeFor</c>；
    /// 身上有光 ⇒ 别人看你的视距被放大 <c>Actor.ExposedCone</c>）。**+40% 无处可挂**，
    /// 硬挂到视锥上＝<b>拿别的效果冒充</b> ⇒ 不干。落地它需要新建「玩家 pawn 的探索关隐匿分」这条**引擎新轴**。
    /// </para>
    /// 数值外置 perks.json（<c>RatLevel3DarknessStealthBonus</c>）。
    /// </summary>
    public static double Level3DarknessStealthBonus => GameConfigCatalog.Section<PerkConfig>().RatLevel3DarknessStealthBonus;

    /// <summary>
    /// L3：「**破隐先手攻击**额外再造成 **35%** 的伤害」（用户原话）。
    /// <para>
    /// 🔴 <b>[T61 挂起 · 未接线]</b>：<c>CombatResolver</c> **没有任何攻方伤害乘子**（只有守方的
    /// <c>incomingDamageReduction</c>），**更没有"未被发现/偷袭"的概念**。项目里唯一叫 <c>FirstStrike</c> 的是
    /// **岗位属性**（<c>GuardPost</c> 的"暗哨"给一次**无冷却的免费攻击**）—— 它**既不是伤害加成，也不看有没有被发现**，
    /// <b>不是同一个东西</b>。落地它需要两样引擎里都没有的东西：①攻方伤害乘子入口 ②"我此刻未被任何敌人发现"的判定，
    /// 且**会进 Sim 结算路径 ⇒ 要受控 A/B**。
    /// </para>
    /// 数值外置 perks.json（<c>RatLevel3AmbushDamageBonus</c>）。
    /// </summary>
    public static double Level3AmbushDamageBonus => GameConfigCatalog.Section<PerkConfig>().RatLevel3AmbushDamageBonus;

    /// <summary>读她累计搜出的件数（未设置/不可解析 → 0）。</summary>
    public static int ItemsScavenged(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(ScavengeCountFlag), out int n) ? n : 0;

    /// <summary>
    /// 记 <paramref name="count"/> 件**她本人搜出**的物品（由 <c>LootSession</c> 每转出一件即记；
    /// 别人搜的不算 —— 这是**她的**生存秘诀）：累计件数 +count、写回旗标，返回**新件数**。
    /// </summary>
    public static int RecordScavenged(StoryFlags flags, int count = 1)
    {
        if (flags is null || count <= 0)
        {
            return ItemsScavenged(flags!);
        }
        int n = ItemsScavenged(flags) + count;
        flags.Set(ScavengeCountFlag, n.ToString());
        return n;
    }

    /// <summary>由累计搜出件数派生等级（入队即 L1；≥75→L2；≥250→L3）。</summary>
    public static int EvaluateLevel(int itemsScavenged)
        => itemsScavenged >= Level3ThresholdItems ? 3
         : itemsScavenged >= Level2ThresholdItems ? 2
         : 1;

    /// <summary>由 StoryFlags 直接取她当前等级（读件数→派生）。</summary>
    public static int LevelOf(StoryFlags flags) => EvaluateLevel(ItemsScavenged(flags));

    /// <summary>
    /// **搜刮速度倍率**（喂给 <c>LootSession.Advance(delta, workEfficiency)</c> / <c>EffectiveSecondsPerItem</c>
    /// 的那条乘子链**之上再乘一层**）。非耗子恒 1.0（零回归）。
    /// <para>
    /// 🔴 <b>这一条是"加算"，而且是<u>用户明确指定</u>的例外，不是漏网的加算残留 —— 别"顺手改成乘算"</b>：
    /// 用户原话 L1「速度 <b>+50%</b>」、L2「速度<b>再 +100%</b>」⇒ 主 agent 拍板口径
    /// <b>L2 = 1 + 0.50 + 1.00 = 2.50</b>。（项目通则「百分比一律乘算」针对的是**不同来源**的加成相叠；
    /// 这里是**同一个 perk 自己的两级台阶**，用户是按"总量"口述的。）有护栏测试钉死 2.50，改成乘算(3.0)当场红。
    /// </para>
    /// <para>
    /// ⚠️ <b>绝不能挂进 <c>CampMain.WorkEfficiencyOf</c></b> —— 那条乘子链是**制作/砌墙/挖废墟/搜刮共用**的，
    /// 而耗子的加成是**搜刮专属**（用户原话就是"翻找搜刮速度"）。必须在搜刮调用点单独乘这一层。
    /// </para>
    /// </summary>
    public static double LootSpeedMultiplier(bool isRat, int ratLevel)
    {
        if (!isRat)
        {
            return 1.0;
        }
        double bonus = Level1LootSpeedBonus;                       // L1：+50%
        if (ratLevel >= 2)
        {
            bonus += Level2LootSpeedBonus;                          // L2：再 +100% ⇒ 合计 +150%
        }
        return 1.0 + bonus;                                         // L1=1.50 / L2=L3=2.50
    }

    /// <summary>
    /// **动作噪音半径乘子**：耗子的脚步/开门/撬锁/静默拆除按此缩（L1 起 0.60）。非耗子恒 1.0（零回归）。
    /// <para>见 <see cref="Level1ActionNoiseMultiplier"/> 的大段注释：<b>调用方必须逐个 emitter 点名，
    /// 不许拿 <see cref="NoiseKind"/> 当开关</b>（武器攻击/砸门破防**不得**经过这里）。</para>
    /// </summary>
    public static double ActionNoiseMultiplier(bool isRat, int ratLevel)
        => isRat ? Level1ActionNoiseMultiplier : 1.0;

    /// <summary>
    /// **搜刮噪音乘子**：L2 起「她翻找东西**不会产生任何噪音**」⇒ 0.0；L1 与非耗子恒 1.0。
    /// <para>
    /// ⚠️ <b>现状：搜刮本身在引擎里就不发噪音</b>（<c>LootSession</c> 不走 <c>EmitNoise</c> 任何通道）
    /// ⇒ 本乘子当前**恒等于"无事发生"**，是一条**面向未来的护栏**：哪天给"翻找"加了噪音（如 <c>SilentDismantleLogic</c> 那样的
    /// 35 半径工作噪音），耗子 L2 必须**自动**是 0 —— 而不是等人想起来再补。有护栏测试钉死这个语义。
    /// </para>
    /// </summary>
    public static double LootNoiseMultiplier(bool isRat, int ratLevel)
        => isRat && ratLevel >= 2 ? 0.0 : 1.0;

    /// <summary>
    /// 该噪音**来源**是否吃耗子的 −40%（<b>唯一的分类真值表</b> —— 调用方一律查这里，别自己判）。
    /// <para>用户原话：「耗子的**脚步和动作**轻不可闻，声音减少 40%。（**战斗、开枪、破坏**这些不减少）」。</para>
    /// </summary>
    /// <param name="source">噪音来源，见 <see cref="RatNoiseSource"/>。</param>
    public static bool AppliesToActionNoise(RatNoiseSource source) => source switch
    {
        RatNoiseSource.Footstep => true,           // 脚步 —— 用户点名
        RatNoiseSource.DoorOpen => true,           // 开门 —— "动作"，且**不是**战斗/开枪/破坏
        RatNoiseSource.Lockpick => true,           // 撬锁 —— 同上
        RatNoiseSource.SilentDismantle => true,    // 静默拆除 —— 同上（拆的是自家结构，不是"破坏"敌方门）
        RatNoiseSource.WeaponAttack => false,      // 🔴 战斗、开枪 —— 用户明确排除
        RatNoiseSource.Breach => false,            // 🔴 砸门破防＝"破坏" —— 用户明确排除
        _ => false,                                 // 未知来源一律不减（保守：宁可不给，不可偷偷给）
    };
}

/// <summary>
/// 噪音的**来源**（[T61]）。<b>这是给耗子的 −40% 用的分类轴，和 <see cref="NoiseKind"/> 是两个正交的轴，别混</b>：
/// <list type="bullet">
/// <item><see cref="NoiseKind"/> 的语义轴 = <b>"这个声音分不分阵营"</b>（Movement 分 / Combat 不分）。</item>
/// <item><see cref="RatNoiseSource"/> 的语义轴 = <b>"这个声音是不是'脚步和动作'"</b>（⇔ 战斗/开枪/破坏）。</item>
/// </list>
/// 两者**不重合**：开门/撬锁/静默拆除在 <see cref="NoiseKind"/> 里是 <c>Combat</c>（不分阵营 —— 谁听见都过来看），
/// 但在本轴里是**"动作"**（该被耗子压低）。⇒ <b>拿 NoiseKind 当耗子的开关是错的</b>。
/// </summary>
public enum RatNoiseSource
{
    /// <summary>脚步（<c>Actor.FootstepNoiseRadius</c>）。</summary>
    Footstep,
    /// <summary>开门（<c>DoorLogic.NoiseOfOpening</c>）。</summary>
    DoorOpen,
    /// <summary>撬锁（<c>NoiseLogic.LockpickNoiseRadius</c>）。</summary>
    Lockpick,
    /// <summary>静默拆除（<c>NoiseLogic.SilentDismantleNoiseRadius</c>）。</summary>
    SilentDismantle,
    /// <summary>🔴 武器攻击（近战挥击 / 开枪 / 枪托）—— <b>用户明确排除，不减</b>。</summary>
    WeaponAttack,
    /// <summary>🔴 砸门破防（"破坏"）—— <b>用户明确排除，不减</b>。</summary>
    Breach,
}
