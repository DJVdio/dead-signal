using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「南丁格尔的小药店」可招募护士纯逻辑单测（[SPEC-B13]，招募机制照 VillageRescue 先例）：
///   · 招募门控 ShouldOfferRecruitment（未答应且未入队 → 可弹对话；答应/入队后不再弹）；
///   · 相遇解析 Resolve（对 id 且可招募 → 出招募邀约；异 id / 已答应 / 已入队 → null）；
///   · **护士清醒可对话与道格差异**：婉拒不置任何旗 → 可反复触发对话（同一 flags 多次 Resolve 恒非空）；
///   · 回营正史入队条件 ShouldEnlistOnReturn（答应且未入队 → 应注入；入队后不复注入，含护士身故后）；
///   · 南丁格尔护士三级 perk（授予/幂等、手术台数升级阈值、三级手术基础点(她30/常人15/L3全营+5遗产)、
///     全营感染乘子的三级开闭与死亡/离营失效矩阵(1失/2失/3存)与叠加(-25%)）。
/// 数值/文案皆 draft（拟定待调、台词待用户），测试锁的是规则形态与当前拟定值。
/// </summary>
public class NurseRecruitTests
{
    // ---------------- 招募门控 ----------------

    [Fact]
    public void ShouldOfferRecruitment_FreshFlags_True()
    {
        var flags = new StoryFlags();
        Assert.True(NurseRecruit.ShouldOfferRecruitment(flags)); // 未答应未入队 → 可弹对话
    }

    [Fact]
    public void ShouldOfferRecruitment_Agreed_False()
    {
        var flags = new StoryFlags();
        flags.Set(NurseRecruit.AgreedFlag, "true");
        Assert.False(NurseRecruit.ShouldOfferRecruitment(flags)); // 已答应等回营注入，不再弹
    }

    [Fact]
    public void ShouldOfferRecruitment_Enlisted_False()
    {
        var flags = new StoryFlags();
        flags.Set(NurseRecruit.EnlistedFlag, "true");
        Assert.False(NurseRecruit.ShouldOfferRecruitment(flags)); // 已入队，不再弹
    }

    [Fact]
    public void ShouldOfferRecruitment_NullFlags_False()
    {
        Assert.False(NurseRecruit.ShouldOfferRecruitment(null!));
    }

    // ---------------- 相遇解析 ----------------

