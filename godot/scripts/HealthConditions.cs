using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs / SkillSet.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 只 **只读引用** 战斗引擎（DeadSignal.Combat）的出血/骨折状态语义，绝不改战斗引擎定稿。
//
// 承载"伤病随时间恶化 + 治疗"的全部规则（模型层，不接 Body/CampMain 每帧推进——接入后续波）：
//   · 一个幸存者持一份 HealthConditionSet（若干病状 HealthCondition，各带严重度 severity 0..1）。
//   · 每昼夜 TickDay：未处置病状按规则恶化——出血拖久失血过多致死；开放伤口按几率感染；
//     感染恶化→肢体坏疽截肢(致残)/躯干败血症(致死)；骨折未固定→畸形愈合致残。
//   · Treat：给定(病状、药品、施治者医疗技能)→ 降严重度/止血/固定；技能越高越有效；休养(TickDay resting)加速。
//   · 药品语义（治哪种病状 + 药效）在 MedicineCatalog，物品数据在 Materials.cs（MaterialCategory.Medical）。
//   · HealthMapping.SeedFromBody：从战斗态只读播种伤病集（出血伤口→Bleeding、骨折→Fracture）。
// 数值全部"拟定待调 draft"，用于走通规则形态；具体值用 Sim 校准、结局狠辣是有意为之。

/// <summary>病状类别（可扩）。战斗产出 <see cref="Bleeding"/>/<see cref="Fracture"/>；<see cref="Infection"/> 由开放伤口衍生；<see cref="Disease"/> 预留环境/瘟疫来源。</summary>
public enum HealthConditionType
{
    /// <summary>外伤出血（开放伤口）：未处理逐日加重，封顶=失血过多致死；同时是感染来源。</summary>
    Bleeding,

    /// <summary>伤口感染：逐日恶化，封顶=肢体坏疽截肢(致残)/非肢体败血症(致死)。</summary>
    Infection,

    /// <summary>骨折：需夹板固定 + 时间愈合；未固定逐日畸形化，封顶=该肢永久致残（不致死）。</summary>
    Fracture,

    /// <summary>疾病：泛化的病症（发热/痢疾等），逐日恶化，封顶=致死。预留环境来源。</summary>
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

/// <summary>一次治疗后的状态标签。</summary>
public enum TreatmentStatus
{
    /// <summary>药不对症/无从下手，未产生疗效。</summary>
    NoEffect,

    /// <summary>已施治、严重度下降但未清除。</summary>
    Applied,

    /// <summary>出血已完全止住（病状随即移除）。</summary>
    Staunched,

    /// <summary>感染/疾病已治愈清除。</summary>
    Cured,

    /// <summary>骨折已用夹板固定（进入可愈合状态）。</summary>
    Immobilized,
}

/// <summary>
/// 单条伤病（可变）：类别 + 部位 + 严重度(0..1) + 是否位于肢体（决定终态是致残还是致死） +
/// 骨折固定标记 + 本昼夜是否已处置(压制当日恶化) + 已历天数。数值 draft。
/// </summary>
public sealed class HealthCondition
{
    /// <param name="severity">初始严重度，clamp 到 [0,1]。</param>
    /// <param name="bodyPart">部位名（对齐 <see cref="HumanBody"/> 部位名；系统性疾病可 null）。</param>
    /// <param name="onLimb">是否位于肢体：感染/骨折封顶时肢体→致残、非肢体→致死/无后果。</param>
    public HealthCondition(HealthConditionType type, double severity, string? bodyPart = null, bool onLimb = false)
    {
        Type = type;
        BodyPart = bodyPart;
        OnLimb = onLimb;
        Severity = Math.Clamp(severity, 0.0, 1.0);
    }

    public HealthConditionType Type { get; }

    /// <summary>部位名（可空：系统性疾病无具体部位）。</summary>
    public string? BodyPart { get; }

    /// <summary>是否位于肢体（感染/骨折终态分流：肢体致残、非肢体致死/无后果）。</summary>
    public bool OnLimb { get; }

    /// <summary>严重度 0..1（1=封顶触发终态）。</summary>
    public double Severity { get; private set; }

    /// <summary>骨折是否已用夹板固定（唯有固定才随时间愈合）。</summary>
    public bool Immobilized { get; private set; }

    /// <summary>本昼夜是否已处置（Treat 置真，TickDay 结算后清零）：压制当日自然恶化。</summary>
    public bool Tended { get; private set; }

    /// <summary>已历昼夜数（供 UI/叙事）。</summary>
    public int DaysElapsed { get; private set; }

