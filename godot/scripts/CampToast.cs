using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 营地顶部一行瞬时提示（制作/改装/搜刮反馈）。独立 <see cref="CanvasLayer"/>，Layer 叠在各模态之上。
/// **手动显隐**——制作面板开着时时标冻结（<c>Engine.TimeScale = 0</c>），SceneTreeTimer 不走，故不做自动淡出：
/// 显示后一直挂到下一次 <see cref="Show"/> 覆盖或 <see cref="Hide"/>（面板关闭时清）。
/// </summary>
public sealed partial class CampToast : CanvasLayer
{
    public static readonly Color Ok = new(0.62f, 0.82f, 0.55f);
    public static readonly Color Bad = new(0.86f, 0.5f, 0.42f);

    private PanelContainer _box = null!;
    private Label _label = null!;

    public override void _Ready()
    {
        // 顶部一条全宽窄带，CenterContainer 把提示盒在带内居中（横向居中、贴近顶部）。不吃鼠标，纯提示。
        var center = new CenterContainer();
        center.AnchorLeft = 0f;
        center.AnchorRight = 1f;
        center.AnchorTop = 0f;
        center.AnchorBottom = 0f;
        center.OffsetLeft = 0f;
        center.OffsetRight = 0f;
        center.OffsetTop = 16f;
        center.OffsetBottom = 96f;
        center.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(center);

        _box = new PanelContainer();
        _box.MouseFilter = Control.MouseFilterEnum.Ignore;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.92f),
            BorderColor = new Color(0.3f, 0.28f, 0.22f),
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
        };
        _box.AddThemeStyleboxOverride("panel", style);
        center.AddChild(_box);

        _label = new Label();
        _label.MouseFilter = Control.MouseFilterEnum.Ignore;
        _label.AddThemeFontSizeOverride("font_size", 15);
        _box.AddChild(_label);

        Visible = false;
    }

    /// <summary>显示一行提示（<paramref name="color"/> 建议用 <see cref="Ok"/>/<see cref="Bad"/>）。收起用继承的 <see cref="CanvasLayer.Hide"/>。</summary>
    public void Show(string text, Color color)
    {
        _label.Text = text;
        _label.AddThemeColorOverride("font_color", color);
        Visible = true;
    }
}
