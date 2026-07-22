using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat; // ArmorSlot / HumanBody（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载 §6 装备槽系统的全部规则：11 个穿戴槽的占用/校验/断肢禁装/替换/卸装，
// 以及"哪些身体部位被护甲覆盖"的聚合（供战斗层 DefenderArmor 只读消费）。
// **只管"穿在哪 / 防哪些部位"两件事**——不碰武器持握槽（主手/副手是独立系统）、
// 不碰护甲防御数值（数值在 DeadSignal.Combat.ArmorTable，本类只做槽位与覆盖）。

/// <summary>
/// §6 拍板的 11 个穿戴槽。武器主手/副手为独立持握槽，**不在此枚举内**。
/// 躯干三层（贴身/外套/装甲）与 <see cref="ArmorSlot"/> 一一对应（见 <see cref="ApparelSlots.ToArmorSlot"/>）。
/// 四肢按左右分独立槽——因断肢机制：断某侧则该侧槽失效（见 <see cref="ApparelSlots.SlotAnchor"/>）。
/// </summary>
public enum EquipSlot
{
    Head,       // 头部
    Eyes,       // 眼镜
    Face,       // 面部
    SkinLayer,  // 贴身层（躯干最内）
    OuterLayer, // 外套层（躯干中间）
    PlateLayer, // 装甲层（躯干最外）
    LeftHand,   // 左手
    RightHand,  // 右手
    Pants,      // 裤子
    LeftFoot,   // 左脚
    RightFoot,  // 右脚
}

/// <summary>一次 <see cref="ApparelSlots.TryEquip"/> 的结果。</summary>
public enum EquipOutcome
{
    /// <summary>已成功穿戴（占用全部声明槽）。</summary>
    Equipped,
    /// <summary>目标槽为空集，未声明占用任何槽。</summary>
    BlockedNoSlots,
    /// <summary>目标槽含被断肢禁用的槽（该侧肢体已切除，无处可穿）。</summary>
    BlockedSeveredLimb,
    /// <summary>目标槽含已被占用的槽，且未开启替换。</summary>
    BlockedSlotOccupied,
}

/// <summary>
/// 单个角色的穿戴态：槽 → 已装物品（物品以标识 string 表示，如护甲名）。
/// 一件装备可占单槽（粗布外套=外套层）/多槽（防毒面具=眼镜+面部、一体板甲=装甲层+裤子）/
/// 成对（左手套=左手、右手套=右手 各一件）。断肢感知全部走**入参**（哪些部位已切除），
/// 不直接耦合 Body/Godot，保持纯逻辑可测。
/// </summary>
public sealed class ApparelSlots
{
    // 一件"在身的穿戴实例"：物品标识 + 它占的槽 + 它覆盖的部位。
    // 键是**实例**而非物品名——因为成对品（劳保手套/运动鞋）不分左右，同名可同时在身两件
    // （一只左手/一只右手），各覆盖自己那一边（[SPEC-B18-补]）。
    private sealed record Worn(string Item, HashSet<EquipSlot> Slots, IReadOnlySet<string> Covers);

    // 槽 → 占用它的穿戴实例（多槽装备如板甲会让多个槽映射到同一实例）。
    private readonly Dictionary<EquipSlot, Worn> _slotOwner = new();
    // 全部在身实例（同名可有多件）。
    private readonly List<Worn> _worn = new();

    /// <summary>
    /// 每个"可被断肢禁用"的槽 → 该槽依附的身体部位名（<see cref="HumanBody"/> 常量）。
    /// 该部位在入参断肢集合中 → 对应槽不可用（无处可穿；改假肢后由上层把它移出断肢集合即恢复）。
    /// 头/眼/面/躯干三层/裤子无肢体依附——恒可用（头/躯干失去即死亡，不在穿戴范畴）。
    /// </summary>
    public static readonly IReadOnlyDictionary<EquipSlot, string> SlotAnchor = new Dictionary<EquipSlot, string>
    {
        [EquipSlot.LeftHand] = HumanBody.LeftHand,
        [EquipSlot.RightHand] = HumanBody.RightHand,
        [EquipSlot.LeftFoot] = HumanBody.LeftFoot,
        [EquipSlot.RightFoot] = HumanBody.RightFoot,
    };

    /// <summary>躯干三层穿戴槽 ↔ 护甲层 <see cref="ArmorSlot"/>；非躯干层槽返回 null。</summary>
    public static ArmorSlot? ToArmorSlot(EquipSlot slot) => slot switch
    {
        EquipSlot.SkinLayer => ArmorSlot.Skin,
        EquipSlot.OuterLayer => ArmorSlot.Outer,
        EquipSlot.PlateLayer => ArmorSlot.Plate,
        _ => null,
    };

