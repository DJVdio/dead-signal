using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 营地容器搜刮纯逻辑单测。
// 覆盖：LootItem 四类工厂、ContainerLoot 登记/一次性搜刮/已搜标记、LootApplication 落地到库存/食物/书登记、
//       CampResources.AddFood 累加不越界。
public class ContainerLootTests
{
    // ---- LootItem 工厂 ----

    [Fact]
    public void Food_loot_carries_clamped_quantity()
    {
        Assert.Equal(4, LootItem.Food(4).Quantity);
        Assert.Equal(LootKind.Food, LootItem.Food(4).Kind);
        Assert.Equal(0, LootItem.Food(-3).Quantity); // clamp 到 ≥0
    }

    [Fact]
    public void Book_weapon_armor_loot_carry_ref_id()
    {
        Assert.Equal("wilderness_survival_guide", LootItem.Book("wilderness_survival_guide").RefId);
        Assert.Equal("刺剑", LootItem.Weapon("刺剑").RefId);
        Assert.Equal("粗布外套", LootItem.Armor("粗布外套").RefId);
    }

    // ---- ContainerLoot 登记 / 一次性搜刮 ----

    [Fact]
    public void Search_returns_loot_once_then_marks_searched()
    {
        var loot = new ContainerLoot();
        loot.Register("衣柜", new[] { LootItem.Armor("粗布外套"), LootItem.Armor("劳保手套") });

        Assert.True(loot.Has("衣柜"));
        Assert.False(loot.IsSearched("衣柜"));

        IReadOnlyList<LootItem> first = loot.Search("衣柜");
        Assert.Equal(2, first.Count);
        Assert.True(loot.IsSearched("衣柜"));

        // 再搜同一容器：空（不重复产出）。
        Assert.Empty(loot.Search("衣柜"));
    }

    [Fact]
    public void Search_unknown_container_returns_empty()
    {
        var loot = new ContainerLoot();
        Assert.Empty(loot.Search("不存在的容器"));
        Assert.False(loot.Has("不存在的容器"));
        Assert.False(loot.IsSearched("不存在的容器"));
    }

    [Fact]
    public void Register_empty_name_is_ignored()
    {
        var loot = new ContainerLoot();
        loot.Register("", new[] { LootItem.Food(1) });
        Assert.False(loot.Has(""));
    }

    // ---- LootApplication 落地 ----

    private static Func<string, BookData?> ResolverFrom(params BookData[] books)
    {
        var map = books.ToDictionary(b => b.Id);
        return id => map.TryGetValue(id, out BookData? b) ? b : null;
    }

    [Fact]
    public void Apply_routes_food_to_return_and_rest_to_inventory()
    {
        var inv = new InventoryStore();
        var registry = new Dictionary<string, BookData>();
        BookData wilderness = BookLibrary.WildernessSurvivalGuide();

        var loot = new List<LootItem>
        {
            LootItem.Food(4),
            LootItem.Weapon("刺剑"),
            LootItem.Armor("粗布外套"),
            LootItem.Book(wilderness.Id),
        };

        int food = LootApplication.Apply(loot, inv, registry, ResolverFrom(wilderness));

        Assert.Equal(4, food);                       // 食物走返回值（不入库存）
        Assert.Empty(inv.Foods);                      // 食物不长留库存
        Assert.Single(inv.Weapons);
        Assert.Single(inv.Armors);
        Assert.Single(inv.Books);
        Assert.Equal("刺剑", inv.Weapons.First().RefKey);
        Assert.Equal("粗布外套", inv.Armors.First().RefKey);
        Assert.Equal(wilderness.Id, inv.Books.First().RefKey);
    }

    [Fact]
    public void Apply_registers_same_book_instance_for_shared_read_state()
    {
        var inv = new InventoryStore();
        var registry = new Dictionary<string, BookData>();
        BookData book = BookLibrary.FarmerHundredQuestions();

        LootApplication.Apply(new[] { LootItem.Book(book.Id) }, inv, registry, ResolverFrom(book));

        Assert.True(registry.ContainsKey(book.Id));
        Assert.Same(book, registry[book.Id]); // 登记同一实例，阅读面板 MarkRead 后已读态共享
        Assert.False(registry[book.Id].IsRead);
        registry[book.Id].MarkRead();
        Assert.True(book.IsRead);
    }

    [Fact]
    public void Apply_skips_unresolvable_book_id()
    {
        var inv = new InventoryStore();
        var registry = new Dictionary<string, BookData>();

        int food = LootApplication.Apply(
            new[] { LootItem.Book("no_such_book") }, inv, registry, ResolverFrom());

        Assert.Equal(0, food);
        Assert.Empty(inv.Books);
        Assert.Empty(registry);
    }

    // ---- CampResources.AddFood ----

    [Fact]
    public void AddFood_accumulates_and_clamps_negative_to_zero()
    {
        var res = new CampResources(food: 5);
        res.AddFood(4);
        Assert.Equal(9, res.Food);
        res.AddFood(-10); // 负数当 0，不减食物
        Assert.Equal(9, res.Food);
    }
}
