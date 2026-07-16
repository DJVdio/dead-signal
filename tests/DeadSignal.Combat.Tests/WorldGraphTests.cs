using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T57] 调查点**网状解锁图**：门槛规则 + **真数据**结构校验 + **消费层覆盖自检**。
///
/// <para>用户拍板的规格（原话）：「调查点应当是网状结构，需要去过前置的调查点且探索度大于50%，才能够走到之后的调查点。
/// 开局可以选择向东或者向西北的两个简单前期探查点，随后在这两条路径上扩散出去，把现有的调查点按照难度和剧情加入进去。」</para>
///
/// <para>🔴 本文件里最重要的不是纯函数那几条，是**拿真 <c>godot/data/world_graph.json</c> 跑的那几条**：
/// 纯逻辑全绿、消费层没接线 ⇒ 功能根本不存在。所以这里既跑真数据的图校验（反死锁/反孤岛/反环），
/// 也源码守卫 <c>WorldMapPanel</c> 真的把闸门接上了。</para>
/// </summary>
public class WorldGraphTests
{
    // ═══════════════════════ 门槛规则（纯函数）═══════════════════════

    [Theory]
    // 严格大于 50%：一半整不算数。
    [InlineData(0, 5, false)]
    [InlineData(2, 5, false)]  // 40%
    [InlineData(3, 5, true)]   // 60%
    [InlineData(5, 10, false)] // 恰好 50% ⇒ **不**算数（用户原话"大于50%"）
    [InlineData(6, 10, true)]  // 60%
    [InlineData(15, 30, false)]
    [InlineData(16, 30, true)]
    [InlineData(0, 0, false)]  // 没有可调查的点位 ⇒ 永远过不了门槛（死锁源头）
    public void 探索度门槛_严格大于一半(int done, int total, bool expected)
        => Assert.Equal(expected, WorldGraphUnlock.MeetsExplorationThreshold(done, total));

    [Fact]
    public void 前置满足_必须同时是去过与探索度过半_两个条件缺一不可()
    {
        var graph = RealGraph();
        var flags = new StoryFlags();

        // 消防站 5 处搜刮点。只"去过"、一处没搜 ⇒ 不满足。
        var visited = new HashSet<string> { "消防站" };
        Assert.False(WorldGraphUnlock.PrereqSatisfied("消防站", visited, flags, false));

        // 去过 + 只搜了 2/5（40%）⇒ 仍然不满足（这正是用户要的：踩一脚就走不算数）。
        SearchN("消防站", flags, 2);
        Assert.False(WorldGraphUnlock.PrereqSatisfied("消防站", visited, flags, false));

        // 去过 + 搜到 3/5（60%）⇒ 满足。
        SearchN("消防站", flags, 3);
        Assert.True(WorldGraphUnlock.PrereqSatisfied("消防站", visited, flags, false));

        // 探索度够了、但**没去过**（凭空满足 flag）⇒ 仍然不满足。
        Assert.False(WorldGraphUnlock.PrereqSatisfied("消防站", new HashSet<string>(), flags, false));
    }

    [Fact]
    public void 起点开局即可去_其余一律锁着()
    {
        var graph = RealGraph();
        var none = new HashSet<string>();
        var flags = new StoryFlags();

        foreach (var n in graph.Nodes)
        {
            var st = WorldGraphUnlock.StateOf(graph, n.Name, none, flags, false);
            if (n.IsStart)
            {
                Assert.True(st.Unlocked, $"起点「{n.Display}」开局必须能去");
            }
            else
            {
                Assert.False(st.Unlocked, $"非起点「{n.Display}」开局必须锁着");
                // 🔴 锁着必须**说清为什么**——"锁着的点干脆不显示/不解释"会让玩家不知道有路可走。
                Assert.False(string.IsNullOrWhiteSpace(st.Reason), $"「{n.Display}」锁着却没给理由");
                Assert.Contains("50%", st.Reason);
            }
        }
    }

