using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T71]《尖峰时刻》(peak_hour) —— 滑雪极限运动书，读完解锁「自制简易墨镜」(木制缝隙雪镜)。
/// 意图护栏（先红后绿）：①书真的存在且是「书」不是「日记」；②解锁真的生效（配方挂了本书门槛，
/// 没读书造不出、读了书能造）；③书有投放点（否则是拿不到的死书）；④解锁产物是一件真护甲。
/// </summary>
public class PeakHourBookTests
{
    // ── ① 书存在（Manual，不是日记；用户在 wiki 上定的 readHours=6）──
    [Fact]
    public void 尖峰时刻_是一本真书_读6小时()
    {
        BookData book = BookLibrary.Manuals().SingleOrDefault(b => b.Id == BookLibrary.PeakHourId)!;
        Assert.NotNull(book);
        Assert.Equal("尖峰时刻", book.Title);
        Assert.Equal(BookKind.Manual, book.Kind);
        Assert.False(book.IsDiary);
        Assert.Equal(6, book.ReadHours);
        Assert.False(string.IsNullOrWhiteSpace(book.Body)); // 正文已代笔，不是占位空串
    }

    // 两份事实源要焊死：ExplorationCache 里的书 id 副本必须与 BookLibrary 权威一致。
    [Fact]
    public void 书id_两处事实源一致()
        => Assert.Equal(BookLibrary.PeakHourId, ExplorationCache.PeakHourBookId);

    // ── ② 解锁生效：配方挂本书门槛（权威在 RequiredBookIds），且行为上真的把门看住了 ──
    private static RecipeData Recipe()
        => RecipeBook.All.Single(r => r.OutputKey == "snow_goggles");

    [Fact]
    public void 自制简易墨镜配方_门槛是尖峰时刻()
    {
        RecipeData r = Recipe();
        Assert.Equal("自制简易墨镜", r.DisplayName);
        Assert.Contains(BookLibrary.PeakHourId, r.RequiredBookIds);
    }

    [Fact]
    public void 没读尖峰时刻_造不出简易墨镜()
    {
        CraftAvailability a = CraftingLogic.CanCraft(
            Recipe(),
            availableMaterial: _ => 99,
            isBookRead: _ => false,               // 一本书都没读
            installedTools: new HashSet<ToolSlot>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void 读了尖峰时刻并有材料_可造简易墨镜()
    {
        CraftAvailability a = CraftingLogic.CanCraft(
            Recipe(),
            availableMaterial: _ => 99,
            isBookRead: id => id == BookLibrary.PeakHourId,
            installedTools: new HashSet<ToolSlot>());
        Assert.True(a.CanCraft);
    }

    // ── ③ 书有投放点（城市之巅瞭望观景台·瞭望员值班室），否则是死书 ──
    [Fact]
    public void 尖峰时刻_有投放点_瞭望员值班室()
    {
        CacheResult room = ExplorationCache
            .Resolve(ExplorationCache.LookoutWardensRoomId, new StoryFlags())!.Value;
        Assert.Contains(room.Loot, l => l.Kind == LootKind.Book && l.RefId == BookLibrary.PeakHourId);
    }

    // ── ④ 解锁产物是一件真护甲（用户 authored 的数值/覆盖）──
    [Fact]
    public void 自制简易墨镜_是护甲_眼镜槽护双眼_用户数值()
    {
        ArmorLayer g = ArmorTable.SelfMadeSnowGoggles();
        Assert.Equal("自制简易墨镜", g.Name);
        Assert.Equal(12, g.SharpDefense);
        Assert.Equal(6, g.BluntDefense);
        Assert.Equal(0.1, g.Weight, 6);
        Assert.NotNull(g.CoversParts);
        Assert.Contains(HumanBody.LeftEye, g.CoversParts!);
        Assert.Contains(HumanBody.RightEye, g.CoversParts!);

        // 占眼镜槽（与墨镜/平光眼镜互斥），并被穿戴目录登记。
        ApparelCatalog.ApparelDef def = ApparelCatalog.Defs["自制简易墨镜"];
        Assert.Contains(EquipSlot.Eyes, def.Slots);

        // 配方产物落地成 Item.Armor（同名引用键）。
        List<Item> made = CraftOutputFactory.Create("snow_goggles", 1).ToList();
        Assert.Single(made);
        Assert.Equal("自制简易墨镜", made[0].DisplayName);
    }
}
