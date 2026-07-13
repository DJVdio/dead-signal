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

        // 桶按工具需求的**首次出现顺序**排列：无工具 → 锯片 → 烧杯 → 卡尺。
        // 断言按**语义**写（分桶正确性），不锁死每桶的配方条数——配方表在持续增补（新武器/弹药/装备各批次都在加），
        // 硬编码计数会让任何一条新配方无端打红这个跟它无关的测试。
        Assert.Equal(4, groups.Count);

        Assert.Empty(groups[0].Tools);
        Assert.Equal(new[] { ToolSlot.SawBlade }, groups[1].Tools);
        Assert.Equal(new[] { ToolSlot.Beaker }, groups[2].Tools);
        Assert.Equal(new[] { ToolSlot.Calipers }, groups[3].Tools);

        // 每个桶里的配方，其工具需求必须与桶头一致（分桶的本质）。
        foreach (RecipeToolGroup g in groups)
        {
            Assert.All(g.Recipes, r => Assert.Equal(g.Tools.OrderBy(t => t), r.RequiredTools.OrderBy(t => t)));
        }

        // 全部配方恰好被分掉一次（不漏不重）。
        Assert.Equal(RecipeBook.All.Count, groups.Sum(g => g.Recipes.Count));

        // 各桶的代表配方（锚点，防止分桶逻辑把东西丢错桶）。
        Assert.Contains(groups[0].Recipes, r => r.Id == "bone_knife");   // 骨刀：无工具
        Assert.Contains(groups[1].Recipes, r => r.Id == "chair");        // 木椅：锯片
        Assert.Contains(groups[2].Recipes, r => r.Id == "gunpowder");    // 火药：烧杯
        Assert.Contains(groups[3].Recipes, r => r.Id == "handmade_bow"); // 自制弓：卡尺
        Assert.Contains(groups[3].Recipes, r => r.Id == "improvised_shotgun"); // 自制霰弹枪：卡尺精工
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
