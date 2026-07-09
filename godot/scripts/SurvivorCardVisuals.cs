namespace DeadSignal.Godot;

/// <summary>
/// 幸存者卡牌栏的**纯逻辑**部分（不引 Godot 类型，故可 Link 进 Combat.Tests 单测）：
/// 按 <c>Pawn.Id</c> 稳定映射头像索引 + 生成稳定占位色。SurvivorCardBar 只做 Godot 渲染，
/// 把这里的结果转成 <c>Texture</c> 路径 / <c>Color</c>。稳定=同一 Id 每次结果一致，与创建顺序无关。
/// </summary>
public static class SurvivorCardVisuals
{
    /// <summary>可用头像张数（OpenGameArt Survivor Portraits，7 女 + 6 男）。见 assets/portraits/CREDITS.md。</summary>
    public const int PortraitCount = 13;

    /// <summary>
    /// 13 张头像文件名，稳定顺序（索引即映射目标）。SurvivorCardBar 前缀 <c>res://assets/portraits/</c> 载入。
    /// 顺序固定，勿随意重排——否则同一 Id 换脸。
    /// </summary>
    public static readonly string[] PortraitFiles =
    {
        "femaleportrait1.png", "maleportrait1.png", "femaleportrait2.png", "maleportrait2.png",
        "femaleportrait3.png", "maleportrait3.png", "femaleportrait4.png", "maleportrait4.png",
        "femaleportrait5.png", "maleportrait5.png", "femaleportrait6.png", "maleportrait6.png",
        "femaleportrait7.png",
    };

    /// <summary>Id → 头像索引 [0,PortraitCount)。非负取模，负 Id 也稳定落在合法区间。</summary>
    public static int PortraitIndexForId(int id) => ((id % PortraitCount) + PortraitCount) % PortraitCount;

    /// <summary>Id → 头像文件名（稳定）。</summary>
    public static string PortraitFileForId(int id) => PortraitFiles[PortraitIndexForId(id)];

    /// <summary>
    /// Id → 稳定占位色（RGB 0..1）。用黄金分割角散布色相，固定饱和/明度，保证相邻 Id 也颜色分明、
    /// 且同 Id 恒定。无素材（头像缺失）时卡牌用它做色块，有素材时用作边框/选中高亮基色。
    /// </summary>
    public static (float R, float G, float B) StableColorForId(int id)
    {
        // 黄金分割共轭：色相在 [0,1) 上近均匀散布，避免相邻 Id 撞色。
        const double golden = 0.618033988749895;
        // 先把 Id 折成非负，再乘黄金角取小数部分作色相。
        uint u = unchecked((uint)id);
        double hue = (u * golden) % 1.0;
        return HsvToRgb(hue, 0.55, 0.80);
    }

    /// <summary>HSV(h,s,v 均 0..1) → RGB(0..1)。纯函数，供占位色用。</summary>
    private static (float R, float G, float B) HsvToRgb(double h, double s, double v)
    {
        double hh = (h - System.Math.Floor(h)) * 6.0; // [0,6)
        int i = (int)hh;
        double f = hh - i;
        double p = v * (1.0 - s);
        double q = v * (1.0 - s * f);
        double t = v * (1.0 - s * (1.0 - f));
        (double r, double g, double b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return ((float)r, (float)g, (float)b);
    }
}
