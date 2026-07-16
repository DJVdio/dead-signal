using DeadSignal.Combat; // IRandomSource（纯 C# 引擎，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 SurvivorPerks.cs / HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// **皮特**·青春期大男孩、曾校田径队（authored 三级专属效果，纯逻辑）。
/// 用户口径（主 agent 已拍板，原话不许引申，数值**非拟定**）：
///   · 一级：移速 = 普通 <see cref="Level1MoveSpeedMultiplier"/>(1.15×)；
///     <see cref="ExtraHungerDropChance"/>(25%) 概率一相位掉 2 饥饿值（**不论几级都常驻**）。
///   · 二级：移速 <see cref="Level2MoveSpeedMultiplier"/>(1.25×)；操作能力 +<see cref="OperationCapabilityBonus"/>(5%)。
///   · 三级：移速 <see cref="Level3MoveSpeedMultiplier"/>(1.3×)；负重 &lt;<see cref="DodgeMaxCarriedKg"/>(30kg) 时受击
///     <see cref="DodgeChanceValue"/>(15%) 概率判定闪避（攻击无效）。
/// 三级效果**累进**（升级不丢下级：L2/L3 保留 L1 的移速台阶与饥饿掉 2；L3 保留 L2 的操作光环）。
///
/// **升级轴**（形态混合，同框架"不要求条件单调"）：
///   · <b>L1→L2「连续五天饥饿≥3」</b>＝**相位级**每相位查：饥饿≥<see cref="HungerThresholdForStreak"/>(3) 连续计数 +1、
///     任一相位 &lt;3 则**清零重记**；连续 <see cref="Level2ConsecutivePhases"/>(10) 相位（＝5 天 ×2 相位/天）不断 → **永久**升到 L2。
///     ⚠️ 连续计数会被清零，故它**不能直接当等级源**（一次饿相位就把已达成的 L2 抹掉是错的）——升到 L2 后**latch**
///     一个永久旗标 <see cref="Level2ReachedFlag"/>，此后 streak 再清零也不倒退（与山姆的"可倒退"相反）。
///   · <b>L2→L3「饥饿=5出行三次」</b>＝出发瞬间饥饿 ≤<see cref="DepartureHungerCeiling"/>(5) 即计数 +1、**单调累计**
///     （只增不减），累计 <see cref="Level3DepartureCount"/>(3) 次 → L3。"作为调查团一员"＝在出行队伍名单里即可（运行时判）。
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

    // —— 三级移速乘子（用户原话，非拟定）——
    /// <summary>一级：移速 = 普通 1.15×。</summary>
    public const double Level1MoveSpeedMultiplier = 1.15;
    /// <summary>二级：移速 1.25×。</summary>
    public const double Level2MoveSpeedMultiplier = 1.25;
    /// <summary>三级：移速 1.3×。</summary>
    public const double Level3MoveSpeedMultiplier = 1.30;

    // —— L2 操作能力 +5%（用户原话）——
    /// <summary>二级：操作能力 +5%（走消费点乘算×1.05、不 clamp，见 <see cref="OperationCapabilityWithBonus"/>）。</summary>
    public const double OperationCapabilityBonus = 0.05;

    // —— L3 闪避（用户原话）——
    /// <summary>三级：受击闪避概率 15%（负重 &lt;30kg 时）。</summary>
    public const double DodgeChanceValue = 0.15;
    /// <summary>三级闪避的负重门槛：当前负重 &lt;30kg 才可闪（30kg=负重免罚线；恰 30kg 不闪）。</summary>
    public const double DodgeMaxCarriedKg = 30.0;

    // —— 饥饿掉 2（用户原话，L1 起常驻不论等级）——
    /// <summary>一相位额外掉 1 饥饿（叠加普通 -1 ⇒ 合计掉 2）的概率：25%。</summary>
    public const double ExtraHungerDropChance = 0.25;

    // —— 升级阈值（用户口径已拍板）——
    /// <summary>连续计数的饥饿下限：相位饥饿 ≥3 才续上连续，&lt;3 清零。</summary>
    public const int HungerThresholdForStreak = 3;
    /// <summary>L1→L2 所需连续相位数：连续 10 相位≥3（＝5 天 ×2 相位/天）。</summary>
    public const int Level2ConsecutivePhases = 10;
    /// <summary>L2→L3 出行计数的饥饿上限：出发瞬间饥饿 ≤5 才计一次。</summary>
    public const int DepartureHungerCeiling = 5;
    /// <summary>L2→L3 所需的合格出行次数：饥饿≤5 出发累计 3 次。</summary>
    public const int Level3DepartureCount = 3;

    // —— 升级计数持久化旗标（字符串承载整数/布尔，同南丁格尔/耗子）——
    /// <summary>当前连续≥3 相位计数（工作态：会被 &lt;3 相位清零；**不是**等级源）。</summary>
    public const string HungerStreakFlag = "pete_hunger_streak";
    /// <summary>是否已达成 L2（latch 永久布尔："true" 后不因 streak 清零倒退）。</summary>
    public const string Level2ReachedFlag = "pete_l2_reached";
    /// <summary>饥饿≤5 出发的**单调**累计次数（只增不减）。</summary>
    public const string DepartureCountFlag = "pete_full_departure_count";

    // ==================== 效果数值纯函数 ====================

    /// <summary>
    /// 皮特的**移速乘子**（1.0=无加成）：L1=1.15 / L2=1.25 / L3=1.30。非皮特恒 1.0（零回归）。
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
    /// 皮特的**操作能力乘子**（1.0=无光环）：L2 起 ×1.05（累进，L3 保留）。非皮特/L1 恒 1.0。
    /// </summary>
    public static double OperationCapabilityMultiplier(int peteLevel, bool isPete)
        => isPete && peteLevel >= 2 ? 1.0 + OperationCapabilityBonus : 1.0;

    /// <summary>
    /// **[通则·乘算] 把 L2 操作 +5% 施加到某人当前操作能力上**：<c>当前实际操作能力 × 1.05</c>。
    /// <para>必须乘算、不 clamp 截断（同山姆 <see cref="SamPerk.OperationCapabilityWithAura"/>）：
    /// 满状态者 1.0×1.05=1.05 应能 &gt;1（否则 +5% 白给）；而残缺归零者 0×1.05=0——**没有手的人凭空得 5% 操作能力**才是荒谬。</para>
    /// 供 <c>CampMain.EffectiveWorkCapability</c> 一类消费点乘（及于全部操作能力消费点，同山姆消费点集合）。
    /// </summary>
    /// <param name="baseCapability">该角色**当前实际**操作能力（残疾×饥饿×骨折已折算，见 <c>Pawn.OperationCapability</c>）。</param>
    public static double OperationCapabilityWithBonus(double baseCapability, int peteLevel, bool isPete)
        => baseCapability * OperationCapabilityMultiplier(peteLevel, isPete);

    /// <summary>
    /// 皮特受击的**闪避概率**（0=不闪）：仅 L3、且当前负重 &lt;30kg → 0.15；否则 0。
    /// 恰 30kg 不闪（30kg=负重免罚线，边界取严格 &lt;）。供 <c>Pawn.EvadeIncoming</c> override 掷免整次攻击（pete-runtime）。
    /// </summary>
    /// <param name="peteLevel">皮特当前等级（&lt;3 恒 0）。</param>
    /// <param name="carriedKg">该 pawn 当前负重（<c>MemberLoad.CarriedKg</c>，运行时传入）。</param>
    public static double DodgeChance(int peteLevel, double carriedKg)
        => peteLevel >= 3 && carriedKg < DodgeMaxCarriedKg ? DodgeChanceValue : 0.0;

    // ==================== 饥饿 25% 掉 2（注入随机复现） ====================

    /// <summary>
    /// 皮特的**饥饿相位结算变体**（在 <see cref="HungerState.ResolvePhase"/> 的普通 -1 基础上，25% 概率再掉 1 ⇒ 合计掉 2）。
    /// <para>
    /// 大男孩代谢快，**不论进食与否**都可能额外掉 1：未进食命中 = 掉 2（5→3）；进食命中 = 净 -1（吃回抵消普通衰减、再额外 -1）。
    /// 非皮特 → 直接走普通 <see cref="HungerState.ResolvePhase"/>，**不触碰 <paramref name="rng"/>**（不消耗随机流，零回归）。
    /// 进餐前已饿死（终态）不复活、额外掉 1 被 <see cref="HungerState.Decay"/> clamp 到 0（不跨 0 误杀）。
    /// </para>
    /// 随机走可注入 <see cref="IRandomSource"/>（测试 <see cref="SequenceRandomSource"/> 复现；生产走 <see cref="SystemRandomSource"/>）。
    /// </summary>
    /// <returns>结算后是否饿死（刻度归 0）。</returns>
    public static bool ResolveHungerPhase(HungerState state, bool ate, IRandomSource rng, bool isPete)
    {
        bool starved = state.ResolvePhase(ate);
        if (isPete && !starved && rng.Range(0.0, 1.0) < ExtraHungerDropChance)
        {
            state.Decay();               // 额外 -1（本相位合计掉 2）
            starved = state.IsStarved;
        }
        return starved;
    }

    // ==================== 升级条件纯函数 ====================

    /// <summary>读当前连续≥3 相位计数（未设置/不可解析 → 0）。</summary>
    public static int HungerStreakPhases(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(HungerStreakFlag), out int n) ? n : 0;

    /// <summary>是否已 latch 达成 L2（旗标为 "true"）。</summary>
    public static bool Level2Reached(StoryFlags flags)
        => flags != null && flags.Equals(Level2ReachedFlag, "true");

    /// <summary>读饥饿≤5 出发的单调累计次数（未设置/不可解析 → 0）。</summary>
    public static int DeparturesLogged(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(DepartureCountFlag), out int n) ? n : 0;

    /// <summary>
    /// 喂入**一个相位**的饥饿值，更新连续计数：≥3 → 计数 +1，&lt;3 → 清零重记。若计数达
    /// <see cref="Level2ConsecutivePhases"/>(10) 则**latch** <see cref="Level2ReachedFlag"/>（永久，不倒退）。
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
    /// 记一次**出行出发**：出发瞬间饥饿 ≤<see cref="DepartureHungerCeiling"/>(5) 才计数 +1（单调累计）；&gt;5 不计。
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
    /// 由两升级量派生等级：未达 L2 → 恒 L1（出行再多也 L1，L3 以 L2 为前提）；达 L2 后出行累计 ≥3 → L3，否则 L2。
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
