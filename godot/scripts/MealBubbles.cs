using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「按条件播放用户写的台词」框架的**选择器层**：过滤（condition）+ 加权抽取 + 跨餐去重。
// ★台词/条件/权重/剧情走向全部来自 meal_bubbles.json 里用户填的数据；代码只写"怎么挑"的规则。

/// <summary>
/// 一条聚餐气泡（schema v2，向后兼容 v1 的 speaker/text/phase 三字段）。数据来自 <c>meal_bubbles.json</c>。
/// 气泡替代原信件作为支线/营地需求/故事事件入口（设计文档 §7.4）。
/// </summary>
public sealed class MealBubble
{
    /// <summary>说话人显示名；为空则作旁白/环境气泡。</summary>
    public string? speaker { get; set; }

    /// <summary>气泡文本（跨餐去重以它为身份键）。</summary>
    public string text { get; set; } = "";

    /// <summary>适用时段："dawn" | "dusk" | "any"（缺省 any）。粗筛，细条件走 <see cref="condition"/>。</summary>
    public string phase { get; set; } = "any";

    /// <summary>可选播放条件（谓词的与/或）；省略=通用池，任何上下文都合格。</summary>
    public BubbleCondition? condition { get; set; }

    /// <summary>加权抽取权重（缺省 1）；≤0 视为 0（不抽中，除非兜底）。</summary>
    public double weight { get; set; } = 1.0;

    /// <summary>可选：说完这句后要施加的 flag 改动（推动剧情）。</summary>
    public List<BubbleTrigger>? triggers { get; set; }

    /// <summary>
    /// 可选 opt-out：为 <c>true</c> 时豁免"说话人须在营存活"的自动门控，
    /// 用于"追忆离场者 / 遗言回响"等**刻意让不在场者发声**的 authored 台词。缺省 <c>false</c>。
    /// </summary>
    public bool allowAbsentSpeaker { get; set; } = false;

    /// <summary>本条对某上下文是否合格（相位粗筛 + 具名说话人在场门控 + condition 细筛）。</summary>
    public bool Matches(MealWorldContext ctx)
    {
        bool phaseOk = string.IsNullOrEmpty(phase)
                       || phase.Equals("any", StringComparison.OrdinalIgnoreCase)
                       || phase.Equals(ctx.Phase, StringComparison.OrdinalIgnoreCase);
        if (!phaseOk)
        {
            return false;
        }

        // 具名说话人在场门控（框架层统一）：speaker 必须是"当前在营存活"的 pawn 才播其台词，
        // 避免角色未登场（如克莉丝汀入营前）或已死亡时，其署名气泡仍以本人口吻播出。
        // 无 speaker 的通用/旁白气泡不受影响；个别刻意让离场者发声的台词用 allowAbsentSpeaker 豁免。
        if (!allowAbsentSpeaker && !string.IsNullOrEmpty(speaker))
        {
            var p = ctx.PawnNamed(speaker);
            if (p is null || p.IsDead)
            {
                return false;
            }
        }

        return condition is null || condition.IsSatisfied(ctx);
    }
}

/// <summary>
/// 气泡池（选择器 v2）：给定世界上下文 → 过滤出合格条 → 跨餐去重 → 按 weight 加权抽 N 条（不重复）。
/// 随机走可注入的 <see cref="IRandomSource"/>（测试用 <see cref="SequenceRandomSource"/> 复现）。
/// 去重：记住最近播过的 N 条 text（<see cref="DedupWindow"/>），合格池够时不连播；不够则忽略去重兜底
/// （宁可重复也不空场）。数值"拟定待调"。
/// </summary>
public sealed class MealBubblePool
{
    /// <summary>跨餐去重窗口（拟定待调）：记住最近播过的这么多条 text 不连播。默认 6≈近两餐。</summary>
    public const int DefaultDedupWindow = 6;

    private readonly List<MealBubble> _all;
    private readonly IRandomSource _rng;
    private readonly int _dedupWindow;

    // 最近播过的 text（队尾为最新），长度截到 _dedupWindow。
    private readonly LinkedList<string> _recent = new LinkedList<string>();

