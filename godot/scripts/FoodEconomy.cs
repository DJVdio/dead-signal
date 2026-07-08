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
public readonly record struct FoodDiner(int HungerValue, bool IsWounded = false);

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
/// 食物稀缺经济模型（纯静态函数，无状态、无 Godot 依赖）。数值全部"拟定待调"。
/// </summary>
public static class FoodEconomy
{
    /// <summary>每人一餐默认口粮份数（1 份 = 1 人 1 餐，与 CampResources "1 份=1 人 1 餐"口径一致）。拟定待调。</summary>
    public const int DefaultRationPerCapita = 1;

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
