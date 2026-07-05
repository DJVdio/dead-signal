using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 伪等距（faux-iso，PZ 做法）投影：把 cartesian 逻辑平面（camp.json 用的像素坐标）线性映射到
/// 2:1 菱形等距屏幕坐标。逻辑/物理/寻路/碰撞全部留在 cartesian 平面，本类只服务渲染层与鼠标反投影。
///
/// 关键约束：<see cref="Project"/> 与 <see cref="Unproject"/> 必须互为**精确逆变换**——右键落点、
/// 点选命中全靠 Unproject 把 iso 屏幕坐标还原回 cartesian 再和 actor 逻辑位置比对，误差会直接表现为
/// 「点哪走别处」。二者皆为纯线性无仿射偏移，世界原点 (0,0) 投到屏幕 (0,0)。
///
/// 单位说明：设计方案给的公式 sx=(wx-wy)*halfW, sy=(wx+wy)*halfH 以「瓦片」为单位；而 camp.json
/// 是像素坐标，故这里引入 <see cref="TileSize"/> 作桥（cartesian 像素 → 瓦片再投影），
/// 等价于线性缩放 halfW/TileSize，不影响逆变换的精确性。三个常数皆「拟定待调」占位。
/// </summary>
public static class Iso
{
    /// <summary>每个 iso 菱形对应多少 cartesian 像素（像素↔瓦片换算桥）。「拟定待调」</summary>
    public const float TileSize = 64f;

    /// <summary>菱形半宽（屏幕像素）。「拟定待调」</summary>
    public const float HalfW = 32f;

    /// <summary>菱形半高（屏幕像素），2:1 → = HalfW/2。「拟定待调」</summary>
    public const float HalfH = 16f;

    /// <summary>cartesian（像素）→ iso 屏幕坐标。</summary>
    public static Vector2 Project(Vector2 cart) => Project(cart.X, cart.Y);

    public static Vector2 Project(float wx, float wy)
    {
        float tx = wx / TileSize;
        float ty = wy / TileSize;
        return new Vector2((tx - ty) * HalfW, (tx + ty) * HalfH);
    }

    /// <summary>iso 屏幕坐标 → cartesian（像素）。<see cref="Project"/> 的精确逆。</summary>
    public static Vector2 Unproject(Vector2 screen) => Unproject(screen.X, screen.Y);

    public static Vector2 Unproject(float sx, float sy)
    {
        float tx = (sx / HalfW + sy / HalfH) / 2f;
        float ty = (sy / HalfH - sx / HalfW) / 2f;
        return new Vector2(tx * TileSize, ty * TileSize);
    }

    /// <summary>把一个 cartesian 矩形四角投影后的屏幕空间包围盒（相机边界用）。</summary>
    public static Rect2 ProjectBounds(Rect2 cart)
    {
        Vector2 a = Project(cart.Position);
        Vector2 b = Project(new Vector2(cart.End.X, cart.Position.Y));
        Vector2 c = Project(cart.End);
        Vector2 d = Project(new Vector2(cart.Position.X, cart.End.Y));
        float minX = Mathf.Min(Mathf.Min(a.X, b.X), Mathf.Min(c.X, d.X));
        float minY = Mathf.Min(Mathf.Min(a.Y, b.Y), Mathf.Min(c.Y, d.Y));
        float maxX = Mathf.Max(Mathf.Max(a.X, b.X), Mathf.Max(c.X, d.X));
        float maxY = Mathf.Max(Mathf.Max(a.Y, b.Y), Mathf.Max(c.Y, d.Y));
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }
}
