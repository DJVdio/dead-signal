using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「损毁」（部位 MaxHp 被磨到 0 而永久报废）的护栏。
///
/// <para>
/// 用户追问「钝器导致的损毁算进 sim 了吗」。答案是**算了**，但此前没有任何测试钉住它——
/// 本文件补上，防止它退化成"只打个 tag 不消费"。
/// </para>
///
/// <para><b>损毁 ≠ 切除 ≠ 骨折，是三套并行机制：</b></para>
/// <list type="bullet">
/// <item><b>切除</b>（锐器）：单次伤害 ≥ 部位 MaxHP，或砍已经 0HP 的部位 ⇒ 部位被砍下来（掉装备、附带大出血）。</item>
/// <item><b>损毁</b>（钝伤）：砸**已经 0HP** 的部位 ⇒ 永久磨损其 MaxHp，磨到 0 ⇒ 部位报废
///   （连着但废了：不掉装备、不出血）。钝器打不断肢体，但能把它砸成没用的东西。</item>
/// <item><b>骨折</b>（天然钝器，任意部位、造成伤害即按概率触发）：部位还在、还有 HP，但功能受损。</item>
/// </list>
///
/// <para>
/// 三者的**战斗力消费口径统一**：<see cref="Body.IsGone"/> = 切除 ∪ 损毁 ⇒ 损毁的手同样不能持械、
/// 同样计入 <see cref="Body.RecalculatePenalties"/> 的操作能力惩罚（-50%/手）。骨折另走乘算系数。
/// </para>
/// </summary>
public class DestroyedPartTests
{
    [Fact]
    public void Destroyed_IsGone_JustLikeSevered()
    {
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftHand, 999);              // 打到 0HP
        body.ErodeMaxHp(HumanBody.LeftHand, 999);               // 再砸 → MaxHp 归 0 → 损毁

        Assert.True(body.IsDestroyed(HumanBody.LeftHand));
        Assert.False(body.IsSevered(HumanBody.LeftHand));       // 没被砍下来
        Assert.True(body.IsGone(HumanBody.LeftHand));           // 但对战斗力而言，一样是没了
    }

    [Fact]
    public void DestroyedHand_CostsOperationCapability_SameAsSeveredHand()
    {
        var destroyed = HumanBody.NewBody();
        destroyed.ApplyDamage(HumanBody.LeftHand, 999);
        destroyed.ErodeMaxHp(HumanBody.LeftHand, 999);
        destroyed.RecalculatePenalties();

        var severed = HumanBody.NewBody();
        severed.Sever(HumanBody.LeftHand);
        severed.RecalculatePenalties();

        // 砸废的手和砍掉的手，操作能力惩罚必须一样——否则钝器又被低估一层。
        Assert.Equal(
            severed.DisabilityModifiers.OperationPenalty,
            destroyed.DisabilityModifiers.OperationPenalty,
            9);
        Assert.True(destroyed.DisabilityModifiers.OperationPenalty > 0);
    }

    [Fact]
    public void BothHandsDestroyed_CannotOperateAtAll()
    {
        var body = HumanBody.NewBody();
        foreach (var hand in new[] { HumanBody.LeftHand, HumanBody.RightHand })
        {
            body.ApplyDamage(hand, 999);
            body.ErodeMaxHp(hand, 999);
        }

        body.RecalculatePenalties();

        // 两只手都被砸废 ⇒ 操作能力归零 ⇒ DuelEngine.PickNext 里这人再也出不了手。
        Assert.True(body.DisabilityModifiers.OperationPenalty >= 1.0);
    }

    [Fact]
    public void BluntOn_ZeroHpPart_Erodes_WhileSharpSevers()
    {
        // 同一个 0HP 的手，锐器来 → 砍下来（切除）；钝器来 → 砸（磨损/损毁）。两条路互斥。
        var sharpVictim = HumanBody.NewBody();
        sharpVictim.ApplyDamage(HumanBody.LeftHand, 999);
        var sharpOut = new CombatEffectResolver(new SequenceRandomSource(0.99)).Apply(
            sharpVictim,
            new Weapon { DamageType = DamageType.Sharp },
            new CombatResult
            {
                HitPart = sharpVictim.Parts[HumanBody.LeftHand],
                FinalDamage = 3,
                FinalDamageType = DamageType.Sharp,
                InitialAttackRoll = 3,
            });

        Assert.NotEmpty(sharpOut.SeveredParts);
        Assert.Equal(0, sharpOut.MaxHpEroded);

        var bluntVictim = HumanBody.NewBody();
        bluntVictim.ApplyDamage(HumanBody.LeftHand, 999);
        var bluntOut = new CombatEffectResolver(new SequenceRandomSource(0.99)).Apply(
            bluntVictim,
            new Weapon { DamageType = DamageType.Blunt },
            new CombatResult
            {
                HitPart = bluntVictim.Parts[HumanBody.LeftHand],
                FinalDamage = 3,
                FinalDamageType = DamageType.Blunt,
                InitialAttackRoll = 3,
            });

        Assert.Empty(bluntOut.SeveredParts);
        Assert.True(bluntOut.MaxHpEroded > 0, "钝器砸 0HP 部位必须磨损其 MaxHp——这是钝器的致残赛道");
    }

    [Fact]
    public void DuelEngine_ActuallyProducesDestroyedParts_WithBluntWeapons()
    {
        // 端到端：钝器打完一场对决，"损毁"确实会发生（且远多于锐器）。
        int bluntDestroy = CountDestroyTags(WeaponTable.Warhammer());
        int sharpDestroy = CountDestroyTags(WeaponTable.Longsword());

        Assert.True(bluntDestroy > 0, "钝器对决必须真的打出「损毁」");
        Assert.True(
            bluntDestroy > sharpDestroy,
            $"损毁应当是钝器的赛道：钝器 {bluntDestroy} 次 vs 锐器 {sharpDestroy} 次");
    }

    private static int CountDestroyTags(Weapon w)
    {
        int n = 0;
        var zombie = new DuelFighter
        {
            Name = "丧尸",
            Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
            Armor = ArmorTable.ZombieHide().ToArray(),
            BodyFactory = HumanBody.NewZombieBody,
        };
        var me = new DuelFighter
        {
            Name = "我方",
            Weapons = new[] { new WeaponMount { Weapon = w, RequiresHand = false } },
            Armor = Array.Empty<ArmorLayer>(),
        };

        for (int seed = 0; seed < 300; seed++)
        {
            var r = new DuelEngine(new SystemRandomSource(20260713 + seed * 131)).Run(me, zombie);
            n += r.Events.Count(e => e.Tags.Any(t => t.StartsWith("损毁:", StringComparison.Ordinal)));
        }

        return n;
    }
}
