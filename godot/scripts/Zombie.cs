using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 丧尸：白天原地休眠不动；夜晚游荡，发现幸存者后追击并近战爪击（钝器）。
/// AI 全在 <see cref="Think"/> 内，靠 <see cref="GameClock.IsNight"/> 切换行为。
/// </summary>
public sealed partial class Zombie : Actor
{
    private const float DetectionRadius = 220f;
    private const float LoseTargetRadius = 320f;

    private Rect2 _wanderBounds;
    private Func<IEnumerable<Actor>>? _survivorProvider;
    private double _wanderTimer;
    private bool _wasNight;
    private readonly RandomNumberGenerator _rng = new();

    public static Zombie Create(Rect2 wanderBounds, Func<IEnumerable<Actor>> survivorProvider)
    {
        var z = new Zombie
        {
            BodyColor = new Color(0.45f, 0.6f, 0.35f),
            _wanderBounds = wanderBounds,
            _survivorProvider = survivorProvider,
        };
        z.Faction = Faction.Zombie;
        z.Radius = 13f;
        z.MoveSpeed = 55f;
        z.Body = CombatData.NewHumanoidBody();
        z.AttackWeapon = CombatData.ZombieClaw();
        z.AttackRange = 24f;
        z.AttackCooldown = 1.3;
        z.DefenderArmor = CombatData.ZombieHide();
        return z;
    }

    protected override void OnReady() => _rng.Randomize();

    protected override void Think(double delta)
    {
        bool night = Clock.IsNight;

        // 昼夜切换的边沿处理。
        if (night != _wasNight)
        {
            _wasNight = night;
            if (!night)
            {
                // 入昼：休眠，站住不动。
                CancelOrders();
                BodyColor = new Color(0.4f, 0.5f, 0.32f); // 休眠偏暗（ActorSprite 每帧取 BodyTint 自动跟随）
            }
            else
            {
                BodyColor = new Color(0.5f, 0.68f, 0.38f); // 夜间活跃偏亮
            }
        }

        if (!night)
        {
            return; // 白天休眠
        }

        // 追击目标维护。
        Actor? tgt = CurrentAttackTarget;
        if (tgt is { Alive: true })
        {
            if (GlobalPosition.DistanceTo(tgt.GlobalPosition) > LoseTargetRadius)
            {
                CancelOrders();
            }
            else
            {
                return; // 继续追（Actor 基类负责逼近+攻击）
            }
        }

        // 侦测最近幸存者。
        Actor? nearest = FindNearestSurvivor();
        if (nearest != null)
        {
            CommandAttack(nearest);
            return;
        }

        // 游荡：到点或超时就换一个随机目标点。
        _wanderTimer -= delta;
        if (!HasMoveOrder || _wanderTimer <= 0)
        {
            _wanderTimer = _rng.RandfRange(2.5f, 5.0f);
            var p = new Vector2(
                _rng.RandfRange(_wanderBounds.Position.X, _wanderBounds.End.X),
                _rng.RandfRange(_wanderBounds.Position.Y, _wanderBounds.End.Y));
            CommandMoveTo(p);
        }
    }

    private Actor? FindNearestSurvivor()
    {
        if (_survivorProvider == null)
        {
            return null;
        }
        Actor? best = null;
        float bestDist = DetectionRadius;
        foreach (Actor s in _survivorProvider())
        {
            if (!s.Alive)
            {
                continue;
            }
            float d = GlobalPosition.DistanceTo(s.GlobalPosition);
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
            }
        }
        return best;
    }
}
