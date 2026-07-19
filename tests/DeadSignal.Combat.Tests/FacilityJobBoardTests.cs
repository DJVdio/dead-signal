using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class FacilityJobBoardTests
{
    [Fact]
    public void 不同设施各有独立槽_可以同时生产()
    {
        var board = new FacilityJobBoard();

        Assert.True(board.TryStart(FacilityJobKeys.MainWorkbench, Job("chair"), 1, true).Started);
        Assert.True(board.TryStart(FacilityJobKeys.MainCookStation, Job("cook:2"), 2, true).Started);
        Assert.True(board.TryStart(FacilityJobKeys.MainModBench, Job("weaponmod:pistol|scope"), 3, true).Started);

        Assert.Equal(3, board.Count);
    }

    [Fact]
    public void 同设施拒绝第二单_同一人也不能跨槽兼任()
    {
        var board = new FacilityJobBoard();
        Assert.True(board.TryStart(FacilityJobKeys.MainWorkbench, Job("chair"), 1, true).Started);

        Assert.Equal(FacilityJobStartFailure.FacilityBusy,
            board.TryStart(FacilityJobKeys.MainWorkbench, Job("table"), 2, true).Failure);
        Assert.Equal(FacilityJobStartFailure.WorkerAlreadyAssigned,
            board.TryStart(FacilityJobKeys.MainCookStation, Job("cook:1"), 1, true).Failure);
    }

    [Fact]
    public void 站岗等不可生产角色不能开工()
    {
        var board = new FacilityJobBoard();

        FacilityJobStartResult result = board.TryStart(FacilityJobKeys.MainWorkbench, Job("chair"), 7,
            workerMayProduce: false);

        Assert.False(result.Started);
        Assert.Equal(FacilityJobStartFailure.WorkerUnavailable, result.Failure);
        Assert.Empty(board.Jobs);
    }

    [Fact]
    public void 只有到对应设施且未参战才推进_离台和参战都保留进度()
    {
        var board = new FacilityJobBoard();
        Assert.True(board.TryStart(FacilityJobKeys.MainWorkbench, Job("chair", 30), 1, true).Started);

        Assert.Equal(0, board.Advance(FacilityJobKeys.MainWorkbench, 5, false, true, false));
        Assert.Equal(0, board.Advance(FacilityJobKeys.MainWorkbench, 5, true, true, true));
        Assert.Equal(0, board.Advance(FacilityJobKeys.MainWorkbench, 5, true, false, false));
        Assert.Equal(5, board.Advance(FacilityJobKeys.MainWorkbench, 5, true, true, false));
        Assert.Equal(5, board.FindBySlot(FacilityJobKeys.MainWorkbench)!.Job.ElapsedWorkMinutes);
    }

    [Fact]
    public void 完工或取消会正确释放工人和设施()
    {
        var board = new FacilityJobBoard();
        Assert.True(board.TryStart(FacilityJobKeys.MainWorkbench, Job("chair", 5), 1, true).Started);
        Assert.Null(board.TakeCompleted(FacilityJobKeys.MainWorkbench));
        board.Advance(FacilityJobKeys.MainWorkbench, 5, true, true, false);

        Assert.NotNull(board.TakeCompleted(FacilityJobKeys.MainWorkbench));
        Assert.Null(board.FindByWorker(1));
        Assert.True(board.TryStart(FacilityJobKeys.MainCookStation, Job("cook:1"), 1, true).Started);
        Assert.NotNull(board.Cancel(FacilityJobKeys.MainCookStation));
        Assert.Empty(board.Jobs);
    }

    [Theory]
    [InlineData("chair", "workbench:main")]
    [InlineData("cook:3", "cookstation:main")]
    [InlineData("butcher:rabbit", "butcher:main")]
    [InlineData("plant:菜园#3", "cropplot:菜园#3")]
    [InlineData("weaponmod:pistol|scope", "modbench:main")]
    [InlineData("salvage:chair", "workbench:main")]
    [InlineData("salvage:prop#chair#2", "worksite:prop#chair#2")]
    public void 旧单按任务标识确定性迁入唯一稳定槽(string recipeId, string expectedSlot)
    {
        CraftingJob old = Job(recipeId, 30);
        old.Advance(11, true);

        FacilityJobBoard migrated = FacilityJobBoard.FromLegacySingleJob(old, 9);

        FacilityJobSlot slot = Assert.Single(migrated.Jobs);
        Assert.Equal(expectedSlot, slot.SlotKey);
        Assert.Equal(9, slot.WorkerId);
        Assert.Equal(11, slot.Job.ElapsedWorkMinutes);
    }

    [Fact]
    public void 存档快照按槽键稳定排序_读档拒绝重复工人()
    {
        var board = new FacilityJobBoard();
        Assert.True(board.TryRestore(new FacilityJobSlot("workbench:z", Job("z"), 2)).Started);
        Assert.True(board.TryRestore(new FacilityJobSlot("workbench:a", Job("a"), 1)).Started);

        Assert.Equal(new[] { "workbench:a", "workbench:z" }, board.Jobs.Select(x => x.SlotKey));
        Assert.Equal(FacilityJobStartFailure.WorkerAlreadyAssigned,
            board.TryRestore(new FacilityJobSlot("cookstation:main", Job("cook:1"), 1)).Failure);
    }

    [Fact]
    public void V3全营单槽存档无损迁到V4设施槽()
    {
        const string oldJson = """
        {
          "Version": 3,
          "Camp": {
            "CraftingJob": {
              "RecipeId": "cook:3",
              "Times": 3,
              "TotalWorkMinutes": 90,
              "ElapsedWorkMinutes": 25,
              "WorkerId": 7
            }
          }
        }
        """;

        SaveLoadResult migrated = SaveCodec.Deserialize(oldJson);

        Assert.True(migrated.Ok, migrated.Error);
        Assert.Null(migrated.Data!.Camp.CraftingJob);
        FacilityJobSave job = Assert.Single(migrated.Data.Camp.FacilityJobs);
        Assert.Equal(FacilityJobKeys.MainCookStation, job.SlotKey);
        Assert.Equal(25, job.ElapsedWorkMinutes);
        Assert.Equal(7, job.WorkerId);
    }

    private static CraftingJob Job(string id, int minutes = 30) => new(id, minutes);
}
