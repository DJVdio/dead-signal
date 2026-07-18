using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 布料/破布合并为单一材料「布」（用户拍板：不区分布料与破布）。
/// 合并口径：保留 key <c>cloth</c>、显示名改「布」，<c>scrap_cloth</c> 退役；
/// 凡同时消耗/投放两者之处按**单位相加**合并（纺织物总量守恒，经济零净变）。
/// 另含全仓「材料引用无悬空」的完整性自检（配方 + 探索点战利品 + 商人价表）。
/// </summary>
public class ClothMergeTests
{
    // ---- 材料目录 ----

    [Fact]
    public void ScrapCloth_IsRetired()
    {
        Assert.False(Materials.Has("scrap_cloth"), "破布已并入布，目录不应再有 scrap_cloth");
        Assert.Null(Materials.Find("scrap_cloth"));
    }

    [Fact]
    public void Cloth_IsTheSingleTextile_NamedBu()
    {
        MaterialDef cloth = Materials.Find("cloth") ?? throw new InvalidOperationException("目录缺少 cloth");
        Assert.Equal("布", cloth.DisplayName);
        Assert.Equal(MaterialCategory.Cloth, cloth.Category);
        Assert.False(string.IsNullOrWhiteSpace(cloth.Description));

        // Cloth 类目下有且仅有这一种材料。
        Assert.Single(Materials.All.Where(m => m.Category == MaterialCategory.Cloth));
    }

    // ---- 配方用量：当前合并后的 Wiki 成本 ----

    [Theory]
    [InlineData("bone_knife", 1)]      // 旧 破布1               → 布1
    [InlineData("cloth_vest", 2)]      // 当前 Wiki 值             → 布2
    [InlineData("cloth_jacket", 6)]    // 旧 布料3 + 破布3       → 布6
    [InlineData("torch", 1)]           // 旧 破布1               → 布1
    [InlineData("dog_cloth_vest", 2)]  // 当前 Wiki 值             → 布2
    [InlineData("dog_pocket_vest", 3)] // 旧 布料1 + 破布2       → 布3
    [InlineData("dog_wire_helmet", 1)] // 旧 破布1               → 布1
    public void Recipe_ClothCost_MatchesAcceptedMergedValues(string recipeId, int expectedCloth)
    {
        RecipeData r = RecipeBook.Find(recipeId) ?? throw new InvalidOperationException($"缺少配方 {recipeId}");
        Assert.Equal(expectedCloth, r.MaterialCosts.TryGetValue("cloth", out int n) ? n : 0);
    }

    [Fact]
    public void NoRecipe_StillCosts_ScrapCloth()
    {
        foreach (RecipeData r in RecipeBook.All)
            Assert.False(r.MaterialCosts.ContainsKey("scrap_cloth"), $"配方 {r.Id} 仍在消耗已退役的 scrap_cloth");
    }

    /// <summary>布夹克（带袖）用料应多于粗布背心（无袖）——合并后档次序不变。</summary>
    [Fact]
    public void ClothJacket_CostsMoreCloth_ThanClothVest()
    {
        int jacket = RecipeBook.Find("cloth_jacket")!.MaterialCosts["cloth"];
        int vest = RecipeBook.Find("cloth_vest")!.MaterialCosts["cloth"];
        Assert.True(jacket > vest, $"布夹克({jacket})应比粗布背心({vest})更费布");
    }

    // ---- 商人价表 ----

    [Fact]
    public void Merchant_BuysCloth_ButNotScrapCloth()
    {
        Assert.True(MerchantBuyList.CanSell(Item.Material("cloth", "布", 1)));
        Assert.False(MerchantBuyList.CanSell(Item.Material("scrap_cloth", "破布", 1)));
    }

    // ---- 完整性自检：全仓材料引用无悬空 ----

    [Fact]
    public void EveryRecipeMaterialCost_ResolvesToACatalogMaterial()
    {
        var dangling = new List<string>();
        foreach (RecipeData r in RecipeBook.All)
            foreach (string key in r.MaterialCosts.Keys)
                if (!Materials.Has(key))
                    dangling.Add($"{r.Id} → {key}");

        Assert.True(dangling.Count == 0, "配方引用了不存在的材料：" + string.Join("、", dangling));
    }

    /// <summary>
    /// 反射枚举 <see cref="ExplorationCache"/> 上全部 <c>public const string *Id</c> 探索点，
    /// 逐个 Resolve，断言每件材料掉落的 RefId 都在材料目录里（并行 agent 新加的点也自动纳入）。
    /// </summary>
    [Fact]
    public void EveryCacheLootMaterial_ResolvesToACatalogMaterial()
    {
        var dangling = new List<string>();
        foreach (string cacheId in AllCacheIds())
        {
            CacheResult? res = ExplorationCache.Resolve(cacheId, new StoryFlags());
            if (res is null) continue;
            foreach (LootItem l in res.Value.Loot)
                if (l.Kind == LootKind.Material && !Materials.Has(l.RefId))
                    dangling.Add($"{cacheId} → {l.RefId}");
        }

        Assert.True(dangling.Count == 0, "探索点投放了不存在的材料：" + string.Join("、", dangling));
    }

    [Fact]
    public void NoCache_StillDrops_ScrapCloth()
    {
        var offenders = AllCacheIds()
            .Select(id => (id, res: ExplorationCache.Resolve(id, new StoryFlags())))
            .Where(t => t.res is not null
                        && t.res.Value.Loot.Any(l => l.Kind == LootKind.Material && l.RefId == "scrap_cloth"))
            .Select(t => t.id)
            .ToList();

        Assert.True(offenders.Count == 0, "探索点仍在投放已退役的 scrap_cloth：" + string.Join("、", offenders));
    }

    /// <summary>合并后同一探索点不应出现两条布掉落（应已相加成一条）。</summary>
    [Fact]
    public void NoCache_ListsClothTwice()
    {
        var offenders = AllCacheIds()
            .Select(id => (id, res: ExplorationCache.Resolve(id, new StoryFlags())))
            .Where(t => t.res is not null
                        && t.res.Value.Loot.Count(l => l.Kind == LootKind.Material && l.RefId == "cloth") > 1)
            .Select(t => t.id)
            .ToList();

        Assert.True(offenders.Count == 0, "探索点出现重复的布掉落条目（应合并为一条）：" + string.Join("、", offenders));
    }

    private static IEnumerable<string> AllCacheIds() =>
        typeof(ExplorationCache)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith("Id", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Where(v => v.StartsWith("cache_", StringComparison.Ordinal));
}
