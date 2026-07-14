using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 只 **只读引用** 战斗引擎（DeadSignal.Combat）的出血/骨折状态语义，绝不改战斗引擎定稿。
//
// 承载"伤病随时间恶化 + 手术治疗"的全部规则（模型层，不接 Body/CampMain 每帧推进——接入后续波）：
//   · 一个幸存者持一份 HealthConditionSet（若干病状 HealthCondition，各带严重度 severity 0..1）。
//   · 每昼夜 TickDay：未处置病状按规则恶化——出血拖久失血过多致死；开放伤口按几率感染；
//     感染恶化→肢体坏疽截肢(致残)/躯干败血症(致死)；骨折未手术→畸形愈合致残。
//   · **流血/骨折不会自动痊愈，只能靠手术**（PerformSurgery）：未手术的出血仍按 TickDay 恶化/致死。
//     手术 = 点数池(基础 15 + 床 +10 + 材料) → roll[0,池] = 恢复效率%；roll ≤ 10 失败需重做；
//     材料成功失败都消耗；医疗技能完全不参与（纯材料+床+运气）。成功后该伤按恢复效率%逐日愈合。
//   · 感染/疾病**不在手术机制内**：仍走药品 TreatIllness（抗生素治感染 / 成药治疾病，疗效取固定基数——通用技能系统已删）。
//   · 手术耗材（点数/适用伤类）在 SurgeryCatalog；药品语义在 MedicineCatalog；物品数据在 Materials.cs。
//   · HealthMapping.SeedFromBody：从战斗态只读播种伤病集（出血伤口→Bleeding、骨折→Fracture）。
// 数值全部"拟定待调 draft"，用于走通规则形态；具体值用 Sim 校准、结局狠辣是有意为之。

/// <summary>病状类别（可扩）。战斗产出 <see cref="Bleeding"/>/<see cref="Fracture"/>（需手术）；<see cref="Infection"/> 由开放伤口衍生（走药品）；<see cref="Disease"/> 预留环境/瘟疫来源（走药品）。</summary>
public enum HealthConditionType
{
    /// <summary>外伤出血（开放伤口）：不会自愈，只能手术；未手术逐日加重，同时是感染来源。
    /// 致命失血伤口(<see cref="HealthCondition.LethalBleed"/>=true，大部位)封顶=失血过多致死；
    /// 非致命(小锐器/咬伤类，小部位)只溃烂感染、不失血死。**止血≠无菌**：已手术伤口愈合闭口前仍保留感染窗。</summary>
    Bleeding,

    /// <summary>伤口感染：逐日恶化，封顶=肢体坏疽截肢(致残)/非肢体败血症(致死)。走抗生素治疗。</summary>
    Infection,

    /// <summary>骨折：不会自愈，只能手术；未手术逐日畸形化，封顶=该肢永久致残（不致死）。</summary>
    Fracture,

    /// <summary>疾病：泛化的病症（发热/痢疾等），逐日恶化，封顶=致死。走成药治疗。预留环境来源。</summary>
    Disease,
}

/// <summary>病状恶化封顶后的终态后果（供接入波据此对 Body 施加截肢/死亡）。</summary>
public enum ConditionOutcome
{
    /// <summary>无终态（仍在演变，或封顶但无系统后果）。</summary>
    None,

    /// <summary>致死。</summary>
    Death,

    /// <summary>致残：该部位永久失去（接入波据 BodyPart 执行截肢）。</summary>
    Maim,
}

/// <summary>一次药品治疗（感染/疾病）后的状态标签。</summary>
public enum TreatmentStatus
{
    /// <summary>药不对症/该病状不归药品处理（如流血/骨折），未产生疗效。</summary>
    NoEffect,

    /// <summary>已施治、严重度下降但未清除。</summary>
    Applied,

    /// <summary>感染/疾病已治愈清除。</summary>
    Cured,
}

/// <summary>
/// 单条伤病（可变）：类别 + 部位 + 严重度(0..1) + 是否位于肢体（决定终态是致残还是致死） +
/// 手术恢复效率(0=未手术) + 本昼夜是否已用药处置(压制感染/疾病当日恶化) + 已历天数。数值 draft。
/// </summary>
public sealed class HealthCondition
{
    /// <param name="severity">初始严重度，clamp 到 [0,1]。</param>
    /// <param name="bodyPart">部位名（对齐 <see cref="HumanBody"/> 部位名；系统性疾病可 null）。</param>
    /// <param name="onLimb">是否位于肢体：感染/骨折封顶时肢体→致残、非肢体→致死/无后果。</param>
    /// <param name="lethalBleed">仅对 <see cref="HealthConditionType.Bleeding"/> 有意义：true=大伤口，拖久失血过多致死；
    /// false=小锐器/咬伤类，只会溃烂感染、**不会失血致死**（severity 封顶在 <see cref="HealthConditionSet.MinorBleedSeverityCap"/> 之下）。</param>
    /// <param name="infectionProneness">感染倾向系数（乘进每昼夜感染几率）：**越小的伤越低**（大部位 1.0、手/脚 0.4、指/趾/面 0.2，见 <see cref="HealthMapping"/>）；直接构造默认 1.0。</param>
    /// <param name="selfHealing">仅"**很小的伤**"（微小部位 指/趾/眼/面/耳，或擦伤级低严重度）为真：新鲜期结束后**自行闭合**（无需手术）；中等非致命小伤(手/脚)与致命大伤均为 false（必须手术）。默认 false。</param>
    public HealthCondition(HealthConditionType type, double severity, string? bodyPart = null, bool onLimb = false, bool lethalBleed = true, double infectionProneness = 1.0, bool selfHealing = false)
    {
        Type = type;
        BodyPart = bodyPart;
        OnLimb = onLimb;
        LethalBleed = lethalBleed;
        InfectionProneness = Math.Max(0.0, infectionProneness);
        SelfHealing = selfHealing;
        Severity = Math.Clamp(severity, 0.0, 1.0);
    }

    public HealthConditionType Type { get; }

    /// <summary>部位名（可空：系统性疾病无具体部位）。</summary>
    public string? BodyPart { get; }

    /// <summary>是否位于肢体（感染/骨折终态分流：肢体致残、非肢体致死/无后果）。</summary>
    public bool OnLimb { get; }

    /// <summary>（仅 <see cref="HealthConditionType.Bleeding"/> 有意义）是否为致命失血伤口：true=大伤口，拖久失血致死；
    /// false=小锐器/咬伤类，只溃烂感染、不失血致死（severity 封顶在 <see cref="HealthConditionSet.MinorBleedSeverityCap"/> 之下）。</summary>
    public bool LethalBleed { get; }

    /// <summary>感染倾向系数（乘进每昼夜感染几率）：越小的伤越低（大 1.0 / 手脚 0.4 / 指趾面 0.2，draft）。让玩家"省不省这瓶抗生素"成为赌局。</summary>
    public double InfectionProneness { get; }

    /// <summary>是否为"很小的伤"（微小部位/擦伤级）：新鲜期结束后自行闭合、无需手术（中等小伤与致命大伤为 false，必须手术）。</summary>
    public bool SelfHealing { get; }

    /// <summary>严重度 0..1（1=封顶触发终态）。</summary>
    public double Severity { get; private set; }

    /// <summary>手术恢复效率 %（≥0，可 &gt;100）：0=未手术（流血/骨折不愈合、出血继续恶化）；&gt;10=手术成功，逐日按此愈合（100=正常速度，&gt;100=过载/超常发挥、愈合更快更好）。</summary>
    public int RecoveryEfficiency { get; private set; }

    /// <summary>
    /// [SPEC-B14-补3·感染双进度竞速] **治疗进度** 0..1（仅 <see cref="HealthConditionType.Infection"/> 有意义）：与 <see cref="Severity"/>（此处即"感染/死亡进度"）赛跑。
    /// 每次用药 <see cref="HealthConditionSet.TreatIllness"/> 按 <c>药物治疗效率(Efficacy) × 基准积累速率</c> 累加、**跨药持续累计**（先茶垫后抗生素接力有意义）；
    /// 先到 1.0 → 感染清除（双条清零、条目移除）。**治疗不减缓感染进度**（独立竞速，用户拍板）：感染进度照常在 <see cref="HealthConditionSet.TickDay"/> 累积，谁先到顶谁赢。
    /// </summary>
    public double CureProgress { get; private set; }

    /// <summary>是否已成功手术（流血/骨折进入愈合态）。</summary>
    public bool IsOperated => RecoveryEfficiency > 0;

    /// <summary>本昼夜是否已用药处置（TreatIllness 置真，TickDay 结算后清零）：压制感染/疾病当日自然恶化。</summary>
    public bool Tended { get; private set; }

    /// <summary>已历昼夜数（供 UI/叙事）。</summary>
    public int DaysElapsed { get; private set; }

    /// <summary>上次手术时的 <see cref="DaysElapsed"/> 值（-1=从未手术过）。用于"隔日可重做"门槛。</summary>
    public int LastSurgeryDay { get; private set; } = -1;

    /// <summary>距上次手术的昼夜数（从未手术 → <see cref="int.MaxValue"/>）。供 UI 显示与重做门槛判定。</summary>
    public int DaysSinceLastSurgery => LastSurgeryDay < 0 ? int.MaxValue : DaysElapsed - LastSurgeryDay;

    // ---- 以下 setter 仅供 HealthConditionSet 内部结算调用 ----
    internal void SetSeverity(double v) => Severity = Math.Clamp(v, 0.0, 1.0);
    internal void AddSeverity(double d) => SetSeverity(Severity + d);
    internal void MarkSurgeryDay() => LastSurgeryDay = DaysElapsed;
    internal void MarkTended() => Tended = true;
    internal void ClearTended() => Tended = false;
    internal void SetRecoveryEfficiency(int eff) => RecoveryEfficiency = Math.Max(0, eff); // 不封顶 100：允许过载效率 >100
    internal void AddCureProgress(double d) => CureProgress = Math.Clamp(CureProgress + d, 0.0, 1.0);
    internal void AdvanceDay() => DaysElapsed++;

