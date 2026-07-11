using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs / CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 食物稀缺经济模型：把"营地共享库存 vs 全员口粮需求"做成真稀缺压迫。
// 本模型只出"谁该吃到 / 谁该挨饿 / 库存怎么扣 / 还能撑几相"，**不**实现饥饿→虚弱→营养不良→死
// （那条链全在 HungerState：各级能力惩罚 + IsStarved 饿死）。接入层拿本模型的 Fed[] 逐人喂给
// HungerState.ResolvePhase(ate)，让挨饿者刻度 -1，自然走既有惩罚阶梯与饿死终态。

/// <summary>
/// 分粮策略（库存 &lt; 总需求时谁先吃）。均为稳定排序（同权按原名单序），保证结算可复现。
/// "玩家指定"不单列策略：给 <see cref="FoodEconomy.Allocate"/> 传 playerPriority 覆盖策略排序即可。
/// </summary>
public enum RationStrategy
{
    /// <summary>按名单原序（先到先吃）——等价既有 ConsumeMeal 的行为，作接入迁移基线。</summary>
    AsGiven,

    /// <summary>先喂最饿的（饥饿刻度最低者优先）——把口粮压在濒死者身上，最大化"少死人"。</summary>
    HungriestFirst,

    /// <summary>先喂伤员（伤员优先养伤）；伤员组内再按最饿优先，健康者垫后。</summary>
    WoundedFirst,
}

/// <summary>
/// 单个进餐者的分粮输入（纯值对象，与 Pawn/Actor 解耦）：接入层把每个存活幸存者映射成本结构。
/// </summary>
/// <param name="HungerValue">当前饥饿刻度序号（见 <see cref="HungerLevel"/>，0=饿死…5=正常…6=吃撑）。越小越饿。</param>
/// <param name="IsWounded">是否伤员（供 <see cref="RationStrategy.WoundedFirst"/> 优先；由接入层据引擎真实伤情判定）。</param>
/// <param name="Cap">该进餐者饥饿上限（普通 5、"大胃袋" 6）。供补餐判"是否掉档(<see cref="HungerValue"/> &lt; Cap)"，避免给已满者浪费口粮。</param>
public readonly record struct FoodDiner(int HungerValue, bool IsWounded = false, int Cap = HungerState.DefaultCap);

/// <summary>
/// 一次分粮结算的只读明细。<see cref="Fed"/> 按**输入名单原序**对齐（第 i 位对应第 i 个进餐者），
/// 接入层据此逐人 <c>HungerState.ResolvePhase(Fed[i])</c>。<see cref="Consumed"/>/<see cref="Remaining"/>
/// 在每份口粮=1（默认）时与 <c>CampResources.ConsumeMeal</c> 的扣减完全一致，仅"谁吃到"的取舍不同。
/// </summary>
/// <param name="HeadCount">进餐人数。</param>
/// <param name="RationPerCapita">每人一餐所需口粮份数。</param>
/// <param name="Demand">总需求份数 = HeadCount × RationPerCapita。</param>
/// <param name="Stock">结算前库存份数。</param>
/// <param name="Consumed">实际消耗份数 = FedCount × RationPerCapita。</param>
/// <param name="Remaining">扣减后余量份数。</param>
/// <param name="FedCount">吃到饭的人数。</param>
/// <param name="StarvedCount">挨饿人数（吃不上饭 = 本相位饥饿将 -1）。</param>
/// <param name="Shortfall">缺口份数 = Demand − 实际能覆盖到的需求 = StarvedCount × RationPerCapita。</param>
/// <param name="Fed">按输入原序，各人本相位是否吃到。</param>
public readonly record struct RationOutcome(
    int HeadCount,
    int RationPerCapita,
    int Demand,
    int Stock,
    int Consumed,
    int Remaining,
    int FedCount,
    int StarvedCount,
    int Shortfall,
    IReadOnlyList<bool> Fed)
{
    /// <summary>库存是否够全员吃饱（无人挨饿）。</summary>
    public bool Sufficient => StarvedCount == 0;
}

