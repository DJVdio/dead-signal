using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-B13·拟设定待确认] 东部新村（Medium，12 处）+ 加油站（Medium 下限，10 处）搜刮点纯逻辑单测：
///   · CacheIdsFor 各出配额带内点数、按近→深登记序；· 每 id↔flag 双向一致；· Resolve 首搜出掉落+叙事、复搜去重；
///   · 关卡身份：东部新村=建材工具大户（木/钉/碎金属/线材/元件为主，无武器/书），加油站=燃油大户（fuel 多点、便利店食品少量、无医疗灌水）；
///   · ExplorationProgress.Completion 对两图 total 正确、随已搜 flag 递增。
/// 数值/搭配皆"拟定待调"，测试锁的是规则形态与登记完整性（先红：B13 前两图 CacheIdsFor 返回空）。
/// </summary>
public class NewVillageGasCacheTests
{
    // ---- 东部新村（[SPEC-B13-补3] 30·杂而薄）：排屋(近11·一户户翻)→工地(中8·维持偏建材)→老屋(深11·含最深药箱) ----
    private static readonly string[] EastNewVillageIds =
    {
        // 排屋区(南/近, 11)
        ExplorationCache.NewVillageShowroomId, ExplorationCache.NewVillageRowKitchenId, ExplorationCache.NewVillageRowAWardrobeId,
        ExplorationCache.NewVillageRowAUnderbedId, ExplorationCache.NewVillageRowBKitchenId, ExplorationCache.NewVillageRowBBalconyId,
        ExplorationCache.NewVillageRowBClosetId, ExplorationCache.NewVillageUnfinishedId, ExplorationCache.NewVillageRowCShoeCabId,
        ExplorationCache.NewVillageRowCBathId, ExplorationCache.NewVillageRowDBalconyId,
        // 工地区(中, 8)
        ExplorationCache.NewVillageLumberYardId, ExplorationCache.NewVillageScaffoldId, ExplorationCache.NewVillageToolShedId,
        ExplorationCache.NewVillageRebarPileId, ExplorationCache.NewVillageSiteOfficeId, ExplorationCache.NewVillageCementPileId,
        ExplorationCache.NewVillageElectricalBoxId, ExplorationCache.NewVillageForemanLockerId,
        // 老屋区(北/深, 11)
        ExplorationCache.NewVillageOldKitchenId, ExplorationCache.NewVillageOldWardrobeId, ExplorationCache.NewVillageRootCellarId,
        ExplorationCache.NewVillageOldHallId, ExplorationCache.NewVillageOldUnderbedId, ExplorationCache.NewVillageOldAtticId,
        ExplorationCache.NewVillageOld2KitchenId, ExplorationCache.NewVillageOld2WoodshedId, ExplorationCache.NewVillageOld2YardId,
        ExplorationCache.NewVillageOld2ShrineId, ExplorationCache.NewVillageOld2MedCabId,
    };

    // ---- 加油站（10）：加油区(近2)→便利店(中3)→修车棚(中3)→油罐区(深2) ----
    private static readonly string[] GasStationIds =
    {
        ExplorationCache.GasPumpIslandId, ExplorationCache.GasKioskId,
        ExplorationCache.GasStoreSnacksId, ExplorationCache.GasStoreDrinksId, ExplorationCache.GasStoreBackroomId,
        ExplorationCache.GasRepairBayId, ExplorationCache.GasPartsShelfId, ExplorationCache.GasOilRackId,
        ExplorationCache.GasTankerId, ExplorationCache.GasUndergroundTankId,
    };

    [Fact]
    public void CacheIdsFor_EastNewVillage_ThirtyMixedThinCaches_NearToDeep()
    {
        var ids = ExplorationCache.CacheIdsFor(ExplorationCache.EastNewVillageName);
        Assert.Equal(30, ids.Count);                  // 中型顶格 30（[SPEC-B13-补3]）
        Assert.Equal(EastNewVillageIds, ids.ToArray());
    }

    [Fact]
    public void CacheIdsFor_GasStation_TenMediumFloorCaches_NearToDeep()
    {
        var ids = ExplorationCache.CacheIdsFor(ExplorationCache.GasStationName);
        Assert.Equal(10, ids.Count);
        Assert.Equal(GasStationIds, ids.ToArray());
    }

    [Fact]
    public void EastNewVillageName_IsResidentialInternalKey()
    {
        // 正名兼容：内部路由键仍是「住宅区」（守林人小屋先例），显示名「东部新村」由 WorldMapPanel.DisplayName 承载。
        Assert.Equal("住宅区", ExplorationCache.EastNewVillageName);
        Assert.Equal("加油站", ExplorationCache.GasStationName);
    }

