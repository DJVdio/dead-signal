using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 布鲁斯（狗）穿戴系统纯逻辑单测（批次5，道格 2 级解锁五件套）：
/// 身体/头两槽的穿戴/顶替/卸装、护甲层聚合（喂 DefenderArmor）、口袋狗衣携带容量、目录映射。
/// 护甲/容量数值皆"拟定待调"，测试锁的是规则形态。
/// </summary>
public class DogApparelTests
{
    // ---- 目录 ----

    [Fact]
    public void Catalog_HasFivePieces_WithExpectedSlots()
    {
        Assert.Equal(5, DogGearCatalog.Defs.Count);
        Assert.Equal(DogEquipSlot.Body, DogGearCatalog.Get(DogGearCatalog.ClothVestKey)!.Slot);
        Assert.Equal(DogEquipSlot.Body, DogGearCatalog.Get(DogGearCatalog.LeatherVestKey)!.Slot);
        Assert.Equal(DogEquipSlot.Body, DogGearCatalog.Get(DogGearCatalog.PocketVestKey)!.Slot);
        Assert.Equal(DogEquipSlot.Head, DogGearCatalog.Get(DogGearCatalog.IronHelmetKey)!.Slot);
        Assert.Equal(DogEquipSlot.Head, DogGearCatalog.Get(DogGearCatalog.WireHelmetKey)!.Slot);
    }

    [Fact]
    public void Catalog_IsDogGear_TrueOnlyForRegisteredKeys()
    {
        Assert.True(DogGearCatalog.IsDogGear(DogGearCatalog.ClothVestKey));
        Assert.False(DogGearCatalog.IsDogGear("粗布背心"));
        Assert.False(DogGearCatalog.IsDogGear(null));
    }

    /// <summary>[SPEC-B18] 五件套<b>全部</b>有甲——口袋狗衣也从无甲改为薄甲(2/1)。</summary>
    [Fact]
    public void Catalog_AllFiveGearsHaveArmor()
    {
        Assert.NotNull(DogGearCatalog.Get(DogGearCatalog.ClothVestKey)!.Armor());
        Assert.NotNull(DogGearCatalog.Get(DogGearCatalog.LeatherVestKey)!.Armor());
        Assert.NotNull(DogGearCatalog.Get(DogGearCatalog.PocketVestKey)!.Armor()); // 口袋狗衣：薄甲 + 6kg 负重
        Assert.NotNull(DogGearCatalog.Get(DogGearCatalog.IronHelmetKey)!.Armor());
        Assert.NotNull(DogGearCatalog.Get(DogGearCatalog.WireHelmetKey)!.Armor());
    }

    /// <summary>[SPEC-B18] 铁皮头甲锐防更高但更重；钝防两者持平（表值 18/12 vs 12/12）——铁丝是"轻便档"而非纯劣档。</summary>
    [Fact]
    public void Catalog_IronHelmet_OutDefendsWireHelmet_OnSharpOnly()
    {
        ArmorLayer iron = DogGearCatalog.Get(DogGearCatalog.IronHelmetKey)!.Armor()!;
        ArmorLayer wire = DogGearCatalog.Get(DogGearCatalog.WireHelmetKey)!.Armor()!;
        Assert.True(iron.SharpDefense > wire.SharpDefense);
        Assert.Equal(iron.BluntDefense, wire.BluntDefense);   // 钝防持平（表口径）
        // 铁丝更轻便。
        Assert.True(wire.Weight < iron.Weight);
    }

    [Fact]
    public void Catalog_LeatherVest_OutDefendsClothVest()
    {
        ArmorLayer cloth = DogGearCatalog.Get(DogGearCatalog.ClothVestKey)!.Armor()!;
        ArmorLayer leather = DogGearCatalog.Get(DogGearCatalog.LeatherVestKey)!.Armor()!;
        Assert.True(leather.SharpDefense > cloth.SharpDefense);
    }

    // ---- 穿戴 ----

    [Fact]
    public void Equip_Body_OccupiesBodySlot()
    {
        var a = new DogApparelSlots();
        Assert.Equal(DogEquipOutcome.Equipped, a.TryEquip(DogGearCatalog.ClothVestKey, out var displaced));
        Assert.Null(displaced);
        Assert.Equal(DogGearCatalog.ClothVestKey, a.ItemAt(DogEquipSlot.Body));
        Assert.True(a.IsEquipped(DogGearCatalog.ClothVestKey));
        Assert.False(a.IsOccupied(DogEquipSlot.Head));
    }

    [Fact]
    public void Equip_UnknownGear_Blocked()
    {
        var a = new DogApparelSlots();
        Assert.Equal(DogEquipOutcome.BlockedUnknownGear, a.TryEquip("不存在的装备", out _));
        Assert.False(a.IsOccupied(DogEquipSlot.Body));
    }

