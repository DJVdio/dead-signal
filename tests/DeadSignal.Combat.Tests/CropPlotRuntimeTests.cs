using System.Collections.Generic;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【T72·消费层接线】<b>菜园覆盖自检</b> —— 拿<b>真 <see cref="InventoryStore"/> + 真 <see cref="StoryFlags"/></b>
/// 跑通"种下 → 时钟推进 84 游戏小时 → 收获 → 库存真的多了土豆"这<b>两层</b>。
///
/// <para>🔴 <b>这是"纯逻辑绿 ≠ 功能生效"的解药</b>：<see cref="ForageFarmingButcheryTests"/> 测的是纯规则
/// （<see cref="CropPlotLogic"/> 一颗多久熟、收几颗）；<b>本文件测的是运行时编排 <see cref="CropPlotRuntime"/> 真的调了它、
/// 结果真的进了库存</b>——种薯真扣了、计时器真在 <see cref="StoryFlags"/> 里走、土豆真按新分布入了 <see cref="InventoryStore"/>。</para>
///
/// <para><b>两份事实源焊死</b>：容量 16、成熟 84h、种薯 1、收获分布全从 <see cref="CropPlotSpec"/>/<see cref="CropPlotLogic"/> 读，
/// 本文件不硬编码第二份。</para>
/// </summary>
public sealed class CropPlotRuntimeTests
{
    private const string Plot = CropPlotSpec.FurnitureKey + "#1";   // "菜园#1"（走真的家具名口径）
    private static readonly string PotatoKey = CropPlotLogic.CropKey;

    private static InventoryStore InvWithPotatoes(int count)
    {
        var inv = new InventoryStore();
        if (count > 0)
        {
            inv.Add(Materials.Find(PotatoKey)!.Value.ToItem(count));
        }
        return inv;
    }

    /// <summary>
    /// 🔴 <b>核心覆盖自检</b>：种 1 颗 → 分帧把 84 游戏小时喂进 <see cref="CropPlotRuntime.TickGrowth"/> → 收获 → <b>库存真的多了土豆</b>。
    /// 全程走真 <see cref="InventoryStore"/>/<see cref="StoryFlags"/>，不是只掐 <see cref="CropPlotLogic"/>。
    /// </summary>
    [Fact]
    public void 种下_推进84游戏小时_收获_土豆真的进了库存()
    {
        var flags = new StoryFlags();
        var inv = InvWithPotatoes(3);

        // ── 下种（同 cook: 的开工即扣料 + 完工落计时器）──
        Assert.True(CropPlotRuntime.CanPlant(flags, inv, Plot, out _));
        Assert.True(CropPlotRuntime.BeginPlant(inv));                 // 扣 1 种薯
        Assert.Equal(2, inv.MaterialCount(PotatoKey));               // 3 − 1 = 2（真扣了）
        int slot = CropPlotRuntime.CompletePlant(flags, Plot);
        Assert.Equal(1, slot);                                       // 落到第 1 格
        Assert.Equal(1, CropPlotRuntime.PlantedCount(flags, Plot));
        Assert.Equal(0, CropPlotRuntime.RipeCount(flags, Plot));     // 刚下种，没熟
        Assert.Equal(1, CropPlotRuntime.GrowingCount(flags, Plot));

        // ── 分帧推进 84 游戏小时（累积过程，证明每帧 Tick 真在走）──
        CropPlotRuntime.TickGrowth(flags, 40.0);
        Assert.Equal(0, CropPlotRuntime.RipeCount(flags, Plot));     // 走了 40h，还没熟
        CropPlotRuntime.TickGrowth(flags, 40.0);                     // 累计 80h
        Assert.Equal(0, CropPlotRuntime.RipeCount(flags, Plot));
        CropPlotRuntime.TickGrowth(flags, 4.0);                      // 累计 84h = CropPlotLogic.GrowGameHours
        Assert.Equal(1, CropPlotRuntime.RipeCount(flags, Plot));     // 到点即熟

        // ── 收获：走真随机源，rng<0.5 ⇒ 出 2（新分布的 50% 档）──
        (int plants, int potatoes) = CropPlotRuntime.HarvestRipe(flags, inv, Plot, new SequenceRandomSource(0.0));
        Assert.Equal(1, plants);
        Assert.Equal(2, potatoes);                                  // 出 2

        // 🔴 库存真的多了：收前 2 颗（剩的种薯）+ 收获 2 颗 = 4
        Assert.Equal(4, inv.MaterialCount(PotatoKey));

        // 收掉后腾出空格、无幽灵计时器
        Assert.Equal(0, CropPlotRuntime.PlantedCount(flags, Plot));
        Assert.Equal(0, CropPlotRuntime.RipeCount(flags, Plot));
    }

