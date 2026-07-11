using System;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。只引 DeadSignal.Combat 的 IRandomSource（战斗引擎公共随机源）。
// 神秘商人来访调度状态机：距上次来访 1~5 天随机（可注入随机源、可复现）；白天到访；袭营/异常日顺延到次日再试。

/// <summary>
/// 神秘商人来访调度（纯状态机）：维护"下次到访日" <see cref="NextVisitDay"/>，间隔在 [<see cref="MinGap"/>, <see cref="MaxGap"/>]
/// 天内均匀随机（draft：1~5 天，用户拍板）。随机走可注入的 <see cref="IRandomSource"/> 以复现。
/// 用法：每天开始（相位钩子）调 <see cref="ShouldVisit"/>；返回 true 则派商人进场，到访落地后调 <see cref="CompleteVisit"/> 滚下一次。
/// </summary>
public sealed class MerchantSchedule
{
    /// <summary>来访间隔天数下限（draft，用户拍板 1）。</summary>
    public int MinGap { get; }

    /// <summary>来访间隔天数上限（draft，用户拍板 5）。</summary>
    public int MaxGap { get; }

    private readonly IRandomSource _rng;

    /// <summary>下次预定到访日（游戏内天数，与 <c>GameClock.Day</c> 同轴）。当天 ≥ 它且非异常日即到访。</summary>
    public int NextVisitDay { get; private set; }

    /// <summary>
    /// 以当前天数 <paramref name="currentDay"/> 起排下一次到访：间隔在 [<paramref name="minGap"/>, <paramref name="maxGap"/>] 内随机。
    /// 首次调度即 <c>currentDay + roll</c>（默认 1~5 天后首访，不在开局当天）。
    /// </summary>
    public MerchantSchedule(IRandomSource rng, int currentDay, int minGap = 1, int maxGap = 5)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        MinGap = Math.Max(1, minGap);
        MaxGap = Math.Max(MinGap, maxGap);
        NextVisitDay = currentDay + RollGap();
    }

    /// <summary>
    /// 今天商人是否到访：<paramref name="currentDay"/> 未到 <see cref="NextVisitDay"/> → false（还没到日子）；
    /// 到点但 <paramref name="dayBlocked"/>（袭营/异常日）→ 顺延到明天再试、返回 false；到点且平安 → true（该派商人进场）。
    /// 顺延不消耗一次来访、也不重滚间隔，只把到访日推到次日，直到某天平安为止。
    /// </summary>
    public bool ShouldVisit(int currentDay, bool dayBlocked)
    {
        if (currentDay < NextVisitDay)
        {
            return false;
        }

        if (dayBlocked)
        {
            NextVisitDay = currentDay + 1; // 顺延到明天再试
            return false;
        }

        return true;
    }

    /// <summary>
    /// 本次到访落地后调用，滚下一次到访日 = <paramref name="currentDay"/> + 随机间隔。
    /// 与 <see cref="ShouldVisit"/> 返回 true 成对使用（一次到访对应一次 CompleteVisit）。
    /// </summary>
    public void CompleteVisit(int currentDay) => NextVisitDay = currentDay + RollGap();

    // [MinGap, MaxGap] 上的均匀整数：在 [MinGap, MaxGap+1) 取实数再下取整；边界 MaxGap+1 极值 clamp 回 MaxGap。
    private int RollGap()
    {
        double r = _rng.Range(MinGap, MaxGap + 1);
        int g = (int)Math.Floor(r);
        return Math.Clamp(g, MinGap, MaxGap);
    }
}
