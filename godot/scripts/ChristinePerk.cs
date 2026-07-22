using DeadSignal.Combat; // IRandomSource（纯 C# 引擎，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SurvivorPerks.cs / PetePerk.cs / HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// **克莉丝汀**·教学关反水后收留的销售员（authored 三级专属效果「巧舌如簧」，纯逻辑）。
/// 数值取自 Wiki 配置（**authored·非拟定**）：
///   · 一级：懂得挨饿——每个相位变化时按配置几率**不掉饥饿值**（入队即得）。
///   · 二级：从商人处买东西按配置折扣（<b>需她在营存活</b>，同南丁格尔 L2/山姆光环先例）。
///   · 三级：大仇得报，回归销售员本质——卖出价率与额外饥饿保护均按配置。
/// 三级效果**累进**（升级不丢下级；合并口径见 Wiki）。
///
/// **升级轴**（<c>characters.json</c>「加入营地是一级，存活三天后升到二级，清剿金手指帮后升三级」）：
///   · <b>L1→L2「在营存活达到配置门槛」</b>＝调用方喂**在营存活天数**（同道格羁绊 <see cref="DougBruceBond"/> 的「共同存活天数」先例，
///     调用方每昼夜她存活即 +1；她死/离营天然停累加）；累计达到 <see cref="Level2ThresholdDays"/> → L2。
///   · <b>L2→L3「清剿金手指帮」</b>＝据点守备全灭时置的永久旗标 <see cref="GoldfingerDiscovery.GangClearedFlag"/>
///     （灭帮是中后期一次性事件，其时在营天数早已 ≥3 ⇒ 旗标置位即直取 L3）。
///
/// <para>🔴 <b>[Q1·主 agent 拍板] L1 与 L3 额外效果的合并按总量加算，不是独立掷</b>：
/// 用户原话「额外」＝同 perk 两级台阶按总量加算（同耗子等级效果的指定加算例外先例）。
/// 项目通则「百分比一律乘算」针对**不同来源**相叠；这里是**同一 perk 自己的两级台阶**，按总量加算。</para>
///
/// <para>🔴 <b>[Q2·主 agent 拍板] 商人折扣/卖价加成均需她在营存活</b>（沿用南丁格尔 L2「卫生意识需她在营维持」、
/// 山姆光环「只要山姆还活着」先例）：她死/离营 → 折扣与卖价加成即失。</para>
///
/// **无实例等级字段**：等级由「在营存活天数」（调用方运行时累积、随 <c>BondSave</c> 落盘）+「灭帮旗标」（<c>StoryFlags</c>）派生，
/// 身份标记＝<see cref="SurvivorPerks.IsChristine"/>。效果应用（相位饥饿钩子 <see cref="ResolveHungerPhase"/>、
/// 商人买卖价率 <see cref="MerchantBuyDiscount"/>/<see cref="MerchantSellRatePercent"/>）皆在消费层接线，本类只出**纯函数常量/派生/判定**。
/// </summary>
public static class ChristinePerk
{
    /// <summary>她的显示名（<c>Pawn.Create</c> 按此名授予 <see cref="SurvivorPerks.GrantChristine"/>，同山姆/耗子/皮特按名授予先例）。</summary>
    public const string ChristineName = "克莉丝汀";

    // —— 升级阈值（characters.json「存活三天后升到二级」）。数值外置 perks.json ——
    /// <summary>L1→L2 所需在营存活天数（characters.json：存活三天）。</summary>
    public static int Level2ThresholdDays => GameConfigCatalog.Section<PerkConfig>().ChristineLevel2ThresholdDays;

    // —— 三级效果数值（characters.json 克莉丝汀行，authored·非拟定）。数值外置 perks.json ——
    /// <summary>L1：每相位「不掉饥饿」的基础几率；当前值以 Wiki 配置为准。</summary>
    public static double L1HungerSkipChance => GameConfigCatalog.Section<PerkConfig>().ChristineL1HungerSkipChance;
    /// <summary>L3：每相位「不掉饥饿」的**额外**几率；与 L1 的合并口径见 Wiki。</summary>
    public static double L3ExtraHungerSkipChance => GameConfigCatalog.Section<PerkConfig>().ChristineL3ExtraHungerSkipChance;
    /// <summary>L2：商人买入折扣；当前值以 Wiki 配置为准，需她在营存活。</summary>
    public static double Level2BuyDiscount => GameConfigCatalog.Section<PerkConfig>().ChristineLevel2BuyDiscount;
    /// <summary>L3：商人卖出价率；当前值以 Wiki 配置为准，需她在营存活。</summary>
    public static int Level3SellRatePercent => GameConfigCatalog.Section<PerkConfig>().ChristineLevel3SellRatePercent;

