using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次6 夜防「警戒力 vs 潜行力」对抗纯逻辑单测（先红后绿）。
/// 覆盖：听力距离衰减、岗哨加成、疲劳/站岗效率拉低警戒、潜行各项、对抗胜负边界、
/// 两种未发现后果（潜行先手/静默偷窃）、三档响应名册。随机全走 SequenceRandomSource 复现。
/// </summary>
public sealed class NightWatchContestTests
{
    // ── 听力：近处满、达基准归零、更远仍零，单调不增 ──
    [Fact]
    public void HearingFalloff_DecaysToZeroAtRange()
    {
        float near = NightWatchContest.HearingFalloff(0f, 200f);
        float half = NightWatchContest.HearingFalloff(100f, 200f);
        float atRange = NightWatchContest.HearingFalloff(200f, 200f);
        float beyond = NightWatchContest.HearingFalloff(400f, 200f);

        Assert.Equal(1f, near, 3);
        Assert.Equal(0.5f, half, 3);
        Assert.Equal(0f, atRange, 3);
        Assert.Equal(0f, beyond, 3);
        Assert.True(near > half && half > atRange);
    }

    // ── 视力项：看不见=0；看得见时越近越清晰；视距边缘趋 0 ──
    [Fact]
    public void VisionAcuity_ZeroWhenUnseen_HigherWhenCloser()
    {
        var cone = VisionLogic.ConeFor(1f); // 白天满档：Range=BaseRange=300
        Assert.Equal(0f, NightWatchContest.VisionAcuity(canSee: false, distance: 10f, cone), 3);

        float close = NightWatchContest.VisionAcuity(canSee: true, distance: 30f, cone);
        float far = NightWatchContest.VisionAcuity(canSee: true, distance: 270f, cone);
        Assert.True(close > far);
        Assert.True(close > 0.8f);
        Assert.True(far < 0.2f);
    }

    // ── 岗哨建筑加成抬高警戒 ──
    [Fact]
    public void ComputeAlertness_StructureBonusRaises()
    {
        float baseline = NightWatchContest.ComputeAlertness(visionAcuity: 0.5f, distance: 100f);
        float withPost = NightWatchContest.ComputeAlertness(visionAcuity: 0.5f, distance: 100f, structureBonus: 0.4f);
        Assert.True(withPost > baseline);
    }

    // ── 疲劳 debuff 拉低警戒 ──
    [Fact]
    public void ComputeAlertness_FatigueLowers()
    {
        float rested = NightWatchContest.ComputeAlertness(visionAcuity: 0.8f, distance: 50f, fatigueMultiplier: 1f);
        float tired = NightWatchContest.ComputeAlertness(visionAcuity: 0.8f, distance: 50f, fatigueMultiplier: 0.5f);
        Assert.True(tired < rested);
    }

    // ── 站岗效率系数（布鲁斯 75%，consumer 传入）拉低警戒 ──
    [Fact]
    public void ComputeAlertness_WatchEfficiencyLowers()
    {
        float human = NightWatchContest.ComputeAlertness(visionAcuity: 0.8f, distance: 50f, watchEfficiency: 1f);
        float dog = NightWatchContest.ComputeAlertness(visionAcuity: 0.8f, distance: 50f, watchEfficiency: 0.75f);
        Assert.True(dog < human);
    }

    // ── 潜行力：越暗/服饰越多/越远/遮蔽越强 → 越高 ──
    [Fact]
    public void ComputeStealth_MonotonicInEachAxis()
    {
        float mid = NightWatchContest.ComputeStealth(lightLevel: 0.5f, apparelStealthSum: 0.2f, distance: 150f, coverWeight: 0.3f);

        Assert.True(NightWatchContest.ComputeStealth(0.1f, 0.2f, 150f, 0.3f) > mid); // 更暗
        Assert.True(NightWatchContest.ComputeStealth(0.5f, 0.6f, 150f, 0.3f) > mid); // 服饰更多
        Assert.True(NightWatchContest.ComputeStealth(0.5f, 0.2f, 280f, 0.3f) > mid); // 更远
        Assert.True(NightWatchContest.ComputeStealth(0.5f, 0.2f, 150f, 0.9f) > mid); // 遮蔽更强
    }

    // ── 服饰潜行值合计：从已装列表累加目录值 ──
    [Fact]
    public void ApparelStealthSum_SumsCatalogValues()
    {
        float none = NightWatchContest.ApparelStealthSum(System.Array.Empty<string>());
        Assert.Equal(0f, none, 3);

        // 未登记的物品不贡献潜行值
        Assert.Equal(0f, NightWatchContest.ApparelStealthSum(new[] { "一体板甲" }), 3);

        // 已登记的潜行服饰应贡献正值
        float dark = NightWatchContest.ApparelStealthSum(new[] { NightWatchContest.DarkCloakId });
        Assert.True(dark > 0f);
    }

