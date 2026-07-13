using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 自动存档的**轮转策略**。玩家没有"存档"这个动作（用户拍板：只有自动存档），
// 所以存档槽的生死全由这里决定——写错了，玩家就会永久失去他唯一的退路。

/// <summary>
/// 自动存档轮转：保留最近 <see cref="KeepCount"/> 个，更老的淘汰。
/// <para>
/// <b>为什么不是单槽覆盖</b>：单槽意味着"最后一次自动存档"会无条件盖掉上一次。
/// 一旦某次存档正好落在一个**已经输了的局面**上（全员重伤、粮食见底、尸潮明天到），
/// 玩家就被永久锁死在那个必死的世界里——他没有手动存档可以回退，因为我们把那个动作删掉了。
/// 留一串历史档是这个设计的**前提**，不是可选项。
/// </para>
/// <para>
/// 一天存两次（清晨聚餐 / 黄昏聚餐），故 <see cref="KeepCount"/>=6 ≈ <b>回退三天</b>——
/// 够玩家从一个坏局面里爬出来，又不至于把 S/L 变相还给他（回退三天要重做三天的所有决策）。
/// </para>
/// </summary>
public static class SaveRotation
{
    /// <summary>保留的自动存档数（一天 2 次 ⇒ 6 个 ≈ 三天）。「拟定待调」</summary>
    public const int KeepCount = 6;

    /// <summary>自动存档槽名的前缀（据此从存档目录里认出哪些是自动存档）。</summary>
    public const string SlotPrefix = "auto_";

    /// <summary>
    /// 这一刻的自动存档叫什么。
    /// <para>
    /// 用「第几天 + 相位」而不是真实时间戳：槽名本身就说清了它是什么档（<c>auto_d12_DuskMeal</c>），
    /// 手动翻存档目录调 bug 时一眼可读。天数单调递增 ⇒ 不会撞名。
    /// </para>
    /// </summary>
    public static string SlotNameFor(int day, DayPhase phase) => $"{SlotPrefix}d{day}_{phase}";

    /// <summary>某个槽名是不是自动存档。</summary>
    public static bool IsAutosaveSlot(string slot) => slot.StartsWith(SlotPrefix);

    /// <summary>
    /// 写完新档后，挑出**该删掉的**旧档（按存档时刻从新到旧排，超出 <paramref name="keep"/> 的淘汰）。
    /// 只动自动存档槽，别的一概不碰。
    /// </summary>
    /// <param name="slotsNewestFirst">全部槽名 + 其存档时刻（ISO 8601），**已按时刻从新到旧排好**。</param>
    public static IReadOnlyList<string> SlotsToPrune(
        IEnumerable<(string Slot, string SavedAtUtc)> slotsNewestFirst,
        int keep = KeepCount)
        => slotsNewestFirst
            .Where(s => IsAutosaveSlot(s.Slot))
            .Skip(keep < 0 ? 0 : keep)
            .Select(s => s.Slot)
            .ToList();

    /// <summary>
    /// 这个相位切换该不该触发自动存档。
    /// <para>
    /// <b>一天两次：清晨聚餐 + 黄昏聚餐。</b>用户原话是「相位变化的时候，也就是一天存两次」——
    /// 而实际相位有 8 个（清晨聚餐/白天筹备/出发路上/外出探索/返回营地/黄昏聚餐/夜间部署/夜间行动），
    /// 一天要切 8 次。取这两个，是因为它们正好把一天切成**白天段**与**夜晚段**，
    /// 且二者都是<b>模态聚餐相位</b>：全员聚在一起吃饭，世界本来就停着，分粮刚结算完，
    /// 没有飞在半空的子弹、没有搜到一半的柜子——是这一天里最干净的两个静止点。
    /// </para>
    /// <para>
    /// 于是读档的粒度天然就是「重做一整个白天」或「重做一整个夜晚」：
    /// 派谁出门、搜哪些点、开没开门、排谁的班——全部重来。
    /// <b>而不是"读回到开枪前一秒重掷骰子"</b>。这正是「决策不可逆」要守的东西。
    /// </para>
    /// </summary>
    public static bool ShouldAutosaveAt(DayPhase phase)
        => phase is DayPhase.DawnMeal or DayPhase.DuskMeal;
}
