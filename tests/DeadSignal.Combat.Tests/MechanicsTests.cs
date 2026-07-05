using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class BallisticsTests
{
    [Fact]
    public void ZeroSpread_AlwaysDeadCenter_NoRoll()
    {
        var rng = new SequenceRandomSource(); // 不应消耗
        Assert.Equal(0, Ballistics.SampleDeflectionDegrees(0, rng));
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void Spread_SampleWithinCone()
    {
        var rng = new SequenceRandomSource(3.0); // 落在 [-5,5]
        Assert.Equal(3.0, Ballistics.SampleDeflectionDegrees(5, rng), 9);
    }

    [Fact]
    public void ShotDirection_AimPlusDeflection()
    {
        var rng = new SequenceRandomSource(-2.0);
        Assert.Equal(88.0, Ballistics.SampleShotDirectionDegrees(90, 5, rng), 9);
    }
}

public class DualWieldTests
{
    [Fact]
    public void AttackRate_DualWieldIsSeventyPercent()
    {
        Assert.Equal(7.0, DualWield.EffectiveAttackRate(10, dualWielding: true), 9);
        Assert.Equal(10.0, DualWield.EffectiveAttackRate(10, dualWielding: false), 9);
    }

    [Fact]
    public void Interval_DualWieldLengthens()
    {
        Assert.Equal(1.0 / 0.7, DualWield.EffectiveAttackInterval(1.0, dualWielding: true), 9);
    }

    [Fact]
    public void Spread_OnlyRangedDualWieldGetsIncrease()
    {
        Assert.Equal(3.75, DualWield.EffectiveSpreadDegrees(3.0, dualWielding: true, ranged: true), 9); // ×1.25
        Assert.Equal(3.0, DualWield.EffectiveSpreadDegrees(3.0, dualWielding: true, ranged: false), 9); // 近战不变
        Assert.Equal(3.0, DualWield.EffectiveSpreadDegrees(3.0, dualWielding: false, ranged: true), 9); // 单持不变
    }
}

public class LoadoutTests
{
    [Fact]
    public void UnderFiftyPercent_NoPenalty()
    {
        double cap = 100;
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(40, cap));
        Assert.Equal(1.0, Loadout.SpeedMultiplier(40, cap), 9);
        Assert.Equal(1.0, Loadout.SpeedMultiplier(50, cap), 9); // 边界
    }

    [Fact]
    public void FiftyToHundred_LinearPenalty()
    {
        double cap = 100;
        Assert.Equal(LoadoutTier.Encumbered, Loadout.TierOf(75, cap));
        Assert.Equal(0.85, Loadout.SpeedMultiplier(75, cap), 9);  // 半程线性
        Assert.Equal(0.70, Loadout.SpeedMultiplier(100, cap), 9); // 满负重
    }

    [Fact]
    public void OverHundred_SteepPenalty_Floored()
    {
        double cap = 100;
        Assert.Equal(LoadoutTier.Overloaded, Loadout.TierOf(150, cap));
        Assert.Equal(0.30, Loadout.SpeedMultiplier(150, cap), 9); // 0.7 - 0.5*0.8
        Assert.Equal(0.10, Loadout.SpeedMultiplier(1000, cap), 9); // 下限
    }

    [Fact]
    public void CapacityFromStrength_Scales()
    {
        Assert.Equal(60, Loadout.CapacityFromStrength(10), 9);
    }
}
