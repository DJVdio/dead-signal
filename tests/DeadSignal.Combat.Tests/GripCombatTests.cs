using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

// 持握态战斗消费纯逻辑（GripCombat）：把引擎 DualWield 的持握系数并入 Actor 的实时攻速/误差角。
// 只测取值算法（有效间隔含 grip 的乘法、双持误差角 ×1.25），计时器/发射/空间归 Actor 运行时层。
public class GripCombatTests
{
    // ---- 有效出手间隔：残疾×饥饿的操作能力再乘一个 grip 攻速系数，不破坏原乘法 ----

    [Fact]
    public void Interval_OneHanded_FullOperation_UnchangedBaseline()
    {
        // 单手 ×1.0、操作能力满：等价旧行为（丧尸/劫掠者零回归的关键）。
        Assert.Equal(1.0, GripCombat.EffectiveInterval(1.0, 1.0, GripMode.OneHanded), 9);
    }

    [Fact]
    public void Interval_TwoHanded_NoBonus_SameAsOneHanded()
    {
        // 双手握**无攻速加成**（旧 ×1.15 已按用户口径删除）→ 有效间隔 == 基础冷却，与单手逐位相同。
        Assert.Equal(1.0, GripCombat.EffectiveInterval(1.0, 1.0, GripMode.TwoHanded), 9);
        Assert.Equal(GripCombat.EffectiveInterval(1.0, 1.0, GripMode.OneHanded),
            GripCombat.EffectiveInterval(1.0, 1.0, GripMode.TwoHanded), 9);
    }

    [Fact]
    public void Interval_DualWield_LengthensInterval()
    {
        // 双持 ×0.70 → 间隔更长（更慢）。
        Assert.Equal(1.0 / DualWield.AttackSpeedFactor,
            GripCombat.EffectiveInterval(1.0, 1.0, GripMode.DualWield), 9);
        Assert.True(GripCombat.EffectiveInterval(1.0, 1.0, GripMode.DualWield) > 1.0);
    }

    [Fact]
    public void Interval_KeepsDisabilityHungerMultiplication()
    {
        // 操作能力 0.5（残疾/饥饿）先把间隔翻倍，再乘 grip 系数——这条乘法链本身不因删掉双手加成而改变，
        // 只是双手那一项现在是 ×1.0。双持仍照常并入（0.5 × 0.70）。
        double operation = 0.5;
        Assert.Equal(1.0 / operation, // 单手：仍是原来的翻倍
            GripCombat.EffectiveInterval(1.0, operation, GripMode.OneHanded), 9);
        Assert.Equal(1.0 / operation, // 双手：无加成 ⇒ 与单手同值
            GripCombat.EffectiveInterval(1.0, operation, GripMode.TwoHanded), 9);
        Assert.Equal(1.0 / (operation * DualWield.AttackSpeedFactor),
            GripCombat.EffectiveInterval(1.0, operation, GripMode.DualWield), 9);
    }

    [Fact]
    public void Interval_NonPositiveOperation_FallsBackToBaseCooldown()
    {
        // 操作能力 ≤0（断双手等无法出手）→ 回落基础冷却保持正值，避免除零（此时 Actor 本就跳过出手）。
        Assert.Equal(1.0, GripCombat.EffectiveInterval(1.0, 0.0, GripMode.DualWield), 9);
        Assert.Equal(1.0, GripCombat.EffectiveInterval(1.0, -0.3, GripMode.TwoHanded), 9);
    }

    // ---- 远程误差角：仅双持两把放大 ×1.25，单手/双手一把不变 ----

    [Theory]
    [InlineData(GripMode.OneHanded)]
    [InlineData(GripMode.TwoHanded)]
    public void Spread_NonDualWield_Unchanged(GripMode grip)
    {
        Assert.Equal(8.0, GripCombat.EffectiveSpreadDegrees(8.0, grip), 9);
    }

    [Fact]
    public void Spread_DualWield_ScaledBy125()
    {
        Assert.Equal(8.0 * DualWield.RangedSpreadFactor,
            GripCombat.EffectiveSpreadDegrees(8.0, GripMode.DualWield), 9);
    }
}
