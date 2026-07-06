using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 残疾操作惩罚接入对决攻速节奏（设计文档 §5 / next-steps）。用户拍板形态：
/// 有效出手间隔 = 基础出手间隔 / (1 − OperationPenalty)——操作惩罚 0.5 → 间隔翻倍；
/// 惩罚 ≥ 1（两手尽失等）→ 无法进行该武器攻击。
/// <see cref="Body.RecalculatePenalties"/> 出的净惩罚必须被 <see cref="DuelEngine"/> 的出手节奏消费。
/// </summary>
public class OperationPenaltyDuelTests
{
    private static Weapon Dagger() => new() { Name = "匕首", DamageMin = 4, DamageMax = 14, Penetration = 0.09, DamageType = DamageType.Sharp, AttackInterval = 0.7 };
    private static Weapon Claw() => new() { Name = "利爪", DamageMin = 4, DamageMax = 10, Penetration = 0.10, DamageType = DamageType.Sharp, AttackInterval = 0.7 };

    private static Body OneHandedBody()
    {
        var b = HumanBody.NewBody();
        b.Sever(HumanBody.LeftHand);   // 右手尚存 → 匕首仍可握持
        b.RecalculatePenalties();       // 操作惩罚 0.5
        return b;
    }

    private static Body NoHandsBody()
    {
        var b = HumanBody.NewBody();
        b.Sever(HumanBody.LeftHand);
        b.Sever(HumanBody.RightHand);
        b.RecalculatePenalties();       // 操作惩罚 1.0
        return b;
    }

    // 无武器的靶子：不会还手、不会给攻击方施加流血/震荡减益，隔离出纯粹的攻速节奏。
    private static DuelFighter Dummy() => new()
    {
        Name = "B",
        Weapons = System.Array.Empty<WeaponMount>(),
        Armor = System.Array.Empty<ArmorLayer>(),
    };

    private static IReadOnlyList<double> AttackTimes(DuelResult r, string attacker) =>
        r.Events.Where(e => e.Attacker == attacker && e.Weapon.Length > 0).Select(e => e.Time).ToList();

    [Fact]
    public void OperationPenalty50_DoublesEffectiveInterval()
    {
        var attacker = new DuelFighter
        {
            Name = "A",
            Weapons = new[] { new WeaponMount { Weapon = Dagger() } },
            Armor = System.Array.Empty<ArmorLayer>(),
            BodyFactory = OneHandedBody,
        };
        var r = new DuelEngine(new SystemRandomSource(7)).Run(attacker, Dummy());

        var times = AttackTimes(r, "A");
        Assert.True(times.Count >= 2, "断手幸存者应至少出手两次以观测间隔");
        // 基础 0.7 / (1 − 0.5) = 1.4。未接入时该间隔会是 0.7（红）。
        Assert.Equal(1.4, times[1] - times[0], 6);
    }

    [Fact]
    public void NoPenalty_KeepsBaseInterval()
    {
        var attacker = new DuelFighter
        {
            Name = "A",
            Weapons = new[] { new WeaponMount { Weapon = Dagger() } },
            Armor = System.Array.Empty<ArmorLayer>(),
        };
        var r = new DuelEngine(new SystemRandomSource(7)).Run(attacker, Dummy());

        var times = AttackTimes(r, "A");
        Assert.True(times.Count >= 2);
        Assert.Equal(0.7, times[1] - times[0], 6);
    }

    [Fact]
    public void OperationPenalty100_CannotAttack()
    {
        // 两手尽失、改用天生武器（不需握持，绕过 handAlive 排除）——仍应因操作能力尽失而无法出手。
        var attacker = new DuelFighter
        {
            Name = "A",
            Weapons = new[] { new WeaponMount { Weapon = Claw(), RequiresHand = false } },
            Armor = System.Array.Empty<ArmorLayer>(),
            BodyFactory = NoHandsBody,
        };
        var r = new DuelEngine(new SystemRandomSource(7)).Run(attacker, Dummy());

        // 未接入时天生武器不吃 handAlive 排除 → A 会照常出手（红）；接入后操作惩罚 1.0 → 零出手。
        Assert.Empty(AttackTimes(r, "A"));
    }
}
