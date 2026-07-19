using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// Actor 的断肢装备消费面（独立 partial，避免把空间掉落规则塞进基础战斗文件）。
/// Body 只报告部位；本层把报告变成 LootItem，再交给 CampMain 的尸体/地面容器。
/// </summary>
public abstract partial class Actor
{
    private bool _equipmentDropWired;
    protected readonly SeveredEquipmentDropLedger _equipmentDropLedger = new();

    /// <summary>
    /// 断肢装备掉落广播（诊断/其他空间宿主可订阅）。CampMain 由当前 Actor 父链直接接收，
    /// 不依赖静态订阅，避免换场景后遗留旧宿主。
    /// </summary>
    public static event Action<Actor, IReadOnlyList<LootItem>>? AnyEquipmentDropped;

    /// <summary>把本 Actor 的 Body 断肢回调挂到消费层；重复调用无副作用。</summary>
    protected void WireEquipmentDrop()
    {
        if (_equipmentDropWired || Body is null)
        {
            return;
        }

        _equipmentDropWired = true;
        Body.EquipmentDropped = OnBodyEquipmentDropped;
    }

    /// <summary>
    /// 基类消费：劫掠者等单武器 Actor 断手时放下其持械武器。
    /// Pawn 在 partial 中覆写，用左右手持械/11 槽模型精确移除。
    /// </summary>
    protected virtual IReadOnlyList<LootItem> ConsumeSeveredEquipment(IReadOnlyList<string> removedParts)
    {
        IReadOnlyList<Hand> hands = _equipmentDropLedger.ClaimHands(removedParts);
        if (hands.Count == 0 || AttackWeapon is null || !CorpseLoot.IsSalvageable(AttackWeapon))
        {
            return Array.Empty<LootItem>();
        }

        string weaponName = AttackWeapon.Name;
        // 断手后，非 Pawn 的单武器 Actor 退回天生拳脚；天生武器本来就不可搜刮，
        // 因而不会被误产出为 LootItem。
        AttackWeapon = CombatData.Fists();
        AttackCooldown = AttackWeapon.AttackInterval;
        AttackRange = 26f;
        IsRanged = false;
        return new[] { LootItem.Weapon(weaponName) };
    }

    private void OnBodyEquipmentDropped(IReadOnlyList<string> removedParts)
    {
        if (removedParts is null || removedParts.Count == 0)
        {
            return;
        }

        IReadOnlyList<LootItem> loot = ConsumeSeveredEquipment(removedParts);
        if (loot.Count == 0)
        {
            return;
        }

        AnyEquipmentDropped?.Invoke(this, loot);
        RouteEquipmentDropToCurrentSpace(loot);
    }

    /// <summary>
    /// 沿 Actor→CampMain 父链路由，不使用静态 CampMain 事件。
    /// 探索关由 CampMain 再把同一份 LootItem 放入关内容器；营地由 CorpseYard 落地并走存档链。
    /// </summary>
    private void RouteEquipmentDropToCurrentSpace(IReadOnlyList<LootItem> loot)
    {
        Node? node = this;
        while (node is not null && node is not CampMain)
        {
            node = node.GetParent();
        }

        if (node is CampMain camp)
        {
            camp.ReceiveEquipmentDrop(this, loot);
        }
    }
}
