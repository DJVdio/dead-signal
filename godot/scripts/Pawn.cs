using Godot;
using DeadSignal.Combat;

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

    /// <summary>
    /// 驻守途中（D 守卫防御战）：守卫正走向岗位站位。为 true 时 Guard 分支放行移动令
    /// （不当作杂散指令取消），抵达即自动清除、回到原地驻守。
    /// </summary>
    public bool Stationing { get; set; }

    /// <summary>
    /// 饥饿刻度状态机（见 <see cref="HungerState"/>，数值化 0-6）。全部规则（衰减/进食/上限/惩罚/士气）
    /// 归纯逻辑对象；Pawn 只持有并在昼夜切换/聚餐时驱动，并把能力惩罚经钩子喂给战斗消费点。
    /// 普通幸存者上限 5；"大胃袋"特质将来可传 6（本轮所有 Pawn 默认 5）。
    /// </summary>
    public HungerState Hunger { get; } = new HungerState();

    /// <summary>饥饿对战斗能力的惩罚（喂给 <see cref="Actor"/> 的钩子）。丧尸基类返回 0，此处返回饥饿净值。</summary>
    protected override double HungerAbilityPenalty => Hunger.AbilityPenalty;

    /// <summary>
    /// 一次昼夜相位聚餐净结算：无条件 -1，吃到饭再 +1（净零维持 / 净 -1 前进一级），一步 clamp。
    /// 避免旧两步"1→0 途中进食被短路"的跨 0 误杀。返回本次是否饿死（刻度归 0）。
    /// </summary>
    public bool ResolveHungerPhase(bool ate) => Hunger.ResolvePhase(ate);

    /// <summary>饥饿刻度已归 0（饿死）。由聚餐结算据此走统一死亡路径。</summary>
    public bool IsStarvedToDeath => Hunger.IsStarved;

    /// <summary>饿死：走统一非战斗死亡路径（触发 Died 事件 + 移出场，复用现有死亡消费）。</summary>
    public void StarveToDeath() => KillNonCombat();

    protected override void Think(double delta)
    {
        switch (Role)
        {
            case PawnRole.Sleeping:
                CancelOrders();
                break;
            case PawnRole.Guard:
                // 驻守途中放行移动令（走向岗位）；抵达即恢复原地驻守。非驻守时沿用原逻辑取消杂散移动令。
                if (Stationing && IsNavigationFinished())
                    Stationing = false;
                if (HasMoveOrder && !Stationing)
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
        PawnInspection.FromBody(Body, AttackWeapon, DefenderArmor, DisplayName, Hunger.Value, Hunger.Level.Label());

    /// <summary>
    /// 给某个空槽（被切除的手/腿）装一副某等级的成品假肢：本轮直接给（调试/掉落来源，不做制作/搜刮/交易链），
    /// 走已有的 <see cref="Body.AttachProsthetic"/> 恢复能力并即时重算净惩罚。返回装后新快照供面板刷新。
    /// </summary>
    /// <param name="replacesRegion">取代区域：<see cref="BodyRegion.Hand"/>=手（恢复操作）/ <see cref="BodyRegion.Leg"/>=腿（恢复移动）。</param>
    public PawnInspection EquipProsthetic(BodyRegion replacesRegion, ProstheticGrade grade)
    {
        Body.AttachProsthetic(Prosthetic.OfGrade(grade, replacesRegion, ProstheticDisplayName(grade)));
        return Inspect();
    }

    /// <summary>假肢等级中文显示名（木制/简易/仿生）。</summary>
    private static string ProstheticDisplayName(ProstheticGrade grade) => grade switch
    {
        ProstheticGrade.Wooden => "木制假肢",
        ProstheticGrade.Simple => "简易假肢",
        ProstheticGrade.Bionic => "仿生假肢",
        _ => "假肢",
    };
}
