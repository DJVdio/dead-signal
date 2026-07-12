using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 南林村庄「大调查点」搜刮点纯逻辑单测（[SPEC-B11-补]：大点=5天+探索量，9 处搜刮点分区铺设）：
///   · CacheIdsFor(南林村庄) 出 9 处（对齐"大"内容量）；· 每处 id↔flag 双向一致；
///   · Resolve 首搜出掉落+叙事、复搜(flag 已置)去重返回 null；· 与救援主线点隔离(救援不进物资完成度)；
///   · ExplorationProgress.Completion 对南林村庄聚合 total=9、随已搜 flag 递增 done。
/// 数值/搭配皆"拟定待调"，测试锁的是规则形态与登记完整性。
/// </summary>
public class VillageCacheTests
{
    // [SPEC-B12] 大点 9→30（band 30+ 硬口径）：既有四分区加密 + 新增后山/河滩两分区，近入口→藏深序。
    private static readonly string[] ExpectedIds =
    {
        // 村口/杂物(3)
        ExplorationCache.VillageRoadsideCarId, ExplorationCache.VillageGatePostId, ExplorationCache.VillageTrikeId,
        // 民居区(9)
        ExplorationCache.VillageKitchenId, ExplorationCache.VillageWardrobeId, ExplorationCache.VillageBedroom2Id,
        ExplorationCache.VillageCourtyardId, ExplorationCache.VillageCoopId, ExplorationCache.VillagePantry2Id,
        ExplorationCache.VillageLoftId, ExplorationCache.VillageWoodpileId, ExplorationCache.VillageBackRoomId,
        // 村中心(6)
        ExplorationCache.VillageShopShelfId, ExplorationCache.VillageCoopStoreId, ExplorationCache.VillageBusStopId,
        ExplorationCache.VillageSchoolId, ExplorationCache.VillageWellToolboxId, ExplorationCache.VillageForgeId,
        // 村尾/藏深(6)
        ExplorationCache.VillageToolShedId, ExplorationCache.VillageBarnId, ExplorationCache.VillageBeehiveId,
        ExplorationCache.VillageGraveHutId, ExplorationCache.VillageShrineId, ExplorationCache.VillageClinicId,
        // 后山(3)
        ExplorationCache.VillageBackhillBlindId, ExplorationCache.VillageBackhillKilnId, ExplorationCache.VillageBackhillCaveId,
        // 河滩(3)
        ExplorationCache.VillageRiverbankBoatId, ExplorationCache.VillageRiverbankShackId, ExplorationCache.VillageRiverbankPumpId,
    };

    [Fact]
    public void CacheIdsFor_Village_ThirtyLargePointCaches()
    {
        var ids = ExplorationCache.CacheIdsFor(VillageRescue.DestinationName);
        Assert.Equal(30, ids.Count);                         // 大点=30+ 硬口径
        Assert.Equal(ExpectedIds, ids.ToArray());            // 且按近→深登记序（村口→民居→村中心→村尾→后山→河滩）
    }

    [Fact]
    public void FlagForCache_EveryVillageId_RoundTripsNonEmpty()
    {
        foreach (string id in ExpectedIds)
            Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)), $"{id} 缺 flag 映射");
    }

    [Fact]
    public void Resolve_EveryVillageCache_FirstSearchGivesLootAndNarrative_ThenDedup()
    {
        foreach (string id in ExpectedIds)
        {
            var flags = new StoryFlags();
            CacheResult? first = ExplorationCache.Resolve(id, flags);
            Assert.True(first != null, $"{id} 首搜应出结果");
            Assert.True(first!.Value.Loot.Count > 0, $"{id} 应有掉落");
            Assert.False(string.IsNullOrWhiteSpace(first.Value.Narrative), $"{id} 应有叙事");
            Assert.Equal(ExplorationCache.FlagForCache(id), first.Value.StoryFlag); // 落地 flag 与反查一致

            flags.Set(first.Value.StoryFlag, "true"); // 模拟调用方落地后置 flag
            Assert.Null(ExplorationCache.Resolve(id, flags)); // 复搜去重（跨访持久=空搜）
        }
    }

    [Fact]
    public void RescueDiscovery_NotCountedAsMaterialCache()
    {
        // 救援锁屋是主线入队触发点，不在物资搜刮点集内（同瞭望台望远镜口径）。
        Assert.DoesNotContain(VillageRescue.RescueDiscoveryId, ExplorationCache.CacheIdsFor(VillageRescue.DestinationName));
        Assert.Null(ExplorationCache.Resolve(VillageRescue.RescueDiscoveryId, new StoryFlags()));
    }

    [Fact]
    public void Completion_Village_TotalThirty_DoneRisesWithSearches()
    {
        var flags = new StoryFlags();
        var (done0, total) = ExplorationProgress.Completion(VillageRescue.DestinationName, flags, christineLeftForRevenge: false);
        Assert.Equal(30, total);   // 30 处登记（救援主线点不计）
        Assert.Equal(0, done0);

        // 搜掉三处（跨分区）→ done=3。
        flags.Set(ExplorationCache.VillageRoadsideCarFlag, "true");
        flags.Set(ExplorationCache.VillageKitchenFlag, "true");
        flags.Set(ExplorationCache.VillageBackhillCaveFlag, "true");
        var (done3, total3) = ExplorationProgress.Completion(VillageRescue.DestinationName, flags, christineLeftForRevenge: false);
        Assert.Equal(30, total3);
        Assert.Equal(3, done3);
    }

    [Fact]
    public void Village_IsLargeTier_Label()
    {
        // 南林村庄定级＝大（site-tiers 代填 Tier=Large；此处锁文案口径，Destination.Tier 由 WorldMapPanel 持有）。
        Assert.Equal("大·约5天+", ExplorationProgress.TierLabel(SizeTier.Large));
    }
}
