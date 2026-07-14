using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-T60] 破败教堂 —— <b>一关"视野"，不是一关"战力"</b>。
///
/// <para>用户原话（authored 唯一事实源）：「破败教堂，规模中，<b>穿过教堂的视野盲区，打开门看到后院墓地中有大量丧尸
/// （突然看到吓一跳的感觉）</b>，在这里可以找到一些军方留下的被烧了一半的忏悔录和一些被军方屠杀的人用血写在墙上的对军方的辱骂。」</para>
///
/// <para>
/// 🔴 本文件存在的理由：**把"吓一跳"从一句演出词，变成两个可以红/绿的数字。**
/// 项目里那套锥形视野/遮挡（<see cref="VisionLogic"/> / <c>VisionOcclusion</c>）一直在，却从没有一关拿它当主轴。
/// 这一关的每一堵墙都是为**挡视线**砌的。于是"惊吓"有了判据：
///   · <see cref="TheJumpScare_BeforeYouOpenTheDoor_YouSeeNotOneOfThem"/>：门关着 ⇒ 墓地可见丧尸数 <b>= 0</b>。
///   · <see cref="TheJumpScare_TheInstantYouStepIntoTheDoorway_AWholeGraveyardEntersYourVisionAtOnce"/>：
///     推开门、迈进门洞 ⇒ <b>≥8 只同时进视野锥</b>。
/// 这两条一红，"吓一跳"就没了 —— 它是机制，不是脚本。
/// </para>
/// </summary>
public class RuinedChurchTests
{
    // 关卡边界（与 TestExploration 的 LevelW/LevelH 一致）。
    private static readonly WallRect Level = new(0f, 0f, 2400f, 1600f);

    /// <summary>返回区（关外）：南面，正门正对着它。</summary>
    private static readonly (float X, float Y) ReturnZone = (1100f, 1500f);

    /// <summary>丧尸的碰撞直径（<c>Zombie.cs</c>：Radius = 13f）。围攻名额的几何全靠它。</summary>
    private const float ZombieDiameter = 26f;

    private static List<WallRect> WallsWithAllDoorsClosed()
        => LevelReachability.WithDoorsClosed(RuinedChurch.Walls(), RuinedChurch.Doors());

    private static Vector2 V((float X, float Y) p) => new(p.X, p.Y);

    /// <summary>白昼的视野锥（教堂不是恒暗关，玩家吃满档 300/60°）。</summary>
    private static VisionLogic.VisionCone DaylightCone()
        => VisionLogic.ConeFor(VisionLogic.AmbientLight(DayPhase.DayExplore, indoorsDark: false));

    /// <summary>站在 <paramref name="eye"/>、朝 <paramref name="facing"/>，在 <paramref name="obstacles"/> 之下看得见几只。</summary>
    private static int VisibleCount(
        IReadOnlyList<WallRect> obstacles,
        (float X, float Y) eye,
        Vector2 facing,
        IReadOnlyList<(float X, float Y)> targets)
    {
        VisionLogic.VisionCone cone = DaylightCone();
        int n = 0;
        foreach ((float X, float Y) t in targets)
        {
            bool occluded = ExplorationWalls.SegmentHitsAnyWall(obstacles, eye.X, eye.Y, t.X, t.Y);
            if (VisionLogic.CanSee(V(eye), facing, V(t), cone, occluded))
                n++;
        }
        return n;
    }

    // ══════════════════════ 🔴 「吓一跳」——这一关的灵魂，两条 ══════════════════════

    /// <summary>
    /// 🔴 <b>推开门之前，你一只都看不见。</b>
    /// 门在本仓的口径里就是墙层（<c>VisionOcclusion.WallMask</c>）⇒ 关着的门<b>挡视线</b>。
    /// 站在后院门前（<see cref="RuinedChurch.DoorApproachPoint"/>）、脸朝北，到墓地里 12 只丧尸的
    /// <b>每一条视线都被挡死</b>。这不是"美术没画出来"，是几何上真的看不见。
    /// </summary>
    [Fact]
    public void TheJumpScare_BeforeYouOpenTheDoor_YouSeeNotOneOfThem()
    {
        int seen = VisibleCount(
            WallsWithAllDoorsClosed(),
            RuinedChurch.DoorApproachPoint,
            new Vector2(0f, -1f), // 朝北，正对着那扇门
            RuinedChurch.GraveyardZombieSpots);

        Assert.Equal(0, seen);
    }

