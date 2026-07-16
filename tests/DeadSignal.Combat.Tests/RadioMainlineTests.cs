using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 电台主线状态机（<see cref="RadioMainline"/>）纯逻辑（用户拍板 [SPEC-B8]）：
/// 未知情 → 已收听广播 → 持有发出设备 → (终态分岔) 已回复军方 | 已呼叫南方。
/// 终态互斥不可逆；回复军方记录回复日、回复日 +2 期满触发军袭事件钩子（一次性）。
/// 状态存 <see cref="StoryFlags"/>，判定/推进全走纯函数（幂等、不降级）。
/// </summary>
public class RadioMainlineTests
{
    // —— 初始/默认阶段 ——

    [Fact]
    public void FreshFlags_StartUnknown_NothingUnlocked()
    {
        var flags = new StoryFlags();
        Assert.Equal(RadioMainlineStage.Unknown, RadioMainline.Stage(flags));
        Assert.False(RadioMainline.HasHeardBroadcast(flags));
        Assert.False(RadioMainline.HasTransmitter(flags));
        Assert.False(RadioMainline.IsDecisionAvailable(flags));
        Assert.False(RadioMainline.HasChosenEnding(flags));
    }

    // —— 收听广播：Unknown → HeardBroadcast ——

    [Fact]
    public void MarkBroadcastHeard_AdvancesFromUnknown_Once()
    {
        var flags = new StoryFlags();
        Assert.True(RadioMainline.MarkBroadcastHeard(flags)); // 首次收听推进
        Assert.Equal(RadioMainlineStage.HeardBroadcast, RadioMainline.Stage(flags));
        Assert.True(RadioMainline.HasHeardBroadcast(flags));
        Assert.False(RadioMainline.MarkBroadcastHeard(flags)); // 再听不重复推进
    }

    [Fact]
    public void HeardBroadcast_HasNoTransmitter_NoDecisionYet()
    {
        var flags = new StoryFlags();
        RadioMainline.MarkBroadcastHeard(flags);
        Assert.False(RadioMainline.HasTransmitter(flags));
        Assert.False(RadioMainline.IsDecisionAvailable(flags)); // 只能收听，抉择要先有设备
    }

    // —— 取得发出设备：→ HasTransmitter ——

    [Fact]
    public void GrantTransmitter_FromHeard_UnlocksDecision()
    {
        var flags = new StoryFlags();
        RadioMainline.MarkBroadcastHeard(flags);
        Assert.True(RadioMainline.GrantTransmitter(flags));
        Assert.Equal(RadioMainlineStage.HasTransmitter, RadioMainline.Stage(flags));
        Assert.True(RadioMainline.HasTransmitter(flags));
        Assert.True(RadioMainline.IsDecisionAvailable(flags)); // 持设备→抉择入口
        Assert.False(RadioMainline.GrantTransmitter(flags)); // 再取无操作
    }

    [Fact]
    public void GrantTransmitter_WithoutHearing_StillWorks_RankJump()
    {
        // 发出设备由探索取得，可能先于营地收音机收听：直接 Unknown→HasTransmitter。
        var flags = new StoryFlags();
        Assert.True(RadioMainline.GrantTransmitter(flags));
        Assert.Equal(RadioMainlineStage.HasTransmitter, RadioMainline.Stage(flags));
        Assert.True(RadioMainline.HasHeardBroadcast(flags)); // rank 隐含"听过"（更后阶段）
        Assert.True(RadioMainline.IsDecisionAvailable(flags));
    }

    [Fact]
    public void MarkBroadcastHeard_DoesNotDowngradeTransmitter()
    {
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        Assert.False(RadioMainline.MarkBroadcastHeard(flags)); // 已在更后阶段，不降级
        Assert.Equal(RadioMainlineStage.HasTransmitter, RadioMainline.Stage(flags));
    }

    // —— 终态①：回复军方 ——

    [Fact]
    public void ReplyToMilitary_FromHasTransmitter_RecordsDayAndLocks()
    {
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        Assert.True(RadioMainline.ReplyToMilitary(flags, currentDay: 5));
        Assert.Equal(RadioMainlineStage.RepliedMilitary, RadioMainline.Stage(flags));
        Assert.True(RadioMainline.HasChosenEnding(flags));
        Assert.False(RadioMainline.IsDecisionAvailable(flags)); // 抉择已锁死
        Assert.Equal(5, RadioMainline.ReplyDay(flags));
    }

    [Fact]
    public void Reply_RequiresTransmitter_RejectedWhenOnlyHeard()
    {
        var flags = new StoryFlags();
        RadioMainline.MarkBroadcastHeard(flags);
        Assert.False(RadioMainline.ReplyToMilitary(flags, 3)); // 无发出设备不能回复
        Assert.Equal(RadioMainlineStage.HeardBroadcast, RadioMainline.Stage(flags));
        Assert.Null(RadioMainline.ReplyDay(flags));
    }

    [Fact]
    public void ReplyToMilitary_DoesNotSetEndgameFreeze()
    {
        // 用户口径：回复军方后尸潮时限不冻结（第 3 天军袭先到）。
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        RadioMainline.ReplyToMilitary(flags, 5);
        Assert.False(flags.Has(HordeTimeline.EndgameFreezeFlag));
    }

    // —— 终态②：呼叫南方 ——

    [Fact]
    public void CallSouth_FromHasTransmitter_OpensSouthEscapeAndLocks()
    {
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        Assert.True(RadioMainline.CallSouth(flags));
        Assert.Equal(RadioMainlineStage.CalledSouth, RadioMainline.Stage(flags));
        Assert.True(RadioMainline.HasChosenEnding(flags));
        Assert.True(flags.Has(RadioMainline.SouthEscapeOpenFlag)); // 南逃线开启
        Assert.False(RadioMainline.IsDecisionAvailable(flags));
    }

