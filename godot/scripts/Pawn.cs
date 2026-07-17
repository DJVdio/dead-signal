using System.Collections.Generic;
using System.Linq;
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
    /// 饥饿刻度状态机（见 <see cref="HungerState"/>，数值化 0-6）。全部规则（衰减/进食/上限/惩罚）
    /// 归纯逻辑对象；Pawn 只持有并在昼夜切换/聚餐时驱动，并把能力惩罚经钩子喂给战斗消费点。
    /// 普通幸存者上限 5；"大胃袋"特质将来可传 6（本轮所有 Pawn 默认 5）。
    /// </summary>
    public HungerState Hunger { get; } = new HungerState();

    /// <summary>饥饿对战斗能力的惩罚（喂给 <see cref="Actor"/> 的钩子）。丧尸基类返回 0，此处返回饥饿净值。</summary>
    protected override double HungerAbilityPenalty => Hunger.AbilityPenalty;

    /// <summary>
    /// 幸存者个人已读书集（见 <see cref="ReadBookSet"/>，纯逻辑）。配方书门槛按"制作者本人已读"判定 —— 消费本对象，
    /// 不再走营地全局已读（<see cref="BookData.IsRead"/> 仍供库存"已读"标记等营地视角点使用）。丧尸/Raider 不涉。
    /// </summary>
    private readonly ReadBookSet _readBooks = new();

    /// <summary>本 Pawn 是否读完某书（按 book id）。配方书门槛的权威判据（按制作者）。</summary>
    public bool HasReadBook(string bookId) => _readBooks.HasRead(bookId);

    /// <summary>标记本 Pawn 读完某书（幂等）。阅读结算按读者调用。</summary>
    public void MarkBookRead(string bookId) => _readBooks.MarkRead(bookId);

    // ================= 读书活动（夜间指派）：走到座位坐下累进度 → 读满标已读 + 诺蒂涨 perk =================
    // 读书是**指派的夜间活动**（Role=Reading，仅 NightAct）：被指派者寻路走到座位坐下，逐帧累计阅读小时，
    // 读满 BookData.ReadHours 即标个人已读（配方门槛）+ 全局已读（库存标记）。诺蒂持"书虫"专属效果——
    // 边读边涨等级、读得越快。无座 -10% 读速。读书算休养、无医疗代价（营地昼夜推进的 resting 由营地层给）。

    /// <summary>本 Pawn 的专属效果容器（诺蒂·书虫，其余角色无 perk → 各加成为 0）。见 <see cref="SurvivorPerks"/>。</summary>
    public SurvivorPerks Perks { get; } = new();

    /// <summary>
    /// [T61] **耗子**：脚步/开门/撬锁/静默拆除的噪音半径 ×0.60（用户原话「耗子的脚步和动作轻不可闻，声音减少 40%」）。
    /// <b>战斗/开枪/破坏不减</b> —— 那是由 <see cref="RatPerk.AppliesToActionNoise"/> 在 <see cref="Actor.EmitNoiseAt"/>
    /// 里按 <see cref="RatNoiseSource"/> 裁掉的，**本属性只管"缩多少"，不管"缩不缩"**。
    /// 其余角色 <see cref="SurvivorPerks.IsRat"/>=false ⇒ 恒 1.0（零回归：既有单位噪音半径一个数不变）。
    /// <para>L1 即生效（不看等级）⇒ 无需读 StoryFlags，Pawn 不必持有营地态。</para>
    /// </summary>
    protected override double ActionNoiseScale => RatPerk.ActionNoiseMultiplier(Perks.IsRat, ratLevel: 1);

    /// <summary>本 Pawn 逐书阅读进度（跨夜持久）。见 <see cref="ReadingProgress"/>。</summary>
    private readonly ReadingProgress _readingProgress = new();

    /// <summary>当前被指派在读的书 id（未在读为 <c>null</c>）。读满或结束后清空。</summary>
    public string? AssignedBookId { get; private set; }

    /// <summary>当前认领的座位（无座为 <c>null</c>，按 -10% 读速就地读）。由 CampMain 认领/释放，Pawn 只持有并据其判有无座。</summary>
    public CampMain.SeatClaim? ReadingSeat { get; set; }

    /// <summary>当前在读书的底层数据（供读满时置全局已读 + 取 ReadHours 判完成）。</summary>
    private BookData? _assignedBook;

    /// <summary>本夜有效的全营读速加成汇总（由 CampMain 遍历全体求和喂入，含读者自身贡献）。</summary>
    private double _campWideReadingMult = 1.0;   // [整改] 全营读速乘子(∏(1+各L3书虫贡献))，非旧加成和；无书虫=1.0

    /// <summary>一夜的实时长度（游戏内秒，受时标缩放同口径）：把每帧 delta 换算成游戏内小时（一整夜=12 小时）。</summary>
    private double _nightLengthSeconds;

    /// <summary>
    /// 开始一次读书指派：记下要读的书、全营加成汇总、夜长（换算时间用）。座位由 CampMain 另行认领并置
    /// <see cref="ReadingSeat"/>。之后每帧 <see cref="Think"/> Reading 分支累进阅读进度。
    /// </summary>
    public void BeginReading(BookData book, double campWideMult, double nightLengthSeconds)
    {
        _assignedBook = book;
        AssignedBookId = book.Id;
        _campWideReadingMult = campWideMult;
        _nightLengthSeconds = nightLengthSeconds;
    }

    /// <summary>结束读书（读满/夜晚结束/中断）：清运行时态。座位释放由 CampMain 负责（它持认领句柄）。</summary>
    public void EndReading()
    {
        _assignedBook = null;
        AssignedBookId = null;
        ReadingSeat = null;
        Stationing = false;
    }

    /// <summary>
    /// 累进一帧阅读进度：delta 已受 Engine.TimeScale 缩放，与夜长同口径 → 游戏内小时=delta/夜长×12。
    /// 乘有效读速（自身 perk + 全营加成 + 有无座系数）推进进度；诺蒂据此累计升级；读满标个人+全局已读并结束该书。
    /// </summary>
    private void AccrueReading(double delta, BookData book)
    {
        if (_nightLengthSeconds <= 0)
            return;

        double gameHours = delta / _nightLengthSeconds * 12.0;
        bool hasSeat = ReadingSeat.HasValue;
        // 书籍前置链：没读完前置书读得极慢（×0.2），但不禁止（读满阈值不变，只是更耗时）。
        double prereqFactor = ReadingSpeed.PrerequisiteFactor(book, HasReadBook);
        // [装备→能力加成] 读者穿戴品读速乘子（平光眼镜 ×1.05）：经 ApparelEffectMultiplier 从真实穿戴品名取数，勿手写常数。
        double apparelMult = ApparelCatalog.ApparelEffectMultiplier(EquippedApparel, ApparelCatalog.EquipEffectKind.ReadingSpeed);
        double speed = ReadingSpeed.Effective(1.0, Perks.SelfReadingSpeedBonus, hasSeat, _campWideReadingMult, apparelMult, prereqFactor);
        double hours = gameHours * speed;
        if (hours <= 0)
            return;

        _readingProgress.Advance(book.Id, hours);
        Perks.Bookworm?.AddReadingTime(hours); // 诺蒂：累计阅读时间涨等级（无 perk 者 Bookworm==null 跳过，别双算别的书）

        if (_readingProgress.IsComplete(book.Id, book.ReadHours))
        {
            MarkBookRead(book.Id); // 个人已读（配方门槛按制作者本人）
            book.MarkRead();       // 全局已读（库存"已读"标记等营地视角）
            AssignedBookId = null; // 停止累进；等下一指派或回 Idle
            _assignedBook = null;
        }
    }

    // ---- §6 装备两模型：穿戴槽（护甲/穿戴品）+ 持械（左右手武器）----
    // Pawn 是唯一持有者与生效点：两模型只管"穿在哪/持在哪"的槽位规则（定稿、纯逻辑），
    // 由 Pawn 把它们的结果**投影**回 Actor 的战斗消费字段（AttackWeapon / DefenderArmor / IsRanged /
    // AttackRange / AttackCooldown）。任何穿脱后都重投一次，保证战斗读到的永远是当前装备态。

    /// <summary>穿戴态（11 槽：头/眼/面/躯干三层/左右手/裤子/左右脚）。见 <see cref="ApparelSlots"/>。</summary>
    private readonly ApparelSlots _apparel = new();

    /// <summary>持械态（左右手各一把，推导 <see cref="GripMode"/>）。见 <see cref="WeaponLoadout"/>。</summary>
    private readonly WeaponLoadout _loadout = new();

    /// <summary>手持光源态（手电/火把，占一只手，读 <see cref="WeaponLoadout"/> 校验，与双手武器互斥）。见 <see cref="HeldLightState"/>。</summary>
    private readonly HeldLightState _heldLight = new();

    /// <summary>当前手持光源态（只读）：暴露/照明/装备 UI 消费。</summary>
    public HeldLightState HeldLight => _heldLight;

    /// <summary>
    /// 已穿护甲名 → 其生效 <see cref="ArmorLayer"/>（带防御数值）。<see cref="ApparelSlots"/> 只存名/覆盖部位、
    /// 不存防御数值，故此处并存一份 name→layer 以在穿脱后重组 <see cref="DefenderArmor"/>。
    /// 纯覆盖类穿戴品（如防毒面具，无护甲数值）不入此表——参与遮挡、不参与逐层减伤。
    /// </summary>
    private readonly Dictionary<string, ArmorLayer> _apparelLayers = new();

    /// <summary>持握态（只读，供战斗层后续消费攻速/误差角系数；本轮只暴露不消费）。</summary>
    public GripMode Grip => _loadout.Grip;

    // ———————————— 🔴 [T45·负重激活] 这个人这一趟背了多少 ————————————

    /// <summary>他这一趟的负重账（装备 + 分摊的战利品）。营地内 / 非探索队 = <see cref="MemberLoad.None"/>（全 1.0，零回归）。</summary>
    private MemberLoad _carryLoad = MemberLoad.None;

    /// <summary>
    /// 他这一趟的负重账（只读，供 HUD / 角色面板显示"你超重了、慢了多少"）。
    /// 由 <c>CampMain.SyncExpeditionLoad</c> 每帧灌入（关内可能断手掉甲 ⇒ 装备与上限都会变）。
    /// </summary>
    public MemberLoad CarryLoad => _carryLoad;

    /// <summary>他身上装备的实重（kg）：左右手武器（双手握一把只算一次）+ 11 槽护甲（成对品逐只计）。</summary>
    public double GearKg => GearWeight.Of(HeldWeapons, WornArmor);

    /// <summary>
    /// 灌入这一趟的负重账 —— <b>负重 debuff 从死代码变成真效果的唯一入口</b>：
    /// 把 <see cref="MemberLoad"/> 的两个乘子落到 <see cref="Actor"/> 的移动链（<c>CarryLoadSpeedMult</c>）
    /// 与出手间隔（<c>CarryLoadAttackSpeedMult</c>）上。
    /// </summary>
    public void SetCarryLoad(MemberLoad load)
    {
        _carryLoad = load;
        CarryLoadSpeedMult = load.SpeedMultiplier;
        CarryLoadAttackSpeedMult = load.AttackSpeedMultiplier;
    }

    /// <summary>回营/离队：清账，恢复无罚（营地里没有背包，也就没有负重档）。</summary>
    public void ClearCarryLoad() => SetCarryLoad(MemberLoad.None);

    // ———————————— 皮特·闪避（L3 且当前负重 <30kg 时 15% 掷免整次攻击）————————————

    /// <summary>皮特当前等级提供者（由 <c>CampMain.AddActor</c> 注入 <c>() => PeteLevelNow()</c>，读实时 StoryFlags 派生）。
    /// null（非皮特/未注入）⇒ <see cref="EvadeIncoming"/> 恒 false 零回归。</summary>
    private System.Func<int>? _peteLevelProvider;

    /// <summary>注入皮特等级提供者 lambda。null 清除（＝无闪避，零回归）。</summary>
    public void SetPeteLevelProvider(System.Func<int>? f) => _peteLevelProvider = f;

    /// <summary>
    /// 皮特受击闪避：仅皮特 L3 且当前负重 &lt;30kg → 15% 概率整次躲开（<see cref="PetePerk.DodgeChance"/>）。
    /// 非皮特/未注入等级提供者 ⇒ 恒 false（基类零回归）。随机走引擎注入的 <see cref="IRandomSource"/>（可复现）。
    /// </summary>
    protected override bool EvadeIncoming(IRandomSource rng)
    {
        if (!Perks.IsPete || _peteLevelProvider is not { } level)
        {
            return false;
        }
        double chance = PetePerk.DodgeChance(level(), CarryLoad.CarriedKg);
        return chance > 0.0 && rng.Range(0.0, 1.0) < chance;
    }

    /// <summary>当前主攻武器（= <see cref="WeaponLoadout.PrimaryWeapon"/>，与 <see cref="Actor.AttackWeapon"/> 同源）。</summary>
    public Weapon? PrimaryWeapon => _loadout.PrimaryWeapon;

    /// <summary>
    /// 幸存者手里握着的武器（倒下时进尸体）。<b>覆写基类</b>是因为只有幸存者能<b>双持</b>——基类那个
    /// "唯一一把 <c>AttackWeapon</c>" 的口径会漏掉副手那把（双持短剑的人死了只掉一把）。
    /// <para>直接转发给 <see cref="WeaponLoadout.HeldWeapons"/>：双手握一把的去重也在那里收口
    /// （否则抱着重剑倒下的人会掉出两把重剑）。空手 ⇒ 空列表（<c>AttackWeapon</c> 此时是拳脚，
    /// 而拳脚本就不是能拿走的东西）。</para>
    /// </summary>
    public override IReadOnlyList<Weapon> HeldWeapons => _loadout.HeldWeapons;

    /// <summary>某手所持武器（空手 null），供装备 UI 渲染。</summary>
    public Weapon? WeaponInHand(Hand hand) => hand == Hand.Left ? _loadout.LeftHand : _loadout.RightHand;

    /// <summary>某穿戴槽当前装了什么（空 null），供装备 UI 渲染。</summary>
    public string? ApparelAt(EquipSlot slot) => _apparel.ItemAt(slot);

    /// <summary>当前已穿的全部穿戴品标识，供装备 UI 渲染。</summary>
    public IReadOnlyCollection<string> EquippedApparel => _apparel.EquippedItems;

    /// <summary>因断肢当前不可用的穿戴槽（供 UI 灰显），依据本 Pawn 躯体的已切除部位实时求得。</summary>
    public IReadOnlySet<EquipSlot> DisabledApparelSlots => ApparelSlots.DisabledSlots(SeveredParts());

    /// <summary>
    /// 一次昼夜相位聚餐净结算：无条件 -1，吃到饭再 +1（净零维持 / 净 -1 前进一级），一步 clamp。
    /// 避免旧两步"1→0 途中进食被短路"的跨 0 误杀。返回本次是否饿死（刻度归 0）。
    /// </summary>
    public bool ResolveHungerPhase(bool ate) => Hunger.ResolvePhase(ate);

    /// <summary>
    /// 一相位内补第二餐（补餐回升，用户拍板"应该能补回来"）：净 +1，clamp 到该角色上限 <see cref="HungerState.Cap"/>。
    /// 把掉档者往回喂——仅在第一餐已保证、余粮充裕时由营地层对分粮给出的补餐名单调用。饿死为终态不复活（<see cref="HungerState.Feed"/> 内部守卫）。
    /// </summary>
    public void ServeSecondMeal() => Hunger.Feed();

    /// <summary>饥饿刻度已归 0（饿死）。由聚餐结算据此走统一死亡路径。</summary>
    public bool IsStarvedToDeath => Hunger.IsStarved;

    /// <summary>饿死：走统一非战斗死亡路径（触发 Died 事件 + 移出场，复用现有死亡消费）。</summary>
    public void StarveToDeath() => KillNonCombat();

    // ================= 伤病系统（医疗）：持伤病态 + 昼夜恶化 + 死/致残联动 =================
    // 每个幸存者挂一份 HealthConditionSet（纯逻辑，见 HealthConditions.cs）：战斗产出的出血/骨折经
    // ArchiveWounds 建档进伤病集；营地每昼夜 AdvanceHealthDay 推进恶化/愈合，未手术必恶化——封顶
    // 致残(Sever 断肢)/致死(走统一非战斗死亡路径)。手术/用药（里程碑②）由医疗面板直接在 Health 上调
    // PerformSurgery/TreatIllness。手术数值/点数不对玩家展示（UI 只显模糊描述）。

    /// <summary>本幸存者的伤病集（出血/骨折/感染/疾病）。医疗面板直接在其上做手术/用药；营地昼夜推进它。</summary>
    public HealthConditionSet Health { get; } = new();

    /// <summary>已建档的骨折部位：Body 无"消骨折"接口，故骨折靠此永久去重，防每昼夜 ArchiveWounds 反复建档（出血靠止血同步、按活跃条目去重）。</summary>
    private readonly HashSet<string> _fractureArchived = new();

    /// <summary>本幸存者当前是否有活跃伤病（供 UI / 分粮"伤员"判定的补充信号）。</summary>
    public bool HasHealthConditions => Health.Conditions.Count > 0;

    /// <summary>
    /// [批次21·impl-bedrest] 本昼夜的**休养流水账**：每过一个相位记一笔休养质量（不休养/地铺/床），
    /// 黎明结算时折成 <see cref="RestLedger.RestFraction"/>/<see cref="RestLedger.BedFraction"/> 喂 <see cref="AdvanceHealthDay"/>，随后清账。
    /// 白天在营地睡的相位就是从这里进的账 —— 旧模型整日只取一个布尔，那三个相位压根没人记。
    /// </summary>
    public RestLedger Rest { get; } = new();

    /// <summary>
    /// [批次21·impl-bedrest] 玩家是否已下令让他**卧床养病**（跨相位持续，直到叫他起来）。
    /// 与 <see cref="PawnRole.Bedrest"/> 的关系：这是**意图**（玩家的令），Role 是**当下的执行态**（由 PawnRoleManager 每相位重排）。
    /// 分开存是因为 Role 每次相位切换都会被重置成 Idle，令不能跟着丢。
    /// </summary>
    public bool BedrestOrdered { get; private set; }

    /// <summary>下令卧床养病 / 叫他起床（放不放得下由 <see cref="BedrestLogic.CanOrderBedrest"/> 裁定，床位占用由 <see cref="BedRegistry"/> 管）。</summary>
    public void SetBedrest(bool on) => BedrestOrdered = on;

    /// <summary>
    /// [SPEC-B14-补3·护栏①] 感染**疗程指派**：当前指派用于治疗本人感染的药 key（抗生素/草药膏/蒲公英茶），null=未指派疗程。
    /// 营地每昼夜黎明自动扣一份该药并累进治疗进度，直到治愈/断药/撤销（防"忘点一天=冤死"）。以人为单位：一人一份疗程。
    /// </summary>
    public string? InfectionTreatmentMedKey { get; private set; }

    /// <summary>指派一档药做感染疗程（覆盖旧指派）。</summary>
    public void AssignInfectionTreatment(string medKey) => InfectionTreatmentMedKey = string.IsNullOrEmpty(medKey) ? null : medKey;

    /// <summary>撤销感染疗程（治愈/断药/玩家停止/无感染时清空）。</summary>
    public void ClearInfectionTreatment() => InfectionTreatmentMedKey = null;

    /// <summary>[SPEC-B14-补2] 玫瑰果茶恢复加成的恢复效率加算量（**百分点**，加算，用户原话非拟定）：喝下后 24 游戏小时内伤病恢复速度 +此值。
    /// 数值真源已外置至 <c>health.json</c>（<see cref="HealthConfig.RosehipTeaHealBonusPct"/>）；本属性委托到 catalog 段。</summary>
    public static double RosehipTeaHealBonusPct => GameConfigCatalog.Section<HealthConfig>().RosehipTeaHealBonusPct;

    /// <summary>剩余恢复加成的**昼夜恢复次数**（玫瑰果茶 buff）：≥1 时下次 <see cref="AdvanceHealthDay"/> 加 <see cref="RosehipTeaHealBonusPct"/>pp 后自减。
    /// 24 游戏小时≈一昼夜恢复结算，故 1 次覆盖一整天的恢复（GameClock 无细游戏小时读数，以昼夜恢复次数近似，draft 待确认）。</summary>
    private int _rosehipTeaHealTicks;

    /// <summary>玫瑰果茶恢复加成当前是否生效（供 UI 显示）。</summary>
    public bool HasRosehipTeaHealBuff => _rosehipTeaHealTicks > 0;

    /// <summary>饮用玫瑰果茶：激活 24 游戏小时（≈一次昼夜恢复结算）的 +9pp 恢复加成（覆盖旧计时=续杯刷新，不叠加层数）。</summary>
    public void DrinkRosehipTea() => _rosehipTeaHealTicks = 1;

    /// <summary>
    /// 施术者操作能力 0..1（= 1 − 残疾操作惩罚，再并饥饿净值；断手/饥饿拉低）。喂给
    /// <see cref="HealthConditionSet.PerformSurgery"/> 的 operationCapability（与战斗出手间隔同源口径）。
    /// </summary>
    public double OperationCapability =>
        System.Math.Clamp(
            HungerState.CombineCapability(Body.DisabilityModifiers.OperationPenalty, HungerAbilityPenalty),
            0.0, 1.0);

    /// <summary>本幸存者已读书 id 集（供 <see cref="MedicalBookPoints.SumAlways"/> / <see cref="MedicalBookPoints.SumWithoutSupplies"/> 求施术者医疗书加点）。</summary>
    public IReadOnlyCollection<string> ReadBookIds => _readBooks.ReadBooks;

    /// <summary>
    /// 从当前战斗态（Body 出血伤口/骨折部位）建档伤病：经 <see cref="HealthMapping.SeedFromBody"/> 映射为伤病条目并入
    /// <see cref="Health"/>（幂等去重）。出血按"该部位是否已有活跃出血条目"去重（术后止血会从 Body 摘除，故可再度受伤重新建档）；
    /// 骨折靠 <see cref="_fractureArchived"/> 永久去重（Body 无消骨折接口）。每昼夜推进前调用，把战斗产物接进生存层伤病系统。
    /// </summary>
    public void ArchiveWounds()
    {
        foreach (HealthCondition c in HealthMapping.SeedFromBody(Body).Conditions)
        {
            if (c.Type == HealthConditionType.Bleeding)
            {
                bool active = Health.Conditions.Any(x => x.Type == HealthConditionType.Bleeding && x.BodyPart == c.BodyPart);
                if (!active)
                {
                    Health.Add(c);
                }
            }
            else if (c.Type == HealthConditionType.Fracture && c.BodyPart != null && _fractureArchived.Add(c.BodyPart))
            {
                Health.Add(c);
            }
        }
    }

    /// <summary>
    /// 推进本幸存者一昼夜的伤病演变：先建档新伤，再 <see cref="HealthConditionSet.TickDay"/>（未手术恶化 / 已手术愈合）。
    /// 封顶致残的部位就地 <see cref="Body.Sever"/> 断肢（装备联动由每帧 ReconcileSeverance 兜底）；术后愈合/清除的出血同步
    /// 从 Body 止血；骨折治疗档回写 Body（手术成功→<see cref="Body.MarkFractureTreated"/> 减半惩罚，康复清除→<see cref="Body.HealFracture"/> 归零），
    /// 保持战斗层能力系数与医疗态一致。是否致死经返回值交营地统一走死亡路径。
    /// </summary>
    /// <param name="rng">感染 roll 随机源。</param>
    /// <param name="resting">本昼夜是否卧床休养（减缓感染/疾病恶化、加速术后愈合）。**已被 <paramref name="restFraction"/> 取代**，仅在不传占比时回落。</param>
    /// <param name="healSpeedMultiplier">
    /// 全营恢复速度乘子（默认 1.0＝无光环，零回归）：山姆 3 级"英雄风范"光环 ×1.03（<c>SamPerk.CampHealSpeedMultiplier</c>），
    /// 由 <c>CampMain</c> 按当前营地人数算好传入。作用于术后愈合量，与玫瑰果茶/睡床的加算百分点是正交两轴。
    /// </param>
    /// <param name="restFraction">
    /// [批次21·impl-bedrest] 本昼夜**休养占比** 0..1（默认 null → 回落布尔，零回归）：由 <see cref="RestLedger"/> 按相位累计。
    /// **白天在营地睡的相位自此计入** —— 旧模型整日只取一个布尔且在黎明读到昨夜的角色，白天睡整天等于白睡。
    /// </param>
    /// <param name="bedFraction">[批次21·impl-bedrest] 本昼夜**睡床占比** 0..1（默认 null → 回落布尔）：地铺不吃这一轴，床要造。</param>
    public HealthTickResult AdvanceHealthDay(IRandomSource rng, bool resting, bool restedInBed = false, double infectionChanceMultiplier = 1.0, double healSpeedMultiplier = 1.0,
        double? restFraction = null, double? bedFraction = null)
    {
        ArchiveWounds();
        // [SPEC-B14-补2] 玫瑰果茶恢复加成：生效则本昼夜恢复效率 +RosehipTeaHealBonusPct 个百分点，随后自减一次计时。
        double extraHealBonusPct = 0.0;
        if (_rosehipTeaHealTicks > 0)
        {
            extraHealBonusPct = RosehipTeaHealBonusPct;
            _rosehipTeaHealTicks--;
        }
        // [A3] 书 → 骨折恢复被动：读过《尖峰时刻》⇒ 本人**骨折**逐日愈合 ×1.15（仅骨折，见 FractureRecoveryBooks）。
        // 这是**该人本人**的属性（判据＝其 ReadBookSet），故在此按 this 的已读书就地算——与山姆 L3 全营光环（healSpeedMultiplier，
        // 由 CampMain 统一传入）是正交两轴、对骨折连乘。非 Pawn 单位不经此路径 ⇒ 零回归。
        double fractureHealSpeedMultiplier = FractureRecoveryBooks.HealSpeedMultiplier(HasReadBook);
        HealthTickResult result = Health.TickDay(rng, resting, restedInBed, infectionChanceMultiplier, extraHealBonusPct, healSpeedMultiplier,
            restFraction, bedFraction, fractureHealSpeedMultiplier);

        foreach (string part in result.MaimedParts)
        {
            Body.Sever(part); // 坏疽/畸形封顶致残：切除该肢
        }

        // 出血条目闭合 → 从 Body 止血，保持战斗层一致。**单一同步路径**（基于"条目是否存在"，与闭合成因无关）：
        // 手术养好、微小伤新鲜期自愈、截肢连带清理——任一使该部位不再有活跃出血条目，都在此统一 StopBleed。
        foreach (string part in Body.BleedingWounds.ToList())
        {
            if (!Health.Conditions.Any(c => c.Type == HealthConditionType.Bleeding && c.BodyPart == part))
            {
                Body.StopBleed(part);
            }
        }

        // [T53] 休养自然回血（用户拍板：「补——休养自然回血」；不做输血/血袋）。
        //
        // 🔴 在此之前**实机没有任何回血手段**：Body.Blood 只有 LoseBlood（只减）与 SetBloodMax（回满），
        //    而 SetBloodMax 在 Godot 层一次都没被调用 ⇒ 储血单调递减、无恢复路径，长线必然见底。
        //
        // 挂在**既有休养系统**上（restFraction/bedFraction，与术后愈合同一套账），不另起炉灶：
        //   · 量：BloodRegenPerRestDay(10) × 休养占比 × 睡床加成（复用 BedSleepHealBonusPct=10 个百分点，加算，同族）
        //   · 70 储血 / 10 每昼夜 = **7 昼夜从零回满**，与「骨折愈合 7 昼夜」同量级。
        //   · **必须先止血**（BleedingWoundCount == 0，即伤口已被手术缝合）：还在流的口子边流边补是自欺欺人，
        //     也会架空用户「任何时候只要伤口没被手术治疗就会流血」这条规则。
        //     注意本行在上面的 StopBleed 同步**之后** ⇒ 本昼夜刚缝合的伤口，当天就能开始回血。
        // 规则本体是**纯函数** BloodRecovery.PerRestDay（在 HealthConditions.cs，已 Link 进单测）——
        // Pawn 只做"取参数 → 调规则 → 落 Body"这三步。规则若写在这个 Godot 节点里就**根本无法单测**。
        Body.RecoverBlood(BloodRecovery.PerRestDay(
            restFraction ?? (resting ? 1.0 : 0.0),
            bedFraction ?? (restedInBed ? 1.0 : 0.0),
            hasOpenWound: Body.BleedingWoundCount > 0));

        // 骨折治疗档回写 Body（能力系数三档：未治 -30% / 术后 -15% / 痊愈 0，见 Body.HandFractureOperationFactor/LegFractureMobilityFactor）：
        //  · 手术成功(IsOperated) → MarkFractureTreated：惩罚减半（-15%）。幂等，仅对 Body 仍骨折的部位生效。
        foreach (HealthCondition c in Health.Conditions)
        {
            if (c.Type == HealthConditionType.Fracture && c.BodyPart != null && c.IsOperated)
            {
                Body.MarkFractureTreated(c.BodyPart);
            }
        }
        //  · 康复清除/畸形封顶等使该部位不再有活跃骨折条目 → HealFracture：归零（同出血的存在性同步）。
        foreach (string part in Body.FracturedParts.ToList())
        {
            if (!Health.Conditions.Any(c => c.Type == HealthConditionType.Fracture && c.BodyPart == part))
            {
                Body.HealFracture(part);
            }
        }

        return result;
    }

    /// <summary>
    /// [SPEC-B14/终稿+补7] 推进本人感染竞速一个时间片（相位级 dt&lt;1 或整日 dt=1）：调 <see cref="HealthConditionSet.AdvanceInfectionRace"/>
    /// （感染进度累积、用药期间按档减缓+累进治疗进度、治疗先到顶清除、**感染到 100% 立刻死亡**——不再自动截肢）。
    /// 是否致死经返回结果 <see cref="InfectionRaceResult.Outcome"/> 交营地统一走死亡路径。保命的主动截肢走 <see cref="AmputateInfectedLimb"/>。
    /// </summary>
    /// <param name="dtDays">时间片天数（相位级传 1/相位数，整日传 1.0）。</param>
    /// <param name="medicated">本时段是否在用药（疗程指派且有药）。</param>
    /// <param name="medicine">本时段所用感染药（medicated 为真时给，取 Efficacy/WorsenMultiplier）。</param>
    /// <param name="campWorsenMultiplier">
    /// 全营感染条上升速度乘子（默认 1.0＝无光环，零回归）：山姆 3 级光环 ×0.97（<c>SamPerk.CampInfectionWorsenMultiplier</c>），
    /// 由 <c>CampMain</c> 按当前营地人数算好传入。与用药的 <c>Medicine.WorsenMultiplier</c> 相乘、互不吞没。
    /// </param>
    public InfectionRaceResult AdvanceInfectionRace(double dtDays, bool medicated, Medicine? medicine,
        double campWorsenMultiplier = 1.0)
        => Health.AdvanceInfectionRace(dtDays, medicated, medicine, campWorsenMultiplier);

    /// <summary>
    /// [SPEC-B14-补7] 主动截肢一处感染的肢体（玩家抉择的保命手术）：调 <see cref="HealthConditionSet.PerformAmputation"/> 判成败，
    /// **成功即就地 <see cref="Body.Sever"/> 断肢**（感染双条已清零；装备联动由每帧 ReconcileSeverance 兜底）。返回手术结果供 UI 出模糊反馈。
    /// </summary>
    public SurgeryResult AmputateInfectedLimb(
        HealthCondition infection, IReadOnlyList<string>? materials, bool onBed, IRandomSource rng,
        int surgeonBookBonus = 0, bool selfSurgery = false, double operationCapability = 1.0, int? surgeryBasePoints = null,
        int surgeonBookBonusNoSupplies = 0)
    {
        string? part = infection.BodyPart;
        SurgeryResult r = Health.PerformAmputation(infection, materials, onBed, rng, surgeonBookBonus, selfSurgery, operationCapability, surgeryBasePoints,
            surgeonBookBonusNoSupplies);
        if (r.Success && part != null)
        {
            Body.Sever(part); // 截肢成功：切除该肢
        }
        return r;
    }

    /// <summary>因伤病恶化不治身故：走统一非战斗死亡路径（触发 Died 事件 → 营地名单清理 + 可能触发全灭）。</summary>
    public void DieOfWounds() => KillNonCombat();

    protected override void Think(double delta)
    {
        // 断肢联动兜底（每帧、幂等、变更时才重投）：手/脚被切除或损毁后，同步持械模型（该手武器落地）
        // 与穿戴模型（该肢体上的穿戴品失效），再重组生效战斗数据。见 <see cref="ReconcileSeverance"/>。
        ReconcileSeverance();

        // 手持光源（light-items HANDOFF）：持光手被切除即落地；每帧同步自照亮强度（暴露链 Actor.ExposedCone 读 CarriedLightIntensity）。
        if (_heldLight.IsActive && _heldLight.HandUsed is Hand hlHand
            && (hlHand == Hand.Left ? _loadout.LeftHandLost : _loadout.RightHandLost))
        {
            _heldLight.Drop();
        }
        SyncCarriedLight();

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
            case PawnRole.Reading:
                // 仿 Guard Stationing：走向座位途中放行移动令，抵达（导航完成）即坐下开读。
                // 无座者不下移动令（Stationing=false），就地立即累进（-10% 速）。
                if (Stationing && IsNavigationFinished())
                    Stationing = false;
                if (HasMoveOrder && !Stationing)
                    CancelOrders();
                if (!Stationing && _assignedBook is { } book)
                    AccrueReading(delta, book);
                break;
        }
    }

    public static Pawn Create(string name, StartingWeapon weapon, Color color, IReadOnlyList<string>? extraApparel = null)
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

        // authored 背景在躯体上的开局痕迹（按名应用）：山姆开局左手缺小指+无名指——九岁那年为救诺蒂被疯狗咬掉，
        // 人称"小英雄"的代价。走引擎既有切除通则（−7%/指 操作惩罚，共 −14%），不豁免、不加码。见 SurvivorBackstory。
        SurvivorBackstory.ApplyTo(name, p.Body);

        // 通用技能系统已删——角色能力改由 authored 专属效果 + 读过的书承载，此处不再直设初始技能。
        // authored 专属效果按名授予：诺蒂天生"书虫"L1（读得快、越读越快）；南丁格尔天生"外科手"（手术点数池加点，[SPEC-B13]）；
        // 山姆天生"英雄风范"（耐揍/负重/全营光环——**等级不存在 Pawn 上**，由营地当前人数实时派生，见 SamPerk）。其余角色无 perk。
        if (name == "诺蒂")
            p.Perks.GrantBookworm();
        else if (name == NurseRecruit.NurseName)
            p.Perks.GrantNightingale();
        else if (name == SamPerk.SamName)
            p.Perks.GrantSam();
        else if (name == RatPerk.RatName)
            p.Perks.GrantRat(); // [T61] 耗子：下水道招募（等级同样**不存在 Pawn 上**，由累计搜出件数从 StoryFlags 派生，见 RatPerk）
        else if (name == PetePerk.PeteName)
            p.Perks.GrantPete(); // 皮特：Day7 敲门救援入队（等级**不存在 Pawn 上**，由"连续五天≥3"latch + "饥饿≤5出行三次"累计从 StoryFlags 派生，见 PetePerk）
        else if (name == ChristinePerk.ChristineName)
            p.Perks.GrantChristine(); // 克莉丝汀：教学关反水后收留入队（等级**不存在 Pawn 上**，由在营存活天数 + 灭金手指帮旗标派生，见 ChristinePerk）

        // 初始主手武器（authored「自带装备」，逐角色）：无=空手入队 / 手枪→远程 / 匕首·棍棒→近战。
        // EquipToHand 自动按 TwoHanded 分流；StartingWeapon.None 不发主手武器（多数幸存者 + 皮特空手入队）。
        Weapon? primary = weapon switch
        {
            StartingWeapon.Pistol => CombatData.Pistol(),
            StartingWeapon.Dagger => CombatData.Dagger(),
            StartingWeapon.Club => CombatData.Club(),
            _ => null,
        };
        if (primary is not null)
            p._loadout.EquipToHand(primary, Hand.Right);
        // 开局发三件基础衣物：长袖布衣（贴身层护上身）+ 长裤（裤子槽护腿）+ 一双运动鞋（左右脚各一只，[SPEC-B18-补]：
        // 鞋不分左右但一只占一只脚槽，故发两只才护住双脚——开局防护等价性不变）。
        // 不带皮夹克等特殊护甲——特殊装备/护甲只能靠搜刮/制作获得（[SPEC-B16-补·护甲纠错]）。走 EquipApparel 统一路径（目录占槽+登记防御层）。
        //
        // 🔴 [T45] 清单**不再写在这里**，改读纯逻辑的 SurvivorStartingKit —— 那是单一事实源。
        // 理由：负重的覆盖自检要能在**不起 Godot 的情况下**算出"一个新幸存者出门有多重"（本方法是 Godot 类型，
        // 进不了单测）。清单若只活在这段方法体里，测试就只能把名字抄一遍 ⇒ 两份事实源一漂移，
        // 「出门负重 ≠ 0」那条护栏就在无声中失效——这正是本项目反复踩的"纯逻辑绿≠功能生效"。
        foreach ((string item, EquipSlot? slot) in SurvivorStartingKit.Apparel)
        {
            p.EquipApparel(item, slot: slot);
        }
        // authored 逐角色额外穿戴品（默认空）：走同一条 EquipApparel 统一路径（目录占槽 + 登记防御层）。
        // 现阶段唯一来源＝道格的墨镜（占眼镜槽，slot 由目录自解析）——保持接线单一，不另开旁路。
        if (extraApparel is not null)
        {
            foreach (string item in extraApparel)
            {
                p.EquipApparel(item);
            }
        }
        // 由两模型投影出生效战斗数据：AttackWeapon=PrimaryWeapon(+手感/IsRanged)、DefenderArmor=已穿护甲层。
        p.SyncCombatFromEquipment();
        return p;
    }

    /// <summary>
    /// 拍一份只读检视快照给"角色面板 UI"读取。内部就地读自身 Body/AttackWeapon/DefenderArmor
    /// （皆为受保护的可变引擎对象），构造纯数据 <see cref="PawnInspection"/> —— UI 只拿死数据、改不坏战斗。
    /// </summary>
    public PawnInspection Inspect() =>
        PawnInspection.FromBody(Body, AttackWeapon, DefenderArmor, DisplayName, Hunger.Value, Hunger.Level.Label(), Health.Conditions);

    /// <summary>头顶状态条快照并入伤病集（含感染），使感染在头顶常驻可见（非仅医疗面板）。</summary>
    protected override PawnInspection BuildStatusInspection() => Inspect();

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

    /// <summary>
    /// [SPEC-B14-补8] 以**手术**方式给一个失去的肢体安装假肢：走 <see cref="HealthConditionSet.PerformProstheticSurgery"/> 判成败，
    /// **成功才 <see cref="EquipProsthetic"/> 装上**（假肢本体消耗由营地在成功时扣；失败默认不损耗、可重试）。返回手术结果供 UI 出模糊反馈。
    /// </summary>
    public SurgeryResult InstallProstheticSurgery(
        BodyRegion replacesRegion, ProstheticGrade grade, IReadOnlyList<string>? materials, bool onBed, IRandomSource rng,
        int surgeonBookBonus = 0, bool selfSurgery = false, double operationCapability = 1.0, int? surgeryBasePoints = null,
        int surgeonBookBonusNoSupplies = 0)
    {
        SurgeryResult r = Health.PerformProstheticSurgery(materials, onBed, rng, surgeonBookBonus, selfSurgery, operationCapability, surgeryBasePoints,
            surgeonBookBonusNoSupplies);
        if (r.Success)
        {
            EquipProsthetic(replacesRegion, grade); // 成功：假肢就位、能力恢复即时重算
        }
        return r;
    }

    /// <summary>假肢等级中文显示名（木制/简易/仿生）。</summary>
    private static string ProstheticDisplayName(ProstheticGrade grade) => grade switch
    {
        ProstheticGrade.Wooden => "木制假肢",
        ProstheticGrade.Simple => "简易假肢",
        ProstheticGrade.Bionic => "仿生假肢",
        _ => "假肢",
    };

    // ================= §6 装备穿脱 API（供装备 UI 从库存调） =================
    // 约定：入参为标识名（= 库存 Item.RefKey：武器名 / 护甲名）。武器名经 ModdedWeaponRegistry.WeaponByName、
    // 护甲名经 ApparelCatalog(占槽/覆盖) + ArmorLayerCatalog(防御数值) 解析。无法解析 → 返回 false，不改状态。
    // 每次穿脱后必调 SyncCombatFromEquipment() 重投生效战斗数据（AttackWeapon/DefenderArmor/…）。

    // ⚠️ 这里**曾经**有一个 `WeaponCatalog = WeaponTable.Arsenal().ToDictionary(...)` 的静态字典，
    // 它是个 P0 陷阱：Arsenal 只含**原厂**武器，而玩家改装出来的「步枪（刺刀型）」是**运行时注册**的变体
    // ⇒ 按名回查落空 ⇒ EquipWeapon 静默返 false ⇒ **玩家花了材料+工时改出来的枪，永远拿不起来**
    //   （连带：利爪/创伤/刺刀三种枪托型态永远进不了战斗——枪都拿不到手里）。
    // **那个字典已被整个删除，而不是"改成也查一下注册表"** —— 只要它还在，下一个人就还会去 TryGetValue 它。
    // 现在武器名回查**只有一个入口**：ModdedWeaponRegistry.WeaponByName（先原厂表、后改装表；
    // 对原厂武器行为完全不变 ⇒ 零回归）。CarryWeight / SaveMapper / CraftingPanel 走的也是它。

    /// <summary>
    /// 护甲名 → 生效护甲层（含防御数值）。汇集数据表『护甲表』的人形 14 件（[SPEC-B18]）：
    /// 开局三件套、花衬衫、短裤、外套五件（粗布背心/粗布外套/布夹克/牛仔外套/皮夹克）、护甲层三件（皮革胸甲/皮甲/板甲）、劳保手套。
    /// 纯覆盖品（防毒面具）无护甲数值，不在此表。狗装备另走 <see cref="DogGearCatalog"/>。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ArmorLayer> ArmorLayerCatalog = BuildArmorLayerCatalog();

    private static Dictionary<string, ArmorLayer> BuildArmorLayerCatalog()
    {
        var d = new Dictionary<string, ArmorLayer>();
        foreach (ArmorLayer l in new[]
        {
            ArmorTable.LongSleeveShirt(), ArmorTable.FloralShirt(),
            ArmorTable.Trousers(), ArmorTable.Sneakers(), ArmorTable.Shorts(),
            ArmorTable.CoarseClothVest(), ArmorTable.CoarseClothCoat(),
            ArmorTable.ClothJacket(), ArmorTable.DenimJacket(), ArmorTable.LeatherJacket(),
            ArmorTable.ChestPlate(), ArmorTable.Leather(), ArmorTable.Plate(),
            ArmorTable.MilitaryHelmet(), ArmorTable.RiotHelmet(), ArmorTable.WorkGloves(),
        })
        {
            d[l.Name] = l;
        }
        return d;
    }

    /// <summary>本 Pawn 躯体当前已不存在（切除/损毁）的部位名集合——喂给穿戴/持械模型的断肢入参。</summary>
    private IReadOnlySet<string> SeveredParts()
        => Body.Parts.Keys.Where(Body.IsGone).ToHashSet();

    /// <summary>
    /// 穿一件武器到某手（双手武器自动占两手）。断手/双持约束不满足则拒绝、状态不变。返回是否穿上。
    /// <para>
    /// 手持光源门槛（<see cref="HeldLightState.BlocksWeaponEquip"/>）：<b>双手武器与光源互斥</b>——正举着火把
    /// 就装不上步枪（需先 <see cref="UnequipLight"/>）。单手武器若首选手正被光源占用，则落到另一只手
    /// （与 <see cref="EquipLight"/> 首选不成试另一手的范式对称），两手皆不可才拒绝。
    /// </para>
    /// </summary>
    public bool EquipWeapon(string weaponName, Hand hand)
    {
        ReconcileSeverance(); // 先把最新断肢态同步进持械模型，避免在已断的手上穿
        // 先原厂表、后改装表——改装出来的变体（"步枪（刺刀型）"）也必须装得上。
        if (ModdedWeaponRegistry.WeaponByName(weaponName) is not { } w)
        {
            return false;
        }

        if (HeldLightState.BlocksWeaponEquip(_heldLight, w, hand))
        {
            // 双手武器 → 两只手都被挡（与光源互斥），直接拒绝；单手武器 → 只有持光那只手被挡，改用另一只手。
            Hand other = hand == Hand.Left ? Hand.Right : Hand.Left;
            if (HeldLightState.BlocksWeaponEquip(_heldLight, w, other))
            {
                return false;
            }
            hand = other;
        }

        if (!_loadout.EquipToHand(w, hand))
        {
            return false;
        }
        SyncCombatFromEquipment();
        return true;
    }

    /// <summary>
    /// 把一把武器双手持握（双手武器，或单手武器改双手握——**无攻速加成**）：占两手。任一手断、或正持手持光源
    /// （双手握需两手俱在，与光源互斥——见 <see cref="HeldLightState.BlocksTwoHandedEquip"/>）则拒绝。返回是否穿上。
    /// </summary>
    public bool EquipWeaponTwoHanded(string weaponName)
    {
        ReconcileSeverance();
        if (HeldLightState.BlocksTwoHandedEquip(_heldLight))
        {
            return false; // 举着火把腾不出两只手：先放下光源
        }
        // 同上：改装变体也要能双手握。
        if (ModdedWeaponRegistry.WeaponByName(weaponName) is not { } w || !_loadout.EquipTwoHanded(w))
        {
            return false;
        }
        SyncCombatFromEquipment();
        return true;
    }

    /// <summary>卸下某手武器（双手握则两手一起清空）。</summary>
    public void UnequipWeapon(Hand hand)
    {
        _loadout.Unequip(hand);
        SyncCombatFromEquipment();
    }

    // ───────────────────────── [T47] 消耗型改装：打一下掉一次，用光即脱落 ─────────────────────────

    /// <summary>
    /// **手上这把武器的改装被磨没了**（锋刃研磨砍满三下）。营地层订阅它去：
    /// ① 把库存/手上的那件东西换成 <c>NewWeaponName</c>；② 给玩家一行提示（<b>不能静默失效</b>）。
    /// <para>
    /// 事件而不是直接在这里改库存：<c>Pawn</c> 不认识 <c>InventoryStore</c>（那是营地的东西），
    /// 而战斗可能发生在探索关。谁持有库存谁去换 —— 这是既有的分层。
    /// </para>
    /// </summary>
    public event System.Action<Pawn, string /*旧变体名*/, string /*新武器名*/, IReadOnlyList<string> /*脱落的改装*/>? WeaponModBroken;

    /// <summary>
    /// 一次攻击真的打出去了 ⇒ 手上武器的消耗型改装掉一次。用光 ⇒ 改装脱落，武器当场换成脱落后的样子。
    /// <para>
    /// 绝大多数情况下这是**一次字典 miss 就返回**（原厂武器 / 永久改装都没有耐久条目）⇒ 热路径开销可忽略。
    /// </para>
    /// </summary>
    protected override void OnAttackDelivered(Weapon used)
    {
        if (used is null) return;

        ModWearResult wear = ModdedWeaponRegistry.ConsumeUse(used.Name);
        if (!wear.Changed) return;   // 没有消耗型改装，或还没用光 —— 绝大多数攻击走这条

        // 改装脱落：把手上这把换成脱落后的武器（可能是另一个变体，也可能是回落的基础武器）。
        // 先记下它在哪只手、是不是双手握 —— 换完要原样放回去。
        bool twoHanded = _loadout.Grip == GripMode.TwoHanded;
        Hand hand = _loadout.RightHand is not null ? Hand.Right : Hand.Left;

        UnequipWeapon(hand);
        bool re = twoHanded ? EquipWeaponTwoHanded(wear.WeaponName) : EquipWeapon(wear.WeaponName, hand);
        if (!re)
        {
            // 理论上不该发生（脱落后的武器一定是登记过的变体或原厂武器）。真发生了也不能把人打成空手
            // 而无人知晓 —— 事件照发，营地层会把这把武器退回库存。
            SyncCombatFromEquipment();
        }

        WeaponModBroken?.Invoke(this, used.Name, wear.WeaponName, wear.BrokenModNames);
    }

    /// <summary>
    /// 持起一件手持光源（<paramref name="key"/> 对齐 <see cref="LightSource"/> 目录，如手电/火把）：占一只手，
    /// 读 <see cref="WeaponLoadout"/> 校验（断手/双手武器占两手/该手已持械→拒绝）。首选 <paramref name="hand"/>，不行试另一手；
    /// 成功即同步自照亮强度（暴露链自动通）。返回是否持起。
    /// </summary>
    public bool EquipLight(string key, Hand hand = Hand.Left)
    {
        if (LightSource.Find(key) is not LightProfile profile)
        {
            return false;
        }
        ReconcileSeverance(); // 先同步断肢态，避免往已断的手上持
        if (!_heldLight.TryHold(profile, hand, _loadout))
        {
            Hand other = hand == Hand.Left ? Hand.Right : Hand.Left;
            if (!_heldLight.TryHold(profile, other, _loadout))
            {
                return false;
            }
        }
        SyncCarriedLight();
        return true;
    }

    /// <summary>放下手持光源（返回被放下的光源 key，无则 null），供调用方回库存。</summary>
    public string? UnequipLight()
    {
        string? key = _heldLight.Held?.Key;
        _heldLight.Drop();
        SyncCarriedLight();
        return key;
    }

    /// <summary>把手持光源强度同步给 <see cref="Actor"/> 自照亮（<see cref="Actor.ExposedCone"/> 暴露链据此计算，无光=0）。</summary>
    private void SyncCarriedLight() => SetCarriedLight(_heldLight.Held?.Intensity ?? 0f);

    /// <summary>
    /// 穿一件穿戴品（护甲名）。占槽/覆盖：目录品走 <see cref="ApparelCatalog"/>（如左右手套→对应手槽）；
    /// 未登记的原始护甲层走其 <see cref="ArmorLayer.Slot"/>→躯干层槽。断肢槽被禁用则拒绝。默认顶替同槽旧装备。返回是否穿上。
    /// </summary>
    public bool EquipApparel(string apparelName, bool replace = true, EquipSlot? slot = null)
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get(apparelName);
        ArmorLayerCatalog.TryGetValue(apparelName, out ArmorLayer? layer);

        EquipOutcome outcome;
        if (def is not null)
        {
            // 目录品（含成对品：手套/鞋一件占一只手/脚，slot 不给则自动挑空闲那侧）。
            outcome = ApparelCatalog.Equip(_apparel, apparelName, slot, SeveredParts(), replace);
        }
        else if (layer is not null)
        {
            outcome = _apparel.TryEquip(
                apparelName, TorsoSlotSet(layer.Slot), out _, layer.CoversParts, SeveredParts(), replace);
        }
        else
        {
            return false; // 既非目录品、又无具名护甲层：无法解析
        }

        if (outcome != EquipOutcome.Equipped)
        {
            return false;
        }
        // 有护甲数值的登记进 name→layer；纯覆盖品清掉可能的旧登记。
        if (layer is not null) _apparelLayers[apparelName] = layer; else _apparelLayers.Remove(apparelName);
        SyncCombatFromEquipment();
        return true;
    }

    /// <summary>卸下某名穿戴品的<b>全部</b>在身件（成对品两只一起脱；只脱一只用 <see cref="UnequipApparelAt"/>）。</summary>
    public void UnequipApparel(string apparelName)
    {
        if (_apparel.Unequip(apparelName))
        {
            _apparelLayers.Remove(apparelName);
            SyncCombatFromEquipment();
        }
    }

    /// <summary>
    /// 卸下占用某槽的那一件（成对品只脱这一只，同名的另一只留在身上，[SPEC-B18-补]）。
    /// 返回被卸下的装备名（该槽本空则 null）。
    /// </summary>
    public string? UnequipApparelAt(EquipSlot slot)
    {
        string? removed = _apparel.UnequipSlot(slot);
        if (removed is null)
        {
            return null;
        }
        if (!_apparel.IsEquipped(removed)) _apparelLayers.Remove(removed);
        SyncCombatFromEquipment();
        return removed;
    }

    /// <summary>护甲层 <see cref="ArmorSlot"/> → 躯干三层穿戴槽（局部护甲如手套不走此路，另由目录定义占手/脚槽）。</summary>
    private static IReadOnlySet<EquipSlot> TorsoSlotSet(ArmorSlot slot) => slot switch
    {
        ArmorSlot.Plate => new HashSet<EquipSlot> { EquipSlot.PlateLayer },
        ArmorSlot.Outer => new HashSet<EquipSlot> { EquipSlot.OuterLayer },
        ArmorSlot.Skin => new HashSet<EquipSlot> { EquipSlot.SkinLayer },
        _ => new HashSet<EquipSlot>(),
    };

    /// <summary>
    /// 把两模型的当前态投影回 <see cref="Actor"/> 的战斗消费字段：
    /// 武器 = 主手武器（并按远程/近战套用手感：IsRanged/AttackRange/AttackCooldown）；护甲 = 已穿护甲层。
    /// <b>空手（无主手武器）= 拳脚</b>（<see cref="CombatData.Fists"/>，钝伤近战）——旧口径"保留上一件武器手感"
    /// 是空手未建模时的权宜（卸了长剑还在拿长剑的数值打人），现已由天生武器「拳脚」取代。
    /// </summary>
    private void SyncCombatFromEquipment()
    {
        // 空手 → 拳脚（用户拍板：空手近战＝钝伤）。持弓弩者的近战同样等于空手，但那是**出手那一刻**的
        // 降级（Actor.TryAttack 走 Unarmed.MeleeFor），不改这里的持械投影——弓仍是弓，要靠它射箭。
        Weapon w = _loadout.PrimaryWeapon ?? CombatData.Fists();

        AttackWeapon = w;
        // 冷却读武器权威间隔（WeaponTable 全局慢节奏值），不再硬编码手感常量；GripMode/操作能力系数仍在 EffectiveAttackInterval 叠乘。
        AttackCooldown = w.AttackInterval;
        if (w.IsRanged)
        {
            IsRanged = true;
            AttackRange = 260f;   // 远程：中距离（拟定待调；远程交战门权威口径为武器 MaxRange，此值主要作近战兜底）
        }
        else
        {
            IsRanged = false;
            AttackRange = 26f;    // 近战（拟定待调；空手同近战——要凑到跟前才打得着）
        }

        DefenderArmor = BuildDefenderArmor();
    }

    /// <summary>
    /// 由当前已穿护甲品组出生效护甲层列表（纯覆盖品无层、跳过）。层序归一交给 CombatResolver。
    /// <b>逐件</b>取（成对品两只 = 两层同名甲，各只覆盖自己那一侧的手/脚）——覆盖以实际穿戴那一侧为准，
    /// 防御数值仍来自 <see cref="ArmorLayerCatalog"/>（[SPEC-B18-补]）。
    /// </summary>
    private IReadOnlyList<ArmorLayer> BuildDefenderArmor()
        => _apparel.ActiveCoverage()
            .Where(c => _apparelLayers.ContainsKey(c.Item))
            .Select(c => WithCoverage(_apparelLayers[c.Item], c.CoversParts))
            .ToList();

    /// <summary>把某件的护甲数值与它"这一件实际覆盖的部位"合成一层（覆盖为空则沿用护甲表口径）。</summary>
    private static ArmorLayer WithCoverage(ArmorLayer layer, IReadOnlySet<string> covers)
        => covers.Count == 0 || (layer.CoversParts is not null && covers.SetEquals(layer.CoversParts))
            ? layer
            : new ArmorLayer
            {
                Name = layer.Name,
                Description = layer.Description,
                SharpDefense = layer.SharpDefense,
                BluntDefense = layer.BluntDefense,
                Weight = layer.Weight,
                Slot = layer.Slot,
                CoversParts = covers,
            };

    /// <summary>
    /// 断肢联动兜底：手/脚被切除或损毁后，同步持械模型（该手武器落地）与穿戴模型（该肢体穿戴品失效），
    /// 变更时重投战斗数据。幂等——已处理的手（_loadout 已记断手）/已空的槽不再触发；每帧调用开销为常数级查表。
    /// </summary>
    private void ReconcileSeverance()
    {
        bool changed = false;

        if (Body.IsGone(HumanBody.LeftHand) && !_loadout.LeftHandLost)
        {
            _loadout.NotifyHandLost(Hand.Left);
            changed = true;
        }
        if (Body.IsGone(HumanBody.RightHand) && !_loadout.RightHandLost)
        {
            _loadout.NotifyHandLost(Hand.Right);
            changed = true;
        }

        // 断肢部位上的穿戴品（手套/鞋等）失效：卸下该槽装备。IsOccupied 守卫保证幂等。
        foreach (KeyValuePair<EquipSlot, string> kv in ApparelSlots.SlotAnchor)
        {
            if (Body.IsGone(kv.Value) && _apparel.IsOccupied(kv.Key))
            {
                string? removed = _apparel.UnequipSlot(kv.Key);
                // 同名另一只（如另一只手的手套）还在身时保留其防御层登记。
                if (removed is not null && !_apparel.IsEquipped(removed)) _apparelLayers.Remove(removed);
                changed = true;
            }
        }

        if (changed)
        {
            SyncCombatFromEquipment();
        }
    }
}
