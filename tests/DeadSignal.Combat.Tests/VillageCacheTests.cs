using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 南林村庄「大调查点」搜刮点纯逻辑单测（[SPEC-B11-补]：大点=5天+探索量；[放大·≈5天量级] 画布 4200×2800 后加密 30→42）：
///   · CacheIdsFor(南林村庄) 出 42 处（对齐"大·≈5天"内容量）；· 每处 id↔flag 双向一致；
///   · Resolve 首搜出掉落+叙事、复搜(flag 已置)去重返回 null；· 与救援主线点隔离(救援不进物资完成度)；
///   · ExplorationProgress.Completion 对南林村庄聚合 total=42、随已搜 flag 递增 done。
/// 数值/搭配皆"拟定待调"，测试锁的是规则形态与登记完整性。
/// </summary>
public class VillageCacheTests
{
    // [放大·≈5天量级] 大点 30→42（band 30+ 硬口径，各分区加密 + 新分区·果园梯田），近入口→藏深序。
    private static readonly string[] ExpectedIds =
    {
        // 村口/杂物(4)
        ExplorationCache.VillageRoadsideCarId, ExplorationCache.VillageGatePostId, ExplorationCache.VillageTrikeId,
        ExplorationCache.VillageRoadHutId,
        // 民居区(11)
        ExplorationCache.VillageKitchenId, ExplorationCache.VillageWardrobeId, ExplorationCache.VillageBedroom2Id,
        ExplorationCache.VillageCourtyardId, ExplorationCache.VillageCoopId, ExplorationCache.VillagePantry2Id,
        ExplorationCache.VillageLoftId, ExplorationCache.VillageWoodpileId, ExplorationCache.VillageBackRoomId,
        ExplorationCache.VillageOldWellHouseId, ExplorationCache.VillageDryingShedId,
        // 村中心(8)
        ExplorationCache.VillageShopShelfId, ExplorationCache.VillageCoopStoreId, ExplorationCache.VillageBusStopId,
        ExplorationCache.VillageSchoolId, ExplorationCache.VillageWellToolboxId, ExplorationCache.VillageForgeId,
        ExplorationCache.VillagePostOfficeId, ExplorationCache.VillageCreditCoopId,
        // 村尾/藏深(8)
        ExplorationCache.VillageToolShedId, ExplorationCache.VillageBarnId, ExplorationCache.VillageBeehiveId,
        ExplorationCache.VillageGraveHutId, ExplorationCache.VillageShrineId, ExplorationCache.VillageClinicId,
        ExplorationCache.VillageThreshingId, ExplorationCache.VillageStoneMillId,
        // 后山(5)
        ExplorationCache.VillageBackhillBlindId, ExplorationCache.VillageBackhillKilnId, ExplorationCache.VillageBackhillCaveId,
        ExplorationCache.VillageBackhillHerbHutId, ExplorationCache.VillageBackhillSnareId,
        // 河滩(4)
        ExplorationCache.VillageRiverbankBoatId, ExplorationCache.VillageRiverbankShackId, ExplorationCache.VillageRiverbankPumpId,
        ExplorationCache.VillageWatermillId,
        // 新分区·果园梯田(2, 最深)
        ExplorationCache.VillageOrchardCellarId, ExplorationCache.VillageOrchardShedId,
    };

    [Fact]
    public void CacheIdsFor_Village_FortyTwoLargePointCaches()
    {
        var ids = ExplorationCache.CacheIdsFor(VillageRescue.DestinationName);
        Assert.Equal(42, ids.Count);                         // [放大·≈5天量级] 大点加密 30→42
        Assert.Equal(ExpectedIds, ids.ToArray());            // 且按近→深登记序（村口→民居→村中心→村尾→后山→河滩→果园）
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
    public void Completion_Village_TotalFortyTwo_DoneRisesWithSearches()
    {
        var flags = new StoryFlags();
        var (done0, total) = ExplorationProgress.Completion(VillageRescue.DestinationName, flags, christineLeftForRevenge: false);
        Assert.Equal(42, total);   // [放大·≈5天量级] 42 处登记（救援主线点不计）
        Assert.Equal(0, done0);

        // 搜掉三处（跨分区）→ done=3。
        flags.Set(ExplorationCache.VillageRoadsideCarFlag, "true");
        flags.Set(ExplorationCache.VillageKitchenFlag, "true");
        flags.Set(ExplorationCache.VillageBackhillCaveFlag, "true");
        var (done3, total3) = ExplorationProgress.Completion(VillageRescue.DestinationName, flags, christineLeftForRevenge: false);
        Assert.Equal(42, total3);
        Assert.Equal(3, done3);
    }

    [Fact]
    public void Village_IsLargeTier_Label()
    {
        // 南林村庄定级＝大（site-tiers 代填 Tier=Large；此处锁文案口径，Destination.Tier 由 WorldMapPanel 持有）。
        Assert.Equal("大", ExplorationProgress.TierLabel(SizeTier.Large));
    }
}
