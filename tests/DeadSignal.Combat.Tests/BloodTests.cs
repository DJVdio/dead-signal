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

        body.RegisterBleed(HumanBody.LeftHand, BleedModel.BleedSeverity.Medium);
        body.TickBleed(5); // 手=轻微伤（权重 0.5）：1 伤口 × 0.5 × 1.0 × 5s = 2.5 失血

        // 断言**失血量**而非绝对血量：储血上限是可调数值（BleedModel.DefaultBloodMax），
        // 写死 100 只是碰巧和当时的默认值一样，上限一改就假红。
        Assert.Equal(2.5, body.BloodMax - body.Blood, 9);     // 储血扣了
        Assert.Equal(handHpBefore, body.HpOf(HumanBody.LeftHand), 9); // 部位 HP 不变
    }

    [Fact]
    public void MultipleWounds_StackBleedRate()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(HumanBody.LeftHand, BleedModel.BleedSeverity.Medium); // 轻微（权重 0.5）
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);    // 致命（权重 1.0）
        body.TickBleed(2); // (0.5 + 1.0) × 1.0 × 2s = 3

        // 伤口叠加，但按部位分级加权——胸口的深伤口比手上的划伤放血快一倍。
        Assert.Equal(3, body.BloodMax - body.Blood, 9);
    }

    [Fact]
    public void BloodTiers_TransitionAtThresholds()
    {
        // 分级是**比例**阈值（75/50/25%），故按 BloodMax 的比例扣，不写死绝对值。
        var body = HumanBody.NewBody();
        double max = body.BloodMax;
        Assert.Equal(BloodLossTier.None, body.BloodTier);

        body.LoseBlood(max * 0.30); // 剩 70% → 轻度
        Assert.Equal(BloodLossTier.Mild, body.BloodTier);

        body.LoseBlood(max * 0.25); // 剩 45% → 中度
        Assert.Equal(BloodLossTier.Moderate, body.BloodTier);

        body.LoseBlood(max * 0.25); // 剩 20% → 重度昏迷
        Assert.Equal(BloodLossTier.Severe, body.BloodTier);
        Assert.True(body.IsUnconscious);
        Assert.False(body.IsDead);
    }

    [Fact]
    public void BloodPool_ToZero_BleedsToDeath()
    {
        var body = HumanBody.NewBody();
        body.LoseBlood(body.BloodMax);
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
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.StopBleed(HumanBody.Chest);
        body.TickBleed(10);
        Assert.Equal(body.BloodMax, body.Blood, 9); // 止血后不再失血（一滴没掉）
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
