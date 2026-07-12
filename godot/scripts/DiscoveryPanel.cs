using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 探索发现环境叙事面板（模态）：探索队踏入发现点时弹出，显示一段环境叙事（标题 + 滚动正文）。
/// 与 <see cref="ReaderPanel"/> 同骨架（CanvasLayer + <see cref="UiStyle.BuildModalShell"/>），
/// 区别在语义——这是"当场看到了什么"的一次性叙事，关闭按钮为「继续」，不进库存/阅读链路。
/// 关联的日记以 <see cref="Item.Book"/> 入库，回营再经 StashPanel→ReaderPanel 细读。
///
/// 支持**多屏分段连播**（叙事调查点 [SPEC-B12] 极乐迪斯科式，一段 2~4 屏）：<see cref="ShowPaged"/> 传多屏文本，
/// 「继续」在非末屏推进到下一屏、末屏才触发 <see cref="Continued"/> 关闭。单屏 <see cref="Show"/> 即一屏的 ShowPaged。
/// </summary>
public sealed partial class DiscoveryPanel : CanvasLayer
{
    private Control _root = null!;
    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;
    private ScrollContainer _scroll = null!;
    private Button _continueBtn = null!;
    private Label _pageIndicator = null!;

    // 多屏连播态：当前展示的分屏文本 + 屏索引（单屏叙事即一元素）。
    private IReadOnlyList<string> _pages = System.Array.Empty<string>();
    private int _pageIndex;

    /// <summary>点「继续」（多屏时为末屏点击）：CampMain 据此隐藏本面板、放探索队继续行动。</summary>
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

        // 分屏指示（左下角，如「1 / 3」；单屏时空）。
        _pageIndicator = new Label();
        _pageIndicator.Position = new Vector2(28, 404);
        _pageIndicator.Size = new Vector2(120, 28);
        _pageIndicator.AddThemeFontSizeOverride("font_size", 13);
        _pageIndicator.AddThemeColorOverride("font_color", new Color(0.6f, 0.56f, 0.48f));
        panel.AddChild(_pageIndicator);

        _continueBtn = new Button();
        _continueBtn.Text = "继续";
        _continueBtn.Position = new Vector2(436, 396);
        _continueBtn.Size = new Vector2(136, 40);
        _continueBtn.Pressed += OnContinuePressed;
        UiStyle.StyleButton(_continueBtn, new Color(0.4f, 0.35f, 0.28f), fontSize: 16);
        panel.AddChild(_continueBtn);
    }

    /// <summary>展示一段单屏环境叙事（标题 + 正文；滚动回到顶部）。既有单屏调用方入口。</summary>
    public void Show(string title, string body) => ShowPaged(title, new[] { body });

    /// <summary>展示一段**多屏**环境叙事（标题 + 分屏文本连播）：从首屏起，「继续」逐屏推进，末屏关闭。</summary>
    public void ShowPaged(string title, IReadOnlyList<string> pages)
    {
        _titleLabel.Text = title;
        _pages = pages is { Count: > 0 } ? pages : new[] { "" };
        _pageIndex = 0;
        RenderPage();
    }

    /// <summary>渲染当前屏：正文 + 复位滚动 + 按是否末屏切按钮文案/分屏指示。</summary>
    private void RenderPage()
    {
        _bodyLabel.Text = _pages[_pageIndex];
        _scroll.ScrollVertical = 0; // 每屏复位到顶，避免连看时串位
        bool last = _pageIndex >= _pages.Count - 1;
        _continueBtn.Text = last ? "继续" : "继续 ▸"; // 非末屏用 ▸ 提示"后面还有"
        _pageIndicator.Text = _pages.Count > 1 ? $"{_pageIndex + 1} / {_pages.Count}" : "";
    }

    /// <summary>点「继续」：非末屏推进到下一屏，末屏触发 <see cref="Continued"/>（CampMain 关面板+恢复时标）。</summary>
    private void OnContinuePressed()
    {
        if (_pageIndex < _pages.Count - 1)
        {
            _pageIndex++;
            RenderPage();
        }
        else
        {
            Continued?.Invoke();
        }
    }
}
