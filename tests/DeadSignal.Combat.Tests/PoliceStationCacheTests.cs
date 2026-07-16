using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [警察局] 警察局（Small，5 处·Medium）搜刮点纯逻辑单测。用户拍板：前中期·小·室内多拐角·危险 Medium，
/// 前置挂消防站或河边小屋之后。
/// <para>
/// 锁死的形态（数值/搭配"拟定待调"，形态不是）：
/// ① <b>小点＝5 处</b>（既有 band：小 5 / 中 10~30 / 大 30+），按近→深登记；② 每 id↔flag 双向一致；
/// ③ Resolve 首搜出掉落+叙事、复搜去重；④ <b>身份＝防护装备</b>：整关最值钱的是<b>禁闭室里那套防暴头盔 + 防弹背心</b>
/// （authored 新甲，police-armor 落地），且两件甲<b>只</b>出在禁闭室、别处一件不出；
/// ⑤ 其余搜刮点＝<b>少量手枪弹（ammo_short）+ 杂项材料 + 一两只死老鼠（rat）</b>，整体前中期·中等偏紧；
/// ⑥ <b>没有枪</b>：这一关的回报是甲，不是枪（枪械另有门槛/别处产），也无白银/抗生素/书。
/// </para>
/// 危险度（Medium＝4 只，各藏一房深角）与「室内多拐角」的几何护栏落在 <see cref="PoliceStationTests"/>（WallRect 补集 + 感知≤1 硬不变量）。
/// </summary>
public class PoliceStationCacheTests
{
    // ---- 警察局（小点，5 处）：前台(门厅·近) → 办公区(中) → 证物室(中) → 更衣室(深) → 禁闭室/囚室(最深·两件甲) ----
    private static readonly string[] PoliceIds =
    {
        ExplorationCache.PoliceFrontDeskId,
        ExplorationCache.PoliceBullpenId,
        ExplorationCache.PoliceEvidenceId,
        ExplorationCache.PoliceLockerRoomId,
        ExplorationCache.PoliceHoldingCellId,
    };

    [Fact]
    public void CacheIdsFor_PoliceStation_FiveSmallCaches_NearToDeep()
    {
        var ids = ExplorationCache.CacheIdsFor(ExplorationCache.PoliceStationName);
        Assert.Equal(5, ids.Count);                 // 小点 band＝5（用户口径「小」）
        Assert.Equal(PoliceIds, ids.ToArray());
    }

    [Fact]
    public void PoliceStation_RouteKeyAndCacheIds_AreStable()
    {
        // 目的地名＝内部路由键（WorldMapPanel/TestExploration/ExplorationCache/world_graph.json 必须一致）；
        // cache id 一旦发布就是**存档 flag 的一部分**，改名＝老存档的"已搜过"全部失忆 ⇒ 在这里钉死字面量。
        Assert.Equal("警察局", ExplorationCache.PoliceStationName);
        Assert.Equal("cache_police_front_desk", ExplorationCache.PoliceFrontDeskId);
        Assert.Equal("cache_police_bullpen", ExplorationCache.PoliceBullpenId);
        Assert.Equal("cache_police_evidence", ExplorationCache.PoliceEvidenceId);
        Assert.Equal("cache_police_locker_room", ExplorationCache.PoliceLockerRoomId);
        Assert.Equal("cache_police_holding_cell", ExplorationCache.PoliceHoldingCellId);
    }

