using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 表现逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测，故颜色用中性的 MoteColor 表示，
//  Godot 侧再 new Color(r,g,b) 映射；FloatingText/CombatFeed 负责落地渲染）。

/// <summary>
/// 战报飘字的中性颜色（RGB，各分量 0..1），脱离 Godot.Color 以便纯单测断言。
/// Godot 侧用 <c>new Color(c.R, c.G, c.B)</c> 还原。
/// </summary>
public readonly struct MoteColor
{
    public readonly float R;
    public readonly float G;
    public readonly float B;

    public MoteColor(float r, float g, float b)
    {
        R = r;
        G = g;
        B = b;
    }
}

/// <summary>飘字的显示串 + 颜色（表现层直接吃这一对）。</summary>
public readonly struct MoteText
{
    public readonly string Text;
    public readonly MoteColor Color;

    public MoteText(string text, MoteColor color)
    {
        Text = text;
        Color = color;
    }
}

/// <summary>
/// 把一次攻击结果（<see cref="AttackOutcome"/>）翻译成飘字的"串 + 色"。纯函数、无副作用、脱 Godot。
///
/// 配色（用户拍板，硬要求）：伤害数字按<b>伤害类型</b>着色——钝器=黄字、锐器=红字。
/// 因飘字是单色 Label，整串取该伤害类型色（伤害数字配色优先于旧的阵营配色）。
/// 被甲完全挡下（Damage=0/Blocked）单独走中性灰的"叮·挡下"提示。
///
/// 效果标志与旧 ShowOutcome 的差异（本单为"飘字①增强"）：旧逻辑按优先级只显示一个效果，
/// 这里把当次命中触发的多个效果<b>依序拼接</b>（断肢→骨折→震荡→流血），信息更全。
/// 不做 Miss/落空飘字（近战必中、远程落空是空间性的静默，用户已拍板）。
/// </summary>
public static class CombatMoteText
{
    // 伤害类型配色（拟定待调，形态为用户拍板）。
    /// <summary>钝器伤害=黄字。</summary>
    public static readonly MoteColor BluntColor = new(1f, 0.85f, 0.35f);
    /// <summary>锐器伤害=红字。</summary>
    public static readonly MoteColor SharpColor = new(1f, 0.4f, 0.35f);
    /// <summary>被甲挡下=中性灰。</summary>
    public static readonly MoteColor BlockedColor = new(0.75f, 0.78f, 0.85f);
    /// <summary>半身掩体判无效=中性青（与"被甲挡下"的灰、伤害的钝黄/锐红都分得开——这一下没有伤害发生）。</summary>
    public static readonly MoteColor CoverColor = new(0.55f, 0.82f, 0.80f);

    /// <summary>
    /// 半身掩体整发判无效的飘字（用户口径：<b>"这一下射中了人，但是判定 25% 几率无效"</b>——
    /// 所以说的是"打中了但没伤到"，<b>不是</b>落空/未命中，也不是被甲挡下）。
    /// 无伤害发生 ⇒ 无伤害数字、无部位、无效果后缀，只有一句"掩体挡下"。
    /// </summary>
    public static MoteText BuildCoverNegated() => new("掩体挡下", CoverColor);

    public static MoteText Build(AttackOutcome hit)
    {
        if (hit.Blocked)
        {
            // 被甲完全挡下：给个"叮·挡下"手感提示，体现护甲不是绝对保险。
            return new MoteText($"叮·{hit.PartName}挡下", BlockedColor);
        }

        string typeTag = hit.FinalType == DamageType.Blunt ? "钝" : "锐";
        MoteColor color = hit.FinalType == DamageType.Blunt ? BluntColor : SharpColor;
        string text = $"-{hit.Damage:0.#} {hit.PartName}({typeTag}){EffectSuffix(hit)}"; // 小数伤害显示一位小数（去尾零，[SPEC-B14-补6]）
        return new MoteText(text, color);
    }

    /// <summary>把当次命中触发的效果依序拼成后缀（可多个，无则空串）。</summary>
    private static string EffectSuffix(AttackOutcome hit)
    {
        string s = "";
        if (hit.Severed) s += " 断!";
        if (hit.Fractured) s += " 骨折";
        if (hit.Concussed) s += " 震荡";
        if (hit.Bled) s += " 流血";
        // 致命标志排在末位：钝黄锐红配色不变，仅在文本收尾追加"毙"。
        if (hit.Died) s += " 毙";
        return s;
    }
}
