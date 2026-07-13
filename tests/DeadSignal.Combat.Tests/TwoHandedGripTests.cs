using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 持握三态 + 双手奖励 + 远程枪托近战 profile（设计文档 §5：单手/双手之分；双持惩罚）。
/// 用户拍板口径：单手持单武器=基线；双手持一把武器=攻速 +15%；双持两把单手武器=攻速×0.70 + 远程误差角×1.25。
/// 数值（+15% / ×0.70 / ×1.25 / 枪托值）为原型期拟定待调。
/// </summary>
public class TwoHandedGripTests
{
    // ---- DualWield：双手奖励常量 + 三态纯函数 ----

    [Fact]
    public void TwoHandedBonus_IsFifteenPercentFaster()
    {
        Assert.Equal(1.15, DualWield.TwoHandedSpeedBonus, 9);
    }

    [Fact]
    public void GripSpeedFactor_ThreeStates()
    {
        Assert.Equal(1.00, DualWield.GripSpeedFactor(GripMode.OneHanded), 9); // 基线
        Assert.Equal(1.15, DualWield.GripSpeedFactor(GripMode.TwoHanded), 9); // 双手 +15%（更快）
        Assert.Equal(0.70, DualWield.GripSpeedFactor(GripMode.DualWield), 9); // 双持 ×0.70（更慢）
    }

    [Fact]
    public void GripInterval_TwoHandedShortens_DualLengthens()
    {
        Assert.Equal(1.0, DualWield.EffectiveGripInterval(1.0, GripMode.OneHanded), 9);
        Assert.Equal(1.0 / 1.15, DualWield.EffectiveGripInterval(1.0, GripMode.TwoHanded), 9);
        Assert.Equal(1.0 / 0.70, DualWield.EffectiveGripInterval(1.0, GripMode.DualWield), 9);
    }

    [Fact]
    public void TwoHandedBonusAndDualPenalty_AreMutuallyExclusive()
    {
        // 持握态是单一枚举值——不可能既双持又双手；且两者方向相反（一快一慢）。
        double twoHanded = DualWield.GripSpeedFactor(GripMode.TwoHanded);
        double dual = DualWield.GripSpeedFactor(GripMode.DualWield);
        Assert.True(twoHanded > 1.0, "双手是加速");
        Assert.True(dual < 1.0, "双持是减速");
        Assert.NotEqual(twoHanded, dual);
    }

    // ---- 远程枪托近战 profile ----

    [Fact]
    public void RangedWeapon_HasBluntStockMeleeProfile()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.True(rifle.HasMeleeProfile);

