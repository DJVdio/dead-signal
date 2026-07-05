using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 直线弹道子弹（占位）。发射瞬间朝目标方向锁定方向，沿直线匀速飞行，
/// 命中目标即调 CombatResolver 结算，飞出最大射程则落空自毁。
///
/// 这是几何弹道模型的占位骨架：当前无散布/无命中率/不制导。等 DeadSignal.Combat 落地
/// 误差角锥采样后，把发射方向换成锥内采样、命中判定改为路径首个碰撞体即可平滑升级。
/// </summary>
public sealed partial class Projectile : Node2D
{
    private Vector2 _dir;
    private const float Speed = 560f;
    private float _maxDist;
    private float _traveled;
    private Weapon _weapon = null!;
    private CombatEngine _combat = null!;
    private Actor? _target;

    public static Projectile Spawn(
        Node parent, Vector2 pos, Vector2 dir, float maxDist,
        Weapon weapon, CombatEngine combat, Actor target)
    {
        var b = new Projectile
        {
            _dir = dir,
            _maxDist = maxDist,
            _weapon = weapon,
            _combat = combat,
            _target = target,
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
        Position += _dir * step;
        _traveled += step;

        if (_target != null && GodotObject.IsInstanceValid(_target) && _target.Alive
            && GlobalPosition.DistanceTo(_target.GlobalPosition) <= _target.Radius + 4f)
        {
            _target.ReceiveAttack(_weapon, _combat);
            QueueFree();
            return;
        }

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
