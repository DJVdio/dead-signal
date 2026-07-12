using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 三结局 **CG 载体**（全屏黑底 + 分段文本逐段渐显 + 按键/点击继续 + 终局收束）。
/// 吃一份分段 CG 文本（<see cref="EndingCg"/> / <see cref="SouthTrial.EscapeCg"/>）：每段一屏，淡入后等玩家按键推进下一段；
/// 末段后浮出「重新开始 / 退出游戏」（同 <see cref="GameOverPanel"/> 语义——CG 播完即终局）。
/// 弹出即 <c>Engine.TimeScale = 0</c> 暂停整局；重开前复位回 1（否则新场景冻住）。
/// 骨架照 <see cref="GameOverPanel"/>（CanvasLayer + 全屏 ColorRect），层号高于其他 HUD，独占画面。
/// </summary>
public sealed partial class EndingPanel : CanvasLayer
{
    private const float FadeInSeconds = 1.2f; // 每段淡入时长
    private const float HintDelaySeconds = 0.6f; // 淡入完再等一下才浮"继续"提示，避免手快跳字

    private IReadOnlyList<string> _segments = Array.Empty<string>();
    private string _title = string.Empty;
    private int _index = -1;
    private float _elapsed;
    private bool _finished; // 末段已过、已浮出收束按钮

    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;
    private Label _hintLabel = null!;
    private Control _endButtons = null!;

