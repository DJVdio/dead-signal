using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat; // ArmorLayer / ArmorTable（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ApparelSlots.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 布鲁斯（狗）穿戴系统（批次5，道格 2 级解锁制作五件套）。
// 独立于人类 11 槽 ApparelSlots——狗只有**身体 + 头 2 槽**，无断肢/眼/面/持械等人类复杂度，
// 蹭人类槽模型只会引入不适用的分支。本类只管三件事：
//   ① 每槽单件的穿戴/替换/卸装；
//   ② 已穿装备聚合成护甲层（喂 Dog.DefenderArmor，走部位/护甲三段判定的现有战斗管道）；
//   ③ 口袋狗衣的携带容量聚合（探索出队负重加成——接线待"狗随队出探索"落地，见 Dog/CampMain TODO）。
// 护甲数值单一真源＝ DeadSignal.Combat.ArmorTable（本类只登记"哪件占哪槽/给多少容量"，不自持防御值）。

/// <summary>狗的两个穿戴槽（远少于人类 11 槽）。</summary>
public enum DogEquipSlot
{
    /// <summary>身体（布制/皮制/口袋狗衣，互斥）。</summary>
    Body,
    /// <summary>头部（铁皮/铁丝头甲，互斥）。</summary>
    Head,
}

/// <summary>一次 <see cref="DogApparelSlots.TryEquip"/> 的结果。</summary>
public enum DogEquipOutcome
{
    /// <summary>已穿上（目标槽原为空）。</summary>
    Equipped,
    /// <summary>已穿上并顶替了原槽旧装备（旧件见 out displaced）。</summary>
    Replaced,
    /// <summary>装备键未在 <see cref="DogGearCatalog"/> 登记，穿不上。</summary>
    BlockedUnknownGear,
}

/// <summary>
/// 一件狗装备的静态定义：占用槽 + 护甲层（null＝纯功能件，如口袋狗衣无甲）+ 携带容量加成（口袋狗衣才 &gt;0）。
/// <see cref="Armor"/> 从 <see cref="ArmorTable"/> 现取（工厂委托，保证与唯一护甲真源同步）。
/// </summary>
public sealed record DogGearDef(
    string Key,
    string DisplayName,
    DogEquipSlot Slot,
    System.Func<ArmorLayer>? ArmorFactory,
    float CarryCapacityBonus = 0f)
{
    /// <summary>这件装备提供的护甲层（无甲件返回 null）。每次现取（不缓存，护甲数值随 ArmorTable 变）。</summary>
    public ArmorLayer? Armor() => ArmorFactory?.Invoke();
}

/// <summary>
/// 狗装备目录（批次5 五件套，草稿 draft）。键＝库存物品 <see cref="Item.RefKey"/>（护甲物品，
/// 由 <see cref="CraftOutputFactory"/> 按配方产物 key 落地）。材料/配方在 <see cref="RecipeBook"/>，护甲值在 <see cref="ArmorTable"/>。
/// </summary>
public static class DogGearCatalog
{
    // 键＝库存护甲物品 RefKey＝ArmorTable 护甲层名（中文）——对齐本仓护甲身份键惯例
    // （ApparelCatalog/ArmorTable/Item.Armor 皆以中文护甲名作 RefKey）。配方 OutputKey 亦用此中文键。
    /// <summary>布制狗衣（身体·贴身甲）。</summary>
    public const string ClothVestKey = "布制狗衣";
    /// <summary>皮制狗衣（身体·外套甲）。</summary>
    public const string LeatherVestKey = "皮制狗衣";
    /// <summary>口袋狗衣（身体·无甲·携带容量）。</summary>
    public const string PocketVestKey = "口袋狗衣";
    /// <summary>铁皮头甲（头·高防）。</summary>
    public const string IronHelmetKey = "铁皮头甲";
    /// <summary>铁丝头甲（头·轻便）。</summary>
    public const string WireHelmetKey = "铁丝头甲";

    /// <summary>口袋狗衣携带容量加成（探索出队负重，草稿；量级参照 <see cref="Loadout.CapacityPerStrength"/>）。</summary>
    public const float PocketVestCapacity = 12f;

    /// <summary>五件套定义（键 → 定义）。</summary>
    public static readonly IReadOnlyDictionary<string, DogGearDef> Defs = new Dictionary<string, DogGearDef>
    {
        [ClothVestKey] = new(ClothVestKey, "布制狗衣", DogEquipSlot.Body, ArmorTable.DogClothVest),
        [LeatherVestKey] = new(LeatherVestKey, "皮制狗衣", DogEquipSlot.Body, ArmorTable.DogLeatherVest),
        [PocketVestKey] = new(PocketVestKey, "口袋狗衣", DogEquipSlot.Body, ArmorFactory: null, CarryCapacityBonus: PocketVestCapacity),
        [IronHelmetKey] = new(IronHelmetKey, "铁皮头甲", DogEquipSlot.Head, ArmorTable.DogIronHelmet),
        [WireHelmetKey] = new(WireHelmetKey, "铁丝头甲", DogEquipSlot.Head, ArmorTable.DogWireHelmet),
    };

