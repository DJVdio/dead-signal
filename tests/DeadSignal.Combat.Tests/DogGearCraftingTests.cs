using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 狗装备制作单测（批次5，道格 2 级解锁）：五件配方存在性 + 制作者门槛（CanCraft 的 crafterGate/CrafterLocked）
/// + CraftingService 门槛穿透 + CraftOutputFactory 产出为可穿戴狗装备护甲。材料/工时数值皆"拟定待调"。
/// </summary>
public class DogGearCraftingTests
{
    private static readonly string[] DogRecipeIds =
    {
        "dog_cloth_vest", "dog_leather_vest", "dog_pocket_vest", "dog_iron_helmet", "dog_wire_helmet",
    };

    // ---- 配方存在性 + 门槛标注 ----

    [Fact]
    public void RecipeBook_HasFiveDogGearRecipes_AllGatedByDougBond_NoBookNoTool()
    {
        foreach (string id in DogRecipeIds)
        {
            RecipeData? r = RecipeBook.Find(id);
            Assert.NotNull(r);
            Assert.Contains(RecipeBook.DogGearCrafterGate, r!.RequiredCrafterGates ?? new List<string>());
            Assert.Empty(r.RequiredBookIds);   // 无书门槛
            Assert.Empty(r.RequiredTools);     // 无工具槽
            Assert.True(r.WorkMinutes > 0);
        }
    }

    [Fact]
    public void DogRecipe_OutputKeys_AreRegisteredDogGear()
    {
        foreach (string id in DogRecipeIds)
        {
            RecipeData r = RecipeBook.Find(id)!;
            Assert.True(DogGearCatalog.IsDogGear(r.OutputKey), $"{id} 产物 {r.OutputKey} 应是登记的狗装备");
        }
    }

    // ---- CanCraft 门槛 ----

    private static Func<string, int> Have(params (string k, int n)[] mats)
    {
        var d = mats.ToDictionary(x => x.k, x => x.n);
        return k => d.TryGetValue(k, out int n) ? n : 0;
    }

    private static readonly HashSet<ToolSlot> NoTools = new();

