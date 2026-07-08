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
    // 槽 → 占用它的物品标识。多槽装备会让多个槽映射到同一标识。
    private readonly Dictionary<EquipSlot, string> _slotOwner = new();
    // 物品标识 → 它占用的全部槽（多槽/成对的反查）。
    private readonly Dictionary<string, HashSet<EquipSlot>> _itemSlots = new();
    // 物品标识 → 它覆盖的身体部位名集合（Equip 时给入，供覆盖聚合）。
    private readonly Dictionary<string, IReadOnlySet<string>> _itemCovers = new();

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
    public string? ItemAt(EquipSlot slot) => _slotOwner.TryGetValue(slot, out var it) ? it : null;

    /// <summary>某槽是否已被占用。</summary>
    public bool IsOccupied(EquipSlot slot) => _slotOwner.ContainsKey(slot);

    /// <summary>某件装备占用哪些槽（未穿则空集）。</summary>
    public IReadOnlySet<EquipSlot> SlotsOf(string item)
        => _itemSlots.TryGetValue(item, out var s) ? s : (IReadOnlySet<EquipSlot>)new HashSet<EquipSlot>();

    /// <summary>某件装备是否已穿。</summary>
    public bool IsEquipped(string item) => _itemSlots.ContainsKey(item);

    /// <summary>当前已穿的全部装备标识（去重）。</summary>
    public IReadOnlyCollection<string> EquippedItems => _itemSlots.Keys;

    /// <summary>
    /// 尝试穿戴 <paramref name="item"/>，占用 <paramref name="occupiesSlots"/> 声明的全部槽。
    /// 校验：声明槽非空、无被断肢禁用的槽、目标槽全空（除非 <paramref name="replace"/>）。
    /// <paramref name="replace"/>=true 时，先整件卸下占了目标任一槽的旧装备（含其其它槽），再穿新的。
    /// </summary>
    /// <param name="item">装备标识（如护甲名）。已穿同标识会被视为"重复穿戴"——先内部卸下再穿（幂等）。</param>
    /// <param name="occupiesSlots">这件装备占用的槽集合（单槽/多槽/成对由调用方声明）。</param>
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

        // 2) 占用冲突：收集占了目标槽的旧装备（排除自己——重复穿戴视为幂等重穿）。
        var conflicts = occupiesSlots
            .Where(_slotOwner.ContainsKey)
            .Select(s => _slotOwner[s])
            .Where(owner => owner != item)
            .Distinct()
            .ToList();

        if (conflicts.Count > 0 && !replace)
        {
            return EquipOutcome.BlockedSlotOccupied;
        }

        // 3) 顶替：整件卸下冲突旧装备；自身若已穿也先卸（幂等重穿到新槽集）。
        var removed = new List<string>();
        foreach (var c in conflicts)
        {
            if (Unequip(c)) removed.Add(c);
        }
        Unequip(item); // 幂等：清掉自身旧占用，再按新槽集穿

        // 4) 落位。
        var slots = new HashSet<EquipSlot>(occupiesSlots);
        foreach (var s in slots)
        {
            _slotOwner[s] = item;
        }
        _itemSlots[item] = slots;
        _itemCovers[item] = coversParts ?? new HashSet<string>();

        displaced = removed;
        return EquipOutcome.Equipped;
    }

    /// <summary>整件卸下某装备（清空它占的全部槽）。返回是否确实卸下了。</summary>
    public bool Unequip(string item)
    {
        if (!_itemSlots.TryGetValue(item, out var slots))
        {
            return false;
        }
        foreach (var s in slots)
        {
            _slotOwner.Remove(s);
        }
        _itemSlots.Remove(item);
        _itemCovers.Remove(item);
        return true;
    }

    /// <summary>卸下占用某槽的装备（连带它占的其它槽）。返回被卸下的装备标识（该槽本空则 null）。</summary>
    public string? UnequipSlot(EquipSlot slot)
    {
        if (!_slotOwner.TryGetValue(slot, out var item))
        {
            return null;
        }
        Unequip(item);
        return item;
    }

    // ---- 护甲覆盖聚合：供战斗层 DefenderArmor 只读消费 ----

    /// <summary>
    /// 当前所有已穿装备覆盖到的身体部位**并集**（各件 CoversParts 求并）。
    /// 战斗层结算命中某部位时，可据此判断该处是否有甲层参与（具体逐层减伤仍走 ArmorLayer.Covers）。
    /// </summary>
    public IReadOnlySet<string> CoveredParts()
    {
        var union = new HashSet<string>();
        foreach (var cover in _itemCovers.Values)
        {
            union.UnionWith(cover);
        }
        return union;
    }

    /// <summary>
    /// 逐件覆盖清单（物品标识 + 其覆盖部位集），供战斗层把每件映射成带防御值的护甲层。
    /// 本类不持有防御数值——防御来自 <see cref="ArmorTable"/>，此处只交代"哪件防哪些部位"。
    /// </summary>
    public IReadOnlyList<(string Item, IReadOnlySet<string> CoversParts)> ActiveCoverage()
        => _itemCovers.Select(kv => (kv.Key, kv.Value)).ToList();
}

