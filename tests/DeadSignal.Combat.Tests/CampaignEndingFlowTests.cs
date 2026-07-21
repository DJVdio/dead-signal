using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 电台主线到三种终局的跨子系统旅程护栏。
/// 与各纯逻辑类的细粒度测试互补：这里从开线一路推进到 outcome，包含存档往返，
/// 并钉住 CampMain 的真实相位入口，防止“单元都绿、生产接线断掉”。
/// </summary>
public sealed class CampaignEndingFlowTests
{
    [Fact]
    public void IgnoreRadio_DeadlineNight_ReachesHordeSouthEscapeEnding()
    {
        var flags = new StoryFlags();

        Assert.False(HordeTimeline.ShouldTriggerSiege(
            HordeTimeline.DeadlineDay - 1, sighted: false, endgameFrozen: false));
        Assert.True(HordeTimeline.ShouldTriggerSiege(
            HordeTimeline.DeadlineDay, sighted: false, endgameFrozen: false));

        var alive = new List<string> { "山姆", "诺蒂", "克莉丝汀" };
        string escapee = SouthEscapeEnding.SelectEscapee(alive, new SequenceRandomSource(1.2))!;
        flags.Set(HordeTimeline.ArrivedFlag, "true");
        SouthEscapeEnding.RecordEscapee(flags, escapee, "pawn_notty", SouthEscapeTrigger.HordeSiege);

        var restored = new StoryFlags(flags.Snapshot());
        Assert.True(restored.Has(HordeTimeline.ArrivedFlag));
        Assert.Equal("诺蒂", SouthEscapeEnding.EscapeeName(restored));
        Assert.Equal(SouthEscapeTrigger.HordeSiege, SouthEscapeEnding.TriggerOf(restored));
        Assert.True(SouthEscapeEnding.IsSequenceActive(restored));
        Assert.False(FamilyEscapeWin.HasWon(restored));
    }

    [Fact]
    public void ReplyMilitary_DueDawn_ReachesOneShotMilitarySouthEscapeEnding()
    {
        var flags = ReachDecisionPoint();
        const int replyDay = 12;

        Assert.True(RadioMainline.ReplyToMilitary(flags, replyDay));
        Assert.False(RadioMainline.TryFireMilitaryRaidHook(
            flags, replyDay + RadioMainline.MilitaryRaidDelayDays - 1));
        Assert.True(RadioMainline.TryFireMilitaryRaidHook(
            flags, replyDay + RadioMainline.MilitaryRaidDelayDays));

        string escapee = SouthEscapeEnding.SelectEscapee(
            new List<string> { "山姆", "克莉丝汀" }, new SequenceRandomSource(0.1))!;
        SouthEscapeEnding.RecordEscapee(flags, escapee, "pawn_sam", SouthEscapeTrigger.MilitaryRaid);

        var restored = new StoryFlags(flags.Snapshot());
        Assert.False(RadioMainline.TryFireMilitaryRaidHook(restored, replyDay + 99));
        Assert.Equal(SouthEscapeTrigger.MilitaryRaid, SouthEscapeEnding.TriggerOf(restored));
        Assert.Equal("山姆", SouthEscapeEnding.EscapeeName(restored));
        Assert.True(SouthEscapeEnding.IsSequenceActive(restored));
        Assert.False(FamilyEscapeWin.HasWon(restored));
    }

    [Fact]
    public void PassSouthTrial_DepartBeforeDeadline_ReachesFamilyWinWithFullRoster()
    {
        var flags = ReachDecisionPoint();
        Assert.True(RadioMainline.CallSouth(flags));
        Assert.True(SouthTrial.RecordAnswer(flags, 2));
        Assert.True(SouthTrial.RecordAnswer(flags, 1));
        Assert.True(SouthTrial.RecordAnswer(flags, 2));
        Assert.True(SouthTrial.IsPassed(flags));
        Assert.True(SouthTrial.MarkPassed(flags));
        Assert.True(FamilyEscapeWin.MarkDeparted(flags));

        FamilyEscapeWin.RecordFamily(flags, new List<FamilyEscapeWin.Member>
        {
            new("山姆", "1"),
            new("诺蒂", "2"),
            new("克莉丝汀", "3"),
        });

        var restored = new StoryFlags(flags.Snapshot());
        Assert.True(FamilyEscapeWin.HasWon(restored));
        Assert.True(FamilyEscapeWin.HasDeparted(restored));
        Assert.Equal(3, FamilyEscapeWin.RosterCount(restored));
        Assert.False(SouthEscapeEnding.IsSequenceActive(restored));
        Assert.False(RadioMainline.TryFireMilitaryRaidHook(restored, HordeTimeline.DeadlineDay));
        Assert.False(HordeTimeline.ShouldTriggerSiege(
            HordeTimeline.DeadlineDay,
            sighted: true,
            endgameFrozen: restored.Has(HordeTimeline.EndgameFreezeFlag)));
    }

    [Fact]
    public void FailSouthTrial_PermanentlyClosesSouth_AndReopensBothBadEndings()
    {
        var military = FailedSouthTrial();
        Assert.True(RadioMainline.ReplyToMilitary(military, currentDay: 20));
        Assert.True(RadioMainline.TryFireMilitaryRaidHook(
            military, 20 + RadioMainline.MilitaryRaidDelayDays));

        var horde = FailedSouthTrial();
        Assert.False(horde.Has(HordeTimeline.EndgameFreezeFlag));
        Assert.True(HordeTimeline.ShouldTriggerSiege(
            HordeTimeline.DeadlineDay,
            sighted: false,
            endgameFrozen: horde.Has(HordeTimeline.EndgameFreezeFlag)));
    }

