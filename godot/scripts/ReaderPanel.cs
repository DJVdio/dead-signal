using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 书籍阅读面板（模态滚动正文）：显示 <see cref="BookData"/> 的标题 + 正文。已读标记由 CampMain 在弹出前置位
/// （<see cref="BookData.MarkRead"/>），本面板只负责呈现。叠在库存面板之上（更高 CanvasLayer 层），关闭回到库存。
/// 骨架照 <see cref="ChoicePanel"/>：CanvasLayer + <see cref="UiStyle.BuildModalShell"/>。
/// </summary>
public sealed partial class ReaderPanel : CanvasLayer
{
    private Control _root = null!;
    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;

    /// <summary>点「合上书」：CampMain 据此隐藏本面板（时标仍由库存面板持有，不在此恢复）。</summary>
    public event Action? Closed;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "ReaderPanel",
            overlayAlpha: 0.55f,
            panelSize: new Vector2(600, 520),
            borderColor: new Color(0.3f, 0.27f, 0.2f));

        // 根 Control 吃掉背景点击（避免点漏到营地世界）。
        _root.MouseFilter = Control.MouseFilterEnum.Stop;

        _titleLabel = new Label();
        _titleLabel.Position = new Vector2(28, 20);
        _titleLabel.Size = new Vector2(544, 34);
        _titleLabel.AddThemeFontSizeOverride("font_size", 22);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(_titleLabel);

        var bodyBg = new Panel();
        bodyBg.Position = new Vector2(28, 64);
        bodyBg.Size = new Vector2(544, 380);
        var bodyStyle = new StyleBoxFlat();
        bodyStyle.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.8f);
        bodyStyle.CornerRadiusTopLeft = 4;
        bodyStyle.CornerRadiusTopRight = 4;
        bodyStyle.CornerRadiusBottomLeft = 4;
        bodyStyle.CornerRadiusBottomRight = 4;
        bodyBg.AddThemeStyleboxOverride("panel", bodyStyle);
        panel.AddChild(bodyBg);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.AddThemeConstantOverride("margin_left", 12);
        scroll.AddThemeConstantOverride("margin_top", 12);
        scroll.AddThemeConstantOverride("margin_right", 12);
        scroll.AddThemeConstantOverride("margin_bottom", 12);
        scroll.MouseFilter = Control.MouseFilterEnum.Pass;
        bodyBg.AddChild(scroll);

        _bodyLabel = new Label();
        _bodyLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _bodyLabel.CustomMinimumSize = new Vector2(508, 0);
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.AddThemeFontSizeOverride("font_size", 15);
        _bodyLabel.AddThemeColorOverride("font_color", new Color(0.82f, 0.8f, 0.74f));
        scroll.AddChild(_bodyLabel);

        var closeBtn = new Button();
        closeBtn.Text = "合上书";
        closeBtn.Position = new Vector2(436, 456);
        closeBtn.Size = new Vector2(136, 40);
        closeBtn.Pressed += () => Closed?.Invoke();
        UiStyle.StyleButton(closeBtn, new Color(0.5f, 0.4f, 0.3f), fontSize: 16);
        panel.AddChild(closeBtn);
    }

    /// <summary>展示一本书的标题与正文（滚动回到顶部）。</summary>
    public void ShowBook(string title, string body)
    {
        _titleLabel.Text = title;
        _bodyLabel.Text = body;
    }
}