    [Fact]
    public void CanCraft_DogGear_BlockedWhenGateFails()
    {
        RecipeData r = RecipeBook.Find("dog_cloth_vest")!;
        // 材料充足，但门槛判据返回阻塞说明（如非道格/羁绊不足）。
        CraftAvailability a = CraftingLogic.CanCraft(
            r, Have(("cloth", 9), ("scrap_cloth", 9)), _ => true, NoTools,
            crafterGate: _ => "需道格制作、且与布鲁斯羁绊达 2 级");
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.CrafterLocked);
    }

    [Fact]
    public void CanCraft_DogGear_FailClosed_WhenNoGateEvaluatorSupplied()
    {
        RecipeData r = RecipeBook.Find("dog_cloth_vest")!;
        // 材料充足但没传 crafterGate → fail-closed 拦下（不误放）。
        CraftAvailability a = CraftingLogic.CanCraft(
            r, Have(("cloth", 9), ("scrap_cloth", 9)), _ => true, NoTools);
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.CrafterLocked);
    }

    [Fact]
    public void CanCraft_DogGear_AllowedWhenGateSatisfiedAndMaterialsPresent()
    {
        RecipeData r = RecipeBook.Find("dog_cloth_vest")!;
        CraftAvailability a = CraftingLogic.CanCraft(
            r, Have(("cloth", 9), ("scrap_cloth", 9)), _ => true, NoTools,
            crafterGate: _ => null); // null=满足
        Assert.True(a.CanCraft);
        Assert.Empty(a.Blocks);
    }

    [Fact]
    public void CanCraft_DogGear_GateSatisfiedButMaterialShort_StillBlockedOnMaterial()
    {
        RecipeData r = RecipeBook.Find("dog_leather_vest")!;
        CraftAvailability a = CraftingLogic.CanCraft(
            r, Have(("leather", 0)), _ => true, NoTools, crafterGate: _ => null);
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial);
    }

    [Fact]
    public void NonDogRecipe_UnaffectedByMissingGate()
    {
        // 火把无制作者门槛：不传 crafterGate 也应可做（回归保护）。
        RecipeData r = RecipeBook.Find("torch")!;
        CraftAvailability a = CraftingLogic.CanCraft(
            r, Have(("wood", 9), ("scrap_cloth", 9), ("fuel", 9)), _ => true, NoTools);
        Assert.True(a.CanCraft);
    }

    // ---- 门槛判据接 DougBruceBond ----

    [Fact]
    public void GateVia_DougBruceBond_L2AndBothAlive_Unlocks()
    {
        // 模拟营地层门槛判据：道格且羁绊≥2级且两者皆存活。
        Func<int, bool, Func<string, string?>> gateFor = (daysBothAlive, bothAlive) => _ =>
            DougBruceBond.CanCraftDogGear(DougBruceBond.EvaluateLevel(daysBothAlive), bothAlive)
                ? null : "需道格制作、且与布鲁斯羁绊达 2 级";

        RecipeData r = RecipeBook.Find("dog_wire_helmet")!;
        var mats = Have(("wire", 9), ("scrap_cloth", 9));

        // 1 级（共存 0 天）→ 挡下。
        Assert.False(CraftingLogic.CanCraft(r, mats, _ => true, NoTools, gateFor(0, true)).CanCraft);
        // 2 级（共存达阈）但布鲁斯死 → 挡下。
        Assert.False(CraftingLogic.CanCraft(r, mats, _ => true, NoTools, gateFor(DougBruceBond.Level2Days, false)).CanCraft);
        // 2 级 + 两者皆活 → 放行。
        Assert.True(CraftingLogic.CanCraft(r, mats, _ => true, NoTools, gateFor(DougBruceBond.Level2Days, true)).CanCraft);
    }

    // ---- CraftingService 门槛穿透 + 产出可穿戴狗装备 ----

    private static InventoryStore InvWith(params (string k, int n)[] mats)
    {
        var inv = new InventoryStore();
        foreach (var (k, n) in mats) inv.Add(Item.Material(k, k, n));
        return inv;
    }

    [Fact]
    public void CraftingService_Craft_BlockedByGate_NoMutation()
    {
        RecipeData r = RecipeBook.Find("dog_cloth_vest")!;
        var inv = InvWith(("cloth", 4), ("scrap_cloth", 4));
        int before = inv.Count;
        CraftResult res = CraftingService.Craft(
            r, _ => true, new WorkbenchState(), inv, 1, CraftOutputFactory.Create,
            crafterGate: _ => "羁绊不足");
        Assert.False(res.Success);
        Assert.Contains(res.Blocks, b => b.Reason == CraftBlockReason.CrafterLocked);
        Assert.Equal(before, inv.Count); // 未扣未产
    }

    [Fact]
    public void CraftingService_Craft_GateSatisfied_ProducesWearableDogArmor()
    {
        RecipeData r = RecipeBook.Find("dog_iron_helmet")!;
        var inv = InvWith(("scrap_metal", 4), ("leather", 4));
        CraftResult res = CraftingService.Craft(
            r, _ => true, new WorkbenchState(), inv, 1, CraftOutputFactory.Create,
            crafterGate: _ => null);

        Assert.True(res.Success);
        Item produced = Assert.Single(res.Produced);
        Assert.Equal(ItemCategory.Armor, produced.Category);
        // 产物 RefKey 必须是可被 DogApparelSlots 穿戴的登记键。
        Assert.True(DogGearCatalog.IsDogGear(produced.RefKey));

        var dogApparel = new DogApparelSlots();
        Assert.Equal(DogEquipOutcome.Equipped, dogApparel.TryEquip(produced.RefKey!, out _));
        Assert.Single(dogApparel.ArmorLayers()); // 铁皮头甲进护甲聚合
    }

    // ---- CraftOutputFactory 直接产出 ----

    [Fact]
    public void CraftOutputFactory_ProducesDogGearAsArmor_ForEachPiece()
    {
        foreach (string key in DogGearCatalog.AllKeys)
        {
            Item item = Assert.Single(CraftOutputFactory.Create(key, 1));
            Assert.Equal(ItemCategory.Armor, item.Category);
            Assert.Equal(key, item.RefKey);
        }
    }
}