    /// <summary>
    /// 读档：把全部可变进度覆盖回来。不变量（<see cref="Type"/>/<see cref="BodyPart"/>/致命性…）走构造器，
    /// 本方法只管"病到哪一步了"。
    /// <para>
    /// ⚠️ 为什么不复用 <see cref="MarkSurgeryDay"/>：那个只能把手术日记成"此刻"，而读档要还原的是
    /// <b>历史上的某一天</b>（"三天前动过刀" ≠ "刚动过刀"，重做手术冷却会因此算错）。
    /// </para>
    /// </summary>
    internal void RestoreState(double severity, int recoveryEfficiency, double cureProgress, bool tended, int daysElapsed, int lastSurgeryDay)
    {
        Severity = Math.Clamp(severity, 0.0, 1.0);
        RecoveryEfficiency = Math.Max(0, recoveryEfficiency);
        CureProgress = Math.Clamp(cureProgress, 0.0, 1.0);
        Tended = tended;
        DaysElapsed = Math.Max(0, daysElapsed);
        LastSurgeryDay = lastSurgeryDay;
    }
}

/// <summary>一条药品的治疗语义（物品数据在 <see cref="Materials"/>，此处只描述"治哪种病状 + 药效强度 + 治疗效率"）。仅覆盖**感染/疾病**（流血/骨折走手术，不在此）。</summary>
/// <param name="MaterialKey">对应 <see cref="MaterialDef.Key"/>（搜刮入库/治疗消耗按此扣减）。</param>
/// <param name="Treats">该药主治的病状类别（Infection/Disease）。</param>
/// <param name="Potency">药效基数 0..1（乘固定照护系数得该药"满效"下的单次降severity量）。</param>
/// <param name="Efficacy">
/// **治疗效率** 0..1（[SPEC-B14/终稿] 用户拍板值，非拟定）：
///   · 感染（双进度竞速·三档双效）——**治疗进度积累速率乘子**：用药期间累加治疗进度 = <c>Efficacy × <see cref="HealthConditionSet.CureProgressBaseRate"/> × dt</c>，跨药累计、先到 1.0 清除。
///     抗生素=1.00（通常 1~2 天痊愈）、草药膏=0.35（须趁早的次选）、蒲公英茶=0.15（只能延缓、单用赢不了、混合策略先垫进度再换药）。
///   · 疾病——仍走 severity 消退模型，消退量 = <c>Potency × 照护系数 × Efficacy</c>（成药 Efficacy=1.00，行为不变）。
/// 默认 1.00。与南丁格尔特长的"预防感染率乘子"(TickDay.infectionChanceMultiplier)是**正交两轴**：那个压"会不会感染"，本轴调"已感染治得多快"。
/// </param>
/// <param name="WorsenMultiplier">
/// **感染恶化减缓乘子** 0..1（[SPEC-B14/终稿] 用户拍板值，非拟定；仅感染用药有意义）：用药期间感染进度累积速率 ×此值。
/// 抗生素=0.50、草药膏=0.75、蒲公英茶=0.85（越强的药越能压住恶化）。默认 1.00（不减缓/非感染药）。这是三档"双效"的第二效——与 <see cref="Efficacy"/> 同为用户终稿定值。
/// </param>
public readonly record struct Medicine(string MaterialKey, HealthConditionType Treats, double Potency, double Efficacy = 1.0, double WorsenMultiplier = 1.0);

/// <summary>
/// 药品目录（**draft**，但 <see cref="Medicine.Efficacy"/> 三档百分比为 [SPEC-B14] 用户原话定值）：材料标识名 → 治疗语义。
/// **只含感染/疾病用药**（抗生素/草药膏/蒲公英茶治感染，成药治疾病）；流血/骨折不吃药、只做手术（见 <see cref="SurgeryCatalog"/>）。
/// 供 <see cref="HealthConditionSet.TreatIllness"/> 消费。感染三档共用同一 Potency 基数，仅治疗效率(Efficacy)不同——玩家据此权衡"用珍贵抗生素还是自制草药"。
/// </summary>
public static class MedicineCatalog
{
    private static readonly IReadOnlyDictionary<string, Medicine> _byKey = new[]
    {
        // [SPEC-B14/终稿·三档双效] 感染三档=治疗效率 + 恶化减缓（六值皆用户原话非拟定）：
        // 抗生素：治疗效率 1.00 + 恶化 ×0.50（通常 1~2 天痊愈）。
        new Medicine("antibiotics", HealthConditionType.Infection, 0.5, Efficacy: 1.00, WorsenMultiplier: 0.50),
        // 草药膏（蒲公英+玫瑰果+老君须）：治疗效率 0.35 + 恶化 ×0.75（须趁早的次选）。
        new Medicine("herbal_salve", HealthConditionType.Infection, 0.5, Efficacy: 0.35, WorsenMultiplier: 0.75),
        // 蒲公英茶：治疗效率 0.15 + 恶化 ×0.85（只能延缓，单用赢不了）。
        new Medicine("dandelion_tea", HealthConditionType.Infection, 0.5, Efficacy: 0.15, WorsenMultiplier: 0.85),
        // 成药：缓解泛化疾病，满效（疾病走 severity 消退模型，不涉恶化减缓）。
        new Medicine("medicine", HealthConditionType.Disease, 0.6, Efficacy: 1.00),
    }.ToDictionary(m => m.MaterialKey);

    /// <summary>按材料标识名查药品治疗语义；非药品返回 null。</summary>
    public static Medicine? For(string materialKey)
        => materialKey != null && _byKey.TryGetValue(materialKey, out Medicine m) ? m : null;

    /// <summary>该材料是否为（感染/疾病）药品。</summary>
    public static bool IsMedicine(string materialKey) => For(materialKey) != null;
}

/// <summary>一条手术耗材的语义（物品数据在 <see cref="Materials"/>，此处只描述"给多少手术点数 + 适用哪种伤 + 是否独占"）。</summary>
/// <param name="MaterialKey">对应 <see cref="MaterialDef.Key"/>（手术消耗按此扣减）。</param>
/// <param name="Points">该耗材贡献的手术点数。</param>
/// <param name="Exclusive">是否独占：独占耗材（急救包）不可与任何其他耗材同用于一台手术。</param>
/// <param name="Treats">适用的伤类（可多种，如急救包兼治流血+骨折）。</param>
public readonly record struct SurgerySupply(string MaterialKey, int Points, bool Exclusive, IReadOnlyList<HealthConditionType> Treats)
{
    /// <summary>本耗材是否适用于某伤类。</summary>
    public bool CanTreat(HealthConditionType type) => Treats.Contains(type);
}

/// <summary>
/// 手术耗材目录（**draft**）：材料标识名 → 手术点数/适用伤类/是否独占。与 <see cref="Materials"/> 里
/// <see cref="MaterialCategory.Medical"/> 手术耗材一一对应，供 <see cref="HealthConditionSet.PerformSurgery"/> 消费。
///   · 流血：绷带 +15 / 草药绷带 +25（绷带上位替代）/ 针线 +15（非独占可叠加）；或急救包 +60（独占）。
///   · 骨折：夹板 +25；或急救包 +60（独占）。
/// </summary>
public static class SurgeryCatalog
{
    private static readonly IReadOnlyDictionary<string, SurgerySupply> _byKey = new[]
    {
        new SurgerySupply("bandage", 15, false, new[] { HealthConditionType.Bleeding }),
        // [SPEC-B14-补] 草药绷带：普通绷带的上位替代，止血手术供点 25（15/25 用户原话非拟定）。非独占，按现有加算模型可与针线叠加。
        new SurgerySupply("herbal_bandage", 25, false, new[] { HealthConditionType.Bleeding }),
        new SurgerySupply("needle_thread", 15, false, new[] { HealthConditionType.Bleeding }),
        new SurgerySupply("splint", 25, false, new[] { HealthConditionType.Fracture }),
        new SurgerySupply("first_aid_kit", 60, true, new[] { HealthConditionType.Bleeding, HealthConditionType.Fracture }),
    }.ToDictionary(s => s.MaterialKey);

    /// <summary>按材料标识名查手术耗材语义；非手术耗材返回 null。</summary>
    public static SurgerySupply? For(string materialKey)
        => materialKey != null && _byKey.TryGetValue(materialKey, out SurgerySupply s) ? s : null;

    /// <summary>该材料是否为手术耗材。</summary>
    public static bool IsSupply(string materialKey) => For(materialKey) != null;
}

/// <summary>一本医疗书给的手术加点（点值 + 生效条件）。</summary>
/// <param name="Points">手术点数（加算进点数池的原始点，不是百分比乘子）。</param>
/// <param name="OnlyWithoutSupplies">是否**只在这台手术一件耗材都没投**时才生效（徒手/野路子手术的补偿）。</param>
public readonly record struct MedicalBookBonus(int Points, bool OnlyWithoutSupplies);

/// <summary>
/// 医疗书籍 → 手术治疗点注册表（**draft**，点值不对玩家展示）。
/// 施术者**读过**的医疗书各按其点值加算进手术点数池（靠书籍知识，与 Medical 技能无关）。
/// <para>
/// 加点分两个桶，因为书的效果可以带条件：
///   · <b>无条件</b>（<see cref="SumAlways"/>）——读过就算，随便你用什么耗材；
///   · <b>只在不用耗材时</b>（<see cref="SumWithoutSupplies"/>）——《野外生存指南》就是这一档：它教的是
///     没有手术刀、没有缝合线时怎么用林子里的土办法硬撑，所以**一旦你投了正规耗材，这本书就不加分了**
///     （正规器械有自己的加成）。
/// </para>
/// 接入波用"施术者 ReadBookSet ∩ 本表"分桶求和，两个数分别喂给 <see cref="HealthConditionSet.PerformSurgery"/> 的
/// <c>surgeonBookBonus</c> / <c>surgeonBookBonusNoSupplies</c>；**"有没有投耗材"由引擎按实际投入判定**（本模型只出点、不耦合 ReadBookSet/Pawn）。
/// </summary>
public static class MedicalBookPoints
{
    // book id（对齐 BookData.Id）→ 手术加点。UI 只显示"略增医学学识"之类模糊描述，不展示具体点数。
    private static readonly IReadOnlyDictionary<string, MedicalBookBonus> _byBookId = new Dictionary<string, MedicalBookBonus>
    {
        // 《野外生存指南》：**不使用任何手术材料时** +6（用户原话）。徒手手术的补偿，投了耗材即不生效。
        ["wilderness_survival_guide"] = new MedicalBookBonus(6, OnlyWithoutSupplies: true),
    };

