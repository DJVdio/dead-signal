using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 SaveCodec.cs / SaveData.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// 存档迁移：把旧版本的存档 JSON **就地升级**到 <see cref="SaveCodec.CurrentVersion"/>。
///
/// <para>
/// <b>为什么会有这个文件</b>（<see cref="SaveCodec"/> 的类注释明写"版本号 + 明确拒绝，不做迁移链"）：
/// 那条规矩的理由是<b>半兼容读档比读不了更糟</b> —— 旧档缺了"手指切除"那个字段，读回来山姆的手指就长回来了，
/// 游戏不报错，玩家也不知情，他只是拿到了一个被悄悄改写过的世界。
/// </para>
/// <para>
/// <b>v2 → v3 不是那种情况。</b>合并「废金属 + 金属锭 → 铁」时，老档里那两样是**纯数量**，
/// 合并是一条确定的算术（铁 = 废×1 + 锭×<see cref="IngotToIronRatio"/>），
/// **没有任何"猜一个不知道的字段"的成分** —— 不是"手指长回来了"，是"两堆金属并成一堆，一克不少"。
/// 而如果按老规矩直接作废老档，就等于<b>拿一个 bug 的修复去吞掉玩家攒的金属</b>（金属锭拿不到本身就是那个 bug）。
/// </para>
/// <para>
/// ⇒ 规矩收窄为：<b>能被无损、确定地修好的旧档才迁移；但凡要猜，一律拒读。</b>
/// 迁不动的版本（v1 及更早）照旧明确拒绝，绝不"尽力而为"地读出半个世界。
/// </para>
/// </summary>
public static class SaveMigration
{
    /// <summary>
    /// [T46] 1 个「金属锭」折算成几个「铁」。与 <c>RecipeBook</c> / <c>WeaponModCatalog</c> / <c>CampStructure</c>
    /// 里配方成本的换算**必须是同一个系数**，否则老档读回来会出现"我的锭变少了/配方变贵了"的静默贬值。
    /// </summary>
    public const int IngotToIronRatio = 2;

    /// <summary>老材料键（v2 及更早）。迁移后这两个键**不允许在存档里再出现任何一处**。</summary>
    private const string LegacyScrapMetalKey = "scrap_metal";
    private const string LegacyMetalIngotKey = "metal_ingot";

    /// <summary>能迁移的最低版本。低于它的存档没有升级路径（那些版本的世界结构已经对不上了）。</summary>
    public const int MinMigratableVersion = 2;

    /// <summary>这个版本号能不能升到当前版本（含"本来就是当前版本"）。</summary>
    public static bool CanMigrate(int version)
        => version >= MinMigratableVersion && version <= SaveCodec.CurrentVersion;

    /// <summary>
    /// 把 <paramref name="json"/> 从 <paramref name="fromVersion"/> 升级到 <see cref="SaveCodec.CurrentVersion"/>。
    /// <para>成功：<paramref name="migrated"/> = 升级后的 JSON 文本，返回 <c>true</c>。</para>
    /// <para>失败：<paramref name="error"/> = <b>给玩家看的中文</b>，返回 <c>false</c>。<b>绝不返回半个世界</b>。</para>
    /// </summary>
    public static bool TryMigrate(string json, int fromVersion, out string? migrated, out string? error)
    {
        migrated = null;
        error = null;

        if (fromVersion == SaveCodec.CurrentVersion)
        {
            migrated = json;      // 已是当前版本，原样放行
            return true;
        }

        if (!CanMigrate(fromVersion))
        {
            error = fromVersion < MinMigratableVersion
                ? $"这份存档是旧版本的（v{fromVersion}），当前版本 v{SaveCodec.CurrentVersion} 没有从它升级的路径。游戏还在开发中，规则改动会让旧存档失效。"
                : $"这份存档来自更新的版本（v{fromVersion}），当前版本 v{SaveCodec.CurrentVersion} 读不了它。";
            return false;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, nodeOptions: null, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException e)
        {
            error = $"存档已损坏，读不出来（{e.Message}）。";
            return false;
        }

        if (root is not JsonObject obj)
        {
            error = "存档已损坏，读不出来。";
            return false;
        }

        // v2 → v3：废金属 + 金属锭 → 铁
        if (fromVersion < 3)
        {
            MergeLegacyMetalsIntoIron(obj);
        }

        // 🔴 兜底：迁移后但凡还剩一个老键，说明有一处存档结构是我没走到的 ⇒ **明确失败，不许静默吞物品**。
        // 宁可让玩家看见"这份存档迁移失败"，也不能让他读回一个"金属凭空少了一半"的世界。
        if (FindFirstLegacyKey(obj) is string leftover)
        {
            error = $"存档迁移失败：还有没能合并到「铁」的旧材料（{leftover}）。为免弄丢你的东西，这份存档没有被读取。";
            return false;
        }

        obj["Version"] = SaveCodec.CurrentVersion;
        migrated = obj.ToJsonString();
        return true;
    }

    // ---- v2 → v3 ----

