using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 VisionLogic.cs / NightWatchContest.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载探索关**墙体几何**的唯一事实源：锁屋 / 房间轮廓的墙段矩形怎么排。
// 空间执行（StaticBody2D + RectangleShape2D 实体化、导航 obstruction 烘焙）归 TestExploration 运行时层，
// 本类只出纯矩形——同一批矩形同时充当碰撞体、导航障碍、墙层 0b0100 射线遮挡三用，故几何一错三处全错。

/// <summary>一段墙的轴对齐矩形（世界坐标，左上角 + 尺寸；对应 Godot 的 Rect2）。</summary>
public readonly struct WallRect
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }

    public WallRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>右边界（X + Width）。</summary>
    public float Right => X + Width;
    /// <summary>下边界（Y + Height）。</summary>
    public float Bottom => Y + Height;
}

/// <summary>房间占位轮廓的门洞所在边。</summary>
public enum RoomEdge
{
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>废弃医院的四个分区（南→北＝浅→深；越深越"洁净"、也越危险、医疗越集中）。</summary>
public enum HospitalZone
{
    /// <summary>门诊/急诊大厅（南·近）：非医疗为主。</summary>
    Lobby,

    /// <summary>住院部（中）。</summary>
    Ward,

    /// <summary>药房（深·医疗集中）。</summary>
    Pharmacy,

    /// <summary>手术层（最深·手术耗材+高价值医疗）。</summary>
    OperatingRoom,
}

/// <summary>废弃医院的一处搜刮点（id 与 <see cref="ExplorationCache"/> 一致；坐标/文案/分区在此，供关卡逐点铺设）。</summary>
public readonly record struct HospitalCacheSpot(string Id, float X, float Y, string Label, HospitalZone Zone);

/// <summary>
/// 一道墙上的门洞。<paramref name="DoorName"/> 非 null＝这个洞里装了一扇**可关的门**；null＝**永远敞着的洞**。
/// <para>
/// 🔴 每道边界都必须留至少一个 null —— 见 <see cref="ExplorationWalls.HospitalBoundaries"/> 的类注：
/// 关门是"隔开丧尸"的手段，不能变成"把自己反锁在里面"的手段。
/// </para>
/// </summary>
/// <param name="Center">门洞沿该墙轴向的中心（横墙＝X，竖墙＝Y）。</param>
public readonly record struct Doorway(float Center, string? DoorName);

/// <summary>一道分区边界（一整面墙）及其上的全部门洞。</summary>
public readonly record struct HospitalBoundary(string Name, IReadOnlyList<Doorway> Doorways);

/// <summary>外墙上的一处入口。<paramref name="DoorName"/> 非 null＝装了可关的门；null＝永远敞着。</summary>
public readonly record struct HospitalEntrance(string Name, string? DoorName);

/// <summary>
/// 探索关里一扇**可关的门**：门板矩形 + 初始状态。门板恰好填满它所在的那个门洞
/// （故"开门"＝把这块矩形从墙层/导航洞里摘掉，"关门"＝装回去，与营地门同一口径）。
/// </summary>
public readonly record struct ExplorationDoor(string Name, WallRect Rect, DoorState Initial);

/// <summary>
/// 探索关墙体几何（纯逻辑）。TestExploration 按这些矩形实体化墙：
/// 每段 → StaticBody2D(CollisionLayer=<c>VisionOcclusion.WallMask</c> 0b0100) + RectangleShape2D + 导航 obstruction outline。
/// 三件事一次到位：<b>挡路</b>（碰撞层）、<b>阻断寻路</b>（obstruction）、<b>挡视线</b>（VisionOcclusion 射线打的就是这一层）。
/// </summary>
public static class ExplorationWalls
{
    /// <summary>导航体半径（须与 TestExploration.BuildNavigation 的 NavigationPolygon.AgentRadius 一致）：门洞宽度的下限由它决定。</summary>
    public const float NavAgentRadius = 14f;

    // ── 锁屋（南林村庄，道格与布鲁斯被困处）─────────────────────────────────
    /// <summary>锁屋墙厚。</summary>
    public const float LockedHouseWallThickness = 16f;
    /// <summary>锁屋南墙门缺口的半宽（缺口总宽＝2×）。</summary>
    public const float LockedHouseDoorHalfWidth = 45f;

    // ── 房间占位轮廓（守林人小屋 / 南丁格尔药店 / 超市里屋）──────────────────
    /// <summary>房间轮廓墙厚（细墙，内嵌于房间矩形）。</summary>
    public const float RoomWallThickness = 8f;
    /// <summary>房间门洞宽。</summary>
    public const float RoomDoorWidth = 64f;

