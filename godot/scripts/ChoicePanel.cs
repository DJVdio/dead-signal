using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 教学关"对幸存者的处置"三选一。数值以 int 承载在 <see cref="ChoiceOption.Value"/>，
/// 调用方 cast 回本枚举即可（<c>(CristineChoice)value</c>）。
/// </summary>
public enum CristineChoice
{
    Recruit = 0, // 收留
    Exile = 1,   // 放逐
    Execute = 2, // 处决
}

/// <summary>
/// 通用"提示文本 + N 个选项按钮"模态抉择面板。点某个按钮即 emit 该选项的
/// <see cref="ChoiceOption.Value"/>（订阅 <see cref="Confirmed"/>），无需二次确认。
/// 选项文本/值/配色全部由调用方 <see cref="Setup"/> 传入，可复用于任何"N 选一"场景；
/// 教学关的收留/放逐/处决用 <see cref="CristineChoice"/> 装配即可（见 <see cref="ForCristine"/>）。
/// </summary>
public sealed partial class ChoicePanel : CanvasLayer
{
    public struct ChoiceOption
    {
        public int Value;          // 通常是某枚举强转的 int
        public string Label;       // 按钮主文本
        public string Description; // 可空：按钮下方小字说明
        public Color Accent;       // 按钮描边/hover 主色
    }

    private Control _root = null!;
    private Label _promptLabel = null!;
    private VBoxContainer _optionContainer = null!;
    private Button? _cancelBtn;

    /// <summary>点击某选项按钮后 emit 其 Value（教学关即 (int)CristineChoice）。</summary>
    public event Action<int>? Confirmed;

    /// <summary>点击取消（若 Setup 时 showCancel=true）时触发。</summary>
    public event Action? Cancelled;

    public override void _Ready()
    {
        var panel = UiStyle.BuildModalShell(
            this, out _root, "ChoicePanel",
            overlayAlpha: 0.65f,
            panelSize: new Vector2(480, 420),
            borderColor: new Color(0.25f, 0.22f, 0.18f));

        _promptLabel = new Label();
        _promptLabel.Position = new Vector2(28, 24);
        _promptLabel.Size = new Vector2(424, 120);
        _promptLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _promptLabel.AddThemeFontSizeOverride("font_size", 17);
        _promptLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        panel.AddChild(_promptLabel);

        _optionContainer = new VBoxContainer();
        _optionContainer.Position = new Vector2(28, 152);
        _optionContainer.Size = new Vector2(424, 240);
        _optionContainer.AddThemeConstantOverride("separation", 12);
        _optionContainer.MouseFilter = Control.MouseFilterEnum.Pass;
        panel.AddChild(_optionContainer);
    }

    /// <summary>
    /// 配置提示文本与选项按钮。可重复调用刷新内容。
    /// </summary>
    /// <param name="prompt">顶部提示语。</param>
    /// <param name="options">选项列表，按传入顺序竖排。</param>
    /// <param name="showCancel">是否在末尾追加一个"取消"按钮（触发 <see cref="Cancelled"/>）。</param>
    public void Setup(string prompt, IReadOnlyList<ChoiceOption> options, bool showCancel = false)
    {
        _promptLabel.Text = prompt;
        UiStyle.ClearChildren(_optionContainer);
        _cancelBtn = null;

        foreach (var opt in options)
        {
            var value = opt.Value;

            var btn = new Button();
            btn.Text = string.IsNullOrEmpty(opt.Description)
                ? opt.Label
                : $"{opt.Label}\n{opt.Description}";
            btn.CustomMinimumSize = new Vector2(0, string.IsNullOrEmpty(opt.Description) ? 44 : 56);
            btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            btn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            btn.Pressed += () => Confirmed?.Invoke(value);
            UiStyle.StyleButton(btn, opt.Accent, fontSize: 16);
            _optionContainer.AddChild(btn);
        }

        if (showCancel)
        {
            _cancelBtn = new Button();
            _cancelBtn.Text = "取消";
            _cancelBtn.CustomMinimumSize = new Vector2(0, 36);
            _cancelBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _cancelBtn.Pressed += () => Cancelled?.Invoke();
            UiStyle.StyleButton(_cancelBtn, new Color(0.45f, 0.42f, 0.4f), fontSize: 14);
            _optionContainer.AddChild(_cancelBtn);
        }
    }

    /// <summary>
    /// 教学关便捷装配：收留/放逐/处决三选一。选项 Value 即 (int)CristineChoice，
    /// 订阅方 <c>Confirmed += v =&gt; Handle((CristineChoice)v)</c>。
    /// </summary>
    public void ForCristine(string prompt)
    {
        Setup(prompt, new List<ChoiceOption>
        {
            new() { Value = (int)CristineChoice.Recruit, Label = "收留",
                    Description = "接纳她加入营地，多一张嘴也多一双手",
                    Accent = new Color(0.3f, 0.6f, 0.3f) },
            new() { Value = (int)CristineChoice.Exile, Label = "放逐",
                    Description = "把她赶走，眼不见为净",
                    Accent = new Color(0.6f, 0.5f, 0.25f) },
            new() { Value = (int)CristineChoice.Execute, Label = "处决",
                    Description = "一了百了，永绝后患",
                    Accent = new Color(0.65f, 0.2f, 0.2f) },
        });
    }
}
