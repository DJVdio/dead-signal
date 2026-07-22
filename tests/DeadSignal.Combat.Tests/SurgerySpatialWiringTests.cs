using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class SurgerySpatialWiringTests
{
    private static string CampMain()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "godot", "scripts", "CampMain.cs"));
    }

    [Fact]
    public void HandlersRouteThroughSpatialApproachBeforeWorktime()
    {
        string code = CampMain();
        Assert.Contains("BeginSurgeryApproach", code);
        Assert.Contains("TryStartOperating", code);
        Assert.Contains("AdvanceSpatial", code);
        Assert.Contains("SurgerySpatiallyValid", code);
        Assert.DoesNotContain("_surgeryJob = kind == SurgeryKind.Prosthetic", code);
    }

    [Fact]
    public void RuntimeStillUsesRealtimeBedRegistryForAllThreeSettlements()
    {
        string code = CampMain();
        Assert.Contains("PerformSurgery(\n                condition!, job.MaterialKeys, RealOnBed(patient)", code);
        Assert.Contains("AmputateInfectedLimb(\n                condition!, job.MaterialKeys, RealOnBed(patient)", code);
        Assert.Contains("InstallProstheticSurgery(\n                job.ProstheticRegion!.Value, job.ProstheticGrade!.Value, job.MaterialKeys,\n                RealOnBed(patient)", code);
    }

    [Fact]
    public void LoadSafelyInterruptsLegacyWorkWithoutSpatialContext()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string save = File.ReadAllText(Path.Combine(dir!.FullName, "godot", "scripts", "CampMain.Save.cs"));
        Assert.Contains("_surgeryJob = null;", save);
        Assert.DoesNotContain("SurgeryJob.Restore(camp.SurgeryJob)", save);
        Assert.DoesNotContain("SurgeryJob = _surgeryJob?.Snapshot()", save);
        Assert.Null(typeof(CampSave).GetProperty("SurgeryJob"));
    }

    [Fact]
    public void SurgeryParticipantsAreExcludedFromOtherRoleWork()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string pawn = File.ReadAllText(Path.Combine(dir!.FullName, "godot", "scripts", "Pawn.cs"));
        string roles = File.ReadAllText(Path.Combine(dir.FullName, "godot", "scripts", "PawnRoleManager.cs"));
        Assert.Contains("Role == PawnRole.Idle && !SurgeryOccupied", pawn);
        Assert.Contains("if (p.SurgeryOccupied && p.Role != PawnRole.Bedrest)", roles);
    }
}
