using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class BloodTests
{
    [Fact]
    public void Bleed_TicksBloodPool_NotPartHp()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        double handHpBefore = body.HpOf(HumanBody.LeftHand);

        body.RegisterBleed(HumanBody.LeftHand);
        body.TickBleed(5); // 1 伤口 × 1.0 × 5s = 5 失血

        Assert.Equal(95, body.Blood, 9);       // 储血扣了
        Assert.Equal(handHpBefore, body.HpOf(HumanBody.LeftHand), 9); // 部位 HP 不变
    }

    [Fact]
    public void MultipleWounds_StackBleedRate()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(HumanBody.LeftHand);
        body.RegisterBleed(HumanBody.Chest);
        body.TickBleed(2); // 2 伤口 × 1.0 × 2s = 4

        Assert.Equal(96, body.Blood, 9);
    }

    [Fact]
    public void BloodTiers_TransitionAtThresholds()
    {
        var body = HumanBody.NewBody(); // 100
        Assert.Equal(BloodLossTier.None, body.BloodTier);

        body.LoseBlood(30); // 70% → 轻度
        Assert.Equal(BloodLossTier.Mild, body.BloodTier);

        body.LoseBlood(25); // 45% → 中度
        Assert.Equal(BloodLossTier.Moderate, body.BloodTier);

        body.LoseBlood(25); // 20% → 重度昏迷
        Assert.Equal(BloodLossTier.Severe, body.BloodTier);
        Assert.True(body.IsUnconscious);
        Assert.False(body.IsDead);
    }

    [Fact]
    public void BloodPool_ToZero_BleedsToDeath()
    {
        var body = HumanBody.NewBody();
        body.LoseBlood(100);
        Assert.Equal(0, body.Blood, 9);
        Assert.True(body.BledOut);
        Assert.True(body.IsDead);
        Assert.Equal(BloodLossTier.Dead, body.BloodTier);
        Assert.False(body.IsUnconscious); // 已死不算昏迷
    }

    [Fact]
    public void StopBleed_HaltsWound()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(HumanBody.LeftHand);
        body.StopBleed(HumanBody.LeftHand);
        body.TickBleed(10);
        Assert.Equal(100, body.Blood, 9); // 止血后不再失血
    }

    [Fact]
    public void SharpHit_RegistersBleedingWound_OnBody()
    {
        var body = HumanBody.NewBody();
        var res = new CombatResult
        {
            HitPart = body.Parts[HumanBody.LeftHand],
            FinalDamage = 5,
            FinalDamageType = DamageType.Sharp,
            InitialAttackRoll = 6,
        };
        var rng = new SequenceRandomSource(0.0); // 流血必触发
        new CombatEffectResolver(rng).Apply(body, new Weapon { DamageType = DamageType.Sharp }, res);

        Assert.True(body.BleedingWoundCount > 0);
        Assert.Contains(HumanBody.LeftHand, body.BleedingWounds);
    }
}
