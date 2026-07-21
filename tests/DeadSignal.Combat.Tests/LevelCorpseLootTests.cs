using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>探索关杀敌也要落尸、也要能搜刮</b>（用户拍板）。
///
/// <para><b>为什么这条通道非通不可</b>：「敌人掉装备」此前<b>只在营地生效</b>——探索关直接不落尸
/// （<c>CampMain.OnAnyActorDied</c> 见到 <c>_currentLevel != null</c> 就 return）。可<b>出门探索恰恰是玩家
/// 该拿装备的地方</b>：超市骗局那 4 个持匕首的敌对幸存者、日后探索关的任何持械敌人，杀了一件东西都掉不出来，
/// 而营地夜袭的劫掠者只有匕首和手枪 ⇒ 整个「敌人＝行走的战利品」的设计价值大打折扣。</para>
///
/// <para><b>本单不新写一套扒装备逻辑，也不发明新交互</b>——两样都是复用：
/// <list type="bullet">
/// <item>扒什么：<see cref="CorpseLoot.Strip"/>（持什么掉什么、穿什么扒什么，武器在前、零掷骰）——营地探索同一条。</item>
/// <item>怎么搜：<see cref="ContainerLoot"/> + <see cref="LootSession"/>（逐件转出、走开即停手）——与探索关既有的
/// 物资搜刮点同一条链路，尸体只是<b>又一个可搜刮容器</b>。</item>
/// </list></para>
///
/// <para><b>本类要钉死的新规则只有一条半</b>：
/// ① <see cref="CorpseNaming"/> —— 尸体容器的命名与路由（营地/探索关共用），<b>序号必须唯一</b>
/// （撞名＝后一具尸体把前一具的登记顶掉，前一具身上的东西静默蒸发）；
/// ② 尸体容器名与<b>全部 authored 发现点 id</b> 结构性隔离（authored 一律 ascii snake_case，尸体名带中文
/// 「的尸体 #」）⇒ 动态尸体永远不会误触发剧情点，剧情点也永远不会被当成尸体去搜。</para>
///
/// <para>探索关尸体不进营地 <c>CorpseYard</c>，但位置、剩余遗物与半天计数跨关卡保存；
/// 重访同一地点时恢复搜刮点，满三个半天才连同未取回物资一起消失。</para>
/// 🔴 authored 剧情尸体（祖母/树上的哥顿/帮众/克莉丝汀）是<b>发现点</b>、不是本通道造出来的战斗尸体，两者
/// 命名空间隔离（见下），本单一根汗毛都没碰。</para>
/// </summary>
public class LevelCorpseLootTests
{
    // ============ ① 命名：撞名就是印钱的反面——静默蒸发 ============

    /// <summary>
    /// 🔴 <b>同一种敌人杀两个，必须是两个容器</b>。超市据点一次刷 4 个<b>同名</b>的「据点幸存者」——
    /// 若容器名不带序号，第二具尸体的登记会<b>顶掉</b>第一具（<see cref="ContainerLoot.Register"/> 按名覆盖），
    /// 玩家杀了 4 个人只搜得到 1 具的东西，另外 3 把匕首静默蒸发。
    /// </summary>
    [Fact]
    public void 同名敌人的每一具尸体_都是各自独立的容器()
    {
        var names = new HashSet<string>();
        for (int seq = 1; seq <= 4; seq++)
        {
            Assert.True(names.Add(CorpseNaming.ContainerName("据点幸存者", seq)), "同名敌人的尸体容器撞名了");
        }

        Assert.Equal(4, names.Count);
    }

    /// <summary>营地与探索关<b>同一条命名规则</b>（<c>CorpseYard</c> 也走它）——玩家看到的字面一致。</summary>
    [Fact]
    public void 尸体容器名_是人话_不是内部id()
    {
        Assert.Equal("据点幸存者的尸体 #3", CorpseNaming.ContainerName("据点幸存者", 3));
        Assert.Equal("丧尸的尸体 #1", CorpseNaming.ContainerName("丧尸", 1));
        Assert.Equal("丧尸的尸体 #远征1", CorpseNaming.ExplorationContainerName("丧尸", 1));
        Assert.NotEqual(CorpseNaming.ContainerName("丧尸", 1), CorpseNaming.ExplorationContainerName("丧尸", 1));
        Assert.True(CorpseNaming.IsCorpseContainer(CorpseNaming.ExplorationContainerName("丧尸", 1)));
    }

