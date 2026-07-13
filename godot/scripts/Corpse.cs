using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 一具躺在地上的尸体（iso 层视觉）。
///
/// <b>没有碰撞体积</b>：本节点是纯 <see cref="Node2D"/>，<b>不建</b> CollisionShape / StaticBody /
/// NavigationObstacle，<b>不挖</b>导航洞 —— 活人和丧尸直接从尸体上走过去，寻路也不绕。它唯一「占」的东西
/// 是 <see cref="CorpseField"/> 里的一个**尸体格**（只管下一具尸体往哪躺，不管谁能不能走）。
/// 这与 camp.json 里 role=corpse 静态道具（祖母的尸体）的既有口径一致。
///
/// 绘制与 <see cref="BloodDecal"/> 同范式：挂 iso_layer（YSortEnabled），YSort 锚点比脚点上抬
/// <see cref="YSortLift"/>、本体在 _Draw 里下移补偿 —— 保证站在尸体上的人形（Y 更大）画在尸体之上。
/// 俯身姿态用一组 2:1 压扁椭圆（贴合 iso 地面透视）画：躯干 + 头 + 摊开的四肢，朝向由所在格坐标
/// 派生（确定性，无 RNG —— 同一格的尸体每次重开都朝同一边）。
/// </summary>
public sealed partial class Corpse : Node2D
{
    private const float YSortLift = 6f;   // 上抬 YSort 锚点：人从尸体上走过时人在上

    private Color _body;
    private float _r = 12f;
    private float _angle;                 // 躺倒朝向（iso 屏幕弧度）

    /// <summary>本尸占的尸体格（回收时据此还格给 <see cref="CorpseField"/>）。</summary>
    public CorpseCell Cell { get; private set; }

    /// <summary>
    /// 这具尸体身上能扒下来的东西 —— <b>它生前穿的那些，原样照搬</b>（<see cref="CorpseLoot.Strip"/>：
    /// 穿什么扒什么、零掷骰、不折损）。
    /// 空 = 什么也没有（衣不蔽体）⇒ 营地层<b>不为它登记可搜刮点</b>，地图上就只是一具躺着的尸体。
    /// 纯数据，与绘制/落位无关。
    /// </summary>
    public List<LootItem> Loot { get; } = new();

    /// <summary>
    /// 本尸在 <see cref="ContainerLoot"/> / 营地容器表里的唯一标识（如「丧尸的尸体 #12」）。
    /// 空串 = 没登记成容器（身上没东西）。被 <see cref="CorpseYard"/> 回收时据此注销登记。
    /// </summary>
    public string ContainerId { get; set; } = "";

    /// <summary>落点的 <b>cartesian</b> 世界坐标（<see cref="Position"/> 是 iso 投影后的屏幕坐标，两者不通用）。
    /// 营地层用它算可点击容器的命中矩形（<c>HitContainerAt</c> 收的是 cartesian）。</summary>
    public Vector2 CartPosition { get; set; }

    /// <summary>
    /// 落地时的相位计数（<see cref="CorpseYard.PhaseTick"/>）。过了 <see cref="CorpseDecay.LifetimePhases"/>
    /// 个相位这具尸体就被清理掉（搜刮窗口到此为止）。祖母那具 authored 尸体不是 Corpse 节点，不受此限。
    /// </summary>
    public int SpawnPhaseTick { get; set; }

    /// <summary>
    /// 在 iso 层生成一具尸体。<paramref name="footIso"/>=落点（尸体格中心）的 iso 屏幕坐标；
    /// <paramref name="bodyTint"/>/<paramref name="radius"/> 取自死者（丧尸偏绿、活人偏肉色）。
    /// </summary>
    public static Corpse Spawn(Node isoLayer, Vector2 footIso, CorpseCell cell, Color bodyTint, float radius)
    {
        var c = new Corpse
        {
            Cell = cell,
            _body = bodyTint.Darkened(0.35f),   // 死了的颜色：压暗、去饱和
            _r = Mathf.Max(6f, radius),
        };
        isoLayer.AddChild(c);
        c.Position = footIso - new Vector2(0, YSortLift);
        // 朝向由格坐标哈希派生：确定性（同一格恒同姿态），但一片尸堆看起来是乱躺的。
        int h = cell.X * 73856093 ^ cell.Y * 19349663;
        c._angle = Mathf.Pi * 2f * ((h & 0xFFFF) / 65535f);
        c.ZIndex = 0;
        return c;
    }

    public override void _Draw()
    {
        // 补偿 YSort 上抬：本体画回真实落点。
        var o = new Vector2(0, YSortLift);
        float r = _r;

        Color body = _body;
        Color dark = _body.Darkened(0.35f);
        Color outline = new(0.05f, 0.05f, 0.06f, 0.75f);

        var f = new Vector2(Mathf.Cos(_angle), Mathf.Sin(_angle) * 0.5f);        // 身体长轴（iso 压扁 2:1）
        var p = new Vector2(-Mathf.Sin(_angle), Mathf.Cos(_angle) * 0.5f);       // 侧向

        Vector2 torso = o;
        Vector2 head = o + f * r * 1.05f;
        Vector2 legL = o - f * r * 1.15f + p * r * 0.38f;
        Vector2 legR = o - f * r * 1.15f - p * r * 0.38f;
        Vector2 armL = o + f * r * 0.25f + p * r * 1.05f;
        Vector2 armR = o + f * r * 0.20f - p * r * 1.10f;

        // 四肢摊开（先画，压在躯干下）
        DrawLine(torso, legL, outline, r * 0.50f);
        DrawLine(torso, legR, outline, r * 0.50f);
        DrawLine(torso, armL, outline, r * 0.40f);
        DrawLine(torso, armR, outline, r * 0.40f);
        DrawLine(torso, legL, dark, r * 0.34f);
        DrawLine(torso, legR, dark, r * 0.34f);
        DrawLine(torso, armL, dark, r * 0.26f);
        DrawLine(torso, armR, dark, r * 0.26f);

        // 躯干（俯卧：椭圆压扁，长轴沿身体朝向）
        DrawColoredPolygon(Ellipse(torso, r * 0.95f + 1.2f, r * 0.55f + 1.2f, _angle), outline);
        DrawColoredPolygon(Ellipse(torso, r * 0.95f, r * 0.55f, _angle), body);
        DrawColoredPolygon(Ellipse(torso - f * r * 0.25f, r * 0.5f, r * 0.32f, _angle), dark);

        // 头
        DrawColoredPolygon(Ellipse(head, r * 0.52f + 1.2f, r * 0.34f + 1.2f, _angle), outline);
        DrawColoredPolygon(Ellipse(head, r * 0.52f, r * 0.34f, _angle), body);
    }

    /// <summary>绕 <paramref name="rot"/> 旋转的椭圆多边形（iso 地面透视：长轴 a、短轴 b）。</summary>
    private static Vector2[] Ellipse(Vector2 c, float a, float b, float rot, int seg = 18)
    {
        var pts = new Vector2[seg];
        float cs = Mathf.Cos(rot), sn = Mathf.Sin(rot) * 0.5f;   // 短轴方向同样吃 2:1 压扁
        for (int i = 0; i < seg; i++)
        {
            float t = Mathf.Tau * i / seg;
            float x = a * Mathf.Cos(t);
            float y = b * Mathf.Sin(t);
            pts[i] = c + new Vector2(x * cs - y * sn * 2f, x * sn + y * cs * 0.5f);
        }
        return pts;
    }
}