    public MealBubblePool(IEnumerable<MealBubble>? bubbles, IRandomSource? rng = null, int dedupWindow = DefaultDedupWindow)
    {
        _all = (bubbles ?? Enumerable.Empty<MealBubble>()).ToList();
        _rng = rng ?? new SystemRandomSource();
        _dedupWindow = Math.Max(0, dedupWindow);
    }

    /// <summary>最近播过的 text（只读，供测试/调试）。</summary>
    public IReadOnlyCollection<string> Recent => _recent;

    /// <summary>
    /// 选择器 v2：按上下文过滤 + 去重 + 加权抽最多 <paramref name="count"/> 条（不重复），并登记去重历史。
    /// 合格池排除近期已播后为空时，退回"含近期已播"的合格池兜底（不空场）。
    /// </summary>
    public IReadOnlyList<MealBubble> Pick(MealWorldContext ctx, int count)
    {
        int n = Math.Max(0, count);
        if (n == 0)
        {
            return Array.Empty<MealBubble>();
        }

        var eligible = _all.Where(b => b.Matches(ctx)).ToList();
        if (eligible.Count == 0)
        {
            return Array.Empty<MealBubble>();
        }

        // 优先用"近期未播过"的合格条；若被去重清空则退回全体合格条兜底。
        var recentSet = new HashSet<string>(_recent, StringComparer.Ordinal);
        var pool = eligible.Where(b => !recentSet.Contains(b.text)).ToList();
        if (pool.Count == 0)
        {
            pool = eligible;
        }

        var chosen = WeightedSampleWithoutReplacement(pool, n);
        foreach (var b in chosen)
        {
            RememberPlayed(b.text);
        }
        return chosen;
    }

    /// <summary>
    /// 向后兼容的旧签名（无 condition/flags 的纯相位抽取）：等价于空 flags/无角色的上下文。
    /// 现有仅传相位的调用点无需改动即可继续工作。
    /// </summary>
    public IReadOnlyList<MealBubble> Pick(string phase, int count) =>
        Pick(new MealWorldContext { Phase = phase }, count);

    /// <summary>把选中气泡的 triggers 施加到 flags（推动剧情）。选择器不隐式改 flag，故意独立成步。</summary>
    public static void ApplyTriggers(IEnumerable<MealBubble> chosen, StoryFlags flags)
    {
        foreach (var b in chosen)
        {
            if (b.triggers is null)
            {
                continue;
            }
            foreach (var t in b.triggers)
            {
                if (!string.IsNullOrEmpty(t.key))
                {
                    flags.Set(t.key, t.value);
                }
            }
        }
    }

    private void RememberPlayed(string text)
    {
        if (_dedupWindow == 0)
        {
            return;
        }
        _recent.AddLast(text);
        while (_recent.Count > _dedupWindow)
        {
            _recent.RemoveFirst();
        }
    }

    /// <summary>按 weight 加权抽 <paramref name="n"/> 条不重复。权重≤0 的条正常情况不入选（兜底除外）。</summary>
    private List<MealBubble> WeightedSampleWithoutReplacement(List<MealBubble> pool, int n)
    {
        var remaining = new List<MealBubble>(pool);
        var result = new List<MealBubble>(Math.Min(n, remaining.Count));

        while (result.Count < n && remaining.Count > 0)
        {
            double total = remaining.Sum(b => Math.Max(0, b.weight));
            MealBubble pick;
            if (total <= 0)
            {
                // 全为非正权重：退化为等概率，避免死循环/空抽。
                pick = remaining[(int)Math.Floor(_rng.Range(0, remaining.Count - 1e-9))];
            }
            else
            {
                double r = _rng.Range(0, total);
                double acc = 0;
                pick = remaining[remaining.Count - 1];
                foreach (var b in remaining)
                {
                    acc += Math.Max(0, b.weight);
                    if (r < acc)
                    {
                        pick = b;
                        break;
                    }
                }
            }
            result.Add(pick);
            remaining.Remove(pick);
        }
        return result;
    }
}