    /// <summary>某槽是否被断肢禁用（该侧肢体在 <paramref name="severedParts"/> 中）。</summary>
    public static bool IsSlotDisabled(EquipSlot slot, IReadOnlySet<string>? severedParts)
        => severedParts is not null
           && SlotAnchor.TryGetValue(slot, out var anchor)
           && severedParts.Contains(anchor);

    /// <summary>某槽当前是否可穿（未被断肢禁用）。占用与否不影响"可用"，只影响冲突。</summary>
    public static bool IsSlotUsable(EquipSlot slot, IReadOnlySet<string>? severedParts)
        => !IsSlotDisabled(slot, severedParts);

    /// <summary>因断肢当前不可用的全部槽（供 UI 灰显）。</summary>
    public static IReadOnlySet<EquipSlot> DisabledSlots(IReadOnlySet<string>? severedParts)
        => SlotAnchor.Keys.Where(s => IsSlotDisabled(s, severedParts)).ToHashSet();

    /// <summary>某槽当前装了什么（空则 null）。</summary>
    public string? ItemAt(EquipSlot slot) => _slotOwner.TryGetValue(slot, out var w) ? w.Item : null;

    /// <summary>某槽是否已被占用。</summary>
    public bool IsOccupied(EquipSlot slot) => _slotOwner.ContainsKey(slot);

    /// <summary>该名装备占用哪些槽（同名多件则为并集；未穿则空集）。</summary>
    public IReadOnlySet<EquipSlot> SlotsOf(string item)
    {
        var slots = new HashSet<EquipSlot>();
        foreach (Worn w in _worn.Where(w => w.Item == item))
        {
            slots.UnionWith(w.Slots);
        }
        return slots;
    }

    /// <summary>该名装备是否至少穿了一件。</summary>
    public bool IsEquipped(string item) => _worn.Any(w => w.Item == item);

    /// <summary>当前在身的全部穿戴品，<b>逐件</b>列出（成对品穿两只则同名出现两次）。</summary>
    public IReadOnlyCollection<string> EquippedItems => _worn.Select(w => w.Item).ToList();

    /// <summary>
    /// 尝试穿戴 <paramref name="item"/>，占用 <paramref name="occupiesSlots"/> 声明的全部槽。
    /// 校验：声明槽非空、无被断肢禁用的槽、目标槽全空（除非 <paramref name="replace"/>）。
    /// <paramref name="replace"/>=true 时，先整件卸下占了目标任一槽的旧装备（含其其它槽），再穿新的。
    /// </summary>
    /// <param name="item">
    /// 装备标识（如护甲名）。同名可穿多件——只要落在不同槽（成对品：一只左手、一只右手）。
    /// 同名穿到<b>已被自己占的槽</b>视为重复穿戴，就地重穿（幂等），不新增一件。
    /// </param>
    /// <param name="occupiesSlots">这件装备占用的槽集合（单槽/多槽由调用方声明；成对品每件只声明一个槽）。</param>
    /// <param name="coversParts">这件装备覆盖的身体部位名集合（供覆盖聚合；null=不提供覆盖信息）。</param>
    /// <param name="severedParts">哪些身体部位已切除（断肢禁装判定入参）。</param>
    /// <param name="replace">目标槽被占时是否顶替旧装备。</param>
    /// <param name="displaced">因穿戴（替换/重复穿）而被卸下的旧装备标识。</param>
    public EquipOutcome TryEquip(
        string item,
        IReadOnlySet<EquipSlot> occupiesSlots,
        out IReadOnlyList<string> displaced,
        IReadOnlySet<string>? coversParts = null,
        IReadOnlySet<string>? severedParts = null,
        bool replace = false)
    {
        displaced = Array.Empty<string>();

        if (occupiesSlots is null || occupiesSlots.Count == 0)
        {
            return EquipOutcome.BlockedNoSlots;
        }

        // 1) 断肢禁装：任一目标槽被禁用即整件穿不上。
        if (occupiesSlots.Any(s => IsSlotDisabled(s, severedParts)))
        {
            return EquipOutcome.BlockedSeveredLimb;
        }

        // 2) 占用冲突：收集占了目标槽的在身实例。同名实例=重复穿戴（幂等重穿，不算冲突）；
        //    异名实例=真冲突，需 replace 才顶替。同名的**其它**实例（如另一只手上的手套）不受影响。
        var occupants = occupiesSlots
            .Where(_slotOwner.ContainsKey)
            .Select(s => _slotOwner[s])
            .Distinct()
            .ToList();
        var conflicts = occupants.Where(w => w.Item != item).ToList();

        if (conflicts.Count > 0 && !replace)
        {
            return EquipOutcome.BlockedSlotOccupied;
        }

        // 3) 顶替：整件卸下冲突旧装备；占了目标槽的同名实例先卸（幂等重穿）。
        var removed = new List<string>();
        foreach (Worn c in conflicts)
        {
            Remove(c);
            removed.Add(c.Item);
        }
        foreach (Worn self in occupants.Where(w => w.Item == item))
        {
            Remove(self);
        }

        // 4) 落位（一件 = 一个实例）。
        var worn = new Worn(item, new HashSet<EquipSlot>(occupiesSlots), coversParts ?? new HashSet<string>());
        foreach (EquipSlot s in worn.Slots)
        {
            _slotOwner[s] = worn;
        }
        _worn.Add(worn);

        displaced = removed;
        return EquipOutcome.Equipped;
    }