    /// <summary>该书的手术治疗点（**不判条件**，只是点值——问条件用 <see cref="RequiresNoSupplies"/>）；非医疗书返回 0。</summary>
    public static int For(string bookId)
        => bookId != null && _byBookId.TryGetValue(bookId, out MedicalBookBonus b) ? b.Points : 0;

    /// <summary>该书的加点是否**只在不投任何手术耗材时**才生效；非医疗书返回 false。</summary>
    public static bool RequiresNoSupplies(string bookId)
        => bookId != null && _byBookId.TryGetValue(bookId, out MedicalBookBonus b) && b.OnlyWithoutSupplies;

    /// <summary>该书是否为医疗书（计入手术治疗点）。</summary>
    public static bool IsMedicalBook(string bookId) => bookId != null && _byBookId.ContainsKey(bookId);

    /// <summary>一组已读书里**无条件生效**的手术加点合计 → 喂 <c>surgeonBookBonus</c>。</summary>
    public static int SumAlways(IEnumerable<string> readBookIds)
        => readBookIds == null ? 0 : readBookIds.Where(id => !RequiresNoSupplies(id)).Sum(For);

    /// <summary>一组已读书里**只在不投耗材时生效**的手术加点合计 → 喂 <c>surgeonBookBonusNoSupplies</c>（引擎按实际投入决定加不加）。</summary>
    public static int SumWithoutSupplies(IEnumerable<string> readBookIds)
        => readBookIds == null ? 0 : readBookIds.Where(RequiresNoSupplies).Sum(For);
}

/// <summary>一次药品治疗（感染/疾病）结果。</summary>
public sealed class TreatmentResult
{
    public TreatmentStatus Status { get; init; }
    public double SeverityBefore { get; init; }
    public double SeverityAfter { get; init; }
    /// <summary>该病状是否因本次治疗被移除（治愈）。</summary>
    public bool Removed { get; init; }
}

/// <summary>一次手术的判定结果。</summary>
public enum SurgeryStatus
{
    /// <summary>手术成功（roll &gt; 失败阈值 10）：该伤进入愈合态。</summary>
    Success,

    /// <summary>手术失败（roll ≤ 10）：材料照耗，需重做。</summary>
    Failed,

    /// <summary>门槛未过（有效池 P &lt; 15，凑不出可行手术）：不 roll、不消耗、不改病状。给玩家展示 <see cref="SurgeryResult.NotAllowedMessage"/>。</summary>
    NotAllowed,

    /// <summary>重做冷却未到（距上次手术 ≤ <see cref="HealthConditionSet.RedoSurgeryCooldownDays"/> 昼夜）：不 roll、不消耗、不改病状。给玩家展示 <see cref="SurgeryResult.RedoTooSoonMessage"/>。</summary>
    RedoTooSoon,
}

/// <summary>一次手术（流血/骨折）结果。</summary>
public sealed class SurgeryResult
{
    /// <summary>门槛未过时给玩家的提示文案（**不暴露具体点数**）。</summary>
    public const string NotAllowedMessage = "现状不支持进行这场手术";

    /// <summary>重做冷却未到时给玩家的提示文案。</summary>
    public const string RedoTooSoonMessage = "距上次手术时间太短，暂时无法再次手术";

    /// <summary>本次手术判定。</summary>
    public SurgeryStatus Status { get; init; }

    /// <summary>是否成功（= <see cref="Status"/> 为 <see cref="SurgeryStatus.Success"/>）。</summary>
    public bool Success => Status == SurgeryStatus.Success;

    /// <summary>本次掷出的效率值 = 分段 roll 结果（成功时即恢复效率%，可 &gt;100；失败时 ≤10；门槛未过时 0）。</summary>
    public int Roll { get; init; }

    /// <summary>实际赋予该伤的恢复效率 %（成功=Roll，可 &gt;100；失败/门槛未过=0）。</summary>
    public int Efficiency { get; init; }

    /// <summary>本次手术的**有效点数池 P**（= (基础15+床+材料+医疗书) × 操作能力 × 自体系数，取整）；门槛判定与 roll 区间皆据此。</summary>
    public int PointPool { get; init; }

    /// <summary>本次消耗的手术耗材 Key（成功和失败**都消耗**；门槛未过**零消耗**；重做需再取）。</summary>
    public IReadOnlyList<string> ConsumedMaterials { get; init; } = Array.Empty<string>();

    /// <summary>给玩家的提示文案（目前仅门槛未过时非空）。</summary>
    public string? PlayerMessage { get; init; }
}

/// <summary>
/// 一个幸存者的伤病集（纯逻辑）：持若干 <see cref="HealthCondition"/>，
/// <see cref="TickDay"/> 推进恶化/愈合、<see cref="PerformSurgery"/> 做手术（流血/骨折）、
/// <see cref="TreatIllness"/> 用药（感染/疾病）。到致死终态置 <see cref="IsDead"/>。阈值/流速皆 draft，用 Sim 校准。
/// </summary>
public sealed class HealthConditionSet
{
    // ---- 恶化速率（每昼夜，draft）----
    private const double BleedWorsenPerDay = 0.10;       // 未手术出血逐日加重；draft：0.15→0.10 放宽操作窗（致命伤死亡线第 5→约第 8 昼夜后移，仍必死）
    private const double InfectionBaseChance = 0.45;     // 开放伤口感染几率基数（× 出血严重度 × 伤口大小系数）
    private const double InfectionInitialSeverity = 0.0; // 新感染初始"感染进度"（死亡赛道起点，终稿：从 0 起算）
    // [SPEC-B14/终稿·三档双效] 感染双进度竞速：Severity 即"感染/死亡进度"，按 dt 天数累积；用药期间按档 ×WorsenMultiplier 减缓（非"不减速"）。封顶 1.0=坏疽/败血症。
    // draft：r_i = 1/6 天≈0.1667/日 → 未用药约第 6 昼夜封顶（T_i=6 天，与失血 7 天死线错开）。用 Sim 校准。**全程 double 不取整**（[SPEC-B14-补4]）。
    private const double InfectionWorsenPerDay = 1.0 / 6.0;
    /// <summary>[SPEC-B14/终稿] 治疗进度基准积累速率（每日，效率 1.0 满效值，draft）：用药期间累加治疗进度 = <see cref="Medicine.Efficacy"/> × 本值 × dt天数。
    /// draft 0.67：锚点=抗生素(1.00)通常 1~2 天痊愈（用户原话）；草药膏(0.35→0.2345/日)须趁早的次选(~4.3 天连续)；蒲公英茶(0.15→0.1005/日)只能延缓、单用赢不了。用 Sim 校准。</summary>
    public const double CureProgressBaseRate = 0.67;
    private const double DiseaseWorsenPerDay = 0.12;     // 疾病逐日恶化
    private const double RestWorsenFactor = 0.5;         // 休养减缓感染/疾病恶化系数（抗生素才是正解、卧床只拖延）
    private const double FractureMalunionPerDay = 0.05;  // 未手术骨折逐日畸形化（封顶致残）

    // ---- 感染窗 / 非致命失血（draft）----
    /// <summary>非致命失血伤口（小锐器/咬伤类）的严重度封顶：只溃烂感染、拖再久也不失血死。draft。</summary>
    public const double MinorBleedSeverityCap = 0.6;
    /// <summary>止血≠无菌：已手术伤口 severity ≥ 此闭口阈值前仍有感染窗；降到其下=伤口封闭、不再感染。draft。</summary>
    private const double WoundClosedThreshold = 0.15;
    /// <summary>已手术（止血中）伤口的感染几率折减系数（相对未手术开放伤口）：止血降低但不清零感染风险。draft。</summary>
    private const double OperatedInfectionFactor = 0.5;
    /// <summary>伤口"新鲜期"感染窗口（昼夜数）：只有伤口存在的头几昼夜有感染风险；过后即使不闭口/不手术也不再新感染
    /// （身体已把伤口壁垒化）。这让**放任小伤不再累积到 100% 坏疽**、感染成为"值得赌但会翻车"的有限概率事件。draft。</summary>
    public const int InfectionWindowDays = 4;
    /// <summary>擦伤级严重度阈值：初始 severity ≤ 此值的出血视作"很小的伤"，可自行闭合（配合微小部位判定，见 <see cref="HealthMapping"/>）。draft。</summary>
    public const double AbrasionSeverityThreshold = 0.2;

    // ---- 愈合速率（每昼夜，draft）：手术成功后按 恢复效率% 折算，100%=下述基速 ----
    private const double BleedHealPerDay = 0.20;         // 已手术出血逐日愈合（× 效率/100）；出血愈合本就够快，不动
    private const double FractureHealPerDay = 0.24;      // 已手术骨折逐日愈合（× 效率/100）；draft：0.12→0.24 提速（齐装卧床 ~10→~7 昼夜）
    private const double RestHealBonus = 1.5;            // 休养加速愈合系数

    // ---- 手术点数（draft）----
    /// <summary>手术基础点数（徒手无床的底池）。</summary>
    public const int SurgeryBasePoints = 15;
    /// <summary>床位加成点数。</summary>
    public const int BedBonusPoints = 10;
    /// <summary>手术失败阈值：roll ≤ 此值 → 失败，需重做（材料照耗）。</summary>
    public const int SurgeryFailThreshold = 10;
    /// <summary>手术门槛：有效池 P &lt; 此值 → 不允许手术（凑不出可行手术，零消耗零改动）。</summary>
    public const int SurgeryMinPoints = 15;
    /// <summary>自体手术能力系数：对自己动手，池 ×0.60。</summary>
    public const double SelfSurgeryFactor = 0.60;

