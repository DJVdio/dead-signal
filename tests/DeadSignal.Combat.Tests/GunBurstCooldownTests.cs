using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 枪械攻击模型：冷却→射击→冷却→射击（起手先满冷却，首发也要过一次冷却）+ 连发（一次射击 = BurstCount 发）。
/// 用不还手、扛得住的靶子隔离出纯粹的时序/连发节奏。
/// </summary>
public class GunBurstCooldownTests
{
    // 低伤武器：三连发内也打不死靶子，便于观测完整时序。
    private static Weapon Popgun(double interval, int burst, double burstGap) => new()
    {
        Name = "测试枪", DamageMin = 1, DamageMax = 1, Penetration = 0,
        DamageType = DamageType.Sharp, IsRanged = true,
        AttackInterval = interval, BurstCount = burst, BurstInterval = burstGap,
    };

    private static DuelFighter Attacker(Weapon w) => new()
    {
        Name = "A",
        Weapons = new[] { new WeaponMount { Weapon = w } },
        Armor = System.Array.Empty<ArmorLayer>(),
    };

    // 无武器、扛揍的靶子（大量储血，短时间内不会流血致死，隔离时序观测）。
    private static DuelFighter Dummy() => new()
    {
        Name = "B",
        Weapons = System.Array.Empty<WeaponMount>(),
        Armor = System.Array.Empty<ArmorLayer>(),
    };

    private static System.Collections.Generic.IReadOnlyList<double> AttackTimes(DuelResult r) =>
        r.Events.Where(e => e.Attacker == "A" && e.Weapon.Length > 0).Select(e => e.Time).ToList();

    [Fact]
    public void CooldownFirst_FirstShotAfterOneCooldown_NotInstant()
    {
        // 单发、间隔 1.0：起手先满冷却 → 首发在 t≈1.0，而非 t=0。
        var r = new DuelEngine(new SystemRandomSource(7)).Run(Attacker(Popgun(1.0, 1, 0)), Dummy());
        var times = AttackTimes(r);
        Assert.True(times.Count >= 2, "应至少出手两次");
        Assert.Equal(1.0, times[0], 6);   // 冷却前置：首发非瞬发
        Assert.Equal(2.0, times[1], 6);   // 之后每个冷却一发
    }

    [Fact]
    public void Burst_ThreeRoundsPerTrigger_ThenCooldown()
    {
        // 三连发、冷却 1.0、连发内间隔 0.1：
        // 冷却→[1.0, 1.1, 1.2]三连发→冷却→[2.2, 2.3, 2.4]……
        var r = new DuelEngine(new SystemRandomSource(7)).Run(Attacker(Popgun(1.0, 3, 0.1)), Dummy());
        var times = AttackTimes(r);
        Assert.True(times.Count >= 6, "两轮连发应至少 6 发");
        Assert.Equal(1.0, times[0], 6);
        Assert.Equal(1.1, times[1], 6);
        Assert.Equal(1.2, times[2], 6);
        // 冷却在整轮连发之后：末发 1.2 + 冷却 1.0 = 2.2 起下一轮
        Assert.Equal(2.2, times[3], 6);
        Assert.Equal(2.3, times[4], 6);
        Assert.Equal(2.4, times[5], 6);
    }

    [Fact]
    public void SingleShot_DefaultBurstCountOne()
    {
        var w = Popgun(1.0, 1, 0);
        Assert.Equal(1, w.BurstCount);
        // 默认单发武器 BurstCount=1（Weapon 默认值）
        Assert.Equal(1, new Weapon { Name = "x" }.BurstCount);
    }

    [Fact]
    public void Smg_IsThreeRoundBurst_OthersSingle()
    {
        Assert.Equal(3, WeaponTable.Smg().BurstCount);
        Assert.True(WeaponTable.Smg().BurstInterval > 0);
        Assert.Equal(1, WeaponTable.Rifle().BurstCount);
        Assert.Equal(1, WeaponTable.Pistol().BurstCount);
        Assert.Equal(1, WeaponTable.Zipgun().BurstCount);
    }
}