    [Fact]
    public void 任一前置满足即解锁_这就是网状汇合()
    {
        var graph = RealGraph();
        // 城市之巅瞭望观景台：前置 超市 或 东部新村(住宅区)，requireAll=false ⇒ 走哪条路都能到。
        var lookout = graph.Find("城市之巅瞭望观景台")!;
        Assert.False(lookout.RequireAll);
        Assert.Equal(2, lookout.Prereq.Count);

        // 只走西北线（超市）。
        var f1 = new StoryFlags();
        var v1 = new HashSet<string> { "消防站", "超市" };
        SearchAll("超市", f1);
        Assert.True(WorldGraphUnlock.CanTravelTo(graph, "城市之巅瞭望观景台", v1, f1, false));

        // 只走东线（东部新村）。
        var f2 = new StoryFlags();
        var v2 = new HashSet<string> { "河边小屋", "住宅区" };
        SearchAll("住宅区", f2);
        Assert.True(WorldGraphUnlock.CanTravelTo(graph, "城市之巅瞭望观景台", v2, f2, false));
    }

    [Fact]
    public void 终局广播台_两条证据链都得走完_全图唯一的全部前置()
    {
        var graph = RealGraph();
        var radio = graph.Find("广播台")!;
        Assert.True(radio.RequireAll);

        // 全图**只有**广播台是「全部前置」——这个资格是终局独有的。
        Assert.Equal(new[] { "广播台" }, graph.Nodes.Where(n => n.RequireAll).Select(n => n.Name).ToArray());

        var flags = new StoryFlags();
        var visited = new HashSet<string>();

        // 只走完【西北链】（破败教堂：军方做了什么）⇒ 仍然锁着，缺【东链】。
        visited.Add("破败教堂");
        SearchAll("破败教堂", flags);
        var st = WorldGraphUnlock.StateOf(graph, radio.Name, visited, flags, false);
        Assert.False(st.Unlocked);
        Assert.Contains("斯图尔特家族庄园", st.Reason);

        // 两条链都走完 ⇒ 开。在你决定要不要回复军方之前，你已经看过军方干了什么、人干了什么。
        visited.Add("斯图尔特家族庄园");
        SearchAll("斯图尔特家族庄园", flags);
        Assert.True(WorldGraphUnlock.CanTravelTo(graph, radio.Name, visited, flags, false));
    }

    [Fact]
    public void 破败教堂是广播台的强制前置_不看过军方干了什么就不许按那个按钮()
    {
        var graph = RealGraph();
        var radio = graph.Find("广播台")!;
        // 广播台 = 回复军方（结局②：军方来屠杀你的营地）／呼叫南方（结局③）的岔口。
        // 破败教堂 = 军方留下的忏悔录 + 被军方屠杀的人用血写的辱骂。
        // 🔴 它必须是**强制**前置（requireAll）—— 全游戏最重的一次道德抉择，不能是盲选。
        Assert.Contains("破败教堂", radio.Prereq);
        Assert.True(radio.RequireAll);
    }

    [Fact]
    public void 金手指帮的线索链成立_只能从守林人小屋进去()
    {
        var graph = RealGraph();
        var gf = graph.Find(ExplorationProgress.GoldfingerBaseName)!;
        // 哥顿的上吊尸 + 日记 B 在守林人小屋 —— 那是玩家得知"这伙人是谁、在哪"的唯一途径。
        // 🔴 单前置是**刻意的**：给它加任何 OR 前置，玩家就能绕开那具尸体，线索白挂。
        Assert.Equal(new[] { ExplorationProgress.WatchersCabinName }, gf.Prereq.ToArray());

        // 网状性由守林人小屋自己承担：它是两路可达的汇合点 ⇒ 两条路都能通到金手指帮。
        var cabin = graph.Find(ExplorationProgress.WatchersCabinName)!;
        Assert.True(cabin.Prereq.Count >= 2);
        Assert.False(cabin.RequireAll);
    }

