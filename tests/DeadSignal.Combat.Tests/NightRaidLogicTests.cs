using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次6 夜防对抗运行时编排辅助纯逻辑单测（night-response，先红后绿）。
/// 覆盖：岗哨建筑加成映射、威胁规模分档、营地多守卫对抗聚合（任一发现即全营发现/空岗恒未发现/随机序列确定消费）。
/// 单守卫对抗规则本体的单测见 NightWatchContestTests。随机走 SequenceRandomSource 复现。
/// </summary>
public sealed class NightRaidLogicTests
{
    // ── 岗哨建筑加成：平地无加成、哨塔正加成、异常 <1 clamp 到 0 ──
    [Fact]
    public void StructureBonusFrom_FlatPostNoBonus_TowerPositive()
    {
        Assert.Equal(0f, NightRaidLogic.StructureBonusFrom(1.0f), 4);
        Assert.Equal(0.10f, NightRaidLogic.StructureBonusFrom(1.10f), 4);
        Assert.True(NightRaidLogic.StructureBonusFrom(1.25f) > NightRaidLogic.StructureBonusFrom(1.10f));
        Assert.Equal(0f, NightRaidLogic.StructureBonusFrom(0.5f), 4); // <1 不产生负加成
    }

    // ── 威胁规模分档：阈值边界 ──
    [Theory]
    [InlineData(1, NightRaidLogic.ThreatBand.Small)]
    [InlineData(2, NightRaidLogic.ThreatBand.Small)]
    [InlineData(3, NightRaidLogic.ThreatBand.Medium)]
    [InlineData(4, NightRaidLogic.ThreatBand.Medium)]
    [InlineData(5, NightRaidLogic.ThreatBand.Large)]
    [InlineData(9, NightRaidLogic.ThreatBand.Large)]
    public void BandFor_ThresholdBoundaries(int count, NightRaidLogic.ThreatBand expected)
        => Assert.Equal(expected, NightRaidLogic.BandFor(count));

    [Fact]
    public void ThreatLabel_NonEmptyForAllBands()
    {
        Assert.False(string.IsNullOrWhiteSpace(NightRaidLogic.ThreatLabel(NightRaidLogic.ThreatBand.Small)));
        Assert.False(string.IsNullOrWhiteSpace(NightRaidLogic.ThreatLabel(NightRaidLogic.ThreatBand.Medium)));
        Assert.False(string.IsNullOrWhiteSpace(NightRaidLogic.ThreatLabel(NightRaidLogic.ThreatBand.Large)));
    }

    // ── 营地聚合：无守卫（没人望风）恒未发现，不消耗任何 roll ──
    [Fact]
    public void ResolveCampDetection_NoGuards_NeverDetected()
    {
        var rng = new SequenceRandomSource(); // 空序列：若被掷点即抛，证明零守卫不 roll
        bool detected = NightRaidLogic.ResolveCampDetection(new float[0], stealth: 0.1f, rng);
        Assert.False(detected);
        Assert.Equal(0, rng.Remaining);
    }

    // ── 营地聚合：单守卫高警戒低潜行、掷点低于阈值 → 发现 ──
    [Fact]
    public void ResolveCampDetection_SingleGuardRollBelowChance_Detected()
    {
        // a=1,s=0 → chance=1.0；roll=0.5<1.0 → 发现。
        var rng = new SequenceRandomSource(0.5);
        Assert.True(NightRaidLogic.ResolveCampDetection(new[] { 1.0f }, stealth: 0f, rng));
        Assert.Equal(0, rng.Remaining);
    }

    // ── 营地聚合：首个守卫失手、次个守卫掷中 → 全营发现（任一即中），逐守卫各消耗一次 roll ──
    [Fact]
    public void ResolveCampDetection_AnyGuardDetects_ConsumesOnePerGuard()
    {
        // stealth=3 共享；guard1 a=1→chance=0.25，roll=0.5 失手；guard2 a=5→chance=0.625，roll=0.1 命中。
        var rng = new SequenceRandomSource(0.5, 0.1);
        bool detected = NightRaidLogic.ResolveCampDetection(new[] { 1.0f, 5.0f }, stealth: 3f, rng);
        Assert.True(detected);
        Assert.Equal(0, rng.Remaining); // 两守卫各掷一次，序列恰好耗尽（确定性）
    }

    // ── 营地聚合：已发现后仍走完剩余守卫的掷点（不 break），随机序列确定 ──
    [Fact]
    public void ResolveCampDetection_ContinuesRollingAfterDetected()
    {
        // 三守卫全高警戒，提供恰好 3 个 roll；若命中即 break 会剩 roll，若耗尽会抛——都不发生方为确定消费。
        var rng = new SequenceRandomSource(0.1, 0.2, 0.3);
        bool detected = NightRaidLogic.ResolveCampDetection(new[] { 1.0f, 1.0f, 1.0f }, stealth: 0f, rng);
        Assert.True(detected);
        Assert.Equal(0, rng.Remaining);
    }

    // ── 疲劳调整警戒：非疲劳=ComputeAlertness 原值 ──
    [Fact]
    public void FatigueAdjustedAlertness_NotFatigued_EqualsComputeAlertness()
    {
        float baseline = NightWatchContest.ComputeAlertness(0.5f, 120f, 0.1f, 1f, 1f, NightWatchContest.HearingBaseRange);
        float adj = NightRaidLogic.FatigueAdjustedAlertness(0.5f, 120f, 0.1f, 1f, fatigued: false, NightWatchContest.HearingBaseRange);
        Assert.Equal(baseline, adj, 4);
    }

    // ── 疲劳调整警戒：听力项在范围内时，疲劳按系数削去听力贡献的一部分 ──
    [Fact]
    public void FatigueAdjustedAlertness_Fatigued_ReducesByHearingPortion()
    {
        const float dist = 120f, acuity = 0.5f, structBonus = 0.1f, watchEff = 1f;
        float notFat = NightRaidLogic.FatigueAdjustedAlertness(acuity, dist, structBonus, watchEff, false, NightWatchContest.HearingBaseRange);
        float fat = NightRaidLogic.FatigueAdjustedAlertness(acuity, dist, structBonus, watchEff, true, NightWatchContest.HearingBaseRange);
        Assert.True(fat < notFat); // 疲劳削听力 → 警戒降低

        // 精确：削去的量 = 听力贡献 × (1 - 系数)
        float hearingContrib = NightWatchContest.HearingWeight
            * NightWatchContest.HearingFalloff(dist, NightWatchContest.HearingBaseRange) * watchEff;
        float expected = notFat - hearingContrib * (1f - NightRaidLogic.FatigueHearingMult);
        Assert.Equal(expected, fat, 4);
    }

    // ── 疲劳调整警戒：听力范围外（无听力可削）时，疲劳与非疲劳相等 ──
    [Fact]
    public void FatigueAdjustedAlertness_BeyondHearing_NoDifference()
    {
        float far = NightWatchContest.HearingBaseRange + 50f; // 听力衰减到 0
        float notFat = NightRaidLogic.FatigueAdjustedAlertness(0.3f, far, 0f, 1f, false, NightWatchContest.HearingBaseRange);
        float fat = NightRaidLogic.FatigueAdjustedAlertness(0.3f, far, 0f, 1f, true, NightWatchContest.HearingBaseRange);
        Assert.Equal(notFat, fat, 4);
    }
}
