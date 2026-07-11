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

    // ---- 栓动猎枪：新增民用猎枪，入 Arsenal（自动进 WeaponCatalog/掉落可得），数值介于步枪与土制枪之间 ----

    [Fact]
    public void BoltActionHuntingRifle_InArsenal_WithBetweenStats()
    {
        var names = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.Contains("栓动猎枪", names);

        Weapon bolt = WeaponTable.BoltActionHuntingRifle();
        Weapon rifle = WeaponTable.Rifle();
        Weapon zip = WeaponTable.Zipgun();

        Assert.True(bolt.IsRanged);
        Assert.True(bolt.TwoHanded);
        Assert.Equal(1, bolt.BurstCount);                       // 单发，非连发
        Assert.Equal(4.5, bolt.AttackInterval, 6);              // 栓动慢冷却锚点
        // 伤害/穿透/射程介于步枪（军用强）与土制枪（土制弱）之间
        Assert.InRange(bolt.DamageMax, zip.DamageMax, rifle.DamageMax);
        Assert.InRange(bolt.Penetration, zip.Penetration, rifle.Penetration);
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

    // ---- 劳保手套：手部轻护甲层（当前无 12 槽系统，先作一层轻护甲） ----

    [Fact]
    public void WorkGloves_IsPairOfLightArmorLayers()
    {
        // 一副 = 左手套 + 右手套两件独立护甲（装备槽按左右分）。
        IReadOnlyList<ArmorLayer> pair = ArmorTable.WorkGloves();
        Assert.Equal(2, pair.Count);
        Assert.Contains(pair, g => g.Name == "左手套");
        Assert.Contains(pair, g => g.Name == "右手套");

        foreach (ArmorLayer glove in pair)
        {
            // 防护极低（轻护甲）。
            Assert.True(glove.SharpDefense > 0 && glove.SharpDefense <= 2, "手套锐防应为轻护甲");
            Assert.True(glove.BluntDefense > 0 && glove.BluntDefense <= 2, "手套钝防应为轻护甲");
            Assert.True(glove.SharpDefense < ArmorTable.CoarseClothCoat().SharpDefense, "手套防护应低于粗布外套");
        }
    }
}
