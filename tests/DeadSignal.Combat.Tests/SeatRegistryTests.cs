using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 座位家具纯逻辑：座位登记 + 就近取空座（标记占用）+ 释放。
/// 座位的实体建造/寻路走到坐下在 Godot 层，不在此测。
/// </summary>
public sealed class SeatRegistryTests
{
    [Fact]
    public void 空登记_取空座返回负一()
    {
        var reg = new SeatRegistry();
        Assert.Equal(0, reg.Count);
        Assert.Equal(0, reg.FreeCount);
        Assert.Equal(-1, reg.ClaimNearest(0, 0));
    }

    [Fact]
    public void 就近取到最近的空座并标记占用()
    {
        var reg = new SeatRegistry();
        reg.Add(0, 0);      // 0：远
        int near = reg.Add(100, 0); // 1：近
        reg.Add(500, 0);    // 2：更远

        int claimed = reg.ClaimNearest(120, 0);
        Assert.Equal(near, claimed);
        Assert.True(reg.IsOccupied(near));
        Assert.Equal(2, reg.FreeCount);
    }

    [Fact]
    public void 同点连取_跳过已占取次近()
    {
        var reg = new SeatRegistry();
        int a = reg.Add(100, 0);
        int b = reg.Add(200, 0);

        Assert.Equal(a, reg.ClaimNearest(0, 0));
        Assert.Equal(b, reg.ClaimNearest(0, 0)); // a 已占 → 取 b
        Assert.Equal(-1, reg.ClaimNearest(0, 0)); // 全占 → 无座
    }

    [Fact]
    public void 释放后可再次取用()
    {
        var reg = new SeatRegistry();
        int a = reg.Add(50, 50);

        Assert.Equal(a, reg.ClaimNearest(0, 0));
        Assert.Equal(-1, reg.ClaimNearest(0, 0));

        reg.Release(a);
        Assert.False(reg.IsOccupied(a));
        Assert.Equal(1, reg.FreeCount);
        Assert.Equal(a, reg.ClaimNearest(0, 0)); // 释放后又能取到
    }

    [Fact]
    public void 坐标可回读_越界安全()
    {
        var reg = new SeatRegistry();
        int a = reg.Add(320, 480);

        (double x, double y) = reg.PositionOf(a);
        Assert.Equal(320, x, 3);
        Assert.Equal(480, y, 3);

        // 越界：读坐标回退 (0,0)、视作已占、释放幂等不抛。
        Assert.Equal((0d, 0d), reg.PositionOf(99));
        Assert.True(reg.IsOccupied(99));
        reg.Release(99);
        reg.Release(a); // 重复释放幂等
        reg.Release(a);
        Assert.False(reg.IsOccupied(a));
    }
}
