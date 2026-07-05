using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// PawnInspection 只读检视快照映射单测。核心约束：快照是纯数据拷贝，
/// 构造后再改动源 Body 不应回改快照（UI 拿到的是死数据、改不坏战斗）。
/// </summary>
public class PawnInspectionTests
{
    private static Body FreshBody() => CombatData.NewHumanoidBody();

    [Fact]
    public void FromBody_MapsAggregateAndAllParts()
    {
        Body body = FreshBody();
        Weapon weapon = CombatData.Pistol();
        var armor = CombatData.SurvivorArmor();

        PawnInspection snap = PawnInspection.FromBody(body, weapon, armor, "阿强");

        Assert.Equal("阿强", snap.DisplayName);
        Assert.False(snap.IsDead);
        Assert.False(snap.IsUnconscious);
        Assert.False(snap.IsFullyBlind);
        Assert.Equal(BloodLossTier.None, snap.BloodTier);
        Assert.Equal(1.0, snap.BloodRatio, 3);
        // 满员人体细部位数 = 15。
        Assert.Equal(body.Parts.Count, snap.Parts.Count);
        Assert.Equal(15, snap.Parts.Count);
    }

    [Fact]
    public void FromBody_MapsPartFields()
    {
        Body body = FreshBody();
        PawnInspection snap = PawnInspection.FromBody(body, null, System.Array.Empty<ArmorLayer>(), "x");

        PartStatus torso = snap.Parts.Single(p => p.Name == HumanBody.Torso);
        Assert.Equal(body.HpOf(HumanBody.Torso), torso.Hp);
        Assert.Equal(body.MaxHpOf(HumanBody.Torso), torso.MaxHp);
        Assert.Equal(BodyRegion.Torso, torso.Region);
        Assert.Equal(BodyPartCategory.Vital, torso.Category);
        Assert.Null(torso.ParentName);
        Assert.False(torso.IsSevered);
        Assert.False(torso.IsDestroyed);
        Assert.False(torso.IsDisabled);
        Assert.False(torso.IsFractured);
        Assert.False(torso.IsBleeding);

        // 头挂在颈下：ParentName 映射父部位名。
        PartStatus head = snap.Parts.Single(p => p.Name == HumanBody.Head);
        Assert.Equal(HumanBody.Neck, head.ParentName);
    }

    [Fact]
    public void FromBody_ReflectsDamageEffects()
    {
        Body body = FreshBody();
        // 左手切除（连带无后代）+ 右上臂骨折 + 左上臂流血。
        body.Sever(HumanBody.LeftHand);
        body.MarkFractured(HumanBody.RightArm);
        body.RegisterBleed(HumanBody.LeftArm);

        PawnInspection snap = PawnInspection.FromBody(body, null, System.Array.Empty<ArmorLayer>(), "x");

        PartStatus leftHand = snap.Parts.Single(p => p.Name == HumanBody.LeftHand);
        Assert.True(leftHand.IsSevered);
        Assert.True(leftHand.IsDisabled);

        Assert.True(snap.Parts.Single(p => p.Name == HumanBody.RightArm).IsFractured);
        Assert.True(snap.Parts.Single(p => p.Name == HumanBody.LeftArm).IsBleeding);
    }

    [Fact]
    public void FromBody_MapsWeaponAndArmor()
    {
        Body body = FreshBody();
        Weapon pistol = CombatData.Pistol();
        var armor = CombatData.SurvivorArmor();

        PawnInspection snap = PawnInspection.FromBody(body, pistol, armor, "x");

        Assert.NotNull(snap.Weapon);
        Assert.Equal("手枪", snap.Weapon!.Name);
        Assert.Equal(pistol.DamageMin, snap.Weapon.DamageMin);
        Assert.Equal(pistol.DamageMax, snap.Weapon.DamageMax);
        Assert.Equal(pistol.Penetration, snap.Weapon.Penetration);
        Assert.True(snap.Weapon.IsRanged);
        Assert.False(snap.Weapon.TwoHanded);
        Assert.Equal(pistol.AttackInterval, snap.Weapon.AttackInterval);

        Assert.Equal(2, snap.Armor.Count);
        ArmorInfo jacket = snap.Armor.Single(a => a.Name == "皮夹克");
        Assert.Equal(6, jacket.SharpDefense);
        Assert.Equal(3, jacket.BluntDefense);
        Assert.Equal(ArmorSlot.Outer, jacket.Slot);
    }

    [Fact]
    public void FromBody_NullWeapon_YieldsNullWeaponInfo()
    {
        Body body = FreshBody();
        PawnInspection snap = PawnInspection.FromBody(body, null, System.Array.Empty<ArmorLayer>(), "x");
        Assert.Null(snap.Weapon);
        Assert.Empty(snap.Armor);
    }

    [Fact]
    public void Snapshot_IsDetachedFromLiveBody()
    {
        Body body = FreshBody();
        PawnInspection snap = PawnInspection.FromBody(body, null, System.Array.Empty<ArmorLayer>(), "x");

        double torsoHpBefore = snap.Parts.Single(p => p.Name == HumanBody.Torso).Hp;
        // 快照拍完后重创躯干：快照里的旧值不应变化。
        body.ApplyDamage(HumanBody.Torso, 20);

        Assert.Equal(torsoHpBefore, snap.Parts.Single(p => p.Name == HumanBody.Torso).Hp);
        Assert.True(body.HpOf(HumanBody.Torso) < torsoHpBefore);
    }
}
