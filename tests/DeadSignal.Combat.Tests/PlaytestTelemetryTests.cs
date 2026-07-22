using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class PlaytestTelemetryTests
{
    [Fact]
    public void PhaseSnapshot_RecordsCoreEconomyAndExportsChineseCsvAndMarkdown()
    {
        var ledger = new PlaytestTelemetryLedger("session-fixed");
        ledger.RecordPhaseSnapshot(12, DayPhase.DuskMeal, new PlaytestResourceSnapshot
        {
            Population = 5,
            Food = 17,
            BedsOccupied = 2,
            Inventory = new Dictionary<string, int>
            {
                [Materials.CurrencyKey] = Silver.FromWhole(42),
                [AmmoKeys.ShortBullet] = 9,
                ["bandage"] = 3,
            },
        });

        PlaytestTelemetryEntry entry = Assert.Single(ledger.Entries);
        Assert.Equal(PlaytestEventKind.HalfDaySnapshot, entry.Kind);
        Assert.Equal(17, entry.Resources.Food);
        Assert.Equal(Silver.FromWhole(42), entry.Resources.Count(Materials.CurrencyKey));

        string csv = PlaytestTelemetryExporter.ToCsv(ledger);
        Assert.Contains("序号,天数,相位,事件", csv);
        Assert.Contains("黄昏聚餐", csv);
        Assert.Contains("42.00", csv);

        string markdown = PlaytestTelemetryExporter.ToMarkdown(ledger);
        Assert.Contains("# Dead Signal 实机验收记录", markdown);
        Assert.Contains("第 12 天", markdown);
        Assert.Contains("食物 17", markdown);
    }

    [Fact]
    public void ExpeditionReturn_ComputesAmmoSpentAndKeepsLootSeparate()
    {
        var ledger = new PlaytestTelemetryLedger("session-fixed");
        var start = Resources(food: 10, shortAmmo: 20);
        ledger.BeginExpedition(3, DayPhase.DayExplore, "废弃医院", new[] { "山姆", "诺蒂" },
            bruceAlong: false, start, worldMinute: 320, gearKg: 12.5, capacityKg: 80);

        ledger.EndExpedition(3, DayPhase.DayReturn, "废弃医院", survived: true,
            lootKg: 17.25, lootSummary: "绷带×4；短子弹×3", hasTransmitter: false,
            Resources(food: 10, shortAmmo: 13), worldMinute: 455,
            injurySummary: "新增出血 2；新增骨折 1");

        PlaytestTelemetryEntry returned = ledger.Entries.Last();
        Assert.Equal(PlaytestEventKind.ExpeditionReturned, returned.Kind);
        Assert.Equal(7, returned.AmmoSpent[AmmoKeys.ShortBullet]);
        Assert.Equal(135, returned.DurationGameMinutes);
        Assert.Equal(17.25, returned.LootKg, 2);
        Assert.Equal(12.5, returned.GearKg, 2);
        Assert.Equal(80, returned.CarryCapacityKg, 2);
        Assert.Contains("新增骨折 1", returned.Detail);
    }

    [Fact]
    public void SnapshotRestore_PreservesSessionSequenceAndEntries()
    {
        var ledger = new PlaytestTelemetryLedger("session-fixed");
        ledger.RecordEvent(2, DayPhase.DawnMeal, PlaytestEventKind.CorpseRecovery,
            "找回遗体物资", "下水道", Resources(8, 5), "找回关键设备");

        PlaytestTelemetryLedger restored = PlaytestTelemetryLedger.Restore(ledger.Snapshot());
        restored.RecordEvent(2, DayPhase.DayPrep, PlaytestEventKind.ProductionCompleted,
            "生产完成", "营地", Resources(8, 5), "绷带×2");

        Assert.Equal("session-fixed", restored.SessionId);
        Assert.Equal(new[] { 1, 2 }, restored.Entries.Select(e => e.Sequence));
        Assert.Contains("找回关键设备", PlaytestTelemetryExporter.ToMarkdown(restored));
    }

    [Fact]
    public void CsvEscapesCommaQuoteAndNewline()
    {
        var ledger = new PlaytestTelemetryLedger("session-fixed");
        ledger.RecordEvent(1, DayPhase.DayPrep, PlaytestEventKind.ProductionCompleted,
            "制作,完成", "营地", Resources(4, 0), "产出 \"急救包\"\n两件");

        string csv = PlaytestTelemetryExporter.ToCsv(ledger);
        Assert.Contains("\"制作,完成\"", csv);
        Assert.Contains("\"产出 \"\"急救包\"\"\n两件\"", csv);
    }

    [Fact]
    public void SaveCodec_RoundTripsTelemetryWithoutChangingSaveVersion()
    {
        var ledger = new PlaytestTelemetryLedger("session-save");
        ledger.RecordPhaseSnapshot(9, DayPhase.DawnMeal, Resources(14, 11));
        var save = new SaveData { Telemetry = ledger.Snapshot() };

        SaveLoadResult result = SaveCodec.Deserialize(SaveCodec.Serialize(save));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(SaveCodec.CurrentVersion, result.Data!.Version);
        Assert.Equal("session-save", result.Data.Telemetry.SessionId);
        Assert.Single(result.Data.Telemetry.Entries);
        Assert.Equal(14, result.Data.Telemetry.Entries[0].Resources.Food);
    }

    [Fact]
    public void CampRuntime_WiresAllRequiredTelemetryEventsWithoutLaunchingGodot()
    {
        string root = RepoRoot();
        string camp = File.ReadAllText(Path.Combine(root, "godot/scripts/CampMain.cs"));
        string save = File.ReadAllText(Path.Combine(root, "godot/scripts/CampMain.Save.cs"));
        string family = File.ReadAllText(Path.Combine(root, "godot/scripts/CampMain.FamilyEscape.cs"));
        string south = File.ReadAllText(Path.Combine(root, "godot/scripts/CampMain.SouthEscape.cs"));

        Assert.Contains("RecordPlaytestHalfDay();", camp);
        Assert.Contains("RecordPlaytestExpeditionStart(destinationName);", camp);
        Assert.Contains("RecordPlaytestExpeditionReturn(_currentLevel.DestinationName, expeditionSurvived);", camp);
        Assert.Contains("RecordPlaytestProduction(completed.Job.RecipeId, before);", camp);
        Assert.Contains("PlaytestEventKind.SurvivorDied", camp);
        Assert.Contains("关键设备回到原位", camp);
        Assert.Contains("Telemetry = _playtestTelemetry.Snapshot()", save);
        Assert.Contains("PlaytestTelemetryLedger.Restore(s.Telemetry)", save);
        Assert.Contains("new PlaytestTelemetryLedger(NewPlaytestSessionId())", save);
        Assert.Contains("PlaytestEventKind.Ending", family);
        Assert.Contains("PlaytestEventKind.Ending", south);
    }

    private static PlaytestResourceSnapshot Resources(int food, int shortAmmo) => new()
    {
        Population = 3,
        Food = food,
        Inventory = new Dictionary<string, int> { [AmmoKeys.ShortBullet] = shortAmmo },
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("找不到 Dead Signal 仓库根目录");
    }
}