    /// <summary>
    /// 🔴 <b>推开门、迈进门洞的那一刻，一整片墓地同时进入你的视野。</b>
    /// 同一个人、同一个朝向、同一片丧尸——<b>只是门板从墙层里摘掉了</b>，可见数就从 0 跳到 ≥8。
    /// <b>信息量在一帧里爆炸，这就是"突然看到吓一跳"。</b>
    /// <para>
    /// 而 <c>docs/research/2026-07-14-lanchester.md</c> 说得很清楚：2 只围攻＝胜率 16.6%，3 只＝0.8%，4 只起＝<b>0%</b>。
    /// ⇒ <b>你看到的不是一场仗，是一个必须立刻离开的房间。</b>
    /// </para>
    /// </summary>
    [Fact]
    public void TheJumpScare_TheInstantYouStepIntoTheDoorway_AWholeGraveyardEntersYourVisionAtOnce()
    {
        List<WallRect> opened = LevelReachability.WithOneDoorOpen(
            RuinedChurch.Walls(), RuinedChurch.Doors(), RuinedChurch.BackyardDoor);

        int seen = VisibleCount(
            opened,
            RuinedChurch.DoorwayPoint,
            new Vector2(0f, -1f),
            RuinedChurch.GraveyardZombieSpots);

        Assert.True(seen >= 8, $"推开后院门、迈进门洞，应当**一眼看到一片**（≥8 只），实际只有 {seen} 只进锥——惊吓没了。");
    }

    /// <summary>
    /// 「大量丧尸」是用户的原话 ⇒ 墓地那一群不能是三五只。同时教堂本体要**稀**（进关不会当场被淹）。
    /// </summary>
    [Fact]
    public void TheGraveyardIsAHorde_AndTheChurchItselfIsNot()
    {
        Assert.True(RuinedChurch.GraveyardZombieSpots.Count >= 10,
            $"后院墓地只有 {RuinedChurch.GraveyardZombieSpots.Count} 只——用户要的是「大量」。");
        Assert.True(RuinedChurch.ChurchZombieSpots.Count <= 4,
            "教堂本体应当稀——一进关就被淹，玩家永远走不到那扇门前，这一关的全部就没了。");
    }

    // ══════════════════════ 视野盲区：「穿过教堂的视野盲区」 ══════════════════════

    /// <summary>
    /// 🔴 <b>从正门望进去，你看不见那扇门，也看不见圣坛。</b>
    /// 屏风（中殿↔圣坛的横墙）+ 中央走道里左右交错的三根立柱 ⇒ <b>走道是折的</b>。
    /// ⇒ 玩家是**摸着走进去**的：他不知道尽头有什么，直到他站在门前。这是"吓一跳"能成立的前提。
    /// </summary>
    [Fact]
    public void FromTheFrontDoor_YouCanSeeNeitherTheChancelNorTheBackyardDoor()
    {
        List<WallRect> walls = WallsWithAllDoorsClosed();
        (float X, float Y) eye = RuinedChurch.FrontDoorPoint;

        // 后院门（门板中心）
        Assert.True(
            ExplorationWalls.SegmentHitsAnyWall(walls, eye.X, eye.Y, RuinedChurch.BackyardDoorX, RuinedChurch.GraveyardWallY + 6f),
            "站在正门就能一眼望到后院门——屏风/立柱形同虚设，'穿过视野盲区'没了。");

        // 祭台（圣坛正中）
        Assert.True(
            ExplorationWalls.SegmentHitsAnyWall(walls, eye.X, eye.Y, 1100f, 630f),
            "站在正门就能看见祭台——圣坛不该从门口一览无余。");
    }

    /// <summary>
    /// 中殿的长椅把这里切成一条条**东西向的窄廊**：站在正门，中殿深处大多数搜刮点是**看不见**的
    /// —— 你得挤进去。（这是"视野盲区"的正面表述：不是"路难走"，是"看不清"。）
    /// </summary>
    [Fact]
    public void ThePewsAndColumnsHideMostOfWhatIsInTheChurch()
    {
        List<WallRect> walls = WallsWithAllDoorsClosed();
        (float X, float Y) eye = RuinedChurch.FrontDoorPoint;

        var deep = RuinedChurch.CacheSpots
            .Where(s => s.Zone is ChurchZone.Nave or ChurchZone.Chancel or ChurchZone.Graveyard)
            .ToList();

        int hidden = deep.Count(s => ExplorationWalls.SegmentHitsAnyWall(walls, eye.X, eye.Y, s.X, s.Y));

        Assert.True(hidden * 10 >= deep.Count * 8,
            $"站在正门就能直接看见 {deep.Count - hidden}/{deep.Count} 处深处点位——盲区不够，教堂成了开阔地。");
    }