    // ---- 存档：穿戴态的快照与恢复 ----

    /// <summary>一件在身装备的存档形态：物品名 + 它占的槽 + 它覆盖的部位。</summary>
    public readonly record struct WornSnapshot(string Item, IReadOnlyList<EquipSlot> Slots, IReadOnlyList<string> Covers);

    /// <summary>
    /// 导出穿戴态（存档用）。<b>逐件导出，不是导出物品名列表</b>——因为成对品（手套/鞋）同名可在身两件，
    /// 只存名字会丢"哪只在左、哪只在右"，读回来就成了两只左手套。
    /// </summary>
    public IReadOnlyList<WornSnapshot> Snapshot()
        => _worn.Select(w => new WornSnapshot(w.Item, w.Slots.ToList(), w.Covers.ToList())).ToList();

    /// <summary>
    /// 读档：清空并逐件摆回穿戴态。
    /// <para>
    /// <b>刻意绕过 <see cref="TryEquip"/> 的校验</b>：读档不是"重新穿一遍衣服"，是把身体摆回它存档那一刻的样子。
    /// 走 TryEquip 会拿断肢集合再判一次禁装——而那件装备当初能穿上，本身就证明它当时是合法的。
    /// 再判一次只会引入"读档后装备莫名消失"这类幽灵 bug。
    /// </para>
    /// </summary>
    public void Restore(IEnumerable<WornSnapshot> worn)
    {
        _slotOwner.Clear();
        _worn.Clear();
        foreach (WornSnapshot w in worn)
        {
            var inst = new Worn(w.Item, new HashSet<EquipSlot>(w.Slots), new HashSet<string>(w.Covers));
            foreach (EquipSlot s in inst.Slots)
            {
                _slotOwner[s] = inst;
            }
            _worn.Add(inst);
        }
    }

    /// <summary>把一个在身实例从槽表与在身清单里摘掉。</summary>
    private void Remove(Worn worn)
    {
        foreach (EquipSlot s in worn.Slots)
        {
            _slotOwner.Remove(s);
        }
        _worn.Remove(worn);
    }

    /// <summary>卸下该名装备的<b>全部</b>在身件（成对品两只一起脱）。返回是否确实卸下了。要只脱一只用 <see cref="UnequipSlot"/>。</summary>
    public bool Unequip(string item)
    {
        List<Worn> mine = _worn.Where(w => w.Item == item).ToList();
        foreach (Worn w in mine)
        {
            Remove(w);
        }
        return mine.Count > 0;
    }

    /// <summary>卸下占用某槽的那一件（连带它占的其它槽；同名的另一只不受影响）。返回被卸下的装备标识（该槽本空则 null）。</summary>
    public string? UnequipSlot(EquipSlot slot)
    {
        if (!_slotOwner.TryGetValue(slot, out Worn? worn))
        {
            return null;
        }
        Remove(worn);
        return worn.Item;
    }

    // ---- 护甲覆盖聚合：供战斗层 DefenderArmor 只读消费 ----

    /// <summary>
    /// 当前所有已穿装备覆盖到的身体部位**并集**（各件 CoversParts 求并）。
    /// 战斗层结算命中某部位时，可据此判断该处是否有甲层参与（具体逐层减伤仍走 ArmorLayer.Covers）。
    /// </summary>
    public IReadOnlySet<string> CoveredParts()
    {
        var union = new HashSet<string>();
        foreach (Worn w in _worn)
        {
            union.UnionWith(w.Covers);
        }
        return union;
    }

