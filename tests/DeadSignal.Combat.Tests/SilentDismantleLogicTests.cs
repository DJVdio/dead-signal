using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>静默拆除</b>纯逻辑测试（围栏一格）。
///
/// ⚠️ <b>这套东西是"谁都能用"的</b>（用户口径：「同样的，玩家也可以控制角色……点击围栏时静默拆除/破坏」）：
/// <b>玩家和 AI 调的是同一个函数、同一组数值</b>。这批测试的头等大事就是把这份<b>对称性</b>钉死——
/// 谁也不能开后门（AI 拆得特别快 / 玩家拆得特别安静，都是设计失败）。
/// </summary>
public class SilentDismantleLogicTests
{
    private static readonly SilentDismantleParams P = SilentDismantleParams.Default;

    // ─────────────── 对称性：玩家和 AI 是同一套 ───────────────

    [Fact]
    public void 玩家和AI拆同一格围栏_耗时完全相同_没有后门()
    {
        // API 里根本**没有"谁在拆"这个参数** —— 对称性不是靠自觉，是靠签名保证的。
        // 这条测试真正在断言的是：SecondsFor 只吃"拆的是什么"，不吃"谁在拆"。
        double a = SilentDismantleLogic.SecondsFor(StructureTier.FenceBasic, P);
        double b = SilentDismantleLogic.SecondsFor(StructureTier.FenceBasic, P);
        Assert.Equal(a, b);
        Assert.True(a > 0);
    }

    [Fact]
    public void 静默拆除的噪音是一个常量_对谁都一样()
    {
        Assert.Equal(NoiseLogic.SilentDismantleNoiseRadius, SilentDismantleLogic.NoiseRadius);
    }

    // ─────────────── 噪音梯度：静默必须真的静默 ───────────────

    [Fact]
    public void 静默拆除必须低于丧尸嗅觉_否则它就不是静默只是慢速版破坏()
    {
        // 若拆围栏能把东西招来，它就只是"又慢又招人的破坏"——没有任何人（玩家或 AI）会选它。
        Assert.True(SilentDismantleLogic.NoiseRadius < NoiseLogic.ZombieSmellRadius); // 35 < 70
    }

    [Fact]
    public void 静默拆除比走路还轻_蹲那儿拆比走过去更不引人注意()
    {
        Assert.True(SilentDismantleLogic.NoiseRadius < NoiseLogic.WalkNoiseRadius);   // 35 < 40
    }

    [Fact]
    public void 噪音梯度完整_撬锁最轻_拆围栏略响_破坏震天响()
    {
        // 撬锁 30（金属细碎刮擦）< 拆围栏 35（撬木板 + 压着声音放下）< 走路 40 < 嗅觉 70 ≪ 破坏 180
        Assert.True(NoiseLogic.LockpickNoiseRadius < SilentDismantleLogic.NoiseRadius);
        Assert.True(SilentDismantleLogic.NoiseRadius < NoiseLogic.WalkNoiseRadius);
        Assert.True(NoiseLogic.BreachNoiseRadius > SilentDismantleLogic.NoiseRadius * 5);
    }

    // ─────────────── 什么能静默拆 ───────────────

    [Fact]
    public void 只有围栏能静默拆()
    {
        Assert.True(SilentDismantleLogic.CanDismantle(CampStructureKind.Fence, destroyed: false));
    }

    [Fact]
    public void 门不能静默拆_门要么撬锁要么砸()
    {
        // 门有它自己的两条路（撬锁 30 / 砸 180，见 DoorLogic）。给门再开一条"静默拆"是重复机制。
        Assert.False(SilentDismantleLogic.CanDismantle(CampStructureKind.Door, destroyed: false));
    }

    [Fact]
    public void 已经拆没了的不能再拆()
    {
        Assert.False(SilentDismantleLogic.CanDismantle(CampStructureKind.Fence, destroyed: true));
    }

    // ─────────────── 耗时：慢就是静默的代价 ───────────────

    [Fact]
    public void 静默拆除比砸慢得多_慢就是静默的代价()
    {
        // 砸一格基础围栏：150 血 ÷ 每击 25 × 2.5s 冷却 ≈ 15 秒。
        const double bashSeconds = 150.0 / 25.0 * 2.5;
        double quiet = SilentDismantleLogic.SecondsFor(StructureTier.FenceBasic, P);
        Assert.True(quiet >= bashSeconds * 3,
            $"静默 {quiet}s 应至少是砸 {bashSeconds}s 的三倍——不然没人会选砸（也就没有取舍了）");
    }