    // ══════════════════════ 两条互相拉扯、且都必须成立的硬不变量 ══════════════════════

    /// <summary>
    /// 🔴 <b>不变量 A：墓地关得死。</b>
    /// 墓地边界上的<b>每一个洞都装了门</b>（与医院的「每道边界必留一个关不上的洞」<b>恰恰相反</b>，是刻意的）。
    /// ⇒ 两扇门全关上，墓地与教堂**真的断开**（栅格 BFS 判定）。
    /// <b>"推开门看到一片丧尸 ⇒ 转身跑 ⇒ 把门关上"这条玩法，只有在墓地真的关得死时才成立。</b>
    /// </summary>
    [Fact]
    public void InvariantA_WithBothDoorsShut_TheGraveyardIsTrulySealedOff()
    {
        // ① 边界上没有一个"关不上的洞"
        Assert.All(RuinedChurch.GraveyardDoorways(), d => Assert.NotNull(d.DoorName));
        Assert.Equal(RuinedChurch.GraveyardDoorways().Count, RuinedChurch.Doors().Count);

        // ② 全关 ⇒ 墓地里的每一只丧尸都走不到教堂里来
        List<WallRect> closed = WallsWithAllDoorsClosed();
        foreach ((float X, float Y) z in RuinedChurch.GraveyardZombieSpots)
        {
            Assert.False(
                LevelReachability.PathExists(closed, Level, z, RuinedChurch.FrontDoorPoint),
                $"关上两扇门之后，墓地里 ({z.X},{z.Y}) 的丧尸**照样走得进教堂** —— 关门就成了安慰剂。");
        }
    }

    /// <summary>
    /// 🔴 <b>不变量 B：玩家永远跑得掉。</b>
    /// 外墙的两个入口<b>都是关不上的洞</b>，关内能关的门<b>一扇也不在退路上</b>
    /// ⇒ 就算把关内的门全关死，从教堂里任意一处仍走得出关外。
    /// <para>A 与 B 是**互相拉扯**的（能关死的门越多，越容易把自己反锁）。只有同时钉住，设计才叫成立。</para>
    /// </summary>
    [Fact]
    public void InvariantB_EvenWithEveryDoorShut_YouCanStillWalkOutOfTheChurch()
    {
        // ① 两个入口都关不上
        var entrances = RuinedChurch.Entrances();
        Assert.True(entrances.Count >= 2, "外墙至少两个入口：正门被堵死，不等于这一趟白跑。");
        Assert.All(entrances, e => Assert.Null(e.DoorName));

        // ② 全关 ⇒ 关内任意代表点仍走得到关外
        List<WallRect> closed = WallsWithAllDoorsClosed();
        (float X, float Y)[] inside =
        {
            RuinedChurch.FrontDoorPoint,     // 门厅
            (350f, 1080f),                   // 中殿·西侧廊（最窄的地方）
            (1100f, 900f),                   // 中殿·中央走道
            RuinedChurch.DoorApproachPoint,  // 圣坛·后院门前
        };
        foreach ((float X, float Y) p in inside)
        {
            Assert.True(
                LevelReachability.PathExists(closed, Level, p, ReturnZone),
                $"把关内的门全关上之后，({p.X},{p.Y}) 走不出关外 —— 玩家被自己关死在里面了。");
        }
    }

    /// <summary>
    /// 万一你已经迈进了墓地：门<b>没上锁</b>，推开它就回得来（<see cref="ExplorationDoor"/> 根本没有"锁"这个概念
    /// ——隔离是<b>结构性</b>的，不靠"记得别锁"）。只开后院门 ⇒ 墓地↔返回区连通。
    /// </summary>
    [Fact]
    public void IfYouAreAlreadyInTheGraveyard_PushingTheDoorBackOpenGetsYouHome()
    {
        List<WallRect> onlyBackyardOpen = LevelReachability.WithOneDoorOpen(
            RuinedChurch.Walls(), RuinedChurch.Doors(), RuinedChurch.BackyardDoor);

        Assert.True(
            LevelReachability.PathExists(onlyBackyardOpen, Level, (1100f, 300f), ReturnZone),
            "人在墓地里、推开后院门，却走不回关外 —— 这一关会把玩家永久卡死。");
    }

    /// <summary>门是关着的开局：<b>这一关成立的前提</b>（门开着＝你老远就看见了一片丧尸，惊吓没了）。</summary>
    [Fact]
    public void EveryDoorStartsClosed_OtherwiseThereIsNoSurprise()
        => Assert.All(RuinedChurch.Doors(), d => Assert.Equal(DoorState.Closed, d.Initial));