    [Fact]
    public void Resolve_MeetId_Fresh_ReturnsOffer()
    {
        var flags = new StoryFlags();
        NurseRecruitOffer? offer = NurseRecruit.Resolve(NurseRecruit.MeetDiscoveryId, flags);
        Assert.NotNull(offer);
        // 邀约装配了标题/问话/接受/婉拒文案（供 ChoicePanel）。
        Assert.Equal(NurseRecruit.MeetTitle, offer!.Value.Title);
        Assert.False(string.IsNullOrEmpty(offer.Value.Prompt));
        Assert.False(string.IsNullOrEmpty(offer.Value.AcceptLabel));
        Assert.False(string.IsNullOrEmpty(offer.Value.DeclineLabel));
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsNull()
    {
        var flags = new StoryFlags();
        Assert.Null(NurseRecruit.Resolve("discovery_not_the_nurse", flags));
    }

    [Fact]
    public void Resolve_AfterAgreed_ReturnsNull()
    {
        var flags = new StoryFlags();
        flags.Set(NurseRecruit.AgreedFlag, "true");
        Assert.Null(NurseRecruit.Resolve(NurseRecruit.MeetDiscoveryId, flags)); // 已答应：不再弹对话
    }

    [Fact]
    public void Resolve_AfterEnlisted_ReturnsNull()
    {
        var flags = new StoryFlags();
        flags.Set(NurseRecruit.EnlistedFlag, "true");
        Assert.Null(NurseRecruit.Resolve(NurseRecruit.MeetDiscoveryId, flags)); // 已入队：不再弹对话
    }

    [Fact]
    public void Resolve_Declined_StaysRepeatable()
    {
        // 护士清醒可对话与道格的关键差异：婉拒**不置任何旗**，故对话可反复触发直到答应。
        var flags = new StoryFlags();
        Assert.NotNull(NurseRecruit.Resolve(NurseRecruit.MeetDiscoveryId, flags)); // 第一次相遇
        // 玩家选"暂不"——调用方不置旗（模拟）：flags 保持原样。
        Assert.NotNull(NurseRecruit.Resolve(NurseRecruit.MeetDiscoveryId, flags)); // 再访仍可弹
        Assert.NotNull(NurseRecruit.Resolve(NurseRecruit.MeetDiscoveryId, flags)); // 再再访仍可弹
    }

    // ---------------- 回营正史入队条件 ----------------

    [Fact]
    public void ShouldEnlistOnReturn_AgreedNotEnlisted_True()
    {
        var flags = new StoryFlags();
        flags.Set(NurseRecruit.AgreedFlag, "true");
        Assert.True(NurseRecruit.ShouldEnlistOnReturn(flags));
    }

    [Fact]
    public void ShouldEnlistOnReturn_NotAgreed_False()
    {
        var flags = new StoryFlags();
        Assert.False(NurseRecruit.ShouldEnlistOnReturn(flags)); // 没答应就不该注入
    }

    [Fact]
    public void ShouldEnlistOnReturn_AlreadyEnlisted_False()
    {
        // 注入一次硬守卫：已入队（含护士日后身故 flag 仍在）不复注入。
        var flags = new StoryFlags();
        flags.Set(NurseRecruit.AgreedFlag, "true");
        flags.Set(NurseRecruit.EnlistedFlag, "true");
        Assert.False(NurseRecruit.ShouldEnlistOnReturn(flags));
    }

    [Fact]
    public void ShouldEnlistOnReturn_NullFlags_False()
    {
        Assert.False(NurseRecruit.ShouldEnlistOnReturn(null!));
    }

    // ---------------- 旗标/id/命名 自洽 ----------------

    [Fact]
    public void Flags_And_Ids_AreDistinctAndNonEmpty()
    {
        Assert.NotEqual(NurseRecruit.AgreedFlag, NurseRecruit.EnlistedFlag);
        Assert.False(string.IsNullOrEmpty(NurseRecruit.MeetDiscoveryId));
        Assert.False(string.IsNullOrEmpty(NurseRecruit.NurseName));
        // 正名：内部路由键仍"药店"，显示名为"南丁格尔的小药店"（两者不同、均非空）。
        Assert.Equal("药店", NurseRecruit.DestinationName);
        Assert.NotEqual(NurseRecruit.DestinationName, NurseRecruit.DisplayName);
        Assert.False(string.IsNullOrEmpty(NurseRecruit.DisplayName));
    }

    // ---------------- 南丁格尔护士三级 perk：授予/幂等 ----------------

    [Fact]
    public void GrantNightingale_MarksIdentity_L1ByDefault()
    {
        var perks = new SurvivorPerks();
        Assert.False(perks.IsNightingale);          // 普通角色非护士
        perks.GrantNightingale();
        Assert.True(perks.IsNightingale);           // 标记身份
        // 无手术记录 → 入队即 L1（等级由台数派生，非实例状态）。
        Assert.Equal(1, NightingalePerk.EvaluateLevel(0));
    }

    [Fact]
    public void GrantNightingale_IsIdempotent()
    {
        var perks = new SurvivorPerks();
        perks.GrantNightingale();
        perks.GrantNightingale();
        Assert.True(perks.IsNightingale); // 幂等：重复授予仍为护士
    }

    // ---------------- 升级轴＝她本人手术台数（成败都计，计数持久化走 StoryFlags） ----------------

    [Fact]
    public void EvaluateLevel_ThresholdBoundaries()
    {
        Assert.Equal(1, NightingalePerk.EvaluateLevel(0));                                       // 入队即 L1
        Assert.Equal(1, NightingalePerk.EvaluateLevel(NightingalePerk.Level2ThresholdSurgeries - 1));
        Assert.Equal(2, NightingalePerk.EvaluateLevel(NightingalePerk.Level2ThresholdSurgeries)); // 达 L2 阈值
        Assert.Equal(2, NightingalePerk.EvaluateLevel(NightingalePerk.Level3ThresholdSurgeries - 1));
        Assert.Equal(3, NightingalePerk.EvaluateLevel(NightingalePerk.Level3ThresholdSurgeries)); // 达 L3 阈值
        Assert.Equal(3, NightingalePerk.EvaluateLevel(NightingalePerk.Level3ThresholdSurgeries + 5)); // 到顶后不再涨
    }

    [Fact]
    public void RecordSurgery_PersistsCountInStoryFlags_AndLevelsUp()
    {
        var flags = new StoryFlags();
        Assert.Equal(0, NightingalePerk.SurgeriesPerformed(flags)); // 初始无计数

        // 累计到 L2 阈值：每台 +1、写回旗标（字符串承载整数），等级随台数派生。
        int count = 0;
        for (int i = 0; i < NightingalePerk.Level2ThresholdSurgeries; i++)
            count = NightingalePerk.RecordSurgery(flags);
        Assert.Equal(NightingalePerk.Level2ThresholdSurgeries, count);
        Assert.Equal(NightingalePerk.Level2ThresholdSurgeries, NightingalePerk.SurgeriesPerformed(flags)); // 持久化在旗标
        Assert.Equal(2, NightingalePerk.LevelOf(flags));

        // 继续到 L3 阈值。
        while (NightingalePerk.SurgeriesPerformed(flags) < NightingalePerk.Level3ThresholdSurgeries)
            NightingalePerk.RecordSurgery(flags);
        Assert.Equal(3, NightingalePerk.LevelOf(flags));
    }

    [Fact]
    public void SurgeryCount_FrozenWhenSheStopsOperating_L3LegacyPersists()
    {
        // 她死后计数天然冻结（调用方不再对她 RecordSurgery），持久化台数与 L3 遗产旗标照旧。
        var flags = new StoryFlags();
        while (NightingalePerk.SurgeriesPerformed(flags) < NightingalePerk.Level3ThresholdSurgeries)
            NightingalePerk.RecordSurgery(flags);
        int frozen = NightingalePerk.SurgeriesPerformed(flags);
        Assert.Equal(3, NightingalePerk.LevelOf(flags));

        // 模拟她身故：不再记手术 → 台数不变、等级不变。
        Assert.Equal(frozen, NightingalePerk.SurgeriesPerformed(flags));
        Assert.Equal(3, NightingalePerk.LevelOf(flags));
        // L3 遗产（营地层置永久旗标后）在她死/离营仍生效：感染仅遗产 ×0.90、全营手术仍 +5。
        Assert.Equal(0.90, NightingalePerk.CampInfectionMultiplier(0, nurseAliveInCamp: false, l3LegacyActive: true), 6);
        Assert.Equal(NightingalePerk.DefaultSurgeryBasePoints + NightingalePerk.CampSurgeryBaseBonus,
            NightingalePerk.SurgeryBasePoints(surgeonIsNightingale: false, l3LegacyActive: true));
    }

    // ---------------- 三级：手术基础点数（她30/常人15/L3全营+5遗产） ----------------

    [Fact]
    public void SurgeryBasePoints_NightingaleVsCommonerVsLegacy()
    {
        // 1级：她本人 30、常人 15（无 L3 遗产）。
        Assert.Equal(NightingalePerk.NightingaleSurgeryBasePoints,
            NightingalePerk.SurgeryBasePoints(surgeonIsNightingale: true, l3LegacyActive: false));   // 30
        Assert.Equal(NightingalePerk.DefaultSurgeryBasePoints,
            NightingalePerk.SurgeryBasePoints(surgeonIsNightingale: false, l3LegacyActive: false));  // 15
        // 3级遗产：全营（含常人）手术基础点 +5。
        Assert.Equal(NightingalePerk.DefaultSurgeryBasePoints + NightingalePerk.CampSurgeryBaseBonus,
            NightingalePerk.SurgeryBasePoints(surgeonIsNightingale: false, l3LegacyActive: true));   // 20
        Assert.Equal(NightingalePerk.NightingaleSurgeryBasePoints + NightingalePerk.CampSurgeryBaseBonus,
            NightingalePerk.SurgeryBasePoints(surgeonIsNightingale: true, l3LegacyActive: true));    // 35（她 30 + 遗产 5）
    }

    // ---------------- 三级：全营感染乘子——开闭/死亡离营矩阵/叠加 ----------------

    [Fact]
    public void CampInfectionMultiplier_LevelGatingAndStacking()
    {
        // L1 存活：无任何减免 → ×1.0。
        Assert.Equal(1.0, NightingalePerk.CampInfectionMultiplier(1, nurseAliveInCamp: true, l3LegacyActive: false), 6);
        // L2 存活在营：−15% → ×0.85。
        Assert.Equal(0.85, NightingalePerk.CampInfectionMultiplier(2, nurseAliveInCamp: true, l3LegacyActive: false), 6);
        // L3 存活在营：2级−15% + 3级−10% 叠加 = −25% → ×0.75（用户口径"合计-25%"）。
        Assert.Equal(0.75, NightingalePerk.CampInfectionMultiplier(3, nurseAliveInCamp: true, l3LegacyActive: true), 6);
    }

    [Fact]
    public void CampInfectionMultiplier_DeathAndAwayMatrix()
    {
        // 死亡/离营后：2级(−15%)失效（需她在营存活），仅 3级遗产(−10%)存续 → ×0.90。
        Assert.Equal(0.90, NightingalePerk.CampInfectionMultiplier(0, nurseAliveInCamp: false, l3LegacyActive: true), 6);
        // 未到 L3 就死（无遗产）：全失 → ×1.0（2级失/3级无）。
        Assert.Equal(1.0, NightingalePerk.CampInfectionMultiplier(0, nurseAliveInCamp: false, l3LegacyActive: false), 6);
        // 离营但活着、已 L2 未 L3：2级需"在营"→失效 → ×1.0。
        Assert.Equal(1.0, NightingalePerk.CampInfectionMultiplier(2, nurseAliveInCamp: false, l3LegacyActive: false), 6);
    }

    [Fact]
    public void SurgeryBaseAndInfection_UseUserFixedValues_NotDraft()
    {
        // 效果数值为用户原话非拟定：锁定 15/30/+5/−15%/−10%。
        Assert.Equal(15, NightingalePerk.DefaultSurgeryBasePoints);
        Assert.Equal(30, NightingalePerk.NightingaleSurgeryBasePoints);
        Assert.Equal(5, NightingalePerk.CampSurgeryBaseBonus);
        Assert.Equal(0.15, NightingalePerk.Level2InfectionReduction, 6);
        Assert.Equal(0.10, NightingalePerk.Level3InfectionReduction, 6);
    }
}
