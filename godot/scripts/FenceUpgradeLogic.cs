using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 SalvageLogic.cs / SilentDismantleLogic.cs / SiteActions.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（派人走过去、逐格立墙、重建碰撞体/导航洞/掩体登记）归 Godot 实时层（CampMain），本文件只出**计划 + 数值**。

/// <summary>
/// 一格墙此刻的样子（<b>只有档次和血量比例，没有位置</b>）。
/// <para>
/// ⚠️ <b>「没有位置」是本模块最重要的一条</b>：升级/修复的规则<b>根本不知道墙在哪</b>，
/// 因此它<b>造不出一格新墙</b>——它只能对着一格<b>已经存在的墙</b>说"把它换成 X 档"。
/// 位置只在建图时从 <c>camp.json</c> 读出来一次（<c>CampMain.SplitFence</c>），此后<b>再无第二处诞生围栏的代码</b>。
/// 这就是「墙不能建、只能升级」这条设计底线的<b>结构性保证</b>（不是靠自觉，是靠签名里没有那个参数）。
/// </para>
/// </summary>
/// <param name="Tier">这一格的档次（被砸没了也保留——修复时要知道该按哪一档立回来）。</param>
/// <param name="HealthFraction">血量占比 [0,1]。<b>0 = 这里现在是个洞</b>。</param>
public readonly record struct FenceSegmentState(StructureTier Tier, double HealthFraction)
{
    /// <summary>这一格已经没了（是个洞）。</summary>
    public bool IsHole => HealthFraction <= 0d;

    /// <summary>缺了多少血 [0,1]（洞 = 1，完好 = 0）——修复要付的料与工时都按它折算。</summary>
    public double Missing => Math.Clamp(1d - HealthFraction, 0d, 1d);
}

/// <summary>这趟活是在**加固**还是在**补窟窿**。</summary>
public enum FenceWorkKind
{
    /// <summary>升级：把整条边低于目标档的格全提到目标档（含被砸没的格——直接按新档立回来）。</summary>
    Upgrade,

    /// <summary>修复：把整条边受损/塌掉的格恢复到**它们各自原本的档**（<b>不升档</b>）。</summary>
    Repair,
}

/// <summary>计划里的一步：动哪一格、立成哪档、要多少料、干多少秒。</summary>
public sealed record FenceWorkStep(
    int SegmentIndex,
    StructureTier TargetTier,
    IReadOnlyDictionary<string, int> Cost,
    double WorkSeconds);

/// <summary>
/// 一条边（一面墙 / 一扇大门）的施工计划：动几格、总共要多少料、总共要干多久。
/// <b>空计划 = 没得干</b>（已满档 / 完好无损 / 传进来的边是空的）。
/// </summary>
public sealed record FenceWorkPlan(
    FenceWorkKind Kind,
    StructureTier? TargetTier,
    IReadOnlyList<FenceWorkStep> Steps,
    IReadOnlyDictionary<string, int> TotalCost,
    double TotalWorkSeconds)
{
    /// <summary>没有任何一格要动。</summary>
    public bool IsEmpty => Steps.Count == 0;

    /// <summary>空计划（没得干）。</summary>
    public static FenceWorkPlan None(FenceWorkKind kind)
        => new(kind, null, Array.Empty<FenceWorkStep>(), new Dictionary<string, int>(), 0d);
}

/// <summary>
/// <b>围墙升级 / 修复</b>的纯规则 —— 用户拍板那句「<b>墙不能建，只能升级开局自带的围栏</b>」的唯一落点。
///
/// <para>
/// <b>为什么"不能建"</b>（<b>设计底线，别好心加回来</b>）：可自由摆墙 ⇒ 玩家能搭 kill box（用墙的迷宫牵着敌人寻路，
/// 把一场战斗变成一道几何题），会<b>架空视野锥 / 噪音 / 包抄 / 掩体 / 岗哨</b>一整套系统。见 <see cref="SalvageLogic"/>
/// 里那段注释与 <see cref="StructureBuildCost"/> 的类注。
/// </para>
///
/// <para>
/// <b>那升级凭什么值得？</b>两条硬收益（今天刚建立，别做丢）：
/// <list type="number">
/// <item><b>丧尸要啃更久</b>：围栏已切成一格 100px（<c>CampMain.FenceSegment</c>），每格独立血量 ——
///       升一档 = <b>每一格</b>都更厚（150 → 250 → 400 → 750）。</item>
/// <item><b>劫掠者要拆更久</b>：静默拆一格 45 秒起，<b>每升一档 +20 秒</b>（<see cref="SilentDismantleLogic"/>）
///       ⇒ 直接换成<b>守夜人更多的发现机会</b>（<see cref="SilentDismantleLogic.DetectionRolls"/>）。</item>
/// </list>
/// 且有<b>硬顺序：先大门，后围栏</b>——大门是单独一处（不是 16 格），升起来又便宜又快，而它是敌人最先撞的地方。
/// </para>
///
/// <para>
/// <b>下令按「一条边」，结算按「一格」</b>（取法见 <see cref="PlanUpgrade"/>）。
/// </para>
///
/// <para>
/// <b>⚠️ 升级/修复 = 「把这一格按某档重新立一遍」，旧料一点不退</b> —— 与「墙零回收」（<see cref="SalvageLogic"/>）
/// 同一个口径：你把铁皮钉上去，底下那些木桩不会还给你。故<b>造→拆→造的套利口子根本不存在</b>：墙压根拆不了。
/// </para>
/// </summary>
public static class FenceUpgradeLogic
{
    /// <summary>再小的活也得干一会儿（秒）——不许"点一下墙就好了"。</summary>
    public const int MinWorkSeconds = 5;

