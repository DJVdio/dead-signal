using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-B17-补]（部位细分=装备覆盖取舍：皮革胸甲更轻不防腹 / 短裤更凉快不防小腿）+
/// [SPEC-B16-补2]（运动鞋脚部装备）+ 粗布背心补独立覆盖层 的规则形态校验。
/// 三新件 + 粗布背心走覆盖体系任意子集能力；数值皆"拟定待调"，测试锁规则形态不锁具体数字。
/// </summary>
public class PartGearTests
{
    // ---- 皮革胸甲：仅护胸（不防腹），防护高于长袖布衣、低于板甲，重量轻于板甲 ----

    [Fact]
    public void ChestPlate_CoversChestOnly_NotAbdomen()
    {
        IReadOnlySet<string>? covers = ArmorTable.ChestPlate().CoversParts;
        Assert.NotNull(covers);
        Assert.Contains(HumanBody.Chest, covers!);
        Assert.DoesNotContain(HumanBody.Abdomen, covers!);   // 取舍核心：不防腹（[SPEC-B17-补]）
    }

    [Fact]
    public void ChestPlate_DefenseBetweenShirtAndPlate_LighterThanPlate()
    {
        ArmorLayer chest = ArmorTable.ChestPlate();
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();
        ArmorLayer plate = ArmorTable.Plate();

        // 防护高于长袖布衣、低于全躯干板甲。
        Assert.True(chest.SharpDefense > shirt.SharpDefense && chest.SharpDefense < plate.SharpDefense);
        Assert.True(chest.BluntDefense > shirt.BluntDefense && chest.BluntDefense < plate.BluntDefense);
        // 重量轻于板甲。
        Assert.True(chest.Weight < plate.Weight);
        Assert.False(string.IsNullOrWhiteSpace(chest.Description));
    }

