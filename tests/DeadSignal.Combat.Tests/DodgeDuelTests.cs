using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 对决引擎整次闪避轴（param-calibration 为校准布鲁斯"高闪避低伤缠斗"新增）。
/// 忠实映射运行时 <c>Actor.EvadeIncoming</c>：目标以 <see cref="DuelFighter.DodgeChance"/> 概率整发躲开（无伤、无效果）。
/// 默认 0 时引擎不掷点、不消耗随机流——既有对决位级不变（本类第二测守此不回归）。
/// </summary>
public class DodgeDuelTests
{
    private static Weapon Dagger() => new() { Name = "匕首", DamageMin = 4, DamageMax = 10, Penetration = 0.09, DamageType = DamageType.Sharp, AttackInterval = 1.4 };

    private static DuelFighter Attacker() => new()
    {
        Name = "攻击者",
        Weapons = new[] { new WeaponMount { Weapon = Dagger() } },
        Armor = System.Array.Empty<ArmorLayer>(),
    };

    // 无武器靶子（不还手），带指定闪避概率。
    private static DuelFighter Target(double dodge) => new()
    {
        Name = "靶子",
        Weapons = System.Array.Empty<WeaponMount>(),
        Armor = System.Array.Empty<ArmorLayer>(),
        DodgeChance = dodge,
    };

    [Fact]
    public void FullDodge_NeverTakesDamage_AndSurvives()
    {
        var engine = new DuelEngine(new SystemRandomSource(4242), new DuelConfig { MaxSimSeconds = 120 });
        var result = engine.Run(Attacker(), Target(1.0));

        // 所有落在靶子身上的攻击事件都必须是闪避（伤害 0），无一造成伤害。
        var landedOnTarget = result.Events
            .Where(e => e.Defender == "靶子" && e.Weapon.Length > 0)
            .ToList();
        Assert.NotEmpty(landedOnTarget);               // 攻击者确实出手了
        Assert.All(landedOnTarget, e => Assert.Contains("闪避", e.Tags));
        Assert.All(landedOnTarget, e => Assert.Equal(0, e.Damage));
        // 全闪避 + 靶子不还手 → 无人能死，判超时（靶子未倒下）。
        Assert.NotEqual("靶子", result.Loser);
    }

    [Fact]
    public void ZeroDodge_DoesNotConsumeRng_BitIdenticalToOmitted()
    {
        // 显式 DodgeChance=0 与不设（默认 0）必须产出位级一致的战报——证明 >0 守卫下零掷点。
        var omitted = new DuelFighter
        {
            Name = "靶子",
            Weapons = System.Array.Empty<WeaponMount>(),
            Armor = System.Array.Empty<ArmorLayer>(),
        };
        var explicitZero = Target(0.0);

        var r1 = new DuelEngine(new SystemRandomSource(777)).Run(Attacker(), omitted);
        var r2 = new DuelEngine(new SystemRandomSource(777)).Run(Attacker(), explicitZero);

        Assert.Equal(r1.TotalActions, r2.TotalActions);
        Assert.Equal(r1.DurationSeconds, r2.DurationSeconds);
        Assert.Equal(r1.Events.Count, r2.Events.Count);
        // 且靶子确实挨了伤（默认无闪避回归）。
        Assert.Contains(r1.Events, e => e.Defender == "靶子" && e.Damage > 0);
    }

    [Fact]
    public void HalfDodge_HalvesLandedHitsRoughly()
    {
        // 0.5 闪避应让约半数来袭被躲（统计口径，宽松区间）。
        int landed = 0, dodged = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var engine = new DuelEngine(new SystemRandomSource(seed), new DuelConfig { MaxSimSeconds = 30 });
            var result = engine.Run(Attacker(), Target(0.5));
            foreach (var e in result.Events.Where(e => e.Defender == "靶子" && e.Weapon.Length > 0))
            {
                if (e.Tags.Contains("闪避")) dodged++;
                else landed++;
            }
        }
        int total = landed + dodged;
        Assert.True(total > 100, $"样本过少：{total}");
        double dodgeRate = (double)dodged / total;
        Assert.InRange(dodgeRate, 0.4, 0.6);
    }
}
