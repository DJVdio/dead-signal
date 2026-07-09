using System.Collections.Generic;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 幸存者卡牌栏纯逻辑单测：Id→头像索引/文件名稳定映射 + Id→稳定占位色。
/// UI 渲染（SurvivorCardBar）以构建通过为准，不在此测。
/// </summary>
public class SurvivorCardVisualsTests
{
    [Fact]
    public void PortraitFiles_HasExactlyPortraitCount()
    {
        Assert.Equal(SurvivorCardVisuals.PortraitCount, SurvivorCardVisuals.PortraitFiles.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(999)]
    [InlineData(-1)]
    [InlineData(-13)]
    public void PortraitIndex_AlwaysInRange(int id)
    {
        int idx = SurvivorCardVisuals.PortraitIndexForId(id);
        Assert.InRange(idx, 0, SurvivorCardVisuals.PortraitCount - 1);
    }

    [Fact]
    public void PortraitIndex_IsStablePerId()
    {
        Assert.Equal(
            SurvivorCardVisuals.PortraitIndexForId(7),
            SurvivorCardVisuals.PortraitIndexForId(7));
        // 稳定与创建顺序无关：Id 7 恒定映射同一索引。
        Assert.Equal(7 % SurvivorCardVisuals.PortraitCount, SurvivorCardVisuals.PortraitIndexForId(7));
    }

    [Fact]
    public void PortraitIndex_WrapsModulo()
    {
        Assert.Equal(
            SurvivorCardVisuals.PortraitIndexForId(0),
            SurvivorCardVisuals.PortraitIndexForId(SurvivorCardVisuals.PortraitCount));
    }

    [Fact]
    public void FirstThirteenIds_CoverAllPortraits()
    {
        var seen = new HashSet<int>();
        for (int id = 0; id < SurvivorCardVisuals.PortraitCount; id++)
        {
            seen.Add(SurvivorCardVisuals.PortraitIndexForId(id));
        }
        Assert.Equal(SurvivorCardVisuals.PortraitCount, seen.Count); // 前 13 个 Id 恰好覆盖全部头像
    }

    [Fact]
    public void PortraitFileForId_ReturnsListedFile()
    {
        string file = SurvivorCardVisuals.PortraitFileForId(3);
        Assert.Contains(file, SurvivorCardVisuals.PortraitFiles);
        Assert.Equal(SurvivorCardVisuals.PortraitFiles[3 % SurvivorCardVisuals.PortraitCount], file);
    }

    [Fact]
    public void StableColor_IsDeterministic()
    {
        var a = SurvivorCardVisuals.StableColorForId(42);
        var b = SurvivorCardVisuals.StableColorForId(42);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(-7)]
    public void StableColor_ChannelsInUnitRange(int id)
    {
        var (r, g, b) = SurvivorCardVisuals.StableColorForId(id);
        Assert.InRange(r, 0f, 1f);
        Assert.InRange(g, 0f, 1f);
        Assert.InRange(b, 0f, 1f);
    }

    [Fact]
    public void StableColor_DistinctForNeighbouringIds()
    {
        // 黄金角散布：相邻 Id 色相拉开，不应撞同色。
        Assert.NotEqual(SurvivorCardVisuals.StableColorForId(0), SurvivorCardVisuals.StableColorForId(1));
        Assert.NotEqual(SurvivorCardVisuals.StableColorForId(1), SurvivorCardVisuals.StableColorForId(2));
    }
}
