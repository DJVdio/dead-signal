using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [警察局] 警察局的**几何护栏**。用户拍板：前中期 · 规模小 · <b>室内多拐角</b> · 危险 <b>Medium</b>。
/// <para>
/// 「室内多拐角」＝中央脊廊 + 侧向 loot 房间（房间的单一门洞遮挡远比开放拐角强）。「Medium」＝丧尸 <b>4 只</b>
/// （下水道低危是 3 只），各藏一间房的深角。但**同时**能感知到你的丧尸仍被钉死在 ≤ 1——
/// 「Medium」是总量更多，不是围攻更狠。
/// </para>
/// <para>
/// 🔴 依据 <c>sim-lanchester</c> 实测：<b>2 只围攻胜率 16.6%、3 只 0.8%</b> ⇒ 围攻是**断崖**。
/// 所以"任一可行走点最多被 1 只感知"不是风味参数，是这一关能不能兑现「多拐角吓人但不被围死」的**生死线**。
/// </para>
/// </summary>
public class PoliceStationTests
{
    private static IReadOnlyList<WallRect> Walls => ExplorationWalls.PoliceWalls();

    /// <summary>把可行走区域按 <paramref name="step"/> 采成点阵（护栏测试的采样基底）。</summary>
    private static IEnumerable<(float X, float Y)> WalkableSamples(float step = 20f)
    {
        foreach (PoliceRoom c in ExplorationWalls.PoliceRooms)
        {
            WallRect r = c.Rect;
            for (float x = r.X + 5f; x <= r.Right - 5f; x += step)
            {
                for (float y = r.Y + 5f; y <= r.Bottom - 5f; y += step)
                {
                    yield return (x, y);
                }
            }
        }
    }

    // ==================== 🔴 核心护栏：任何位置最多被 1 只丧尸感知 ====================

    [Fact]
    public void 硬不变量_任一可行走位置能感知到你的丧尸都不超过一只()
    {
        IReadOnlyList<WallRect> walls = Walls;
        IReadOnlyList<(float X, float Y)> zombies = ExplorationWalls.PoliceZombieSpots;
        float r = ExplorationWalls.PoliceAggroRadius;

        var worst = new List<((float X, float Y) Pos, int Count)>();

        foreach ((float px, float py) in WalkableSamples())
        {
            int seen = 0;
            foreach ((float zx, float zy) in zombies)
            {
                float dx = zx - px, dy = zy - py;
                if (dx * dx + dy * dy > r * r)
                {
                    continue;                       // 太远：感知不到
                }
                if (ExplorationWalls.SegmentHitsAnyWall(walls, px, py, zx, zy))
                {
                    continue;                       // 隔着房间墙：门洞把它挡住了 —— **这正是这一关的设计**
                }
                seen++;
            }
            if (seen > ExplorationWalls.PoliceMaxConcurrentZombies)
            {
                worst.Add(((px, py), seen));
            }
        }

        Assert.True(
            worst.Count == 0,
            $"警察局有 {worst.Count} 个位置会被 2 只以上丧尸同时感知（围攻＝胜率 16.6%，这一关的口径是「多拐角吓人但不被围死」）。" +
            $"头几个：{string.Join("；", worst.Take(5).Select(w => $"({w.Pos.X:0},{w.Pos.Y:0}) → {w.Count} 只"))}");
    }

