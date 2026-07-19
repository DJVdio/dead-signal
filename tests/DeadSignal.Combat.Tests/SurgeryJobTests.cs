using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class SurgeryJobTests
{
    [Fact]
    public void OrdinarySurgery_TakesThirtyGameMinutes()
    {
        SurgeryJob job = SurgeryJob.ForCondition(
            SurgeryKind.Treatment, surgeonId: 1, patientId: 2,
            HealthConditionType.Bleeding, "胸部", new[] { "bandage" },
            surgerySpeedMultiplier: 1.0);

        Assert.Equal(30, job.TotalMinutes);
        Assert.Equal(SurgeryAdvanceStatus.InProgress,
            job.Advance(29, clockPaused: false, patientAlive: true, targetExists: true));
        Assert.False(job.IsReady);
        Assert.Equal(SurgeryAdvanceStatus.Ready,
            job.Advance(1, clockPaused: false, patientAlive: true, targetExists: true));
        Assert.True(job.IsReady);
    }

    [Fact]
    public void FamilyFirstAidReader_TakesTwentyFourGameMinutes()
    {
        SurgeryJob job = SurgeryJob.ForCondition(
            SurgeryKind.Treatment, 1, 2, HealthConditionType.Fracture, "左上肢",
            Array.Empty<string>(), surgerySpeedMultiplier: 1.25);

        Assert.Equal(24, job.TotalMinutes);
        job.Advance(23, clockPaused: false, patientAlive: true, targetExists: true);
        Assert.False(job.IsReady);
        job.Advance(1, clockPaused: false, patientAlive: true, targetExists: true);
        Assert.True(job.IsReady);
    }

    [Fact]
    public void Pause_DoesNotAdvanceWork()
    {
        SurgeryJob job = SurgeryJob.ForCondition(
            SurgeryKind.Treatment, 1, 2, HealthConditionType.Bleeding, "躯干",
            Array.Empty<string>(), 1.0);

        Assert.Equal(SurgeryAdvanceStatus.Paused,
            job.Advance(20, clockPaused: true, patientAlive: true, targetExists: true));
        Assert.Equal(0, job.ElapsedMinutes);
        Assert.Equal(30, job.RemainingMinutes);
    }

    [Fact]
    public void PatientBleedsToDeath_InterruptsAndCannotSettle()
    {
        SurgeryJob job = SurgeryJob.ForCondition(
            SurgeryKind.Treatment, 1, 2, HealthConditionType.Bleeding, "颈部",
            new[] { "first_aid_kit" }, 1.0);

        job.Advance(12, clockPaused: false, patientAlive: true, targetExists: true);
        Assert.Equal(SurgeryAdvanceStatus.Interrupted,
            job.Advance(1, clockPaused: false, patientAlive: false, targetExists: true));
        Assert.True(job.IsInterrupted);
        Assert.False(job.TryClaimSettlement());
        Assert.Equal(12, job.ElapsedMinutes);
    }

    [Fact]
    public void ExistingBodyBleedTick_CanKillPatientBeforeSurgerySettlement()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Large);
        SurgeryJob job = SurgeryJob.ForCondition(
            SurgeryKind.Treatment, 1, 2, HealthConditionType.Bleeding, HumanBody.Chest,
            new[] { "bandage" }, 1.0);
        job.Advance(10, clockPaused: false, patientAlive: true, targetExists: true);

        // 运行时正是 Actor 调这条既有链；手术状态机不另扣血，下一次轮询只读取死亡结果并中断。
        body.TickBleed(100_000);
        SurgeryAdvanceStatus status = job.Advance(
            1, clockPaused: false, patientAlive: !body.IsDead, targetExists: true);

        Assert.True(body.IsDead);
        Assert.Equal(SurgeryAdvanceStatus.Interrupted, status);
        Assert.Equal(10, job.ElapsedMinutes);
        Assert.False(job.TryClaimSettlement());
    }

    [Fact]
    public void TargetConditionDisappears_InterruptsAndCannotSettle()
    {
        SurgeryJob job = SurgeryJob.ForCondition(
            SurgeryKind.Amputation, 1, 2, HealthConditionType.Infection, "右下肢",
            new[] { "splint" }, 1.0);

        Assert.Equal(SurgeryAdvanceStatus.Interrupted,
            job.Advance(0, clockPaused: false, patientAlive: true, targetExists: false));
        Assert.True(job.IsInterrupted);
        Assert.False(job.TryClaimSettlement());
    }

    [Fact]
    public void ReadyJob_ClaimsSettlementOnlyOnce()
    {
        SurgeryJob job = SurgeryJob.ForCondition(
            SurgeryKind.Treatment, 1, 2, HealthConditionType.Bleeding, "腹部",
            new[] { "bandage", "needle_thread" }, 1.0);

        job.Advance(30, clockPaused: false, patientAlive: true, targetExists: true);

        Assert.True(job.TryClaimSettlement());
        Assert.False(job.TryClaimSettlement());
        Assert.True(job.IsSettled);
    }

    [Fact]
    public void Snapshot_RoundTripsStableIdsTargetAndProgress()
    {
        SurgeryJob original = SurgeryJob.ForCondition(
            SurgeryKind.Treatment, surgeonId: 17, patientId: 23,
            HealthConditionType.Fracture, "右上肢", new[] { "splint", "bandage" }, 1.25);
        original.Advance(11, clockPaused: false, patientAlive: true, targetExists: true);

        SurgeryJob restored = SurgeryJob.Restore(original.Snapshot());

        Assert.Equal(17, restored.SurgeonId);
        Assert.Equal(23, restored.PatientId);
        Assert.Equal(HealthConditionType.Fracture, restored.ConditionType);
        Assert.Equal("右上肢", restored.BodyPartKey);
        Assert.Equal(24, restored.TotalMinutes);
        Assert.Equal(11, restored.ElapsedMinutes);
        Assert.Equal(new[] { "splint", "bandage" }, restored.MaterialKeys);
        restored.Advance(13, clockPaused: false, patientAlive: true, targetExists: true);
        Assert.True(restored.TryClaimSettlement());
    }

    [Fact]
    public void SaveCodec_RoundTripsActiveSurgeryWithoutObjectReferences()
    {
        SurgeryJob job = SurgeryJob.ForProsthetic(
            surgeonId: 5, patientId: 8, bodyPartKey: "左手",
            BodyRegion.Hand, ProstheticGrade.Simple, Array.Empty<string>(), 1.0);
        job.Advance(9, clockPaused: false, patientAlive: true, targetExists: true);
        SaveData data = new();
        data.Camp.SurgeryJob = job.Snapshot();

        SaveData restored = SaveCodec.Deserialize(SaveCodec.Serialize(data)).Data!;
        SurgeryJob back = SurgeryJob.Restore(restored.Camp.SurgeryJob!);

        Assert.Equal(5, back.SurgeonId);
        Assert.Equal(8, back.PatientId);
        Assert.Equal("左手", back.BodyPartKey);
        Assert.Equal(BodyRegion.Hand, back.ProstheticRegion);
        Assert.Equal(ProstheticGrade.Simple, back.ProstheticGrade);
        Assert.Equal(9, back.ElapsedMinutes);
    }
}
