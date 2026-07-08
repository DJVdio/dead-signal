using System.Collections.Generic;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// §6 穿戴槽纯逻辑单测：11 槽占用、多槽占多槽、成对手套各占一手、断肢禁装、
/// 替换/卸装、护甲覆盖聚合，以及装备目录映射表（粗布外套/左右手套/防毒面具/一体板甲、刺剑草叉非穿戴）。
/// 数值/覆盖集合皆"拟定待调"，测试锁的是规则形态。
/// </summary>
public class ApparelSlotsTests
{
    private static IReadOnlySet<EquipSlot> Slots(params EquipSlot[] s) => new HashSet<EquipSlot>(s);
    private static IReadOnlySet<string> Parts(params string[] p) => new HashSet<string>(p);

    // ---- 单槽 ----

    [Fact]
    public void Equip_SingleSlot_OccupiesThatSlot()
    {
        var a = new ApparelSlots();
        var r = a.TryEquip("粗布外套", Slots(EquipSlot.OuterLayer), displaced: out _);
        Assert.Equal(EquipOutcome.Equipped, r);
        Assert.Equal("粗布外套", a.ItemAt(EquipSlot.OuterLayer));
        Assert.True(a.IsEquipped("粗布外套"));
        Assert.False(a.IsOccupied(EquipSlot.SkinLayer));
    }

    [Fact]
    public void Equip_NoSlots_Blocked()
    {
        var a = new ApparelSlots();
        Assert.Equal(EquipOutcome.BlockedNoSlots, a.TryEquip("空", Slots(), displaced: out _));
    }

    // ---- 多槽占多槽 ----

    [Fact]
    public void Equip_MultiSlot_OccupiesAllDeclaredSlots()
    {
        var a = new ApparelSlots();
        var r = a.TryEquip("一体板甲", Slots(EquipSlot.PlateLayer, EquipSlot.Pants), displaced: out _);
        Assert.Equal(EquipOutcome.Equipped, r);
        Assert.Equal("一体板甲", a.ItemAt(EquipSlot.PlateLayer));
        Assert.Equal("一体板甲", a.ItemAt(EquipSlot.Pants));
        Assert.Equal(Slots(EquipSlot.PlateLayer, EquipSlot.Pants), a.SlotsOf("一体板甲"));
    }

    [Fact]
    public void Equip_MultiSlot_PartialConflict_Blocks()
    {
        var a = new ApparelSlots();
        a.TryEquip("裤子甲", Slots(EquipSlot.Pants), displaced: out _);
        // 一体板甲要占 装甲层+裤子，裤子已被占 → 冲突不穿。
        var r = a.TryEquip("一体板甲", Slots(EquipSlot.PlateLayer, EquipSlot.Pants), displaced: out _);
        Assert.Equal(EquipOutcome.BlockedSlotOccupied, r);
        Assert.False(a.IsEquipped("一体板甲"));
        Assert.False(a.IsOccupied(EquipSlot.PlateLayer)); // 冲突时不得残留占用装甲层
    }

    // ---- 成对手套各占一手 ----

    [Fact]
    public void Equip_PairGloves_EachOccupiesOneHand()
    {
        var a = new ApparelSlots();
        a.TryEquip("左手套", Slots(EquipSlot.LeftHand), displaced: out _);
        a.TryEquip("右手套", Slots(EquipSlot.RightHand), displaced: out _);
        Assert.Equal("左手套", a.ItemAt(EquipSlot.LeftHand));
        Assert.Equal("右手套", a.ItemAt(EquipSlot.RightHand));
        Assert.Equal(2, a.EquippedItems.Count); // 两件独立
    }

    // ---- 断肢禁装 ----

    [Fact]
    public void Equip_SeveredHand_DisablesThatHandSlot()
    {
        var a = new ApparelSlots();
        var severed = Parts(HumanBody.LeftHand);
        var r = a.TryEquip("左手套", Slots(EquipSlot.LeftHand), severedParts: severed, displaced: out _);
        Assert.Equal(EquipOutcome.BlockedSeveredLimb, r);
        Assert.False(a.IsEquipped("左手套"));
    }