    /// <summary>
    /// 上锁的屋子：四面墙围合，南墙正中留一处门缺口（＝唯一通路，"被困"由此成立）。
    /// 四角一律由相邻墙段互相盖住——旧几何只补了东侧两角，西北/西南各漏一个 t×t 方孔，
    /// 纯视觉时看不出来，一旦实体化就是两个能让丧尸斜穿进屋的对角洞。
    /// </summary>
    public static IReadOnlyList<WallRect> LockedHouseWalls(
        float centerX,
        float centerY,
        float halfWidth,
        float halfHeight,
        float thickness = LockedHouseWallThickness,
        float doorHalfWidth = LockedHouseDoorHalfWidth)
    {
        float t = thickness;
        float left = centerX - halfWidth, right = centerX + halfWidth;
        float top = centerY - halfHeight, bottom = centerY + halfHeight;

        return new[]
        {
            // 北墙（横贯，向东多伸 t 盖住东北角）
            new WallRect(left, top - t, halfWidth * 2f + t, t),
            // 西墙（竖贯，向上下各多伸 t 盖住西北/西南角——旧几何在此漏角）
            new WallRect(left - t, top - t, t, halfHeight * 2f + t * 2f),
            // 东墙（竖贯，向南多伸 t 盖住东南角）
            new WallRect(right, top, t, halfHeight * 2f + t),
            // 南墙：中间留门缺口，拆左右两段
            new WallRect(left, bottom, (centerX - doorHalfWidth) - left, t),
            new WallRect(centerX + doorHalfWidth, bottom, right - (centerX + doorHalfWidth), t),
        };
    }

    /// <summary>
    /// 房间占位轮廓：四条细墙边内嵌于 <paramref name="room"/>，<paramref name="doorEdges"/> 上的每条边中段留门洞（拆两段）。
    /// <b>可多门边</b>：如南丁格尔小药店须 Bottom(临街外门) + Top(通后屋药房) 两处——只开一处的话，
    /// 后屋药房的南门会顶死小药店的实心北墙，实体化后玩家永远进不去后屋（既有布局的通行性缺陷）。
    /// 四条边各自贯通全长，四角天然由相邻边重叠盖住（不同于锁屋，无角洞问题）。
    /// </summary>
    public static IReadOnlyList<WallRect> RoomOutlineWalls(
        WallRect room,
        params RoomEdge[] doorEdges)
        => RoomOutlineWalls(room, RoomWallThickness, RoomDoorWidth, doorEdges);

    /// <inheritdoc cref="RoomOutlineWalls(WallRect, RoomEdge[])"/>
    public static IReadOnlyList<WallRect> RoomOutlineWalls(
        WallRect room,
        float thickness,
        float doorWidth,
        params RoomEdge[] doorEdges)
    {
        var doors = new HashSet<RoomEdge>(doorEdges ?? Array.Empty<RoomEdge>());
        var walls = new List<WallRect>(8);
        float t = thickness;

        AddEdge(walls, new WallRect(room.X, room.Y, room.Width, t), horizontal: true, doors.Contains(RoomEdge.Top), doorWidth);
        AddEdge(walls, new WallRect(room.X, room.Bottom - t, room.Width, t), horizontal: true, doors.Contains(RoomEdge.Bottom), doorWidth);
        AddEdge(walls, new WallRect(room.X, room.Y, t, room.Height), horizontal: false, doors.Contains(RoomEdge.Left), doorWidth);
        AddEdge(walls, new WallRect(room.Right - t, room.Y, t, room.Height), horizontal: false, doors.Contains(RoomEdge.Right), doorWidth);

        return walls;
    }

    /// <summary>一条边：无门＝整条；有门＝中段挖 <paramref name="doorWidth"/> 宽的洞，拆成两段。</summary>
    private static void AddEdge(List<WallRect> walls, WallRect edge, bool horizontal, bool withDoor, float doorWidth)
    {
        if (!withDoor)
        {
            walls.Add(edge);
            return;
        }

        if (horizontal)
        {
            float seg = (edge.Width - doorWidth) / 2f;
            if (seg <= 0f)
                return; // 门洞比墙还宽：整条边就是洞
            walls.Add(new WallRect(edge.X, edge.Y, seg, edge.Height));
            walls.Add(new WallRect(edge.X + seg + doorWidth, edge.Y, seg, edge.Height));
        }
        else
        {
            float seg = (edge.Height - doorWidth) / 2f;
            if (seg <= 0f)
                return;
            walls.Add(new WallRect(edge.X, edge.Y, edge.Width, seg));
            walls.Add(new WallRect(edge.X, edge.Y + seg + doorWidth, edge.Width, seg));
        }
    }

    // ── 废弃医院 [T49]：楼层平面 ────────────────────────────────────────────────
    //
    // 🔴 **这套几何存在的理由，是让"绕过去"成为可能。**
    //
    // 用户口径是「大量丧尸 + **中**危」。这两句话只有在**绕得过去**的前提下才同时成立：
    // docs/research/2026-07-14-combat-cost.md 已经证明**连场战斗不能拿胜率相乘去想**
    // （单场 68% 胜率、不治疗连打，能撑过第 3 个的只剩 3.5%、第 4 个 0.6%）。
    // 医院有 14 只丧尸 ⇒ 如果玩家**必须一只只清完**，它就不是"中危"，是**必死**。
    //
    // 改造前的医院是**一片开阔地**：没有一堵墙，14 只丧尸共享同一片视野，站在门口就能被全楼看见，
    // 既躲不掉也隔不开——那正是任务书警告的"死亡陷阱"。医院是**建筑**，就该有建筑该有的东西：
    //   · **多入口**（正门 / 急诊入口 / 员工侧门）——正门被堵死不等于这一趟白跑；员工侧门更是**跳过大厅直插深区**的捷径（更短，也更没有退路）。
    //   · **每道分区边界多个门洞**——一条走廊挤满丧尸时，还有第二条路。
    //   · **可关的门**（防火门/安全门/卷帘门）——医院天生就有这些东西。关上它＝把追你的丧尸挡在门后，
    //     它得绕到这道边界的**另一个门洞**去（很远），这就是你换来的时间。
    //
    // 墙同时挡视线（<see cref="VisionOcclusion"/> 对同一批矩形打射线）⇒ 分区之后，丧尸不再一次性全员发现你。
    // **噪音是这一关的主轴**：开枪（手枪 350 / 步枪 600）的半径足以横穿两三个分区 ⇒ 把整层楼叫醒。
    // 医院的正解是**别开枪**（近战/弓 70）、**关门**、**绕路**——而不是站着清 14 只。

