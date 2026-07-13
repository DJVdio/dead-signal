using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 弓弩体系的**消费层接线闭环**：箭是材料吗？可制作的有配方吗？不可制作的**没有**配方吗？
/// 搜刮点投放了吗？造出来的弓查得到武器数值吗？（引擎侧的组合修正规则见 <c>ArcheryTests</c>。）
/// <para>
/// 本文件取代已退役的 <c>BowTests.cs</c>（impl-bow 的单把「自制弓」测试）——那把弓已并入本体系、
/// 改名为「短弓」，其全部断言由 <c>ArcheryTests</c> 按 8 把逐一覆盖。
/// </para>
/// </summary>
public class ArcheryCraftingTests
{
    // ==================== 箭 = 库存材料 ====================

    [Fact]
    public void 四种箭都是库存材料_归弹药类别()
    {
        foreach (ArrowDef arrow in ArrowTable.All)
        {
            MaterialDef? def = Materials.Find(arrow.Key);

            Assert.NotNull(def);
            Assert.Equal(arrow.Name, def!.Value.DisplayName);
            Assert.Equal(MaterialCategory.Ammo, def.Value.Category);
            Assert.Equal(arrow.Description, def.Value.Description);
        }
    }

    [Fact]
    public void 箭的类别键不是材料_它只是个类别()
    {
        // AmmoKeys.Arrow（"ammo_arrow"）是**类别键**（Weapon.AmmoKey 用它表达"这武器吃箭"），
        // 不是躺在库存里的东西。真正的库存物是 4 个具体箭键。
        // 子弹/霰弹是 1 类别 : 1 材料；只有箭是 1 类别 : 4 材料——因为只有箭会反过来改写武器属性。
        Assert.False(Materials.Has(AmmoKeys.Arrow));

        // 对照：子弹/鹿弹每一种都是货真价实的库存材料（impl-ammo 把枪弹按弹体长度分了 4 种）。
        foreach (string gunAmmo in new[]
                 {
                     AmmoKeys.ShortBullet, AmmoKeys.MediumBullet, AmmoKeys.LongBullet, AmmoKeys.Buckshot,
                 })
        {
            Assert.True(Materials.Has(gunAmmo));
        }
    }

    // ==================== 配方：可制作的有，不可制作的没有 ====================

    [Fact]
    public void 五把可制作弓弩_都有配方_且产物能查到武器数值()
    {
        foreach ((string recipeId, string weaponName) in new[]
                 {
                     ("handmade_bow", "短弓"),     // 承接的旧配方：Id 不动，只改了 DisplayName
                     ("recurve_bow", "反曲弓"),
                     ("longbow", "长弓"),
                     ("light_crossbow", "单手轻弩"),
                     ("heavy_crossbow", "双手重弩"),
                 })
        {
            RecipeData? recipe = RecipeBook.Find(recipeId);
            Assert.NotNull(recipe);
            Assert.Equal(weaponName, recipe!.DisplayName);
            Assert.Equal(RecipeCategory.Precision, recipe.Category);   // 卡尺类精工（弓弩）

            // 产物 → CraftOutputFactory 落地为 Item.Weapon → RefKey 应能在武器表里查到同一把武器。
            Item item = CraftOutputFactory.Create(recipe.OutputKey, 1).Single();
            Assert.Equal(ItemCategory.Weapon, item.Category);
            Assert.Equal(weaponName, item.RefKey);
            Assert.Contains(WeaponTable.ArcheryArsenal(), w => w.Name == item.RefKey);
        }
    }

    [Fact]
    public void 三把不可制作的弓弩_绝不能有配方_只能搜刮()
    {
        // 用户拍板：竞技复合弓 / 狩猎弓 / 复合弩「不可制作」。给它们配方＝毁掉它们的稀有性。
        foreach (string name in new[] { "竞技复合弓", "狩猎弓", "复合弩" })
        {
            Assert.DoesNotContain(RecipeBook.All, r => r.DisplayName == name);
        }
    }

