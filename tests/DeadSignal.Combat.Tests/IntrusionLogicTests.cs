using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 劫掠者<b>安静入侵</b>纯逻辑测试（撬锁 / 轻声拆围栏 / 什么时候改砸 / 守夜人有几次机会发现）。
///
/// ⚠️ <b>这批测试守的是一条反退化设计</b>（用户口径）：
/// 劫掠者若只会砸门（噪音 180，一砸就把你吵醒），<b>玩家的最优解就是"根本不派守夜人"</b>——
/// 反正敌人来了会自己敲锣打鼓通知你。那整套岗哨/夜防系统就全白搭。
/// 有了安静入侵：<b>你不派人看着，他们就无声无息地进来了</b>。守夜从"可选的保险"变成"不派就死"。
/// </summary>
public class IntrusionLogicTests
{
    private static readonly IntrusionParams P = IntrusionParams.Default;

    private static IntrusionSituation Sit(
        bool detected = false,
        bool lockpicks = true,
        bool lockedDoor = true,
        LockTier doorLock = LockTier.Standard,
        bool fence = true,
        StructureTier fenceTier = StructureTier.FenceBasic,
        double untilDawn = 600,          // 夜里还很长
        int failedPicks = 0) => new()
        {
            Detected = detected,
            HasLockpicks = lockpicks,
            LockedDoorNearby = lockedDoor,
            DoorLock = doorLock,
            FenceNearby = fence,
            FenceTier = fenceTier,
            SecondsUntilDawn = untilDawn,
            FailedPickAttempts = failedPicks,
        };

    // ─────────────────── 噪音：安静必须真的安静 ───────────────────

    [Fact]
    public void 安静入侵的噪音必须低于丧尸嗅觉_否则这机制不存在()
    {
        // 若"安静"入侵能把东西招来，它就只是"慢速版砸门"——又慢又要工具还照样惊动全场。
        // 那没有任何劫掠者会选它，整条反退化设计当场作废。
        Assert.True(IntrusionLogic.QuietNoiseRadius < NoiseLogic.ZombieSmellRadius,  // 30 < 70
            "安静入侵的噪音必须低于丧尸嗅觉半径");
        Assert.True(IntrusionLogic.QuietNoiseRadius < NoiseLogic.WalkNoiseRadius,    // 30 < 40
            "安静入侵应当比走路还轻——蹲在那儿撬/拆，比走过去更不引人注意");
    }

    [Fact]
    public void 砸的噪音远大于安静入侵_这就是两条路的全部区别()
    {
        Assert.True(NoiseLogic.BreachNoiseRadius > IntrusionLogic.QuietNoiseRadius * 5); // 180 vs 30
    }

    // ─────────────────── 决策：默认安静，被逼才砸 ───────────────────

    [Fact]
    public void 没被发现且有工具_优先撬锁()
    {
        Assert.Equal(IntrusionMethod.PickLock, IntrusionLogic.Choose(Sit(), P));
    }

    [Fact]
    public void 已经被发现_直接砸_哪怕手里有铁丝()
    {
        // 都交火了还蹲那儿撬锁 = 送死。安静的意义在于"没人知道"，这个前提没了就别装了。
        Assert.Equal(IntrusionMethod.Bash, IntrusionLogic.Choose(Sit(detected: true), P));
    }

    [Fact]
    public void 没有撬锁工具_退而轻声拆围栏_而不是直接砸()
    {
        // 拆围栏不需要任何工具——这是它存在的理由（代价是慢得多）。
        Assert.Equal(IntrusionMethod.QuietDismantle,
            IntrusionLogic.Choose(Sit(lockpicks: false), P));
    }

    [Fact]
    public void 撬断了两次_放弃撬锁_改砸()
    {
        // 撬不开就是撬不开。但这时候他已经在门口蹲了十几秒了——你本来有机会发现他。
        Assert.Equal(IntrusionMethod.Bash,
            IntrusionLogic.Choose(Sit(fence: false, failedPicks: 2), P));
    }

