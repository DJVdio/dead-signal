using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 材料作为物品 + 材料目录（配方系统地基之一）的单测。
// 覆盖：Item.Material 类别/引用键/堆叠数量、MaterialDef→Item 落地、Materials 目录 All/Find/Has/InCategory、
// 材料与既有库存分类/可装备/食物合计的隔离。
public class MaterialsTests
{
    // ---- Item.Material 构造 / 类别 / 引用键 / 堆叠数量 ----

    [Fact]
    public void Material_item_carries_category_ref_key_and_quantity()
    {
        var item = Item.Material("wood", "木料", 5, "劈好的木料");
        Assert.Equal(ItemCategory.Material, item.Category);
        Assert.Equal("木料", item.DisplayName);
        Assert.Equal("wood", item.RefKey); // 引用键=材料标识名
        Assert.Equal(5, item.MaterialQuantity);
        Assert.Equal("劈好的木料", item.Description);
        Assert.False(item.IsEquippable);
        Assert.Equal(0, item.FoodQuantity); // 材料不占食物份数
    }

    [Fact]
    public void Material_quantity_defaults_to_one_and_clamps_to_non_negative()
    {
        Assert.Equal(1, Item.Material("wood", "木料").MaterialQuantity);
        Assert.Equal(0, Item.Material("wood", "木料", -3).MaterialQuantity);
    }

    [Fact]
    public void Materials_with_same_content_are_value_equal()
    {
        Assert.Equal(Item.Material("wood", "木料", 2), Item.Material("wood", "木料", 2));
        Assert.NotEqual(Item.Material("wood", "木料", 2), Item.Material("wood", "木料", 3));
        Assert.NotEqual(Item.Material("wood", "木料", 2), Item.Material("stone", "石料", 2));
    }

    // ---- MaterialDef → Item 落地 ----

    [Fact]
    public void MaterialDef_ToItem_refs_its_key_and_carries_display_fields()
    {
        MaterialDef def = Materials.Find("wood")!.Value;
        Item item = def.ToItem(4);
        Assert.Equal(ItemCategory.Material, item.Category);
        Assert.Equal(def.Key, item.RefKey);
        Assert.Equal(def.DisplayName, item.DisplayName);
        Assert.Equal(def.Description, item.Description);
        Assert.Equal(4, item.MaterialQuantity);
    }

    [Fact]
    public void MaterialDef_ToItem_defaults_to_one()
    {
        Assert.Equal(1, Materials.Find("nails")!.Value.ToItem().MaterialQuantity);
    }

    // ---- Materials 目录 ----

    [Fact]
    public void Catalog_is_non_empty_and_covers_required_basics()
    {
        // 拟定草稿要求覆盖的基础材料标识（用户后续可增删调整）。
        string[] required =
        {
            "wood", "cloth", "iron", "leather", "rawhide",
            "bone", "nails", "wire", "gunpowder", "tanning_solution",
        };
        foreach (string key in required)
        {
            Assert.True(Materials.Has(key), $"目录缺少基础材料：{key}");
        }
    }

    /// <summary>
    /// [T46] 「废金属」与「金属锭」已合并为「铁」——**两个老键必须彻底退役**。
    /// <para>
    /// 留一个在目录外、却还被某条配方引用着，就等于**造出一条永远做不出来的配方**
    /// （这正是合并前金属锭的处境：12 条配方吃它，而它没有任何获取途径）。
    /// </para>
    /// </summary>
    [Fact]
    public void 废金属与金属锭已退役_全部配方成本里都不许再出现()
    {
        Assert.False(Materials.Has("scrap_metal"));
        Assert.False(Materials.Has("metal_ingot"));
        Assert.True(Materials.Has(Materials.IronKey));

        foreach (RecipeData r in RecipeBook.All)
        {
            Assert.False(r.MaterialCosts.ContainsKey("scrap_metal"), $"配方「{r.DisplayName}」还在吃已退役的废金属");
            Assert.False(r.MaterialCosts.ContainsKey("metal_ingot"), $"配方「{r.DisplayName}」还在吃已退役的金属锭");
        }
    }

