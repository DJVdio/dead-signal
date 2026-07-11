using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 探索发现环境叙事面板（模态）：探索队踏入发现点时弹出，显示一段环境叙事（标题 + 滚动正文）。
/// 与 <see cref="ReaderPanel"/> 同骨架（CanvasLayer + <see cref="UiStyle.BuildModalShell"/>），
/// 区别在语义——这是"当场看到了什么"的一次性叙事，关闭按钮为「继续」，不进库存/阅读链路。
/// 关联的日记以 <see cref="Item.Book"/> 入库，回营再经 StashPanel→ReaderPanel 细读。
/// </summary>
public sealed partial class DiscoveryPanel : CanvasLayer
{
    private Control _root = null!;
    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;
    private ScrollContainer _scroll = null!;

    /// <summary>点「继续」：CampMain 据此隐藏本面板、放探索队继续行动。</summary>
    public event Action? Continued;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "DiscoveryPanel",
            overlayAlpha: 0.6f,
            panelSize: new Vector2(600, 460),
            borderColor: new Color(0.32f, 0.24f, 0.2f));

        // 根 Control 吃掉背景点击（避免点漏到探索世界）。
        _root.MouseFilter = Control.MouseFilterEnum.Stop;

        _titleLabel = new Label();
        _titleLabel.Position = new Vector2(28, 20);
        _titleLabel.Size = new Vector2(544, 34);
        _titleLabel.AddThemeFontSizeOverride("font_size", 22);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.82f, 0.68f));
        panel.AddChild(_titleLabel);

        var bodyBg = new Panel();
        bodyBg.Position = new Vector2(28, 64);
        bodyBg.Size = new Vector2(544, 320);
        var bodyStyle = new StyleBoxFlat();
        bodyStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.8f);
        bodyStyle.CornerRadiusTopLeft = 4;
        bodyStyle.CornerRadiusTopRight = 4;
        bodyStyle.CornerRadiusBottomLeft = 4;
        bodyStyle.CornerRadiusBottomRight = 4;
        bodyBg.AddThemeStyleboxOverride("panel", bodyStyle);
        panel.AddChild(bodyBg);

        _scroll = new ScrollContainer();
        _scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _scroll.AddThemeConstantOverride("margin_left", 12);
        _scroll.AddThemeConstantOverride("margin_top", 12);
        _scroll.AddThemeConstantOverride("margin_right", 12);
        _scroll.AddThemeConstantOverride("margin_bottom", 12);
        _scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        bodyBg.AddChild(_scroll);

        _bodyLabel = new Label();
        _bodyLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bodyLabel.CustomMinimumSize = new Vector2(508, 0);
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.AddThemeFontSizeOverride("font_size", 15);
        _bodyLabel.AddThemeColorOverride("font_color", new Color(0.82f, 0.8f, 0.74f));
        _scroll.AddChild(_bodyLabel);

        var continueBtn = new Button();
        continueBtn.Text = "继续";
        continueBtn.Position = new Vector2(436, 396);
        continueBtn.Size = new Vector2(136, 40);
        continueBtn.Pressed += () => Continued?.Invoke();
        UiStyle.StyleButton(continueBtn, new Color(0.4f, 0.35f, 0.28f), fontSize: 16);
        panel.AddChild(continueBtn);
    }

    /// <summary>展示一段环境叙事（标题 + 正文；滚动回到顶部）。</summary>
    public void Show(string title, string body)
    {
        _titleLabel.Text = title;
        _bodyLabel.Text = body;
        _scroll.ScrollVertical = 0; // 每次展示复位到顶，避免连看两段叙事时串位
    }
}
