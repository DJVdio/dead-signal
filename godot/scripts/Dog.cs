using System;
using System.Collections.Generic;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 布鲁斯（狗）——道格的犬类伙伴。体型小、移速快，天生撕咬（近战锐器），可战斗、可被攻击、可被杀
/// （狠辣世界观，不做无敌）。是独立 <see cref="Actor"/> 子类而非 <see cref="Pawn"/> 变体：Pawn 背满人类日常系统
/// （聚餐分配/读书/医疗/装备槽），狗全不适用——营地这些系统对它天然不可见（不入 <c>_survivors</c>、不占卡牌、
/// <see cref="PawnRoleManager"/> 只遍历 Pawn）。
///
/// 行为（<see cref="Think"/>）：①守卫驻守态让位营地巡防调度；②否则自主缠斗侦测半径内最近敌对目标；
/// ③无敌则跟随主人（道格）。站岗效率与视野倍率以 Wiki 配置为准（见 <see cref="GuardEfficiency"/> / <see cref="GuardSightRadiusScaled"/>）。
/// 当前攻击目标经 <see cref="EngagedTarget"/> 暴露，供 synergy-wiring 接 2 级技能；技能倍率以 Wiki 配置为准。
///
/// 战斗定位（用户口径）：**擅长缠斗的特殊战斗单元——高闪避 / 高移速 / 低伤害**，难独自击杀、也难被速杀，
/// 靠拖住敌人给道格创造 2 级输出窗口。高闪避＝覆盖 <see cref="EvadeIncoming"/> 按概率整次躲开来袭（引擎无闪避轴，
/// 落在 Actor 承伤入口）；高移速＝<see cref="MoveSpeed"/> 显著高于人类；低伤害＝<c>WeaponTable.DogBite</c> 压低伤害区间。
/// "咬住即减速留人"由**全角色通用命中减速规则**（RimWorld stagger 式，另属 hit-stagger 任务）承担，布鲁斯只是
/// 高移速 + 该通用机制的受益者（缠斗粘性），本类**不自建减速**。
/// </summary>
public sealed partial class Dog : Actor
{
    // 独立 Id 空间（高位起）：守卫/探险 UI 用「在 _survivors 的下标」当选项 Id，狗不在该列表，
    // 故给狗一段绝不与 Pawn.Id（从 0 递增）相撞的高位 Id，便于守卫指派按真 Id 辨识出狗。
    private const int DogIdBase = 100000;
    private static int _nextDogId = DogIdBase;
    public int Id { get; } = _nextDogId++;
    public string DisplayName { get; private set; } = "布鲁斯";

    // ---- 跟随/侦测参数（空间几何参数；当前值以 Wiki 配置为准）----
    private const float FollowStopRadius = 56f;    // 距主人此半径内不再挪动（贴随完成）
    private const float FollowResumeRadius = 96f;   // 超此半径才重新贴近（滞回，防原地抖动）
    private const float DetectRadius = 200f;        // 自主缠斗侦测半径
    private const float LoseTargetRadius = 300f;    // 超此距离放弃当前缠斗目标

    /// <summary>
    /// 站岗效率作用到守卫巡防有效锁敌半径（<see cref="GuardSightRadiusScaled"/>），当前系数以 Wiki 配置为准。
    /// 单一真源＝doug-logic 的 <see cref="DougBruceBond.BruceGuardEfficiency"/>，接线层 synergy-wiring 归并。
    /// </summary>
    public const float GuardEfficiency = DougBruceBond.BruceGuardEfficiency;

