using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>Pawn 的左右手/肢体槽断肢掉落消费。</summary>
public sealed partial class Pawn
{
    protected override IReadOnlyList<LootItem> ConsumeSeveredEquipment(IReadOnlyList<string> removedParts)
    {
        var loot = new List<LootItem>();
        IReadOnlyList<Hand> hands = _equipmentDropLedger.ClaimHands(removedParts);

        // 先取出当前手上实例，再通知 WeaponLoadout 清掉。双手握同一实例时只掉一把；
        // 双持同名武器是两个实例，分别占左右手，故不能按名字去重。
        var seenWeaponInstances = new HashSet<Weapon>();
        foreach (Hand hand in hands)
        {
            Weapon? weapon = WeaponInHand(hand);
            if (weapon is not null && seenWeaponInstances.Add(weapon) && CorpseLoot.IsSalvageable(weapon))
            {
                loot.Add(LootItem.Weapon(weapon.Name));
            }

            _loadout.NotifyHandLost(hand);
        }

        // 手套/鞋子按实际槽逐件掉落。成对同名物品两侧各是独立实例，不能按名字合并。
        foreach (EquipSlot slot in _equipmentDropLedger.ClaimApparelSlots(removedParts))
        {
            string? apparel = _apparel.ItemAt(slot);
            ArmorLayer? layer = apparel is null ? null : ApparelLayerFor(apparel);
            if (apparel is not null && layer is not null && CorpseLoot.IsSalvageable(layer))
            {
                loot.Add(LootItem.Armor(apparel));
            }

            string? removed = _apparel.UnequipSlot(slot);
            if (removed is not null && !_apparel.IsEquipped(removed))
            {
                _apparelLayers.Remove(removed);
            }
        }

        if (hands.Count > 0 || loot.Count > 0)
        {
            SyncCombatFromEquipment();
        }

        // 断肢装备回到本人背包（暂存，死后可搜出），不扔地面/CorpseYard/动态尸体点。
        // 返回空列表阻止基类的地面容器路由。
        _severedBackpackItems.AddRange(loot);
        return Array.Empty<LootItem>();
    }

    // 装备槽上可能是纯覆盖品（防毒面具）或护甲层；掉落判据必须复用目录/层登记，
    // 不把未登记的字符串偷偷塞进库存。
    private static ArmorLayer? ApparelLayerFor(string name)
        => ArmorLayerCatalog.TryGetValue(name, out ArmorLayer? layer) ? layer : null;
}
