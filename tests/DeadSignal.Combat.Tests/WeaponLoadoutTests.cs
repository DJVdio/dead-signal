using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 武器持械模型（左右手 → GripMode）。用户拍板口径：
/// 两把单手 = 双持；一把双手武器占两手；一把单手 = 单手；一把单手改双手握 = 双手（**无攻速加成**）。断手 → 该手不能持械。
/// </summary>
public class WeaponLoadoutTests
{
    private static Weapon OneHandDW() => new()
    {
        Name = "匕首", DamageMin = 3, DamageMax = 7, DamageType = DamageType.Sharp,
        TwoHanded = false, CanDualWield = true, AttackInterval = 0.5,
    };

    private static Weapon OneHandNoDW() => new()
    {
        Name = "警棍", DamageMin = 2, DamageMax = 6, DamageType = DamageType.Blunt,
        TwoHanded = false, CanDualWield = false, AttackInterval = 0.6,
    };

    private static Weapon Pistol() => new()
    {
        Name = "手枪", DamageMin = 6, DamageMax = 12, DamageType = DamageType.Sharp,
        TwoHanded = false, CanDualWield = true, IsRanged = true, AttackInterval = 0.4,
    };

    private static Weapon TwoHand() => new()
    {
        Name = "消防斧", DamageMin = 10, DamageMax = 20, DamageType = DamageType.Sharp,
        TwoHanded = true, CanDualWield = false, AttackInterval = 1.2,
    };

    // ---- 空手 ----

    [Fact]
    public void Empty_IsUnarmed_OneHandedGrip_NoPrimary()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.IsUnarmed);
        Assert.Equal(GripMode.OneHanded, lo.Grip);
        Assert.Null(lo.PrimaryWeapon);
    }

    // ---- 单手 ----

    [Fact]
    public void SingleOneHand_IsOneHanded()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(OneHandDW(), Hand.Right));
        Assert.Equal(GripMode.OneHanded, lo.Grip);
        Assert.False(lo.IsUnarmed);
        Assert.False(lo.TwoHandGrip);
        Assert.Equal("匕首", lo.PrimaryWeapon!.Name);
    }

    // ---- 双持：两把单手 ----

    [Fact]
    public void TwoOneHand_IsDualWield_RightIsPrimary()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(OneHandDW(), Hand.Left));
        Assert.True(lo.EquipToHand(Pistol(), Hand.Right));
        Assert.Equal(GripMode.DualWield, lo.Grip);
        Assert.False(lo.TwoHandGrip);
        Assert.Equal("手枪", lo.PrimaryWeapon!.Name); // 右手优先
    }

    [Fact]
    public void DualWield_RequiresBothCanDualWield()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(OneHandDW(), Hand.Left));
        // 警棍不可双持 → 拒绝，左手保留匕首，仍单手
        Assert.False(lo.EquipToHand(OneHandNoDW(), Hand.Right));
        Assert.Equal(GripMode.OneHanded, lo.Grip);
        Assert.Null(lo.RightHand);
        Assert.Equal("匕首", lo.LeftHand!.Name);
    }

    [Fact]
    public void NonDualWieldWeapon_AloneIsFineOneHanded()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(OneHandNoDW(), Hand.Right)); // 单独一把不可双持武器 → 单手合法
        Assert.Equal(GripMode.OneHanded, lo.Grip);
    }

    // ---- 双手武器占两手 ----

    [Fact]
    public void TwoHandedWeapon_OccupiesBothHands()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(TwoHand(), Hand.Right));
        Assert.Equal(GripMode.TwoHanded, lo.Grip);
        Assert.True(lo.TwoHandGrip);
        Assert.Same(lo.LeftHand, lo.RightHand);
        Assert.Equal("消防斧", lo.PrimaryWeapon!.Name);
    }

    [Fact]
    public void TwoHandedWeapon_ClearsOtherHand()
    {
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipToHand(OneHandDW(), Hand.Left));
        Assert.True(lo.EquipToHand(TwoHand(), Hand.Right)); // 双手武器抢占两手
        Assert.Equal(GripMode.TwoHanded, lo.Grip);
        Assert.Same(lo.LeftHand, lo.RightHand);
    }

    [Fact]
    public void EquipTwoHanded_OneHandWeaponGivesTwoHandedGrip()
    {
        // 单手武器改双手握 → 双手（持握态变了，但**无攻速加成**）
        var lo = new WeaponLoadout();
        Assert.True(lo.EquipTwoHanded(OneHandDW()));
        Assert.Equal(GripMode.TwoHanded, lo.Grip);
        Assert.True(lo.TwoHandGrip);
    }

    // ---- 断手 ----

    [Fact]
    public void AmputatedHand_RejectsEquip()
    {
        var lo = new WeaponLoadout(rightHandLost: true);
        Assert.False(lo.EquipToHand(OneHandDW(), Hand.Right));
        Assert.Null(lo.RightHand);
        Assert.True(lo.IsUnarmed);
    }

    [Fact]
    public void TwoHandedWeapon_RejectedWhenAnyHandLost()
    {
        var lo = new WeaponLoadout(leftHandLost: true);
        Assert.False(lo.EquipToHand(TwoHand(), Hand.Right));
        Assert.False(lo.EquipTwoHanded(TwoHand()));
        Assert.True(lo.IsUnarmed);
    }

    [Fact]
    public void NotifyHandLost_ClearsThatHand()
    {
        var lo = new WeaponLoadout();
        lo.EquipToHand(OneHandDW(), Hand.Left);
        lo.EquipToHand(Pistol(), Hand.Right);
        lo.NotifyHandLost(Hand.Right);
        Assert.True(lo.RightHandLost);
        Assert.Null(lo.RightHand);
        Assert.Equal("匕首", lo.LeftHand!.Name);
        Assert.Equal(GripMode.OneHanded, lo.Grip);
    }

    [Fact]
    public void NotifyHandLost_TwoHandGrip_DropsWholeWeapon()
    {
        var lo = new WeaponLoadout();
        lo.EquipToHand(TwoHand(), Hand.Right);
        lo.NotifyHandLost(Hand.Left);
        Assert.True(lo.IsUnarmed);
        Assert.False(lo.TwoHandGrip);
        Assert.Equal(GripMode.OneHanded, lo.Grip);
    }

    // ---- 卸下 ----

    [Fact]
    public void Unequip_SingleHand_ClearsOnlyThatHand()
    {
        var lo = new WeaponLoadout();
        lo.EquipToHand(OneHandDW(), Hand.Left);
        lo.EquipToHand(Pistol(), Hand.Right);
        lo.Unequip(Hand.Left);
        Assert.Null(lo.LeftHand);
        Assert.Equal("手枪", lo.RightHand!.Name);
        Assert.Equal(GripMode.OneHanded, lo.Grip);
    }

    [Fact]
    public void Unequip_TwoHandGrip_ClearsBothHands()
    {
        var lo = new WeaponLoadout();
        lo.EquipToHand(TwoHand(), Hand.Right);
        lo.Unequip(Hand.Right);
        Assert.True(lo.IsUnarmed);
        Assert.False(lo.TwoHandGrip);
    }
}
