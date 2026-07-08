using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

/// <summary>袭营敌人破防时的一步动作：贴近到结构攻击站位 / 就地砸墙。</summary>
public enum BreachAction
{
    MoveToApproach,
    Hammer,
}

/// <summary>
/// 「敌人砸墙破防」的纯逻辑地基（无 Godot 依赖，供单测先红后绿）：轴对齐矩形的最近点/边缘距离、
/// 攻击站位推算、以及「够得着就砸、够不着就贴近」的决策。空间执行（寻路/碰撞/伤害施加）在 Godot
/// 消费层（<c>BreachController</c> + <c>CampMain</c>）适配 Rect2/Vector2 调用本类。
/// 关键：择结构用**边缘距离**而非中心距离——长围栏中心可能很远、但其边缘就在敌人脸上。
/// </summary>
public static class BreachLogic
{
    /// <summary>点 (px,py) 到轴对齐矩形 [rx,ry,rw,rh] 的最近点（在矩形内则返回自身，钳到边界）。</summary>
    public static (double x, double y) NearestPointOnRect(
        double px, double py, double rx, double ry, double rw, double rh)
    {
        double cx = Math.Clamp(px, rx, rx + rw);
        double cy = Math.Clamp(py, ry, ry + rh);
        return (cx, cy);
    }

    public static double Distance(double ax, double ay, double bx, double by)
        => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>点到矩形的边缘距离（点在矩形内为 0）。</summary>
    public static double EdgeDistance(double px, double py, double rx, double ry, double rw, double rh)
    {
        (double nx, double ny) = NearestPointOnRect(px, py, rx, ry, rw, rh);
        return Distance(px, py, nx, ny);
    }

    /// <summary>
    /// 攻击站位：从结构最近边缘点朝攻击者方向外推 <paramref name="standoff"/> 像素，得到一个贴在结构外沿、
    /// 可寻路的站位。攻击者已压在边缘点上（距离≈0）时无从取方向，直接返回边缘点。
    /// </summary>
    public static (double x, double y) ApproachPoint(
        double px, double py, double edgeX, double edgeY, double standoff)
    {
        double dx = px - edgeX, dy = py - edgeY;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-4)
        {
            return (edgeX, edgeY);
        }
        return (edgeX + dx / len * standoff, edgeY + dy / len * standoff);
    }

    /// <summary>够得着（边缘距离 ≤ 攻击可及）→ 砸墙；否则先贴近到攻击站位。</summary>
    public static BreachAction Decide(double edgeDistance, double attackReach)
        => edgeDistance <= attackReach ? BreachAction.Hammer : BreachAction.MoveToApproach;

    /// <summary>
    /// 在一组矩形里按**边缘距离**挑离 (px,py) 最近者，返回其下标与该边缘距离；空列表返回 -1。
    /// 供敌人在门外选定要砸的围栏/大门（边缘距离胜过中心距离，见类注）。
    /// </summary>
    public static int NearestRectByEdge(
        double px, double py,
        IReadOnlyList<(double x, double y, double w, double h)> rects,
        out double bestDistance)
    {
        int best = -1;
        bestDistance = double.PositiveInfinity;
        for (int i = 0; i < rects.Count; i++)
        {
            (double x, double y, double w, double h) = rects[i];
            double d = EdgeDistance(px, py, x, y, w, h);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }
}