    /// <summary>
    /// 逐件覆盖清单（物品标识 + 其覆盖部位集），供战斗层把每件映射成带防御值的护甲层。
    /// 成对品穿两只则出现两条同名记录，各带自己那一边的覆盖。
    /// 本类不持有防御数值——防御来自 <see cref="ArmorTable"/>，此处只交代"哪件防哪些部位"。
    /// </summary>
    public IReadOnlyList<(string Item, IReadOnlySet<string> CoversParts)> ActiveCoverage()
        => _worn.Select(w => (w.Item, w.Covers)).ToList();
}

/// <summary>
/// **拟定/待扩**的"具体装备 → 占哪些槽 / 防哪些部位"小映射表（数据先落一处，后续挪 json）。
/// 只登记穿戴品；刺剑/草叉等武器不是穿戴品（<see cref="IsApparel"/> 返回 false）。
/// 覆盖部位对齐 §5 护甲按部位覆盖：手/脚护甲连带该手/脚的指/趾子树（<see cref="HumanBody.SubtreeNames"/>）。
/// </summary>
public static class ApparelCatalog
{
    /// <summary>
    /// 穿戴品效果类型（[装备→能力加成通路]）。<b>通用可扩展</b>：新增一类装备效果只加一个枚举值 + 一处消费层接线。
    /// <para>
    /// 🔴 这是本引擎<b>唯一</b>的"穿戴品给能力供数"通路——此前能力只由 authored perk + 读过的书承载。
    /// 效果<b>只挂消费层</b>（本文件的 <see cref="ApparelDef"/>），<b>绝不进零依赖战斗引擎 <c>ArmorLayer</c></b>
    /// （读速/视野不是战斗概念）⇒ 战斗结算/Sim 结构性读不到，既有基线零漂移。
    /// </para>
    /// </summary>
    public enum EquipEffectKind
    {
        /// <summary>阅读速度乘子；具体效果见 Wiki 配置表。汇总入 <see cref="ReadingSpeed.Effective"/> 的 apparelMult。</summary>
        ReadingSpeed,

        /// <summary>探索时被发现距离倍率；由 NightWatchContest 的 authored 服饰潜行值投影。</summary>
        ExplorationStealth,

        /// <summary>脚步/动作噪音半径倍率；与负重移速倍率在实时消费层连乘。</summary>
        ActionNoise,

        /// <summary>实时移动速度乘子；从真实穿戴品汇总后乘进 Actor 的移动能力链。</summary>
        MovementSpeed,

        // 扩展位：DaylightVision——墨镜/雪镜白天视野效果；“视野”指视距还是视距+锥角仍待用户确认。
    }

    /// <summary>
    /// 一条穿戴品效果：效果类型 + <b>乘子</b>（直接存乘子；§2 通则①全乘算，避免加算歧义）。
    /// </summary>
    public readonly record struct EquipEffect(EquipEffectKind Kind, double Multiplier)
    {
        /// <summary>读速效果工厂：按 Wiki 配置的百分点转换为乘子。</summary>
        public static EquipEffect ReadingSpeed(double pct) => new(EquipEffectKind.ReadingSpeed, 1.0 + pct);

        /// <summary>探索潜行效果工厂：按既有 authored 潜行值转换为发现距离倍率。</summary>
        public static EquipEffect ExplorationStealth(double apparelStealthScore)
            => new(EquipEffectKind.ExplorationStealth, StealthLogic.EquipmentStealthMultiplier(apparelStealthScore));

        /// <summary>动作噪音效果工厂：与探索潜行共用同一服饰倍率。</summary>
        public static EquipEffect ActionNoise(double apparelStealthScore)
            => new(EquipEffectKind.ActionNoise, StealthLogic.EquipmentNoiseMultiplier(apparelStealthScore));

        /// <summary>移动速度效果工厂：百分点按项目通则转换为乘子（如 -10% ⇒ ×0.90）。</summary>
        public static EquipEffect MovementSpeed(double pct) => new(EquipEffectKind.MovementSpeed, 1.0 + pct);
    }