    /// <summary>医院外墙厚。</summary>
    public const float HospitalWallThickness = 16f;

    /// <summary>医院分区隔墙厚（内墙，比外墙薄）。</summary>
    public const float HospitalPartitionThickness = 12f;

    /// <summary>医院门洞宽（须 &gt; 2×<see cref="NavAgentRadius"/>，否则寻路判定此路不通）。</summary>
    public const float HospitalDoorwayWidth = 72f;

    // 建筑外轮廓（关卡 2400×1600；南面留出返回区，故南墙压在 y=1400）。
    private const float HosLeft = 260f;
    private const float HosRight = 2220f;
    private const float HosTop = 100f;
    private const float HosBottom = 1416f;

    // 三道分区隔墙的 y（正落在四片分区地台之间的缝隙里）。
    private const float HosWallAY = 1046f; // 门诊/急诊大厅 ｜ 住院部
    private const float HosWallBY = 602f;  // 住院部 ｜ 药房
    private const float HosWallCY = 366f;  // 药房 ｜ 手术层

    /// <summary>大厅↔住院部 中央走廊上的防火门（关着＝把大厅的丧尸挡在南边）。</summary>
    public const string HospitalLobbyFireDoor = "大厅防火门";
    /// <summary>大厅↔住院部 西侧楼梯门。</summary>
    public const string HospitalWardStairDoor = "住院部楼梯门";
    /// <summary>住院部↔药房 的安全门（药房是医疗集中区，本就该有一道门）。</summary>
    public const string HospitalPharmacyDoor = "药房安全门";
    /// <summary>药房↔手术层 的防火门（手术层最深、收益最高，也最该被关在门后）。</summary>
    public const string HospitalOrFireDoor = "手术层防火门";
    /// <summary>急诊入口的卷帘门（第二个入口；关着＝挡住南面绕过来的丧尸）。</summary>
    public const string HospitalErShutterDoor = "急诊卷帘门";

    /// <summary>
    /// 医院的三道分区边界及其门洞。
    /// <para>
    /// 🔴 <b>硬不变量（已上单测）</b>：① 每道边界 ≥2 个门洞（一条路被堵死时还有第二条）；
    /// ② <b>每道边界上装了门的门洞数 &lt; 门洞总数</b> —— 任何一道边界都必须留一个**关不上的洞**。
    /// 否则"关门隔开丧尸"就会变成"把自己反锁在里面"：玩家关完门，自己也没有退路了。
    /// </para>
    /// </summary>
    public static IReadOnlyList<HospitalBoundary> HospitalBoundaries() => new[]
    {
        // 西侧楼梯(装门) / 中央走廊(装门) / 东侧楼梯(**永远敞着**——这是关完两扇门后仍存在的那条退路)
        new HospitalBoundary("门诊大厅｜住院部", new[]
        {
            new Doorway(480f, HospitalWardStairDoor),
            new Doorway(1180f, HospitalLobbyFireDoor),
            new Doorway(1900f, null),
        }),
        // 中央安全门(装门) / 东侧污物通道(敞着)
        new HospitalBoundary("住院部｜药房", new[]
        {
            new Doorway(1140f, HospitalPharmacyDoor),
            new Doorway(1930f, null),
        }),
        // 中央防火门(装门) / 西侧刷手通道(敞着)
        new HospitalBoundary("药房｜手术层", new[]
        {
            new Doorway(1240f, HospitalOrFireDoor),
            new Doorway(600f, null),
        }),
    };

    /// <summary>
    /// 医院外墙的入口（<b>≥2 个</b>：正门被丧尸堵死，不等于这一趟白跑）。
    /// 正门与员工侧门**永远敞着**（回家的路不能被自己关死）；急诊卷帘门可关（挡住南面绕过来的丧尸）。
    /// </summary>
    public static IReadOnlyList<HospitalEntrance> HospitalEntrances() => new[]
    {
        new HospitalEntrance("正门", null),                        // 南面正中，返回区正对着它
        new HospitalEntrance("急诊入口", HospitalErShutterDoor),   // 南面偏东（救护车通道）
        new HospitalEntrance("员工侧门", null),                    // 西面，**跳过大厅直插住院部**的捷径
    };

