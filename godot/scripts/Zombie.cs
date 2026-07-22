using System;
using System.Collections.Generic;
using DeadSignal.Combat; // IRandomSource（穿衣抽取走可注入随机源）
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 丧尸：发现幸存者后追击并近战爪击（钝器）。AI 全在 <see cref="Think"/> 内，**按所处场景分两条路**：
/// <para>· <b>营地丧尸</b>（含袭营）：靠 <see cref="GameClock.IsNight"/> 切换——白天原地休眠不动、夜晚游荡+感知追击。</para>
/// <para>· <b>探索关丧尸</b>（<see cref="MarkExploration"/> 标记，[SPEC-T60] 威胁模型 <see cref="ZombieActivation"/>）：
/// <b>与昼夜无关</b>（探索发生在白天，它照样杀你）——普通丧尸靠视野/噪音/靠近唤醒；门后特殊丧尸完全冻结，
/// 有且仅有开对应门才唤醒、唤醒后转普通。</para>
/// </summary>
public sealed partial class Zombie : Actor
{
    // 白昼满档视距 R0（喂 VisionLogic.ConeFor 的 baseRange，实际视距＝R0×光照系数）。
    //
    // 490 的来历＝**按营地夜晚反推**：夜间环境光 NightAmbient 0.15 → 视距系数 0.4475、半角 34.5°，
    // 490×0.4475≈219 使**夜间前向视距≈旧半径 220**，即由"半径雷达"变"等距但有朝向+可被墙/暗处遮挡"的锥形——
    // 保留正面威胁、潜行从侧后/掩体/暗处自然涌现，而非单纯削弱。
    //
    // ⚠ [SPEC-T60] 起**别再按"丧尸仅夜间活动"读这个数**：探索关丧尸不看昼夜（见类注释 / ThinkExploration），
    //   而探索恒在 DayExplore＝白天段（DayPhaseSegments→DayNightPhase.Day），故同一个 R0 在探索期吃的是白昼光照：
    //     · 非暗关：环境光 1.0 → 视距**满档 490**、半角 60°（≈夜间 219 的 2.2 倍）。
    //     · 暗关（ExplorationLighting.IsIndoorsDark → IndoorsDarkAmbient 0.10）：490×0.415≈203、半角 33°。
    //   即"219"只是营地夜晚这一档的表现，不是全局有效视距。数值拟定待调，交 Sim/用户校准。
    private const float BaseSightRange = 490f;
    // 嗅觉兜底（用户拍板）：短程全向感知半径，补锥形"侧后死角"被无脑绕过。贴到 70px 内且同房间（未被墙隔）即闻到，
    // 无视朝向/半角/光照（气味不吃暗），**也无视昼夜**——营地夜晚与探索期白天同样生效（唯一例外：[SPEC-T60]
    // 探索期冻结的门后特殊丧尸嗅觉归零，见下方 SmellRadius）。半径拟定待调（用户口径 60~80px 取中）；
    // 穿墙=否（复用遮挡判定，待用户确认）。
    // 单一真源在 NoiseLogic：这个 70 是**整张噪音表的锚点**（走路 40 / 撬锁 30 都是"必须压在它以下"才定出来的），
    // 噪音侧的单测要拿它当护栏断言，故不能两处各写一个字面量（此前正是如此，连 NoiseTests 里都还硬编码着第三个 70）。
    private const float SmellSenseRadius = (float)NoiseLogic.ZombieSmellRadius;

    /// <summary>丧尸嗅觉兜底半径（覆盖基类 0）。见 <see cref="SmellSenseRadius"/>。
    /// [SPEC-T60] 探索期**冻结的门后特殊丧尸**嗅觉归零（对"靠近"免疫，有且仅有开门唤醒）；其余照常。</summary>
    protected override float SmellRadius
        => ZombieActivation.RespondsToPerception(_explorationMode, _doorLocked, _activated) ? SmellSenseRadius : 0f;

    /// <summary>丧尸是 AI，听得见动静就会挪过去看看（用户拍板：「丧尸和劫掠者都听得到」）。
    /// [SPEC-T60] 探索期**冻结的门后特殊丧尸不响应噪音**（不被普通丧尸的动静连锁唤醒）；普通/已激活/营地丧尸照常。</summary>
    protected override bool RespondsToNoise
        => ZombieActivation.RespondsToNoise(_explorationMode, _doorLocked, _activated);

    // [SPEC-T60] 探索期威胁模型（见 ZombieActivation）。营地丧尸三者恒 false ⇒ 走原昼夜 Think，零回归。
    private bool _explorationMode; // true=本只在探索关（走感知唤醒/门后激活；false=营地丧尸，走原昼夜休眠）
    private bool _doorLocked;      // true=门后特殊丧尸（冻结、免疫视野/噪音/靠近，仅其门被开才唤醒）
    private bool _activated;       // 门后特殊丧尸是否已被开门唤醒（唤醒后转普通）

    /// <summary>休眠/冻结偏暗色（表现只映射真实状态：站着的休眠布景）。</summary>
    private static readonly Color DormantColor = new(0.4f, 0.5f, 0.32f);
    /// <summary>活跃/已唤醒偏亮色。</summary>
    private static readonly Color ActiveColor = new(0.5f, 0.68f, 0.38f);

    /// <summary>
    /// [SPEC-T60] 标记本只为**探索关丧尸**并给定类别。<paramref name="doorLocked"/>=true ⇒ 门后特殊丧尸
    /// （冻结待其门被开）；false ⇒ 普通丧尸（视野/噪音/靠近唤醒）。两类开局都取休眠偏暗色。
    /// </summary>
    public void MarkExploration(bool doorLocked)
    {
        _explorationMode = true;
        _doorLocked = doorLocked;
        BodyColor = DormantColor;
    }

