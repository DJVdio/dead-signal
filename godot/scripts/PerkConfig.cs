using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Tests/WikiExtract 以 Link 编入）。

/// <summary>
/// 幸存者 authored 专属效果的<b>可调数值段</b>：<c>perks.json</c>——诺蒂书虫 / 南丁格尔 / 山姆 / 耗子 / 皮特
/// 五套手写 perk 的**散落数字常量**（升级阈值、加成幅度、概率、乘子等）的数值真源。
/// <para>
/// 📐 照 <see cref="NightWatchConfig"/> 范式（config-consumer-pilot 单）：段自报 <see cref="FileName"/> + 自解析
/// <see cref="FromJson"/>，裸载荷 POCO；<see cref="GameConfig"/> 挂一行、<c>GameConfigLoader.Parse</c> 反射自动加载。
/// </para>
/// <para>
/// 🔴 <b>只搬数值，authored 结构留在原代码</b>：每个 perk 的**行为骨架**——分级逻辑（<c>EvaluateLevel</c> 的
/// 派生/可倒退/latch 形态）、升级轴（累计手术台数 / 营地人数 / 阅读时长 / 出行次数 / 饥饿连续相位）、身份标记与名字
/// （<c>SamName</c>/<c>RatName</c>/<c>PeteName</c>）、持久化旗标 key（<c>nightingale_surgery_count</c> 等）、噪音来源
/// 真值表（<c>RatPerk.AppliesToActionNoise</c>）、乘算/加算口径（耗子 L2=2.50 的<b>指定加算例外</b>、山姆连乘）——
/// <b>一律留在 SurvivorPerks.cs / PetePerk.cs 不外置</b>。本段只装能被安全替换成 <c>=&gt; GameConfigCatalog.Section&lt;PerkConfig&gt;().X</c>
/// 的**纯数字常量**（零外部 const 上下文，仅方法体内用）。
/// </para>
/// <para>
/// init 默认值＝迁移前的原始常量（proto 只用于反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/> 覆盖）。
/// 数值分「拟定待调」（皮特升级阈值、读速座位/前置系数等 draft）与「用户拍板·非拟定」（南丁格尔感染减免、山姆个人效果、
/// 耗子/皮特/山姆各效果数值）两类——形态已锁，数值口径以 wiki 表为准（表赢代码）。
/// </para>
/// </summary>
public sealed class PerkConfig : IGameConfigSection
{
    // ── 诺蒂·书虫（BookwormPerk / ReadingSpeed）─────────────────────────────────
    /// <summary>书虫升到 L2 所需累计阅读时长；当前值以 Wiki 配置为准。</summary>
    public double BookwormLevel2ThresholdHours { get; init; } = 24;
    /// <summary>书虫升到 L3 所需累计阅读时长；当前值以 Wiki 配置为准。</summary>
    public double BookwormLevel3ThresholdHours { get; init; } = 72;
    /// <summary>书虫 L1 自身读速加成；合并口径与当前值以 Wiki 配置为准。</summary>
    public double BookwormSelfBonusL1 { get; init; } = 0.25;
    /// <summary>书虫 L2/L3 自身读速加成；L3 升级点在全营加成，当前值以 Wiki 配置为准。</summary>
    public double BookwormSelfBonusL2Plus { get; init; } = 0.75;
    /// <summary>书虫 L3 满级贡献给全营的读速加成幅度；当前值以 Wiki 配置为准。</summary>
    public double BookwormCampWideBonusAtMax { get; init; } = 0.25;
    /// <summary>无座位阅读读速乘子（draft）；当前值以 Wiki 配置为准。</summary>
    public double ReadingNoSeatMultiplier { get; init; } = 0.9;
    /// <summary>未读完前置书时的读速乘子（draft，不禁止）；当前值以 Wiki 配置为准。</summary>
    public double ReadingMissingPrerequisiteMultiplier { get; init; } = 0.2;

