using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-B16-补·护甲纠错] 开局装备定稿 + 护甲去阵营的规则形态校验：
/// 开局幸存者只穿两件基础衣物（长袖布衣=贴身层护上身、长裤=裤子槽护腿），无皮夹克等特殊护甲；
/// 两件均"仅蔽体"（防护低于皮夹克）；护甲无阵营字段（<see cref="ArmorLayer"/> 只有防御/覆盖，任何人可穿）。
/// 数值皆"拟定待调"，测试锁规则形态不锁具体数字。
/// </summary>
public class StartingGearTests
{
    // ---- 两件开局衣物存在且仅蔽体 ----

    [Fact]
    public void StartingClothes_AreLightBodyCover_WeakerThanLeatherJacket()
    {
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();
        ArmorLayer pants = ArmorTable.Trousers();
        ArmorLayer jacket = ArmorTable.SurvivorArmor().Single(a => a.Name == "皮夹克");

        Assert.Equal("长袖布衣", shirt.Name);
        Assert.Equal("长裤", pants.Name);
        // 仅蔽体：锐/钝防都显著低于皮夹克，且有一行黑色幽默文案。
        Assert.True(shirt.SharpDefense < jacket.SharpDefense && shirt.BluntDefense < jacket.BluntDefense);
        Assert.True(pants.SharpDefense < jacket.SharpDefense && pants.BluntDefense < jacket.BluntDefense);
        Assert.False(string.IsNullOrWhiteSpace(shirt.Description));
        Assert.False(string.IsNullOrWhiteSpace(pants.Description));
    }

    // ---- 覆盖部位分离：长袖布衣护上身、长裤护腿，互不越界 ----

    [Fact]
    public void LongSleeveShirt_CoversTorsoAndArmsOnly()
    {
        IReadOnlySet<string>? covers = ArmorTable.LongSleeveShirt().CoversParts;
        Assert.NotNull(covers);
        Assert.Contains(HumanBody.Chest, covers!);    // 躯干细分：胸+腹（[SPEC-B17]）
        Assert.Contains(HumanBody.Abdomen, covers!);
        Assert.Contains(HumanBody.LeftArm, covers!);
        Assert.Contains(HumanBody.RightArm, covers!);
        // 不护腿/头（开局无护腿甲/头盔）。
        Assert.DoesNotContain(HumanBody.LeftLeg, covers!);
        Assert.DoesNotContain(HumanBody.Head, covers!);
    }

    [Fact]
    public void Trousers_CoversLegsOnly()
    {
        IReadOnlySet<string>? covers = ArmorTable.Trousers().CoversParts;
        Assert.NotNull(covers);
        Assert.Contains(HumanBody.LeftLeg, covers!);
        Assert.Contains(HumanBody.RightLeg, covers!);
        Assert.Contains(HumanBody.LeftCalf, covers!);  // 腿细分：大腿+小腿（[SPEC-B17]）
        Assert.Contains(HumanBody.RightCalf, covers!);
        Assert.DoesNotContain(HumanBody.Chest, covers!);
    }

    // ---- 长裤登记在装备目录、占裤子槽（不占躯干层）----

    [Fact]
    public void Trousers_InCatalog_OccupiesPantsSlot()
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("长裤");
        Assert.NotNull(def);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.Pants }, def!.Slots);
        Assert.DoesNotContain(EquipSlot.SkinLayer, def.Slots);
    }

    // ---- 运动鞋（[SPEC-B16-补2]）：开局第三件，占左右脚槽护双脚，仅蔽体 ----

    [Fact]
    public void Sneakers_AreLightFootCover_InCatalog()
    {
        ArmorLayer sneakers = ArmorTable.Sneakers();
        Assert.Equal("运动鞋", sneakers.Name);
        IReadOnlySet<string>? covers = sneakers.CoversParts;
        Assert.NotNull(covers);
        Assert.Contains(HumanBody.LeftFoot, covers!);
        Assert.Contains(HumanBody.RightFoot, covers!);
        Assert.DoesNotContain(HumanBody.LeftCalf, covers!);   // 只护脚不越界到腿

        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get("运动鞋");
        Assert.NotNull(def);
        Assert.Equal(new HashSet<EquipSlot> { EquipSlot.LeftFoot, EquipSlot.RightFoot }, def!.Slots);
    }

    // ---- 三件同穿无冲突（2→3，[SPEC-B16-补2]）：贴身层 + 裤子槽 + 双脚槽，覆盖并集 = 上身 + 腿 + 脚 ----

    [Fact]
    public void ThreeStartingClothes_EquipWithoutConflict_CoverUpperLegsAndFeet()
    {
        var apparel = new ApparelSlots();
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();

        // 长袖布衣走贴身层（原始护甲层路径）。
        EquipOutcome shirtOutcome = apparel.TryEquip(
            shirt.Name, new HashSet<EquipSlot> { EquipSlot.SkinLayer }, out _, shirt.CoversParts);
        // 长裤 + 运动鞋走目录（裤子槽 / 双脚槽）。
        EquipOutcome pantsOutcome = ApparelCatalog.Equip(apparel, "长裤");
        EquipOutcome shoesOutcome = ApparelCatalog.Equip(apparel, "运动鞋");

        Assert.Equal(EquipOutcome.Equipped, shirtOutcome);
        Assert.Equal(EquipOutcome.Equipped, pantsOutcome);
        Assert.Equal(EquipOutcome.Equipped, shoesOutcome);
        // 开局三件套：三件独立穿戴品，互不占槽冲突。
        Assert.Equal(3, apparel.EquippedItems.Count);
        Assert.Equal("长袖布衣", apparel.ItemAt(EquipSlot.SkinLayer));
        Assert.Equal("长裤", apparel.ItemAt(EquipSlot.Pants));
        Assert.Equal("运动鞋", apparel.ItemAt(EquipSlot.LeftFoot));
        Assert.Equal("运动鞋", apparel.ItemAt(EquipSlot.RightFoot));

        IReadOnlySet<string> covered = apparel.CoveredParts();
        Assert.Contains(HumanBody.Chest, covered);
        Assert.Contains(HumanBody.LeftArm, covered);
        Assert.Contains(HumanBody.LeftLeg, covered);
        Assert.Contains(HumanBody.LeftFoot, covered);   // 三件套后脚也被覆盖
        Assert.Contains(HumanBody.RightFoot, covered);
        // 开局这三件不覆盖头/手。
        Assert.DoesNotContain(HumanBody.Head, covered);
        Assert.DoesNotContain(HumanBody.LeftHand, covered);
    }

    // ---- 护甲无阵营字段：ArmorLayer 上没有任何"阵营/faction"属性，任何人可穿同一件 ----

    [Fact]
    public void ArmorLayer_HasNoFactionField()
    {
        System.Reflection.PropertyInfo[] props = typeof(ArmorLayer).GetProperties();
        Assert.DoesNotContain(props, p =>
            p.Name.Contains("Faction") || p.Name.Contains("阵营") || p.Name.Contains("Owner"));
    }
}
