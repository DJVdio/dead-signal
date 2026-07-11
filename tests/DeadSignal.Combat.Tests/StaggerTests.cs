using System.Collections.Generic;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 命中减速（通用，RimWorld stagger 式，用户口径「未破防也会短暂减速 是 所有角色都是这样的（参考rimworld）」）：
/// 任何攻击命中任何角色、**无论是否击穿护甲**都对目标施加很短暂的移动减速。
/// 与震荡严格区分：减速=轻量通用（只降移速；无攻击打断、无冷却清零、无概率 roll——命中即触发）；
/// 震荡=既有重效果不变。数值全部拟定待调（时长 ~1s、移速 ×0.6）。
///
/// 减速是移动层效果：对决引擎 <see cref="DuelEngine"/> 不建模位移，故减速对对决胜率零影响，
/// 引擎只在结算产物里透传数值（<see cref="EffectOutcome.StaggerSeconds"/> / <see cref="AttackOutcome.StaggerSeconds"/>），
/// 由 Godot 实时层（Actor._staggerTimer）消费到移速。
/// </summary>
public class StaggerTests
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

    [Fact]
    public void StaggerConfig_Default_IsLightUniversalSlow()
    {
        var d = EffectConfig.Default();
        Assert.True(d.StaggerSeconds > 0, "命中减速时长应为正（拟定 ~1s）");
        Assert.InRange(d.StaggerSpeedMult, 0.0, 1.0);
        // 减速比震荡轻：移速系数更高（=减速更弱）。震荡打断期 ×0.1，减速 ×0.6。
        Assert.True(d.StaggerSpeedMult > d.ConcussionMoveSlowFactor, "命中减速应弱于震荡打断的移速惩罚");
    }

    [Fact]
    public void Stagger_OnFullyBlockedHit_StillTriggers_NoRollConsumed()
    {
        // 被甲完全挡下（dmg=0）也减速：验证「未破防同样触发」，且不消耗任何 roll（命中即触发，非概率型）。
        var body = HumanBody.NewBody();
        var rng = new SequenceRandomSource(); // 空序列：减速不得取任何 roll（锐器 dmg=0 → 无流血/切除/震荡/骨折 roll）
        var res = Hit(body, HumanBody.LeftHand, dmg: 0, DamageType.Sharp, initialRoll: 5);

        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);

        Assert.Equal(EffectConfig.Default().StaggerSeconds, outcome.StaggerSeconds, 9);
        Assert.True(outcome.StaggerSeconds > 0);
        Assert.Equal(0, rng.Remaining); // 减速不耗 roll
    }

    [Fact]
    public void Stagger_OnPenetratingHit_AlsoSet_RegardlessOfDamageType()
    {
        // 破防命中同理减速（用户口径），锐/钝皆触发、时长与被挡下时相同（幅度不随伤害变）。
        var body = HumanBody.NewBody();
        var sharpRes = Hit(body, HumanBody.LeftHand, dmg: 5, DamageType.Sharp, initialRoll: 6);
        var sharpOut = new CombatEffectResolver(new SequenceRandomSource(0.99)).Apply(body, SharpW, sharpRes);
        Assert.Equal(EffectConfig.Default().StaggerSeconds, sharpOut.StaggerSeconds, 9);

        var body2 = HumanBody.NewBody();
        var bluntRes = Hit(body2, HumanBody.LeftHand, dmg: 5, DamageType.Blunt, initialRoll: 6);
        // 钝器命中手（非震荡部位）：仍会走骨折 roll，给一个不触发的高值；减速与之无关。
        var bluntOut = new CombatEffectResolver(new SequenceRandomSource(0.99)).Apply(body2, BluntW, bluntRes);
        Assert.Equal(EffectConfig.Default().StaggerSeconds, bluntOut.StaggerSeconds, 9);
    }

    [Fact]
    public void Stagger_DoesNotInterruptOrClearCooldown_DistinctFromConcussion()
    {
        // 减速与震荡同路口不同效果：减速只出一个移速时长标量，不产生 Concussion 效果、不打断、不清冷却。
        // 用锐器命中手（不可能触发震荡）验证：有减速时长，但 Effects 里无 Concussion。
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.LeftHand, dmg: 0, DamageType.Sharp, initialRoll: 5);
        var outcome = new CombatEffectResolver(new SequenceRandomSource()).Apply(body, SharpW, res);

        Assert.True(outcome.StaggerSeconds > 0);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Concussion);
    }

    [Fact]
    public void ResolveHit_ThreadsStaggerToAttackOutcome_ForAllActors()
    {
        // Godot 全角色消费边界：所有 Actor.ReceiveAttack → CombatEngine.ResolveHit。
        // 无论破防与否，AttackOutcome 都透传 StaggerSeconds，供 Actor._staggerTimer 消费。
        var engine = new CombatEngine(new SystemRandomSource(11));
        var body = HumanBody.NewBody();

        // 无甲直击（必破防）：透传减速。
        var hit = engine.ResolveHit(CombatData.Dagger(), System.Array.Empty<ArmorLayer>(), body);
        Assert.Equal(engine.Effects.StaggerSeconds, hit.StaggerSeconds, 9);
        Assert.True(hit.StaggerSeconds > 0);
    }
}
