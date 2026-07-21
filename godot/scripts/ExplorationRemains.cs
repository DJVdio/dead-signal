using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

/// <summary>探索地点遗留尸体的纯逻辑；空间生成与容器登记由 CampMain 消费。</summary>
public static class ExplorationRemains
{
    public static ExplorationCorpseSave? AttachPartyLoss(
        IList<ExplorationCorpseSave> corpses,
        string destination,
        IReadOnlySet<int> expeditionIds,
        IEnumerable<LootItem> bag,
        bool hasTransmitter)
    {
        ArgumentNullException.ThrowIfNull(corpses);
        ArgumentNullException.ThrowIfNull(expeditionIds);
        ArgumentNullException.ThrowIfNull(bag);

        ExplorationCorpseSave? carrier = null;
        for (int i = corpses.Count - 1; i >= 0; i--)
        {
            ExplorationCorpseSave candidate = corpses[i];
            if (string.Equals(candidate.Destination, destination, StringComparison.Ordinal)
                && expeditionIds.Contains(candidate.OwnerPawnId))
            {
                carrier = candidate;
                break;
            }
        }
        if (carrier is null)
            return null;

        carrier.Loot.AddRange(bag);
        carrier.HasTransmitter |= hasTransmitter;
        return carrier;
    }

    public static List<ExplorationCorpseSave> SweepExpired(
        IList<ExplorationCorpseSave> corpses,
        int currentPhaseTick)
    {
        ArgumentNullException.ThrowIfNull(corpses);
        var expired = new List<ExplorationCorpseSave>();
        foreach (ExplorationCorpseSave corpse in corpses)
        {
            var entry = new CorpseDecayEntry(corpse.ContainerId, corpse.SpawnPhaseTick, Authored: false);
            if (CorpseDecay.IsExpired(entry, currentPhaseTick))
                expired.Add(corpse);
        }
        foreach (ExplorationCorpseSave corpse in expired)
            corpses.Remove(corpse);
        return expired;
    }

    public static bool HasLostTransmitter(
        IEnumerable<ExplorationCorpseSave> corpses,
        string destination)
    {
        ArgumentNullException.ThrowIfNull(corpses);
        return corpses.Any(c => c.HasTransmitter
            && string.Equals(c.Destination, destination, StringComparison.Ordinal));
    }

    public static bool RecoverTransmitter(ExplorationCorpseSave corpse)
    {
        ArgumentNullException.ThrowIfNull(corpse);
        if (!corpse.HasTransmitter)
            return false;
        corpse.HasTransmitter = false;
        return true;
    }
}
