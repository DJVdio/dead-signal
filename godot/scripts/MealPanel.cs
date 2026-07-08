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
    private Label _summary = null!;
    private VBoxContainer _bubbleBox = null!;

    public event Action? Continued;

    public override void _Ready()
    {
        _panel = UiStyle.BuildModalShell(
            this, out _root, "MealPanel",
            overlayAlpha: 0.7f,
            panelSize: new Vector2(520, 420),
            borderColor: new Color(0.55f, 0.42f, 0.2f));

        _title = new Label();
        _title.Position = new Vector2(24, 18);
        _title.AddThemeFontSizeOverride("font_size", 24);
        _title.AddThemeColorOverride("font_color", new Color(0.95f, 0.8f, 0.45f));
        _panel.AddChild(_title);

        _summary = new Label();
        _summary.Position = new Vector2(24, 58);
        _summary.Size = new Vector2(472, 40);
        _summary.AddThemeFontSizeOverride("font_size", 15);
        _summary.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.75f));
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
    }

    /// <summary>填充并显示：标题 + 本餐食物结算 + 饥饿加深名单 + 气泡对话。</summary>
    public void ShowMeal(string title, MealOutcome outcome, IReadOnlyList<MealBubble> bubbles,
        IReadOnlyList<string>? hungerNotes = null)
    {
        _title.Text = title;

        string shortNote = outcome.Missing > 0
            ? $"　食物不足 {outcome.Missing} 份，{outcome.Missing} 人没吃上饭"
            : "";
        string hungerNote = hungerNotes is { Count: > 0 }
            ? $"\n饥饿加深：{string.Join("、", hungerNotes)}"
            : "";
        _summary.Text =
            $"用餐 {outcome.Diners} 人，消耗食物 {outcome.Served} 份，剩余 {outcome.FoodRemaining}。{shortNote}{hungerNote}";

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
