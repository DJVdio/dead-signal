using System.Linq;
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
            "wood", "scrap_cloth", "scrap_metal", "leather", "rawhide",
            "bone", "nails", "wire", "gunpowder", "tanning_solution",
        };
        foreach (string key in required)
        {
            Assert.True(Materials.Has(key), $"目录缺少基础材料：{key}");
        }
    }

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
        store.Add(Materials.Find("scrap_metal")!.Value.ToItem(4));

        Assert.Equal(2, store.ByCategory(ItemCategory.Material).Count()); // 材料按类别筛出
        Assert.Equal(3, store.TotalFood); // 材料不计入食物合计
        Assert.Single(store.Equippable);  // 材料不可装备
    }
}
