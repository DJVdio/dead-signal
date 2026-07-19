using DeadSignal.Combat; // IRandomSource（纯 C# 引擎，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SurvivorPerks.cs / HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// **皮特**·青春期大男孩、曾校田径队（authored 三级专属效果，纯逻辑）。
/// 用户口径（主 agent 已拍板，原话不许引申）：当前倍率、概率与门槛均以 Wiki 配置为准。
///   · 一级：获得基础移速档，并常驻额外饥饿衰减概率。
///   · 二级：获得更高移速档与操作能力加成。
///   · 三级：获得最高移速档，并在负重门槛内获得受击闪避概率。
/// 三级效果**累进**（升级不丢下级：L2/L3 保留 L1 的移速台阶与饥饿掉 2；L3 保留 L2 的操作光环）。
///
/// **升级轴**（形态混合，同框架"不要求条件单调"）：
///   · <b>L1→L2「连续高饥饿」</b>＝**相位级**每相位查：达到 <see cref="HungerThresholdForStreak"/> 才续上连续计数，
///     未达到则**清零重记**；连续达到 <see cref="Level2ConsecutivePhases"/> 个相位不断 → **永久**升到 L2。
///     ⚠️ 连续计数会被清零，故它**不能直接当等级源**（一次饿相位就把已达成的 L2 抹掉是错的）——升到 L2 后**latch**
///     一个永久旗标 <see cref="Level2ReachedFlag"/>，此后 streak 再清零也不倒退（与山姆的"可倒退"相反）。
///   · <b>L2→L3「低饥饿出行」</b>＝出发瞬间饥饿不超过 <see cref="DepartureHungerCeiling"/> 即计数 +1、**单调累计**
///     （只增不减），累计达到 <see cref="Level3DepartureCount"/> 次 → L3。"作为调查团一员"＝在出行队伍名单里即可（运行时判）。
///
/// **无实例状态**：等级由两个 <c>StoryFlags</c> 计数派生（<see cref="LevelOf"/>），"字符串承载整数"同
/// <c>nightingale_surgery_count</c> / <c>rat_scavenge_count</c> ⇒ <b>存档天然覆盖，不加 SaveData 字段、不撞版本号</b>。
/// 身份标记 = <see cref="SurvivorPerks.IsPete"/>。数值应用（移速槽/闪避 override/操作消费点/饥饿相位钩子）皆为
/// Godot 运行时接线，归后续 pete-runtime 单；本类只出**纯函数常量/派生/判定**。
/// </summary>
public static class PetePerk
{
    /// <summary>他的名字（<c>Pawn.Create</c> 按此名授予 <see cref="SurvivorPerks.GrantPete"/>，同山姆/耗子按名授予先例）。</summary>
    public const string PeteName = "皮特";

    // —— 三级移速乘子（用户原话）。当前数值以 Wiki 配置为准 ——
    /// <summary>一级：移速倍率读取 Wiki 配置。</summary>
    public static double Level1MoveSpeedMultiplier => GameConfigCatalog.Section<PerkConfig>().PeteLevel1MoveSpeedMultiplier;
    /// <summary>二级：移速倍率读取 Wiki 配置。</summary>
    public static double Level2MoveSpeedMultiplier => GameConfigCatalog.Section<PerkConfig>().PeteLevel2MoveSpeedMultiplier;
    /// <summary>三级：移速倍率读取 Wiki 配置。</summary>
    public static double Level3MoveSpeedMultiplier => GameConfigCatalog.Section<PerkConfig>().PeteLevel3MoveSpeedMultiplier;

    // —— L2 操作能力（用户原话）。当前数值以 Wiki 配置为准 ——
    /// <summary>二级：操作能力加成（走消费点乘算、不 clamp，见 <see cref="OperationCapabilityWithBonus"/>）。</summary>
    public static double OperationCapabilityBonus => GameConfigCatalog.Section<PerkConfig>().PeteOperationCapabilityBonus;

    // —— L3 闪避（用户原话）。当前数值以 Wiki 配置为准 ——
    /// <summary>三级：受击闪避概率与负重条件以 Wiki 配置为准。</summary>
    public static double DodgeChanceValue => GameConfigCatalog.Section<PerkConfig>().PeteDodgeChanceValue;
    /// <summary>三级闪避的负重门槛以 Wiki 配置为准；边界采用严格小于。</summary>
    public static double DodgeMaxCarriedKg => GameConfigCatalog.Section<PerkConfig>().PeteDodgeMaxCarriedKg;

