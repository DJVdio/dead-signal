using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>门态徽标</b>：每扇门头顶浮一枚小牌子，把它此刻**开着 / 关着 / 闩着 / 锁着**画成<b>四个不同的形状</b>。
///
/// <para>
/// <b>它堵的洞</b>：闩着和关着此前在画面上<b>长得一模一样</b>（门板视觉都是同一块，只有悬停提示文字区分）。
/// 玩家根本一眼看不出大门到底闩没闩——而<b>这正是攸关生死的一件事</b>：只是"关着"的门，
/// 劫掠者一推就开（<see cref="DoorState.Closed"/>）；"闩着"的门他只能砸（250HP + 180 的砸门声招来丧尸）。
/// 两者天差地别，画面上却毫无区别。
/// </para>
///
/// <para>
/// <b>形状，不是颜色</b>（<see cref="DoorBadgeVisual.GlyphFor"/>，单射，单测钉死）：
/// 空门框 / 门把手 / <b>一道横闩</b> / <b>一把挂锁</b>。颜色只做增强（灰 / 琥珀 / 绿 / 蓝），
/// 因为色觉障碍玩家读不到颜色，而夜间遮暗会把所有颜色都压向同一个暗调——<b>偏偏夜里才是门闩最要命的时候</b>。
/// 锁的档次也用形状编码：锁体上的竖刻痕数（简单 ‖ / 普通 ‖‖ / 坚固 ‖‖‖），玩家据此判断值不值得撬。
/// </para>
///
/// <para>
/// <b>层级</b>：与 <see cref="VisionMask"/>/<see cref="PathOverlay"/> 同为 <c>CampMain</c> 直接子节点、
/// <c>TopLevel</c> 自己画在 iso 世界坐标上。<see cref="BadgeZIndex"/>=4080 <b>&gt; 遮暗层 4000</b>
/// ——夜间遮暗区里的门态依然读得到（这是刻意的：门闩在夜里才决定生死）；&lt; 路径线 4090，不抢玩家当前的操作焦点。
/// </para>
///
/// <para>
/// ⚠️ <b>项目铁律：只映射引擎里真实存在的状态</b>。本类的输入只有 <see cref="DoorState"/> 与 <see cref="LockTier"/>
/// ——引擎里真有的两样东西。<b>不发明</b>"门快撑不住了""正在被砸"之类的状态（HP 条是另一回事，不归这里）。
/// </para>
///
/// <para>
/// 性能：每帧只对门做一次整数哈希脏检（门的数量是个位数），状态没变 → <b>零</b> <see cref="CanvasItem.QueueRedraw"/>。
/// </para>
/// </summary>
public sealed partial class DoorStateOverlay : Node2D
{
    /// <summary>盖在遮暗层（VisionMask=4000）之上——<b>夜里也要读得到门闩</b>；在路径线（4090）之下。</summary>
    public const int BadgeZIndex = 4080;

    /// <summary>一扇门此刻要画的东西：门中心（cartesian）+ 门体高度（徽标浮在门板顶上）+ 两个真实状态。</summary>
    public readonly record struct DoorBadge(Vector2 CenterCart, float Height, DoorState State, LockTier Lock);

    // ---- 尺寸（屏幕像素，「拟定待调」）----
    private const float PlateHalfW = 11f;   // 底牌半宽
    private const float PlateHalfH = 10f;   // 底牌半高
    private const float LiftPx = 10f;       // 徽标底边离门板顶多高

    // ---- 配色：只做增强，语义全在形状上 ----
    private static readonly Color PlateBg = new(0.08f, 0.08f, 0.10f, 0.82f);   // 暗底牌：亮地面/暗夜两种底色都读得出
    private static readonly Color PlateEdge = new(0.30f, 0.28f, 0.22f, 0.9f);  // 与 CampToast 边框同源
    private static readonly Color OpenTint = new(0.60f, 0.58f, 0.54f);          // 开着：灰（民居门常态开着，不该长期报警）
    private static readonly Color ClosedTint = new(0.84f, 0.72f, 0.44f);        // 只是关着：琥珀（对会拧门把手的敌人 = 没关）
    private static readonly Color BarredTint = new(0.62f, 0.82f, 0.55f);        // 闩着：绿（= CampToast.Ok）
    private static readonly Color LockedTint = new(0.55f, 0.70f, 0.88f);        // 锁着：蓝（要撬/要砸）

    private Func<IEnumerable<DoorBadge>>? _provider;
    private readonly List<DoorBadge> _badges = new();
    private int _lastKey;

    public override void _Ready()
    {
        ZIndex = BadgeZIndex;
        TopLevel = true; // 不吃父节点变换（与 VisionMask / PathOverlay 一致：自己就画在 iso 世界坐标上）
        ZAsRelative = false;
    }

    /// <summary>门态供给（CampMain 给：所有未被砸毁的门）。</summary>
    public void SetProvider(Func<IEnumerable<DoorBadge>> provider) => _provider = provider;

