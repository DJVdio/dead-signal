using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 端到端：吃 <c>godot/data/camp.json</c> 的**真实围栏坐标**，按 CampMain 同样的规则灌进掩体场
/// （围栏 = 半身掩体 + 阻断近战），验证守家场景下的实际行为。
/// 防的是"几何单测都对、真营地的墙上却不生效"。
/// </summary>
public class RealFenceCoverTests
{
    private static string CampJsonPath()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string p = Path.Combine(d.FullName, "godot", "data", "camp.json");
            if (File.Exists(p))
                return p;
        }
        throw new FileNotFoundException("从测试程序集向上未找到 godot/data/camp.json");
    }

    private sealed class RectSpec
    {
        public double[]? rect { get; set; }
    }

    private sealed class Cfg
    {
        public RectSpec[]? fences { get; set; }
        public RectSpec[]? gates { get; set; }
    }

    /// <summary>照 CampMain 的口径：围栏进掩体场（blocksMelee），**大门不进**（实心，不是掩体）。</summary>
    private static CoverField LoadFences()
    {
        Cfg cfg = JsonSerializer.Deserialize<Cfg>(File.ReadAllText(CampJsonPath()))!;
        var field = new CoverField();
        foreach (RectSpec f in cfg.fences!)
        {
            double[] r = f.rect!;
            field.Add((float)r[0], (float)r[1], (float)r[2], (float)r[3],
                CoverLogic.DefaultCoverChance, blocksMelee: true);
        }
        return field;
    }

    [Fact]
    public void 真实北墙围栏_丧尸在外啃_守卫贴内侧射它_双方各享百分之二十五()
    {
        CoverField fences = LoadFences();

        // camp.json 北墙围栏 [300,300,800,22] → 墙体 y∈[300,322]，营内在 y>322 一侧。
        Vector2 zombieOutside = new(700, 288); // 贴外墙面啃墙（离墙 12px）
        Vector2 guardInside = new(700, 334);   // 贴内墙面（离墙 12px）

        // 守卫受保护（丧尸隔着网咬/抓他）。
        Assert.Equal(0.25f, fences.ChanceFor(shooter: zombieOutside, target: guardInside), 3);
        // 丧尸也受保护（守卫隔着网射它）——用户点名的那条推论，自动涌现，无特例代码。
        Assert.Equal(0.25f, fences.ChanceFor(shooter: guardInside, target: zombieOutside), 3);

        // 两边头顶都该有"掩"标记。
        Assert.NotNull(fences.AdjacentTo(guardInside));
        Assert.NotNull(fences.AdjacentTo(zombieOutside));
    }

    [Fact]
    public void 真实北墙围栏_丧尸咬不到墙内的守卫_也不许拿长矛隔栏捅出去()
    {
        CoverField fences = LoadFences();
        Vector2 zombieOutside = new(700, 288), guardInside = new(700, 334);

        Assert.True(fences.MeleeBlockedBetween(zombieOutside, guardInside)); // 丧尸咬不进来
        Assert.True(fences.MeleeBlockedBetween(guardInside, zombieOutside)); // 长矛也捅不出去
    }

    [Fact]
    public void 大门不是掩体_实心的门挡视线挡弹道_不给百分之二十五()
    {
        CoverField fences = LoadFences(); // 只灌围栏，大门刻意不灌（同 CampMain）

        // camp.json 北门 [1100,300,200,22]：门后站个人，门外来敌 —— 门是实心的，
        // 走的是"完全遮挡"（子弹根本打不到），不该走 25% 掩体这条路。
        Vector2 behindGate = new(1200, 334), outsideGate = new(1200, 288);
        Assert.Equal(0f, fences.ChanceFor(outsideGate, behindGate), 3);
        Assert.Null(fences.AdjacentTo(behindGate));
    }

    [Fact]
    public void 站在院子里离墙远_不算有掩体_得走到墙边贴住()
    {
        CoverField fences = LoadFences();
        Vector2 zombieOutside = new(700, 288);

        Assert.Equal(0f, fences.ChanceFor(zombieOutside, new Vector2(700, 400)), 3);  // 离墙 78px
        Assert.Equal(0.25f, fences.ChanceFor(zombieOutside, new Vector2(700, 340)), 3); // 贴墙 18px
    }
}
