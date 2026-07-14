using System;
using System.Collections.Generic;
using DeadSignal.Combat; // IRandomSource（穿衣抽取走可注入随机源）
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
    // 单一真源在 NoiseLogic：这个 70 是**整张噪音表的锚点**（走路 40 / 撬锁 30 都是"必须压在它以下"才定出来的），
    // 噪音侧的单测要拿它当护栏断言，故不能两处各写一个字面量（此前正是如此，连 NoiseTests 里都还硬编码着第三个 70）。
    private const float SmellSenseRadius = (float)NoiseLogic.ZombieSmellRadius;

    /// <summary>丧尸嗅觉兜底半径（覆盖基类 0）。见 <see cref="SmellSenseRadius"/>。</summary>
    protected override float SmellRadius => SmellSenseRadius;

    /// <summary>丧尸是 AI，听得见动静就会挪过去看看（用户拍板：「丧尸和劫掠者都听得到」）。</summary>
    protected override bool RespondsToNoise => true;

    private Rect2 _wanderBounds;
    private Func<IEnumerable<Actor>>? _survivorProvider;
    private double _wanderTimer;
    private bool _wasNight;
    private BreachController? _breach; // 门关闭后砸围栏/大门破防（袭营时由 CampMain 注入）
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>
    /// 生成一只丧尸。
    /// <para>
    /// <paramref name="outfitName"/> = null（默认）→ <b>普通丧尸</b>：随机抽一套日常着装（布衣/夹克/长裤/短裤…），
    /// 每只独立抽，故一群丧尸有的还穿着夹克、有的只剩一条裤子。<paramref name="outfitRng"/> 是这次抽取的随机源
    /// （可注入以复现，缺省用系统随机源）。
    /// </para>
    /// <para>
    /// <paramref name="outfitName"/> 给了名字 → <b>authored 精英丧尸</b>：**点名**穿哪套（确定性，不掷骰，
    /// 忽略 <paramref name="outfitRng"/>），如 <c>Zombie.Create(wander, targets, outfitName: "防暴警察丧尸")</c>
    /// 生成一只穿板甲的。精英预设不在随机池里，只能这样点名——这是用户 authored 的高难度点，不会被随机撞出来。
    /// 可用名字见 <see cref="ZombieOutfit.ElitePresets"/>；名字拼错会抛 KeyNotFoundException（而非静默发一套光身）。
    /// </para>
    /// </summary>
    public static Zombie Create(
        Rect2 wanderBounds,
        Func<IEnumerable<Actor>> survivorProvider,
        IRandomSource? outfitRng = null,
        string? outfitName = null)
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
        z.Body = CombatData.NewZombieBody(); // 失血流速 1/3（行尸走肉，血液循环不像活人）
        z.AttackWeapon = CombatData.ZombieClaw();
        z.AttackRange = 24f;
        z.AttackCooldown = z.AttackWeapon.AttackInterval; // 读 WeaponTable 权威间隔（爪击慢节奏 2.3s），敌方同步慢节奏
        // 衣服叠在腐皮之外——只靠腐皮的话防护恒为零，见 CombatData.ZombieArmor。
        z.DefenderArmor = outfitName is null
            ? CombatData.ZombieArmor(outfitRng ?? new SystemRandomSource())
            : CombatData.ZombieArmorNamed(outfitName);
        return z;
    }

    protected override void OnReady() => _rng.Randomize();

    /// <summary>
    /// 袭营时注入破防能力（门关闭后到不了营内幸存者→走到最近围栏/大门前砸墙）。委托由 CampMain 提供
    /// （找最近结构 / 对结构施伤，结构类型不外泄）；<paramref name="campCenter"/> 作无目标时的可达性探测点。
    /// </summary>
    /// <remarks>
    /// <b>丧尸这里一行门的代码都没有——这是刻意的</b>。用户拍板「丧尸不会开门，只会砸」：
    /// 关着的门对它<b>就是一堵墙</b>，而门在 <c>CampMain</c> 里本来就是一处可破坏结构（<see cref="CampStructureKind.Door"/>），
    /// 于是它撞上门后走的还是这条老路——可达性探测失败 → <see cref="BreachLogic"/> 择最近结构（门就在候选里）→ 一爪一爪砸
    /// （木门 60HP ÷ 每爪 7.5 = 8 爪）→ 砸穿 → 涌入。<b>门系统在丧尸这边是零改动的。</b>
    /// <para>
    /// 砸墙伤害与节奏<b>由它的武器（爪击）派生</b>（<see cref="StructureDamage"/>），不再是写死的常数 12——
    /// 爪击的「砸墙系数」是显式的 2.5（<b>撕扯</b>：丧尸不是用爪尖划墙，是整只扑上去撞、扒、咬），
    /// 故每爪 3 × 2.5 = 7.5。武器表调爪击伤害，它砸墙的速度立刻跟着变。
    /// </para>
    /// <para>
    /// <b>它打的从来就不只是门</b>（用户拍板「丧尸也会打围栏，不止会打门」）：候选池里围栏和大门一视同仁，从无 Kind 过滤。
    /// 此前看起来"只砸门"，是因为 <c>SpawnCampZombies</c> 把它们生成在门缝正前方 —— 一出生就够得着门。
    /// 现在每处结构有<b>攻击位名额</b>（<see cref="BreachSlots"/>）：门口站满了，后面的就去啃紧挨着门的那格围栏。
    /// 它<b>依然不会绕路、不会包抄</b>——只是在面前够得着的东西里挑一个还站得下人的。
    /// </para>
    /// </remarks>
    public void ConfigureBreach(
        BreachController.FindNearestStructure find,
        BreachController.DamageNearestStructure damage,
        Vector2 campCenter,
        BreachController.SuppressBreach? suppress = null,
        BreachController.ReleaseBreachSlot? release = null)
    {
        _breach = new BreachController(find, damage, campCenter,
            StructureDamage.PerHit(AttackWeapon), StructureDamage.Interval(AttackWeapon),
            attackReach: 34f + Radius, standoff: Radius + 8f, suppress, alternative: null, release);
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