    [Fact]
    public void FlagForCache_EveryPlacedId_RoundTripsNonEmpty()
    {
        foreach (string id in PoliceIds)
            Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)), $"{id} 缺 flag 映射");
    }

    [Fact]
    public void Resolve_EveryPlacedCache_FirstSearchGivesLootAndNarrative_ThenDedup()
    {
        foreach (string id in PoliceIds)
        {
            var flags = new StoryFlags();
            CacheResult? first = ExplorationCache.Resolve(id, flags);
            Assert.True(first != null, $"{id} 首搜应出结果");
            Assert.True(first!.Value.Loot.Count > 0, $"{id} 应有掉落");
            Assert.False(string.IsNullOrWhiteSpace(first.Value.Title), $"{id} 应有标题");
            Assert.False(string.IsNullOrWhiteSpace(first.Value.Narrative), $"{id} 应有叙事");
            Assert.Equal(ExplorationCache.FlagForCache(id), first.Value.StoryFlag);

            flags.Set(first.Value.StoryFlag, "true");
            Assert.Null(ExplorationCache.Resolve(id, flags)); // 复搜去重（跨访持久）
        }
    }

    [Fact]
    public void HoldingCell_HasBothAuthoredArmors_RiotHelmetAndBallisticVest()
    {
        // 🔴 这一关的身份就长在这一处：禁闭室出**防暴头盔 + 防弹背心**（用户 authored 新甲，police-armor 落地）。
        CacheResult cell = ExplorationCache.Resolve(ExplorationCache.PoliceHoldingCellId, new StoryFlags())!.Value;
        Assert.Contains(cell.Loot, l => l.Kind == LootKind.Armor && l.RefId == "防暴头盔");
        Assert.Contains(cell.Loot, l => l.Kind == LootKind.Armor && l.RefId == "防弹背心");
    }

    [Fact]
    public void Armor_OnlyInHoldingCell_NowhereElse()
    {
        // 两件甲**只**出在禁闭室 —— 别处一件都不出（否则禁闭室"最值钱"的定位就塌了）。
        foreach (string id in PoliceIds.Where(i => i != ExplorationCache.PoliceHoldingCellId))
        {
            CacheResult r = ExplorationCache.Resolve(id, new StoryFlags())!.Value;
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Armor);
        }
    }

    [Fact]
    public void PoliceStation_HasPistolAmmo_MiscMaterials_AndAFewDeadRats()
    {
        // 其余搜刮点＝少量手枪弹 + 杂项材料 + 一两只死老鼠。
        var results = Results();

        int Count(string key) => results.Sum(r => r.Loot.Count(l => l.Kind == LootKind.Material && l.RefId == key));
        Assert.True(Count("ammo_short") > 0, "警察局应产手枪弹（ammo_short）");

        // 杂项材料（机械零件/布/铁 之类）至少一处。
        int MiscPoints() => results.Count(r => r.Loot.Any(l =>
            l.Kind == LootKind.Material && (l.RefId == "components" || l.RefId == "cloth" || l.RefId == "iron")));
        Assert.True(MiscPoints() >= 2, $"警察局应多点产杂项材料，实得 {MiscPoints()}");

        // 死老鼠：一两只（1~2）。用户原话「一两只死老鼠」⇒ 别多，这不是下水道那种耗子窝。
        int Rats() => Count("rat");
        Assert.InRange(Rats(), 1, 2);
    }

    [Fact]
    public void PoliceStation_ArmorIsThePrize_NoGunsNoHighValueLoot()
    {
        // 「回报是甲」＝没有枪/白银/抗生素/书。手枪弹有（甲配着弹才有意义），但枪械另有门槛、别处产。
        var results = Results();
        foreach (CacheResult r in results)
        {
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Weapon);
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Book);
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && l.RefId == "silver");
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && l.RefId == "antibiotics");
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && (l.RefId == "bullet_parts" || l.RefId == "weapon_parts"));
        }

        // 中等偏紧：每点克制（1~4 堆），全关件数落在小点带内。
        foreach (CacheResult r in results)
            Assert.InRange(r.Loot.Count, 1, 4);
    }

    [Fact]
    public void Completion_PoliceStation_TotalIsFive_AndRisesWithSearches()
    {
        var flags = new StoryFlags();
        var (done0, total) = ExplorationProgress.Completion(ExplorationCache.PoliceStationName, flags, christineLeftForRevenge: false);
        Assert.Equal(5, total);
        Assert.Equal(0, done0);

        flags.Set(ExplorationCache.PoliceHoldingCellFlag, "true");
        Assert.Equal(1, ExplorationProgress.Completion(ExplorationCache.PoliceStationName, flags, false).done);
    }

    [Fact]
    public void NarrativeSpots_PoliceStation_EnvironmentalOnly_ResolveWithPages()
    {
        // 用户**没给**警察局任何剧情梗概 ⇒ 只做环境叙事，不编造角色/前史（authored 内容归用户）。
        var spots = NarrativeSpotRegistry.ForDestination(ExplorationCache.PoliceStationName).ToList();
        Assert.Single(spots);

        foreach (NarrativeSpot spot in spots)
        {
            NarrativeSpotResult? r = NarrativeSpotRegistry.Resolve(spot.Id, new StoryFlags());
            Assert.NotNull(r);
            Assert.False(string.IsNullOrWhiteSpace(r!.Value.Title));
            Assert.NotEmpty(r.Value.Pages);
            Assert.All(r.Value.Pages, p => Assert.False(string.IsNullOrWhiteSpace(p)));
            Assert.StartsWith("seen_narrative_", spot.StoryFlag);
        }
    }

    private static CacheResult[] Results()
        => PoliceIds.Select(id => ExplorationCache.Resolve(id, new StoryFlags())!.Value).ToArray();
}