    // ============ ② 路由：动态尸体 vs authored 发现点，结构性隔离 ============

    /// <summary>探索关里踏上一具尸体 ⇒ 上报的就是这个容器名，营地层据此认出"这是尸体，不是剧情点"。</summary>
    [Fact]
    public void 尸体容器名_认得出自己()
    {
        Assert.True(CorpseNaming.IsCorpseContainer(CorpseNaming.ContainerName("据点幸存者", 1)));
        Assert.True(CorpseNaming.IsCorpseContainer(CorpseNaming.ContainerName("丧尸", 27)));

        Assert.False(CorpseNaming.IsCorpseContainer(null));
        Assert.False(CorpseNaming.IsCorpseContainer(""));
    }

    /// <summary>
    /// 🔴 <b>全部 authored 发现点 id 都不许被误判成尸体</b>——否则踏进"帮众尸体"这个<b>剧情发现点</b>
    /// 会被当成一具战斗尸体去搜，那 4 屏叙事凭空消失。
    /// <para>判据是<b>结构性</b>的：authored id 一律 ascii snake_case（<c>cache_</c> / <c>discovery_</c> /
    /// <c>narrative_</c> 前缀），尸体容器名一律带中文「的尸体 #」⇒ 两个命名空间不可能相交。
    /// 日后新增任何 authored 点，只要照旧用 ascii id，就自动继续隔离——不需要有人记得回来加一行黑名单。</para>
    /// </summary>
    [Fact]
    public void 全部authored发现点id_都不会被当成尸体()
    {
        foreach (string id in AllAuthoredDiscoveryIds())
        {
            Assert.False(CorpseNaming.IsCorpseContainer(id), $"authored 发现点「{id}」被误判成了尸体容器");
        }
    }

    /// <summary>
    /// 反向隔离：一具尸体的容器名<b>不会被任何 authored 解析器认领</b>（缓存点/叙事点/剧情尸体全部返回 null）
    /// ⇒ 尸体绝不会凭空触发一段剧情、也不会去置某个剧情 flag。
    /// </summary>
    [Fact]
    public void 尸体容器名_不会被任何authored解析器认领()
    {
        var flags = new StoryFlags();
        string corpse = CorpseNaming.ContainerName("据点幸存者", 1);

        Assert.Null(ExplorationCache.Resolve(corpse, flags));
        Assert.Null(NarrativeSpotRegistry.Resolve(corpse, flags));
        Assert.Null(GoldfingerDiscovery.Resolve(corpse, flags));
        Assert.Null(VillageRescue.Resolve(corpse, flags));
    }

    // ============ ③ 战利品：复用 Strip，一条不另写 ============

    /// <summary>
    /// <b>超市骗局那 4 个持匕首的敌对幸存者</b>：杀一个，尸体里就躺着<b>他的匕首 + 他身上那两层衣服</b>
    /// （[T56] 再加<b>一根骨头</b>——用户拍板「尸体固定产出一个骨头」）。
    /// 武器在前——玩家点开尸体第一眼该看见的就是那把家伙；骨头在最后。
    /// </summary>
    [Fact]
    public void 据点幸存者的尸体_里面是他的匕首和他的衣服和一根骨头()
    {
        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(
            ArmorTable.SurvivorArmor(), new[] { WeaponTable.Dagger() });

        Assert.Equal(
            new[]
            {
                LootItem.Weapon("匕首"),
                LootItem.Armor("皮夹克"),
                LootItem.Armor("长袖布衣"),
                LootItem.Material("bone", 1),
            },
            loot);
    }

