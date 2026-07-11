using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 开局营地废墟挖掘纯逻辑单测（批次9）。
// 覆盖：RubbleSite 工时推进（挖掘者在场才推进）/中断续挖不丢进度/封顶挖满/零工时即满、
//       Harvest 一次性收获（挖满才出产 + 清空标记 + 再挖返空 + 未挖满不出产）、彩蛋位数据槽、
//       RubbleField 登记/查/单点推进/按点收获/清空态/全清判定，以及产出复用 LootApplication 落地。
public class RubbleDigTests
{
    private static IReadOnlyList<LootItem> Rubbish() => new[]
    {
        LootItem.Material("wood", 3),
        LootItem.Material("scrap_metal", 2),
        LootItem.Material("scrap_cloth", 2),
    };

    // ---- RubbleSite 工时进度（同 CraftingJob 形态） ----

    [Fact]
    public void NewSite_StartsAtZeroProgress_NotComplete_NotCleared()
    {
        var site = new RubbleSite("废墟A", totalWorkMinutes: 90, Rubbish());
        Assert.Equal(0, site.ElapsedWorkMinutes);
        Assert.Equal(90, site.RemainingWorkMinutes);
        Assert.Equal(0f, site.Progress);
        Assert.False(site.IsComplete);
        Assert.False(site.Cleared);
        Assert.Equal(3, site.Drops.Count);
    }