    // —— 额外饥饿衰减（用户原话，L1 起常驻不论等级）。当前数值以 Wiki 配置为准 ——
    /// <summary>一相位额外衰减的概率以 Wiki 配置为准，并叠加普通衰减。</summary>
    public static double ExtraHungerDropChance => GameConfigCatalog.Section<PerkConfig>().PeteExtraHungerDropChance;

    // —— 升级阈值（用户口径已拍板）。当前数值以 Wiki 配置为准 ——
    /// <summary>连续计数的饥饿下限以 Wiki 配置为准；未达到即清零。</summary>
    public static int HungerThresholdForStreak => GameConfigCatalog.Section<PerkConfig>().PeteHungerThresholdForStreak;
    /// <summary>L1→L2 所需连续相位数以 Wiki 配置为准。</summary>
    public static int Level2ConsecutivePhases => GameConfigCatalog.Section<PerkConfig>().PeteLevel2ConsecutivePhases;
    /// <summary>L2→L3 出行计数的饥饿上限以 Wiki 配置为准。</summary>
    public static int DepartureHungerCeiling => GameConfigCatalog.Section<PerkConfig>().PeteDepartureHungerCeiling;
    /// <summary>L2→L3 所需的合格出行次数以 Wiki 配置为准。</summary>
    public static int Level3DepartureCount => GameConfigCatalog.Section<PerkConfig>().PeteLevel3DepartureCount;

    // —— 升级计数持久化旗标（字符串承载整数/布尔，同南丁格尔/耗子）——
    /// <summary>当前连续高饥饿相位计数（工作态：未达到配置门槛即清零；**不是**等级源）。</summary>
    public const string HungerStreakFlag = "pete_hunger_streak";
    /// <summary>是否已达成 L2（latch 永久布尔："true" 后不因 streak 清零倒退）。</summary>
    public const string Level2ReachedFlag = "pete_l2_reached";
    /// <summary>满足配置饥饿门槛出发的**单调**累计次数（只增不减）。</summary>
    public const string DepartureCountFlag = "pete_full_departure_count";

    // ==================== 效果数值纯函数 ====================

    /// <summary>
    /// 皮特的**移速乘子**：按等级读取 Wiki 配置；非皮特返回中性值（零回归）。
    /// 供运行时接线乘进 <c>Actor</c> 移动链（仿 <c>CarryLoadSpeedMult</c> 注入槽），归 pete-runtime 单。
    /// </summary>
    public static double MoveSpeedMultiplier(int peteLevel, bool isPete)
    {
        if (!isPete)
        {
            return 1.0;
        }
        return peteLevel switch
        {
            >= 3 => Level3MoveSpeedMultiplier,
            2 => Level2MoveSpeedMultiplier,
            _ => Level1MoveSpeedMultiplier,
        };
    }

    /// <summary>
    /// 皮特的**操作能力乘子**：L2 起读取 Wiki 配置（累进，L3 保留）。非皮特/L1 返回中性值。
    /// </summary>
    public static double OperationCapabilityMultiplier(int peteLevel, bool isPete)
        => isPete && peteLevel >= 2 ? 1.0 + OperationCapabilityBonus : 1.0;

    /// <summary>
    /// **[通则·乘算] 把 L2 操作加成施加到某人当前操作能力上**：<c>当前实际操作能力 ×（1 + 配置中的操作加成）</c>。
    /// <para>必须乘算、不 clamp 截断（同山姆 <see cref="SamPerk.OperationCapabilityWithAura"/>）：
    /// 满状态者可超过 1；而残缺归零者仍为 0——**没有手的人不会凭空获得操作能力**。</para>
    /// 供 <c>CampMain.EffectiveWorkCapability</c> 一类消费点乘（及于全部操作能力消费点，同山姆消费点集合）。
    /// </summary>
    /// <param name="baseCapability">该角色**当前实际**操作能力（残疾×饥饿×骨折已折算，见 <c>Pawn.OperationCapability</c>）。</param>
    public static double OperationCapabilityWithBonus(double baseCapability, int peteLevel, bool isPete)
        => baseCapability * OperationCapabilityMultiplier(peteLevel, isPete);

    /// <summary>
    /// 皮特受击的**闪避概率**：仅 L3 且负重满足 Wiki 配置门槛时生效，否则为 0。
    /// 边界取严格小于。供 <c>Pawn.EvadeIncoming</c> override 掷免整次攻击（pete-runtime）。
    /// </summary>
    /// <param name="peteLevel">皮特当前等级（未达到三级时恒为 0）。</param>
    /// <param name="carriedKg">该 pawn 当前负重（<c>MemberLoad.CarriedKg</c>，运行时传入）。</param>
    public static double DodgeChance(int peteLevel, double carriedKg)
        => peteLevel >= 3 && carriedKg < DodgeMaxCarriedKg ? DodgeChanceValue : 0.0;