    /// <summary>
    /// 关内丧尸<b>只掉衣服、不掉爪子</b>——探索关走的是营地那条同一个 <see cref="CorpseLoot.Strip"/>，
    /// 天生武器的排除是它<b>结构性</b>兜住的（不在 <c>WeaponTable.Arsenal</c> ⇒ 按名回查恒空），
    /// 探索关这边一行都不用另写。金手指帮的 4 名活人守备走 Raider 的持械尸体通道；本测试只钉丧尸天然武器不掉落。
    /// </summary>
    [Fact]
    public void 关内丧尸的尸体_只掉衣服_不掉爪击()
    {
        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(
            ZombieOutfit.ArmorOf("穿牛仔外套的"), new[] { WeaponTable.ZombieClaw() });

        Assert.DoesNotContain(loot, l => l.Kind == LootKind.Weapon);
        Assert.Equal(
            new[] { "牛仔外套", "长袖布衣", "长裤" },
            loot.Where(l => l.Kind == LootKind.Armor).Select(l => l.RefId));
    }

    /// <summary>
    /// 光尸体（衣不蔽体 + 空手）身上<b>没有一件装备</b>——但 [T56] 之后它<b>仍有一根骨头</b>。
    /// <para>
    /// ⚠️ <b>这条测试的结论变了</b>。它从前叫 <c>光尸体_什么都没有_就不该是一个可搜刮点</c>。
    /// 用户随后拍板「<b>尸体固定产出一个骨头</b>」⇒ <see cref="CorpseLoot.Strip"/> <b>永不返回空</b>
    /// ⇒ 光尸体<b>现在也是</b>可搜刮点了。
    /// </para>
    /// <para>
    /// <b>探索关这边尤其无副作用</b>：关内的敌人尸体<b>本来就</b>是可搜刮点（掉武器、掉衣服），
    /// 往里加一根骨头**不产生任何新的交互点**。真正需要掂量的是营地侧（尸潮），
    /// 而那边 <b>85% 的丧尸尸体本来就已经是搜刮点</b>（9 个日常着装预设里只有「衣不蔽体」是空的）
    /// ⇒ 固定产骨把它从 85% 抬到 100%，不是从 0 抬到 100%。
    /// </para>
    /// </summary>
    [Fact]
    public void 光尸体_没有装备_但仍有一根骨头()
    {
        IReadOnlyList<LootItem> loot =
            CorpseLoot.Strip(ZombieOutfit.ArmorOf("衣不蔽体"), new WeaponLoadout().HeldWeapons);

        Assert.DoesNotContain(loot, l => l.Kind is LootKind.Weapon or LootKind.Armor);
        Assert.Single(loot);
        Assert.Equal(LootItem.Material("bone", 1), loot[0]);
    }

    // ============ ④ 搜刮：喂进既有的逐件搜刮链路，不发明新交互 ============

    /// <summary>
    /// <b>端到端（纯逻辑侧）</b>：尸体 → <see cref="ContainerLoot"/> 登记 → <see cref="LootSession"/> 逐件转出。
    /// 搜刮尸体和搜刮一个抽屉<b>是同一件事</b>：站着不动、一件一件往外掏、每件 3 秒。
    /// </summary>
    [Fact]
    public void 尸体喂进既有的逐件搜刮链路_一件一件转出来()
    {
        var container = new ContainerLoot();
        string id = CorpseNaming.ContainerName("据点幸存者", 1);
        IReadOnlyList<LootItem> loot = CorpseLoot.Strip(
            ArmorTable.SurvivorArmor(), new[] { WeaponTable.Dagger() });

        container.Register(id, loot);
        var session = new LootSession(id, container.Remaining(id));

        var taken = new List<LootItem>();
        for (int i = 0; i < 4; i++)   // [T56] 3 件装备 + 1 根骨头
        {
            IReadOnlyList<LootItem> outNow = session.Advance(LootSession.DefaultSecondsPerItem);
            Assert.Single(outNow);
            LootItem? real = container.TakeNext(id);   // 容器才是事实源（session 只管计时）
            Assert.NotNull(real);
            taken.Add(real!.Value);
        }

        Assert.Equal(
            new[]
            {
                LootItem.Weapon("匕首"),
                LootItem.Armor("皮夹克"),
                LootItem.Armor("长袖布衣"),
                LootItem.Material("bone", 1),   // [T56] 骨头也走同一条逐件搜刮链路，不发明新交互
            },
            taken);
        Assert.True(session.IsComplete);
        Assert.True(container.IsSearched(id));
        Assert.Equal(0, container.RemainingCount(id));
    }

