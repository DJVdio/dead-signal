using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 俯视相机：WASD / 屏幕边缘平移 + 滚轮缩放。
///
/// 关键：平移用 <see cref="Time.GetTicksMsec"/> 算真实 delta，而非 _Process 的已缩放 delta——
/// 否则暂停（Engine.TimeScale=0）时相机也会被冻住，无法在战术暂停下四处查看战场。
/// </summary>
public sealed partial class CameraController : Camera2D
{
    private const float PanSpeed = 520f;
    private const float EdgeMargin = 24f;
    private const float ZoomStep = 0.1f;
    private const float ZoomMin = 0.45f;
    private const float ZoomMax = 2.2f;
    private const float DragSensitivity = 1f;

    // 聚焦滑动：指数逼近速率（越大越快）与到达阈值（像素，settle 后停止）。
    private const float FocusLerpSpeed = 9f;
    private const float FocusArriveThreshold = 1.5f;

    private ulong _lastTick;
    private Rect2 _bounds;
    private bool _isDragging;
    private Vector2 _dragStartMouse;

    /// <summary>正在平滑滑向的目标（iso 屏幕坐标，已 clamp 进边界）；无聚焦为 null。玩家任意平移即清除。</summary>
    private Vector2? _focusTarget;

    public void SetBounds(Rect2 bounds) => _bounds = bounds;

    /// <summary>
    /// 平滑滑动聚焦到某 iso 屏幕坐标（如双击卡牌居中该幸存者）。非瞬移：在 <see cref="_Process"/> 里
    /// 逐帧指数逼近，过程中及结束都 <see cref="ClampToBounds"/>。玩家一操作 WASD/边缘/拖拽平移即取消本次滑动。
    /// 目标先按边界 clamp，保证越界目标也能 settle 到最近可达点。
    /// </summary>
    public void FocusOn(Vector2 targetIsoPos) => _focusTarget = ClampPoint(targetIsoPos);

    public override void _Ready() => _lastTick = Time.GetTicksMsec();

    public override void _Process(double _)
    {
        ulong now = Time.GetTicksMsec();
        float rdelta = (now - _lastTick) / 1000f;
        _lastTick = now;
        if (rdelta > 0.1f)
        {
            rdelta = 0.1f;
        }

        Vector2 move = Vector2.Zero;

        if (Input.IsPhysicalKeyPressed(Key.W)) move.Y -= 1;
        if (Input.IsPhysicalKeyPressed(Key.S)) move.Y += 1;
        if (Input.IsPhysicalKeyPressed(Key.A)) move.X -= 1;
        if (Input.IsPhysicalKeyPressed(Key.D)) move.X += 1;

        // 屏幕边缘平移。
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        if (mouse.X < EdgeMargin) move.X -= 1;
        else if (mouse.X > vp.X - EdgeMargin) move.X += 1;
        if (mouse.Y < EdgeMargin) move.Y -= 1;
        else if (mouse.Y > vp.Y - EdgeMargin) move.Y += 1;

        if (move != Vector2.Zero)
        {
            // 玩家主动平移优先：取消正在进行的聚焦滑动，避免两者抢位。
            _focusTarget = null;
            // 缩放越远（Zoom 越小）平移越快，观感一致。
            Position += move.Normalized() * PanSpeed * rdelta / Zoom.X;
            ClampToBounds();
        }
        else if (_focusTarget is { } target)
        {
            // 帧率无关的指数逼近：settle 到阈值内即吸附并停止。
            float t = 1f - Mathf.Exp(-FocusLerpSpeed * rdelta);
            Position = Position.Lerp(target, t);
            if (Position.DistanceTo(target) <= FocusArriveThreshold)
            {
                Position = target;
                _focusTarget = null;
            }
            ClampToBounds();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Middle)
            {
                _isDragging = mb.Pressed;
                _dragStartMouse = mb.Position;
                return;
            }

            if (!mb.Pressed) return;

            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                SetZoom(Zoom.X + ZoomStep);
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoom(Zoom.X - ZoomStep);
            }
        }
        else if (@event is InputEventMouseMotion mm && _isDragging)
        {
            Vector2 delta = mm.Position - _dragStartMouse;
            if (delta != Vector2.Zero)
            {
                _focusTarget = null; // 拖拽平移同样取消聚焦滑动
                Position -= delta * DragSensitivity / Zoom.X;
                ClampToBounds();
                _dragStartMouse = mm.Position;
            }
        }
    }

    private void SetZoom(float z)
    {
        float clamped = Mathf.Clamp(z, ZoomMin, ZoomMax);
        Zoom = new Vector2(clamped, clamped);
    }

    private void ClampToBounds() => Position = ClampPoint(Position);

    /// <summary>把一点按当前相机边界 clamp（边界未设时原样返回）。<see cref="ClampToBounds"/> 与聚焦目标共用。</summary>
    private Vector2 ClampPoint(Vector2 p)
    {
        if (_bounds.Size == Vector2.Zero)
        {
            return p;
        }
        return new Vector2(
            Mathf.Clamp(p.X, _bounds.Position.X, _bounds.End.X),
            Mathf.Clamp(p.Y, _bounds.Position.Y, _bounds.End.Y));
    }
}
