using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 🔴 <b>白天/黑夜两相位的唯一事实源 + 跨消费方焊死自检</b>。
///
/// <para>背景：此前"相位"被劈成三套语义不同的分类（哪几个算夜里 / 哪几个是每日 tick 点 / 哪几个世界冻结），
/// 散在 9+ 处各写各的 inline 集合，「改一处漏一处」正是陷阱产出翻 4 倍那类 bug 的根。现全部收口到
/// <see cref="DayPhaseSegments"/>。本组测试做两件事：</para>
/// <list type="number">
///   <item><b>钉死两相位</b>：所有内部流程节点只归白天或黑夜；清晨聚餐归白天、黄昏聚餐归黑夜。</item>
///   <item><b>焊死各消费方</b>：遍历全部流程节点，断言环境光、陷阱、存档与班别都读取同一事实源。
///   任何消费方日后偷偷 inline 一份自己的相位集合并跑偏，这里必有一处打红。</item>
/// </list>
/// </summary>
public class DayPhaseSegmentsTests
{
    private static readonly DayPhase[] AllPhases = Enum.GetValues<DayPhase>();

    // ———————————————————————————— 钉死当前分类（规范源自身）————————————————————————————

    [Fact]
    public void 玩法相位只有白天和黑夜()
        => Assert.Equal(new[] { DayNightPhase.Day, DayNightPhase.Night }, Enum.GetValues<DayNightPhase>());

    [Fact]
    public void 黑夜相位包含黄昏聚餐_夜间部署_夜间行动()
    {
        var night = AllPhases.Where(DayPhaseSegments.IsNight).ToHashSet();
        Assert.Equal(new HashSet<DayPhase> { DayPhase.DuskMeal, DayPhase.NightPrep, DayPhase.NightAct }, night);
    }

    [Fact]
    public void 聚餐边界流程恰为_DawnMeal_DuskMeal_即每日两个tick点()
    {
        var meal = AllPhases.Where(DayPhaseSegments.IsMeal).ToHashSet();
        Assert.Equal(new HashSet<DayPhase> { DayPhase.DawnMeal, DayPhase.DuskMeal }, meal);
    }

    [Fact]
    public void 白天相位包含清晨聚餐与全部白天流程()
    {
        var day = AllPhases.Where(DayPhaseSegments.IsDay).ToHashSet();
        Assert.Equal(
            new HashSet<DayPhase>
            {
                DayPhase.DawnMeal, DayPhase.DayPrep, DayPhase.DayTravel, DayPhase.DayExplore, DayPhase.DayReturn,
            },
            day);
    }

    [Fact]
    public void 冻结集合恰为_筹备_回营_夜间部署_两顿聚餐()
    {
        // 世界冻结（TimeScale=0）集合 = {DayPrep, DayReturn, NightPrep, DawnMeal, DuskMeal}。
        // 真正实时推进的只有 DayTravel(8×) / DayExplore / NightAct 三个。
        var frozen = AllPhases.Where(DayPhaseSegments.IsFrozen).ToHashSet();
        Assert.Equal(
            new HashSet<DayPhase>
            {
                DayPhase.DayPrep, DayPhase.DayReturn, DayPhase.NightPrep,
                DayPhase.DawnMeal, DayPhase.DuskMeal,
            },
            frozen);
    }

    [Fact]
    public void 每个流程节点恰属白天或黑夜之一()
    {
        foreach (var phase in AllPhases)
        {
            Assert.NotEqual(DayPhaseSegments.IsNight(phase), DayPhaseSegments.IsDay(phase));
        }
    }

    // ———————————————————————————— 焊死各消费方（跨文件不许分叉）————————————————————————————

    [Fact]
    public void 环境光分档与段分类逐值焊死()
    {
        // 聚餐使用暮光表现；其余节点严格映射黑夜/白天环境光。
        foreach (var phase in AllPhases)
        {
            float ambient = VisionLogic.AmbientLight(phase, indoorsDark: false);

            Assert.Equal(DayPhaseSegments.IsMeal(phase), ambient == VisionLogic.TwilightAmbient);
            if (!DayPhaseSegments.IsMeal(phase))
            {
                Assert.Equal(DayPhaseSegments.IsNight(phase), ambient == VisionLogic.NightAmbient);
                Assert.Equal(DayPhaseSegments.IsDay(phase), ambient == VisionLogic.DaylightAmbient);
            }
        }
    }

    [Fact]
    public void 陷阱掷点与聚餐边界流程逐值焊死()
    {
        // 陷阱只在两个昼夜边界各掷一次点（2/天），不随内部流程节点重复结算。
        foreach (var phase in AllPhases)
            Assert.Equal(DayPhaseSegments.IsMeal(phase), TrapLogic.RollsOnPhase(phase));

        // 频率常量也须从谓词数出（不写死），一天恰 2 次。
        Assert.Equal(2, AllPhases.Count(TrapLogic.RollsOnPhase));
    }

    [Fact]
    public void 自动存档点与聚餐边界流程逐值焊死()
    {
        // 自动存档一天两次（清晨/黄昏聚餐），把一天切成"重做整白天/整夜晚"的干净读档粒度。
        foreach (var phase in AllPhases)
            Assert.Equal(DayPhaseSegments.IsMeal(phase), SaveRotation.ShouldAutosaveAt(phase));
    }

    [Fact]
    public void 班别相位与两相位分类逐值焊死()
    {
        // ShiftSchedule.PhaseOf 转发到规范源——两者对每个流程节点必须逐值一致。
        foreach (var phase in AllPhases)
        {
            Assert.Equal(DayPhaseSegments.PhaseOf(phase), ShiftSchedule.PhaseOf(phase));

            Assert.Equal(DayPhaseSegments.IsNight(phase), ShiftSchedule.PhaseOf(phase) == DayNightPhase.Night);
            Assert.Equal(DayPhaseSegments.IsDay(phase), ShiftSchedule.PhaseOf(phase) == DayNightPhase.Day);
        }
    }

    [Fact]
    public void 卧床休养的聚餐判据与聚餐边界流程逐值焊死()
    {
        // BedrestLogic 用 IsMeal 判“聚餐流程不算休养时段”——同一段语义，须与规范源一致。
        foreach (var phase in AllPhases)
            Assert.Equal(
                DayPhaseSegments.IsMeal(phase),
                BedrestLogic.CanOrderBedrest(
                    alive: true,
                    role: PawnRole.Idle,
                    phase: phase,
                    hasOwnBed: true,
                    freeBeds: 0).Status == BedrestOrderStatus.MealPhase);
    }
}
