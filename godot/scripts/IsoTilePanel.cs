using Godot;

namespace DeadSignal.Godot;

/// <summary>像素/菱形块样式（颜色 + 明度抖动）。camp.json 的 ground/mountainStyle/fenceStyle 等映射到此。</summary>
public sealed class PixelStyle
{
    public double[]? color { get; set; }
    public double tile { get; set; }   // 兼容旧字段（现由节点的 Cell 决定瓦片尺寸）
    public double jitter { get; set; }
}

/// <summary>
/// 一块 cartesian 矩形（山体/墙/建筑/地面/道具/屋顶的 footprint）在 iso 层的视觉。
/// <see cref="Height"/>=0 时画平铺菱形地块（地面/门槛）；<see cref="Height"/>&gt;0 时画**抬起的立体块**：
/// 顶面抬升 Height 后平铺菱形（sin-hash 明度抖动），<see cref="Facade"/> 为真再补前向左/右两面立面
/// （左右不同明度做假 3D）。屋顶用 Height&gt;0 + Facade=false（只要抬起的顶面、无裙边）。
///
/// YSort 锚点：节点原点 = footprint 前角（cartesian End，投影后屏幕 Y 最大）。长墙/大建筑由调用方
/// 切成小块各自成节点，即按前沿逐块 YSort，修正 B0 的 actor 擦身错序。
/// </summary>
public sealed partial class IsoTilePanel : Node2D
{
    private const string MaterialTexturePath = "res://assets/world/gritty-material-overlay.png";
    private const string GroundDecalAtlasPath = "res://assets/world/ground-decals.png";
    private static Texture2D? _materialTexture;
    private static Texture2D? _groundDecalAtlas;
    private static readonly int[] AmbientDecalIndices = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12, 13, 14 };

    public Rect2 FootprintCart;      // 世界 cartesian footprint
    public PixelStyle Style = new();
    public int Seed;
    public float Cell = 48f;         // 顶面每格 cartesian 边长「拟定待调」
    public float Height;             // 立面抬升高度（屏幕像素），0 = 平铺
    public bool Facade;              // 是否画前向左右立面

    public override void _Ready()
    {
        Position = Iso.Project(FootprintCart.End);
        TextureFilter = TextureFilterEnum.Nearest;
        TextureRepeat = TextureRepeatEnum.Enabled;
        _materialTexture ??= GD.Load<Texture2D>(MaterialTexturePath);
        _groundDecalAtlas ??= GD.Load<Texture2D>(GroundDecalAtlasPath);
    }

    public override void _Draw()
    {
        Color baseColor = Style.color is { Length: >= 3 }
            ? new Color((float)Style.color[0], (float)Style.color[1], (float)Style.color[2])
            : new Color(0.4f, 0.4f, 0.4f);
        float jitter = (float)Style.jitter;
        Vector2 anchor = FootprintCart.End;               // 节点原点对应的 cartesian 点
        Vector2 up = new(0, -Height);

        Vector2 G(Vector2 cart) => Iso.Project(cart - anchor); // cartesian → iso 局部（地面高度）

        // 立面（先画，顶面覆在其上）。取前向两条边：右面(TR-BR)、左面(BL-BR)。
        if (Facade && Height > 0f)
        {
            Vector2 tr = new(FootprintCart.End.X, FootprintCart.Position.Y);
            Vector2 br = FootprintCart.End;
            Vector2 bl = new(FootprintCart.Position.X, FootprintCart.End.Y);

            Color right = Shade(baseColor, 0.72f);
            Color left = Shade(baseColor, 0.55f);

            DrawMaterialPolygon(
                new[] { G(tr) + up, G(br) + up, G(br), G(tr) },
                right,
                FacadeUvs(FootprintCart.Size.Y, Height, Seed * 19f));
            DrawMaterialPolygon(
                new[] { G(bl) + up, G(br) + up, G(br), G(bl) },
                left,
                FacadeUvs(FootprintCart.Size.X, Height, Seed * 31f));
        }

        // 顶面：抬升 up 后平铺菱形。
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

                var diamond = new[]
                {
                    G(new Vector2(x0, y0)) + up,
                    G(new Vector2(x1, y0)) + up,
                    G(new Vector2(x1, y1)) + up,
                    G(new Vector2(x0, y1)) + up,
                };
                float shade = (Hash(cx, cy) - 0.5f) * 2f * jitter;
                var uvs = new[]
                {
                    MaterialUv(new Vector2(x0, y0)),
                    MaterialUv(new Vector2(x1, y0)),
                    MaterialUv(new Vector2(x1, y1)),
                    MaterialUv(new Vector2(x0, y1)),
                };
                // 纹理均值约 0.78；1.22 的补偿让既有色板亮度基本不漂移，只增加正式材质颗粒。
                DrawMaterialPolygon(diamond, Shade(baseColor, 1.22f + shade), uvs);

                // 仅在平面上稀疏铺环境细节；立体块不贴。血迹/弹壳留给真实战斗反馈，避免凭空讲故事。
                if (Height <= 0f && Hash(cx + 37, cy + 53) > 0.88f)
                    DrawGroundDecal(G(new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f)), cx, cy);
            }
        }
    }

    private void DrawMaterialPolygon(Vector2[] points, Color color, Vector2[] uvs)
    {
        if (_materialTexture is not null)
            DrawColoredPolygon(points, color, uvs, _materialTexture);
        else
            DrawColoredPolygon(points, color);
    }

    private static Vector2 MaterialUv(Vector2 cart) => cart * 2f;

    private static Vector2[] FacadeUvs(float width, float height, float offset)
    {
        float u0 = offset;
        float v0 = offset * 0.37f;
        return new[]
        {
            new Vector2(u0, v0),
            new Vector2(u0 + width * 2f, v0),
            new Vector2(u0 + width * 2f, v0 + height * 2f),
            new Vector2(u0, v0 + height * 2f),
        };
    }

    private void DrawGroundDecal(Vector2 center, int cx, int cy)
    {
        if (_groundDecalAtlas is null)
            return;

        int pick = Mathf.Abs(cx * 31 + cy * 17 + Seed * 13) % AmbientDecalIndices.Length;
        int index = AmbientDecalIndices[pick];
        int col = index % 4;
        int row = index / 4;
        float sourceW = _groundDecalAtlas.GetWidth() / 4f;
        float sourceH = _groundDecalAtlas.GetHeight() / 4f;
        var source = new Rect2(col * sourceW, row * sourceH, sourceW, sourceH);
        float size = Mathf.Clamp(Cell * 0.72f, 34f, 58f);
        DrawTextureRectRegion(_groundDecalAtlas, new Rect2(center - new Vector2(size, size) / 2f, new Vector2(size, size)), source);
    }

    private static Color Shade(Color c, float mul) => new(
        Mathf.Clamp(c.R * mul, 0f, 1f),
        Mathf.Clamp(c.G * mul, 0f, 1f),
        Mathf.Clamp(c.B * mul, 0f, 1f));

    // 确定性伪随机（每格稳定，重绘不闪烁）。
    private float Hash(int cx, int cy)
    {
        float s = Mathf.Sin(cx * 127.1f + cy * 311.7f + Seed * 57.13f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }
}