    [Fact]
    public void 丧尸四只_各在可行走房间里_且入口看不见任何一只()
    {
        // Medium＝4 只（下水道低危是 3 只）。每只都在可行走区（不卡墙）。
        Assert.Equal(4, ExplorationWalls.PoliceZombieSpots.Count);
        foreach ((float x, float y) in ExplorationWalls.PoliceZombieSpots)
        {
            Assert.True(ExplorationWalls.PoliceContains(x, y), $"丧尸 ({x},{y}) 不在房间里（卡墙）");
        }

        // 🔴 **入口(门厅)看不见任何一只**（进门不会当场挨打，同消防站/下水道口径）。
        IReadOnlyList<WallRect> walls = Walls;
        (float ex, float ey) = ExplorationWalls.PoliceEntry;
        foreach ((float zx, float zy) in ExplorationWalls.PoliceZombieSpots)
        {
            Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, ex, ey, zx, zy),
                $"从入口能直接看见丧尸 ({zx},{zy}) —— 那就不叫「进门不当场挨打」了");
        }
    }

    // ==================== 墙：既不漏，也不堵 ====================

    [Fact]
    public void 墙是房间的补集_不漏洞()
    {
        // 沿每个房间/走廊的四条边，往**外**跨半个墙厚取样：那儿要么是另一段可行走区（门洞/junction，本就该通），
        // 要么必须是墙。若两者都不是 ⇒ **墙上有个洞**，丧尸/玩家能从那儿穿出去。
        IReadOnlyList<WallRect> walls = Walls;
        const float t = ExplorationWalls.PoliceWallThickness;
        var leaks = new List<(float X, float Y)>();

        foreach (PoliceRoom c in ExplorationWalls.PoliceRooms)
        {
            WallRect r = c.Rect;
            var probes = new List<(float X, float Y)>();
            for (float x = r.X + 5f; x <= r.Right - 5f; x += 10f)
            {
                probes.Add((x, r.Y - t / 2f));        // 上边外侧
                probes.Add((x, r.Bottom + t / 2f));   // 下边外侧
            }
            for (float y = r.Y + 5f; y <= r.Bottom - 5f; y += 10f)
            {
                probes.Add((r.X - t / 2f, y));        // 左边外侧
                probes.Add((r.Right + t / 2f, y));    // 右边外侧
            }

            foreach ((float px, float py) in probes)
            {
                if (ExplorationWalls.PoliceContains(px, py))
                {
                    continue;   // 是另一段可行走区 ⇒ 门洞/junction，本就该通
                }
                bool inWall = walls.Any(w => px >= w.X && px <= w.Right && py >= w.Y && py <= w.Bottom);
                if (!inWall)
                {
                    leaks.Add((px, py));
                }
            }
        }

        Assert.True(leaks.Count == 0,
            $"警察局墙上有 {leaks.Count} 处漏洞（既不是可行走区也不是墙）：" +
            $"{string.Join("；", leaks.Take(5).Select(l => $"({l.X:0},{l.Y:0})"))}");
    }

    [Fact]
    public void 路是通的_从入口能走到每一处搜刮点和最深处()
    {
        // 泛洪（4 邻域，20px 栅格）：证明**墙没有把路堵死**。手写墙最常见的两个 bug —— 漏个洞 / 堵死一条路 ——
        // 上一条测试管前者，这条管后者。尤其是**两件甲在最深的禁闭区**：走不到＝这一关的回报永远拿不到。
        const float step = 20f;
        var open = new HashSet<(int, int)>();
        foreach ((float x, float y) in WalkableSamples(step))
        {
            open.Add(((int)System.MathF.Round(x / step), (int)System.MathF.Round(y / step)));
        }

        (float ex, float ey) = ExplorationWalls.PoliceEntry;
        var start = ((int)System.MathF.Round(ex / step), (int)System.MathF.Round(ey / step));
        Assert.Contains(start, open);

        var seen = new HashSet<(int, int)> { start };
        var queue = new Queue<(int, int)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int nx, int ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
            {
                if (open.Contains((nx, ny)) && seen.Add((nx, ny)))
                {
                    queue.Enqueue((nx, ny));
                }
            }
        }

        bool Reachable(float x, float y)
        {
            int gx = (int)System.MathF.Round(x / step), gy = (int)System.MathF.Round(y / step);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (seen.Contains((gx + dx, gy + dy)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        foreach (PoliceCacheSpot spot in ExplorationWalls.PoliceCacheSpots)
        {
            Assert.True(Reachable(spot.X, spot.Y), $"搜刮点「{spot.Label}」从入口走不到");
        }

        (float dx2, float dy2) = ExplorationWalls.PoliceDeepest;
        Assert.True(Reachable(dx2, dy2), "最深处（禁闭区·两件甲）从入口走不到 —— 那这一关的回报就永远拿不到了");
    }

    // ==================== 规模：小 ====================

    [Fact]
    public void 规模小_五处搜刮点_全在可行走区_且id不重复()
    {
        // 用户口径「规模小」。对照既有小点：消防站 5 / 河边小屋 5 / 下水道 5。
        Assert.Equal(5, ExplorationWalls.PoliceCacheSpots.Count);

        foreach (PoliceCacheSpot spot in ExplorationWalls.PoliceCacheSpots)
        {
            Assert.True(ExplorationWalls.PoliceContains(spot.X, spot.Y),
                $"搜刮点「{spot.Label}」({spot.X},{spot.Y}) 不在可行走区（卡墙）");
        }

        Assert.Equal(5, ExplorationWalls.PoliceCacheSpots.Select(s => s.Id).Distinct().Count());

        // 与关内内容层（ExplorationCache）一一对应：几何侧的 id 就是 CacheIdsFor 那 5 个（两份事实源焊死）。
        Assert.Equal(
            ExplorationCache.CacheIdsFor(ExplorationCache.PoliceStationName).OrderBy(s => s),
            ExplorationWalls.PoliceCacheSpots.Select(s => s.Id).OrderBy(s => s));
    }

    // ==================== 🔴 拘留区那道锁门：撬开才可达（全项目第一扇真锁门） ====================

    [Fact]
    public void 拘留区铁门_初始锁死_且是普通锁()
    {
        // 全项目第一扇真锁门：初始 Locked（挡人/挡视线/断寻路都成立），档次 ≠ None（撬得动、要铁丝）。
        IReadOnlyList<ExplorationDoor> doors = ExplorationWalls.PoliceDoors();
        ExplorationDoor door = Assert.Single(doors);   // 警察局只有这一扇门
        Assert.Equal(DoorState.Locked, door.Initial);
        Assert.NotEqual(LockTier.None, door.Lock);
        Assert.True(DoorLogic.Blocks(door.Initial), "锁着的门必须挡路，否则禁闭区不设防");

        // 撬锁参数按档次派生（拿这一扇门把「门 ↔ DoorLogic 机制」焊死）：普通锁 0.45/次、6 秒。
        Assert.Equal(ExplorationWalls.PoliceHoldingLockTier, door.Lock);
        Assert.Equal(0.45, DoorLogic.PickChance(door.Lock), 3);
        Assert.Equal(6.0, DoorLogic.PickSeconds(door.Lock), 3);
    }

    [Fact]
    public void 铁门锁着时禁闭区够不着_撬开后才可达_是唯一入口()
    {
        // 🔴 这条护栏证明两件事：①这扇门锁着时，禁闭区（囚室两件甲 + 守甲那只丧尸）**从入口走不到**；
        //    ②它是禁闭区的**唯一**入口（把这一道门当障碍拿掉整个禁闭区就废——没有旁路漏洞）。
        //    做法：在墙的基础上，把门板矩形当额外障碍（＝门锁着/关着的态），泛洪看还到不到禁闭区。
        const float step = 20f;

        ExplorationDoor door = ExplorationWalls.PoliceDoors().Single();
        var dr = door.Rect; // 门板矩形（锁着时它就是一堵墙）

        bool InDoor(float x, float y) =>
            x >= dr.X && x <= dr.Right && y >= dr.Y && y <= dr.Bottom;

        // 可行走点阵；门锁着的态 = 从可行走集合里挖掉落在门板里的点。
        HashSet<(int, int)> BuildOpen(bool doorBlocking)
        {
            var open = new HashSet<(int, int)>();
            foreach (PoliceRoom c in ExplorationWalls.PoliceRooms)
            {
                WallRect r = c.Rect;
                for (float x = r.X + 5f; x <= r.Right - 5f; x += step)
                {
                    for (float y = r.Y + 5f; y <= r.Bottom - 5f; y += step)
                    {
                        if (doorBlocking && InDoor(x, y))
                        {
                            continue;   // 门锁着：这块过不去
                        }
                        open.Add(((int)System.MathF.Round(x / step), (int)System.MathF.Round(y / step)));
                    }
                }
            }
            return open;
        }

        HashSet<(int, int)> Flood(HashSet<(int, int)> open)
        {
            (float ex, float ey) = ExplorationWalls.PoliceEntry;
            var start = ((int)System.MathF.Round(ex / step), (int)System.MathF.Round(ey / step));
            var seen = new HashSet<(int, int)> { start };
            var queue = new Queue<(int, int)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                (int cx, int cy) = queue.Dequeue();
                foreach ((int nx, int ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                {
                    if (open.Contains((nx, ny)) && seen.Add((nx, ny)))
                    {
                        queue.Enqueue((nx, ny));
                    }
                }
            }
            return seen;
        }

        bool Reachable(HashSet<(int, int)> seen, float x, float y)
        {
            int gx = (int)System.MathF.Round(x / step), gy = (int)System.MathF.Round(y / step);
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (seen.Contains((gx + dx, gy + dy)))
                        return true;
            return false;
        }

        (float cellX, float cellY) = (
            ExplorationWalls.PoliceCacheSpots.Single(s => s.Id == ExplorationCache.PoliceHoldingCellId).X,
            ExplorationWalls.PoliceCacheSpots.Single(s => s.Id == ExplorationCache.PoliceHoldingCellId).Y);
        (float deepX, float deepY) = ExplorationWalls.PoliceDeepest;

        // ① 门锁着：囚室（两件甲）和最深处都够不着。
        HashSet<(int, int)> locked = Flood(BuildOpen(doorBlocking: true));
        Assert.False(Reachable(locked, cellX, cellY), "门锁着时还能走到囚室 —— 锁门没起到门控作用（有旁路，或门装错位置）");
        Assert.False(Reachable(locked, deepX, deepY), "门锁着时还能走到禁闭区最深处 —— 锁门没门控住");

        // ② 门撬开：囚室和最深处都够得着（这一道门就是唯一入口，撤掉它禁闭区就通了）。
        HashSet<(int, int)> opened = Flood(BuildOpen(doorBlocking: false));
        Assert.True(Reachable(opened, cellX, cellY), "撬开门后仍走不到囚室 —— 那这一关的两件甲永远拿不到");
        Assert.True(Reachable(opened, deepX, deepY), "撬开门后仍走不到禁闭区最深处");
    }

    [Fact]
    public void 门板恰好填满拘留区南墙缺口_不越界压住囚室或走廊别处()
    {
        // 门板落在拘留区(760,180,420,220)南墙那道缺口上（主走廊 x[900,1040] 穿墙口）。
        ExplorationDoor door = ExplorationWalls.PoliceDoors().Single();
        var r = door.Rect;
        Assert.Equal(900f, r.X, 1);
        Assert.Equal(1040f, r.Right, 1);           // 门宽＝走廊宽 140
        Assert.Equal(400f, r.Y, 1);                // 拘留区底边
        Assert.Equal(ExplorationWalls.PoliceWallThickness, r.Height, 1); // 厚度＝墙厚

        // 门不该压住囚室搜刮点（囚室在门的另一侧、够不着才是设计）。
        PoliceCacheSpot cell = ExplorationWalls.PoliceCacheSpots.Single(s => s.Id == ExplorationCache.PoliceHoldingCellId);
        bool cellInDoor = cell.X >= r.X && cell.X <= r.Right && cell.Y >= r.Y && cell.Y <= r.Bottom;
        Assert.False(cellInDoor, "门板压住了囚室搜刮点");
    }
}
