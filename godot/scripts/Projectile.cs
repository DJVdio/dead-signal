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
    private const float Speed = 560f;
    private float _maxDist;
    private float _traveled;
    private Weapon _weapon = null!;
    private CombatEngine _combat = null!;
    private Actor _shooter = null!;

    // 命中掩码：墙(层3=0b0100) + 幸存者(层1) + 丧尸(层2) + 劫掠者(层4=0b1000)——覆盖全阵营层，
    // 弹道能命中任意阵营；具体是否结算由下方 Factions.IsHostile + 架肩豁免裁定（异阵营命中、同阵营豁免）。
    private const uint HitMask = 0b1111;

    /// <summary>架肩豁免的额外间距余量（像素，拟定待调）。叠加在两者碰撞半径之和上。</summary>
    private const float ShoulderGraceMargin = 6f;

    public static Projectile Spawn(
        Node parent, Vector2 pos, Vector2 dir, float maxDist,
        Weapon weapon, CombatEngine combat, Actor shooter)
    {
        var b = new Projectile
        {
            _dir = dir,
            _maxDist = maxDist,
            _weapon = weapon,
            _combat = combat,
            _shooter = shooter,
            ZIndex = 50,
        };
        parent.AddChild(b);
        b.GlobalPosition = pos;
        return b;
    }

    public override void _PhysicsProcess(double delta)
    {
        // 吃已缩放 delta：暂停时冻结，加速档下飞行更快，与全局时间一致。
        float step = Speed * (float)delta;
        Vector2 from = GlobalPosition;
        Vector2 to = from + _dir * step;

        // 路径首碰撞查询（排除射手自身，避免出膛即自击）。
        // 架肩豁免：紧贴射手的同阵营队友被穿过——排除后对同一段重查，直到命中敌方/远处友军/墙或落空。
        var space = GetWorld2D().DirectSpaceState;
        var excluded = new global::Godot.Collections.Array<Rid> { _shooter.GetRid() };
        while (true)
        {
            var query = PhysicsRayQueryParameters2D.Create(from, to, HitMask);
            query.Exclude = excluded;
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
                float grace = _shooter.Radius + victim.Radius + ShoulderGraceMargin; // 拟定待调：肩宽量级
                double dist = victim.GlobalPosition.DistanceTo(_shooter.GlobalPosition);

                if (FriendlyFire.Resolve(hostile, dist, grace) == ProjectileContact.PassThrough)
                {
                    excluded.Add(victim.GetRid()); // 架肩豁免：穿过这个紧贴队友继续飞
                    continue;
                }

                // 距离衰减：按射手→命中者的间距缩放伤害（满伤段内=1，线性降到 MaxRange 处 FalloffFloor）。近战不经此路径。
                double factor = Ballistics.RangedDamageFactor(dist, _weapon);
                victim.ReceiveAttack(_shooter, _weapon, _combat, factor);
            }

            // 命中体（敌方/远处友军/墙）即终止：命中已结算，撞墙则落空。
            QueueFree();
            return;
        }

        Position += _dir * step;
        _traveled += step;
        if (_traveled >= _maxDist)
        {
            QueueFree();
        }
    }

    public override void _Draw()
    {
        DrawLine(Vector2.Zero, -_dir * 9f, new Color(1f, 0.85f, 0.45f, 0.9f), 2f);
        DrawCircle(Vector2.Zero, 2.5f, new Color(1f, 0.95f, 0.6f));
    }
}