    /// <summary>
    /// 🔴 [T46] **每一种材料成本，都必须是目录里查得到的键**。
    /// <para>
    /// 这条护栏是本单的"起因测试"：合并前 <c>metal_ingot</c> 虽在目录里，却<b>零获取途径</b>，
    /// 于是吃它的 12 条配方全是死配方。目录在不在是**最低门槛**——键要是连目录都查不到，
    /// 配方面板上会直接列出一条材料名都显示不出来的东西。
    /// </para>
    /// </summary>
    [Fact]
    public void 所有配方吃的材料_都必须在材料目录里查得到()
    {
        foreach (RecipeData r in RecipeBook.All)
        {
            foreach (string key in r.MaterialCosts.Keys)
            {
                Assert.True(Materials.Has(key), $"配方「{r.DisplayName}」吃了一个目录里没有的材料：{key}");
            }
        }
    }

    // ════════════ [T46] 「死材料」回归护栏 ════════════
    // 本单的起因：**金属锭是个拿不到的物品**（零配方产出 / 零掉落 / 废墟不出 / 商人不卖），
    // 而 12 条配方吃它 ⇒ 那 12 样东西**一个都造不出来**，游戏不报错、玩家也不知道为什么灰着。
    // 下面两条护栏就是要让这类 bug **下次一写出来就红**，而不是等一次评审去人肉盘。