    public override void _Process(double delta)
    {
        if (_provider is null)
        {
            return;
        }

        _badges.Clear();
        int key = 17;
        foreach (DoorBadge b in _provider())
        {
            _badges.Add(b);
            // 脏检只看真会改变画面的东西：门在哪、什么态、什么锁。门是静止的，位置进哈希是白捡的保险。
            key = key * 31 + (int)b.State;
            key = key * 31 + (int)b.Lock;
            key = key * 31 + Mathf.RoundToInt(b.CenterCart.X) * 7 + Mathf.RoundToInt(b.CenterCart.Y);
        }

        if (key != _lastKey)
        {
            _lastKey = key;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        foreach (DoorBadge b in _badges)
        {
            Vector2 p = Iso.Project(b.CenterCart) - new Vector2(0f, b.Height + LiftPx + PlateHalfH);
            DrawBadge(p, b);
        }
    }

    private void DrawBadge(Vector2 c, DoorBadge b)
    {
        // 底牌：暗色小方牌 + 一圈描边。没有它，细线条会消失在亮地面或夜间遮暗里。
        var plate = new Rect2(c - new Vector2(PlateHalfW, PlateHalfH), new Vector2(PlateHalfW * 2, PlateHalfH * 2));
        DrawRect(plate, PlateBg, filled: true);
        DrawRect(plate, PlateEdge, filled: false, width: 1f);

        DoorGlyph glyph = DoorBadgeVisual.GlyphFor(b.State);
        Color tint = TintFor(b.State);

        switch (glyph)
        {
            case DoorGlyph.OpenFrame:
                DrawOpenFrame(c, tint);
                break;
            case DoorGlyph.Handle:
                DrawHandle(c, tint);
                break;
            case DoorGlyph.Bar:
                DrawBar(c, tint);
                break;
            case DoorGlyph.Padlock:
                DrawPadlock(c, tint, DoorBadgeVisual.LockNotches(b.Lock));
                break;
        }
    }

    private static Color TintFor(DoorState state) => state switch
    {
        DoorState.Open => OpenTint,
        DoorState.Closed => ClosedTint,
        DoorState.Barred => BarredTint,
        DoorState.Locked => LockedTint,
        _ => OpenTint,
    };

    /// <summary><b>开着</b>：一个空心门框——里头什么都没有。你能走过去，别的东西也能。</summary>
    private void DrawOpenFrame(Vector2 c, Color tint)
    {
        var frame = new Rect2(c - new Vector2(5f, 7f), new Vector2(10f, 14f));
        DrawRect(frame, tint, filled: false, width: 1.4f);
    }

    /// <summary><b>关着</b>：一块实心门板 + 一个门把手。<b>推一下就开</b>——包括劫掠者的手。</summary>
    private void DrawHandle(Vector2 c, Color tint)
    {
        var leaf = new Rect2(c - new Vector2(5f, 7f), new Vector2(10f, 14f));
        DrawRect(leaf, tint * new Color(1f, 1f, 1f, 0.45f), filled: true);
        DrawRect(leaf, tint, filled: false, width: 1.2f);
        DrawCircle(c + new Vector2(3f, 0f), 1.8f, tint); // 门把手：这一个圆点就是"推得开"的全部意思
    }

    /// <summary><b>闩着</b>：门板上横着一道粗闩，两端各一个卡座。外人推不开、撬不了——<b>只能砸</b>。</summary>
    private void DrawBar(Vector2 c, Color tint)
    {
        var leaf = new Rect2(c - new Vector2(5f, 7f), new Vector2(10f, 14f));
        DrawRect(leaf, tint * new Color(1f, 1f, 1f, 0.35f), filled: true);
        DrawRect(leaf, tint, filled: false, width: 1.2f);

        // 横闩本体：贯穿整块门板（比门板还宽一点——那根横木是架在两侧墙上的，这正是撬锁撬不到它的原因）。
        DrawRect(new Rect2(c - new Vector2(8f, 1.8f), new Vector2(16f, 3.6f)), tint, filled: true);
        // 两端卡座
        DrawRect(new Rect2(c + new Vector2(-8f, -3.5f), new Vector2(2.4f, 7f)), tint, filled: true);
        DrawRect(new Rect2(c + new Vector2(5.6f, -3.5f), new Vector2(2.4f, 7f)), tint, filled: true);
    }

    /// <summary><b>锁着</b>：一把挂锁。锁体上的竖刻痕数 = 锁的档次（简单 ‖ / 普通 ‖‖ / 坚固 ‖‖‖）——<b>撬它值不值</b>就看这个。</summary>
    private void DrawPadlock(Vector2 c, Color tint, int notches)
    {
        // U 形锁梁（半圆弧）
        DrawArc(c + new Vector2(0f, -2.5f), 3.6f, Mathf.Pi, Mathf.Tau, 10, tint, 1.6f);
        // 锁体
        var body = new Rect2(c + new Vector2(-5f, -2.5f), new Vector2(10f, 8.5f));
        DrawRect(body, tint, filled: true);
        // 刻痕：档次靠**形状**编码，不靠颜色（坚固锁 ‖‖‖ = 期望 32 秒 / 3 根铁丝，门外那群东西不会等你）
        for (int i = 0; i < notches; i++)
        {
            float x = c.X - 2.6f + i * 2.6f;
            DrawLine(new Vector2(x, c.Y - 0.8f), new Vector2(x, c.Y + 4.4f), PlateBg with { A = 1f }, 1f);
        }
    }
}
