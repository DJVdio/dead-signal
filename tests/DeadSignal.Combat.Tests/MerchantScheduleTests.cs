using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 神秘商人来访调度状态机（<see cref="MerchantSchedule"/>）纯逻辑：间隔 1~5 天区间边界、
/// 袭营/异常日顺延、可复现（固定随机序列）。
/// </summary>
public class MerchantScheduleTests
{
    // RollGap 在 [MinGap, MaxGap+1) 取实数下取整；喂固定值可精确控制间隔。
    private static MerchantSchedule Make(int startDay, params double[] rolls)
        => new(new SequenceRandomSource(rolls), startDay);

    [Fact]
    public void FirstVisit_IsScheduledGapDaysAfterStart_NotSameDay()
    {
        // 间隔 roll=3.0 → 首访 = 开局第1天 + 3 = 第4天（不在开局当天）。
        var sched = Make(startDay: 1, 3.0);
        Assert.Equal(4, sched.NextVisitDay);
    }

    [Theory]
    // 下取整边界：1.0→1、1.9→1、5.0→5、5.999→5；上界 6.0（=MaxGap+1）clamp 回 5。
    [InlineData(1.0, 1)]
    [InlineData(1.9, 1)]
    [InlineData(4.2, 4)]
    [InlineData(5.0, 5)]
    [InlineData(5.999, 5)]
    [InlineData(6.0, 5)]
    public void Gap_IsUniformIntegerInOneToFive(double roll, int expectedGap)
    {
        var sched = Make(startDay: 0, roll);
        Assert.Equal(expectedGap, sched.NextVisitDay); // startDay 0 → NextVisitDay == gap
        Assert.InRange(sched.NextVisitDay, 1, 5);
    }

    [Fact]
    public void ShouldVisit_False_BeforeScheduledDay()
    {
        var sched = Make(startDay: 1, 4.0); // 首访第5天
        Assert.False(sched.ShouldVisit(currentDay: 3, dayBlocked: false));
        Assert.False(sched.ShouldVisit(currentDay: 4, dayBlocked: false));
    }

    [Fact]
    public void ShouldVisit_True_OnScheduledPeacefulDay()
    {
        var sched = Make(startDay: 1, 4.0); // 首访第5天
        Assert.True(sched.ShouldVisit(currentDay: 5, dayBlocked: false));
    }

    [Fact]
    public void ShouldVisit_Postpones_WhenDayBlocked()
    {
        var sched = Make(startDay: 1, 4.0); // 首访第5天
        // 第5天恰逢袭营/异常 → 顺延不到访
        Assert.False(sched.ShouldVisit(currentDay: 5, dayBlocked: true));
        Assert.Equal(6, sched.NextVisitDay); // 推到明天再试
        // 第6天仍异常 → 再顺延
        Assert.False(sched.ShouldVisit(currentDay: 6, dayBlocked: true));
        Assert.Equal(7, sched.NextVisitDay);
        // 第7天平安 → 到访
        Assert.True(sched.ShouldVisit(currentDay: 7, dayBlocked: false));
    }

    [Fact]
    public void CompleteVisit_RollsNextGap_FromVisitDay()
    {
        var sched = Make(startDay: 1, 4.0, 2.0); // 首访第5天；下一间隔 roll=2.0
        Assert.True(sched.ShouldVisit(5, false));
        sched.CompleteVisit(currentDay: 5);
        Assert.Equal(7, sched.NextVisitDay); // 5 + 2
    }

    [Fact]
    public void Schedule_IsReproducible_WithSameSeededSequence()
    {
        var a = new MerchantSchedule(new SequenceRandomSource(2.0, 5.0, 1.0), currentDay: 0);
        var b = new MerchantSchedule(new SequenceRandomSource(2.0, 5.0, 1.0), currentDay: 0);
        Assert.Equal(a.NextVisitDay, b.NextVisitDay);
        a.CompleteVisit(a.NextVisitDay);
        b.CompleteVisit(b.NextVisitDay);
        Assert.Equal(a.NextVisitDay, b.NextVisitDay);
        a.CompleteVisit(a.NextVisitDay);
        b.CompleteVisit(b.NextVisitDay);
        Assert.Equal(a.NextVisitDay, b.NextVisitDay);
    }
}