    // ==================== 额外饥饿衰减（注入随机复现） ====================

    /// <summary>
    /// 皮特的**饥饿相位结算变体**（在 <see cref="HungerState.ResolvePhase"/> 的普通衰减基础上，按 Wiki 配置概率再额外衰减）。
    /// <para>
    /// 大男孩代谢快，**不论进食与否**都可能按 Wiki 配置额外衰减；进食仍先走普通结算，再叠加该额外衰减。
    /// 非皮特 → 直接走普通 <see cref="HungerState.ResolvePhase"/>，**不触碰 <paramref name="rng"/>**（不消耗随机流，零回归）。
    /// 进餐前已饿死（终态）不复活、额外衰减被 <see cref="HungerState.Decay"/> clamp 到 0（不跨 0 误杀）。
    /// </para>
    /// 随机走可注入 <see cref="IRandomSource"/>（测试 <see cref="SequenceRandomSource"/> 复现；生产走 <see cref="SystemRandomSource"/>）。
    /// </summary>
    /// <returns>结算后是否饿死（刻度归 0）。</returns>
    public static bool ResolveHungerPhase(HungerState state, bool ate, IRandomSource rng, bool isPete)
    {
        bool starved = state.ResolvePhase(ate);
        if (isPete && !starved && rng.Range(0.0, 1.0) < ExtraHungerDropChance)
        {
            state.Decay();               // 额外衰减（具体幅度由饥饿规则决定）
            starved = state.IsStarved;
        }
        return starved;
    }

    // ==================== 升级条件纯函数 ====================

    /// <summary>读当前连续高饥饿相位计数（未设置/不可解析 → 0）。</summary>
    public static int HungerStreakPhases(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(HungerStreakFlag), out int n) ? n : 0;

    /// <summary>是否已 latch 达成 L2（旗标为 "true"）。</summary>
    public static bool Level2Reached(StoryFlags flags)
        => flags != null && flags.Equals(Level2ReachedFlag, "true");

    /// <summary>读满足配置饥饿门槛出发的单调累计次数（未设置/不可解析 → 0）。</summary>
    public static int DeparturesLogged(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(DepartureCountFlag), out int n) ? n : 0;

    /// <summary>
    /// 喂入**一个相位**的饥饿值，更新连续计数：达到配置门槛 → 计数 +1，未达到 → 清零重记。若计数达
    /// <see cref="Level2ConsecutivePhases"/> 则**latch** <see cref="Level2ReachedFlag"/>（永久，不倒退）。
    /// 返回更新后的连续计数。
    /// </summary>
    public static int RecordPhaseHunger(StoryFlags flags, int phaseHunger)
    {
        if (flags is null)
        {
            return 0;
        }
        int streak = phaseHunger >= HungerThresholdForStreak ? HungerStreakPhases(flags) + 1 : 0;
        flags.Set(HungerStreakFlag, streak.ToString());
        if (streak >= Level2ConsecutivePhases && !Level2Reached(flags))
        {
            flags.Set(Level2ReachedFlag, "true"); // latch：升到 L2 后永久，streak 再清零也不倒退
        }
        return streak;
    }

    /// <summary>
    /// 记一次**出行出发**：出发瞬间饥饿不超过 <see cref="DepartureHungerCeiling"/> 才计数 +1（单调累计）；超过则不计。
    /// 返回更新后的累计次数（不计时返回当前值）。等级是否达 L3 由 <see cref="LevelOf"/> 派生（需先 L2）。
    /// </summary>
    public static int RecordDeparture(StoryFlags flags, int departureHunger)
    {
        if (flags is null || departureHunger > DepartureHungerCeiling)
        {
            return DeparturesLogged(flags!);
        }
        int n = DeparturesLogged(flags) + 1;
        flags.Set(DepartureCountFlag, n.ToString());
        return n;
    }

    /// <summary>
    /// 由两升级量派生等级：未达 L2 → 恒 L1（出行再多也 L1，L3 以 L2 为前提）；达 L2 后累计达到配置门槛 → L3，否则 L2。
    /// 两量皆单调（L2 latch 永久、出行单调累计）⇒ 等级**单调不倒退**。
    /// </summary>
    public static int EvaluateLevel(bool level2Reached, int departures)
    {
        if (!level2Reached)
        {
            return 1;
        }
        return departures >= Level3DepartureCount ? 3 : 2;
    }

    /// <summary>由 <c>StoryFlags</c> 直接取皮特当前等级（读 L2 latch + 出行数 → 派生）。</summary>
    public static int LevelOf(StoryFlags flags) => EvaluateLevel(Level2Reached(flags), DeparturesLogged(flags));
}
