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

    /// <summary>本 Pawn 逐书阅读进度（跨夜持久）。见 <see cref="ReadingProgress"/>。</summary>
    private readonly ReadingProgress _readingProgress = new();

    /// <summary>当前被指派在读的书 id（未在读为 <c>null</c>）。读满或结束后清空。</summary>
    public string? AssignedBookId { get; private set; }

    /// <summary>当前认领的座位（无座为 <c>null</c>，按 -10% 读速就地读）。由 CampMain 认领/释放，Pawn 只持有并据其判有无座。</summary>
    public CampMain.SeatClaim? ReadingSeat { get; set; }

    /// <summary>当前在读书的底层数据（供读满时置全局已读 + 取 ReadHours 判完成）。</summary>
    private BookData? _assignedBook;

    /// <summary>本夜有效的全营读速加成汇总（由 CampMain 遍历全体求和喂入，含读者自身贡献）。</summary>
    private double _campWideReadingBonus;

    /// <summary>一夜的实时长度（游戏内秒，受时标缩放同口径）：把每帧 delta 换算成游戏内小时（一整夜=12 小时）。</summary>
    private double _nightLengthSeconds;

    /// <summary>
    /// 开始一次读书指派：记下要读的书、全营加成汇总、夜长（换算时间用）。座位由 CampMain 另行认领并置
    /// <see cref="ReadingSeat"/>。之后每帧 <see cref="Think"/> Reading 分支累进阅读进度。
    /// </summary>
    public void BeginReading(BookData book, double campWideBonusSum, double nightLengthSeconds)
    {
        _assignedBook = book;
        AssignedBookId = book.Id;
        _campWideReadingBonus = campWideBonusSum;
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
        double speed = ReadingSpeed.Effective(1.0, Perks.SelfReadingSpeedBonus, hasSeat, _campWideReadingBonus, prereqFactor);
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

    /// <summary>当前主攻武器（= <see cref="WeaponLoadout.PrimaryWeapon"/>，与 <see cref="Actor.AttackWeapon"/> 同源）。</summary>
    public Weapon? PrimaryWeapon => _loadout.PrimaryWeapon;

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
    /// 施术者操作能力 0..1（= 1 − 残疾操作惩罚，再并饥饿净值；断手/饥饿拉低）。喂给
    /// <see cref="HealthConditionSet.PerformSurgery"/> 的 operationCapability（与战斗出手间隔同源口径）。
    /// </summary>
    public double OperationCapability =>
        System.Math.Clamp(
            HungerState.CombineCapability(Body.DisabilityModifiers.OperationPenalty, HungerAbilityPenalty),
            0.0, 1.0);

    /// <summary>本幸存者已读书 id 集（供 <see cref="MedicalBookPoints.SumFor"/> 求施术者医疗书加点）。</summary>
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
    /// <param name="resting">本昼夜是否卧床休养（减缓感染/疾病恶化、加速术后愈合）。</param>
    public HealthTickResult AdvanceHealthDay(IRandomSource rng, bool resting, bool restedInBed = false)
    {
        ArchiveWounds();
        HealthTickResult result = Health.TickDay(rng, resting, restedInBed);

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

        // 通用技能系统已删——角色能力改由 authored 专属效果 + 读过的书承载，此处不再直设初始技能。
        // 首个 authored 专属效果：诺蒂天生"书虫"L1（读得快、越读越快）。其余角色无 perk。
        if (name == "诺蒂")
            p.Perks.GrantBookworm();

        // 初始武器进【持械模型】主手（右手）：手枪→远程、匕首→近战。EquipToHand 自动按 TwoHanded 分流。
        p._loadout.EquipToHand(usePistol ? CombatData.Pistol() : CombatData.Dagger(), Hand.Right);
        // 初始护甲两层（皮夹克/贴身布衣）进【穿戴模型】对应躯干层槽。
        foreach (ArmorLayer layer in CombatData.SurvivorArmor())
        {
            p.EquipArmorLayer(layer);
        }
        // 由两模型投影出生效战斗数据：AttackWeapon=PrimaryWeapon(+手感/IsRanged)、DefenderArmor=已穿护甲层。
        // 与旧逻辑等价：手枪→range260/cd1.1/远程；匕首→range26/cd0.7/近战；护甲=SurvivorArmor 两层。
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

    /// <summary>假肢等级中文显示名（木制/简易/仿生）。</summary>
    private static string ProstheticDisplayName(ProstheticGrade grade) => grade switch
    {
        ProstheticGrade.Wooden => "木制假肢",
        ProstheticGrade.Simple => "简易假肢",
        ProstheticGrade.Bionic => "仿生假肢",
        _ => "假肢",
    };

    // ================= §6 装备穿脱 API（供装备 UI 从库存调） =================
    // 约定：入参为标识名（= 库存 Item.RefKey：武器名 / 护甲名）。武器名经 WeaponCatalog、
    // 护甲名经 ApparelCatalog(占槽/覆盖) + ArmorLayerCatalog(防御数值) 解析。无法解析 → 返回 false，不改状态。
    // 每次穿脱后必调 SyncCombatFromEquipment() 重投生效战斗数据（AttackWeapon/DefenderArmor/…）。

    /// <summary>玩家/敌方可用武器名 → 武器工厂输出（取自 <see cref="WeaponTable.Arsenal"/>，含手枪/匕首等 14 种）。</summary>
    private static readonly IReadOnlyDictionary<string, Weapon> WeaponCatalog =
        WeaponTable.Arsenal().ToDictionary(w => w.Name);

    /// <summary>
    /// 护甲名 → 生效护甲层（含防御数值）。汇集当前所有具名护甲层：SurvivorArmor 两层（皮夹克/贴身布衣）、
    /// 参数化甲层（布衣/皮甲/板甲/粗布外套/左右手套），并把目录多槽品"一体板甲"暂借板甲数值（数值待扩，
    /// 见 <see cref="ApparelCatalog"/> 注释）。纯覆盖品（防毒面具）无护甲数值，不在此表。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ArmorLayer> ArmorLayerCatalog = BuildArmorLayerCatalog();

    private static Dictionary<string, ArmorLayer> BuildArmorLayerCatalog()
    {
        var d = new Dictionary<string, ArmorLayer>();
        foreach (ArmorLayer l in ArmorTable.SurvivorArmor()) d[l.Name] = l;         // 皮夹克 / 贴身布衣
        foreach (ArmorLayer l in new[]
        {
            ArmorTable.Cloth(), ArmorTable.Leather(), ArmorTable.Plate(),
            ArmorTable.CoarseClothCoat(), ArmorTable.WorkGlove(leftHand: true), ArmorTable.WorkGlove(leftHand: false),
        })
        {
            d[l.Name] = l;
        }
        // 目录多槽品"一体板甲"数值待扩：暂借板甲防御（换名，占槽/覆盖仍走 ApparelCatalog）。
        ArmorLayer plate = ArmorTable.Plate();
        d["一体板甲"] = new ArmorLayer
        {
            Name = "一体板甲", Slot = plate.Slot,
            SharpDefense = plate.SharpDefense, BluntDefense = plate.BluntDefense, Weight = plate.Weight,
        };
        return d;
    }

    /// <summary>本 Pawn 躯体当前已不存在（切除/损毁）的部位名集合——喂给穿戴/持械模型的断肢入参。</summary>
    private IReadOnlySet<string> SeveredParts()
        => Body.Parts.Keys.Where(Body.IsGone).ToHashSet();

    /// <summary>穿一件武器到某手（双手武器自动占两手）。断手/双持约束不满足则拒绝、状态不变。返回是否穿上。</summary>
    public bool EquipWeapon(string weaponName, Hand hand)
    {
        ReconcileSeverance(); // 先把最新断肢态同步进持械模型，避免在已断的手上穿
        if (!WeaponCatalog.TryGetValue(weaponName, out Weapon? w) || !_loadout.EquipToHand(w, hand))
        {
            return false;
        }
        SyncCombatFromEquipment();
        return true;
    }

    /// <summary>把一把武器双手持握（双手武器，或单手武器改双手握 +15%）：占两手。任一手断则拒绝。返回是否穿上。</summary>
    public bool EquipWeaponTwoHanded(string weaponName)
    {
        ReconcileSeverance();
        if (!WeaponCatalog.TryGetValue(weaponName, out Weapon? w) || !_loadout.EquipTwoHanded(w))
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
    public bool EquipApparel(string apparelName, bool replace = true)
    {
        ApparelCatalog.ApparelDef? def = ApparelCatalog.Get(apparelName);
        ArmorLayerCatalog.TryGetValue(apparelName, out ArmorLayer? layer);

        IReadOnlySet<EquipSlot> slots;
        IReadOnlySet<string>? covers;
        if (def is not null)
        {
            slots = def.Slots;
            covers = def.CoversParts;
        }
        else if (layer is not null)
        {
            slots = TorsoSlotSet(layer.Slot);
            covers = layer.CoversParts;
        }
        else
        {
            return false; // 既非目录品、又无具名护甲层：无法解析
        }

        EquipOutcome outcome = _apparel.TryEquip(apparelName, slots, out _, covers, SeveredParts(), replace);
        if (outcome != EquipOutcome.Equipped)
        {
            return false;
        }
        // 有护甲数值的登记进 name→layer；纯覆盖品清掉可能的旧登记。
        if (layer is not null) _apparelLayers[apparelName] = layer; else _apparelLayers.Remove(apparelName);
        SyncCombatFromEquipment();
        return true;
    }

    /// <summary>卸下某件穿戴品（连带其占的全部槽）。</summary>
    public void UnequipApparel(string apparelName)
    {
        if (_apparel.Unequip(apparelName))
        {
            _apparelLayers.Remove(apparelName);
            SyncCombatFromEquipment();
        }
    }

    /// <summary>把一层原始护甲穿进对应躯干层槽（仅供初始填充/躯干层：Plate/Outer/Skin→层槽），并登记其防御数值。</summary>
    private EquipOutcome EquipArmorLayer(ArmorLayer layer)
    {
        EquipOutcome outcome = _apparel.TryEquip(
            layer.Name, TorsoSlotSet(layer.Slot), out _, layer.CoversParts, SeveredParts(), replace: true);
        if (outcome == EquipOutcome.Equipped)
        {
            _apparelLayers[layer.Name] = layer;
        }
        return outcome;
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
    /// 空手（无主手武器）时保留上一件武器手感（空手战斗未建模，无拳头武器数据——见遗留决策点）。
    /// </summary>
    private void SyncCombatFromEquipment()
    {
        if (_loadout.PrimaryWeapon is { } w)
        {
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
                AttackRange = 26f;    // 近战（拟定待调）
            }
        }

        DefenderArmor = BuildDefenderArmor();
    }

    /// <summary>由当前已穿护甲品组出生效护甲层列表（纯覆盖品无层、跳过）。层序归一交给 CombatResolver。</summary>
    private IReadOnlyList<ArmorLayer> BuildDefenderArmor()
        => _apparel.EquippedItems
            .Where(_apparelLayers.ContainsKey)
            .Select(name => _apparelLayers[name])
            .ToList();

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
                if (removed is not null) _apparelLayers.Remove(removed);
                changed = true;
            }
        }

        if (changed)
        {
            SyncCombatFromEquipment();
        }
    }
}
