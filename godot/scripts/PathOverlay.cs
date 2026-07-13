using System;
using System.Collections.Generic;
using Godot;
using SysVec2 = System.Numerics.Vector2;

namespace DeadSignal.Godot;

/// <summary>
/// 移动路径线（RimWorld 式）：把**己方每一个玩家单位**接下来真要走的路同时画出来（不是只画选中那一个）。
///
/// 战术信息工具、非装饰：路径是各角色 <see cref="NavigationAgent2D"/> 的**真实当前路径**（<see cref="Actor.NavPathCart"/>），
/// 不是起点→终点的直线预测。同屏看到所有人的路线，玩家不必挨个点选就能看出：
/// 两条路会不会撞在一起（谁挡谁）、谁的路线会横穿丧尸的视野锥（<see cref="VisionLogic"/>）、
/// 谁会贴着墙走（脚步声 40 半径惊动屋里的东西，<see cref="NoiseLogic"/>）。门一关 → 寻路改道 → 线跟着改道。
///
/// <para><b>画谁</b>（<see cref="MovePathVisual.ShouldDraw"/>）：只画供给方给的己方单位（活着 + 手头有没走完的路径）。
/// 敌方（丧尸/劫掠者）的路径**绝不画**——那是作弊级信息。</para>
///
/// <para><b>怎么分清是谁的</b>：每条线用该角色自己的 <see cref="Actor.BodyTint"/>（= camp.json spawns 里的角色配色，
/// 山姆蓝/诺蒂绿），与人形主色同源，一眼对得上人。选中者更粗更实、未选中者细而半透明
/// （<see cref="MovePathVisual.StrokeFor"/>）——全局态势与当前焦点两不误，多条线并存也不盖住地面信息
/// （物资点/尸体/血迹/废墟）。</para>
///
/// 层级：与 <see cref="VisionMask"/> 同为 CampMain 直接子节点，<see cref="ZIndex"/>=4090 &gt; 遮暗层 4000
/// → 夜间遮暗区里的路径线依然可读；每条线都带一层黑色暗垫线，白天亮地面上同样勾得出来。
/// 绘制在 iso 屏幕空间（<see cref="Iso.Project"/>），几何（剩余折线/虚线切段）走纯逻辑 <see cref="MovePathVisual"/>。
///
/// 性能：每帧只对**手头真有导航路径**的己方单位做「读一次已缓存路径（不寻路）+ 一个整数哈希脏检」；
/// 谁的路径没变且位移 &lt; 3px 就不重建他那条线。**全员都无变化 → 零 <see cref="CanvasItem.QueueRedraw"/>**。
/// </summary>
public sealed partial class PathOverlay : Node2D
{
    /// <summary>盖在遮暗层（VisionMask=4000）之上，仍在 HUD CanvasLayer 之下。上限 4096 是 Godot 硬约束（超了会被拒→掉回 0，藏到遮暗底下）。</summary>
    public const int PathZIndex = 4090;

    /// <summary>暗垫线：夜间遮暗上是亮线压暗底、白天亮地面上是暗边勾亮线，两种底色都能读。</summary>
    private static readonly Color BackingColor = new(0f, 0f, 0f, 0.5f);

    /// <summary>暗垫线比亮线宽出多少（两侧各半）。</summary>
    private const float BackingExtra = 2.2f;

    /// <summary>角色位移超过此距离（cartesian 像素）才重建他那条线；走路时约每 2~3 帧一次，静止时零重建。</summary>
    private const float MoveRedrawEpsilon = 3f;

    /// <summary>一个角色当前该画的线（已投影到 iso 屏幕空间）+ 他自己的脏检缓存（cartesian）。</summary>
    private sealed class Entry
    {
        public readonly List<(Vector2 A, Vector2 B)> Dashes = new();
        public Vector2 End;
        public bool HasEnd;
        public Color Color;
        public bool Selected;

        public bool Built;
        public int LastIndex = -1;
        public int LastPathKey;
        public Vector2 LastActorCart;
    }

