using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 尸潮时限纯逻辑单测：隐性计时相位、剩余天数、到期无限围攻波次调度。
/// 数值皆"拟定待调"占位——本测验的是规则形态（不依赖发现、到期即终局、波次不停轮且递增），
/// 具体常量若调，断言随常量走（引用 HordeTimeline 常量而非硬编码魔数）。
/// </summary>
public class HordeTimelineTests
{
    // ---------------- Evaluate：三相位 ----------------

    [Fact]
    public void Evaluate_BeforeDeadline_Unsighted_IsHidden()
    {
        Assert.Equal(HordePhase.Hidden, HordeTimeline.Evaluate(1, sighted: false));
        Assert.Equal(HordePhase.Hidden, HordeTimeline.Evaluate(HordeTimeline.DeadlineDay - 1, sighted: false));
    }

    [Fact]
    public void Evaluate_BeforeDeadline_Sighted_IsSighted()
    {
        Assert.Equal(HordePhase.Sighted, HordeTimeline.Evaluate(1, sighted: true));
        Assert.Equal(HordePhase.Sighted, HordeTimeline.Evaluate(HordeTimeline.DeadlineDay - 1, sighted: true));
    }

    [Fact]
    public void Evaluate_AtDeadline_IsArrived_RegardlessOfSighting()
    {
        // 计时不依赖发现：到期即抵达，无论玩家是否上过瞭望台。
        Assert.Equal(HordePhase.Arrived, HordeTimeline.Evaluate(HordeTimeline.DeadlineDay, sighted: false));
        Assert.Equal(HordePhase.Arrived, HordeTimeline.Evaluate(HordeTimeline.DeadlineDay, sighted: true));
    }

    [Fact]
    public void Evaluate_PastDeadline_StaysArrived()
    {
        Assert.Equal(HordePhase.Arrived, HordeTimeline.Evaluate(HordeTimeline.DeadlineDay + 5, sighted: false));
    }

    // ---------------- DaysRemaining ----------------

    [Fact]
    public void DaysRemaining_CountsDownToDeadline()
    {
        Assert.Equal(HordeTimeline.DeadlineDay - 1, HordeTimeline.DaysRemaining(1));
        Assert.Equal(1, HordeTimeline.DaysRemaining(HordeTimeline.DeadlineDay - 1));
        Assert.Equal(0, HordeTimeline.DaysRemaining(HordeTimeline.DeadlineDay));
    }

    [Fact]
    public void DaysRemaining_ClampsAtZeroPastDeadline()
    {
        Assert.Equal(0, HordeTimeline.DaysRemaining(HordeTimeline.DeadlineDay + 10));
    }

    // ---------------- ShouldTriggerSiege：到期触发 + 终局冻结门控 ----------------

    [Fact]
    public void ShouldTriggerSiege_AtDeadline_NotFrozen_True()
    {
        Assert.True(HordeTimeline.ShouldTriggerSiege(HordeTimeline.DeadlineDay, sighted: false, endgameFrozen: false));
        Assert.True(HordeTimeline.ShouldTriggerSiege(HordeTimeline.DeadlineDay + 3, sighted: true, endgameFrozen: false));
    }

    [Fact]
    public void ShouldTriggerSiege_AtDeadline_Frozen_False()
    {
        // 终局冻结分支：主线接管后即便过了时限也不触发围攻。
        Assert.False(HordeTimeline.ShouldTriggerSiege(HordeTimeline.DeadlineDay, sighted: false, endgameFrozen: true));
        Assert.False(HordeTimeline.ShouldTriggerSiege(HordeTimeline.DeadlineDay + 10, sighted: true, endgameFrozen: true));
    }

    [Fact]
    public void ShouldTriggerSiege_BeforeDeadline_False_EitherWay()
    {
        Assert.False(HordeTimeline.ShouldTriggerSiege(HordeTimeline.DeadlineDay - 1, sighted: true, endgameFrozen: false));
        Assert.False(HordeTimeline.ShouldTriggerSiege(HordeTimeline.DeadlineDay - 1, sighted: true, endgameFrozen: true));
    }

    // ---------------- NextWave：到期无限围攻的波次调度 ----------------

    [Fact]
    public void NextWave_FirstWave_SpawnsImmediately()
    {
        var d = HordeTimeline.NextWave(waveIndex: 0, zombiesAlive: 0, secondsSinceLastWave: 0, campSize: 4);
        Assert.True(d.ShouldSpawn);
        Assert.Equal(HordeTimeline.WaveSize(0, 4), d.Count);
        Assert.True(d.Count >= 1);
    }

    [Fact]
    public void NextWave_ManyAlive_ShortlyAfter_Holds()
    {
        // 场上残敌多、距上波很短 → 不补投（避免同一瞬间堆爆）。
        var d = HordeTimeline.NextWave(waveIndex: 1, zombiesAlive: HordeTimeline.WaveClearThreshold + 20,
            secondsSinceLastWave: 0.5, campSize: 4);
        Assert.False(d.ShouldSpawn);
    }

    [Fact]
    public void NextWave_ClearedBelowThreshold_Spawns()
    {
        // 残敌被压到阈值以下 → 立刻下一波（波次不停轮）。
        var d = HordeTimeline.NextWave(waveIndex: 1, zombiesAlive: HordeTimeline.WaveClearThreshold,
            secondsSinceLastWave: 0.5, campSize: 4);
        Assert.True(d.ShouldSpawn);
    }

    [Fact]
    public void NextWave_IntervalElapsed_SpawnsEvenIfManyAlive()
    {
        // 即便残敌仍多，超过最长间隔也强制下一波 → 无限量、不给喘息。
        var d = HordeTimeline.NextWave(waveIndex: 1, zombiesAlive: HordeTimeline.WaveClearThreshold + 20,
            secondsSinceLastWave: HordeTimeline.WaveInterval + 1, campSize: 4);
        Assert.True(d.ShouldSpawn);
    }

    // ---------------- WaveSize：递增、封顶、保底 ----------------

    [Fact]
    public void WaveSize_EscalatesWithWaveIndex()
    {
        int w0 = HordeTimeline.WaveSize(0, 4);
        int w1 = HordeTimeline.WaveSize(1, 4);
        int w2 = HordeTimeline.WaveSize(2, 4);
        Assert.True(w1 > w0);
        Assert.True(w2 > w1);
    }

    [Fact]
    public void WaveSize_CapsAtWaveCap()
    {
        Assert.Equal(HordeTimeline.WaveCap, HordeTimeline.WaveSize(1000, 100));
    }

    [Fact]
    public void WaveSize_FloorsAtOne()
    {
        Assert.True(HordeTimeline.WaveSize(0, 0) >= 1);
    }
}
