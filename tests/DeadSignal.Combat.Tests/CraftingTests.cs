using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 配方系统核心（Workbench 工具槽 + RecipeData + CraftingLogic 判定/结算）的纯逻辑单测。
// 覆盖：工具门槛、技能门槛、书门槛、材料不足、全满足、缺一项各分支，以及 Resolve 产出契约。

public class WorkbenchTests
{
    [Fact]
    public void FreshBench_HasNoTools()
    {
        var bench = new WorkbenchState();
        Assert.False(bench.HasTool(ToolSlot.Calipers));
        Assert.False(bench.HasTool(ToolSlot.SawBlade));
        Assert.False(bench.HasTool(ToolSlot.Beaker));
        Assert.Empty(bench.InstalledTools);
    }

    [Fact]
    public void InstallTool_IsIdempotentAndReflected()
    {
        var bench = new WorkbenchState();
        Assert.True(bench.InstallTool(ToolSlot.SawBlade));
        Assert.False(bench.InstallTool(ToolSlot.SawBlade)); // 幂等：第二次不再"发生装入"
        Assert.True(bench.HasTool(ToolSlot.SawBlade));
        Assert.Contains(ToolSlot.SawBlade, bench.InstalledTools);
    }

    [Fact]
    public void RemoveTool_Uninstalls()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        Assert.True(bench.RemoveTool(ToolSlot.Beaker));
        Assert.False(bench.RemoveTool(ToolSlot.Beaker)); // 幂等
        Assert.False(bench.HasTool(ToolSlot.Beaker));
    }
}

public class RecipeBookTests
{
    [Fact]
    public void Draft_CoversTheSixExamples()
    {
        var ids = RecipeBook.All.Select(r => r.Id).ToHashSet();
        foreach (var expected in new[] { "bone_knife", "cloth_vest", "chair", "gunpowder", "tanning_solution", "handmade_bow" })
        {
            Assert.Contains(expected, ids);
        }
    }

    [Fact]
    public void BoneKnife_UnlockedByWildernessBook_NoTool()
    {
        var r = RecipeBook.Find("bone_knife")!;
        Assert.Contains(RecipeBook.WildernessSurvivalGuideBookId, r.RequiredBookIds);
        Assert.Empty(r.RequiredTools);
    }

    [Fact]
    public void ClothVest_RequiresNoviceTextile()
    {
        var r = RecipeBook.Find("cloth_vest")!;
        Assert.Contains(r.RequiredSkills, s => s.Skill == SkillType.Textile && s.MinLevel == SkillLevel.Novice);
    }

    [Fact]
    public void Chair_RequiresSawBlade()
    {
        Assert.Contains(ToolSlot.SawBlade, RecipeBook.Find("chair")!.RequiredTools);
    }

    [Fact]
    public void Gunpowder_RequiresBeaker()
    {
        Assert.Contains(ToolSlot.Beaker, RecipeBook.Find("gunpowder")!.RequiredTools);
    }

    [Fact]
    public void HandmadeBow_RequiresCalipers()
    {
        Assert.Contains(ToolSlot.Calipers, RecipeBook.Find("handmade_bow")!.RequiredTools);
    }

    [Fact]
    public void Find_Unknown_ReturnsNull()
    {
        Assert.Null(RecipeBook.Find("no_such_recipe"));
    }
}

public class CraftingLogicTests
{
    // 一张全门槛俱全的合成配方，逐项拆分测各分支。
    private static readonly RecipeData FullGate = new(
        Id: "test_full",
        DisplayName: "测试全门槛物",
        Category: RecipeCategory.Chemistry,
        OutputKey: "test_out",
        OutputQuantity: 2,
        MaterialCosts: new Dictionary<string, int> { ["wood"] = 3, ["cloth"] = 1 },
        RequiredTools: new HashSet<ToolSlot> { ToolSlot.Beaker },
        RequiredBookIds: new List<string> { "test_book" },
        RequiredSkills: new List<SkillRequirement> { new(SkillType.Chemistry, SkillLevel.Novice) },
        ExperienceSkill: SkillType.Chemistry,
        ExperienceReward: 40);