    [Fact]
    public void Equip_SameSlot_ReplacesAndReturnsDisplaced()
    {
        var a = new DogApparelSlots();
        a.TryEquip(DogGearCatalog.ClothVestKey, out _);
        // 皮制狗衣与布制狗衣同占身体槽 → 顶替。
        Assert.Equal(DogEquipOutcome.Replaced, a.TryEquip(DogGearCatalog.LeatherVestKey, out var displaced));
        Assert.Equal(DogGearCatalog.ClothVestKey, displaced);
        Assert.Equal(DogGearCatalog.LeatherVestKey, a.ItemAt(DogEquipSlot.Body));
        Assert.False(a.IsEquipped(DogGearCatalog.ClothVestKey));
    }

    [Fact]
    public void Equip_BodyAndHead_Coexist()
    {
        var a = new DogApparelSlots();
        a.TryEquip(DogGearCatalog.LeatherVestKey, out _);
        a.TryEquip(DogGearCatalog.IronHelmetKey, out _);
        Assert.Equal(2, a.EquippedKeys.Count);
        Assert.Equal(DogGearCatalog.LeatherVestKey, a.ItemAt(DogEquipSlot.Body));
        Assert.Equal(DogGearCatalog.IronHelmetKey, a.ItemAt(DogEquipSlot.Head));
    }

    [Fact]
    public void Equip_Idempotent_SameItemTwice_NoDisplaced()
    {
        var a = new DogApparelSlots();
        a.TryEquip(DogGearCatalog.ClothVestKey, out _);
        Assert.Equal(DogEquipOutcome.Equipped, a.TryEquip(DogGearCatalog.ClothVestKey, out var displaced));
        Assert.Null(displaced);
        Assert.Single(a.EquippedKeys);
    }

    [Fact]
    public void Unequip_RemovesItem()
    {
        var a = new DogApparelSlots();
        a.TryEquip(DogGearCatalog.WireHelmetKey, out _);
        Assert.True(a.Unequip(DogGearCatalog.WireHelmetKey));
        Assert.False(a.IsOccupied(DogEquipSlot.Head));
        Assert.False(a.Unequip(DogGearCatalog.WireHelmetKey)); // 已不在身
    }

    [Fact]
    public void UnequipSlot_ReturnsItemThatWasThere()
    {
        var a = new DogApparelSlots();
        a.TryEquip(DogGearCatalog.PocketVestKey, out _);
        Assert.Equal(DogGearCatalog.PocketVestKey, a.UnequipSlot(DogEquipSlot.Body));
        Assert.Null(a.UnequipSlot(DogEquipSlot.Body)); // 已空
    }

    // ---- 护甲聚合 ----

    [Fact]
    public void ArmorLayers_AggregatesEquippedArmor()
    {
        var a = new DogApparelSlots();
        a.TryEquip(DogGearCatalog.LeatherVestKey, out _);
        a.TryEquip(DogGearCatalog.IronHelmetKey, out _);
        var layers = a.ArmorLayers();
        Assert.Equal(2, layers.Count);
        Assert.Contains(layers, l => l.CoversParts!.Contains(HumanBody.Chest)); // 身体甲护躯干（细分后=胸+腹）
        Assert.Contains(layers, l => l.CoversParts!.Contains(HumanBody.Head));  // 头甲护头
    }

    /// <summary>[SPEC-B18] 口袋狗衣不再是"无甲纯容器"：表给了 2/1 薄甲，穿上应产出一层身体甲。</summary>
    [Fact]
    public void ArmorLayers_PocketVest_ProducesThinBodyLayer()
    {
        var a = new DogApparelSlots();
        a.TryEquip(DogGearCatalog.PocketVestKey, out _);
        var layer = Assert.Single(a.ArmorLayers());
        Assert.Equal("口袋狗衣", layer.Name);
        Assert.Contains(HumanBody.Chest, layer.CoversParts!);
        Assert.Contains(HumanBody.Abdomen, layer.CoversParts!);
        // 最薄的一件身体甲：弱于布制狗衣（拿容量换防护）。
        Assert.True(layer.SharpDefense < ArmorTable.DogClothVest().SharpDefense);
        Assert.True(layer.BluntDefense < ArmorTable.DogClothVest().BluntDefense);
    }

    // ---- 携带容量 ----

    [Fact]
    public void CarryCapacity_OnlyPocketVestGrantsCapacity()
    {
        var a = new DogApparelSlots();
        Assert.Equal(0f, a.TotalCarryCapacity());
        a.TryEquip(DogGearCatalog.ClothVestKey, out _);
        Assert.Equal(0f, a.TotalCarryCapacity()); // 布制狗衣无容量
        a.TryEquip(DogGearCatalog.PocketVestKey, out _); // 顶替布制狗衣
        Assert.Equal(DogGearCatalog.PocketVestCapacity, a.TotalCarryCapacity());
    }

    /// <summary>口袋狗衣负重以数值表为准：为狗提供 <b>8kg</b>（T29 用户手改 6 → 8；更早的 12kg 早已作废）。</summary>
    [Fact]
    public void PocketVestCapacity_MatchesArmorTable_EightKg()
    {
        Assert.Equal(8f, DogGearCatalog.PocketVestCapacity);
    }
}
