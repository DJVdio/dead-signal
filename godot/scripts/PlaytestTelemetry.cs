using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 纯 C#：不得引 Godot。运行时只负责把真实状态拍进来和把字符串落盘；账本、差分、导出均可单测。

public enum PlaytestEventKind
{
    HalfDaySnapshot,
    ExpeditionStarted,
    ExpeditionReturned,
    CorpseRecovery,
    ProductionCompleted,
    SurvivorDied,
    Ending,
}

public sealed class PlaytestResourceSnapshot
{
    public int Population { get; set; }
    public int Food { get; set; }
    public int BedsOccupied { get; set; }
    public Dictionary<string, int> Inventory { get; set; } = new(StringComparer.Ordinal);

    public int Count(string key) => Inventory.GetValueOrDefault(key);

    public PlaytestResourceSnapshot Copy() => new()
    {
        Population = Population,
        Food = Food,
        BedsOccupied = BedsOccupied,
        Inventory = Inventory.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
    };
}

public sealed class PlaytestTelemetryEntry
{
    public int Sequence { get; set; }
    public int Day { get; set; }
    public DayPhase Phase { get; set; }
    public PlaytestEventKind Kind { get; set; }
    public string EventName { get; set; } = "";
    public string Destination { get; set; } = "";
    public PlaytestResourceSnapshot Resources { get; set; } = new();
    public int DurationGameMinutes { get; set; }
    public double LootKg { get; set; }
    public double GearKg { get; set; }
    public double CarryCapacityKg { get; set; }
    public Dictionary<string, int> AmmoSpent { get; set; } = new(StringComparer.Ordinal);
    public string Detail { get; set; } = "";
}

public sealed class PlaytestExpeditionBaseline
{
    public string Destination { get; set; } = "";
    public int StartedWorldMinute { get; set; }
    public PlaytestResourceSnapshot Resources { get; set; } = new();
    public double GearKg { get; set; }
    public double CarryCapacityKg { get; set; }
}

public sealed class PlaytestTelemetrySave
{
    public string SessionId { get; set; } = "";
    public int NextSequence { get; set; } = 1;
    public List<PlaytestTelemetryEntry> Entries { get; set; } = new();
    public PlaytestExpeditionBaseline? ActiveExpedition { get; set; }
}

/// <summary>
/// 实机验收的追加式账本。只观察，不参与任何玩法判定；导出失败也不得改变游戏状态。
/// </summary>
public sealed class PlaytestTelemetryLedger
{
    private static readonly string[] AmmoKeysToTrack =
    {
        AmmoKeys.ShortBullet, AmmoKeys.MediumBullet, AmmoKeys.Buckshot, AmmoKeys.LongBullet,
        ArrowKeys.SharpenedStick, ArrowKeys.Handmade, ArrowKeys.Heavy, ArrowKeys.Carbon,
    };

    private int _nextSequence;
    private PlaytestExpeditionBaseline? _activeExpedition;
    private readonly List<PlaytestTelemetryEntry> _entries;

    public PlaytestTelemetryLedger(string sessionId)
        : this(sessionId, 1, new List<PlaytestTelemetryEntry>(), null) { }

    private PlaytestTelemetryLedger(string sessionId, int nextSequence,
        List<PlaytestTelemetryEntry> entries, PlaytestExpeditionBaseline? activeExpedition)
    {
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? "unknown-session" : sessionId;
        _nextSequence = Math.Max(1, nextSequence);
        _entries = entries;
        _activeExpedition = activeExpedition;
    }

    public string SessionId { get; }
    public IReadOnlyList<PlaytestTelemetryEntry> Entries => _entries;

    public void RecordPhaseSnapshot(int day, DayPhase phase, PlaytestResourceSnapshot resources)
        => Add(day, phase, PlaytestEventKind.HalfDaySnapshot, "半天结算", "营地", resources, "聚餐结算后的全局快照");

    public void BeginExpedition(int day, DayPhase phase, string destination, IEnumerable<string> teamNames,
        bool bruceAlong, PlaytestResourceSnapshot resources, int worldMinute, double gearKg = 0, double capacityKg = 0)
    {
        string team = string.Join("、", teamNames.Where(name => !string.IsNullOrWhiteSpace(name)));
        if (bruceAlong) team = string.IsNullOrEmpty(team) ? "布鲁斯" : team + "、布鲁斯";
        _activeExpedition = new PlaytestExpeditionBaseline
        {
            Destination = destination,
            StartedWorldMinute = worldMinute,
            Resources = resources.Copy(),
            GearKg = Math.Max(0, gearKg),
            CarryCapacityKg = Math.Max(0, capacityKg),
        };
        PlaytestTelemetryEntry entry = Add(day, phase, PlaytestEventKind.ExpeditionStarted, "探索出发", destination, resources,
            string.IsNullOrEmpty(team) ? "队伍：无" : $"队伍：{team}");
        entry.GearKg = Math.Max(0, gearKg);
        entry.CarryCapacityKg = Math.Max(0, capacityKg);
    }