    [Fact]
    public void 三种可制作的箭_都有配方_且批量产出()
    {
        foreach (ArrowDef arrow in ArrowTable.Craftable())
        {
            RecipeData recipe = Assert.Single(RecipeBook.All.Where(r => r.OutputKey == arrow.Key));

            Assert.Equal(arrow.Name, recipe.DisplayName);
            Assert.True(recipe.OutputQuantity > 1, "造箭是批量活——一次削一支不合常理，也不好玩");

            // 产物 key 就是材料键 → CraftOutputFactory 自动落地为一堆该材料（无需登记 WeaponOutputs）。
            Item item = CraftOutputFactory.Create(arrow.Key, recipe.OutputQuantity).Single();
            Assert.Equal(ItemCategory.Material, item.Category);
            Assert.Equal(arrow.Key, item.RefKey);
            Assert.Equal(recipe.OutputQuantity, item.MaterialQuantity);
        }
    }

    [Fact]
    public void 碳纤维箭绝不能有配方_稀缺是它唯一的代价()
    {
        Assert.DoesNotContain(RecipeBook.All, r => r.OutputKey == ArrowKeys.Carbon);
        Assert.DoesNotContain(RecipeBook.All, r => r.DisplayName == "碳纤维箭");
    }

    [Fact]
    public void 削尖的木箭_开局即可做_无工具槽无书门槛()
    {
        // 应急货：没箭了不至于打不响。它必须是全表门槛最低的配方之一。
        RecipeData stick = RecipeBook.All.Single(r => r.OutputKey == ArrowKeys.SharpenedStick);

        Assert.Empty(stick.RequiredTools);
        Assert.Empty(stick.RequiredBookIds);
        Assert.Equal(new[] { "wood" }, stick.MaterialCosts.Keys.ToArray());
    }

    [Fact]
    public void 箭一律不吃火药_这是弓相对枪的立身之本()
    {
        // 枪弹要火药 → 要燃料（与火把/发电竞争）。箭只吃木料/金属/布 → 弓弩是**可持续**的远程。
        foreach (ArrowDef arrow in ArrowTable.Craftable())
        {
            RecipeData recipe = RecipeBook.All.Single(r => r.OutputKey == arrow.Key);

            Assert.DoesNotContain("gunpowder", recipe.MaterialCosts.Keys);
            Assert.DoesNotContain("fuel", recipe.MaterialCosts.Keys);
        }
    }

    [Fact]
    public void 弓弩配方的材料全部来自现有材料表_没有新造材料()
    {
        foreach (RecipeData recipe in RecipeBook.All)
        {
            foreach (string key in recipe.MaterialCosts.Keys)
            {
                Assert.True(Materials.Has(key), $"配方「{recipe.DisplayName}」引用了不存在的材料 {key}");
            }
        }
    }

    // ==================== 搜刮投放：不可制作的必须能搜到 ====================

    [Fact]
    public void 三把不可制作的弓弩_都能在探索点搜到_否则永远拿不到()
    {
        // 不给配方又不给投放 = 这三把武器在游戏里根本不存在。
        string[] looted = AllLootRefIds(LootKind.Weapon);

        foreach (string name in new[] { "竞技复合弓", "狩猎弓", "复合弩" })
        {
            Assert.Contains(name, looted);
        }
    }

    [Fact]
    public void 碳纤维箭_只能搜刮_但确实搜得到()
    {
        Assert.Contains(ArrowKeys.Carbon, AllLootRefIds(LootKind.Material));
    }

    [Fact]
    public void 可制作的箭也能搜到_搜刮永远是制作之外的另一条路()
    {
        string[] looted = AllLootRefIds(LootKind.Material);

        foreach (ArrowDef arrow in ArrowTable.Craftable())
        {
            Assert.Contains(arrow.Key, looted);
        }
    }


    // ==================== 《弓与箭之道》：书给被动加成（回收率 25% → 50%） ====================

    [Fact]
    public void 弓与箭之道_在书目里_且不可制作()
    {
        BookData book = BookLibrary.All().Single(b => b.Id == BookLibrary.WayOfBowAndArrowId);

        Assert.Equal("弓与箭之道", book.Title);
        Assert.False(string.IsNullOrWhiteSpace(book.Body));

        // 书就该是捡的，不是造的。
        Assert.DoesNotContain(RecipeBook.All, r => r.OutputKey == BookLibrary.WayOfBowAndArrowId);
    }

