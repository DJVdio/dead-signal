using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 一名 <see cref="Actor"/> 的等距视觉：程序化（纯矢量、无素材）精致人形。
/// 挂在 iso_layer（YSortEnabled）下，每帧把 actor 的 cartesian 逻辑位置投到 iso 屏幕坐标
/// （<c>position = Iso.Project(actor.GlobalPosition)</c>，脚点=原点=YSort 深度锚），并读 actor 暴露的
/// 数据自绘。物理/寻路仍在 Actor（cartesian top-down），此处只表现——伪等距解耦。
///
/// 人形分层画：投影阴影 → 选中环 → 双腿 → 躯干（明暗）→ 双臂 → 肩线 → 头（含朝向标）→ 持械指示 → 血条。
/// 朝向为**自由角度**（用户拍板：矢量绘制、无离散帧）：把 actor 的 cartesian 面朝方向经
/// <c>Iso.Project</c> 转到 iso 屏幕空间取角，指数平滑旋转当前绘制角，肩线/头标/持械手随之转。
/// </summary>
public sealed partial class ActorSprite : Node2D
{
    private Actor _actor = null!;
    private bool _bound;
    private float _drawAngle;          // 当前绘制朝向（iso 屏幕弧度），平滑逼近目标
    private bool _angleInit;

    private const float TurnRate = 16f; // 朝向平滑速率（越大越跟手）