    /// <summary>
    /// 一件穿戴品的静态定义：占用槽 + 覆盖部位（null=全覆盖，向后兼容旧护甲）+ 所属护甲层。
    /// <para>
    /// <b>成对品</b>（<paramref name="Paired"/>=true，如劳保手套/运动鞋，[SPEC-B18-补]）：物品定义<b>不分左右</b>，
    /// 但一件<b>只占一个槽</b>——护住双手/双脚要<b>两件</b>。此时 <paramref name="Slots"/> 是<b>候选槽</b>
    /// （可装入其中任一），<paramref name="CoversParts"/> 是两侧合计（表口径/UI 展示），
    /// 实际生效覆盖按装入的那一侧取 <see cref="CoversFor"/>。
    /// </para>
    /// </summary>
    public sealed record ApparelDef(
        string Name,
        IReadOnlySet<EquipSlot> Slots,
        IReadOnlySet<string>? CoversParts,
        ArmorSlot? Layer,
        bool Paired = false,
        IReadOnlyDictionary<EquipSlot, IReadOnlySet<string>>? CoversBySlot = null,
        IReadOnlyList<EquipEffect>? Effects = null)   // [装备→能力加成] 穿戴效果（null=无，默认；13 件旧甲全不受影响）
    {
        /// <summary>这件装进 <paramref name="slot"/> 时实际占用的槽集（成对品=只占那一个；其余=全部声明槽）。</summary>
        public IReadOnlySet<EquipSlot> SlotsFor(EquipSlot slot)
            => Paired ? new HashSet<EquipSlot> { slot } : Slots;

        /// <summary>这件装进 <paramref name="slot"/> 时实际覆盖的部位（成对品=那一侧；其余=固定覆盖）。</summary>
        public IReadOnlySet<string>? CoversFor(EquipSlot slot)
            => Paired && CoversBySlot is not null && CoversBySlot.TryGetValue(slot, out IReadOnlySet<string>? c)
                ? c
                : CoversParts;
    }

    private static IReadOnlySet<EquipSlot> S(params EquipSlot[] slots) => new HashSet<EquipSlot>(slots);

    /// <summary>
    /// 已登记的穿戴品。键为装备标识（= 护甲名 = 库存 Item.RefKey）。
    /// 人形 13 件的**占槽**在此、**覆盖部位与数值**在 <see cref="ArmorTable"/>（本表直接取其 CoversParts，
    /// 单一事实源=数据表『护甲表』[SPEC-B18]，两处不再各写一份）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ApparelDef> Defs = BuildDefs();

