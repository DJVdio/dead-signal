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
        var res = Hit(body, HumanBody.LeftHand, dmg: 16, DamageType.Sharp, initialRoll: 12); // maxHp 16

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
        body.ApplyDamage(HumanBody.RightHand, 16); // 打到 0
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
        // 手 maxHp16, dmg5 → p = clamp(1.0*5/16, .9) = 0.3125
        var res = Hit(body, HumanBody.LeftHand, dmg: 5, DamageType.Sharp, initialRoll: 6);
        var rng = new SequenceRandomSource(0.2); // 0.2 < 0.3125 → 触发
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);
        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Bleed);
    }

    [Fact]
    public void Bleed_SkipsWhenRollAboveProbability()
    {
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftHand, dmg: 5, DamageType.Sharp, initialRoll: 6);
        var rng = new SequenceRandomSource(0.6); // 0.6 >= 0.3125 → 不触发
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
        body.ApplyDamage(HumanBody.LeftHand, 16); // 打到 0
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
        body.ApplyDamage(HumanBody.LeftHand, 16); // 打到 0
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
        // 被甲完全挡下：dmg=0，但初始 roll 20 仍驱动震荡。头 maxHp16 → p=clamp(0.9*20/16,.85)=0.85（触顶）
        var res = Hit(body, HumanBody.Head, dmg: 0, DamageType.Sharp, initialRoll: 20);
        var rng = new SequenceRandomSource(0.5); // 0.5 < 0.85 → 触发
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
        double headP = 0.9 * 20 / 16;   // 1.125（未截顶原值；实际 cap .85）
        double torsoP = 0.25 * 20 / 28; // ≈0.1786
        Assert.True(torsoP < headP);

        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.Torso, dmg: 0, DamageType.Sharp, initialRoll: 20);
        var rng = new SequenceRandomSource(0.20); // 0.20 >= 0.1786 → 躯干不震荡
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Concussion);
    }

    // ---- 骨折 ----

    [Fact]
    public void Fracture_NativeBlunt_OnDamage()
    {
        var body = HumanBody.NewBody();
        // 腿 maxHp21, dmg11 → p=clamp(0.8*11/21,.6)≈0.419
        var res = Hit(body, HumanBody.LeftLeg, dmg: 11, DamageType.Blunt, initialRoll: 12);
        var rng = new SequenceRandomSource(0.3); // 腿非震荡部位 → 仅骨折 roll；0.3<0.419 触发
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
    }

    [Fact]
    public void Fracture_PersistsOnBody_ForHealthPanelQuery()
    {
        // 用户拍板：骨折要落到 Body 持久态，供角色面板"健康页签"查询（狠辣向健康展示，非平衡问题）。
        // 复现：一次会触发骨折的天然钝器命中后，Body 应能持久查询该部位已骨折。
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftLeg, dmg: 11, DamageType.Blunt, initialRoll: 12); // p≈0.419
        var rng = new SequenceRandomSource(0.3); // 0.3<0.419 → 触发骨折

        Assert.False(body.IsFractured(HumanBody.LeftLeg)); // 命中前未骨折

        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
        Assert.True(body.IsFractured(HumanBody.LeftLeg)); // 结算后持久标记
    }

    [Fact]
    public void HealFracture_ClearsFracture()
    {
        // 与 StopBleed 对称：骨折手术治愈后清除持久标记，部位不再显示骨折、可再次建档。
        var body = HumanBody.NewBody();
        body.MarkFractured(HumanBody.LeftLeg);
        Assert.True(body.IsFractured(HumanBody.LeftLeg));

        body.HealFracture(HumanBody.LeftLeg);

        Assert.False(body.IsFractured(HumanBody.LeftLeg));
        Assert.DoesNotContain(HumanBody.LeftLeg, body.FracturedParts);
    }

    [Fact]
    public void HealFracture_Idempotent_OnUnfracturedPart()
    {
        // 未骨折调用无副作用（幂等）。
        var body = HumanBody.NewBody();
        body.HealFracture(HumanBody.LeftLeg);
        Assert.False(body.IsFractured(HumanBody.LeftLeg));
        Assert.Empty(body.FracturedParts);
    }

    [Fact]
    public void HealFracture_UnknownPart_NoThrow()
    {
        // 部位名不存在安全处理，不影响既有骨折。
        var body = HumanBody.NewBody();
        body.MarkFractured(HumanBody.LeftLeg);
        body.HealFracture("no_such_part");
        Assert.True(body.IsFractured(HumanBody.LeftLeg));
    }

    [Fact]
    public void Fracture_NotMarked_WhenRollAboveProbability()
    {
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftLeg, dmg: 11, DamageType.Blunt, initialRoll: 12); // p≈0.419
        var rng = new SequenceRandomSource(0.99); // 0.99>=0.419 → 不触发
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
        Assert.False(body.IsFractured(HumanBody.LeftLeg));
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
        body.ApplyDamage(HumanBody.LeftHand, 16); // 手打到 0，MaxHp 仍 16
        var res = Hit(body, HumanBody.LeftHand, dmg: 3, DamageType.Blunt, initialRoll: 5);
        var rng = new SequenceRandomSource(); // 降解锐器无任何 roll（不流血/切除/震荡/骨折）

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Equal(3, outcome.MaxHpEroded, 9);
        Assert.Equal(HumanBody.LeftHand, outcome.ErodedPart);
        Assert.Equal(13, body.MaxHpOf(HumanBody.LeftHand), 9);
        Assert.False(body.IsGone(HumanBody.LeftHand));
        Assert.Empty(outcome.SeveredParts);
        Assert.Empty(outcome.DestroyedParts);
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void NativeBlunt_ErodesMaxHp_OnZeroHpPart()
    {
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 16);
        var res = Hit(body, HumanBody.LeftHand, dmg: 4, DamageType.Blunt, initialRoll: 6);
        var rng = new SequenceRandomSource(0.99); // 天然钝器仍 roll 骨折（不触发），磨损为确定性

        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.Equal(4, outcome.MaxHpEroded, 9);
        Assert.Equal(12, body.MaxHpOf(HumanBody.LeftHand), 9);
    }

    [Fact]
    public void NoErosion_WhenZeroHpPartFullyBlocked()
    {
        // 0HP 部位被护甲全额防住（dmg=0）→ 不磨损，与切除豁免同构
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 16);
        var res = Hit(body, HumanBody.LeftHand, dmg: 0, DamageType.Blunt, initialRoll: 8);
        var rng = new SequenceRandomSource();

        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.Equal(0, outcome.MaxHpEroded, 9);
        Assert.Equal(16, body.MaxHpOf(HumanBody.LeftHand), 9); // 上限不变
        Assert.False(body.IsDestroyed(HumanBody.LeftHand));
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void Erosion_ToZero_DestroysPart_WithDescendants()
    {
        // 上限磨损归 0 → 部位永久损毁，连带后代
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftArm, 21); // 上臂打到 0，MaxHp 21
        var res = Hit(body, HumanBody.LeftArm, dmg: 21, DamageType.Blunt, initialRoll: 20);
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
        body.ApplyDamage(HumanBody.LeftHand, 16);
        var res = Hit(body, HumanBody.LeftHand, dmg: 2, DamageType.Sharp, initialRoll: 5);
        var rng = new SequenceRandomSource();

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Contains(HumanBody.LeftHand, outcome.SeveredParts);
        Assert.Empty(outcome.DestroyedParts);
        Assert.Equal(0, outcome.MaxHpEroded, 9);
    }
}
