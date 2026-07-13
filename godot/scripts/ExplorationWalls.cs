using System;
using System.Collections.Generic;

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
}
