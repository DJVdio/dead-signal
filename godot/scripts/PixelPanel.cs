using Godot;

namespace DeadSignal.Godot;

/// <summary>像素面板样式（颜色 + 瓦片尺寸 + 明度抖动），供 <see cref="PixelPanel"/> 与 camp.json 共用。</summary>
public sealed class PixelStyle
{
    public double[]? color { get; set; }
    public double tile { get; set; }
    public double jitter { get; set; }
}

/// <summary>
/// 程序化像素地块：把一块矩形按瓦片切分，每格用确定性哈希做明度抖动，画出简洁像素观感，
/// 免去任何外部素材。营地的地面/山体/墙/屋顶/道具皆复用此节点，只换调色板与瓦片尺寸。
///
/// 注意：本类须独占一个 .cs 文件——Godot .NET 源生成器要求「一个文件一个 Godot 脚本类」，
/// 否则不生成 ScriptPath 映射，场景实例化会报「could not be found」。
/// </summary>
public sealed partial class PixelPanel : Node2D
{
    public Rect2 Area;
    public PixelStyle Style = new();
    public int Seed;

    public override void _Draw()
    {
        Color baseColor = Style.color is { Length: >= 3 }
            ? new Color((float)Style.color[0], (float)Style.color[1], (float)Style.color[2])
            : new Color(0.4f, 0.4f, 0.4f);
        float tile = Style.tile > 0 ? (float)Style.tile : 20f;
        float jitter = (float)Style.jitter;

        int cols = Mathf.CeilToInt(Area.Size.X / tile);
        int rows = Mathf.CeilToInt(Area.Size.Y / tile);

        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                float px = Area.Position.X + cx * tile;
                float py = Area.Position.Y + cy * tile;
                float w = Mathf.Min(tile, Area.End.X - px);
                float h = Mathf.Min(tile, Area.End.Y - py);
                if (w <= 0 || h <= 0)
                {
                    continue;
                }

                float shade = (Hash(cx, cy) - 0.5f) * 2f * jitter;
                Color c = new(
                    Mathf.Clamp(baseColor.R + shade, 0f, 1f),
                    Mathf.Clamp(baseColor.G + shade, 0f, 1f),
                    Mathf.Clamp(baseColor.B + shade, 0f, 1f));
                DrawRect(new Rect2(px, py, w, h), c);
            }
        }
    }

    // 确定性伪随机（每格稳定，重绘不闪烁）。
    private float Hash(int cx, int cy)
    {
        float s = Mathf.Sin(cx * 127.1f + cy * 311.7f + Seed * 57.13f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }
}