    /// <summary>
    /// 这类结构能不能升级/修复：<b>只有围栏和大门</b>——它们正是「开局自带、不可新建」的那两样。
    /// <para>
    /// <b>门体（民居的木门）不走这条路</b>：门是<b>装上去的东西</b>，卸得下来（<see cref="SalvageLogic.CanSalvageStructure"/> 对它为 true），
    /// 它该走的是"拆掉旧门、装一扇新门"那条既有的物品/拆除路，而不是"就地加固一堵墙"。<b>两套机制别混。</b>
    /// </para>
    /// </summary>
    public static bool CanImprove(CampStructureKind kind)
        => kind == CampStructureKind.Fence || kind == CampStructureKind.Gate;

    /// <summary>档次在自己那条阶梯上的级数（基础档 = 0）。</summary>
    public static int TierStep(StructureTier tier) => tier switch
    {
        StructureTier.FenceBasic or StructureTier.GateBasic or StructureTier.DoorWood => 0,
        StructureTier.FenceReinforced or StructureTier.GateSheetMetal or StructureTier.DoorReinforced => 1,
        StructureTier.FenceSheetMetal or StructureTier.GateCastMetal or StructureTier.DoorMetal => 2,
        StructureTier.FenceFullMetal => 3,
        _ => 0,
    };

    /// <summary>
    /// 下一档是什么（<b>一档一档往上升，不许跳档</b>）。已经是顶档 ⇒ <c>null</c>。
    /// <para>
    /// 不许跳档的理由：跳档就等于"攒够全金属的料，一步登天"——中间那两档从此没人做，
    /// 而<b>逐档累加的总成本</b>正是"墙是不可逆的重投入"这句话的重量所在。
    /// </para>
    /// </summary>
    public static StructureTier? NextTier(StructureTier tier) => tier switch
    {
        StructureTier.FenceBasic      => StructureTier.FenceReinforced,
        StructureTier.FenceReinforced => StructureTier.FenceSheetMetal,
        StructureTier.FenceSheetMetal => StructureTier.FenceFullMetal,
        StructureTier.FenceFullMetal  => null,
        StructureTier.GateBasic       => StructureTier.GateSheetMetal,
        StructureTier.GateSheetMetal  => StructureTier.GateCastMetal,
        StructureTier.GateCastMetal   => null,
        _ => null, // 门体不走这条路（见 CanImprove）
    };

