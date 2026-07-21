using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 版本闸门 + 整档 JSON 往返。
/// 版本策略是「版本号 + 明确拒绝，不做迁移」——这些测试钉的就是"拒绝"这件事真的会发生，
/// 而不是悄悄读进来半个世界。
/// </summary>
public class SaveCodecTests
{
    [Fact]
    public void 旧版本的存档被明确拒绝而不是勉强读进来()
    {
        var data = new SaveData();
        string json = SaveCodec.Serialize(data);
        // 把版本号改旧（模拟一份上个版本的存档）
        string stale = json.Replace($"\"Version\": {SaveCodec.CurrentVersion}", "\"Version\": 0");

        SaveLoadResult result = SaveCodec.Deserialize(stale);

        Assert.False(result.Ok);
        Assert.Null(result.Data);                     // 绝不返回半个世界
        Assert.Contains("旧版本", result.Error);      // 给玩家的是人话，不是异常堆栈
    }

    [Fact]
    public void 来自更新版本的存档也被拒绝()
    {
        var data = new SaveData();
        string json = SaveCodec.Serialize(data);
        string future = json.Replace($"\"Version\": {SaveCodec.CurrentVersion}", "\"Version\": 999");

        SaveLoadResult result = SaveCodec.Deserialize(future);

        Assert.False(result.Ok);
        Assert.Contains("更新的版本", result.Error);
    }

    [Fact]
    public void 损坏的存档被拒绝且不抛异常()
    {
        SaveLoadResult result = SaveCodec.Deserialize("{ 这不是 json");

        Assert.False(result.Ok);
        Assert.Contains("损坏", result.Error);
    }

    [Fact]
    public void 空存档被拒绝()
    {
        Assert.False(SaveCodec.Deserialize(null).Ok);
        Assert.False(SaveCodec.Deserialize("").Ok);
        Assert.False(SaveCodec.Deserialize("   ").Ok);
    }

    [Fact]
    public void 当前版本的存档读得进来()
    {
        var data = new SaveData();
        data.Meta.Day = 12;

        SaveLoadResult result = SaveCodec.Deserialize(SaveCodec.Serialize(data));

        Assert.True(result.Ok);
        Assert.Equal(12, result.Data!.Meta.Day);
    }

    [Fact]
    public void 存档摘要不必读全档就能取出来()
    {
        var data = new SaveData();
        data.Meta.Day = 12;
        data.Meta.SurvivorsAlive = 4;
        data.Meta.Phase = DayPhase.DuskMeal;
        data.Meta.Label = "尸潮前夜";

        SaveMeta? meta = SaveCodec.PeekMeta(SaveCodec.Serialize(data));

        Assert.NotNull(meta);
        Assert.Equal(12, meta!.Day);
        Assert.Equal(4, meta.SurvivorsAlive);
        Assert.Equal(DayPhase.DuskMeal, meta.Phase);
        Assert.Equal("尸潮前夜", meta.Label);
    }

    [Fact]
    public void 旧存档在列表里仍然看得见只是读不了()
    {
        // 版本过旧的存档不该从列表上凭空消失——玩家得知道它还在，只是打不开。
        var data = new SaveData();
        data.Meta.Day = 5;
        string stale = SaveCodec.Serialize(data)
            .Replace($"\"Version\": {SaveCodec.CurrentVersion}", "\"Version\": 0");

        Assert.NotNull(SaveCodec.PeekMeta(stale));           // 摘要还读得出来
        Assert.Equal(5, SaveCodec.PeekMeta(stale)!.Day);
        Assert.Equal(0, SaveCodec.PeekVersion(stale));
        Assert.False(SaveCodec.IsCompatible(stale));         // 但读取按钮该置灰
        Assert.False(SaveCodec.Deserialize(stale).Ok);
    }

    [Fact]
    public void 枚举以字符串落盘而不是数字()
    {
        // 数字枚举会在中间插入新值时静默错位：DayPhase 里加一个内部流程节点，
        // 所有旧存档的"黄昏"就变成"夜间"了，而且不报错。
        var data = new SaveData();
        data.World.Phase = DayPhase.NightAct;

        string json = SaveCodec.Serialize(data);

        Assert.Contains("NightAct", json);
    }

