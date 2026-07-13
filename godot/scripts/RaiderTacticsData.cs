using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 战术参数的**数据加载器**（Godot 层，唯一读盘处）：<c>res://data/raider_tactics.json</c> → <see cref="RaiderTacticsParams"/>。
/// 规则在 <see cref="RaiderTactics"/>（纯逻辑），数值在数据文件——代码里不留魔法数。
/// 文件缺失/解析失败/字段缺省一律回落 <see cref="RaiderTacticsParams.Default"/>（不崩、不卡关）。
/// 全场共享一份（进程内只读一次）。
/// </summary>
public static class RaiderTacticsData
{
    private const string Path = "res://data/raider_tactics.json";
    private static RaiderTacticsParams? _cached;

    /// <summary>取战术参数（首次调用读盘，之后走缓存）。</summary>
    public static RaiderTacticsParams Params => _cached ??= Load();

    private static RaiderTacticsParams Load()
    {
        if (!FileAccess.FileExists(Path))
        {
            return RaiderTacticsParams.Default;
        }

        using FileAccess f = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
        if (f == null)
        {
            return RaiderTacticsParams.Default;
        }

        try
        {
            Raw raw = JsonSerializer.Deserialize<Raw>(f.GetAsText());
            return raw.ToParams();
        }
        catch (JsonException e)
        {
            GD.PushWarning($"raider_tactics.json 解析失败，用默认战术参数：{e.Message}");
            return RaiderTacticsParams.Default;
        }
    }

    // JSON 映射（字段名对应 raider_tactics.json，故意小写驼峰）。可空 → 缺字段即回落默认值。
    private readonly struct Raw
    {
        [JsonPropertyName("retreatHealthFraction")] public double? RetreatHealthFraction { get; init; }
        [JsonPropertyName("outnumberedRatio")] public double? OutnumberedRatio { get; init; }
        [JsonPropertyName("loneSurvivorHostiles")] public int? LoneSurvivorHostiles { get; init; }
        [JsonPropertyName("escapeDetourWeight")] public float? EscapeDetourWeight { get; init; }

        [JsonPropertyName("coverSearchRadius")] public float? CoverSearchRadius { get; init; }
        [JsonPropertyName("coverSampleCount")] public int? CoverSampleCount { get; init; }
        [JsonPropertyName("coverMinEnemyDistance")] public float? CoverMinEnemyDistance { get; init; }
        [JsonPropertyName("coverIdealRangeFactor")] public float? CoverIdealRangeFactor { get; init; }
        [JsonPropertyName("coverApproachWeight")] public float? CoverApproachWeight { get; init; }
        [JsonPropertyName("coverRecomputeInterval")] public double? CoverRecomputeInterval { get; init; }
        [JsonPropertyName("coverEnemyMoveInvalidate")] public float? CoverEnemyMoveInvalidate { get; init; }
        [JsonPropertyName("peekOffset")] public float? PeekOffset { get; init; }
        [JsonPropertyName("peekLeadTime")] public double? PeekLeadTime { get; init; }
        [JsonPropertyName("suppressedHunkerTime")] public double? SuppressedHunkerTime { get; init; }

        [JsonPropertyName("flankMinSquad")] public int? FlankMinSquad { get; init; }
        [JsonPropertyName("flankMarginDeg")] public float? FlankMarginDeg { get; init; }
        [JsonPropertyName("flankJitterDeg")] public float? FlankJitterDeg { get; init; }
        [JsonPropertyName("flankRadiusFactor")] public float? FlankRadiusFactor { get; init; }
        [JsonPropertyName("flankArrivalTolerance")] public float? FlankArrivalTolerance { get; init; }
        [JsonPropertyName("flankEnemyMoveInvalidate")] public float? FlankEnemyMoveInvalidate { get; init; }

        [JsonPropertyName("callRadius")] public float? CallRadius { get; init; }
        [JsonPropertyName("callCooldown")] public double? CallCooldown { get; init; }
        [JsonPropertyName("loudWeaponNoiseRadius")] public double? LoudWeaponNoiseRadius { get; init; }
        [JsonPropertyName("noiseCoversCallWindow")] public double? NoiseCoversCallWindow { get; init; }
        [JsonPropertyName("shoutNoiseRadius")] public double? ShoutNoiseRadius { get; init; }

        public RaiderTacticsParams ToParams()
        {
            RaiderTacticsParams d = RaiderTacticsParams.Default;
            return new RaiderTacticsParams
            {
                RetreatHealthFraction = RetreatHealthFraction ?? d.RetreatHealthFraction,
                OutnumberedRatio = OutnumberedRatio ?? d.OutnumberedRatio,
                LoneSurvivorHostiles = LoneSurvivorHostiles ?? d.LoneSurvivorHostiles,
                EscapeDetourWeight = EscapeDetourWeight ?? d.EscapeDetourWeight,

                CoverSearchRadius = CoverSearchRadius ?? d.CoverSearchRadius,
                CoverSampleCount = CoverSampleCount ?? d.CoverSampleCount,
                CoverMinEnemyDistance = CoverMinEnemyDistance ?? d.CoverMinEnemyDistance,
                CoverIdealRangeFactor = CoverIdealRangeFactor ?? d.CoverIdealRangeFactor,
                CoverApproachWeight = CoverApproachWeight ?? d.CoverApproachWeight,
                CoverRecomputeInterval = CoverRecomputeInterval ?? d.CoverRecomputeInterval,
                CoverEnemyMoveInvalidate = CoverEnemyMoveInvalidate ?? d.CoverEnemyMoveInvalidate,
                PeekOffset = PeekOffset ?? d.PeekOffset,
                PeekLeadTime = PeekLeadTime ?? d.PeekLeadTime,
                SuppressedHunkerTime = SuppressedHunkerTime ?? d.SuppressedHunkerTime,

                FlankMinSquad = FlankMinSquad ?? d.FlankMinSquad,
                FlankMarginDeg = FlankMarginDeg ?? d.FlankMarginDeg,
                FlankJitterDeg = FlankJitterDeg ?? d.FlankJitterDeg,
                FlankRadiusFactor = FlankRadiusFactor ?? d.FlankRadiusFactor,
                FlankArrivalTolerance = FlankArrivalTolerance ?? d.FlankArrivalTolerance,
                FlankEnemyMoveInvalidate = FlankEnemyMoveInvalidate ?? d.FlankEnemyMoveInvalidate,

                CallRadius = CallRadius ?? d.CallRadius,
                CallCooldown = CallCooldown ?? d.CallCooldown,
                LoudWeaponNoiseRadius = LoudWeaponNoiseRadius ?? d.LoudWeaponNoiseRadius,
                NoiseCoversCallWindow = NoiseCoversCallWindow ?? d.NoiseCoversCallWindow,
                ShoutNoiseRadius = ShoutNoiseRadius ?? d.ShoutNoiseRadius,
            };
        }
    }
}