    /// <summary>绑定所表现的 Actor，并把绘制朝向初始化到其当前朝向（避免出生瞬间甩头）。</summary>
    public void Bind(Actor actor)
    {
        _actor = actor;
        _bound = true;
        ZIndex = 0;
        SyncToActor();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!_bound)
        {
            return;
        }
        // Actor 死亡即 QueueFree 自身，或将被回收——同步销毁视觉，避免悬挂。
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.Alive)
        {
            QueueFree();
            return;
        }

        SyncToActor();

        // 目标朝向：cartesian 面朝向量经 iso 投影（线性、原点→原点）后取屏幕角，再指数平滑。
        Vector2 fScreen = Iso.Project(Vector2.FromAngle(_actor.FacingAngle));
        if (fScreen != Vector2.Zero)
        {
            float target = fScreen.Angle();
            if (!_angleInit)
            {
                _drawAngle = target;
                _angleInit = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-TurnRate * (float)delta); // 帧率无关
                _drawAngle = Mathf.LerpAngle(_drawAngle, target, t);
            }
        }

        QueueRedraw();

        if (_actor is Pawn pawn)
        {
            Modulate = pawn.Role == PawnRole.Sleeping
                ? new Color(1, 1, 1, 0.35f)
                : Colors.White;
        }
    }

    private void SyncToActor() => Position = Iso.Project(_actor.GlobalPosition);

    public override void _Draw()
    {
        if (!_bound || !GodotObject.IsInstanceValid(_actor))
        {
            return;
        }

        float r = _actor.Radius;
        Vector2 f = Vector2.FromAngle(_drawAngle);
        var p = new Vector2(-f.Y, f.X);

        Color tint = _actor.BodyTint;

        float headCY = -r * 2.78f;
        float headR = r * 0.62f;

        DrawColoredPolygon(Ellipse(new Vector2(0, -r * 0.12f), r * 1.2f, r * 0.55f), new Color(0, 0, 0, 0.28f));

        if (_actor.Selected)
        {
            Vector2[] ring = Ellipse(new Vector2(0, -r * 0.12f), r * 1.45f, r * 0.66f, 28);
            DrawPolyline(Close(ring), new Color(0.4f, 1f, 0.55f), 2f, true);
        }

        if (_actor is Pawn { SleepingVisual: true })
        {
            DrawSleeping(r, tint);
        }
        else
        {
            DrawStanding(r, tint, f, p);
        }

        float barW = r * 2.4f;
        float barH = 4f;
        var barPos = new Vector2(-barW / 2f, headCY - headR - 8f);
        float frac = _actor.HealthFraction;
        DrawRect(new Rect2(barPos, new Vector2(barW, barH)), new Color(0.1f, 0.1f, 0.1f, 0.85f));
        Color hp = frac > 0.5f ? new Color(0.4f, 0.85f, 0.4f)
            : frac > 0.25f ? new Color(0.9f, 0.8f, 0.3f)
            : new Color(0.9f, 0.35f, 0.3f);
        DrawRect(new Rect2(barPos, new Vector2(barW * frac, barH)), hp);

        if (_actor is Pawn rp && rp.Role != PawnRole.Idle)
        {
            Color dot = rp.Role switch
            {
                PawnRole.Sleeping => new Color(0.3f, 0.5f, 0.9f, 0.7f),
                PawnRole.Guard => new Color(0.9f, 0.7f, 0.2f, 0.8f),
                PawnRole.Expedition => new Color(0.3f, 0.8f, 0.4f, 0.8f),
                _ => Colors.Transparent,
            };
            DrawCircle(new Vector2(r * 1.5f, headCY - headR - 4f), 3f, dot);
        }
    }

    private void DrawStanding(float r, Color tint, Vector2 f, Vector2 p)
    {
        Color torso = tint;
        Color torsoDark = tint.Darkened(0.42f);
        Color torsoLight = tint.Lightened(0.28f);
        Color legCol = tint.Darkened(0.58f);
        Color headCol = tint.Lightened(0.40f);
        Color headShade = headCol.Darkened(0.35f);
        var outline = new Color(0.05f, 0.05f, 0.07f, 0.9f);

        float feetY = 0f;
        float hipY = -r * 1.15f;
        float torsoCY = -r * 1.72f;
        float shoulderY = -r * 2.25f;
        float headCY = -r * 2.78f;
        float headR = r * 0.62f;

        Vector2 hipL = new Vector2(0, hipY) + p * r * 0.28f;
        Vector2 hipR = new Vector2(0, hipY) - p * r * 0.28f;
        Vector2 footL = p * r * 0.42f + f * r * 0.10f + new Vector2(0, feetY);
        Vector2 footR = -p * r * 0.42f - f * r * 0.10f + new Vector2(0, feetY);
        DrawLine(hipL, footL, outline, r * 0.62f);
        DrawLine(hipR, footR, outline, r * 0.62f);
        DrawLine(hipL, footL, legCol, r * 0.42f);
        DrawLine(hipR, footR, legCol, r * 0.42f);

        Vector2 torsoC = new Vector2(0, torsoCY);
        DrawColoredPolygon(Ellipse(torsoC, r * 0.78f + 1.2f, r * 1.05f + 1.2f), outline);
        DrawColoredPolygon(Ellipse(torsoC, r * 0.78f, r * 1.05f), torso);
        DrawColoredPolygon(Ellipse(torsoC + new Vector2(0, r * 0.35f), r * 0.6f, r * 0.7f), torsoDark);
        DrawColoredPolygon(Ellipse(torsoC + f * r * 0.18f - new Vector2(0, r * 0.32f), r * 0.42f, r * 0.5f), torsoLight);

        Vector2 shoulderC = new Vector2(0, shoulderY);
        Vector2 shoulderL = shoulderC + p * r * 0.72f;
        Vector2 shoulderR = shoulderC - p * r * 0.72f;
        Vector2 handOff = new Vector2(0, r * 0.95f);
        Vector2 handL = shoulderL + handOff + f * r * 0.18f;
        Vector2 handR = shoulderR + new Vector2(0, r * 0.78f) + f * r * 0.62f;
        DrawLine(shoulderL, handL, torsoDark, r * 0.34f);
        DrawLine(shoulderR, handR, torsoDark, r * 0.34f);

        DrawLine(shoulderL, shoulderR, torsoDark, r * 0.30f);

        DrawColoredPolygon(Ellipse(new Vector2(0, headCY), headR + 1.2f, headR + 1.2f), outline);
        DrawColoredPolygon(Ellipse(new Vector2(0, headCY), headR, headR), headCol);
        DrawColoredPolygon(Ellipse(new Vector2(0, headCY) + f * headR * 0.5f, headR * 0.42f, headR * 0.42f), headShade);

        if (_actor.RangedArmed)
        {
            var gun = new Color(0.2f, 0.22f, 0.26f);
            DrawLine(handR, handR + f * r * 1.35f, gun, r * 0.26f);
        }
        else
        {
            var blade = new Color(0.72f, 0.75f, 0.82f);
            DrawLine(handR, handR + f * r * 0.82f, blade, r * 0.16f);
        }
    }

    private void DrawSleeping(float r, Color tint)
    {
        Color headCol = tint.Lightened(0.40f);
        var outline = new Color(0.05f, 0.05f, 0.07f, 0.9f);

        float bodyLen = r * 2.2f;
        float bodyH = r * 0.65f;
        Vector2 bodyC = new Vector2(0, -r * 1.1f);
        DrawColoredPolygon(Ellipse(bodyC, bodyLen / 2, bodyH), outline);
        DrawColoredPolygon(Ellipse(bodyC, bodyLen / 2 - 1, bodyH - 1), tint);

        Vector2 headC = bodyC + new Vector2(bodyLen / 2 + r * 0.25f, -r * 0.08f);
        float hr = r * 0.48f;
        DrawColoredPolygon(Ellipse(headC, hr + 1, hr + 1), outline);
        DrawColoredPolygon(Ellipse(headC, hr, hr), headCol);
    }

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry, int seg = 22)
    {
        var pts = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = Mathf.Tau * i / seg;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return pts;
    }

    /// <summary>把多边形点集闭合（首点补到尾）用于 DrawPolyline 画成环。</summary>
    private static Vector2[] Close(Vector2[] pts)
    {
        var closed = new Vector2[pts.Length + 1];
        pts.CopyTo(closed, 0);
        closed[pts.Length] = pts[0];
        return closed;
    }
}
