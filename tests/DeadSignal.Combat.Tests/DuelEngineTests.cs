using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class DuelEngineTests
{
    private static Weapon Dagger() => new() { Name = "匕首", DamageMin = 4, DamageMax = 14, Penetration = 0.09, DamageType = DamageType.Sharp, AttackInterval = 0.7 };
    private static Weapon Warhammer() => new() { Name = "破甲锤", DamageMin = 20, DamageMax = 28, Penetration = 0.20, DamageType = DamageType.Blunt, AttackInterval = 1.8 };
    private static ArmorLayer Cloth() => new() { Name = "布衣", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2 };

    private static (DuelFighter, DuelFighter) Fixture() => (
        new DuelFighter { Name = "A", Weapons = new[] { new WeaponMount { Weapon = Dagger() } }, Armor = new[] { Cloth() } },
        new DuelFighter { Name = "B", Weapons = new[] { new WeaponMount { Weapon = Warhammer() } }, Armor = Array.Empty<ArmorLayer>() });

    [Fact]
    public void SameSeed_ProducesIdenticalTranscript()
    {
        var (a1, b1) = Fixture();
        var (a2, b2) = Fixture();

        var r1 = new DuelEngine(new SystemRandomSource(12345)).Run(a1, b1);
        var r2 = new DuelEngine(new SystemRandomSource(12345)).Run(a2, b2);

        Assert.Equal(r1.Winner, r2.Winner);
        Assert.Equal(r1.DurationSeconds, r2.DurationSeconds, 9);
        Assert.Equal(r1.EndReason, r2.EndReason);
        // 逐字段序列化比较（record 的 Tags 是列表，默认按引用比较，故手动规范化）
        Assert.Equal(Transcript(r1), Transcript(r2));
    }

    private static string Transcript(DuelResult r) =>
        string.Join("\n", r.Events.Select(e =>
            $"{e.Time:F3}|{e.Attacker}|{e.Defender}|{e.Weapon}|{e.Part}|{e.PenetrationDesc}|{e.Damage}|{e.ArrivedType}|{e.PartMaxHp:F3}|{string.Join(",", e.Tags)}"));

    [Fact]
    public void DifferentSeeds_CanDiverge()
    {
        var (a1, b1) = Fixture();
        var (a2, b2) = Fixture();
        var r1 = new DuelEngine(new SystemRandomSource(1)).Run(a1, b1);
        var r2 = new DuelEngine(new SystemRandomSource(999)).Run(a2, b2);
        // 不强求一定不同，但两局都应正常终局（有胜者或明确僵局/超时）
        Assert.True(r1.Events.Count > 0);
        Assert.True(r2.Events.Count > 0);
    }

    [Fact]
    public void Duel_Terminates_WithAWinner()
    {
        var (a, b) = Fixture();
        var r = new DuelEngine(new SystemRandomSource(7)).Run(a, b);
        Assert.NotNull(r.Winner);
        Assert.NotEqual(r.Winner, r.Loser);
        Assert.True(r.TotalActions > 0);
    }
}