    private static Dictionary<string, ApparelDef> BuildDefs()
    {
        var d = new Dictionary<string, ApparelDef>();
        void Add(ArmorLayer l, params EquipSlot[] slots)
            => d[l.Name] = new ApparelDef(l.Name, S(slots), l.CoversParts, l.Slot);

        // 在已登记定义上追加效果，保留原槽集合（板甲这类多槽装备不能用“单槽效果”捷径重建定义）。
        void AddEffect(string name, EquipEffect effect)
        {
            ApparelDef def = d[name];
            List<EquipEffect> effects = def.Effects?.ToList() ?? new List<EquipEffect>();
            effects.Add(effect);
            d[name] = def with { Effects = effects };
        }

        // 成对品（[SPEC-B18-补]）：一个 def 不分左右，一件占一只手/脚槽——两件才护全。
        void AddPaired(ArmorLayer l, EquipSlot left, EquipSlot right, string leftPart, string rightPart)
            => d[l.Name] = new ApparelDef(
                l.Name, S(left, right), l.CoversParts, l.Slot, Paired: true,
                CoversBySlot: new Dictionary<EquipSlot, IReadOnlySet<string>>
                {
                    [left] = HumanBody.SubtreeNames(leftPart),
                    [right] = HumanBody.SubtreeNames(rightPart),
                });

        // 开局三件套：长袖布衣=贴身层(胸腹双臂)、长裤=裤装槽(双腿)、运动鞋=一只脚一只鞋(含趾)。
        Add(ArmorTable.LongSleeveShirt(), EquipSlot.SkinLayer);
        // 花衬衫：与长袖布衣同占贴身层（互斥），数值同档。开局营地那具尸体身上扒下来的就是它。
        Add(ArmorTable.FloralShirt(), EquipSlot.SkinLayer);
        Add(ArmorTable.Trousers(), EquipSlot.Pants);
        AddPaired(ArmorTable.Sneakers(), EquipSlot.LeftFoot, EquipSlot.RightFoot, HumanBody.LeftFoot, HumanBody.RightFoot);
        // [T72] 护踝鞋具：成对·脚槽，一件占一只脚、护那侧小腿+脚(含趾)——与运动鞋同槽互斥，双侧要两件。
        // 每侧取小腿子树(SubtreeNames(LeftCalf) = 左小腿+左脚+左五趾)，比运动鞋多护一截小腿。
        AddPaired(ArmorTable.AnkleGuard(), EquipSlot.LeftFoot, EquipSlot.RightFoot, HumanBody.LeftCalf, HumanBody.RightCalf);
        // 短裤：与长裤同占裤装槽（互斥），仅护大腿=不防小腿（覆盖取舍，[SPEC-B17-补]）。
        Add(ArmorTable.Shorts(), EquipSlot.Pants);
        // 外套层五件（互斥）：粗布背心(无袖不护臂) / 粗布外套 / 布夹克 / 牛仔外套 / 皮夹克——后四件同覆盖，防护递增。
        Add(ArmorTable.CoarseClothVest(), EquipSlot.OuterLayer);
        Add(ArmorTable.CoarseClothCoat(), EquipSlot.OuterLayer);
        Add(ArmorTable.ClothJacket(), EquipSlot.OuterLayer);
        Add(ArmorTable.DenimJacket(), EquipSlot.OuterLayer);
        Add(ArmorTable.LeatherJacket(), EquipSlot.OuterLayer);
        // 装甲层三件（互斥·**只管上身**）：皮革胸甲(仅护胸) / 皮甲 / 板甲——板甲多占裤装槽，故与长裤/短裤也互斥。
        Add(ArmorTable.ChestPlate(), EquipSlot.PlateLayer);
        Add(ArmorTable.Leather(), EquipSlot.PlateLayer);
        Add(ArmorTable.Plate(), EquipSlot.PlateLayer, EquipSlot.Pants);
        // Wiki authored：越重的装甲越拖慢行动。效果从真实穿戴表进入统一乘算链；板甲的双槽定义保持不变。
        AddEffect("皮革胸甲", EquipEffect.MovementSpeed(-0.01));
        AddEffect("皮甲", EquipEffect.MovementSpeed(-0.03));
        AddEffect("板甲", EquipEffect.MovementSpeed(-0.10));
        // 头盔两件（[SPEC-B19]，同占头槽互斥）：
        //   军用头盔 → 只占头槽 ⇒ 眼/面还空着，能再扣一张防毒面具；脸也因此完全裸露（挖眼照旧有效）。
        //   防暴头盔 → 头 + 眼镜 + 面部三槽（面罩罩住整张脸）⇒ 与防毒面具互斥（戴着面罩没法再扣面具）。
        // 两者都**不占装甲层槽**（PlateLayer）⇒ 戴头盔与穿板甲/皮甲互不冲突。
        // 用户口径：「装甲层是只针对上身的装备层……头盔这类肯定不在装甲层」——头盔占的是 EquipSlot.Head，
        // 这里正是那句话在代码里的落点（ArmorSlot 是伤害层序，与"占哪个槽"无关，别混）。
        Add(ArmorTable.MilitaryHelmet(), EquipSlot.Head);
        Add(ArmorTable.RiotHelmet(), EquipSlot.Head, EquipSlot.Eyes, EquipSlot.Face);
        // 劳保手套：物品不分左右，一件占一只手槽、护那一只手（含五指）——双手要两件（[SPEC-B18-补]）。
        AddPaired(ArmorTable.WorkGloves(), EquipSlot.LeftHand, EquipSlot.RightHand, HumanBody.LeftHand, HumanBody.RightHand);
        // 纯覆盖品（无护甲数值，Layer=null）：防毒面具 = 眼镜 + 面部两槽。
        d["防毒面具"] = new ApparelDef(
            "防毒面具", S(EquipSlot.Eyes, EquipSlot.Face),
            new HashSet<string> { HumanBody.LeftEye, HumanBody.RightEye, HumanBody.Nose }, null);

        // [批次21·T26] 三件新可制作穿戴品 + 战争面具（配方见 RecipeBook；**不造新槽**，全落既有槽）。
        //   战争面具 → **面部 + 眼镜两槽**（[T59] 用户改的；它现在遮眼、也护眼——见 ArmorTable.WarMask 的注释：
        //             只占槽而不护眼是纯负收益，且这两个槽上现存的两件本就与它互斥 ⇒ 多占眼镜槽不改变任何互斥关系）。
        //             与防毒面具/防暴头盔互斥（都要这张脸），但与**军用头盔**可同戴（那顶只占头槽）。
        //   粗布衬衫 → 贴身层（与长袖布衣/花衬衫互斥）。
        //   粗布短裤 / 粗布长裤 → 裤装槽（与长裤/短裤/板甲互斥；短款仅护大腿，见 ArmorTable）。
        Add(ArmorTable.WarMask(), EquipSlot.Face, EquipSlot.Eyes);
        Add(ArmorTable.CoarseClothShirt(), EquipSlot.SkinLayer);
        Add(ArmorTable.CoarseShorts(), EquipSlot.Pants);
        Add(ArmorTable.CoarseTrousers(), EquipSlot.Pants);

        // [T59] 棉帽（用户在 wiki 上新加）→ **头槽**，与两顶头盔互斥。
        // 头槽此前只有 4.5kg 级的重甲头盔 ⇒ 前期头部完全裸着；这顶 0.15kg 的布帽子把那个洞堵上。
        Add(ArmorTable.CottonHat(), EquipSlot.Head);

        // [T68] 用户在 wiki 上新加的三件（**不造新槽**，全落既有槽）：
        //   恐怖装甲 → **装甲层**（与皮革胸甲/皮甲/板甲互斥）。装甲层的廉价档：比皮革胸甲弱一档，但轻、且不吃皮革产能。
        Add(ArmorTable.HorrorArmor(), EquipSlot.PlateLayer);
        //   墨镜 / 平光眼镜 → **只占眼镜槽**（彼此互斥）。
        //   🔴 这是本表**第一次**出现"只占眼镜槽、不连着占面部槽"的东西：在它们之前，眼镜槽上的三位住客
        //   （防暴头盔 / 战争面具 / 防毒面具）**全都要连着占面部槽** ⇒ 眼镜槽从来没有过独立的候选人。
        //   ⇒ 戴一副眼镜 = **放弃那三件里的任何一件**（头盔的面罩、面具的骨片、防毒面具的滤毒罐）。
        //   这个取舍是这两件东西的**主要价值**——真正的效果（白天视野 / 阅读速度）
        //   是引擎新轴，尚未落地（见 ArmorTable.Sunglasses / PlainGlasses 的注释）。
        Add(ArmorTable.Sunglasses(), EquipSlot.Eyes);
        //   平光眼镜：[装备→能力加成] 挂阅读效果（用户 authored，wiki 护甲表）。这是本引擎第一件"穿戴给能力供数"的装备。
        //   效果经 ApparelEffectMultiplier 汇总入 ReadingSpeed.Effective 的 apparelMult。墨镜的"白天视野"是另一条链、后续。
        Add(ArmorTable.PlainGlasses(), EquipSlot.Eyes);
        AddEffect("平光眼镜", EquipEffect.ReadingSpeed(0.05));
        //   [T71] 自制简易墨镜（木缝雪镜）→ 同占眼镜槽，与墨镜/平光眼镜/防暴盔/战争面具/防毒面具互斥。
        //   它是墨镜的**可制作对应物**（读《尖峰时刻》解锁），护双眼数值见 Wiki 配置表。
        Add(ArmorTable.SelfMadeSnowGoggles(), EquipSlot.Eyes);

        // [警察局] 防弹背心 → **贴身层**（EquipSlot.SkinLayer，与长袖布衣/花衬衫/粗布衬衫互斥）。
        // 🔴 它是贴身层、**不是装甲层**：抗弹背心贴身穿 ⇒ **能与皮甲/板甲叠穿**（打底 + 装甲层罩外），
        // 不与装甲层三件(皮革胸甲/皮甲/板甲)互斥。护胸+腹，数值见 ArmorTable.BallisticVest（拟定待 Sim 校准）。
        Add(ArmorTable.BallisticVest(), EquipSlot.SkinLayer);

        // [Wiki 新增] 厚重裤子 / 厚重披风 / 雪地靴；当前 Wiki 只登记防护、重量与覆盖。
        Add(ArmorTable.HeavyTrousers(), EquipSlot.Pants);
        // 披风只占装甲层；即使保护部位显示包含双大腿，也不抢裤装槽。
        Add(ArmorTable.HeavyCape(), EquipSlot.PlateLayer);
        AddPaired(ArmorTable.SnowBoots(), EquipSlot.LeftFoot, EquipSlot.RightFoot, HumanBody.LeftFoot, HumanBody.RightFoot);

        // [missing-book-items] 牛仔帽 / 马靴 / 简易装甲
        Add(ArmorTable.CowboyHat(), EquipSlot.Head);
        AddPaired(ArmorTable.RidingBoots(), EquipSlot.LeftFoot, EquipSlot.RightFoot, HumanBody.LeftFoot, HumanBody.RightFoot);
        Add(ArmorTable.SimpleArmor(), EquipSlot.PlateLayer);

        // 实时潜行/噪音效果直接从 authored 夜防服饰表投影，避免另抄一张数值表。
        // 已有 ReadingSpeed（如平光眼镜）保留，新的两条效果只追加，不覆盖旧效果。
        foreach ((string name, float stealthScore) in NightWatchContest.ApparelStealth)
        {
            if (!d.TryGetValue(name, out ApparelDef? def))
            {
                // 夜行斗篷/软底鞋是夜防专用物品，暂未登记为可穿戴目录项；不强行造槽。
                continue;
            }

            List<EquipEffect> effects = def.Effects?.ToList() ?? new List<EquipEffect>();
            effects.Add(EquipEffect.ExplorationStealth(stealthScore));
            effects.Add(EquipEffect.ActionNoise(stealthScore));
            d[name] = def with { Effects = effects };
        }

        return d;
    }

