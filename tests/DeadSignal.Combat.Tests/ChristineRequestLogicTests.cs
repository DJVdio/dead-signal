using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 克莉丝汀「请求出兵清剿」支线状态机的纯逻辑单测（先红后绿）：
// 收留开线 → 递进 3 次请求门控 → 答应即停 / 累计 3 次"暂不"触发下次交替离开 → 死亡中止。
// 全部脱 Godot：只用 StoryFlags + ChristineRequestLogic 的纯字符串状态推进。
public class ChristineRequestLogicTests
{
    [Fact]
    public void Begin_SetsStateToZero_NoDeclinesNoAgreeNoLeave()
    {
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);

        Assert.Equal("0", f.Get(ChristineRequestLogic.StateKey)); // 第 1 次请求气泡门控值
        Assert.Equal(0, ChristineRequestLogic.DeclineCount(f));
        Assert.False(ChristineRequestLogic.HasAgreed(f));
        Assert.False(ChristineRequestLogic.HasPendingRequest(f));
    }

    [Fact]
    public void StateValue_GatesThreeProgressiveRequests()
    {
        // 三条请求气泡分别挂 state=="0"/"1"/"2"；每拒一次状态精确前进一格，正好对上下一条。
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);
        Assert.Equal("0", f.Get(ChristineRequestLogic.StateKey)); // 请求 1 可播

        ChristineRequestLogic.Resolve(f, agreed: false);
        Assert.Equal("1", f.Get(ChristineRequestLogic.StateKey)); // 请求 2 可播

        ChristineRequestLogic.Resolve(f, agreed: false);
        Assert.Equal("2", f.Get(ChristineRequestLogic.StateKey)); // 请求 3 可播
    }

    [Fact]
    public void Resolve_ConsumesPending()
    {
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);
        f.Set(ChristineRequestLogic.PendingKey, "true"); // 模拟请求气泡 trigger 置 pending
        Assert.True(ChristineRequestLogic.HasPendingRequest(f));

        ChristineRequestLogic.Resolve(f, agreed: false);
        Assert.False(ChristineRequestLogic.HasPendingRequest(f)); // 抉择后本次 pending 被消费
    }

    [Fact]
    public void Agree_StopsLineForever_NoLeave()
    {
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);

        bool leaves = ChristineRequestLogic.Resolve(f, agreed: true);

        Assert.False(leaves);
        Assert.True(ChristineRequestLogic.HasAgreed(f));
        Assert.Equal(0, ChristineRequestLogic.DeclineCount(f)); // "agreed" 非数字→计数归 0，永不触发离开
        Assert.False(ChristineRequestLogic.ConsumeLeaving(f));
        // 已答应后不应再有任何请求门控值（"0"/"1"/"2" 皆不再匹配）
        Assert.Equal("agreed", f.Get(ChristineRequestLogic.StateKey));
    }

    [Fact]
    public void ThreeDeclines_TriggerLeave_OnlyOnThird()
    {
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);

        Assert.False(ChristineRequestLogic.Resolve(f, agreed: false)); // 拒 1
        Assert.False(ChristineRequestLogic.Resolve(f, agreed: false)); // 拒 2
        bool leavesOnThird = ChristineRequestLogic.Resolve(f, agreed: false); // 拒 3

        Assert.True(leavesOnThird);
        Assert.Equal(3, ChristineRequestLogic.DeclineCount(f));
        Assert.True(f.Has(ChristineRequestLogic.LeavingKey));
    }

    [Fact]
    public void ConsumeLeaving_FiresOnceThenClears()
    {
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);
        ChristineRequestLogic.Resolve(f, agreed: false);
        ChristineRequestLogic.Resolve(f, agreed: false);
        ChristineRequestLogic.Resolve(f, agreed: false); // 触发离开

        Assert.True(ChristineRequestLogic.ConsumeLeaving(f));  // 下次交替：离开
        Assert.False(ChristineRequestLogic.ConsumeLeaving(f)); // 只走一次，不复触发
        Assert.False(f.Has(ChristineRequestLogic.LeavingKey));
    }

    [Fact]
    public void ConsumeLeaving_FalseWhenNotScheduled()
    {
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);
        ChristineRequestLogic.Resolve(f, agreed: false); // 只拒 1 次

        Assert.False(ChristineRequestLogic.ConsumeLeaving(f));
    }

    [Fact]
    public void Abort_ClearsAllFlags_StopsLine()
    {
        var f = new StoryFlags();
        ChristineRequestLogic.Begin(f);
        ChristineRequestLogic.Resolve(f, agreed: false);
        ChristineRequestLogic.Resolve(f, agreed: false);
        ChristineRequestLogic.Resolve(f, agreed: false); // 已排期离开

        ChristineRequestLogic.Abort(f);

        Assert.False(f.Has(ChristineRequestLogic.StateKey));
        Assert.False(f.Has(ChristineRequestLogic.PendingKey));
        Assert.False(f.Has(ChristineRequestLogic.LeavingKey));
        Assert.False(ChristineRequestLogic.ConsumeLeaving(f)); // 中止后不再离开
    }
}
