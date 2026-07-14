using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 新增装备数据的字段/形态校验（数值皆原型期拟定待调，这里只锁"规则形态"不锁具体数字）：
/// 刺剑=单手锐器可双持、草叉=双手锐器、粗布外套=外套层且防护低于皮甲、劳保手套=手部轻护甲层；
/// 并验证冲锋枪已放开可双持、两把新近战入 Arsenal。
/// </summary>
public class NewEquipmentDataTests
{
    // ---- 刺剑：单手锐器，可双持 ----

    [Fact]
    public void Rapier_IsOneHandedSharp_CanDualWield()
    {
        Weapon rapier = WeaponTable.Rapier();
        Assert.Equal("刺剑", rapier.Name);
        Assert.Equal(DamageType.Sharp, rapier.DamageType);
        Assert.False(rapier.TwoHanded);
        Assert.True(rapier.CanDualWield);
        Assert.False(rapier.IsRanged);
        Assert.True(rapier.DamageMax > rapier.DamageMin);
        Assert.True(rapier.Penetration > 0);
    }

    // ---- 草叉：双手锐器 ----

    [Fact]
    public void Pitchfork_IsTwoHandedSharp()
    {
        Weapon fork = WeaponTable.Pitchfork();
        Assert.Equal("草叉", fork.Name);
        Assert.Equal(DamageType.Sharp, fork.DamageType);
        Assert.True(fork.TwoHanded);
        Assert.False(fork.IsRanged);
        Assert.True(fork.DamageMax > fork.DamageMin);
        Assert.True(fork.Penetration > 0);
    }

    // ---- 冲锋枪：双手抵肩、**不可双持**（用户拍板「保双手，放弃双持」，推翻旧的"放开可双持"口径） ----

    [Fact]
    public void Smg_IsTwoHanded_AndNotDualWieldable()
    {
        Weapon smg = WeaponTable.Smg();
        Assert.True(smg.TwoHanded);
        // 旧口径曾断言 CanDualWield=true，但双手武器在 EquipToHand 里被短路、永远进不了双持分支，
        // 那个标记从未生效 ⇒ 用户拍板删除。双持只对单手武器开放（设计文档 §5「双持限单手类」）。
        Assert.False(smg.CanDualWield);
    }

    // ---- 两把新近战入 Arsenal ----

    [Fact]
    public void Arsenal_ContainsNewMeleeWeapons()
    {
        var names = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.Contains("刺剑", names);
        Assert.Contains("草叉", names);
    }

    // ---- 栓动猎枪：**已被用户从数值表删除**（T29）----
    //
    // 本处原有 BoltActionHuntingRifle_InArsenal_WithBetweenStats，钉的是"这把民用猎枪存在，
    // 且数值介于步枪与自制猎枪之间"。用户在 wiki 上把整行划掉（墓碑 sync=「删除·待同步进代码」）
    // ⇒ **它编码的意图已被整体推翻**：武器都没了，谈何"数值介于两者之间"。
    // 故不是改数字，而是**改钉新意图**：这把枪不许再回到 Arsenal（防止日后有人"顺手补回来"）。

    [Fact]
    public void BoltActionHuntingRifle_已被用户删除_不得再出现在武器表里()
    {
        var names = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.DoesNotContain("栓动猎枪", names);

        // 枪械剩 6 把：自制猎枪 / 手枪 / 冲锋枪 / 步枪 / 狙击枪 / 自制霰弹枪。
        // 全表 25 把 = 23（删掉栓动猎枪之后）+ 消防斧（[批次25·T44]）+ 骨刀（[T56]），两把都追加在末尾。
        // ⚠️ 这个计数本身**不是**本测试的意图（意图是"栓动猎枪不许回来"）——它只是顺手的护栏。
        //    以后再加武器，改这个数字就行；但 DoesNotContain 那条一个字都不许动。
        Assert.Equal(25, WeaponTable.Arsenal().Count);
    }

    // ---- 粗布外套：外套层，防护劣于皮甲 ----

    [Fact]
    public void CoarseClothCoat_IsOuterLayer_WeakerThanLeather()
    {
        ArmorLayer coat = ArmorTable.CoarseClothCoat();
        ArmorLayer leather = ArmorTable.Leather();
        Assert.Equal("粗布外套", coat.Name);
        Assert.Equal(ArmorSlot.Outer, coat.Slot);
        Assert.True(coat.SharpDefense < leather.SharpDefense, "粗布外套锐防应劣于皮甲");
        Assert.True(coat.BluntDefense < leather.BluntDefense, "粗布外套钝防应劣于皮甲");
        Assert.True(coat.SharpDefense > 0 && coat.BluntDefense > 0);
    }

    // ---- 劳保手套：物品定义不分左右（表里只有一行），表口径覆盖双手含五指；
    //      实际一件只占一只手槽、只护那一只手，双手要两件（[SPEC-B18-补]，见 ApparelSlotsTests）----

    [Fact]
    public void WorkGloves_AreOneUnsidedDef_TableCoversBothHands()
    {
        // 表口径（这件"能"护的部位类别）；穿戴时按槽裁剪成单只手。
        ArmorLayer gloves = ArmorTable.WorkGloves();
        Assert.Equal("劳保手套", gloves.Name);
        Assert.Equal(ArmorSlot.Skin, gloves.Slot);
        Assert.NotNull(gloves.CoversParts);
        Assert.Contains(HumanBody.LeftHand, gloves.CoversParts!);
        Assert.Contains(HumanBody.RightHand, gloves.CoversParts!);
        Assert.Contains(HumanBody.LeftThumb, gloves.CoversParts!);   // 连带五指子树
        Assert.Contains(HumanBody.RightPinky, gloves.CoversParts!);
        Assert.DoesNotContain(HumanBody.Chest, gloves.CoversParts!);

        // 全身最轻的一件（覆盖面最小）。
        Assert.True(gloves.Weight < ArmorTable.CoarseClothCoat().Weight, "手套应比粗布外套轻");
        Assert.True(gloves.SharpDefense > 0 && gloves.BluntDefense > 0);
    }
}
