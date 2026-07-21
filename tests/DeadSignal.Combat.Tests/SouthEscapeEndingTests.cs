using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「南逃谢幕」结局序列纯逻辑内核（<see cref="SouthEscapeEnding"/>）：随机半残南逃者选择（注入 rng 复现）、
/// 南逃者身份持久化（存档 Snapshot 往返）、触发上下文、序列态 flag。REUSABLE 入口的逻辑侧护栏。
/// </summary>
public sealed class SouthEscapeEndingTests
{
    // —— 随机南逃者选择 ——

    [Fact]
    public void SelectEscapee_EmptyList_ReturnsDefault()
    {
        var rng = new SequenceRandomSource();
        Assert.Null(SouthEscapeEnding.SelectEscapee(new List<string>(), rng));
    }

    [Fact]
    public void SelectEscapee_SingleSurvivor_ReturnsIt_NoRoll()
    {
        var rng = new SequenceRandomSource(); // 单人不 roll，序列可空
        Assert.Equal("山姆", SouthEscapeEnding.SelectEscapee(new List<string> { "山姆" }, rng));
    }

    [Theory]
    [InlineData(0.0, "山姆")]
    [InlineData(1.5, "诺蒂")]
    [InlineData(2.9, "克莉丝汀")] // (int)2.9=2，末位
    public void SelectEscapee_PicksByInjectedRoll(double roll, string expected)
    {
        var survivors = new List<string> { "山姆", "诺蒂", "克莉丝汀" };
        var rng = new SequenceRandomSource(roll);
        Assert.Equal(expected, SouthEscapeEnding.SelectEscapee(survivors, rng));
    }

    [Fact]
    public void SelectEscapee_RollAtUpperBound_ClampsToLast()
    {
        var survivors = new List<string> { "A", "B", "C" };
        // Range(0,3) 上界 3.0 落 (int)=3 越界 → 钳到末位（防 rng 极端返回上界）。
        var rng = new SequenceRandomSource(3.0);
        Assert.Equal("C", SouthEscapeEnding.SelectEscapee(survivors, rng));
    }

    // —— 身份持久化（存档 Snapshot 往返）——

    [Fact]
    public void RecordEscapee_ThenReadBack()
    {
        var flags = new StoryFlags();
        SouthEscapeEnding.RecordEscapee(flags, "山姆", "pawn_sam", SouthEscapeTrigger.MilitaryRaid);

        Assert.True(SouthEscapeEnding.HasEscapee(flags));
        Assert.True(SouthEscapeEnding.IsSequenceActive(flags));
        Assert.Equal("山姆", SouthEscapeEnding.EscapeeName(flags));
        Assert.Equal("pawn_sam", SouthEscapeEnding.EscapeeId(flags));
        Assert.Equal(SouthEscapeTrigger.MilitaryRaid, SouthEscapeEnding.TriggerOf(flags));
    }

    [Fact]
    public void RecordEscapee_SurvivesSnapshotRestore()
    {
        var flags = new StoryFlags();
        SouthEscapeEnding.RecordEscapee(flags, "诺蒂", "pawn_notty", SouthEscapeTrigger.HordeSiege);

        // 存档往返：Snapshot → new StoryFlags(snapshot)，南逃者身份/触发/序列态全部留存（第二幕桥梁角色接口）。
        var restored = new StoryFlags(flags.Snapshot());
        Assert.Equal("诺蒂", SouthEscapeEnding.EscapeeName(restored));
        Assert.Equal("pawn_notty", SouthEscapeEnding.EscapeeId(restored));
        Assert.Equal(SouthEscapeTrigger.HordeSiege, SouthEscapeEnding.TriggerOf(restored));
        Assert.True(SouthEscapeEnding.IsSequenceActive(restored));
    }

    [Fact]
    public void RecordEscapee_FirstTerminalOutcomeWinsAcrossSaveRestore()
    {
        var flags = new StoryFlags();
        SouthEscapeEnding.RecordEscapee(flags, "山姆", "pawn_sam", SouthEscapeTrigger.MilitaryRaid);

        // 模拟读档后旧入口再次被调用：终局身份与触发源必须保持首次结果，不能从军袭串成尸潮。
        var restored = new StoryFlags(flags.Snapshot());
        SouthEscapeEnding.RecordEscapee(restored, "诺蒂", "pawn_notty", SouthEscapeTrigger.HordeSiege);

        Assert.Equal("山姆", SouthEscapeEnding.EscapeeName(restored));
        Assert.Equal("pawn_sam", SouthEscapeEnding.EscapeeId(restored));
        Assert.Equal(SouthEscapeTrigger.MilitaryRaid, SouthEscapeEnding.TriggerOf(restored));
    }

