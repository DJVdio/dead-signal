using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationProgress.cs / ExplorationCache.cs / StoryFlags.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// ══════════════════════════════════════════════════════════════════════════════
// 调查点「网状解锁图」（[T57]，用户拍板，原话）：
//   「调查点应当是网状结构，需要去过前置的调查点且探索度大于50%，才能够走到之后的调查点。
//     开局可以选择向东或者向西北的两个简单前期探查点，随后在这两条路径上扩散出去，
//     把现有的调查点按照难度和剧情加入进去。」
//
// 🔴 **图是数据，不是代码**：拓扑全部落在 godot/data/world_graph.json（wiki 上也有对应的表）。
//    想重排路线 ⇒ 改那份数据，**不用动这个文件**。本文件只写规则：
//      · 解锁 = 前置点【已去过】 **且** 前置点【探索度 > 50%】——**两个独立条件**。
//        （同一个点可以「去过但只搜了 20%」⇒ 后续仍然不解锁。这正是用户要的：逼你把一个点吃透，而不是踩一脚就走。）
//      · 探索度**不是我发明的新概念**：它就是既有的 ExplorationProgress.Completion(done,total) —— 该点已调查的
//        点位数 / 登记点位总数（物资搜刮点 + 剧情尸体发现点）。>50% 用**严格整数比较** done*2 > total，不碰浮点。
//      · prereq 为空 ⇒ 起点，开局即可去。
//      · requireAll=false（默认）⇒ **任一**前置满足即解锁 —— 这就是「网」：多条路通到同一个点。
//      · requireAll=true ⇒ **全部**前置都要满足（只有终局的金手指帮用它）。
//
// 🔴 **反死锁**（本系统最容易出的 bug）：若某个前置点的**登记点位总数为 0**，它的探索度永远是 0/0，
//    **永远过不了 50%** ⇒ 它后面的整片图**永久锁死**，玩家卡关且无从得知。
//    故 <see cref="Validate"/> 把「前置点必须有可调查的点位」当作硬校验，测试里拿**真数据**跑它。
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>世界图上的一个调查点（数据，来自 world_graph.json）。</summary>
public sealed class WorldNode
{
    /// <summary>内部路由键（与 ExplorationCache / *.DestinationName 一字不差）。</summary>
    public string Name { get; init; } = "";

    /// <summary>玩家看到的名字（如 医院 → 废弃医院）。</summary>
    public string Display { get; init; } = "";

    /// <summary>起点方向（"东" / "西北"）；非起点为 null。起点无前置，开局即可去。</summary>
    public string? Start { get; init; }

    /// <summary>前置调查点（Name）。空 ⇒ 起点。</summary>
    public IReadOnlyList<string> Prereq { get; init; } = Array.Empty<string>();

    /// <summary>true ⇒ **全部**前置都要满足；false ⇒ **任一**前置满足即可（默认，网状汇合靠它）。</summary>
    public bool RequireAll { get; init; }

    public int TravelSeconds { get; init; }
    public SizeTier Size { get; init; } = SizeTier.Medium;

    /// <summary>危险度（null＝未定级，地图上不显示——不拿没人拍过的数字骗玩家送死）。</summary>
    public DangerTier? Danger { get; init; }

    public float X { get; init; }
    public float Y { get; init; }

    /// <summary>给玩家看的简介。</summary>
    public string Summary { get; init; } = "";

    /// <summary>设计备注（用户在 wiki 上写特殊说明用；不出现在游戏里）。</summary>
    public string Note { get; init; } = "";

    public bool IsStart => Prereq.Count == 0;
}

/// <summary>营地锚点（方向按它算：起点在营地的东/西北）。</summary>
public sealed class WorldCamp
{
    public float X { get; init; }
    public float Y { get; init; }
    public string Display { get; init; } = "营地";
}

/// <summary>世界图（数据容器 + 校验）。纯逻辑，可脱 Godot 单测。</summary>
public sealed class WorldGraph
{
    public IReadOnlyList<WorldNode> Nodes { get; }
    public WorldCamp Camp { get; }

    private readonly Dictionary<string, WorldNode> _byName;

