using DeadSignal.Godot;
using System.IO;
using System.Runtime.CompilerServices;

namespace DeadSignal.Combat.Tests;

public sealed class ExplorationRemainsTests
{
    private static ExplorationCorpseSave Corpse(int owner, int tick = 10) => new()
    {
        Destination = ExplorationCache.BroadcastStationName,
        ContainerId = $"队员的尸体 #{owner}",
        OwnerPawnId = owner,
        SpawnPhaseTick = tick,
        Loot = new List<LootItem> { LootItem.Armor("长袖布衣") },
    };

    [Fact]
    public void 全灭_背包和关键设备留在最后倒下的队员尸体上()
    {
        var corpses = new List<ExplorationCorpseSave> { Corpse(7), Corpse(8) };
        LootItem[] bag = { LootItem.Food(2), LootItem.Material("iron", 3), LootItem.Tool("calipers") };

        ExplorationCorpseSave? carrier = ExplorationRemains.AttachPartyLoss(
            corpses, ExplorationCache.BroadcastStationName, new HashSet<int> { 7, 8 }, bag, hasTransmitter: true);

        Assert.Same(corpses[1], carrier);
        Assert.Equal(4, carrier!.Loot.Count);
        Assert.Contains(LootItem.Tool("calipers"), carrier.Loot);
        Assert.True(carrier.HasTransmitter);
        Assert.True(ExplorationRemains.HasLostTransmitter(corpses, ExplorationCache.BroadcastStationName));
    }

    [Fact]
    public void 找到遗体_关键设备转回当前探索队_尸体不再持有()
    {
        ExplorationCorpseSave corpse = Corpse(7);
        corpse.HasTransmitter = true;

        Assert.True(ExplorationRemains.RecoverTransmitter(corpse));
        Assert.False(corpse.HasTransmitter);
        Assert.False(ExplorationRemains.RecoverTransmitter(corpse));
    }

    [Fact]
    public void 未满三个半天_尸体和遗物仍在()
    {
        var corpses = new List<ExplorationCorpseSave> { Corpse(7, tick: 10) };

        Assert.Empty(ExplorationRemains.SweepExpired(corpses, currentPhaseTick: 12));
        Assert.Single(corpses);
        Assert.NotEmpty(corpses[0].Loot);
    }

    [Fact]
    public void 第三个半天_尸体刷没_普通物资消失_设备不再被尸体占有()
    {
        ExplorationCorpseSave corpse = Corpse(7, tick: 10);
        corpse.HasTransmitter = true;
        var corpses = new List<ExplorationCorpseSave> { corpse };

        List<ExplorationCorpseSave> expired = ExplorationRemains.SweepExpired(corpses, currentPhaseTick: 13);

        Assert.Single(expired);
        Assert.Empty(corpses);
        Assert.False(ExplorationRemains.HasLostTransmitter(corpses, ExplorationCache.BroadcastStationName));
    }

    [Fact]
    public void 生产接线_全灭不卸包_活人回营才提交关键设备_半天边界统一扫尸体()
    {
        string camp = Source("godot/scripts/CampMain.cs");
        string station = Source("godot/scripts/TestExploration.BroadcastStation.cs");

        Assert.Contains("if (CorpseDecay.AdvancesOn(phase))", camp);
        Assert.Contains("SweepExplorationCorpses();", camp);
        Assert.Contains("bool expeditionSurvived", camp);
        Assert.Contains("ExplorationRemains.AttachPartyLoss(", camp);
        Assert.Contains("if (_expeditionHasTransmitter)\n                RadioMainline.GrantTransmitter(_storyFlags);", camp);
        Assert.Contains("RestoreLevelCorpses(initializedLevel);", camp);
        Assert.DoesNotContain("if (l.Kind == LootKind.Tool)", camp);
        Assert.Contains("CollectLoot(new[] { LootItem.Book(d.BookId) }, out _, out _);", camp);
        Assert.Contains("if (TransmitterAvailableAtOrigin)", station);
    }

    private static string Source(string relativePath, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        return File.ReadAllText(Path.Combine(root, relativePath));
    }
}