    public void EndExpedition(int day, DayPhase phase, string destination, bool survived,
        double lootKg, string lootSummary, bool hasTransmitter, PlaytestResourceSnapshot resources,
        int worldMinute, string injurySummary)
    {
        PlaytestExpeditionBaseline? baseline = _activeExpedition;
        var ammoSpent = new Dictionary<string, int>(StringComparer.Ordinal);
        if (baseline is not null)
        {
            foreach (string key in AmmoKeysToTrack)
            {
                int spent = baseline.Resources.Count(key) - resources.Count(key);
                if (spent > 0) ammoSpent[key] = spent;
            }
        }

        int duration = baseline is null ? 0 : Math.Max(0, worldMinute - baseline.StartedWorldMinute);
        string[] details =
        {
            survived ? "队伍有人生还" : "探索队全灭",
            string.IsNullOrWhiteSpace(lootSummary) ? "带回物资：无" : $"带回物资：{lootSummary}",
            string.IsNullOrWhiteSpace(injurySummary) ? "新增伤病：无" : injurySummary,
            hasTransmitter ? "携带关键设备" : "未携带关键设备",
        };
        PlaytestTelemetryEntry entry = Add(day, phase, PlaytestEventKind.ExpeditionReturned,
            "探索返程", destination, resources, string.Join("；", details));
        entry.DurationGameMinutes = duration;
        entry.LootKg = Math.Max(0, lootKg);
        entry.GearKg = baseline?.GearKg ?? 0;
        entry.CarryCapacityKg = baseline?.CarryCapacityKg ?? 0;
        entry.AmmoSpent = ammoSpent;
        _activeExpedition = null;
    }

    public void RecordEvent(int day, DayPhase phase, PlaytestEventKind kind, string eventName,
        string destination, PlaytestResourceSnapshot resources, string detail)
        => Add(day, phase, kind, eventName, destination, resources, detail);

    public PlaytestTelemetrySave Snapshot() => new()
    {
        SessionId = SessionId,
        NextSequence = _nextSequence,
        Entries = _entries.Select(CopyEntry).ToList(),
        ActiveExpedition = _activeExpedition is null ? null : new PlaytestExpeditionBaseline
        {
            Destination = _activeExpedition.Destination,
            StartedWorldMinute = _activeExpedition.StartedWorldMinute,
            Resources = _activeExpedition.Resources.Copy(),
            GearKg = _activeExpedition.GearKg,
            CarryCapacityKg = _activeExpedition.CarryCapacityKg,
        },
    };

    public static PlaytestTelemetryLedger Restore(PlaytestTelemetrySave? save)
    {
        if (save is null || string.IsNullOrWhiteSpace(save.SessionId))
            return new PlaytestTelemetryLedger("unknown-session");
        List<PlaytestTelemetryEntry> entries = (save.Entries ?? new List<PlaytestTelemetryEntry>())
            .Select(CopyEntry).OrderBy(entry => entry.Sequence).ToList();
        int next = Math.Max(save.NextSequence, entries.Count == 0 ? 1 : entries.Max(e => e.Sequence) + 1);
        return new PlaytestTelemetryLedger(save.SessionId, next, entries, save.ActiveExpedition);
    }

    private PlaytestTelemetryEntry Add(int day, DayPhase phase, PlaytestEventKind kind, string eventName,
        string destination, PlaytestResourceSnapshot resources, string detail)
    {
        var entry = new PlaytestTelemetryEntry
        {
            Sequence = _nextSequence++,
            Day = Math.Max(0, day),
            Phase = phase,
            Kind = kind,
            EventName = eventName ?? "",
            Destination = destination ?? "",
            Resources = resources.Copy(),
            Detail = detail ?? "",
        };
        _entries.Add(entry);
        return entry;
    }

    private static PlaytestTelemetryEntry CopyEntry(PlaytestTelemetryEntry source) => new()
    {
        Sequence = source.Sequence,
        Day = source.Day,
        Phase = source.Phase,
        Kind = source.Kind,
        EventName = source.EventName,
        Destination = source.Destination,
        Resources = source.Resources?.Copy() ?? new PlaytestResourceSnapshot(),
        DurationGameMinutes = source.DurationGameMinutes,
        LootKg = source.LootKg,
        GearKg = source.GearKg,
        CarryCapacityKg = source.CarryCapacityKg,
        AmmoSpent = (source.AmmoSpent ?? new Dictionary<string, int>())
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        Detail = source.Detail,
    };
}

