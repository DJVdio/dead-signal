using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 探索点「搜刮点」解析纯逻辑单测：cacheId → (flag/一批掉落/环境叙事)，已搜过（flag 已置）返回 null 防重复。
// 两个前中期探索点（用户拍板"加两个探索点 河边小屋 联合收割机仓库"）：
//   · 河边小屋：枪柜 ← 栓动猎枪；床底木箱 ← 通用搜刮（食物/医疗/材料）。
//   · 联合收割机仓库：工具柜 ← 通用木工材料；阁楼铁皮箱 ←《进阶木匠技术》。
//     （《木匠入门》原拟放工具柜，用户改单撤出→改由「神秘商人」系统出售，本探索点不再投放它。）
// 全部脱 Godot：只用 StoryFlags + ExplorationCache 的纯字符串/LootItem 状态。
public class ExplorationCacheTests
{
    [Fact]
    public void UnknownCacheId_ReturnsNull()
    {
        Assert.Null(ExplorationCache.Resolve("no_such_cache", new StoryFlags()));
    }

    [Fact]
    public void RiversideGunCabinet_FirstSearch_GrantsBoltRifleAndFlag()
    {
        var f = new StoryFlags();
        CacheResult? r = ExplorationCache.Resolve(ExplorationCache.RiversideGunCabinetId, f);

        Assert.NotNull(r);
        Assert.Equal(ExplorationCache.RiversideGunCabinetFlag, r!.Value.StoryFlag);
        // 原钉「柜里有栓动猎枪」。用户已把这把武器从数值表删除 ⇒ 枪柜不再产枪，改钉新事实：
        // 一件武器都不给（弹药/箭/布照旧），且**不许留下悬空的武器投放**（名字查不到 WeaponTable 工厂 = 没数值的枪）。
        Assert.DoesNotContain(r.Value.Loot, l => l.Kind == LootKind.Weapon);
        Assert.Contains(r.Value.Loot, l => l.Kind == LootKind.Material);
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Title));
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Narrative));
    }

    [Fact]
    public void WarehouseAtticChest_GrantsAdvancedCarpentry_ToolCabinetHasNoBook()
    {
        // 用户改单：《木匠入门》撤出仓库（改神秘商人出售），仓库只投放《进阶木匠技术》且藏在阁楼（藏深）。
        CacheResult tool = ExplorationCache.Resolve(ExplorationCache.WarehouseToolCabinetId, new StoryFlags())!.Value;
        Assert.DoesNotContain(tool.Loot, l => l.Kind == LootKind.Book);
        Assert.Contains(tool.Loot, l => l.Kind == LootKind.Material);

        CacheResult attic = ExplorationCache.Resolve(ExplorationCache.WarehouseAtticChestId, new StoryFlags())!.Value;
        Assert.Contains(attic.Loot, l => l.Kind == LootKind.Book && l.RefId == ExplorationCache.AdvancedCarpentryBookId);

        // 全仓库任一搜刮点都不再投放《木匠入门》(carpentry_basics)。
        foreach (string id in ExplorationCache.CacheIdsFor(ExplorationCache.HarvesterWarehouseName))
        {
            CacheResult r = ExplorationCache.Resolve(id, new StoryFlags())!.Value;
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Book && l.RefId == "carpentry_basics");
        }
    }

    [Fact]
    public void RiversideBedChest_HasGeneralSupplies()
    {
        CacheResult chest = ExplorationCache.Resolve(ExplorationCache.RiversideBedChestId, new StoryFlags())!.Value;
        Assert.Contains(chest.Loot, l => l.Kind == LootKind.Food);
        Assert.Contains(chest.Loot, l => l.Kind == LootKind.Material);
    }

    [Fact]
    public void AlreadySearched_ReturnsNull()
    {
        var f = new StoryFlags();
        f.Set(ExplorationCache.RiversideGunCabinetFlag, "true");
        Assert.Null(ExplorationCache.Resolve(ExplorationCache.RiversideGunCabinetId, f));

        var g = new StoryFlags();
        g.Set(ExplorationCache.WarehouseAtticChestFlag, "true");
        Assert.Null(ExplorationCache.Resolve(ExplorationCache.WarehouseAtticChestId, g));
    }

    [Fact]
    public void CacheIdsFor_MapsDestinations_QuotaBandsNearToDeep_OthersEmpty()
    {
        // [SPEC-B12] 扩到三级配额带下限：河边小屋/瞭望台=5(小)，仓库=10(中)；近→深序（招牌武器/书仍在入口/最深）。
        Assert.Equal(
            new[]
            {
                ExplorationCache.RiversideGunCabinetId, ExplorationCache.RiversideHearthId,
                ExplorationCache.RiversideFishingId, ExplorationCache.RiversideBedChestId,
                ExplorationCache.RiversideCellarId,
            },
            ExplorationCache.CacheIdsFor(ExplorationCache.RiversideCabinName).ToArray());
        Assert.Equal(
            new[]
            {
                ExplorationCache.WarehouseToolCabinetId, ExplorationCache.WarehouseWorkbenchId,
                ExplorationCache.WarehouseBreakCornerId, ExplorationCache.WarehousePartsBinId,
                ExplorationCache.WarehouseFuelDrumId, ExplorationCache.WarehouseLumberRackId,
                ExplorationCache.WarehouseHayLoftId, ExplorationCache.WarehouseScrapPileId,
                ExplorationCache.WarehouseCombineCabId, ExplorationCache.WarehouseAtticChestId,
            },
            ExplorationCache.CacheIdsFor(ExplorationCache.HarvesterWarehouseName).ToArray());
        // 城市之巅瞭望观景台：5 处物资搜刮点（望远镜发现尸潮的剧情点不在此列，归 LookoutSighting）。
        Assert.Equal(
            new[]
            {
                ExplorationCache.LookoutGiftShopId, ExplorationCache.LookoutVendingId,
                ExplorationCache.LookoutStaffLockerId, ExplorationCache.LookoutMachineRoomId,
                ExplorationCache.LookoutWardensRoomId,
            },
            ExplorationCache.CacheIdsFor(ExplorationCache.CityRooftopLookoutName).ToArray());
        // 广播台：10 处（茶水间近→备件仓库最深）。
        Assert.Equal(10, ExplorationCache.CacheIdsFor(ExplorationCache.BroadcastStationName).Count);
        // [SPEC-B13] 超市/医院已落真探索关：超市 11 处（外围 7 + 内圈 4）、医院 30 处（丧尸巢·药房手术层医疗集中）。
        Assert.Equal(11, ExplorationCache.CacheIdsFor(ExplorationCache.SupermarketName).Count);
        Assert.Equal(30, ExplorationCache.CacheIdsFor(ExplorationCache.HospitalName).Count);
        // 未登记的目的地仍返回空清单。
        Assert.Empty(ExplorationCache.CacheIdsFor("不存在的目的地"));
    }

    [Fact]
    public void LookoutCaches_HaveGeneralSuppliesNoWeaponNoBook()
    {
        // 瞭望台是剧情主点，物资为前中期通用搭配（食水/医疗/材料/电子件），不给招牌武器/书（draft 拟定，用户可加招牌物）。
        CacheResult shop = ExplorationCache.Resolve(ExplorationCache.LookoutGiftShopId, new StoryFlags())!.Value;
        Assert.Contains(shop.Loot, l => l.Kind == LootKind.Food);
        Assert.Contains(shop.Loot, l => l.Kind == LootKind.Material);

        CacheResult room = ExplorationCache.Resolve(ExplorationCache.LookoutWardensRoomId, new StoryFlags())!.Value;
        Assert.Contains(room.Loot, l => l.Kind == LootKind.Material);

        foreach (string id in ExplorationCache.CacheIdsFor(ExplorationCache.CityRooftopLookoutName))
        {
            CacheResult r = ExplorationCache.Resolve(id, new StoryFlags())!.Value;
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Weapon);
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Book);
        }
    }

    [Fact]
    public void LookoutCaches_AlreadySearched_ReturnsNull()
    {
        var f = new StoryFlags();
        f.Set(ExplorationCache.LookoutGiftShopFlag, "true");
        Assert.Null(ExplorationCache.Resolve(ExplorationCache.LookoutGiftShopId, f));

        var g = new StoryFlags();
        g.Set(ExplorationCache.LookoutWardensRoomFlag, "true");
        Assert.Null(ExplorationCache.Resolve(ExplorationCache.LookoutWardensRoomId, g));
    }

    [Fact]
    public void EveryPlacedCacheId_Resolves()
    {
        // TestExploration 铺的每个搜刮点 id 必须解析得到（否则踏入零掉落无叙事）。
        foreach (string dest in new[]
                 {
                     ExplorationCache.RiversideCabinName,
                     ExplorationCache.HarvesterWarehouseName,
                     ExplorationCache.CityRooftopLookoutName,
                 })
            foreach (string id in ExplorationCache.CacheIdsFor(dest))
                Assert.NotNull(ExplorationCache.Resolve(id, new StoryFlags()));
    }

    [Fact]
    public void BookIds_MatchBookLibraryEntries()
    {
        // 搜刮点给的书 id 必须在内置书目里解析得到（否则 LootApplication 会静默跳过）。
        var snapshot = BookLibrary.All().ToDictionary(b => b.Id);
        Assert.True(snapshot.ContainsKey(ExplorationCache.AdvancedCarpentryBookId));
    }

    /// <summary>
    /// 原 BoltRifleName_MatchesWeaponTable：钉的是"投放点的栓动猎枪名要与 WeaponTable 对得上"。
    /// 用户已删除这把武器 ⇒ 常量与工厂双双撤下，这条断言无从谈起。改钉它背后**真正在防的那件事**：
    /// 搜刮点投放的每一个武器名，都必须能在 WeaponTable 里查到工厂——否则玩家会捡到一把没有任何数值的枪
    /// （Item.Weapon 以中文名作 RefKey）。这条护栏比原来那条更强：它管的是全部投放点，不止一把枪。
    /// </summary>
    [Theory]
    [InlineData(ExplorationCache.RangersCabinAtticId)]      // ← 狩猎弓
    [InlineData(ExplorationCache.GoldfingerArmoryId)]       // ← 冲锋枪 + 复合弩
    [InlineData(ExplorationCache.SupermarketHoardGearId)]   // ← 竞技复合弓
    public void 搜刮点投放的武器名_都能在武器表里查到(string cacheId)
    {
        var arsenal = DeadSignal.Combat.WeaponTable.Arsenal().Select(w => w.Name).ToHashSet();

        CacheResult r = ExplorationCache.Resolve(cacheId, new StoryFlags())!.Value;

        foreach (LootItem loot in r.Loot.Where(l => l.Kind == LootKind.Weapon))
        {
            Assert.True(arsenal.Contains(loot.RefId),
                $"搜刮点「{cacheId}」投放了武器「{loot.RefId}」，但 WeaponTable 里没有它 ⇒ 悬空引用（捡到的枪没有任何数值）");
        }
    }

    [Fact]
    public void GangSmgName_MatchesWeaponTable()
    {
        // 金手指帮军械柜招牌武器名须与 WeaponTable.Smg 一致。
        Assert.Equal(ExplorationCache.GangSmgName, DeadSignal.Combat.WeaponTable.Smg().Name);
    }

    [Fact]
    public void GoldfingerCaches_GangStockpileSemantics_NoFoodMedicalCapped()
    {
        // [SPEC-B12-补] 中型·战斗为主：帮派储备＝弹药火药/碎金属/武器配件/白银/皮革布料；
        // 禁食物医疗灌水——全 11 点无食物，含医疗(bandage/antibiotics/first_aid_kit)的点至多 1 处（头目急救箱封顶）。
        var ids = ExplorationCache.CacheIdsFor(ExplorationCache.GoldfingerBaseName);
        Assert.Equal(11, ids.Count);

        int medicalPoints = 0;
        bool hasSilver = false, hasWeapon = false, hasGunpowder = false;
        foreach (string id in ids)
        {
            CacheResult r = ExplorationCache.Resolve(id, new StoryFlags())!.Value;
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Food); // 无食物
            bool med = r.Loot.Any(l => l.Kind == LootKind.Material &&
                (l.RefId == "bandage" || l.RefId == "antibiotics" || l.RefId == "first_aid_kit"));
            if (med) medicalPoints++;
            if (r.Loot.Any(l => l.Kind == LootKind.Material && l.RefId == "silver")) hasSilver = true;
            if (r.Loot.Any(l => l.Kind == LootKind.Weapon)) hasWeapon = true;
            if (r.Loot.Any(l => l.Kind == LootKind.Material && l.RefId == "gunpowder")) hasGunpowder = true;
        }
        Assert.True(medicalPoints <= 1, $"金手指帮含医疗点应≤1，实际 {medicalPoints}");
        Assert.True(hasSilver, "帮派储备应含白银（硬通货）");
        Assert.True(hasWeapon, "帮派军械柜应含武器（打过才拿）");
        Assert.True(hasGunpowder, "帮派储备应含火药（弹药料）");
    }

    [Fact]
    public void GoldfingerCaches_EveryPlacedId_Resolves()
    {
        foreach (string id in ExplorationCache.CacheIdsFor(ExplorationCache.GoldfingerBaseName))
            Assert.NotNull(ExplorationCache.Resolve(id, new StoryFlags()));
    }
}