    // ── 南丁格尔·护士三级（NightingalePerk）——数值用户拍板·非拟定 ─────────────────
    /// <summary>南丁格尔升到 L2 所需她本人累计手术台数（拟定待调）。</summary>
    public int NightingaleLevel2ThresholdSurgeries { get; init; } = 3;
    /// <summary>南丁格尔升到 L3 所需她本人累计手术台数（拟定待调）。</summary>
    public int NightingaleLevel3ThresholdSurgeries { get; init; } = 8;
    /// <summary>常人手术基础点数。</summary>
    public int NightingaleDefaultSurgeryBasePoints { get; init; } = 15;
    /// <summary>1级：南丁格尔本人手术基础点数；当前值以 Wiki 配置为准。</summary>
    public int NightingaleSurgeryBasePoints { get; init; } = 30;
    /// <summary>3级：全营手术基础点加成（永续遗产）；当前值以 Wiki 配置为准。</summary>
    public int NightingaleCampSurgeryBaseBonus { get; init; } = 5;
    /// <summary>2级：全营感染率减免（她在营存活时生效；当前值以 Wiki 配置为准）。</summary>
    public double NightingaleLevel2InfectionReduction { get; init; } = 0.15;
    /// <summary>3级：全营感染率追加减免（永续遗产；当前值以 Wiki 配置为准）。</summary>
    public double NightingaleLevel3InfectionReduction { get; init; } = 0.10;
    /// <summary>南丁格尔 L2：干净床铺恢复效率加成；当前值以 Wiki 配置为准。</summary>
    public double NightingaleBedSleepHealBonusPct { get; init; } = 20.0;

    // ── 山姆·英雄风范（SamPerk）——数值用户拍板·非拟定 ─────────────────────────────
    /// <summary>山姆升到 L2 所需营地人数（活着的在营人类，含山姆）。</summary>
    public int SamLevel2CampPopulation { get; init; } = 3;
    /// <summary>山姆升到 L3 所需营地人数（活着的在营人类，含山姆）。</summary>
    public int SamLevel3CampPopulation { get; init; } = 6;
    /// <summary>2级：他收到的伤害减免（护甲结算后乘算）；当前值以 Wiki 配置为准。</summary>
    public double SamLevel1DamageReduction { get; init; } = 0.10;
    /// <summary>1级：他的负重加成；当前值以 Wiki 配置为准。</summary>
    public double SamLevel2CarryBonus { get; init; } = 0.15;
    /// <summary>1级：山姆操作能力加成；当前值以 Wiki 配置为准。</summary>
    public double SamLevel1OperationBonus { get; init; } = 0.10;
    /// <summary>2级：山姆身体恢复速度加成；当前值以 Wiki 配置为准。</summary>
    public double SamLevel2HealSpeedBonus { get; init; } = 0.30;
    /// <summary>3级：被震荡概率降低；当前值以 Wiki 配置为准。</summary>
    public double SamLevel3ConcussionReduction { get; init; } = 0.75;
    /// <summary>3级：上肢操作、下肢移动两种骨折惩罚的负面缺口减轻；当前值以 Wiki 配置为准。</summary>
    public double SamLevel3FracturePenaltyReduction { get; init; } = 0.30;
    /// <summary>旧版 Sam 光环兼容字段：当前 authored 页面未启用。</summary>
    public double SamAuraCarryBonus { get; init; } = 0.03;
    /// <summary>旧版 Sam 光环兼容字段：当前 authored 页面未启用。</summary>
    public double SamAuraWorkSpeedBonus { get; init; } = 0.03;
    /// <summary>旧版 Sam 光环兼容字段：当前 authored 页面未启用。</summary>
    public double SamAuraHealSpeedBonus { get; init; } = 0.03;
    /// <summary>旧版 Sam 光环兼容字段：当前 authored 页面未启用。</summary>
    public double SamAuraInfectionWorsenReduction { get; init; } = 0.03;