    [Fact]
    public void 去过的点永远保持解锁_重排图不会把在玩的档锁进矛盾态()
    {
        var graph = RealGraph();
        var flags = new StoryFlags();

        // 场景：玩家在**旧图**下去过广播台（那时它才深度 4）。现在图重排了，广播台成了终局、
        // 前置换成"破败教堂 且 斯图尔特庄园"，两个都没去过 ⇒ 按前置算它该锁着。
        var visited = new HashSet<string> { "广播台" };

        // 🔴 但它必须仍然开着 —— 玩家已经去过了，路踩出来了。
        // 否则会出现"这个点我明明去过，现在却锁着"（关内 flag 还在、探索度还在涨，门却关了）。
        Assert.True(WorldGraphUnlock.CanTravelTo(graph, "广播台", visited, flags, false));

        // 没去过的点不受影响，照锁不误。
        Assert.False(WorldGraphUnlock.CanTravelTo(graph, "破败教堂", visited, flags, false));
    }

    [Fact]
    public void 老存档兜底_视为全部已解锁_不剥夺已有进度()
    {
        var graph = RealGraph();
        var none = new HashSet<string>();
        var flags = new StoryFlags();
        foreach (var n in graph.Nodes)
            Assert.True(WorldGraphUnlock.CanTravelTo(graph, n.Name, none, flags, false, legacyFullUnlock: true));
    }

    [Fact]
    public void 图外的目的地不设门_不凭空拦住别人的功能()
    {
        var graph = RealGraph();
        Assert.True(WorldGraphUnlock.CanTravelTo(graph, "某个还没进图的新点", new HashSet<string>(), new StoryFlags(), false));
    }

    // ═══════════════════════ 真数据（godot/data/world_graph.json）═══════════════════════

    /// <summary>
    /// 🔴 <b>关卡本体还没落地的点</b>（<c>ExplorationCache</c> 里还没有它们的搜刮点 ⇒ 探索度恒为 0/0 ⇒
    /// <b>永远过不了 50% 门槛</b> ⇒ 它后面的整片图锁死）。
    ///
    /// <para><b>这是一个真实存在的、已知的临时死锁</b>，不是被容忍的技术债：
    /// 难民营地 → 破败教堂 → 广播台（终局）这条链现在<b>走不通</b>，游戏此刻通不了关。
    /// 三个点的关卡本体正由 <c>impl-lategame</c>（破败教堂/难民营地）与 <c>impl-sewer</c>（下水道）在做。</para>
    ///
    /// <para>⚠️ <b>这份清单只许缩短，不许变长</b>：往图里加一个没有关卡内容的点，就是往玩家路上埋一堵墙。</para>
    ///
    /// <para>✅ <b>已清空</b>：下水道（impl-sewer）/ 难民营地 · 破败教堂（impl-lategame）的搜刮点都已接进
    /// <c>ExplorationCache.CacheIdsFor</c> ⇒ 那条临时死锁已经不存在，
    /// <see cref="真数据_图结构健康_无环无孤岛无死锁"/> 现在是**不打折的硬门**。
    /// 这条清单留着当闸：以后谁再往图里加一个没有关卡内容的点，当场红。</para>
    /// </summary>
    private static readonly string[] PendingLevelContent = System.Array.Empty<string>();

    [Fact]
    public void 真数据_关卡本体待接线清单_只许缩短不许变长()
    {
        var graph = RealGraph();
        var pending = graph.Nodes
            .Where(n => RegisteredPointCount(n.Name) == 0)
            .Select(n => n.Name)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var known = PendingLevelContent.OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var unexpected = pending.Except(known).ToArray();

        Assert.True(unexpected.Length == 0,
            "🔴 这些点进了世界图，却没有任何可调查的点位 ⇒ 探索度永远 0/0 ⇒ 它后面的图全部锁死：\n  "
            + string.Join("\n  ", unexpected)
            + "\n往图里加点之前，先把它的搜刮点落进 ExplorationCache.CacheIdsFor。");

        // 反向提醒：某个点接完线了，就把它从 PendingLevelContent 里删掉（别让这张免死金牌留着）。
        var landed = known.Except(pending).ToArray();
        Assert.True(landed.Length == 0,
            "这些点的关卡本体已经落地了，请从 PendingLevelContent 里删掉：" + string.Join("、", landed));
    }