    /// <summary>
    /// 闪避概率 [0,1]（高闪避）：每次来袭（近战/远程皆吃）以此概率整次躲开。配合极低伤害构成
    /// "难独自击杀、难被速杀"的缠斗定位。
    /// 当前闪避系数以 Wiki 配置为准；本处只描述"高闪避"身份。
    /// <para>
    /// 🔴 <b>数字一律以真源报告为准，别在这里抄快照</b>：<c>docs/research/2026-07-12-dog-calibration.md</c>
    /// （harness＝<c>src/DeadSignal.Sim/DogCalibration.cs</c>，重跑：<c>dotnet run --project src/DeadSignal.Sim -- dogcal</c>）。
    /// **历史报告/非配置源**：此前这段曾内联抄写武器伤害、胜率、时长与匕首基线，相关快照均已失效
    /// （"1-4" 更是 born-stale：初版 9bba641 就是 2-6，min 从来没到过 1；现 2-4 见 weapons.json dog_bite）。
    /// </para>
    /// <para>
    /// 仅作历史校准记录，不能作为当前平衡依据；当前系数、目标区间与战斗数据以 Wiki/最新报告为准。
    /// </para>
    /// </summary>
    private const float DodgeChance = 0.45f;

    /// <summary>
    /// 脚步噪音：狗比人轻得多（四只软肉垫、体型与步态更安静）；当前噪音半径以 Wiki 配置为准。
    /// 布鲁斯移速也意味着他脚步更**密**（噪音按位移节流），但每一声都传得更**近** ——
    /// 净效果是他天然适合被放出去探路。
    /// </summary>
    protected override double FootstepNoiseRadius => NoiseLogic.DogWalkNoiseRadius;

    /// <summary>
    /// 布鲁斯开不了门 —— <b>狗没有手</b>。
    /// <para>
    /// 他是 <see cref="Faction.Survivor"/> 阵营，<see cref="DoorLogic.CanOperateDoors"/> 若只看阵营就会把他
    /// 误判成"会开门"；"是不是动物"因此是独立的一维。玩法后果是对的：把布鲁斯放出去探路时，
    /// <b>一扇关着的门就能拦住他</b>——他能钻过缺口，但拧不开门把手，只能在门外等人来开。
    /// </para>
    /// </summary>
    public override bool CanOperateDoors => false;

    private Func<Actor?>? _masterProvider;                 // 主人（道格）当前实例，离场/身故返回 null
    private Func<IEnumerable<Actor>>? _hostileProvider;    // 场上潜在敌对单位（狗自行按 IsHostile 过滤）
    private readonly RandomNumberGenerator _rng = new();
    // 布鲁斯 2 级视野距离消费口：自主缠斗侦测半径按羁绊系数缩放（synergy-wiring 注入 DougBruceBond.BruceRangeMult；
    // null=中性值零回归）。仅放大侦测获取半径（"视距"语义），放弃半径 LoseTargetRadius 不随之变（防抖）。
    private Func<float>? _detectRangeMultProvider;

    /// <summary>注入侦测视距系数（布鲁斯 L2 视距加成，synergy-wiring 喂 DougBruceBond.BruceRangeMult(level,dougAlive)）。null=无加成。</summary>
    public void SetDetectRangeMultProvider(Func<float>? provider) => _detectRangeMultProvider = provider;

    /// <summary>
    /// 守卫驻守态：为 true 时让位给营地守卫调度——不自主跟随/侦测，走向岗位与巡防锁敌由 CampMain 驱动
    /// （对齐 <see cref="Pawn.Stationing"/> 的放行语义）。上/下岗由 CampMain 置位。
    /// </summary>
    public bool GuardStationing { get; set; }

    /// <summary>
    /// 布鲁斯当前正在攻击的存活目标（无则 null）。供 synergy-wiring 判定 2 级「道格攻击正在被布鲁斯攻击的
    /// 敌人时使用 Wiki 配置的羁绊伤害倍率」。只读干净接口，不暴露内部指令态。
    /// </summary>
    public Actor? EngagedTarget => CurrentAttackTarget is { Alive: true } t ? t : null;

    // ---- 进食（犬类最简，用户口径：吃一份增加饥饿 / 每聚餐相位衰减 / 不上桌；当前数值以 Wiki 配置为准）----

    /// <summary>布鲁斯的饥饿刻度（见 <see cref="DogHungerState"/>）。营地聚餐结算按余粮驱动，不占分配面板/坐席/气泡。</summary>
    public DogHungerState Hunger { get; } = new();

    /// <summary>饥饿对战斗能力的惩罚（喂给 <see cref="Actor"/> 的钩子）：越饿攻速/移速越低（与人类同阶梯）。</summary>
    protected override double HungerAbilityPenalty => Hunger.AbilityPenalty;