    [Fact]
    public void Equip_SeveredLeft_RightHandStillWearable()
    {
        var a = new ApparelSlots();
        var severed = Parts(HumanBody.LeftHand);
        var r = a.TryEquip("右手套", Slots(EquipSlot.RightHand), severedParts: severed, displaced: out _);
        Assert.Equal(EquipOutcome.Equipped, r);
    }

    [Fact]
    public void Equip_MultiSlot_AnySeveredSlotBlocksWhole()
    {
        var a = new ApparelSlots();
        // 假设占用左脚+右脚的连体靴，右脚已断 → 整件穿不上，左脚也不残留。
        var r = a.TryEquip("连体靴", Slots(EquipSlot.LeftFoot, EquipSlot.RightFoot),
            severedParts: Parts(HumanBody.RightFoot), displaced: out _);
        Assert.Equal(EquipOutcome.BlockedSeveredLimb, r);
        Assert.False(a.IsOccupied(EquipSlot.LeftFoot));
    }

    [Fact]
    public void DisabledSlots_ReflectsSeveredLimbs()
    {
        var disabled = ApparelSlots.DisabledSlots(Parts(HumanBody.LeftHand, HumanBody.RightFoot));
        Assert.Contains(EquipSlot.LeftHand, disabled);
        Assert.Contains(EquipSlot.RightFoot, disabled);
        Assert.DoesNotContain(EquipSlot.RightHand, disabled);
        Assert.DoesNotContain(EquipSlot.OuterLayer, disabled); // 躯干层无肢体依附
    }

    // ---- 替换 / 卸装 ----

    [Fact]
    public void Equip_Replace_DisplacesOldItem()
    {
        var a = new ApparelSlots();
        a.TryEquip("粗布外套", Slots(EquipSlot.OuterLayer), displaced: out _);
        var r = a.TryEquip("皮夹克", Slots(EquipSlot.OuterLayer), replace: true, displaced: out var displaced);
        Assert.Equal(EquipOutcome.Equipped, r);
        Assert.Equal("皮夹克", a.ItemAt(EquipSlot.OuterLayer));
        Assert.False(a.IsEquipped("粗布外套"));
        Assert.Contains("粗布外套", displaced);
    }

    [Fact]
    public void Equip_Replace_MultiSlot_FullyRemovesConflictingItems()
    {
        var a = new ApparelSlots();
        a.TryEquip("单板甲", Slots(EquipSlot.PlateLayer), displaced: out _);
        a.TryEquip("普通裤", Slots(EquipSlot.Pants), displaced: out _);
        // 一体板甲替换占 装甲层+裤子 → 顶掉两件旧装备。
        var r = a.TryEquip("一体板甲", Slots(EquipSlot.PlateLayer, EquipSlot.Pants), replace: true, displaced: out var displaced);
        Assert.Equal(EquipOutcome.Equipped, r);
        Assert.False(a.IsEquipped("单板甲"));
        Assert.False(a.IsEquipped("普通裤"));
        Assert.Equal(2, displaced.Count);
    }

    [Fact]
    public void Unequip_ClearsAllOccupiedSlots()
    {
        var a = new ApparelSlots();
        a.TryEquip("一体板甲", Slots(EquipSlot.PlateLayer, EquipSlot.Pants), displaced: out _);
        Assert.True(a.Unequip("一体板甲"));
        Assert.False(a.IsOccupied(EquipSlot.PlateLayer));
        Assert.False(a.IsOccupied(EquipSlot.Pants));
        Assert.False(a.Unequip("一体板甲")); // 再卸返回 false
    }

    [Fact]
    public void UnequipSlot_RemovesWholeMultiSlotItem()
    {
        var a = new ApparelSlots();
        a.TryEquip("一体板甲", Slots(EquipSlot.PlateLayer, EquipSlot.Pants), displaced: out _);
        var removed = a.UnequipSlot(EquipSlot.Pants); // 从裤子槽拔，连带装甲层
        Assert.Equal("一体板甲", removed);
        Assert.False(a.IsOccupied(EquipSlot.PlateLayer));
        Assert.Null(a.UnequipSlot(EquipSlot.Head)); // 空槽返回 null
    }

