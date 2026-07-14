using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 废弃医院 [T49] 的**楼层平面**（<see cref="ExplorationWalls.HospitalWalls"/> / <see cref="ExplorationWalls.HospitalDoors"/>）。
///
/// <para>
/// 🔴 <b>这批断言钉的是一件事：医院不许是个死亡陷阱。</b>
/// 用户口径「大量丧尸 + <b>中</b>危」——这两句只有在**绕得过去**时才同时成立。
/// <c>docs/research/2026-07-14-combat-cost.md</c> 已经证明连场战斗不能拿胜率相乘去想
/// （单场 68% 胜率、不治疗连打，能撑过第 3 个的只剩 3.5%）⇒ 14 只丧尸如果**必须一只只清完**，
/// 医院就不是"中危"，是**必死**。所以"能绕"不是手感问题，是这一关的<b>正确性问题</b>，故上单测。
/// </para>
///
/// <para>
/// 落到几何上就是三条硬不变量：<b>①多入口</b>（正门被堵死不等于进不去）<b>②每道分区边界≥2 个门洞</b>
/// （一条走廊挤满丧尸时还有第二条）<b>③每道边界上"装了可关的门"的门洞数 &lt; 门洞总数</b>
/// （关门是<b>隔开丧尸</b>的手段，绝不能变成<b>把自己反锁在里面</b>的手段——任何一道边界都必须留一个关不上的洞）。
/// </para>
/// </summary>
public class HospitalLayoutTests
{
    private static IReadOnlyList<WallRect> Walls() => ExplorationWalls.HospitalWalls();
    private static IReadOnlyList<ExplorationDoor> Doors() => ExplorationWalls.HospitalDoors();
    private static IReadOnlyList<HospitalBoundary> Boundaries() => ExplorationWalls.HospitalBoundaries();

    [Fact]
    public void 墙段几何非退化()
    {
        IReadOnlyList<WallRect> walls = Walls();
        Assert.NotEmpty(walls);
        Assert.All(walls, w =>
        {
            Assert.True(w.Width > 0f, "墙段宽须为正");
            Assert.True(w.Height > 0f, "墙段高须为正");
        });
    }

    /// <summary>①多入口：外墙上不止一个入口——正门被丧尸堵死，不等于这一趟白跑。</summary>
    [Fact]
    public void 外墙至少两个入口()
    {
        IReadOnlyList<HospitalEntrance> entrances = ExplorationWalls.HospitalEntrances();
        Assert.True(entrances.Count >= 2,
            $"外墙只有 {entrances.Count} 个入口——正门一堵就进不去了，这不是中危，是随机死。");
    }

    /// <summary>
    /// 🔴 <b>回家的路不能被自己关死</b>：外墙入口里必须至少有一个是**关不上的**。
    /// 否则玩家可以把自己焊死在一栋满是丧尸的楼里——那不是难度，那是 bug。
    /// </summary>
    [Fact]
    public void 外墙至少留一个关不上的入口()
    {
        IReadOnlyList<HospitalEntrance> entrances = ExplorationWalls.HospitalEntrances();
        Assert.Contains(entrances, e => e.DoorName == null);
    }

    /// <summary>②每道分区边界 ≥2 个门洞：一条走廊挤满丧尸时，还得有第二条路可走（"绕"才成立）。</summary>
    [Fact]
    public void 每道分区边界至少两个门洞()
    {
        Assert.All(Boundaries(), b =>
            Assert.True(b.Doorways.Count >= 2,
                $"边界「{b.Name}」只有 {b.Doorways.Count} 个门洞——这条路被堵死就没有替代路线，玩家只能站着清完。"));
    }

    /// <summary>
    /// ③🔴 <b>关门不许把自己反锁在里面</b>：每道边界上装了可关的门的门洞，必须**严格少于**门洞总数。
    /// 否则"关门隔开丧尸"这一手就变成了"把整层楼焊死"——玩家关完门，自己也再没有退路。
    /// </summary>
    [Fact]
    public void 每道边界都留有一个关不上的洞()
    {
        Assert.All(Boundaries(), b =>
        {
            int withDoor = b.Doorways.Count(d => d.DoorName != null);
            Assert.True(withDoor < b.Doorways.Count,
                $"边界「{b.Name}」的 {b.Doorways.Count} 个门洞全装了门（{withDoor} 个）——" +
                "关上就是把自己反锁在里面。每道边界必须留至少一个关不上的洞。");
        });
    }

