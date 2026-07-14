using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeadSignal.WikiExtract;

// ═══════════════════════════════════════════════════════════════════════════
// 「备注」= 用户写给 agent 看的设计笔记（"这把锤子应该能砸开保险箱"）。
//
// 它**不是游戏数据**：代码里没有对应字段，也不该有 ⇒ 报成"待同步进代码"是在撒谎（没有代码位置可同步）。
//
// 🔴 但它有一个**必须解决的危险**：
//    今天刚栽过一次 —— 用户写在「效果」列里的设计意图（家具速度+5%、弓箭射程+10%…）
//    被静默吞了整整一天，**没有任何人知道他提过**。
//    「备注」列天生就是这种东西的温床：他写下"这把刀该有 X 效果"，然后……然后就没有然后了。
//
// ⇒ 本类的全部意义：**让新写的备注跳出来，让处理过的安静下去**。
//
//    · 抽取器每次跑完，单列一节「📝 用户备注（待处理）」
//    · **只报"新写的 / 改过的"** —— 已经处理过的不再刷屏（否则第二次跑就变成噪音，然后被无视，
//      那就跟没有这个机制一样了）
//    · 怎么区分：把每条备注的**内容哈希**记在 `docs/wiki/data/notes-ack.json` 里。
//      内容 ≠ 已记录的哈希 ⇒ 就是新的或改过的 ⇒ 报。
//    · agent 处理完一批备注后，跑 `dotnet run --project tools/WikiExtract -- --ack-notes` 认领它们。
//      用户之后再改那条备注 ⇒ 哈希又变了 ⇒ **再次跳出来**。
//
//    ⚠️ ack 记的是**哈希不是"已读"布尔** —— 布尔会让"改过的备注"混过去（标记还在，内容却变了）。
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>一条待处理的用户备注。</summary>
internal sealed record PendingNote(string Category, string RowName, string Id, string Text);

internal static class UserNotes
{
    /// <summary>
    /// 备注列的 key（全分区统一）。
    /// ⚠️ **不能叫 <c>note</c>** —— 武器改装 / 烹饪规则 / 全局规则的「说明」列本来就用着这个键，
    /// 撞上去会把它们的说明文本当成用户备注报出来（第一版就这么撞了，26 条全是误报）。
    /// </summary>
    internal const string Key = "userNote";

    private const string AckFileName = "notes-ack.json";

    /// <summary>
    /// 扫出**新写的 / 改过的**备注。已经被 <c>--ack-notes</c> 认领过、且内容没变的，不再报。
    /// </summary>
    internal static IReadOnlyList<PendingNote> Pending(IReadOnlyList<Category> categories, string dataDir)
    {
        Dictionary<string, string> ack = LoadAck(dataDir);
        var pending = new List<PendingNote>();

        foreach (Category c in categories)
        {
            foreach (Dictionary<string, object?> row in c.Rows)
            {
                if (row.GetValueOrDefault(Key) is not string text || text.Trim().Length == 0) continue;

                string id = row.GetValueOrDefault("_id") as string ?? "";
                string ackKey = $"{c.Id}/{id}";

                // 内容哈希 ≠ 已认领的哈希 ⇒ 新的或改过的
                if (ack.TryGetValue(ackKey, out string? seen) && seen == Hash(text)) continue;

                pending.Add(new PendingNote(c.Label, RowName(row, id), id, text.Trim()));
            }
        }
        return pending;
    }

    /// <summary>把当前全部备注记为「已处理」（agent 处理完一批后跑 <c>-- --ack-notes</c>）。</summary>
    internal static int Acknowledge(IReadOnlyList<Category> categories, string dataDir)
    {
        var ack = new JsonObject();
        int n = 0;
        foreach (Category c in categories)
        {
            foreach (Dictionary<string, object?> row in c.Rows)
            {
                if (row.GetValueOrDefault(Key) is not string text || text.Trim().Length == 0) continue;
                string id = row.GetValueOrDefault("_id") as string ?? "";
                ack[$"{c.Id}/{id}"] = Hash(text);
                n++;
            }
        }

        var doc = new JsonObject
        {
            ["note"] = "「备注」列的已处理标记（内容哈希）。用户改了备注 ⇒ 哈希变 ⇒ 下次抽取会重新报出来。"
                       + "agent 处理完一批备注后跑 `dotnet run --project tools/WikiExtract -- --ack-notes` 更新它。自动生成，勿手改。",
            ["acked"] = ack,
        };
        File.WriteAllText(
            Path.Combine(dataDir, AckFileName),
            doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return n;
    }

    private static Dictionary<string, string> LoadAck(string dataDir)
    {
        string path = Path.Combine(dataDir, AckFileName);
        if (!File.Exists(path)) return new Dictionary<string, string>();

        try
        {
            JsonNode? acked = JsonNode.Parse(File.ReadAllText(path))?["acked"];
            if (acked?.AsObject() is not { } o) return new Dictionary<string, string>();

            var d = new Dictionary<string, string>();
            foreach ((string k, JsonNode? v) in o)
            {
                if (v?.GetValue<string>() is { } h) d[k] = h;
            }
            return d;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();   // ack 文件坏了 ⇒ 当作全都没处理过（宁可多报，不可漏报）
        }
    }

    /// <summary>内容哈希（不是"已读"布尔——布尔会让**改过的**备注混过去：标记还在，内容却变了）。</summary>
    private static string Hash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim())))[..16];

    private static string RowName(Dictionary<string, object?> row, string fallback)
        => row.GetValueOrDefault("name") as string
           ?? row.GetValueOrDefault("title") as string
           ?? row.GetValueOrDefault("label") as string
           ?? (fallback.Length == 0 ? "(无名)" : fallback);
}