    /// <summary>仓库根（不写死绝对路径，同 <c>RealCampCoverTests</c>）。</summary>
    private static string RepoRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "godot", "data", "camp.json")))
            {
                return d.FullName;
            }
        }
        throw new FileNotFoundException("从测试程序集向上未找到 godot/data/camp.json");
    }

    /// <summary>真实 <c>godot/data/camp.json</c> 里废墟掉出来的全部材料 id。</summary>
    private static IEnumerable<string> RubbleMaterialIds()
    {
        using JsonDocument doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot(), "godot", "data", "camp.json")));

        foreach (JsonElement prop in doc.RootElement.EnumerateObject()
                     .Where(p => p.Value.ValueKind == JsonValueKind.Array)
                     .SelectMany(p => p.Value.EnumerateArray()))
        {
            if (prop.ValueKind != JsonValueKind.Object
                || !prop.TryGetProperty("drops", out JsonElement drops))
            {
                continue;
            }
            foreach (JsonElement d in drops.EnumerateArray())
            {
                if (d.TryGetProperty("kind", out JsonElement k) && k.GetString() == "material"
                    && d.TryGetProperty("id", out JsonElement id) && id.GetString() is string s)
                {
                    yield return s;
                }
            }
        }
    }

    /// <summary>
    /// 🔴 **每一种被配方消耗的材料，都必须真的拿得到**（掉落 / 废墟 / 或由另一条配方产出）。
    /// <para>
    /// 这条是**本单那个 bug 的直接护栏**：把 <c>metal_ingot</c> 加回配方而不给它任何来源，这条就会红。
    /// "目录里有" ≠ "拿得到" —— 金属锭当年就在目录里，安安稳稳地在那儿躺了很久，谁也没发现它根本刷不出来。
    /// </para>
    /// </summary>
    [Fact]
    public void 配方吃到的每一种材料_都必须真的拿得到_不许有死材料()
    {
        // 获取途径：搜刮点掉落、营地废墟、另一条配方的产物、拆除回收、**宰杀产出**。
        //（「废木料」只从拆木结构里来——漏掉拆除回收会把它误判成死材料。
        //  🔴 [T67] **宰杀是第五条途径，且它不是 RecipeData**（RecipeData 只有单一 OutputKey，
        //  表达不了"一刀出两样东西"）⇒ 羽毛/碎皮革/老鼠肉/鸟肉全从这条来，漏掉它会把整条弓箭线误判成死材料。
        //  这与 WikiSyncT59Tests 那条"食物类死物品"护栏加"上得了案板"是同一处世界形状的两个投影。）
        var obtainable = new HashSet<string>(StringComparer.Ordinal);

        foreach (string quarry in ButcheryLogic.ButcherableKeys)
        {
            ButcherYield y = ButcheryLogic.Resolve(
                ButcherTier.SimplePoint, ButcherKnife.Dagger, quarry, new SequenceRandomSource(0.99))!.Value;
            obtainable.Add(y.MeatKey);
            obtainable.Add(y.ByproductKey);
        }

        foreach (string cacheId in AllCacheIds())
        {
            if (ExplorationCache.Resolve(cacheId, new StoryFlags()) is { } res)
            {
                foreach (LootItem l in res.Loot.Where(l => l.Kind == LootKind.Material))
                {
                    obtainable.Add(l.RefId);
                }
            }
        }
        foreach (string id in RubbleMaterialIds())
        {
            obtainable.Add(id);
        }
        foreach (RecipeData r in RecipeBook.All)
        {
            obtainable.Add(r.OutputKey);

            // 拆掉自己造的东西，能拿回一部分料（木料例外：25% 木料 + 25% 废木料）
            if (SalvageLogic.CanSalvage(r))
            {
                foreach (string key in SalvageLogic.YieldOfRecipe(r).Keys)
                {
                    obtainable.Add(key);
                }
            }
        }
        foreach (StructureTier tier in Enum.GetValues<StructureTier>().Where(SalvageLogic.CanSalvageStructure))
        {
            foreach (string key in SalvageLogic.YieldOfStructure(tier).Keys)
            {
                obtainable.Add(key);
            }
        }

        var dead = new List<string>();
        foreach (RecipeData r in RecipeBook.All)
        {
            foreach (string key in r.MaterialCosts.Keys.Where(k => !obtainable.Contains(k)))
            {
                dead.Add($"「{r.DisplayName}」吃的「{Materials.Find(key)?.DisplayName ?? key}」({key})");
            }
        }

        Assert.True(dead.Count == 0,
            "下列配方吃的材料**没有任何获取途径**（掉落/废墟/配方产出都没有）⇒ 这些配方永远造不出来：\n  "
            + string.Join("\n  ", dead.Distinct()));
    }

    /// <summary>
    /// 🔴 **消费层自检**：真实 <c>godot/data/camp.json</c> 的废墟掉落，material id 必须条条查得到目录。
    /// <para>
    /// 本项目有个反复出现的静默失效模式：**纯逻辑绿、消费层没跟上**。合并材料时只改了 C# 目录、
    /// 忘了改 <c>camp.json</c>，废墟就会掉出一堆查不到目录的幽灵物品——不报错，只是永远用不掉。
    /// （<c>ExplorationCache</c> 那一侧由 <c>ClothMergeTests</c> 的同名护栏盯着。）
    /// </para>
    /// </summary>
    [Fact]
    public void 真实camp_json_的废墟掉落_每条material_id都查得到目录()
    {
        List<string> dangling = RubbleMaterialIds().Where(id => !Materials.Has(id)).Distinct().ToList();

        Assert.True(dangling.Count == 0, "camp.json 的废墟掉落了目录里不存在的材料：" + string.Join("、", dangling));
    }

    /// <summary>[T46] 铁在真实数据里**确实能捡到**——搜刮点与废墟两条路至少各有一条。</summary>
    [Fact]
    public void 铁在真实数据里能捡到_搜刮点与废墟都有()
    {
        Assert.Contains(Materials.IronKey, RubbleMaterialIds());

        bool inCaches = AllCacheIds()
            .Select(id => ExplorationCache.Resolve(id, new StoryFlags()))
            .Where(r => r is not null)
            .SelectMany(r => r!.Value.Loot)
            .Any(l => l.Kind == LootKind.Material && l.RefId == Materials.IronKey);

        Assert.True(inCaches, "搜刮点一处都不掉铁——铁会变成第二个「金属锭」");
    }

    /// <summary>全部搜刮点 id（反射取 <c>ExplorationCache</c> 的 <c>cache_*</c> 常量，同 <c>ClothMergeTests</c>）。</summary>
    private static IEnumerable<string> AllCacheIds() =>
        typeof(ExplorationCache)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith("Id", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.StartsWith("cache_", StringComparison.Ordinal));

    [Fact]
    public void Catalog_keys_are_unique()
    {
        var keys = Materials.All.Select(m => m.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Every_entry_has_key_display_name_and_description()
    {
        Assert.All(Materials.All, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Key));
            Assert.False(string.IsNullOrWhiteSpace(m.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(m.Description));
        });
    }

    [Fact]
    public void Find_returns_matching_def_and_null_for_unknown()
    {
        Assert.Equal("火药", Materials.Find("gunpowder")!.Value.DisplayName);
        Assert.Null(Materials.Find("does_not_exist"));
        Assert.Null(Materials.Find(null!));
    }

    [Fact]
    public void Has_reports_membership()
    {
        Assert.True(Materials.Has("leather"));
        Assert.False(Materials.Has("does_not_exist"));
        Assert.False(Materials.Has(null!));
    }

    /// <summary>
    /// [波1·item2] **精密零件自成一类**：机械零件 / 子弹零件 / 武器零件三味"造不出来、只能捡"的精密件
    /// 从原先散落的 <see cref="MaterialCategory.Misc"/>（机械零件）/ <see cref="MaterialCategory.Metal"/>
    /// （子弹零件、武器零件）迁入新枚举 <see cref="MaterialCategory.Component"/>。
    /// <para>
    /// 这条护栏钉死三点：① 新枚举存在且这三味归它；② 它们**不再**留在旧类里（迁移彻底，不是复制）；
    /// ③ 精密零件类**只装这三味**（别把别的材料顺手塞进来）。wiki 材料表靠它显示「精密零件」分类。
    /// </para>
    /// </summary>
    [Fact]
    public void 精密零件三味_归入Component类_且已离开Misc与Metal()
    {
        string[] parts = { "components", "bullet_parts", "weapon_parts" };
        foreach (string key in parts)
        {
            MaterialDef def = Materials.Find(key)!.Value;
            Assert.Equal(MaterialCategory.Component, def.Category);
        }

        // 迁移必须彻底：旧类里一味都不许再有它们
        foreach (MaterialDef m in Materials.InCategory(MaterialCategory.Misc))
        {
            Assert.DoesNotContain(m.Key, parts);
        }
        foreach (MaterialDef m in Materials.InCategory(MaterialCategory.Metal))
        {
            Assert.DoesNotContain(m.Key, parts);
        }

        // 精密零件类恰好只装这三味
        Assert.Equal(
            parts.OrderBy(k => k),
            Materials.InCategory(MaterialCategory.Component).Select(m => m.Key).OrderBy(k => k));
    }

    [Fact]
    public void InCategory_filters_by_category_and_all_categories_represented()
    {
        // 六类每类至少一条，且筛出的每条类别一致。
        foreach (MaterialCategory cat in System.Enum.GetValues<MaterialCategory>())
        {
            var inCat = Materials.InCategory(cat).ToList();
            Assert.NotEmpty(inCat);
            Assert.All(inCat, m => Assert.Equal(cat, m.Category));
        }
    }

    // ---- 与既有库存分类的隔离 ----

    [Fact]
    public void Materials_flow_through_inventory_ByCategory_without_touching_food_or_equippable()
    {
        var store = new InventoryStore();
        store.Add(Item.Weapon("匕首"));
        store.Add(Item.Food(3));
        store.Add(Materials.Find("wood")!.Value.ToItem(10));
        store.Add(Materials.Find("iron")!.Value.ToItem(4));

        Assert.Equal(2, store.ByCategory(ItemCategory.Material).Count()); // 材料按类别筛出
        Assert.Equal(3, store.TotalFood); // 材料不计入食物合计
        Assert.Single(store.Equippable);  // 材料不可装备
    }
}
