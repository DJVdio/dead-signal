using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 幸存者：完全由玩家指令驱动（左键选中、右键移动/攻击），无自主 AI。
/// 两名幸存者一个持手枪（中距离）、一个持匕首（近战），通过工厂参数区分。
/// </summary>
public sealed partial class Pawn : Actor
{
    private static int _nextId;
    public int Id { get; } = _nextId++;
    public string DisplayName { get; private set; } = "幸存者";

    public PawnRole Role { get; set; } = PawnRole.Idle;
    public bool IsControllable => Role == PawnRole.Idle;
    protected override bool CanAct => Role != PawnRole.Sleeping;

    /// <summary>当前饥饿级别（见 <see cref="HungerLevel"/>）。本版只记状态，各级效果 TODO 后续。</summary>
    public HungerLevel Hunger { get; private set; } = HungerLevel.Sated;

    /// <summary>
    /// 本餐吃到饭：向正常方向恢复一级（对称于错餐前进一级；具体恢复速率拟定待调，
    /// 亦可改为直接回正常）。<see cref="HungerLevel.Starved"/> 为终态，进食不复活。
    /// </summary>
    public void Feed()
    {
        if (Hunger == HungerLevel.Starved)
            return; // 饿死是终态，不因进食恢复
        if (Hunger > HungerLevel.Sated)
            Hunger -= 1;
    }

    /// <summary>
    /// 本餐没吃上饭：向饿死方向前进一级（封顶 <see cref="HungerLevel.Starved"/>）。
    /// 本版只推进状态，不接具体掉血/致死后果（TODO: 饥饿各级实际效果后续）。
    /// </summary>
    public void Starve()
    {
        if (Hunger < HungerLevel.Starved)
            Hunger += 1;
    }

    protected override void Think(double delta)
    {
        switch (Role)
        {
            case PawnRole.Sleeping:
                CancelOrders();
                break;
            case PawnRole.Guard:
                if (HasMoveOrder)
                    CancelOrders();
                if (CurrentAttackTarget is { Alive: true } tgt)
                {
                    float dist = GlobalPosition.DistanceTo(tgt.GlobalPosition);
                    if (dist > AttackRange + Radius + tgt.Radius)
                        CancelOrders();
                }
                break;
        }
    }

    public static Pawn Create(string name, bool usePistol, Color color)
    {
        var p = new Pawn
        {
            DisplayName = name,
            BodyColor = color,
        };
        p.Faction = Faction.Survivor;
        p.Radius = 12f;
        p.MoveSpeed = 95f;
        p.Body = CombatData.NewHumanoidBody();
        p.DefenderArmor = CombatData.SurvivorArmor();

        if (usePistol)
        {
            p.AttackWeapon = CombatData.Pistol();
            p.AttackRange = 260f;   // 中距离
            p.AttackCooldown = 1.1;
            p.IsRanged = true;      // 锥形散布弹道（误差角来自武器 BaseSpreadDegrees）
        }
        else
        {
            p.AttackWeapon = CombatData.Dagger();
            p.AttackRange = 26f;    // 近战
            p.AttackCooldown = 0.7;
        }
        return p;
    }

    /// <summary>
    /// 拍一份只读检视快照给"角色面板 UI"读取。内部就地读自身 Body/AttackWeapon/DefenderArmor
    /// （皆为受保护的可变引擎对象），构造纯数据 <see cref="PawnInspection"/> —— UI 只拿死数据、改不坏战斗。
    /// </summary>
    public PawnInspection Inspect() =>
        PawnInspection.FromBody(Body, AttackWeapon, DefenderArmor, DisplayName);
}