    private Func<IEnumerable<Actor>>? _unitsProvider;   // 己方玩家单位供给（CampMain 给活着的幸存者）
    private Actor? _selected;                            // 当前选中者（只影响醒目程度；未选中者照画）

    private readonly Dictionary<Actor, Entry> _entries = new();
    private readonly List<Actor> _live = new();          // 本帧真有路径可画的角色
    private readonly List<Actor> _stale = new();         // 本帧要淘汰的条目（死了/走完了/离场了）

    public override void _Ready()
    {
        ZIndex = PathZIndex;
        TopLevel = true; // 不吃父节点变换（与 VisionMask 一致：自己就画在 iso 世界坐标上）
    }

    /// <summary>己方玩家单位供给（CampMain 给活着的幸存者）。敌方绝不入列。</summary>
    public void SetUnitsProvider(Func<IEnumerable<Actor>> provider) => _unitsProvider = provider;

    /// <summary>谁是当前选中者（只影响**醒目程度**，不影响画不画）。</summary>
    public void SetSelected(Actor? actor)
    {
        if (ReferenceEquals(_selected, actor))
        {
            return;
        }
        _selected = actor;
        foreach ((Actor a, Entry e) in _entries)
        {
            e.Selected = ReferenceEquals(a, actor);
        }
        QueueRedraw(); // 只是笔触（粗细/透明度）变了，线的几何没变 → 不重建
    }

    public override void _Process(double delta)
    {
        if (_unitsProvider is null)
        {
            return;
        }

        bool dirty = false;
        _live.Clear();

        foreach (Actor actor in _unitsProvider())
        {
            if (actor is null || !GodotObject.IsInstanceValid(actor))
            {
                continue;
            }

            // 画谁：己方单位（供给方保证）+ 活着 + 手头有没走完的路径。站着不动/已到达 → 不画、也不占缓存。
            // 敌方不在供给里，故永远不会被画。
            if (!MovePathVisual.ShouldDraw(isPlayerUnit: true, actor.Alive, actor.HasNavPath))
            {
                continue;
            }

            // 读的是导航系统**已经算好并缓存**的路径（不触发寻路）。
            Vector2[] path = actor.NavPathCart();
            if (path.Length == 0)
            {
                continue;
            }

            _live.Add(actor);

            int index = actor.NavPathIndex;
            Vector2 actorCart = actor.GlobalPosition;
            int pathKey = PathKey(path);

            if (!_entries.TryGetValue(actor, out Entry? entry))
            {
                entry = new Entry();
                _entries[actor] = entry;
            }
            else if (entry.Built
                && index == entry.LastIndex
                && pathKey == entry.LastPathKey
                && actorCart.DistanceSquaredTo(entry.LastActorCart) < MoveRedrawEpsilon * MoveRedrawEpsilon)
            {
                // 路径没变（含关门导致的导航重烘焙→改道）+ 推进下标没变 + 没明显位移 → 他这条线原样留着。
                continue;
            }

            Rebuild(entry, actor, path, index, actorCart);
            entry.Built = true;
            entry.LastIndex = index;
            entry.LastPathKey = pathKey;
            entry.LastActorCart = actorCart;
            dirty = true;
        }

        // 淘汰本帧没路径可画的条目（走到了/收到 CancelOrders/死亡/离场）。
        if (_entries.Count > _live.Count)
        {
            _stale.Clear();
            foreach (Actor a in _entries.Keys)
            {
                if (!_live.Contains(a))
                {
                    _stale.Add(a);
                }
            }
            foreach (Actor a in _stale)
            {
                _entries.Remove(a);
                dirty = true;
            }
        }

        if (dirty)
        {
            QueueRedraw();
        }
    }

