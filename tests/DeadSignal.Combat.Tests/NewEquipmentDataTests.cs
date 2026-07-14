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

        // ⚠ T21：「伤害介于步枪与自制猎枪之间」这条旧意图**已被用户的新值推翻**——
        // 他把步枪削到 10~24，栓动 16~28 的**单发伤害反而高于步枪**了。
        // 这不是倒挂，是生态位分化，且说得通：
        //   · 栓动猎枪 = 大口径慢速单发（一枪很重：上限 28 > 步枪 24，但 4.5s 才一发 ⇒ DPS ≈ 4.9）
        //   · 军用步枪 = 小口径快速二连发（单发轻，但 2.8s 打两发 ⇒ DPS ≈ 12.1，仍远强）
        // 故改钉新意图：单发更重、但 DPS 显著更低；穿透仍低于步枪。
        Assert.True(bolt.DamageMax > rifle.DamageMax, "栓动＝大口径，单发上限应高于军用步枪");
        Assert.True(bolt.DamageMax > zip.DamageMax, "但仍强于土法自制猎枪");
        double Dps(Weapon w) => (w.DamageMin + w.DamageMax) / 2.0 * w.BurstCount / w.AttackInterval;
        Assert.True(Dps(bolt) < Dps(rifle), "栓动虽然单发重，DPS 必须显著低于步枪（慢+单发）");

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
