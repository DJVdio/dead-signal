using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 「首次触发一行提示」框架纯逻辑单测：id → 一行文案 + StoryFlags 背书的一次性去重。
// 全部脱 Godot：只用 StoryFlags + HintTracker 的纯字符串状态。文案是 draft（用户改），断言只查"非空/一行/去重语义"。
public class HintTrackerTests
{
    [Fact]
    public void ShouldShow_FirstTime_TrueThenFalseAfterMark()
    {
        var f = new StoryFlags();
        Assert.True(HintTracker.ShouldShow(HintTracker.Injury, f));

        HintTracker.MarkShown(HintTracker.Injury, f);
        Assert.False(HintTracker.ShouldShow(HintTracker.Injury, f));
    }

    [Fact]
    public void MarkShown_IsIdempotent()
    {
        var f = new StoryFlags();
        HintTracker.MarkShown(HintTracker.CraftOrder, f);
        int after1 = f.Count;
        HintTracker.MarkShown(HintTracker.CraftOrder, f);
        Assert.Equal(after1, f.Count); // 重复标记不新增 flag
        Assert.False(HintTracker.ShouldShow(HintTracker.CraftOrder, f));
    }

    [Fact]
    public void MarkShown_OnlyAffectsThatHint()
    {
        var f = new StoryFlags();
        HintTracker.MarkShown(HintTracker.Merchant, f);

        Assert.False(HintTracker.ShouldShow(HintTracker.Merchant, f));
        // 其余提示互不影响
        Assert.True(HintTracker.ShouldShow(HintTracker.Reading, f));
        Assert.True(HintTracker.ShouldShow(HintTracker.Nightfall, f));
    }

    [Fact]
    public void FlagKey_IsHintPrefixed_AndPersistsInSnapshot()
    {
        var f = new StoryFlags();
        HintTracker.MarkShown(HintTracker.GuardShift, f);

        Assert.Equal("hint_guard_shift", HintTracker.FlagFor(HintTracker.GuardShift));
        Assert.True(f.Has(HintTracker.FlagFor(HintTracker.GuardShift)));
        // 存档快照里可见（随 StoryFlags 一并持久）
        Assert.True(f.Snapshot().ContainsKey(HintTracker.FlagFor(HintTracker.GuardShift)));
    }

    [Fact]
    public void UnknownId_NeverShows_AndMarkIsNoop()
    {
        var f = new StoryFlags();
        Assert.False(HintTracker.ShouldShow("no_such_hint", f));
        Assert.Null(HintTracker.Text("no_such_hint"));

        HintTracker.MarkShown("no_such_hint", f);
        Assert.Equal(0, f.Count); // 未知 id 不污染 flag 空间
    }

    [Fact]
    public void NullFlags_TreatedAsNeverShown_ForKnownId()
    {
        Assert.True(HintTracker.ShouldShow(HintTracker.Infection, null!));
        // null flags 下 MarkShown 空操作、不抛
        HintTracker.MarkShown(HintTracker.Infection, null!);
    }

    [Fact]
    public void EveryHintId_HasOneLineNonEmptyText()
    {
        foreach (var id in HintTracker.AllIds)
        {
            string? text = HintTracker.Text(id);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain('\n', text!); // 单行提示：不含换行
        }
    }

    [Fact]
    public void AllEightSystemHints_ArePresent()
    {
        var ids = HintTracker.AllIds.ToHashSet();
        foreach (var expected in new[]
        {
            HintTracker.Injury, HintTracker.Infection, HintTracker.Fracture, HintTracker.CraftOrder,
            HintTracker.Nightfall, HintTracker.GuardShift, HintTracker.Merchant, HintTracker.Reading,
        })
        {
            Assert.Contains(expected, ids);
        }
    }
}
