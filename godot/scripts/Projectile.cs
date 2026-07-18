using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 锥形散布弹道子弹。发射方向由 <see cref="Actor"/> 用 <see cref="Ballistics"/> 误差角锥采样得出，
/// 之后沿该方向匀速直线飞行，不制导。命中判定为**路径上首个碰撞体**：
/// 撞到任意 <see cref="Actor"/> → 对其结算一次攻击；撞墙或飞出最大射程 → 落空自毁。
///
/// 空间层职责（符合架构：几何/碰撞归 Godot，引擎只出纯函数）。每物理步用一段射线查询
/// 本步位移路径上最近的碰撞体，避免高速穿透（tunneling）。
/// </summary>
public sealed partial class Projectile : Node2D
{
    private Vector2 _dir;
    // [T68] 飞速改为逐武器字段（Weapon.FlightSpeed，当前值以 Wiki 配置为准），出膛时从有效武器捕获。
    // 有效武器＝弓弩的「弓 ⊗ 箭 ⊗ 书」（Archery.Combine 会把《弓与箭之道》的弹道速度加成连乘进 FlightSpeed），
    // 也含后续弓弩改装的飞速乘子。缓存进本枚弹丸，避免飞行途中再回查武器。
    private float _speed = 560f;
    private float _maxDist;
    private float _traveled;
    private Weapon _weapon = null!;

    /// <summary>本发弹药的具体材料键（弓弩＝射出去的那种箭）。仅供箭矢回收；空串＝不吃弹药/不回收。</summary>
    private string _ammoKey = "";
    private CombatEngine _combat = null!;
    private Actor _shooter = null!;
    // 射手岗位射程倍率（守卫哨塔/屋顶远程加成以 Wiki 配置为准）：把命中距离压回武器原生衰减曲线，令 +射程时衰减曲线整体拉长
    // （否则超原生 MaxRange 段会静默变 0 伤）。默认值为中性值，零回归。
    private float _rangeMultiplier = 1f;
    // 攻方专属先手倍率（默认中性；实时层注入，CombatResolver 保持原样）。
    private double _damageFactor = 1.0;

    // 命中掩码：墙(层3=0b0100) + 幸存者(层1) + 丧尸(层2) + 劫掠者(层4=0b1000)——覆盖全阵营层，
    // 弹道能命中任意阵营；具体是否结算由下方 Factions.IsHostile + 架肩豁免裁定（异阵营命中、同阵营豁免）。
    private const uint HitMask = 0b1111;

    /// <summary>架肩豁免的额外间距余量（空间几何参数，当前值以 Wiki 配置为准）。叠加在两者碰撞半径之和上。</summary>
    private const float ShoulderGraceMargin = 6f;

    // 复用的射线查询对象与排除表：每物理帧清空重填，避免每帧新建 Godot 托管集合（Variant 编组开销高）。
    private readonly global::Godot.Collections.Array<Rid> _excluded = new();
    private PhysicsRayQueryParameters2D? _query;

    /// <param name="ammoKey">
    /// 本发弹药的**具体**材料键（弓弩＝射出去的那种箭；枪＝子弹/霰弹键；不吃弹药＝空串）。
    /// 只用于**箭矢回收**：`weapon.AmmoKey` 对弓弩而言是类别键（"ammo_arrow"），反推不出是哪种箭，故须显式带上。
    /// </param>
    public static Projectile Spawn(
        Node parent, Vector2 pos, Vector2 dir, float maxDist,
        Weapon weapon, CombatEngine combat, Actor shooter, float rangeMultiplier = 1f,
        string ammoKey = "", double damageFactor = 1.0)
    {
        var b = new Projectile
        {
            _dir = dir,
            _maxDist = maxDist,
            _speed = (float)weapon.FlightSpeed,   // [T68] 逐武器飞速（弓弩已含箭/书/改装的连乘）
            _weapon = weapon,
            _combat = combat,
            _shooter = shooter,
            _rangeMultiplier = rangeMultiplier,
            _ammoKey = ammoKey,
            _damageFactor = damageFactor,
            ZIndex = 50,
        };
        parent.AddChild(b);
        b.GlobalPosition = pos;
        return b;
    }