/// <summary>
/// **拟定/待扩**的"具体装备 → 占哪些槽 / 防哪些部位"小映射表（数据先落一处，后续挪 json）。
/// 只登记穿戴品；刺剑/草叉等武器不是穿戴品（<see cref="IsApparel"/> 返回 false）。
/// 覆盖部位对齐 §5 护甲按部位覆盖：手/脚护甲连带该手/脚的指/趾子树（<see cref="HumanBody.SubtreeNames"/>）。
/// </summary>
public static class ApparelCatalog
{
    /// <summary>一件穿戴品的静态定义：占用槽 + 覆盖部位（null=全覆盖，向后兼容旧护甲）+ 所属护甲层。</summary>
    public sealed record ApparelDef(
        string Name,
        IReadOnlySet<EquipSlot> Slots,
        IReadOnlySet<string>? CoversParts,
        ArmorSlot? Layer);

    private static IReadOnlySet<EquipSlot> S(params EquipSlot[] slots) => new HashSet<EquipSlot>(slots);

    /// <summary>已登记的穿戴品（拟定待扩）。键为装备标识。</summary>
    public static readonly IReadOnlyDictionary<string, ApparelDef> Defs = new Dictionary<string, ApparelDef>
    {
        // 单槽：粗布外套 = 外套层；沿用 ArmorTable 口径全覆盖（CoversParts=null）。
        ["粗布外套"] = new("粗布外套", S(EquipSlot.OuterLayer), null, ArmorSlot.Outer),
        // 成对：左/右手套各一件，各只覆盖对应那只手（含五指），贴身层。
        ["左手套"] = new("左手套", S(EquipSlot.LeftHand), HumanBody.SubtreeNames(HumanBody.LeftHand), ArmorSlot.Skin),
        ["右手套"] = new("右手套", S(EquipSlot.RightHand), HumanBody.SubtreeNames(HumanBody.RightHand), ArmorSlot.Skin),
        // 多槽示例（待扩数值）：防毒面具 = 眼镜 + 面部；一体板甲 = 装甲层 + 裤子。
        ["防毒面具"] = new("防毒面具", S(EquipSlot.Eyes, EquipSlot.Face), new HashSet<string> { HumanBody.LeftEye, HumanBody.RightEye, HumanBody.Nose }, null),
        ["一体板甲"] = new("一体板甲", S(EquipSlot.PlateLayer, EquipSlot.Pants), null, ArmorSlot.Plate),
    };

    /// <summary>该标识是否为穿戴品（刺剑/草叉等武器返回 false）。</summary>
    public static bool IsApparel(string name) => Defs.ContainsKey(name);

    /// <summary>取穿戴品定义（未登记返回 null）。</summary>
    public static ApparelDef? Get(string name) => Defs.TryGetValue(name, out var d) ? d : null;

    /// <summary>便捷：按目录定义把某件穿到 <paramref name="slots"/> 上（未登记则不动，返回 BlockedNoSlots）。</summary>
    public static EquipOutcome Equip(ApparelSlots slots, string name, IReadOnlySet<string>? severedParts = null, bool replace = false)
    {
        var def = Get(name);
        if (def is null)
        {
            return EquipOutcome.BlockedNoSlots;
        }
        return slots.TryEquip(name, def.Slots, out _, def.CoversParts, severedParts, replace);
    }
}