    private WorldGraph(IReadOnlyList<WorldNode> nodes, WorldCamp camp)
    {
        Nodes = nodes;
        Camp = camp;
        _byName = nodes.ToDictionary(n => n.Name, StringComparer.Ordinal);
    }

    public WorldNode? Find(string name)
        => name != null && _byName.TryGetValue(name, out var n) ? n : null;

    public bool Contains(string name) => name != null && _byName.ContainsKey(name);

    public IEnumerable<WorldNode> Starts => Nodes.Where(n => n.IsStart);

    /// <summary>解析 world_graph.json（纯字符串入口——Godot 层负责读盘，测试直接从磁盘读同一份文件）。</summary>
    public static WorldGraph FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var camp = new WorldCamp();
        if (root.TryGetProperty("camp", out var c))
        {
            camp = new WorldCamp
            {
                X = c.TryGetProperty("x", out var cx) ? cx.GetSingle() : 0,
                Y = c.TryGetProperty("y", out var cy) ? cy.GetSingle() : 0,
                Display = c.TryGetProperty("display", out var cd) ? (cd.GetString() ?? "营地") : "营地",
            };
        }

        var nodes = new List<WorldNode>();
        foreach (var e in root.GetProperty("nodes").EnumerateArray())
        {
            string name = e.GetProperty("name").GetString() ?? "";
            var prereq = new List<string>();
            if (e.TryGetProperty("prereq", out var p) && p.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in p.EnumerateArray())
                {
                    string? s = q.GetString();
                    if (!string.IsNullOrEmpty(s))
                        prereq.Add(s);
                }
            }

