using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Tests / WikiExtract 以 Link 编入）。

/// <summary>一味感染/疾病药品的**可调数字**（Potency/Efficacy/WorsenMultiplier）。
/// 「治哪种病状（Treats）」是 authored 语义，**留在 <see cref="MedicineCatalog"/> 代码里不外置**。</summary>
public readonly record struct MedicineNumbers(double Potency, double Efficacy, double WorsenMultiplier);

/// <summary>一味手术耗材的**可调数字**（Points 供点 / InfectionChanceMultiplier 敷用后感染乘子）。
/// 「适用哪种伤（Treats）/ 是否独占（Exclusive）」是 authored 结构，**留在 <see cref="SurgeryCatalog"/> 代码里不外置**。</summary>
public readonly record struct SurgerySupplyNumbers(int Points, double InfectionChanceMultiplier);

/// <summary>
/// 感染 + 医疗数值段：<c>health.json</c>——<see cref="HealthConditionSet"/> 的恶化/愈合速率、感染几率基数、
/// 免疫窗、手术点数/阈值，以及三张目录（药品/手术耗材/医疗书）的**逐条可调数字**（数值真源）。
/// <para>
/// 📐 照 <see cref="NightWatchConfig"/> 的消费层 config 范式（对应纯库的 WeaponConfig）：段类自报 <see cref="FileName"/> +
/// 自解析 <see cref="FromJson"/>，<see cref="GameConfig"/> 加一行、加载器反射自动发现。
/// </para>
/// <para>
/// ⚠️ <b>只搬数值、不搬 authored 结构</b>：感染系统的多感染条/免疫条**结构逻辑**、目录的**条目结构/authored 语义**
/// （药品治哪种病、耗材适用哪种伤/是否独占、医疗书是否只在无耗材时生效）全部**留在 <c>HealthConditions.cs</c> 代码里**，
/// 本段只装「几率/乘子/供点/阈值」这些数字。
/// </para>
/// <para>
/// init 默认值＝迁移前的原始常量（proto 只用于反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/>
/// 加载的 json 值覆盖）。感染/医疗是 <c>HealthConditions</c> 纯逻辑、<b>不进 Duel/Sim 战斗结算</b>，零漂移由
/// <c>HealthConfigMigrationTests</c> 的位级往返 + 字面锚定证明（不跑 Sim MD5）。数值皆「拟定待调」。
/// </para>
/// </summary>
public sealed class HealthConfig : IGameConfigSection
{
    // ── 感染：每伤口感染几率基数（按流血【等级】离散查表，与部位无关）+ 免疫窗 + 恶化/治疗速率 ──
    /// <summary>大流血伤口感染几率基数；当前值以 Wiki 配置为准。</summary>
    public double InfectionBaseChanceLarge { get; init; } = 0.25;
    /// <summary>中流血伤口感染几率基数；当前值以 Wiki 配置为准。</summary>
    public double InfectionBaseChanceMedium { get; init; } = 0.15;
    /// <summary>小流血伤口感染几率基数；当前值以 Wiki 配置为准，未知等级/旧档从宽按此。</summary>
    public double InfectionBaseChanceSmall { get; init; } = 0.05;
    /// <summary>已手术（止血中）伤口的感染几率折减系数；当前值以 Wiki 配置为准。</summary>
    public double OperatedInfectionFactor { get; init; } = 0.5;
    /// <summary>免疫窗激活期间的感染几率乘子；当前值以 Wiki 配置为准。</summary>
    public double ImmuneWindowInfectionFactor { get; init; } = 0.05;
    /// <summary>免疫条满后置一段免疫窗，窗时长由 Wiki 配置提供。</summary>
    public double ImmuneWindowDays { get; init; } = 1.0;
    /// <summary>感染死亡进度每日累积速率；当前值以 Wiki 配置为准。</summary>
    public double InfectionWorsenPerDay { get; init; } = 1.0 / 6.0;
    /// <summary>治疗（免疫）进度基准积累速率（每日，效率 1.0 满效值）。</summary>
    public double CureProgressBaseRate { get; init; } = 0.67;
    /// <summary>新感染初始「感染进度」（死亡赛道起点，从 0 起算）。</summary>
    public double InfectionInitialSeverity { get; init; } = 0.0;
    /// <summary>止血≠无菌：已手术伤口 severity ≥ 此闭口阈值前仍有感染窗。</summary>
    public double WoundClosedThreshold { get; init; } = 0.15;
    /// <summary>伤口「新鲜期」感染窗口（昼夜数）：只有伤口存在的头几昼夜有感染风险。</summary>
    public int InfectionWindowDays { get; init; } = 4;

    // ── 流血 / 骨折 / 疾病：恶化与愈合速率、休养系数、非致命封顶、擦伤自愈线 ──
    /// <summary>未手术出血逐日加重。</summary>
    public double BleedWorsenPerDay { get; init; } = 0.10;
    /// <summary>已手术出血逐日愈合（按恢复效率缩放）。</summary>
    public double BleedHealPerDay { get; init; } = 0.20;
    /// <summary>未手术骨折逐日畸形化（封顶致残）。</summary>
    public double FractureMalunionPerDay { get; init; } = 0.05;
    /// <summary>已手术骨折逐日愈合（×效率/100）。</summary>
    public double FractureHealPerDay { get; init; } = 0.24;
    /// <summary>疾病逐日恶化。</summary>
    public double DiseaseWorsenPerDay { get; init; } = 0.12;
    /// <summary>休养减缓感染/疾病恶化系数。</summary>
    public double RestWorsenFactor { get; init; } = 0.5;
    /// <summary>休养加速愈合系数。</summary>
    public double RestHealBonus { get; init; } = 1.5;
    /// <summary>非致命失血伤口的严重度封顶（只溃烂感染、拖再久也不失血死）。</summary>
    public double MinorBleedSeverityCap { get; init; } = 0.6;
    /// <summary>擦伤级严重度阈值：初始 severity ≤ 此值的出血视作「很小的伤」，可自行闭合。</summary>
    public double AbrasionSeverityThreshold { get; init; } = 0.2;

