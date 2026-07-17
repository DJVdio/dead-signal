using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// **克莉丝汀**的 authored 三级专属效果「巧舌如簧」（<see cref="ChristinePerk"/>）纯逻辑单测。
/// 数值取自 wiki characters.json 克莉丝汀行（authored·非拟定，用常量引用）：
///   · L1：每相位 25% 几率不掉饥饿（入队即得）。
///   · L2：商人买入 6.25% 折扣（需她在营存活）。
///   · L3：卖出价率 60%→70%（需她在营存活）；每相位额外 10% 不掉饥饿 ⇒ [Q1] 与 L1 加算 = 35%。
/// 升级轴：加入=L1、在营存活三天=L2、清剿金手指帮=L3。随机走可注入 <see cref="SequenceRandomSource"/> 复现。
/// </summary>
public class ChristinePerkTests
{
    // ==================== 相位「不掉饥饿」几率：L1/L2=25%，L3 加算=35% ====================

    [Fact]
    public void 不掉饥饿几率_L1L2为25pct_L3加算为35pct()
    {
        Assert.Equal(0.25, ChristinePerk.L1HungerSkipChance, precision: 10);
        Assert.Equal(0.10, ChristinePerk.L3ExtraHungerSkipChance, precision: 10);

        Assert.Equal(0.25, ChristinePerk.HungerSkipChance(1), precision: 10);
        Assert.Equal(0.25, ChristinePerk.HungerSkipChance(2), precision: 10);
        // [Q1] 主 agent 拍板：同 perk 两级台阶按总量加算，25%+10% = 35%（不是独立掷的 32.5%）。
        Assert.Equal(0.35, ChristinePerk.HungerSkipChance(3), precision: 10);
    }

    // ==================== ResolveHungerPhase：命中则不掉饥饿 ====================

    [Fact]
    public void 未进食_L1命中25pct则不掉饥饿_未命中掉1()
    {
        // 命中（0.1 < 0.25）：本相位不掉饥饿（净零维持）。
        var hit = new HungerState(value: 5);
        ChristinePerk.ResolveHungerPhase(hit, ate: false, new SequenceRandomSource(0.1), isChristine: true, christineLevel: 1);
        Assert.Equal(5, hit.Value);

        // 未命中（0.9 ≥ 0.25）：正常掉 1。
        var miss = new HungerState(value: 5);
        ChristinePerk.ResolveHungerPhase(miss, ate: false, new SequenceRandomSource(0.9), isChristine: true, christineLevel: 1);
        Assert.Equal(4, miss.Value);
    }

    [Fact]
    public void L3的35pct边界_掷0点34命中不掉_0点35及以上掉1()
    {
        var hit = new HungerState(value: 5);
        ChristinePerk.ResolveHungerPhase(hit, ate: false, new SequenceRandomSource(0.34), isChristine: true, christineLevel: 3);
        Assert.Equal(5, hit.Value); // 0.34 < 0.35 命中 → 不掉

        var miss = new HungerState(value: 5);
        ChristinePerk.ResolveHungerPhase(miss, ate: false, new SequenceRandomSource(0.35), isChristine: true, christineLevel: 3);
        Assert.Equal(4, miss.Value); // 0.35 ≥ 0.35 未命中 → 掉 1

        // L1 在 0.30 处：0.30 ≥ 0.25 未命中（证明 L3 的额外 10% 确实扩大了命中带）。
        var l1 = new HungerState(value: 5);
        ChristinePerk.ResolveHungerPhase(l1, ate: false, new SequenceRandomSource(0.30), isChristine: true, christineLevel: 1);
        Assert.Equal(4, l1.Value);
    }

    [Fact]
    public void 进食相位_无衰减可跳_不掷骰净零维持()
    {
        // 进食本就净零，无衰减可跳 ⇒ 不触碰 rng（传空序列源不该抛耗尽异常）。
        var fed = new HungerState(value: 5);
        var emptyRng = new SequenceRandomSource();
        ChristinePerk.ResolveHungerPhase(fed, ate: true, emptyRng, isChristine: true, christineLevel: 3);
        Assert.Equal(5, fed.Value);
        Assert.Equal(0, emptyRng.Remaining);
    }

    [Fact]
    public void 非克莉丝汀_与普通ResolvePhase一致_且不消耗随机流()
    {
        var s = new HungerState(value: 5);
        var emptyRng = new SequenceRandomSource(); // 空：若被 Range 调用即抛
        bool starved = ChristinePerk.ResolveHungerPhase(s, ate: false, emptyRng, isChristine: false, christineLevel: 3);
        Assert.Equal(4, s.Value);
        Assert.False(starved);
        Assert.Equal(0, emptyRng.Remaining);
    }

