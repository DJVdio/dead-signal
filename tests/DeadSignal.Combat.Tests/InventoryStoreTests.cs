using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 物品模型 + 营地共享库存（纯数据地基）的单测。
// 覆盖：Item 四类工厂/类别/引用键、库存增删查、按类别筛选、可装备筛选、食物份数合计、书已读标记。
public class InventoryStoreTests
{
    // ---- Item 构造 / 类别 / 引用键 ----

    [Fact]
    public void Weapon_item_carries_category_and_ref_key()
    {
        var item = Item.Weapon("匕首", "一把趁手的匕首");
        Assert.Equal(ItemCategory.Weapon, item.Category);
        Assert.Equal("匕首", item.DisplayName);
        Assert.Equal("匕首", item.RefKey); // 引用键=武器名
        Assert.Equal("一把趁手的匕首", item.Description);
        Assert.True(item.IsEquippable);
        Assert.Equal(0, item.FoodQuantity);
    }

    [Fact]
    public void Armor_item_carries_category_and_ref_key()
    {
        var item = Item.Armor("皮夹克");
        Assert.Equal(ItemCategory.Armor, item.Category);
        Assert.Equal("皮夹克", item.RefKey); // 引用键=护甲名
        Assert.True(item.IsEquippable);
    }

    [Fact]
    public void Book_item_refs_book_id_and_is_not_equippable()
    {
        var item = Item.Book("wilderness_survival_guide", "野外生存指南");
        Assert.Equal(ItemCategory.Book, item.Category);
        Assert.Equal("野外生存指南", item.DisplayName); // 显示名=标题
        Assert.Equal("wilderness_survival_guide", item.RefKey); // 引用键=书 id
        Assert.False(item.IsEquippable);
    }

    [Fact]
    public void Food_item_carries_quantity_and_no_ref_key()
    {
        var item = Item.Food(3);
        Assert.Equal(ItemCategory.Food, item.Category);
        Assert.Equal(3, item.FoodQuantity);
        Assert.Null(item.RefKey);
        Assert.False(item.IsEquippable);
    }

    [Fact]
    public void Food_quantity_clamps_to_non_negative()
    {
        Assert.Equal(0, Item.Food(-5).FoodQuantity);
    }

    [Fact]
    public void Items_with_same_content_are_value_equal()
    {
        Assert.Equal(Item.Weapon("匕首"), Item.Weapon("匕首"));
        Assert.NotEqual(Item.Weapon("匕首"), Item.Weapon("手枪"));
    }

    // ---- 库存增删查 ----

    [Fact]
    public void New_store_is_empty()
    {
        var store = new InventoryStore();
        Assert.Equal(0, store.Count);
        Assert.Empty(store.Items);
    }

    [Fact]
    public void Add_appends_item()
    {
        var store = new InventoryStore();
        store.Add(Item.Weapon("匕首"));
        Assert.Equal(1, store.Count);
        Assert.Equal("匕首", store.Items[0].DisplayName);
    }

    [Fact]
    public void AddRange_appends_all()
    {
        var store = new InventoryStore();
        store.AddRange(new[] { Item.Weapon("匕首"), Item.Armor("皮夹克") });
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void Remove_deletes_matching_item_and_reports_success()
    {
        var store = new InventoryStore();
        var dagger = Item.Weapon("匕首");
        store.Add(dagger);
        Assert.True(store.Remove(dagger));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Remove_absent_item_returns_false()
    {
        var store = new InventoryStore();
        store.Add(Item.Weapon("匕首"));
        Assert.False(store.Remove(Item.Weapon("手枪")));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Remove_by_value_equality_drops_one_instance()
    {
        var store = new InventoryStore();
        store.Add(Item.Weapon("匕首"));
        store.Add(Item.Weapon("匕首"));
        Assert.True(store.Remove(Item.Weapon("匕首"))); // 按值相等移除一件
        Assert.Equal(1, store.Count);
    }

    // ---- 按类别筛选 / 可装备 / 食物合计 ----

    [Fact]
    public void ByCategory_filters_correctly()
    {
        var store = SampleStore();
        Assert.Equal(2, store.ByCategory(ItemCategory.Weapon).Count());
        Assert.Single(store.ByCategory(ItemCategory.Armor));
        Assert.Single(store.ByCategory(ItemCategory.Book));
        Assert.Equal(2, store.ByCategory(ItemCategory.Food).Count());
    }

    [Fact]
    public void Category_shortcuts_match_ByCategory()
    {
        var store = SampleStore();
        Assert.Equal(store.ByCategory(ItemCategory.Weapon).Count(), store.Weapons.Count());
        Assert.Equal(store.ByCategory(ItemCategory.Armor).Count(), store.Armors.Count());
        Assert.Equal(store.ByCategory(ItemCategory.Book).Count(), store.Books.Count());
        Assert.Equal(store.ByCategory(ItemCategory.Food).Count(), store.Foods.Count());
    }

    [Fact]
    public void Equippable_returns_weapons_and_armors_only()
    {
        var store = SampleStore();
        var equippable = store.Equippable.ToList();
        Assert.Equal(3, equippable.Count); // 2 武器 + 1 护甲
        Assert.All(equippable, i => Assert.True(i.IsEquippable));
    }

    [Fact]
    public void TotalFood_sums_food_quantities_only()
    {
        var store = SampleStore(); // 食物 3 + 5
        Assert.Equal(8, store.TotalFood);
    }

    // ---- 书已读标记 ----

    [Fact]
    public void Book_starts_unread()
    {
        Assert.False(BookLibrary.WildernessSurvivalGuide().IsRead);
    }

    [Fact]
    public void MarkRead_sets_flag_and_is_idempotent()
    {
        var book = BookLibrary.WildernessSurvivalGuide();
        book.MarkRead();
        Assert.True(book.IsRead);
        book.MarkRead(); // 幂等
        Assert.True(book.IsRead);
    }

    [Fact]
    public void Recipe_books_carry_recipe_stub_lore_diaries_do_not()
    {
        // 配方书（野外生存指南 / 农场主问答）带配方桩；纯 lore 日记（金手指帮日记A/B）无配方产出。
        Assert.False(string.IsNullOrEmpty(BookLibrary.WildernessSurvivalGuide().GrantsRecipeStub));
        Assert.False(string.IsNullOrEmpty(BookLibrary.FarmerHundredQuestions().GrantsRecipeStub));
        Assert.Null(BookLibrary.GoldfingerDiaryA().GrantsRecipeStub);
        Assert.Null(BookLibrary.GoldfingerDiaryB().GrantsRecipeStub);
    }

    [Fact]
    public void Book_ToItem_refs_its_id()
    {
        var book = BookLibrary.FarmerHundredQuestions();
        var item = book.ToItem();
        Assert.Equal(ItemCategory.Book, item.Category);
        Assert.Equal(book.Id, item.RefKey);
        Assert.Equal(book.Title, item.DisplayName);
    }

    private static InventoryStore SampleStore()
    {
        var store = new InventoryStore();
        store.AddRange(new[]
        {
            Item.Weapon("匕首"),
            Item.Weapon("手枪"),
            Item.Armor("皮夹克"),
            BookLibrary.WildernessSurvivalGuide().ToItem(),
            Item.Food(3),
            Item.Food(5),
        });
        return store;
    }
}
