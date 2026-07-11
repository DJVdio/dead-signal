using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 命中溅血贴花：在承伤方脚下（iso 屏幕空间）落一摊程序化血迹，缓慢淡出后自毁。
///
/// 挂在 <c>iso_layer</c>（YSortEnabled）下，与 <see cref="ActorSprite"/> 同层同坐标系——由
/// ActorSprite 的受击订阅在承伤方脚点生成。为让血迹压在站在其上的人形<b>之下</b>，本节点的
/// YSort 锚点（node.Y）比脚点上抬 <see cref="YSortLift"/>（Y 越小 = YSort 越靠后 = 先画/在下层），
/// 再把血斑本体在 _Draw 里下移同等像素补偿——视觉仍精确落在脚点，路过前方的人形（Y 更大）照常遮挡。
///
/// 血斑为若干 2:1 压扁椭圆（贴合 iso 地面透视），随机散布叠成一摊；severity 越高（断肢/重伤）
/// 越大越多。纯视觉，用 Godot 内置 RNG 取散布，不走战斗引擎的 IRandomSource。
/// </summary>
public sealed partial class BloodDecal : Node2D
{
    // 停留后再淡出：前 HoldTime 满不透，随后在剩余寿命里线性淡出。
    private const double Lifetime = 26.0;
    private const double HoldTime = 18.0;
    private const float YSortLift = 4f;   // 上抬 YSort 锚点，保证压在人形脚下

    // 并发血斑上限（拟定待调）：持续战斗会短时堆积大量各自每帧 _Process 的 Node，超限即回收最老的。
    // Live 按生成顺序排列（表头最老）；节点自然过期或被回收时经 _ExitTree 自摘。营地全在主线程，静态表安全。
    private const int MaxConcurrent = 64;
    private static readonly System.Collections.Generic.List<BloodDecal> Live = new();

    private double _age;

    private Color[] _cols = System.Array.Empty<Color>();
    private Vector2[] _centers = System.Array.Empty<Vector2>();
    private Vector2[] _radii = System.Array.Empty<Vector2>();

    /// <summary>
    /// 在 iso 脚点生成一摊血。<paramref name="footIso"/>=承伤方脚点的 iso 屏幕坐标（= ActorSprite 的 node 位置）；
    /// <paramref name="severity"/>=0..1 溅血强度（重伤/断肢趋近 1）；<paramref name="heavy"/>=断肢/大流血给更暗更浓的血。
    /// </summary>
    public static BloodDecal Spawn(Node parent, Vector2 footIso, float severity, bool heavy)
    {
        var d = new BloodDecal();
        parent.AddChild(d);
        // node.Y 上抬做 YSort 压层；血斑本体在 _Draw 里补偿下移 YSortLift。
        d.Position = footIso - new Vector2(0, YSortLift);
        d.Build(Mathf.Clamp(severity, 0f, 1f), heavy);

        Live.Add(d);
        while (Live.Count > MaxConcurrent)
        {
            var oldest = Live[0];
            Live.RemoveAt(0);
            if (GodotObject.IsInstanceValid(oldest))
            {
                oldest.QueueFree();
            }
        }
        return d;
    }

    public override void _ExitTree() => Live.Remove(this);

    private void Build(float severity, bool heavy)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        // 血斑数量/尺寸随强度增长：轻伤 2~3 小点，断肢 5~6 大摊。
        int blobs = 2 + Mathf.RoundToInt(severity * 4f) + (heavy ? 1 : 0);
        float spread = 5f + severity * 14f;    // 散布半径（iso 屏幕像素）
        float baseRx = 4f + severity * 9f;     // 基准横向半径

        // 血色：常规暗红，断肢/大流血更深更沉。
        Color blood = heavy ? new Color(0.42f, 0.03f, 0.04f) : new Color(0.55f, 0.07f, 0.07f);

        _cols = new Color[blobs];
        _centers = new Vector2[blobs];
        _radii = new Vector2[blobs];
        for (int i = 0; i < blobs; i++)
        {
            // 椭圆水平散布（iso 地面偏横向铺开），补偿 YSortLift 使中心视觉落在脚点。
            float ox = rng.RandfRange(-spread, spread);
            float oy = rng.RandfRange(-spread * 0.5f, spread * 0.5f) + YSortLift;
            _centers[i] = new Vector2(ox, oy);
            float rx = baseRx * rng.RandfRange(0.55f, 1.25f);
            _radii[i] = new Vector2(rx, rx * 0.5f); // 2:1 压扁贴地
            _cols[i] = blood.Darkened(rng.RandfRange(0f, 0.22f));
        }

        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= Lifetime)
        {
            QueueFree();
            return;
        }
        // 停留期满后线性淡出。
        float alpha = _age <= HoldTime
            ? 1f
            : 1f - (float)((_age - HoldTime) / (Lifetime - HoldTime));
        Modulate = new Color(1, 1, 1, Mathf.Clamp(alpha, 0f, 1f));
    }

    public override void _Draw()
    {
        for (int i = 0; i < _centers.Length; i++)
        {
            DrawColoredPolygon(Ellipse(_centers[i], _radii[i].X, _radii[i].Y), _cols[i]);
        }
    }

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry, int seg = 16)
    {
        var pts = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = Mathf.Tau * i / seg;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return pts;
    }
}