    [Fact]
    public void Advance_WithWorker_AccumulatesAndReportsAppliedMinutes()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        int applied = site.Advance(30, workerPresent: true);
        Assert.Equal(30, applied);
        Assert.Equal(30, site.ElapsedWorkMinutes);
        Assert.Equal(60, site.RemainingWorkMinutes);
        Assert.InRange(site.Progress, 0.33f, 0.34f);
        Assert.False(site.IsComplete);
    }

    [Fact]
    public void Advance_NoWorker_DoesNotProgress()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        Assert.Equal(0, site.Advance(40, workerPresent: false));
        Assert.Equal(0, site.ElapsedWorkMinutes);
    }

    [Fact]
    public void Advance_InterruptThenResume_KeepsProgress()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        site.Advance(30, workerPresent: true);   // 挖了 30
        site.Advance(999, workerPresent: false); // 被袭营/改派拉走—停，不丢进度
        Assert.Equal(30, site.ElapsedWorkMinutes);
        site.Advance(20, workerPresent: true);   // 回来续挖
        Assert.Equal(50, site.ElapsedWorkMinutes);
    }

    [Fact]
    public void Advance_CapsAtTotal_ThenComplete()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        int applied = site.Advance(200, workerPresent: true); // 超额只补到封顶
        Assert.Equal(90, applied);
        Assert.True(site.IsComplete);
        Assert.Equal(1f, site.Progress);
        Assert.Equal(0, site.RemainingWorkMinutes);
    }

    [Fact]
    public void Advance_NonPositiveMinutes_IsNoOp()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        Assert.Equal(0, site.Advance(0, workerPresent: true));
        Assert.Equal(0, site.Advance(-5, workerPresent: true));
        Assert.Equal(0, site.ElapsedWorkMinutes);
    }

    [Fact]
    public void ZeroTotalWork_IsImmediatelyComplete()
    {
        var site = new RubbleSite("废墟A", totalWorkMinutes: 0, Rubbish());
        Assert.True(site.IsComplete);
        Assert.Equal(1f, site.Progress);
    }

    [Fact]
    public void NegativeTotalWork_ClampedToZero_ImmediatelyComplete()
    {
        var site = new RubbleSite("废墟A", totalWorkMinutes: -30, Rubbish());
        Assert.Equal(0, site.TotalWorkMinutes);
        Assert.True(site.IsComplete);
    }

    [Fact]
    public void EmptyId_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RubbleSite("", 60));
        Assert.Throws<ArgumentException>(() => new RubbleSite(null!, 60));
    }

    // ---- Harvest 一次性收获（挖满才出产 + 清空 + 不重复） ----

    [Fact]
    public void Harvest_BeforeComplete_ReturnsEmpty_NotCleared()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        site.Advance(30, workerPresent: true);
        Assert.Empty(site.Harvest());   // 没挖满不出产
        Assert.False(site.Cleared);
    }

    [Fact]
    public void Harvest_WhenComplete_ReturnsDropsAndClears()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        site.Advance(90, workerPresent: true);
        IReadOnlyList<LootItem> got = site.Harvest();
        Assert.Equal(3, got.Count);
        Assert.True(site.Cleared);
    }

    [Fact]
    public void Harvest_Twice_SecondReturnsEmpty()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        site.Advance(90, workerPresent: true);
        Assert.Equal(3, site.Harvest().Count);
        Assert.Empty(site.Harvest());   // 已收获过，不重复产出
    }

    [Fact]
    public void Advance_AfterCleared_IsNoOp()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        site.Advance(90, workerPresent: true);
        site.Harvest();
        Assert.True(site.Cleared);
        Assert.Equal(0, site.Advance(10, workerPresent: true));
    }

    // ---- 彩蛋位数据槽（内容 authored 待用户） ----

    [Fact]
    public void EggSlot_DefaultsOff_NoContent()
    {
        var site = new RubbleSite("废墟A", 90, Rubbish());
        Assert.False(site.HasEggSlot);
        Assert.Equal("", site.EggContentId);
    }

    [Fact]
    public void EggSlot_CarriesFlagAndAuthoredKey()
    {
        var site = new RubbleSite("废墟彩蛋", 120, Rubbish(), hasEggSlot: true, eggContentId: "");
        Assert.True(site.HasEggSlot);
        Assert.Equal("", site.EggContentId); // MVP 未填：仍只出普通材料
        // 挖满收获照常出普通掉落（彩蛋落地由 CampMain 后续接 authored 内容）。
        site.Advance(120, workerPresent: true);
        Assert.Equal(3, site.Harvest().Count);
    }

    // ---- RubbleField 注册表 ----

    [Fact]
    public void Field_RegisterFindHas()
    {
        var field = new RubbleField();
        var a = new RubbleSite("废墟A", 90, Rubbish());
        field.Register(a);
        Assert.True(field.Has("废墟A"));
        Assert.Same(a, field.Find("废墟A"));
        Assert.False(field.Has("废墟X"));
        Assert.Null(field.Find("废墟X"));
    }

    [Fact]
    public void Field_IgnoresNull()
    {
        var field = new RubbleField();
        field.Register(null!);
        Assert.False(field.Has(""));
    }

    [Fact]
    public void Field_AdvanceRoutesToSite()
    {
        var field = new RubbleField();
        field.Register(new RubbleSite("废墟A", 90, Rubbish()));
        Assert.Equal(40, field.Advance("废墟A", 40, workerPresent: true));
        Assert.Equal(40, field.Find("废墟A")!.ElapsedWorkMinutes);
        Assert.Equal(0, field.Advance("废墟X", 40, workerPresent: true)); // 未登记
    }

    [Fact]
    public void Field_HarvestRoutesAndClears()
    {
        var field = new RubbleField();
        field.Register(new RubbleSite("废墟A", 90, Rubbish()));
        field.Advance("废墟A", 90, workerPresent: true);
        Assert.Equal(3, field.Harvest("废墟A").Count);
        Assert.True(field.IsCleared("废墟A"));
        Assert.Empty(field.Harvest("废墟X")); // 未登记
    }

    [Fact]
    public void Field_ActiveSites_ExcludesCleared_AndAllClearedTracks()
    {
        var field = new RubbleField();
        field.Register(new RubbleSite("废墟A", 30, Rubbish()));
        field.Register(new RubbleSite("废墟B", 30, Rubbish()));
        Assert.Equal(2, field.ActiveSites.Count());
        Assert.False(field.AllCleared);

        field.Advance("废墟A", 30, workerPresent: true);
        field.Harvest("废墟A");
        Assert.Single(field.ActiveSites);
        Assert.Equal("废墟B", field.ActiveSites.Single().Id);
        Assert.False(field.AllCleared);

        field.Advance("废墟B", 30, workerPresent: true);
        field.Harvest("废墟B");
        Assert.Empty(field.ActiveSites);
        Assert.True(field.AllCleared);
    }

    // ---- 产出复用 LootApplication 落地（不发明新落地逻辑） ----

    [Fact]
    public void HarvestedDrops_LandIntoInventory_ViaLootApplication()
    {
        var site = new RubbleSite("废墟A", 30, Rubbish());
        site.Advance(30, workerPresent: true);
        IReadOnlyList<LootItem> drops = site.Harvest();

        var inv = new InventoryStore();
        var registry = new Dictionary<string, BookData>();
        int food = LootApplication.Apply(drops, inv, registry, _ => null);

        Assert.Equal(0, food);
        Assert.Equal(3, inv.MaterialCount("wood"));
        Assert.Equal(2, inv.MaterialCount("scrap_metal"));
        Assert.Equal(2, inv.MaterialCount("scrap_cloth"));
    }
}
