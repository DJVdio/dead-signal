using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 屏幕叠加层：左上角昼夜/天数/速度档信息，底部操控提示，以及框选时的选择矩形。
/// 选择矩形画在全屏 Control 上（屏幕坐标，不受相机变换影响）。
/// </summary>
public sealed partial class Hud : CanvasLayer
{
    private Label _statusLabel = null!;
    private Label _helpLabel = null!;
    private SelectionOverlay _overlay = null!;

    public override void _Ready()
    {
        _overlay = new SelectionOverlay();
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_overlay);

        _statusLabel = MakeLabel();
        _statusLabel.Position = new Vector2(16, 12);
        _statusLabel.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_statusLabel);

        _helpLabel = MakeLabel();
        _helpLabel.Position = new Vector2(16, 44);
        _helpLabel.AddThemeFontSizeOverride("font_size", 13);
        _helpLabel.Modulate = new Color(1, 1, 1, 0.75f);
        _helpLabel.Text =
            "左键选中/拖拽框选  右键移动或攻击  空格暂停  1/2/3 速度档  WASD/边缘平移相机  滚轮缩放  T 快进昼夜";
        AddChild(_helpLabel);
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

    public void ShowSelectionRect(Rect2 rect) => _overlay.SetRect(rect);

    public void HideSelectionRect() => _overlay.SetRect(null);

    /// <summary>全屏透明 Control，只负责画框选矩形。</summary>
    private sealed partial class SelectionOverlay : Control
    {
        private Rect2? _rect;

        public void SetRect(Rect2? rect)
        {
            _rect = rect;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_rect is not { } r)
            {
                return;
            }
            DrawRect(r, new Color(0.4f, 1f, 0.5f, 0.15f), true);
            DrawRect(r, new Color(0.4f, 1f, 0.5f, 0.9f), false, 1.5f);
        }
    }
}