    /// <summary>
    /// 医院的全部墙段（外墙 + 三道分区隔墙；门洞处断开）。这批矩形三用：碰撞 / 导航 obstruction / 视线遮挡。
    /// <b>不含门板</b>——门板是 <see cref="HospitalDoors"/>，它随开关动态增删（开＝摘掉，关＝装回）。
    /// </summary>
    public static IReadOnlyList<WallRect> HospitalWalls()
    {
        const float t = HospitalWallThickness;
        const float p = HospitalPartitionThickness;
        const float w = HospitalDoorwayWidth;
        var walls = new List<WallRect>(16);

        // ── 外墙 ──
        // 北墙（实心，横贯；连带盖住左上/右上角）
        walls.Add(new WallRect(HosLeft, HosTop, HosRight - HosLeft, t));
        // 东墙（实心，竖贯）
        walls.Add(new WallRect(HosRight - t, HosTop, t, HosBottom - HosTop));
        // 西墙：员工侧门（y 中心 1000）处断开
        AddSplitEdge(walls, new WallRect(HosLeft, HosTop, t, HosBottom - HosTop), horizontal: false, new[] { 1000f }, w);
        // 南墙：正门（x 中心 1180）+ 急诊入口（x 中心 1850）两处断开
        AddSplitEdge(walls, new WallRect(HosLeft, HosBottom - t, HosRight - HosLeft, t), horizontal: true, new[] { 1180f, 1850f }, w);

        // ── 三道分区隔墙（内墙贴在外墙内表面之间；门洞中心取自 HospitalBoundaries）──
        foreach (HospitalBoundary b in HospitalBoundaries())
        {
            float y = b.Name switch
            {
                "门诊大厅｜住院部" => HosWallAY,
                "住院部｜药房" => HosWallBY,
                _ => HosWallCY,
            };
            var centers = new List<float>();
            foreach (Doorway d in b.Doorways)
                centers.Add(d.Center);

            AddSplitEdge(
                walls,
                new WallRect(HosLeft + t, y, (HosRight - t) - (HosLeft + t), p),
                horizontal: true,
                centers,
                w);
        }

        return walls;
    }

    /// <summary>
    /// 医院的全部**可关的门**（门板矩形恰好填满其门洞）。初始一律 <see cref="DoorState.Closed"/>——
    /// 医院的防火门本来就是关着的，而这恰好也是这一关成立的前提：**深区的丧尸一开始被关在门后**，
    /// 不会在你踏进大厅的那一刻全员向你涌来。推开一扇门＝100 半径的吱呀声（<c>NoiseLogic.DoorNoiseRadius</c>），
    /// 门后的东西听得见——但那也远比开一枪（350~600）便宜。
    /// <para>门**没有上锁**（<see cref="LockTier.None"/>）：不需要铁丝，玩家绝不会被一扇门永久卡死。</para>
    /// </summary>
    public static IReadOnlyList<ExplorationDoor> HospitalDoors()
    {
        const float t = HospitalWallThickness;
        const float p = HospitalPartitionThickness;
        const float w = HospitalDoorwayWidth;
        var doors = new List<ExplorationDoor>(5);

        // 外墙上的门：急诊卷帘门（南墙，x 中心 1850）
        doors.Add(new ExplorationDoor(
            HospitalErShutterDoor,
            new WallRect(1850f - w / 2f, HosBottom - t, w, t),
            DoorState.Closed));

        // 分区隔墙上的门
        foreach (HospitalBoundary b in HospitalBoundaries())
        {
            float y = b.Name switch
            {
                "门诊大厅｜住院部" => HosWallAY,
                "住院部｜药房" => HosWallBY,
                _ => HosWallCY,
            };
            foreach (Doorway d in b.Doorways)
            {
                if (d.DoorName is null)
                    continue; // 永远敞着的洞：没有门板
                doors.Add(new ExplorationDoor(
                    d.DoorName,
                    new WallRect(d.Center - w / 2f, y, w, p),
                    DoorState.Closed));
            }
        }

        return doors;
    }

    /// <summary>
    /// 一条边按若干门洞中心断开成多段（<paramref name="horizontal"/>＝沿 X 轴断，否则沿 Y 轴断）。
    /// 门洞两侧各留一段实墙；中心排序后逐段推进，故传入顺序无关。
    /// </summary>
    private static void AddSplitEdge(
        List<WallRect> walls, WallRect edge, bool horizontal, IReadOnlyList<float> doorCenters, float doorWidth)
    {
        var centers = new List<float>(doorCenters);
        centers.Sort();

        float cursor = horizontal ? edge.X : edge.Y;
        float end = horizontal ? edge.Right : edge.Bottom;

        foreach (float c in centers)
        {
            float gapStart = c - doorWidth / 2f;
            float gapEnd = c + doorWidth / 2f;
            if (gapStart > cursor)
            {
                walls.Add(horizontal
                    ? new WallRect(cursor, edge.Y, gapStart - cursor, edge.Height)
                    : new WallRect(edge.X, cursor, edge.Width, gapStart - cursor));
            }
            cursor = Math.Max(cursor, gapEnd);
        }

        if (end > cursor)
        {
            walls.Add(horizontal
                ? new WallRect(cursor, edge.Y, end - cursor, edge.Height)
                : new WallRect(edge.X, cursor, edge.Width, end - cursor));
        }
    }