    /// <summary>手术成功即刻恢复量：任何成功手术（含擦边低效率成功）当场对该伤 severity 立减此值。draft，防止擦边成功康复过久。</summary>
    public const double ImmediateHealOnSuccess = 0.05;
    /// <summary>睡床加算恢复速度（**百分点**，加算非乘算）：术后愈合当昼夜在床上睡觉休息 → 恢复效率 +此值再折愈合速度（如 33%→43%）。draft。</summary>
    public const double BedSleepHealBonusPct = 10.0;
    /// <summary>重做手术冷却（昼夜）：距上次手术 &gt; 此值才可重做（当前=1 → "超过一天"）。draft，边界待确认。</summary>
    public const int RedoSurgeryCooldownDays = 1;

    private readonly List<HealthCondition> _conditions = new();

    public IReadOnlyList<HealthCondition> Conditions => _conditions;

    /// <summary>是否已因某病状致死（终态）。</summary>
    public bool IsDead { get; private set; }

    /// <summary>是否含某类病状。</summary>
    public bool Has(HealthConditionType type) => _conditions.Any(c => c.Type == type);

    /// <summary>登记一条病状（战斗产出/环境衍生皆经此入库）。</summary>
    public void Add(HealthCondition condition) => _conditions.Add(condition);

    /// <summary>
    /// 读档：清空并灌回全套病状，同时还原"是否已病死"这个终态。
    /// <para>
    /// <see cref="IsDead"/> 平时只由 <c>TickDay</c>/手术置位，没有 setter——这是对的（生死不该被随便写）。
    /// 但读档必须能还原一个已经死于感染的人，否则读回来他会诈尸。故开这个唯一的恢复入口。
    /// </para>
    /// </summary>
    internal void Restore(IEnumerable<HealthCondition> conditions, bool isDead)
    {
        _conditions.Clear();
        _conditions.AddRange(conditions);
        IsDead = isDead;
    }

    /// <summary>单次药效固定基数（draft，仅感染/疾病用药）：通用医疗技能已删，疗效不再随人变，取一个"照护得当"的固定系数，用 Sim 校准。</summary>
    private const double TreatmentPotencyFactor = 0.8;

    /// <summary>
    /// 一台手术能吃到多少**医疗书加点**（三个手术入口共用一份口径）：无条件那份恒计；"只在不用耗材时"那份
    /// 仅当本台手术**实际投入的耗材为零**时才计——判据是过滤后的 <paramref name="supplyCount"/>（不适用该伤类的材料
    /// 本就被忽略、不消耗，故不剥夺该加成）。负数一律按 0 兜底。
    /// </summary>
    private static int BookPoints(int always, int noSupplies, int supplyCount)
        => Math.Max(0, always) + (supplyCount == 0 ? Math.Max(0, noSupplies) : 0);

    /// <summary>
    /// 分段 roll 区间（纯函数，供测试直接验）：给定有效池 P，返回 roll 的闭区间 [min,max]（区间内均匀整数）。
    ///   · P ≤ 100：[0, P]；· 100 &lt; P ≤ 200：[P−100, 100]；· P &gt; 200：[100, P−100]。
    /// 连续：P=100→[0,100]、P=200→[100,100]（定值无方差）、P=250→[100,150]。堆点数越高下限越高、越稳（P≥111 下限≥11 不可能失败）。
    /// </summary>
    public static (int Min, int Max) RollRange(int p)
    {
        if (p <= 100)
        {
            return (0, p);
        }
        if (p <= 200)
        {
            return (p - 100, 100);
        }
        return (100, p - 100);
    }

    /// <summary>
    /// [SPEC-B14-补3] 感染进度(Severity 0..1)的**显示分档**（"坏疽"降为高进度呈现层，非独立终态）：
    /// 0–33% 初期 / 34–66% 扩散 / 67–99% 濒坏疽。供 UI 给感染条着色/措辞；100% 才是真终态（坏疽截肢/败血症死）。
    /// </summary>
    public static string InfectionStageWord(double severity)
        => severity < 0.34 ? "初期" : severity < 0.67 ? "扩散" : "濒坏疽";

    /// <summary>
    /// 该已手术伤口现在是否可**重做手术**（重新 roll 恢复效率、覆盖旧值）：需已手术且距上次手术 &gt; <see cref="RedoSurgeryCooldownDays"/> 昼夜。
    /// 供 UI 决定"再次手术"入口是否可用；未手术的伤（首次手术）不走此判定、按门槛正常做。
    /// </summary>
    public bool CanRedoSurgery(HealthCondition condition)
        => condition != null && condition.IsOperated && condition.DaysSinceLastSurgery > RedoSurgeryCooldownDays;

    /// <summary>
    /// 对一处**流血/骨折**做一台手术（<b>医疗技能不参与</b>，靠材料+床+医疗书+操作能力+运气）：
    /// 有效池 <c>P = round( (基础15 + 床10 + 材料 + 施术者医疗书加点) × 操作能力 × (自体?0.60:1) )</c>；
    /// <b>P &lt; 15 → 门槛未过</b>（<see cref="SurgeryStatus.NotAllowed"/>，不 roll/不消耗/不改病状，给玩家 <see cref="SurgeryResult.NotAllowedMessage"/>）；
    /// 否则按 <see cref="RollRange"/>(P) 分段 roll 得效率值（**可 &gt;100**）；≤ <see cref="SurgeryFailThreshold"/>=失败需重做；
    /// 成功则给该伤记恢复效率%，<see cref="TickDay"/> 里逐日据此愈合。**材料成功失败都消耗（门槛未过零消耗）。**
    /// 医疗书点值/操作能力/池点数**均为内部数值，不对玩家展示**（UI 只给模糊描述）。
    /// </summary>
    /// <param name="condition">目标伤（必须在本集内、且为 Bleeding/Fracture，否则抛 <see cref="ArgumentException"/>）。</param>
    /// <param name="materials">投入的材料 Key 列表（非手术耗材/不适用该伤类的忽略、不消耗；急救包独占，与其他耗材同投则抛异常）。</param>
    /// <param name="onBed">是否在床上操作（+10 点数）。</param>
    /// <param name="rng">分段 roll（<see cref="IRandomSource.Range"/>，区间由 <see cref="RollRange"/> 定）。</param>
    /// <param name="surgeonBookBonus">施术者已读医疗书里**无条件生效**的加点合计（调用方走 <see cref="MedicalBookPoints.SumAlways"/>）。</param>
    /// <param name="surgeonBookBonusNoSupplies">
    /// 施术者已读医疗书里**只在这台手术一件耗材都没投时**才生效的加点合计（调用方走 <see cref="MedicalBookPoints.SumWithoutSupplies"/>；
    /// 《野外生存指南》+6 即此档）。判据是**实际投入**的耗材——不适用该伤类的材料本就被忽略、不消耗，故也不剥夺此加成。
    /// </param>
    /// <param name="selfSurgery">是否对自己手术（true → 池 ×0.60）。</param>
    /// <param name="operationCapability">施术者操作能力 0..1（满=1.0，残疾&lt;1；接入波从 Pawn 操作能力映射；池 ×它）。</param>
    /// <param name="surgeryBasePoints">
    /// 本台手术的**基础点数**（默认 null → 用常量 <see cref="SurgeryBasePoints"/>=15）。per-surgeon 可变入口
    /// （[SPEC-B13-补] 南丁格尔三级特长：她本人 30、L3 后全营 +5，由调用方经 <c>NightingalePerk.SurgeryBasePoints</c> 算好传入）。
    /// </param>
    public SurgeryResult PerformSurgery(
        HealthCondition condition,
        IReadOnlyList<string>? materials,
        bool onBed,
        IRandomSource rng,
        int surgeonBookBonus = 0,
        bool selfSurgery = false,
        double operationCapability = 1.0,
        int? surgeryBasePoints = null,
        int surgeonBookBonusNoSupplies = 0)
    {
        if (condition.Type != HealthConditionType.Bleeding && condition.Type != HealthConditionType.Fracture)
        {
            throw new ArgumentException("手术只处理流血/骨折；感染/疾病请走 TreatIllness。", nameof(condition));
        }
        if (!_conditions.Contains(condition))
        {
            throw new ArgumentException("该伤不在本伤病集内。", nameof(condition));
        }

        // 重做冷却：已手术伤口距上次手术不足 → 暂不可重做（不 roll/不消耗/不改病状）。
        if (condition.IsOperated && condition.DaysSinceLastSurgery <= RedoSurgeryCooldownDays)
        {
            return new SurgeryResult
            {
                Status = SurgeryStatus.RedoTooSoon,
                Roll = 0,
                Efficiency = 0,
                PointPool = 0,
                ConsumedMaterials = Array.Empty<string>(),
                PlayerMessage = SurgeryResult.RedoTooSoonMessage,
            };
        }

        // 解析投入的手术耗材：只保留适用于该伤类的（非耗材/不适用者忽略、不消耗）。
        var supplies = new List<SurgerySupply>();
        if (materials != null)
        {
            foreach (string key in materials)
            {
                SurgerySupply? s = SurgeryCatalog.For(key);
                if (s is SurgerySupply sup && sup.CanTreat(condition.Type))
                {
                    supplies.Add(sup);
                }
            }
        }

        // 独占校验：急救包不可与任何其他耗材（含另一个急救包）同用于一台手术。
        if (supplies.Any(s => s.Exclusive) && supplies.Count > 1)
        {
            throw new ArgumentException("急救包为独占耗材，不可与绷带/针线/夹板或另一急救包叠加。", nameof(materials));
        }

        // 有效池 P = (基础 + 床 + 材料 + 医疗书) × 操作能力 × 自体系数，取整。
        double cap = Math.Clamp(operationCapability, 0.0, 1.0);
        int rawPoints = (surgeryBasePoints ?? SurgeryBasePoints) + (onBed ? BedBonusPoints : 0) + supplies.Sum(s => s.Points)
                        + BookPoints(surgeonBookBonus, surgeonBookBonusNoSupplies, supplies.Count);
        int pool = (int)Math.Round(rawPoints * cap * (selfSurgery ? SelfSurgeryFactor : 1.0), MidpointRounding.AwayFromZero);

        // 门槛：P < 15 凑不出可行手术 → 不 roll、不消耗、不改病状。
        if (pool < SurgeryMinPoints)
        {
            return new SurgeryResult
            {
                Status = SurgeryStatus.NotAllowed,
                Roll = 0,
                Efficiency = 0,
                PointPool = pool,
                ConsumedMaterials = Array.Empty<string>(),
                PlayerMessage = SurgeryResult.NotAllowedMessage,
            };
        }

        // 分段 roll：区间内均匀整数（走 rng）。
        (int lo, int hi) = RollRange(pool);
        int roll = hi <= lo ? lo : Math.Clamp((int)rng.Range(lo, hi + 1), lo, hi);
        bool success = roll > SurgeryFailThreshold;

        // 材料成功失败都消耗。
        var consumed = supplies.Select(s => s.MaterialKey).ToList();

        // 记录本次手术昼夜（成功/失败都算一次手术 → 重置重做冷却）。
        condition.MarkSurgeryDay();

        if (success)
        {
            // 成功（含擦边）→ 覆盖恢复效率（重做双向风险：高值也会被刷掉）+ 即刻恢复 5%。
            condition.SetRecoveryEfficiency(roll); // 进入/刷新愈合态：TickDay 据此逐日愈合（效率可 >100）
            condition.AddSeverity(-ImmediateHealOnSuccess);
        }
        // 失败：初次=未止血(RecoveryEfficiency 仍 0，继续恶化)；重做=保守（保留旧效率/闭口进度，仅白费材料）——均不动 severity。

        return new SurgeryResult
        {
            Status = success ? SurgeryStatus.Success : SurgeryStatus.Failed,
            Roll = roll,
            Efficiency = success ? roll : 0,
            PointPool = pool,
            ConsumedMaterials = consumed,
        };
    }