    [Fact]
    public void RecordEscapee_CannotOverwriteFamilyDepartureOrWin()
    {
        var flags = new StoryFlags();
        Assert.True(FamilyEscapeWin.MarkDeparted(flags));
        FamilyEscapeWin.RecordFamily(flags, new[] { new FamilyEscapeWin.Member("克莉丝汀", "3") });

        SouthEscapeEnding.RecordEscapee(flags, "山姆", "pawn_sam", SouthEscapeTrigger.HordeSiege);

        Assert.True(FamilyEscapeWin.HasWon(flags));
        Assert.False(SouthEscapeEnding.IsSequenceActive(flags));
        Assert.False(SouthEscapeEnding.HasEscapee(flags));
    }

    // —— 第 40 天尸潮终局路由（复用回归锁：CampMain.TryTriggerHordeSiegeEnding 的组合决策）——

    [Fact]
    public void HordeSiegeEnding_SelectsCrippledEscapee_AndRecordsHordeSiegeTrigger()
    {
        // 第 40 天尸潮到期终局：复用 SelectEscapee 选半残南逃者 + RecordEscapee 打 HordeSiege 触发。
        // 与军袭同一套单角色南逃谢幕，唯一区别＝触发源 HordeSiege（≠ MilitaryRaid），CG-A 施暴方由它分叉成丧尸。
        var alive = new List<string> { "山姆", "诺蒂", "克莱夫" };
        var rng = new SequenceRandomSource(1.5); // (int)1.5=1 → 诺蒂
        string escapee = SouthEscapeEnding.SelectEscapee(alive, rng)!;
        Assert.Equal("诺蒂", escapee);

        var flags = new StoryFlags();
        SouthEscapeEnding.RecordEscapee(flags, escapee, "pawn_" + escapee, SouthEscapeTrigger.HordeSiege);
        Assert.Equal(SouthEscapeTrigger.HordeSiege, SouthEscapeEnding.TriggerOf(flags));
        Assert.NotEqual(SouthEscapeTrigger.MilitaryRaid, SouthEscapeEnding.TriggerOf(flags)!.Value);
        Assert.Equal("诺蒂", SouthEscapeEnding.EscapeeName(flags));
        Assert.True(SouthEscapeEnding.IsSequenceActive(flags));
    }

    [Fact]
    public void HordeSiegeEnding_NoSurvivors_SelectsNone_NoRouting()
    {
        // 无存活幸存者 → SelectEscapee 返 default → TryTriggerHordeSiegeEnding 兜底不启动（全灭另有 GameOverCondition 路由）。
        var rng = new SequenceRandomSource();
        Assert.Null(SouthEscapeEnding.SelectEscapee(new List<string>(), rng));
    }

    [Fact]
    public void RecordEscapee_NullId_LeavesIdUnset()
    {
        var flags = new StoryFlags();
        SouthEscapeEnding.RecordEscapee(flags, "无名者", null, SouthEscapeTrigger.MilitaryRaid);
        Assert.Equal("无名者", SouthEscapeEnding.EscapeeName(flags));
        Assert.Null(SouthEscapeEnding.EscapeeId(flags));
        Assert.True(SouthEscapeEnding.HasEscapee(flags));
    }

    [Fact]
    public void FreshFlags_NoEscapee_NoSequence()
    {
        var flags = new StoryFlags();
        Assert.False(SouthEscapeEnding.HasEscapee(flags));
        Assert.False(SouthEscapeEnding.IsSequenceActive(flags));
        Assert.Null(SouthEscapeEnding.EscapeeName(flags));
        Assert.Null(SouthEscapeEnding.TriggerOf(flags));
    }

    [Fact]
    public void NullFlags_Tolerated()
    {
        SouthEscapeEnding.RecordEscapee(null!, "x", "y", SouthEscapeTrigger.MilitaryRaid); // 不抛
        Assert.False(SouthEscapeEnding.HasEscapee(null!));
        Assert.Null(SouthEscapeEnding.EscapeeName(null!));
        Assert.Null(SouthEscapeEnding.TriggerOf(null!));
    }
}