    // ==================== 等级派生纯函数 ====================

    /// <summary>
    /// 由「在营存活天数」+「灭金手指帮旗标」派生等级：灭帮 → L3；否则达到 <see cref="Level2ThresholdDays"/> → L2；
    /// 其余（入队即得）→ L1。灭帮是永久旗标、在营天数单调累加（她死/离营停累加）⇒ 等级**单调不倒退**。
    /// </summary>
    /// <param name="daysSurvivedInCamp">她入队后**在营存活**的累计天数（调用方每昼夜她存活即 +1；不在营不累加）。</param>
    /// <param name="goldfingerGangCleared">金手指帮是否已被清剿（<see cref="GoldfingerDiscovery.GangCleared"/>）。</param>
    public static int EvaluateLevel(int daysSurvivedInCamp, bool goldfingerGangCleared)
    {
        if (goldfingerGangCleared) return 3;
        if (daysSurvivedInCamp >= Level2ThresholdDays) return 2;
        return 1;
    }

    /// <summary>由 <c>StoryFlags</c> 灭帮旗标 + 在营存活天数直接取她当前等级。</summary>
    public static int LevelOf(int daysSurvivedInCamp, StoryFlags flags)
        => EvaluateLevel(daysSurvivedInCamp, GoldfingerDiscovery.GangCleared(flags));

    // ==================== 相位「不掉饥饿」（注入随机复现） ====================

    /// <summary>
    /// 某等级每相位「不掉饥饿」的几率：L1/L2 使用 <see cref="L1HungerSkipChance"/>；
    /// L3 起再按 <see cref="L3ExtraHungerSkipChance"/> 合并，口径以 Wiki 为准。
    /// </summary>
    public static double HungerSkipChance(int christineLevel)
        => L1HungerSkipChance + (christineLevel >= 3 ? L3ExtraHungerSkipChance : 0.0);

    /// <summary>
    /// 克莉丝汀的**饥饿相位结算变体**：本相位本会掉饥饿（未进食、未处饿死终态）时，以 <see cref="HungerSkipChance"/> 几率
    /// **跳过这次衰减**（视作净零维持，即「这个相位没掉饥饿」）；未命中/进食则走普通 <see cref="HungerState.ResolvePhase"/>。
    /// <para>
    /// 非克莉丝汀 → 直接走普通结算，<b>不触碰 <paramref name="rng"/></b>（不消耗随机流，零回归，同 <see cref="PetePerk.ResolveHungerPhase"/>）。
    /// 进食相位（<paramref name="ate"/>=true）本就净零、无衰减可跳 ⇒ 同样不掷骰。
    /// </para>
    /// 随机走可注入 <see cref="IRandomSource"/>（测试 <see cref="SequenceRandomSource"/> 复现；生产走 <see cref="SystemRandomSource"/>）。
    /// </summary>
    /// <returns>结算后是否饿死（刻度归 0）。</returns>
    public static bool ResolveHungerPhase(HungerState state, bool ate, IRandomSource rng, bool isChristine, int christineLevel)
    {
        if (isChristine && !ate && !state.IsStarved && rng.Range(0.0, 1.0) < HungerSkipChance(christineLevel))
        {
            return state.ResolvePhase(ate: true); // 不掉饥饿 = 当作吃到一餐的净零维持（不额外回填）
        }
        return state.ResolvePhase(ate);
    }

    // ==================== 商人买卖价率（需在营存活） ====================

    /// <summary>
    /// 克莉丝汀给的**商人买入折扣**（零值=无折扣）：L2 起、且**她在营存活**（<paramref name="aliveInCamp"/>）→ <see cref="Level2BuyDiscount"/>；
    /// 否则零值（零回归：无她/她死/离营 ⇒ 原价）。供 <c>MerchantTrade.Buy</c> 乘算到实付买价上。
    /// </summary>
    public static double MerchantBuyDiscount(int christineLevel, bool aliveInCamp)
        => aliveInCamp && christineLevel >= 2 ? Level2BuyDiscount : 0.0; // L2 起累进保留（L3 仍享折扣）

    /// <summary>
    /// 克莉丝汀影响下的**商人卖出价率**：L3 起、且**她在营存活** → <see cref="Level3SellRatePercent"/>；
    /// 否则回退 <paramref name="defaultRatePercent"/>（零回归）。供 <c>MerchantTrade.SellPrice</c> 取率。
    /// </summary>
    public static int MerchantSellRatePercent(int christineLevel, bool aliveInCamp, int defaultRatePercent)
        => aliveInCamp && christineLevel >= 3 ? Level3SellRatePercent : defaultRatePercent;
}