    /// <summary>
    /// 用药治疗一处**疾病**（流血/骨折走手术、感染走 <see cref="AdvanceInfectionRace"/> 竞速，均不经此）：severity 消退 =
    /// <c>Potency × <see cref="TreatmentPotencyFactor"/> × Efficacy</c>，归 0 治愈；用药当日 <see cref="HealthCondition.Tended"/> 置真跳过自然恶化。
    /// 药不对症或病状非疾病 → <see cref="TreatmentStatus.NoEffect"/>。（感染改双进度竞速后不再从此走一次性消退。）
    /// </summary>
    public TreatmentResult TreatIllness(HealthCondition condition, Medicine? medicine)
    {
        double before = condition.Severity;

        bool matches = condition.Type == HealthConditionType.Disease && medicine.HasValue && medicine.Value.Treats == HealthConditionType.Disease;
        if (!matches)
        {
            return new TreatmentResult { Status = TreatmentStatus.NoEffect, SeverityBefore = before, SeverityAfter = before, Removed = false };
        }

        condition.MarkTended();
        condition.AddSeverity(-medicine!.Value.Potency * TreatmentPotencyFactor * medicine.Value.Efficacy);
        if (condition.Severity <= 0)
        {
            _conditions.Remove(condition);
            return new TreatmentResult { Status = TreatmentStatus.Cured, SeverityBefore = before, SeverityAfter = 0, Removed = true };
        }
        return new TreatmentResult { Status = TreatmentStatus.Applied, SeverityBefore = before, SeverityAfter = condition.Severity, Removed = false };
    }

    /// <summary>
    /// [SPEC-B14/终稿·三档双效] 推进本人**唯一一场**感染竞速一个时间片 <paramref name="dtDays"/>（天，可 &lt;1=相位级细分；全程 double 不取整）：
    ///   · 感染进度(Severity) += <c><see cref="InfectionWorsenPerDay"/> × dt × (用药? medicine.WorsenMultiplier : 1)</c>（用药期间按档减缓恶化）；
    ///   · 用药期间治疗进度(CureProgress) += <c>medicine.Efficacy × <see cref="CureProgressBaseRate"/> × dt</c>（跨药累计）；
    ///   · **治疗进度先到 1.0** → 清除感染（治疗抢先，双条随条目移除）；否则感染进度到 1.0 → 坏疽截肢(肢体)/败血症死(非肢体)。
    /// <paramref name="medicated"/>=false 表示本时间片未用药（无疗程/断药）：只累积感染、不减缓、不加治疗。无感染条目 → 返回 <see cref="ConditionOutcome.None"/>。
    /// </summary>
    /// <param name="dtDays">本时间片天数（相位级传 1/相位数；整日传 1.0）。</param>
    /// <param name="medicated">本时间片是否在用药（疗程指派且有药）。</param>
    /// <param name="medicine">所用药（<paramref name="medicated"/> 为真时必给感染药，取其 Efficacy/WorsenMultiplier）。</param>
    /// <param name="campWorsenMultiplier">
    /// 全营**感染条上升速度**乘子（<b>默认 1.0＝无影响</b>，既有调用零回归）：承载 authored 专属效果对恶化**速率**的加成——
    /// 现阶段唯一来源是**山姆 L3 光环 −3%**（×0.97，见 <c>SamPerk.CampInfectionWorsenMultiplier</c>）。
    /// 与用药的 <see cref="Medicine.WorsenMultiplier"/> 是**两个独立乘子**（相乘，互不吞没：药压得多、光环再压一点）。
    /// 与南丁格尔的 <c>NightingalePerk.CampInfectionMultiplier</c>（喂 <see cref="TickDay"/>）**正交**：
    /// 那个压"会不会感染"(新生几率)，本条压"已感染的条涨多快"(恶化速率)。只缩放恶化，不动治疗进度。
    /// </param>
    public InfectionRaceResult AdvanceInfectionRace(double dtDays, bool medicated, Medicine? medicine,
        double campWorsenMultiplier = 1.0)
    {
        HealthCondition? inf = _conditions.FirstOrDefault(c => c.Type == HealthConditionType.Infection);
        if (inf is null || dtDays <= 0.0)
        {
            return new InfectionRaceResult { Outcome = ConditionOutcome.None };
        }

        bool useMed = medicated && medicine is { } m0 && m0.Treats == HealthConditionType.Infection;
        double worsenMult = (useMed ? medicine!.Value.WorsenMultiplier : 1.0) * Math.Max(0.0, campWorsenMultiplier);
        inf.AddSeverity(InfectionWorsenPerDay * dtDays * worsenMult);
        if (useMed)
        {
            inf.AddCureProgress(medicine!.Value.Efficacy * CureProgressBaseRate * dtDays);
        }

        // 治疗抢先：治疗进度先到顶 → 清除感染（≥ 比较，不取整）。
        if (inf.CureProgress >= 1.0)
        {
            _conditions.Remove(inf);
            return new InfectionRaceResult { Outcome = ConditionOutcome.None, Cured = true };
        }
        // [SPEC-B14-补7] 感染进度到顶(≥100%) → **当相位立刻死亡**（无论肢体/非肢体，不再自动坏疽截肢）。
        // 想保命只能在到顶前由玩家主动选做 <see cref="PerformAmputation"/> 截肢（可选手术、需抉择），系统不自动截肢、不弹建议。
        if (inf.Severity >= 1.0)
        {
            IsDead = true;
            return new InfectionRaceResult { Outcome = ConditionOutcome.Death };
        }
        return new InfectionRaceResult { Outcome = ConditionOutcome.None };
    }

    /// <summary>
    /// [SPEC-B14-补7] **主动截肢手术**（可选、玩家抉择，非系统自动）：切除一处**感染的肢体**以中止感染竞速——最后的保命手段。
    /// 走既有手术点数池/耗材/成败流程：有效池 <c>P = round((基础15+床10+止血耗材+医疗书) × 操作能力 × (自体?0.60:1))</c>；
    /// P&lt;15 → <see cref="SurgeryStatus.NotAllowed"/>；否则按 <see cref="RollRange"/> 分段 roll，roll &gt; <see cref="SurgeryFailThreshold"/>=成功。
    /// **成功**：移除该感染（双条清零）+ 消解该部位其余病状（残端后果照既有规则：**保留仍在活动的致命失血=残端失血**，不软化）→ 调用方 <see cref="Body.Sever"/> 断肢；
    /// **失败**：耗材照耗、部位保留、感染继续竞速（未中止）。耗材取止血类（绷带/针线/草药绷带/急救包，关合残端）；点数/roll 不对玩家展示。
    /// </summary>
    /// <param name="infection">目标感染（须在本集内、Type=Infection 且 <see cref="HealthCondition.OnLimb"/>，否则抛 <see cref="ArgumentException"/>）。</param>
    /// <param name="materials">投入的止血耗材 Key（关合残端；非止血/不适用者忽略、不消耗；急救包独占）。</param>
    public SurgeryResult PerformAmputation(
        HealthCondition infection,
        IReadOnlyList<string>? materials,
        bool onBed,
        IRandomSource rng,
        int surgeonBookBonus = 0,
        bool selfSurgery = false,
        double operationCapability = 1.0,
        int? surgeryBasePoints = null,
        int surgeonBookBonusNoSupplies = 0)
    {
        if (infection.Type != HealthConditionType.Infection)
        {
            throw new ArgumentException("截肢手术只针对感染的肢体。", nameof(infection));
        }
        if (!infection.OnLimb)
        {
            throw new ArgumentException("只能截除肢体部位（非肢体感染无肢可截）。", nameof(infection));
        }
        if (!_conditions.Contains(infection))
        {
            throw new ArgumentException("该感染不在本伤病集内。", nameof(infection));
        }

        // 止血耗材（关合残端）：SurgeryCatalog 里能治 Bleeding 的供点计入。
        var supplies = new List<SurgerySupply>();
        if (materials != null)
        {
            foreach (string key in materials)
            {
                if (SurgeryCatalog.For(key) is SurgerySupply sup && sup.CanTreat(HealthConditionType.Bleeding))
                {
                    supplies.Add(sup);
                }
            }
        }
        if (supplies.Any(s => s.Exclusive) && supplies.Count > 1)
        {
            throw new ArgumentException("急救包为独占耗材，不可与其他耗材叠加。", nameof(materials));
        }

        double cap = Math.Clamp(operationCapability, 0.0, 1.0);
        int rawPoints = (surgeryBasePoints ?? SurgeryBasePoints) + (onBed ? BedBonusPoints : 0) + supplies.Sum(s => s.Points)
                        + BookPoints(surgeonBookBonus, surgeonBookBonusNoSupplies, supplies.Count);
        int pool = (int)Math.Round(rawPoints * cap * (selfSurgery ? SelfSurgeryFactor : 1.0), MidpointRounding.AwayFromZero);

        if (pool < SurgeryMinPoints)
        {
            return new SurgeryResult
            {
                Status = SurgeryStatus.NotAllowed,
                Roll = 0,
                Efficiency = 0,
                PointPool = pool,
                ConsumedMaterials = Array.Empty<string>(),
                PlayerMessage = SurgeryResult.NotAllowedMessage,
            };
        }

        (int lo, int hi) = RollRange(pool);
        int roll = hi <= lo ? lo : Math.Clamp((int)rng.Range(lo, hi + 1), lo, hi);
        bool success = roll > SurgeryFailThreshold;
        var consumed = supplies.Select(s => s.MaterialKey).ToList();

        if (success)
        {
            string? part = infection.BodyPart;
            _conditions.Remove(infection); // 双条随条目移除清零
            // 残端后果照既有规则：该部位其余病状消解，但仍在活动的致命失血例外（残端失血，别软化）。
            if (part != null)
            {
                _conditions.RemoveAll(h => h.BodyPart == part
                    && !(h.Type == HealthConditionType.Bleeding && h.LethalBleed && !h.IsOperated));
            }
        }
        // 失败：耗材照耗、部位保留、感染未中止（继续竞速）。

        return new SurgeryResult
        {
            Status = success ? SurgeryStatus.Success : SurgeryStatus.Failed,
            Roll = roll,
            Efficiency = success ? roll : 0,
            PointPool = pool,
            ConsumedMaterials = consumed,
        };
    }