    /// <summary>[SPEC-T60] 唤醒（门后特殊丧尸的门被开、或普通丧尸感知/听见玩家时调用）：转入活跃、变亮色。幂等。</summary>
    public void Activate()
    {
        if (_activated)
            return;
        _activated = true;
        BodyColor = ActiveColor;
    }

    private Rect2 _wanderBounds;
    private Func<IEnumerable<Actor>>? _survivorProvider;
    private double _wanderTimer;
    private bool _wasNight;
    private double _groanTimer;
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
        string? outfitName = null,
        IRandomSource? visualRng = null)
    {
        var z = new Zombie
        {
            BodyColor = new Color(0.45f, 0.6f, 0.35f),
            VisualModelIndex = EnemyVisualModels.Pick(visualRng ?? new SystemRandomSource()),
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

    protected override void OnReady()
    {
        _rng.Randomize();
        // 仅作听感错峰，不使用战斗随机源；实例号只决定第一次何时发声。
        _groanTimer = 3.5 + GetInstanceId() % 500 / 100.0;
    }

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
        TickAmbientVoice(delta);
        // [SPEC-T60] 探索关走威胁模型（普通丧尸感知唤醒 / 门后特殊丧尸开门激活）；营地丧尸走原昼夜休眠（零回归）。
        if (_explorationMode)
        {
            ThinkExploration(delta);
            return;
        }
        ThinkCamp(delta);
    }

    private void TickAmbientVoice(double delta)
    {
        // 休眠布景保持安静；探索中已唤醒、或营地夜间活跃的丧尸才低声呻吟。
        bool active = _explorationMode ? _activated : Clock.IsNight;
        if (!active) return;
        _groanTimer -= Math.Max(0, delta);
        if (_groanTimer > 0) return;
        GameAudioRuntime.PlayWorld(AudioCue.ZombieGroan, GlobalPosition);
        _groanTimer = 7.0 + GetInstanceId() % 600 / 100.0;
    }

    /// <summary>营地丧尸（含袭营）：白天原地休眠、夜晚游荡+感知追击。<b>本方法一字未改行为</b>（探索轴不接管此路）。</summary>
    private void ThinkCamp(double delta)
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
                BodyColor = DormantColor; // 休眠偏暗（ActorSprite 每帧取 BodyTint 自动跟随）
            }
            else
            {
                BodyColor = ActiveColor; // 夜间活跃偏亮
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

        WanderStep(delta);
    }

    /// <summary>
    /// [SPEC-T60] 探索关丧尸（探索期恒为白天 DayExplore）：
    ///   · **门后特殊丧尸**门未开 ⇒ 完全冻结（免疫视野/噪音/靠近），站着不动。
    ///   · **普通丧尸**休眠时也扫描（视野锥/嗅觉），感知到玩家即唤醒（转入追击）；未感知到则站桩不游荡（保住 authored 站位）。
    ///   · **已唤醒**（普通感知到 / 门后被开门激活）：有目标则交基类逼近+攻击，无目标则游荡（含丢失视野后侦查）。
    /// </summary>
    private void ThinkExploration(double delta)
    {
        // 门后特殊丧尸：门未开 ⇒ 冻结。表现＝休眠偏暗（MarkExploration 已置），站住不动。
        if (ZombieActivation.IsFrozen(_doorLocked, _activated))
        {
            return;
        }

        // 感知：普通丧尸休眠期也扫描；已激活者刷新/丢失目标。（冻结的门后特殊丧尸走不到这里。）
        if (PerceptionDue(delta))
        {
            UpdatePerception(_survivorProvider?.Invoke() ?? Array.Empty<Actor>(), BaseSightRange);
        }

        // 普通丧尸一旦靠视野/靠近感知到玩家 ⇒ 唤醒（转入追击、变亮色）。
        if (!_activated && CurrentAttackTarget is { Alive: true })
        {
            Activate();
        }

        // 仍未唤醒的普通丧尸：站桩继续扫描、不游荡（保住"各藏一房"/authored 站位；表现仍是休眠布景）。
        if (!_activated)
        {
            return;
        }

        // 破防（探索关未注入 BreachController ⇒ 恒 no-op，harmless）。
        if (_breach != null && _breach.TryBreach(this, delta, CurrentAttackTarget?.GlobalPosition))
        {
            return;
        }

        // 已唤醒：有目标交基类逼近+攻击，无目标游荡。
        if (CurrentAttackTarget is { Alive: true })
        {
            return;
        }

        WanderStep(delta);
    }

    /// <summary>无目标时的随机游荡（含丢失视野后走向最后目击点的一次侦查 move，到点/超时后恢复随机游荡）。</summary>
    private void WanderStep(double delta)
    {
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
    /// [SPEC-T60] 听见噪音：探索期**普通丧尸**据此唤醒（并循基类走过去侦查）——"根据噪音唤醒"。
    /// 冻结的门后特殊丧尸 <see cref="RespondsToNoise"/>=false ⇒ <c>EmitNoiseAt</c> 已挡、根本走不到这里（免疫噪音）。
    /// 营地丧尸（非探索）行为不变。
    /// </summary>
    protected override void HearNoise(Vector2 pos)
    {
        if (_explorationMode && !_activated)
        {
            Activate();
        }
        base.HearNoise(pos);
    }
}
