using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 命中后在世界坐标浮起的战报文字（伤害数 + 部位），上飘淡出后自毁。
/// 用 Label 直接画字，无需外部字体资源。
/// </summary>
public sealed partial class FloatingText : Node2D
{
    private double _age;
    private const double Lifetime = 1.1;
    private const float RiseSpeed = 34f;
    private Label _label = null!;

    public static FloatingText Spawn(Node parent, Vector2 worldPos, string text, Color color)
    {
        var ft = new FloatingText();
        parent.AddChild(ft);
        ft.GlobalPosition = worldPos;
        ft.Build(text, color);
        return ft;
    }

    private void Build(string text, Color color)
    {
        _label = new Label
        {
            Text = text,
            Modulate = color,
            ZIndex = 100,
        };
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _label.AddThemeConstantOverride("outline_size", 4);
        // 居中于生成点。
        _label.Position = new Vector2(-40, -10);
        _label.CustomMinimumSize = new Vector2(80, 0);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        _age += delta;
        Position += new Vector2(0, -RiseSpeed * (float)delta);
        float t = (float)(_age / Lifetime);
        Modulate = new Color(1, 1, 1, Mathf.Clamp(1f - t, 0f, 1f));
        if (_age >= Lifetime)
        {
            QueueFree();
        }
    }
}