    /// <summary>门洞必须过得去人（宽 &gt; 2×导航体半径），否则寻路判"此路不通"，门就成了墙。</summary>
    [Fact]
    public void DoorwaysAreWideEnoughToWalkThrough()
        => Assert.True(RuinedChurch.DoorwayWidth > 2f * ExplorationWalls.NavAgentRadius);

    /// <summary>没有一处搜刮点/丧尸被砌进墙里（几何与布点同源，这条是它的证明）。</summary>
    [Fact]
    public void NothingIsBuriedInsideAWall()
    {
        IReadOnlyList<WallRect> walls = RuinedChurch.Walls();
        foreach (ChurchCacheSpot s in RuinedChurch.CacheSpots)
        {
            Assert.False(LevelReachability.IsBlocked(walls, s.X, s.Y, 0f),
                $"搜刮点「{s.Label}」({s.X},{s.Y}) 被砌进墙里了。");
        }
        foreach ((float X, float Y) z in RuinedChurch.ChurchZombieSpots.Concat(RuinedChurch.GraveyardZombieSpots))
        {
            Assert.False(LevelReachability.IsBlocked(walls, z.X, z.Y, 0f),
                $"丧尸 ({z.X},{z.Y}) 被砌进墙里了。");
        }
    }

    // ══════════════════════ 物资：规模「中」，且「高风险不是永远高回报」 ══════════════════════

    /// <summary>规模「中」＝ 10~16 处搜刮点（既有硬口径：小 5~9 / 中 10~16 / 大 30+），且四处登记同步。</summary>
    [Fact]
    public void MediumSite_TwelveCaches_AllFourRegistriesAgree()
    {
        IReadOnlyList<string> ids = ExplorationCache.CacheIdsFor(RuinedChurch.DestinationName);
        Assert.InRange(ids.Count, 10, 16);
        Assert.Equal(12, ids.Count);

        // ① 关卡铺的点 == 注册表登记的点（一个不多一个不少）
        Assert.Equal(
            ids.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            RuinedChurch.CacheSpots.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal).ToList());

