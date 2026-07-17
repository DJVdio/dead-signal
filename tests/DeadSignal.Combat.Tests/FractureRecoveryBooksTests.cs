using System;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [A3]《尖峰时刻》骨折恢复 +15% —— 读过该书的人，术后**骨折**逐日愈合量 ×1.15（乘算，禁加算）。
/// 意图护栏（先红[编译红]→绿）：
///   ① 纯逻辑 <see cref="FractureRecoveryBooks.HealSpeedMultiplier"/>：没读=1.0、读过尖峰时刻=1.15、读别的书=1.0；
///   ② TickDay 新增 <c>fractureHealSpeedMultiplier</c> 轴真作用于骨折愈合（×1.15 ⇒ 当日愈合量 ×1.15）；
///   ③ **仅骨折**（用户 wiki 字面「骨折恢复」）：出血愈合**不吃**本轴（与山姆 L3 光环那条作用两者的 healSpeedMultiplier 分开）。
/// </summary>
public class FractureRecoveryBooksTests
{
    private static IRandomSource NoInfection(int ticks = 64)
        => new SequenceRandomSource(Enumerable.Repeat(1.0, ticks).ToArray());
    private static IRandomSource Roll(double value) => new SequenceRandomSource(value);

    // ── ① 纯逻辑乘子 ──
    [Fact]
    public void 没读书_骨折恢复乘子1()
        => Assert.Equal(1.0, FractureRecoveryBooks.HealSpeedMultiplier(_ => false), 9);

    [Fact]
    public void 读过尖峰时刻_骨折恢复乘子1点15()
        => Assert.Equal(1.15, FractureRecoveryBooks.HealSpeedMultiplier(id => id == BookLibrary.PeakHourId), 9);

    [Fact]
    public void 只读别的书_骨折恢复乘子1_零回归()
        => Assert.Equal(1.0, FractureRecoveryBooks.HealSpeedMultiplier(id => id == "carpentry_basics"), 9);

    // ── ② TickDay 骨折轴：×1.15 ⇒ 当日骨折愈合量 ×1.15 ──
    [Fact]
    public void 骨折恢复乘子1点15_当日愈合量按比例放大()
    {
        double healBase = OperatedFractureHealOneDay(fractureMult: 1.0);
        double healBoosted = OperatedFractureHealOneDay(fractureMult: 1.15);
        Assert.True(healBase > 0, "术后骨折应有正的逐日愈合量");
        Assert.Equal(1.15, healBoosted / healBase, 6);
    }

    // ── ③ 仅骨折：出血愈合不吃本轴（零回归）──
    [Fact]
    public void 出血愈合_不受骨折轴影响()
    {
        double healBase = OperatedBleedHealOneDay(fractureMult: 1.0);
        double healWithFrac = OperatedBleedHealOneDay(fractureMult: 1.15);
        Assert.True(healBase > 0);
        Assert.Equal(healBase, healWithFrac, 9); // 骨折轴对出血一字未动
    }

    // 建一条术后骨折，推进一天，返回当日愈合量（术后 severity − tick 后 severity）。
    private static double OperatedFractureHealOneDay(double fractureMult)
    {
        var set = new HealthConditionSet();
        var c = new HealthCondition(HealthConditionType.Fracture, 0.6, "右大腿", onLimb: true);
        set.Add(c);
        set.PerformSurgery(c, new[] { "splint" }, onBed: false, Roll(30)); // 成功入愈合态
        double pre = c.Severity;
        set.TickDay(NoInfection(), resting: false, fractureHealSpeedMultiplier: fractureMult);
        return pre - c.Severity;
    }

    // 建一条术后出血，推进一天，返回当日愈合量。
    private static double OperatedBleedHealOneDay(double fractureMult)
    {
        var set = new HealthConditionSet();
        var c = new HealthCondition(HealthConditionType.Bleeding, 0.4, "右手", onLimb: true);
        set.Add(c);
        set.PerformSurgery(c, new[] { "bandage", "needle_thread" }, onBed: false, Roll(30));
        double pre = c.Severity;
        set.TickDay(NoInfection(), resting: false, fractureHealSpeedMultiplier: fractureMult);
        return pre - c.Severity;
    }
}