            nodes.Add(new WorldNode
            {
                Name = name,
                Display = Str(e, "display") ?? name,
                Start = Str(e, "start"),
                Prereq = prereq,
                RequireAll = e.TryGetProperty("requireAll", out var ra) && ra.ValueKind == JsonValueKind.True,
                TravelSeconds = e.TryGetProperty("travelSeconds", out var t) ? t.GetInt32() : 0,
                Size = ParseSize(Str(e, "size")),
                Danger = ParseDanger(Str(e, "danger")),
                X = e.TryGetProperty("x", out var x) ? x.GetSingle() : 0,
                Y = e.TryGetProperty("y", out var y) ? y.GetSingle() : 0,
                Summary = Str(e, "summary") ?? "",
                Note = Str(e, "note") ?? "",
            });
        }

        return new WorldGraph(nodes, camp);

        static string? Str(JsonElement e, string key)
            => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static SizeTier ParseSize(string? s) => s switch
    {
        "Small" => SizeTier.Small,
        "Large" => SizeTier.Large,
        _ => SizeTier.Medium,
    };

    private static DangerTier? ParseDanger(string? s) => s switch
    {
        "Low" => DangerTier.Low,
        "Medium" => DangerTier.Medium,
        "High" => DangerTier.High,
        _ => (DangerTier?)null,
    };

    /// <summary>
    /// 图的结构校验。返回问题清单（空＝健康）。测试拿**真数据**跑它——这是防「玩家永久卡关」的那道闸。
    /// <para>
    /// 逐条检查：① 至少两个起点，方向覆盖「东」和「西北」；② 前置必须指向存在的点；③ 无环；
    /// ④ 每个点都能从起点走到（不能有孤岛内容）；⑤ 🔴 **每个被当作前置的点，都必须有可调查的点位**
    /// （<paramref name="registeredPointCount"/> &gt; 0），否则它的探索度永远是 0/0、永远过不了 50%，后面整片图锁死。
    /// </para>
    /// </summary>
    /// <param name="registeredPointCount">目的地 → 其登记点位总数（生产上＝ExplorationProgress.PointFlagsFor(name).Count）。</param>
    public IReadOnlyList<string> Validate(Func<string, int> registeredPointCount)
    {
        var problems = new List<string>();

        // ① 起点
        var starts = Starts.ToList();
        if (starts.Count == 0)
            problems.Add("没有起点：所有点都有前置 ⇒ 开局一个地方都去不了。");
        foreach (var s in starts)
        {
            if (string.IsNullOrEmpty(s.Start))
                problems.Add($"起点「{s.Display}」没写方向（start 字段）。");
        }
        var dirs = starts.Select(s => s.Start).Where(d => !string.IsNullOrEmpty(d)).ToHashSet(StringComparer.Ordinal);
        foreach (string want in new[] { "东", "西北" })
        {
            if (!dirs.Contains(want))
                problems.Add($"缺少「向{want}」的开局起点（用户拍板：开局可选向东或向西北）。");
        }

        // ② 前置存在性 + ⑤ 反死锁
        foreach (var n in Nodes)
        {
            foreach (string p in n.Prereq)
            {
                if (!Contains(p))
                {
                    problems.Add($"「{n.Display}」的前置「{p}」不存在于图里。");
                    continue;
                }
                int total = registeredPointCount(p);
                if (total <= 0)
                {
                    problems.Add(
                        $"🔴 死锁：「{n.Display}」的前置「{Find(p)!.Display}」没有任何可调查的点位（登记点位总数 0）⇒ " +
                        "它的探索度永远到不了 50%，后面整片图永久锁死。");
                }
            }
        }

        // ③ 无环（DFS 三色）
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0 未访 1 在栈 2 完成
        foreach (var n in Nodes)
        {
            if (HasCycle(n.Name, state, out string cyc))
            {
                problems.Add($"🔴 图里有环（{cyc}）⇒ 环上的点互为前置，谁都解锁不了。");
                break;
            }
        }

        // ④ 可达性：从起点出发的解锁传播能不能覆盖全图
        var reached = new HashSet<string>(starts.Select(s => s.Name), StringComparer.Ordinal);
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var n in Nodes)
            {
                if (reached.Contains(n.Name) || n.Prereq.Count == 0)
                    continue;
                bool ok = n.RequireAll
                    ? n.Prereq.All(reached.Contains)
                    : n.Prereq.Any(reached.Contains);
                if (ok && reached.Add(n.Name))
                    grew = true;
            }
        }
        foreach (var n in Nodes)
        {
            if (!reached.Contains(n.Name))
                problems.Add($"🔴 「{n.Display}」从任何起点都走不到（孤岛）⇒ 这块内容玩家永远见不到。");
        }

        return problems;
    }

    private bool HasCycle(string name, Dictionary<string, int> state, out string path)
    {
        path = "";
        if (state.TryGetValue(name, out int s))
        {
            if (s == 1) { path = name; return true; }
            return false;
        }
        state[name] = 1;
        var node = Find(name);
        if (node != null)
        {
            foreach (string p in node.Prereq)
            {
                if (!Contains(p))
                    continue;
                if (HasCycle(p, state, out string inner))
                {
                    path = $"{name} → {inner}";
                    return true;
                }
            }
        }
        state[name] = 2;
        return false;
    }
}

/// <summary>某个点当下的解锁状态（给 UI：锁没锁 + **为什么锁着**）。</summary>
public sealed class WorldNodeLock
{
    public bool Unlocked { get; init; }

    /// <summary>锁着时给玩家的一句话（"需要先把「超市」探索到 50% 以上"）。解锁时为空。</summary>
    public string Reason { get; init; } = "";
}

/// <summary>
/// 解锁判定（纯函数）。**唯一事实源** —— UI 显示锁、点击拦截、存档回读，全都走这里，
/// 不许任何地方另写一份"是不是能去"的判断（那正是"纯逻辑绿了但消费层没接线"的静默失效怎么来的）。
/// </summary>
public static class WorldGraphUnlock
{
    /// <summary>探索度门槛：**严格大于 50%**（用户原话"探索度大于50%"）。整数比较 done*2 &gt; total，不碰浮点。</summary>
    public static bool MeetsExplorationThreshold(int done, int total) => total > 0 && done * 2 > total;

    /// <summary>门槛文案（给 UI 与测试共用）。</summary>
    public const string ThresholdLabel = "50%";

