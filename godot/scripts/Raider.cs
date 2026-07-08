using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 劫掠者：携武器的人类敌人 AI 单位。侦测/追击/丢失/游荡的自主 <see cref="Think"/> 借鉴
/// <see cref="Zombie"/>，但两点不同：①人类昼夜皆活动，不受夜间休眠门约束；②攻击对象不写死"打幸存者"，
/// 而由**可运行时替换的候选池 <see cref="SetTargetProvider"/> + <see cref="Factions.IsHostile"/>** 裁定择敌。
///
/// 克莉丝汀战斗时就是一个 Raider 实例：起手 <c>Faction=Raider</c> 打幸存者；反水由编排层（块D）
/// 运行时 <see cref="Actor.SetFaction"/>(Survivor) + <see cref="SetTargetProvider"/>(劫掠者池) 整套改边——
/// 本类只负责让这两个运行时改动即刻生效，不含触发/生成/抉择编排（那是块D/C）。
///
/// 攻击复用 <see cref="Actor"/> 既有路径：远程走锥形散布弹道（<c>IsRanged</c>+FireProjectile），
/// 近战走 ReceiveAttack；命中/友伤由弹道层经 <see cref="Factions.IsHostile"/> 裁定，本类不自造战斗。
/// </summary>
public sealed partial class Raider : Actor
{
    private const float DetectionRadius = 300f;   // 人类警觉半径（比丧尸略大：主动进攻方）
    private const float LoseTargetRadius = 420f;   // 追出此半径即放弃目标
    private const int StructureHitDamage = 25;    // 每次砸墙伤害（拟定待调；人类比丧尸更快破门。远近程一律贴身砸）

    private Rect2 _wanderBounds;
    private Func<IEnumerable<Actor>>? _targetProvider;
    private double _wanderTimer;
    private BreachController? _breach; // 门关闭后砸围栏/大门破防（袭营时由 CampMain 注入）
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>战斗日志/检视显示名（克莉丝汀等具名劫掠者用）；默认"劫掠者"。</summary>
    public string DisplayName { get; private set; } = "劫掠者";

    /// <summary>
    /// 工厂：仿 <see cref="Zombie.Create"/> 风格，供块D 脚本化生成。
    /// </summary>
    /// <param name="wanderBounds">无目标时的游荡巡场范围（cartesian）。</param>
    /// <param name="targetProvider">择敌候选池（返回可能的攻击对象；本类再按 IsHostile+侦测半径挑最近敌对者）。</param>
    /// <param name="usePistol">true=手枪远程；false=匕首近战。</param>
    /// <param name="displayName">战斗日志显示名（克莉丝汀传其名）。</param>
    public static Raider Create(
        Rect2 wanderBounds,
        Func<IEnumerable<Actor>> targetProvider,
        bool usePistol = true,
        string displayName = "劫掠者")
    {
        var r = new Raider
        {
            DisplayName = displayName,
            BodyColor = new Color(0.72f, 0.26f, 0.22f), // 暗红：与幸存者（自定义色）、丧尸（绿）一眼区分
            _wanderBounds = wanderBounds,
            _targetProvider = targetProvider,
        };
        r.Faction = Faction.Raider;
        r.Radius = 12f;
        r.MoveSpeed = 92f;
        r.Body = CombatData.NewHumanoidBody();
        r.DefenderArmor = CombatData.SurvivorArmor(); // 人类：外套 + 贴身布衣两层

        if (usePistol)
        {
            r.AttackWeapon = CombatData.Pistol();
            r.AttackRange = 240f;   // 中距离（略短于玩家手枪，拟定待调）
            r.AttackCooldown = 1.2;
            r.IsRanged = true;      // 锥形散布弹道（误差角来自武器 BaseSpreadDegrees）
        }
        else
        {
            r.AttackWeapon = CombatData.Dagger();
            r.AttackRange = 26f;    // 近战
            r.AttackCooldown = 0.8;
        }
        return r;
    }

    protected override void OnReady() => _rng.Randomize();

    /// <summary>
    /// 运行时替换择敌候选池（克莉丝汀反水编排用）：只换"从哪群 Actor 里挑敌人"，不动攻防逻辑。
    /// 与 <see cref="Actor.SetFaction"/> 配合即可整套改边——但即便只切阵营不换池，<see cref="FindNearestHostile"/>
    /// 也会因 IsHostile 结果翻转而自动改打对象。节点未入树时调用也安全。
    /// </summary>
    public void SetTargetProvider(Func<IEnumerable<Actor>> provider) => _targetProvider = provider;

    /// <summary>
    /// 袭营时注入破防能力（门关闭后到不了营内目标→走到最近围栏/大门前砸墙）。委托由 CampMain 提供
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
        // 追击目标维护：目标死亡（基类每帧会清空）或跑出丢失半径则放弃，转入重新侦测/游荡。
        Actor? tgt = CurrentAttackTarget;
        if (tgt is { Alive: true })
        {
            if (GlobalPosition.DistanceTo(tgt.GlobalPosition) > LoseTargetRadius)
            {
                CancelOrders();
            }
            else
            {
                return; // 继续追（Actor 基类负责逼近 + 射击/近战）
            }
        }

        // 侦测最近的敌对单位。阵营由 Factions.IsHostile 裁定 —— 反水切阵营后自动改打对象。
        Actor? nearest = FindNearestHostile();

        // 破防：若被围栏/大门阻隔（到目标/营心无导航路径），走到最近结构前砸墙；接管本帧则不再追击/游荡。
        if (_breach != null && _breach.TryBreach(this, delta, nearest?.GlobalPosition))
        {
            return;
        }

        // 可达（或未配破防）：常规追击已侦测的敌对单位。
        if (nearest != null)
        {
            CommandAttack(nearest);
            return;
        }

        // 游荡巡场：到点或超时换一个随机目标点。
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

    /// <summary>
    /// 在 <see cref="_targetProvider"/> 给出的候选池里挑侦测半径内最近的**敌对**单位
    /// （敌我一律走 <see cref="Factions.IsHostile"/>，不散写阵营相等比较）。因此：块D 只要 SetFaction 即改变敌我，
    /// 或 SetTargetProvider 换候选池，二者皆运行时立即生效——这正是克莉丝汀反水所需的两个改动。
    /// </summary>
    private Actor? FindNearestHostile()
    {
        if (_targetProvider == null)
        {
            return null;
        }
        Actor? best = null;
        float bestDist = DetectionRadius;
        foreach (Actor a in _targetProvider())
        {
            if (!a.Alive || !Factions.IsHostile(Faction, a.Faction))
            {
                continue;
            }
            float d = GlobalPosition.DistanceTo(a.GlobalPosition);
            if (d < bestDist)
            {
                bestDist = d;
                best = a;
            }
        }
        return best;
    }
}