    [Fact]
    public void 弓与箭之道_不解锁任何配方_它给的是被动加成()
    {
        // 这本书是项目里书籍的**新用法**：既有的《木匠入门》《裁缝手记》都是"解锁配方"的门槛，
        // 而它给的是**被动效果**（箭矢回收率 25% → 50%）——沿用 MedicalBookPoints 已经确立的同一套模式
        // （引擎只吃一个值，调用方从读者的 ReadBookSet 里取），**不新造架构**。
        Assert.DoesNotContain(RecipeBook.All, r => r.RequiredBookIds.Contains(BookLibrary.WayOfBowAndArrowId));
    }

    [Fact]
    public void 弓与箭之道_能搜到_否则50pct回收率永远拿不到()
    {
        Assert.Contains(BookLibrary.WayOfBowAndArrowId, AllLootRefIds(LootKind.Book));
    }

    [Fact]
    public void 读者已读判定_经Pawn的已读书集_与配方门槛同一套判据()
    {
        // 消费层怎么把"读没读过"喂给引擎：读者的 ReadBookSet（配方书门槛用的是同一个对象）。
        var reader = new ReadBookSet();

        Assert.False(reader.HasRead(BookLibrary.WayOfBowAndArrowId));
        Assert.Equal(Archery.BaseArrowRecoveryRate,
            Archery.ArrowRecoveryRate(reader.HasRead(BookLibrary.WayOfBowAndArrowId)));

        reader.MarkRead(BookLibrary.WayOfBowAndArrowId);

        Assert.True(reader.HasRead(BookLibrary.WayOfBowAndArrowId));
        Assert.Equal(Archery.SkilledArrowRecoveryRate,
            Archery.ArrowRecoveryRate(reader.HasRead(BookLibrary.WayOfBowAndArrowId)));
    }

    [Fact]
    public void 箭的造价不低_否则25pct回收率就没意义了()
    {
        // 设计口径：基础只有 25% 回收 ⇒ 箭是**持续消耗品**。若造箭近乎白送，"跑回战场捡箭"就不值得做，
        // 回收率这条机制也就白设计了。故除了应急用的木箭，每支箭都要吃到金属（废金属/金属锭）。
        foreach (ArrowDef arrow in ArrowTable.Craftable().Where(a => a.Key != ArrowKeys.SharpenedStick))
        {
            RecipeData recipe = RecipeBook.All.Single(r => r.OutputKey == arrow.Key);

            Assert.True(
                recipe.MaterialCosts.ContainsKey("scrap_metal") || recipe.MaterialCosts.ContainsKey("metal_ingot"),
                $"「{arrow.Name}」得吃金属——不然箭太便宜，25% 回收率就没有意义");
        }
    }

    /// <summary>全部探索点目的地（<c>ExplorationCache</c> 未导出清单，故这里按其常量列齐）。</summary>
    private static readonly string[] Destinations =
    {
        ExplorationCache.RiversideCabinName,
        ExplorationCache.HarvesterWarehouseName,
        ExplorationCache.WatchersCabinName,
        ExplorationCache.CityRooftopLookoutName,
        ExplorationCache.BroadcastStationName,
        ExplorationCache.GoldfingerBaseName,
        ExplorationCache.EastNewVillageName,
        ExplorationCache.GasStationName,
        ExplorationCache.SupermarketName,
        ExplorationCache.HospitalName,
    };

    /// <summary>把所有探索点的所有搜刮点跑一遍，收集某类战利品的全部引用 id。</summary>
    private static string[] AllLootRefIds(LootKind kind)
    {
        var flags = new StoryFlags();

        return Destinations
            .SelectMany(ExplorationCache.CacheIdsFor)
            .Select(id => ExplorationCache.Resolve(id, flags))
            .Where(r => r.HasValue)
            .SelectMany(r => r!.Value.Loot)
            .Where(l => l.Kind == kind)
            .Select(l => l.RefId)
            .Distinct()
            .ToArray();
    }
}
