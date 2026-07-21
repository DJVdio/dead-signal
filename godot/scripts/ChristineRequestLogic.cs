using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 StoryFlags.cs / MealCondition.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 克莉丝汀「请求出兵清剿金手指帮」支线的**状态机纯逻辑**：收留后她在聚餐里递进请求（共 3 次），
// 每次请求那一餐结束后弹抉择面板；玩家答应即留下不再逼问，累计 3 次「暂不」则她在下一次昼夜交替离开。
// 状态只存在 StoryFlags 的字符串里（引擎无整数字段）：这里只做"读值→判定→写值"，不碰 Godot、可单测。
// ★台词/数值皆剧情向占位，最终由用户手写；本类只保证状态推进可跑、可测。

/// <summary>
/// 克莉丝汀请求线的状态判定。核心状态存于 flag <see cref="StateKey"/>：
///   "0" → 收留后待发第 1 次请求；"1" → 已拒 1 次待第 2 次；"2" → 已拒 2 次待第 3 次；
///   "3" → 已拒满 3 次（触发离开）；"agreed" → 已答应，永久停播。
/// 请求气泡按 <see cref="StateKey"/> 的精确值门控（"0"/"1"/"2"），说出时经 trigger 置 <see cref="PendingKey"/>；
/// 抉择结果经 <see cref="Resolve"/> 推进；离开经 <see cref="ConsumeLeaving"/> 在下次相位切换一次性触发。
/// </summary>
public static class ChristineRequestLogic
{
    /// <summary>请求线主状态 flag（"0"/"1"/"2"/"3"/"agreed"）。</summary>
    public const string StateKey = "christine_req";

    /// <summary>本餐有一次请求正等待玩家抉择（由请求气泡的 trigger 置，抉择后 <see cref="Resolve"/> 消费）。</summary>
    public const string PendingKey = "christine_req_pending";

    /// <summary>已拒满则置：她将在下一次昼夜交替（进入聚餐边界流程）离开，不立即走。</summary>
    public const string LeavingKey = "christine_leaving_pending";

    /// <summary>累计"暂不"达此次数即离开。</summary>
    public const int DeclinesToLeave = 3;

    /// <summary>收留时开启请求线：状态置 "0"（待发第 1 次请求）。</summary>
    public static void Begin(StoryFlags flags) => flags?.Set(StateKey, "0");

    /// <summary>当前累计"暂不"次数（= 状态整数值；未设/"agreed"/非数字皆为 0）。</summary>
    public static int DeclineCount(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(StateKey), out int n) ? n : 0;

    /// <summary>是否已答应出兵（此后不再计数、不再逼问）。</summary>
    public static bool HasAgreed(StoryFlags flags)
        => string.Equals(flags?.Get(StateKey), "agreed", StringComparison.OrdinalIgnoreCase);

    /// <summary>本餐是否有一次请求正待抉择。</summary>
    public static bool HasPendingRequest(StoryFlags flags) => flags?.Has(PendingKey) ?? false;

    /// <summary>
    /// 抉择面板选择后推进状态并消费本次 pending。
    /// <paramref name="agreed"/>=true → 置 "agreed" 永久停播；false → 计一次"暂不"，
    /// 累计达 <see cref="DeclinesToLeave"/> 则置 <see cref="LeavingKey"/>。
    /// 返回值：本次抉择是否触发了"她将离开"。
    /// </summary>
    public static bool Resolve(StoryFlags flags, bool agreed)
    {
        if (flags == null)
        {
            return false;
        }
        flags.Set(PendingKey, null); // 消费本次 pending，避免重复弹面板
        if (agreed)
        {
            flags.Set(StateKey, "agreed");
            return false;
        }
        int declines = DeclineCount(flags) + 1;
        flags.Set(StateKey, declines.ToString());
        if (declines >= DeclinesToLeave)
        {
            flags.Set(LeavingKey, "true");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 下次昼夜交替时判定是否该让她离开：置了 <see cref="LeavingKey"/> 则返回 true 并清除该 flag
    /// （保证只离开一次）。未置则 false。
    /// </summary>
    public static bool ConsumeLeaving(StoryFlags flags)
    {
        if (flags == null || !flags.Has(LeavingKey))
        {
            return false;
        }
        flags.Set(LeavingKey, null);
        return true;
    }

    /// <summary>克莉丝汀在请求线结束前离场（死亡等）时清空全部相关 flag，彻底停播支线。</summary>
    public static void Abort(StoryFlags flags)
    {
        if (flags == null)
        {
            return;
        }
        flags.Set(StateKey, null);
        flags.Set(PendingKey, null);
        flags.Set(LeavingKey, null);
    }
}
