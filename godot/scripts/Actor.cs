using System;
using System.Collections.Generic;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

public enum Faction
{
    Survivor,
    Zombie,
}

/// <summary>
/// 幸存者与丧尸的共同基类：一个带寻路的圆形色块小人，头顶血条，能被结算伤害。
/// 视觉全部走 <see cref="_Draw"/>（圆身 + 血条 + 选中环），不依赖任何外部素材。
/// 移动走 NavigationAgent2D 寻路 + CharacterBody2D 物理碰撞（撞墙）。
/// </summary>
public abstract partial class Actor : CharacterBody2D
{
    // ---- 基础属性 ----
    public Faction Faction { get; protected set; }
    public float Radius { get; protected set; } = 12f;
    public double MaxHp { get; protected set; } = 100;
    public double Hp { get; protected set; } = 100;
    public bool Alive => Hp > 0;
    protected Color BodyColor = Colors.White;
    protected float MoveSpeed = 90f;

    // ---- 战斗（作为防御方的护甲/部位；作为攻击方的武器 + 手感参数） ----
    protected Weapon AttackWeapon = null!;
    protected float AttackRange = 32f;
    protected double AttackCooldown = 1.0;
    /// <summary>远程武器：攻击时发射直线弹道子弹（占位），而非近战瞬时结算。</summary>
    protected bool IsRanged;
    protected IReadOnlyList<ArmorLayer> DefenderArmor = Array.Empty<ArmorLayer>();
    protected IReadOnlyList<BodyPart> DefenderParts = Array.Empty<BodyPart>();

    private double _attackTimer;

    // ---- 指令目标 ----
    private Vector2? _moveTarget;
    private Actor? _attackTarget;

    // ---- 依赖（由 Main 注入） ----
    protected CombatEngine Combat = null!;
    protected GameClock Clock = null!;

    private NavigationAgent2D _agent = null!;

    public event Action<Actor>? Died;

    public void Inject(CombatEngine combat, GameClock clock)
    {
        Combat = combat;
        Clock = clock;
    }

    public override void _Ready()
    {
        // 碰撞体（撞墙）。
        var shape = new CollisionShape2D { Shape = new CircleShape2D { Radius = Radius } };
        AddChild(shape);

        _agent = new NavigationAgent2D
        {
            PathDesiredDistance = 4f,
            TargetDesiredDistance = 6f,
            AvoidanceEnabled = false,
        };
        AddChild(_agent);

        // 供点选命中检测（右键点敌人）：整个圆作为可点区域用碰撞层区分。
        CollisionLayer = Faction == Faction.Survivor ? 0b0001u : 0b0010u;
        CollisionMask = 0b0100u; // 只与墙（层 3）碰撞，彼此穿过避免挤堆。

        OnReady();
    }

    /// <summary>子类初始化钩子（在基类节点搭好后调用）。</summary>
    protected virtual void OnReady() { }

    // ---- 指令接口 ----

    public void CommandMoveTo(Vector2 worldPos)
    {
        _attackTarget = null;
        _moveTarget = worldPos;
        _agent.TargetPosition = worldPos;
    }

    public void CommandAttack(Actor target)
    {
        if (target == this || !target.Alive)
        {
            return;
        }
        _attackTarget = target;
        _moveTarget = null;
    }

    /// <summary>撤销一切指令（丧尸白天休眠时用）。</summary>
    public void CancelOrders()
    {
        _attackTarget = null;
        _moveTarget = null;
        Velocity = Vector2.Zero;
    }

    protected Actor? CurrentAttackTarget => _attackTarget;
    protected bool HasMoveOrder => _moveTarget.HasValue;

    // ---- 每帧决策：子类填 AI（丧尸游荡/追击；幸存者留空由玩家指令驱动） ----
    protected virtual void Think(double delta) { }

    public override void _PhysicsProcess(double delta)
    {
        if (!Alive)
        {
            return;
        }

        Think(delta);

        if (_attackTimer > 0)
        {
            _attackTimer -= delta;
        }

        // 有攻击目标：目标死了则清空；在射程内停下开打，否则寻路逼近。
        if (_attackTarget is { } tgt)
        {
            if (!tgt.Alive)
            {
                _attackTarget = null;
            }
            else
            {
                float dist = GlobalPosition.DistanceTo(tgt.GlobalPosition);
                if (dist <= AttackRange + Radius + tgt.Radius)
                {
                    Velocity = Vector2.Zero;
                    MoveAndSlide();
                    TryAttack(tgt);
                    QueueRedraw();
                    return;
                }
                _agent.TargetPosition = tgt.GlobalPosition;
            }
        }

        // 寻路移动。
        if (NavigationServer2D.MapGetIterationId(_agent.GetNavigationMap()) == 0)
        {
            return;
        }
        if (_attackTarget == null && !_moveTarget.HasValue)
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            return;
        }
        if (_agent.IsNavigationFinished())
        {
            Velocity = Vector2.Zero;
            MoveAndSlide();
            _moveTarget = null;
            return;
        }

