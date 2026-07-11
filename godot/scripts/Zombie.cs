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
    // 白昼满档视距 R0。丧尸仅夜间活动（环境光≈NightAmbient 0.15 → ConeFor 视距系数≈0.4475、半角≈34.5°）：
    // 490×0.4475≈219 使**夜间前向视距≈旧半径 220**，即由"半径雷达"变"等距但有朝向+可被墙/暗处遮挡"的锥形——
    // 保留正面威胁、潜行从侧后/掩体/暗处自然涌现，而非单纯削弱。数值拟定待调，交 Sim/用户校准。
    private const float BaseSightRange = 490f;
    // 嗅觉兜底（用户拍板）：短程全向感知半径，补锥形"侧后死角"被无脑绕过。夜间贴到 70px 内且同房间（未被墙隔）即闻到，
    // 无视朝向/半角/光照（气味不吃暗）。半径拟定待调（用户口径 60~80px 取中）；穿墙=否（复用遮挡判定，待用户确认）。
    private const float SmellSenseRadius = 70f;
    private const int StructureHitDamage = 12; // 每爪砸墙伤害（拟定待调；数只丧尸合砸基础大门 250 数十秒破）

    /// <summary>丧尸嗅觉兜底半径（覆盖基类 0）。见 <see cref="SmellSenseRadius"/>。</summary>
    protected override float SmellRadius => SmellSenseRadius;

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

        // 目标获取/丢失：锥形+光照+遮挡感知（raycast 贵，节流重算；其间维持现指令，基类照常逼近/攻击）。
        if (PerceptionDue(delta))
        {
            UpdatePerception(_survivorProvider?.Invoke() ?? Array.Empty<Actor>(), BaseSightRange);
        }

        // 破防：每帧真 delta（TryBreach 内部自带 0.3s 可达节流，不可外层节流否则砸墙节奏失真）。
        // 被围栏/大门阻隔则走到最近结构前砸墙；接管本帧则不再追击/游荡。
        if (_breach != null && _breach.TryBreach(this, delta, CurrentAttackTarget?.GlobalPosition))
        {
            return;
        }

        // 可达且有已感知目标：交基类逼近+攻击。
        if (CurrentAttackTarget is { Alive: true })
        {
            return;
        }

        // 无目标：游荡（含丢失视野后走向最后目击点的一次侦查 move——由 UpdatePerception 下达，到点/超时后此处恢复随机游荡）。
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
}