    [Fact]
    public void 真数据_图结构健康_无环无孤岛无死锁()
    {
        var graph = RealGraph();

        // 拓扑校验：把"关卡本体还没落地"那几个点**当作已经有内容**（假设 5 处搜刮点）来跑——
        // 这样测的是**这张图本身对不对**（环 / 孤岛 / 死锁），而不是"别人的活干完没有"。
        // 真正的待接线死锁由 真数据_关卡本体待接线清单 单独看着，两件事分开报，谁都不遮谁。
        var problems = graph.Validate(AssumedPointCount);
        Assert.True(problems.Count == 0, "world_graph.json 有问题：\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void 真数据_开局两个起点_一个向东一个向西北_且方向与地图坐标对得上()
    {
        var graph = RealGraph();
        var starts = graph.Starts.ToList();
        Assert.Equal(2, starts.Count);

        var east = starts.Single(s => s.Start == "东");
        var northwest = starts.Single(s => s.Start == "西北");

        // 🔴 方向不能是嘴上说说：拿营地锚点核对真坐标（别把西边的点标成"向东"）。
        // 屏幕坐标 y 向下 ⇒ 北 = y 更小。
        Assert.True(east.X > graph.Camp.X, $"「{east.Display}」标着「向东」，坐标却不在营地以东");
        Assert.True(Math.Abs(east.Y - graph.Camp.Y) < Math.Abs(east.X - graph.Camp.X),
            $"「{east.Display}」标着「向东」，但南北偏移比东西偏移还大——那不叫东");

        Assert.True(northwest.X < graph.Camp.X, $"「{northwest.Display}」标着「向西北」，坐标却不在营地以西");
        Assert.True(northwest.Y < graph.Camp.Y, $"「{northwest.Display}」标着「向西北」，坐标却不在营地以北");
    }

    [Fact]
    public void 真数据_两个起点都是简单前期点_小图且不高危()
    {
        var graph = RealGraph();
        foreach (var s in graph.Starts)
        {
            Assert.Equal(SizeTier.Small, s.Size);                 // 用户："两个**简单**前期探查点"
            Assert.NotEqual(DangerTier.High, s.Danger ?? DangerTier.Low);
            // 起点的行程必须落在全图偏短的一头（不能让开局的人走 11 分钟）。
            Assert.True(s.TravelSeconds <= 360, $"起点「{s.Display}」行程 {s.TravelSeconds}s，对开局太远了");
        }
        // 消防站（低危 + 全图最短行程 + 3 只丧尸）是天然的起点。
        var fire = graph.Find("消防站")!;
        Assert.Equal("西北", fire.Start);
        Assert.Equal(DangerTier.Low, fire.Danger);
        Assert.Equal(graph.Nodes.Min(n => n.TravelSeconds), fire.TravelSeconds);
    }

    [Fact]
    public void 真数据_难度递增_高危点在后半程_终局是广播台()
    {
        var graph = RealGraph();
        var depth = Depths(graph);
        int maxDepth = depth.Values.Max();

        // 全图唯一的高危点（斯图尔特庄园）必须在**后半程**，且是**终局的直接前置**——
        // 它是【东链】的末端，份量不能被削掉。
        var high = graph.Nodes.Where(n => n.Danger == DangerTier.High).ToList();
        var radio = graph.Find("广播台")!;
        Assert.NotEmpty(high);
        foreach (var n in high)
        {
            Assert.True(depth[n.Name] * 2 > maxDepth, $"高危点「{n.Display}」深度只有 {depth[n.Name]}，离起点太近了");
            Assert.Contains(n.Name, radio.Prereq);
        }

        // 终局＝广播台（用户拍板：金手指帮改中期、广播站接过终局）。
        Assert.Equal(maxDepth, depth["广播台"]);

        // 起点深度 0。
        foreach (var s in graph.Starts)
            Assert.Equal(0, depth[s.Name]);
    }

    [Fact]
    public void 真数据_剧情编排_关键节点排在该排的地方()
    {
        var graph = RealGraph();
        var depth = Depths(graph);
        int maxDepth = depth.Values.Max();

        // 废弃医院＝医疗物资总源 ⇒ **中段**（太早玩家不需要，太晚已经死人了）。
        Assert.InRange(depth[ExplorationCache.HospitalName], 2, maxDepth - 2);

        // 南林村庄＝村中心铁匠铺（全游戏铁的主要来源，铸铁大门要 48 铁）⇒ 资源线关键节点，中段。
        Assert.InRange(depth["南林村庄"], 2, maxDepth - 2);

        // 城市之巅瞭望观景台＝望远镜目击尸潮 ⇒ 开启倒计时，必须在中段而不是终局。
        Assert.InRange(depth[WorldMapPanel_LookoutName], 1, maxDepth - 2);

        // 联合收割机仓库＝消防斧 +《进阶木匠技术》同馆（工具线）⇒ 用户拍板「**很早期**」。
        Assert.Equal(1, depth[ExplorationCache.HarvesterWarehouseName]);

        // 金手指帮根据地 ⇒ 用户拍板「**中期**」：既不在前两层，也不在最后两层。
        int gf = depth[ExplorationProgress.GoldfingerBaseName];
        Assert.InRange(gf, 2, maxDepth - 2);

        // 守林人小屋（哥顿日记 B ＝ 金手指帮的线索）⇒ 中期，且**必须排在金手指帮之前**。
        int cabin = depth[ExplorationProgress.WatchersCabinName];
        Assert.InRange(cabin, 2, maxDepth - 2);
        Assert.True(cabin < gf, "线索（哥顿的尸体）必须排在它指向的那个地方（金手指帮）之前");

        // 下水道 ⇒ 用户拍板「**前中期**」·小·低危（恐怖靠黑暗与视野，不靠敌人）。
        var sewer = graph.Find("下水道")!;
        Assert.InRange(depth["下水道"], 1, 2);
        Assert.Equal(SizeTier.Small, sewer.Size);
        Assert.Equal(DangerTier.Low, sewer.Danger);

        // 破败教堂 / 难民营地 ⇒ 用户拍板「**后期**」·规模中。
        foreach (string late in new[] { "破败教堂", "难民营地" })
        {
            Assert.InRange(depth[late], maxDepth - 2, maxDepth - 1);
            Assert.Equal(SizeTier.Medium, graph.Find(late)!.Size);
        }
    }

    [Fact]
    public void 真数据_两条证据链_西北是军方干了什么_东路是人干了什么()
    {
        var graph = RealGraph();
        var radio = graph.Find("广播台")!;

        // 终局的两个前置，各自是一条链的末端。
        Assert.Equal(
            new[] { "斯图尔特家族庄园", "破败教堂" },
            radio.Prereq.OrderBy(s => s, StringComparer.Ordinal).ToArray());

        // 【西北链·军方做了什么】废弃医院 → 难民营地 → 破败教堂
        Assert.Equal(new[] { "医院" }, graph.Find("难民营地")!.Prereq.ToArray());
        Assert.Equal(new[] { "难民营地" }, graph.Find("破败教堂")!.Prereq.ToArray());
        // 废弃医院的两个前置都只长在西北路上 ⇒ 它是【西北链】的门户，绕不过去。
        Assert.Equal(new[] { "加油站", "药店" },
            graph.Find(ExplorationCache.HospitalName)!.Prereq.OrderBy(s => s, StringComparer.Ordinal).ToArray());

        // 【东链·人做了什么】金手指帮根据地 → 斯图尔特家族庄园
        Assert.Equal(new[] { ExplorationProgress.GoldfingerBaseName }, graph.Find("斯图尔特家族庄园")!.Prereq.ToArray());
    }

    [Fact]
    public void 真数据_任何一个起点都通得了关_不会有走死的那条路()
    {
        var graph = RealGraph();

        // 🔴 逐个起点单独开一局：玩家开局选了这一个方向，最后**通得了关吗**？
        // （另一个起点**永远开着**——起点无前置 ⇒ 玩家随时能回头走另一条路，这是允许的。）
        foreach (var start in graph.Starts)
        {
            var visited = Playthrough(graph, start.Name);
            Assert.True(visited.Contains("广播台"),
                $"从起点「{start.Display}」（向{start.Start}）开局，**通不到终局**。走死的路 = 玩家白玩一局。"
                + $"\n  这一路只够得着：{string.Join("、", visited)}");
        }
    }

    [Fact]
    public void 真数据_只走东路通不了关_必须回头铺西北_而这不是死锁()
    {
        var graph = RealGraph();

        // 事实（设计使然，不是 bug）：废弃医院的两个前置（南丁格尔小药店 / 加油站）**都只长在西北路上**
        // ⇒ 只走东路的玩家永远到不了医院 ⇒ 到不了难民营地/破败教堂 ⇒ 到不了终局。
        var eastOnly = Playthrough(graph, "河边小屋", ignoreOtherStarts: true);
        Assert.DoesNotContain("医院", eastOnly);
        Assert.DoesNotContain("广播台", eastOnly);

        // 🔴 但这**不是死锁**：消防站是**起点**（无前置）⇒ **永远开着** ⇒ 玩家随时可以回头去铺西北。
        // 这正是「网」该有的样子——最终两条路都得走完，那本来就是终局「全部前置」要的东西。
        var fire = graph.Find("消防站")!;
        Assert.True(fire.IsStart);
        Assert.True(WorldGraphUnlock.CanTravelTo(graph, "消防站", eastOnly, new StoryFlags(), false));
    }

    /// <summary>
    /// 模拟一整轮：从 <paramref name="startName"/> 开局，反复"去一个已解锁的点 + 把它搜干净（探索度 100%）"，
    /// 直到没有新点可去。返回这一局能够得着的全部点。
    ///
    /// <para>⚠️ 这里走的是**图层级**的级联（前置在 visited 里 ⇒ 满足），刻意**不**过 StoryFlags 那一层：
    /// 「探索度真的到得了 50% 吗」是**另一个**问题，由 <see cref="真数据_关卡本体待接线清单_只许缩短不许变长"/> 和
    /// <see cref="真数据_每个前置点的探索度都真能超过五成_不会把玩家永久锁死"/> 单独盯着。
    /// 两件事分开测，谁都不遮谁——否则三个还没接线的新点会把"这张图通不通"这个问题一起淹掉。</para>
    /// </summary>
    private static HashSet<string> Playthrough(WorldGraph graph, string startName, bool ignoreOtherStarts = false)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);

        for (int round = 0; round < graph.Nodes.Count + 2; round++)
        {
            foreach (var n in graph.Nodes)
            {
                if (visited.Contains(n.Name))
                    continue;
                if (ignoreOtherStarts && n.IsStart && n.Name != startName)
                    continue;

                bool open = n.Prereq.Count == 0
                    || (n.RequireAll ? n.Prereq.All(visited.Contains) : n.Prereq.Any(visited.Contains));
                if (open)
                    visited.Add(n.Name);
            }
        }
        return visited;
    }

