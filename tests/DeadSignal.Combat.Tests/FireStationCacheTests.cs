using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [批次25·T50] 消防站（Small，5 处）搜刮点纯逻辑单测。用户原话：「消防站（一些基础物资和消防斧，小地图，低危）」。
/// <para>
/// 锁死的形态（数值/搭配"拟定待调"，形态不是）：
/// ① <b>小点＝5 处</b>（既有 band：小 5 / 中 10~30 / 大 30+），按近→深登记；② 每 id↔flag 双向一致；
/// ③ Resolve 首搜出掉落+叙事、复搜去重；④ <b>身份＝救援装备</b>（绳索多点 + 急救齐全但克制 + 破拆/基础建材），
/// ⑤ <b>"基础物资"＝没有高价值</b>：无白银/无抗生素/无书/无枪械/无弹药——它的定位是"稳、少、安全"，不是白送；
/// ⑥ <b>消防斧</b>：消防斧是这座建筑最不需要解释的东西 ⇒ 器材墙上挂着一把，且它是<b>全站唯一</b>的武器。
/// </para>
/// <para>
/// 🔴 <b>消防斧投放分布（本单调整，已在 journal 告知 impl-axe）</b>：原为 联合收割机仓库·木料架 + 南林村庄·柴垛 两处。
/// 现改为 <b>消防站·器材墙</b>（近·低危·全图最短行程）+ <b>联合收割机仓库·木料架</b>（远·中点·与《进阶木匠技术》同馆）两处，
/// <b>撤掉南林村庄·柴垛那一把</b>——南林村庄是大点/中后期/丧尸围困，走到那儿的人早有消防斧或早能造消防斧，第三把纯属冗余。
/// 撤掉之后梯度才立得住：<b>低危点给一把（拿得早、拿得稳）</b>，<b>远点给一把（顺手，且配套那本造消防斧的书）</b>。
/// （<b>没动</b>守林人小屋·后院柴房——那儿的 authored 叙事写着「斧子不见了，大概是主人最后用它做了别的事」，那是钩子，不是漏投。）
/// </para>
/// 危险度（低危＝3 只游荡丧尸）落在关卡层 <c>TestExploration.FireStationZombieSpots</c>（含 Godot 依赖，纯逻辑测不到）。
/// </summary>
public class FireStationCacheTests
{
    // ---- 消防站（小点，5 处）：车库·消防车(近) → 车库·器材墙(近·消防斧) → 值班室(中) → 器材间·急救柜(深) → 后院·杂物棚(最深) ----
    private static readonly string[] FireStationIds =
    {
        ExplorationCache.FireStationEngineBayId,
        ExplorationCache.FireStationGearWallId,
        ExplorationCache.FireStationDutyRoomId,
        ExplorationCache.FireStationMedCabinetId,
        ExplorationCache.FireStationBackyardShedId,
    };

    [Fact]
    public void CacheIdsFor_FireStation_FiveSmallCaches_NearToDeep()
    {
        var ids = ExplorationCache.CacheIdsFor(ExplorationCache.FireStationName);
        Assert.Equal(5, ids.Count);                 // 小点 band＝5（用户口径"小地图"）
        Assert.Equal(FireStationIds, ids.ToArray());
    }

    [Fact]
    public void FireStation_RouteKeyAndCacheIds_AreStable()
    {
        // 目的地名＝内部路由键（WorldMapPanel/TestExploration/ExplorationCache 三处必须一致）；
        // cache id 一旦发布就是**存档 flag 的一部分**，改名＝老存档的"已搜过"全部失忆 ⇒ 在这里钉死字面量。
        Assert.Equal("消防站", ExplorationCache.FireStationName);
        Assert.Equal("cache_firestation_engine_bay", ExplorationCache.FireStationEngineBayId);
        Assert.Equal("cache_firestation_gear_wall", ExplorationCache.FireStationGearWallId);
        Assert.Equal("cache_firestation_duty_room", ExplorationCache.FireStationDutyRoomId);
        Assert.Equal("cache_firestation_med_cabinet", ExplorationCache.FireStationMedCabinetId);
        Assert.Equal("cache_firestation_backyard_shed", ExplorationCache.FireStationBackyardShedId);
    }

