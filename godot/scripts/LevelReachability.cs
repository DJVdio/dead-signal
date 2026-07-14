using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationWalls.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 探索关几何的**连通性判据**（离线镜像）。运行时的寻路走 Godot 的 NavigationServer2D，
// 这里不重复实现它——本类只回答一个**几何问题**：
//   「把这批矩形当障碍，A 点走不走得到 B 点？」
// 有了它，两条以往只能靠目视的设计意图，第一次变成了**可以红/绿的护栏**：
//   · **玩家跑得掉**：把关内能关的门全关上，从关内任意一点仍走得到关外 ⇒ 不会有人被自己关死在里面。
//   · **关门真的隔得开**：把某道边界的门全关上，边界两侧**真的断开** ⇒ "关门隔开丧尸"不是安慰剂。
// 这两条是**互相拉扯**的（能关死的门越多，越容易把自己反锁），只有同时钉住才叫设计成立。
//
// 判据（刻意保守，宁可判"走不通"也不判假的"走得通"）：
//   格心落在任一障碍矩形（外扩 <see cref="ExplorationWalls.NavAgentRadius"/>）内 ⇒ 该格不可走。
//   外扩＝把导航体的半径算进来，与 NavigationPolygon.AgentRadius 同一口径：
//   一个半径 14 的身子挤不过 20px 的缝，格心判定看不出来，外扩之后就看得出来。

/// <summary>关卡几何的连通性判据（栅格 BFS）。障碍＝一批 <see cref="WallRect"/>（墙 + 关着的门板）。</summary>
public static class LevelReachability
{
    /// <summary>默认栅格边长（够细，能看出 72px 的门洞；也够粗，2400×1600 一次 BFS 是毫秒级）。</summary>
    public const float DefaultCell = 12f;

    /// <summary>
    /// 在 <paramref name="bounds"/> 内、以 <paramref name="obstacles"/> 为障碍，
    /// 从 <paramref name="from"/> 是否走得到 <paramref name="to"/>。
    /// <para>障碍按 <see cref="ExplorationWalls.NavAgentRadius"/> 外扩（导航体挤不过去的缝，这里也判不通）。</para>
    /// </summary>
    public static bool PathExists(
        IReadOnlyList<WallRect> obstacles,
        WallRect bounds,
        (float X, float Y) from,
        (float X, float Y) to,
        float cell = DefaultCell,
        float agentRadius = ExplorationWalls.NavAgentRadius)
    {
        HashSet<(int, int)>? reached = Flood(obstacles, bounds, from, cell, agentRadius);
        if (reached is null)
            return false;

        (int cx, int cy) = ToCell(bounds, to, cell);
        return reached.Contains((cx, cy));
    }

    /// <summary>
    /// 从 <paramref name="from"/> 泛洪，返回可达格集合；起点本身不可走则返回 <c>null</c>
    /// （起点被砌进墙里＝几何写错了，调用方应当当作硬错误，而不是"走不通"）。
    /// </summary>
    public static HashSet<(int, int)>? Flood(
        IReadOnlyList<WallRect> obstacles,
        WallRect bounds,
        (float X, float Y) from,
        float cell = DefaultCell,
        float agentRadius = ExplorationWalls.NavAgentRadius)
    {
        int cols = Math.Max(1, (int)MathF.Ceiling(bounds.Width / cell));
        int rows = Math.Max(1, (int)MathF.Ceiling(bounds.Height / cell));

        var blocked = new bool[cols, rows];
        for (int i = 0; i < cols; i++)
        {
            for (int j = 0; j < rows; j++)
            {
                float px = bounds.X + (i + 0.5f) * cell;
                float py = bounds.Y + (j + 0.5f) * cell;
                blocked[i, j] = IsBlocked(obstacles, px, py, agentRadius);
            }
        }

        (int sx, int sy) = ToCell(bounds, from, cell);
        if (sx < 0 || sy < 0 || sx >= cols || sy >= rows || blocked[sx, sy])
            return null;

        var reached = new HashSet<(int, int)> { (sx, sy) };
        var queue = new Queue<(int, int)>();
        queue.Enqueue((sx, sy));

        // 4 邻域（不走对角：对角穿两堵墙的夹角是假通路）。
        Span<(int dx, int dy)> dirs = stackalloc (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            foreach ((int dx, int dy) in dirs)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows)
                    continue;
                if (blocked[nx, ny] || reached.Contains((nx, ny)))
                    continue;
                reached.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }

        return reached;
    }

    /// <summary>某点是否被障碍挡住（障碍按 <paramref name="agentRadius"/> 外扩）。</summary>
    public static bool IsBlocked(IReadOnlyList<WallRect> obstacles, float px, float py, float agentRadius)
    {
        if (obstacles is null)
            return false;

        foreach (WallRect w in obstacles)
        {
            if (px >= w.X - agentRadius && px <= w.Right + agentRadius &&
                py >= w.Y - agentRadius && py <= w.Bottom + agentRadius)
            {
                return true;
            }
        }
        return false;
    }

    private static (int, int) ToCell(WallRect bounds, (float X, float Y) p, float cell)
        => ((int)MathF.Floor((p.X - bounds.X) / cell), (int)MathF.Floor((p.Y - bounds.Y) / cell));

    /// <summary>把一批可关的门的门板并进障碍集合（＝「把这些门全关上」的几何）。</summary>
    public static List<WallRect> WithDoorsClosed(IReadOnlyList<WallRect> walls, IReadOnlyList<ExplorationDoor> doors)
    {
        var all = new List<WallRect>(walls);
        foreach (ExplorationDoor d in doors)
            all.Add(d.Rect);
        return all;
    }

    /// <summary>把除 <paramref name="openName"/> 之外的门板并进障碍集合（＝「只推开这一扇」的几何）。</summary>
    public static List<WallRect> WithOneDoorOpen(
        IReadOnlyList<WallRect> walls, IReadOnlyList<ExplorationDoor> doors, string openName)
    {
        var all = new List<WallRect>(walls);
        foreach (ExplorationDoor d in doors)
        {
            if (d.Name != openName)
                all.Add(d.Rect);
        }
        return all;
    }
}
