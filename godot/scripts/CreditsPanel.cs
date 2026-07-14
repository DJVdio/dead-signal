using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// **素材出处面板**（F1）。骨架照 <see cref="SavePanel"/>：CanvasLayer + <see cref="UiStyle.BuildModalShell"/>，
/// 视觉语言与其余面板一致。内容是只读的，来自 <see cref="CreditsContent"/>（那才是文本的单一事实源）。
///
/// <para>
/// <b>为什么这一页非有不可</b>：物品图标全部来自 game-icons.net，授权是 <b>CC BY 3.0</b>——
/// 可以随便用、随便改，<b>但必须署名，而且署名要让使用者看得见</b>。只在仓库里放一份 CREDITS.md
/// 是给开发者看的，玩家看不到；所以署名得在游戏里能翻出来。这一页就是那个"能翻出来"。
/// </para>
/// <para>
/// <b>不冻结时标</b>：与库存/医疗那些模态不同，这页只是"看一眼出处"，没有任何决策，
/// 没理由把世界按停。玩家开着它，营地照常运转。
/// </para>
/// </summary>
public sealed partial class CreditsPanel : CanvasLayer
{
    private Control _root = null!;

    /// <summary>面板关闭（CampMain 据此清理自己的开关态；本面板不动时标）。</summary>
    public event Action? Closed;

    /// <summary>面板当前是否可见（CampMain 的 ESC 集中派发据此判断该不该关它）。</summary>
    public bool IsOpen => _root.Visible;

    public override void _Ready()
    {
        Layer = 12;
        Panel panel = UiStyle.BuildModalShell(
            this, out _root, "CreditsPanel",
            overlayAlpha: 0.55f,
            panelSize: new Vector2(660, 540),
            borderColor: new Color(0.28f, 0.24f, 0.18f));

        var box = new VBoxContainer
        {
            Position = new Vector2(20, 18),
            CustomMinimumSize = new Vector2(620, 504),
        };
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var title = new Label { Text = CreditsContent.Title };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        box.AddChild(title);

        var subtitle = new Label { Text = CreditsContent.Subtitle };
        subtitle.AddThemeFontSizeOverride("font_size", 12);
        subtitle.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.58f));
        box.AddChild(subtitle);

        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(616, 410) };
        box.AddChild(scroll);

        var list = new VBoxContainer { CustomMinimumSize = new Vector2(600, 0) };
        list.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(list);

        foreach (CreditsSection section in CreditsContent.Sections)
        {
            list.AddChild(BuildSection(section));
        }

        var close = new Button { Text = "关闭", CustomMinimumSize = new Vector2(100, 34) };
        UiStyle.StyleButton(close, new Color(0.5f, 0.4f, 0.3f));
        close.Pressed += Close;
        box.AddChild(close);

        _root.Visible = false;
    }

    private static Control BuildSection(CreditsSection section)
    {
        var card = new VBoxContainer { CustomMinimumSize = new Vector2(596, 0) };
        card.AddThemeConstantOverride("separation", 2);

        var head = new Label { Text = section.Title };
        head.AddThemeFontSizeOverride("font_size", 15);
        head.AddThemeColorOverride("font_color", new Color(0.72f, 0.68f, 0.55f));
        card.AddChild(head);

        // 授权那一行单独用醒目色：它是这一页存在的理由，不该混在正文里被略过。
        var license = new Label { Text = section.License };
        license.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        license.CustomMinimumSize = new Vector2(596, 0);
        license.AddThemeFontSizeOverride("font_size", 13);
        license.AddThemeColorOverride("font_color", UiStyle.Success);
        card.AddChild(license);

        foreach (string line in section.Lines)
        {
            var label = new Label { Text = line };
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(596, 0);
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_color", new Color(0.78f, 0.76f, 0.7f));
            card.AddChild(label);
        }

        return card;
    }

    /// <summary>打开面板（只读，不冻结时标）。</summary>
    public void Open() => _root.Visible = true;

    /// <summary>关闭面板并通知外部。</summary>
    public void Close()
    {
        _root.Visible = false;
        Closed?.Invoke();
    }

    /// <summary>F1 开关：开着就关，关着就开。</summary>
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }
}