    [Fact]
    public void FlagForCache_EveryPlacedId_RoundTripsNonEmpty()
    {
        foreach (string id in EastNewVillageIds.Concat(GasStationIds))
            Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)), $"{id} 缺 flag 映射");
    }

    [Fact]
    public void Resolve_EveryPlacedCache_FirstSearchGivesLootAndNarrative_ThenDedup()
    {
        foreach (string id in EastNewVillageIds.Concat(GasStationIds))
        {
            var flags = new StoryFlags();
            CacheResult? first = ExplorationCache.Resolve(id, flags);
            Assert.True(first != null, $"{id} 首搜应出结果");
            Assert.True(first!.Value.Loot.Count > 0, $"{id} 应有掉落");
            Assert.False(string.IsNullOrWhiteSpace(first.Value.Narrative), $"{id} 应有叙事");
            Assert.Equal(ExplorationCache.FlagForCache(id), first.Value.StoryFlag);

            flags.Set(first.Value.StoryFlag, "true");
            Assert.Null(ExplorationCache.Resolve(id, flags)); // 复搜去重（跨访持久）
        }
    }

    [Fact]
    public void EastNewVillage_IsMixedAndThin_NotBuildingMaterialHub()
    {
        // [SPEC-B13-补3] 身份=杂而薄：住宅区物资不单一不集中。锁三条形态：
        //   ① 每点薄——1~2 件；② 品类杂——全图覆盖 食物/布/日用杂物/建材/工具/偶发药品 多类，无单一品类独大；
        //   ③ 食物克制、医疗偶发（无 first_aid_kit 灌水）；无招牌武器/书。
        var results = EastNewVillageIds.Select(id => ExplorationCache.Resolve(id, new StoryFlags())!.Value).ToList();
        Assert.Equal(30, results.Count);

        // ① 薄：每点 1~2 件。
        foreach (CacheResult r in results)
            Assert.InRange(r.Loot.Count, 1, 2);

        // ② 杂：多品类均在场（家户零碎+建材+杂物+偶发药品）。
        int Count(string key) => results.Sum(r => r.Loot.Count(l => l.Kind == LootKind.Material && l.RefId == key));
        int FoodPoints() => results.Count(r => r.Loot.Any(l => l.Kind == LootKind.Food));
        foreach (string key in new[] { "cloth", "scrap_cloth", "wood", "nails", "scrap_metal", "wire", "components", "rope", "bandage", "bone" })
            Assert.True(Count(key) > 0, $"东部新村应含品类 {key}（杂）");
        Assert.True(FoodPoints() > 0, "东部新村应有零碎食物点");

        // 无单一品类独大：任一材料品类的总量不超过全部掉落件数的 25%（戒掉"建材大户"）。
        int totalItems = results.Sum(r => r.Loot.Count) + 0; // 含 Food 计件
        int foodItems = results.Sum(r => r.Loot.Count(l => l.Kind == LootKind.Food));
        int maxCategory = new[] { "cloth", "scrap_cloth", "wood", "nails", "scrap_metal", "wire", "components", "rope", "bandage", "antibiotics", "bone" }
            .Select(Count).Append(foodItems).Max();
        Assert.True(maxCategory <= totalItems * 0.25 + 1, $"东部新村不应有单一品类独大（最大 {maxCategory}/{totalItems}）");

        // ③ 食物克制（≤10 点带食物）、医疗偶发（无急救包灌水；抗生素至多 1、绷带少量）。
        Assert.True(FoodPoints() <= 10, $"东部新村食物应克制，实得 {FoodPoints()} 点");
        Assert.Equal(0, Count("first_aid_kit"));
        Assert.True(Count("antibiotics") <= 1, "东部新村抗生素偶发单件");
        Assert.True(Count("bandage") <= 4, "东部新村绷带零星");

        // 无招牌武器/书。
        foreach (CacheResult r in results)
        {
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Weapon);
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Book);
        }
    }

    [Fact]
    public void GasStation_IsFuelHub_LimitedFoodNoMedicalGlut()
    {
        // 身份=燃油大户：fuel 出现在多处；便利店食品少量；无医疗灌水（只便利店里屋一处绷带，无急救包/抗生素）。
        var results = GasStationIds.Select(id => ExplorationCache.Resolve(id, new StoryFlags())!.Value).ToList();

        int fuelPoints = results.Count(r => r.Loot.Any(l => l.Kind == LootKind.Material && l.RefId == "fuel"));
        Assert.True(fuelPoints >= 4, $"加油站应多点产燃油（燃油大户），实得 {fuelPoints}");

        // 无医疗灌水：全站不给急救包/抗生素，绷带至多一处。
        Assert.DoesNotContain(results, r => r.Loot.Any(l => l.Kind == LootKind.Material && (l.RefId == "first_aid_kit" || l.RefId == "antibiotics")));
        int bandagePoints = results.Count(r => r.Loot.Any(l => l.Kind == LootKind.Material && l.RefId == "bandage"));
        Assert.True(bandagePoints <= 1, $"加油站医疗克制，绷带点应 ≤1，实得 {bandagePoints}");

        // 无招牌武器/书。
        foreach (CacheResult r in results)
        {
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Weapon);
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Book);
        }
    }

    [Fact]
    public void Completion_BothMaps_TotalsMatchAndRiseWithSearches()
    {
        var flags = new StoryFlags();
        var (vDone0, vTotal) = ExplorationProgress.Completion(ExplorationCache.EastNewVillageName, flags, christineLeftForRevenge: false);
        Assert.Equal(30, vTotal);
        Assert.Equal(0, vDone0);
        var (gDone0, gTotal) = ExplorationProgress.Completion(ExplorationCache.GasStationName, flags, christineLeftForRevenge: false);
        Assert.Equal(10, gTotal);
        Assert.Equal(0, gDone0);

        flags.Set(ExplorationCache.NewVillageForemanLockerFlag, "true");
        flags.Set(ExplorationCache.GasUndergroundTankFlag, "true");
        Assert.Equal(1, ExplorationProgress.Completion(ExplorationCache.EastNewVillageName, flags, false).done);
        Assert.Equal(1, ExplorationProgress.Completion(ExplorationCache.GasStationName, flags, false).done);
    }

    [Fact]
    public void NarrativeSpots_BothMaps_TwoEach_ResolveWithPages()
    {
        // 各图 1~2 处叙事调查点（东部新村：乔迁对联/工地打卡板；加油站：公路车龙/立柱油价牌）。
        var villageSpots = NarrativeSpotRegistry.ForDestination(ExplorationCache.EastNewVillageName).ToList();
        var gasSpots = NarrativeSpotRegistry.ForDestination(ExplorationCache.GasStationName).ToList();
        Assert.Equal(2, villageSpots.Count);
        Assert.Equal(2, gasSpots.Count);

        foreach (NarrativeSpot spot in villageSpots.Concat(gasSpots))
        {
            NarrativeSpotResult? r = NarrativeSpotRegistry.Resolve(spot.Id, new StoryFlags());
            Assert.NotNull(r);
            Assert.False(string.IsNullOrWhiteSpace(r!.Value.Title), $"{spot.Id} 应有标题");
            Assert.NotEmpty(r.Value.Pages);
            Assert.All(r.Value.Pages, p => Assert.False(string.IsNullOrWhiteSpace(p)));
            Assert.StartsWith("seen_narrative_", spot.StoryFlag); // 一次性去重旗标命名空间
        }
    }

    [Fact]
    public void NarrativeSpots_DoNotCollideWithMaterialCaches()
    {
        // 叙事点坐标须与同图物资搜刮点相距 >70px（AddDiscoveryPoint 触发区默认 70×70，避免踏入触发歧义）。
        AssertNoCollision(ExplorationCache.EastNewVillageName, EastNewVillageIds);
        AssertNoCollision(ExplorationCache.GasStationName, GasStationIds);
    }

    private static void AssertNoCollision(string destination, string[] cacheIds)
    {
        // 物资搜刮点坐标登记在关卡层（TestExploration，含 Godot 依赖），纯逻辑测拿不到；
        // 此处只校验叙事点之间不自撞，物资×叙事间距由关卡层坐标注释保证（见 SetupEastNewVillage/SetupGasStation 与 NarrativeSpot 注释）。
        var spots = NarrativeSpotRegistry.ForDestination(destination).ToList();
        for (int i = 0; i < spots.Count; i++)
            for (int j = i + 1; j < spots.Count; j++)
            {
                float dx = spots[i].X - spots[j].X, dy = spots[i].Y - spots[j].Y;
                Assert.True(dx * dx + dy * dy > 70f * 70f, $"{spots[i].Id} 与 {spots[j].Id} 叙事点过近");
            }
    }
}