public static class PlaytestTelemetryExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ToCsv(PlaytestTelemetryLedger ledger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("序号,天数,相位,事件,地点,人口,食物,白银,库存明细,短子弹,中子弹,鹿弹,长子弹,绷带,抗生素,占用床位,远征分钟,装备kg,战利品kg,运力上限kg,弹药消耗,说明");
        foreach (PlaytestTelemetryEntry e in ledger.Entries)
        {
            string ammo = string.Join("；", e.AmmoSpent.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{Display(p.Key)}×{p.Value}"));
            string[] cells =
            {
                e.Sequence.ToString(Inv), e.Day.ToString(Inv), DisplayNames.Of(e.Phase), e.EventName,
                e.Destination, e.Resources.Population.ToString(Inv), e.Resources.Food.ToString(Inv),
                Silver.Format(e.Resources.Count(Materials.CurrencyKey)),
                InventorySummary(e.Resources),
                e.Resources.Count(AmmoKeys.ShortBullet).ToString(Inv),
                e.Resources.Count(AmmoKeys.MediumBullet).ToString(Inv),
                e.Resources.Count(AmmoKeys.Buckshot).ToString(Inv),
                e.Resources.Count(AmmoKeys.LongBullet).ToString(Inv),
                e.Resources.Count("bandage").ToString(Inv),
                e.Resources.Count("antibiotics").ToString(Inv),
                e.Resources.BedsOccupied.ToString(Inv), e.DurationGameMinutes.ToString(Inv),
                e.GearKg.ToString("0.##", Inv), e.LootKg.ToString("0.##", Inv),
                e.CarryCapacityKg.ToString("0.##", Inv), ammo, e.Detail,
            };
            sb.AppendLine(string.Join(",", cells.Select(EscapeCsv)));
        }
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string ToMarkdown(PlaytestTelemetryLedger ledger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Dead Signal 实机验收记录");
        sb.AppendLine();
        sb.AppendLine($"会话：`{EscapeMarkdown(ledger.SessionId)}`　事件数：{ledger.Entries.Count}");
        sb.AppendLine();
        foreach (PlaytestTelemetryEntry e in ledger.Entries)
        {
            sb.AppendLine($"## {e.Sequence}. 第 {e.Day} 天 · {DisplayNames.Of(e.Phase)} · {EscapeMarkdown(e.EventName)}");
            sb.AppendLine();
            sb.AppendLine($"- 地点：{EscapeMarkdown(e.Destination)}");
            sb.AppendLine($"- 快照：人口 {e.Resources.Population}；食物 {e.Resources.Food}；白银 {Silver.Format(e.Resources.Count(Materials.CurrencyKey))}；占床 {e.Resources.BedsOccupied}");
            sb.AppendLine($"- 库存：{EscapeMarkdown(InventorySummary(e.Resources))}");
            if (e.DurationGameMinutes > 0 || e.LootKg > 0 || e.CarryCapacityKg > 0)
                sb.AppendLine($"- 远征：{e.DurationGameMinutes} 游戏分钟；装备 {e.GearKg.ToString("0.##", Inv)} kg；战利品 {e.LootKg.ToString("0.##", Inv)} kg；运力 {e.CarryCapacityKg.ToString("0.##", Inv)} kg");
            if (e.AmmoSpent.Count > 0)
                sb.AppendLine($"- 弹药消耗：{string.Join("；", e.AmmoSpent.Select(p => $"{Display(p.Key)}×{p.Value}"))}");
            sb.AppendLine($"- 说明：{EscapeMarkdown(e.Detail)}");
            sb.AppendLine();
        }
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Display(string key) => Materials.Find(key)?.DisplayName ?? key;

    private static string InventorySummary(PlaytestResourceSnapshot resources)
        => resources.Inventory.Count == 0
            ? "空"
            : string.Join("；", resources.Inventory.Where(pair => pair.Value != 0)
                .OrderBy(pair => Display(pair.Key), StringComparer.Ordinal)
                .Select(pair => $"{Display(pair.Key)}×{pair.Value}"));

    private static string EscapeCsv(string? value)
    {
        string v = value ?? "";
        return v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    private static string EscapeMarkdown(string? value)
        => (value ?? "").Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ").Replace("\n", " ");
}
