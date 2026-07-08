using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 制作执行服务（CraftingService）纯逻辑单测：
//   跨堆材料合计/够付/扣减（易错点）、Craft 实扣实产（成功/被门槛挡/批量材料不足）、
//   内置产物工厂（材料 vs 非材料）、ApplyWeaponMod 消耗基础武器入变体/冲突/缺武器（通用技能门槛已删）。

public class CrossStackMaterialTests
{
    private static Item Mat(string key, int qty) => Item.Material(key, key, qty);

    [Fact]
    public void MaterialTotal_SumsAcrossStacks_IgnoringOtherKeysAndCategories()
    {
        var items = new List<Item>
        {
            Mat("wood", 3), Mat("wood", 2), Mat("cloth", 5),
            Item.Food(10), Item.Weapon("匕首"),
        };
        Assert.Equal(5, CraftingService.MaterialTotal(items, "wood"));
        Assert.Equal(5, CraftingService.MaterialTotal(items, "cloth"));
        Assert.Equal(0, CraftingService.MaterialTotal(items, "stone"));
    }

    [Fact]
    public void HasEnough_TrueOnlyWhenEveryDemandMetAcrossStacks()
    {
        var items = new List<Item> { Mat("wood", 3), Mat("wood", 2), Mat("nails", 1) };
        Assert.True(CraftingService.HasEnough(items, new Dictionary<string, int> { ["wood"] = 5, ["nails"] = 1 }));
        Assert.False(CraftingService.HasEnough(items, new Dictionary<string, int> { ["wood"] = 6 }));
        Assert.False(CraftingService.HasEnough(items, new Dictionary<string, int> { ["nails"] = 2 }));
    }

    [Fact]
    public void Deduct_ConsumesAcrossStacks_DropsEmpty_ShrinksPartial_KeepsOthers()
    {
        var items = new List<Item>
        {
            Mat("wood", 3), Mat("wood", 2), Mat("cloth", 5), Item.Food(4),
        };
        // 需要 4 wood：耗尽首堆(3)、次堆扣 1 剩 1；cloth/食物不动。
        IReadOnlyList<Item> after = CraftingService.Deduct(items, new Dictionary<string, int> { ["wood"] = 4 });

        Assert.Equal(1, CraftingService.MaterialTotal(after, "wood"));
        Assert.Equal(5, CraftingService.MaterialTotal(after, "cloth"));
        Assert.Contains(after, i => i.Category == ItemCategory.Food); // 非材料保留
        // 只剩一个 wood 堆（耗尽的那堆被丢弃）。
        Assert.Single(after.Where(i => i.RefKey == "wood"));
    }

    [Fact]
    public void Deduct_ExactlyEmptiesStack_DropsIt()
    {
        var items = new List<Item> { Mat("wood", 2), Mat("wood", 2) };
        IReadOnlyList<Item> after = CraftingService.Deduct(items, new Dictionary<string, int> { ["wood"] = 4 });
        Assert.Empty(after.Where(i => i.RefKey == "wood"));
        Assert.Equal(0, CraftingService.MaterialTotal(after, "wood"));
    }
}

public class CraftExecutionTests
{
    // 一张自造配方：需 Beaker + 读 test_book，材料 wood×3/cloth×1，产出材料 gunpowder×2。
    private static RecipeData MakeRecipe(string output = "gunpowder", int outQty = 2) => new(
        Id: "svc_test",
        DisplayName: "测试物",
        Category: RecipeCategory.Chemistry,
        OutputKey: output,
        OutputQuantity: outQty,
        MaterialCosts: new Dictionary<string, int> { ["wood"] = 3, ["cloth"] = 1 },
        RequiredTools: new HashSet<ToolSlot> { ToolSlot.Beaker },
        RequiredBookIds: new List<string> { "test_book" });

