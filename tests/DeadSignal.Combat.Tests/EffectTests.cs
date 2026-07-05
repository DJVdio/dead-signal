using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class EffectTests
{
    private static readonly Weapon SharpW = new() { Name = "锐", DamageType = DamageType.Sharp };
    private static readonly Weapon BluntW = new() { Name = "钝", DamageType = DamageType.Blunt };

    private static CombatResult Hit(Body body, string part, int dmg, DamageType finalType, double initialRoll)
        => new()
        {
            HitPart = body.Parts[part],
            FinalDamage = dmg,
            FinalDamageType = finalType,
            InitialAttackRoll = initialRoll,
            Terminated = dmg == 0,
        };

    // ---- 切除 ----

    [Fact]
    public void Sever_WhenSingleHitMeetsMaxHp()
    {
        var body = HumanBody.NewBody();
        var rng = new SequenceRandomSource(); // 切除为阈值判定，不耗 roll
        var res = Hit(body, HumanBody.LeftHand, dmg: 10, DamageType.Sharp, initialRoll: 12); // maxHp 10

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Contains(HumanBody.LeftHand, outcome.SeveredParts);
        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Sever);
        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Bleed); // 切除附带出血
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void Sever_WhenHittingAlreadyZeroHpPart()
    {
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.RightHand, 10); // 打到 0
        var rng = new SequenceRandomSource();
        var res = Hit(body, HumanBody.RightHand, dmg: 1, DamageType.Sharp, initialRoll: 3);

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Contains(HumanBody.RightHand, outcome.SeveredParts);
    }

    [Fact]
    public void Sever_OnVitalPart_CausesDeath()
    {
        var body = HumanBody.NewBody();
        var rng = new SequenceRandomSource();
        var res = Hit(body, HumanBody.Head, dmg: 25, DamageType.Sharp, initialRoll: 30);

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.True(outcome.CausedDeath);
        Assert.True(body.IsDead);
    }

    // ---- 流血 ----

    [Fact]
    public void Bleed_FiresWhenRollBelowProbability()
    {
        var body = HumanBody.NewBody();
        // 手 maxHp10, dmg5 → p = clamp(1.0*5/10, .9) = 0.5
        var res = Hit(body, HumanBody.LeftHand, dmg: 5, DamageType.Sharp, initialRoll: 6);
        var rng = new SequenceRandomSource(0.4); // 0.4 < 0.5 → 触发
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);
        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Bleed);
    }

    [Fact]
    public void Bleed_SkipsWhenRollAboveProbability()
    {
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftHand, dmg: 5, DamageType.Sharp, initialRoll: 6);
        var rng = new SequenceRandomSource(0.6); // 0.6 >= 0.5 → 不触发
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Bleed);
    }

    [Fact]
    public void ConvertedSharp_ProducesNoEffectsAtAll()
    {
        // 用户终裁：锐器被护甲降解成钝伤后（FinalDamageType=Blunt）无任何状态效果——
        // 不流血、不切除、不震荡、不骨折，只造成伤害。且不消耗任何效果 roll。
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftHand, dmg: 5, DamageType.Blunt, initialRoll: 12);
        var rng = new SequenceRandomSource(); // 一个 roll 都不该被取
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Empty(outcome.Effects);
        Assert.Empty(outcome.SeveredParts);
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void ConvertedSharp_HeadHit_NoConcussionNoFracture()
    {
        // team-lead 指定：锐转钝命中头部 → 不触发震荡/骨折（终裁下连流血/切除也不触发）
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.Head, dmg: 10, DamageType.Blunt, initialRoll: 20);
        var rng = new SequenceRandomSource(); // 无任何 roll
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Concussion);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Bleed);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Sever);
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void NoSever_WhenZeroHpPartFullyBlocked()
    {
        // 用户口径：0HP 部位被护甲全额防住（dmg=0）→ 不切除，部位保住
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 10); // 打到 0
        var res = Hit(body, HumanBody.LeftHand, dmg: 0, DamageType.Sharp, initialRoll: 8); // 被挡下
        var rng = new SequenceRandomSource();
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Sever);
        Assert.False(body.IsSevered(HumanBody.LeftHand));
        Assert.Empty(outcome.SeveredParts);
        Assert.Equal(0, rng.Remaining); // dmg=0：无流血/切除 roll
    }

    [Fact]
    public void Sever_WhenZeroHpPartTakesAnyPenetratingDamage()
    {
        // 0HP 部位 + 穿甲造成任意 >0 伤害 → 切除
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 10); // 打到 0
        var res = Hit(body, HumanBody.LeftHand, dmg: 2, DamageType.Sharp, initialRoll: 5);
        var rng = new SequenceRandomSource();
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Contains(HumanBody.LeftHand, outcome.SeveredParts);
    }

    // ---- 震荡 ----

    [Fact]
    public void Concussion_NativeBlunt_FiresEvenWhenFullyBlocked()
    {
        var body = HumanBody.NewBody();
        // 被甲完全挡下：dmg=0，但初始 roll 20 仍驱动震荡。头 maxHp25 → p=clamp(0.9*20/25,.85)=0.72
        var res = Hit(body, HumanBody.Head, dmg: 0, DamageType.Sharp, initialRoll: 20);
        var rng = new SequenceRandomSource(0.5); // 0.5 < 0.72 → 触发
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Concussion);
    }

    [Fact]
    public void Concussion_OnlyHeadAndTorso_NotLimbs()
    {
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftHand, dmg: 8, DamageType.Blunt, initialRoll: 20);
        var rng = new SequenceRandomSource(0.99, 0.0); // 手不震荡；仅骨折 roll 会被消耗
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Concussion);
        Assert.Equal(1, rng.Remaining); // 只消耗了骨折 roll，没有震荡 roll
    }

    [Fact]
    public void Concussion_Torso_LowerProbabilityThanHead()
    {
        // 同样 initialRoll，躯干概率应远低于头
        double headP = 0.9 * 20 / 25;   // 0.72
        double torsoP = 0.25 * 20 / 55; // 0.09
        Assert.True(torsoP < headP);

        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.Torso, dmg: 0, DamageType.Sharp, initialRoll: 20);
        var rng = new SequenceRandomSource(0.10); // 0.10 >= 0.09 → 躯干不震荡
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Concussion);
    }

    // ---- 骨折 ----

    [Fact]
    public void Fracture_NativeBlunt_OnDamage()
    {
        var body = HumanBody.NewBody();
        // 腿 maxHp22, dmg11 → p=clamp(0.8*11/22,.6)=0.4
        var res = Hit(body, HumanBody.LeftLeg, dmg: 11, DamageType.Blunt, initialRoll: 12);
        var rng = new SequenceRandomSource(0.3); // 腿非震荡部位 → 仅骨折 roll；0.3<0.4 触发
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
    }

    [Fact]
    public void Fracture_SkippedWhenNoDamage()
    {
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftLeg, dmg: 0, DamageType.Blunt, initialRoll: 12);
        var rng = new SequenceRandomSource(); // 无伤 → 骨折不 roll
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
        Assert.Equal(0, rng.Remaining);
    }

    // ---- 0HP 部位 MaxHp 磨损（非状态效果，不走概率门控）----

    [Fact]
    public void DegradedBlunt_ErodesMaxHp_OnZeroHpPart()
    {
        // 锐转钝的降解钝伤（武器锐器、FinalDamageType=Blunt）打 0HP 部位 → 磨损上限，不切除
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 10); // 手打到 0，MaxHp 仍 10
        var res = Hit(body, HumanBody.LeftHand, dmg: 3, DamageType.Blunt, initialRoll: 5);
        var rng = new SequenceRandomSource(); // 降解锐器无任何 roll（不流血/切除/震荡/骨折）

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Equal(3, outcome.MaxHpEroded, 9);
        Assert.Equal(HumanBody.LeftHand, outcome.ErodedPart);
        Assert.Equal(7, body.MaxHpOf(HumanBody.LeftHand), 9);
        Assert.False(body.IsGone(HumanBody.LeftHand));
        Assert.Empty(outcome.SeveredParts);
        Assert.Empty(outcome.DestroyedParts);
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void NativeBlunt_ErodesMaxHp_OnZeroHpPart()
    {
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 10);
        var res = Hit(body, HumanBody.LeftHand, dmg: 4, DamageType.Blunt, initialRoll: 6);
        var rng = new SequenceRandomSource(0.99); // 天然钝器仍 roll 骨折（不触发），磨损为确定性

        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.Equal(4, outcome.MaxHpEroded, 9);
        Assert.Equal(6, body.MaxHpOf(HumanBody.LeftHand), 9);
    }

    [Fact]
    public void NoErosion_WhenZeroHpPartFullyBlocked()
    {
        // 0HP 部位被护甲全额防住（dmg=0）→ 不磨损，与切除豁免同构
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 10);
        var res = Hit(body, HumanBody.LeftHand, dmg: 0, DamageType.Blunt, initialRoll: 8);
        var rng = new SequenceRandomSource();

        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.Equal(0, outcome.MaxHpEroded, 9);
        Assert.Equal(10, body.MaxHpOf(HumanBody.LeftHand), 9); // 上限不变
        Assert.False(body.IsDestroyed(HumanBody.LeftHand));
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void Erosion_ToZero_DestroysPart_WithDescendants()
    {
        // 上限磨损归 0 → 部位永久损毁，连带后代
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftArm, 18); // 上臂打到 0，MaxHp 18
        var res = Hit(body, HumanBody.LeftArm, dmg: 18, DamageType.Blunt, initialRoll: 20);
        var rng = new SequenceRandomSource();

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res); // 降解钝伤

        Assert.Equal(0, body.MaxHpOf(HumanBody.LeftArm), 9);
        Assert.True(body.IsDestroyed(HumanBody.LeftArm));
        Assert.Contains(HumanBody.LeftArm, outcome.DestroyedParts);
        Assert.Contains(HumanBody.LeftHand, outcome.DestroyedParts); // 连带下游
        Assert.False(outcome.CausedDeath); // 四肢损毁非致死
    }

    [Fact]
    public void SharpUndegraded_OnZeroHpPart_SeversNotErodes()
    {
        // 锐器（未降解）对 0HP 部位造成实质伤害 → 仍走切除，不磨损
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 10);
        var res = Hit(body, HumanBody.LeftHand, dmg: 2, DamageType.Sharp, initialRoll: 5);
        var rng = new SequenceRandomSource();

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Contains(HumanBody.LeftHand, outcome.SeveredParts);
        Assert.Empty(outcome.DestroyedParts);
        Assert.Equal(0, outcome.MaxHpEroded, 9);
    }
}
