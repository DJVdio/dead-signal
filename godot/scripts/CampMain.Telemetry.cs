using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 无界面实机验收采集层。所有入口都只读运行时真状态；任何导出异常只写开发者警告，绝不阻断玩法。
/// 文件静默覆盖到 user://playtest-telemetry，结束一局后可直接取 CSV/Markdown 分析。
/// </summary>
public sealed partial class CampMain
{
    private PlaytestTelemetryLedger _playtestTelemetry = new(NewPlaytestSessionId());

    private static string NewPlaytestSessionId()
        => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
           + "-" + Guid.NewGuid().ToString("N")[..8];

    private PlaytestResourceSnapshot CapturePlaytestResources()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Item item in _inventory.Items)
        {
            string key = item.RefKey ?? item.DisplayName;
            if (string.IsNullOrWhiteSpace(key)) continue;
            int quantity = item.Category switch
            {
                ItemCategory.Material => item.MaterialQuantity,
                ItemCategory.Food => item.FoodQuantity,
                _ => 1,
            };
            counts[key] = counts.GetValueOrDefault(key) + quantity;
        }
        return new PlaytestResourceSnapshot
        {
            Population = _survivors.Count(p => p.Alive),
            Food = _resources.Food,
            BedsOccupied = _beds.Occupancy.Count,
            Inventory = counts,
        };
    }

    private void RecordPlaytestHalfDay()
    {
        _playtestTelemetry.RecordPhaseSnapshot(_clock.Day, _clock.CurrentPhase, CapturePlaytestResources());
        TryExportPlaytestTelemetry();
    }

    private void RecordPlaytestExpeditionStart(string destination)
    {
        string[] team = _survivors
            .Where(p => p.Alive && _todaysExpeditionIds.Contains(p.Id))
            .Select(p => p.DisplayName)
            .ToArray();
        _playtestTelemetry.BeginExpedition(_clock.Day, _clock.CurrentPhase, destination, team,
            _bruceExpedition && _bruce is { Alive: true }, CapturePlaytestResources(), _clock.WorldMinuteStamp(),
            _bag?.GearKg ?? 0, _bag?.CapacityKg ?? 0);
        TryExportPlaytestTelemetry();
    }

    private void RecordPlaytestExpeditionReturn(string destination, bool survived)
    {
        double lootKg = _bag?.LootKg ?? 0;
        string loot = _bag is null || _bag.Contents.Count == 0
            ? ""
            : string.Join("、", _bag.Contents.Select(LootDisplay.NameOf));
        _playtestTelemetry.EndExpedition(_clock.Day, _clock.CurrentPhase, destination, survived,
            lootKg, loot, _expeditionHasTransmitter, CapturePlaytestResources(), _clock.WorldMinuteStamp(),
            DescribeExpeditionInjuries());
        TryExportPlaytestTelemetry();
    }

    private string DescribeExpeditionInjuries()
    {
        Pawn[] present = _survivors.Where(p => _todaysExpeditionIds.Contains(p.Id)).ToArray();
        int bleeding = present.Sum(p => p.Health.Conditions.Count(c => c.Type == HealthConditionType.Bleeding));
        int fractures = present.Sum(p => p.Health.Conditions.Count(c => c.Type == HealthConditionType.Fracture));
        int infections = present.Sum(p => p.Health.Conditions.Count(c => c.Type == HealthConditionType.Infection));
        int severed = present.Sum(p => p.Inspect().Parts.Count(part => part.IsSevered));
        int alive = present.Count(p => p.Alive);
        int dead = Math.Max(0, _todaysExpeditionIds.Count - alive);
        return $"返程伤病：出血 {bleeding}；骨折 {fractures}；感染 {infections}；切除部位 {severed}；死亡 {dead}";
    }

    private PlaytestResourceSnapshot BeginPlaytestProductionSnapshot() => CapturePlaytestResources();

    private void RecordPlaytestProduction(string recipeId, PlaytestResourceSnapshot before)
    {
        PlaytestResourceSnapshot after = CapturePlaytestResources();
        _playtestTelemetry.RecordEvent(_clock.Day, _clock.CurrentPhase, PlaytestEventKind.ProductionCompleted,
            "生产完成", "营地", after, $"任务 {recipeId}；{DescribePlaytestDelta(before, after)}");
        TryExportPlaytestTelemetry();
    }

    private static string DescribePlaytestDelta(PlaytestResourceSnapshot before, PlaytestResourceSnapshot after)
    {
        var changes = new List<string>();
        int food = after.Food - before.Food;
        if (food != 0) changes.Add($"食物{Signed(food)}");
        foreach (string key in before.Inventory.Keys.Concat(after.Inventory.Keys).Distinct(StringComparer.Ordinal)
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            int delta = after.Count(key) - before.Count(key);
            if (delta == 0) continue;
            string name = Materials.Find(key)?.DisplayName ?? key;
            changes.Add($"{name}{Signed(delta)}");
        }
        return changes.Count == 0 ? "无即时库存变化" : string.Join("、", changes);
    }

    private static string Signed(int value)
        => value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);

    private void RecordPlaytestEvent(PlaytestEventKind kind, string name, string destination, string detail)
    {
        _playtestTelemetry.RecordEvent(_clock.Day, _clock.CurrentPhase, kind, name, destination,
            CapturePlaytestResources(), detail);
        TryExportPlaytestTelemetry();
    }

    private void TryExportPlaytestTelemetry()
    {
        try
        {
            string directory = ProjectSettings.GlobalizePath("user://playtest-telemetry");
            Directory.CreateDirectory(directory);
            WriteTelemetryFile(Path.Combine(directory, _playtestTelemetry.SessionId + ".csv"),
                PlaytestTelemetryExporter.ToCsv(_playtestTelemetry));
            WriteTelemetryFile(Path.Combine(directory, _playtestTelemetry.SessionId + ".md"),
                PlaytestTelemetryExporter.ToMarkdown(_playtestTelemetry));
        }
        catch (Exception ex)
        {
            GD.PushWarning($"实机验收账本导出失败：{ex.Message}");
        }
    }

    private static void WriteTelemetryFile(string path, string content)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}
