using System;
using System.IO;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 用户拍板删除「干豆」(beans)：这条护栏钉死「删干净、无死引用」。
///
/// <para>干豆曾横跨六个面：材料目录(<see cref="Materials"/>)、重量登记(<see cref="ItemRegistry"/>)、
/// 热量点(<see cref="FoodCalories"/>)、图标映射(<see cref="ItemIcons"/>)、搜刮点掉落(<see cref="ExplorationCache"/>)、
/// 开局库存(camp.json)。任一处漏删都是**悬空引用**——玩家会捡到/吃到一个没有定义的物品键。</para>
/// </summary>
public class BeansRemovalTests
{
    private const string BeansKey = "beans";

    // ---- 材料目录 / 重量登记 / 热量点 / 图标：四张表都不许再有干豆 ----

    [Fact]
    public void 材料目录里没有干豆()
    {
        Assert.False(Materials.Has(BeansKey), "Materials 目录仍登记着 beans");
        Assert.Null(Materials.Find(BeansKey));
        Assert.DoesNotContain(Materials.All, m => m.Key == BeansKey);
        Assert.DoesNotContain(Materials.All, m => m.DisplayName == "干豆");
    }

    [Fact]
    public void 重量登记表里没有干豆()
        => Assert.False(ItemRegistry.Materials.ContainsKey(BeansKey), "ItemRegistry 仍给 beans 登记了重量");

    [Fact]
    public void 热量点表里没有干豆()
    {
        Assert.False(FoodCalories.Has(BeansKey), "FoodCalories 仍认 beans 是食材");
        Assert.Equal(0, FoodCalories.Of(BeansKey));
    }

    [Fact]
    public void 图标映射里没有干豆()
        => Assert.Equal(ItemIcons.PlaceholderPath, ItemIcons.PathFor(BeansKey));

    // ---- 搜刮点：曾投放干豆的四处，掉落表里都不许再出现它 ----

    [Theory]
    [InlineData(ExplorationCache.RangersCabinPantryId)]
    [InlineData(ExplorationCache.SupermarketHoardFoodId)]
    [InlineData(ExplorationCache.StuartPantryId)]
    [InlineData(ExplorationCache.StuartRootCellarId)]
    public void 搜刮点掉落表里没有干豆(string cacheId)
    {
        CacheResult r = ExplorationCache.Resolve(cacheId, new StoryFlags())!.Value;
        Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && l.RefId == BeansKey);
        // 删了不许留空点：每处仍要给出至少一件东西（否则叙事承诺了物资却什么都不掉）。
        Assert.NotEmpty(r.Loot);
    }

    // ---- 开局库存：camp.json 里不许再有 beans 这个物料 id ----

    [Fact]
    public void 开局库存camp_json里没有干豆()
    {
        string text = File.ReadAllText(CampJsonPath());
        Assert.DoesNotContain("\"beans\"", text);
    }

    /// <summary>从测试程序集向上找仓库根，定位 <c>godot/data/camp.json</c>（不写死绝对路径）。</summary>
    private static string CampJsonPath()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "godot", "data", "camp.json");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException("从测试程序集向上未找到 godot/data/camp.json");
    }
}