    /// <summary>门洞宽须容得下导航体（AgentRadius 14 ⇒ 直径 28）：否则寻路认为过不去，AI 会绕开甚至卡死。</summary>
    [Fact]
    public void 门洞宽度容得下导航体()
    {
        Assert.True(ExplorationWalls.HospitalDoorwayWidth > ExplorationWalls.NavAgentRadius * 2f,
            "门洞比导航体直径还窄——寻路会判定此路不通。");
    }

    /// <summary>每一处「可关的门」都真的落在某个门洞上（门板不许砌在实心墙里，也不许悬空）。</summary>
    [Fact]
    public void 门板都落在门洞上()
    {
        IEnumerable<string> fromBoundaries = Boundaries()
            .SelectMany(b => b.Doorways)
            .Select(d => d.DoorName)
            .Where(n => n != null)
            .Select(n => n!);

        IEnumerable<string> fromEntrances = ExplorationWalls.HospitalEntrances()
            .Select(e => e.DoorName)
            .Where(n => n != null)
            .Select(n => n!);

        IReadOnlyList<string> declared = fromBoundaries.Concat(fromEntrances).ToList();
        IReadOnlyList<string> built = Doors().Select(d => d.Name).ToList();

        Assert.Equal(declared.OrderBy(x => x), built.OrderBy(x => x));
    }

    /// <summary>门板不许和墙段重叠：门是墙上的洞，不是墙上再糊一层。重叠会让"开门"开出一堵墙。</summary>
    [Fact]
    public void 门板不与墙段重叠()
    {
        IReadOnlyList<WallRect> walls = Walls();
        foreach (ExplorationDoor door in Doors())
        {
            foreach (WallRect w in walls)
            {
                bool overlaps = door.Rect.X < w.Right && w.X < door.Rect.Right
                             && door.Rect.Y < w.Bottom && w.Y < door.Rect.Bottom;
                Assert.False(overlaps,
                    $"门「{door.Name}」与墙段重叠——开了门还是一堵墙。");
            }
        }
    }

    /// <summary>
    /// 🔴 <b>搜刮点一个都不许被砌进墙里</b>：医院 30 处搜刮点是这一关的全部收益，
    /// 埋进墙里的点＝玩家永远走不到＝这一趟的收益凭空少一块（且没有任何提示）。
    /// </summary>
    [Fact]
    public void 三十处搜刮点无一被砌进墙里()
    {
        IReadOnlyList<WallRect> walls = Walls();
        IReadOnlyList<HospitalCacheSpot> spots = ExplorationWalls.HospitalCacheSpots;

        Assert.Equal(30, spots.Count);
        Assert.Equal(
            ExplorationCache.CacheIdsFor(ExplorationCache.HospitalName).OrderBy(x => x),
            spots.Select(s => s.Id).OrderBy(x => x));

        foreach (HospitalCacheSpot s in spots)
        {
            foreach (WallRect w in walls)
            {
                bool inside = s.X >= w.X && s.X <= w.Right && s.Y >= w.Y && s.Y <= w.Bottom;
                Assert.False(inside, $"搜刮点「{s.Id}」({s.X},{s.Y}) 落在墙段里——玩家永远搜不到它。");
            }
        }
    }

    /// <summary>
    /// 叙事调查点（分诊台公告 / 住院部病房）也不许被砌进墙里——那是这一关的**环境叙事**，
    /// 埋进墙里就等于把它从游戏里删了（且没有任何提示）。
    /// </summary>
    [Fact]
    public void 叙事调查点无一被砌进墙里()
    {
        IReadOnlyList<WallRect> walls = Walls();
        IReadOnlyList<NarrativeSpot> spots =
            NarrativeSpotRegistry.ForDestination(ExplorationCache.HospitalName).ToList();

        Assert.NotEmpty(spots);
        foreach (NarrativeSpot s in spots)
        {
            foreach (WallRect w in walls)
            {
                bool inside = s.X >= w.X && s.X <= w.Right && s.Y >= w.Y && s.Y <= w.Bottom;
                Assert.False(inside, $"叙事点「{s.Id}」({s.X},{s.Y}) 落在墙段里——玩家永远读不到它。");
            }
        }
    }

