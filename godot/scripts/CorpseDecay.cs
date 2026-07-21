using System.Collections.Generic;

namespace DeadSignal.Godot;

/// <summary>
/// 一具尸体在过期清理表里的登记（纯数据）：<paramref name="Id"/> 容器 id（空=光尸体，没登记成可搜刮点）、
/// <paramref name="SpawnPhaseTick"/> 落地时的半天计数、<paramref name="Authored"/> 是否是**手写剧情尸体**。
/// </summary>
public readonly record struct CorpseDecayEntry(string Id, int SpawnPhaseTick, bool Authored);

/// <summary>
/// 尸体按半天过期清理的纯逻辑（零 Godot 依赖，Link 进 DeadSignal.Combat.Tests）。
///
/// 【规则·用户拍板】「所有尸体统一保留三个相位，也就是三个半天」——相位计数只在
/// <see cref="DayPhase.DawnMeal"/> / <see cref="DayPhase.DuskMeal"/> 两个半天边界推进，一个昼夜恰好 +2。
/// 尸体落地时记下当时的半天计数，此后经过 <see cref="LifetimePhases"/> 个半天即到期清理。
/// 探索队白天全灭后，尸体在次日白天仍可找回；到第三个半天边界才刷没。
/// 这也顺带堵掉了刷装备（挂机刷不出无限的牛仔外套）——用世界的规则限制，而不是用随机数惩罚。
///
/// 【🔴 authored 尸体永不清理】祖母的尸体（山姆被迫杀死的、尸变的祖母，camp.json 的 role=corpse prop）
/// 是**手写剧情内容**：她永久躺在住宅南门外，首次点击播那 4 屏叙事。她若被当成普通战斗尸体收走，那段剧情
/// 就凭空消失了。同理探索关里的 authored 尸体（帮众尸体/树上的哥顿/克莉丝汀）也都是叙事发现点。
/// 本类对 <see cref="CorpseDecayEntry.Authored"/> 的登记**永远返回不过期**——运行时它们根本不进
/// CorpseYard（那里只装 SpawnFor 造出来的战斗尸体），这条规则是**第二道保险**，把口径钉死在纯逻辑里。
///
/// 【与数量封顶的关系】<see cref="CorpseYard.MaxCorpses"/> 仍在：**两道防线**——时间到期（本类）+ 数量封顶
/// （万一某一个相位里死了海量丧尸，先撑不住的是节点数）。两条路都走同一个回收出口（注销可搜刮容器登记）。
///
/// 数值「拟定待调」。
/// </summary>
public static class CorpseDecay
{
    /// <summary>尸体能挺过几个半天（用户拍板：3 个半天）。</summary>
    public const int LifetimePhases = 3;

    /// <summary>统一尸体时钟只在清晨/黄昏两个半天边界推进。</summary>
    public static bool AdvancesOn(DayPhase phase) =>
        phase is DayPhase.DawnMeal or DayPhase.DuskMeal;

    /// <summary>这具尸体到期了吗。authored（剧情）尸体<b>永远</b>不过期。</summary>
    public static bool IsExpired(CorpseDecayEntry entry, int currentPhaseTick)
    {
        if (entry.Authored)
        {
            return false;
        }
        return currentPhaseTick - entry.SpawnPhaseTick >= LifetimePhases;
    }

    /// <summary>
    /// 还剩几个半天可以来搜刮（0 = 这个半天边界就没了）。authored 尸体返回 <see cref="int.MaxValue"/>（永远有的是时间）。
    /// 供 UI/提示消费（如尸体悬停提示「快烂了」）。
    /// </summary>
    public static int PhasesRemaining(CorpseDecayEntry entry, int currentPhaseTick)
    {
        if (entry.Authored)
        {
            return int.MaxValue;
        }
        int elapsed = currentPhaseTick - entry.SpawnPhaseTick;
        int left = LifetimePhases - elapsed;
        return left > 0 ? left : 0;
    }

    /// <summary>
    /// 这个半天边界该清理掉哪些尸体（按登记顺序返回，确定性）。调用方拿着这张单子去销毁节点、还格、
    /// **注销可搜刮容器登记**（三件事必须一起做，否则玩家会去搜一具已经不在的尸体）。
    /// </summary>
    public static List<CorpseDecayEntry> Sweep(IEnumerable<CorpseDecayEntry> corpses, int currentPhaseTick)
    {
        var expired = new List<CorpseDecayEntry>();
        foreach (CorpseDecayEntry c in corpses)
        {
            if (IsExpired(c, currentPhaseTick))
            {
                expired.Add(c);
            }
        }
        return expired;
    }
}