    public override void _Ready()
    {
        Layer = 200; // 高于所有 HUD/模态，CG 独占画面

        var overlay = new ColorRect();
        overlay.Color = new Color(0.02f, 0.02f, 0.03f); // 近纯黑
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop; // 吃掉一切点击，独占
        AddChild(overlay);

        _titleLabel = new Label();
        _titleLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _titleLabel.AnchorLeft = 0.5f; _titleLabel.AnchorRight = 0.5f;
        _titleLabel.OffsetTop = 90; _titleLabel.OffsetLeft = -400; _titleLabel.OffsetRight = 400;
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _titleLabel.AddThemeFontSizeOverride("font_size", 24);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.62f, 0.5f));
        _titleLabel.Visible = false;
        overlay.AddChild(_titleLabel);

        _bodyLabel = new Label();
        // 屏幕中央一块居中文本区（相对锚点，随分辨率自适应）。
        _bodyLabel.AnchorLeft = 0.5f; _bodyLabel.AnchorRight = 0.5f;
        _bodyLabel.AnchorTop = 0.5f; _bodyLabel.AnchorBottom = 0.5f;
        _bodyLabel.OffsetLeft = -420; _bodyLabel.OffsetRight = 420;
        _bodyLabel.OffsetTop = -120; _bodyLabel.OffsetBottom = 160;
        _bodyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bodyLabel.VerticalAlignment = VerticalAlignment.Center;
        _bodyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bodyLabel.AddThemeFontSizeOverride("font_size", 20);
        _bodyLabel.AddThemeColorOverride("font_color", new Color(0.86f, 0.83f, 0.77f));
        _bodyLabel.Modulate = new Color(1, 1, 1, 0);
        overlay.AddChild(_bodyLabel);

        _hintLabel = new Label();
        _hintLabel.AnchorLeft = 0.5f; _hintLabel.AnchorRight = 0.5f;
        _hintLabel.AnchorTop = 1f; _hintLabel.AnchorBottom = 1f;
        _hintLabel.OffsetLeft = -200; _hintLabel.OffsetRight = 200;
        _hintLabel.OffsetTop = -70; _hintLabel.OffsetBottom = -40;
        _hintLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _hintLabel.Text = "按任意键继续 ▸";
        _hintLabel.AddThemeFontSizeOverride("font_size", 13);
        _hintLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.48f, 0.44f));
        _hintLabel.Visible = false;
        overlay.AddChild(_hintLabel);

        // 末段后浮出的收束按钮组（重新开始 / 退出）。
        _endButtons = new Control();
        _endButtons.AnchorLeft = 0.5f; _endButtons.AnchorRight = 0.5f;
        _endButtons.AnchorTop = 1f; _endButtons.AnchorBottom = 1f;
        _endButtons.OffsetLeft = -190; _endButtons.OffsetRight = 190;
        _endButtons.OffsetTop = -100; _endButtons.OffsetBottom = -48;
        _endButtons.Visible = false;
        overlay.AddChild(_endButtons);

        var restartBtn = new Button();
        restartBtn.Text = "重新开始";
        restartBtn.Position = new Vector2(0, 0);
        restartBtn.Size = new Vector2(180, 44);
        restartBtn.Pressed += Restart;
        UiStyle.StyleButton(restartBtn, new Color(0.25f, 0.45f, 0.3f), fontSize: 16);
        _endButtons.AddChild(restartBtn);

        var quitBtn = new Button();
        quitBtn.Text = "退出游戏";
        quitBtn.Position = new Vector2(200, 0);
        quitBtn.Size = new Vector2(180, 44);
        quitBtn.Pressed += () => GetTree().Quit();
        UiStyle.StyleButton(quitBtn, new Color(0.55f, 0.22f, 0.22f), fontSize: 16);
        _endButtons.AddChild(quitBtn);

        Advance(); // 进入首段
    }

    /// <summary>推进到下一段（淡入复位）；已在末段则浮出收束按钮。</summary>
    private void Advance()
    {
        _index++;
        if (_index >= _segments.Count)
        {
            ShowEnd();
            return;
        }
        if (_index == 0 && !string.IsNullOrEmpty(_title))
        {
            _titleLabel.Text = _title;
            _titleLabel.Visible = true;
        }
        _bodyLabel.Text = _segments.Count > 0 ? _segments[_index] : string.Empty;
        _bodyLabel.Modulate = new Color(1, 1, 1, 0);
        _hintLabel.Visible = false;
        _elapsed = 0f;
    }

    /// <summary>末段结束：隐去正文/提示，浮出「重新开始 / 退出」。</summary>
    private void ShowEnd()
    {
        _finished = true;
        _bodyLabel.Visible = false;
        _titleLabel.Visible = false;
        _hintLabel.Visible = false;
        _endButtons.Visible = true;
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        // 真实时钟推进淡入（TimeScale=0 下 delta 仍为 0？——Godot 的 _Process delta 受 TimeScale 影响，
        // 故用未缩放时间：这里以 Process 帧的墙钟增量近似。TimeScale=0 时 delta≈0，改用固定步进兜底。）
        float dt = (float)delta;
        if (dt <= 0f) dt = 1f / 60f; // TimeScale=0 冻结下 delta 为 0，用固定步进保证淡入照走
        _elapsed += dt;
        float a = Mathf.Clamp(_elapsed / FadeInSeconds, 0f, 1f);
        _bodyLabel.Modulate = new Color(1, 1, 1, a);
        if (_elapsed >= FadeInSeconds + HintDelaySeconds && !_hintLabel.Visible)
            _hintLabel.Visible = true;
    }

    public override void _Input(InputEvent @event)
    {
        if (_finished) return;
        bool advanceKey =
            (@event is InputEventKey { Pressed: true, Echo: false }) ||
            (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left });
        if (!advanceKey) return;
        GetViewport().SetInputAsHandled();

        // 淡入未完：先补满这段（快进），不直接跳段——避免误吞未读文本。
        if (_elapsed < FadeInSeconds)
        {
            _elapsed = FadeInSeconds + HintDelaySeconds;
            _bodyLabel.Modulate = new Color(1, 1, 1, 1);
            _hintLabel.Visible = true;
            return;
        }
        Advance();
    }

    private void Restart()
    {
        Engine.TimeScale = 1;
        CombatFeed.Reset();
        GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// 弹出并暂停：挂到指定 HUD 层，播放一份分段 CG（<paramref name="segments"/>），播完浮出收束按钮。
    /// 弹出即 <c>Engine.TimeScale = 0</c>（不设恢复——CG 播完即终局，唯一出口是重开/退出）。
    /// </summary>
    public static void Show(CanvasLayer host, IReadOnlyList<string> segments, string title = "")
    {
        Engine.TimeScale = 0;
        var panel = new EndingPanel { _segments = segments ?? Array.Empty<string>(), _title = title ?? string.Empty };
        host.AddChild(panel);
    }
}