    [Fact]
    public void CallSouth_DoesNotSetEndgameFreeze()
    {
        // 用户拍板：南逃线不冻结时限，须抢在第 40 天尸潮前。
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        RadioMainline.CallSouth(flags);
        Assert.False(flags.Has(HordeTimeline.EndgameFreezeFlag));
    }

    // —— 终态互斥不可逆 ——

    [Fact]
    public void Endings_AreMutuallyExclusive_And_Irreversible()
    {
        var replied = new StoryFlags();
        RadioMainline.GrantTransmitter(replied);
        RadioMainline.ReplyToMilitary(replied, 5);
        Assert.False(RadioMainline.CallSouth(replied)); // 已回复军方后不能再呼叫南方
        Assert.Equal(RadioMainlineStage.RepliedMilitary, RadioMainline.Stage(replied));
        Assert.False(replied.Has(RadioMainline.SouthEscapeOpenFlag));

        var called = new StoryFlags();
        RadioMainline.GrantTransmitter(called);
        RadioMainline.CallSouth(called);
        Assert.False(RadioMainline.ReplyToMilitary(called, 8)); // 已呼叫南方后不能再回复军方
        Assert.Equal(RadioMainlineStage.CalledSouth, RadioMainline.Stage(called));
        Assert.Null(RadioMainline.ReplyDay(called));
    }

    [Fact]
    public void GrantTransmitter_DoesNotOverwriteTerminalChoice()
    {
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        RadioMainline.ReplyToMilitary(flags, 5);
        Assert.False(RadioMainline.GrantTransmitter(flags)); // 终态不被设备再取覆盖
        Assert.Equal(RadioMainlineStage.RepliedMilitary, RadioMainline.Stage(flags));
    }

    // —— 军方来袭倒计时边界（回复日 +2，用户拍板改自 +3）——

    [Theory]
    [InlineData(5, 6, false)] // 回复日+1：未到期
    [InlineData(5, 7, true)]  // 回复日+2：正好期满
    [InlineData(5, 8, true)]  // 回复日+3：已过期仍算到期
    [InlineData(5, 5, false)] // 当天：未到期
    public void MilitaryRaidDue_ArithmeticBoundary(int replyDay, int currentDay, bool expected)
    {
        Assert.Equal(expected, RadioMainline.MilitaryRaidDue(replyDay, currentDay));
    }

    [Fact]
    public void IsMilitaryRaidDue_FalseUnlessRepliedAndDue()
    {
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        Assert.False(RadioMainline.IsMilitaryRaidDue(flags, 100)); // 未回复军方永不到期
        RadioMainline.ReplyToMilitary(flags, 5);
        Assert.False(RadioMainline.IsMilitaryRaidDue(flags, 6)); // 回复日+1
        Assert.True(RadioMainline.IsMilitaryRaidDue(flags, 7));  // 回复日+2
    }

    [Fact]
    public void TryFireMilitaryRaidHook_FiresOnceWhenDue()
    {
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        RadioMainline.ReplyToMilitary(flags, 5);

        Assert.False(RadioMainline.TryFireMilitaryRaidHook(flags, 6)); // 未到期不触发
        Assert.False(flags.Has(RadioMainline.MilitaryRaidFiredKey));

        Assert.True(RadioMainline.TryFireMilitaryRaidHook(flags, 7)); // 期满首次触发
        Assert.True(flags.Has(RadioMainline.MilitaryRaidFiredKey));

        Assert.False(RadioMainline.TryFireMilitaryRaidHook(flags, 8)); // 已触发不重复
        Assert.False(RadioMainline.TryFireMilitaryRaidHook(flags, 40));
    }

    // —— 持久化 & 容错 ——

    [Fact]
    public void State_SurvivesFlagsSnapshotRestore()
    {
        var flags = new StoryFlags();
        RadioMainline.GrantTransmitter(flags);
        RadioMainline.ReplyToMilitary(flags, 6);

        var restored = new StoryFlags(flags.Snapshot());
        Assert.Equal(RadioMainlineStage.RepliedMilitary, RadioMainline.Stage(restored));
        Assert.Equal(6, RadioMainline.ReplyDay(restored));
        Assert.True(RadioMainline.IsMilitaryRaidDue(restored, 8)); // 回复日 6 + 2
    }

    [Fact]
    public void NullFlags_AreTolerated_DefaultUnknown()
    {
        Assert.Equal(RadioMainlineStage.Unknown, RadioMainline.Stage(null!));
        Assert.False(RadioMainline.MarkBroadcastHeard(null!));
        Assert.False(RadioMainline.GrantTransmitter(null!));
        Assert.False(RadioMainline.ReplyToMilitary(null!, 5));
        Assert.False(RadioMainline.CallSouth(null!));
        Assert.False(RadioMainline.IsMilitaryRaidDue(null!, 100));
        Assert.Null(RadioMainline.ReplyDay(null!));
    }

    // —— 文案草稿非空（供用户改，但骨架须可跑）——

    [Fact]
    public void DraftText_AllNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.MilitaryBroadcastLoop));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.DecisionPrompt));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.ReplyOptionLabel));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.CallSouthOptionLabel));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.DeferOptionLabel));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.ReplyConfirmPrompt));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.CallSouthConfirmPrompt));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.TransmitterPickupTitle));
        Assert.False(string.IsNullOrWhiteSpace(RadioMainline.TransmitterPickupNarrative));
    }
}
