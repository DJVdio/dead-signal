using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class RecoveryPolicyTests
{
    private static double Heal(double restFraction, double bedFraction)
    {
        var set = new HealthConditionSet();
        var wound = new HealthCondition(HealthConditionType.Bleeding, 0.8, "躯干", onLimb: false);
        set.Add(wound);
        Assert.True(set.PerformSurgery(wound, new[] { "first_aid_kit" }, onBed: true,
            new SequenceRandomSource(new[] { 80.0 })).Success);
        double before = wound.Severity;
        set.TickDay(new SequenceRandomSource(new[] { 0.99 }), resting: restFraction > 0,
            restedInBed: bedFraction > 0, restFraction: restFraction, bedFraction: bedFraction);
        return before - wound.Severity;
    }

    [Fact]
    public void 主动卧床但没睡床_不再获得通用休养倍率()
        => Assert.Equal(Heal(0, 0), Heal(1, 0), 12);

    [Fact]
    public void 睡床只提供最高百分之十加成()
    {
        double plain = Heal(0, 0);
        double bed = Heal(0, 1);
        Assert.True(bed > plain);
        Assert.True(bed < plain * 1.2);
    }

    [Fact]
    public void 睡床占比按分钟加权_不是按相位计数()
    {
        var ledger = new RestLedger();
        ledger.RecordMinutes(60, onBed: true);
        ledger.RecordMinutes(180, onBed: false);
        Assert.Equal(240, ledger.MinutesCounted);
        Assert.Equal(60, ledger.BedMinutes);
        Assert.Equal(0.25, ledger.BedFraction, 12);
    }

    [Fact]
    public void 疾病与成药已删除()
    {
        Assert.DoesNotContain("Disease", System.Enum.GetNames<HealthConditionType>());
        Assert.Null(Materials.Find("medicine"));
        Assert.Null(MedicineCatalog.For("medicine"));
    }

    [Fact]
    public void 世界分钟_白昼夜晚各720_过渡相位不推进()
    {
        Assert.Equal(1440, BedrestLogic.WorldMinuteStamp(1, DayPhase.DayPrep, 0, 120, 120));
        Assert.Equal(1800, BedrestLogic.WorldMinuteStamp(1, DayPhase.DayExplore, 60, 120, 120));
        Assert.Equal(2160, BedrestLogic.WorldMinuteStamp(1, DayPhase.DayReturn, 999, 120, 120));
        Assert.Equal(2520, BedrestLogic.WorldMinuteStamp(1, DayPhase.NightAct, 60, 120, 120));
        Assert.Equal(2880, BedrestLogic.WorldMinuteStamp(1, DayPhase.DawnMeal, 999, 120, 120));
        Assert.Equal(2880, BedrestLogic.WorldMinuteStamp(2, DayPhase.DayPrep, 0, 120, 120));
    }

    [Fact]
    public void V4相位账本无损迁成分钟权重_含疾病则拒读()
    {
        string clean = """{"Version":4,"Survivors":[{"RestPhases":6,"RestRestPhases":3,"RestBedPhases":2}]}""";
        SaveLoadResult migrated = SaveCodec.Deserialize(clean);
        Assert.True(migrated.Ok, migrated.Error);
        PawnSave pawn = Assert.Single(migrated.Data!.Survivors);
        Assert.Equal(6, pawn.RestMinutes);
        Assert.Equal(3, pawn.RestRestMinutes);
        Assert.Equal(2, pawn.RestBedMinutes);

        string disease = """{"Version":4,"Survivors":[{"Conditions":[{"Type":"Disease"}]}]}""";
        SaveLoadResult rejected = SaveCodec.Deserialize(disease);
        Assert.False(rejected.Ok);
        Assert.Contains("疾病", rejected.Error);
    }
}
