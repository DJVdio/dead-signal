using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 一块 cartesian 矩形（山体/墙/建筑/地面/道具的 footprint）在 iso 层的**菱形块状**视觉。
/// 把 footprint 按 <see cref="Cell"/> 切成 cartesian 方格，逐格投影成 iso 菱形填充，
/// 沿用 <see cref="PixelPanel"/> 的 sin-hash 明度抖动免外部素材。B0 只求块状观感，立面/精致留 B1。
///
/// YSort 锚点：节点原点设在 footprint 的**前角**（cartesian End，即 x+y 最大处，投影后屏幕 Y 最大），
/// 使高遮挡物按「前沿」参与 <see cref="Node2D"/> 的 YSort 排序（B0 为逐物体粗粒度，长墙可能偶发错序——
/// 已知限制，B1 再按小瓦片细分）。地面这类恒底层者由调用方直接压 z_index，不依赖 YSort。
/// </summary>
public sealed partial class IsoTilePanel : Node2D
{
    public Rect2 FootprintCart;      // 世界 cartesian footprint
    public PixelStyle Style = new();
    public int Seed;
    public float Cell = 64f;         // 每格 cartesian 边长「拟定待调」

    public override void _Ready() => Position = Iso.Project(FootprintCart.End);

    public override void _Draw()
    {
        Color baseColor = Style.color is { Length: >= 3 }
            ? new Color((float)Style.color[0], (float)Style.color[1], (float)Style.color[2])
            : new Color(0.4f, 0.4f, 0.4f);
        float jitter = (float)Style.jitter;
        Vector2 anchor = FootprintCart.End; // 节点原点对应的 cartesian 点

        int cols = Mathf.CeilToInt(FootprintCart.Size.X / Cell);
        int rows = Mathf.CeilToInt(FootprintCart.Size.Y / Cell);

        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                float x0 = FootprintCart.Position.X + cx * Cell;
                float y0 = FootprintCart.Position.Y + cy * Cell;
                float x1 = Mathf.Min(x0 + Cell, FootprintCart.End.X);
                float y1 = Mathf.Min(y0 + Cell, FootprintCart.End.Y);
                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }

                // 方格四角投到 iso 局部空间（Project 线性，减 anchor 即节点局部坐标）。
                var diamond = new Vector2[]
                {
                    Iso.Project(new Vector2(x0, y0) - anchor),
                    Iso.Project(new Vector2(x1, y0) - anchor),
                    Iso.Project(new Vector2(x1, y1) - anchor),
                    Iso.Project(new Vector2(x0, y1) - anchor),
                };

                float shade = (Hash(cx, cy) - 0.5f) * 2f * jitter;
                Color c = new(
                    Mathf.Clamp(baseColor.R + shade, 0f, 1f),
                    Mathf.Clamp(baseColor.G + shade, 0f, 1f),
                    Mathf.Clamp(baseColor.B + shade, 0f, 1f));
                DrawColoredPolygon(diamond, c);
            }
        }
    }

    // 确定性伪随机（每格稳定，重绘不闪烁）。与 PixelPanel 同式。
    private float Hash(int cx, int cy)
    {
        float s = Mathf.Sin(cx * 127.1f + cy * 311.7f + Seed * 57.13f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }
}
