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
    // 中立方（神秘商人）：与任何阵营（含彼此、含同为中立）都不敌对
    [InlineData(Faction.Neutral, Faction.Survivor, false)]
    [InlineData(Faction.Survivor, Faction.Neutral, false)]
    [InlineData(Faction.Neutral, Faction.Zombie, false)]
    [InlineData(Faction.Zombie, Faction.Neutral, false)]
    [InlineData(Faction.Neutral, Faction.Raider, false)]
    [InlineData(Faction.Raider, Faction.Neutral, false)]
    [InlineData(Faction.Neutral, Faction.Neutral, false)]
    public void IsHostile_CoversAllPairs(Faction a, Faction b, bool expected)
    {
        Assert.Equal(expected, Factions.IsHostile(a, b));
    }

    [Fact]
    public void IsHostile_IsSymmetric_ForEveryPair()
    {
        var all = new[] { Faction.Survivor, Faction.Zombie, Faction.Raider, Faction.Neutral };
        foreach (Faction a in all)
        {
            foreach (Faction b in all)
            {
                Assert.Equal(Factions.IsHostile(a, b), Factions.IsHostile(b, a));
            }
        }
    }

    [Fact]
    public void Neutral_IsHostileToNoOne()
    {
        var all = new[] { Faction.Survivor, Faction.Zombie, Faction.Raider, Faction.Neutral };
        foreach (Faction other in all)
        {
            Assert.False(Factions.IsHostile(Faction.Neutral, other));
            Assert.False(Factions.IsHostile(other, Faction.Neutral));
        }
    }
}
