using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 教学关"克莉丝汀反水"纯判定逻辑单测：反水触发条件 + 敌方剩余计数。
/// 编排（生成/飘字/切阵营/抉择面板）在 Godot 层，以编译+走读为准，本处只钉住可脱 Godot 的决策。
/// </summary>
public class TutorialRaidLogicTests
{
    // ---- 反水触发：任一劫掠者受伤较重 或 克莉丝汀受到任意伤害 ----

    [Fact]
    public void ShouldTurncoat_AllHealthy_False()
    {
        Assert.False(TutorialRaidLogic.ShouldTurncoat(new[] { 1f, 1f }, christineHealthFraction: 1f));
    }

    [Fact]
    public void ShouldTurncoat_ChristineTookAnyDamage_True()
    {
        // 克莉丝汀掉血超过 1% 即翻转。
        Assert.True(TutorialRaidLogic.ShouldTurncoat(new[] { 1f, 1f }, christineHealthFraction: 0.98f));
    }

    /// <summary>
    /// T21（用户在数值表上手改 100 → 99）：反水阈值由「满血，掉一丝血就翻」放宽到「<b>掉血超过 1% 才翻</b>」。
    /// 意图：给"擦伤级"的一两点掉血留出容错，免得教学关被一次无关刮蹭误触发反水。
    /// 故 0.995（掉 0.5%）**不该**翻转——这是新旧口径的分水岭，旧阈值(=1f)下它会翻。
    /// </summary>
    [Fact]
    public void ShouldTurncoat_ChristineGrazed_BelowOnePercent_False()
    {
        Assert.False(TutorialRaidLogic.ShouldTurncoat(new[] { 1f, 1f }, christineHealthFraction: 0.995f));
    }

    [Fact]
    public void ShouldTurncoat_RaiderWoundedBelowThreshold_True()
    {
        Assert.True(TutorialRaidLogic.ShouldTurncoat(new[] { 1f, 0.49f }, christineHealthFraction: 1f));
    }

    [Fact]
    public void ShouldTurncoat_RaiderExactlyAtThreshold_False()
    {
        // 阈值处不触发（严格小于）。
        Assert.False(TutorialRaidLogic.ShouldTurncoat(
            new[] { TutorialRaidLogic.RaiderWoundedThreshold }, christineHealthFraction: 1f));
    }

    [Fact]
    public void ShouldTurncoat_NoRaiders_ChristineHealthy_False()
    {
        Assert.False(TutorialRaidLogic.ShouldTurncoat(System.Array.Empty<float>(), christineHealthFraction: 1f));
    }

    // ---- 敌方剩余计数（喂给 RaidResolution.Evaluate 的"敌人数"）----

    [Fact]
    public void EnemiesRemaining_ChristineStillHostile_CountsHer()
    {
        Assert.Equal(3, TutorialRaidLogic.EnemiesRemaining(raidersAlive: 2, christineAliveAndHostile: true));
    }

    [Fact]
    public void EnemiesRemaining_ChristineTurnedOrDead_NotCounted()
    {
        Assert.Equal(2, TutorialRaidLogic.EnemiesRemaining(raidersAlive: 2, christineAliveAndHostile: false));
    }

    [Fact]
    public void EnemiesRemaining_AllCleared_Zero()
    {
        Assert.Equal(0, TutorialRaidLogic.EnemiesRemaining(raidersAlive: 0, christineAliveAndHostile: false));
    }
}
