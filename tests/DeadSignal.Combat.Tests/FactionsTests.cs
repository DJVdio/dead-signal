using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 三方敌对矩阵纯逻辑（<see cref="Factions.IsHostile"/>）。
/// 用户拍板：Survivor↔Zombie、Survivor↔Raider、Zombie↔Raider 均敌对（丧尸也打劫掠者）；
/// 同阵营不敌对。矩阵对称、自反非敌对。
/// </summary>
public class FactionsTests
{
    [Theory]
    // 同阵营：不敌对
    [InlineData(Faction.Survivor, Faction.Survivor, false)]
    [InlineData(Faction.Zombie, Faction.Zombie, false)]
    [InlineData(Faction.Raider, Faction.Raider, false)]
    // 跨阵营：均敌对（两个方向都测，验证对称）
    [InlineData(Faction.Survivor, Faction.Zombie, true)]
    [InlineData(Faction.Zombie, Faction.Survivor, true)]
    [InlineData(Faction.Survivor, Faction.Raider, true)]
    [InlineData(Faction.Raider, Faction.Survivor, true)]
    [InlineData(Faction.Zombie, Faction.Raider, true)]
    [InlineData(Faction.Raider, Faction.Zombie, true)]
    public void IsHostile_CoversAllPairs(Faction a, Faction b, bool expected)
    {
        Assert.Equal(expected, Factions.IsHostile(a, b));
    }

    [Fact]
    public void IsHostile_IsSymmetric_ForEveryPair()
    {
        var all = new[] { Faction.Survivor, Faction.Zombie, Faction.Raider };
        foreach (Faction a in all)
        {
            foreach (Faction b in all)
            {
                Assert.Equal(Factions.IsHostile(a, b), Factions.IsHostile(b, a));
            }
        }
    }
}