    /// <summary>
    /// [SPEC-B14-补8] **安装假肢手术**的成败判定（纯 roll，无病状目标、无副作用）：给一个失去的肢体装假肢也是手术——
    /// 走既有点数池/耗材/成败流程。有效池 <c>P = round((基础15+床10+止血耗材+医疗书) × 操作能力 × (自体?0.60:1))</c>；
    /// P&lt;15 → <see cref="SurgeryStatus.NotAllowed"/>；否则 <see cref="RollRange"/> 分段 roll，roll &gt; <see cref="SurgeryFailThreshold"/>=成功。
    /// 本方法只判成败并返回耗材清单；假肢就位（<c>Body.AttachProsthetic</c>）与假肢本体消耗由调用方按结果处理
    /// （失败默认假肢本体不损耗、留库存可重试，见 Pawn/CampMain 接线）。耗材取止血类（关合残端）。
    /// </summary>
    public SurgeryResult PerformProstheticSurgery(
        IReadOnlyList<string>? materials,
        bool onBed,
        IRandomSource rng,
        int surgeonBookBonus = 0,
        bool selfSurgery = false,
        double operationCapability = 1.0,
        int? surgeryBasePoints = null,
        int surgeonBookBonusNoSupplies = 0)
    {
        var supplies = new List<SurgerySupply>();
        if (materials != null)
        {
            foreach (string key in materials)
            {
                if (SurgeryCatalog.For(key) is SurgerySupply sup && sup.CanTreat(HealthConditionType.Bleeding))
                {
                    supplies.Add(sup);
                }
            }
        }
        if (supplies.Any(s => s.Exclusive) && supplies.Count > 1)
        {
            throw new ArgumentException("急救包为独占耗材，不可与其他耗材叠加。", nameof(materials));
        }

        double cap = Math.Clamp(operationCapability, 0.0, 1.0);
        int rawPoints = (surgeryBasePoints ?? SurgeryBasePoints) + (onBed ? BedBonusPoints : 0) + supplies.Sum(s => s.Points)
                        + BookPoints(surgeonBookBonus, surgeonBookBonusNoSupplies, supplies.Count);
        int pool = (int)Math.Round(rawPoints * cap * (selfSurgery ? SelfSurgeryFactor : 1.0), MidpointRounding.AwayFromZero);

        if (pool < SurgeryMinPoints)
        {
            return new SurgeryResult
            {
                Status = SurgeryStatus.NotAllowed,
                Roll = 0,
                Efficiency = 0,
                PointPool = pool,
                ConsumedMaterials = Array.Empty<string>(),
                PlayerMessage = SurgeryResult.NotAllowedMessage,
            };
        }

        (int lo, int hi) = RollRange(pool);
        int roll = hi <= lo ? lo : Math.Clamp((int)rng.Range(lo, hi + 1), lo, hi);
        bool success = roll > SurgeryFailThreshold;

        return new SurgeryResult
        {
            Status = success ? SurgeryStatus.Success : SurgeryStatus.Failed,
            Roll = roll,
            Efficiency = success ? roll : 0,
            PointPool = pool,
            ConsumedMaterials = supplies.Select(s => s.MaterialKey).ToList(),
        };
    }

    /// <summary>
    /// 开放/未闭口伤口按几率感染（同部位已有感染则不重复）：命中则向 <paramref name="newConditions"/> 追加一条感染并返回 true。
    /// 短路：chance≤0 或该部位已感染 → 不消耗 rng、返回 false（供测试稳定断言 rng 消耗）。
    /// </summary>
    private bool TryContractInfection(HealthCondition wound, double chance, IRandomSource rng, List<HealthCondition> newConditions)
    {
        if (chance <= 0.0)
        {
            return false;
        }
        // [SPEC-B14-补3·护栏] 感染以人为单位：一名幸存者同时只跑**一场**感染竞速。已有任何感染 → 新伤口不再重开条（忽略并入，最简）。
        bool alreadyInfected = _conditions.Concat(newConditions)
            .Any(x => x.Type == HealthConditionType.Infection);
        if (alreadyInfected)
        {
            return false;
        }
        if (rng.Range(0.0, 1.0) < chance)
        {
            newConditions.Add(new HealthCondition(
                HealthConditionType.Infection, InfectionInitialSeverity, wound.BodyPart, wound.OnLimb));
            return true;
        }
        return false;
    }

