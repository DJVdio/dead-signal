using System;
using System.Collections.Generic;
using System.Linq;
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
    // 白昼满档视距 R0（作日间正面警觉，比丧尸略大：主动进攻方）。人类昼夜皆活动：白昼环境光满档→视距≈300、
    // 半角 60°；夜间环境光≈0.15→视距≈134、半角≈34.5°，夜间对潜行明显收紧（与丧尸夜锥同规则）。数值拟定待调。
    private const float BaseSightRange = 300f;
    private const int StructureHitDamage = 25;    // 每次砸墙伤害（拟定待调；人类比丧尸更快破门。远近程一律贴身砸）

    // 战斗时掏火把（用户口径：被发现正式开战后才拿出光源，潜行阶段不持光）。火把强度读 LightSource 目录（拟定待调，缺则 0.7）。
    // 效果：进入战斗态→SetCarriedLight(火把强度)：①自照亮提升本体局部光照→ConeFor 视锥变大（夜战视野恢复）；
    // ②成持光信标→他人 ExposedCone 读本体 CarriedLightIntensity 放大对己视距（暴露代价对己生效）。脱战即收火把归 0。
    private static readonly float TorchLightIntensity =
        LightSource.Find(LightSource.TorchKey)?.Intensity ?? 0.7f;

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
            r.AttackCooldown = r.AttackWeapon.AttackInterval; // 读 WeaponTable 权威间隔（手枪慢节奏 2.5s），敌方同步慢节奏
            r.IsRanged = true;      // 锥形散布弹道（误差角来自武器 BaseSpreadDegrees）
        }
        else
        {
            r.AttackWeapon = CombatData.Dagger();
            r.AttackRange = 26f;    // 近战
            r.AttackCooldown = r.AttackWeapon.AttackInterval; // 读 WeaponTable 权威间隔（匕首慢节奏 1.4s）
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
        // 目标获取/丢失：锥形+光照+遮挡感知（raycast 贵，节流重算；其间维持现指令，基类照常逼近/射击/近战）。
        // 候选=IsHostile 过滤后的敌对池——反水切阵营/换池后自动改打对象（克莉丝汀反水所需的运行时改边不变）。
        if (PerceptionDue(delta))
        {
            UpdatePerception(HostileCandidates(), BaseSightRange);
        }

        // 战斗态掏/收火把：有存活目标=已开战→点火把（自照亮回视野+成暴露信标）；脱战→收火把归 0。每帧据现目标切，幂等便宜。
        // TODO（待 light-items LightField 就绪）：火把还应作为 PlacedLight 并入场光源，照亮劫掠者周边（利于它与友军看清，也被玩家看见）；
        //   现仅实现"照亮自身位置"（本体视野+暴露），周边照明留待 ConfigurePerception 光源场接线时补。
        SetCarriedLight(CurrentAttackTarget is { Alive: true } ? TorchLightIntensity : 0f);

        // 破防：每帧真 delta（TryBreach 内部自带 0.3s 可达节流）。被围栏/大门阻隔则走到最近结构前砸墙；接管本帧则不再追击/游荡。
        if (_breach != null && _breach.TryBreach(this, delta, CurrentAttackTarget?.GlobalPosition))
        {
            return;
        }

        // 可达且有已感知目标：交基类逼近+攻击。
        if (CurrentAttackTarget is { Alive: true })
        {
            return;
        }

        // 游荡巡场（含丢失视野后走向最后目击点的一次侦查 move）：到点或超时换一个随机目标点。
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
    /// 候选敌对池：<see cref="_targetProvider"/> 输出经 <see cref="Factions.IsHostile"/> + 存活过滤
    /// （敌我一律走 IsHostile，不散写阵营相等比较）。感知锥/遮挡由基类 <see cref="Actor.PerceiveNearest"/> 再裁。
    /// </summary>
    private IEnumerable<Actor> HostileCandidates()
        => _targetProvider is null
            ? Array.Empty<Actor>()
            : _targetProvider().Where(a => a.Alive && Factions.IsHostile(Faction, a.Faction));
}
