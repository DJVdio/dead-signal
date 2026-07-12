using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 调查点规模三级 + 探索完成度聚合纯逻辑单测（用户 [SPEC-B11-补] 拍板）：
//   · TierLabel：小/中/大 → 含预计天数的克制文案；
//   · Completion：目的地登记点位集(物资搜刮+剧情尸体) vs StoryFlags 已置状态 → (已调查X, 总Y)，跨访持久由 flag 承载。
// 全部脱 Godot：只用 StoryFlags + ExplorationCache/GoldfingerDiscovery/ExplorationProgress 的纯字符串状态。
public class ExplorationProgressTests
{
    [Theory]
    [InlineData(SizeTier.Small, "小·约1-2天")]
    [InlineData(SizeTier.Medium, "中·约3-5天")]
    [InlineData(SizeTier.Large, "大·约5天+")]
    public void TierLabel_MapsEachTier(SizeTier tier, string expected)
    {
        Assert.Equal(expected, ExplorationProgress.TierLabel(tier));
    }

    [Fact]
    public void RangersCabin_HasThreePoints_GordonPlusTwoCaches()
    {
        var flags = ExplorationProgress.PointFlagsFor(ExplorationProgress.WatchersCabinName, christineLeftForRevenge: false);
        Assert.Equal(3, flags.Count);
        Assert.Contains(GoldfingerDiscovery.GordonHangedFlag, flags);
        Assert.Contains(ExplorationCache.RangersCabinPantryFlag, flags);
        Assert.Contains(ExplorationCache.RangersCabinShedFlag, flags);
    }

    [Fact]
    public void RangersCabin_Completion_CountsSetFlags()
    {
        var story = new StoryFlags();
        Assert.Equal((0, 3), ExplorationProgress.Completion(ExplorationProgress.WatchersCabinName, story, false));

        story.Set(GoldfingerDiscovery.GordonHangedFlag, "true"); // 发现哥顿上吊尸
        Assert.Equal((1, 3), ExplorationProgress.Completion(ExplorationProgress.WatchersCabinName, story, false));

        story.Set(ExplorationCache.RangersCabinPantryFlag, "true");
        story.Set(ExplorationCache.RangersCabinShedFlag, "true");
        Assert.Equal((3, 3), ExplorationProgress.Completion(ExplorationProgress.WatchersCabinName, story, false));
    }

    [Fact]
    public void GoldfingerBase_RevengeLine_AddsChristineCorpsePoint()
    {
        // 非复仇线：仅帮众尸体一处登记点。
        var noRevenge = ExplorationProgress.PointFlagsFor(ExplorationProgress.GoldfingerBaseName, christineLeftForRevenge: false);
        Assert.Single(noRevenge);
        Assert.Contains(GoldfingerDiscovery.GangMemberCorpseFlag, noRevenge);

        // 复仇线：另加克莉丝汀本人尸体点，总 2。
        var revenge = ExplorationProgress.PointFlagsFor(ExplorationProgress.GoldfingerBaseName, christineLeftForRevenge: true);
        Assert.Equal(2, revenge.Count);
        Assert.Contains(GoldfingerDiscovery.ChristineCorpseFlag, revenge);
    }

    [Fact]
    public void RiversideCabin_HasTwoCachePoints()
    {
        Assert.Equal((0, 2), ExplorationProgress.Completion(ExplorationCache.RiversideCabinName, new StoryFlags(), false));
    }

    [Fact]
    public void Lookout_CountsOnlyCaches_TelescopeExcluded()
    {
        // 望远镜是主线触发点（置 HordeSighted），刻意不计入完成度：仅两处物资搜刮点。
        Assert.Equal((0, 2), ExplorationProgress.Completion(ExplorationCache.CityRooftopLookoutName, new StoryFlags(), false));
    }

    [Fact]
    public void BackgroundDestination_NoRegisteredPoints_TotalZero()
    {
        // 无登记调查点的目的地（如超市）→ total=0，调用方据此不显示完成度。
        Assert.Equal((0, 0), ExplorationProgress.Completion("超市", new StoryFlags(), false));
    }

    [Fact]
    public void FlagForCache_RoundTripsKnownIds_EmptyForUnknown()
    {
        Assert.Equal(ExplorationCache.RangersCabinPantryFlag, ExplorationCache.FlagForCache(ExplorationCache.RangersCabinPantryId));
        Assert.Equal(ExplorationCache.RiversideGunCabinetFlag, ExplorationCache.FlagForCache(ExplorationCache.RiversideGunCabinetId));
        Assert.Equal("", ExplorationCache.FlagForCache("no_such_cache"));
    }

    [Fact]
    public void EveryCacheId_HasNonEmptyFlag()
    {
        // 全目的地的搜刮点 id 都必须能反查到 flag（防 FlagForCache 与 CacheIdsFor 脱节）。
        string[] destinations =
        {
            ExplorationCache.RiversideCabinName,
            ExplorationCache.HarvesterWarehouseName,
            ExplorationCache.CityRooftopLookoutName,
            ExplorationCache.BroadcastStationName,
            ExplorationCache.WatchersCabinName,
        };
        foreach (string dest in destinations)
        {
            foreach (string id in ExplorationCache.CacheIdsFor(dest))
                Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)), $"cache {id} 无 flag");
        }
    }
}