    /// <summary>
    /// 医院 30 处搜刮点（id 与 <see cref="ExplorationCache"/> 一一对应）：坐标 + 中文标签 + 分区。
    /// 分区由南（近）到北（深）：门诊/急诊 7 → 住院部 8 → 药房 7（医疗集中）→ 手术层 8（高价值医疗）。
    /// <b>坐标与改造前逐字一致</b>（墙是加在它们之间的，一处点都没挪）。
    /// </summary>
    public static readonly IReadOnlyList<HospitalCacheSpot> HospitalCacheSpots = new[]
    {
        // 门诊/急诊大厅（南·近，7·非医疗为主）
        new HospitalCacheSpot(ExplorationCache.HospitalReceptionId, 700f, 1300f, "挂号台", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalTriageId, 900f, 1150f, "分诊台", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalWaitingRoomId, 1200f, 1250f, "候诊区", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalVendingId, 1500f, 1300f, "自动贩卖机", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalErTrolleyId, 1750f, 1150f, "急诊抢救推车", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalSecurityId, 400f, 1150f, "保安室", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalCafeteriaId, 2000f, 1250f, "食堂", HospitalZone.Lobby),

        // 住院部（中，8）
        new HospitalCacheSpot(ExplorationCache.HospitalWardLinenId, 600f, 900f, "病房布草间", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalWardLockerId, 900f, 850f, "病床储物柜", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalNurseStationId, 1200f, 900f, "护士站", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalDoctorOfficeId, 1600f, 850f, "医生办公室", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalDirtyUtilityId, 1900f, 950f, "污物处置间", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalKitchenetteId, 700f, 680f, "配餐间", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalFloorStoreId, 2050f, 700f, "楼层库房", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalMorgueId, 350f, 700f, "太平间", HospitalZone.Ward),

        // 药房（深，7·医疗集中——高价值）
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyCounterId, 700f, 520f, "药房前台", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyShelfId, 1000f, 470f, "处方药架", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyFridgeId, 1300f, 500f, "冷藏药柜", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyBackId, 1600f, 460f, "药库后间", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalNarcoticsCabinetId, 1900f, 520f, "管制药柜", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalDispensaryId, 500f, 420f, "配药室", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalMedSupplyRoomId, 2100f, 460f, "医材库", HospitalZone.Pharmacy),

        // 手术层（最深，8·手术耗材+高价值医疗）
        new HospitalCacheSpot(ExplorationCache.HospitalOrScrubId, 600f, 300f, "刷手准备间", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalOrTheatreId, 900f, 240f, "手术室", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalSterileStoreId, 1200f, 300f, "无菌耗材库", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalIcuId, 1500f, 240f, "ICU 重症监护", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalBloodBankId, 1800f, 300f, "血库", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalAnesthesiaId, 2050f, 240f, "麻醉科", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalSterilizerId, 350f, 280f, "器械灭菌室", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalChiefSafeId, 1250f, 150f, "主任药品保险柜", HospitalZone.OperatingRoom),
    };

    /// <summary>
    /// 医院游荡丧尸布点（14 只，band 12~16 —— 全图最高密度，"大量丧尸"是它的身份）。
    /// 向住院部/药房/手术层深区扎堆：**收益越高的分区，丧尸越密**。
    /// <para>
    /// 🔴 这 14 只**不是让你清完的**（连场战斗的代价见 <c>docs/research/2026-07-14-combat-cost.md</c>）。
    /// 它们是让你**选择不打**的：关门、绕路、别开枪。数量/布点拟定待调。
    /// </para>
    /// <b>坐标与改造前逐字一致</b>（墙是加在它们之间的，一只都没挪）。
    /// </summary>
    public static readonly IReadOnlyList<(float X, float Y)> HospitalZombieSpots = new[]
    {
        // 门诊/急诊大厅（南·近，2·稀——进门不会当场被淹）
        (700f, 1150f), (1500f, 1200f),
        // 住院部（中，4）
        (600f, 850f), (1100f, 780f), (1600f, 900f), (900f, 650f),
        // 药房（北·深，4·扎堆守医疗）
        (1200f, 450f), (700f, 400f), (1600f, 420f), (2000f, 500f),
        // 手术层（最北·最深，4·扎堆守高价值医疗）
        (1000f, 220f), (1400f, 200f), (1800f, 240f), (500f, 260f),
    };

    /// <summary>
    /// 线段是否与任一墙段相交（含线段整段落在墙内）。这是 <see cref="VisionOcclusion.IsOccluded"/> 的**离线镜像**——
    /// 二者打的是同一批矩形，故本判定可在无物理世界（单测/headless）时证明"这堵墙确实挡住了这条视线/这条路"。
    /// </summary>
    public static bool SegmentHitsAnyWall(IEnumerable<WallRect> walls, float ax, float ay, float bx, float by)
    {
        if (walls is null)
            return false;

        foreach (WallRect w in walls)
        {
            if (SegmentHitsRect(w, ax, ay, bx, by))
                return true;
        }
        return false;
    }

    /// <summary>线段 vs 轴对齐矩形（slab 法）。端点在矩形内也算命中。</summary>
    public static bool SegmentHitsRect(WallRect rect, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float tMin = 0f, tMax = 1f;

        if (!ClipSlab(ax, dx, rect.X, rect.Right, ref tMin, ref tMax))
            return false;
        if (!ClipSlab(ay, dy, rect.Y, rect.Bottom, ref tMin, ref tMax))
            return false;

        return tMin <= tMax;
    }

