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
    [InlineData(SizeTier.Small, "小")]
    [InlineData(SizeTier.Medium, "中")]
    [InlineData(SizeTier.Large, "大")]
    public void TierLabel_MapsEachTier(SizeTier tier, string expected)
    {
        Assert.Equal(expected, ExplorationProgress.TierLabel(tier));
    }

    [Fact]
    public void RangersCabin_HasSixPoints_GordonPlusFiveCaches()
    {
        // [SPEC-B12] 守林人小屋 2→5 物资搜刮点 + 哥顿上吊尸 = 6 处登记点（小点配额带下限，密度仍克制）。
        var flags = ExplorationProgress.PointFlagsFor(ExplorationProgress.WatchersCabinName, christineLeftForRevenge: false);
        Assert.Equal(6, flags.Count);
        Assert.Contains(GoldfingerDiscovery.GordonHangedFlag, flags);
        Assert.Contains(ExplorationCache.RangersCabinPantryFlag, flags);
        Assert.Contains(ExplorationCache.RangersCabinShedFlag, flags);
        Assert.Contains(ExplorationCache.RangersCabinAtticFlag, flags);
        Assert.Contains(ExplorationCache.RangersCabinUnderbedFlag, flags);
        Assert.Contains(ExplorationCache.RangersCabinPorchFlag, flags);
    }

    [Fact]
    public void RangersCabin_Completion_CountsSetFlags()
    {
        var story = new StoryFlags();
        Assert.Equal((0, 6), ExplorationProgress.Completion(ExplorationProgress.WatchersCabinName, story, false));

        story.Set(GoldfingerDiscovery.GordonHangedFlag, "true"); // 发现哥顿上吊尸
        Assert.Equal((1, 6), ExplorationProgress.Completion(ExplorationProgress.WatchersCabinName, story, false));

        story.Set(ExplorationCache.RangersCabinPantryFlag, "true");
        story.Set(ExplorationCache.RangersCabinShedFlag, "true");
        Assert.Equal((3, 6), ExplorationProgress.Completion(ExplorationProgress.WatchersCabinName, story, false));
    }

    [Fact]
    public void GoldfingerBase_RevengeLine_AddsChristineCorpsePoint()
    {
        // [SPEC-B12-补] 金手指帮改中型·铺 11 物资点：登记点＝剧情尸体点 + 11 物资搜刮点。
        // 非复仇线：帮众尸体 1 + 物资 11 = 12。
        var noRevenge = ExplorationProgress.PointFlagsFor(ExplorationProgress.GoldfingerBaseName, christineLeftForRevenge: false);
        Assert.Equal(1 + 11, noRevenge.Count);
        Assert.Contains(GoldfingerDiscovery.GangMemberCorpseFlag, noRevenge);
        Assert.DoesNotContain(GoldfingerDiscovery.ChristineCorpseFlag, noRevenge);

        // 复仇线：另加克莉丝汀本人尸体点 → 2 尸体 + 11 物资 = 13。
        var revenge = ExplorationProgress.PointFlagsFor(ExplorationProgress.GoldfingerBaseName, christineLeftForRevenge: true);
        Assert.Equal(2 + 11, revenge.Count);
        Assert.Contains(GoldfingerDiscovery.ChristineCorpseFlag, revenge);
        // 剧情尸体点与物资点命名空间独立、共存不干扰。
        Assert.Contains(ExplorationCache.GoldfingerArmoryFlag, revenge);
    }

    [Fact]
    public void GoldfingerBase_IsMediumTier_ElevenMaterialCaches()
    {
        // 中型点=11 物资搜刮点（band 10~30 下限）。
        Assert.Equal(11, ExplorationCache.CacheIdsFor(ExplorationCache.GoldfingerBaseName).Count);
        // 完成度＝尸体点 + 物资点（非复仇线 1+11）。
        var (done, total) = ExplorationProgress.Completion(ExplorationProgress.GoldfingerBaseName, new StoryFlags(), false);
        Assert.Equal(0, done);
        Assert.Equal(12, total);
    }

    [Fact]
    public void RiversideCabin_HasFiveCachePoints()
    {
        // [SPEC-B12] 河边小屋 2→5（小点配额带下限）。
        Assert.Equal((0, 5), ExplorationProgress.Completion(ExplorationCache.RiversideCabinName, new StoryFlags(), false));
    }

    [Fact]
    public void Lookout_CountsOnlyCaches_TelescopeExcluded()
    {
        // 望远镜是主线触发点（置 HordeSighted），刻意不计入完成度：[SPEC-B12] 扩至 5 处物资搜刮点。
        Assert.Equal((0, 5), ExplorationProgress.Completion(ExplorationCache.CityRooftopLookoutName, new StoryFlags(), false));
    }

    [Fact]
    public void BackgroundDestination_NoRegisteredPoints_TotalZero()
    {
        // 无登记调查点的目的地（未建真探索关的字符串）→ total=0，调用方据此不显示完成度。
        // 注：超市/医院等已在 [SPEC-B13] 落真探索关+搜刮点，改用一个确实未登记的名字验证空态路径。
        Assert.Equal((0, 0), ExplorationProgress.Completion("不存在的目的地", new StoryFlags(), false));
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
