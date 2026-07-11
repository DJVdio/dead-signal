using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 聚餐吃饭动画期间在角色头顶悬停的世界内聊天气泡（替代旧 MealPanel 里的文本行）：
/// 一个带边框的小面板 + 台词文字，在生成点悬停数秒后淡出自毁。挂到 <b>iso 可视层</b>
/// （<c>_isoLayer</c>），坐标已由营地层用 <c>Iso.Project(pawn.GlobalPosition)</c> 投影 + 头顶偏移算好。
/// 坐着/站着是否冒泡由 <see cref="MealBubbleDelivery"/> 掷点（站着 −50%），此节点只负责"冒出来的那条"的呈现。
/// </summary>
public sealed partial class MealSpeechBubble : Node2D
{
    private double _age;
    private const double Lifetime = 3.4;     // 悬停总时长（拟定待调）
    private const double FadeInTime = 0.18;  // 淡入
    private const double FadeOutTime = 0.6;  // 末段淡出

    /// <summary>在 <paramref name="parent"/>（应为 iso 可视层）的 <paramref name="isoPos"/> 处生成一条头顶气泡。</summary>
    public static MealSpeechBubble Spawn(Node parent, Vector2 isoPos, string? speaker, string text, Color accent)
    {
        var b = new MealSpeechBubble { Position = isoPos, ZIndex = 200, ZAsRelative = false };
        parent.AddChild(b);
        b.Build(speaker, text, accent);
        return b;
    }

    private void Build(string? speaker, string text, Color accent)
    {
        string shown = string.IsNullOrEmpty(speaker) ? text : $"{speaker}：{text}";

        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.08f, 0.1f, 0.92f),
            BorderColor = accent,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 2,   // 右下缺角，像话尾指向说话人
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
        };
        box.AddThemeStyleboxOverride("panel", style);

        var label = new Label
        {
            Text = shown,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 0),
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.AddThemeColorOverride("font_color", new Color(0.94f, 0.92f, 0.84f));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 3);
        // 限制气泡最大宽度，长句自动换行。
        label.CustomMinimumSize = new Vector2(0, 0);
        box.CustomMinimumSize = new Vector2(0, 0);
        box.AddChild(label);

        // 让气泡横向以生成点为中心（宽度自适应内容，最多 ~200px）。
        box.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        box.Position = new Vector2(-100, -18);
        box.CustomMinimumSize = new Vector2(0, 0);
        var wrap = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        wrap.AddChild(box);
        box.Size = new Vector2(200, 0);
        label.CustomMinimumSize = new Vector2(180, 0);
        AddChild(wrap);
    }

    public override void _Process(double delta)
    {
        _age += delta;
        // 轻微上浮（前段）+ 末段淡出。
        Position += new Vector2(0, -6f * (float)delta);
        float alpha = 1f;
        if (_age < FadeInTime)
        {
            alpha = (float)(_age / FadeInTime);
        }
        else if (_age > Lifetime - FadeOutTime)
        {
            alpha = (float)((Lifetime - _age) / FadeOutTime);
        }
        Modulate = new Color(1, 1, 1, Mathf.Clamp(alpha, 0f, 1f));
        if (_age >= Lifetime)
        {
            QueueFree();
        }
    }
}
