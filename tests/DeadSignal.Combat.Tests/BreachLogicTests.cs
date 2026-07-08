using System.Collections.Generic;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 敌人「砸墙破防」纯逻辑：矩形最近点/边缘距离、攻击站位外推、够不够得着的决策、按边缘距离择结构。
/// 空间执行（寻路/碰撞/重烘焙）在 Godot 层，不在此测。
/// </summary>
public sealed class BreachLogicTests
{
    // ---------------- 最近点 / 边缘距离 ----------------

    [Fact]
    public void 矩形外的点_最近点钳到边缘()
    {
        // 矩形 [100,100,200,20]（南门那种横条）；点在其正南方 (200,200)。
        (double x, double y) = BreachLogic.NearestPointOnRect(200, 200, 100, 100, 200, 20);
        Assert.Equal(200, x, 3);
        Assert.Equal(120, y, 3); // 钳到下边缘 y=100+20
        Assert.Equal(80, BreachLogic.EdgeDistance(200, 200, 100, 100, 200, 20), 3);
    }

    [Fact]
    public void 矩形内的点_边缘距离为0()
    {
        Assert.Equal(0, BreachLogic.EdgeDistance(150, 108, 100, 100, 200, 20), 6);
    }

    // ---------------- 攻击站位外推 ----------------

    [Fact]
    public void 攻击站位_从边缘朝攻击者外推standoff()
    {
        // 攻击者在 (200,200)，结构边缘点 (200,120)，standoff=30 → 站位应在两者连线上、离边缘 30。
        (double ax, double ay) = BreachLogic.ApproachPoint(200, 200, 200, 120, 30);
        Assert.Equal(200, ax, 3);
        Assert.Equal(150, ay, 3); // 120 + 30 朝南
    }

    [Fact]
    public void 攻击者压在边缘上_站位回退到边缘点()
    {
        (double ax, double ay) = BreachLogic.ApproachPoint(200, 120, 200, 120, 30);
        Assert.Equal(200, ax, 3);
        Assert.Equal(120, ay, 3);
    }

    // ---------------- 够得着就砸、够不着就贴近 ----------------

    [Theory]
    [InlineData(10, 34, BreachAction.Hammer)]        // 边缘距离 < 可及 → 砸
    [InlineData(34, 34, BreachAction.Hammer)]        // 恰好等于 → 砸
    [InlineData(80, 34, BreachAction.MoveToApproach)] // 够不着 → 先贴近
    public void 决策_按边缘距离与可及范围(double edgeDist, double reach, BreachAction expected)
    {
        Assert.Equal(expected, BreachLogic.Decide(edgeDist, reach));
    }

    // ---------------- 按边缘距离择结构（长围栏中心远、边缘近） ----------------

    [Fact]
    public void 择结构_用边缘距离而非中心距离()
    {
        // 敌人在 (1200,1540) 门外。结构 A=南门 [1100,1478,200,22]（边缘近、中心也近）；
        // 结构 B=一段很长的南墙 [300,1478,800,22]（中心在 x=700 很远，但边缘 x=1100 也不算最近）。
        var rects = new List<(double, double, double, double)>
        {
            (300, 1478, 800, 22),   // 长墙段（中心 700,1489）
            (1100, 1478, 200, 22),  // 门（中心 1200,1489）
        };
        int idx = BreachLogic.NearestRectByEdge(1200, 1540, rects, out double dist);
        Assert.Equal(1, idx);              // 选门
        Assert.Equal(40, dist, 1);         // 1540 到下边缘 1500 = 40
    }

    [Fact]
    public void 择结构_长墙边缘比另一小矩形中心更近时选长墙()
    {
        // 验证「边缘距离」确实生效：长墙边缘就在脚下，另一小矩形整体更远。
        var rects = new List<(double, double, double, double)>
        {
            (0, 0, 1000, 20),    // 长墙，边缘距点 (500,40) 仅 20
            (490, 200, 20, 20),  // 小矩形，中心 (500,210) 距点 170
        };
        int idx = BreachLogic.NearestRectByEdge(500, 40, rects, out double dist);
        Assert.Equal(0, idx);
        Assert.Equal(20, dist, 3);
    }

    [Fact]
    public void 空列表_返回负一()
    {
        int idx = BreachLogic.NearestRectByEdge(0, 0,
            new List<(double, double, double, double)>(), out double dist);
        Assert.Equal(-1, idx);
        Assert.True(double.IsPositiveInfinity(dist));
    }
}