    // ── 对抗概率边界：警戒压倒→趋1；潜行压倒→趋0；相等→0.5；皆零→0.5 ──
    [Fact]
    public void DetectionChance_Boundaries()
    {
        Assert.True(NightWatchContest.DetectionChance(10f, 0.1f) > 0.95f);
        Assert.True(NightWatchContest.DetectionChance(0.1f, 10f) < 0.05f);
        Assert.Equal(0.5f, NightWatchContest.DetectionChance(3f, 3f), 3);
        Assert.Equal(0.5f, NightWatchContest.DetectionChance(0f, 0f), 3);
    }

    // ── Resolve：roll 低于命中率→发现；高于→未发现；发现时记录发现距离 ──
    [Fact]
    public void Resolve_RollBelowChanceDetects()
    {
        // alertness=3, stealth=1 → chance=0.75
        var detected = NightWatchContest.Resolve(3f, 1f, new SequenceRandomSource(0.50), encounterDistance: 120f);
        Assert.True(detected.Detected);
        Assert.Equal(0.75f, detected.DetectionChance, 3);
        Assert.Equal(120f, detected.DetectionDistance, 3);

        var missed = NightWatchContest.Resolve(3f, 1f, new SequenceRandomSource(0.90), encounterDistance: 120f);
        Assert.False(missed.Detected);
        Assert.Equal(0f, missed.DetectionDistance, 3); // 未发现无发现距离
    }

    // ── 未发现·杀戮意图 → 潜行先手 1.5x；劫掠意图无先手加成 ──
    [Fact]
    public void PreemptiveDamageMultiplier_KillerGetsBonus()
    {
        Assert.Equal(1.5f, NightWatchContest.PreemptiveDamageMultiplier(RaiderIntent.Killer), 3);
        Assert.Equal(1f, NightWatchContest.PreemptiveDamageMultiplier(RaiderIntent.Looter), 3);
    }

    // ── 未发现·劫掠意图 → 静默偷窃量级：受存量封顶，空仓偷不到 ──
    [Fact]
    public void SilentTheftAmount_CappedByStock()
    {
        Assert.Equal(0, NightWatchContest.SilentTheftAmount(0, new SequenceRandomSource()));
        // 存量 2，rng 高值 → 顶到存量 2（不超过 max 也不超过 stock）
        Assert.Equal(2, NightWatchContest.SilentTheftAmount(2, new SequenceRandomSource(0.99)));
        // 充足存量，rng 低值 → 取下限
        int low = NightWatchContest.SilentTheftAmount(20, new SequenceRandomSource(0.0));
        Assert.Equal(NightWatchContest.SilentTheftMinUnits, low);
        // 充足存量，rng 高值 → 取上限
        int high = NightWatchContest.SilentTheftAmount(20, new SequenceRandomSource(0.99));
        Assert.Equal(NightWatchContest.SilentTheftMaxUnits, high);
    }

    // ── 三档响应名册：低=仅守卫；中=除睡觉探险队外全部；高=全员 ──
    [Fact]
    public void RespondersFor_TierRosters()
    {
        var roster = new[]
        {
            new WatchMember("guard1", WatchDuty.Guard, asleep: false),
            new WatchMember("prod1", WatchDuty.Producer, asleep: false),
            new WatchMember("exp1", WatchDuty.Expedition, asleep: true),
            new WatchMember("exp2", WatchDuty.Expedition, asleep: true),
        };

        var low = NightWatchContest.RespondersFor(RaidTier.Low, roster).Select(m => m.Id).ToList();
        Assert.Equal(new[] { "guard1" }, low);

        var mid = NightWatchContest.RespondersFor(RaidTier.Medium, roster).Select(m => m.Id).ToList();
        Assert.Equal(new[] { "guard1", "prod1" }, mid); // 睡觉探险队被排除

        var high = NightWatchContest.RespondersFor(RaidTier.High, roster).Select(m => m.Id).ToList();
        Assert.Equal(new[] { "guard1", "prod1", "exp1", "exp2" }, high); // 全员含探险队
    }

    // ── 被唤醒者（参战的睡眠者）供 shift-sleep 施次相位 debuff ──
    [Fact]
    public void AwokenSleepers_OnlySleepingParticipants()
    {
        var roster = new[]
        {
            new WatchMember("guard1", WatchDuty.Guard, asleep: false),
            new WatchMember("prod1", WatchDuty.Producer, asleep: false),
            new WatchMember("exp1", WatchDuty.Expedition, asleep: true),
        };

        // 低/中危不唤醒睡眠探险队
        Assert.Empty(NightWatchContest.AwokenSleepers(RaidTier.Low, roster));
        Assert.Empty(NightWatchContest.AwokenSleepers(RaidTier.Medium, roster));

        // 高危唤醒睡眠探险队
        var awoken = NightWatchContest.AwokenSleepers(RaidTier.High, roster).Select(m => m.Id).ToList();
        Assert.Equal(new[] { "exp1" }, awoken);
    }
}