    /// <summary>
    /// <b>走开＝停手，但东西还在原地</b>：正在掏的那件掉回尸体里，已掏出的归你。
    /// <para>⚠️ 这正是探索关的尸体点<b>必须可重复踏入</b>的理由——被丧尸打断后跑开，回头得能接着掏。
    /// （authored 发现点是一次性的，尸体不是。）</para>
    /// </summary>
    [Fact]
    public void 掏到一半跑了_剩下的还在尸体里_回头能接着掏()
    {
        var container = new ContainerLoot();
        string id = CorpseNaming.ContainerName("据点幸存者", 2);
        container.Register(id, CorpseLoot.Strip(ArmorTable.SurvivorArmor(), new[] { WeaponTable.Dagger() }));

        var first = new LootSession(id, container.Remaining(id));
        Assert.Single(first.Advance(LootSession.DefaultSecondsPerItem));
        Assert.Equal(LootItem.Weapon("匕首"), container.TakeNext(id));   // 匕首到手了，跑也带得走
        first.Advance(LootSession.DefaultSecondsPerItem * 0.6f);         // 第二件掏了一半…
        first.Interrupt();                                                // …被咬了一口，撒手

        Assert.False(container.IsSearched(id));                           // 尸体还没搜空
        var again = new LootSession(id, container.Remaining(id));         // 回头再来（再踏一次）
        Assert.Equal(3, again.RemainingCount);                            // [T56] 剩 2 件衣服 + 1 根骨头
        Assert.Equal(LootItem.Armor("皮夹克"), again.CurrentItem);        // 那件衣服还在原地，从头翻起
    }

    /// <summary>
    /// <b>背不动就带不走</b>（既有约束，本单不改）：一趟杀穿据点，四把匕首＋八件衣服未必背得回来——
    /// 负重上限先把你拦下。搜刮工时限制你"有时间拿"多少，负重限制你"能带走"多少，两把钳子方向不同。
    /// </summary>
    [Fact]
    public void 尸体上的东西_同样吃负重上限_背不动就留下()
    {
        LootItem dagger = LootItem.Weapon("匕首");
        double one = ItemWeights.OfLoot(dagger);
        var bag = new ExpeditionBag(one * 2);   // 只背得下两把

        Assert.True(bag.TryAdd(dagger));
        Assert.True(bag.TryAdd(dagger));
        Assert.False(bag.TryAdd(dagger));       // 第三具尸体的匕首：背不动，留在关里
    }

    // ---- 全部 authored 发现点 id（隔离测试的输入）----

    private static IEnumerable<string> AllAuthoredDiscoveryIds()
    {
        string[] destinations =
        {
            ExplorationCache.RiversideCabinName, ExplorationCache.HarvesterWarehouseName,
            ExplorationCache.WatchersCabinName, ExplorationCache.CityRooftopLookoutName,
            ExplorationCache.BroadcastStationName, ExplorationCache.GoldfingerBaseName,
            ExplorationCache.EastNewVillageName, ExplorationCache.GasStationName,
            ExplorationCache.SupermarketName, ExplorationCache.HospitalName,
            VillageRescue.DestinationName, NurseRecruit.DestinationName,
        };

        foreach (string dest in destinations)
        {
            foreach (string id in ExplorationCache.CacheIdsFor(dest))
            {
                yield return id;
            }
        }

        foreach (NarrativeSpot spot in NarrativeSpotRegistry.All)
        {
            yield return spot.Id;
        }

        yield return GoldfingerDiscovery.GangMemberCorpseId;
        yield return GoldfingerDiscovery.ChristineCorpseId;
        yield return GoldfingerDiscovery.GordonHangedId;
        yield return VillageRescue.RescueDiscoveryId;
        yield return NurseRecruit.MeetDiscoveryId;
        yield return SupermarketAmbush.ContactDiscoveryId;
        yield return SupermarketAmbush.InnerRingDiscoveryId;
        yield return RadioMainline.TransmitterDiscoveryId;
    }
}
