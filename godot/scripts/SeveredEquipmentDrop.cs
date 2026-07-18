using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 纯逻辑（无 Godot 依赖）：把 Body.Sever 的“被移除部位”投影到真正挂在肢体上的装备槽。
// 空间层只消费 Claim 出来的槽/手并把 LootItem 放进尸体/地面容器；这里不碰库存、不碰节点。
/// <summary>
/// 断肢装备掉落的幂等账本。
///
/// <para>Body 以部位子树报告断肢（断左臂同时包含左手），而装备模型以四个锚槽/左右手持械。
/// 本类收口两套命名之间的映射，避免 Pawn、尸体和探索关各自猜一遍；同一断肢事件重复回放时，已 Claim
/// 的槽不会再次产出掉落。</para>
/// </summary>
public sealed class SeveredEquipmentDropLedger
{
    private readonly HashSet<EquipSlot> _claimedApparelSlots = new();
    private readonly HashSet<Hand> _claimedHands = new();

    /// <summary>
    /// 由被移除部位 Claim 对应的手/脚穿戴槽。返回顺序固定为 EquipSlot 枚举顺序。
    /// </summary>
    public IReadOnlyList<EquipSlot> ClaimApparelSlots(IEnumerable<string>? removedParts)
    {
        var removed = Normalize(removedParts);
        var fresh = new List<EquipSlot>();
        foreach ((EquipSlot slot, string anchor) in ApparelSlots.SlotAnchor)
        {
            if (removed.Contains(anchor) && _claimedApparelSlots.Add(slot))
            {
                fresh.Add(slot);
            }
        }

        return fresh;
    }

    /// <summary>由被移除部位 Claim 对应的左右手持械槽。重复事件不会重复 Claim。</summary>
    public IReadOnlyList<Hand> ClaimHands(IEnumerable<string>? removedParts)
    {
        var removed = Normalize(removedParts);
        var fresh = new List<Hand>(2);
        if (removed.Contains(HumanBody.LeftHand) && _claimedHands.Add(Hand.Left))
        {
            fresh.Add(Hand.Left);
        }
        if (removed.Contains(HumanBody.RightHand) && _claimedHands.Add(Hand.Right))
        {
            fresh.Add(Hand.Right);
        }

        return fresh;
    }

    /// <summary>测试/诊断用：已 Claim 的槽不再回放。</summary>
    public bool IsApparelSlotClaimed(EquipSlot slot) => _claimedApparelSlots.Contains(slot);

    /// <summary>测试/诊断用：已 Claim 的手不再回放。</summary>
    public bool IsHandClaimed(Hand hand) => _claimedHands.Contains(hand);

    private static HashSet<string> Normalize(IEnumerable<string>? parts)
        => (parts ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.Ordinal);
}