    // ── 手术：点数池 / 门槛 / 失败线 / 自体系数 / 即刻恢复 / 睡床加算 / 重做冷却 / 用药照护基数 ──
    /// <summary>手术基础点数（徒手无床的底池）。</summary>
    public int SurgeryBasePoints { get; init; } = 15;
    /// <summary>床位加成点数。</summary>
    public int BedBonusPoints { get; init; } = 10;
    /// <summary>手术失败阈值：roll ≤ 此值 → 失败，需重做。</summary>
    public int SurgeryFailThreshold { get; init; } = 10;
    /// <summary>手术门槛：有效池 P &lt; 此值 → 不允许手术。</summary>
    public int SurgeryMinPoints { get; init; } = 15;
    /// <summary>自体手术能力系数；当前值以 Wiki 配置为准。</summary>
    public double SelfSurgeryFactor { get; init; } = 0.60;
    /// <summary>手术成功即刻恢复量：任何成功手术当场对该伤 severity 立减此值。</summary>
    public double ImmediateHealOnSuccess { get; init; } = 0.05;
    /// <summary>睡床恢复速度加成；合并口径与当前值以 Wiki 配置为准。</summary>
    public double BedSleepHealBonusPct { get; init; } = 10.0;
    /// <summary>[SPEC-B14-补2] 玫瑰果茶恢复加成；持续时间、幅度与合并口径以 Wiki 配置为准（数值真源，原 Pawn.RosehipTeaHealBonusPct const）。</summary>
    public double RosehipTeaHealBonusPct { get; init; } = 9.0;
    /// <summary>重做手术冷却（昼夜）：距上次手术 &gt; 此值才可重做。</summary>
    public int RedoSurgeryCooldownDays { get; init; } = 1;
    /// <summary>单次药效固定照护基数（仅感染/疾病用药）。</summary>
    public double TreatmentPotencyFactor { get; init; } = 0.8;

    // ── 三张目录的逐条可调数字（结构/authored 语义留在各 Catalog 代码里）──
    /// <summary>药品可调数字：材料标识名 → (Potency, Efficacy, WorsenMultiplier)。治哪种病是 authored、留代码。</summary>
    public IReadOnlyDictionary<string, MedicineNumbers> Medicines { get; init; } = new Dictionary<string, MedicineNumbers>
    {
        ["antibiotics"] = new(0.5, 1.00, 0.50),
        ["herbal_salve"] = new(0.5, 0.35, 0.75),
        ["dandelion_tea"] = new(0.5, 0.15, 0.85),
        ["medicine"] = new(0.6, 1.00, 1.00),
    };

    /// <summary>手术耗材可调数字：材料标识名 → (Points, InfectionChanceMultiplier)。适用伤类/独占是 authored、留代码。</summary>
    public IReadOnlyDictionary<string, SurgerySupplyNumbers> SurgerySupplies { get; init; } = new Dictionary<string, SurgerySupplyNumbers>
    {
        ["bandage"] = new(15, 1.0),
        ["herbal_bandage"] = new(20, 0.75),
        ["needle_thread"] = new(15, 1.0),
        ["splint"] = new(25, 1.0),
        ["first_aid_kit"] = new(60, 1.0),
    };

    /// <summary>医疗书手术加点：book id → 点数。「是否只在无耗材时生效」是 authored、留代码。</summary>
    public IReadOnlyDictionary<string, int> MedicalBooks { get; init; } = new Dictionary<string, int>
    {
        ["wilderness_survival_guide"] = 3,
    };

    /// <summary>按材料标识名取药品可调数字（缺键 fail-fast——目录结构在代码里已保证键存在）。</summary>
    public MedicineNumbers MedicineFor(string materialKey)
        => Medicines.TryGetValue(materialKey, out MedicineNumbers m) ? m
           : throw new InvalidOperationException($"health.json 缺药品数字：{materialKey}（fail-fast）。");

    /// <summary>按材料标识名取手术耗材可调数字（缺键 fail-fast）。</summary>
    public SurgerySupplyNumbers SurgerySupplyFor(string materialKey)
        => SurgerySupplies.TryGetValue(materialKey, out SurgerySupplyNumbers s) ? s
           : throw new InvalidOperationException($"health.json 缺手术耗材数字：{materialKey}（fail-fast）。");

    /// <summary>按 book id 取医疗书手术加点（缺键 fail-fast）。</summary>
    public int MedicalBookPointsFor(string bookId)
        => MedicalBooks.TryGetValue(bookId, out int p) ? p
           : throw new InvalidOperationException($"health.json 缺医疗书加点：{bookId}（fail-fast）。");

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "health.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<HealthConfig>(json, options)
           ?? throw new InvalidOperationException("health.json 反序列化为空（fail-fast）。");
}
