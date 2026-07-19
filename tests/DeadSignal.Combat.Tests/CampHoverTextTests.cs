using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 场上结构/家具的 hover 文案消费护栏。
/// 结构与家具目录早已登记了 authored 简介；这组测试先把「目录文案必须真正进入玩家可见的一行」钉住，
/// 防止以后只改数据却又回到空白默认提示。
/// </summary>
public sealed class CampHoverTextTests
{
    [Fact]
    public void Furniture_AppendsAuthoredDescriptionToBaseHint()
    {
        string text = CampHoverText.AppendFurnitureDescription("工作台", "工作台 · 右键前往");

        Assert.Contains("工作台 · 右键前往", text);
        Assert.Contains("所有\"造\"出来的东西", text);
    }

    [Fact]
    public void Furniture_UnknownKey_KeepsBaseHintUntouched()
    {
        Assert.Equal("未知", CampHoverText.AppendFurnitureDescription("不是家具", "未知"));
    }

    [Fact]
    public void Structure_ReportsTierBlurbAndDurability()
    {
        string text = CampHoverText.Structure("北墙", StructureTier.FenceBasic, hp: 75, destroyed: false);

        Assert.Contains("北墙", text);
        Assert.Contains("几根木桩钉起来的围栏", text);
        Assert.Contains("耐久 75/150", text);
    }

    [Fact]
    public void DestroyedStructure_ReportsGapInsteadOfFakeDurability()
    {
        string text = CampHoverText.Structure("南墙", StructureTier.FenceBasic, hp: 0, destroyed: true);

        Assert.Contains("已毁", text);
        Assert.DoesNotContain("耐久", text);
    }
}
