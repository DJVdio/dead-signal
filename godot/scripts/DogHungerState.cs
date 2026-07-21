using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 布鲁斯（狗）的饥饿刻度——**犬类最简**，与人类 HungerState 分开（进食节奏不同：吃一份 +3、每次聚餐 -1）。

/// <summary>
/// 布鲁斯（狗）的饥饿刻度状态机（用户口径定稿）：吃一份食物 <see cref="EatGain"/>=+3；每次聚餐无条件 -1；
/// 到 0 饿死（终态，进食不复活）。刻度 0-<see cref="Cap"/> 与人类 <see cref="HungerState"/> 同尺度，故能力惩罚
/// 直接复用 <see cref="HungerState.PenaltyFor"/> 阶梯（越饿战斗越弱，喂进 <c>Dog</c> 的 HungerAbilityPenalty）。
/// "不上桌"（不占坐席/不产聚餐气泡/不入分配面板）由营地层保证，本状态机只管刻度算术。数值全"拟定待调"。
/// </summary>
public sealed class DogHungerState
{
    /// <summary>狗饥饿上限（拟定待调；吃一份 +3、每次聚餐 -1，故一份约管三餐）。</summary>
    public const int Cap = 6;

    /// <summary>吃一份食物的刻度增益（用户口径：+3）。</summary>
    public const int EatGain = 3;

    /// <summary>饿死刻度（终态）。</summary>
    public const int StarvedValue = 0;

    /// <summary>当前饥饿刻度（0=饿死 … Cap=饱）。</summary>
    public int Value { get; private set; }

    /// <param name="value">初始刻度（默认满 <see cref="Cap"/>），clamp 到 [0, Cap]。</param>
    public DogHungerState(int value = Cap)
    {
        Value = Math.Clamp(value, StarvedValue, Cap);
    }

    /// <summary>已饿死（刻度归 0，终态）。</summary>
    public bool IsStarved => Value <= StarvedValue;

    /// <summary>
    /// 饥饿能力惩罚净值 0~1（1=完全丧失）：复用人类 <see cref="HungerState.PenaltyFor"/> 阶梯（0-6 同尺度）。
    /// 战斗消费点把它与残疾惩罚按 <see cref="HungerState.CombineCapability"/> 合并（狗无残疾，实际仅饥饿一项）。
    /// </summary>
    public double AbilityPenalty => HungerState.PenaltyFor(Value);

    /// <summary>
    /// 一次聚餐流程结算（净模型，一次性施加，避免"-1 落 0 → +3 被短路"的跨 0 误杀）：
    /// 无条件 -1，吃到一份再 +<see cref="EatGain"/> —— 净变化 = 吃到 ? +2 : -1，clamp 到 [0, Cap]。
    /// 结算前已饿死者（终态）不复活、维持 0。返回结算后是否饿死。
    /// </summary>
    /// <param name="ate">本次聚餐是否吃到一份食物（由营地层按余粮决定）。</param>
    public bool ResolvePhase(bool ate)
    {
        if (IsStarved)
        {
            return true; // 终态：不复活、也不再变化
        }
        int delta = -1 + (ate ? EatGain : 0);
        Value = Math.Clamp(Value + delta, StarvedValue, Cap);
        return IsStarved;
    }

    /// <summary>
    /// 把刻度直接压到指定档（**仅降不升**）：用于剧情设定态（如布鲁斯随道格"饿昏迷"入队时压到低档）。
    /// 目标 clamp 到 [0, <see cref="Cap"/>]；已比目标更低则不动。压到 0 即饿死（终态）。与人类 <see cref="HungerState.DrainTo"/> 同义。
    /// </summary>
    public void DrainTo(int level)
        => Value = Math.Min(Value, Math.Clamp(level, StarvedValue, Cap));

    /// <summary>
    /// 读档：把刻度直接设回去（<b>可升可降</b>）。同 <see cref="HungerState.Restore"/>——
    /// <see cref="DrainTo"/> 只饿不喂，拿它读档会静默丢状态。
    /// </summary>
    internal void Restore(int value) => Value = Math.Clamp(value, StarvedValue, Cap);
}
