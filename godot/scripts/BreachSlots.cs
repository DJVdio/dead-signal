using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

/// <summary>一处可砸结构的破防候选：稳定 Id（= CampMain 结构表下标）+ 矩形 + 攻击位名额。</summary>
public readonly record struct BreachCandidate(int Id, double X, double Y, double W, double H, int Capacity);

/// <summary>
/// 「谁占着哪处结构的攻击位」的账本（纯逻辑，无 Godot 依赖）。
/// <para>
/// <b>它建模的是空间物理</b>：攻击者有碰撞体积、近战是贴身距离 ⇒ <b>一处结构前只站得下几只</b>，后面的挤不进来。
/// 此前这条约束**在代码里根本不存在**（谁都能砸边缘最近的那处），于是一波丧尸会全叠在同一扇门上砸；
/// 而 Sim 也没有碰撞体积，只会做线性叠加 —— 两边一起失真。名额账本把它显式化：<b>规则层和 Sim 从此共用同一条空间约束。</b>
/// </para>
/// </summary>
public sealed class BreachSlotBook
{
    private readonly Dictionary<ulong, int> _held = new();   // 攻击者 → 它占着的结构 Id
    private readonly Dictionary<int, int> _count = new();    // 结构 Id → 当前占位数

    /// <summary>该结构当前被几个攻击者占着。</summary>
    public int Occupancy(int target) => _count.TryGetValue(target, out int n) ? n : 0;

    /// <summary>该攻击者当前占着哪处结构（没占 = null）。</summary>
    public int? HeldBy(ulong attacker) => _held.TryGetValue(attacker, out int t) ? t : null;

    /// <summary>
    /// 占一个位。已经占着同一处 → 直接成功且<b>不重复计数</b>（黏性：攻击者不会每帧把自己数一遍）。
    /// 占着别处 → 先松开旧的。满员 → 失败（调用方去挑别处）。
    /// </summary>
    public bool TryClaim(ulong attacker, int target, int capacity)
    {
        if (_held.TryGetValue(attacker, out int cur))
        {
            if (cur == target)
            {
                return true;
            }
            Release(attacker);
        }
        if (Occupancy(target) >= capacity)
        {
            return false;
        }
        _held[attacker] = target;
        _count[target] = Occupancy(target) + 1;
        return true;
    }

    /// <summary>攻击者退场（死亡 / 转为常规追击）→ 把位子还回去，后面挤着的那只顶上来。</summary>
    public void Release(ulong attacker)
    {
        if (!_held.Remove(attacker, out int target))
        {
            return;
        }
        int n = Occupancy(target) - 1;
        if (n > 0)
        {
            _count[target] = n;
        }
        else
        {
            _count.Remove(target);
        }
    }

    /// <summary>这处结构被砸穿了 → 占着它的全部松开（下一帧它们改走缺口 / 另择目标）。</summary>
    public void ReleaseTarget(int target)
    {
        _count.Remove(target);
        var holders = new List<ulong>();
        foreach (KeyValuePair<ulong, int> kv in _held)
        {
            if (kv.Value == target)
            {
                holders.Add(kv.Key);
            }
        }
        foreach (ulong h in holders)
        {
            _held.Remove(h);
        }
    }
}

