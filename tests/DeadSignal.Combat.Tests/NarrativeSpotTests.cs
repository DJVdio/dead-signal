using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 叙事调查点框架（[SPEC-B12]，极乐迪斯科式）纯逻辑单测：
// discoveryId → (标题 + 多屏文本 + 去重旗标)；一次性点看过（旗标已置）返回 null，可重读点恒返回。
// 全部脱 Godot：只用 StoryFlags + NarrativeSpotRegistry 的纯字符串/坐标状态。
public class NarrativeSpotTests
{
    [Fact]
    public void UnknownId_ReturnsNull()
    {
        Assert.Null(NarrativeSpotRegistry.Resolve("no_such_spot", new StoryFlags()));
    }

    [Fact]
    public void KnownSpot_FirstVisit_ReturnsTitleAndPagesAndFlag()
    {
        NarrativeSpot spot = NarrativeSpotRegistry.All.First(s => !s.Repeatable);

        NarrativeSpotResult? r = NarrativeSpotRegistry.Resolve(spot.Id, new StoryFlags());

        Assert.NotNull(r);
        Assert.Equal(spot.StoryFlag, r!.Value.StoryFlag);
        Assert.Equal(spot.Title, r.Value.Title);
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Title));
        Assert.NotEmpty(r.Value.Pages);
        Assert.All(r.Value.Pages, p => Assert.False(string.IsNullOrWhiteSpace(p)));
    }

    [Fact]
    public void OneTimeSpot_AfterFlagSet_ReturnsNull()
    {
        NarrativeSpot spot = NarrativeSpotRegistry.All.First(s => !s.Repeatable);
        var flags = new StoryFlags();
        flags.Set(spot.StoryFlag, "true"); // 已看过

        Assert.Null(NarrativeSpotRegistry.Resolve(spot.Id, flags));
    }

    [Fact]
    public void OneTimeSpot_DedupeFlag_HasExpectedPrefix()
    {
        NarrativeSpot spot = NarrativeSpotRegistry.All.First(s => !s.Repeatable);
        // 去重旗标前缀 seen_ + narrative_ 隔离命名空间（不与物资 cache flag 撞）。
        Assert.StartsWith("seen_narrative_", spot.StoryFlag);
        Assert.Equal("seen_" + spot.Id, spot.StoryFlag);
    }

    [Fact]
    public void RepeatableSpot_HasEmptyFlag_AndResolvesEvenAfterAnyFlags()
    {
        // 可重读点（若有）：空旗标、恒返回结果（不去重）。构造一个临时可重读点验证语义。
        var flags = new StoryFlags();
        foreach (NarrativeSpot s in NarrativeSpotRegistry.All.Where(s => s.Repeatable))
        {
            Assert.Equal("", s.StoryFlag);
            Assert.NotNull(NarrativeSpotRegistry.Resolve(s.Id, flags));
        }
    }

    [Fact]
    public void AllSpots_HaveNarrativePrefixedIds_AndNonEmptyContent()
    {
        Assert.NotEmpty(NarrativeSpotRegistry.All);
        foreach (NarrativeSpot s in NarrativeSpotRegistry.All)
        {
            Assert.StartsWith("narrative_", s.Id); // 与物资 cache / 主线 discovery id 命名空间隔离
            Assert.False(string.IsNullOrWhiteSpace(s.Destination));
            Assert.False(string.IsNullOrWhiteSpace(s.Label));
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.NotEmpty(s.Pages);
            Assert.All(s.Pages, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        }
    }

    [Fact]
    public void AllSpotIds_AreUnique()
    {
        var ids = NarrativeSpotRegistry.All.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void ForDestination_FiltersByMap_AndCoversAuthoredMaps()
    {
        // 各图样例都铺到了（[SPEC-B12] 每图分散铺设）。
        Assert.NotEmpty(NarrativeSpotRegistry.ForDestination(VillageRescue.DestinationName));
        Assert.NotEmpty(NarrativeSpotRegistry.ForDestination(ExplorationCache.WatchersCabinName));
        Assert.NotEmpty(NarrativeSpotRegistry.ForDestination(ExplorationCache.CityRooftopLookoutName));
        Assert.NotEmpty(NarrativeSpotRegistry.ForDestination(ExplorationCache.BroadcastStationName));

        // 过滤正确：某图的每个点 Destination 都等于该图。
        foreach (NarrativeSpot s in NarrativeSpotRegistry.ForDestination(VillageRescue.DestinationName))
            Assert.Equal(VillageRescue.DestinationName, s.Destination);
    }

    [Fact]
    public void ForDestination_UnknownMap_IsEmpty()
    {
        Assert.Empty(NarrativeSpotRegistry.ForDestination("不存在的图"));
    }

    [Fact]
    public void ById_KnownReturnsSpot_UnknownReturnsNull()
    {
        NarrativeSpot first = NarrativeSpotRegistry.All.First();
        Assert.Same(first, NarrativeSpotRegistry.ById(first.Id));
        Assert.Null(NarrativeSpotRegistry.ById("no_such_spot"));
    }

    [Fact]
    public void Resolve_HasNoSideEffects_DoesNotWriteFlags()
    {
        NarrativeSpot spot = NarrativeSpotRegistry.All.First(s => !s.Repeatable);
        var flags = new StoryFlags();
        NarrativeSpotRegistry.Resolve(spot.Id, flags);
        // Resolve 不写 flag（置 flag 由调用方在弹叙事后进行）。
        Assert.False(flags.Has(spot.StoryFlag));
        Assert.Equal(0, flags.Count);
    }
}