    private static (WorkbenchState bench, InventoryStore inv) Ready()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        var inv = new InventoryStore();
        // 跨两堆凑 wood=4，cloth=2。
        inv.Add(Item.Material("wood", "木料", 3));
        inv.Add(Item.Material("wood", "木料", 1));
        inv.Add(Item.Material("cloth", "布料", 2));
        return (bench, inv);
    }

    [Fact]
    public void Craft_Success_DeductsAcrossStacks_ProducesMaterial()
    {
        var (bench, inv) = Ready();
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => true, bench, inv);

        Assert.True(r.Success);
        Assert.Empty(r.Blocks);
        // 扣了 wood×3（4→1）、cloth×1（2→1）。
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "wood"));
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "cloth"));
        // 产出 gunpowder×2（材料堆）。
        Assert.Equal(2, CraftingService.MaterialTotal(inv.Items, "gunpowder"));
        Assert.Contains(r.Produced, i => i.RefKey == "gunpowder" && i.MaterialQuantity == 2);
    }

    [Fact]
    public void Craft_Blocked_MissingTool_LeavesInventoryUntouched()
    {
        var (_, inv) = Ready();
        int before = inv.Count;
        var emptyBench = new WorkbenchState(); // 无 Beaker
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => true, emptyBench, inv);

        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
        Assert.Equal(before, inv.Count); // 未扣未产
        Assert.Equal(0, CraftingService.MaterialTotal(inv.Items, "gunpowder"));
    }

    [Fact]
    public void Craft_Blocked_UnreadBook()
    {
        var (bench, inv) = Ready();
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => false, bench, inv);
        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void Craft_Batch_InsufficientMaterial_Fails_NoMutation()
    {
        var (bench, inv) = Ready(); // wood=4, cloth=2；单份需 wood3/cloth1
        int before = inv.Count;
        // times=2 需 wood6 → 不够。
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => true, bench, inv, times: 2);
        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial && b.Key == "wood");
        Assert.Equal(before, inv.Count);
    }

    [Fact]
    public void Craft_DefaultOutput_NonMaterialKey_ProducesWeaponItems()
    {
        var (bench, inv) = Ready();
        // 产出 bone_knife（不在材料目录）×2 → 默认工厂造 2 件武器物品。
        CraftResult r = CraftingService.Craft(MakeRecipe(output: "bone_knife", outQty: 2), _ => true, bench, inv);
        Assert.True(r.Success);
        Assert.Equal(2, r.Produced.Count);
        Assert.All(r.Produced, i => Assert.Equal(ItemCategory.Weapon, i.Category));
        Assert.All(r.Produced, i => Assert.Equal("bone_knife", i.RefKey));
    }

    [Fact]
    public void Craft_CustomOutputFactory_IsUsed()
    {
        var (bench, inv) = Ready();
        CraftResult r = CraftingService.Craft(
            MakeRecipe(output: "cloth_vest", outQty: 1), _ => true, bench, inv,
            outputFactory: (key, qty) => Enumerable.Range(0, qty).Select(_ => Item.Armor(key)));
        Assert.True(r.Success);
        Assert.Single(r.Produced);
        Assert.Equal(ItemCategory.Armor, r.Produced[0].Category);
    }

    [Fact]
    public void Craft_RealRecipe_Gunpowder_EndToEnd()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        var inv = new InventoryStore();
        inv.Add(Item.Material("stone", "石料", 2));
        inv.Add(Item.Material("fuel", "燃料", 2));

        RecipeData gp = RecipeBook.Find("gunpowder")!;
        CraftResult r = CraftingService.Craft(gp, _ => true, bench, inv);
        Assert.True(r.Success);
        Assert.Equal(2, CraftingService.MaterialTotal(inv.Items, "gunpowder")); // 产出 2
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "stone"));     // 扣 1
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "fuel"));      // 扣 1
    }
}

public class WeaponModExecutionTests
{
    private static InventoryStore InvWith(string weaponName)
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon(weaponName));
        return inv;
    }

    [Fact]
    public void ApplyWeaponMod_Success_ConsumesBase_AddsVariant_WithModEffect()
    {
        var inv = InvWith("短剑");
        // 锋刃研磨（锐器/刃部位，穿透+0.05）。
        WeaponModResult r = CraftingService.ApplyWeaponMod("短剑", new[] { "锋刃研磨" }, inv);

        Assert.True(r.Success);
        Assert.NotNull(r.Variant);
        // 基础武器已消耗，库存里已无 RefKey=="短剑" 的原件。
        Assert.DoesNotContain(inv.Items, i => i.Category == ItemCategory.Weapon && i.RefKey == "短剑");
        // 变体入库（名字带改装后缀）。
        Assert.NotNull(r.Produced);
        Assert.Contains(inv.Items, i => i == r.Produced);
        Assert.Contains("锋刃研磨", r.Produced!.RefKey);
        // 数值确有改动（穿透↑）。
        Weapon baseW = WeaponTable.Arsenal().First(w => w.Name == "短剑");
        Assert.True(r.Variant!.Weapon.Penetration > baseW.Penetration);
    }

    [Fact]
    public void ApplyWeaponMod_SamePartConflict_Fails_NoMutation()
    {
        var inv = InvWith("短剑");
        int before = inv.Count;
        // 锯齿剑刃 与 锋刃研磨 都占"刃"部位 → 冲突。
        WeaponModResult r = CraftingService.ApplyWeaponMod("短剑", new[] { "锯齿剑刃", "锋刃研磨" }, inv);
        Assert.False(r.Success);
        Assert.NotNull(r.FailureReason);
        Assert.Equal(before, inv.Count);
        Assert.Contains(inv.Items, i => i.RefKey == "短剑"); // 基础武器仍在
    }

    [Fact]
    public void ApplyWeaponMod_MissingBaseWeapon_Fails()
    {
        var inv = new InventoryStore(); // 空
        WeaponModResult r = CraftingService.ApplyWeaponMod("短剑", new[] { "锋刃研磨" }, inv);
        Assert.False(r.Success);
        Assert.Contains("短剑", r.FailureReason);
    }

    [Fact]
    public void ApplyWeaponMod_UnknownModForClass_Fails()
    {
        var inv = InvWith("短剑");
        // 铁丝强化 是钝器改装，不属于锐器"短剑"。
        WeaponModResult r = CraftingService.ApplyWeaponMod("短剑", new[] { "铁丝强化" }, inv);
        Assert.False(r.Success);
        Assert.Contains(inv.Items, i => i.RefKey == "短剑"); // 未消耗
    }
}
