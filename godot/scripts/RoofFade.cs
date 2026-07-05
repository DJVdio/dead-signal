using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 屋顶淡出触发区：覆盖建筑室内的 Area2D，检测幸存者进入/离开，
/// 让绑定的屋顶 CanvasItem 在「不透明」与「90% 透明」之间平滑过渡。
///
/// 关键：淡入淡出用 <see cref="Time.GetTicksMsec"/> 算真实 delta（不吃 Engine.TimeScale），
/// 于是战术暂停下走进屋檐，屋顶也照常渐隐——与 CameraController 同一手法。
/// </summary>
public sealed partial class RoofFade : Area2D
{
    private const float OccupiedAlpha = 0.1f;   // 有人在屋檐下：90% 透明
    private const float ClearAlpha = 1.0f;      // 无人：完全不透明
    private const float FadeSpeed = 6.0f;       // 每秒 alpha 变化速率

    private CanvasItem _roof = null!;
    private int _inside;
    private ulong _lastTick;

    /// <summary>装配触发区：屋顶节点 + 室内矩形（局部坐标，Area2D 自身位于其中心）。</summary>
    public void Setup(CanvasItem roof, Rect2 interiorLocal)
    {
        _roof = roof;

        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0;
        CollisionMask = 0b0001; // 只探测幸存者（Actor 幸存者层 = 层 1）

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = interiorLocal.Size },
            Position = interiorLocal.Position + interiorLocal.Size / 2,
        };
        AddChild(shape);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    public override void _Ready() => _lastTick = Time.GetTicksMsec();

    private void OnBodyEntered(Node2D body)
    {
        if (body is Pawn)
        {
            _inside++;
        }
    }

    private void OnBodyExited(Node2D body)
    {
        if (body is Pawn && _inside > 0)
        {
            _inside--;
        }
    }

    public override void _Process(double _)
    {
        ulong now = Time.GetTicksMsec();
        float rdelta = (now - _lastTick) / 1000f;
        _lastTick = now;
        if (rdelta > 0.1f)
        {
            rdelta = 0.1f;
        }

        float target = _inside > 0 ? OccupiedAlpha : ClearAlpha;
        Color c = _roof.Modulate;
        c.A = Mathf.MoveToward(c.A, target, FadeSpeed * rdelta);
        _roof.Modulate = c;
    }
}