        Weapon? melee = rifle.MeleeProfile();
        Assert.NotNull(melee);
        Assert.Equal(DamageType.Blunt, melee!.DamageType);   // 枪托是钝击
        Assert.False(melee.IsRanged);                        // 贴脸近战：必中、无误差角
        Assert.True(melee.DamageMax < rifle.DamageMax);      // 枪托伤害远低于开火
        Assert.True(melee.Penetration < rifle.Penetration);  // 穿透低
        Assert.Equal(rifle.TwoHanded, melee.TwoHanded);      // 单双手语义沿用本武器
    }

    [Fact]
    public void MeleeWeapon_HasNoStockProfile()
    {
        Assert.False(WeaponTable.Dagger().HasMeleeProfile);
        Assert.Null(WeaponTable.Dagger().MeleeProfile());
    }

    [Fact]
    public void AllRangedWeapons_HaveStockProfile()
    {
        // ⚠ 收窄：不是所有远程武器都有"枪托"——**弓/弩没有枪托**（HasMeleeProfile=false，这是对的：
        // 弓身砸人不是"枪托钝击"这套机制）。故本不变式只约束**枪械**。
        // 判据走 Archery.UsesArrows（按弹药类别键，吃箭的即弓弩）而非硬编码武器名：
        // 「自制弓」已按用户拍板改名「短弓」并扩成 8 把弓弩，按名字排除会随每次改名/增弓而失效。
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.IsRanged && !Archery.UsesArrows(w)))
        {
            Assert.True(w.HasMeleeProfile, $"{w.Name} 枪械应有枪托近战 profile");
            Assert.Equal(DamageType.Blunt, w.MeleeProfile()!.DamageType);
        }
    }

    // ---- WeaponTable：单双手标记 ----

    [Fact]
    public void LongGunsAndHeavyMelee_AreTwoHanded()
    {
        Assert.True(WeaponTable.Rifle().TwoHanded);
        Assert.True(WeaponTable.SniperRifle().TwoHanded);
        Assert.True(WeaponTable.Smg().TwoHanded);
        Assert.True(WeaponTable.Greatsword().TwoHanded);
        Assert.True(WeaponTable.Longsword().TwoHanded);
        Assert.True(WeaponTable.Warhammer().TwoHanded);
    }

    [Fact]
    public void SidearmsAndLightMelee_AreOneHanded()
    {
        Assert.False(WeaponTable.Dagger().TwoHanded);
        Assert.False(WeaponTable.Pistol().TwoHanded);
        Assert.True(WeaponTable.ImprovisedHuntingGun().TwoHanded);   // 自制猎枪＝长枪，双手
        Assert.False(WeaponTable.Shortsword().TwoHanded);
        Assert.False(WeaponTable.Club().TwoHanded);
    }

    // ---- Duel：三态接入出手节奏（仿 OperationPenaltyDuelTests）----

    private static Weapon Blade() => new()
    {
        Name = "刃", DamageMin = 4, DamageMax = 10, Penetration = 0.09,
        DamageType = DamageType.Sharp, AttackInterval = 0.7,
    };

    private static DuelFighter Dummy() => new()
    {
        Name = "B",
        Weapons = System.Array.Empty<WeaponMount>(),
        Armor = System.Array.Empty<ArmorLayer>(),
    };

    private static DuelFighter AttackerWith(GripMode grip) => new()
    {
        Name = "A",
        Grip = grip,
        Weapons = new[] { new WeaponMount { Weapon = Blade() } },
        Armor = System.Array.Empty<ArmorLayer>(),
    };

    private static IReadOnlyList<double> AttackTimes(DuelResult r, string attacker) =>
        r.Events.Where(e => e.Attacker == attacker && e.Weapon.Length > 0).Select(e => e.Time).ToList();

    private static double FirstInterval(GripMode grip)
    {
        var r = new DuelEngine(new SystemRandomSource(7)).Run(AttackerWith(grip), Dummy());
        var times = AttackTimes(r, "A");
        Assert.True(times.Count >= 2, "应至少出手两次以观测间隔");
        return times[1] - times[0];
    }

    [Fact]
    public void Duel_OneHanded_KeepsBaseInterval()
    {
        Assert.Equal(0.7, FirstInterval(GripMode.OneHanded), 6);
    }

    [Fact]
    public void Duel_TwoHanded_FifteenPercentFaster()
    {
        Assert.Equal(0.7 / 1.15, FirstInterval(GripMode.TwoHanded), 6);
    }

    [Fact]
    public void Duel_DualWield_SeventyPercentSpeed()
    {
        Assert.Equal(0.7 / 0.70, FirstInterval(GripMode.DualWield), 6);
    }

    [Fact]
    public void Duel_LegacyDualWieldingBool_StillWorks()
    {
        // 向后兼容：旧 API DualWielding=true 仍等价于双持（Sim/DuelReport 沿用）。
        var attacker = new DuelFighter
        {
            Name = "A",
            DualWielding = true,
            Weapons = new[] { new WeaponMount { Weapon = Blade() } },
            Armor = System.Array.Empty<ArmorLayer>(),
        };
        var r = new DuelEngine(new SystemRandomSource(7)).Run(attacker, Dummy());
        var times = AttackTimes(r, "A");
        Assert.True(times.Count >= 2);
        Assert.Equal(0.7 / 0.70, times[1] - times[0], 6);
    }
}
