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

    // ---- §6 装备两模型：穿戴槽（护甲/穿戴品）+ 持械（左右手武器）----
    // Pawn 是唯一持有者与生效点：两模型只管"穿在哪/持在哪"的槽位规则（定稿、纯逻辑），
    // 由 Pawn 把它们的结果**投影**回 Actor 的战斗消费字段（AttackWeapon / DefenderArmor / IsRanged /
    // AttackRange / AttackCooldown）。任何穿脱后都重投一次，保证战斗读到的永远是当前装备态。

    /// <summary>穿戴态（11 槽：头/眼/面/躯干三层/左右手/裤子/左右脚）。见 <see cref="ApparelSlots"/>。</summary>
    private readonly ApparelSlots _apparel = new();

    /// <summary>持械态（左右手各一把，推导 <see cref="GripMode"/>）。见 <see cref="WeaponLoadout"/>。</summary>
    private readonly WeaponLoadout _loadout = new();

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

    /// <summary>饥饿刻度已归 0（饿死）。由聚餐结算据此走统一死亡路径。</summary>
    public bool IsStarvedToDeath => Hunger.IsStarved;

    /// <summary>饿死：走统一非战斗死亡路径（触发 Died 事件 + 移出场，复用现有死亡消费）。</summary>
    public void StarveToDeath() => KillNonCombat();

    protected override void Think(double delta)
    {
        // 断肢联动兜底（每帧、幂等、变更时才重投）：手/脚被切除或损毁后，同步持械模型（该手武器落地）
        // 与穿戴模型（该肢体上的穿戴品失效），再重组生效战斗数据。见 <see cref="ReconcileSeverance"/>。
        ReconcileSeverance();

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

        // 通用技能系统已删——角色能力改由 authored 专属效果 + 读过的书承载，此处不再直设初始技能。

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
            if (w.IsRanged)
            {
                IsRanged = true;
                AttackRange = 260f;   // 远程：中距离（拟定待调；远程交战门权威口径为武器 MaxRange，此值主要作近战兜底）
                AttackCooldown = 1.1; // 拟定待调（沿用旧手枪手感；GripMode 攻速系数为后续消费步）
            }
            else
            {
                IsRanged = false;
                AttackRange = 26f;    // 近战（拟定待调）
                AttackCooldown = 0.7; // 拟定待调（沿用旧匕首手感）
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
