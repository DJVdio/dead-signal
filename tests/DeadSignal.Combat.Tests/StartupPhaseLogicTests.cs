using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 开局起始相位决策纯逻辑单测。
/// 口径（用户拍板「游戏应当从第一天晚上开始」）：
///  - startAtNight=true  ⇒ 起始相位 NightAct 且需显式置首日 Day=1（该相位不自增 Day）。
///  - startAtNight=false ⇒ 起始相位 DayPrep 且不显式置日（由 TransitionTo(DayPrep) 自增 0→1）。
/// </summary>
public class StartupPhaseLogicTests
{
    [Fact]
    public void StartAtNight_True_YieldsNightActAndSetsDayToOne()
    {
        StartupPhaseLogic.Decision d = StartupPhaseLogic.Resolve(startAtNight: true);

        Assert.Equal(DayPhase.NightAct, d.Phase);
        Assert.True(d.SetDayToOne);
    }

    [Fact]
    public void StartAtNight_False_YieldsDayPrepAndDoesNotSetDay()
    {
        StartupPhaseLogic.Decision d = StartupPhaseLogic.Resolve(startAtNight: false);

        Assert.Equal(DayPhase.DayPrep, d.Phase);
        Assert.False(d.SetDayToOne);
    }
}