    [Fact]
    public void FlagForCache_EveryPlacedId_RoundTripsNonEmpty()
    {
        foreach (string id in FireStationIds)
            Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)), $"{id} 缺 flag 映射");
    }

    [Fact]
    public void Resolve_EveryPlacedCache_FirstSearchGivesLootAndNarrative_ThenDedup()
    {
        foreach (string id in FireStationIds)
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
    public void FireStation_IsRescueGearHub_RopeAndFirstAid()
    {
        // 身份＝救援装备（用户："撬棍/绳索/急救/防护服那一类救援装备是它的性格"）。
        // ⚠️「撬棍」「防护服」在本作里**根本不是物品**（Materials/ArmorTable 里都没有）——遵"别自创新物品"，
        //    只投既有物：绳索(rope) + 急救(bandage/splint/first_aid_kit) + 破拆(消防斧) + 基础建材/工具。
        var results = Results();

        int RopePoints() => results.Count(r => r.Loot.Any(l => l.Kind == LootKind.Material && l.RefId == "rope"));
        Assert.True(RopePoints() >= 2, $"消防站应多点产绳索（救援装备），实得 {RopePoints()}");

        int Count(string key) => results.Sum(r => r.Loot.Count(l => l.Kind == LootKind.Material && l.RefId == key));
        Assert.True(Count("bandage") > 0, "消防站应有绷带（急救）");
        Assert.Equal(1, Count("first_aid_kit")); // 急救柜里恰一个：这是它的性格，也仅止于此
    }

    [Fact]
    public void FireStation_IsBasicSupplies_NoHighValueLoot()
    {
        // 「一些**基础**物资」＝没有高价值：不给白银/抗生素/书/枪械/弹药。它的回报是"稳、少、安全"，不是"低危白捡"。
        // （胜率不是成本——但反过来也一样：低危点不该给你高价值的东西，否则其他点位的代价就白付了。）
        var results = Results();
        foreach (CacheResult r in results)
        {
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Book);
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && l.RefId == "silver");
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && l.RefId == "antibiotics");
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && l.RefId.StartsWith("ammo_"));
            Assert.DoesNotContain(r.Loot, l => l.Kind == LootKind.Material && (l.RefId == "bullet_parts" || l.RefId == "weapon_parts"));
        }

        // 每点克制：1~4 堆（既有小点单点最多 6~7 堆，消防站更薄）。
        foreach (CacheResult r in results)
            Assert.InRange(r.Loot.Count, 1, 4);
    }

    [Fact]
    public void FireStation_LowRisk_MustNotOutEarnANormalRiskSmallMap()
    {
        // 🔴 这条是「低危」真正的代价护栏，也是本单唯一一条**相对**护栏（不写死数字，跟着别人的表一起动）：
        //    消防站是全图最安全的点（3 只丧尸 / 3 分钟 / 无战斗门槛）⇒ 它**不许比一个常规危险度的小点更肥**。
        //    参照物取两个"默认 5 只丧尸"的小点：河边小屋（6 分钟）与 守林人小屋（10 分钟）。
        //    （胜率不是成本——反过来也一样：没让你付代价的点，就不该给你更多。）
        // 实测基线（2026-07-14）：河边小屋 21 堆/45 件、守林人小屋 22 堆/41 件、瞭望台 17 堆/23 件、
        //    小药店 13 堆/15 件（刻意最薄）、**消防站 17 堆/24 件** —— 落在小点带内，且低于两个参照物。
        int Stacks(string dest) => ExplorationCache.CacheIdsFor(dest)
            .Sum(id => ExplorationCache.Resolve(id, new StoryFlags())!.Value.Loot.Count);
        int Quantity(string dest) => ExplorationCache.CacheIdsFor(dest)
            .Sum(id => ExplorationCache.Resolve(id, new StoryFlags())!.Value.Loot.Sum(l => l.Quantity));

        foreach (string reference in new[] { ExplorationCache.RiversideCabinName, ExplorationCache.WatchersCabinName })
        {
            Assert.True(Stacks(ExplorationCache.FireStationName) < Stacks(reference),
                $"消防站（低危）掉落堆数 {Stacks(ExplorationCache.FireStationName)} 不该 ≥ {reference} 的 {Stacks(reference)}——安全的点给得更多，其他点的代价就白付了");
            Assert.True(Quantity(ExplorationCache.FireStationName) < Quantity(reference),
                $"消防站（低危）掉落件数 {Quantity(ExplorationCache.FireStationName)} 不该 ≥ {reference} 的 {Quantity(reference)}");
        }
    }

    [Fact]
    public void FireStation_GearWall_HasTheOnlyWeapon_AndItIsTheAxe()
    {
        // 消防斧挂在器材墙上——这座建筑里最不需要解释的东西。且它是全站**唯一**的武器（低危点不发第二把）。
        var results = Results();
        var weapons = results.SelectMany(r => r.Loot).Where(l => l.Kind == LootKind.Weapon).ToList();
        Assert.Single(weapons);
        Assert.Equal(ExplorationCache.AxeName, weapons[0].RefId);

        CacheResult wall = ExplorationCache.Resolve(ExplorationCache.FireStationGearWallId, new StoryFlags())!.Value;
        Assert.Contains(wall.Loot, l => l.Kind == LootKind.Weapon && l.RefId == ExplorationCache.AxeName);
    }

    [Fact]
    public void 消防斧全图恰两处_消防站与联合收割机仓库_南林村庄柴垛已撤()
    {
        // 🔴 本单调整了 impl-axe 的投放分布（已 journal 告知）：低危近点一把 + 中点远处一把（配套那本造消防斧的书），
        // 撤掉南林村庄·柴垛那第三把（大点/中后期/丧尸围困——走到那儿的人早就有消防斧了，纯冗余）。
        var found = AllDestinations()
            .SelectMany(ExplorationCache.CacheIdsFor)
            .Where(id => ExplorationCache.Resolve(id, new StoryFlags()) is { } r
                         && r.Loot.Any(l => l.Kind == LootKind.Weapon && l.RefId == ExplorationCache.AxeName))
            .ToList();

        Assert.Equal(2, found.Count);
        Assert.Contains(ExplorationCache.FireStationGearWallId, found);
        Assert.Contains(ExplorationCache.WarehouseLumberRackId, found);
        Assert.DoesNotContain(ExplorationCache.VillageWoodpileId, found);

        // 守林人小屋·后院柴房：authored 钩子「斧子不见了——大概是主人最后用它做了别的事」⇒ 永远不许往里塞消防斧。
        Assert.DoesNotContain(ExplorationCache.RangersCabinShedId, found);
    }

    [Fact]
    public void Completion_FireStation_TotalIsFive_AndRisesWithSearches()
    {
        var flags = new StoryFlags();
        var (done0, total) = ExplorationProgress.Completion(ExplorationCache.FireStationName, flags, christineLeftForRevenge: false);
        Assert.Equal(5, total);
        Assert.Equal(0, done0);

        flags.Set(ExplorationCache.FireStationGearWallFlag, "true");
        Assert.Equal(1, ExplorationProgress.Completion(ExplorationCache.FireStationName, flags, false).done);
    }

    [Fact]
    public void NarrativeSpots_FireStation_EnvironmentalOnly_ResolveWithPages()
    {
        // 用户**没给**消防站的剧情梗概 ⇒ 只做环境叙事，不编造角色/前史（authored 内容归用户）。
        var spots = NarrativeSpotRegistry.ForDestination(ExplorationCache.FireStationName).ToList();
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
        => FireStationIds.Select(id => ExplorationCache.Resolve(id, new StoryFlags())!.Value).ToArray();

    /// <summary>全部有搜刮点的目的地（消防斧分布统计用）。</summary>
    private static string[] AllDestinations()
        => new[]
        {
            ExplorationCache.RiversideCabinName, ExplorationCache.HarvesterWarehouseName,
            ExplorationCache.WatchersCabinName, ExplorationCache.CityRooftopLookoutName,
            ExplorationCache.BroadcastStationName, ExplorationCache.GoldfingerBaseName,
            ExplorationCache.EastNewVillageName, ExplorationCache.GasStationName,
            ExplorationCache.SupermarketName, ExplorationCache.HospitalName,
            ExplorationCache.FireStationName,
            VillageRescue.DestinationName, NurseRecruit.DestinationName,
        };
}
