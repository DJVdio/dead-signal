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
    private const int StructureHitDamage = 12; // 每爪砸墙伤害（拟定待调；数只丧尸合砸基础大门 250 数十秒破）

    private Rect2 _wanderBounds;
    private Func<IEnumerable<Actor>>? _survivorProvider;
    private double _wanderTimer;
    private bool _wasNight;
    private BreachController? _breach; // 门关闭后砸围栏/大门破防（袭营时由 CampMain 注入）
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
        z.AttackCooldown = z.AttackWeapon.AttackInterval; // 读 WeaponTable 权威间隔（爪击慢节奏 2.3s），敌方同步慢节奏
        z.DefenderArmor = CombatData.ZombieHide();
        return z;
    }

    protected override void OnReady() => _rng.Randomize();

    /// <summary>
    /// 袭营时注入破防能力（门关闭后到不了营内幸存者→走到最近围栏/大门前砸墙）。委托由 CampMain 提供
    /// （找最近结构 / 对结构施伤，结构类型不外泄）；<paramref name="campCenter"/> 作无目标时的可达性探测点。
    /// </summary>
    public void ConfigureBreach(
        BreachController.FindNearestStructure find,
        BreachController.DamageNearestStructure damage,
        Vector2 campCenter)
    {
        _breach = new BreachController(find, damage, campCenter,
            StructureHitDamage, AttackCooldown, attackReach: 34f + Radius, standoff: Radius + 8f);
    }

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

        // 破防：若被围栏/大门阻隔（到幸存者/营心无导航路径），走到最近结构前砸墙；接管本帧则不再追击/游荡。
        if (_breach != null && _breach.TryBreach(this, delta, nearest?.GlobalPosition))
        {
            return;
        }

        // 可达（或未配破防）：常规追击已侦测的幸存者。
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
