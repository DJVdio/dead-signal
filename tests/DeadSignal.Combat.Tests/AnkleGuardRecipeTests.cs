using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [A2]《护踝鞋具》配方补齐 —— armor.json 早有该护甲（authored [T72]），却无配方/无掉落 ⇒ 造不出的死物品。
/// 意图护栏（先红/结构性红 → 绿）：①配方存在且门槛=《尖峰时刻》；②没读书造不出、读了书能造；
/// ③材料成本已外置 recipes.json（非默认 0）；④产物落地成一件真护甲 Item.Armor(护踝鞋具)、且穿戴目录登记在脚槽。
///
/// 补齐前：RecipeBook.All 无 OutputKey=="ankle_guard" ⇒ 下面 Single(...) 抛异常（结构性红）；
/// CraftOutputFactory 的 ArmorOutputs 无此 key ⇒ 产物会落进"家具/杂项材料堆"分支变成戴不上的杂物（静默失效）。
/// </summary>
public class AnkleGuardRecipeTests
{
    private static RecipeData Recipe()
        => RecipeBook.All.Single(r => r.OutputKey == "ankle_guard");

    [Fact]
    public void 护踝鞋具配方_门槛是尖峰时刻()
    {
        RecipeData r = Recipe();
        Assert.Equal("护踝鞋具", r.DisplayName);
        Assert.Contains(BookLibrary.PeakHourId, r.RequiredBookIds);
    }

    [Fact]
    public void 没读尖峰时刻_造不出护踝鞋具()
    {
        CraftAvailability a = CraftingLogic.CanCraft(
            Recipe(),
            availableMaterial: _ => 99,
            isBookRead: _ => false,
            installedTools: new HashSet<ToolSlot>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void 读了尖峰时刻并有材料_可造护踝鞋具()
    {
        CraftAvailability a = CraftingLogic.CanCraft(
            Recipe(),
            availableMaterial: _ => 99,
            isBookRead: id => id == BookLibrary.PeakHourId,
            installedTools: new HashSet<ToolSlot>());
        Assert.True(a.CanCraft);
    }

    [Fact]
    public void 护踝鞋具_材料成本已外置非空()
    {
        RecipeData r = Recipe();
        Assert.NotEmpty(r.MaterialCosts);          // recipes.json 已配（否则 fail-fast）
        Assert.All(r.MaterialCosts.Values, q => Assert.True(q > 0));
        Assert.Equal(1, r.OutputQuantity);          // 一件占一只脚（成对，双侧要两件）
    }

    [Fact]
    public void 护踝鞋具产物_落地成真护甲_脚槽登记()
    {
        // 产物落成 Item.Armor（同名引用键），不落"家具/杂项材料堆"。
        List<Item> made = CraftOutputFactory.Create("ankle_guard", 1).ToList();
        Assert.Single(made);
        Assert.Equal("护踝鞋具", made[0].DisplayName);
        Assert.Equal(ItemCategory.Armor, made[0].Category);

        // 穿戴目录登记在脚槽（成对·与运动鞋同槽互斥，一件占一只脚）。
        ApparelCatalog.ApparelDef def = ApparelCatalog.Defs["护踝鞋具"];
        Assert.True(def.Paired);
        Assert.Contains(EquipSlot.LeftFoot, def.Slots);
        Assert.Contains(EquipSlot.RightFoot, def.Slots);
    }

    [Fact]
    public void Wiki数据_index和bundle_收录护踝鞋具并保持一致()
    {
        string dataDir = Path.Combine(RepoRoot(), "docs", "wiki", "data");
        string recipesText = File.ReadAllText(Path.Combine(dataDir, "recipes.json"));
        using JsonDocument recipes = JsonDocument.Parse(recipesText);
        JsonElement row = recipes.RootElement.GetProperty("rows")
            .EnumerateArray()
            .SingleOrDefault(r => r.GetProperty("_id").GetString() == "ankle_guard");

        Assert.NotEqual(JsonValueKind.Undefined, row.ValueKind);
        Assert.Equal("护踝鞋具", row.GetProperty("name").GetString());
        Assert.Equal("杂项", row.GetProperty("category").GetString());
        Assert.Equal("护甲/服装", row.GetProperty("productType").GetString());
        Assert.Equal("ankle_guard", row.GetProperty("output").GetString());
        Assert.Equal(1, row.GetProperty("outputQty").GetInt32());
        Assert.Equal("皮革*2、绳子*1", row.GetProperty("materials").GetString());
        Assert.Equal("工作台（徒手）", row.GetProperty("craftLocation").GetString());
        Assert.Equal("《尖峰时刻》", row.GetProperty("books").GetString());
        Assert.Equal(80, row.GetProperty("workMinutes").GetInt32());
        Assert.Equal("armor/ankle_guard", row.GetProperty("_icon").GetString());

        using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "index.json")));
        JsonElement indexCategory = index.RootElement.GetProperty("categories")
            .EnumerateArray()
            .Single(c => c.GetProperty("id").GetString() == "recipes");
        int rowCount = recipes.RootElement.GetProperty("rows").GetArrayLength();
        Assert.Equal(57, rowCount);
        Assert.Equal(rowCount, indexCategory.GetProperty("count").GetInt32());

        string bundleText = File.ReadAllText(Path.Combine(dataDir, "bundle.js"));
        const string recipesMarker = "    \"recipes\": ";
        int recipesStart = bundleText.IndexOf(recipesMarker, StringComparison.Ordinal);
        int recipesEnd = bundleText.IndexOf(",\n    \"lights\":", recipesStart, StringComparison.Ordinal);
        Assert.True(recipesStart >= 0 && recipesEnd > recipesStart, "bundle.js 应包含可提取的 recipes 分区");
        using JsonDocument bundleRecipes = JsonDocument.Parse(
            bundleText[(recipesStart + recipesMarker.Length)..recipesEnd]);
        Assert.Equal(rowCount, bundleRecipes.RootElement.GetProperty("rows").GetArrayLength());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(recipesText), JsonNode.Parse(bundleRecipes.RootElement.GetRawText())));
        Assert.Contains("\"id\": \"recipes\",\n      \"label\": \"配方\",\n      \"file\": \"recipes.json\",\n      \"count\": 57,", bundleText);
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        for (DirectoryInfo? d = new(File.Exists(thisFile) ? Path.GetDirectoryName(thisFile)! : thisFile);
             d is not null;
             d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "docs", "wiki", "data"))) return d.FullName;
        }

        throw new DirectoryNotFoundException("从测试文件位置向上未找到仓库根（docs/wiki/data）");
    }
}