    /// <summary>
    /// 单个前置点「够不够格」：**去过** 且 **探索度 &gt; 50%**。两个条件缺一不可
    /// （去过但只搜了 20% ⇒ 不算数）。
    /// </summary>
    public static bool PrereqSatisfied(
        string prereqName,
        IReadOnlyCollection<string> visited,
        StoryFlags flags,
        bool christineLeftForRevenge)
    {
        if (visited == null || !visited.Contains(prereqName))
            return false; // 条件一：没去过
        var (done, total) = ExplorationProgress.Completion(prereqName, flags, christineLeftForRevenge);
        return MeetsExplorationThreshold(done, total); // 条件二：探索度 > 50%
    }

    /// <summary>
    /// 某个点当下锁没锁 + 锁着的理由。
    /// <para>
    /// <paramref name="legacyFullUnlock"/>＝老存档兜底：网状解锁是 [T57] 才有的东西，老档里根本没有
    /// "去过哪些点"的记录 ⇒ 一律视为**全部已解锁**（不剥夺玩家已经打下来的进度）。见 SaveData.VisitedDestinations。
    /// </para>
    /// </summary>
    public static WorldNodeLock StateOf(
        WorldGraph graph,
        string name,
        IReadOnlyCollection<string> visited,
        StoryFlags flags,
        bool christineLeftForRevenge,
        bool legacyFullUnlock = false)
    {
        if (legacyFullUnlock || graph == null)
            return new WorldNodeLock { Unlocked = true };

        // 🔴 **去过的点永远保持解锁** —— 路已经踩出来了，没有"走回去反而进不去"这回事。
        // 这条不是为了方便，是**防矛盾态**：图是数据、随时可能重排（用户已经连改五轮），
        // 而在玩的存档里躺着一份"去过哪些点"的名单。若不认这条，重排之后玩家会看到
        // **"这个点我明明去过，现在却锁着"**（关内的搜刮 flag 还在，探索度还在涨，但门关了）——
        // 那是把玩家的进度锁进一个说不通的状态里。
        if (visited != null && visited.Contains(name))
            return new WorldNodeLock { Unlocked = true };

        var node = graph.Find(name);
        if (node == null)
            return new WorldNodeLock { Unlocked = true }; // 不在图里的目的地不设门（不凭空拦住别人的功能）

        if (node.IsStart)
            return new WorldNodeLock { Unlocked = true }; // 起点开局即可去

        var been = visited ?? (IReadOnlyCollection<string>)Array.Empty<string>();
        var missing = new List<WorldNode>();
        int satisfied = 0;
        foreach (string p in node.Prereq)
        {
            if (PrereqSatisfied(p, been, flags, christineLeftForRevenge))
                satisfied++;
            else if (graph.Find(p) is { } pn)
                missing.Add(pn);
        }

        bool unlocked = node.RequireAll ? missing.Count == 0 : satisfied > 0;
        if (unlocked)
            return new WorldNodeLock { Unlocked = true };

        return new WorldNodeLock { Unlocked = false, Reason = ReasonFor(node, missing) };
    }

    /// <summary>锁着的理由（给玩家一句人话——**不能只显示一把锁**，那样玩家不知道该往哪儿走）。</summary>
    private static string ReasonFor(WorldNode node, IReadOnlyList<WorldNode> missing)
    {
        if (missing.Count == 0)
            return $"需要先探索其它地方（{ThresholdLabel} 以上）才能到这里。";

        string names = string.Join(node.RequireAll ? "、" : " 或 ", missing.Select(m => $"「{m.Display}」"));
        string verb = node.RequireAll && missing.Count > 1 ? "都" : "";
        return $"需要先去过{names}并{verb}探索到 {ThresholdLabel} 以上。";
    }

    /// <summary>
    /// 🔴 **能不能出发去这个点** —— 世界地图点击/确认的**唯一闸门**。
    /// UI 那边的"点不进去"必须调它，不许自己另写一遍 if。
    /// </summary>
    public static bool CanTravelTo(
        WorldGraph graph,
        string name,
        IReadOnlyCollection<string> visited,
        StoryFlags flags,
        bool christineLeftForRevenge,
        bool legacyFullUnlock = false)
        => StateOf(graph, name, visited, flags, christineLeftForRevenge, legacyFullUnlock).Unlocked;
}