    [Fact]
    public void 围栏越结实越难无声拆掉()
    {
        double basic = SilentDismantleLogic.SecondsFor(StructureTier.FenceBasic, P);
        double reinforced = SilentDismantleLogic.SecondsFor(StructureTier.FenceReinforced, P);
        double full = SilentDismantleLogic.SecondsFor(StructureTier.FenceFullMetal, P);
        Assert.True(basic < reinforced);
        Assert.True(reinforced < full);
    }

    // ─────────────── 拆完 = 一个真的洞 ───────────────

    [Fact]
    public void 拆完一格就是一个货真价实的洞_整格满血一次性抹掉()
    {
        // 不另造"洞"的概念：完成 = 对那一格施加满血伤害 → 走既有 DestroyStructure 链路。
        Assert.Equal(CampStructureTable.MaxHp(StructureTier.FenceBasic),
            SilentDismantleLogic.DamageFor(StructureTier.FenceBasic));
        Assert.Equal(CampStructureTable.MaxHp(StructureTier.FenceFullMetal),
            SilentDismantleLogic.DamageFor(StructureTier.FenceFullMetal));
    }

    [Fact]
    public void 静默拆除不返任何材料_潜入者不是拆迁队()
    {
        // 与 SalvageLogic（拆自己营地的东西回收 50% 材料）严格分家：
        // 那是**建造经济**；这是**潜入手段**。混在一起会变成"拆敌营围栏还能顺走木料"的荒诞。
        Assert.False(SilentDismantleLogic.YieldsMaterials);
    }

    // ─────────────── 发现窗口（守夜人 / 哨兵）───────────────

    [Fact]
    public void 拆得越久_守夜人机会越多()
    {
        int basic = SilentDismantleLogic.DetectionRolls(
            SilentDismantleLogic.SecondsFor(StructureTier.FenceBasic, P), P);
        int full = SilentDismantleLogic.DetectionRolls(
            SilentDismantleLogic.SecondsFor(StructureTier.FenceFullMetal, P), P);

        Assert.Equal(3, basic);       // 45s / 15s
        Assert.True(full > basic);    // 升级围栏的第二重价值：更难被悄悄拆开
    }

    [Fact]
    public void 再快的活也至少给一次发现机会()
    {
        Assert.Equal(1, SilentDismantleLogic.DetectionRolls(0.5, P));
    }

    [Fact]
    public void 没人看着_拆到天亮也没人发现()
    {
        // ★ 这条对玩家和 AI **同时**成立，这才是对称：
        //   玩家潜入敌营时，敌营没哨兵 → 玩家随便拆；玩家营地没守夜人 → 劫掠者随便拆。
        Assert.Equal(0.0, SilentDismantleLogic.CumulativeDetection(0.0, rolls: 99), 6);
    }

    [Fact]
    public void 有人看着_慢的那条路大概率被撞见()
    {
        // watchcal：裸营/裸哨 单次约 48% → 3 次机会累计约 86%
        Assert.True(SilentDismantleLogic.CumulativeDetection(0.48, rolls: 3) > 0.80);
    }

    [Fact]
    public void 参数可整体替换_不硬编码魔法数()
    {
        var slow = P with { SecondsBase = 200 };
        Assert.True(SilentDismantleLogic.DetectionRolls(
            SilentDismantleLogic.SecondsFor(StructureTier.FenceBasic, slow), slow) > 3);
    }

    // ─────────────── 单一真源：AI 侧不许另算一套 ───────────────

    [Fact]
    public void AI的入侵决策必须复用同一套拆除数值_不许自己另算()
    {
        // IntrusionLogic（AI 的手段选择）里的拆除耗时/伤害/噪音，必须**逐字**等于通用 API 的值。
        // 这条防的是"日后有人在 AI 那边偷偷改快 10 秒"——对称性会悄无声息地崩掉。
        var ip = IntrusionParams.Default;
        foreach (StructureTier t in new[]
                 {
                     StructureTier.FenceBasic, StructureTier.FenceReinforced,
                     StructureTier.FenceSheetMetal, StructureTier.FenceFullMetal,
                 })
        {
            Assert.Equal(SilentDismantleLogic.SecondsFor(t, ip.Dismantle), IntrusionLogic.DismantleSeconds(t, ip));
            Assert.Equal(SilentDismantleLogic.DamageFor(t), IntrusionLogic.DismantleDamage(t));
        }
        Assert.Equal(SilentDismantleLogic.NoiseRadius, IntrusionLogic.QuietNoiseRadius);
    }
}