    [Fact]
    public void 整档往返后世界状态一致()
    {
        var data = new SaveData();
        data.World.Day = 12;
        data.World.Phase = DayPhase.DuskMeal;
        data.World.PhaseElapsed = 33.25;
        data.World.NightEventKind = NightEventKind.HumanRaid;
        data.World.NightEventTriggerGameHour = 2.25;
        data.World.NightEventFired = false;
        data.StoryFlags["radio_mainline"] = "3";
        data.Camp.Food = 27;
        data.Camp.Structures.Add(new StructureSave
        {
            Id = "fence_south_7",
            Tier = StructureTier.FenceSheetMetal,
            Hp = 312.5,
            DoorState = DoorState.Closed,
        });
        data.Corpses.PhaseTick = 9;
        data.Corpses.Corpses.Add(new CorpseSave
        {
            ContainerId = "丧尸的尸体 #3",
            SpawnPhaseTick = 7,
            Loot = { LootItem.Armor("牛仔外套") },
        });
        data.Merchant.NextVisitDay = 17;

        SaveLoadResult r = SaveCodec.Deserialize(SaveCodec.Serialize(data));

        Assert.True(r.Ok);
        SaveData back = r.Data!;
        Assert.Equal(12, back.World.Day);
        Assert.Equal(DayPhase.DuskMeal, back.World.Phase);
        Assert.Equal(33.25, back.World.PhaseElapsed, 6);
        Assert.Equal(NightEventKind.HumanRaid, back.World.NightEventKind);
        Assert.Equal(2.25, back.World.NightEventTriggerGameHour, 6);
        Assert.False(back.World.NightEventFired);
        Assert.Equal("3", back.StoryFlags["radio_mainline"]);
        Assert.Equal(27, back.Camp.Food);
        Assert.Equal(StructureTier.FenceSheetMetal, back.Camp.Structures[0].Tier);
        Assert.Equal(312.5, back.Camp.Structures[0].Hp, 6);
        Assert.Equal(17, back.Merchant.NextVisitDay);
    }

    [Fact]
    public void 尸体还剩几个半天就烂没这件事读得回来()
    {
        // 尸体的搜刮窗口是硬的（3 个半天）。若 PhaseTick 没存对，一地尸体要么当场全烂光、要么永远不烂。
        var data = new SaveData();
        data.Corpses.PhaseTick = 9;
        data.Corpses.NextId = 14;
        data.Corpses.Corpses.Add(new CorpseSave
        {
            ContainerId = "丧尸的尸体 #12",
            SpawnPhaseTick = 8,            // 躺了 1 个半天，还剩 2 个半天
            X = 1200, Y = 900,
            CellX = 37, CellY = 28,
            Loot = { LootItem.Armor("皮夹克"), LootItem.Weapon("匕首") },
        });

        SaveData back = SaveCodec.Deserialize(SaveCodec.Serialize(data)).Data!;

        Assert.Equal(9, back.Corpses.PhaseTick);
        Assert.Equal(14, back.Corpses.NextId);       // id 水位：新尸体不会和恢复出来的撞号
        CorpseSave c = Assert.Single(back.Corpses.Corpses);
        Assert.Equal(8, c.SpawnPhaseTick);
        var entry = new CorpseDecayEntry(c.ContainerId, c.SpawnPhaseTick, Authored: false);
        Assert.Equal(CorpseDecay.LifetimePhases - 1, CorpseDecay.PhasesRemaining(entry, back.Corpses.PhaseTick));
        Assert.False(CorpseDecay.IsExpired(entry, back.Corpses.PhaseTick));
        // 尸体身上的东西（穿什么扒什么）也在
        Assert.Equal(2, c.Loot.Count);
        Assert.Equal("皮夹克", c.Loot[0].RefId);
    }

    [Fact]
    public void 探索遗体_位置遗物关键设备与半天时钟能跨存档()
    {
        var data = new SaveData();
        data.Corpses.PhaseTick = 12;
        data.Expedition.NextCorpseId = 27;
        data.Expedition.Corpses.Add(new ExplorationCorpseSave
        {
            Destination = ExplorationCache.BroadcastStationName,
            ContainerId = "山姆的尸体 #远征27",
            OwnerPawnId = 7,
            X = 1200,
            Y = 300,
            SpawnPhaseTick = 11,
            Loot = { LootItem.Food(2), LootItem.Weapon("消防斧") },
            HasTransmitter = true,
        });

        SaveData back = SaveCodec.Deserialize(SaveCodec.Serialize(data)).Data!;

        Assert.Equal(27, back.Expedition.NextCorpseId);
        ExplorationCorpseSave corpse = Assert.Single(back.Expedition.Corpses);
        Assert.Equal(ExplorationCache.BroadcastStationName, corpse.Destination);
        Assert.Equal(7, corpse.OwnerPawnId);
        Assert.Equal(11, corpse.SpawnPhaseTick);
        Assert.Equal(2, corpse.Loot.Count);
        Assert.True(corpse.HasTransmitter);
        Assert.Equal(2, CorpseDecay.PhasesRemaining(
            new CorpseDecayEntry(corpse.ContainerId, corpse.SpawnPhaseTick, Authored: false),
            back.Corpses.PhaseTick));
    }

    [Fact]
    public void 存档时刻与是否自动存档记得住()
    {
        var data = new SaveData();
        data.Meta.SavedAtUtc = "2026-07-13T09:30:00Z";
        data.Meta.IsAutosave = true;

        SaveMeta meta = SaveCodec.Deserialize(SaveCodec.Serialize(data)).Data!.Meta;

        Assert.Equal("2026-07-13T09:30:00Z", meta.SavedAtUtc);
        Assert.True(meta.IsAutosave);
    }
}
