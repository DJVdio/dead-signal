using System;
using System.Collections.Generic;
using System.Numerics;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 坐标一律用 System.Numerics.Vector2（非 Godot.Vector2），与 VisionLogic 同口径；消费方转换即可。

/// <summary>
/// 一处**半身掩体**：桌子/椅子/沙袋一类**矮**物的 cartesian 轴对齐矩形 + 无伤几率。
///
/// <para><b>半身掩体 ≠ 全身遮挡（两套东西，别搞混）</b>：
///  - <b>全身遮挡</b>（墙、工作台、柜子、草垛…凡走 <c>CampMain.AddSolid</c> 的实心物）：碰撞层 0b0100=墙层
///    ⇒ <see cref="VisionOcclusion"/> 判它断视线（根本看不见）、<c>Projectile</c> 撞它就消失（根本打不到）、
///    还挖导航洞（绕着走）。<c>RaiderTactics</c> 找的"掩体"躲的就是这个。
///  - <b>半身掩体</b>（本类）：**非实心**——不建碰撞、不挖导航洞、不断视线 ⇒ **看得见、打得到、走得过**，
///    只是远程命中后按 <see cref="Chance"/> 掷一次"整发无效"。子弹从它上方/缝隙飞过是常态，
///    偶尔（25%）钉在掩体上——但**不做弹道与掩体的物理碰撞**（用户原话："这样不好做"）。</para>
/// </summary>
public readonly struct HalfCover
{
    /// <summary>矩形左上（cartesian，含）。</summary>
    public readonly Vector2 Min;
    /// <summary>矩形右下（cartesian，含）。</summary>
    public readonly Vector2 Max;
    /// <summary>躲在其后时，远程命中被整发判无效的几率 [0,1]。</summary>
    public readonly float Chance;

    /// <summary>
    /// 本掩体是否<b>阻断近战</b>。<b>围栏 = true</b>（用户拍板「不允许隔着围栏近战」——网格能看穿、能射穿，
    /// 但捅不过去）；<b>桌椅/沙袋 = false</b>（矮物，绕过去/跨过去就能贴身砍，本就不该挡近战）。
    ///
    /// <para>这条造出用户要的两难：一群丧尸贴着围栏在啃，你要么<b>开枪</b>（吃 25% 掩体惩罚 + 烧子弹 + 枪声引怪），
    /// 要么<b>开门出去打</b>（放弃整条防线），要么<b>不管</b>（墙被啃穿）——没有"站在安全的墙后拿长矛慢慢捅"
    /// 这条免费路，否则围栏就成了免费的杀戮机器，整个两难消失。</para>
    /// </summary>
    public readonly bool BlocksMelee;

    public HalfCover(Vector2 min, Vector2 max, float chance, bool blocksMelee = false)
    {
        Min = min;
        Max = max;
        Chance = Math.Clamp(chance, 0f, 1f);
        BlocksMelee = blocksMelee;
    }

    /// <summary>用 camp.json 同款 [x, y, w, h]（左上角 + 尺寸）建一处掩体。</summary>
    public static HalfCover FromRect(float x, float y, float w, float h, float chance, bool blocksMelee = false) =>
        new(new Vector2(x, y), new Vector2(x + MathF.Max(0f, w), y + MathF.Max(0f, h)), chance, blocksMelee);

    /// <summary>矩形中心（表现层高亮/描边用）。</summary>
    public Vector2 Center => (Min + Max) * 0.5f;

    /// <summary>点到本矩形的最近距离；点在矩形内为 0。</summary>
    public float DistanceTo(Vector2 p)
    {
        float dx = MathF.Max(MathF.Max(Min.X - p.X, 0f), p.X - Max.X);
        float dy = MathF.Max(MathF.Max(Min.Y - p.Y, 0f), p.Y - Max.Y);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>点是否落在矩形内（站在掩体上，不是躲在它后面）。</summary>
    public bool Contains(Vector2 p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;
}

/// <summary>
/// 半身掩体的判定纯逻辑（零 Godot 依赖，Link 进 DeadSignal.Combat.Tests）。
///
/// <para><b>用户口径（原话，不得引申）</b>：「当躲在**半身掩体**（例如桌子、椅子、沙袋）后，被**远程攻击**时，
/// 会有 **25% 的无伤概率**。逻辑上来说是敌人的子弹射在掩体上了，但是**这样不好做**，游戏的表现是
/// **这一下射中了人，但是判定 25% 几率无效**。」</para>
///
/// <para><b>落点</b>：不做弹道与掩体的物理碰撞——子弹照常飞、照常命中角色，在**承伤入口**
/// （<c>Actor.ReceiveAttack</c>，与哨塔围栏抵挡 <see cref="GuardPostMath.RangedBlocked"/> 同一层）
/// 掷一次点，中了就整发无效（不结算伤害/效果，只出"掩体挡下"飘字）。**引擎 <c>CombatResolver</c> 一字未改**
/// ⇒ Sim 既有基线结构性零漂移。</para>
///
/// <para><b>方向性</b>（"子弹射在掩体上"的前提是子弹得先撞到掩体）：只有掩体**落在射击者与目标的连线上**
/// 才生效——敌人绕到你背后，掩体白躲。这让包抄（<c>RaiderTactics</c>）有了真正的战术意义。</para>
///
/// <para><b>双向对称</b>：本类是纯几何函数、不认阵营。你朝躲在桌后的劫掠者开枪，一样吃 25% 无效。</para>
/// </summary>
public static class CoverLogic
{
    /// <summary>半身掩体的默认无伤几率（用户拍板 25%，数值拟定待调）。</summary>
    public const float DefaultCoverChance = 0.25f;

    /// <summary>
    /// <b>紧贴</b>掩体的距离阈值（世界像素，拟定待调）：目标中心到掩体矩形表面的距离须 ≤ 本值。
    ///
    /// <para>用户拍板：「掩体只有在<b>紧贴</b>掩体，并且攻击来自掩体方向时才生效」——不是"躲在桌子后面某处"，
    /// 是**得走到那张桌子边上**。取 24px：角色碰撞半径 12~13，故身体表面离掩体只剩约 10px，就是"贴着"的量级
    /// （一个身位以内）。站在房间中央声称自己有掩体不作数。</para>
    ///
    /// <para>这让掩体成为一个<b>位置决策</b>：你得放弃机动性走过去贴住它。</para>
    /// </summary>
    public const float AdjacencyRadius = 24f;

    private const float Epsilon = 1e-4f;

    /// <summary>
    /// 目标是否受这处掩体保护（**方向性**判定）。三条同时成立：
    /// ① 目标<b>贴着</b>掩体（距离 ≤ <paramref name="adjacency"/>，且**不在掩体内**——站在椅子上不算躲在椅子后）；
    /// ② 射击者<b>不在</b>掩体内（贴脸趴在同一张桌子上打，桌子救不了你）；
    /// ③ 射击者→目标的<b>连线穿过</b>掩体矩形（子弹得先经过掩体）。
    /// </summary>
    public static bool Protects(in HalfCover cover, Vector2 shooter, Vector2 target,
        float adjacency = AdjacencyRadius)
    {
        if (cover.Chance <= 0f)
            return false;

        // ① 站在掩体上 ≠ 躲在掩体后。不排除的话，连线终点落在矩形内 → 任何方向都相交 → 360° 无死角掩体。
        if (cover.Contains(target))
            return false;

        float d = cover.DistanceTo(target);
        if (d <= Epsilon || d > adjacency)
            return false;

        // ② 射击者站在掩体上/趴在掩体里：连线起点即在矩形内，恒相交 → 会误判成"绕后也有掩体"。
        if (cover.Contains(shooter))
            return false;

        // ③ 连线是否穿过掩体（slab 法，见 SegmentHitsBox）。
        return SegmentHitsBox(shooter, target, cover.Min, cover.Max);
    }

    /// <summary>
    /// 场上诸掩体对该次射击的**有效无伤几率**：取所有保护该目标的掩体中最高的 <see cref="HalfCover.Chance"/>；
    /// 一处都不保护（含绕后、站太远）→ 0。**不叠加**（躲两张桌子后面不等于 50%）。
    /// </summary>
    public static float CoverChanceFor(IReadOnlyList<HalfCover> covers, Vector2 shooter, Vector2 target,
        float adjacency = AdjacencyRadius)
    {
        float best = 0f;
        for (int i = 0; i < covers.Count; i++)
        {
            HalfCover c = covers[i];
            if (c.Chance > best && Protects(c, shooter, target, adjacency))
                best = c.Chance;
        }
        return best;
    }

    /// <summary>
    /// **掷点**：本次命中是否被掩体整发判无效。
    ///
    /// <para><b>只对远程生效</b>（<paramref name="ranged"/>=false 直接短路——贴身砍你，桌子挡不住）。</para>
    /// <para><b>零漂移护栏</b>：<paramref name="coverChance"/> ≤ 0（场上无掩体/已绕后/近战）时
    /// **一个数都不从随机源里取**（短路求值），随机流与既有路径逐位一致。同
    /// <see cref="GuardPostMath.RangedBlocked"/> 的写法。</para>
    /// </summary>
    public static bool Negates(bool ranged, float coverChance, IRandomSource rng)
        => ranged && coverChance > 0f && rng.Range(0.0, 1.0) < coverChance;

    /// <summary>
    /// <b>近战是否被阻断</b>：攻击者与目标之间是否横着一处 <see cref="HalfCover.BlocksMelee"/> 的掩体（围栏）。
    ///
    /// <para>用户拍板「<b>不允许隔着围栏近战</b>」。这条必须显式判——**光靠碰撞体挡不住**：围栏厚 22px，
    /// 丧尸 Radius 13 + AttackRange 24 ⇒ 够得着 49px，而隔栏两边贴住时中心距只有 12+22+13=47px
    /// &lt; 49 ⇒ <b>丧尸能隔着栅栏咬到你</b>（长兵器同理）。故在出手前用同一套线段-矩形几何拦掉。</para>
    ///
    /// <para>只拦 <see cref="HalfCover.BlocksMelee"/> 的（围栏）；桌椅/沙袋不拦（矮物，绕过去就能砍）。</para>
    /// </summary>
    public static bool MeleeBlocked(IReadOnlyList<HalfCover> covers, Vector2 attacker, Vector2 target)
    {
        for (int i = 0; i < covers.Count; i++)
        {
            HalfCover c = covers[i];
            if (!c.BlocksMelee)
                continue;
            // 双方都在围栏里/同侧贴着时不算阻断（端点在矩形内会恒相交）——只拦"隔着它"的那种。
            if (c.Contains(attacker) || c.Contains(target))
                continue;
            if (SegmentHitsBox(attacker, target, c.Min, c.Max))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 目标当前**贴着**的掩体（不看射击方向），供表现层高亮"你有掩体可用"。返回距离最近的一处；无则 null。
    /// 与 <see cref="Protects"/> 的①②同口径（站在掩体上不算），避免提示与实际判定打架。
    /// </summary>
    public static HalfCover? AdjacentCover(IReadOnlyList<HalfCover> covers, Vector2 pos,
        float adjacency = AdjacencyRadius)
    {
        HalfCover? best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < covers.Count; i++)
        {
            HalfCover c = covers[i];
            if (c.Chance <= 0f || c.Contains(pos))
                continue;
            float d = c.DistanceTo(pos);
            if (d > Epsilon && d <= adjacency && d < bestDist)
            {
                bestDist = d;
                best = c;
            }
        }
        return best;
    }

    /// <summary>
    /// 线段 [a,b] 是否与轴对齐矩形 [min,max] 相交（slab 法）。两端点重合时退化为"点是否在矩形内"。
    /// 端点在矩形内也算相交——调用方（<see cref="Protects"/>）已先行排除了那两种情形。
    /// </summary>
    private static bool SegmentHitsBox(Vector2 a, Vector2 b, Vector2 min, Vector2 max)
    {
        Vector2 d = b - a;
        float t0 = 0f, t1 = 1f;

        for (int axis = 0; axis < 2; axis++)
        {
            float da = axis == 0 ? d.X : d.Y;
            float aa = axis == 0 ? a.X : a.Y;
            float lo = axis == 0 ? min.X : min.Y;
            float hi = axis == 0 ? max.X : max.Y;

            if (MathF.Abs(da) < Epsilon)
            {
                // 该轴上线段不动：起点必须已落在 slab 内，否则永不相交。
                if (aa < lo || aa > hi)
                    return false;
                continue;
            }

            float inv = 1f / da;
            float tNear = (lo - aa) * inv;
            float tFar = (hi - aa) * inv;
            if (tNear > tFar)
                (tNear, tFar) = (tFar, tNear);

            t0 = MathF.Max(t0, tNear);
            t1 = MathF.Min(t1, tFar);
            if (t0 > t1)
                return false;
        }
        return true;
    }
}

/// <summary>
/// 当前关卡的半身掩体场（纯逻辑注册表，零 Godot 依赖）。关卡建场时把每处掩体 <see cref="Add"/> 进来，
/// 承伤入口（<c>Actor.ReceiveAttack</c>）按射击者/目标位置问 <see cref="ChanceFor"/>，表现层问 <see cref="AdjacentTo"/>。
/// <b>换关/场景重载务必 <see cref="Clear"/></b>，否则残留上一关的掩体。
/// </summary>
public sealed class CoverField
{
    private readonly List<HalfCover> _covers = new();

    /// <summary>场上已登记的掩体（只读；表现层遍历高亮用）。</summary>
    public IReadOnlyList<HalfCover> Covers => _covers;

    public void Add(HalfCover cover)
    {
        if (cover.Chance > 0f)
            _covers.Add(cover);
    }

    /// <summary>用 camp.json 同款 [x,y,w,h] 登记一处掩体。<paramref name="blocksMelee"/>=true 即围栏（隔着它捅不到）。</summary>
    public void Add(float x, float y, float w, float h, float chance = CoverLogic.DefaultCoverChance,
        bool blocksMelee = false)
        => Add(HalfCover.FromRect(x, y, w, h, chance, blocksMelee));

    /// <summary>
    /// 移除一处掩体（按矩形匹配）。<b>围栏被啃穿/拆除时必须调</b>——否则那段墙没了，掩体判定还留在原地，
    /// 玩家会在一个空洞后面白享 25%。返回是否真的移除了一处。
    /// </summary>
    public bool RemoveRect(float x, float y, float w, float h)
    {
        var target = HalfCover.FromRect(x, y, w, h, 1f);
        for (int i = 0; i < _covers.Count; i++)
        {
            HalfCover c = _covers[i];
            if (Near(c.Min, target.Min) && Near(c.Max, target.Max))
            {
                _covers.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    private static bool Near(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) < 0.01f;

    public void Clear() => _covers.Clear();

    /// <summary>本次射击的有效无伤几率（方向性；无掩体保护 → 0，调用方据此短路、不掷点）。</summary>
    public float ChanceFor(Vector2 shooter, Vector2 target) =>
        CoverLogic.CoverChanceFor(_covers, shooter, target);

    /// <summary>该位置贴着的掩体（表现层"你有掩体可用"提示）；无则 null。</summary>
    public HalfCover? AdjacentTo(Vector2 pos) => CoverLogic.AdjacentCover(_covers, pos);

    /// <summary>攻击者与目标之间是否隔着围栏（<b>近战打不过去</b>）。见 <see cref="CoverLogic.MeleeBlocked"/>。</summary>
    public bool MeleeBlockedBetween(Vector2 attacker, Vector2 target) =>
        CoverLogic.MeleeBlocked(_covers, attacker, target);
}
