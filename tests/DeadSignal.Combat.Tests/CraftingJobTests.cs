using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 工时制在制品（CraftingJob）纯逻辑单测：
//   进度推进（人在工作台且生产相位才推进）、中断/继续不丢进度、封顶完工、零工时即完、批量放大总工时。
public class CraftingJobTests
{
    [Fact]
    public void NewJob_StartsAtZeroProgress_NotComplete()
    {
        var job = new CraftingJob("bench", totalWorkMinutes: 60);
        Assert.Equal(0, job.ElapsedWorkMinutes);
        Assert.Equal(60, job.RemainingWorkMinutes);
        Assert.Equal(0f, job.Progress);
        Assert.False(job.IsComplete);
        Assert.Equal(1, job.Times);
    }

    [Fact]
    public void Advance_WithWorker_AccumulatesAndReportsAppliedMinutes()
    {
        var job = new CraftingJob("bench", 60);
        int applied = job.Advance(25, canWork: true);
        Assert.Equal(25, applied);
        Assert.Equal(25, job.ElapsedWorkMinutes);
        Assert.Equal(35, job.RemainingWorkMinutes);
        Assert.InRange(job.Progress, 0.41f, 0.42f);
        Assert.False(job.IsComplete);
    }

    [Fact]
    public void Advance_NoWorker_DoesNotProgress()
    {
        var job = new CraftingJob("bench", 60);
        int applied = job.Advance(30, canWork: false);
        Assert.Equal(0, applied);
        Assert.Equal(0, job.ElapsedWorkMinutes);
    }

    [Fact]
    public void Advance_InterruptThenResume_KeepsProgress()
    {
        var job = new CraftingJob("bench", 60);
        job.Advance(20, canWork: true);   // 干了 20
        job.Advance(999, canWork: false); // 被袭营拉走—暂停，不丢进度
        Assert.Equal(20, job.ElapsedWorkMinutes);
        job.Advance(15, canWork: true);   // 回来继续
        Assert.Equal(35, job.ElapsedWorkMinutes);
    }

    [Fact]
    public void Advance_CapsAtTotal_ThenComplete()
    {
        var job = new CraftingJob("bench", 60);
        int applied = job.Advance(100, canWork: true); // 超额只补到封顶
        Assert.Equal(60, applied);
        Assert.Equal(60, job.ElapsedWorkMinutes);
        Assert.True(job.IsComplete);
        Assert.Equal(1f, job.Progress);
        Assert.Equal(0, job.RemainingWorkMinutes);
    }

    [Fact]
    public void Advance_AfterComplete_IsNoOp()
    {
        var job = new CraftingJob("bench", 30);
        job.Advance(30, canWork: true);
        Assert.True(job.IsComplete);
        Assert.Equal(0, job.Advance(10, canWork: true));
        Assert.Equal(30, job.ElapsedWorkMinutes);
    }

    [Fact]
    public void Advance_NonPositiveMinutes_IsNoOp()
    {
        var job = new CraftingJob("bench", 30);
        Assert.Equal(0, job.Advance(0, canWork: true));
        Assert.Equal(0, job.Advance(-5, canWork: true));
        Assert.Equal(0, job.ElapsedWorkMinutes);
    }

    [Fact]
    public void ZeroTotalWork_IsImmediatelyComplete()
    {
        var job = new CraftingJob("bench", totalWorkMinutes: 0);
        Assert.True(job.IsComplete);
        Assert.Equal(1f, job.Progress);
    }

    [Fact]
    public void Times_ClampedToAtLeastOne()
    {
        Assert.Equal(1, new CraftingJob("bench", 60, times: 0).Times);
        Assert.Equal(1, new CraftingJob("bench", 60, times: -3).Times);
        Assert.Equal(3, new CraftingJob("bench", 60, times: 3).Times);
    }

    [Fact]
    public void EmptyRecipeId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CraftingJob("", 60));
        Assert.Throws<ArgumentException>(() => new CraftingJob(null!, 60));
    }
}

// 工时制开工/完工执行（CraftingService.StartJob / CompleteJob）单测：
//   开工即扣材料(锁定)+返回在制品(未产出)、门槛挡则不动库存、完工产出入库、全生命周期。
public class CraftingWorktimeServiceTests
{
    // 需 Beaker + 读 test_book，材料 wood×3/cloth×1，产出 gunpowder×2，工时 40。
    private static RecipeData MakeRecipe(int work = 40) => new(
        Id: "svc_work",
        DisplayName: "测试物",
        Category: RecipeCategory.Chemistry,
        OutputKey: "gunpowder",
        OutputQuantity: 2,
        MaterialCosts: new Dictionary<string, int> { ["wood"] = 3, ["cloth"] = 1 },
        RequiredTools: new HashSet<ToolSlot> { ToolSlot.Beaker },
        RequiredBookIds: new List<string> { "test_book" },
        WorkMinutes: work);