    /// <summary>一次聚餐相位的饥饿结算（进食与自然衰减数值以 Wiki 配置为准）。返回本次是否饿死。营地层每聚餐相位对布鲁斯调一次。</summary>
    public bool ResolveHungerPhase(bool ate) => Hunger.ResolvePhase(ate);

    /// <summary>饥饿刻度已归 0（饿死）。由聚餐结算据此走统一死亡路径。</summary>
    public bool IsStarvedToDeath => Hunger.IsStarved;

    /// <summary>饿死：走统一非战斗死亡路径（触发 Died 事件 + 移出场，复用现有死亡消费）。</summary>
    public void StarveToDeath() => KillNonCombat();

    // ---- 狗装备（批次5，道格 2 级解锁制作五件套）----

    /// <summary>
    /// 布鲁斯的穿戴态（身体 + 头 2 槽）。穿脱后须调 <see cref="RefreshArmor"/> 把护甲聚合喂进 <c>DefenderArmor</c>，
    /// 布鲁斯挨打即走部位/护甲三段判定的现有战斗管道。狗装备物品（库存 Item.Armor，RefKey∈ <see cref="DogGearCatalog"/>）
    /// 由道格制作产出（见 <c>RecipeBook</c> 狗装备五件套 + <c>CraftOutputFactory</c>）。
    /// </summary>
    public DogApparelSlots Apparel { get; } = new();

    /// <summary>
    /// 口袋狗衣提供的携带容量（探索出队负重加成，<see cref="Loadout"/> 体系）。无口袋狗衣＝0。
    /// <para>
    /// <b>已全链接线</b>（别再按"待负重系统建立"理解——系统早已建成，见 <c>CarryWeight.cs</c>）：
    /// <c>CampMain.ExpeditionDogCarryBonus</c>（CampMain.cs:2367，仅随队且活着才计）读本属性
    /// → <c>PartyCarryLimit()</c>（CampMain.cs:2532「每个队员的上限之和 <b>+ 随队布鲁斯口袋狗衣的驮运容量</b>」）
    /// → <c>_bag.SetCapacity(...)</c>（CampMain.cs:2580）抬高这一趟的硬上限，
    /// 并经 <c>dogCapacityKg</c> 喂进逐人分档 <c>ExpeditionLoad.For</c>（CampMain.cs:2597）。
    /// ⇒ 布鲁斯驮的那几 kg 真的抬高上限、真的改变每个人的负重档。
    /// </para>
    /// </summary>
    public float CarryCapacity => Apparel.TotalCarryCapacity();

    /// <summary>
    /// 穿上一件狗装备（同槽旧件自动顶替，被顶替的旧件键经 out <paramref name="displaced"/> 返回供退回库存）。
    /// 成功即刷新 <c>DefenderArmor</c>。未登记键返回 false。
    /// </summary>
    public bool EquipGear(string gearKey, out string? displaced)
    {
        DogEquipOutcome outcome = Apparel.TryEquip(gearKey, out displaced);
        if (outcome == DogEquipOutcome.BlockedUnknownGear)
        {
            return false;
        }
        RefreshArmor();
        return true;
    }

    /// <summary>脱下一件狗装备并刷新护甲。返回是否确实脱下（不在身则 false）。</summary>
    public bool UnequipGear(string gearKey)
    {
        if (!Apparel.Unequip(gearKey))
        {
            return false;
        }
        RefreshArmor();
        return true;
    }

    /// <summary>把当前穿戴的狗装备护甲层聚合喂给 <c>DefenderArmor</c>（穿脱后调）。无甲件（口袋狗衣）不产层。</summary>
    public void RefreshArmor() => DefenderArmor = Apparel.ArmorLayers();

    /// <summary>
    /// 守卫巡防有效锁敌半径 = 基类岗位锁敌半径 × <see cref="GuardEfficiency"/>（当前系数以 Wiki 配置为准）。CampMain 袭营巡防用此半径
    /// 找最近来袭丧尸。基类 <c>GuardSightRadius</c> 非虚，故以派生属性叠系数（不改 Actor）。
    /// </summary>
    public float GuardSightRadiusScaled => GuardSightRadius * GuardEfficiency;

