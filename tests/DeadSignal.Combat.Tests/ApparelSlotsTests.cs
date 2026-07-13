using System.Collections.Generic;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// §6 穿戴槽纯逻辑单测：11 槽占用、多槽占多槽、成对手套各占一手、断肢禁装、
/// 替换/卸装、护甲覆盖聚合，以及装备目录映射表（粗布外套/劳保手套/防毒面具/板甲、刺剑草叉非穿戴）。
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
        // 板甲要占 装甲层+裤装，裤子已被占 → 冲突不穿。
        var r = a.TryEquip("板甲", Slots(EquipSlot.PlateLayer, EquipSlot.Pants), displaced: out _);
        Assert.Equal(EquipOutcome.BlockedSlotOccupied, r);
        Assert.False(a.IsEquipped("板甲"));
        Assert.False(a.IsOccupied(EquipSlot.PlateLayer)); // 冲突时不得残留占用装甲层
    }

    // ---- 同名多实例：成对品不分左右，同名两件各占一手（[SPEC-B18-补]）----

    [Fact]
    public void Equip_SameNameTwice_DifferentSlots_KeepsBothItems()
    {
        var a = new ApparelSlots();
        a.TryEquip("劳保手套", Slots(EquipSlot.LeftHand), coversParts: Parts(HumanBody.LeftHand), displaced: out _);
        a.TryEquip("劳保手套", Slots(EquipSlot.RightHand), coversParts: Parts(HumanBody.RightHand), displaced: out _);

        Assert.Equal("劳保手套", a.ItemAt(EquipSlot.LeftHand));
        Assert.Equal("劳保手套", a.ItemAt(EquipSlot.RightHand));
        Assert.Equal(2, a.EquippedItems.Count);   // 同名两件都在身（不互相顶替）
        Assert.Equal(Slots(EquipSlot.LeftHand, EquipSlot.RightHand), a.SlotsOf("劳保手套"));
        // 覆盖是两件之并。
        Assert.Contains(HumanBody.LeftHand, a.CoveredParts());
        Assert.Contains(HumanBody.RightHand, a.CoveredParts());
    }

    [Fact]
    public void Equip_SameNameSameSlot_IsIdempotentRewear()
    {
        var a = new ApparelSlots();
        a.TryEquip("劳保手套", Slots(EquipSlot.LeftHand), coversParts: Parts(HumanBody.LeftHand), displaced: out _);
        a.TryEquip("劳保手套", Slots(EquipSlot.LeftHand), coversParts: Parts(HumanBody.LeftHand), displaced: out _);
        Assert.Single(a.EquippedItems);   // 同名穿到自己已占的槽 = 重穿，不新增一件
    }

    [Fact]
    public void Unequip_ByName_RemovesAllItemsOfThatName()
    {
        var a = new ApparelSlots();
        a.TryEquip("劳保手套", Slots(EquipSlot.LeftHand), coversParts: Parts(HumanBody.LeftHand), displaced: out _);
        a.TryEquip("劳保手套", Slots(EquipSlot.RightHand), coversParts: Parts(HumanBody.RightHand), displaced: out _);
        Assert.True(a.Unequip("劳保手套"));   // 按名卸 = 两只一起脱
        Assert.Empty(a.EquippedItems);
        Assert.Empty(a.CoveredParts());
    }

    // ---- 断肢禁装 ----

    [Fact]
    public void Equip_SeveredHand_DisablesThatHandSlot()
    {
        var a = new ApparelSlots();
        var severed = Parts(HumanBody.LeftHand);
        var r = a.TryEquip("劳保手套", Slots(EquipSlot.LeftHand), severedParts: severed, displaced: out _);
        Assert.Equal(EquipOutcome.BlockedSeveredLimb, r);
        Assert.False(a.IsEquipped("劳保手套"));
    }

    [Fact]
    public void Equip_SeveredLeft_RightHandStillWearable()
    {
        var a = new ApparelSlots();
        var severed = Parts(HumanBody.LeftHand);
        var r = a.TryEquip("劳保手套", Slots(EquipSlot.RightHand), severedParts: severed, displaced: out _);
        Assert.Equal(EquipOutcome.Equipped, r);   // 断左手不妨碍右手那只
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
        Assert.DoesNotContain(HumanBody.Chest, covered); // 未穿躯干甲则胸不在覆盖并集
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

    // ---- 成对品（劳保手套/运动鞋）：物品定义不分左右，但一件只占一个槽——护住双手/双脚要两件 [SPEC-B18-补] ----

    [Fact]
    public void Catalog_WorkGloves_ArePaired_OneDefTwoCandidateSlots()
    {
        var gloves = ApparelCatalog.Get("劳保手套")!;
        Assert.True(gloves.Paired);                                          // 成对品：一件占一槽，可装左或右
        Assert.Equal(Slots(EquipSlot.LeftHand, EquipSlot.RightHand), gloves.Slots);  // 候选槽（不是"同时占两个"）
        // 按槽取实际覆盖：装左手只护左手（含左五指），不越到右手。
        IReadOnlySet<string> left = gloves.CoversFor(EquipSlot.LeftHand)!;
        Assert.Contains(HumanBody.LeftHand, left);
        Assert.Contains(HumanBody.LeftThumb, left);
        Assert.DoesNotContain(HumanBody.RightHand, left);
        IReadOnlySet<string> right = gloves.CoversFor(EquipSlot.RightHand)!;
        Assert.Contains(HumanBody.RightHand, right);
        Assert.Contains(HumanBody.RightPinky, right);
        Assert.DoesNotContain(HumanBody.LeftHand, right);
    }

    [Fact]
    public void Catalog_TwoWorkGloves_CoverBothHands_AsTwoSeparateItems()
    {
        var a = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(a, "劳保手套", EquipSlot.LeftHand));
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(a, "劳保手套", EquipSlot.RightHand));

        // 两件独立在身（同名两实例），双手都护住。
        Assert.Equal(2, a.EquippedItems.Count);
        Assert.Equal("劳保手套", a.ItemAt(EquipSlot.LeftHand));
        Assert.Equal("劳保手套", a.ItemAt(EquipSlot.RightHand));
        Assert.Contains(HumanBody.LeftHand, a.CoveredParts());
        Assert.Contains(HumanBody.RightHand, a.CoveredParts());
    }

    [Fact]
    public void Catalog_OneWorkGlove_CoversOnlyThatHand()
    {
        var a = new ApparelSlots();
        ApparelCatalog.Equip(a, "劳保手套", EquipSlot.LeftHand);

        Assert.Single(a.EquippedItems);
        Assert.Null(a.ItemAt(EquipSlot.RightHand));
        Assert.Contains(HumanBody.LeftHand, a.CoveredParts());
        Assert.DoesNotContain(HumanBody.RightHand, a.CoveredParts());   // 只装一只 = 只护一边（用户要的粒度）
    }

    [Fact]
    public void Catalog_UnequipSlot_RemovesOnlyThatGlove()
    {
        var a = new ApparelSlots();
        ApparelCatalog.Equip(a, "劳保手套", EquipSlot.LeftHand);
        ApparelCatalog.Equip(a, "劳保手套", EquipSlot.RightHand);

        Assert.Equal("劳保手套", a.UnequipSlot(EquipSlot.LeftHand));
        // 右手那只还在（同名不连坐）。
        Assert.Single(a.EquippedItems);
        Assert.Equal("劳保手套", a.ItemAt(EquipSlot.RightHand));
        Assert.DoesNotContain(HumanBody.LeftHand, a.CoveredParts());
        Assert.Contains(HumanBody.RightHand, a.CoveredParts());
    }

    [Fact]
    public void Catalog_Sneakers_ArePaired_TwoShoesForTwoFeet()
    {
        var shoes = ApparelCatalog.Get("运动鞋")!;
        Assert.True(shoes.Paired);
        Assert.Equal(Slots(EquipSlot.LeftFoot, EquipSlot.RightFoot), shoes.Slots);

        var a = new ApparelSlots();
        ApparelCatalog.Equip(a, "运动鞋", EquipSlot.LeftFoot);
        Assert.Contains(HumanBody.LeftFoot, a.CoveredParts());
        Assert.DoesNotContain(HumanBody.RightFoot, a.CoveredParts());   // 一只鞋只护一只脚
        ApparelCatalog.Equip(a, "运动鞋", EquipSlot.RightFoot);
        Assert.Contains(HumanBody.RightFoot, a.CoveredParts());
        Assert.Equal(2, a.EquippedItems.Count);
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
        // 不指定槽 → 成对品自动落到第一只空闲候选槽（左优先）。
        var a = new ApparelSlots();
        var r = ApparelCatalog.Equip(a, "劳保手套");
        Assert.Equal(EquipOutcome.Equipped, r);
        Assert.Equal("劳保手套", a.ItemAt(EquipSlot.LeftHand));
        Assert.Null(a.ItemAt(EquipSlot.RightHand));   // 一件只占一槽
        Assert.Contains(HumanBody.LeftHand, a.CoveredParts());
    }

    [Fact]
    public void Catalog_Equip_RespectsSeveredLimb()
    {
        // 指定断掉的那只手 → 穿不上。
        var a = new ApparelSlots();
        var r = ApparelCatalog.Equip(a, "劳保手套", EquipSlot.LeftHand, severedParts: Parts(HumanBody.LeftHand));
        Assert.Equal(EquipOutcome.BlockedSeveredLimb, r);

        // 不指定槽 → 自动跳过断手，落到还在的右手。
        var b = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(b, "劳保手套", severedParts: Parts(HumanBody.LeftHand)));
        Assert.Equal("劳保手套", b.ItemAt(EquipSlot.RightHand));
    }
}
