using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 共享 UI 样式/构建 helper。四个模态面板复用，避免复制粘贴与样式漂移。
/// </summary>
public static class UiStyle
{
    /// <summary>
    /// 移除并释放节点全部子节点。Node 非 RefCounted，只 RemoveChild 会造成原生对象泄漏，
    /// 必须 QueueFree。先 RemoveChild 立即脱离父节点（避免重建时的瞬时布局重复），再 QueueFree 释放。
    /// </summary>
    public static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// 构建模态外壳：全屏遮罩 + 居中带边框的 Panel。返回内容 Panel（调用方继续往里加标题等），
    /// 并通过 <paramref name="root"/> 输出承载一切的根 Control。
    /// </summary>
    public static Panel BuildModalShell(
        CanvasLayer layer,
        out Control root,
        string name,
        float overlayAlpha,
        Vector2 panelSize,
        Color borderColor)
    {
        root = new Control { Name = name };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Pass;
        layer.AddChild(root);

        var overlay = new ColorRect();
        overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        overlay.Color = new Color(0, 0, 0, overlayAlpha);
        overlay.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(overlay);

        var panel = new Panel();
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        panel.Size = panelSize;
        panel.Position = -panelSize / 2f;
        panel.MouseFilter = Control.MouseFilterEnum.Pass;
        root.AddChild(panel);

        var bg = new StyleBoxFlat();
        bg.BgColor = new Color(0.08f, 0.08f, 0.1f, 0.95f);
        bg.BorderColor = borderColor;
        bg.BorderWidthLeft = 2;
        bg.BorderWidthRight = 2;
        bg.BorderWidthTop = 2;
        bg.BorderWidthBottom = 2;
        bg.CornerRadiusTopLeft = 8;
        bg.CornerRadiusTopRight = 8;
        bg.CornerRadiusBottomLeft = 8;
        bg.CornerRadiusBottomRight = 8;
        panel.AddThemeStyleboxOverride("panel", bg);

        return panel;
    }

    /// <summary>
    /// 给按钮套统一的 normal/hover/disabled StyleBoxFlat。以功能最全的一版为基准（含 disabled 态），
    /// 差异项参数化：圆角半径、normal 底色、hover 透明度、可选字号。
    /// </summary>
    public static void StyleButton(
        Button btn,
        Color accent,
        int cornerRadius = 4,
        Color? normalBg = null,
        float hoverAlpha = 0.3f,
        int? fontSize = null)
    {
        btn.AddThemeColorOverride("font_color", new Color(0.95f, 0.95f, 0.9f));
        if (fontSize.HasValue)
            btn.AddThemeFontSizeOverride("font_size", fontSize.Value);

        var normal = new StyleBoxFlat();
        normal.BgColor = normalBg ?? new Color(0.15f, 0.15f, 0.17f);
        ApplyBorder(normal, accent, cornerRadius);
        btn.AddThemeStyleboxOverride("normal", normal);

        var hover = new StyleBoxFlat();
        hover.BgColor = accent * new Color(1, 1, 1, hoverAlpha);
        ApplyBorder(hover, accent, cornerRadius);
        btn.AddThemeStyleboxOverride("hover", hover);

        var disabled = new StyleBoxFlat();
        disabled.BgColor = new Color(0.08f, 0.08f, 0.1f);
        ApplyBorder(disabled, new Color(0.2f, 0.2f, 0.2f), cornerRadius);
        btn.AddThemeStyleboxOverride("disabled", disabled);
    }

    private static void ApplyBorder(StyleBoxFlat box, Color borderColor, int cornerRadius)
    {
        box.BorderColor = borderColor;
        box.BorderWidthLeft = 1;
        box.BorderWidthRight = 1;
        box.BorderWidthTop = 1;
        box.BorderWidthBottom = 1;
        box.CornerRadiusTopLeft = cornerRadius;
        box.CornerRadiusTopRight = cornerRadius;
        box.CornerRadiusBottomLeft = cornerRadius;
        box.CornerRadiusBottomRight = cornerRadius;
    }
}