    [Fact]
    public void 撬断两次但旁边还有围栏_先去拆围栏_仍然不砸()
    {
        Assert.Equal(IntrusionMethod.QuietDismantle,
            IntrusionLogic.Choose(Sit(failedPicks: 2), P));
    }

    [Fact]
    public void 天快亮了_来不及慢慢来_改砸()
    {
        // 没这条，天都亮了他还蹲在那儿轻手轻脚地拆。
        // 拆基础围栏要 45s，×1.5 余量 = 67.5s；只剩 30s → 来不及
        Assert.Equal(IntrusionMethod.Bash,
            IntrusionLogic.Choose(Sit(lockpicks: false, untilDawn: 30), P));
    }

    [Fact]
    public void 时间刚好够撬锁但不够拆围栏_那就撬锁()
    {
        // 撬普通锁 6s × 1.5 = 9s；拆围栏 45s × 1.5 = 67.5s。剩 20s → 只够撬。
        Assert.Equal(IntrusionMethod.PickLock,
            IntrusionLogic.Choose(Sit(untilDawn: 20), P));
    }

    [Fact]
    public void 门和围栏都不挡路了_不再入侵_交回常规追击()
    {
        Assert.Equal(IntrusionMethod.None,
            IntrusionLogic.Choose(Sit(lockedDoor: false, fence: false), P));
    }

    [Fact]
    public void 绝不无脑选最快的_有时间就一定走安静路线()
    {
        // 反退化的核心：砸永远最快（15s vs 45s），若按"最快"决策就会退化回"总是砸门"。
        // 只要没被发现、时间够，就绝不砸。
        foreach (LockTier tier in new[] { LockTier.Simple, LockTier.Standard, LockTier.Sturdy })
        {
            Assert.NotEqual(IntrusionMethod.Bash,
                IntrusionLogic.Choose(Sit(doorLock: tier, untilDawn: 3600), P));
        }
        Assert.NotEqual(IntrusionMethod.Bash,
            IntrusionLogic.Choose(Sit(lockpicks: false, untilDawn: 3600), P));
    }

    // ─────────────────── 拆围栏：慢就是代价 ───────────────────

    [Fact]
    public void 轻声拆围栏比砸慢得多_慢就是安静的代价()
    {
        // 砸一格基础围栏：150 血 ÷ 每击 25 × 2.5s 冷却 = 6 击 × 2.5 = 约 15 秒。
        const double bashSeconds = 150.0 / 25.0 * 2.5;
        double quiet = IntrusionLogic.DismantleSeconds(StructureTier.FenceBasic, P);

        Assert.True(quiet >= bashSeconds * 3,
            $"安静拆除 {quiet}s 应至少是砸 {bashSeconds}s 的三倍——不然没人会选砸");
    }

    [Fact]
    public void 围栏越结实_越难无声拆掉()
    {
        double basic = IntrusionLogic.DismantleSeconds(StructureTier.FenceBasic, P);
        double reinforced = IntrusionLogic.DismantleSeconds(StructureTier.FenceReinforced, P);
        double metal = IntrusionLogic.DismantleSeconds(StructureTier.FenceFullMetal, P);

        Assert.True(basic < reinforced);
        Assert.True(reinforced < metal);
        // 升级围栏因此有了第二重价值：不只是更耐砸，也**更难被悄悄拆开**（守夜人的机会更多）
        Assert.True(IntrusionLogic.DetectionRolls(metal, P) > IntrusionLogic.DetectionRolls(basic, P));
    }

    [Fact]
    public void 拆完一格就是一个货真价实的洞_整格满血一次性抹掉()
    {
        // 不另造"洞"的概念：拆除完成 = 对那一格施加它的满血伤害 → 复用既有摧毁链路。
        Assert.Equal(CampStructureTable.MaxHp(StructureTier.FenceBasic),
            IntrusionLogic.DismantleDamage(StructureTier.FenceBasic));
        Assert.Equal(CampStructureTable.MaxHp(StructureTier.FenceFullMetal),
            IntrusionLogic.DismantleDamage(StructureTier.FenceFullMetal));
    }