    /// <summary>
    /// 收获入库的<b>数量走新分布 50/25/25 出 2/3/1</b>：三颗同熟，随机序列 [0.0,0.5,0.75] ⇒ 2+3+1 = 6 个土豆真入库。
    /// </summary>
    [Fact]
    public void 收获入库数量走新分布_2加3加1()
    {
        var flags = new StoryFlags();
        var inv = InvWithPotatoes(3);

        // 种满 3 颗（扣光 3 种薯 → 库里 0）
        for (int i = 0; i < 3; i++)
        {
            Assert.True(CropPlotRuntime.BeginPlant(inv));
            CropPlotRuntime.CompletePlant(flags, Plot);
        }
        Assert.Equal(0, inv.MaterialCount(PotatoKey));
        Assert.Equal(3, CropPlotRuntime.PlantedCount(flags, Plot));

        // 一次推满 84h ⇒ 三颗全熟
        CropPlotRuntime.TickGrowth(flags, CropPlotLogic.GrowGameHours);
        Assert.Equal(3, CropPlotRuntime.RipeCount(flags, Plot));

        // 三段分布逐颗钉死：0.0→2、0.5→3、0.75→1
        (int plants, int potatoes) = CropPlotRuntime.HarvestRipe(
            flags, inv, Plot, new SequenceRandomSource(0.0, 0.5, 0.75));
        Assert.Equal(3, plants);
        Assert.Equal(6, potatoes);                 // 2 + 3 + 1
        Assert.Equal(6, inv.MaterialCount(PotatoKey));
        Assert.Equal(0, CropPlotRuntime.PlantedCount(flags, Plot));
    }

    /// <summary>下种扣的是 <see cref="CropPlotLogic.SeedCost"/>（1 颗），且熟前不入库、库存零变化（不白送）。</summary>
    [Fact]
    public void 下种只扣1种薯_未熟不入库()
    {
        var flags = new StoryFlags();
        var inv = InvWithPotatoes(5);

        CropPlotRuntime.BeginPlant(inv);
        CropPlotRuntime.CompletePlant(flags, Plot);
        Assert.Equal(5 - CropPlotLogic.SeedCost, inv.MaterialCount(PotatoKey));   // 只扣种薯

        CropPlotRuntime.TickGrowth(flags, 83.0);                     // 差 1h，未熟
        (int plants, int potatoes) = CropPlotRuntime.HarvestRipe(flags, inv, Plot, new SequenceRandomSource());
        Assert.Equal(0, plants);
        Assert.Equal(0, potatoes);                                  // 没熟 ⇒ 一颗不收、一次点不掷（空序列不抛）
        Assert.Equal(4, inv.MaterialCount(PotatoKey));              // 库存零变化
    }

    /// <summary>两道闸：库里没种薯不能种；种满 16 颗（= <see cref="CropPlotSpec.MaxPlants"/>）后不能再种。</summary>
    [Fact]
    public void 无种薯不能种_满16颗不能再种()
    {
        var flags = new StoryFlags();
        var empty = new InventoryStore();
        Assert.False(CropPlotRuntime.CanPlant(flags, empty, Plot, out string? r1));
        Assert.Contains("种薯", r1);

        // 种满 16 颗（容量从 CropPlotSpec 读，不硬编码）
        var inv = InvWithPotatoes(CropPlotSpec.MaxPlants);
        for (int i = 0; i < CropPlotSpec.MaxPlants; i++)
        {
            Assert.True(CropPlotRuntime.BeginPlant(inv));
            Assert.NotEqual(0, CropPlotRuntime.CompletePlant(flags, Plot));
        }
        Assert.Equal(CropPlotSpec.MaxPlants, CropPlotRuntime.PlantedCount(flags, Plot));
        Assert.Equal(0, CropPlotRuntime.NextFreeSlot(flags, Plot));   // 满了

        var more = InvWithPotatoes(1);
        Assert.False(CropPlotRuntime.CanPlant(flags, more, Plot, out string? r2));
        Assert.Contains("种满", r2);
    }

    /// <summary>整座菜园被拆走 ⇒ 名下所有格的计时器清干净（不留幽灵计时器）。</summary>
    [Fact]
    public void 拆掉菜园_清干净计时器()
    {
        var flags = new StoryFlags();
        var inv = InvWithPotatoes(3);
        for (int i = 0; i < 3; i++)
        {
            CropPlotRuntime.BeginPlant(inv);
            CropPlotRuntime.CompletePlant(flags, Plot);
        }
        Assert.Equal(3, CropPlotRuntime.PlantedCount(flags, Plot));

        CropPlotRuntime.ClearPlot(flags, Plot);
        Assert.Equal(0, CropPlotRuntime.PlantedCount(flags, Plot));
        Assert.Equal(0, flags.Count);   // StoryFlags 里一条幽灵计时器都不剩
    }

    /// <summary>每帧 delta → 游戏小时换算（消费层每帧喂 <see cref="CropPlotRuntime.TickGrowth"/> 的那步）：白天 720s、夜晚 480s 各铺 12 游戏小时。</summary>
    [Fact]
    public void 每帧delta折算游戏小时_白天720_夜晚480()
    {
        // 白天：整整一个白天相位（720s）= 12 游戏小时
        Assert.Equal(12.0, CropPlotRuntime.GameHoursForElapsed(720.0, 720.0), 9);
        // 夜晚：整整一个夜晚相位（480s）= 12 游戏小时
        Assert.Equal(12.0, CropPlotRuntime.GameHoursForElapsed(480.0, 480.0), 9);
        // 一帧 1/60 秒（白天）
        Assert.Equal(12.0 / 720.0 / 60.0, CropPlotRuntime.GameHoursForElapsed(1.0 / 60.0, 720.0), 12);
        // 冻结相位（delta=0）⇒ 不长
        Assert.Equal(0.0, CropPlotRuntime.GameHoursForElapsed(0.0, 720.0), 9);
        // 累积一整昼夜（720 白 + 480 夜）恰好 24 游戏小时（= GameHoursPerDayNightCycle）
        double full = CropPlotRuntime.GameHoursForElapsed(720.0, 720.0) + CropPlotRuntime.GameHoursForElapsed(480.0, 480.0);
        Assert.Equal(CropPlotLogic.GameHoursPerDayNightCycle, full, 9);
    }
}