    /// <summary>
    /// 把<b>一格</b>墙按该档立起来要干多少<b>工作秒</b>（效率 1.0 的人；<b>拟定待调</b>）。
    /// <para>
    /// 校准锚点（daynight：白天 720 实时秒）：一条 16 格的南墙升到<b>支柱加固</b> = 16 × 45 = <b>720 秒</b>
    /// ＝ <b>一个人一整个白天</b>。这正是要的重量——升一面墙，就是一整天不去搜刮的机会成本。
    /// 而<b>大门只有一处</b>：升到铁皮 150 秒。<b>先大门后围栏的硬顺序，在时间上也自动成立。</b>
    /// </para>
    /// <para>实际耗时 = 本数 ÷ 干活人的工作效率（<c>CampMain.WorkEfficiencyOf</c>，与搜刮/制作同一条乘子链，<b>乘算通则</b>）。</para>
    /// </summary>
    public static int BuildWorkSeconds(StructureTier tier) => tier switch
    {
        StructureTier.FenceBasic      => 30,
        StructureTier.FenceReinforced => 45,
        StructureTier.FenceSheetMetal => 60,
        StructureTier.FenceFullMetal  => 90,
        StructureTier.GateBasic       => 90,
        StructureTier.GateSheetMetal  => 150,
        StructureTier.GateCastMetal   => 240,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "这类结构不能升级/修复（见 CanImprove）"),
    };

    /// <summary>
    /// 把<b>一格</b>墙按该档立起来要多少料 —— <b>纯转发</b> <see cref="StructureBuildCost.Of"/>（唯一真源，不自己造一套）。
    /// </summary>
    public static IReadOnlyDictionary<string, int> BuildCost(StructureTier tier) => StructureBuildCost.Of(tier);

    /// <summary>
    /// 修一格<b>破损</b>的墙要多少料：<b>按缺失血量比例折算</b>（缺一半血 = 半价），<b>向上取整、每样最少 1</b>。
    /// 完好无损 ⇒ 空表（没得修）。
    /// <para>
    /// 向上取整 + 最少 1：修墙<b>永远不是白干的</b>——擦破点皮也得钉两根钉子。这堵死了"故意让墙掉一点血再修"的无聊操作。
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, int> RepairCost(StructureTier tier, double healthFraction)
    {
        double missing = Math.Clamp(1d - healthFraction, 0d, 1d);
        var cost = new Dictionary<string, int>();
        if (missing <= 0d)
        {
            return cost;
        }

        foreach (KeyValuePair<string, int> kv in BuildCost(tier))
        {
            if (kv.Value <= 0)
            {
                continue;
            }
            cost[kv.Key] = Math.Max(1, (int)Math.Ceiling(kv.Value * missing));
        }
        return cost;
    }

    /// <summary>修一格破损的墙要干多少工作秒（同样按缺失血量折算，下限 <see cref="MinWorkSeconds"/>）。完好 ⇒ 0。</summary>
    public static int RepairWorkSeconds(StructureTier tier, double healthFraction)
    {
        double missing = Math.Clamp(1d - healthFraction, 0d, 1d);
        return missing <= 0d
            ? 0
            : Math.Max(MinWorkSeconds, (int)Math.Ceiling(BuildWorkSeconds(tier) * missing));
    }

    /// <summary>
    /// <b>升级一条边</b>：目标档 = 这条边上<b>最低档</b>的下一档；把所有低于目标档的格提上来
    /// （<b>包括被砸没的格</b>——它们直接按新档立回来，一步到位）。
    ///
    /// <para>
    /// <b>为什么下令按「一条边」而不是按「一格」</b>（这是本模块的核心取法）：
    /// <list type="bullet">
    /// <item><b>一格的升级没有意义</b>。丧尸/劫掠者是<b>就近</b>挑一格砸的——你事先不知道它们会撞哪一格。
    ///       "一条防线的强度 = 最弱那一格"，所以只升一格 = 白花料。<b>能改变结果的最小单位是一整面墙。</b></item>
    /// <item>而且按格下令意味着<b>点 16 次</b>（南墙 16 格）——用仪式感惩罚玩家。</item>
    /// </list>
    /// <b>但结算仍按格</b>（<see cref="FenceWorkStep"/> 一步一格）：这样中途被袭营打断，<b>已经立好的格保住</b>，
    /// 没轮到的格保持原样——半面加固的墙是个诚实的结果，不是 bug。
    /// </para>
    ///
    /// <para><b>它自动把墙"抹平"</b>：8 格基础 + 8 格加固的一面墙，升级只动那 8 格基础的 ⇒ 玩家<b>不会掉进"最弱一格"的陷阱</b>。</para>
    /// </summary>
    /// <param name="kind">这条边是围栏还是大门（门体一律空计划）。</param>
    /// <param name="run">
    /// 这条边上的每一格（<b>顺序即下标</b>；<see cref="FenceWorkStep.SegmentIndex"/> 就是这里的下标）。
    /// <b>空列表 ⇒ 空计划</b> —— 没有墙的地方，永远造不出墙来。
    /// </param>
    public static FenceWorkPlan PlanUpgrade(CampStructureKind kind, IReadOnlyList<FenceSegmentState> run)
    {
        if (!CanImprove(kind) || run is null || run.Count == 0)
        {
            return FenceWorkPlan.None(FenceWorkKind.Upgrade);
        }

        // 目标档 = 最低档的下一档（顶档 ⇒ 没得升）。
        StructureTier lowest = run[0].Tier;
        foreach (FenceSegmentState seg in run)
        {
            if (TierStep(seg.Tier) < TierStep(lowest))
            {
                lowest = seg.Tier;
            }
        }
        if (NextTier(lowest) is not { } target)
        {
            return FenceWorkPlan.None(FenceWorkKind.Upgrade);
        }

        var steps = new List<FenceWorkStep>();
        for (int i = 0; i < run.Count; i++)
        {
            if (TierStep(run[i].Tier) >= TierStep(target))
            {
                continue; // 这一格已经够高了，别重复收费
            }
            steps.Add(new FenceWorkStep(i, target, BuildCost(target), BuildWorkSeconds(target)));
        }
        return Assemble(FenceWorkKind.Upgrade, target, steps);
    }

    /// <summary>
    /// <b>修复一条边</b>：把所有破损/塌掉的格恢复到<b>它们各自原本的档</b>（<b>绝不升档</b>——想升档请走 <see cref="PlanUpgrade"/>，
    /// 那条路要付全额）。
    ///
    /// <para>
    /// <b>⚠️ 这是「被砸穿的墙怎么补」的唯一答案，而它必须<b>不是</b>「建墙」</b>：
    /// 本函数只对着<b>传进来的这条边上已经存在的格</b>出计划（步骤下标恒在 <c>[0, run.Count)</c> 内），
    /// <b>永远不会凭空多出一格</b>。玩家能补的洞，只有"原来那儿本来就有墙"的洞 —— kill box 的门从这里就是关死的。
    /// </para>
    /// </summary>
    public static FenceWorkPlan PlanRepair(CampStructureKind kind, IReadOnlyList<FenceSegmentState> run)
    {
        if (!CanImprove(kind) || run is null || run.Count == 0)
        {
            return FenceWorkPlan.None(FenceWorkKind.Repair);
        }

        var steps = new List<FenceWorkStep>();
        for (int i = 0; i < run.Count; i++)
        {
            FenceSegmentState seg = run[i];
            if (seg.Missing <= 0d)
            {
                continue; // 完好，不用修
            }
            steps.Add(new FenceWorkStep(
                i,
                seg.Tier, // **原档**——修复不改档，这是它与升级的分水岭
                RepairCost(seg.Tier, seg.HealthFraction),
                RepairWorkSeconds(seg.Tier, seg.HealthFraction)));
        }

        // 目标档只在"整条边同档"时有意义；混档时给 null（UI 只报格数与总价）。
        StructureTier? target = steps.Count > 0 ? steps[0].TargetTier : null;
        foreach (FenceWorkStep s in steps)
        {
            if (s.TargetTier != target)
            {
                target = null;
                break;
            }
        }
        return Assemble(FenceWorkKind.Repair, target, steps);
    }

    /// <summary>手里的料够不够开这趟工（缺一样都不算够）。</summary>
    public static bool CanAfford(IReadOnlyDictionary<string, int> cost, Func<string, int> materialCount)
    {
        if (cost is null) throw new ArgumentNullException(nameof(cost));
        if (materialCount is null) throw new ArgumentNullException(nameof(materialCount));

        foreach (KeyValuePair<string, int> kv in cost)
        {
            if (materialCount(kv.Key) < kv.Value)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>还缺哪些料、各缺多少（够了 ⇒ 空表）——菜单要把这个写给玩家看，否则他只会以为"这墙升不了"。</summary>
    public static IReadOnlyDictionary<string, int> Missing(
        IReadOnlyDictionary<string, int> cost, Func<string, int> materialCount)
    {
        if (cost is null) throw new ArgumentNullException(nameof(cost));
        if (materialCount is null) throw new ArgumentNullException(nameof(materialCount));

        var lack = new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> kv in cost)
        {
            int gap = kv.Value - materialCount(kv.Key);
            if (gap > 0)
            {
                lack[kv.Key] = gap;
            }
        }
        return lack;
    }

    private static FenceWorkPlan Assemble(FenceWorkKind kind, StructureTier? target, List<FenceWorkStep> steps)
    {
        if (steps.Count == 0)
        {
            return FenceWorkPlan.None(kind);
        }

        var total = new Dictionary<string, int>();
        double seconds = 0d;
        foreach (FenceWorkStep s in steps)
        {
            seconds += s.WorkSeconds;
            foreach (KeyValuePair<string, int> kv in s.Cost)
            {
                total[kv.Key] = total.TryGetValue(kv.Key, out int had) ? had + kv.Value : kv.Value;
            }
        }
        return new FenceWorkPlan(kind, target, steps, total, seconds);
    }
}
