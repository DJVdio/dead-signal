using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 营地配方接入的纯逻辑单测：
//   产物分类工厂（CraftOutputFactory）——6 个内置配方产物各造对类别（武器/护甲/材料/杂项）；
//   工具键解析（ContainerLoot.ParseToolSlot）；材料/工具搜刮落地（LootApplication.Apply → 库存/工作台收集器）。

public class CraftOutputFactoryTests
{
    [Fact]
    public void WeaponOutputs_BecomeWeaponItems_WithChineseDisplayName()
    {
        Item knife = Assert.Single(CraftOutputFactory.Create("bone_knife", 1));
        Assert.Equal(ItemCategory.Weapon, knife.Category);
        Assert.Equal("骨刀", knife.DisplayName); // 配方 DisplayName，对齐武器表中文命名惯例

        // handmade_bow 是「短弓」的内部键（配方 Id/OutputKey 未改，只把 DisplayName 从「自制弓」改成了
        // 「短弓」——用户拍板的 5 把弓里没有「自制弓」这个名字，见 WeaponTable.ShortBow）。
        Item bow = Assert.Single(CraftOutputFactory.Create("handmade_bow", 1));
        Assert.Equal(ItemCategory.Weapon, bow.Category);
        Assert.Equal("短弓", bow.DisplayName);
    }

    [Fact]
    public void ArmorOutput_BecomesArmorItem()
    {
        Item vest = Assert.Single(CraftOutputFactory.Create("cloth_vest", 1));
        Assert.Equal(ItemCategory.Armor, vest.Category);
        Assert.Equal("粗布背心", vest.DisplayName);
    }

    [Fact]
    public void MaterialOutputs_BecomeSingleMaterialStack_OfGivenQuantity()
    {
        // 火药/鞣制药水：产物 key 同时是材料标识名 → 造一堆该材料（不是逐件）。
        Item powder = Assert.Single(CraftOutputFactory.Create("gunpowder", 2));
        Assert.Equal(ItemCategory.Material, powder.Category);
        Assert.Equal("gunpowder", powder.RefKey);
        Assert.Equal("火药", powder.DisplayName);
        Assert.Equal(2, powder.MaterialQuantity);

        Item tan = Assert.Single(CraftOutputFactory.Create("tanning_solution", 2));
        Assert.Equal(ItemCategory.Material, tan.Category);
        Assert.Equal(2, tan.MaterialQuantity);
    }

    [Fact]
    public void FurnitureOutput_FallsBackToMiscMaterialStack_KeepingKeyAndName()
    {
        // 木椅：无家具类别 → 作杂项材料堆（key 保留、显示名取配方名）。
        Item chair = Assert.Single(CraftOutputFactory.Create("chair", 1));
        Assert.Equal(ItemCategory.Material, chair.Category);
        Assert.Equal("chair", chair.RefKey);
        Assert.Equal("木椅", chair.DisplayName);
    }

    [Fact]
    public void WeaponOutput_MakesOneItemPerQuantity()
    {
        List<Item> two = CraftOutputFactory.Create("bone_knife", 2).ToList();
        Assert.Equal(2, two.Count);
        Assert.All(two, i => Assert.Equal(ItemCategory.Weapon, i.Category));
    }
}

public class ToolSlotParsingTests
{
    [Theory]
    [InlineData("calipers", ToolSlot.Calipers)]
    [InlineData("Calipers", ToolSlot.Calipers)]
    [InlineData("卡尺", ToolSlot.Calipers)]
    [InlineData("sawblade", ToolSlot.SawBlade)]
    [InlineData("saw_blade", ToolSlot.SawBlade)]
    [InlineData("锯片", ToolSlot.SawBlade)]
    [InlineData("beaker", ToolSlot.Beaker)]
    [InlineData("烧杯", ToolSlot.Beaker)]
    public void ParseToolSlot_MapsKnownKeys(string key, ToolSlot expected)
        => Assert.Equal(expected, ContainerLoot.ParseToolSlot(key));

    [Theory]
    [InlineData("hammer")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseToolSlot_UnknownReturnsNull(string? key)
        => Assert.Null(ContainerLoot.ParseToolSlot(key));
}

public class MaterialAndToolLootTests
{
    private static InventoryStore Store() => new();
    private static Dictionary<string, BookData> Registry() => new();
    private static BookData? NoBooks(string _) => null;

    [Fact]
    public void Apply_MaterialLoot_EntersInventoryAsStack_WithCatalogNameAndQuantity()
    {
        var inv = Store();
        int food = LootApplication.Apply(
            new[] { LootItem.Material("wood", 5) }, inv, Registry(), NoBooks);

        Assert.Equal(0, food);
        Item stack = Assert.Single(inv.ByCategory(ItemCategory.Material));
        Assert.Equal("wood", stack.RefKey);
        Assert.Equal("木料", stack.DisplayName); // 取自 Materials 目录
        Assert.Equal(5, stack.MaterialQuantity);
    }

    [Fact]
    public void Apply_ToolLoot_GoesToToolSink_NotInventory()
    {
        var inv = Store();
        var tools = new List<ToolSlot>();
        LootApplication.Apply(
            new[] { LootItem.Tool("beaker") }, inv, Registry(), NoBooks, tools);

        Assert.Equal(0, inv.Count);                       // 工具不入库存
        Assert.Equal(new[] { ToolSlot.Beaker }, tools);   // 进工作台收集器
    }

    [Fact]
    public void Apply_ToolLoot_WithoutSink_IsIgnoredSafely()
    {
        var inv = Store();
        // 不传收集器（无工作台语境）：工具掉落静默忽略，不抛、不入库存。
        int food = LootApplication.Apply(
            new[] { LootItem.Tool("calipers"), LootItem.Food(3) }, inv, Registry(), NoBooks);
        Assert.Equal(3, food);
        Assert.Equal(0, inv.Count);
    }

    [Fact]
    public void Apply_UnknownToolKey_NotAddedToSink()
    {
        var tools = new List<ToolSlot>();
        LootApplication.Apply(
            new[] { LootItem.Tool("wrench") }, Store(), Registry(), NoBooks, tools);
        Assert.Empty(tools);
    }
}
