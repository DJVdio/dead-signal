using System.Text.Json;

namespace DeadSignal.WikiExtract;

// ═══════════════════════════════════════════════════════════════════════════
// [T57] 「调查点路线」分区 —— 网状解锁图的表。
//
// 与其它分区不同：它的种子不是 C# 代码，而是 **godot/data/world_graph.json**（那份数据本身就是事实源，
// 游戏运行时直接读它）。所以这张表天生就是"改一下就能重排路线"的那个东西——
// 用户在网页上改「前置调查点」，agent 把改动同步回那份 JSON 即可，**不用动任何 C# 代码**。
// ═══════════════════════════════════════════════════════════════════════════

internal static class WorldGraphTable
{
    internal const string DataPath = "godot/data/world_graph.json";

    internal static Category Build(string repoRoot)
    {
        string json = File.ReadAllText(Path.Combine(repoRoot, "godot", "data", "world_graph.json"));
        using var doc = JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateArray().ToList();

        // 内部路由键 → 显示名（表里一律讲人话，前置也写中文名而不是内部键）。
        var display = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in nodes)
        {
            string name = Str(n, "name") ?? "";
            display[name] = Str(n, "display") ?? name;
        }

        var cols = new List<Col>
        {
            new("place", "调查点", "text", Primary: true,
                Hint: "玩家在世界地图上看到的名字。"),
            new("start", "开局起点", "chip",
                Hint: "开局就能去的两个点，写它相对营地的方向（东 / 西北）。非起点留空。"),
            new("prereq", "前置调查点", "text",
                Hint: "要先去过、并且把它探索到 50% 以上，才走得到这个点。多个用「、」隔开。留空 = 起点。"),
            new("prereqMode", "前置要求", "chip",
                Hint: "任一 = 前置里随便满足一个就解锁（这就是「网」：多条路通到同一个地方）。全部 = 每个前置都要满足。"),
            new("danger", "危险度", "chip",
                Hint: "去之前唯一该知道的事：这趟要拿多少伤病和人命去换。留空 = 还没定级（地图上不显示）。"),
            new("size", "地图大小", "chip",
                Hint: "内容量：小≈1~2 天 / 中≈3~5 天 / 大≈5 天以上。与危险度是两回事——大地图不一定危险，小地图也可能要命。"),
            new("travelMinutes", "单程行程", "number",
                Hint: "从营地走到那儿要多少分钟。"),
            new("_id", "内部 id（勿改）", "text", Internal: true),
            new("_anchor", "代码位置（勿改）", "text", Internal: true),
        };

        var rows = new List<Dictionary<string, object?>>();
        foreach (var n in nodes)
        {
            string name = Str(n, "name") ?? "";
            var prereq = n.TryGetProperty("prereq", out var p) && p.ValueKind == JsonValueKind.Array
                ? p.EnumerateArray().Select(q => q.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            bool requireAll = n.TryGetProperty("requireAll", out var ra) && ra.ValueKind == JsonValueKind.True;

            rows.Add(new Dictionary<string, object?>
            {
                ["place"] = display[name],
                ["start"] = Str(n, "start") ?? "",
                ["prereq"] = string.Join("、", prereq.Select(k => display.TryGetValue(k, out var d) ? d : k)),
                ["prereqMode"] = prereq.Count == 0 ? "" : (requireAll ? "全部" : "任一"),
                ["danger"] = Str(n, "danger") switch
                {
                    "Low" => "低危",
                    "Medium" => "中危",
                    "High" => "高危",
                    _ => "",
                },
                ["size"] = Str(n, "size") switch
                {
                    "Small" => "小",
                    "Large" => "大",
                    _ => "中",
                },
                ["travelMinutes"] = n.TryGetProperty("travelSeconds", out var t) ? t.GetInt32() / 60 : 0,
                ["description"] = Str(n, "summary") ?? "",
                [UserNotes.Key] = Str(n, "note") ?? "",
                ["_id"] = name,
                ["_anchor"] = DataPath + " :: nodes[]",
            });
        }

        return new Category("world-graph", "调查点路线",
            DataPath,
            "调查点是**网状**的：要先**去过**前置的点、并且把它**探索到 50% 以上**，才走得到后面的点（两个条件缺一不可——" +
            "去过但只翻了两成，不算数）。开局只有两个简单的点开着，一个在营地**东边**、一个在**西北**，从这两条路往外铺开，" +
            "中途多次交汇，最后在**金手指帮根据地**收口（那是全图唯一要求「全部前置」的点——两条路都得走完）。\n\n" +
            "🔴 **这张表就是那张图**：改「前置调查点」就等于重排路线，改完 agent 同步回 " + DataPath + " 即可，**不用改任何代码**。\n" +
            "⚠️ 别把一个点的前置排成环（甲要乙、乙要甲），也别让某个点谁都到不了——游戏启动时的自检会当场报出来。",
            cols, rows);
    }

    // ⚠️ 刻意**不**在这张表里放「可搜刮点位数」那一列：那个数的唯一事实源是
    //    godot/scripts/ExplorationCache.cs 的 CacheIdsFor —— 在这儿再抄一份，就是第二份事实源，
    //    迟早跟代码各说各话（本项目已经吃过这个亏）。要知道一个点有多大，看「地图大小」那列。

    private static string? Str(JsonElement e, string key)
        => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
