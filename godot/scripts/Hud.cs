using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 屏幕叠加层：左上角昼夜/天数/速度档信息，右上角尸潮南下倒计时/到期警示，
/// 底部操控提示，以及跟随鼠标的悬停辨识小标签。
/// </summary>
public sealed partial class Hud : CanvasLayer
{
    private Label _statusLabel = null!;
    private Label _helpLabel = null!;
    private Label _hoverLabel = null!;   // 跟随鼠标的容器辨识提示（工作台/储物柜/搜刮物）
    private Label _hordeLabel = null!;   // 右上角尸潮倒计时/到期警示（未发现时全隐，Hidden 零痕迹）


    // 拟定：剩余 ≤ 此天数转 Warning 黄警示（数值待 Sim/用户调）。
    private const int HordeUrgentDays = 7;

    private bool _hordeArrived;          // Arrived 态：驱动红字呼吸动画
    private double _hordePulseT;         // 呼吸相位累计（缩放时标下，暂停即冻结，合期望）

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
            "左键选中角色（底部卡牌栏亦可，双击聚焦）  右键：空地移动 / 对准工作台·柜子前往开面板交互  " +
            "M 医疗面板  空格暂停  ESC 关面板  1/2/3 速度档  WASD/边缘平移相机  滚轮缩放";
        AddChild(_helpLabel);

        _hoverLabel = MakeLabel();
        _hoverLabel.AddThemeFontSizeOverride("font_size", 13);
        _hoverLabel.ZIndex = 100;
        _hoverLabel.Visible = false;
        AddChild(_hoverLabel);

        // 右上角尸潮倒计时：锚右上、右对齐、向左生长（不与左上状态行争位）。默认隐（Hidden 态零痕迹）。
        _hordeLabel = MakeLabel();
        _hordeLabel.AddThemeFontSizeOverride("font_size", 18);
        _hordeLabel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _hordeLabel.GrowHorizontal = Control.GrowDirection.Begin;
        _hordeLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _hordeLabel.OffsetTop = 12;
        _hordeLabel.OffsetRight = -16;
        _hordeLabel.Visible = false;
        AddChild(_hordeLabel);
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

    /// <summary>
    /// 更新右上角尸潮倒计时/警示（三态；调用方由 HordeTimeline.HordePhase 映射来，Hud 不依赖纯逻辑层）：
    /// <list type="bullet">
    /// <item><c>!sighted</c>（Hidden）：完全隐藏，零痕迹——玩家未上瞭望台知情前不剧透时限。</item>
    /// <item><c>sighted &amp;&amp; !arrived</c>（Sighted）：常驻「尸潮南下」倒计时；剩余 ≤ <see cref="HordeUrgentDays"/> 天转 Warning 黄。</item>
    /// <item><c>arrived</c>（Arrived）：转 Danger 红警示「尸潮已至」，红字缓慢呼吸（克制，不闪烁）。</item>
    /// </list>
    /// 文案为草稿（供用户改）。<paramref name="daysRemaining"/> 负值按 0 显示。
    /// </summary>
    public void SetHordeCountdown(bool sighted, bool arrived, int daysRemaining)
    {
        if (!sighted)
        {
            _hordeLabel.Visible = false;
            _hordeArrived = false;
            return;
        }

        _hordeLabel.Visible = true;

        if (arrived)
        {
            if (!_hordeArrived)
            {
                _hordeArrived = true;
                _hordePulseT = 0;
            }
            _hordeLabel.Text = "尸潮已至";  // 草稿文案
            _hordeLabel.AddThemeColorOverride("font_color", UiStyle.Danger);
            return;
        }

        // 由 Arrived 回退到 Sighted（一般不会，稳健起见复位呼吸 alpha）。
        _hordeArrived = false;
        _hordeLabel.Modulate = new Color(1, 1, 1, 1);

        int days = daysRemaining < 0 ? 0 : daysRemaining;
        _hordeLabel.Text = $"尸潮南下 · 距抵达约 {days} 天";  // 草稿文案
        Color col = days <= HordeUrgentDays ? UiStyle.Warning : new Color(0.95f, 0.97f, 1f);
        _hordeLabel.AddThemeColorOverride("font_color", col);
    }

    public override void _Process(double delta)
    {
        if (!_hordeArrived)
            return;
        // Arrived 红字缓慢呼吸：alpha 0.5↔1.0，周期 ~2s。仅此态启用，其余帧直接 return 零开销。
        _hordePulseT += delta;
        float a = 0.75f + 0.25f * Mathf.Sin((float)_hordePulseT * 3.0f);
        _hordeLabel.Modulate = new Color(1, 1, 1, a);
    }

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
