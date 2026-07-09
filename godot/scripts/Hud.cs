using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 屏幕叠加层：左上角昼夜/天数/速度档信息，底部操控提示，以及跟随鼠标的悬停辨识小标签。
/// </summary>
public sealed partial class Hud : CanvasLayer
{
    private Label _statusLabel = null!;
    private Label _helpLabel = null!;
    private Label _hoverLabel = null!;   // 跟随鼠标的容器辨识提示（工作台/储物柜/搜刮物）

    public override void _Ready()
    {
        _statusLabel = MakeLabel();
        _statusLabel.Position = new Vector2(16, 12);
        _statusLabel.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_statusLabel);

        _helpLabel = MakeLabel();
        _helpLabel.Position = new Vector2(16, 44);
        _helpLabel.AddThemeFontSizeOverride("font_size", 13);
        _helpLabel.Modulate = new Color(1, 1, 1, 0.75f);
        _helpLabel.Text =
            "左键选中角色  右键：空地移动 / 对准工作台·柜子前往交互  空格暂停  1/2/3 速度档  WASD/边缘平移相机  滚轮缩放";
        AddChild(_helpLabel);

        _hoverLabel = MakeLabel();
        _hoverLabel.AddThemeFontSizeOverride("font_size", 13);
        _hoverLabel.ZIndex = 100;
        _hoverLabel.Visible = false;
        AddChild(_hoverLabel);
    }

    private static Label MakeLabel()
    {
        var l = new Label();
        l.AddThemeColorOverride("font_color", new Color(0.95f, 0.97f, 1f));
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("outline_size", 5);
        return l;
    }

    public void SetStatus(string text) => _statusLabel.Text = text;

    /// <summary>显示（或更新）跟随鼠标的悬停辨识标签；<paramref name="screenPos"/> 为屏幕坐标（视口鼠标位）。</summary>
    public void ShowHoverLabel(string text, Vector2 screenPos)
    {
        _hoverLabel.Text = text;
        _hoverLabel.Position = screenPos + new Vector2(16, 16);
        _hoverLabel.Visible = true;
    }

    /// <summary>隐藏悬停辨识标签（未命中容器 / 面板打开 / 探索中）。</summary>
    public void HideHoverLabel() => _hoverLabel.Visible = false;
}
