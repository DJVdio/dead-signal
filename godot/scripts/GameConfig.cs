namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（被 DeadSignal.Combat.Tests 与 DeadSignal.Sim 以 Link 方式编入；godot 运行时直接编译）。

/// <summary>
/// 消费层数值配置<b>聚合容器</b>（init-only POCO）。<b>只做聚合引用，不塞逻辑</b>：每个消费层子系统的段类
/// （<see cref="NightWatchConfig"/> …）各占独立文件，此处每段只挂<b>一行</b>属性。
/// <para>
/// 🔴 这是纯库 <c>CombatConfig</c> 在 godot 消费层的<b>平行镜像</b>——数值类型都在 <c>godot/scripts</c>，
/// 纯库装不下（循环依赖），故另立于此，范式一字不差。
/// </para>
/// <para>
/// 🔴 <b>高并发扩展点（消费层各单唯一共享文件·一行）</b>：后续迁移单在此**加一行**（如
/// <c>public HungerConfig Hunger { get; init; } = new();</c>），再建自己的 <c>HungerConfig.cs</c> +
/// <c>hunger.json</c> 即接入——<see cref="GameConfigLoader.Parse"/> 反射本类的所有
/// <see cref="IGameConfigSection"/> 属性自动加载，<b>加载器与宿主接线都不用动</b>。各段属性必须给默认实例
/// （<c>= new()</c>），以便加载前反射能读到其 <see cref="IGameConfigSection.FileName"/>。
/// </para>
/// </summary>
public sealed class GameConfig
{
    /// <summary>夜防对抗段（nightwatch.json）——潜行力权重等数值。</summary>
    public NightWatchConfig NightWatch { get; init; } = new();

    /// <summary>配方数值段（recipes.json）——各配方产量/工时/材料成本。</summary>
    public RecipeConfig Recipes { get; init; } = new();

    /// <summary>材料重量数值段（materials.json）——材料重量 kg 表（原 ItemRegistry.Materials 字面字典）。</summary>
    public MaterialConfig Materials { get; init; } = new();

    /// <summary>尸潮时限段（horde.json）——到期日 + 到期终局围攻波次调度数值。</summary>
    public HordeConfig Horde { get; init; } = new();

    /// <summary>饥饿能力惩罚段（hunger.json）——PenaltyFor 三档梯度惩罚值。</summary>
    public HungerConfig Hunger { get; init; } = new();

    /// <summary>电台主线段（military.json）——回复军方后军袭倒计时的间隔天数。</summary>
    public MilitaryConfig Military { get; init; } = new();

    /// <summary>家具建造数值段（furniture.json）——各家具建造材料数量 + 工时。</summary>
    public FurnitureConfig Furniture { get; init; } = new();

    /// <summary>武器改装数值段（weaponmods.json）——20 条改装的可调乘子/加值/否决几率 + 近战型态攻速折扣。</summary>
    public WeaponModConfig WeaponMods { get; init; } = new();

    /// <summary>幸存者专属效果段（perks.json）——诺蒂/南丁格尔/山姆/耗子/皮特 perk 数值。</summary>
    public PerkConfig Perks { get; init; } = new();

    /// <summary>感染+医疗数值段（health.json）——感染几率/免疫窗/恶化愈合速率/手术点数阈值 + 药品/手术耗材/医疗书逐条数字。</summary>
    public HealthConfig Health { get; init; } = new();

    /// <summary>南方三问考验段（southtrial.json）——三题总分的通过门槛（南逃 WIN 入口）。</summary>
    public SouthTrialConfig SouthTrial { get; init; } = new();

    /// <summary>神秘商人经济段（merchant.json）——买卖价率。</summary>
    public MerchantConfig Merchant { get; init; } = new();

    /// <summary>诱捕命中率段（farming.json）——圈套/捕鸟陷阱的基础命中率/递减/地板 + 圈套兔鼠分配比例。</summary>
    public FarmingConfig Farming { get; init; } = new();

    // ── 后续消费层 config 迁移单在此加一行（各自独立、互不撞车）──────────────────
    // public HungerConfig    Hunger    { get; init; } = new();   // hunger.json
    // public RecipeConfig    Recipes   { get; init; } = new();   // recipes.json
    // public FurnitureConfig Furniture { get; init; } = new();   // furniture.json
    // …
}
