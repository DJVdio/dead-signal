using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「营地全灭 → 游戏结束」判定纯逻辑单测。
/// 口径：存活玩家幸存者数为 0（或空名单）即全灭；尚有一人存活即未全灭。
/// </summary>
public class GameOverConditionTests
{
    [Theory]
    [InlineData(0, true)]   // 全灭
    [InlineData(-1, true)]  // 兜底：负数视为全灭
    [InlineData(1, false)]  // 尚有一人存活
    [InlineData(5, false)]  // 满编存活
    public void AllSurvivorsDead_ByCount(int aliveCount, bool expected)
    {
        Assert.Equal(expected, GameOverCondition.AllSurvivorsDead(aliveCount));
    }

    [Fact]
    public void AllSurvivorsDead_EmptyRoster_IsWipe()
    {
        Assert.True(GameOverCondition.AllSurvivorsDead(System.Array.Empty<bool>()));
    }

    [Fact]
    public void AllSurvivorsDead_AllDeadFlags_IsWipe()
    {
        Assert.True(GameOverCondition.AllSurvivorsDead(new[] { false, false, false }));
    }

    [Fact]
    public void AllSurvivorsDead_OneAlive_NotWipe()
    {
        Assert.False(GameOverCondition.AllSurvivorsDead(new[] { false, true, false }));
    }
}
