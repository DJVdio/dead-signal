using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 尸体过三个相位自动清理（用户拍板：「过了三个相位尸体会被清理掉，缓解性能压力，也给了足够的时间去搜刮尸体」）。
/// 🔴 authored 尸体（祖母/帮众/树上的哥顿）**永不清理**——她们是手写剧情，收走了剧情就凭空消失。
/// </summary>
public class CorpseDecayTests
{
    private static CorpseDecayEntry Battle(string id, int spawnTick) => new(id, spawnTick, Authored: false);
    private static CorpseDecayEntry Authored(string id, int spawnTick) => new(id, spawnTick, Authored: true);

    // ── 三个相位的搜刮窗口 ───────────────────────────────────────────────────

    [Fact]
    public void LifetimeIsThreePhases()
    {
        Assert.Equal(3, CorpseDecay.LifetimePhases);
    }

    [Theory]
    [InlineData(0, false)]   // 刚死那一刻
    [InlineData(1, false)]   // 过了 1 次相位切换：还能扒
    [InlineData(2, false)]   // 过了 2 次：还能扒（最后的机会）
    [InlineData(3, true)]    // 过了 3 次：清理
    [InlineData(9, true)]    // 早该没了
    public void BattleCorpse_ExpiresAfterThreePhaseChanges(int phasesElapsed, bool expired)
    {
        var corpse = Battle("丧尸的尸体 #1", spawnTick: 10);
        Assert.Equal(expired, CorpseDecay.IsExpired(corpse, currentPhaseTick: 10 + phasesElapsed));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(5, 0)]   // 过期后不给负数
    public void PhasesRemaining_CountsDownTheSalvageWindow(int phasesElapsed, int remaining)
    {
        var corpse = Battle("丧尸的尸体 #2", spawnTick: 4);
        Assert.Equal(remaining, CorpseDecay.PhasesRemaining(corpse, 4 + phasesElapsed));
    }

    // ── 🔴 祖母的尸体：过再多相位也不会被清理 ────────────────────────────────

    [Fact]
    public void 祖母的尸体_过再多相位也不会被清理()
    {
        var grandmother = Authored("祖母的尸体", spawnTick: 0);

        // 一个昼夜 8 个相位 → 400 次相位切换 ≈ 50 天，远超一整局
        for (int tick = 0; tick <= 400; tick++)
        {
            Assert.False(CorpseDecay.IsExpired(grandmother, tick), $"祖母在第 {tick} 次相位切换时被清理了——那段叙事就没了");
        }
        Assert.Equal(int.MaxValue, CorpseDecay.PhasesRemaining(grandmother, 400));
    }

    [Fact]
    public void Sweep_TakesTheBattleDead_AndLeavesTheAuthoredOnes()
    {
        var corpses = new List<CorpseDecayEntry>
        {
            Authored("祖母的尸体", 0),          // 剧情：永不清理
            Battle("丧尸的尸体 #1", 0),         // 早该没了
            Battle("丧尸的尸体 #2", 5),         // 刚死，还能扒
            Authored("墙角的帮众尸体", 0),      // 剧情：永不清理
            Battle("劫掠者的尸体 #3", 2),       // 到期
        };

        List<CorpseDecayEntry> expired = CorpseDecay.Sweep(corpses, currentPhaseTick: 5);

        Assert.Equal(new[] { "丧尸的尸体 #1", "劫掠者的尸体 #3" }, expired.Select(e => e.Id).ToArray());
        Assert.DoesNotContain(expired, e => e.Authored);
    }

    [Fact]
    public void Sweep_IsDeterministic_PreservesRegistrationOrder()
    {
        var corpses = Enumerable.Range(0, 20).Select(i => Battle($"#{i}", spawnTick: i % 4)).ToList();

        List<CorpseDecayEntry> a = CorpseDecay.Sweep(corpses, 4);
        List<CorpseDecayEntry> b = CorpseDecay.Sweep(corpses, 4);

        Assert.Equal(a.Select(e => e.Id), b.Select(e => e.Id));
        // spawnTick 0/1 的到期（4-0>=3, 4-1>=3），2/3 的还在
        Assert.All(a, e => Assert.True(e.SpawnPhaseTick <= 1));
    }

    [Fact]
    public void Sweep_EmptyField_ReturnsNothing()
    {
        Assert.Empty(CorpseDecay.Sweep(new List<CorpseDecayEntry>(), currentPhaseTick: 99));
    }

    // ── 搜刮窗口的设计意涵：尸潮打完那一堆，三个相位内扒不完就是扒不完 ────────

    [Fact]
    public void HordeAftermath_WholeBatchExpiresTogether_ThreePhasesLater()
    {
        // 一波尸潮：80 具尸体同一个相位倒下
        List<CorpseDecayEntry> horde = Enumerable.Range(0, 80).Select(i => Battle($"丧尸的尸体 #{i}", 12)).ToList();

        Assert.Empty(CorpseDecay.Sweep(horde, 14));         // 两个相位后：全都还在，随便扒
        Assert.Equal(80, CorpseDecay.Sweep(horde, 15).Count); // 第三个相位：整批一起消失
    }
}