    [Fact]
    public void ChestPlate_InCatalog_OccupiesPlateSlot()
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("皮革胸甲");
        Assert.NotNull(def);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.PlateLayer }, def!.Slots);
    }

    // ---- 短裤：仅护大腿（不防小腿），比长裤轻，与长裤同占裤子槽（互斥）----

    [Fact]
    public void Shorts_CoversThighsOnly_NotCalves()
    {
        IReadOnlySet<string>? covers = ArmorTable.Shorts().CoversParts;
        Assert.NotNull(covers);
        Assert.Contains(HumanBody.LeftLeg, covers!);
        Assert.Contains(HumanBody.RightLeg, covers!);
        Assert.DoesNotContain(HumanBody.LeftCalf, covers!);   // 取舍核心：不防小腿（[SPEC-B17-补]）
        Assert.DoesNotContain(HumanBody.RightCalf, covers!);
    }

    [Fact]
    public void Shorts_LighterThanTrousers()
    {
        Assert.True(ArmorTable.Shorts().Weight < ArmorTable.Trousers().Weight);
        Assert.False(string.IsNullOrWhiteSpace(ArmorTable.Shorts().Description));
    }

    [Fact]
    public void ShortsAndTrousers_SharePantsSlot_MutuallyExclusive()
    {
        ApparelCatalog.ApparelDef? shorts = ApparelCatalog.Get("短裤");
        ApparelCatalog.ApparelDef? trousers = ApparelCatalog.Get("长裤");
        Assert.NotNull(shorts);
        Assert.NotNull(trousers);
        Assert.Contains(EquipSlot.Pants, shorts!.Slots);
        Assert.Contains(EquipSlot.Pants, trousers!.Slots);

        // 同占裤子槽：穿了长裤再穿短裤会顶替长裤（互斥）。
        var apparel = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(apparel, "长裤"));
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(apparel, "短裤", replace: true));
        Assert.Equal("短裤", apparel.ItemAt(EquipSlot.Pants));
        Assert.False(apparel.IsEquipped("长裤"));
    }

    // ---- 运动鞋：护双脚（含脚趾子树），占左右脚槽，仅蔽体级 ----

    [Fact]
    public void Sneakers_CoverBothFeetAndToes()
    {
        IReadOnlySet<string>? covers = ArmorTable.Sneakers().CoversParts;
        Assert.NotNull(covers);
        Assert.Contains(HumanBody.LeftFoot, covers!);
        Assert.Contains(HumanBody.RightFoot, covers!);
        // 脚趾子树连带（照手套护指先例）。
        Assert.Contains(HumanBody.LeftBigToe, covers!);
        Assert.Contains(HumanBody.RightBigToe, covers!);
        // 不越界到腿。
        Assert.DoesNotContain(HumanBody.LeftCalf, covers!);
    }

    [Fact]
    public void Sneakers_LightBodyCover_WeakerThanLeatherJacket()
    {
        ArmorLayer sneakers = ArmorTable.Sneakers();
        ArmorLayer jacket = ArmorTable.SurvivorArmor().Single(a => a.Name == "皮夹克");
        Assert.True(sneakers.SharpDefense < jacket.SharpDefense && sneakers.BluntDefense < jacket.BluntDefense);
        Assert.False(string.IsNullOrWhiteSpace(sneakers.Description));
    }

    [Fact]
    public void Sneakers_InCatalog_ArePaired_OneShoePerFootSlot()
    {
        // [SPEC-B18-补] 鞋不分左右（一个 def），但一只鞋只占一只脚槽——两只才护全。
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("运动鞋");
        Assert.NotNull(def);
        Assert.True(def!.Paired);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.LeftFoot, EquipSlot.RightFoot }, def.Slots);  // 候选槽
        Assert.DoesNotContain(HumanBody.RightFoot, def.CoversFor(EquipSlot.LeftFoot)!);               // 左脚那只不护右脚
    }

    // ---- 粗布背心：补独立覆盖层，仅护胸+腹（无袖不护臂），不再是全覆盖 null ----

    [Fact]
    public void CoarseClothVest_CoversChestAndAbdomenOnly_NotArms()
    {
        IReadOnlySet<string>? covers = ArmorTable.CoarseClothVest().CoversParts;
        Assert.NotNull(covers);   // 关键：不再是 null（全覆盖）
        Assert.Contains(HumanBody.Chest, covers!);
        Assert.Contains(HumanBody.Abdomen, covers!);
        Assert.DoesNotContain(HumanBody.LeftArm, covers!);   // 无袖背心不护臂
        Assert.DoesNotContain(HumanBody.RightArm, covers!);
    }

    [Fact]
    public void CoarseClothVest_InCatalog_OccupiesOuterLayer()
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("粗布背心");
        Assert.NotNull(def);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.OuterLayer }, def!.Slots);
        Assert.NotNull(def.CoversParts);   // 覆盖信息落地，不再空
    }

    // ---- 覆盖体系任意子集：皮革胸甲(仅胸)叠长袖布衣(胸+腹+臂)，命中胸有双层、命中腹只有布衣 ----

    [Fact]
    public void ChestPlateOverShirt_ChestDoubleCovered_AbdomenSingleCovered()
    {
        var apparel = new ApparelSlots();
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();
        apparel.TryEquip(shirt.Name, new HashSet<EquipSlot> { EquipSlot.SkinLayer }, out _, shirt.CoversParts);
        ApparelCatalog.Equip(apparel, "皮革胸甲");

        // 命中胸：皮革胸甲 + 长袖布衣两件都覆盖。
        var coversChest = apparel.ActiveCoverage().Where(c => c.CoversParts.Contains(HumanBody.Chest)).Select(c => c.Item).ToHashSet();
        Assert.Contains("皮革胸甲", coversChest);
        Assert.Contains("长袖布衣", coversChest);
        // 命中腹：只有长袖布衣覆盖（皮革胸甲不防腹）。
        var coversAbdomen = apparel.ActiveCoverage().Where(c => c.CoversParts.Contains(HumanBody.Abdomen)).Select(c => c.Item).ToHashSet();
        Assert.DoesNotContain("皮革胸甲", coversAbdomen);
        Assert.Contains("长袖布衣", coversAbdomen);
    }
}