    // ── 耗子（RatPerk）——数值用户原话·非拟定 ────────────────────────────────────
    /// <summary>耗子升到 L2 所需累计搜出件数；当前值以 Wiki 配置为准。</summary>
    public int RatLevel2ThresholdItems { get; init; } = 75;
    /// <summary>耗子升到 L3 所需累计搜出件数；当前值以 Wiki 配置为准。</summary>
    public int RatLevel3ThresholdItems { get; init; } = 250;
    /// <summary>L1：动作噪音半径乘子；当前值以 Wiki 配置为准。</summary>
    public double RatLevel1ActionNoiseMultiplier { get; init; } = 0.60;
    /// <summary>L1：翻找搜刮速度加成；当前值以 Wiki 配置为准。</summary>
    public double RatLevel1LootSpeedBonus { get; init; } = 0.50;
    /// <summary>L2：翻找搜刮速度追加加成（同 perk 两级台阶按总量加算）；当前值以 Wiki 配置为准。</summary>
    public double RatLevel2LootSpeedBonus { get; init; } = 1.00;
    /// <summary>L3：黑暗隐匿点加成（未接线）；当前值以 Wiki 配置为准。</summary>
    public double RatLevel3DarknessStealthBonus { get; init; } = 0.50;
    /// <summary>L3：破隐先手攻击额外伤害（未接线）；当前值以 Wiki 配置为准。</summary>
    public double RatLevel3AmbushDamageBonus { get; init; } = 0.35;

    // ── 皮特（PetePerk）——数值用户口径·非拟定 ───────────────────────────────────
    /// <summary>一级：移速乘子；当前值以 Wiki 配置为准。</summary>
    public double PeteLevel1MoveSpeedMultiplier { get; init; } = 1.15;
    /// <summary>二级：移速乘子；当前值以 Wiki 配置为准。</summary>
    public double PeteLevel2MoveSpeedMultiplier { get; init; } = 1.25;
    /// <summary>三级：移速乘子；当前值以 Wiki 配置为准。</summary>
    public double PeteLevel3MoveSpeedMultiplier { get; init; } = 1.30;
    /// <summary>二级：操作能力加成（乘算）；当前值以 Wiki 配置为准。</summary>
    public double PeteOperationCapabilityBonus { get; init; } = 0.05;
    /// <summary>三级：受击闪避概率与负重门槛由 Wiki 配置提供。</summary>
    public double PeteDodgeChanceValue { get; init; } = 0.15;
    /// <summary>三级闪避的负重门槛（kg）：当前负重严格 &lt;此值才可闪。</summary>
    public double PeteDodgeMaxCarriedKg { get; init; } = 30.0;
    /// <summary>一相位额外掉饥饿的概率（L1 起常驻）；当前值以 Wiki 配置为准。</summary>
    public double PeteExtraHungerDropChance { get; init; } = 0.25;
    /// <summary>连续计数的饥饿下限：相位饥饿 ≥此值才续上连续，&lt;则清零。</summary>
    public int PeteHungerThresholdForStreak { get; init; } = 3;
    /// <summary>L1→L2 所需连续相位数；当前值以 Wiki 配置为准。</summary>
    public int PeteLevel2ConsecutivePhases { get; init; } = 10;
    /// <summary>L2→L3 出行计数的饥饿上限：出发瞬间饥饿 ≤此值才计一次。</summary>
    public int PeteDepartureHungerCeiling { get; init; } = 5;
    /// <summary>L2→L3 所需的合格出行次数。</summary>
    public int PeteLevel3DepartureCount { get; init; } = 3;

    // ── 克莉丝汀·巧舌如簧（ChristinePerk）——数值 authored·非拟定（characters.json 克莉丝汀行）─────
    /// <summary>L1→L2 所需在营存活天数（characters.json：存活三天）。</summary>
    public int ChristineLevel2ThresholdDays { get; init; } = 3;
    /// <summary>L1：每相位「不掉饥饿」的基础几率；当前值以 Wiki 配置为准。</summary>
    public double ChristineL1HungerSkipChance { get; init; } = 0.25;
    /// <summary>L3：每相位「不掉饥饿」的额外几率（与 L1 加算）；当前值以 Wiki 配置为准。</summary>
    public double ChristineL3ExtraHungerSkipChance { get; init; } = 0.10;
    /// <summary>L2：商人买入折扣（需她在营存活）；当前值以 Wiki 配置为准。</summary>
    public double ChristineLevel2BuyDiscount { get; init; } = 0.0625;
    /// <summary>L3：商人卖出价率（百分比，70；需她在营存活）。</summary>
    public int ChristineLevel3SellRatePercent { get; init; } = 70;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "perks.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<PerkConfig>(json, options)
           ?? throw new InvalidOperationException("perks.json 反序列化为空（fail-fast）。");
}
