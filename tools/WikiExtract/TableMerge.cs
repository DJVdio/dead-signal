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
    /// <summary>
    /// 哪些列类型在 JSON 里是**数字**。
    /// <para>
    /// 🔴 <c>percent</c>（穿透力 25%）和 <c>mult</c>（砸墙 *0.2）**只是给用户看的写法**——
    /// JSON 与引擎里存的仍是 <c>0.25</c> / <c>0.2</c> 这样的小数。漏掉它们的话，这里会把数字当字符串读，
    /// 合并时整列变成 <c>"0.25"</c>、漂移比较也失效。**加新的数字类写法时，务必往这里补一个。**
    /// </para>
    /// </summary>
    private static bool IsNumeric(string type) => type is "number" or "percent" or "mult" or "hours";

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
                AssertCovered(col);   // 任何可编辑列的类型都必须有归宿，没有就当场喊出来
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
                if (IsNumeric(col.Type) && seededVal is not null && edited is not null
                    && Math.Abs(Program.Round(Convert.ToDouble(edited)) - Program.Round(Convert.ToDouble(seededVal))) > 1e-9)
                {
                    Drift.Add($"  [数值漂移] {fresh.Label}·{Name(row, id)}·{col.Label}：表 = {edited} ≠ 代码 = {Program.Round(Convert.ToDouble(seededVal))}"
                              + $"　⇒ 把表里的值同步进 {row.GetValueOrDefault("_anchor")}");
                }

                // 🔴 **布尔列被改了也要报**。此前 bool 完全不在报告覆盖里 —— 用户把「可双持」从否改成是，
                //    **没有任何人会知道**。这跟"文本被静默吞掉"是同一个病，只是更隐蔽（一个绿点而已）。
                else if (col.Type == "bool" && seededVal is bool seedFlag && edited is bool editedFlag
                         && seedFlag != editedFlag)
                {
                    Drift.Add($"  [开关改动] {fresh.Label}·{Name(row, id)}·{col.Label}："
                              + $"表 = {(editedFlag ? "是" : "否")} ≠ 代码 = {(seedFlag ? "是" : "否")}"
                              + $"　⇒ 把表里的值同步进 {row.GetValueOrDefault("_anchor")}");
                }

                // 文本类（含 **chip**）被改了也要报。
                // ⚠️ chip 此前也不在覆盖里 —— 伤害类型、吃什么弹药、装备槽、材料类别…全是 chip，
                //    用户改了同样是石沉大海。**凡是可编辑的列，改动就必须被看见**，否则用户以为提了需求，
                //    那句话其实只是躺在 JSON 里。
                //
                // 🔴 判据是**种子值非空**：种子非空 = 代码里本来就有对应内容（书的效果、物品的描述）
                //    ⇒ 用户改了它就该同步回代码。
                //    种子为空 = authored 内容（角色的背景故事、剧情文本，C# 里根本没有这些句子）
                //    ⇒ 用户写什么都不该报，否则每次重跑刷一屏。
                // 🔴 **「备注」列一个字都不报**：它是用户写给 agent 看的设计笔记，代码里没有对应字段、
                //    也不该有 ⇒ 没有"代码位置"可以同步，报成"待同步进代码"是在撒谎。
                //    但它**绝不能就这么躺着没人看** —— 抽取器结尾有一节「📝 用户备注（待处理）」专门捞它，
                //    见 Program.ReportNotes。（今天刚栽过一次：用户写在「效果」列里的设计意图被静默吞了一整天。）
                else if (col.UserNote)
                {
                    // 什么都不报，但下面照样 row[col.Key] = edited —— 保留住，重跑不冲掉
                }

                else if (col.Type is "text" or "longtext" or "chip" or "multiselect"
                         // AlwaysSync（「简介」）：种子为空也要报 —— 空只说明**代码里那个字段还没建**，
                         // 用户往里写字恰恰是在说"这里该有个字段"，不能当 authored 文本吞掉。
                         && (col.AlwaysSync
                             ? edited is string
                             : seededVal is string { Length: > 0 })
                         && seededVal is string seedText
                         && edited is string editedText
                         // 比之前先把空白归一化：正文里"。 "和"。  "（一个空格 vs 两个）不是改动，
                         // 报出来只会淹掉真正的改动。
                         && !string.Equals(Squash(seedText), Squash(editedText), StringComparison.Ordinal))
                {
                    Drift.Add($"  [文本改动] {fresh.Label}·{Name(row, id)}·{col.Label} 被改过了"
                              + $"　⇒ 表：「{Clip(editedText)}」"
                              + $"　⇒ 代码：「{Clip(seedText)}」"
                              + $"　⇒ 该同步进 {row.GetValueOrDefault("_anchor")}");
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

    /// <summary>
    /// **每一种可编辑的列类型，都必须在漂移报告里有归宿**——否则用户改了它，没有任何人会知道
    /// （chip 和 bool 就这么被漏了整整一批：改「可双持」只是翻一个绿点，石沉大海）。
    /// 加新列类型时，务必同时在 <see cref="WithExisting"/> 的比较链里给它加一个分支，并更新这里。
    /// </summary>
    private static readonly string[] CoveredTypes = { "number", "percent", "mult", "hours", "bool", "text", "longtext", "chip", "multiselect", "note" };

    /// <summary>把没归宿的列类型当场喊出来（宁可吵，也不要静默吞掉用户的改动）。</summary>
    private static void AssertCovered(Col col)
    {
        if (col.Internal || col.ReadOnly || col.Key == SyncKey) return;
        if (CoveredTypes.Contains(col.Type)) return;
        Drift.Add($"  [⚠️ 工具缺陷] 列「{col.Label}」的类型 {col.Type} **不在漂移报告的覆盖里**"
                  + "　⇒ 用户改了它不会被任何人看见。请去 TableMerge 补一个比较分支。");
    }

    private static object? Read(JsonNode cell, string type)
    {
        try
        {
            if (IsNumeric(type)) return cell.GetValue<double>();
            return type switch
            {
                "bool" => cell.GetValue<bool>(),
                _ => cell.GetValue<string>(),   // text / longtext / chip
            };
        }
        catch (InvalidOperationException)
        {
            return null;   // 类型对不上（用户在数字格里打了字之类）：当作空，不炸掉整次抽取
        }
    }

    /// <summary>把连续空白（空格/换行/全角空格）压成一个，用于比较——排版差异不是内容改动。</summary>
    private static string Squash(string s)
        => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>报告里截断长文本（正文/背景故事动辄几百字，全打出来没法看）。</summary>
    private static string Clip(string s, int max = 40)
    {
        s = s.Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    /// <summary>取这一行的主键名（给漂移报告用人话指认是哪一条）。</summary>
    /// <summary>
    /// 取这一行的**人话名字**（给待同步报告用，好让人一眼认出是哪一条）。
    /// <para>
    /// ⚠️ 各分区的主键列名并不一样：物品叫 <c>name</c>、书叫 <c>title</c>、
    /// 而「角色数值 / 烹饪规则 / 全局规则」这类"一行一个数字"的分区叫 <c>label</c>。
    /// 漏了 <c>label</c> 的话，报告里打出来的会是内部 id（<c>read_no_seat</c>）而不是「没座位读书的速度」——
    /// 报告是给人看的，得说人话。
    /// </para>
    /// </summary>
    private static string Name(Dictionary<string, object?> row, string fallback)
        => row.GetValueOrDefault("name") as string
           ?? row.GetValueOrDefault("title") as string
           ?? row.GetValueOrDefault("label") as string
           ?? row.GetValueOrDefault("who") as string
           ?? (fallback.Length == 0 ? "(无名)" : fallback);
}
