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

    // ── 服饰潜行系数：全装备覆盖 + 梯度自洽（[SPEC-B18] 护甲表 20 件的补全） ──────────

    /// <summary>
    /// 覆盖率门禁：<see cref="ApparelCatalog.Defs"/> 里每一件人形穿戴品都必须在
    /// <see cref="NightWatchContest.ApparelStealth"/> 登记潜行系数（可以是 0＝已核对为中性，但不许"忘了给"）。
    /// 这条正是本轮遗留的根因防线——布夹克/牛仔外套/花衬衫当初都是新增护甲时漏登记。
    /// 新增任何护甲/穿戴品，请同时在 ApparelStealth 补一行。
    /// </summary>
    [Fact]
    public void ApparelStealth_CoversEveryWearableInCatalog()
    {
        var missing = ApparelCatalog.Defs.Keys
            .Where(name => !NightWatchContest.ApparelStealth.ContainsKey(name))
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"这些穿戴品没登记潜行系数（新增护甲须同步 NightWatchContest.ApparelStealth）：{string.Join("、", missing)}");
    }

    /// <summary>外套层梯度：布夹克(轻软) &gt; 粗布外套(基准) &gt; 牛仔外套(厚重挺括) &gt; 皮夹克(反光且吱呀，转负)。</summary>
    [Fact]
    public void ApparelStealth_OuterLayerGradient()
    {
        float cloth = NightWatchContest.ApparelStealth["布夹克"];
        float coarse = NightWatchContest.ApparelStealth["粗布外套"];
        float denim = NightWatchContest.ApparelStealth["牛仔外套"];
        float leather = NightWatchContest.ApparelStealth["皮夹克"];

        Assert.Equal(0.05f, coarse, 3); // 既定基准不得漂移
        Assert.True(cloth > coarse, "布夹克(轻软 0.3kg)应优于粗布外套");
        Assert.True(coarse > denim, "牛仔外套(厚重 0.6kg、摩擦作响)应劣于粗布外套");
        Assert.True(denim > leather, "皮夹克(表面反光、皮革吱呀)应劣于牛仔外套");
        Assert.True(leather < 0f, "皮夹克潜行为负");
    }

    /// <summary>鲜艳/刚性/沉重＝负系数：花衬衫显眼、板甲是会走路的铁罐头（潜行极差）。</summary>
    [Fact]
    public void ApparelStealth_LoudAndHeavyGearIsNegative()
    {
        float floral = NightWatchContest.ApparelStealth["花衬衫"];
        float plainShirt = NightWatchContest.ApparelStealth["长袖布衣"];
        float plate = NightWatchContest.ApparelStealth["板甲"];
        float leatherArmor = NightWatchContest.ApparelStealth["皮甲"];
        float chestPlate = NightWatchContest.ApparelStealth["皮革胸甲"];

        Assert.True(floral < 0f, "花衬衫够艳＝夜里一团彩色，潜行为负");
        Assert.True(plainShirt > 0f, "同款素色长袖布衣应为正（对照组）");

        Assert.True(plate < 0f, "板甲潜行为负");
        Assert.True(plate < leatherArmor, "板甲(15kg 金属)应比皮甲更差");
        Assert.True(leatherArmor < chestPlate, "整身皮甲应比只护胸的皮革胸甲更差");
        // 铁罐头摸黑：一件板甲足以吃掉一整件夜行斗篷的收益，还有余。
        Assert.True(plate + NightWatchContest.ApparelStealth[NightWatchContest.DarkCloakId] < 0f);
    }

    /// <summary>负系数真能压低潜行力：板甲 + 花衬衫的合计为负，同光照/距离下潜行力低于赤身。</summary>
    [Fact]
    public void ComputeStealth_NegativeApparelLowersStealth()
    {
        float bare = NightWatchContest.ApparelStealthSum(System.Array.Empty<string>());
        float tin = NightWatchContest.ApparelStealthSum(new[] { "板甲", "花衬衫" });
        Assert.True(tin < bare, "板甲+花衬衫的潜行合计应为负");

        float bareStealth = NightWatchContest.ComputeStealth(0.3f, bare, 150f, 0.2f);
        float tinStealth = NightWatchContest.ComputeStealth(0.3f, tin, 150f, 0.2f);
        Assert.True(tinStealth < bareStealth, "穿铁罐头摸黑应比赤身更容易被发现");

        // 一身潜行装反过来抬高潜行力
        float sneak = NightWatchContest.ApparelStealthSum(new[] { NightWatchContest.DarkCloakId, "软底鞋", "长袖布衣" });
        Assert.True(NightWatchContest.ComputeStealth(0.3f, sneak, 150f, 0.2f) > bareStealth);
    }
}