        // ② 每个 id 都有 flag、都 Resolve 得出东西
        var flags = new StoryFlags();
        foreach (string id in ids)
        {
            Assert.False(string.IsNullOrEmpty(ExplorationCache.FlagForCache(id)), $"{id} 没有登记 flag。");
            CacheResult? r = ExplorationCache.Resolve(id, flags);
            Assert.NotNull(r);
            Assert.NotEmpty(r!.Value.Loot);
        }
    }

    /// <summary>一次性：搜过就是空的（flag 已置 ⇒ 二访 null）。</summary>
    [Fact]
    public void EachCacheIsOneShot()
    {
        var flags = new StoryFlags();
        foreach (string id in ExplorationCache.CacheIdsFor(RuinedChurch.DestinationName))
        {
            CacheResult? first = ExplorationCache.Resolve(id, flags);
            Assert.NotNull(first);
            flags.Set(first!.Value.StoryFlag, "true");
            Assert.Null(ExplorationCache.Resolve(id, flags));
        }
    }

    /// <summary>
    /// 🔴 <b>这一关最值钱的东西不是银烛台，是那两页残纸。</b>（「高风险不是永远高回报」是用户拍板的通则）
    /// 教堂**一把枪、一发子弹都没有** —— 教堂本来就不该有。别因为"后期高危"就往里塞高价值物资。
    /// </summary>
    [Fact]
    public void HighRiskIsNotAlwaysHighReward_NotOneGunAndNotOneRoundInTheWholeChurch()
    {
        var flags = new StoryFlags();
        foreach (string id in ExplorationCache.CacheIdsFor(RuinedChurch.DestinationName))
        {
            CacheResult r = ExplorationCache.Resolve(id, flags)!.Value;
            foreach (LootItem it in r.Loot)
            {
                Assert.NotEqual(LootKind.Weapon, it.Kind);
                Assert.False(it.RefId.StartsWith("ammo_", StringComparison.Ordinal),
                    $"教堂里出现了弹药（{it.RefId}）—— 这一关的回报是证据，不是补给。");
            }
        }
    }

    // ══════════════════════ authored 叙事：军方干了什么 ══════════════════════

    /// <summary>
    /// 用户给了两样东西，一样一处：<b>烧了一半的忏悔录</b>（军方留下的）与<b>墙上的血字</b>（被军方屠杀的人写的）。
    /// 它们是「军方干了什么」的第一手证据 —— 而广播台那条主线的岔口正是「回复军方 / 呼叫南方」。
    /// </summary>
    [Fact]
    public void TheTwoPiecesOfEvidenceTheUserAskedFor_AreBothThere()
    {
        var spots = NarrativeSpotRegistry.ForDestination(RuinedChurch.DestinationName).ToList();
        Assert.Equal(2, spots.Count);
        Assert.Contains(spots, s => s.Id == NarrativeSpotRegistry.RuinedChurchConfessionSpotId);
        Assert.Contains(spots, s => s.Id == NarrativeSpotRegistry.RuinedChurchBloodWallSpotId);
        Assert.All(spots, s => Assert.NotEmpty(s.Pages));

        // 一次性（看过就不再弹）
        var flags = new StoryFlags();
        foreach (NarrativeSpot s in spots)
        {
            Assert.NotNull(NarrativeSpotRegistry.Resolve(s.Id, flags));
            flags.Set(s.StoryFlag, "true");
            Assert.Null(NarrativeSpotRegistry.Resolve(s.Id, flags));
        }
    }

    /// <summary>
    /// 🔴 <b>不替玩家下判断。</b>（同 <c>StuartManorTests</c> 的护栏，扩到"军方"这条线上）
    /// 让玩家<b>看到证据，自己决定要不要回复军方</b> —— 游戏里一句"军方是恶魔"都不许有。
    /// <para>⚠️ 同时<b>不许软化</b>（CLAUDE.md：黑暗向设定是有意为之）：这条护栏扫的是<b>结论</b>，不是<b>内容</b>。</para>
    /// </summary>
    [Fact]
    public void TheGameShowsYouTheEvidence_ItNeverTellsYouWhatToThinkOfIt()
    {
        string[] verdicts = { "复仇", "报应", "讨回", "公道", "罪有应得", "该死", "恶魔", "畜生", "活该", "禽兽" };
        foreach (NarrativeSpot s in NarrativeSpotRegistry.ForDestination(RuinedChurch.DestinationName))
        {
            string text = s.Title + string.Concat(s.Pages);
            foreach (string v in verdicts)
                Assert.DoesNotContain(v, text, StringComparison.Ordinal);
        }
    }

    /// <summary>叙事点是 <c>narrative_</c> 前缀的 ascii id ⇒ 与可扒的战斗尸体（中文「的尸体 #」）命名空间不相交。</summary>
    [Fact]
    public void AuthoredEvidenceIsNeverALootableCorpse()
    {
        foreach (NarrativeSpot s in NarrativeSpotRegistry.ForDestination(RuinedChurch.DestinationName))
        {
            Assert.StartsWith("narrative_", s.Id, StringComparison.Ordinal);
            Assert.False(s.Id.Contains(CorpseNaming.Marker, StringComparison.Ordinal));
        }
    }

    // ══════════════════════ Sim 零漂移（结构性） ══════════════════════

    /// <summary>
    /// 新点位<b>不在 Sim 的结算路径上</b>：<c>RuinedChurch</c> / <c>RefugeeCamp</c> / <c>LevelReachability</c>
    /// 都不在 <c>DeadSignal.Sim.csproj</c> 的 Link 清单里 ⇒ Sim 根本读不到它们，既有基线不可能漂移。
    /// （这条与 <c>RefugeeCampTests</c> 的同名护栏成对；两个都在，谁删掉一个都还剩一个。）
    /// </summary>
    [Fact]
    public void SimCannotEvenSeeTheseLevels()
    {
        // `AssemblyName.Name` 声明为 string?（实际不会为 null）⇒ 兜一个空串，免 CS8604。
        Assert.DoesNotContain(typeof(RuinedChurch).Assembly.GetName().Name ?? "", "DeadSignal.Sim", StringComparison.Ordinal);
        // 结构性事实：关卡几何只被 Godot 消费层与本测试引用，Sim 的 Duel/Arena/Ballistics 一处都没引。
        Assert.Empty(typeof(RuinedChurch).Assembly
            .GetReferencedAssemblies()
            .Where(a => a.Name is not null && a.Name.Contains("DeadSignal.Sim", StringComparison.Ordinal)));
    }

    /// <summary>过道/门洞的战术含义靠丧尸直径撑着——这个数变了，上面那些"一次过得来几只"的话就全变了。</summary>
    [Fact]
    public void ZombieDiameterIsTwentySix_TheNumberEveryChokepointRestsOn()
        => Assert.Equal(26f, ZombieDiameter);
}