    private static CraftAvailability Eval(
        RecipeData recipe,
        Dictionary<string, int>? mats = null,
        Dictionary<SkillType, SkillLevel>? skills = null,
        HashSet<string>? readBooks = null,
        HashSet<ToolSlot>? tools = null)
    {
        mats ??= new Dictionary<string, int> { ["wood"] = 10, ["cloth"] = 10 };
        skills ??= new Dictionary<SkillType, SkillLevel> { [SkillType.Chemistry] = SkillLevel.Adept };
        readBooks ??= new HashSet<string> { "test_book" };
        tools ??= new HashSet<ToolSlot> { ToolSlot.Beaker };
        return CraftingLogic.CanCraft(
            recipe,
            k => mats.TryGetValue(k, out var v) ? v : 0,
            s => skills.TryGetValue(s, out var l) ? l : SkillLevel.None,
            b => readBooks.Contains(b),
            tools);
    }

    [Fact]
    public void AllSatisfied_CanCraft()
    {
        var a = Eval(FullGate);
        Assert.True(a.CanCraft);
        Assert.Empty(a.Blocks);
    }

    [Fact]
    public void MissingTool_Blocks()
    {
        var a = Eval(FullGate, tools: new HashSet<ToolSlot>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
    }

    [Fact]
    public void MissingSkill_Blocks()
    {
        var a = Eval(FullGate, skills: new Dictionary<SkillType, SkillLevel> { [SkillType.Chemistry] = SkillLevel.None });
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.MissingSkill);
    }

    [Fact]
    public void UnreadBook_Blocks()
    {
        var a = Eval(FullGate, readBooks: new HashSet<string>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void InsufficientMaterial_Blocks()
    {
        var a = Eval(FullGate, mats: new Dictionary<string, int> { ["wood"] = 1, ["cloth"] = 1 });
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial && b.Key == "wood");
    }

    [Fact]
    public void MissingMaterialEntry_TreatedAsZero()
    {
        var a = Eval(FullGate, mats: new Dictionary<string, int>()); // 完全没登记 wood/cloth
        Assert.False(a.CanCraft);
        Assert.Equal(2, a.Blocks.Count(b => b.Reason == CraftBlockReason.InsufficientMaterial));
    }

    [Fact]
    public void AllGatesFail_ListsEveryReason()
    {
        var a = Eval(
            FullGate,
            mats: new Dictionary<string, int>(),
            skills: new Dictionary<SkillType, SkillLevel>(),
            readBooks: new HashSet<string>(),
            tools: new HashSet<ToolSlot>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.MissingSkill);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial);
    }

    [Fact]
    public void HigherSkillThanRequired_Passes()
    {
        // 门槛初级、制作者中级 → 过。
        var a = Eval(FullGate, skills: new Dictionary<SkillType, SkillLevel> { [SkillType.Chemistry] = SkillLevel.Adept });
        Assert.True(a.CanCraft);
    }

    [Fact]
    public void Resolve_ProducesNegativeMaterialDeltasAndOutputAndExp()
    {
        var res = CraftingLogic.Resolve(FullGate);
        Assert.Equal(-3, res.MaterialDeltas["wood"]);
        Assert.Equal(-1, res.MaterialDeltas["cloth"]);
        Assert.Equal("test_out", res.OutputKey);
        Assert.Equal(2, res.OutputQuantity);
        Assert.Equal(SkillType.Chemistry, res.ExperienceSkill);
        Assert.Equal(40, res.ExperienceReward);
    }

    [Fact]
    public void Resolve_BatchMultipliesEverything()
    {
        var res = CraftingLogic.Resolve(FullGate, times: 3);
        Assert.Equal(-9, res.MaterialDeltas["wood"]);
        Assert.Equal(6, res.OutputQuantity);
        Assert.Equal(120, res.ExperienceReward);
    }

    [Fact]
    public void Resolve_ClampsNonPositiveTimesToOne()
    {
        var res = CraftingLogic.Resolve(FullGate, times: 0);
        Assert.Equal(-3, res.MaterialDeltas["wood"]);
        Assert.Equal(2, res.OutputQuantity);
    }

    [Fact]
    public void RealRecipe_BoneKnife_GatedByBookOnly()
    {
        var r = RecipeBook.Find("bone_knife")!;
        // 材料够、书未读 → 只卡书。
        var blocked = CraftingLogic.CanCraft(
            r,
            _ => 99,
            _ => SkillLevel.Expert,
            _ => false,
            new HashSet<ToolSlot>());
        Assert.False(blocked.CanCraft);
        Assert.All(blocked.Blocks, b => Assert.Equal(CraftBlockReason.UnreadBook, b.Reason));

        // 读完书 → 过（无工具/技能门槛）。
        var ok = CraftingLogic.CanCraft(
            r,
            _ => 99,
            _ => SkillLevel.None,
            _ => true,
            new HashSet<ToolSlot>());
        Assert.True(ok.CanCraft);
    }
}