    public override void _PhysicsProcess(double delta)
    {
        // 吃已缩放 delta：暂停时冻结，加速档下飞行更快，与全局时间一致。
        float step = _speed * (float)delta;
        Vector2 from = GlobalPosition;
        Vector2 to = from + _dir * step;

        // 路径首碰撞查询（排除射手自身，避免出膛即自击）。
        // 架肩豁免：紧贴射手的同阵营队友被穿过——排除后对同一段重查，直到命中敌方/远处友军/墙或落空。
        var space = GetWorld2D().DirectSpaceState;
        var excluded = _excluded;
        excluded.Clear();
        excluded.Add(_shooter.GetRid());
        var query = _query ??= new PhysicsRayQueryParameters2D();
        query.From = from;
        query.To = to;
        query.CollisionMask = HitMask;
        while (true)
        {
            query.Exclude = excluded; // 排除表被下方架肩豁免/尸体分支就地追加后需重设，令重查生效
            global::Godot.Collections.Dictionary hit = space.IntersectRay(query);

            if (hit.Count == 0)
            {
                break; // 本步路径无碰撞，继续前进
            }

            var collider = hit["collider"].As<GodotObject>();
            if (collider is Actor victim)
            {
                if (!victim.Alive)
                {
                    // 尸体（理论上已 QueueFree，兜底）：穿过继续查。
                    excluded.Add(victim.GetRid());
                    continue;
                }

                bool hostile = Factions.IsHostile(_shooter.Faction, victim.Faction);
                float grace = _shooter.Radius + victim.Radius + ShoulderGraceMargin; // 空间几何参数：肩宽量级，当前值以 Wiki 配置为准
                double dist = victim.GlobalPosition.DistanceTo(_shooter.GlobalPosition);

                if (FriendlyFire.Resolve(hostile, dist, grace) == ProjectileContact.PassThrough)
                {
                    excluded.Add(victim.GetRid()); // 架肩豁免：穿过这个紧贴队友继续飞
                    continue;
                }

                // 距离衰减：按射手→命中者的间距缩放伤害（满伤段内=1，线性降到 MaxRange 处 FalloffFloor）。近战不经此路径。
                // 岗位 +射程：把间距压回原生曲线（dist/倍率），使整条衰减曲线随射程一同拉长（否则超原生 MaxRange 段变 0 伤）。
                double factor = Ballistics.RangedDamageFactor(
                    GuardPostMath.EffectiveRangeDistance(dist, _rangeMultiplier), _weapon);
                victim.ReceiveAttack(_shooter, _weapon, _combat, factor * _damageFactor, ranged: true);
            }

            // 命中体（敌方/远处友军/墙）即终止：命中已结算，撞墙则落空。
            Despawn();
            return;
        }

        Position += _dir * step;
        _traveled += step;
        if (_traveled >= _maxDist)
        {
            Despawn();
        }
    }

    /// <summary>
    /// 弹丸消亡（命中了什么 / 飞完了射程）——也就是"箭落地"的那一刻，回收判定在此发生。
    /// 子弹/霰弹一次性（打出去就没了），**箭可回收**：这是弓相对枪的核心优势。
    /// </summary>
    private void Despawn()
    {
        TryRecoverArrow();
        QueueFree();
    }

    /// <summary>
    /// 箭矢回收：一枚 Projectile ＝ 一支箭（弓弩 PelletCount=1），逐支独立掷一次
    /// （<see cref="Archery.RollArrowRecovery"/>）——捡得回来就把**那一种**箭还进射手的弹药源
    /// （玩家＝营地共享库存；敌方的无限源 Recover 是空实现，不受影响）。
    /// 回收的是 <see cref="_ammoKey"/>（具体箭种）而非 <c>_weapon.AmmoKey</c>（那是类别键"ammo_arrow"，
    /// 库存里根本没这种东西）——射出去的是碳纤维箭，捡回来的就得是碳纤维箭。
    /// 随机走引擎可注入的 <see cref="CombatEngine.Rng"/>，不用 Godot 的随机（保持可复现）。
    /// <para>
    /// <b>回收率取决于射手读没读过《弓与箭之道》</b>（用户拍板）：基础值与读书后的提升值以 Wiki 配置为准。
    /// 判据是射手**本人**的已读书集（<see cref="Pawn.HasReadBook"/>，与配方书门槛同一个对象）——
    /// 丧尸/劫掠者不是 <see cref="Pawn"/>，一律按未读算（它们本来也走无限弹药源，回收是空实现）。
    /// </para>
    /// <para>
    /// **弓弩不是免费远程**，它只是后勤压力小于枪；那本书降低箭矢损耗，于是它成了弓弩流的硬前置。
    /// </para>
    /// <para>
    /// 回收即时入库而非在地上生成可拾取物：本作没有"地面掉落物"这一层，且搜刮所得本就直接进营地共享库存
    /// （<c>LootApplication</c> 同款抽象）——语义是"打完这一仗，你把能捡的箭捡了回来"，捡不回的就是崩断的、
    /// 射进草丛找不着的、扎在颅骨里拔不出来的那些。
    /// </para>
    /// </summary>
    private void TryRecoverArrow()
    {
        if (!ArrowTable.IsArrow(_ammoKey) || !IsInstanceValid(_shooter))
        {
            return;
        }

        bool hasReadArcheryBook = _shooter is Pawn pawn && pawn.HasReadBook(BookLibrary.WayOfBowAndArrowId);

        int recovered = Archery.RollArrowRecovery(1, hasReadArcheryBook, _combat.Rng);
        _shooter.Ammo.Recover(_ammoKey, recovered);
    }

    public override void _Draw()
    {
        DrawLine(Vector2.Zero, -_dir * 9f, new Color(1f, 0.85f, 0.45f, 0.9f), 2f);
        DrawCircle(Vector2.Zero, 2.5f, new Color(1f, 0.95f, 0.6f));
    }
}