    private static (WorkbenchState bench, InventoryStore inv) Ready()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        var inv = new InventoryStore();
        inv.Add(Item.Material("wood", "木料", 4));
        inv.Add(Item.Material("cloth", "布", 2));
        return (bench, inv);
    }

    [Fact]
    public void StartJob_Success_DeductsMaterialsUpFront_NoOutputYet()
    {
        var (bench, inv) = Ready();
        CraftStartResult r = CraftingService.StartJob(MakeRecipe(40), _ => true, bench, inv);

        Assert.True(r.Success);
        Assert.NotNull(r.Job);
        // 开工即扣材料（锁定防重复下单）。
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "wood"));  // 4→1
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "cloth")); // 2→1
        // 尚未产出。
        Assert.Equal(0, CraftingService.MaterialTotal(inv.Items, "gunpowder"));
        Assert.Equal("svc_work", r.Job!.RecipeId);
        Assert.Equal(40, r.Job.TotalWorkMinutes);
    }

    [Fact]
    public void StartJob_BatchMultipliesWork_AndMaterial()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        var inv = new InventoryStore();
        inv.Add(Item.Material("wood", "木料", 6));
        inv.Add(Item.Material("cloth", "布", 2));
        CraftStartResult r = CraftingService.StartJob(MakeRecipe(40), _ => true, bench, inv, times: 2);
        Assert.True(r.Success);
        Assert.Equal(80, r.Job!.TotalWorkMinutes); // 工时线性放大
        Assert.Equal(0, CraftingService.MaterialTotal(inv.Items, "wood")); // 6→0
    }

    [Fact]
    public void StartJob_Blocked_MissingTool_LeavesInventoryUntouched()
    {
        var (_, inv) = Ready();
        int before = inv.Count;
        CraftStartResult r = CraftingService.StartJob(MakeRecipe(), _ => true, new WorkbenchState(), inv);
        Assert.False(r.Success);
        Assert.Null(r.Job);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
        Assert.Equal(before, inv.Count); // 未扣材料
    }

    [Fact]
    public void StartJob_Blocked_InsufficientMaterial_NoDeduction()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        var inv = new InventoryStore();
        inv.Add(Item.Material("wood", "木料", 1)); // 不够 3
        CraftStartResult r = CraftingService.StartJob(MakeRecipe(), _ => true, bench, inv);
        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial);
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "wood")); // 原封不动
    }

    [Fact]
    public void CompleteJob_ProducesOutputIntoInventory()
    {
        var (bench, inv) = Ready();
        CraftStartResult start = CraftingService.StartJob(MakeRecipe(40), _ => true, bench, inv);
        IReadOnlyList<Item> produced = CraftingService.CompleteJob(start.Job!, MakeRecipe(40), inv);
        Assert.Contains(produced, i => i.RefKey == "gunpowder" && i.MaterialQuantity == 2);
        Assert.Equal(2, CraftingService.MaterialTotal(inv.Items, "gunpowder"));
    }

    [Fact]
    public void FullLifecycle_Start_Advance_Complete()
    {
        var (bench, inv) = Ready();
        RecipeData recipe = MakeRecipe(40);
        CraftStartResult start = CraftingService.StartJob(recipe, _ => true, bench, inv);
        CraftingJob job = start.Job!;

        // 夜里人在工作台，逐段推进到完工。
        job.Advance(30, canWork: true);
        Assert.False(job.IsComplete);
        job.Advance(30, canWork: true); // 累计 60 ≥ 40 封顶
        Assert.True(job.IsComplete);

        CraftingService.CompleteJob(job, recipe, inv);
        Assert.Equal(2, CraftingService.MaterialTotal(inv.Items, "gunpowder"));
    }

    [Fact]
    public void CompleteJob_UsesCustomOutputFactory()
    {
        var (bench, inv) = Ready();
        RecipeData recipe = MakeRecipe(40) with { OutputKey = "cloth_vest", OutputQuantity = 1 };
        CraftStartResult start = CraftingService.StartJob(recipe, _ => true, bench, inv);
        IReadOnlyList<Item> produced = CraftingService.CompleteJob(
            start.Job!, recipe, inv, (key, qty) => Enumerable.Range(0, qty).Select(_ => Item.Armor(key)));
        Assert.Single(produced);
        Assert.Equal(ItemCategory.Armor, produced[0].Category);
    }
}
