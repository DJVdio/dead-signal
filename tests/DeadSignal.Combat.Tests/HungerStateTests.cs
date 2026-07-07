using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 饥饿系统 v2 纯逻辑单测：刻度 0-6 数值化、每昼夜切换 -1/吃一餐 +1 的净零模型、
/// 上限 clamp（含"大胃袋"6）、到 0 饿死、各级能力惩罚与士气阶梯、以及"饥饿×残疾"能力合并。
/// 数值皆"拟定待调"，测试锁的是规则形态与当前拟定值。
/// </summary>
public class HungerStateTests
{
    [Fact]
    public void Defaults_StartAtSated_CapFive()
    {
        var h = new HungerState();
        Assert.Equal((int)HungerLevel.Sated, h.Value); // 正常=5
        Assert.Equal(5, h.Cap);
        Assert.Equal(HungerLevel.Sated, h.Level);
        Assert.False(h.IsStarved);
    }

    [Fact]
    public void Decay_DropsOnePerPhase_FloorAtStarved()
    {
        var h = new HungerState(); // 5
        h.Decay(); Assert.Equal((int)HungerLevel.Peckish, h.Value);      // 4 有点饿
        h.Decay(); Assert.Equal((int)HungerLevel.Hungry, h.Value);       // 3 饥饿
        h.Decay(); Assert.Equal((int)HungerLevel.Ravenous, h.Value);     // 2 极度饥饿
        h.Decay(); Assert.Equal((int)HungerLevel.Malnourished, h.Value); // 1 营养不良
        h.Decay(); Assert.Equal((int)HungerLevel.Starved, h.Value);      // 0 饿死
        Assert.True(h.IsStarved);
        h.Decay(); Assert.Equal(0, h.Value); // 触底不再下探
    }

    [Fact]
    public void SatedMinusTwoPhases_LandsOnHungry()
    {
        // 正常(5)连缺两餐（两次昼夜切换各 -1，无进食）→ 饥饿(3)。
        var h = new HungerState();
        h.Decay();
        h.Decay();
        Assert.Equal(HungerLevel.Hungry, h.Level);
    }

    [Fact]
    public void DecayThenFeed_IsNetZero_MaintainsLevel()
    {
        // 一次昼夜切换 -1 + 吃到一餐 +1 = 净零维持（吃满两餐即不变）。
        var h = new HungerState(); // 5
        h.Decay(); h.Feed();
        Assert.Equal((int)HungerLevel.Sated, h.Value);
        h.Decay(); h.Feed();
        Assert.Equal((int)HungerLevel.Sated, h.Value);
    }

    // ---- P0 回归：一次昼夜相位结算 ResolvePhase 的跨 0 净模型（修误杀）----

    [Fact]
    public void ResolvePhase_Malnourished_Fed_Maintains_NotStarved()
    {
        // 营养不良(1)当餐吃到饭 → 净零维持在 1、不死（旧两步 Decay+Feed 会 1→0→短路→误杀）。
        var h = new HungerState(value: (int)HungerLevel.Malnourished);
        bool starved = h.ResolvePhase(ate: true);
        Assert.Equal((int)HungerLevel.Malnourished, h.Value);
        Assert.False(starved);
        Assert.False(h.IsStarved);
    }

    [Fact]
    public void ResolvePhase_Malnourished_NotFed_Starves()
    {
        // 营养不良(1)没吃到 → 净 -1 到 0 饿死。
        var h = new HungerState(value: (int)HungerLevel.Malnourished);
        bool starved = h.ResolvePhase(ate: false);
        Assert.Equal((int)HungerLevel.Starved, h.Value);
        Assert.True(starved);
    }

    [Fact]
    public void ResolvePhase_AlreadyStarved_NoRevive_StaysDead()
    {
        // 进餐前已饿死(0)：吃到饭也不复活，维持 0（保留"终态不复活"原意）。
        var h = new HungerState(value: (int)HungerLevel.Starved);
        bool starved = h.ResolvePhase(ate: true);
        Assert.Equal(0, h.Value);
        Assert.True(starved);
    }

