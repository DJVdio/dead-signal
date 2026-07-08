namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 GameOverCondition.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载「防走A」攻击冷却的重置决策：收到攻击指令时，是否把攻击计时器重置为满有效间隔
// （先走满冷却再打第一下 wind-up）。只做布尔决策，计时器/空间/有效间隔计算归 Actor 运行时层。

/// <summary>
/// 攻击指令冷却重置决策（防走A）。用户拍板三规则：
/// <list type="number">
///   <item>非攻击态收到攻击指令 → 重置（先走满冷却再打第一下；1 秒攻速→先等 1 秒）。</item>
///   <item>已在攻击态 + 同一目标 + 未移动过（右键狂点同一目标）→ 保持，不刷新冷却（防卡冷却）。</item>
///   <item>切换攻击目标 或 移动后再攻击 → 重置（重新 wind-up）。</item>
/// </list>
/// </summary>
public static class AttackCommandState
{
    /// <summary>
    /// 收到攻击指令时是否应把攻击冷却重置为满有效间隔。
    /// 唯一「保持」情形 = 攻击态 且 同一目标 且 未移动过（即同一指令的重复下达）；其余一律重置。
    /// </summary>
    /// <param name="isAttacking">下令前是否已处于攻击态（已有攻击目标）。</param>
    /// <param name="sameTarget">新目标是否与当前攻击目标相同。</param>
    /// <param name="movedSinceCommand">自上次进入稳定攻击后是否移动过（玩家给移动令 / 实际位移逼近）。</param>
    /// <returns>true=重置冷却（重新 wind-up）；false=保持当前冷却（忽略重复指令）。</returns>
    public static bool ShouldResetCooldown(bool isAttacking, bool sameTarget, bool movedSinceCommand)
        => !isAttacking || !sameTarget || movedSinceCommand;
}
