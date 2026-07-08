namespace DeadSignal.Godot;

using DeadSignal.Combat;

// 本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 AttackCommandState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载「持握态(GripMode)对实时攻击的两处消费」的取值算法：
//   ① 有效出手间隔——把 grip 攻速系数并入残疾×饥饿的操作能力乘法（再乘一个因子，不覆盖原乘法）；
//   ② 远程双持的误差角放大。
// 计时器推进 / 空间发射归 Actor 运行时层，本类只出纯取值。系数权威来自引擎 DualWield。

/// <summary>
/// 持握态战斗消费（纯函数）。把引擎 <see cref="DualWield"/> 的持握系数接进 Actor 的实时攻速/误差角：
/// 双手一把 ×1.15（攻更快）、双持两把 ×0.70（更慢，远程另放大误差角）、单手 ×1.0（基线）。
/// 不改残疾×饥饿那套操作能力乘法，只在其上再乘一个 grip 因子。
/// </summary>
public static class GripCombat
{
    /// <summary>
    /// 含持握的有效出手间隔（秒/次）。<paramref name="operation"/>=残疾×饥饿合并后的操作能力（0~1，
    /// 见 <see cref="HungerState.CombineCapability"/>）；有效间隔 = 基础冷却 / (操作能力 × 持握攻速系数)。
    /// 双手 1.15→间隔更短、双持 0.70→更长、单手 1.0 不变。操作能力 ≤0（断双手等无法出手）时回落基础冷却
    /// 保持正值——此时 Actor 本就跳过出手，避免除零变 NaN/负值。
    /// </summary>
    public static double EffectiveInterval(double baseCooldown, double operation, GripMode grip)
        => operation > 0
            ? baseCooldown / (operation * DualWield.GripSpeedFactor(grip))
            : baseCooldown;

    /// <summary>
    /// 含持握的远程误差角（度）。双持两把 → 基础误差角 ×<see cref="DualWield.RangedSpreadFactor"/>（1.25）；
    /// 单手 / 双手一把 → 不变。近战无误差角、不经此路径。
    /// </summary>
    public static double EffectiveSpreadDegrees(double baseSpread, GripMode grip)
        => grip == GripMode.DualWield ? baseSpread * DualWield.RangedSpreadFactor : baseSpread;
}
