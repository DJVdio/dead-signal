using System.Collections.Generic;
using System.Numerics;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 视野场纯逻辑（<see cref="VisionField"/>）单测：点可见并集判定 + 网格遮暗位图。遮挡谓词以委托注入（模拟 raycast），
/// 不依赖 Godot。视距/半角判定本体在 <see cref="VisionLogic"/> 已另测，这里聚焦"多观察者并集 / 遮挡谓词调用 / 网格产出"。
/// </summary>
public class VisionFieldTests
{
    // 无遮挡谓词（空场）：任何两点间畅通。
    private static bool NeverOccluded(Vector2 a, Vector2 b) => false;

    private static VisionField.Viewer Viewer(Vector2 pos, Vector2 facing, float range = 300f, float halfAngle = 60f)
        => new(pos, facing, new VisionLogic.VisionCone(range, halfAngle));

    [Fact]
    public void IsPointVisible_NoViewers_False()
    {
        Assert.False(VisionField.IsPointVisible(new List<VisionField.Viewer>(), new Vector2(10, 0), NeverOccluded));
    }

    [Fact]
    public void IsPointVisible_InFrontWithinCone_True()
    {
        var viewers = new List<VisionField.Viewer> { Viewer(Vector2.Zero, new Vector2(1, 0), range: 100f, halfAngle: 60f) };
        Assert.True(VisionField.IsPointVisible(viewers, new Vector2(50, 0), NeverOccluded));
    }

    [Fact]
    public void IsPointVisible_BehindViewer_False()
    {
        var viewers = new List<VisionField.Viewer> { Viewer(Vector2.Zero, new Vector2(1, 0), range: 100f, halfAngle: 60f) };
        // 目标在观察者正后方 → 超出 ±60° 锥。
        Assert.False(VisionField.IsPointVisible(viewers, new Vector2(-50, 0), NeverOccluded));
    }

    [Fact]
    public void IsPointVisible_BeyondRange_False()
    {
        var viewers = new List<VisionField.Viewer> { Viewer(Vector2.Zero, new Vector2(1, 0), range: 100f, halfAngle: 60f) };
        Assert.False(VisionField.IsPointVisible(viewers, new Vector2(150, 0), NeverOccluded));
    }

    [Fact]
    public void IsPointVisible_Occluded_False()
    {
        var viewers = new List<VisionField.Viewer> { Viewer(Vector2.Zero, new Vector2(1, 0), range: 100f, halfAngle: 60f) };
        // 落在锥内但遮挡谓词判"被挡" → 不可见。
        Assert.False(VisionField.IsPointVisible(viewers, new Vector2(50, 0), (_, _) => true));
    }

    [Fact]
    public void IsPointVisible_UnionOfTwoViewers_SecondSees()
    {
        var viewers = new List<VisionField.Viewer>
        {
            Viewer(Vector2.Zero, new Vector2(1, 0), range: 100f, halfAngle: 60f),   // 朝 +X，看不到 -X 侧
            Viewer(new Vector2(-100, 0), new Vector2(-1, 0), range: 100f, halfAngle: 60f), // 朝 -X，能看到自己前方
        };
        // 该点在第一位观察者身后（不可见），但落在第二位观察者的锥内（可见）→ 并集为可见。
        Assert.True(VisionField.IsPointVisible(viewers, new Vector2(-150, 0), NeverOccluded));
    }

    [Fact]
    public void IsPointVisible_OcclusionOnlyQueriedWhenInCone()
    {
        var viewers = new List<VisionField.Viewer> { Viewer(Vector2.Zero, new Vector2(1, 0), range: 100f, halfAngle: 60f) };
        int occlusionCalls = 0;
        bool Counting(Vector2 a, Vector2 b) { occlusionCalls++; return false; }

        // 点在锥外（身后）：廉价锥检先失败 → 不应触发遮挡查询（性能约定）。
        VisionField.IsPointVisible(viewers, new Vector2(-50, 0), Counting);
        Assert.Equal(0, occlusionCalls);

        // 点在锥内：应触发恰好一次遮挡查询。
        VisionField.IsPointVisible(viewers, new Vector2(50, 0), Counting);
        Assert.Equal(1, occlusionCalls);
    }

    [Fact]
    public void ComputeDarkCells_NoViewers_AllDark()
    {
        bool[] dark = VisionField.ComputeDarkCells(
            Vector2.Zero, new Vector2(96, 96), cellSize: 48f,
            new List<VisionField.Viewer>(), NeverOccluded, out int cols, out int rows);

        Assert.Equal(2, cols);
        Assert.Equal(2, rows);
        Assert.All(dark, Assert.True);
    }

    [Fact]
    public void ComputeDarkCells_ViewerLightsNearbyCells()
    {
        // 观察者在原点朝 +X，大视距全角；覆盖 4×4 格（cellSize 48，包围盒 192×192）。
        var viewers = new List<VisionField.Viewer> { Viewer(new Vector2(24, 96), new Vector2(1, 0), range: 300f, halfAngle: 80f) };
        bool[] dark = VisionField.ComputeDarkCells(
            Vector2.Zero, new Vector2(192, 192), cellSize: 48f,
            viewers, NeverOccluded, out int cols, out int rows);

        // 格心 = ((c+0.5)*48, (r+0.5)*48)。c=1,r=1 → (72,72)：在观察者(24,96)的 +X 前方近处（偏角约 27°<80°）→ 点亮（非暗）。
        Assert.False(dark[1 * cols + 1]);
        // c=0,r=0 → (24,24)：相对观察者(24,96) 向量 (0,-72)，正上方偏离朝向 90°>80° 半角 → 出锥 → 暗。
        Assert.True(dark[0 * cols + 0]);
    }
}
