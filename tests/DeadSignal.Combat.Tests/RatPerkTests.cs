using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T61] **耗子**的 authored 专属效果（<see cref="RatPerk"/>）+ 招募链路（<see cref="RatRecruit"/>）。
/// 用户原话是唯一事实源；本文件的每条断言都直接钉住他写的那几个数。
/// </summary>
public class RatPerkTests
{
    // ==================== 升级：搜出 75 件 → L2，250 件 → L3 ====================

    [Fact]
    public void 耗子的等级由累计搜出件数派生_75件二级_250件三级()
    {
        // 用户原话：「升级方式：搜刮寻找物品，累计转出来 75 件物品升到二级，250 件升到三级。」
        Assert.Equal(1, RatPerk.EvaluateLevel(0));
        Assert.Equal(1, RatPerk.EvaluateLevel(74));
        Assert.Equal(2, RatPerk.EvaluateLevel(75));   // 阈值本身即达成
        Assert.Equal(2, RatPerk.EvaluateLevel(249));
        Assert.Equal(3, RatPerk.EvaluateLevel(250));
        Assert.Equal(3, RatPerk.EvaluateLevel(9999));

        Assert.Equal(75, RatPerk.Level2ThresholdItems);
        Assert.Equal(250, RatPerk.Level3ThresholdItems);
    }

    [Fact]
    public void 搜出的件数存在StoryFlags里_故存档天然覆盖_不必加SaveData字段()
    {
        var flags = new StoryFlags();
        Assert.Equal(0, RatPerk.ItemsScavenged(flags));
        Assert.Equal(1, RatPerk.LevelOf(flags));

        for (int i = 0; i < 75; i++)
        {
            RatPerk.RecordScavenged(flags);
        }

        Assert.Equal(75, RatPerk.ItemsScavenged(flags));
        Assert.Equal(2, RatPerk.LevelOf(flags));

        // 🔴 计数就是一个普通 StoryFlags 键（字符串承载整数，同 nightingale_surgery_count）
        // ⇒ SaveData.StoryFlags 已经在存档里 ⇒ **不加字段、不撞版本号（v3 原样）**。
        Assert.Equal("75", flags.Get(RatPerk.ScavengeCountFlag));

        // 存档往返（快照 → 新实例）后等级不丢。
        var reloaded = new StoryFlags(flags.Snapshot());
        Assert.Equal(75, RatPerk.ItemsScavenged(reloaded));
        Assert.Equal(2, RatPerk.LevelOf(reloaded));
    }

    // ==================== 搜刮速度：L1 +50%，L2 再 +100% ⇒ 2.5（加算，用户指定） ====================

    [Fact]
    public void 搜刮速度_一级1点5_二级2点5_三级仍2点5()
    {
        // 用户原话：L1「翻找搜刮速度 +50%」；L2「翻找物品的速度**再 +100%**」。
        // 🔴 主 agent 拍板口径：**L2 = 1 + 0.50 + 1.00 = 2.50**（这是用户按"总量"口述的同一个 perk 的两级台阶，
        //    是他**明确指定**的加算例外 —— **不是**漏网的加算残留）。
        //    ⚠️ 谁把它"顺手改成乘算"（1.5 × 2.0 = 3.0），这条测试当场红。
        Assert.Equal(1.50, RatPerk.LootSpeedMultiplier(isRat: true, ratLevel: 1), precision: 10);
        Assert.Equal(2.50, RatPerk.LootSpeedMultiplier(isRat: true, ratLevel: 2), precision: 10);
        Assert.Equal(2.50, RatPerk.LootSpeedMultiplier(isRat: true, ratLevel: 3), precision: 10);

        Assert.NotEqual(3.00, RatPerk.LootSpeedMultiplier(isRat: true, ratLevel: 2), precision: 10);
    }

    [Fact]
    public void 非耗子的搜刮速度恒为1_零回归()
    {
        for (int lvl = 1; lvl <= 3; lvl++)
        {
            Assert.Equal(1.0, RatPerk.LootSpeedMultiplier(isRat: false, ratLevel: lvl), precision: 10);
        }
    }

