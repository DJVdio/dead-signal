using System;
using System.Collections.Generic;
using System.Numerics;

namespace DeadSignal.Godot;

/// <summary>
/// 移动路径线（RimWorld 式「接下来要走的线路」）的纯几何逻辑：零 Godot 依赖，Link 进 DeadSignal.Combat.Tests。
///
/// 职责边界：本类只做「导航路径点数组 → 该画哪些线段」的纯变换。
///  - 路径**从哪来**不归本类：由 Godot 层的 <c>Actor</c> 直接吐 <c>NavigationAgent2D</c> 的**真实当前路径**
///    （<c>GetCurrentNavigationPath()</c> + <c>GetCurrentNavigationPathIndex()</c>），本类不重算寻路 ——
///    因此关着的门/绕远路会天然反映在线上（画的就是他真要走的那条）。
///  - 画（颜色/线宽/iso 投影/终点标记）不归本类：归 <c>PathOverlay</c>。
///
/// 坐标一律 cartesian 像素、用 <see cref="System.Numerics.Vector2"/>（非 Godot.Vector2）以保零依赖；
/// iso 投影由绘制方 <c>Iso.Project</c> 负责——投影是线性变换，故「先裁虚线再投影」与「先投影再裁虚线」
/// 只在虚线的**屏幕等距性**上有别：本类在 cartesian 裁段（世界等距），投影后近远景短划长度一致，符合俯视直觉。
///
/// 数值（划长/间隙）皆「拟定待调」。
/// </summary>
public static class MovePathVisual
{
    /// <summary>虚线短划长（cartesian 像素）。「拟定待调」</summary>
    public const float DashLength = 16f;

    /// <summary>虚线间隙长（cartesian 像素）。「拟定待调」</summary>
    public const float GapLength = 10f;

    /// <summary>路径首点与角色当前位置的合并阈值：小于此距离视作同点，不生成零长首段。</summary>
    public const float MergeEpsilon = 2f;

    private const float Tiny = 1e-4f;

    // ── 谁该画 / 画多醒目 ────────────────────────────────────────────────

    /// <summary>
    /// 该不该给这个角色画路径线。三条与规则：
    ///  1. <paramref name="isPlayerUnit"/> —— **只画己方玩家单位**。丧尸/劫掠者的路径绝不画（那是作弊级信息）。
    ///  2. <paramref name="alive"/> —— 死人不画。
    ///  3. <paramref name="hasNavPath"/> —— 手头有一条没走完的导航路径才画（站着不动/已到达 → 无线可画）。
    /// </summary>
    public static bool ShouldDraw(bool isPlayerUnit, bool alive, bool hasNavPath)
        => isPlayerUnit && alive && hasNavPath;

    /// <summary>一条路径线的笔触（线宽 + 不透明度）。</summary>
    public readonly record struct Stroke(float Width, float Alpha);

    /// <summary>选中者的笔触：粗一点、实一点（当前焦点）。「拟定待调」</summary>
    public static readonly Stroke SelectedStroke = new(2.2f, 0.95f);

    /// <summary>未选中者的笔触：细、半透明（同时画 N 条也不糊、不盖住地上的物资点/尸体/血迹）。「拟定待调」</summary>
    public static readonly Stroke UnselectedStroke = new(1.3f, 0.45f);

    /// <summary>按是否选中取笔触：选中恒比未选中更粗更实（全局态势 + 当前焦点两不误）。</summary>
    public static Stroke StrokeFor(bool selected) => selected ? SelectedStroke : UnselectedStroke;

    /// <summary>
    /// 由导航路径点 + 当前推进下标 + 角色当前位置，拼出**还没走的那截**折线：
    /// 起点恒为角色脚下（<paramref name="actorPos"/>，故路径随角色前进实时缩短），其后接 <paramref name="index"/>
    /// 及之后的路径点（已走过的点丢弃），终点即导航终点。
    ///
    /// <para><paramref name="index"/> 取 <c>NavigationAgent2D.GetCurrentNavigationPathIndex()</c>：
    /// 它指向「下一个要去的路径点」，故从它开始取即为剩余路径。越界/负值均安全钳制。</para>
    ///
    /// 返回点数 &lt; 2 表示无可画（无路径 / 已到终点 / 剩余路径退化为一个点）。
    /// </summary>
    public static List<Vector2> RemainingPolyline(IReadOnlyList<Vector2>? path, int index, Vector2 actorPos)
    {
        var result = new List<Vector2>();
        if (path == null || path.Count == 0)
        {
            return result;
        }

        int start = Math.Clamp(index, 0, path.Count);
        result.Add(actorPos);
        for (int i = start; i < path.Count; i++)
        {
            Vector2 p = path[i];
            // 与上一个已收点重合的点丢弃（首点常与角色位置重合；导航路径也可能含重复点）。
            if (Vector2.Distance(p, result[^1]) <= MergeEpsilon)
            {
                continue;
            }
            result.Add(p);
        }

        if (result.Count < 2)
        {
            result.Clear();
        }
        return result;
    }

    /// <summary>折线总长（cartesian 像素）。</summary>
    public static float Length(IReadOnlyList<Vector2>? polyline)
    {
        if (polyline == null || polyline.Count < 2)
        {
            return 0f;
        }

        float total = 0f;
        for (int i = 1; i < polyline.Count; i++)
        {
            total += Vector2.Distance(polyline[i - 1], polyline[i]);
        }
        return total;
    }

    /// <summary>
    /// 折线 → 虚线短划段列表。短划按**沿折线的弧长**切（周期 = dash+gap），故拐角处的短划会被拆成两段
    /// 贴着折线走（不切角、不飞线）；虚线相位沿全程连续，看起来是一条被打断的线而非每段各画各的。
    ///
    /// <paramref name="dash"/> 或 <paramref name="gap"/> 非正 → 退化为实线（整条折线逐段返回）。
    /// </summary>
    public static List<(Vector2 A, Vector2 B)> Dashes(IReadOnlyList<Vector2>? polyline, float dash, float gap)
    {
        var result = new List<(Vector2, Vector2)>();
        if (polyline == null || polyline.Count < 2)
        {
            return result;
        }

        if (dash <= 0f || gap <= 0f)
        {
            for (int i = 1; i < polyline.Count; i++)
            {
                if (Vector2.Distance(polyline[i - 1], polyline[i]) > Tiny)
                {
                    result.Add((polyline[i - 1], polyline[i]));
                }
            }
            return result;
        }

        float period = dash + gap;
        float walked = 0f; // 本段起点在整条折线上的弧长
        for (int i = 1; i < polyline.Count; i++)
        {
            Vector2 a = polyline[i - 1];
            Vector2 b = polyline[i];
            float segLen = Vector2.Distance(a, b);
            if (segLen <= Tiny)
            {
                continue;
            }

            float segStart = walked;
            float segEnd = walked + segLen;
            Vector2 dir = (b - a) / segLen;

            // 与本段相交的每个短划区间 [k*period, k*period+dash) 裁到段内
            int k = (int)MathF.Floor(segStart / period);
            for (; k * period < segEnd; k++)
            {
                float dStart = MathF.Max(k * period, segStart);
                float dEnd = MathF.Min(k * period + dash, segEnd);
                if (dEnd - dStart <= Tiny)
                {
                    continue;
                }
                result.Add((a + dir * (dStart - segStart), a + dir * (dEnd - segStart)));
            }

            walked = segEnd;
        }

        return result;
    }
}