    [Fact]
    public void ResolvePhase_Sated_TwoMealsFed_MaintainsNetZero()
    {
        // 正常(5)吃满两餐（两次相位各吃到）→ 净零维持在 5。
        var h = new HungerState();
        Assert.False(h.ResolvePhase(ate: true));
        Assert.False(h.ResolvePhase(ate: true));
        Assert.Equal((int)HungerLevel.Sated, h.Value);
    }

    [Fact]
    public void Feed_ClampsToCap_NormalCapFive()
    {
        var h = new HungerState(value: (int)HungerLevel.Sated, cap: 5); // 已在上限
        h.Feed();
        Assert.Equal(5, h.Value); // 普通角色吃不到"吃撑"(6)
    }

    [Fact]
    public void BigStomachTrait_CapSix_AllowsStuffed()
    {
        // "大胃袋"上限 6：从正常再喂可达吃撑(6)，且不越过 6。
        var h = new HungerState(value: (int)HungerLevel.Sated, cap: HungerState.MaxCap);
        h.Feed();
        Assert.Equal((int)HungerLevel.Stuffed, h.Value);
        h.Feed();
        Assert.Equal(6, h.Value);
    }

    [Fact]
    public void Constructor_ClampsValueToCap()
    {
        var h = new HungerState(value: 99, cap: 5);
        Assert.Equal(5, h.Value);
        var over = new HungerState(value: 5, cap: 99); // cap 自身封顶 6
        Assert.Equal(HungerState.MaxCap, over.Cap);
    }

    [Fact]
    public void Feed_AfterStarved_DoesNotRevive()
    {
        var h = new HungerState(value: 0);
        Assert.True(h.IsStarved);
        h.Feed();
        Assert.Equal(0, h.Value); // 饿死是终态
        Assert.True(h.IsStarved);
    }

    [Theory]
    [InlineData(HungerLevel.Stuffed, 0.0)]
    [InlineData(HungerLevel.Sated, 0.0)]
    [InlineData(HungerLevel.Peckish, 0.0)]
    [InlineData(HungerLevel.Hungry, 0.10)]
    [InlineData(HungerLevel.Ravenous, 0.25)]
    [InlineData(HungerLevel.Malnourished, 0.45)]
    public void AbilityPenalty_LaddersByLevel(HungerLevel level, double expected)
    {
        var h = new HungerState(value: (int)level, cap: HungerState.MaxCap);
        Assert.Equal(expected, h.AbilityPenalty, 3);
    }

    [Theory]
    [InlineData(HungerLevel.Sated, 0.0)]
    [InlineData(HungerLevel.Peckish, 0.0)]
    [InlineData(HungerLevel.Hungry, 1.0)]
    [InlineData(HungerLevel.Ravenous, 3.0)]
    [InlineData(HungerLevel.Malnourished, 6.0)]
    public void MoralePenalty_LaddersByLevel(HungerLevel level, double expected)
    {
        var h = new HungerState(value: (int)level, cap: HungerState.MaxCap);
        Assert.Equal(expected, h.MoralePenaltyPerPhase, 3);
    }

    [Fact]
    public void CombineCapability_MultipliesDisabilityAndHunger()
    {
        // 断一只手（残疾操作惩罚 0.5）叠极度饥饿（饥饿惩罚 0.25）：有效操作 = 0.5 × 0.75 = 0.375。
        double eff = HungerState.CombineCapability(0.5, 0.25);
        Assert.Equal(0.375, eff, 3);
    }

    [Fact]
    public void CombineCapability_NoHunger_LeavesDisabilityUntouched()
    {
        // 饥饿惩罚 0（正常/丧尸）→ 有效能力 = 1−残疾惩罚（残疾数学不被改坏）。
        Assert.Equal(0.5, HungerState.CombineCapability(0.5, 0.0), 3);
        Assert.Equal(1.0, HungerState.CombineCapability(0.0, 0.0), 3);
    }

    [Fact]
    public void CombineCapability_FullPenalty_ClampsToZero()
    {
        // 断双手（残疾 1.0）或饿死（饥饿 1.0）→ 完全丧失，绝不为负。
        Assert.Equal(0.0, HungerState.CombineCapability(1.0, 0.25), 3);
        Assert.Equal(0.0, HungerState.CombineCapability(0.5, 1.0), 3);
    }
}