    /// <summary>
    /// 推进一昼夜：已手术(RecoveryEfficiency&gt;0)的流血/骨折按 恢复效率% 逐日愈合（severity 归 0 移除）；未手术逐日恶化
    /// （出血加重/按几率感染/致命伤封顶失血死；骨折畸形化封顶致残）；感染/疾病未用药按规则恶化。返回本昼夜事件汇总；已死空转。
    /// </summary>
    /// <param name="rng">未手术开放伤口感染 roll 用（<see cref="IRandomSource.Range"/>(0,1)）。</param>
    /// <param name="resting">本昼夜是否卧床休养（减缓感染/疾病恶化、×<see cref="RestHealBonus"/> 加速术后愈合）。</param>
    /// <param name="restedInBed">本昼夜是否**在床上睡觉休息**（而非地铺）：术后愈合恢复效率**加算 +<see cref="BedSleepHealBonusPct"/> 个百分点**。默认 false，接入层按睡眠处是床/地铺传入。</param>
    /// <param name="infectionChanceMultiplier">
    /// 全营感染率乘子（默认 1.0＝无影响）：[SPEC-B13-补] 南丁格尔三级特长的营地卫生减免（她 L2 在营 ×0.85 / L3 遗产叠加至 ×0.75 等），
    /// 由调用方经 <c>NightingalePerk.CampInfectionMultiplier</c> 算好传入；只缩放本昼夜的开放伤口感染几率，不改其余恶化/愈合。
    /// </param>
    /// <param name="extraHealBonusPct">
    /// [SPEC-B14-补2] 额外恢复效率加成（**百分点**，加算，默认 0）：与睡床加算 <see cref="BedSleepHealBonusPct"/> **同族叠加**，只作用术后流血/骨折的逐日愈合速度。
    /// 玫瑰果茶 buff 生效时由调用方传 <c>+9</c>（Pawn 上 24 游戏小时计时）；不改感染/疾病恶化。
    /// </param>
    /// <param name="healSpeedMultiplier">
    /// 全营**身体恢复速度**乘子（<b>默认 1.0＝无影响</b>，既有调用零回归）：承载 authored 专属效果对愈合**速度**的百分比加成——
    /// 现阶段唯一来源是**山姆 L3 光环 +3%**（×1.03，见 <c>SamPerk.CampHealSpeedMultiplier</c>）。
    /// 只作用术后流血/骨折的逐日愈合量，不改恶化/感染几率。
    /// 与 <paramref name="restedInBed"/>/<paramref name="extraHealBonusPct"/> 那条**加算百分点**的轴（改的是恢复效率点数）
    /// 是**正交两轴**：本条是最终愈合量的**乘子**（用户口径"恢复速度 +3%"＝速度的百分比，非效率 +3 点）。
    /// </param>
    /// <param name="restFraction">
    /// [批次21·impl-bedrest] 本昼夜的**休养占比** 0..1（默认 null → 回落到 <paramref name="resting"/> 布尔，零回归）：
    /// 由 <see cref="RestLedger.RestFraction"/> 按相位累计得出——**白天在营地睡的相位自此计入**（旧模型整日只取一个布尔、
    /// 且在黎明读到的是昨夜的角色，白天睡整天等于白睡，见 <see cref="BedrestLogic"/> 文件头）。
    /// 1.0=整日休养（与旧 <c>resting:true</c> 逐比特等价）、0.0=整日在勤、中间值线性插值恢复/恶化两轴。
    /// </param>
    /// <param name="bedFraction">
    /// [批次21·impl-bedrest] 本昼夜的**睡床占比** 0..1（默认 null → 回落到 <paramref name="restedInBed"/> 布尔，零回归）：
    /// 由 <see cref="RestLedger.BedFraction"/> 得出，线性折算 <see cref="BedSleepHealBonusPct"/> 加算百分点。
    /// 睡地铺只吃 <paramref name="restFraction"/> 那一轴、不吃本轴——**床是要造的**（见 <see cref="BedRegistry"/>）。
    /// </param>
    public HealthTickResult TickDay(IRandomSource rng, bool resting, bool restedInBed = false, double infectionChanceMultiplier = 1.0, double extraHealBonusPct = 0.0, double healSpeedMultiplier = 1.0,
        double? restFraction = null, double? bedFraction = null)
    {
        if (IsDead)
        {
            return new HealthTickResult();
        }

        // [批次21·impl-bedrest] 休养/睡床由「整日布尔」推广为「按相位累计的占比」（见 BedrestLogic 文件头）：
        // 布尔恰是占比 ∈{0,1} 的特例 —— 传 null 时回落到布尔，且下面三个插值在端点上与旧式逐比特等价，
        // 故既有结算**零回归**。占比 <1 的中间态是新增能力：白天睡了半天的人，就吃半天的加成。
        double rf = Math.Clamp(restFraction ?? (resting ? 1.0 : 0.0), 0.0, 1.0);
        double bf = Math.Clamp(bedFraction ?? (restedInBed ? 1.0 : 0.0), 0.0, 1.0);

        // 端点校验：rf=1 → RestHealBonus(1.5)；rf=0 → 1.0。与旧 `(resting ? RestHealBonus : 1.0)` 同值。
        double restHealMult = 1.0 + (RestHealBonus - 1.0) * rf;
        // 端点校验：rf=1 → RestWorsenFactor(0.5)；rf=0 → 1.0。与旧 `(resting ? RestWorsenFactor : 1.0)` 同值。
        double restWorsenMult = 1.0 - (1.0 - RestWorsenFactor) * rf;
        // 端点校验：bf=1 → BedSleepHealBonusPct(10)；bf=0 → 0。与旧 `(restedInBed ? BedSleepHealBonusPct : 0.0)` 同值。
        double bedBonusPct = BedSleepHealBonusPct * bf;

        var events = new List<HealthTickEvent>();
        var maimed = new List<string>();
        var toRemove = new List<HealthCondition>();
        var newConditions = new List<HealthCondition>();
        bool anyDeath = false;

        // 快照遍历：本昼夜新生感染不在本轮再演变。
        foreach (HealthCondition c in _conditions.ToList())
        {
            double before = c.Severity;
            ConditionOutcome outcome = ConditionOutcome.None;
            bool contracted = false; // 该条自身不是新生（新生条目单独 event）

            switch (c.Type)
            {
                case HealthConditionType.Bleeding:
                    if (c.IsOperated)
                    {
                        // 已手术：按恢复效率愈合（睡床 +BedSleepHealBonusPct 与玫瑰果茶 +extraHealBonusPct 同族加算，非乘算）。
                        // healSpeedMultiplier（山姆 L3 光环 ×1.03）是**另一轴**：最终愈合量的乘子，与上面的加算点数正交。
                        double effPct = c.RecoveryEfficiency + bedBonusPct + Math.Max(0.0, extraHealBonusPct);
                        double heal = BleedHealPerDay * (effPct / 100.0) * restHealMult * Math.Max(0.0, healSpeedMultiplier);
                        c.AddSeverity(-heal);
                    }
                    else if (c.SelfHealing && c.DaysElapsed >= InfectionWindowDays)
                    {
                        // 很小的伤：新鲜期结束 → 自行闭合（无需手术）。若期间已中招，感染作为独立病状继续，此处只闭合伤口本身。
                        c.SetSeverity(0.0);
                    }
                    else
                    {
                        c.AddSeverity(BleedWorsenPerDay);
                        // 非致命失血伤口（小锐器/咬伤类）：severity 封顶、永不失血死，只作感染源。
                        if (!c.LethalBleed && c.Severity > MinorBleedSeverityCap)
                        {
                            c.SetSeverity(MinorBleedSeverityCap);
                        }
                    }

                    // 统一感染窗：伤口在**新鲜期内(DaysElapsed<窗口)** 且尚未愈合闭口时按几率感染；
                    // 几率随 出血严重度 × 伤口大小系数(越小越低) 缩放，已止血再折减（止血≠无菌）。
                    // 窗口过后不再新感染 → 放任小伤不累积到 100% 坏疽，成为有限概率赌局。
                    if (c.DaysElapsed < InfectionWindowDays && c.Severity >= WoundClosedThreshold)
                    {
                        double chance = InfectionBaseChance * c.Severity * c.InfectionProneness * (c.IsOperated ? OperatedInfectionFactor : 1.0) * Math.Max(0.0, infectionChanceMultiplier);
                        if (TryContractInfection(c, chance, rng, newConditions))
                        {
                            contracted = true;
                        }
                    }

                    // 仅未手术的致命失血伤口封顶致死；非致命伤口的终局只能来自感染（坏疽/败血症）。
                    if (!c.IsOperated && c.LethalBleed && c.Severity >= 1.0)
                    {
                        outcome = ConditionOutcome.Death; // 失血过多
                    }
                    break;

                case HealthConditionType.Infection:
                    // [SPEC-B14/终稿] 感染进度/治疗进度的推进不在此日结算——移到相位级 <see cref="AdvanceInfectionRace"/>（每昼夜多次，dt 细分），
                    // 以免整日粒度吃掉草药膏的胜利窗口。TickDay 只保留"新伤口按几率新生感染(TryContractInfection)"与"截肢连带清感染"。此处对既有感染无操作。
                    break;

                case HealthConditionType.Fracture:
                    if (c.IsOperated)
                    {
                        // 睡床 +BedSleepHealBonusPct 与玫瑰果茶 +extraHealBonusPct 同族加算（非乘算）；山姆 L3 光环走 healSpeedMultiplier 乘子轴。
                        double effPct = c.RecoveryEfficiency + bedBonusPct + Math.Max(0.0, extraHealBonusPct);
                        double heal = FractureHealPerDay * (effPct / 100.0) * restHealMult * Math.Max(0.0, healSpeedMultiplier);
                        c.AddSeverity(-heal);
                    }
                    else
                    {
                        c.AddSeverity(FractureMalunionPerDay); // 未手术 → 畸形化
                        if (c.Severity >= 1.0)
                        {
                            outcome = c.OnLimb ? ConditionOutcome.Maim : ConditionOutcome.None; // 畸形愈合致残（肢体）
                        }
                    }
                    break;

                case HealthConditionType.Disease:
                    if (!c.Tended)
                    {
                        double w = DiseaseWorsenPerDay * restWorsenMult;
                        c.AddSeverity(w);
                    }
                    if (c.Severity >= 1.0)
                    {
                        outcome = ConditionOutcome.Death;
                    }
                    break;
            }

            c.AdvanceDay();
            c.ClearTended();

            events.Add(new HealthTickEvent
            {
                Type = c.Type,
                BodyPart = c.BodyPart,
                SeverityBefore = before,
                SeverityAfter = c.Severity,
                Outcome = outcome,
                ContractedInfection = contracted,
            });

            switch (outcome)
            {
                case ConditionOutcome.Death:
                    anyDeath = true;
                    break;
                case ConditionOutcome.Maim:
                    if (c.BodyPart != null)
                    {
                        maimed.Add(c.BodyPart);
                    }
                    toRemove.Add(c); // 肢体已失，该病状消解
                    break;
            }

            // 完全愈合（severity 归 0，如手术后养好的出血/骨折、或微小伤新鲜期自愈）→ 移除。
            // **感染除外**：其 severity=感染/死亡进度（新鲜感染从 0 起算，非"愈合"），移除由 AdvanceInfectionRace（治愈/坏疽）负责，这里不得按 ≤0 误删。
            // Body 侧止血由接入波（Pawn.AdvanceHealthDay）按"该部位已无活跃出血条目"统一 StopBleed 同步，此处不额外出清单。
            if (c.Severity <= 0 && outcome == ConditionOutcome.None && c.Type != HealthConditionType.Infection)
            {
                toRemove.Add(c);
            }
        }

        // 截肢连带：某部位坏疽截肢后，该部位上其余病状（残留骨折/已止血伤口/同部位新生感染）一并消解——
        // **但仍在活动失血的致命伤例外**：断肢残端继续失血 → 致命失血伤口终究失血致死（保"未处置致命伤必死"，感染不得靠截肢救活）。
        if (maimed.Count > 0)
        {
            bool KeepAsStumpBleed(HealthCondition h) =>
                h.Type == HealthConditionType.Bleeding && h.LethalBleed && !h.IsOperated;

            foreach (HealthCondition cond in _conditions)
            {
                if (cond.BodyPart != null && maimed.Contains(cond.BodyPart) && !KeepAsStumpBleed(cond) && !toRemove.Contains(cond))
                {
                    toRemove.Add(cond);
                }
            }
            newConditions.RemoveAll(n => n.BodyPart != null && maimed.Contains(n.BodyPart) && !KeepAsStumpBleed(n));
        }

        foreach (HealthCondition r in toRemove)
        {
            _conditions.Remove(r);
        }
        _conditions.AddRange(newConditions);

        // 新生感染各补一条 event（供 UI 提示"伤口感染了"）。
        foreach (HealthCondition ni in newConditions)
        {
            events.Add(new HealthTickEvent
            {
                Type = ni.Type,
                BodyPart = ni.BodyPart,
                SeverityBefore = 0,
                SeverityAfter = ni.Severity,
                Outcome = ConditionOutcome.None,
                ContractedInfection = true,
            });
        }

        if (anyDeath)
        {
            IsDead = true;
        }

        return new HealthTickResult
        {
            Events = events,
            AnyDeath = anyDeath,
            MaimedParts = maimed,
        };
    }
}