/// <summary>
/// 一相位"一餐 + 可选补餐"的完整分粮明细（见 <see cref="FoodEconomy.AllocatePhaseMeal"/>）。
/// <see cref="First"/> 是第一餐结算（谁吃到=净零维持）；<see cref="SecondFed"/> 按输入原序，各人本相位是否补到第二餐（=净 +1 回升）。
/// 接入层：第一餐 <c>ResolvePhase(First.Fed[i])</c>、补餐 <c>Feed()</c>（对 SecondFed[i]），并按 <see cref="TotalConsumed"/> 实扣库存。
/// </summary>
/// <param name="First">第一餐分粮结算（既有 <see cref="RationOutcome"/> 语义）。</param>
/// <param name="SecondFed">按输入原序，各人是否补到第二餐（+1 回升）。</param>
/// <param name="SecondCount">补到第二餐的人数。</param>
/// <param name="SecondConsumed">补餐消耗份数 = SecondCount × 每人口粮。</param>
/// <param name="Remaining">第一餐 + 补餐全部扣完后的余量份数。</param>
public readonly record struct PhaseMealOutcome(
    RationOutcome First,
    IReadOnlyList<bool> SecondFed,
    int SecondCount,
    int SecondConsumed,
    int Remaining)
{
    /// <summary>本相位聚餐总消耗份数（第一餐 + 补餐）。接入层据此实扣库存。</summary>
    public int TotalConsumed => First.Consumed + SecondConsumed;
}

/// <summary>
/// 食物稀缺经济模型（纯静态函数，无状态、无 Godot 依赖）。数值全部"拟定待调"。
/// </summary>
public static class FoodEconomy
{
    /// <summary>每人一餐默认口粮份数（1 份 = 1 人 1 餐，与 CampResources "1 份=1 人 1 餐"口径一致）。拟定待调。</summary>
    public const int DefaultRationPerCapita = 1;

    /// <summary>每人每餐最多份数（用户拍板"一个人一次最多吃两份食物"：第一餐+一份补餐）。拟定待调。</summary>
    public const int MaxServingsPerMeal = 2;