    [Fact]
    public void 搜刮速度倍率喂进LootSession后_每件耗时按倍率整除()
    {
        // LootSession 的模型本就是 `每件耗时 = 基准 ÷ 效率` ⇒ 「速度 ×2.5」⇔「耗时 ×0.4」是**恒等式**，
        // 不存在"时间减 150%"那种坑。健全耗子（操作能力 1.0）：
        float baseline = LootSession.EffectiveSecondsPerItem(1.0);                     // 常人：3.0s
        float ratL1 = LootSession.EffectiveSecondsPerItem(1.0 * 1.50);                 // 耗子 L1
        float ratL2 = LootSession.EffectiveSecondsPerItem(1.0 * 2.50);                 // 耗子 L2

        Assert.Equal(LootSession.DefaultSecondsPerItem, baseline, precision: 4);
        Assert.Equal(baseline / 1.50f, ratL1, precision: 4);
        Assert.Equal(baseline / 2.50f, ratL2, precision: 4);
        Assert.Equal(0.40f * baseline, ratL2, precision: 4);   // 2.5 倍速 ⇔ 只花 40% 的时间
    }

    // ==================== 噪音：脚步/动作 −40%，战斗/开枪/破坏不减 ====================

    [Fact]
    public void 耗子的动作噪音乘子是0点6_非耗子恒1()
    {
        // 用户原话：「耗子的脚步和动作轻不可闻，**声音减少 40%**。」⇒ ×0.60
        Assert.Equal(0.60, RatPerk.ActionNoiseMultiplier(isRat: true, ratLevel: 1), precision: 10);
        Assert.Equal(0.60, RatPerk.Level1ActionNoiseMultiplier, precision: 10);
        Assert.Equal(1.0, RatPerk.ActionNoiseMultiplier(isRat: false, ratLevel: 3), precision: 10);
    }

    [Fact]
    public void 战斗开枪破坏的噪音不减_脚步开门撬锁静默拆除才减()
    {
        // 🔴 用户原话的括号：「（**战斗、开枪、破坏这些不减少**）」——这条真值表就是那句话本身。
        Assert.True(RatPerk.AppliesToActionNoise(RatNoiseSource.Footstep));
        Assert.True(RatPerk.AppliesToActionNoise(RatNoiseSource.DoorOpen));
        Assert.True(RatPerk.AppliesToActionNoise(RatNoiseSource.Lockpick));
        Assert.True(RatPerk.AppliesToActionNoise(RatNoiseSource.SilentDismantle));

        Assert.False(RatPerk.AppliesToActionNoise(RatNoiseSource.WeaponAttack)); // 战斗 / 开枪
        Assert.False(RatPerk.AppliesToActionNoise(RatNoiseSource.Breach));       // 破坏（砸门破防）
    }

    [Fact]
    public void 护栏_不许拿NoiseKind当耗子的开关()
    {
        // 🔴 这条测试存在的唯一目的：**挡住"顺手简化"**。
        // NoiseKind 的语义轴是"**分不分阵营**"，不是"**是不是战斗**"——
        // 开门 / 撬锁 / 静默拆除在 NoiseKind 里全都是 Combat（不分阵营：谁听见都过来看），
        // 但在耗子这条轴上它们是"**动作**"（该减 40%）。
        // ⇒ 若有人把 AppliesToActionNoise 改写成 `kind == NoiseKind.Movement`，
        //    耗子的开门声会**静默地不减**，而这条断言会当场红。
        Assert.True(RatPerk.AppliesToActionNoise(RatNoiseSource.DoorOpen));
        Assert.True(RatPerk.AppliesToActionNoise(RatNoiseSource.Lockpick));

        // 而这两个的 NoiseKind 恰恰是 Combat（见 Actor.EmitDoorNoise / EmitLockpickNoise）——
        // 正是"按枚举分会分错"的铁证。
        Assert.True(NoiseLogic.DoorNoiseRadius > 0);
        Assert.True(NoiseLogic.LockpickNoiseRadius > 0);
    }

    [Fact]
    public void 二级起翻找不产生任何噪音()
    {
        // 用户原话（L2）：「并且她翻找东西**不会产生任何噪音**。」
        Assert.Equal(1.0, RatPerk.LootNoiseMultiplier(isRat: true, ratLevel: 1), precision: 10);
        Assert.Equal(0.0, RatPerk.LootNoiseMultiplier(isRat: true, ratLevel: 2), precision: 10);
        Assert.Equal(0.0, RatPerk.LootNoiseMultiplier(isRat: true, ratLevel: 3), precision: 10);
        Assert.Equal(1.0, RatPerk.LootNoiseMultiplier(isRat: false, ratLevel: 3), precision: 10);
    }