/// <summary>TickDay 里单条病状本昼夜的演变事件（供 UI/战报读取）。</summary>
public sealed class HealthTickEvent
{
    public HealthConditionType Type { get; init; }
    public string? BodyPart { get; init; }
    public double SeverityBefore { get; init; }
    public double SeverityAfter { get; init; }
    public ConditionOutcome Outcome { get; init; }
    /// <summary>本事件是否为"新感染"（开放伤口本昼夜感染而新生的感染条目）。</summary>
    public bool ContractedInfection { get; init; }
}

/// <summary>[SPEC-B14/终稿] 一个时间片感染竞速推进的结果（供运行时施加截肢/死亡/清疗程）。</summary>
public readonly record struct InfectionRaceResult
{
    /// <summary>本时间片终态：None（继续竞速）/ Maim（坏疽截肢，见 <see cref="MaimedPart"/>）/ Death（败血症致死）。</summary>
    public ConditionOutcome Outcome { get; init; }
    /// <summary>治疗进度先到顶、感染被清除（胜局）。</summary>
    public bool Cured { get; init; }
    /// <summary>坏疽截肢的部位名（Outcome=Maim 时非空）。</summary>
    public string? MaimedPart { get; init; }
}

/// <summary>一次 TickDay 的汇总。</summary>
public sealed class HealthTickResult
{
    public IReadOnlyList<HealthTickEvent> Events { get; init; } = Array.Empty<HealthTickEvent>();
    /// <summary>本昼夜是否有病状致死。</summary>
    public bool AnyDeath { get; init; }
    /// <summary>本昼夜被致残(封顶截肢)的部位名（供接入波对 Body 执行截肢）。</summary>
    public IReadOnlyList<string> MaimedParts { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 战斗态 → 伤病态的**只读**映射（不改 <see cref="Body"/>）：把战斗引擎产出的出血伤口/骨折部位
/// 播种为伤病集，供接入波在战后建档。<see cref="HealthCondition.OnLimb"/> 由部位类别(<see cref="BodyPartCategory.Limb"/>)判定。
/// </summary>
public static class HealthMapping
{
    /// <summary>
    /// 从一具 <see cref="Body"/> 的当前出血伤口(<see cref="Body.BleedingWounds"/>)与骨折部位(<see cref="Body.FracturedParts"/>)
    /// 播种一份伤病集（只读 Body，不改其状态）。初始严重度 draft。
    /// </summary>
    /// <param name="bleedingSeverity">
    /// 🔴 [T58] **已退役为兜底值**：出血的初始严重度现在**由该部位的出血【等级】决定**
    /// （<see cref="BleedModel.ConditionSeverityOf"/>：小 0.25 / 中 0.45 / **大 0.70**），
    /// 而**不再是所有伤口一个平摊的 0.35**。
    /// <para>
    /// 这就是用户那句「防止过多的伤口浪费手术时间」在手术侧的落地：
    /// **手术台数按【部位】走（本来就是——每部位一条 Bleeding），而难度/剩余抢救时间按【等级】走。**
    /// 三个小口子在 <see cref="Body"/> 里已经合并成一个大口子 ⇒ 这里出的是**一台**手术，
    /// 而且它是一台**大流血**的手术（0.70 ⇒ 不治只剩 3 昼夜），不是三台各自轻飘飘的小手术。
    /// </para>
    /// 只有当 <see cref="Body"/> 里查不到等级（理论上不会发生）时才回落到本参数。
    /// </param>
    public static HealthConditionSet SeedFromBody(Body body, double bleedingSeverity = 0.35, double fractureSeverity = 0.6)
    {
        var set = new HealthConditionSet();
        foreach (string part in body.BleedingWounds)
        {
            // [T58] 严重度 = 该部位那处出血的【等级】（合并后的结果）。
            double severity = body.BleedSeverityOn(part) is BleedModel.BleedSeverity lvl
                ? BleedModel.ConditionSeverityOf(lvl)
                : bleedingSeverity;

            // 三层梯度：很小的伤(微小部位/擦伤级)→自愈；中等非致命小伤(手/脚)→需手术不自愈；大部位→致命失血。
            bool selfHealing = IsMicroBleedPart(body, part) || severity <= HealthConditionSet.AbrasionSeverityThreshold;
            bool lethal = !selfHealing && IsLethalBleedPart(body, part); // 自愈伤绝不致命失血
            set.Add(new HealthCondition(
                HealthConditionType.Bleeding, severity, part, IsLimb(body, part),
                lethal, InfectionPronenessOf(body, part), selfHealing));
        }
        foreach (string part in body.FracturedParts)
        {
            set.Add(new HealthCondition(HealthConditionType.Fracture, fractureSeverity, part, IsLimb(body, part)));
        }
        return set;
    }

    /// <summary>部位是否为肢体（用于终态致残/致死分流）；部位表查不到按非肢体。</summary>
    private static bool IsLimb(Body body, string part)
        => body.Parts.TryGetValue(part, out BodyPart? p) && p.Category == BodyPartCategory.Limb;

    /// <summary>
    /// 该部位的出血是否为**致命失血**（拖久失血致死）：大部位（躯干/头/颈/上臂/大腿）= 致命失血；
    /// 小/远端部位（手/脚/指/趾/眼/面/耳）= 只溃烂感染、非致命失血（对应"小锐器/咬伤类"口径）。
    /// <para>
    /// 分级本身已下沉到引擎 <see cref="BleedModel"/>，**战斗内失血与战后建档读同一个函数** ——
    /// 此前两边各写一份，导致"手指划伤在战斗中能把人流血流死、战后却算不致命"的自相矛盾。
    /// </para>
    /// </summary>
    private static bool IsLethalBleedPart(Body body, string part) => BleedModel.IsLethalPart(body, part);

    /// <summary>
    /// 该部位出血伤口的**感染倾向系数**（越小的伤越低，draft）：大部位(躯干/头/颈/上臂/大腿) 1.0、
    /// 手/脚 0.4、指/趾/眼/面/耳 0.2。喂进每昼夜感染几率，使小伤"值得赌要不要省抗生素"。部位表查不到按 1.0（从狠）。
    /// </summary>
    private static double InfectionPronenessOf(Body body, string part)
    {
        if (!body.Parts.TryGetValue(part, out BodyPart? p))
        {
            return 1.0;
        }
        return p.Region switch
        {
            BodyRegion.Hand or BodyRegion.Foot => 0.4,
            BodyRegion.Finger or BodyRegion.Toe or BodyRegion.Eye or BodyRegion.Face or BodyRegion.Ear => 0.2,
            _ => 1.0, // 躯干/头/颈/上臂/大腿 等大部位
        };
    }

    /// <summary>是否为"微小部位"（指/趾/眼/面/耳）：其出血伤口属"很小的伤"，可自行闭合。
    /// 同样读引擎 <see cref="BleedModel"/> 的分级，与战斗内失血口径一致。</summary>
    private static bool IsMicroBleedPart(Body body, string part) => BleedModel.IsMicroPart(body, part);
}

/// <summary>
/// 【T53】休养自然回血的**规则层**（纯函数，无 Godot 依赖 ⇒ Link 进单测）。
/// 用户拍板：「**补——休养自然回血**」（不做输血/血袋；医院「血库」保留为叙事，不接机制）。
///
/// <para>
/// 🔴 <b>它补的是一个真空</b>：此前**实机没有任何回血手段** —— <c>Body.Blood</c> 只有 <c>LoseBlood</c>（只减）
/// 与 <c>SetBloodMax</c>（回满）两条路径，而 <c>SetBloodMax</c> 在 Godot 层**一次都没被调用**
/// ⇒ 幸存者的储血整个战役单调递减、无恢复路径。手术只"止住伤口"，**流掉的血不会回来**。
/// </para>
/// <para>
/// 规则写在这里（而不是 <c>Pawn</c> 里）是刻意的：<c>Pawn</c> 是 Godot 节点、**无法单测**，
/// 把判据留在它身体里就等于没有护栏（项目长期教训：纯逻辑绿 ≠ 功能生效）。<c>Pawn</c> 只当三行调用方。
/// </para>
/// </summary>
public static class BloodRecovery
{
    /// <summary>
    /// 本昼夜应回复的血量。
    ///
    /// <para>量 = <see cref="BleedModel.BloodRegenPerRestDay"/>(10) × 休养占比 × 睡床加成
    /// （复用既有 <see cref="HealthConditionSet.BedSleepHealBonusPct"/>=10 个百分点，**加算、同族**，不另起炉灶）。
    /// 70 储血 ÷ 10 = **7 昼夜从零回满**，与「骨折愈合 7 昼夜」同量级（占床、不能干活、不能站岗）。</para>
    /// </summary>
    /// <param name="restFraction">本昼夜休养占比 0..1（来自 <c>RestLedger</c>）。0 = 没休养 ⇒ 不回血。</param>
    /// <param name="bedFraction">本昼夜睡床占比 0..1。地铺不吃这一轴，床要造。</param>
    /// <param name="hasOpenWound">
    /// 身上是否还有**未止住**的出血伤口（<c>Body.BleedingWoundCount &gt; 0</c>）。
    /// 🔴 <b>还在流就不回血</b>：边流边补是自欺欺人，也会架空用户「任何时候只要伤口没被手术治疗就会流血」这条规则。
    /// ⇒ **必须先手术缝合，才谈得上养回来。**
    /// </param>
    public static double PerRestDay(double restFraction, double bedFraction, bool hasOpenWound)
    {
        if (hasOpenWound)
        {
            return 0; // 口子还开着：先去做手术
        }

        double rest = Math.Clamp(restFraction, 0, 1);
        if (rest <= 0)
        {
            return 0; // 没休养就没有回血——干活/站岗的人不回血
        }

        double bedBonus = 1.0 + (HealthConditionSet.BedSleepHealBonusPct / 100.0) * Math.Clamp(bedFraction, 0, 1);
        return BleedModel.BloodRegenPerRestDay * rest * bedBonus;
    }
}