    [Fact]
    public void 已饿死终态_命中也不复活_不掷骰()
    {
        var dead = new HungerState(value: 0);
        var emptyRng = new SequenceRandomSource();
        bool starved = ChristinePerk.ResolveHungerPhase(dead, ate: false, emptyRng, isChristine: true, christineLevel: 3);
        Assert.Equal(0, dead.Value);
        Assert.True(starved);
        Assert.Equal(0, emptyRng.Remaining); // IsStarved 短路，不掷骰
    }

    // ==================== 升级轴：加入=L1、在营三天=L2、灭帮=L3 ====================

    [Fact]
    public void 等级派生_入队L1_在营满3天L2_灭帮L3()
    {
        Assert.Equal(3, ChristinePerk.Level2ThresholdDays);

        Assert.Equal(1, ChristinePerk.EvaluateLevel(daysSurvivedInCamp: 0, goldfingerGangCleared: false));
        Assert.Equal(1, ChristinePerk.EvaluateLevel(daysSurvivedInCamp: 2, goldfingerGangCleared: false));
        Assert.Equal(2, ChristinePerk.EvaluateLevel(daysSurvivedInCamp: 3, goldfingerGangCleared: false));
        Assert.Equal(2, ChristinePerk.EvaluateLevel(daysSurvivedInCamp: 99, goldfingerGangCleared: false));
        // 灭帮 → L3（不论天数）。
        Assert.Equal(3, ChristinePerk.EvaluateLevel(daysSurvivedInCamp: 0, goldfingerGangCleared: true));
        Assert.Equal(3, ChristinePerk.EvaluateLevel(daysSurvivedInCamp: 5, goldfingerGangCleared: true));
    }

    [Fact]
    public void LevelOf_读灭帮旗标_置位后升L3()
    {
        var flags = new StoryFlags();
        Assert.False(GoldfingerDiscovery.GangCleared(flags));
        Assert.Equal(2, ChristinePerk.LevelOf(daysSurvivedInCamp: 4, flags));

        GoldfingerDiscovery.MarkGangCleared(flags);
        Assert.True(GoldfingerDiscovery.GangCleared(flags));
        Assert.Equal(3, ChristinePerk.LevelOf(daysSurvivedInCamp: 4, flags));

        // 灭帮旗标存 StoryFlags：存档往返后不丢。
        var reloaded = new StoryFlags(flags.Snapshot());
        Assert.Equal(3, ChristinePerk.LevelOf(daysSurvivedInCamp: 0, reloaded));
    }

    // ==================== 商人买入折扣：L2+在营=6.25%，否则 0 ====================

    [Fact]
    public void 商人买入折扣_L2起且在营为6点25pct_否则0()
    {
        Assert.Equal(0.0625, ChristinePerk.Level2BuyDiscount, precision: 10);

        Assert.Equal(0.0625, ChristinePerk.MerchantBuyDiscount(christineLevel: 2, aliveInCamp: true), precision: 10);
        Assert.Equal(0.0625, ChristinePerk.MerchantBuyDiscount(christineLevel: 3, aliveInCamp: true), precision: 10); // 累进保留
        Assert.Equal(0.0, ChristinePerk.MerchantBuyDiscount(christineLevel: 1, aliveInCamp: true), precision: 10);
        // [Q2] 需在营存活：她死/离营 → 折扣失效。
        Assert.Equal(0.0, ChristinePerk.MerchantBuyDiscount(christineLevel: 3, aliveInCamp: false), precision: 10);
    }

    // ==================== 商人卖出价率：L3+在营=70，否则回退默认 ====================

    [Fact]
    public void 商人卖出价率_L3起且在营为70_否则回退默认60()
    {
        Assert.Equal(70, ChristinePerk.Level3SellRatePercent);

        Assert.Equal(70, ChristinePerk.MerchantSellRatePercent(christineLevel: 3, aliveInCamp: true, defaultRatePercent: 60));
        // 未到 L3 / 不在营 → 回退默认率（零回归）。
        Assert.Equal(60, ChristinePerk.MerchantSellRatePercent(christineLevel: 2, aliveInCamp: true, defaultRatePercent: 60));
        Assert.Equal(60, ChristinePerk.MerchantSellRatePercent(christineLevel: 3, aliveInCamp: false, defaultRatePercent: 60));
    }

    // ==================== SurvivorPerks 身份标记 ====================

    [Fact]
    public void 克莉丝汀身份标记_GrantChristine置IsChristine_其余角色false()
    {
        var perks = new SurvivorPerks();
        Assert.False(perks.IsChristine);
        perks.GrantChristine();
        Assert.True(perks.IsChristine);
        Assert.Equal("克莉丝汀", ChristinePerk.ChristineName);
    }
}
