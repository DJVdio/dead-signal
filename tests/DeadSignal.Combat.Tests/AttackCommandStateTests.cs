using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

// 防走A 攻击冷却决策纯函数（AttackCommandState.ShouldResetCooldown）的真值组合。
// 三条用户拍板规则：
//  1. 非攻击态收到攻击指令 → 重置（先走满冷却再打第一下 wind-up）。
//  2. 已在攻击态 + 同一目标 + 未移动过（右键狂点同一目标）→ 保持（不刷新冷却）。
//  3. 切换攻击目标 或 移动后再攻击 → 重置（重新 wind-up）。
public class AttackCommandStateTests
{
    // 规则 1：非攻击态一律重置，无论目标是否相同 / 是否移动过。
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void NonAttackState_AlwaysResets(bool isAttacking, bool sameTarget, bool moved)
    {
        Assert.True(AttackCommandState.ShouldResetCooldown(isAttacking, sameTarget, moved));
    }

    // 规则 2：攻击态 + 同一目标 + 未移动过 = 同一指令 → 唯一「保持」情形。
    [Fact]
    public void AttackingSameTargetNoMove_KeepsCooldown()
    {
        Assert.False(AttackCommandState.ShouldResetCooldown(isAttacking: true, sameTarget: true, movedSinceCommand: false));
    }

    // 规则 3a：攻击态 + 切换目标 → 重置。
    [Fact]
    public void AttackingSwitchTarget_Resets()
    {
        Assert.True(AttackCommandState.ShouldResetCooldown(isAttacking: true, sameTarget: false, movedSinceCommand: false));
    }

    // 规则 3b：攻击态 + 同一目标 但 移动过 → 重置（走A 后再下令重新 wind-up）。
    [Fact]
    public void AttackingSameTargetButMoved_Resets()
    {
        Assert.True(AttackCommandState.ShouldResetCooldown(isAttacking: true, sameTarget: true, movedSinceCommand: true));
    }
}
