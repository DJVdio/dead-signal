using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

/// <summary>
/// 一条聚餐气泡：说话人 + 内容 + 适用时段。数据来自 <c>meal_bubbles.json</c>，代码只读规则。
/// 气泡替代原信件作为支线/营地需求/故事事件入口（设计文档 §7.4）。
/// </summary>
public sealed class MealBubble
{
    /// <summary>说话人显示名；为空则作旁白/环境气泡。</summary>
    public string? speaker { get; set; }

    /// <summary>气泡文本。</summary>
    public string text { get; set; } = "";

    /// <summary>适用时段："dawn" | "dusk" | "any"（缺省 any）。</summary>
    public string phase { get; set; } = "any";
}

/// <summary>
/// 气泡池：按时段筛选后随机取若干条。目前为事件驱动占位实现——
/// 「关系变化自动提示」需等关系系统落地后接入（见回报 TODO），此处先支持数据池抽取。
/// </summary>
public sealed class MealBubblePool
{
    private readonly List<MealBubble> _all;
    private readonly Random _rng;

    public MealBubblePool(IEnumerable<MealBubble>? bubbles, Random? rng = null)
    {
        _all = (bubbles ?? Enumerable.Empty<MealBubble>()).ToList();
        _rng = rng ?? new Random();
    }

    /// <summary>取本时段（含 any）最多 <paramref name="count"/> 条气泡，随机不重复。</summary>
    public IReadOnlyList<MealBubble> Pick(string phase, int count)
    {
        var pool = _all
            .Where(b => string.IsNullOrEmpty(b.phase)
                        || b.phase.Equals("any", StringComparison.OrdinalIgnoreCase)
                        || b.phase.Equals(phase, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => _rng.Next())
            .Take(Math.Max(0, count))
            .ToList();
        return pool;
    }
}