    /// <summary>丧尸也不许被生成在墙里（会卡死在墙内，既打不着也不会动）。</summary>
    [Fact]
    public void 丧尸布点无一落在墙里()
    {
        IReadOnlyList<WallRect> walls = Walls();
        IReadOnlyList<(float X, float Y)> spots = ExplorationWalls.HospitalZombieSpots;

        Assert.NotEmpty(spots);
        foreach ((float x, float y) in spots)
        {
            foreach (WallRect w in walls)
            {
                bool inside = x >= w.X && x <= w.Right && y >= w.Y && y <= w.Bottom;
                Assert.False(inside, $"丧尸布点 ({x},{y}) 落在墙段里——它会卡死在墙内。");
            }
        }
    }

    /// <summary>
    /// 墙真的挡视线（＝挡得住丧尸的感知，"绕"才有意义）：从大厅正中射向手术层正中的直线必然穿墙。
    /// 这条线同时也证明了碰撞/寻路（三者同一批矩形，见 <see cref="ExplorationWalls"/> 类注）。
    /// </summary>
    [Fact]
    public void 大厅到手术层的直线必然穿墙()
    {
        Assert.True(
            ExplorationWalls.SegmentHitsAnyWall(Walls(), 1180f, 1250f, 1180f, 240f),
            "从大厅一眼望穿到手术层——那就等于全楼的丧尸一开始就全看见你了。");
    }

    // ── 噪音：这一关的主轴 ──────────────────────────────────────────────────────
    //
    // 医院的正解是「**别开枪**、关门、绕路」。那句话不是气氛文案，它有数值前提——
    // 下面两条把这个前提钉死：哪天有人把枪声调轻、或把门声调响到同一个量级，这两条就该红。

    /// <summary>
    /// 🔴 <b>开一枪 = 把整层楼叫醒</b>：**最安静的那把枪**（手枪 350）的动静，也远远盖过
    /// 推一扇门（100）和走路（40）。医院分区隔墙的跨度是几百像素级 ⇒ 一声枪响足以横穿两三个分区。
    /// </summary>
    [Fact]
    public void 最静的枪也远比推门响得多()
    {
        double quietestGun = WeaponTable.Pistol().NoiseRadius;

        Assert.True(quietestGun >= NoiseLogic.DoorNoiseRadius * 3,
            $"最静的枪（{quietestGun}）没比推门（{NoiseLogic.DoorNoiseRadius}）响多少——" +
            "那「别开枪」就不再是一个有代价的选择，医院的整条潜行解法失去意义。");
        Assert.True(NoiseLogic.DoorNoiseRadius > NoiseLogic.WalkNoiseRadius,
            "推门比走路还轻——那开门就没有代价了。");
    }

    /// <summary>
    /// 🔴 <b>近战/弓是医院的正解，因为它们真的更安静</b>：全表**最响的近战**（战锤 150）
    /// 也还是比**最静的枪**（手枪 350）安静得多。否则"用近战摸过去"就只是句空话。
    /// </summary>
    [Fact]
    public void 最响的近战也比最静的枪安静得多()
    {
        double loudestMelee = WeaponTable.Warhammer().NoiseRadius;
        double quietestGun = WeaponTable.Pistol().NoiseRadius;

        Assert.True(loudestMelee < quietestGun,
            $"最响的近战（{loudestMelee}）比最静的枪（{quietestGun}）还吵——" +
            "那在医院里就没有理由收起枪，「大量丧尸」会立刻退化成「站着清完 14 只」。");
    }

    /// <summary>门全开时，它所在的门洞是真的通的（不许"开了门还是走不过去"）。</summary>
    [Fact]
    public void 门开着时门洞是通的()
    {
        IReadOnlyList<WallRect> walls = Walls();
        foreach (ExplorationDoor door in Doors())
        {
            float cx = door.Rect.X + door.Rect.Width / 2f;
            float cy = door.Rect.Y + door.Rect.Height / 2f;

            // 沿门的法线方向穿过去（门板窄边即法线）：门开着＝门板不在墙表里 ⇒ 这条短线段不该撞到任何墙段。
            bool horizontal = door.Rect.Width >= door.Rect.Height;
            (float ax, float ay, float bx, float by) = horizontal
                ? (cx, cy - 24f, cx, cy + 24f)   // 横门板：南北穿
                : (cx - 24f, cy, cx + 24f, cy);  // 竖门板：东西穿

            Assert.False(ExplorationWalls.SegmentHitsAnyWall(walls, ax, ay, bx, by),
                $"门「{door.Name}」开着，门洞却还是堵的。");
        }
    }
}
