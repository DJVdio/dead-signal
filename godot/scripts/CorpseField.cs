using System;
using System.Collections.Generic;
using System.Numerics;

namespace DeadSignal.Godot;

/// <summary>尸体落位用的概念格坐标（整数格，非世界像素）。</summary>
public readonly record struct CorpseCell(int X, int Y);

/// <summary>
/// 一次落尸的结果：<paramref name="Cell"/> 实际占的格、<paramref name="Position"/> 该格中心的世界坐标、
/// <paramref name="Displaced"/> 是否被从死亡点挤开、<paramref name="Stacked"/> 是否退化为堆叠（搜索半径内已无空位）。
/// </summary>
public readonly record struct CorpsePlacement(CorpseCell Cell, Vector2 Position, bool Displaced, bool Stacked);

/// <summary>
/// 尸体不堆叠推挤（RimWorld 式）的纯逻辑（零 Godot 依赖，Link 进 DeadSignal.Combat.Tests）。
///
/// 【本作不是格子制】地图是连续像素坐标（faux-iso 只是渲染投影，寻路走 NavigationAgent2D），所以「一格」
/// 是**为尸体单独定义的概念格**：把世界坐标按 <see cref="CellSize"/> 量化，同一格最多躺一具尸体。它
/// **只用于尸体落位去重**，不参与寻路、不参与碰撞、不参与视野——等价于「尸体之间至少隔一个身位」的最小间距。
///
/// 【尸体没有碰撞体积】本类不提供、也永远不该提供任何「此处是否被尸体挡住」的查询：活人和丧尸从尸体上直接
/// 走过去。通行性（墙/水/不可通行区）是**调用方注入**的 <c>passable</c> 谓词（Godot 层用导航图判定），
/// 本类只读不写——这是「尸体不阻挡移动」在纯逻辑层的结构性保证（见 CorpsePushTests）。
///
/// 【推挤策略】家格（死亡点所在格）空且可通行 → 就地躺下；否则按**同心环外扩**（切比雪夫环 r=1..
/// <see cref="MaxSearchRing"/>），环内候选按 (欧氏距离², dy, dx) 升序取第一个「空且可通行」的格：
/// 正交邻居先于对角邻居，完全确定性（无随机，故不需要 IRandomSource）。
///
/// 【边界】① 搜索窗内全满 → 退化为**堆叠**在搜索过程中遇到的最近的「可通行」格（通常就是家格）——宁可
/// 破一次「不堆叠」，也绝不让尸体凭空消失。② 死亡点落在墙里/不可通行区（贴墙被击杀等）→ 同样外扩到可通行
/// 地面，任何情况下都不会把尸体留在墙里。③ 搜索窗 (2r+1)² 与场上尸体总数无关 → 落一具尸体是 O(1) 常数级，
/// 不遍历全场（占用集是 HashSet，命中查询 O(1)）。
///
/// 【战术后果】丧尸有碰撞体积、尸体没有：门前的尸体不挡路（可以踩过去），但会**占掉尸体格**，把后来的
/// 尸体推得越来越远——尸堆铺开成一片，而不是在门口叠成一柱。这与「丧尸碰撞体积 + 近战距离 ⇒ 一扇门前只
/// 站得下 3~4 只」是两件事：前者占「尸体格」，后者占「站人格」。
///
/// 数值皆「拟定待调」。
/// </summary>
public sealed class CorpseField
{
    /// <summary>尸体概念格边长（世界像素）。≈ 一个人形躺下的占位（Actor 半径 12~13 → 直径 24~26）。「拟定待调」</summary>
    public const float CellSize = 32f;

    /// <summary>找空位的最大外扩环数（切比雪夫半径）。6 环 = 最远挤出 6×32 = 192px。「拟定待调」</summary>
    public const int MaxSearchRing = 6;

    private readonly HashSet<CorpseCell> _occupied = new();

    /// <summary>
    /// 搜索窗内所有候选偏移（含 (0,0) 家格自己），按 (切比雪夫环, 欧氏距离², dy, dx) 升序**预计算一次**：
    /// 由近及远、正交先于对角、同距固定序 —— 落一具尸体只是顺着这张表走，零分配零排序，
    /// 且与场上尸体总数无关（(2r+1)² = 169 项上限）。偏移只依赖半径，不依赖家格，故可静态复用。
    /// </summary>
    private static readonly (int Dx, int Dy)[] SearchOffsets = BuildSearchOffsets();

