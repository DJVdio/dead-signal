using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T61] 下水道的**几何护栏**。用户对这一关的原话是：
/// 「规模小…**除了某几个拐角可能有一只丧尸，基本没有危险**，主要靠**黑暗逼仄的环境**和**大量拐角的差视野**…吓人。」
/// <para>
/// ⇒ 「**吓人**」和「**危险**」是两件事。本文件就是把它们**焊开**的那几颗螺丝：
/// 视野差（拐角遮挡）要**真的成立**，而围攻要**根本不可能发生**。
/// </para>
/// <para>
/// 🔴 依据 <c>docs/research/2026-07-14-lanchester.md</c>（Sim <c>lanchester</c> 模式·机器生成·
/// **2026-07-17 全仓重跑值**，中甲皮夹克+长袖布衣·长剑）：
/// <b>1 只 100% → 2 只 84.5% → 3 只 22.0% → 4 只 1.6% → 5 只 0%</b> ⇒ 围攻是**断崖不是斜坡**，拐点在**第 3 只**。
/// </para>
/// <para>
/// ⚠️ <b>此前这里写的"2 只 16.6%、3 只 0.8%"是陈旧数，已作废</b>：那组出自 <b>70/1.5 的热流血口径</b>，
/// 而 [T53] 用户已否决该口径并**回退到 100/0.55**（<c>BleedModel</c> 里那段 [T53 二次拍板] 注释自陈
/// 「16.6% 不是实机的数」，现值见 <c>BleedModel.DefaultBleedRatePerWound</c> = 0.55）⇒ 那组数在当前代码里复现不出来。
/// </para>
/// <para>
/// 🔴 <b>数字变了，这条护栏的理由没变</b>——<b>胜率不是成本</b>（CLAUDE.md 通则）：84.5% 的「2 只」不是白捡，
/// 打赢时平均仍留下 <b>1.14 处永久残缺、30% 惨胜率、20% 失血</b>；而 15.5% 的败率里 <b>12.8% 是昏迷/失能倒地</b>——
/// 文档原话「<b>在一群丧尸中间昏过去 ＝ 被吃掉</b>」。所以"最多同时被 1 只感知"仍不是风味参数，
/// 是这一关能不能兑现用户设计的**生死线**（且它比断崖点还保守一格＝留了裕度，这是有意的）。
/// </para>
/// </summary>
public class SewerTests
{
    private static IReadOnlyList<WallRect> Walls => ExplorationWalls.SewerWalls();

