using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>放置预览</b>：家具落位前跟着鼠标走的那个"幽灵"footprint —— <b>绿 = 放得下，红 = 放不下</b>。
///
/// <para>
/// <b>为什么它是必需的、而不是锦上添花</b>：「不许贴着大门和围栏」（<see cref="PlacementRules"/>）是一条
/// <b>看不见的</b>规则 —— 那条 64px 的禁建带在地上没有任何痕迹。没有预览，玩家只能<b>盲点</b>：
/// 点一下、吃一句拒绝、再挪一点、再吃一句。规则会从"防呆"变成"折磨"。预览把这条隐形的线画出来。
/// </para>
///
/// <para>
/// <b>它自己跟鼠标、自己重画</b>（<see cref="_Process"/> 里反投影鼠标 → 问一次判定 → 变了才重绘）：
/// 这样接它的人<b>一行 <c>_Process</c> 都不用改</b>，只要 <see cref="Begin"/> / <see cref="End"/> 两下。
/// 判定不在这里 —— 能不能放由 <see cref="PlacementRules"/> 说了算（空间执行归 Godot 层、判定归纯逻辑，项目既有分工）。
/// </para>
///
/// <para>ZIndex 4085：压在夜间遮暗层（4000）之上 —— <b>夜里也看得见自己在往哪儿放东西</b>。</para>
/// </summary>
public sealed partial class PlacementGhost : Node2D
{
    private PlaceableSpec _spec;
    private Func<Vector2, bool>? _canPlaceAt;
    private Rect2 _cart;
    private bool _ok;
    private bool _drawn;

    public PlacementGhost()
    {
        ZIndex = 4085;
        Visible = false;
        SetProcess(false);
    }

    /// <summary>
    /// 进入放置模式：预览开始跟着鼠标走。
    /// <paramref name="canPlaceAt"/> = 「这个落点放不放得下」（消费层转发给 <see cref="PlacementRules.CanPlace"/>）。
    /// </summary>
    public void Begin(in PlaceableSpec spec, Func<Vector2, bool> canPlaceAt)
    {
        _spec = spec;
        _canPlaceAt = canPlaceAt ?? throw new ArgumentNullException(nameof(canPlaceAt));
        _drawn = false;
        Visible = true;
        SetProcess(true);
    }

    /// <summary>退出放置模式（放下了 / 右键作罢）：收起预览。</summary>
    public void End()
    {
        _canPlaceAt = null;
        _drawn = false;
        Visible = false;
        SetProcess(false);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_canPlaceAt is null)
        {
            return;
        }

        Vector2 cart = Iso.Unproject(GetGlobalMousePosition());
        var size = new Vector2(_spec.Width, _spec.Height);
        var rect = new Rect2(cart - size / 2f, size);
        bool ok = _canPlaceAt(cart);

        // 鼠标没动、判定没变 ⇒ 不重绘（放置模式下每帧都在问判定，别再每帧重画一遍多边形）。
        if (_drawn && rect == _cart && ok == _ok)
        {
            return;
        }

        _cart = rect;
        _ok = ok;
        _drawn = true;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_drawn)
        {
            return;
        }

        Vector2 p = _cart.Position, e = _cart.End;
        var quad = new[]
        {
            Iso.Project(p),
            Iso.Project(new Vector2(e.X, p.Y)),
            Iso.Project(e),
            Iso.Project(new Vector2(p.X, e.Y)),
        };

        // 红/绿之外还差着描边粗细与一个叉 —— **不只靠颜色区分**（同门态徽标那条口径：色盲玩家也得读得出来）。
        Color fill = _ok ? new Color(0.35f, 0.85f, 0.40f, 0.35f) : new Color(0.90f, 0.22f, 0.18f, 0.42f);
        Color edge = _ok ? new Color(0.55f, 1.00f, 0.60f, 0.90f) : new Color(1.00f, 0.35f, 0.28f, 1.00f);

        DrawColoredPolygon(quad, fill);
        DrawPolyline(new[] { quad[0], quad[1], quad[2], quad[3], quad[0] }, edge, _ok ? 2f : 3f);

        if (!_ok)
        {
            DrawLine(quad[0], quad[2], edge, 2f);
            DrawLine(quad[1], quad[3], edge, 2f);
        }
    }
}