    [Fact]
    public void 真数据_每个前置点的探索度都真能超过五成_不会把玩家永久锁死()
    {
        var graph = RealGraph();
        var prereqs = graph.Nodes.SelectMany(n => n.Prereq).Distinct().ToList();
        Assert.NotEmpty(prereqs);

        foreach (string p in prereqs)
        {
            // 关卡本体还没落地的点：由 真数据_关卡本体待接线清单 那条单独报（那儿的报错更准，也更响）。
            if (PendingLevelContent.Contains(p))
                continue;

            int total = RegisteredPointCount(p);
            Assert.True(total > 0, $"🔴 死锁：前置点「{p}」没有任何可调查的点位，探索度永远是 0/0");

            // 把它全搜一遍（探索度 100%）⇒ 必须过得了 50% 门槛。
            var flags = new StoryFlags();
            SearchAll(p, flags);
            var (done, tot) = ExplorationProgress.Completion(p, flags, false);
            Assert.Equal(tot, done);
            Assert.True(WorldGraphUnlock.MeetsExplorationThreshold(done, tot),
                $"🔴 死锁：「{p}」就算搜到 100%（{done}/{tot}）也过不了 50% 门槛");

            // 更狠一条：**过半所需的最少点数**必须小于等于总点数（即 50% 门槛在物理上够得着）。
            int need = total / 2 + 1;
            Assert.True(need <= total, $"🔴 死锁：「{p}」要 {need} 处才过半，但它总共只有 {total} 处");
        }
    }

