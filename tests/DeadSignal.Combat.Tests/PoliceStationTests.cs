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
/// 🔴 围攻是**断崖不是斜坡**（<c>docs/research/2026-07-14-lanchester.md</c>，<b>2026-07-17 全仓重跑值</b>）：
/// <b>1 只 100% → 2 只 84.5% → 3 只 22.0% → 4 只 1.6% → 5 只 0%</b> ⇒ 拐点在**第 3 只**。
/// </para>
/// <para>
/// ⚠️ <b>两组作废数，谁都别再捡回来</b>：
/// <list type="number">
/// <item><b>"2 只 16.6% / 3 只 0.8%"</b>——出自 [T53] 已被用户否决并**回退掉**的 <b>70/1.5 热流血口径</b>
/// （<c>BleedModel.cs:181-190</c> 自陈「16.6% 不是实机的数」；现值 <c>BleedModel.DefaultBleedRatePerWound = 0.55</c>）
/// ⇒ 现行代码复现不出来。</item>
/// <item><b>"2 只 84.4% / 3 只 24.1% / 1.05 处"</b>——出自 <c>991b777</c> 那版 <b>born-stale 报告</b>：该 commit 手改了
/// 报告里一行冷却值让它看着像新的、**表格却一字没重跑**；其自身代码实跑就是 84.5/22.0，且与 HEAD 输出 MD5 逐字节相同
/// ⇒ 提交后零漂移、对不上只能是出生即错。已由 2026-07-17 全仓重跑取代。</item>
/// </list>
/// </para>
/// <para>
/// 🔴 <b>数字两次翻修，这条护栏的理由一个字没变</b>——<b>胜率不是成本</b>（CLAUDE.md 通则）：84.5% 的「2 只」不是白捡，
/// 15.5% 的败率里有 <b>12.8% 是昏迷倒地</b>（lanchester.md 原话「在一群丧尸中间昏过去＝被吃掉」），打赢时平均还留下
/// <b>1.14 处永久残缺、30% 惨胜率、20% 失血</b>。所以"任一可行走点最多被 1 只感知"仍不是风味参数，是这一关
/// 能不能兑现「多拐角吓人但不被围死」的**生死线**（且它比断崖点还保守一格＝**留了裕度，这是有意的**；
/// 重跑后 3 只从 24.1% 掉到 22.0% ⇒ 断崖只更陡，这一格裕度更该留）。
/// <b>放宽它＝改关卡难度＝数值变更，须用户拍板，不是顺手能做的事。</b>
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
            $"警察局有 {worst.Count} 个位置会被 2 只以上丧尸同时感知（2 只＝84.5% 胜率但要付 1.14 处永久残缺，" +
            $"且第 3 只就掉到 22.0% 的断崖；这一关的口径是「多拐角吓人但不被围死」）。" +
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
        // 门板落在拘留区(1440,200,560,240)南墙那道缺口上（主走廊 x[1600,1740] 穿墙口）。
        ExplorationDoor door = ExplorationWalls.PoliceDoors().Single();
        var r = door.Rect;
        Assert.Equal(1600f, r.X, 1);
        Assert.Equal(1740f, r.Right, 1);           // 🔴 门宽＝走廊宽 140 —— 放大到 2800×1900 后**刻意不缩放**
        Assert.Equal(440f, r.Y, 1);                // 拘留区底边
        Assert.Equal(ExplorationWalls.PoliceWallThickness, r.Height, 1); // 厚度＝墙厚

        // 门不该压住囚室搜刮点（囚室在门的另一侧、够不着才是设计）。
        PoliceCacheSpot cell = ExplorationWalls.PoliceCacheSpots.Single(s => s.Id == ExplorationCache.PoliceHoldingCellId);
        bool cellInDoor = cell.X >= r.X && cell.X <= r.Right && cell.Y >= r.Y && cell.Y <= r.Bottom;
        Assert.False(cellInDoor, "门板压住了囚室搜刮点");
    }

    // ==================== 🔴 [SPEC-T60] 探索威胁模型：拘留区那只锁在铁门后 ====================

    /// <summary>
    /// 🔴 拘留区那只（守着两件甲）＝**门后特殊丧尸**：冻结在拘留区铁门后，对视野/噪音/靠近全免疫，
    /// **有且仅有撬开铁门才唤醒**（转为普通丧尸）。另 3 间无门开放侧房的那 3 只＝普通丧尸（靠近/视野唤醒）。
    /// <para>门未开时它冻结，正是"入口看不见 / 走廊摸过去也不惊动它"的机制侧保证——威胁绑门实体、与尺度无关（Phase2 可放大）。</para>
    /// </summary>
    [Fact]
    public void 拘留区那只锁在铁门后_撬开才唤醒_另三只是普通丧尸()
    {
        var behindGate = ExplorationWalls.PoliceZombieSpots
            .Where(s => ExplorationWalls.PoliceSpotBehindHoldingDoor(s)).ToList();
        Assert.Single(behindGate);                    // 有且仅有拘留区那只锁在门后
        Assert.Equal((1960f, 240f), behindGate[0]);   // 守着两件甲的那只（Phase2 放大后的拘留区远 NE 角）

        // 门未开：它冻结（免疫视野/噪音/靠近）。
        Assert.True(ZombieActivation.IsFrozen(doorLocked: true, activated: false));

        // 撬开拘留区铁门 ⇒ 唤醒它（转普通）。
        Assert.True(ZombieActivation.DoorOpenActivates(
            new[] { ExplorationWalls.PoliceHoldingDoorName }, ExplorationWalls.PoliceHoldingDoorName, activated: false));

        // 另 3 只不在拘留区 ⇒ 普通丧尸（不锁门、靠近/视野唤醒）。
        Assert.Equal(3, ExplorationWalls.PoliceZombieSpots.Count(s => !ExplorationWalls.PoliceSpotBehindHoldingDoor(s)));
    }
}
