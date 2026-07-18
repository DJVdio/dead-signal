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
/// 探索关里一扇**可关的门**：门板矩形 + 初始状态 + <b>锁的档次</b>。门板恰好填满它所在的那个门洞
/// （故"开门"＝把这块矩形从墙层/导航洞里摘掉，"关门"＝装回去，与营地门同一口径）。
/// <para>
/// <paramref name="Lock"/> 默认 <see cref="LockTier.None"/>（绝大多数关内门不上锁，玩家推一下就开）——
/// 仅当 <paramref name="Initial"/> 为 <see cref="DoorState.Locked"/> 时才有意义：撬它要**铁丝**，档次决定成功率/耗时
/// （见 <see cref="DoorLogic.PickChance"/>/<see cref="DoorLogic.PickSeconds"/>）。警察局禁闭区那道门是全项目**第一扇**真锁门。
/// </para>
/// </summary>
public readonly record struct ExplorationDoor(string Name, WallRect Rect, DoorState Initial, LockTier Lock = LockTier.None);

/// <summary>
/// 敌营里一格不可自由摆放的围栏：空间层只消费矩形，档位沿用 <see cref="CampStructureTable"/>
/// 的可破坏结构表，避免为探索关另造一套 HP/等级。
/// </summary>
public readonly record struct ExplorationFence(string Name, WallRect Rect, StructureTier Tier);

/// <summary>
/// 探索关墙体几何（纯逻辑）。TestExploration 按这些矩形实体化墙：
/// 每段 → StaticBody2D(CollisionLayer=<c>VisionOcclusion.WallMask</c> 0b0100) + RectangleShape2D + 导航 obstruction outline。
/// 三件事一次到位：<b>挡路</b>（碰撞层）、<b>阻断寻路</b>（obstruction）、<b>挡视线</b>（VisionOcclusion 射线打的就是这一层）。
/// </summary>
public static class ExplorationWalls
{
    /// <summary>导航体半径（须与 TestExploration.BuildNavigation 的 NavigationPolygon.AgentRadius 一致）：门洞宽度的下限由它决定。</summary>
    public const float NavAgentRadius = 14f;

    // ── 金手指帮根据地：围栏 + 锁门（[TODO 20 A②/B④]）─────────────────────
    // 这是敌营的 authored 外围，不是玩家可自由建墙的系统：空间层把它实体化为静态障碍，
    // 门则复用 ExplorationDoor → DoorLogic → 右键前往/撬锁/开门激活链。
    // 围栏档位沿用 CampStructure，给后续破坏/损坏消费层留下唯一等级入口。
    public const string GoldfingerGateName = "金手指帮寨门";
    public const float GoldfingerGateCenterX = 1200f;
    public const float GoldfingerFenceTopY = 120f;
    public const float GoldfingerFenceBottomY = 1230f;
    public const float GoldfingerFenceLeftX = 420f;
    public const float GoldfingerFenceRightX = 2180f;
    public const float GoldfingerFenceThickness = 24f;
    public const float GoldfingerGateWidth = 120f;

    /// <summary>
    /// 金手指帮外围五段基础围栏。南侧中段留给 <see cref="GoldfingerDoors"/> 的寨门，
    /// 其余三边和南侧两翼均是完整屏障；围栏不能被玩家建造，只能按现有结构规则破坏。
    /// </summary>
    public static IReadOnlyList<ExplorationFence> GoldfingerFences()
    {
        const float t = GoldfingerFenceThickness;
        float southLeftWidth = GoldfingerGateCenterX - GoldfingerGateWidth / 2f - GoldfingerFenceLeftX;
        float southRightX = GoldfingerGateCenterX + GoldfingerGateWidth / 2f;
        float southRightWidth = GoldfingerFenceRightX - southRightX;
        return new[]
        {
            new ExplorationFence("金手指帮西围栏",
                new WallRect(GoldfingerFenceLeftX, GoldfingerFenceTopY, t,
                    GoldfingerFenceBottomY - GoldfingerFenceTopY), StructureTier.FenceBasic),
            new ExplorationFence("金手指帮北围栏",
                new WallRect(GoldfingerFenceLeftX, GoldfingerFenceTopY,
                    GoldfingerFenceRightX - GoldfingerFenceLeftX, t), StructureTier.FenceBasic),
            new ExplorationFence("金手指帮东围栏",
                new WallRect(GoldfingerFenceRightX - t, GoldfingerFenceTopY, t,
                    GoldfingerFenceBottomY - GoldfingerFenceTopY), StructureTier.FenceBasic),
            new ExplorationFence("金手指帮南围栏·西段",
                new WallRect(GoldfingerFenceLeftX, GoldfingerFenceBottomY,
                    southLeftWidth, t), StructureTier.FenceBasic),
            new ExplorationFence("金手指帮南围栏·东段",
                new WallRect(southRightX, GoldfingerFenceBottomY,
                    southRightWidth, t), StructureTier.FenceBasic),
        };
    }