    /// <summary>把可行走区域按 <paramref name="step"/> 采成点阵（护栏测试的采样基底）。</summary>
    private static IEnumerable<(float X, float Y)> WalkableSamples(float step = 20f)
    {
        foreach (SewerCorridor c in ExplorationWalls.SewerCorridors)
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
    public void 硬不变量_通道内任何位置能感知到你的丧尸都不超过一只()
    {
        IReadOnlyList<WallRect> walls = Walls;
        IReadOnlyList<(float X, float Y)> zombies = ExplorationWalls.SewerZombieSpots;
        float r = ExplorationWalls.SewerAggroRadius;

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
                    continue;                       // 隔着墙：拐角把它挡住了 —— **这正是这一关的设计**
                }
                seen++;
            }
            if (seen > ExplorationWalls.SewerMaxConcurrentZombies)
            {
                worst.Add(((px, py), seen));
            }
        }

        Assert.True(
            worst.Count == 0,
            $"下水道有 {worst.Count} 个位置会被 2 只以上丧尸同时感知（2 只围攻＝胜率 84.5% 但留 1.14 处永久残缺，" +
            $"3 只就掉到 22.0%＝断崖；这一关的口径是「基本没有危险」）。" +
            $"头几个：{string.Join("；", worst.Take(5).Select(w => $"({w.Pos.X:0},{w.Pos.Y:0}) → {w.Count} 只"))}");
    }

    [Fact]
    public void 丧尸只有三只_且入口与最深处附近一只都没有()
    {
        // 用户原话：「除了**某几个拐角**可能有**一只**丧尸」。
        Assert.Equal(3, ExplorationWalls.SewerZombieSpots.Count);

        // 对照：消防站（既有的「低危小点」）3 只 ⇒ 下水道与全图最低危持平，名副其实。
        // 每只都必须站在可行走区域里（不能卡在墙里）。
        foreach ((float x, float y) in ExplorationWalls.SewerZombieSpots)
        {
            Assert.True(ExplorationWalls.SewerContains(x, y), $"丧尸 ({x},{y}) 不在通道里（卡墙）");
        }

        // 🔴 **最深处（耗子）附近不许有丧尸** —— 她在那儿活了很久，门口不该蹲着一只。
        (float dx, float dy) = ExplorationWalls.SewerDeepestPoint;
        foreach ((float zx, float zy) in ExplorationWalls.SewerZombieSpots)
        {
            float d = System.MathF.Sqrt((zx - dx) * (zx - dx) + (zy - dy) * (zy - dy));
            Assert.True(d > ExplorationWalls.SewerAggroRadius,
                $"丧尸 ({zx},{zy}) 离最深处（耗子）只有 {d:0}px —— 她门口不该蹲着一只");
        }

        // 🔴 **入口不许有丧尸**（同消防站口径：进门不会当场挨打）。
        (float ex, float ey) = ExplorationWalls.SewerEntry;
        foreach ((float zx, float zy) in ExplorationWalls.SewerZombieSpots)
        {
            float d = System.MathF.Sqrt((zx - ex) * (zx - ex) + (zy - ey) * (zy - ey));
            Assert.True(d > ExplorationWalls.SewerAggroRadius, $"丧尸 ({zx},{zy}) 离入口太近（{d:0}px）");
        }

        // 🔴 更强的一条：**站在耗子站的地方，一只丧尸都看不见**（不只是"够远"，是"根本没有视线"）。
        IReadOnlyList<WallRect> walls = Walls;
        foreach ((float zx, float zy) in ExplorationWalls.SewerZombieSpots)
        {
            Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, dx, dy, zx, zy),
                $"从耗子站的地方能直接看见丧尸 ({zx},{zy}) —— 她在那底下活了很久，这说不通");
        }
    }

    // ==================== 拐角：视野差要**真的**成立 ====================

    [Fact]
    public void 拐角真的挡视线_入口看不见最深处_也看不见任何一只丧尸()
    {
        IReadOnlyList<WallRect> walls = Walls;
        (float ex, float ey) = ExplorationWalls.SewerEntry;
        (float dx, float dy) = ExplorationWalls.SewerDeepestPoint;

        // 站在入口，**看不见**最深处（否则"走到最深处才遇到她"就没有了）。
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, ex, ey, dx, dy),
            "从入口能直接看到最深处 —— 拐角没起作用");

        // 站在入口，**一只丧尸都看不见**（拐角的意义：你不知道前面有什么）。
        foreach ((float zx, float zy) in ExplorationWalls.SewerZombieSpots)
        {
            Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, ex, ey, zx, zy),
                $"从入口能直接看见丧尸 ({zx},{zy}) —— 那就不叫「拐角吓人」了");
        }
    }

    [Fact]
    public void 通道逼仄_每段宽度都是140()
    {
        foreach (SewerCorridor c in ExplorationWalls.SewerCorridors)
        {
            if (c.Name == "最深处汇流室")
            {
                continue;   // 汇流室是唯一"宽一点"的地方 —— 走到头终于能站直了，也正好衬出前面有多挤
            }
            float thin = System.MathF.Min(c.Rect.Width, c.Rect.Height);
            Assert.Equal(ExplorationWalls.SewerCorridorWidth, thin, precision: 2);
        }
    }

    // ==================== 墙：既不漏，也不堵 ====================

    [Fact]
    public void 墙是通道的补集_不漏洞()
    {
        // 沿每段通道的四条边，往**外**跨半个墙厚取样：那儿要么是另一段通道（＝拐角/junction，本就该通），
        // 要么必须是墙。若两者都不是 ⇒ **墙上有个洞**，丧尸/玩家能从那儿穿出去。
        IReadOnlyList<WallRect> walls = Walls;
        const float t = ExplorationWalls.SewerWallThickness;
        var leaks = new List<(float X, float Y)>();

        foreach (SewerCorridor c in ExplorationWalls.SewerCorridors)
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
                if (ExplorationWalls.SewerContains(px, py))
                {
                    continue;   // 是另一段通道 ⇒ 拐角，本就该通
                }
                bool inWall = walls.Any(w => px >= w.X && px <= w.Right && py >= w.Y && py <= w.Bottom);
                if (!inWall)
                {
                    leaks.Add((px, py));
                }
            }
        }

        Assert.True(leaks.Count == 0,
            $"下水道墙上有 {leaks.Count} 处漏洞（既不是通道也不是墙）：" +
            $"{string.Join("；", leaks.Take(5).Select(l => $"({l.X:0},{l.Y:0})"))}");
    }

    [Fact]
    public void 路是通的_从入口能走到每一处搜刮点和最深处()
    {
        // 泛洪（4 邻域，20px 栅格）：证明**墙没有把路堵死**。
        // 手写墙最常见的两个 bug —— 漏个洞 / 堵死一条路 —— 上一条测试管前者，这条管后者。
        const float step = 20f;
        var open = new HashSet<(int, int)>();
        foreach ((float x, float y) in WalkableSamples(step))
        {
            open.Add(((int)System.MathF.Round(x / step), (int)System.MathF.Round(y / step)));
        }

        (float ex, float ey) = ExplorationWalls.SewerEntry;
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

        foreach (SewerCacheSpot spot in ExplorationWalls.SewerCacheSpots)
        {
            Assert.True(Reachable(spot.X, spot.Y), $"搜刮点「{spot.Label}」从入口走不到");
        }

        (float dx2, float dy2) = ExplorationWalls.SewerDeepestPoint;
        Assert.True(Reachable(dx2, dy2), "最深处（耗子）从入口走不到 —— 那她就永远招不到了");
    }

    // ==================== 规模：小 ====================

    [Fact]
    public void 规模小_五处搜刮点_全在通道里()
    {
        // 用户原话「规模小」+「**很少量**的物资点」。对照既有小点：消防站 5 / 河边小屋 5 / 药店 5。
        Assert.Equal(5, ExplorationWalls.SewerCacheSpots.Count);

        foreach (SewerCacheSpot spot in ExplorationWalls.SewerCacheSpots)
        {
            Assert.True(ExplorationWalls.SewerContains(spot.X, spot.Y),
                $"搜刮点「{spot.Label}」({spot.X},{spot.Y}) 不在通道里（卡墙）");
        }

        // id 不重复。
        Assert.Equal(5, ExplorationWalls.SewerCacheSpots.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void 黑暗_本关不铺固定光源_环境光为室内黑暗档()
    {
        // 「黑暗逼仄」是这一关的身份 ⇒ 无固定光源 ⇒ 环境光 = IndoorsDarkAmbient(0.10)
        // ⇒ 视锥当场缩到 ~124px / 半角 30°：**你基本上必须手持光源**，而举着光的人也正是最先被看见的人。
        VisionLogic.VisionCone dark = VisionLogic.ConeFor(VisionLogic.IndoorsDarkAmbient);
        VisionLogic.VisionCone day = VisionLogic.ConeFor(VisionLogic.DaylightAmbient);

        Assert.True(dark.Range < day.Range * 0.5f, "下水道的黑暗必须显著缩短视距");
        Assert.True(dark.HalfAngleDeg < day.HalfAngleDeg, "下水道的黑暗必须收窄视锥");

        // 保守感知半径（400）必须**明显大于**丧尸在黑暗中的真实视距 ⇒ 上面那条围攻护栏才是"留了裕度"的。
        Assert.True(ExplorationWalls.SewerAggroRadius > dark.Range * 1.5f);
    }
}
