using System.Text.Json.Nodes;

namespace DeadSignal.WikiExtract;

// ═══════════════════════════════════════════════════════════════════════════
// 表与代码的合并语义 —— **表赢代码**，全分区通用。
//
// 为什么必须有它：抽取器如果每次「以代码为准」重写 JSON，用户在网页上改的、新增的、删除的，
// 下一次重跑就被静默抹掉了 —— 那正是这个 wiki 要解决的问题的反面。
// （这套语义由 impl-wiki-chars 在角色分区上首创，这里提升成全分区通用，并补齐「删除」的完整生命周期。）
//
// 重跑一次，只做四件事：
//   ① 代码里新增的条目 → 补进表里（用代码的种子值）
//   ② 表里已有的条目   → **值一律以表为准**；与代码现值不符的记进「漂移报告」，由 agent 决定往哪边同步
//   ③ 用户新增的条目（代码里没有）→ 原样保留，标「新增·待同步进代码」；id 非法则标出问题
//   ④ 用户删除的条目   → 见下面的「墓碑」
//
// **删除为什么要墓碑（软删除）**：用户在网页上删一行，代码里那把武器还在。
// 若直接把行从 JSON 抹掉，下次重跑抽取器又从代码里把它捡回来 —— 用户的删除就"自己撤销"了。
// 故删除＝在表里留一行墓碑（`sync = 删除·待同步进代码`，网页上显示为划掉的灰行）：
//   · 代码里还有它 ⇒ 墓碑保留，一直提醒 agent「这条该从 C# 里删掉」
//   · 代码里已经没它了 ⇒ 删除已落地，墓碑自动消失（这一行彻底不见）
// 于是「删」这个动作在 表→代码 同步完成之前一直可见，完成之后自动清场。
// ═══════════════════════════════════════════════════════════════════════════

internal static class TableMerge
{
    /// <summary>同步状态列的 key。空 = 表与代码一致；非空 = 这行还没同步进代码。</summary>
    internal const string SyncKey = "sync";

    /// <summary>用户在网页上新增、但代码里还没有的行。</summary>
    internal const string SyncNew = "新增·待同步进代码";

    /// <summary>用户在网页上删掉、但代码里还留着的行（墓碑）。</summary>
    internal const string SyncDeleted = "删除·待同步进代码";

    /// <summary>id 有问题（空/重复/非 ASCII）——它是回写代码的定位锚，坏了 agent 就找不到该改哪儿。</summary>
    internal const string SyncBadId = "⚠️ 内部 id 有问题";

    /// <summary>本次重跑发现的「表 ≠ 代码」漂移 + 待同步项（Program 跑完打印）。</summary>
    internal static readonly List<string> Drift = new();

    /// <summary>同步状态列（每个分区都有；用户只读，由本合并器维护）。</summary>
    internal static Col SyncColumn() => new(
        SyncKey, "同步状态", "chip", ReadOnly: true,
        Hint: "空 = 已在代码里。「新增/删除·待同步进代码」= 你在网页上的改动还没进游戏，需要 agent 同步到 C#。");