    // ─────────────────── 守夜人的机会（本条的核心平衡点）───────────────────

    [Fact]
    public void 拆围栏给守夜人三次机会_撬锁只给一次()
    {
        // 这是两条路真正的取舍：撬锁快（守夜人只有一次机会撞见你），拆围栏慢（三次）。
        Assert.Equal(3, IntrusionLogic.DetectionRolls(
            IntrusionLogic.DismantleSeconds(StructureTier.FenceBasic, P), P));   // 45s / 15s
        Assert.Equal(1, IntrusionLogic.DetectionRolls(DoorLogic.PickSeconds(LockTier.Standard), P)); // 6s
    }

    [Fact]
    public void 再快的活也至少给一次机会_不存在零窗口的入侵()
    {
        Assert.Equal(1, IntrusionLogic.DetectionRolls(0.5, P));
        Assert.Equal(1, IntrusionLogic.DetectionRolls(0, P));
    }

    [Fact]
    public void 不派任何守夜人_拆到天亮也没人看得见()
    {
        // ★ 这条就是整条设计的目的：没人守夜 ⇒ 警戒力 0 ⇒ 单次发现率 0 ⇒ 累计发现率恒 0。
        //   劫掠者无声无息地进来，直接摸到睡觉的人和仓库。**不派岗哨就是死。**
        Assert.Equal(0.0, IntrusionLogic.CumulativeDetection(0.0, rolls: 3), 6);
        Assert.Equal(0.0, IntrusionLogic.CumulativeDetection(0.0, rolls: 999), 6);
    }

    [Fact]
    public void 派了守夜人_慢的那条路大概率会被撞见()
    {
        // watchcal 校准值：裸营守夜人单次发现率约 48%。拆围栏给他 3 次机会 → 累计约 86%。
        double bare = IntrusionLogic.CumulativeDetection(0.48, rolls: 3);
        Assert.True(bare > 0.80, $"裸营守夜人对慢速拆围栏的累计发现率 {bare:P0} 应当很高——他是称职的");

        // 满配守夜人（85%）：一次就基本抓住了
        Assert.True(IntrusionLogic.CumulativeDetection(0.85, rolls: 1) > 0.80);
    }

    [Fact]
    public void 撬锁是劫掠者的技术路线_它买的就是更少的暴露窗口()
    {
        // 同一个裸营守夜人：面对撬锁（1 次机会）只有 48%，面对拆围栏（3 次）有 86%。
        // ⇒ 带着铁丝的劫掠者明显更难防。这是"工具 = 优势"的正确表达。
        double vsPick = IntrusionLogic.CumulativeDetection(0.48, IntrusionLogic.DetectionRolls(
            DoorLogic.PickSeconds(LockTier.Standard), P));
        double vsDismantle = IntrusionLogic.CumulativeDetection(0.48, IntrusionLogic.DetectionRolls(
            IntrusionLogic.DismantleSeconds(StructureTier.FenceBasic, P), P));

        Assert.True(vsPick < vsDismantle);
        Assert.InRange(vsPick, 0.40, 0.55);
    }

    [Fact]
    public void 入侵参数可整体替换_不硬编码魔法数()
    {
        // 拆除数值现在**只有一处真源**（SilentDismantleParams，玩家和 AI 共用）——
        // AI 侧改不了它自己那一份，只能换掉整个通用参数。这正是对称性的结构保证。
        var patient = P with { Dismantle = P.Dismantle with { SecondsBase = 200 } };
        Assert.True(IntrusionLogic.DetectionRolls(
            IntrusionLogic.DismantleSeconds(StructureTier.FenceBasic, patient), patient) > 3);
    }
}