    [Fact]
    public void 真数据_图里的点与代码里的目的地常量一字不差_两份事实源焊死()
    {
        var graph = RealGraph();
        // 内部路由键必须与各消费方常量对得上（写错一个字，解锁门就挂在一个不存在的目的地上，静默失效）。
        foreach (string name in new[]
                 {
                     ExplorationCache.FireStationName, ExplorationCache.RiversideCabinName,
                     ExplorationCache.SupermarketName, ExplorationCache.HospitalName,
                     ExplorationCache.EastNewVillageName, ExplorationCache.GasStationName,
                     ExplorationCache.HarvesterWarehouseName, ExplorationCache.WatchersCabinName,
                     ExplorationCache.CityRooftopLookoutName, ExplorationCache.BroadcastStationName,
                     ExplorationCache.GoldfingerBaseName, ExplorationCache.StuartManorName,
                     ExplorationCache.PoliceStationName,
                     NurseRecruit.DestinationName, VillageRescue.DestinationName,
                 })
        {
            Assert.True(graph.Contains(name), $"world_graph.json 里没有目的地「{name}」——解锁门会挂空");
        }

        // 三个新点（关卡本体在建）：路由键先在图里定死，impl-lategame / impl-sewer 照它建 ExplorationCache 常量。
        foreach (string name in PendingLevelContent)
            Assert.True(graph.Contains(name), $"world_graph.json 里没有新点「{name}」");

        Assert.Equal(18, graph.Nodes.Count); // 14 既有 + 下水道 + 难民营地 + 破败教堂 + 警察局
    }

