using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 袭营敌人「砸墙破防」的共用行为（Zombie/Raider 各持一个实例，在 Think 里调用 <see cref="TryBreach"/>）。
///
/// 背景：营地四面围栏围合、大门默认关闭（皆实心带 HP），门外生成的敌人**没有到营内幸存者的导航路径**。
/// 本控制器让敌人在被阻隔时走到最近的围栏/大门前、按攻击节奏砸墙；某段结构 HP 归零→导航开口→
/// 敌人下一次可达性探测即转为常规追击，穿破口涌入。
///
/// 分工：可达性判定走 <see cref="Actor.CanReach"/>（只读寻路查询）；几何/决策走纯逻辑 <see cref="BreachLogic"/>；
/// 「找最近结构 / 对结构施伤」是 <c>CampMain</c> 私有结构体系，经注入委托暴露（不外泄结构类型）。
/// 结构伤害不经 <see cref="Actor"/> 的战斗结算（结构非 Actor），故不动战斗定稿。
/// </summary>
public sealed class BreachController
{
    /// <summary>找最近未毁结构：在 <paramref name="radius"/> 内按边缘距离取最近者，输出其最近边缘点与边缘距离；无则 false。</summary>
    public delegate bool FindNearestStructure(Vector2 from, float radius, out Vector2 edgePoint, out float edgeDistance);

    /// <summary>对 <paramref name="from"/> 附近 <paramref name="radius"/> 内最近未毁结构施加 <paramref name="amount"/> 伤害，返回是否本击摧毁。</summary>
    public delegate bool DamageNearestStructure(Vector2 from, float radius, int amount);

    private const float SearchRadius = 320f;  // 门外找可砸结构的搜索半径（拟定待调）
    private const float DamageRadius = 72f;    // 砸墙时判定「贴着哪个结构」的半径（拟定待调）
    private const double ProbeInterval = 0.3;  // 可达性重算节流（避免每帧 A*；结构摧毁后最多 0.3s 转追击）

    private readonly FindNearestStructure _find;
    private readonly DamageNearestStructure _damage;
    private readonly Vector2 _fallbackObjective; // 未侦测到幸存者时的可达性探测点（营心）
    private readonly int _hitDamage;             // 每次砸墙伤害（拟定待调，取敌人类型常量）
    private readonly double _hitCooldown;        // 砸墙节奏（复用敌人攻击冷却）
    private readonly float _attackReach;         // 够得着结构的距离（≈近战边缘，不管远近程一律贴身砸）
    private readonly float _standoff;            // 贴近时停在结构外沿多远

    private double _hitTimer;
    private double _probeTimer;
    private bool _blocked;

    public BreachController(
        FindNearestStructure find,
        DamageNearestStructure damage,
        Vector2 fallbackObjective,
        int hitDamage,
        double hitCooldown,
        float attackReach,
        float standoff)
    {
        _find = find;
        _damage = damage;
        _fallbackObjective = fallbackObjective;
        _hitDamage = hitDamage;
        _hitCooldown = hitCooldown;
        _attackReach = attackReach;
        _standoff = standoff;
    }

    /// <summary>
    /// 尝试接管本帧：若敌人到目标（幸存者，或营心）不可达（被结构阻隔）则走到最近结构前砸墙，返回 <c>true</c>
    /// （敌人应 return、不要再自行追击/游荡）；可达则返回 <c>false</c>（交回常规追击/游荡）。
    /// </summary>
    /// <param name="self">敌人自身。</param>
    /// <param name="delta">帧时长。</param>
    /// <param name="survivorPos">已侦测到的追击目标位置；无则传 null（改用营心探可达性）。</param>
    public bool TryBreach(Actor self, double delta, Vector2? survivorPos)
    {
        Vector2 probe = survivorPos ?? _fallbackObjective;

        _probeTimer -= delta;
        if (_probeTimer <= 0)
        {
            _probeTimer = ProbeInterval;
            _blocked = !self.CanReach(probe);
        }

        if (!_blocked)
        {
            _hitTimer = 0;
            return false; // 可达：交回常规追击/游荡
        }

        // 被阻隔：找最近围栏/大门。找不到（异常）就不接管，让敌人自行朝目标挪。
        if (!_find(self.GlobalPosition, SearchRadius, out Vector2 edge, out float edgeDist))
        {
            return false;
        }

        if (BreachLogic.Decide(edgeDist, _attackReach) == BreachAction.Hammer)
        {
            // 够得着：站定砸墙。CancelOrders 清指令+零速，敌人贴墙不动、按节奏施伤。
            self.CancelOrders();
            _hitTimer -= delta;
            if (_hitTimer <= 0)
            {
                _hitTimer = _hitCooldown;
                _damage(self.GlobalPosition, DamageRadius, _hitDamage);
                // 摧毁后不必特判：下次可达性探测（≤0.3s）会转 false，敌人自然穿破口追击。
            }
        }
        else
        {
            // 够不着：寻路到结构外沿的攻击站位。
            (double ax, double ay) = BreachLogic.ApproachPoint(
                self.GlobalPosition.X, self.GlobalPosition.Y, edge.X, edge.Y, _standoff);
            self.CommandMoveTo(new Vector2((float)ax, (float)ay));
        }
        return true;
    }
}
