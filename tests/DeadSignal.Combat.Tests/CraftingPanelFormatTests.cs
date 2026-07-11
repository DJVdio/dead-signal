using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 制作面板纯展示逻辑（CraftingPanelFormat）单测：配方按工具分组、工具需求文案/满足判定、库存材料计数聚合。

public class CraftingPanelFormatTests
{
    [Fact]
    public void GroupByTool_BucketsSameToolRequirementTogether_PreservesOrder()
    {
        IReadOnlyList<RecipeToolGroup> groups = CraftingPanelFormat.GroupByTool(RecipeBook.All);

        // RecipeBook 声明顺序：骨刀(无)、粗布背心(无)、板凳(无)、木椅(锯片)、火药(烧杯)、鞣制药水(烧杯)、自制弓(卡尺)、火把(无)、
        //   狗装备五件套(布/皮/口袋狗衣·铁皮/铁丝头甲，皆无工具)。
        // 桶按首次出现顺序：无 → 锯片 → 烧杯 → 卡尺，共 4 组（火把 + 狗装备五件套无工具，归入"无"桶末尾）。
        Assert.Equal(4, groups.Count);
        Assert.Empty(groups[0].Tools);
        Assert.Equal(9, groups[0].Recipes.Count);            // 骨刀 + 粗布背心 + 板凳 + 火把 + 狗装备五件套(5)
        Assert.Equal(new[] { ToolSlot.SawBlade }, groups[1].Tools);
        Assert.Single(groups[1].Recipes);                    // 木椅
        Assert.Equal(new[] { ToolSlot.Beaker }, groups[2].Tools);
        Assert.Equal(2, groups[2].Recipes.Count);            // 火药 + 鞣制药水
        Assert.Equal(new[] { ToolSlot.Calipers }, groups[3].Tools);
        Assert.Single(groups[3].Recipes);                    // 自制弓
    }

    [Fact]
    public void ToolRequirementLabel_NoTool_And_SingleTool_And_MultiTool()
    {
        Assert.Equal("无需工具", CraftingPanelFormat.ToolRequirementLabel(new List<ToolSlot>()));
        Assert.Equal("需卡尺", CraftingPanelFormat.ToolRequirementLabel(new[] { ToolSlot.Calipers }));
        Assert.Equal("需卡尺、烧杯",
            CraftingPanelFormat.ToolRequirementLabel(new[] { ToolSlot.Calipers, ToolSlot.Beaker }));
    }

    [Fact]
    public void ToolsInstalled_TrueOnlyWhenAllPresent()
    {
        IReadOnlySet<ToolSlot> installed = new HashSet<ToolSlot> { ToolSlot.SawBlade };
        Assert.True(CraftingPanelFormat.ToolsInstalled(new List<ToolSlot>(), installed));       // 无需求恒真
        Assert.True(CraftingPanelFormat.ToolsInstalled(new[] { ToolSlot.SawBlade }, installed));
        Assert.False(CraftingPanelFormat.ToolsInstalled(new[] { ToolSlot.Beaker }, installed));
        Assert.False(CraftingPanelFormat.ToolsInstalled(
            new[] { ToolSlot.SawBlade, ToolSlot.Beaker }, installed));                          // 缺一即假
    }

    [Fact]
    public void MaterialCount_SumsStacksByRefKey_IgnoresOtherKeysAndCategories()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Material("wood", "木料", 4));
        inv.Add(Item.Material("wood", "木料", 3));   // 同 key 多堆求和
        inv.Add(Item.Material("stone", "石料", 5));  // 别的 key 不计
        inv.Add(Item.Weapon("手枪"));                 // 非材料不计

        Assert.Equal(7, CraftingPanelFormat.MaterialCount(inv, "wood"));
        Assert.Equal(5, CraftingPanelFormat.MaterialCount(inv, "stone"));
        Assert.Equal(0, CraftingPanelFormat.MaterialCount(inv, "nails"));
    }

    // ---- 工时制面板展示 ----

    [Theory]
    [InlineData(0, "即时")]
    [InlineData(-5, "即时")]
    [InlineData(45, "45 分")]
    [InlineData(60, "1 小时")]
    [InlineData(90, "1 小时 30 分")]
    [InlineData(150, "2 小时 30 分")]
    public void FormatWorkDuration_HumanReadable(int minutes, string expected)
        => Assert.Equal(expected, CraftingPanelFormat.FormatWorkDuration(minutes));

    [Fact]
    public void WorkProgressLabel_Null_ReturnsNull()
        => Assert.Null(CraftingPanelFormat.WorkProgressLabel(null));

    [Fact]
    public void WorkProgressLabel_InProgress_ShowsRemainingAndPercent()
    {
        var job = new CraftingJob("bench", 60);
        job.Advance(15, canWork: true); // 25%，剩 45 分
        Assert.Equal("制作中 · 剩 45 分（25%）", CraftingPanelFormat.WorkProgressLabel(job));
    }

    [Fact]
    public void WorkProgressLabel_Complete_ShowsReadyToCollect()
    {
        var job = new CraftingJob("bench", 30);
        job.Advance(30, canWork: true);
        Assert.Equal("已完成 · 待取出", CraftingPanelFormat.WorkProgressLabel(job));
    }
}
