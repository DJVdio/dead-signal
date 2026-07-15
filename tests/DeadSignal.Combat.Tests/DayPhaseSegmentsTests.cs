using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 🔴 <b>昼夜段分类的唯一事实源 + 跨消费方焊死自检</b>（R2 相位统一）。
///
/// <para>背景：此前"相位"被劈成三套语义不同的分类（哪几个算夜里 / 哪几个是每日 tick 点 / 哪几个世界冻结），
/// 散在 9+ 处各写各的 inline 集合，「改一处漏一处」正是陷阱产出翻 4 倍那类 bug 的根。现全部收口到
/// <see cref="DayPhaseSegments"/>。本组测试做两件事：</para>
/// <list type="number">
///   <item><b>钉死当前分类</b>：把 8 值 <see cref="DayPhase"/> 的段归属逐值写死（白天 4 / 夜晚 2 / 聚餐 2、
///   冻结 5）。谁挪了 DawnMeal、加了第 9 个相位、或改错派生源，这里立刻报红。</item>
///   <item><b>焊死各消费方</b>：foreach 全 8 相位，断言每个消费方的分类与规范源<b>逐值一致</b>——
///   环境光==夜/暮/昼 ⟺ IsNight/IsMeal/IsDay、陷阱掷点/自动存档 ⟺ IsMeal、班别归块 ⟺ Segment。
///   任何消费方日后偷偷 inline 一份自己的相位集合并跑偏，这里必有一处打红。</item>
/// </list>
/// </summary>
public class DayPhaseSegmentsTests
{
    private static readonly DayPhase[] AllPhases = Enum.GetValues<DayPhase>();

    // ———————————————————————————— 钉死当前分类（规范源自身）————————————————————————————

    [Fact]
    public void 枚举保持八值不动_不许悄悄增删相位()
    {
        // DayPhase 必须留 8 值驱动 GameClock 状态机 / 尸体腐烂按相位步进 / DisplayNames / 视野。
        // 加第 9 个相位会同时打红这里与下面所有"穷尽覆盖"断言——逼加相位者必须显式分好段。
        Assert.Equal(8, AllPhases.Length);
    }

    [Fact]
    public void 夜晚段恰为_NightPrep_NightAct()
    {
        var night = AllPhases.Where(DayPhaseSegments.IsNight).ToHashSet();
        Assert.Equal(new HashSet<DayPhase> { DayPhase.NightPrep, DayPhase.NightAct }, night);
    }

    [Fact]
    public void 聚餐段恰为_DawnMeal_DuskMeal_即每日两个tick点()
    {
        var meal = AllPhases.Where(DayPhaseSegments.IsMeal).ToHashSet();
        Assert.Equal(new HashSet<DayPhase> { DayPhase.DawnMeal, DayPhase.DuskMeal }, meal);
    }

    [Fact]
    public void 白天段恰为四个相位()
    {
        var day = AllPhases.Where(DayPhaseSegments.IsDay).ToHashSet();
        Assert.Equal(
            new HashSet<DayPhase>
            {
                DayPhase.DayPrep, DayPhase.DayTravel, DayPhase.DayExplore, DayPhase.DayReturn,
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
    public void 每个相位恰属一个段_三段互斥且穷尽()
    {
        foreach (var phase in AllPhases)
        {
            int count = (DayPhaseSegments.IsNight(phase) ? 1 : 0)
                        + (DayPhaseSegments.IsMeal(phase) ? 1 : 0)
                        + (DayPhaseSegments.IsDay(phase) ? 1 : 0);
            Assert.True(count == 1, $"相位 {phase} 落在 {count} 个段里（应恰好 1 个）。");
        }
    }

    // ———————————————————————————— 焊死各消费方（跨文件不许分叉）————————————————————————————

    [Fact]
    public void 环境光分档与段分类逐值焊死()
    {
        // VisionLogic.AmbientLight 的三档（暮光/微月/满档）必须严格映射到聚餐/夜晚/白天段。
        foreach (var phase in AllPhases)
        {
            float ambient = VisionLogic.AmbientLight(phase, indoorsDark: false);

            Assert.Equal(DayPhaseSegments.IsNight(phase), ambient == VisionLogic.NightAmbient);
            Assert.Equal(DayPhaseSegments.IsMeal(phase), ambient == VisionLogic.TwilightAmbient);
            Assert.Equal(DayPhaseSegments.IsDay(phase), ambient == VisionLogic.DaylightAmbient);
        }
    }

    [Fact]
    public void 陷阱掷点与聚餐段逐值焊死()
    {
        // 陷阱一天只在两顿聚餐各掷一次点（2/天）——早期误按 8 相位逐个掷点让产出翻 4 倍。
        foreach (var phase in AllPhases)
            Assert.Equal(DayPhaseSegments.IsMeal(phase), TrapLogic.RollsOnPhase(phase));

        // 频率常量也须从谓词数出（不写死），一天恰 2 次。
        Assert.Equal(2, AllPhases.Count(TrapLogic.RollsOnPhase));
    }

    [Fact]
    public void 自动存档点与聚餐段逐值焊死()
    {
        // 自动存档一天两次（清晨/黄昏聚餐），把一天切成"重做整白天/整夜晚"的干净读档粒度。
        foreach (var phase in AllPhases)
            Assert.Equal(DayPhaseSegments.IsMeal(phase), SaveRotation.ShouldAutosaveAt(phase));
    }

    [Fact]
    public void 班别归块与段分类逐值焊死()
    {
        // ShiftSchedule.BlockOf 是段分类的既有 API，现转发到规范源——两者对每个相位必须逐值一致。
        foreach (var phase in AllPhases)
        {
            Assert.Equal(DayPhaseSegments.SegmentOf(phase), ShiftSchedule.BlockOf(phase));

            Assert.Equal(DayPhaseSegments.IsNight(phase), ShiftSchedule.BlockOf(phase) == PhaseBlock.Night);
            Assert.Equal(DayPhaseSegments.IsMeal(phase), ShiftSchedule.BlockOf(phase) == PhaseBlock.Meal);
            Assert.Equal(DayPhaseSegments.IsDay(phase), ShiftSchedule.BlockOf(phase) == PhaseBlock.Day);
        }
    }

    [Fact]
    public void 卧床休养的聚餐判据与聚餐段逐值焊死()
    {
        // BedrestLogic 用 BlockOf==Meal 判"聚餐相位不算休养时段"——同一段语义，须与规范源一致。
        foreach (var phase in AllPhases)
            Assert.Equal(
                DayPhaseSegments.IsMeal(phase),
                ShiftSchedule.BlockOf(phase) == PhaseBlock.Meal);
    }
}