    /// <summary>单轴 slab 裁剪：把线段参数区间 [tMin,tMax] 收窄到该轴落在 [lo,hi] 内的部分；空区间＝不相交。</summary>
    private static bool ClipSlab(float origin, float delta, float lo, float hi, ref float tMin, ref float tMax)
    {
        const float Epsilon = 1e-6f;

        if (Math.Abs(delta) < Epsilon)
            return origin >= lo && origin <= hi; // 该轴上平行：只要起点落在 slab 内即不排除

        float t1 = (lo - origin) / delta;
        float t2 = (hi - origin) / delta;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);
        return tMin <= tMax;
    }
    // ==================================================================================
    // [T61] 下水道的几何（纯静态表 —— 与医院同一范式，可脱 Godot 单测）
    // ==================================================================================

    /// <summary>通道宽（px）。**逼仄**是这一关的身份：窄到你没法绕开迎面撞上的东西（对照：医院大厅上千宽）。</summary>
    public const float SewerCorridorWidth = 140f;

    /// <summary>下水道墙厚（px）。</summary>
    public const float SewerWallThickness = 40f;

    /// <summary>
    /// 🔴 <b>硬不变量：通道内任何一个位置，能感知到你的丧尸 ≤ 1 只。</b>（已上单测 <c>SewerTests</c>。）
    /// <para>
    /// 依据 <c>sim-lanchester</c> 实测：<b>2 只围攻胜率 16.6%、3 只 0.8%</b> ⇒ <b>围攻是断崖</b>。
    /// 用户对本关的原话是「<b>基本没有危险</b>…只是**某几个拐角可能有一只丧尸**」——
    /// 「吓人」和「危险」是两件事，这条护栏就是把它们**焊开**的那颗螺丝。<b>谁想往这儿加丧尸，先让这条测试红给他看。</b>
    /// </para>
    /// </summary>
    public const int SewerMaxConcurrentZombies = 1;

    /// <summary>
    /// 判"这只丧尸能感知到这个位置"用的**保守**半径（px）。
    /// <para>
    /// 校准（<b>取真实上界的近 2 倍，故意留裕度</b>）：本关无固定光源 ⇒ 环境光 = <see cref="VisionLogic.IndoorsDarkAmbient"/>(0.10)
    /// ⇒ 丧尸视距 <see cref="VisionLogic.ConeFor"/>(0.10).Range ≈ <b>124</b>；玩家举火把时被
    /// <c>Actor.ExposedCone</c> 放大至多 ×(1+<see cref="VisionLogic.MaxExposureBonus"/>) ⇒ ≈ <b>211</b>；
    /// 嗅觉 <see cref="NoiseLogic.ZombieSmellRadius"/> = 70；脚步噪音 <see cref="NoiseLogic.WalkNoiseRadius"/> = 40。
    /// ⇒ 真实上界 ≈ 211，取 <b>400</b>。
    /// </para>
    /// ⚠️ <b>它管不了枪声</b>：战斗噪音（350~600、且 <see cref="NoiseKind.Combat"/> 不分阵营）能把整条下水道叫醒——
    /// 那是**玩家自己的选择**，几何护栏不该、也无法替他兜底。本不变量保证的是：**你不开枪，就不会被围。**
    /// </summary>
    public const float SewerAggroRadius = 400f;

    /// <summary>
    /// 下水道的**可行走通道**（8 段 + 最深处汇流室）。墙 = 这些矩形的**补集**（<see cref="SewerWalls"/> 自动推出）
    /// ⇒ 通道与墙**不可能对不上**（手写墙最容易出的 bug 就是留个洞或堵死一条路）。
    /// <para>
    /// 相邻段**必须真重叠**（不是边贴边）—— 重叠区就是那个"拐角"，也是 <see cref="SewerWalls"/> 用来在墙上开口子的依据。
    /// </para>
    /// <para>
    /// 走法：入口竖井(南) → 横廊一 → <b>弯</b> → 竖廊一 → <b>弯</b>（这里岔出一条**西死胡同**，尽头有货）
    /// → 横廊二 → <b>弯</b> → 竖廊二 → <b>弯</b> → 横廊三 → <b>弯</b> → 竖廊三 → <b>最深处汇流室（耗子在这儿）</b>。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<SewerCorridor> SewerCorridors = new[]
    {
        new SewerCorridor("入口竖井",   new WallRect(360f, 1180f, 140f, 220f)),
        new SewerCorridor("横廊一",     new WallRect(360f, 1120f, 700f, 140f)),
        new SewerCorridor("竖廊一",     new WallRect(920f,  720f, 140f, 540f)),
        new SewerCorridor("西死胡同",   new WallRect(360f,  720f, 700f, 140f)),  // 支线：**长**——绕进去就是一次真正的承诺（尽头有货，路上有一只）
        new SewerCorridor("横廊二",     new WallRect(920f,  720f, 710f, 140f)),
        new SewerCorridor("竖廊二",     new WallRect(1490f, 420f, 140f, 440f)),
        new SewerCorridor("横廊三",     new WallRect(790f,  420f, 840f, 140f)),
        new SewerCorridor("竖廊三",     new WallRect(790f,  220f, 140f, 340f)),
        new SewerCorridor("最深处汇流室", new WallRect(700f, 180f, 320f, 200f)), // 耗子
    };

    /// <summary>探索队进关的落点（入口竖井底），也是回营的返回区。</summary>
    public static readonly (float X, float Y) SewerEntry = (430f, 1360f);

    /// <summary>
    /// **最深处** —— 耗子站的地方（用户原话：「走到**最深处**，会看到一个…女人」）。
    /// 与 <c>RatRecruit.MeetDiscoveryId</c> 对应；<b>非物资点</b>，不计探索完成度。
    /// </summary>
    public static readonly (float X, float Y) SewerDeepestPoint = (860f, 270f);

    /// <summary>
    /// 下水道 5 处搜刮点（**很少量物资** —— 用户原话）。id 与 <c>ExplorationCache</c> 一一对应。
    /// 由近到深：入口 → 横廊 → <b>西死胡同尽头</b>（绕路的报酬）→ 泵房（拐角四的丧尸就守在它边上）→ 老鼠窝。
    /// </summary>
    public static readonly IReadOnlyList<SewerCacheSpot> SewerCacheSpots = new[]
    {
        new SewerCacheSpot(ExplorationCache.SewerEntryDebrisId,  430f, 1300f, "检修梯下的杂物"),
        new SewerCacheSpot(ExplorationCache.SewerDriftPileId,    700f, 1190f, "水线上的漂浮杂物堆"),
        new SewerCacheSpot(ExplorationCache.SewerDeadEndLockerId, 400f,  790f, "死胡同尽头的锈铁柜"),
        new SewerCacheSpot(ExplorationCache.SewerPumpRoomId,     1560f,  800f, "泵房检修箱"),
        new SewerCacheSpot(ExplorationCache.SewerRatNestId,      1200f,  490f, "老鼠窝"),
    };

    /// <summary>
    /// 下水道游荡丧尸布点 —— <b>3 只，各蹲一个拐角</b>（用户原话：「除了**某几个拐角**可能有**一只**丧尸」）。
    /// <para>
    /// 拐角二（近）/ 西死胡同深处（支线）/ 拐角四（中）。三只**互相看不见**，也**互相走不到一块儿去**（除非你开枪）。
    /// </para>
    /// <para>
    /// 🔴 <b>入口一只都没有</b>（进门不会当场挨打，同消防站口径）；<b>最深处（耗子）一只都没有</b> ——
    /// 她在那底下活了很久，门口不该蹲着一只；而且**从她站的地方，一只都看不见**。
    /// </para>
    /// <para>
    /// 🔴 <b>挪动/新增任何一只之前，先跑 SewerTests</b> —— 挪 100px 就可能让某个拐角同时暴露两只，而两只 = 胜率 16.6%（断崖）。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<(float X, float Y)> SewerZombieSpots = new[]
    {
        (990f, 1200f),  // 拐角二：横廊一 → 竖廊一 的弯（你刚拐过去就撞脸）
        (480f, 790f),   // 西死胡同深处：**绕路的报酬是有人守着的** —— 锈铁柜就在它背后
        (1560f, 790f),  // 拐角四：横廊二 → 竖廊二 的弯（守着泵房那处货）
    };

    // 🔴 **这三个坐标是"解"出来的，不是摆出来的** —— 别凭手感挪。
    //
    // 定这三个点时，先后有 **4 组看起来很合理的布点被上面那条不变量当场打回**：
    //   · 「进最深处前的最后一个弯」放一只 ⇒ 它离耗子只有 <b>220px</b>，且中间是直筒 ⇒ **她等于和它同处一室**。
    //   · 竖廊二 + 横廊三 各放一只 ⇒ 站在 S6/S7 那个外拐角上**两只同时进视野**（两条走廊在那儿交汇，都不到 400）。
    //   · 西死胡同放浅了（x≥500）⇒ 站在 S3/S4 的丁字口，**一只在左、一只在上，同屏**。
    // ⇒ 结论：**在这么小的图上，400px 的保守半径只容得下这一种三只布法。**
    //   西死胡同因此被**加长到 x=360**（原 500）—— 不是为了好看，是为了把那只丧尸推到"从丁字口看不到"的深处。
    //   **要挪任何一只、或者想加第四只，先跑 SewerTests，它会告诉你行不行。**

    /// <summary>某点是否在下水道的可行走区域内（＝落在任一通道矩形里）。</summary>
    public static bool SewerContains(float x, float y)
    {
        foreach (SewerCorridor c in SewerCorridors)
        {
            WallRect r = c.Rect;
            if (x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 下水道的全部墙段 —— **由通道的补集自动推出**，不手写。
    /// <para>
    /// 做法：给每段通道的四条边各贴一条**朝外**的墙条，再把**被其它通道压住**的区间挖掉
    /// （那些区间就是"拐角"／junction，必须是通的）。⇒ 通道与墙**天然对得上**：
    /// 既不会漏个洞（墙条覆盖了每一条边），也不会把路堵死（junction 一定被挖开）。
    /// </para>
    /// 这批矩形三用：碰撞 / 导航 obstruction / <b>视线遮挡</b>（<see cref="VisionOcclusion"/> 打的就是这一层）——
    /// "拐角看不见前面有什么"就是靠它们成立的。
    /// </summary>
    public static IReadOnlyList<WallRect> SewerWalls()
    {
        const float t = SewerWallThickness;
        var corridors = SewerCorridors.Select(c => c.Rect).ToList();
        var walls = new List<WallRect>(48);

        foreach (WallRect r in corridors)
        {
            // 四条朝外的墙条（角上留出 t，使相邻墙条在外角处接得住）
            AddSewerStrip(walls, new WallRect(r.X - t, r.Y - t, r.Width + 2f * t, t), corridors, horizontal: true);   // 上
            AddSewerStrip(walls, new WallRect(r.X - t, r.Bottom, r.Width + 2f * t, t), corridors, horizontal: true);  // 下
            AddSewerStrip(walls, new WallRect(r.X - t, r.Y, t, r.Height), corridors, horizontal: false);              // 左
            AddSewerStrip(walls, new WallRect(r.Right, r.Y, t, r.Height), corridors, horizontal: false);              // 右
        }
        return walls;
    }

    /// <summary>
    /// 贴一条墙条，但把**与任一通道相交**的区间挖成口子（那就是拐角/junction）。
    /// <paramref name="horizontal"/>=true ⇒ 沿 X 轴切；否则沿 Y 轴切。
    /// </summary>
    private static void AddSewerStrip(
        List<WallRect> walls, WallRect strip, IReadOnlyList<WallRect> corridors, bool horizontal)
    {
        const float eps = 0.01f;

        // 收集要挖掉的区间（该墙条与某通道**真相交**的那一段）
        var gaps = new List<(float Lo, float Hi)>();
        foreach (WallRect c in corridors)
        {
            bool overlaps = c.X < strip.Right - eps && c.Right > strip.X + eps
                         && c.Y < strip.Bottom - eps && c.Bottom > strip.Y + eps;
            if (!overlaps)
            {
                continue;
            }
            gaps.Add(horizontal
                ? (Math.Max(strip.X, c.X), Math.Min(strip.Right, c.Right))
                : (Math.Max(strip.Y, c.Y), Math.Min(strip.Bottom, c.Bottom)));
        }

        float start = horizontal ? strip.X : strip.Y;
        float end = horizontal ? strip.Right : strip.Bottom;

        gaps.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        float cursor = start;
        foreach ((float lo, float hi) in gaps)
        {
            if (lo > cursor + eps)
            {
                walls.Add(horizontal
                    ? new WallRect(cursor, strip.Y, lo - cursor, strip.Height)
                    : new WallRect(strip.X, cursor, strip.Width, lo - cursor));
            }
            cursor = Math.Max(cursor, hi);
        }
        if (end > cursor + eps)
        {
            walls.Add(horizontal
                ? new WallRect(cursor, strip.Y, end - cursor, strip.Height)
                : new WallRect(strip.X, cursor, strip.Width, end - cursor));
        }
    }

}

// ======================================================================================
// [T61] 下水道 —— 前中期 · 规模小 · 低危。**恐怖靠环境，不靠战力。**
//
// 🔴 用户原话（authored 事实源）：「规模小，下水道，**除了某几个拐角可能有一只丧尸，基本没有危险**，
//    主要靠**黑暗逼仄的环境**和**大量拐角的差视野**，配合滴滴答答的水滴声和脚步声和回声吓人。」
//
// ⇒ 三条设计约束，全部下沉成**可单测的几何不变量**（见 SewerTests）：
//   ①【逼仄】通道宽 SewerCorridorWidth = 140px（对照：医院大厅动辄上千宽）。窄到你没法绕开迎面的东西。
//   ②【大量拐角】主路是一条**蛇形折线**，8 个直角弯 + 1 条死胡同支线 —— **拐角 = 你看不见前面有什么**。
//   ③【基本没有危险】丧尸**只有 3 只、且各自蹲在一个拐角**，并由硬不变量钉死：
//      **通道内任何一个位置，"能感知到你"的丧尸 ≤ 1 只**（SewerMaxConcurrentZombies）。
//      ⚠️ 依据 sim-lanchester 实测：**2 只围攻胜率 16.6%、3 只 0.8%** ⇒ 围攻是断崖。
//      用户要的是"**吓人但不危险**" ⇒ 丧尸必须**单只、且分散到互相看不见**。这条不是风味，是硬护栏。
//
// 📌【黑暗】本关不铺任何固定光源（LightField 为空）⇒ 环境光走 VisionLogic.IndoorsDarkAmbient(0.10)，
//    视锥当场缩到 ~124px / 半角 30°（VisionLogic.ConeFor(0.10)）—— **玩家基本上必须手持光源**（HeldLightState，占一只手
//    ⇒ 与双手武器互斥 ⇒ "要么看得见，要么打得动"）。而举着光的人，也正是丧尸最先看见的人（Actor.ExposedCone）。
//
// 📌【音效】用户要的"滴滴答答的水滴声/脚步声/回声"**没有承载它的系统** —— 本项目至今**没有任何音效系统**
//    （无 AudioStreamPlayer / 无音频资源 / 无 audio bus）。**不在此处伪造**，已作为重大缺口上报（见 journal [T61]）。
// ======================================================================================

/// <summary>下水道的一段**可行走通道**（矩形）。墙由 <see cref="ExplorationWalls.SewerWalls"/> 从通道的**补集**推出。</summary>
public readonly record struct SewerCorridor(string Name, WallRect Rect);

/// <summary>下水道的一处搜刮点（id 与 <c>ExplorationCache</c> 一一对应）。</summary>
public readonly record struct SewerCacheSpot(string Id, float X, float Y, string Label);
