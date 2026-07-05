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

    /// <summary>防御方躯体（细部位 HP/切除/损毁/失血）。由子类工厂在 Create 时装配。</summary>
    protected Body Body = null!;

    /// <summary>存活 = 躯体未死（致死部位归零/斩首/开膛/失血致死皆算死）。</summary>
    public bool Alive => Body is { IsDead: false };

    protected Color BodyColor = Colors.White;
    protected float MoveSpeed = 90f;

    // ---- 战斗（作为防御方的护甲；作为攻击方的武器 + 手感参数） ----
    protected Weapon AttackWeapon = null!;
    protected float AttackRange = 32f;
    protected double AttackCooldown = 1.0;
    /// <summary>远程武器：攻击时发射锥形散布弹道子弹，而非近战瞬时结算。</summary>
    protected bool IsRanged;
    protected IReadOnlyList<ArmorLayer> DefenderArmor = Array.Empty<ArmorLayer>();

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

        // 持续失血推进（吃已缩放 delta：暂停冻结、加速档更快）。可能因失血致死。
        if (Body.BleedingWoundCount > 0)
        {
            Body.TickBleed(delta);
            QueueRedraw();
            if (!Alive)
            {
                Die();
                return;
            }
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
        Vector2 toTarget = target.GlobalPosition - GlobalPosition;
        if (toTarget == Vector2.Zero)
        {
            return;
        }

        // 误差角锥采样：以指向目标的准星方向为轴，在武器基础误差角内随机一个射击方向。
        // 命中判定交给弹道实时层（路径首个碰撞体，含撞墙落空）——引擎只出纯函数采样方向。
        double aimDeg = Mathf.RadToDeg(toTarget.Angle());
        double shotDeg = Combat.SampleShotDirectionDegrees(aimDeg, AttackWeapon.BaseSpreadDegrees);
        Vector2 dir = Vector2.FromAngle(Mathf.DegToRad((float)shotDeg));

        Projectile.Spawn(GetParent(), GlobalPosition + dir * (Radius + 2f), dir,
            AttackRange * 1.25f, AttackWeapon, Combat, this);
    }

    /// <summary>作为防御方承受一次攻击：用自身护甲跑逐层结算 + 效果结算，施加到自身躯体。近战与子弹共用。</summary>
    public void ReceiveAttack(Weapon weapon, CombatEngine combat)
    {
        if (!Alive)
        {
            return;
        }
        // 伤害/流血/切除/致死已在此调用内施加到 Body；下方仅做表现。
        AttackOutcome hit = combat.ResolveHit(weapon, DefenderArmor, Body);
        ShowOutcome(hit);

        if (!Alive)
        {
            Die();
        }
    }

    private void ShowOutcome(AttackOutcome hit)
    {
        Color txtColor;
        string text;
        if (hit.Blocked)
        {
            // 被甲挡下 —— 也给一条战报，体现"护甲不是绝对保险"的手感。
            text = $"挡下·{hit.PartName}";
            txtColor = new Color(0.75f, 0.78f, 0.85f);
        }
        else
        {
            string typeTag = hit.FinalType == DamageType.Blunt ? "钝" : "锐";
            string fx = hit.Severed ? " 断!" : hit.Fractured ? " 骨折" : hit.Concussed ? " 震荡" : hit.Bled ? " 流血" : "";
            text = $"-{hit.Damage} {hit.PartName}({typeTag}){fx}";
            txtColor = Faction == Faction.Survivor
                ? new Color(1f, 0.5f, 0.45f)
                : new Color(1f, 0.85f, 0.4f);
        }

        FloatingText.Spawn(GetParent(), GlobalPosition + new Vector2(0, -Radius - 10), text, txtColor);
        QueueRedraw();
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

        // 血条：取"部位血量比"与"储血比"的较小值——部位伤（打击/切除）与失血两条致死轴都能反映。
        float barW = Radius * 2.4f;
        float barH = 4f;
        var barPos = new Vector2(-barW / 2, -Radius - 12);
        float frac = (float)Mathf.Clamp(Mathf.Min(PartHealthRatio(), Body.BloodRatio), 0, 1);
        DrawRect(new Rect2(barPos, new Vector2(barW, barH)), new Color(0.1f, 0.1f, 0.1f, 0.85f));
        Color hpColor = frac > 0.5f ? new Color(0.4f, 0.85f, 0.4f)
            : frac > 0.25f ? new Color(0.9f, 0.8f, 0.3f)
            : new Color(0.9f, 0.35f, 0.3f);
        DrawRect(new Rect2(barPos, new Vector2(barW * frac, barH)), hpColor);
    }

    /// <summary>当前各部位 HP 之和 / 模板 MaxHp 之和（分母用模板上限，条长不受磨损影响而失真）。</summary>
    private double PartHealthRatio()
    {
        double cur = 0, max = 0;
        foreach (BodyPart p in Body.Parts.Values)
        {
            cur += Body.HpOf(p.Name);
            max += p.MaxHp;
        }
        return max > 0 ? cur / max : 0;
    }
}
