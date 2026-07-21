using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 营地 authored 道具的正式像素表现。脚点仍锚在 cartesian footprint 的前角并投影到 iso 层，
/// 只覆盖既有程序化块的外观；碰撞、导航洞、掩体、容器和交互仍由 CampMain 原节点负责。
/// </summary>
public sealed partial class IsoPropSprite : Node2D
{
    private const string AtlasPath = "res://assets/world/camp-props.png";
    private static Texture2D? _atlas;

    public Rect2 FootprintCart;
    public int AtlasIndex;

    public override void _Ready()
    {
        Position = Iso.Project(FootprintCart.End);
        TextureFilter = TextureFilterEnum.Nearest;
        _atlas ??= GD.Load<Texture2D>(AtlasPath);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_atlas is null || AtlasIndex is < 0 or >= 16)
            return;

        const float cell = 96f;
        int col = AtlasIndex % 4;
        int row = AtlasIndex / 4;
        var source = new Rect2(col * cell, row * cell, cell, cell);

        // footprint 只决定屏幕占幅，源图保持正方形与底部脚点，避免重新解释玩法几何。
        float size = Mathf.Clamp((FootprintCart.Size.X + FootprintCart.Size.Y) * 0.48f, 54f, 112f);
        var destination = new Rect2(-size / 2f, -size, size, size);
        DrawTextureRectRegion(_atlas, destination, source);
    }
}
