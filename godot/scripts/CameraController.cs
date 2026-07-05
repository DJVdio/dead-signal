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

    private ulong _lastTick;
    private Rect2 _bounds;
    private bool _isDragging;
    private Vector2 _dragStartMouse;

    public void SetBounds(Rect2 bounds) => _bounds = bounds;

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
            // 缩放越远（Zoom 越小）平移越快，观感一致。
            Position += move.Normalized() * PanSpeed * rdelta / Zoom.X;
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

    private void ClampToBounds()
    {
        if (_bounds.Size == Vector2.Zero)
        {
            return;
        }
        Position = new Vector2(
            Mathf.Clamp(Position.X, _bounds.Position.X, _bounds.End.X),
            Mathf.Clamp(Position.Y, _bounds.Position.Y, _bounds.End.Y));
    }
}
