using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 持握三态 + 远程枪托近战 profile（设计文档 §5：单手/双手之分；双持惩罚）。
/// 用户拍板口径：单手持单武器=基线；<b>双手持一把武器=同样是基线（无攻速加成）</b>；
/// 双持两把单手武器=攻速×0.70 + 远程误差角×1.25。
/// <para><b>双手握的 +15% 攻速加成已按用户口径删除</b>（旧 <c>DualWield.TwoHandedSpeedBonus</c> 已退役）：
/// 双手武器的代价（占满两只手、不能同时持光源/第二把武器）是<b>装备约束</b>，不再附带攻速回报。
/// 下面的测试就是钉死"双手握 == 单手握，攻速上一模一样"的护栏。</para>
/// 数值（×0.70 / ×1.25 / 枪托值）为原型期拟定待调。
/// </summary>
public class TwoHandedGripTests
{
    // ---- DualWield：三态纯函数（双手无加成）----

    [Fact]
    public void GripSpeedFactor_ThreeStates()
    {
        Assert.Equal(1.00, DualWield.GripSpeedFactor(GripMode.OneHanded), 9); // 基线
        Assert.Equal(1.00, DualWield.GripSpeedFactor(GripMode.TwoHanded), 9); // 双手：无加成，同基线
        Assert.Equal(0.70, DualWield.GripSpeedFactor(GripMode.DualWield), 9); // 双持 ×0.70（更慢）
    }

    [Fact]
    public void TwoHandedGrip_GivesNoAttackSpeedBonus()
    {
        // 本单的核心口径：双手握**不再**加攻速——系数与单手逐位相等，有效间隔 == 武器基础间隔。
        Assert.Equal(DualWield.GripSpeedFactor(GripMode.OneHanded),
            DualWield.GripSpeedFactor(GripMode.TwoHanded), 9);
        Assert.Equal(0.7, DualWield.EffectiveGripInterval(0.7, GripMode.TwoHanded), 9);
    }

    [Fact]
    public void GripInterval_TwoHandedUnchanged_DualLengthens()
    {
        Assert.Equal(1.0, DualWield.EffectiveGripInterval(1.0, GripMode.OneHanded), 9);
        Assert.Equal(1.0, DualWield.EffectiveGripInterval(1.0, GripMode.TwoHanded), 9); // 无加成
        Assert.Equal(1.0 / 0.70, DualWield.EffectiveGripInterval(1.0, GripMode.DualWield), 9);
    }

    [Fact]
    public void DualPenalty_IsTheOnlyGripSpeedModifier()
    {
        // 删掉双手加成后，持握态里**只剩双持这一个减速**——没有任何持握能把攻速抬到基线之上。
        foreach (GripMode grip in System.Enum.GetValues<GripMode>())
        {
            Assert.True(DualWield.GripSpeedFactor(grip) <= 1.0, $"{grip} 不该快于基线");
        }
        Assert.True(DualWield.GripSpeedFactor(GripMode.DualWield) < 1.0, "双持是减速");
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
    public void Duel_TwoHanded_KeepsBaseInterval_NoBonus()
    {
        // 双手握在 Duel 的出手节奏上也**与单手完全一致**（基础间隔 0.7s，不缩短）。
        Assert.Equal(0.7, FirstInterval(GripMode.TwoHanded), 6);
        Assert.Equal(FirstInterval(GripMode.OneHanded), FirstInterval(GripMode.TwoHanded), 6);
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