    /// <summary>该键是否为已登记的狗装备。</summary>
    public static bool IsDogGear(string? key) => key != null && Defs.ContainsKey(key);

    /// <summary>取狗装备定义（未登记返回 null）。</summary>
    public static DogGearDef? Get(string? key) => key != null && Defs.TryGetValue(key, out var d) ? d : null;

    /// <summary>五件套全部键（供配方产物/UI 遍历）。</summary>
    public static IEnumerable<string> AllKeys => Defs.Keys;
}

/// <summary>
/// 单条狗的穿戴态：槽 → 已穿装备键（最多 2 件：身体 + 头）。每槽单件，穿同槽新件自动顶替旧件。
/// 提供 <see cref="ArmorLayers"/>（喂战斗 DefenderArmor）与 <see cref="TotalCarryCapacity"/>（探索负重）两项聚合。
/// </summary>
public sealed class DogApparelSlots
{
    private readonly Dictionary<DogEquipSlot, string> _slot = new();

    /// <summary>某槽当前穿了什么（空则 null）。</summary>
    public string? ItemAt(DogEquipSlot slot) => _slot.TryGetValue(slot, out var k) ? k : null;

    /// <summary>某槽是否已占用。</summary>
    public bool IsOccupied(DogEquipSlot slot) => _slot.ContainsKey(slot);

    /// <summary>某件装备是否已穿。</summary>
    public bool IsEquipped(string gearKey) => _slot.Values.Contains(gearKey);

    /// <summary>当前已穿的全部装备键（0~2 件）。</summary>
    public IReadOnlyCollection<string> EquippedKeys => _slot.Values.ToList();

    /// <summary>
    /// 穿上 <paramref name="gearKey"/>（占其所属槽）。同槽已有旧件则整件顶替（旧件经 out <paramref name="displaced"/> 返回，供退回库存）。
    /// 未登记的键穿不上（<see cref="DogEquipOutcome.BlockedUnknownGear"/>）。幂等：重复穿同一件视为顶替自身，displaced=null。
    /// </summary>
    public DogEquipOutcome TryEquip(string gearKey, out string? displaced)
    {
        displaced = null;
        var def = DogGearCatalog.Get(gearKey);
        if (def is null)
        {
            return DogEquipOutcome.BlockedUnknownGear;
        }

        bool replaced = _slot.TryGetValue(def.Slot, out var old) && old != gearKey;
        if (replaced)
        {
            displaced = old;
        }
        _slot[def.Slot] = gearKey;
        return replaced ? DogEquipOutcome.Replaced : DogEquipOutcome.Equipped;
    }

    /// <summary>脱下某件装备（不在身则不动）。返回是否确实脱下。</summary>
    public bool Unequip(string gearKey)
    {
        var def = DogGearCatalog.Get(gearKey);
        if (def is null || ItemAt(def.Slot) != gearKey)
        {
            return false;
        }
        _slot.Remove(def.Slot);
        return true;
    }

    /// <summary>脱下占用某槽的装备。返回被脱下的装备键（该槽本空则 null）。</summary>
    public string? UnequipSlot(DogEquipSlot slot)
    {
        if (_slot.TryGetValue(slot, out var k))
        {
            _slot.Remove(slot);
            return k;
        }
        return null;
    }

    /// <summary>
    /// 当前所有已穿装备的护甲层集合（无甲件如口袋狗衣不产层）。喂给 <c>Dog.DefenderArmor</c>——
    /// 布鲁斯挨打即走部位/护甲三段判定的现有战斗管道（层序由 CombatResolver.OrderOuterToInner 归一）。
    /// </summary>
    public IReadOnlyList<ArmorLayer> ArmorLayers()
    {
        var layers = new List<ArmorLayer>();
        foreach (var key in _slot.Values)
        {
            ArmorLayer? layer = DogGearCatalog.Get(key)?.Armor();
            if (layer is not null)
            {
                layers.Add(layer);
            }
        }
        return layers;
    }

    /// <summary>
    /// 已穿装备提供的携带容量合计（当前仅口袋狗衣 &gt;0）。探索出队时叠加到队伍负重上限
    /// （<see cref="Loadout"/> 体系）——实际接线待"狗随队出探索"落地（bruce-actor TODO）。
    /// </summary>
    public float TotalCarryCapacity()
        => _slot.Values.Sum(k => DogGearCatalog.Get(k)?.CarryCapacityBonus ?? 0f);
}
