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

    /// <summary>
    /// 削尖的木箭：<b>无工具槽</b>、只吃木料，但<b>要读《野外生存指南》</b>（[SPEC-B21·T26] 用户拍板）。
    /// <para>
    /// ⚠️ <b>本条推翻了旧断言</b>「无工具槽<b>无书门槛</b>」——用户在 wiki 书籍表里把「削减木箭」写进了
    /// 《野外生存指南》的效果列（表赢代码）。
    /// </para>
    /// <para>
    /// <b>这不会卡死弓弩线的开局</b>（核实过，别再当成收紧）：① 这本书<b>开局就在共享库存里</b>
    /// （camp.json 住宅-柜子 role=storage），不用搜刮，只需读完；② <b>短弓本身已经要这同一本书</b>
    /// ⇒ 没读书的人根本没弓可射，给箭加同一道门槛不多锁任何东西；③ 就算搜到成品弓却没读书，
    /// <b>重头箭仍是零书门槛</b>（只要卡尺）。
    /// </para>
    /// </summary>
    [Fact]
    public void 削尖的木箭_无工具槽_但要读野外生存指南()
    {
        RecipeData stick = RecipeBook.All.Single(r => r.OutputKey == ArrowKeys.SharpenedStick);

        Assert.Empty(stick.RequiredTools);                                                  // 工具门槛照旧：一把刀削根棍
        Assert.Equal(new[] { RecipeBook.WildernessSurvivalGuideBookId }, stick.RequiredBookIds.ToArray());
        Assert.Equal(new[] { "wood" }, stick.MaterialCosts.Keys.ToArray());
    }

    /// <summary>
    /// <b>没读书之前也不会彻底没箭可用</b> —— 这条是上面那道新门槛的<b>安全网</b>：
    /// <b>重头箭</b>（用户没提 ⇒ 一个字没动）仍是<b>零书门槛</b>，只要卡尺（营地展示柜里就有）。
    /// <para>谁哪天顺手把重头箭也"统一"到某本书名下，这条会红一次 —— 那一刻弓弩线才真的可能被书卡死。</para>
    /// </summary>
    [Fact]
    public void 重头箭仍是零书门槛_这是没读书时的安全网()
    {
        RecipeData heavy = RecipeBook.All.Single(r => r.OutputKey == ArrowKeys.Heavy);

        Assert.Empty(heavy.RequiredBookIds);
        Assert.Contains(ToolSlot.Calipers, heavy.RequiredTools);
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

    /// <summary>
    /// 《弓与箭之道》<b>解锁「自制箭」，且只解锁这一条</b>（[SPEC-B21·T26] 用户拍板）。
    /// <para>
    /// ⚠️ <b>本条推翻了旧断言</b>「本书不解锁任何配方，它给的是被动加成」——那曾是这本书的"新用法"，
    /// 现在它<b>两样都给</b>：解锁自制箭 <b>＋</b> 四项被动（回收率 25%→50%、射程 +10%、锥形角 −10%、攻速 +2%）。
    /// </para>
    /// <para>
    /// <b>只解锁这一条</b>是关键：<b>重头箭用户没提 ⇒ 没动</b>（仍零书门槛）。这条断言把"别顺手统一成
    /// 『好箭都归这本书』"钉死 —— 那是引申，不是用户说的。
    /// </para>
    /// </summary>
    [Fact]
    public void 弓与箭之道_只解锁自制箭这一条()
    {
        var unlocked = RecipeBook.All
            .Where(r => r.RequiredBookIds.Contains(BookLibrary.WayOfBowAndArrowId))
            .Select(r => r.OutputKey)
            .ToArray();

        Assert.Equal(new[] { ArrowKeys.Handmade }, unlocked);
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

    /// <summary>
    /// 《机械之美》<b>能搜到</b> —— 否则两把可制作的弩永远拿不到（它们搜刮不到，只能造）。
    /// <para>照抄 <see cref="弓与箭之道_能搜到_否则50pct回收率永远拿不到"/> 那条的用意：
    /// <b>一本拿不到的书 = 一整条武器线不存在</b>，而这种事编译器和别的测试都抓不到。</para>
    /// </summary>
    [Fact]
    public void 机械之美能搜到_否则两把可制作的弩永远拿不到()
    {
        Assert.Contains(BookLibrary.MechanicalBeautyId, AllLootRefIds(LootKind.Book));
    }

    /// <summary>
    /// <b>武器零件的全图总量，够造 2~3 把弩</b>（[SPEC-B21·T26] 稀缺度靶心；数值拟定待调）。
    /// <para>
    /// 弩＝中后期武器 ⇒ 零件该<b>稀缺但不至于拿不到</b>。这条断言是那条线的两侧护栏：
    /// 低于 5（连一套轻弩+重弩都凑不齐）⇒ 太紧，弩形同虚设；高于 12 ⇒ 太松，弩变成量产品。
    /// </para>
    /// <para>投放点按<b>语义</b>挑：金手指帮军械柜(3，打过才拿) / 加油站修车棚零件货架(2) / 工位(1) / 联合收割机仓库工具柜(2)。</para>
    /// </summary>
    [Fact]
    public void 武器零件全图总量_够造两三把弩()
    {
        var flags = new StoryFlags();
        int total = Destinations
            .SelectMany(ExplorationCache.CacheIdsFor)
            .Select(id => ExplorationCache.Resolve(id, flags))
            .Where(r => r.HasValue)
            .SelectMany(r => r!.Value.Loot)
            .Where(l => l.Kind == LootKind.Material && l.RefId == Materials.WeaponPartsKey)
            .Sum(l => l.Quantity);

        int light = RecipeBook.Find("light_crossbow")!.MaterialCosts[Materials.WeaponPartsKey];   // 2
        int heavy = RecipeBook.Find("heavy_crossbow")!.MaterialCosts[Materials.WeaponPartsKey];   // 3

        Assert.InRange(total, light + heavy, 12);     // 至少凑得齐一套轻+重；上限防"弩变量产品"
        Assert.InRange(total / heavy, 2, 4);          // 全押重弩也能出 2~4 把
    }

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