    /// <summary>
    /// 玩家手动分配的聚餐结算（"换壳不换账目"）：把每人指定份数（0..<see cref="MaxServingsPerMeal"/>）
    /// 落成与 <see cref="AllocatePhaseMeal"/> 同构的 <see cref="PhaseMealOutcome"/>——
    /// <c>Fed[i]=份数≥1</c>（第一餐，净零维持）、<c>SecondFed[i]=份数≥2</c>（补餐，净 +1）。
    /// 接入层照旧对 <c>First.Fed[i]</c> 走 <c>ResolvePhase</c>、对 <c>SecondFed[i]</c> 走 <c>ServeSecondMeal</c>，
    /// 故"进分配环节 -1、吃一份 +1、至多两份"的账目与既有净零模型完全同构（面板显示的是 decay 前当前刻度）。
    /// 防御性 clamp：每人份数夹到 [0,Max]；饿死终态者（刻度 ≤ <see cref="HungerState.StarvedValue"/>）强制 0 份（救不活不浪费）；
    /// 库存不足以覆盖全部指定份数时，先满足所有第一餐（原名单序）再满足补餐，逐份扣到库存告罄。
    /// **不**改任何饥饿状态——只出决策明细，由接入层施加。
    /// </summary>
    /// <param name="stock">当前营地食物库存份数（负数按 0）。</param>
    /// <param name="diners">存活进餐者名单（各输出 Fed/SecondFed 与之原序对齐）；null/空视为 0 人。</param>
    /// <param name="servings">玩家指定的每人份数（与 diners 原序对齐；缺项/越界按 0，脏值会被防御性 clamp）。</param>
    /// <param name="rationPerCapita">每份口粮份数（&lt;1 按 1）。</param>
    public static PhaseMealOutcome ResolveManual(
        int stock,
        IReadOnlyList<FoodDiner>? diners,
        IReadOnlyList<int>? servings,
        int rationPerCapita = DefaultRationPerCapita)
    {
        stock = Math.Max(0, stock);
        int ration = Math.Max(1, rationPerCapita);
        int n = diners?.Count ?? 0;
        var fed = new bool[n];
        var secondFed = new bool[n];
        if (n == 0)
        {
            var empty = new RationOutcome(0, ration, 0, stock, 0, stock, 0, 0, 0, fed);
            return new PhaseMealOutcome(empty, secondFed, 0, 0, stock);
        }

        int Want(int i)
        {
            int w = servings != null && i < servings.Count ? servings[i] : 0;
            w = Math.Clamp(w, 0, MaxServingsPerMeal);
            if (diners![i].HungerValue <= HungerState.StarvedValue)
            {
                w = 0; // 饿死终态：进食不复活，不分配口粮
            }
            return w;
        }

        // 第一轮：保障所有"想吃第一餐"者的第一餐（原序），确保"人人先有第一餐"再谈补餐。
        int remaining = stock;
        for (int i = 0; i < n; i++)
        {
            if (Want(i) >= 1 && remaining >= ration)
            {
                fed[i] = true;
                remaining -= ration;
            }
        }

        // 第二轮：给"已吃第一餐 且 想吃第二份"者补餐（原序），库存不够则先到先补。
        int secondCount = 0;
        int secondConsumed = 0;
        for (int i = 0; i < n; i++)
        {
            if (Want(i) >= 2 && fed[i] && remaining >= ration)
            {
                secondFed[i] = true;
                remaining -= ration;
                secondConsumed += ration;
                secondCount++;
            }
        }

        int fedCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (fed[i]) fedCount++;
        }
        int consumed = fedCount * ration;
        int starved = n - fedCount;
        int shortfall = n * ration - consumed;
        var first = new RationOutcome(
            n, ration, n * ration, stock, consumed, stock - consumed, fedCount, starved, shortfall, fed);
        return new PhaseMealOutcome(first, secondFed, secondCount, secondConsumed, remaining);
    }

    /// <summary>
    /// 校验一份玩家分配是否"干净"（无需 clamp 即合法）：<c>servings</c> 长度须等于人数；每人份数 ∈ [0,Max]；
    /// 饿死终态者未被分配；且总份数 × 口粮 ≤ 库存。UI 据此启用/禁用"确认"（脏分配即便走
    /// <see cref="ResolveManual"/> 会被防御性 clamp，但不应放行确认）。
    /// </summary>
    public static bool IsAllocationValid(
        int stock,
        IReadOnlyList<FoodDiner>? diners,
        IReadOnlyList<int>? servings,
        int rationPerCapita = DefaultRationPerCapita)
    {
        int n = diners?.Count ?? 0;
        int ration = Math.Max(1, rationPerCapita);
        if ((servings?.Count ?? 0) != n)
        {
            return false; // 长度须与人数一一对应（防越界/漏项）
        }
        int total = 0;
        for (int i = 0; i < n; i++)
        {
            int w = servings![i];
            if (w < 0 || w > MaxServingsPerMeal)
            {
                return false;
            }
            if (w > 0 && diners![i].HungerValue <= HungerState.StarvedValue)
            {
                return false; // 给饿死者分配非法
            }
            total += w;
        }
        return total * ration <= Math.Max(0, stock);
    }

    /// <summary>
    /// 分配面板预填：按现行自动分粮策略（先保第一餐 → 余粮补最饿）给出每人建议份数（0/1/2），
    /// 玩家在此基础上用 +/- 微调。等于 <see cref="AllocatePhaseMeal"/> 的 <c>Fed + SecondFed</c> 折成份数，
    /// 故预填必是一份"干净"分配（<see cref="IsAllocationValid"/> 恒真）。
    /// </summary>
    public static int[] Prefill(
        int stock,
        IReadOnlyList<FoodDiner>? diners,
        bool allowSeconds = true,
        RationStrategy strategy = RationStrategy.HungriestFirst,
        int rationPerCapita = DefaultRationPerCapita)
    {
        int n = diners?.Count ?? 0;
        var result = new int[n];
        PhaseMealOutcome phase = AllocatePhaseMeal(stock, diners, allowSeconds, strategy, rationPerCapita);
        for (int i = 0; i < n; i++)
        {
            result[i] = (phase.First.Fed[i] ? 1 : 0) + (phase.SecondFed[i] ? 1 : 0);
        }
        return result;
    }

    /// <summary>
    /// 一相位聚餐分粮结算（核心）：库存不足以喂饱全员时，按 <paramref name="strategy"/>（或 <paramref name="playerPriority"/>）
    /// 决定谁吃到、谁挨饿，并给出扣减后余量与缺口。**不**改任何饥饿状态——只出决策明细，由接入层施加。
    /// </summary>
    /// <param name="stock">当前营地食物库存份数（负数按 0）。</param>
    /// <param name="diners">存活进餐者名单（Fed[] 与之原序对齐）；null/空 视为 0 人。</param>
    /// <param name="strategy">分粮策略（默认先喂最饿=少死人）。</param>
    /// <param name="rationPerCapita">每人一餐口粮份数（&lt;1 按 1）。</param>
    /// <param name="playerPriority">玩家指定的优先吃名单（diners 的下标，按优先次序）；非空则覆盖 <paramref name="strategy"/> 排序，
    /// 未列入者按原序垫后。越界/重复下标自动忽略。</param>
    public static RationOutcome Allocate(
        int stock,
        IReadOnlyList<FoodDiner>? diners,
        RationStrategy strategy = RationStrategy.HungriestFirst,
        int rationPerCapita = DefaultRationPerCapita,
        IReadOnlyList<int>? playerPriority = null)
    {
        stock = Math.Max(0, stock);
        int ration = Math.Max(1, rationPerCapita);
        int n = diners?.Count ?? 0;
        int demand = n * ration;

        var fed = new bool[n];
        if (n == 0)
        {
            return new RationOutcome(0, ration, 0, stock, 0, stock, 0, 0, 0, fed);
        }

        // 进餐次序：玩家指定优先，否则按策略排序（均稳定：同权保持原名单序）。
        IEnumerable<int> order = playerPriority != null
            ? FeedingOrderFromPriority(playerPriority, n)
            : FeedingOrder(diners!, strategy);

        int remaining = stock;
        int fedCount = 0;
        foreach (int i in order)
        {
            if (remaining < ration)
            {
                continue; // 这一份喂不起，跳过（后面可能有需求更小者？口粮均等，故其余也喂不起，但保持遍历语义简单）
            }
            fed[i] = true;
            remaining -= ration;
            fedCount++;
        }

        int consumed = fedCount * ration;
        int starved = n - fedCount;
        int shortfall = demand - consumed; // = starved * ration
        return new RationOutcome(n, ration, demand, stock, consumed, remaining, fedCount, starved, shortfall, fed);
    }

    /// <summary>
    /// 一相位"一餐 + 可选补餐"的完整分粮结算（补餐回升机制，用户拍板"应该能补回来"）：
    /// 先按 <paramref name="strategy"/> 分第一餐（<see cref="Allocate"/> 既有语义，吃到=净零维持）；
    /// 若 <paramref name="allowSeconds"/> 且第一餐后仍有余粮，把余粮补给**已吃到第一餐且掉档（刻度 &lt; Cap）**的存活者，
    /// 最饿（刻度最低）优先、同刻度原序、每人至多补 1 餐。补餐 = 净 +1 回升（接入层对补到者调 <c>HungerState.Feed()</c>）。
    /// 只补掉档者 → 全员满档时不动余粮（物资不浪费）；第一餐未保住全员（库存告罄）时无余粮 → 一律不补。
    /// **不**改任何饥饿状态——只出决策明细，由接入层施加（第一餐 <c>ResolvePhase(Fed[i])</c>、补餐 <c>Feed()</c>），并按 <see cref="PhaseMealOutcome.TotalConsumed"/> 实扣库存。
    /// </summary>
    /// <param name="stock">当前营地食物库存份数（负数按 0）。</param>
    /// <param name="diners">存活进餐者名单（各输出 Fed/SecondFed 与之原序对齐）；null/空视为 0 人。</param>
    /// <param name="allowSeconds">是否启用补餐（玩家可关以囤粮）。false 时余粮原样保留、无人补餐。</param>
    /// <param name="strategy">第一餐分粮策略（默认先喂最饿=少死人）。</param>
    /// <param name="rationPerCapita">每人一餐口粮份数（&lt;1 按 1）；补餐同样按此份数。</param>
    /// <param name="firstPriority">第一餐的玩家指定优先名单（透传给 <see cref="Allocate"/> 的 playerPriority）。</param>
    public static PhaseMealOutcome AllocatePhaseMeal(
        int stock,
        IReadOnlyList<FoodDiner>? diners,
        bool allowSeconds,
        RationStrategy strategy = RationStrategy.HungriestFirst,
        int rationPerCapita = DefaultRationPerCapita,
        IReadOnlyList<int>? firstPriority = null)
    {
        RationOutcome first = Allocate(stock, diners, strategy, rationPerCapita, firstPriority);
        int n = diners?.Count ?? 0;
        var secondFed = new bool[n];

        // 补餐仅在：开启 + 有人 + 第一餐后余粮够一份 时进行（第一餐未保住全员则余粮必为 0，天然不补）。
        if (!allowSeconds || n == 0 || first.Remaining < first.RationPerCapita)
        {
            return new PhaseMealOutcome(first, secondFed, 0, 0, first.Remaining);
        }

        int ration = first.RationPerCapita;
        // 候选：已吃到第一餐 且 掉档（刻度 < 该人 Cap，补了才涨得动）。最饿优先、同刻度原序（稳定）。
        var candidates = Enumerable.Range(0, n)
            .Where(i => first.Fed[i] && diners![i].HungerValue < diners[i].Cap)
            .OrderBy(i => diners![i].HungerValue);

        int remaining = first.Remaining;
        int secondCount = 0;
        int secondConsumed = 0;
        foreach (int i in candidates)
        {
            if (remaining < ration)
            {
                break; // 余粮不够再补一份
            }
            secondFed[i] = true;   // 每人至多在此置一次 → 每相至多补 1 餐
            remaining -= ration;
            secondConsumed += ration;
            secondCount++;
        }

        return new PhaseMealOutcome(first, secondFed, secondCount, secondConsumed, remaining);
    }

    /// <summary>按策略生成进餐次序（返回 diners 的下标序列，稳定排序）。</summary>
    private static IEnumerable<int> FeedingOrder(IReadOnlyList<FoodDiner> diners, RationStrategy strategy)
    {
        var idx = Enumerable.Range(0, diners.Count);
        return strategy switch
        {
            // 最饿(刻度最低)优先；同刻度按原序（稳定）。
            RationStrategy.HungriestFirst =>
                idx.OrderBy(i => diners[i].HungerValue),
            // 伤员优先；伤员/健康各组内再最饿优先；同权原序。
            RationStrategy.WoundedFirst =>
                idx.OrderBy(i => diners[i].IsWounded ? 0 : 1)
                   .ThenBy(i => diners[i].HungerValue),
            // AsGiven：原名单序（先到先吃）。
            _ => idx,
        };
    }

    /// <summary>玩家指定优先名单 → 完整进餐次序：先按玩家给的有效去重下标，未列入者按原序垫后。</summary>
    private static IEnumerable<int> FeedingOrderFromPriority(IReadOnlyList<int> priority, int n)
    {
        var seen = new bool[n];
        foreach (int i in priority)
        {
            if (i >= 0 && i < n && !seen[i])
            {
                seen[i] = true;
                yield return i;
            }
        }
        for (int i = 0; i < n; i++)
        {
            if (!seen[i])
            {
                yield return i;
            }
        }
    }

    /// <summary>
    /// 一相位聚餐的总口粮需求份数（存活人数 × 每人口粮）。给 UI/预警用。
    /// </summary>
    public static int DemandFor(int headCount, int rationPerCapita = DefaultRationPerCapita)
        => Math.Max(0, headCount) * Math.Max(1, rationPerCapita);

    /// <summary>
    /// 短缺趋势预警：按当前库存与每相位消耗，还能**全员吃饱几个相位**（超过后开始有人挨饿）。
    /// 一"昼夜"含黎明/黄昏两相（两餐），故 天数 ≈ 本值 / 2（见 <see cref="DaysUntilShortfall"/>）。
    /// 无人进餐（headCount≤0）时永不短缺，返回 <see cref="int.MaxValue"/>。假设人数/消耗不变（粗略趋势，非精确模拟）。
    /// </summary>
    public static int PhasesUntilShortfall(int stock, int headCount, int rationPerCapita = DefaultRationPerCapita)
    {
        int perPhase = DemandFor(headCount, rationPerCapita);
        if (perPhase <= 0)
        {
            return int.MaxValue; // 没人吃 → 永不短缺
        }
        return Math.Max(0, stock) / perPhase;
    }

    /// <summary>短缺趋势（昼夜口径）：还能全员吃饱几个整昼夜（每昼夜两餐）。见 <see cref="PhasesUntilShortfall"/>。</summary>
    public static int DaysUntilShortfall(int stock, int headCount, int rationPerCapita = DefaultRationPerCapita)
    {
        int phases = PhasesUntilShortfall(stock, headCount, rationPerCapita);
        return phases == int.MaxValue ? int.MaxValue : phases / 2;
    }
}
