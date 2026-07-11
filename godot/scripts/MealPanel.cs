using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 聚餐模态面板（黎明/黄昏）：全员聚集用餐 + 头顶气泡以对话框形式呈现。替代原信件 UI。
/// 强制暂停期间显示；玩家点"继续"关闭并推进相位。内容每次 <see cref="ShowMeal"/> 重建。
/// </summary>
public sealed partial class MealPanel : CanvasLayer
{
    private Control _root = null!;
    private Panel _panel = null!;
    private Label _title = null!;
    private RichTextLabel _summary = null!;
    private VBoxContainer _bubbleBox = null!;
    private CheckBox _secondsToggle = null!;

    public event Action? Continued;

    /// <summary>补餐开关切换（勾=启用补餐：余粮把掉档者喂回；取消=囤粮）。营地层据此改分粮策略，下一餐生效。</summary>
    public event Action<bool>? SecondServingToggled;

    public override void _Ready()
    {
        _panel = UiStyle.BuildModalShell(
            this, out _root, "MealPanel",
            overlayAlpha: 0.7f,
            panelSize: new Vector2(520, 420),
            borderColor: new Color(0.55f, 0.42f, 0.2f));

        _title = new Label();
        _title.Position = new Vector2(24, 18);
        _title.AddThemeFontSizeOverride("font_size", 22);
        _title.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        _panel.AddChild(_title);

        // RichTextLabel + BBCode：便于只给「食物不足」一段标红，而不牵动饥饿/补餐等中性行。
        _summary = new RichTextLabel();
        _summary.Position = new Vector2(24, 58);
        _summary.Size = new Vector2(472, 40);
        _summary.BbcodeEnabled = true;
        _summary.ScrollActive = false;
        _summary.FitContent = true;
        _summary.AddThemeFontSizeOverride("normal_font_size", 15);
        _summary.AddThemeColorOverride("default_color", new Color(0.85f, 0.82f, 0.75f));
        _summary.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        _panel.AddChild(_summary);

        var scroll = new ScrollContainer
        {
            Position = new Vector2(24, 100),
            Size = new Vector2(472, 250),
        };
        _panel.AddChild(scroll);

        _bubbleBox = new VBoxContainer();
        _bubbleBox.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _bubbleBox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_bubbleBox);

        var continueBtn = new Button
        {
            Text = "继续",
            Position = new Vector2(190, 362),
            Size = new Vector2(140, 44),
        };
        continueBtn.Pressed += () => Continued?.Invoke();
        UiStyle.StyleButton(continueBtn, new Color(0.75f, 0.55f, 0.2f),
            cornerRadius: 6, normalBg: new Color(0.12f, 0.12f, 0.14f), hoverAlpha: 0.25f, fontSize: 16);
        _panel.AddChild(continueBtn);

        // 补餐开关（最小可用）：勾选=物资充裕时用余粮把掉档者喂回（净 +1），取消=囤粮。下一餐生效。
        _secondsToggle = new CheckBox
        {
            Text = "补餐（余粮把掉档者喂回）",
            Position = new Vector2(24, 372),
            Size = new Vector2(160, 24),
            TooltipText = "启用后，保障全员第一餐仍有余粮时，自动给掉档者补一餐使饥饿回升一级。取消则囤积食物。下一餐生效。",
        };
        _secondsToggle.AddThemeFontSizeOverride("font_size", 13);
        _secondsToggle.Toggled += on => SecondServingToggled?.Invoke(on);
        _panel.AddChild(_secondsToggle);
    }

    /// <summary>反映当前营地补餐策略到开关（不触发 Toggled 事件）。营地层在弹面板前调用一次。</summary>
    public void SetSecondServingPolicy(bool enabled) => _secondsToggle.SetPressedNoSignal(enabled);

    /// <summary>填充并显示：标题 + 本餐食物结算 + 饥饿加深名单 + 补餐回升名单 + 气泡对话。</summary>
    public void ShowMeal(string title, MealOutcome outcome, IReadOnlyList<MealBubble> bubbles,
        IReadOnlyList<string>? hungerNotes = null, IReadOnlyList<string>? secondNotes = null)
    {
        _title.Text = title;

        // 食物不足是本餐最要紧的负面信息，单列一行并标红醒目（BBCode 只染这一段）。取公共语义色 Danger。
        string shortNote = outcome.Missing > 0
            ? $"\n[color=#{UiStyle.Danger.ToHtml(false)}]食物不足 {outcome.Missing} 份，{outcome.Missing} 人没吃上饭[/color]"
            : "";
        string hungerNote = hungerNotes is { Count: > 0 }
            ? $"\n饥饿加深：{string.Join("、", hungerNotes)}"
            : "";
        string secondNote = secondNotes is { Count: > 0 }
            ? $"\n补餐回升：{string.Join("、", secondNotes)}"
            : "";
        _summary.Text =
            $"用餐 {outcome.Diners} 人，消耗食物 {outcome.Served} 份，剩余 {outcome.FoodRemaining}。{shortNote}{hungerNote}{secondNote}";

        UiStyle.ClearChildren(_bubbleBox);
        if (bubbles.Count == 0)
        {
            _bubbleBox.AddChild(MakeBubbleLabel(null, "（席间一片沉默。）"));
        }
        else
        {
            foreach (var b in bubbles)
                _bubbleBox.AddChild(MakeBubbleLabel(b.speaker, b.text));
        }

        Visible = true;
    }

    private static Label MakeBubbleLabel(string? speaker, string text)
    {
        var l = new Label();
        l.Text = string.IsNullOrEmpty(speaker) ? $"— {text}" : $"{speaker}：{text}";
        l.AddThemeFontSizeOverride("font_size", 15);
        l.AddThemeColorOverride("font_color",
            string.IsNullOrEmpty(speaker)
                ? new Color(0.7f, 0.7f, 0.68f)   // 旁白偏灰
                : new Color(0.92f, 0.9f, 0.82f));
        l.AutowrapMode = TextServer.AutowrapMode.Arbitrary;
        l.CustomMinimumSize = new Vector2(460, 0);
        return l;
    }
}
