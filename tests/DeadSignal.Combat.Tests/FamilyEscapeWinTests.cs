using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 「举家南逃 WIN」好结局纯逻辑（family-escape-win）先红后绿护栏：
//   · 通过（IsPassed）→ 举家 WIN 路由（HasWon，非坏结局南逃谢幕 SequenceActive）；
//   · 全营名单存档往返（Snapshot → 新 StoryFlags 仍一致，含中文名 + 空 id 位）；
//   · WIN 与坏结局 outcome 互斥可区分；
//   · 启程一次性去重；WIN CG 文本非空且不复用坏结局措辞。
public sealed class FamilyEscapeWinTests
{
    private static StoryFlags Passed()
    {
        // 三题各满分 → 总分 6 ≥ 5，答满三问 → IsPassed。
        var f = new StoryFlags();
        SouthTrial.RecordAnswer(f, 2);
        SouthTrial.RecordAnswer(f, 2);
        SouthTrial.RecordAnswer(f, 2);
        return f;
    }

    private static List<FamilyEscapeWin.Member> SampleRoster() => new()
    {
        new FamilyEscapeWin.Member("山姆", "1"),
        new FamilyEscapeWin.Member("诺蒂", "2"),
        new FamilyEscapeWin.Member("克莉丝汀", null), // 无 id 位（仅靠名字）
        new FamilyEscapeWin.Member("皮特", "7"),
    };

    // —— 路由：三问通过 → 举家 WIN（非坏结局）——

    [Fact]
    public void PassedTrial_RoutesToFamilyWin_NotBadEnding()
    {
        var f = Passed();
        Assert.True(SouthTrial.IsPassed(f)); // 前提：满 5 分通过

        FamilyEscapeWin.RecordFamily(f, SampleRoster());

        Assert.True(FamilyEscapeWin.HasWon(f));                    // 好结局 outcome 已达成
        Assert.False(SouthEscapeEnding.IsSequenceActive(f));      // 未走坏结局南逃谢幕
        Assert.False(SouthEscapeEnding.HasEscapee(f));            // 未记录单个半残南逃者
    }

    // —— WIN 与坏结局 outcome 互斥可区分（flag 命名空间独立）——

    [Fact]
    public void FamilyWin_And_BadEnding_AreDistinguishable()
    {
        var win = new StoryFlags();
        FamilyEscapeWin.RecordFamily(win, SampleRoster());
        Assert.True(FamilyEscapeWin.HasWon(win));
        Assert.False(SouthEscapeEnding.IsSequenceActive(win));

        var bad = new StoryFlags();
        SouthEscapeEnding.RecordEscapee(bad, "山姆", "1", SouthEscapeTrigger.MilitaryRaid);
        Assert.True(SouthEscapeEnding.IsSequenceActive(bad));
        Assert.False(FamilyEscapeWin.HasWon(bad));
    }

    [Fact]
    public void BadEnding_LocksOutFamilyDepartureAndWin()
    {
        var flags = new StoryFlags();
        SouthEscapeEnding.RecordEscapee(flags, "山姆", "1", SouthEscapeTrigger.MilitaryRaid);

        Assert.False(FamilyEscapeWin.MarkDeparted(flags));
        FamilyEscapeWin.RecordFamily(flags, SampleRoster());

        Assert.True(SouthEscapeEnding.IsSequenceActive(flags));
        Assert.False(FamilyEscapeWin.HasDeparted(flags));
        Assert.False(FamilyEscapeWin.HasWon(flags));
        Assert.Empty(FamilyEscapeWin.Roster(flags));
    }

    // —— 全营名单持久化：存档往返（Snapshot → 新 StoryFlags）——

    [Fact]
    public void Roster_RoundTripsThroughSaveSnapshot()
    {
        var roster = SampleRoster();
        var f = new StoryFlags();
        FamilyEscapeWin.RecordFamily(f, roster);

        // 存档往返：导出快照 → 恢复到新 StoryFlags（仿 CampMain.Save 恢复口径）。
        var restored = new StoryFlags(f.Snapshot());

        var got = FamilyEscapeWin.Roster(restored);
        Assert.Equal(roster.Count, got.Count);
        for (int i = 0; i < roster.Count; i++)
        {
            Assert.Equal(roster[i].Name, got[i].Name);
            Assert.Equal(roster[i].Id, got[i].Id); // 含 null id 位保真
        }
        Assert.Equal(4, FamilyEscapeWin.RosterCount(restored));
        Assert.True(FamilyEscapeWin.HasWon(restored));
    }

    [Fact]
    public void Roster_EmptyFlags_YieldsEmptyRoster()
    {
        var f = new StoryFlags();
        Assert.Empty(FamilyEscapeWin.Roster(f));
        Assert.Equal(0, FamilyEscapeWin.RosterCount(f));
        Assert.False(FamilyEscapeWin.HasWon(f));
    }

    [Fact]
    public void RecordFamily_EmptyRoster_DoesNotMarkWon()
    {
        var f = new StoryFlags();
        FamilyEscapeWin.RecordFamily(f, new List<FamilyEscapeWin.Member>());
        Assert.False(FamilyEscapeWin.HasWon(f)); // 无人可逃 ≠ WIN 达成
    }

    // —— 启程一次性去重 ——

    [Fact]
    public void MarkDeparted_IsOneShot()
    {
        var f = new StoryFlags();
        Assert.False(FamilyEscapeWin.HasDeparted(f));
        Assert.True(FamilyEscapeWin.MarkDeparted(f));  // 首次
        Assert.True(FamilyEscapeWin.HasDeparted(f));
        Assert.False(FamilyEscapeWin.MarkDeparted(f)); // 其后恒 false
    }

    // —— WIN CG 文本：非空、不复用坏结局措辞 ——

    [Fact]
    public void WinCg_IsNonEmpty_AndDoesNotReuseBadEndingLines()
    {
        var cg = FamilyEscapeWin.WinCg();
        Assert.NotEmpty(cg);
        Assert.All(cg, s => Assert.False(string.IsNullOrWhiteSpace(s)));

        // 不得复用坏结局措辞（CG③「活下来的没剩几个」/ CG-B「大桥没有落下」）。
        Assert.DoesNotContain(cg, s => s.Contains("活下来的没剩几个"));
        Assert.DoesNotContain(cg, s => s.Contains("大桥没有落下"));
        // 正面节拍应含大桥落下/被迎接。
        Assert.Contains(cg, s => s.Contains("落了下来") || s.Contains("落下"));

        // 与坏结局文本互不相等（逐段）。
        Assert.Empty(cg.Intersect(EndingCg.SouthEscape));
        Assert.Empty(cg.Intersect(EndingCg.SouthEscapeFarewell));
    }
}
