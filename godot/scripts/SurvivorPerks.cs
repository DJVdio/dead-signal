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
    /// <summary>L3 满级贡献给全营的读速加成幅度（draft）。</summary>
    public const double CampWideBonusAtMax = 0.25;

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
    /// <summary>2级：全营感染率 −10%（她在营存活时生效）。T21 用户在数值表上手改：−15% → −10%。</summary>
    public const double Level2InfectionReduction = 0.10;
    /// <summary>3级：全营感染率再 −5%（永续遗产）。T21 用户在数值表上手改：−10% → −5%。</summary>
    public const double Level3InfectionReduction = 0.05;

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
    /// 全营**感染率乘子**：2级 −10%（<paramref name="nurseAliveInCamp"/> 且 <paramref name="nurseLevel"/>≥2）
    /// + 3级 −5%（<paramref name="l3LegacyActive"/>，永续遗产，她死/离营仍生效）。减免走**加法**（用户口径）：
    /// 存活 L3 = ×(1−0.10−0.05)=×0.85；死后/离营仅遗产 = ×0.95；仅 L2 存活 = ×0.90；无 = ×1.0。
    /// 供 <c>CampMain.AdvanceSurvivorsHealthDay</c> 喂各幸存者 <c>TickDay(infectionChanceMultiplier:…)</c>。纯静态、可脱实例。
    ///
    /// ⚠️ <b>[DECISION] 未决——轴的归属</b>：本乘子作用在<b>预防轴</b>（<c>infectionChanceMultiplier</c>＝"会不会感染"的几率），
    /// 与山姆 L3 的<b>速率轴</b> <see cref="SamPerk.CampInfectionWorsenMultiplier"/>（"感染条涨多快"）显式正交。
    /// 用户已在数值表上把她的文案由「感染率降低」改成「感染<b>条速度</b>降低」，字面指向速率轴，
    /// 但换轴是行为变更（会与山姆的速率乘子叠加）⇒ <b>已上抛用户，未决前代码轴不动</b>。
    /// 护栏见 <c>NurseRecruitTests.NightingaleInfectionPerk_ActsOnPreventionAxis_NotProgressionAxis</c>。
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

    // —— 升级阈值（用户原话："营地 3 人时到达二级，6 人时到达三级"，非拟定）——
    /// <summary>升到 L2 所需的营地人数（活着的在营人类，含山姆）。</summary>
    public const int Level2CampPopulation = 3;
    /// <summary>升到 L3 所需的营地人数（活着的在营人类，含山姆）。</summary>
    public const int Level3CampPopulation = 6;

    // —— 三级效果数值（用户原话，**非拟定，勿标待调**）——
    /// <summary>1级：他收到的伤害 −10%（乘算减伤，作用在**护甲结算之后**，见 <c>CombatResolver.Resolve</c> 的 incomingDamageReduction）。</summary>
    public const double Level1DamageReduction = 0.10;
    /// <summary>2级：他的负重 +15%（作用于负重上限，见 <c>Loadout.CapacityFromStrength</c> 的 capacityMultiplier）。</summary>
    public const double Level2CarryBonus = 0.15;
    /// <summary>3级光环：全营负重 +3%。</summary>
    public const double AuraCarryBonus = 0.03;
    /// <summary>3级光环：全营干活效率 +3%（＝耗时缩短——制作 / 建造 / 搜刮等一切花时间的行为，见 <c>CraftingJob</c>/<c>RubbleSite</c>）。</summary>
    public const double AuraWorkSpeedBonus = 0.03;
    /// <summary>3级光环：全营身体恢复速度 +3%（术后流血/骨折的逐日愈合，见 <c>HealthConditionSet.TickDay</c> 的 healSpeedMultiplier）。</summary>
    public const double AuraHealSpeedBonus = 0.03;
    /// <summary>3级光环：全营感染条上升速度 −3%（感染恶化速率，见 <c>HealthConditionSet.AdvanceInfectionRace</c> 的 campWorsenMultiplier）。</summary>
    public const double AuraInfectionWorsenReduction = 0.03;

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
