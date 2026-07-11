using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 城市之巅瞭望观景台「望远镜瞭望尸潮」发现点解析纯逻辑单测：
// discoveryId → (置 HordeSighted 旗标 + 环境叙事)，已瞭望（旗标已置）返回 null 防重复演出/叙事。
// 全部脱 Godot：只用 StoryFlags + LookoutSighting 的纯字符串状态。
public class LookoutSightingTests
{
    [Fact]
    public void UnknownDiscoveryId_ReturnsNull()
    {
        Assert.Null(LookoutSighting.Resolve("no_such_point", new StoryFlags()));
    }

    [Fact]
    public void Telescope_FirstSight_SetsHordeSightedAndNarrativeNoBook()
    {
        DiscoveryResult? r = LookoutSighting.Resolve(LookoutSighting.TelescopeDiscoveryId, new StoryFlags());

        Assert.NotNull(r);
        Assert.Equal(LookoutSighting.HordeSightedFlag, r!.Value.StoryFlag);
        // 只置旗标+叙事，不给书。
        Assert.Equal(LookoutSighting.NoBookId, r.Value.BookId);
        Assert.True(string.IsNullOrEmpty(r.Value.BookId));
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Title));
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Narrative));
    }

    [Fact]
    public void AlreadySighted_ReturnsNull()
    {
        // HordeSighted 已置（已瞭望过）→ null，不重复演出/叙事。
        var f = new StoryFlags();
        f.Set(LookoutSighting.HordeSightedFlag, "true");
        Assert.Null(LookoutSighting.Resolve(LookoutSighting.TelescopeDiscoveryId, f));
    }

    [Fact]
    public void HordeSightedFlag_AlignsWithCoreTimerCanonical()
    {
        // 置位的旗标键必须等于 core-timer 的 canonical 常量（HUD/时间线只读它，字面量不一致则置位对它们不可见）。
        Assert.Equal(HordeTimeline.SightedFlag, LookoutSighting.HordeSightedFlag);
        Assert.Equal("horde_sighted", LookoutSighting.HordeSightedFlag);
    }

    [Fact]
    public void TelescopeDiscoveryId_MatchesLevelContract()
    {
        // 发现点 id 必须与关卡内触发上报的 id 一致（否则踏入望远镜零效果）。
        Assert.Equal("discovery_lookout_telescope", LookoutSighting.TelescopeDiscoveryId);
    }
}