    /// <summary>该标识是否为穿戴品（刺剑/草叉等武器返回 false）。</summary>
    public static bool IsApparel(string name) => Defs.ContainsKey(name);

    /// <summary>取穿戴品定义（未登记返回 null）。</summary>
    public static ApparelDef? Get(string name) => Defs.TryGetValue(name, out var d) ? d : null;

    /// <summary>
    /// [装备→能力加成] 汇总读者<b>已穿戴品</b>某类效果的<b>乘子</b>（§2 连乘）：遍历 <paramref name="equippedItems"/>，
    /// 查各件 <see cref="ApparelDef.Effects"/> 中 <paramref name="kind"/> 的乘子相乘；无任何该类效果 = 1.0。
    /// <para>
    /// 🔴 这是"穿戴 → 能力"生效的<b>唯一入口</b>：消费方（如 <see cref="ReadingSpeed.Effective"/> 的 apparelMult）
    /// 必须经本函数从<b>真实穿戴品名</b>取数，不得手写常数——那样 <see cref="ApparelDef.Effects"/> 登记就成了摆设（静默失效）。
    /// </para>
    /// </summary>
    public static double ApparelEffectMultiplier(IEnumerable<string> equippedItems, EquipEffectKind kind)
    {
        double m = 1.0;
        foreach (string name in equippedItems)
        {
            if (Get(name) is { Effects: { } effects })
                foreach (EquipEffect e in effects)
                    if (e.Kind == kind)
                        m *= e.Multiplier;
        }
        return m;
    }

