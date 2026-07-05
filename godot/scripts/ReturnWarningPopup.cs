using System;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class ReturnWarningPopup : CanvasLayer
{
    private Control _root = null!;
    private Timer _delayTimer = null!;
    private Label _countdownLabel = null!;
    private int _retryCount;

    public event Action? ReturnNow;
    public event Action? ReturnDelayed;

    public override void _Ready()
    {
        _root = new Control { Name = "ReturnWarningPopup" };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Pass;
        AddChild(_root);

        var overlay = new ColorRect();
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.Color = new Color(0, 0, 0, 0.75f);
        overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(overlay);

        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = new Vector2(420, 280);
        panel.Position = new Vector2(-210, -140);
        panel.MouseFilter = Control.MouseFilterEnum.Pass;
        _root.AddChild(panel);

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        bg.BorderColor = new Color(0.35f, 0.25f, 0.15f);
        bg.BorderWidthLeft = 2;
        bg.BorderWidthRight = 2;
        bg.BorderWidthTop = 2;
        bg.BorderWidthBottom = 2;
        bg.CornerRadiusTopLeft = 8;
        bg.CornerRadiusTopRight = 8;
        bg.CornerRadiusBottomLeft = 8;
        bg.CornerRadiusBottomRight = 8;
        panel.AddThemeStyleboxOverride("panel", bg);

        var title = new Label();
        title.Text = "行动时间宝贵";
        title.Position = new Vector2(24, 20);
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.7f, 0.4f));
        panel.AddChild(title);

        var body = new Label();
        body.Text = "搜索时间即将结束，准备回营。";
        body.Position = new Vector2(24, 60);
        body.Size = new Vector2(372, 50);
        body.AddThemeFontSizeOverride("font_size", 15);
        body.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
        body.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        panel.AddChild(body);

        _countdownLabel = new Label();
        _countdownLabel.Position = new Vector2(24, 110);
        _countdownLabel.Size = new Vector2(372, 30);
        _countdownLabel.AddThemeFontSizeOverride("font_size", 14);
        _countdownLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.6f, 0.2f));
        panel.AddChild(_countdownLabel);

        var returnBtn = new Button();
        returnBtn.Text = "立即回营";
        returnBtn.Position = new Vector2(40, 170);
        returnBtn.Size = new Vector2(150, 44);
        returnBtn.Pressed += () =>
        {
            ReturnNow?.Invoke();
        };
        StyleButton(returnBtn, new Color(0.3f, 0.7f, 0.3f));
        panel.AddChild(returnBtn);

        var delayBtn = new Button();
        delayBtn.Text = "再搜刮一会儿";
        delayBtn.Position = new Vector2(230, 170);
        delayBtn.Size = new Vector2(160, 44);
        delayBtn.Pressed += () =>
        {
            _retryCount++;
            if (_retryCount >= 3)
            {
                ReturnNow?.Invoke();
                return;
            }
            ReturnDelayed?.Invoke();
            double wait = _retryCount == 1 ? 30.0 : 20.0;
            _delayTimer.WaitTime = wait;
            _delayTimer.Start();
            Visible = false;
        };
        StyleButton(delayBtn, new Color(0.75f, 0.45f, 0.15f));
        panel.AddChild(delayBtn);

        _delayTimer = new Timer { OneShot = true, WaitTime = 30.0, Autostart = false };
        _delayTimer.Timeout += OnDelayTimeout;
        AddChild(_delayTimer);
    }

    private void OnDelayTimeout()
    {
        if (!Visible && IsInsideTree())
            Visible = true;
    }

    public void ResetWarning()
    {
        _retryCount = 0;
        _delayTimer.Stop();
    }

    public void SetRemainingTime(double seconds)
    {
        if (_countdownLabel == null)
            return;
        int min = (int)(seconds / 60);
        int sec = (int)(seconds % 60);
        _countdownLabel.Text = $"剩余安全时间: {min:00}:{sec:00}";
    }

    public void CancelDelay()
    {
        if (_delayTimer != null && !_delayTimer.IsStopped())
            _delayTimer.Stop();
    }

    private static void StyleButton(Button btn, Color accent)
    {
        btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.9f));
        btn.AddThemeFontSizeOverride("font_size", 14);
        var normal = new StyleBoxFlat();
        normal.BgColor = new Color(0.12f, 0.12f, 0.14f);
        normal.BorderColor = accent;
        normal.BorderWidthLeft = 1;
        normal.BorderWidthRight = 1;
        normal.BorderWidthTop = 1;
        normal.BorderWidthBottom = 1;
        normal.CornerRadiusTopLeft = 6;
        normal.CornerRadiusTopRight = 6;
        normal.CornerRadiusBottomLeft = 6;
        normal.CornerRadiusBottomRight = 6;
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = accent * new Color(1, 1, 1, 0.25f);
        hover.BorderColor = accent;
        hover.BorderWidthLeft = 1;
        hover.BorderWidthRight = 1;
        hover.BorderWidthTop = 1;
        hover.BorderWidthBottom = 1;
        hover.CornerRadiusTopLeft = 6;
        hover.CornerRadiusTopRight = 6;
        hover.CornerRadiusBottomLeft = 6;
        hover.CornerRadiusBottomRight = 6;
        btn.AddThemeStyleboxOverride("hover", hover);
    }
}