    [Fact]
    public void CampMain_WiresAllThreeOutcomesToRealPhaseAndRuntimeEntrypoints()
    {
        string camp = Source("godot/scripts/CampMain.cs");
        string badEnding = Source("godot/scripts/CampMain.SouthEscape.cs");
        string winEnding = Source("godot/scripts/CampMain.FamilyEscape.cs");

        Assert.Contains("case DayPhase.DawnMeal:", camp);
        Assert.Contains("if (TryTriggerMilitaryRaid())", camp);
        Assert.Contains("RadioMainline.TryFireMilitaryRaidHook(_storyFlags, _clock.Day)", camp);
        Assert.Contains("BeginSouthEscapeEnding(escapee, SouthEscapeTrigger.MilitaryRaid)", camp);

        Assert.Contains("case DayPhase.NightAct:", camp);
        Assert.Contains("HordeTimeline.ShouldTriggerSiege(", camp);
        Assert.Contains("TryTriggerHordeSiegeEnding();", camp);
        Assert.Contains("BeginSouthEscapeEnding(escapee, SouthEscapeTrigger.HordeSiege)", badEnding);

        Assert.Contains("RadioMainline.ReopenAfterSouthFailure(_storyFlags)", camp);
        Assert.Contains("FamilyEscapeWin.MarkDeparted(_storyFlags)", winEnding);
        Assert.Contains("if (BeginFamilyEscapeWin())", camp);
        Assert.Contains("FamilyEscapeWin.RecordFamily(_storyFlags, roster)", winEnding);
        Assert.Contains("EndingPanel.Show(_hud, FamilyEscapeWin.WinCg()", winEnding);
    }

    [Fact]
    public void CampMain_TerminalSequencesSuppressAutosave_AndMilitaryNarrationIsActuallyConsumed()
    {
        string camp = Source("godot/scripts/CampMain.SouthEscape.cs");
        string save = Source("godot/scripts/CampMain.Save.cs");

        // 终局入口先锁 _gameOver；同一昼夜边界末尾即使走到 AutosaveOnPhaseChange，也会直接返回。
        Assert.Contains("_gameOver = true", camp);
        Assert.Contains("if (_gameOver || !SaveRotation.ShouldAutosaveAt(phase))", save);

        // 军袭 CG-A 的 authored 六段旁白已有真实消费者；尸潮没有适配新结局的专用旁白，保持无字幕演出。
        Assert.Contains("? EndingCg.MilitaryRaidMassacre", camp);
        Assert.Contains(": Array.Empty<string>()", camp);
        Assert.Contains("PlayOpeningNarration(openingNarration", camp);
    }

    [Fact]
    public void CampMain_DoesNotLockOrConsumeAnEndingWhenNobodyCanDepart()
    {
        string camp = Source("godot/scripts/CampMain.cs");
        string family = Source("godot/scripts/CampMain.FamilyEscape.cs");

        int militaryMethod = camp.IndexOf("private bool TryTriggerMilitaryRaid()", System.StringComparison.Ordinal);
        int militaryAlive = camp.IndexOf("var alive = _survivors.Where(s => s.Alive).ToList();", militaryMethod, System.StringComparison.Ordinal);
        int militaryFire = camp.IndexOf("RadioMainline.TryFireMilitaryRaidHook", militaryMethod, System.StringComparison.Ordinal);
        Assert.True(militaryMethod >= 0 && militaryAlive > militaryMethod && militaryFire > militaryAlive,
            "军袭必须先确认存在南逃者，再消费一次性到期钩子。");

        int familyMethod = family.IndexOf("private bool BeginFamilyEscapeWin()", System.StringComparison.Ordinal);
        int familyAlive = family.IndexOf("var family = _survivors.Where", familyMethod, System.StringComparison.Ordinal);
        int familyEmptyGuard = family.IndexOf("if (family.Count == 0)", familyMethod, System.StringComparison.Ordinal);
        int familyDeparted = family.IndexOf("FamilyEscapeWin.MarkDeparted(_storyFlags)", familyMethod, System.StringComparison.Ordinal);
        int familyLock = family.IndexOf("_southEscapeActive = true", familyMethod, System.StringComparison.Ordinal);
        Assert.True(familyMethod >= 0 && familyAlive > familyMethod && familyEmptyGuard > familyAlive
                    && familyDeparted > familyEmptyGuard && familyLock > familyDeparted,
            "举家南逃必须先确认至少一名存活者，再消费启程 flag 并锁定终局状态。");
    }

    private static StoryFlags ReachDecisionPoint()
    {
        var flags = new StoryFlags();
        Assert.True(RadioMainline.MarkBroadcastHeard(flags));
        Assert.True(RadioMainline.GrantTransmitter(flags));
        Assert.Equal(RadioMainlineStage.HasTransmitter, RadioMainline.Stage(flags));
        Assert.True(flags.Has(HordeTimeline.EndgameFreezeFlag));
        return flags;
    }

    private static StoryFlags FailedSouthTrial()
    {
        var flags = ReachDecisionPoint();
        Assert.True(RadioMainline.CallSouth(flags));
        Assert.True(SouthTrial.RecordAnswer(flags, 1));
        Assert.True(SouthTrial.RecordAnswer(flags, 1));
        Assert.True(SouthTrial.RecordAnswer(flags, 1));
        Assert.True(SouthTrial.IsFailed(flags));
        Assert.True(RadioMainline.ReopenAfterSouthFailure(flags));
        Assert.Equal(RadioMainlineStage.HasTransmitter, RadioMainline.Stage(flags));
        Assert.True(RadioMainline.IsSouthRefused(flags));
        Assert.False(RadioMainline.CallSouth(flags));
        return flags;
    }

    private static string Source(string relativePath, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        return File.ReadAllText(Path.Combine(root, relativePath));
    }
}