    /// <summary>
    /// 把整棵存档树里的「废金属 / 金属锭」全部改写成「铁」。
    /// <para>
    /// <b>为什么是"整棵树递归"而不是"手写那 5 条路径"</b>：材料键藏在存档的好几个角落
    /// （共享库存 / 容器剩余藏物 / 尸体身上没扒走的东西 / 远征背包 / 商人货架），
    /// 手写路径**漏一处就吞一批物品**，而且以后谁新加一个装材料的字段，这里也不会自动跟上。
    /// 递归按**形状**认（认 <c>RefKey</c>/<c>RefId</c> 这两种带材料键的物品形状），漏不掉，也不用维护路径清单。
    /// </para>
    /// </summary>
    private static void MergeLegacyMetalsIntoIron(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                RewriteItemSave(obj);
                RewriteLootItem(obj);
                foreach (KeyValuePair<string, JsonNode?> kv in obj.ToList())
                {
                    if (kv.Value is not null)
                    {
                        MergeLegacyMetalsIntoIron(kv.Value);
                    }
                }
                break;

            case JsonArray arr:
                foreach (JsonNode? el in arr.ToList())
                {
                    if (el is not null)
                    {
                        MergeLegacyMetalsIntoIron(el);
                    }
                }
                // 先把孩子改写成铁，再把同一个列表里的**多堆铁并成一堆**（废一堆 + 锭一堆 ⇒ 库存里会出现两行「铁」）
                CoalesceIronStacks(arr);
                break;
        }
    }

    /// <summary>一件库存物品（<see cref="ItemSave"/> 形状：<c>RefKey</c> + <c>MaterialQuantity</c>）。</summary>
    private static void RewriteItemSave(JsonObject obj)
    {
        if (!TryLegacyKey(obj, "RefKey", out string? legacy))
        {
            return;
        }

        obj["RefKey"] = Materials.IronKey;
        obj["MaterialQuantity"] = Convert(ReadInt(obj, "MaterialQuantity"), legacy!);

        // ⚠️ 这里**故意改写了显示名与描述文案**，而 ItemSave 的注释明写"照抄、不走目录查表重建"。
        // 那条规矩防的是"改一句 flavor 就把旧存档里的物品悄悄改了"；而这次改的**不是文案，是物品本身**——
        // 一件叫「金属锭」、描述写着"熔炼提纯"的东西，在 v3 的世界里已经不存在了。
        // 留着老名字只会让玩家的库存里出现一个查不到目录、点了没反应的幽灵物品。
        if (Materials.Find(Materials.IronKey) is { } iron)
        {
            obj["DisplayName"] = iron.DisplayName;
            obj["Description"] = iron.Description;
        }
    }

    /// <summary>一条掉落（<see cref="LootItem"/> 形状：<c>RefId</c> + <c>Quantity</c>）：容器藏物 / 尸体身上 / 远征背包。</summary>
    private static void RewriteLootItem(JsonObject obj)
    {
        if (!TryLegacyKey(obj, "RefId", out string? legacy))
        {
            return;
        }

        obj["RefId"] = Materials.IronKey;
        obj["Quantity"] = Convert(ReadInt(obj, "Quantity"), legacy!);
    }

    /// <summary>
    /// 把同一个列表里的多堆铁并成一堆（只并**库存物品**形状，不并掉落列表）。
    /// <para>
    /// 掉落列表<b>刻意不并</b>：容器里的每条 <c>LootItem</c> 是一次"搜出来的东西"，
    /// 把「铁×2 + 铁×2」并成「铁×4」会改掉搜刮的节奏（两次变一次）。而库存是按堆合计的
    /// （<c>InventoryStore.MaterialCount</c> 跨堆求和、<c>TrySpendMaterial</c> 跨堆实扣），
    /// 并不并都不会算错账 —— 并起来纯粹是为了**别在库存面板上列出两行「铁」**。
    /// </para>
    /// </summary>
    private static void CoalesceIronStacks(JsonArray arr)
    {
        List<JsonObject> irons = arr.OfType<JsonObject>().Where(IsIronItemSave).ToList();
        if (irons.Count < 2)
        {
            return;
        }

        int total = irons.Sum(o => ReadInt(o, "MaterialQuantity"));
        irons[0]["MaterialQuantity"] = total;

        foreach (JsonObject dup in irons.Skip(1))
        {
            arr.Remove(dup);
        }
    }

    private static bool IsIronItemSave(JsonObject obj)
        => obj["RefKey"]?.GetValue<string>() == Materials.IronKey
        && obj.ContainsKey("MaterialQuantity")
        && obj.ContainsKey("Category");

    // ---- 小工具 ----

    private static bool TryLegacyKey(JsonObject obj, string field, out string? legacy)
    {
        legacy = null;
        if (!obj.TryGetPropertyValue(field, out JsonNode? v) || v is null)
        {
            return false;
        }

        string? key = v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
        if (key is not (LegacyScrapMetalKey or LegacyMetalIngotKey))
        {
            return false;
        }

        legacy = key;
        return true;
    }

    /// <summary>废 ×1 / 锭 ×<see cref="IngotToIronRatio"/>。</summary>
    private static int Convert(int quantity, string legacyKey)
        => legacyKey == LegacyMetalIngotKey ? quantity * IngotToIronRatio : quantity;

    private static int ReadInt(JsonObject obj, string field)
        => obj.TryGetPropertyValue(field, out JsonNode? v)
        && v is not null
        && v.GetValueKind() == JsonValueKind.Number
        && v.AsValue().TryGetValue(out int n) ? n : 0;

    /// <summary>整棵树里还有没有残留的老材料键（迁移后的自检）。返回第一个找到的，没有则 null。</summary>
    private static string? FindFirstLegacyKey(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (KeyValuePair<string, JsonNode?> kv in obj)
                {
                    if (kv.Value is null)
                    {
                        continue;
                    }
                    if (kv.Value.GetValueKind() == JsonValueKind.String
                        && kv.Value.GetValue<string>() is LegacyScrapMetalKey or LegacyMetalIngotKey)
                    {
                        return kv.Value.GetValue<string>();
                    }
                    if (FindFirstLegacyKey(kv.Value) is string found)
                    {
                        return found;
                    }
                }
                return null;

            case JsonArray arr:
                foreach (JsonNode? el in arr)
                {
                    if (el is not null && FindFirstLegacyKey(el) is string found)
                    {
                        return found;
                    }
                }
                return null;

            default:
                return null;
        }
    }
}