        Vector2 next = _agent.GetNextPathPosition();
        Vector2 dir = GlobalPosition.DirectionTo(next);
        Velocity = dir * MoveSpeed;
        MoveAndSlide();
    }

    private void TryAttack(Actor target)
    {
        if (_attackTimer > 0)
        {
            return;
        }
        _attackTimer = AttackCooldown;

        if (IsRanged)
        {
            FireProjectile(target);
        }
        else
        {
            target.ReceiveAttack(AttackWeapon, Combat);
        }
    }

    private void FireProjectile(Actor target)
    {
        Vector2 dir = (target.GlobalPosition - GlobalPosition).Normalized();
        if (dir == Vector2.Zero)
        {
            return;
        }
        // 占位口径：直线弹道，发射瞬间朝目标方向锁定，之后不制导、无散布、无命中率。
        // TODO(锥形弹道): 待 DeadSignal.Combat 落地误差角锥采样后，把 dir 换成锥内采样方向，
        // 并把命中判定从"追踪指定 target"改为"路径上首个碰撞的 body"（含撞墙落空）。
        Projectile.Spawn(GetParent(), GlobalPosition + dir * (Radius + 2f), dir,
            AttackRange * 1.25f, AttackWeapon, Combat, target);
    }

    /// <summary>作为防御方承受一次攻击：用自身护甲/部位跑逐层结算并施加结果。近战与子弹共用。</summary>
    public void ReceiveAttack(Weapon weapon, CombatEngine combat)
    {
        if (!Alive)
        {
            return;
        }
        AttackOutcome hit = combat.ResolveHit(weapon, DefenderArmor, DefenderParts);
        ApplyDamage(hit);
    }

    private void ApplyDamage(AttackOutcome hit)
    {
        Color txtColor;
        string text;
        if (hit.Damage <= 0)
        {
            // 被甲挡下 —— 也给一条战报，体现"护甲不是绝对保险"的手感。
            text = $"挡下·{hit.PartName}";
            txtColor = new Color(0.75f, 0.78f, 0.85f);
        }
        else
        {
            Hp = Math.Max(0, Hp - hit.Damage);
            string typeTag = hit.FinalType == DamageType.Blunt ? "钝" : "锐";
            text = $"-{hit.Damage} {hit.PartName}({typeTag})";
            txtColor = Faction == Faction.Survivor
                ? new Color(1f, 0.5f, 0.45f)
                : new Color(1f, 0.85f, 0.4f);
        }

        FloatingText.Spawn(GetParent(), GlobalPosition + new Vector2(0, -Radius - 10), text, txtColor);
        QueueRedraw();

        if (!Alive)
        {
            Die();
        }
    }

    private void Die()
    {
        Died?.Invoke(this);
        QueueFree();
    }

    // ---- 表现 ----
    public bool Selected { get; set; }

    public override void _Draw()
    {
        // 选中环（仅幸存者被选时）。
        if (Selected)
        {
            DrawArc(Vector2.Zero, Radius + 5, 0, Mathf.Tau, 32, new Color(0.4f, 1f, 0.5f), 2f, true);
        }

        // 身体。
        DrawCircle(Vector2.Zero, Radius, BodyColor);
        DrawArc(Vector2.Zero, Radius, 0, Mathf.Tau, 24, new Color(0, 0, 0, 0.6f), 1.5f, true);

        // 血条。
        float barW = Radius * 2.4f;
        float barH = 4f;
        var barPos = new Vector2(-barW / 2, -Radius - 12);
        float frac = MaxHp > 0 ? (float)(Hp / MaxHp) : 0f;
        DrawRect(new Rect2(barPos, new Vector2(barW, barH)), new Color(0.1f, 0.1f, 0.1f, 0.85f));
        Color hpColor = frac > 0.5f ? new Color(0.4f, 0.85f, 0.4f)
            : frac > 0.25f ? new Color(0.9f, 0.8f, 0.3f)
            : new Color(0.9f, 0.35f, 0.3f);
        DrawRect(new Rect2(barPos, new Vector2(barW * frac, barH)), hpColor);
    }
}
