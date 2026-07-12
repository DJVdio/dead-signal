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
    internal void AdvanceDay() => DaysElapsed++;
}

/// <summary>一条药品的治疗语义（物品数据在 <see cref="Materials"/>，此处只描述"治哪种病状 + 药效强度"）。仅覆盖**感染/疾病**（流血/骨折走手术，不在此）。</summary>
/// <param name="MaterialKey">对应 <see cref="MaterialDef.Key"/>（搜刮入库/治疗消耗按此扣减）。</param>
/// <param name="Treats">该药主治的病状类别（Infection/Disease）。</param>
/// <param name="Potency">药效基数 0..1（乘施治者医疗技能系数得单次降severity量）。</param>
public readonly record struct Medicine(string MaterialKey, HealthConditionType Treats, double Potency);

/// <summary>
/// 药品目录（**draft**）：材料标识名 → 治疗语义。**只含感染/疾病用药**（抗生素/成药）；
/// 流血/骨折不吃药、只做手术（见 <see cref="SurgeryCatalog"/>）。供 <see cref="HealthConditionSet.TreatIllness"/> 消费。
/// </summary>
public static class MedicineCatalog
{
    private static readonly IReadOnlyDictionary<string, Medicine> _byKey = new[]
    {
        // 抗生素：对抗感染，需连续几天见效。
        new Medicine("antibiotics", HealthConditionType.Infection, 0.5),
        // 成药：缓解泛化疾病。
        new Medicine("medicine", HealthConditionType.Disease, 0.6),
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
///   · 流血：绷带 +15 / 针线 +15（可叠加=同用 +30）；或急救包 +60（独占）。
///   · 骨折：夹板 +25；或急救包 +60（独占）。
/// </summary>
public static class SurgeryCatalog
{
    private static readonly IReadOnlyDictionary<string, SurgerySupply> _byKey = new[]
    {
        new SurgerySupply("bandage", 15, false, new[] { HealthConditionType.Bleeding }),
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

/// <summary>
/// 医疗书籍 → 手术治疗点注册表（**draft**，点值不对玩家展示）。
/// 施术者**读过**的医疗书各按其点值累加成 surgeonBookBonus（靠书籍知识，与 Medical 技能无关、互不冲突）。
/// 接入波用"施术者 ReadBookSet ∩ 本表"求和喂给 <see cref="HealthConditionSet.PerformSurgery"/>；本模型只出点、不耦合 ReadBookSet/Pawn。
/// </summary>
public static class MedicalBookPoints
{
    // book id（对齐 BookData.Id）→ 手术治疗点（draft，默认 +5）。UI 只显示"略增医学学识"之类模糊描述，不展示具体点数。
    private static readonly IReadOnlyDictionary<string, int> _byBookId = new Dictionary<string, int>
    {
        ["wilderness_survival_guide"] = 5, // 《野外生存指南》
    };

    /// <summary>该书的手术治疗点；非医疗书返回 0。</summary>
    public static int For(string bookId)
        => bookId != null && _byBookId.TryGetValue(bookId, out int p) ? p : 0;

    /// <summary>该书是否为医疗书（计入手术治疗点）。</summary>
    public static bool IsMedicalBook(string bookId) => bookId != null && _byBookId.ContainsKey(bookId);

    /// <summary>一组已读书 id 的手术治疗点合计（接入波传施术者 ReadBookSet 的书 id 即可）。</summary>
    public static int SumFor(IEnumerable<string> readBookIds)
        => readBookIds == null ? 0 : readBookIds.Sum(For);
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
    private const double InfectionInitialSeverity = 0.22;// 新感染初始严重度
    private const double InfectionWorsenPerDay = 0.20;   // 感染逐日恶化 → 未治约第 4 昼夜封顶坏疽/败血症
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

    /// <summary>单次药效固定基数（draft，仅感染/疾病用药）：通用医疗技能已删，疗效不再随人变，取一个"照护得当"的固定系数，用 Sim 校准。</summary>
    private const double TreatmentPotencyFactor = 0.8;

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
    /// <param name="surgeonBookBonus">施术者已读医疗书加点合计（调用方从其 ReadBookSet ∩ <see cref="MedicalBookPoints"/> 求和，靠书籍知识、非 Medical 技能）。</param>
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
        int? surgeryBasePoints = null)
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
        int rawPoints = (surgeryBasePoints ?? SurgeryBasePoints) + (onBed ? BedBonusPoints : 0) + supplies.Sum(s => s.Points) + Math.Max(0, surgeonBookBonus);
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
    /// 用药治疗一处**感染/疾病**（流血/骨折不吃药、走 <see cref="PerformSurgery"/>）：给定病状、药品，
    /// 降 severity，归 0 → 治愈并移除。药不对症或病状非感染/疾病 → <see cref="TreatmentStatus.NoEffect"/>。
    /// 疗效取固定基数（<see cref="TreatmentPotencyFactor"/>，通用医疗技能已删）；<see cref="TickDay"/> resting 减缓恶化。
    /// </summary>
    public TreatmentResult TreatIllness(HealthCondition condition, Medicine? medicine)
    {
        double before = condition.Severity;

        bool isIllness = condition.Type == HealthConditionType.Infection || condition.Type == HealthConditionType.Disease;
        bool matches = isIllness && medicine.HasValue && medicine.Value.Treats == condition.Type;
        if (!matches)
        {
            return new TreatmentResult { Status = TreatmentStatus.NoEffect, SeverityBefore = before, SeverityAfter = before, Removed = false };
        }

        condition.MarkTended();
        condition.AddSeverity(-medicine!.Value.Potency * TreatmentPotencyFactor);
        if (condition.Severity <= 0)
        {
            _conditions.Remove(condition);
            return new TreatmentResult { Status = TreatmentStatus.Cured, SeverityBefore = before, SeverityAfter = 0, Removed = true };
        }
        return new TreatmentResult { Status = TreatmentStatus.Applied, SeverityBefore = before, SeverityAfter = condition.Severity, Removed = false };
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
        bool alreadyInfected = _conditions.Concat(newConditions)
            .Any(x => x.Type == HealthConditionType.Infection && x.BodyPart == wound.BodyPart);
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
    public HealthTickResult TickDay(IRandomSource rng, bool resting, bool restedInBed = false, double infectionChanceMultiplier = 1.0)
    {
        if (IsDead)
        {
            return new HealthTickResult();
        }

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
                        // 已手术：按恢复效率愈合（睡床加算 +BedSleepHealBonusPct 个百分点，加算非乘算）。
                        double effPct = c.RecoveryEfficiency + (restedInBed ? BedSleepHealBonusPct : 0.0);
                        double heal = BleedHealPerDay * (effPct / 100.0) * (resting ? RestHealBonus : 1.0);
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
                    if (!c.Tended)
                    {
                        double w = InfectionWorsenPerDay * (resting ? RestWorsenFactor : 1.0);
                        c.AddSeverity(w);
                    }
                    if (c.Severity >= 1.0)
                    {
                        outcome = c.OnLimb ? ConditionOutcome.Maim : ConditionOutcome.Death; // 坏疽截肢 / 败血症
                    }
                    break;

                case HealthConditionType.Fracture:
                    if (c.IsOperated)
                    {
                        // 睡床加算 +BedSleepHealBonusPct 个百分点（加算非乘算）。
                        double effPct = c.RecoveryEfficiency + (restedInBed ? BedSleepHealBonusPct : 0.0);
                        double heal = FractureHealPerDay * (effPct / 100.0) * (resting ? RestHealBonus : 1.0);
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
                        double w = DiseaseWorsenPerDay * (resting ? RestWorsenFactor : 1.0);
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
            // Body 侧止血由接入波（Pawn.AdvanceHealthDay）按"该部位已无活跃出血条目"统一 StopBleed 同步，此处不额外出清单。
            if (c.Severity <= 0 && outcome == ConditionOutcome.None)
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
    public static HealthConditionSet SeedFromBody(Body body, double bleedingSeverity = 0.35, double fractureSeverity = 0.6)
    {
        var set = new HealthConditionSet();
        foreach (string part in body.BleedingWounds)
        {
            // 三层梯度：很小的伤(微小部位/擦伤级)→自愈；中等非致命小伤(手/脚)→需手术不自愈；大部位→致命失血。
            bool selfHealing = IsMicroBleedPart(body, part) || bleedingSeverity <= HealthConditionSet.AbrasionSeverityThreshold;
            bool lethal = !selfHealing && IsLethalBleedPart(body, part); // 自愈伤绝不致命失血
            set.Add(new HealthCondition(
                HealthConditionType.Bleeding, bleedingSeverity, part, IsLimb(body, part),
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
    /// 该部位的出血是否为**致命失血**（拖久失血致死）。draft 启发式：大部位（躯干/头/上臂/大腿）= 致命失血；
    /// 小/远端部位（手/脚/指/趾/眼/面/耳）= 只溃烂感染、非致命失血（对应"小锐器/咬伤类"口径）。部位表查不到按致命（从狠）。
    /// 注：引擎不逐伤口记武器类型，此处以部位尺寸作可判定代理，语义为"拟定待调"。
    /// </summary>
    private static bool IsLethalBleedPart(Body body, string part)
    {
        if (!body.Parts.TryGetValue(part, out BodyPart? p))
        {
            return true; // 未知部位从狠：按致命失血
        }
        return p.Region is BodyRegion.Torso or BodyRegion.Head or BodyRegion.Neck or BodyRegion.Arm or BodyRegion.Leg;
    }

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

    /// <summary>是否为"微小部位"（指/趾/眼/面/耳）：其出血伤口属"很小的伤"，可自行闭合。部位表查不到按非微小。</summary>
    private static bool IsMicroBleedPart(Body body, string part)
        => body.Parts.TryGetValue(part, out BodyPart? p)
           && p.Region is BodyRegion.Finger or BodyRegion.Toe or BodyRegion.Eye or BodyRegion.Face or BodyRegion.Ear;
}
