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

    // ---- 冲锋枪：放开可双持（用户拍板） ----

    [Fact]
    public void Smg_CanDualWield()
    {
        Assert.True(WeaponTable.Smg().CanDualWield);
    }

    // ---- 两把新近战入 Arsenal ----

    [Fact]
    public void Arsenal_ContainsNewMeleeWeapons()
    {
        var names = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.Contains("刺剑", names);
        Assert.Contains("草叉", names);
    }

    // ---- 栓动猎枪：新增民用猎枪，入 Arsenal（自动进 WeaponCatalog/掉落可得），数值介于步枪与自制猎枪之间 ----

    [Fact]
    public void BoltActionHuntingRifle_InArsenal_WithBetweenStats()
    {
        var names = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.Contains("栓动猎枪", names);

        Weapon bolt = WeaponTable.BoltActionHuntingRifle();
        Weapon rifle = WeaponTable.Rifle();
        Weapon zip = WeaponTable.ImprovisedHuntingGun();

        Assert.True(bolt.IsRanged);
        Assert.True(bolt.TwoHanded);
        Assert.Equal(1, bolt.BurstCount);                       // 单发，非连发
        Assert.Equal(4.5, bolt.AttackInterval, 6);              // 栓动慢冷却锚点
        // 伤害/射程介于步枪（军用强）与自制猎枪（土法弱）之间。
        // ⚠ 穿透不再夹在中间：自制猎枪经用户拍板改为 25%（>步枪 21%，"打得不重但钻得深"），
        // 故栓动猎枪 16% 现在是全枪械最低，穿透维度改为只与步枪比。
        Assert.InRange(bolt.DamageMax, zip.DamageMax, rifle.DamageMax);
        Assert.True(bolt.Penetration < rifle.Penetration);
        Assert.InRange(bolt.MaxRange!.Value, zip.MaxRange!.Value, rifle.MaxRange!.Value);
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