    /// <summary>
    /// 金手指帮唯一入口：初始锁住的普通锁门。撬开才进入深处军械/头目区；
    /// 门板恰好填满南侧围栏的门洞，且由消费层统一负责挡路、挡视线、断寻路。
    /// </summary>
    public static IReadOnlyList<ExplorationDoor> GoldfingerDoors()
    {
        const float t = GoldfingerFenceThickness;
        return new[]
        {
            new ExplorationDoor(
                GoldfingerGateName,
                new WallRect(
                    GoldfingerGateCenterX - GoldfingerGateWidth / 2f,
                    GoldfingerFenceBottomY,
                    GoldfingerGateWidth,
                    t),
                DoorState.Locked,
                LockTier.Standard),
        };
    }

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

    // 建筑外轮廓（关卡 4200×2800——[大图放大] 由 2400×1600 均匀 1.75× 得来；南面留出返回区，故南墙压在 y≈2478）。
    // 🔴 全部医院几何常量/坐标一律 ×1.75（4200/2400 == 2800/1600 == 1.75），保 authored 楼层布局比例不变；
    // 门/墙厚与门洞宽刻意不缩放（现实里楼更大门不必更宽；门洞 72 仍 > 2×NavAgentRadius）。数值拟定待调。
    private const float HosLeft = 455f;    // 260 × 1.75
    private const float HosRight = 3885f;  // 2220 × 1.75
    private const float HosTop = 175f;     // 100 × 1.75
    private const float HosBottom = 2478f; // 1416 × 1.75

