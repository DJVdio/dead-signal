using Godot;

namespace DeadSignal.Godot;

/// <summary>俯视地面网格线，纯装饰，增强空间参照。</summary>
public sealed partial class GridLines : Node2D
{
    public Rect2 Bounds;
    public float Cell = 80f;

    public override void _Draw()
    {
        var color = new Color(1, 1, 1, 0.045f);
        for (float x = Bounds.Position.X; x <= Bounds.End.X; x += Cell)
        {
            DrawLine(new Vector2(x, Bounds.Position.Y), new Vector2(x, Bounds.End.Y), color, 1f);
        }
        for (float y = Bounds.Position.Y; y <= Bounds.End.Y; y += Cell)
        {
            DrawLine(new Vector2(Bounds.Position.X, y), new Vector2(Bounds.End.X, y), color, 1f);
        }
    }
}