    // ═══════════════════════ 消费层覆盖自检（源码守卫）═══════════════════════
    //
    // 🔴 本项目最狠的教训：**纯逻辑单测全绿、消费层从没接线 ⇒ 功能根本不存在**。
    // WorldMapPanel 引 Godot 类型、进不了单测程序集 ⇒ 用源码守卫钉死它真的把闸门接上了。

    [Fact]
    public void 覆盖自检_世界地图必须走同一个闸门_锁着的点点不进去也确认不了()
    {
        string src = StripComments(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "scripts", "WorldMapPanel.cs")));

        // ① 地图从**数据**读图，不再硬编码目的地表。
        Assert.Contains("world_graph.json", src);
        Assert.Contains("WorldGraph.FromJson", src);

        // ② 点击拦截 + 确认拦截，两处都必须问同一个纯函数（不许另写一份 if）。
        int gate = CountOccurrences(src, "WorldGraphUnlock.CanTravelTo") + CountOccurrences(src, "WorldGraphUnlock.StateOf");
        Assert.True(gate >= 3,
            $"WorldMapPanel 只调了 {gate} 次解锁闸门——点击/确认/显示三处都得走 WorldGraphUnlock，否则锁是画上去的");

        // ③ 锁着的点**要看得见、要说得出为什么**（不能干脆不显示）。
        Assert.Contains("Reason", src);
        Assert.Contains("Locked", src);
    }

    [Fact]
    public void 覆盖自检_存档必须记住去过哪些点_且老档兜底为全解锁()
    {
        string saveData = File.ReadAllText(Path.Combine(RepoRoot(), "godot", "scripts", "SaveData.cs"));
        // 可空 ⇒ 老档里**没有这个键**时读出来是 null，据此认出"这是 T57 之前的存档" ⇒ 全解锁兜底。
        Assert.Contains("List<string>? VisitedDestinations", saveData);

        // 存档存/读（CampMain.Save.cs）+ 运行时记账与闸门灌注（CampMain.cs）。
        string save = StripComments(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "scripts", "CampMain.Save.cs")));
        Assert.Contains("VisitedDestinations = _visitedDestinations", save); // 存
        Assert.Contains("s.VisitedDestinations", save);                       // 读
        Assert.Contains("_legacyFullUnlock = s.VisitedDestinations is null", save); // 老档兜底真的判了

        string camp = StripComments(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "scripts", "CampMain.cs")));
        Assert.Contains("_visitedDestinations.Add(_pendingDestination)", camp);  // 真踏进图才算「去过」
        Assert.Contains("SetUnlockContext", camp);                               // 上下文真的灌进地图了
        Assert.Contains("_legacyFullUnlock", camp);                              // 老档兜底真的传下去了
    }

    // ═══════════════════════ 工具 ═══════════════════════

    private const string WorldMapPanel_LookoutName = "城市之巅瞭望观景台";

    private static WorldGraph RealGraph()
        => WorldGraph.FromJson(File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "world_graph.json")));

    /// <summary>目的地登记点位总数（＝生产上算探索度用的分母）。</summary>
    private static int RegisteredPointCount(string name)
        => ExplorationProgress.PointFlagsFor(name, christineLeftForRevenge: false).Count;

    /// <summary>
    /// 同上，但把**关卡本体还没落地**的那几个点当成"已经有 5 处搜刮点"。
    /// 用于**拓扑**校验（环/孤岛/死锁）——测的是「这张图本身对不对」，不是「别人的活干完没有」。
    /// 真正的"还没接线"由 <see cref="真数据_关卡本体待接线清单_只许缩短不许变长"/> 单独报。
    /// </summary>
    private static int AssumedPointCount(string name)
    {
        int real = RegisteredPointCount(name);
        return real > 0 ? real : (PendingLevelContent.Contains(name) ? 5 : 0);
    }

    /// <summary>把某个目的地的前 n 处点位标成"已调查"。</summary>
    private static void SearchN(string name, StoryFlags flags, int n)
    {
        var pts = ExplorationProgress.PointFlagsFor(name, false);
        for (int i = 0; i < n && i < pts.Count; i++)
            flags.Set(pts[i], "1");
    }

    private static void SearchAll(string name, StoryFlags flags)
        => SearchN(name, flags, int.MaxValue);

    /// <summary>各点到起点的最短解锁深度（起点=0）。</summary>
    private static Dictionary<string, int> Depths(WorldGraph graph)
    {
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in graph.Starts)
            depth[s.Name] = 0;

        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var n in graph.Nodes)
            {
                if (depth.ContainsKey(n.Name) || n.Prereq.Count == 0)
                    continue;
                var known = n.Prereq.Where(depth.ContainsKey).Select(p => depth[p]).ToList();
                bool ready = n.RequireAll ? known.Count == n.Prereq.Count : known.Count > 0;
                if (ready)
                {
                    depth[n.Name] = known.Max() + 1;
                    grew = true;
                }
            }
        }
        return depth;
    }

    private static string RepoRoot([CallerFilePath] string here = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    /// <summary>
    /// 剥掉行注释（守卫只数**代码**里的调用；解释这条规矩的注释本身当然会提到它）。
    /// ⚠️ 不能拿裸 <c>//</c> 当分隔符：<c>res://data/world_graph.json</c> 里的 <c>//</c> 前面有个冒号，
    /// 那是 URI 不是注释——真被切掉过一次（守卫当场误报）。
    /// </summary>
    private static string StripComments(string src)
        => string.Join('\n', src.Split('\n').Select(line =>
        {
            string t = line.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal))
                return "";
            for (int i = 0; i + 1 < line.Length; i++)
            {
                if (line[i] == '/' && line[i + 1] == '/' && (i == 0 || line[i - 1] != ':'))
                    return line[..i];
            }
            return line;
        }));
}