    // ---- 以下 setter 仅供 HealthConditionSet 内部结算调用 ----
    internal void SetSeverity(double v) => Severity = Math.Clamp(v, 0.0, 1.0);
    internal void AddSeverity(double d) => SetSeverity(Severity + d);
    internal void MarkTended() => Tended = true;
    internal void ClearTended() => Tended = false;
    internal void Splint() => Immobilized = true;
    internal void AdvanceDay() => DaysElapsed++;
}

/// <summary>一条药品的治疗语义（物品数据在 <see cref="Materials"/>，此处只描述"治哪种病状 + 药效强度"）。</summary>
/// <param name="MaterialKey">对应 <see cref="MaterialDef.Key"/>（搜刮入库/治疗消耗按此扣减）。</param>
/// <param name="Treats">该药主治的病状类别。</param>
/// <param name="Potency">药效基数 0..1（乘施治者医疗技能系数得单次降severity量；骨折夹板走固定、忽略此值）。</param>
public readonly record struct Medicine(string MaterialKey, HealthConditionType Treats, double Potency);

/// <summary>
/// 药品目录（**draft**）：材料标识名 → 治疗语义。与 <see cref="Materials"/> 里 <see cref="MaterialCategory.Medical"/>
/// 条目一一对应（物品数据在那边，治疗规则在这边），供 <see cref="HealthConditionSet.Treat"/> 消费。
/// </summary>
public static class MedicineCatalog
{
    private static readonly IReadOnlyDictionary<string, Medicine> _byKey = new[]
    {
        // 绷带：基础止血/包扎，单次止血药效中等。
        new Medicine("bandage", HealthConditionType.Bleeding, 0.5),
        // 止血药：强效止血粉，一次多能封住伤口。
        new Medicine("styptic", HealthConditionType.Bleeding, 1.0),
        // 抗生素：对抗感染，需连续几天见效。
        new Medicine("antibiotics", HealthConditionType.Infection, 0.5),
        // 夹板：固定断骨（potency 语义无效，固定=二元）。
        new Medicine("splint", HealthConditionType.Fracture, 1.0),
        // 成药：缓解泛化疾病。
        new Medicine("medicine", HealthConditionType.Disease, 0.6),
    }.ToDictionary(m => m.MaterialKey);

    /// <summary>按材料标识名查药品治疗语义；非药品返回 null。</summary>
    public static Medicine? For(string materialKey)
        => materialKey != null && _byKey.TryGetValue(materialKey, out Medicine m) ? m : null;

    /// <summary>该材料是否为药品。</summary>
    public static bool IsMedicine(string materialKey) => For(materialKey) != null;
}

/// <summary>一次治疗结果。</summary>
public sealed class TreatmentResult
{
    public TreatmentStatus Status { get; init; }
    public double SeverityBefore { get; init; }
    public double SeverityAfter { get; init; }
    /// <summary>该病状是否因本次治疗被移除（止血/治愈）。</summary>
    public bool Removed { get; init; }
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
/// 一个幸存者的伤病集（纯逻辑）：持若干 <see cref="HealthCondition"/>，
/// <see cref="TickDay"/> 推进恶化、<see cref="Treat"/> 施治。到致死终态置 <see cref="IsDead"/>。
/// 阈值/流速皆 draft，用 Sim 校准。
/// </summary>
public sealed class HealthConditionSet
{
    // ---- 恶化速率（每昼夜，draft）----
    private const double BleedWorsenPerDay = 0.15;       // 未处理出血逐日加重
    private const double InfectionBaseChance = 0.30;     // 开放伤口感染几率基数（× 出血严重度）
    private const double InfectionInitialSeverity = 0.15;// 新感染初始严重度
    private const double InfectionWorsenPerDay = 0.14;   // 感染逐日恶化
    private const double DiseaseWorsenPerDay = 0.12;     // 疾病逐日恶化
    private const double RestWorsenFactor = 0.5;         // 休养减缓感染/疾病恶化系数
    private const double FractureHealPerDay = 0.12;      // 已固定骨折逐日愈合
    private const double RestHealBonus = 1.5;            // 休养加速愈合系数
    private const double FractureMalunionPerDay = 0.05;  // 未固定骨折逐日畸形化（封顶致残）

    // ---- 治疗（draft）----
    private const double HandStaunchPotency = 0.25;      // 徒手按压止血药效（无药时）

    private readonly List<HealthCondition> _conditions = new();

    public IReadOnlyList<HealthCondition> Conditions => _conditions;

    /// <summary>是否已因某病状致死（终态）。</summary>
    public bool IsDead { get; private set; }

    /// <summary>是否含某类病状。</summary>
    public bool Has(HealthConditionType type) => _conditions.Any(c => c.Type == type);

    /// <summary>登记一条病状（战斗产出/环境衍生皆经此入库）。</summary>
    public void Add(HealthCondition condition) => _conditions.Add(condition);

    /// <summary>施治者医疗技能 → 单次疗效系数（draft）：无技能也能救急，但事倍功半。</summary>
    private static double SkillFactor(SkillLevel level) => level switch
    {
        SkillLevel.Expert => 1.0,
        SkillLevel.Adept => 0.8,
        SkillLevel.Novice => 0.65,
        _ => 0.45, // 未掌握：勉强处理
    };