    /// <summary>
    /// 便捷：按目录定义把某件穿到 <paramref name="slots"/> 上（未登记则不动，返回 BlockedNoSlots）。
    /// 成对品（手套/鞋）必须落到某一侧：<paramref name="slot"/> 显式指定；不指定则自动挑第一只
    /// 「未被断肢禁用且空着」的候选槽（左优先），全占满则退回第一只可用槽（配合 replace 顶替）。
    /// </summary>
    public static EquipOutcome Equip(
        ApparelSlots slots, string name, EquipSlot? slot = null,
        IReadOnlySet<string>? severedParts = null, bool replace = false)
    {
        ApparelDef? def = Get(name);
        if (def is null)
        {
            return EquipOutcome.BlockedNoSlots;
        }
        if (!def.Paired)
        {
            return slots.TryEquip(name, def.Slots, out _, def.CoversParts, severedParts, replace);
        }

        EquipSlot? target = slot;
        if (target is null)
        {
            // 候选槽按枚举序（左手/左脚在前）取：先要空闲的，没有空闲则取任一可用的。
            List<EquipSlot> usable = def.Slots
                .Where(s => ApparelSlots.IsSlotUsable(s, severedParts))
                .OrderBy(s => (int)s)
                .ToList();
            target = usable.Cast<EquipSlot?>().FirstOrDefault(s => !slots.IsOccupied(s!.Value))
                     ?? usable.Cast<EquipSlot?>().FirstOrDefault();
        }
        if (target is null)
        {
            return EquipOutcome.BlockedSeveredLimb;   // 两侧肢体都没了
        }
        if (!def.Slots.Contains(target.Value))
        {
            return EquipOutcome.BlockedNoSlots;       // 指定了不属于这件的槽（如把手套往脚上穿）
        }
        return slots.TryEquip(
            name, def.SlotsFor(target.Value), out _, def.CoversFor(target.Value), severedParts, replace);
    }
}
