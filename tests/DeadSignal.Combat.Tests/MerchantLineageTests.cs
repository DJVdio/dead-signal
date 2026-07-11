using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 神秘商人接替链状态机（<see cref="MerchantLineage"/>）纯逻辑（用户拍板 [SPEC-B7] 第5条）：
/// 第一商人在任 → 死于营地 → 第二商人接替(首访台词) → 第二商人再死 → 永久断商（调度不再排访）。
/// 状态存 <see cref="StoryFlags"/>，判定/推进全走纯函数。
/// </summary>
public class MerchantLineageTests
{
    // —— 初始/默认阶段 ——

    [Fact]
    public void FreshFlags_StartAtFirstMerchant_AvailableAndNotSecond()
    {
        var flags = new StoryFlags();
        Assert.Equal(MerchantLineageStage.First, MerchantLineage.Stage(flags));
        Assert.True(MerchantLineage.MerchantsAvailable(flags));
        Assert.False(MerchantLineage.IsSecondMerchant(flags));
        Assert.False(MerchantLineage.ShouldPlaySecondIntro(flags)); // 第一商人不播接替台词
    }

    // —— 第一商人死于营地 → 第二商人接替 ——

    [Fact]
    public void FirstDies_TransitionsToSecond_StillAvailable()
    {
        var flags = new StoryFlags();
        MerchantLineageStage after = MerchantLineage.OnMerchantDiedAtCamp(flags);
        Assert.Equal(MerchantLineageStage.Second, after);
        Assert.Equal(MerchantLineageStage.Second, MerchantLineage.Stage(flags));
        Assert.True(MerchantLineage.MerchantsAvailable(flags)); // 还有第二个商人
        Assert.True(MerchantLineage.IsSecondMerchant(flags));
    }

    [Fact]
    public void SecondMerchant_PlaysIntroOnce_ThenNeverAgain()
    {
        var flags = new StoryFlags();
        MerchantLineage.OnMerchantDiedAtCamp(flags); // → Second

        Assert.True(MerchantLineage.ShouldPlaySecondIntro(flags)); // 首访该播
        MerchantLineage.MarkSecondIntroPlayed(flags);
        Assert.False(MerchantLineage.ShouldPlaySecondIntro(flags)); // 再访不重播
    }

    [Fact]
    public void SecondIntroLine_IsNonEmptyDraft()
    {
        Assert.False(string.IsNullOrWhiteSpace(MerchantLineage.SecondMerchantIntroLine));
    }

    // —— 第二商人再死 → 永久断商 ——

    [Fact]
    public void SecondDies_TransitionsToExtinct_PermanentlyUnavailable()
    {
        var flags = new StoryFlags();
        MerchantLineage.OnMerchantDiedAtCamp(flags); // First → Second
        MerchantLineageStage after = MerchantLineage.OnMerchantDiedAtCamp(flags); // Second → Extinct

        Assert.Equal(MerchantLineageStage.Extinct, after);
        Assert.Equal(MerchantLineageStage.Extinct, MerchantLineage.Stage(flags));
        Assert.False(MerchantLineage.MerchantsAvailable(flags)); // 今后永无商人
        Assert.False(MerchantLineage.IsSecondMerchant(flags));
        Assert.False(MerchantLineage.ShouldPlaySecondIntro(flags));
    }

    [Fact]
    public void Extinct_IsIdempotent_OnFurtherDeaths()
    {
        var flags = new StoryFlags();
        MerchantLineage.OnMerchantDiedAtCamp(flags); // Second
        MerchantLineage.OnMerchantDiedAtCamp(flags); // Extinct
        MerchantLineageStage after = MerchantLineage.OnMerchantDiedAtCamp(flags); // 再触发无操作
        Assert.Equal(MerchantLineageStage.Extinct, after);
        Assert.False(MerchantLineage.MerchantsAvailable(flags));
    }

    // —— 持久化：状态跨 StoryFlags 快照/恢复保真 ——

    [Fact]
    public void Stage_SurvivesFlagsSnapshotRestore()
    {
        var flags = new StoryFlags();
        MerchantLineage.OnMerchantDiedAtCamp(flags); // Second
        MerchantLineage.MarkSecondIntroPlayed(flags);

        var restored = new StoryFlags(flags.Snapshot());
        Assert.Equal(MerchantLineageStage.Second, MerchantLineage.Stage(restored));
        Assert.False(MerchantLineage.ShouldPlaySecondIntro(restored)); // intro-played 也随快照保真
    }

    [Fact]
    public void NullFlags_AreTolerated_DefaultFirst()
    {
        Assert.Equal(MerchantLineageStage.First, MerchantLineage.Stage(null!));
        Assert.True(MerchantLineage.MerchantsAvailable(null!));
        Assert.Equal(MerchantLineageStage.First, MerchantLineage.OnMerchantDiedAtCamp(null!));
    }
}