    // ---- 护甲覆盖聚合 ----

    [Fact]
    public void CoveredParts_UnionsAllEquippedCoverage()
    {
        var a = new ApparelSlots();
        a.TryEquip("左手套", Slots(EquipSlot.LeftHand), coversParts: Parts(HumanBody.LeftHand, HumanBody.LeftThumb), displaced: out _);
        a.TryEquip("右手套", Slots(EquipSlot.RightHand), coversParts: Parts(HumanBody.RightHand), displaced: out _);
        var covered = a.CoveredParts();
        Assert.Contains(HumanBody.LeftHand, covered);
        Assert.Contains(HumanBody.LeftThumb, covered);
        Assert.Contains(HumanBody.RightHand, covered);
        Assert.DoesNotContain(HumanBody.Torso, covered); // 未穿躯干甲则躯干不在覆盖并集
    }

    [Fact]
    public void ActiveCoverage_ListsPerItemCoverage()
    {
        var a = new ApparelSlots();
        a.TryEquip("左手套", Slots(EquipSlot.LeftHand), coversParts: Parts(HumanBody.LeftHand), displaced: out _);
        var list = a.ActiveCoverage();
        Assert.Single(list);
        Assert.Equal("左手套", list[0].Item);
        Assert.Contains(HumanBody.LeftHand, list[0].CoversParts);
    }

    [Fact]
    public void CoveredParts_DropsCoverageWhenUnequipped()
    {
        var a = new ApparelSlots();
        a.TryEquip("左手套", Slots(EquipSlot.LeftHand), coversParts: Parts(HumanBody.LeftHand), displaced: out _);
        a.Unequip("左手套");
        Assert.Empty(a.CoveredParts());
    }

    // ---- 装备目录映射表（拟定/待扩） ----

    [Fact]
    public void Catalog_CoarseCoat_IsSingleOuterSlot()
    {
        var def = ApparelCatalog.Get("粗布外套");
        Assert.NotNull(def);
        Assert.Equal(Slots(EquipSlot.OuterLayer), def!.Slots);
        Assert.Equal(ArmorSlot.Outer, def.Layer);
    }

    [Fact]
    public void Catalog_Gloves_ArePairedPerHandWithSubtreeCoverage()
    {
        var left = ApparelCatalog.Get("左手套")!;
        var right = ApparelCatalog.Get("右手套")!;
        Assert.Equal(Slots(EquipSlot.LeftHand), left.Slots);
        Assert.Equal(Slots(EquipSlot.RightHand), right.Slots);
        // 覆盖连带该手手指子树，且不越到另一只手。
        Assert.Contains(HumanBody.LeftHand, left.CoversParts!);
        Assert.Contains(HumanBody.LeftThumb, left.CoversParts!);
        Assert.DoesNotContain(HumanBody.RightHand, left.CoversParts!);
    }

    [Fact]
    public void Catalog_GasMask_IsMultiSlot_EyesAndFace()
    {
        var def = ApparelCatalog.Get("防毒面具")!;
        Assert.Equal(Slots(EquipSlot.Eyes, EquipSlot.Face), def.Slots);
    }

    [Fact]
    public void Catalog_WeaponsAreNotApparel()
    {
        Assert.False(ApparelCatalog.IsApparel("刺剑"));
        Assert.False(ApparelCatalog.IsApparel("草叉"));
        Assert.True(ApparelCatalog.IsApparel("粗布外套"));
    }

    [Fact]
    public void Catalog_Equip_WiresSlotsAndCoverage()
    {
        var a = new ApparelSlots();
        var r = ApparelCatalog.Equip(a, "左手套");
        Assert.Equal(EquipOutcome.Equipped, r);
        Assert.Equal("左手套", a.ItemAt(EquipSlot.LeftHand));
        Assert.Contains(HumanBody.LeftHand, a.CoveredParts());
    }

    [Fact]
    public void Catalog_Equip_RespectsSeveredLimb()
    {
        var a = new ApparelSlots();
        var r = ApparelCatalog.Equip(a, "左手套", severedParts: Parts(HumanBody.LeftHand));
        Assert.Equal(EquipOutcome.BlockedSeveredLimb, r);
    }
}