    private static (int, int)[] BuildSearchOffsets()
    {
        int r = MaxSearchRing;
        var all = new List<(int Dx, int Dy)>((2 * r + 1) * (2 * r + 1));
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                all.Add((dx, dy));
            }
        }
        all.Sort((a, b) =>
        {
            int ringA = Math.Max(Math.Abs(a.Dx), Math.Abs(a.Dy));
            int ringB = Math.Max(Math.Abs(b.Dx), Math.Abs(b.Dy));
            int cmp = ringA.CompareTo(ringB);
            if (cmp != 0) return cmp;
            cmp = (a.Dx * a.Dx + a.Dy * a.Dy).CompareTo(b.Dx * b.Dx + b.Dy * b.Dy);
            if (cmp != 0) return cmp;
            cmp = a.Dy.CompareTo(b.Dy);
            return cmp != 0 ? cmp : a.Dx.CompareTo(b.Dx);
        });
        return all.ToArray();
    }

    /// <summary>场上已被尸体占掉的格数。</summary>
    public int Count => _occupied.Count;

    /// <summary>世界坐标 → 概念格（floor，负坐标不能截断）。</summary>
    public static CorpseCell CellOf(Vector2 world) =>
        new((int)MathF.Floor(world.X / CellSize), (int)MathF.Floor(world.Y / CellSize));

    /// <summary>概念格 → 该格中心的世界坐标（尸体实际躺的点）。</summary>
    public static Vector2 CenterOf(CorpseCell cell) =>
        new((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize);

    /// <summary>该格是否已躺着一具尸体。</summary>
    public bool IsOccupied(CorpseCell cell) => _occupied.Contains(cell);

    /// <summary>
    /// 只算不登记：给定死亡点与通行谓词，返回尸体该躺哪一格。纯函数（不改状态），供预览/测试用。
    /// <paramref name="passable"/> 收格中心的世界坐标，返回该处是否可通行（墙/水/不可通行区 → false）。
    /// </summary>
    public CorpsePlacement Resolve(Vector2 deathPos, Func<Vector2, bool> passable)
    {
        ArgumentNullException.ThrowIfNull(passable);
        var home = CellOf(deathPos);

        // 堆叠兜底：搜索中遇到的第一个「可通行」格（不论是否已被占）——万一搜索窗内全满就堆在这。
        CorpseCell fallback = home;
        bool hasFallback = false;

        // 由近及远走预计算的候选表（第一项就是家格本身）。绝不遍历全场尸体。
        foreach (var (dx, dy) in SearchOffsets)
        {
            var cell = new CorpseCell(home.X + dx, home.Y + dy);
            var center = CenterOf(cell);
            if (!passable(center))
            {
                continue;   // 墙/水/不可通行区：既不能躺，也不能当堆叠兜底
            }
            if (!_occupied.Contains(cell))
            {
                bool displaced = dx != 0 || dy != 0;
                return new CorpsePlacement(cell, center, displaced, Stacked: false);
            }
            if (!hasFallback)
            {
                fallback = cell;
                hasFallback = true;
            }
        }

        // 搜索窗内一个空位都没有：宁可堆叠也不让尸体消失。落点必为可通行格
        // （除非四下全是墙——那时只能退回家格，此路径下无解可选）。
        return new CorpsePlacement(fallback, CenterOf(fallback), Displaced: fallback != home, Stacked: true);
    }

    /// <summary>落一具尸体：解算落点并登记占用。返回实际落点。</summary>
    public CorpsePlacement Place(Vector2 deathPos, Func<Vector2, bool> passable)
    {
        var p = Resolve(deathPos, passable);
        _occupied.Add(p.Cell);   // 堆叠时该格已在集合里，Add 幂等
        return p;
    }

    /// <summary>尸体被清走（焚烧/搬走/关卡回收）→ 释放格位，原地可以再躺人。</summary>
    public void Remove(CorpseCell cell) => _occupied.Remove(cell);

    /// <summary>清空（换关/重开）。</summary>
    public void Clear() => _occupied.Clear();
}