    /// <summary>cartesian 路径 → 剩余折线 → 虚线短划 → iso 投影缓存（几何全走 <see cref="MovePathVisual"/> 纯函数）。</summary>
    private void Rebuild(Entry entry, Actor actor, Vector2[] path, int index, Vector2 actorCart)
    {
        entry.Dashes.Clear();
        entry.HasEnd = false;
        entry.Color = actor.BodyTint;                    // 角色自己的配色（camp.json spawns.color）
        entry.Selected = ReferenceEquals(actor, _selected);

        var pts = new SysVec2[path.Length];
        for (int i = 0; i < path.Length; i++)
        {
            pts[i] = new SysVec2(path[i].X, path[i].Y);
        }

        List<SysVec2> poly = MovePathVisual.RemainingPolyline(pts, index, new SysVec2(actorCart.X, actorCart.Y));
        if (poly.Count < 2)
        {
            return; // 已到终点/退化 → 无线可画
        }

        foreach ((SysVec2 a, SysVec2 b) in MovePathVisual.Dashes(poly, MovePathVisual.DashLength, MovePathVisual.GapLength))
        {
            entry.Dashes.Add((Iso.Project(new Vector2(a.X, a.Y)), Iso.Project(new Vector2(b.X, b.Y))));
        }

        SysVec2 last = poly[^1];
        entry.End = Iso.Project(new Vector2(last.X, last.Y));
        entry.HasEnd = true;
    }

    /// <summary>路径点序列的廉价哈希（点数 ≤ 数十，O(n) 可忽略）：路径一改（重下令 / 关门导致改道）此值必变。</summary>
    private static int PathKey(Vector2[] path)
    {
        var hash = new HashCode();
        hash.Add(path.Length);
        foreach (Vector2 p in path)
        {
            hash.Add(p.X);
            hash.Add(p.Y);
        }
        return hash.ToHashCode();
    }

    public override void _Draw()
    {
        // 先画未选中的（淡），选中的最后画 → 焦点那条压在最上、不被别人的线盖住。
        foreach (Entry e in _entries.Values)
        {
            if (!e.Selected)
            {
                DrawEntry(e);
            }
        }
        foreach (Entry e in _entries.Values)
        {
            if (e.Selected)
            {
                DrawEntry(e);
            }
        }
    }

    private void DrawEntry(Entry e)
    {
        if (e.Dashes.Count == 0 && !e.HasEnd)
        {
            return;
        }

        MovePathVisual.Stroke stroke = MovePathVisual.StrokeFor(e.Selected);
        var line = new Color(e.Color.R, e.Color.G, e.Color.B, stroke.Alpha);
        // 暗垫线的不透明度跟着线走：淡线配淡垫，未选中那几条不会在地面上压出一圈黑边。
        var backing = new Color(BackingColor.R, BackingColor.G, BackingColor.B, BackingColor.A * stroke.Alpha);
        float backWidth = stroke.Width + BackingExtra;

        // 两遍：先全部暗垫线，再全部亮线（一遍一色，避免亮线被后画的垫线盖住）。
        foreach ((Vector2 a, Vector2 b) in e.Dashes)
        {
            DrawLine(a, b, backing, backWidth, true);
        }
        foreach ((Vector2 a, Vector2 b) in e.Dashes)
        {
            DrawLine(a, b, line, stroke.Width, true);
        }

        if (e.HasEnd)
        {
            DrawDestination(e.End, line, backing, stroke.Width);
        }
    }

    /// <summary>终点标记：贴地的 iso 菱形（与地面瓦片同构，读作"他停在这一格"），用该角色自己的颜色。</summary>
    private void DrawDestination(Vector2 c, Color line, Color backing, float width)
    {
        float hw = Iso.HalfW * 0.55f;
        float hh = Iso.HalfH * 0.55f;
        Vector2[] diamond =
        {
            c + new Vector2(0, -hh),
            c + new Vector2(hw, 0),
            c + new Vector2(0, hh),
            c + new Vector2(-hw, 0),
            c + new Vector2(0, -hh),
        };

        DrawPolyline(diamond, backing, width + BackingExtra, true);
        DrawPolyline(diamond, line, width, true);
    }
}
