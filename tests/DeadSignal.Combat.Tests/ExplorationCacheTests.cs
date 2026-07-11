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
        Assert.Contains(r.Value.Loot, l => l.Kind == LootKind.Weapon && l.RefId == ExplorationCache.BoltActionRifleName);
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
    public void CacheIdsFor_MapsTwoNewDestinations_OthersEmpty()
    {
        Assert.Equal(
            new[] { ExplorationCache.RiversideGunCabinetId, ExplorationCache.RiversideBedChestId },
            ExplorationCache.CacheIdsFor(ExplorationCache.RiversideCabinName).ToArray());
        Assert.Equal(
            new[] { ExplorationCache.WarehouseToolCabinetId, ExplorationCache.WarehouseAtticChestId },
            ExplorationCache.CacheIdsFor(ExplorationCache.HarvesterWarehouseName).ToArray());
        Assert.Empty(ExplorationCache.CacheIdsFor("超市"));
    }

    [Fact]
    public void EveryPlacedCacheId_Resolves()
    {
        // TestExploration 铺的每个搜刮点 id 必须解析得到（否则踏入零掉落无叙事）。
        foreach (string dest in new[] { ExplorationCache.RiversideCabinName, ExplorationCache.HarvesterWarehouseName })
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

    [Fact]
    public void BoltRifleName_MatchesWeaponTable()
    {
        // 搜刮点给的武器名必须与 WeaponTable 一致（否则 Item.Weapon 名对不上装备/展示）。
        Assert.Equal(ExplorationCache.BoltActionRifleName, DeadSignal.Combat.WeaponTable.BoltActionHuntingRifle().Name);
    }
}
