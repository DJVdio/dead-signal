using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「南林村庄」道格布鲁斯正史入队纯逻辑单测（[SPEC-B11]）：
///   · 中距离吠叫触发（任一成员进中距离且未吠 → 起吠；已吠不重复；离范围可判停）；
///   · 锁屋救援解析（踏门→出救援叙事；已救过去重返回 null；异 id 不响应）；
///   · 回营正史入队条件（救过且未入队 → 应注入；入队后不再注入，含道格身故后不复注入）；
///   · "饿昏迷"饥饿低档（HungerState/DogHungerState.DrainTo 仅降不升，压到营养不良档）。
/// 数值皆"拟定待调"，测试锁的是规则形态与当前拟定值。
/// </summary>
public class VillageRescueTests
{
    // ---------------- 中距离吠叫触发 ----------------

    [Fact]
    public void ShouldStartBarking_TeamEntersMidRange_StartsBark()
    {
        // 任一成员进入中距离（≤ 半径）且尚未吠叫 → 起吠。
        Assert.True(VillageRescue.ShouldStartBarking(VillageRescue.BarkTriggerRadius - 1f, alreadyBarking: false));
        Assert.True(VillageRescue.ShouldStartBarking(VillageRescue.BarkTriggerRadius, alreadyBarking: false)); // 边界含
    }

    [Fact]
    public void ShouldStartBarking_TooFar_NoBark()
    {
        Assert.False(VillageRescue.ShouldStartBarking(VillageRescue.BarkTriggerRadius + 1f, alreadyBarking: false));
    }

    [Fact]
    public void ShouldStartBarking_AlreadyBarking_DoesNotRestart()
    {
        // 已在吠叫（起吠是一次性）：即便仍在范围内也不再"起吠"（续吠由 InBarkRange 判）。
        Assert.False(VillageRescue.ShouldStartBarking(0f, alreadyBarking: true));
    }

    [Fact]
    public void InBarkRange_TracksDistanceOnly()
    {
        Assert.True(VillageRescue.InBarkRange(VillageRescue.BarkTriggerRadius));
        Assert.False(VillageRescue.InBarkRange(VillageRescue.BarkTriggerRadius + 0.1f));
    }

    // ---------------- 锁屋救援解析 ----------------

    [Fact]
    public void Resolve_FirstBreachIn_ReturnsRescueNarrative()
    {
        var flags = new StoryFlags();
        RescueOutcome? r = VillageRescue.Resolve(VillageRescue.RescueDiscoveryId, flags);
        Assert.NotNull(r);
        Assert.Equal(VillageRescue.RescuedFlag, r!.Value.StoryFlag);
        Assert.Equal(VillageRescue.RescueTitle, r.Value.Title);
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Narrative));
        // Resolve 不写 flag（由调用方置）：连调仍非空。
        Assert.NotNull(VillageRescue.Resolve(VillageRescue.RescueDiscoveryId, flags));
    }

    [Fact]
    public void Resolve_AlreadyRescued_ReturnsNull()
    {
        var flags = new StoryFlags();
        flags.Set(VillageRescue.RescuedFlag, "true");
        Assert.Null(VillageRescue.Resolve(VillageRescue.RescueDiscoveryId, flags)); // 去重
    }

    [Fact]
    public void Resolve_UnknownDiscoveryId_ReturnsNull()
    {
        Assert.Null(VillageRescue.Resolve("discovery_not_the_village", new StoryFlags()));
    }

    // ---------------- 回营正史入队条件 ----------------

    [Fact]
    public void ShouldEnlistOnReturn_RescuedNotYetEnlisted_True()
    {
        var flags = new StoryFlags();
        flags.Set(VillageRescue.RescuedFlag, "true");
        Assert.True(VillageRescue.ShouldEnlistOnReturn(flags));
    }

    [Fact]
    public void ShouldEnlistOnReturn_NotRescued_False()
    {
        Assert.False(VillageRescue.ShouldEnlistOnReturn(new StoryFlags()));
    }

    [Fact]
    public void ShouldEnlistOnReturn_AlreadyEnlisted_FalseEvenIfDougLaterDies()
    {
        // 入队一次硬守卫：注入后置 EnlistedFlag，即便道格日后身故（游戏侧 _doug 置 null），也不再注入。
        var flags = new StoryFlags();
        flags.Set(VillageRescue.RescuedFlag, "true");
        flags.Set(VillageRescue.EnlistedFlag, "true");
        Assert.False(VillageRescue.ShouldEnlistOnReturn(flags));
    }

    // ---------------- "饿昏迷"饥饿低档 ----------------

    [Fact]
    public void HungerDrainTo_MalnourishedOnEnlist_LowersToTarget()
    {
        var h = new HungerState(); // 5 正常
        h.DrainTo(VillageRescue.DougEnlistHunger);
        Assert.Equal(VillageRescue.DougEnlistHunger, h.Value);
        Assert.Equal(HungerLevel.Malnourished, h.Level); // 饿昏迷=营养不良档
        Assert.False(h.IsStarved);                        // 虚弱但未死
    }

    [Fact]
    public void HungerDrainTo_OnlyLowersNeverRaises()
    {
        var h = new HungerState(value: 0); // 已饿死
        h.DrainTo(VillageRescue.DougEnlistHunger); // 目标更高：不生效（只饿不喂）
        Assert.Equal(0, h.Value);
        Assert.True(h.IsStarved);
    }

    [Fact]
    public void DogHungerDrainTo_BruceLowOnEnlist()
    {
        var d = new DogHungerState(); // 满 6
        d.DrainTo(VillageRescue.BruceEnlistHunger);
        Assert.Equal(VillageRescue.BruceEnlistHunger, d.Value);
        Assert.False(d.IsStarved);
    }

    [Fact]
    public void SiegeZombieCount_InUserRange()
    {
        // 用户拟定 4~6：锁住当前拟定值在区间内。
        Assert.InRange(VillageRescue.SiegeZombieCount, 4, 6);
    }
}