/// <summary>
/// 「攻击位名额」纯逻辑（无 Godot 依赖，Link 进单测与 Sim）：一处结构前站得下几个攻击者，以及**挤不进去的该砸哪儿**。
///
/// <para>
/// <b>它修的是什么</b>：用户拍板「<b>丧尸也会打围栏（墙）的，不止会打门</b>」。查下来，丧尸的 AI 里**本来就没有"只打门"这回事**
/// （<c>BreachController</c> 择的是"边缘距离最近的未毁结构"，围栏和门在同一个候选池里，无 Kind 过滤）——
/// 它们全砸门，纯粹是因为 <c>SpawnCampZombies</c> 把它们**生成在门缝正前方 40px 处**，一出生就够得着门
/// （<c>attackReach = 47px</c>），连一步都不用走。
/// 缺的不是"能不能打墙"，而是 <b>"门口站满了之后该怎么办"</b>：此前的答案是"大家一起砸"（无名额概念），
/// 现在的答案是 <b>"挤不进门口的，就啃旁边的墙"</b> —— 受攻击面从两扇门摊开到整条墙线。
/// </para>
///
/// <para>
/// <b>丧尸的核心行为一个字没改</b>：它仍是直线冲上来的蠢货。<see cref="ChooseTarget"/> 只在**它面前的搜索半径内**
/// 挑一个还站得下人的东西砸；半径内全满 ⇒ 返回 -1，它就在原地挤着 —— <b>绝不会绕到营地另一头去找空位</b>
/// （那是包抄，是人类敌人才有的行为）。
/// </para>
/// </summary>
public static class BreachSlots
{
    /// <summary>
    /// 一个攻击者贴墙时的占位宽度（像素）。取丧尸直径（<c>Radius 13 × 2</c>）＝ 肩并肩紧贴的物理下限。
    /// <b>拟定待调</b>：调大 = 站得更松、每处名额更少、受攻击面摊得更开（如取 39 ⇒ 大门只站得下 5 只）。
    /// </summary>
    public const double DefaultFootprint = 26.0;

    /// <summary>
    /// 这处结构前站得下几个攻击者 ＝ <b>可攻击面长度 ÷ 占位宽度</b>（至少 1 —— 否则会出现"谁都打不了的结构"）。
    /// 可攻击面取矩形的**长边**：攻击者从墙外贴上来、沿着墙面一字排开（22px 的墙厚不是给人站的）。
    /// </summary>
    public static int Capacity(double w, double h, double footprint)
    {
        double face = Math.Max(w, h);
        if (footprint <= 0)
        {
            return 1;
        }
        return Math.Max(1, (int)Math.Floor(face / footprint));
    }

    /// <summary>
    /// 择一处还站得下人的结构去砸，并当场占位。返回结构 Id；半径内没有空位可占则返回 <c>-1</c>
    /// （调用方交回常规行为：在后面挤着，而不是绕远路）。
    /// <list type="bullet">
    /// <item><b>黏性优先</b>：已经占着的那处只要还在候选里、还在半径内，就继续砸它（不来回横跳）。</item>
    /// <item><b>否则</b>：在半径内、**还有空位**的结构里按边缘距离取最近者 —— 门口满了，最近的空位就是紧挨着门的那格围栏。</item>
    /// </list>
    /// </summary>
    public static int ChooseTarget(
        double px, double py, IReadOnlyList<BreachCandidate> candidates, BreachSlotBook book,
        ulong attacker, double radius, out double edgeDistance, out double edgeX, out double edgeY)
    {
        edgeDistance = double.PositiveInfinity;
        edgeX = px;
        edgeY = py;

        // 黏性：还占着的那处仍然有效就不改主意。
        if (book.HeldBy(attacker) is int held)
        {
            bool stillValid = false;
            foreach (BreachCandidate c in candidates)
            {
                if (c.Id != held)
                {
                    continue;
                }
                (double hx, double hy) = BreachLogic.NearestPointOnRect(px, py, c.X, c.Y, c.W, c.H);
                double hd = BreachLogic.Distance(px, py, hx, hy);
                if (hd <= radius)
                {
                    edgeDistance = hd;
                    edgeX = hx;
                    edgeY = hy;
                    stillValid = true;
                }
                break;
            }
            if (stillValid)
            {
                return held;
            }
            book.Release(attacker); // 目标砸穿了 / 走远了 → 松位重挑
        }

        int best = -1;
        double bestD = radius;
        double bx = px, by = py;
        int bestCap = 0;
        foreach (BreachCandidate c in candidates)
        {
            if (book.Occupancy(c.Id) >= c.Capacity)
            {
                continue; // 站满了，挤不进去 —— 换一处
            }
            (double ex, double ey) = BreachLogic.NearestPointOnRect(px, py, c.X, c.Y, c.W, c.H);
            double d = BreachLogic.Distance(px, py, ex, ey);
            if (d < bestD)
            {
                bestD = d;
                best = c.Id;
                bx = ex;
                by = ey;
                bestCap = c.Capacity;
            }
        }

        if (best < 0 || !book.TryClaim(attacker, best, bestCap))
        {
            return -1;
        }

        edgeDistance = bestD;
        edgeX = bx;
        edgeY = by;
        return best;
    }
}