    public static Dog Create(Func<Actor?> masterProvider, Func<IEnumerable<Actor>> hostileProvider)
    {
        var d = new Dog
        {
            DisplayName = "布鲁斯",
            BodyColor = new Color(0.55f, 0.42f, 0.28f), // 褐色犬
            _masterProvider = masterProvider,
            _hostileProvider = hostileProvider,
        };
        d.Faction = Faction.Survivor;   // 己方（与幸存者同阵营，不被友军误判，敌人可攻击）
        d.Radius = 9f;                  // 体型小
        d.MoveSpeed = 150f;             // 高移速（当前值以 Wiki 配置为准；缠斗定位=追得上、留得住）
        // TODO(bruce-actor)：犬类专用躯体（无手、少部位）待建模；暂借人形躯体——可被切除/失血/杀死即满足本批需求。
        d.Body = CombatData.NewHumanoidBody();
        d.AttackWeapon = WeaponTable.DogBite();
        d.AttackRange = 20f;            // 近战撕咬短射程（空间交战参数；当前值以 Wiki 配置为准）
        d.AttackCooldown = d.AttackWeapon.AttackInterval;
        d.DefenderArmor = Array.Empty<ArmorLayer>(); // 无甲
        return d;
    }

    protected override void OnReady() => _rng.Randomize();

    protected override void Think(double delta)
    {
        // 守卫驻守：让位营地巡防调度（走向岗位的移动令 + 巡防锁敌的 CommandAttack 均由 CampMain 下，
        // Actor 基类负责逼近/攻击）。此处不自主跟随/侦测，避免与站岗抢指令。
        if (GuardStationing)
        {
            return;
        }

        // 维护当前缠斗目标：目标死亡（基类会清空）或跑出放弃半径即释放，转入重新侦测/跟随。
        if (CurrentAttackTarget is { Alive: true } cur)
        {
            if (GlobalPosition.DistanceTo(cur.GlobalPosition) <= LoseTargetRadius)
            {
                return; // 继续缠斗（基类逼近 + 撕咬）
            }
            CancelOrders();
        }

        // 自主缠斗：侦测半径内最近敌对目标即扑咬。
        Actor? enemy = FindNearestHostile();
        if (enemy != null)
        {
            CommandAttack(enemy);
            return;
        }

        // 跟随主人（道格）：滞回——超恢复半径才贴近、进停止半径即站定，之间维持前令（防抖动）。
        Actor? master = _masterProvider?.Invoke();
        if (master is { Alive: true })
        {
            float dist = GlobalPosition.DistanceTo(master.GlobalPosition);
            if (dist > FollowResumeRadius)
            {
                CommandMoveTo(master.GlobalPosition); // 每帧重取主人位（跟随移动中的道格）
            }
            else if (dist <= FollowStopRadius && HasMoveOrder)
            {
                CancelOrders();
            }
        }
        else if (HasMoveOrder)
        {
            CancelOrders(); // 主人不在场/已故：原地待命
        }
    }

    private Actor? FindNearestHostile()
    {
        if (_hostileProvider == null)
        {
            return null;
        }
        Actor? best = null;
        float bestDist = DetectRadius * Math.Max(0f, _detectRangeMultProvider?.Invoke() ?? 1f);
        foreach (Actor a in _hostileProvider())
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

    /// <summary>
    /// 高闪避：每次来袭以 <see cref="DodgeChance"/> 概率整次躲开（近战/远程皆吃）。掷免走引擎注入的
    /// <see cref="IRandomSource"/>（与哨塔抵挡同源口径，可复现）。TODO(bruce-actor)：闪避表现（"闪避"飘字）
    /// 待接（当前静默躲开，机制优先）；数值/是否仅限近战待用户确认。
    /// </summary>
    protected override bool EvadeIncoming(IRandomSource rng) => rng.Range(0.0, 1.0) < DodgeChance;
}