    // 三道分区隔墙的 y（正落在四片分区地台之间的缝隙里；均 ×1.75）。
    private const float HosWallAY = 1830.5f; // 门诊/急诊大厅 ｜ 住院部（1046 × 1.75）
    private const float HosWallBY = 1053.5f; // 住院部 ｜ 药房（602 × 1.75）
    private const float HosWallCY = 640.5f;  // 药房 ｜ 手术层（366 × 1.75）

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
        // 西侧楼梯(装门) / 中央走廊(装门) / 东侧楼梯(**永远敞着**——这是关完两扇门后仍存在的那条退路)。门洞中心均 ×1.75。
        new HospitalBoundary("门诊大厅｜住院部", new[]
        {
            new Doorway(840f, HospitalWardStairDoor),   // 480 × 1.75
            new Doorway(2065f, HospitalLobbyFireDoor),  // 1180 × 1.75
            new Doorway(3325f, null),                   // 1900 × 1.75
        }),
        // 中央安全门(装门) / 东侧污物通道(敞着)
        new HospitalBoundary("住院部｜药房", new[]
        {
            new Doorway(1995f, HospitalPharmacyDoor),   // 1140 × 1.75
            new Doorway(3377.5f, null),                 // 1930 × 1.75
        }),
        // 中央防火门(装门) / 西侧刷手通道(敞着)
        new HospitalBoundary("药房｜手术层", new[]
        {
            new Doorway(2170f, HospitalOrFireDoor),     // 1240 × 1.75
            new Doorway(1050f, null),                   // 600 × 1.75
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
        // 西墙：员工侧门（y 中心 1750 = 1000 × 1.75）处断开
        AddSplitEdge(walls, new WallRect(HosLeft, HosTop, t, HosBottom - HosTop), horizontal: false, new[] { 1750f }, w);
        // 南墙：正门（x 中心 2065 = 1180 × 1.75）+ 急诊入口（x 中心 3237.5 = 1850 × 1.75）两处断开
        AddSplitEdge(walls, new WallRect(HosLeft, HosBottom - t, HosRight - HosLeft, t), horizontal: true, new[] { 2065f, 3237.5f }, w);

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

        // ── [大图放大] 分区内部的绕行短墙（wing stubs）──
        // 放大后每片分区变成一块大空地，直线穿行等于免费；这几段**悬浮**短墙（两端都留通道、都不装门、
        // 都不跨满边界）把"走直线"变成"绕 S 形"，也把长视线打断——地形复杂度的主要来源。
        // 它们只**增加**墙覆盖：既不改三道 authored 分区边界（HospitalBoundaries 不变 ⇒ 门洞不变量原样成立），
        // 也不会封死任何分区（每段两端各留 ≥150px 通道）。坐标经离线校验不压任何搜刮点/丧尸点/叙事点（拟定待调）。
        foreach (WallRect stub in HospitalDetourStubs)
            walls.Add(stub);

        return walls;
    }

    /// <summary>
    /// [大图放大] 医院分区内部的绕行短墙（见 <see cref="HospitalWalls"/> 尾部说明）。
    /// 全为**悬浮**竖墙段：两端留通道、不装门、不跨满分区边界 ⇒ 只制造绕路/挡视线，绝不封死分区。
    /// </summary>
    private static readonly IReadOnlyList<WallRect> HospitalDetourStubs = new[]
    {
        // 门诊/急诊大厅（最大）：两道错位竖隔 → S 型
        new WallRect(1350f, 1980.5f, 12f, 480f),
        new WallRect(2950f, 1870.5f, 12f, 470f),
        // 住院部：两道错位竖隔（病房走廊迷宫）
        new WallRect(1350f, 1183.5f, 12f, 460f),
        new WallRect(2950f, 1093.5f, 12f, 480f),
        // 药房：一道货架竖隔
        new WallRect(1500f, 760.5f, 12f, 200f),
        // 手术层：一道无菌区竖隔
        new WallRect(1350f, 265f, 12f, 260f),
    };

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

        // 外墙上的门：急诊卷帘门（南墙，x 中心 3237.5 = 1850 × 1.75）
        doors.Add(new ExplorationDoor(
            HospitalErShutterDoor,
            new WallRect(3237.5f - w / 2f, HosBottom - t, w, t),
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
    /// 医院 44 处搜刮点（id 与 <see cref="ExplorationCache"/> 一一对应）：坐标 + 中文标签 + 分区。
    /// 分区由南（近）到北（深）：门诊/急诊 10 → 住院部 12 → 药房 10（医疗集中）→ 手术层 12（高价值医疗）。
    /// <para>
    /// <b>[大图放大]</b> 原 30 点坐标一律 ×1.75（保 authored 相对布局），另在放大后的分区空档补 14 点
    /// （医疗集中投放的身份保持：新点也把药品/手术耗材压在药房/手术层）——把搜刮工作量抬向 ≈5 天量级。
    /// 坐标经离线校验不落墙内（<c>HospitalLayoutTests</c> 钉死）。掉落/坐标数值拟定待调。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<HospitalCacheSpot> HospitalCacheSpots = new[]
    {
        // 门诊/急诊大厅（南·近，10·非医疗为主）
        new HospitalCacheSpot(ExplorationCache.HospitalReceptionId, 1225f, 2275f, "挂号台", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalTriageId, 1575f, 2012.5f, "分诊台", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalWaitingRoomId, 2100f, 2187.5f, "候诊区", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalVendingId, 2625f, 2275f, "自动贩卖机", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalErTrolleyId, 3062.5f, 2012.5f, "急诊抢救推车", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalSecurityId, 700f, 2012.5f, "保安室", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalCafeteriaId, 3500f, 2187.5f, "食堂", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalGiftShopId, 1150f, 2050f, "便民商店", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalRadiologyId, 3550f, 2050f, "放射科候诊", HospitalZone.Lobby),
        new HospitalCacheSpot(ExplorationCache.HospitalAmbulanceBayId, 2900f, 2350f, "救护车停车区", HospitalZone.Lobby),

        // 住院部（中，12）
        new HospitalCacheSpot(ExplorationCache.HospitalWardLinenId, 1050f, 1575f, "病房布草间", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalWardLockerId, 1575f, 1487.5f, "病床储物柜", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalNurseStationId, 2100f, 1575f, "护士站", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalDoctorOfficeId, 2800f, 1487.5f, "医生办公室", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalDirtyUtilityId, 3325f, 1662.5f, "污物处置间", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalKitchenetteId, 1225f, 1190f, "配餐间", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalFloorStoreId, 3587.5f, 1225f, "楼层库房", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalMorgueId, 612.5f, 1225f, "太平间", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalPhysiotherapyId, 800f, 1550f, "康复理疗室", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalRecordsId, 3600f, 1500f, "病案室", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalStaffLoungeId, 1450f, 1650f, "医护休息室", HospitalZone.Ward),
        new HospitalCacheSpot(ExplorationCache.HospitalIsolationWardId, 3050f, 1300f, "隔离病房", HospitalZone.Ward),

        // 药房（深，10·医疗集中——高价值）
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyCounterId, 1225f, 910f, "药房前台", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyShelfId, 1750f, 822.5f, "处方药架", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyFridgeId, 2275f, 875f, "冷藏药柜", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalPharmacyBackId, 2800f, 805f, "药库后间", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalNarcoticsCabinetId, 3325f, 910f, "管制药柜", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalDispensaryId, 875f, 735f, "配药室", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalMedSupplyRoomId, 3675f, 805f, "医材库", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalCompoundingLabId, 850f, 950f, "配置室", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalIvPrepId, 3550f, 950f, "静配中心", HospitalZone.Pharmacy),
        new HospitalCacheSpot(ExplorationCache.HospitalVaccineFridgeId, 2400f, 830f, "疫苗冷库", HospitalZone.Pharmacy),

        // 手术层（最深，12·手术耗材+高价值医疗）
        new HospitalCacheSpot(ExplorationCache.HospitalOrScrubId, 1050f, 525f, "刷手准备间", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalOrTheatreId, 1575f, 420f, "手术室", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalSterileStoreId, 2100f, 525f, "无菌耗材库", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalIcuId, 2625f, 420f, "ICU 重症监护", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalBloodBankId, 3150f, 525f, "血库", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalAnesthesiaId, 3587.5f, 420f, "麻醉科", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalSterilizerId, 612.5f, 490f, "器械灭菌室", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalChiefSafeId, 2187.5f, 262.5f, "主任药品保险柜", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalRecoveryRoomId, 700f, 560f, "术后恢复室", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalPathologyLabId, 2900f, 430f, "病理科", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalOnCallRoomId, 3650f, 560f, "值班室", HospitalZone.OperatingRoom),
        new HospitalCacheSpot(ExplorationCache.HospitalCentralSupplyId, 1900f, 560f, "中心供应室", HospitalZone.OperatingRoom),
    };

    /// <summary>
    /// 医院游荡丧尸布点（14 只，band 12~16 —— 全图最高密度，"大量丧尸"是它的身份）。
    /// 向住院部/药房/手术层深区扎堆：**收益越高的分区，丧尸越密**。
    /// <para>
    /// 🔴 这 14 只**不是让你清完的**（连场战斗的代价见 <c>docs/research/2026-07-14-combat-cost.md</c>）。
    /// 它们是让你**选择不打**的：关门、绕路、别开枪。数量/布点拟定待调。
    /// </para>
    /// <b>[大图放大]</b> 原 14 只坐标一律 ×1.75；放大后画布是 3 倍面积，为守住「大量丧尸」身份补 4 只到深区
    /// （共 18 只）——密度反而略降、更「能绕」，仍是**让你选择不打**的 14~18 只（数量/布点拟定待调）。
    /// </summary>
    public static readonly IReadOnlyList<(float X, float Y)> HospitalZombieSpots = new[]
    {
        // 门诊/急诊大厅（南·近，2·稀——进门不会当场被淹）
        (1225f, 2012.5f), (2625f, 2100f),
        // 住院部（中，4）
        (1050f, 1487.5f), (1925f, 1365f), (2800f, 1575f), (1575f, 1137.5f),
        // 药房（北·深，4·扎堆守医疗）
        (2100f, 787.5f), (1225f, 700f), (2800f, 735f), (3500f, 875f),
        // 手术层（最北·最深，4·扎堆守高价值医疗）
        (1750f, 385f), (2450f, 350f), (3150f, 420f), (875f, 455f),
        // [大图放大] 补 4 只到放大后的深区东翼/新点周边（守新增医疗点，皆可绕）
        (3400f, 2100f), (3300f, 1400f), (3000f, 950f), (3400f, 560f),
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
    /// 依据 <c>docs/research/2026-07-14-lanchester.md</c>（Sim <c>lanchester</c> 模式·机器生成·<b>2026-07-17 全仓重跑值</b>·
    /// 中甲皮夹克+长袖布衣·长剑）：
    /// 1 只 <b>100%</b> → 2 只 <b>84.5%</b> → 3 只 <b>22.0%</b> → 4 只 1.6% → 5 只 0% ⇒ <b>围攻是断崖</b>，拐点在**第 3 只**。
    /// </para>
    /// <para>
    /// ⚠️ <b>两组作废数，谁都别再捡回来</b>：① "2 只 16.6% / 3 只 0.8%" 出自 [T53] 已被用户否决并回退掉的
    /// <b>70/1.5 热流血口径</b>（<c>BleedModel</c> [T53 二次拍板] 注释自陈「16.6% 不是实机的数」，现值
    /// <c>BleedModel.DefaultBleedRatePerWound</c> = 0.55 ⇒ 现行代码复现不出来）；② "2 只 84.4% / 3 只 24.1% / 1.05 处"
    /// 出自 <c>991b777</c> 那版 <b>born-stale 报告</b>（该 commit 手改报告一行冷却值伪装成新、表格没重跑；其自身代码实跑
    /// 就是 84.5/22.0 且与 HEAD 输出 MD5 逐字节相同 ⇒ 出生即错），已由 2026-07-17 全仓重跑取代。
    /// 与 <c>SewerTests</c> 的作废说明对齐——**源注释与测试同口径，才不会有人再把旧数捡回来**。
    /// </para>
    /// <para>
    /// 🔴 <b>数字两次翻修，这条护栏的理由没变</b>——**胜率不是成本**（CLAUDE.md 通则）：84.5% 的「2 只」不是白捡，
    /// 打赢时平均仍留下 <b>1.14 处永久残缺 / 30% 惨胜率 / 20% 失血</b>，且那 15.5% 的败率里 <b>12.8% 是昏迷倒地</b>
    /// ——而 lanchester.md 自己写着「在一群丧尸中间昏过去 ＝ 被吃掉」。只有 1v1 才是 100%／惨胜 1%／残缺 0.02 处。
    /// ⇒ <b>「≤1」比断崖点（第 3 只）还保守一格＝刻意留的裕度，不是可以回收的冗余。</b>
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
    /// 🔴 <b>挪动/新增任何一只之前，先跑 SewerTests</b> —— 挪 100px 就可能让某个拐角同时暴露两只：
    /// 两只是 84.5%（2026-07-17 全仓重跑值），**不是死局，但要付 1.14 处永久残缺／30% 惨胜**，而 1v1 几乎无代价 ⇒ 别拿这关的"低危"去换。
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

    // ==================================================================================
    // [警察局] 警察局的几何（纯静态表 —— 与下水道同一范式：可行走房间/走廊的**补集**自动推出墙，可脱 Godot 单测）
    //
    // 🔴 用户拍板：警察局＝**前中期 · 规模小 · 室内多拐角 · 危险 Medium**，前置挂消防站/河边小屋之后。
    //   「室内多拐角」在这里兑现为**中央脊廊 + 侧向 loot 房间**：房间的单一门洞遮挡远比开放拐角强，
    //   ⇒ 玩家一次只撞见一间房里的东西。危险 Medium ⇒ 丧尸 **4 只**（下水道低危是 3 只），**各藏一房深角**。
    //   由硬不变量钉死：**任一可行走点，能感知到你的丧尸 ≤ 1 只**（PoliceMaxConcurrentZombies，见 PoliceStationTests）。
    //   ⚠️ 围攻是断崖不是斜坡（docs/research/2026-07-14-lanchester.md，**2026-07-17 全仓重跑值**）：
    //   1只100% → 2只84.5% → 3只22.0% → 4只1.6% → 5只0%，拐点在**第 3 只**。
    //   「Medium」是**总量**更多，不是**同时**更多。
    //   ⚠️ **两组作废数，谁都别再捡回来**：
    //     ① "2只16.6% / 3只0.8%" —— 出自 [T53] 已被用户否决并**回退掉**的 70/1.5 热流血口径
    //        （BleedModel.cs:181-190 自陈「16.6% 不是实机的数」；现值 DefaultBleedRatePerWound = 0.55）⇒ 现行代码复现不出。
    //     ② "2只84.4% / 3只24.1% / 1.05处" —— 出自 991b777 那版 **born-stale 报告**（该 commit 手改了报告里一行冷却值
    //        让它看着像新的，**表格却一字没重跑**；其自身代码实跑就是 84.5/22.0，且与 HEAD 输出 MD5 逐字节相同
    //        ⇒ 提交后零漂移、对不上只能是出生即错）⇒ 已由 2026-07-17 全仓重跑取代。
    // ==================================================================================

    /// <summary>警察局墙厚（px）。</summary>
    public const float PoliceWallThickness = 40f;

    /// <summary>
    /// 🔴 <b>硬不变量：任一可行走点，能感知到你的丧尸 ≤ 1 只。</b>（已上单测 <c>PoliceStationTests</c>。）
    /// 「室内多拐角」的意义是"你不知道下一间房里有什么"，不是"被一群围死"——这条护栏把两者焊开。
    /// <para>
    /// 🔴 <b>为什么是 1 而不是 2（断崖点在第 3 只）</b>——<b>胜率不是成本</b>（CLAUDE.md 通则）：84.5% 的「2 只」不是白捡，
    /// 15.5% 的败率里 <b>12.8% 是昏迷倒地</b>（lanchester.md 原话「在一群丧尸中间昏过去＝被吃掉」），打赢时平均还留下
    /// <b>1.14 处永久残缺</b>、惨胜率 30%。取 1 ＝比断崖点**保守一格留裕度，这是有意的**。
    /// <b>放宽它＝改关卡难度＝数值变更，须用户拍板。</b>
    /// </para>
    /// </summary>
    public const int PoliceMaxConcurrentZombies = 1;

    /// <summary>
    /// 判"这只丧尸能感知到这个位置"用的**保守**半径（px）＝<b>400</b>。
    /// <para>
    /// 校准（警察局<b>不</b>标室内恒暗，与下水道不同）：白昼环境光下丧尸视距 = <see cref="VisionLogic.BaseRange"/>(300)，
    /// 且白昼里<see cref="VisionLogic.MaxExposureBonus"/>暴露放大几乎归零（越亮越不放大）⇒ 真实全向上界 ≈ <b>300</b>（视距）
    /// ＋嗅觉 <see cref="NoiseLogic.ZombieSmellRadius"/>(70)/脚步 40 都更短。取 <b>400</b> 留 ~33% 裕度。
    /// 夜相位视距只会更短（<see cref="VisionLogic.DarkRangeFactor"/>），故 400 对全相位都够保守。
    /// </para>
    /// ⚠️ 它管不了枪声（战斗噪音 350~600 不分阵营，能把整层叫醒）——那是玩家自己的选择，几何护栏不替他兜底。
    /// </summary>
    public const float PoliceAggroRadius = 400f;

    /// <summary>
    /// 警察局的**可行走区域**：中央脊廊（门厅 → 连廊 → 主走廊 → 拘留区）+ 三间侧向 loot 房间（办公区/证物室/更衣室）。
    /// 墙 = 这些矩形的**补集**（<see cref="PoliceWalls"/> 自动推出）⇒ 房间与墙不可能对不上（既不漏洞、也不堵路）。
    /// 相邻矩形**真重叠**的那一小段就是门洞/junction。近→深：门厅(入口) → 办公区/证物室/更衣室 → 拘留区(禁闭室·最深)。
    /// <para>
    /// 🔴 <b>[SPEC-T60·Phase2] 已随画布放大到 2800×1900 铺开</b>（<see cref="ExplorationLevelSize"/> 登记，小图档）。
    /// 放大的前提是 Phase1 把威胁从"几何摆位 + 固定像素感知"改成**绑门实体的开门激活**（拘留区那只）＋普通丧尸感知唤醒
    /// ⇒ 触发与尺度无关。放大**只重排坐标、不改 authored 拓扑语义**：中央脊廊 + 三间侧房 + 最深禁闭区、4 只各藏一房、
    /// 拘留区唯一入口那道锁门，全部照旧。<b>走廊宽 140 / 墙厚 40 刻意不缩放</b>——门板铺满走廊穿墙口 ⇒ 门宽随之恒为 140
    /// （同本文件医院几何「门宽刻意不缩放」先例）。放大出来的是脊廊纵深与房间进深（＝步行/搜刮工作量），不是门洞。
    /// 坐标「拟定待调」，精调归实机校准。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<PoliceRoom> PoliceRooms = new[]
    {
        new PoliceRoom("门厅",   new WallRect(300f, 1380f, 560f, 360f)),   // 入口接待厅（SW）
        new PoliceRoom("连廊",   new WallRect(740f, 1450f, 1000f, 140f)),  // 门厅 → 主走廊（宽 140·不缩放）
        new PoliceRoom("主走廊", new WallRect(1600f, 340f, 140f, 1250f)),  // 纵贯中央走廊（宽 140·不缩放）
        new PoliceRoom("拘留区", new WallRect(1440f, 200f, 560f, 240f)),   // 禁闭区（最深·两件甲）
        new PoliceRoom("办公区", new WallRect(1680f, 1240f, 660f, 350f)),  // 侧房·东（近）
        new PoliceRoom("证物室", new WallRect(980f, 880f, 660f, 340f)),    // 侧房·西（中）
        new PoliceRoom("更衣室", new WallRect(1680f, 560f, 600f, 280f)),   // 侧房·东（深）
    };

    /// <summary>探索队进关的落点（门厅内），也是回营的返回区。</summary>
    public static readonly (float X, float Y) PoliceEntry = (440f, 1660f);

    /// <summary>**最深处** —— 禁闭区核心（拘留区）。用于连通性护栏（从入口必须走得到两件甲）。</summary>
    public static readonly (float X, float Y) PoliceDeepest = (1520f, 270f);

    /// <summary>
    /// 警察局 5 处搜刮点（id 与 <c>ExplorationCache</c> 一一对应）。由近到深：前台 → 办公桌 → 证物柜 → 更衣柜 → 囚室(两件甲)。
    /// 放大**只重排坐标、不新增 id**（新搜刮点须先有 authored 叙事）⇒ <c>PoliceStationCacheTests</c> 计数恒绿。
    /// </summary>
    public static readonly IReadOnlyList<PoliceCacheSpot> PoliceCacheSpots = new[]
    {
        new PoliceCacheSpot(ExplorationCache.PoliceFrontDeskId,    420f, 1500f, "前台"),
        new PoliceCacheSpot(ExplorationCache.PoliceBullpenId,     2140f, 1400f, "办公桌"),
        new PoliceCacheSpot(ExplorationCache.PoliceEvidenceId,    1120f, 1040f, "证物柜"),
        new PoliceCacheSpot(ExplorationCache.PoliceLockerRoomId,  2080f,  700f, "更衣柜"),
        new PoliceCacheSpot(ExplorationCache.PoliceHoldingCellId, 1540f,  320f, "囚室"),
    };

    /// <summary>
    /// 警察局游荡丧尸布点 —— <b>4 只，各藏一间房的深角</b>（Medium）。房间门洞遮挡 ⇒ 从走廊只在门口一小段才看得见它。
    /// <para>
    /// 🔴 <b>入口(门厅)看不见任何一只</b>（进门不当场挨打，同消防站/下水道口径）；禁闭区那只<b>守着两件甲</b>（回报有代价）。
    /// 🔴 <b>挪动/新增任何一只之前，先跑 PoliceStationTests</b> —— 房间遮挡很脆，挪出门口视线就可能让某点同时暴露两只（＝断崖）。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<(float X, float Y)> PoliceZombieSpots = new[]
    {
        (2300f, 1550f),  // 办公区深处（远 SE 角）
        (1020f, 920f),   // 证物室深处（远 NW 角）
        (2240f, 800f),   // 更衣室深处（远 SE 角）
        (1960f, 240f),   // 拘留区深处（远 NE 角·守着两件甲·门后特殊丧尸）
    };

    // ==================================================================================
    // 🔴 拘留区那道**锁死的门** —— 全项目第一扇真锁门（撬锁首次接进探索关）。
    //
    //   几何：主走廊(1600,340,140,1250) 与 拘留区(1440,200,560,240) 在 x[1600,1740] y[340,440] 处**真重叠**（junction），
    //   而它俩的补集把「拘留区南墙」在 x[1600,1740] 处开了个洞（主走廊穿墙上去）——**那个洞就是禁闭区的唯一入口**。
    //   门板恰好填满这个洞：x[1600,1740] y[440,480]（=拘留区南墙缺口，厚度=墙厚）。**门宽＝走廊宽 140，放大时刻意不缩放。**
    //   ⇒ 门锁着 ⇒ 禁闭区（囚室两件甲 + 守甲那只丧尸）**够不着**；撬开才可达。无旁路（更衣室 y560-840 不接拘留区 y200-440，
    //   拘留区只与主走廊相邻）——这条「唯一入口」由 PoliceStationTests 的可达性护栏钉死。
    //
    //   LockTier=Standard（普通锁·0.45/次·期望 ~13 秒 / ~1.2 根丝）：数值「拟定待调」。取普通档而非坚固档的理由——
    //   这是玩家遇到的**第一扇**锁门，坚固锁(0.25·期望 32 秒 / 3 根丝)在禁闭区那只丧尸眼皮底下蹲太久＝劝退；
    //   普通锁已足够让「安静撬 vs 砸开(180 招整层)」的取舍成立。要调档只改这一处。
    // ==================================================================================

    /// <summary>禁闭区那道锁死的门的名字（玩家可见 + 容器登记键）。</summary>
    public const string PoliceHoldingDoorName = "拘留区铁门";

    /// <summary>拘留区（禁闭区）矩形——唯一有门的房。<see cref="PoliceSpotBehindHoldingDoor"/> 据此判某丧尸/点是否锁在铁门后。</summary>
    private static readonly WallRect PoliceHoldingCell = new(1440f, 200f, 560f, 240f);

    /// <summary>
    /// [SPEC-T60·探索威胁模型] 某点是否落在拘留区内（＝锁在拘留区铁门后）。
    /// 🔴 警察局唯一有门的房是拘留区：守着两件甲的那只丧尸是**门后特殊丧尸**（冻结、免疫视野/噪音/靠近），
    /// **撬开拘留区铁门才唤醒**（<see cref="ZombieActivation"/>）。另 3 间无门开放侧房的丧尸是普通丧尸（靠近/视野唤醒）。
    /// </summary>
    public static bool PoliceSpotBehindHoldingDoor((float X, float Y) spot)
        => spot.X >= PoliceHoldingCell.X && spot.X <= PoliceHoldingCell.Right
        && spot.Y >= PoliceHoldingCell.Y && spot.Y <= PoliceHoldingCell.Bottom;

    /// <summary>禁闭区那道门的锁档 —— 普通锁（拟定待调，见上方块注释）。</summary>
    public const LockTier PoliceHoldingLockTier = LockTier.Standard;

    /// <summary>
    /// 警察局的关内门 —— 只有一扇：**拘留区南墙缺口上的锁死铁门**（禁闭区唯一入口）。
    /// 初始 <see cref="DoorState.Locked"/> + <see cref="PoliceHoldingLockTier"/>：撬开(消耗铁丝)才够得着两件甲。
    /// </summary>
    public static IReadOnlyList<ExplorationDoor> PoliceDoors()
    {
        const float t = PoliceWallThickness; // 40：门板厚度＝墙厚，恰好填满南墙缺口
        // 拘留区(1440,200,560,240) 底边 y=440；主走廊在此开洞 x[1600,1740]。门板铺满这个洞。
        // 🔴 门宽 140 ＝走廊宽，**放大时刻意不缩放**（同本文件医院几何先例）。
        return new[]
        {
            new ExplorationDoor(
                PoliceHoldingDoorName,
                new WallRect(1600f, 440f, 140f, t),
                DoorState.Locked,
                PoliceHoldingLockTier),
        };
    }

    /// <summary>某点是否在警察局的可行走区域内（＝落在任一房间/走廊矩形里）。</summary>
    public static bool PoliceContains(float x, float y)
    {
        foreach (PoliceRoom c in PoliceRooms)
        {
            WallRect r = c.Rect;
            if (x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>警察局的全部墙段 —— 由可行走矩形的**补集**自动推出（复用通用 <see cref="ComplementWalls"/>）。三用：碰撞 / 导航 obstruction / **视线遮挡**。</summary>
    public static IReadOnlyList<WallRect> PoliceWalls()
        => ComplementWalls(PoliceRooms.Select(c => c.Rect).ToList(), PoliceWallThickness);

    /// <summary>
    /// 把一组**可行走矩形**的补集推成墙段（通用版）：给每个矩形四条边贴一条朝外的墙条，再把**被别的矩形压住**的区间挖成口子（门洞/junction）。
    /// ⇒ 矩形与墙天然对得上：既不漏洞（每条边都覆盖），也不堵路（重叠处一定被挖开）。下水道另有等价的 <see cref="SewerWalls"/>（保持原样不动）。
    /// </summary>
    private static IReadOnlyList<WallRect> ComplementWalls(IReadOnlyList<WallRect> regions, float t)
    {
        var walls = new List<WallRect>(48);
        foreach (WallRect r in regions)
        {
            AddComplementStrip(walls, new WallRect(r.X - t, r.Y - t, r.Width + 2f * t, t), regions, horizontal: true);   // 上
            AddComplementStrip(walls, new WallRect(r.X - t, r.Bottom, r.Width + 2f * t, t), regions, horizontal: true);  // 下
            AddComplementStrip(walls, new WallRect(r.X - t, r.Y, t, r.Height), regions, horizontal: false);              // 左
            AddComplementStrip(walls, new WallRect(r.Right, r.Y, t, r.Height), regions, horizontal: false);              // 右
        }
        return walls;
    }

    /// <summary>贴一条墙条，但把**与任一可行走矩形相交**的区间挖成口子。<paramref name="horizontal"/>=true ⇒ 沿 X 切；否则沿 Y 切。</summary>
    private static void AddComplementStrip(List<WallRect> walls, WallRect strip, IReadOnlyList<WallRect> regions, bool horizontal)
    {
        const float eps = 0.01f;
        var gaps = new List<(float Lo, float Hi)>();
        foreach (WallRect c in regions)
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
//      ⚠️ 依据 docs/research/2026-07-14-lanchester.md（机器生成·**2026-07-17 全仓重跑值**）：
//      1只 **100%** → 2只 **84.5%** → 3只 **22.0%** → 4只 1.6% → 5只 0% ⇒ 围攻是断崖，拐点在**第 3 只**。
//      ⚠️ **两组作废数别再捡**：① "2只16.6%/3只0.8%" 出自 [T53] 用户否决并回退掉的 70/1.5 热流血口径
//         （现值 BleedModel.DefaultBleedRatePerWound = 0.55 ⇒ 复现不出来）；② "2只84.4%/3只24.1%/1.05处"
//         出自 991b777 born-stale 报告（表格没重跑·其自身代码实跑就是 84.5/22.0）。作废说明与 SewerTests 对齐。
//      🔴 **数字两次翻修，护栏没松**：2 只虽非死局，但已要付 1.14 处永久残缺／30% 惨胜／20% 失血，
//         且 15.5% 的败率里 12.8% 是**昏迷倒地**＝在丧尸堆里被吃掉（**胜率不是成本**）。
//         「≤1」比断崖点还保守一格＝刻意留的裕度。
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

/// <summary>[警察局] 一间**可行走**房间/走廊（矩形）。墙由 <see cref="ExplorationWalls.PoliceWalls"/> 从补集推出。</summary>
public readonly record struct PoliceRoom(string Name, WallRect Rect);

/// <summary>[警察局] 一处搜刮点（id 与 <c>ExplorationCache</c> 一一对应）。</summary>
public readonly record struct PoliceCacheSpot(string Id, float X, float Y, string Label);