    /// <summary>
    /// 把代码抽出来的 <paramref name="fresh"/> 与磁盘上已有的表合并。表不存在（首次播种）⇒ 直接用代码的。
    /// </summary>
    internal static Category WithExisting(Category fresh, string dataDir)
    {
        string path = Path.Combine(dataDir, fresh.Id + ".json");
        Category seeded = fresh with { Columns = fresh.Columns.Append(SyncColumn()).ToList() };

        if (!File.Exists(path))
        {
            foreach (Dictionary<string, object?> row in seeded.Rows) row[SyncKey] = "";
            return seeded;
        }

        JsonArray? oldRows = JsonNode.Parse(File.ReadAllText(path))?["rows"]?.AsArray();
        if (oldRows is null || oldRows.Count == 0)
        {
            foreach (Dictionary<string, object?> row in seeded.Rows) row[SyncKey] = "";
            return seeded;
        }

        var byId = new Dictionary<string, JsonNode>();
        foreach (JsonNode? n in oldRows)
        {
            if (n?["_id"]?.GetValue<string>() is { Length: > 0 } id) byId[id] = n;
        }

        var codeIds = new HashSet<string>();
        var merged = new List<Dictionary<string, object?>>();

        // ── ①② 代码里有的条目：值以表为准，不一致的记漂移 ──
        foreach (Dictionary<string, object?> row in seeded.Rows)
        {
            if (row.GetValueOrDefault("_id") is not string id || id.Length == 0)
            {
                row[SyncKey] = "";
                merged.Add(row);
                continue;
            }
            codeIds.Add(id);

            if (!byId.TryGetValue(id, out JsonNode? old))
            {
                row[SyncKey] = "";   // 代码里新增的条目，表里还没有 ⇒ 用种子值补进来
                merged.Add(row);
                continue;
            }

            // 墓碑：用户删过它，而代码里还有 ⇒ 保留墓碑，继续提醒 agent 去 C# 里删
            bool tombstoned = old[SyncKey]?.GetValue<string>() == SyncDeleted;

            foreach (Col col in seeded.Columns)
            {
                if (col.Internal || col.ReadOnly || col.Key == SyncKey) continue;  // 内部/只读列以代码为准
                if (old[col.Key] is not { } cell) continue;                        // 表里没这一格（新加的列）：用种子

                object? seededVal = row.GetValueOrDefault(col.Key);
                object? edited = Read(cell, col.Type);

                // **比较前必须按写进 JSON 的那个精度归一**，否则 float 字段会永远报一个修不掉的假漂移：
                // LightProfile.Intensity 是 float —— 0.9f 提升成 double 是 0.8999999761581421，
                // 而序列化时 Round(…,4) 写进 JSON 的是整洁的 0.9。下次重跑读回 0.9 去和种子里的
                // 0.899999976… 比，差 2.4e-8 > 1e-9 ⇒ 每跑一次报一遍「表 0.9 ≠ 代码 0.9」。
                // 根因是「序列化四舍五入了、比较却没有」，所以两边都过同一个 Round 才是正解
                // （单纯放宽 epsilon 只是碰巧盖住，float 精度更差的值照样漏）。
                if (col.Type == "number" && seededVal is not null && edited is not null
                    && Math.Abs(Program.Round(Convert.ToDouble(edited)) - Program.Round(Convert.ToDouble(seededVal))) > 1e-9)
                {
                    Drift.Add($"  [数值漂移] {fresh.Label}·{Name(row, id)}·{col.Label}：表 = {edited} ≠ 代码 = {Program.Round(Convert.ToDouble(seededVal))}"
                              + $"　⇒ 把表里的值同步进 {row.GetValueOrDefault("_anchor")}");
                }

                row[col.Key] = edited;   // 表赢：用户改的一律保留
            }

            row[SyncKey] = tombstoned ? SyncDeleted : "";
            if (tombstoned)
            {
                Drift.Add($"  [删除待同步] {fresh.Label}·{Name(row, id)}（id={id}）在表里被删了，代码里还有"
                          + $"　⇒ 把它从 {row.GetValueOrDefault("_anchor")} 删掉");
            }
            merged.Add(row);
        }

        // ── ③④ 表里有、代码里没有的条目 ──
        // **靠 sync 标记分辨这行是谁写的**，这是整套语义的关键：
        //   · 带「新增」标记 ⇒ 用户在网页上加的 ⇒ 代码里本来就不该有它 ⇒ 保留（③）
        //   · 带「删除」标记 ⇒ 用户删的，且代码里也没了 ⇒ 同步已落地 ⇒ 墓碑功成身退，彻底消失（④）
        //   · 没有标记（sync 为空）⇒ 这行是**上一次抽取器自己从代码写下去的**，如今代码里没了 ⇒ 丢掉。
        //     （要么上游把它从 C# 删了，要么它换了分区——比如三种草药从「医疗」迁到「材料」。
        //      不这么判就会把它误当成"用户新增"，在两个分区里各留一份。）
        var seen = new HashSet<string>(codeIds);
        foreach (JsonNode? n in oldRows)
        {
            if (n is null) continue;
            string id = n["_id"]?.GetValue<string>() ?? "";
            if (codeIds.Contains(id)) continue;

            string mark = n[SyncKey]?.GetValue<string>() ?? "";
            if (mark != SyncNew && mark != SyncBadId) continue;   // ④ 及「代码里已不存在的旧行」：都不保留

            // ③ 用户新增的行：绝不能丢，它是用户的设计意图
            var added = new Dictionary<string, object?>();
            foreach (Col col in seeded.Columns)
            {
                added[col.Key] = n[col.Key] is { } c ? Read(c, col.Type) : null;
            }
            added["_id"] = id;

            // id 校验：它是回写代码的定位锚，空/重复/非 ASCII 都会让 agent 找不到该改哪儿
            string problem =
                string.IsNullOrWhiteSpace(id) ? "内部 id 是空的" :
                !seen.Add(id) ? $"内部 id「{id}」和别的行重复了" :
                !id.All(char.IsAscii) ? $"内部 id「{id}」含非 ASCII 字符（它还要当图标文件名用）" : "";

            added[SyncKey] = problem.Length > 0 ? SyncBadId : SyncNew;
            added["_anchor"] = problem.Length > 0 ? problem : "（新增行：代码里还没有，等 agent 建）";

            Drift.Add(problem.Length > 0
                ? $"  [id 无效] {fresh.Label} 表里新增的一行：{problem}　⇒ 修好 id 才能回写代码"
                : $"  [新增待同步] {fresh.Label}·{Name(added, id)}（id={id}）表里有、代码里没有 ⇒ 该在 C# 里把它建出来");

            merged.Add(added);
        }

        return seeded with { Rows = merged };
    }

    private static object? Read(JsonNode cell, string type)
    {
        try
        {
            return type switch
            {
                "number" => cell.GetValue<double>(),
                "bool" => cell.GetValue<bool>(),
                _ => cell.GetValue<string>(),
            };
        }
        catch (InvalidOperationException)
        {
            return null;   // 类型对不上（用户在数字格里打了字之类）：当作空，不炸掉整次抽取
        }
    }

    /// <summary>取这一行的主键名（给漂移报告用人话指认是哪一条）。</summary>
    private static string Name(Dictionary<string, object?> row, string fallback)
        => row.GetValueOrDefault("name") as string
           ?? row.GetValueOrDefault("title") as string
           ?? row.GetValueOrDefault("who") as string
           ?? (fallback.Length == 0 ? "(无名)" : fallback);
}
