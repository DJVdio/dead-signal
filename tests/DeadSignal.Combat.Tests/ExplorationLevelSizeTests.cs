using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 探索关画布尺寸 per-destination 查表（<see cref="ExplorationLevelSize"/>）的护栏。
/// <para>
/// 本任务是纯使能重构：把 <c>TestExploration</c> 写死的 2400×1600 画布改成按目的地取值，但
/// <b>覆盖表为空 ⇒ 每一关仍取默认 2400×1600</b>（零漂移基线）。这些测试把"默认 = 历史固定尺寸"
/// 与"未登记的目的地一律回退默认"钉死——将来 per-map impl 往覆盖表加行时，这里能挡住误改默认值/误破回退。
/// </para>
/// </summary>
public class ExplorationLevelSizeTests
{
    /// <summary>默认尺寸＝改造前 TestExploration 的 const 值（2400×1600）。任何人改动它都要过这条。</summary>
    [Fact]
    public void Default_Is_Historical_2400x1600()
    {
        Assert.Equal(2400f, ExplorationLevelSize.DefaultWidth);
        Assert.Equal(1600f, ExplorationLevelSize.DefaultHeight);
    }

    /// <summary>
    /// 零漂移核心断言：当前**所有**探索目的地都没登记覆盖尺寸 ⇒ 逐一 SizeFor 都必须回退到默认 2400×1600。
    /// 覆盖了 TestExploration.Initialize 那串 if-else 里出现的每一个目的地名。
    /// </summary>
    [Theory]
    [InlineData(WorldMapPanel_WatchersCabinName)]
    [InlineData(WorldMapPanel_CityRooftopLookoutName)]
    [InlineData(ExplorationCache.RiversideCabinName)]
    // 超市/加油站/东部新村/联合收割机仓库/广播台已登记放大覆盖（3200×2200，中图·≈3天）——
    //   从"回退默认"用例移出，尺寸由下方 MidMaps 专测钉。
    // 医院/南林村庄已登记放大覆盖（4200×2800，大图·≈5天）——从"回退默认"用例移出，其尺寸由下方大图专测钉。
    // 🔴 policy A：金手指帮根据地/斯图尔特家族庄园两个敌营是 authored 不变量图（噪音招怪校准/固定像素噪音带），
    //   **维持原尺寸零改动、不硬缩放** ⇒ 仍在此回退默认 2400×1600。
    [InlineData(WorldMapPanel_GoldfingerBaseName)]
    [InlineData(StuartManor.DestinationName)]
    [InlineData(ExplorationCache.FireStationName)]
    [InlineData(ExplorationCache.SewerName)]
    [InlineData(ExplorationCache.PoliceStationName)]
    [InlineData(RuinedChurch.DestinationName)]
    [InlineData(RefugeeCamp.DestinationName)]
    [InlineData(NurseRecruit.DestinationName)]
    public void Every_Destination_FallsBack_To_Default(string destination)
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(destination);
        Assert.Equal(ExplorationLevelSize.DefaultWidth, w);
        Assert.Equal(ExplorationLevelSize.DefaultHeight, h);
    }

    /// <summary>
    /// 废弃医院登记了放大覆盖：大图·目标≈5天探索量级，均匀 1.75×（4200×2800）。
    /// 数值拟定待调（方向对着 5 天锚点）——若日后调档，这条与 <see cref="ExplorationLevelSize"/> 的覆盖行一起改。
    /// </summary>
    [Fact]
    public void Hospital_Is_Enlarged_To_4200x2800()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(ExplorationCache.HospitalName);
        Assert.Equal(4200f, w);
        Assert.Equal(2800f, h);
    }

    /// <summary>
    /// 南林村庄登记了放大覆盖：大图·目标≈5天探索量级（4200×2800，村落院墙巷道 + 加密 30→42 + 游荡丧尸按比例上调）。
    /// 数值拟定待调（方向对着 5 天锚点）——若日后调档，这条与 <see cref="ExplorationLevelSize"/> 的覆盖行一起改。
    /// </summary>
    [Fact]
    public void SouthForestVillage_Is_Enlarged_To_4200x2800()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(VillageRescue.DestinationName);
        Assert.Equal(4200f, w);
        Assert.Equal(2800f, h);
    }

    /// <summary>
    /// 中图（超市/加油站/东部新村/联合收割机仓库/广播台）登记了放大覆盖：中图·目标≈3天探索量级，均匀 4/3×（3200×2200）。
    /// 数值拟定待调（方向对着 3 天锚点，≈1800–2160s 在关工作量）——若日后调档，这条与
    /// <see cref="ExplorationLevelSize"/> 的覆盖行一起改。
    /// <para>🔴 金手指帮/斯图尔特两个敌营按 policy A 维持原尺寸（噪音招怪/固定像素不变量），故不在此列。</para>
    /// </summary>
    [Theory]
    [InlineData(ExplorationCache.SupermarketName)]
    [InlineData(ExplorationCache.GasStationName)]
    [InlineData(ExplorationCache.EastNewVillageName)]
    [InlineData(ExplorationCache.HarvesterWarehouseName)]
    [InlineData(WorldMapPanel_BroadcastStationName)]   // [SPEC-T60] 广播台：占位地台⇒真广播站结构，3200×2200
    public void MidMaps_Enlarged_To_3200x2200(string destination)
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(destination);
        Assert.Equal(3200f, w);
        Assert.Equal(2200f, h);
    }

    /// <summary>未知/未登记目的地名回退默认（防呆）。</summary>
    [Fact]
    public void Unknown_Destination_FallsBack_To_Default()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor("no_such_place_ever");
        Assert.Equal(2400f, w);
        Assert.Equal(1600f, h);
    }

    /// <summary>null 目的地名不炸、回退默认（DestinationName 基类默认空串，防御 null 传入）。</summary>
    [Fact]
    public void Null_Destination_FallsBack_To_Default()
    {
        (float w, float h) = ExplorationLevelSize.SizeFor(null!);
        Assert.Equal(2400f, w);
        Assert.Equal(1600f, h);
    }

    // WorldMapPanel 是 Godot 类型、未 Link 进本工程，其目的地名以字面量镜像（与 WorldMapPanel.*Name 逐字一致）。
    private const string WorldMapPanel_GoldfingerBaseName = "金手指帮根据地";
    private const string WorldMapPanel_WatchersCabinName = "守望者森林小屋";
    private const string WorldMapPanel_CityRooftopLookoutName = "城市之巅瞭望观景台";
    private const string WorldMapPanel_BroadcastStationName = "广播台";
}