    /// <summary>
    /// 治疗判定（确定性，无 rng；成功率以"疗效系数"表达）：给定病状、药品(可空=徒手)、施治者医疗技能，
    /// 施加疗效并按结果消解病状。药不对症 → <see cref="TreatmentStatus.NoEffect"/>。
    /// 出血：降severity，归 0 → 完全止血并移除；感染/疾病：降severity，归 0 → 治愈并移除；
    /// 骨折：夹板固定(置 Immobilized)，进入可愈合态。医疗技能越高单次越有效；<see cref="TickDay"/> resting 加速。
    /// </summary>
    public TreatmentResult Treat(HealthCondition condition, Medicine? medicine, SkillLevel medicalLevel)
    {
        double before = condition.Severity;
        double factor = SkillFactor(medicalLevel);

        // 药是否对症：徒手(medicine==null)仅出血可部分处理；有药需 Treats 匹配病状类别。
        bool matches = medicine.HasValue
            ? medicine.Value.Treats == condition.Type
            : condition.Type == HealthConditionType.Bleeding;
        if (!matches)
        {
            return new TreatmentResult { Status = TreatmentStatus.NoEffect, SeverityBefore = before, SeverityAfter = before, Removed = false };
        }

        condition.MarkTended();

        switch (condition.Type)
        {
            case HealthConditionType.Fracture:
            {
                // 夹板固定：二元，进入可愈合态（severity 由 TickDay 逐日降）。
                condition.Splint();
                return new TreatmentResult { Status = TreatmentStatus.Immobilized, SeverityBefore = before, SeverityAfter = condition.Severity, Removed = false };
            }
            case HealthConditionType.Bleeding:
            {
                double potency = medicine?.Potency ?? HandStaunchPotency;
                condition.AddSeverity(-potency * factor);
                if (condition.Severity <= 0)
                {
                    _conditions.Remove(condition);
                    return new TreatmentResult { Status = TreatmentStatus.Staunched, SeverityBefore = before, SeverityAfter = 0, Removed = true };
                }
                return new TreatmentResult { Status = TreatmentStatus.Applied, SeverityBefore = before, SeverityAfter = condition.Severity, Removed = false };
            }
            default: // Infection / Disease
            {
                double potency = medicine!.Value.Potency;
                condition.AddSeverity(-potency * factor);
                if (condition.Severity <= 0)
                {
                    _conditions.Remove(condition);
                    return new TreatmentResult { Status = TreatmentStatus.Cured, SeverityBefore = before, SeverityAfter = 0, Removed = true };
                }
                return new TreatmentResult { Status = TreatmentStatus.Applied, SeverityBefore = before, SeverityAfter = condition.Severity, Removed = false };
            }
        }
    }

    /// <summary>
    /// 推进一昼夜：对每条未处置(Tended=false)病状按恶化规则演变；开放出血伤口按几率(× 严重度)感染；
    /// 封顶触发终态(致死/致残)。<paramref name="resting"/> 减缓感染/疾病恶化并加速骨折愈合。
    /// 已处置病状本昼夜不自然恶化(治疗把控住了)。返回本昼夜事件汇总。已死则空转。
    /// </summary>
    /// <param name="rng">感染 roll 用（<see cref="IRandomSource.Range"/>(0,1)）。</param>
    /// <param name="resting">该幸存者本昼夜是否卧床休养。</param>
    public HealthTickResult TickDay(IRandomSource rng, bool resting)
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
                    if (!c.Tended)
                    {
                        c.AddSeverity(BleedWorsenPerDay);
                        // 开放伤口按几率感染（几率随出血严重度上升）：同部位已有感染则不重复。
                        double chance = InfectionBaseChance * c.Severity;
                        bool alreadyInfected = _conditions.Concat(newConditions)
                            .Any(x => x.Type == HealthConditionType.Infection && x.BodyPart == c.BodyPart);
                        if (!alreadyInfected && rng.Range(0.0, 1.0) < chance)
                        {
                            newConditions.Add(new HealthCondition(
                                HealthConditionType.Infection, InfectionInitialSeverity, c.BodyPart, c.OnLimb));
                            contracted = true;
                        }
                    }
                    if (c.Severity >= 1.0)
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
                    if (c.Immobilized)
                    {
                        double heal = FractureHealPerDay * (resting ? RestHealBonus : 1.0);
                        c.AddSeverity(-heal);
                    }
                    else
                    {
                        c.AddSeverity(FractureMalunionPerDay); // 未固定 → 畸形化
                    }
                    if (c.Severity >= 1.0 && !c.Immobilized)
                    {
                        outcome = c.OnLimb ? ConditionOutcome.Maim : ConditionOutcome.None; // 畸形愈合致残（肢体）
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

            // 完全愈合（severity 归 0，如固定骨折养好）→ 移除。
            if (c.Severity <= 0 && outcome == ConditionOutcome.None)
            {
                toRemove.Add(c);
            }
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
            set.Add(new HealthCondition(HealthConditionType.Bleeding, bleedingSeverity, part, IsLimb(body, part)));
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
}
