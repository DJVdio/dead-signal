using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 布鲁斯（狗）饥饿刻度纯逻辑单测：吃一份 +3 / 每次聚餐 -1 / 到 0 饿死终态 / 能力惩罚复用人类阶梯。
/// 数值全"拟定待调"，本组只钉规则形态（相对关系），不锁绝对数值。
/// </summary>
public class DogHungerStateTests
{
    [Fact]
    public void Constructor_DefaultsToFullCap()
    {
        var h = new DogHungerState();
        Assert.Equal(DogHungerState.Cap, h.Value);
        Assert.False(h.IsStarved);
    }

    [Fact]
    public void Constructor_ClampsToRange()
    {
        Assert.Equal(0, new DogHungerState(-5).Value);
        Assert.Equal(DogHungerState.Cap, new DogHungerState(999).Value);
    }

    [Fact]
    public void ResolvePhase_NotEaten_DecrementsByOne()
    {
        var h = new DogHungerState(4);
        bool starved = h.ResolvePhase(ate: false);
        Assert.Equal(3, h.Value);
        Assert.False(starved);
    }

    [Fact]
    public void ResolvePhase_Eaten_NetPlusTwo()
    {
        // 吃一份 +3、相位 -1 → 净 +2。
        var h = new DogHungerState(2);
        h.ResolvePhase(ate: true);
        Assert.Equal(4, h.Value);
    }

    [Fact]
    public void ResolvePhase_Eaten_ClampsToCap()
    {
        var h = new DogHungerState(DogHungerState.Cap);
        h.ResolvePhase(ate: true); // -1 +3 = +2，封顶
        Assert.Equal(DogHungerState.Cap, h.Value);
    }

    [Fact]
    public void ResolvePhase_ReachesZero_Starves()
    {
        var h = new DogHungerState(1);
        bool starved = h.ResolvePhase(ate: false); // 1 -1 = 0
        Assert.True(starved);
        Assert.True(h.IsStarved);
    }

    [Fact]
    public void ResolvePhase_Starved_IsTerminal_NoRevive()
    {
        var h = new DogHungerState(1);
        h.ResolvePhase(ate: false);       // → 0 饿死
        Assert.True(h.IsStarved);
        bool stillStarved = h.ResolvePhase(ate: true); // 吃也不复活
        Assert.True(stillStarved);
        Assert.Equal(0, h.Value);
    }

    [Fact]
    public void ResolvePhase_OneMealCoversMultiplePhases()
    {
        // 吃一份后可撑多个不进食相位（+3 / -1）：验"一份约管三相位"的形态。
        var h = new DogHungerState(1);
        h.ResolvePhase(ate: true);  // 1 → 3（净 +2）
        Assert.Equal(3, h.Value);
        h.ResolvePhase(ate: false); // 3 → 2
        h.ResolvePhase(ate: false); // 2 → 1
        h.ResolvePhase(ate: false); // 1 → 0 饿死
        Assert.True(h.IsStarved);
    }

    [Fact]
    public void AbilityPenalty_WorseWhenHungrier()
    {
        // 越饿惩罚越重（复用人类 HungerState 阶梯，0-6 同尺度）；饱时无惩罚。
        Assert.Equal(0.0, new DogHungerState(DogHungerState.Cap).AbilityPenalty);
        double hungry = new DogHungerState(3).AbilityPenalty;
        double ravenous = new DogHungerState(2).AbilityPenalty;
        Assert.True(hungry > 0.0);
        Assert.True(ravenous > hungry);
    }
}
