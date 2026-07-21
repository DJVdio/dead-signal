using Godot;

namespace DeadSignal.Godot;

/// <summary>探索关正式环境物件图集节点；纯视觉，不注册碰撞、导航或交互。</summary>
public sealed partial class ExplorationPropSprite : Node2D
{
    private const string AtlasPath = "res://assets/world/exploration-props.png";
    private static Texture2D? _atlas;

    public Vector2 CartesianPosition;
    public int AtlasIndex;
    public float DisplaySize = 150f;

    public override void _Ready()
    {
        Position = Iso.Project(CartesianPosition);
        TextureFilter = TextureFilterEnum.Nearest;
        _atlas ??= GD.Load<Texture2D>(AtlasPath);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_atlas is null || AtlasIndex is < 0 or >= 16) return;
        float cellW = _atlas.GetWidth() / 4f;
        float cellH = _atlas.GetHeight() / 4f;
        int col = AtlasIndex % 4;
        int row = AtlasIndex / 4;
        var source = new Rect2(col * cellW, row * cellH, cellW, cellH);
        float width = DisplaySize * (cellW / cellH);
        DrawTextureRectRegion(_atlas, new Rect2(-width / 2f, -DisplaySize, width, DisplaySize), source);
    }
}