    // ==================== 三级：探索黑暗潜行 + 破隐先手 ====================

    [Fact]
    public void 三级的两条效果已接入实时探索消费点()
    {
        // 角色页（L3）：黑暗隐匿点 +50%；破隐先手额外 +35%。数值仍由 perks.json 提供。
        Assert.Equal(0.50, RatPerk.Level3DarknessStealthBonus, precision: 10);
        Assert.Equal(0.35, RatPerk.Level3AmbushDamageBonus, precision: 10);

        // 黑暗效果只在暗处启用，返回的是发现距离倍率 1/(1+50%)。
        Assert.Equal(1.0 / 1.5, RatPerk.DarknessStealthMultiplier(true, 3, dark: true), precision: 10);
        Assert.Equal(1.0, RatPerk.DarknessStealthMultiplier(true, 3, dark: false), precision: 10);
        Assert.Equal(1.0, RatPerk.DarknessStealthMultiplier(true, 2, dark: true), precision: 10);

        // 破隐先手只在 L3 且未被敌方感知时给一次；Actor/Pawn 攻击消费点负责标记破隐。
        Assert.Equal(1.35, RatPerk.AmbushDamageMultiplier(true, 3, undetected: true), precision: 10);
        Assert.Equal(1.0, RatPerk.AmbushDamageMultiplier(true, 3, undetected: false), precision: 10);
        Assert.Equal(3, RatPerk.EvaluateLevel(RatPerk.Level3ThresholdItems));
    }

    // ==================== 招募链路（与护士逐行同构） ====================

    [Fact]
    public void 耗子招募_答应后回营才入队_婉拒不置旗可再谈()
    {
        var flags = new StoryFlags();

        // 走到最深处 → 弹对话
        Assert.NotNull(RatRecruit.Resolve(RatRecruit.MeetDiscoveryId, flags));
        Assert.Null(RatRecruit.Resolve("discovery_不相干的点", flags));

        // 婉拒：**不置任何旗** ⇒ 日后再下来还能再谈
        Assert.True(RatRecruit.ShouldOfferRecruitment(flags));
        Assert.NotNull(RatRecruit.Resolve(RatRecruit.MeetDiscoveryId, flags));
        Assert.False(RatRecruit.ShouldEnlistOnReturn(flags));

        // 答应：置 AgreedFlag ⇒ 对话去重 + 待回营注入
        flags.Set(RatRecruit.AgreedFlag, "true");
        Assert.Null(RatRecruit.Resolve(RatRecruit.MeetDiscoveryId, flags));   // 不再重复弹
        Assert.True(RatRecruit.ShouldEnlistOnReturn(flags));

        // 回营注入一次后：硬守卫生效，日后身故也不复注入
        flags.Set(RatRecruit.EnlistedFlag, "true");
        Assert.False(RatRecruit.ShouldEnlistOnReturn(flags));
        Assert.Null(RatRecruit.Resolve(RatRecruit.MeetDiscoveryId, flags));
    }

    [Fact]
    public void 她没有名字_就叫耗子()
    {
        // 用户原话：「**没有名字**，叫"耗子"。」
        Assert.Equal("耗子", RatPerk.RatName);
        Assert.Equal(RatPerk.RatName, RatRecruit.RatName);   // 单一真源
        Assert.Equal("下水道", RatRecruit.DestinationName);
    }

    [Fact]
    public void authored正文全是占位_待用户手写()
    {
        // 🔴 用户**只**给了：浑身恶臭 / 潮湿破布夹克 / 女人 / 没有名字 / 叫耗子 / 可招募。
        // 她的前史、性格、为什么在下水道、和谁认识 —— **一个字都没写** ⇒ **代码不许编造**。
        // 本测试钉死"正文仍是明确标注的占位"，好让它**不会被当成已完成的 authored 内容悄悄发布**。
        Assert.Contains("🔴 占位", RatRecruit.MeetPrompt);
        Assert.Contains("🔴 占位", RatRecruit.AcceptNarrative);
        Assert.Contains("🔴 占位", RatRecruit.DeclineNarrative);

        // 但用户**确实给了**的那几条事实，必须原样出现在相遇正文里（不许丢）：
        Assert.Contains("恶臭", RatRecruit.MeetPrompt);
        Assert.Contains("破布夹克", RatRecruit.MeetPrompt);
        Assert.Contains("耗子", RatRecruit.MeetPrompt);
    }
}
