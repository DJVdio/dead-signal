using System.Collections.Generic;
using System.Numerics;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 可建造、可自由摆放的沙袋（用户拍板）。
/// **它获准自由建造的全部理由是"不挡路、不改寻路"** —— 那条是本文件的头号断言。
/// </summary>
public class SandbagTests
{
    private static SandbagSpec.Box Bounds() => new(0, 0, 2400, 1800);

    // ── ① 沙袋不阻挡移动、不改寻路（它能被允许建造的全部理由）──────────

    [Fact]
    public void 沙袋不阻挡移动_不改寻路_这是它区别于墙的地方()
    {
        // 用户拍板"墙不能建"是为了防 kill box（用墙的迷宫牵着敌人寻路）。
        // 沙袋不建碰撞体、不挖导航洞 ⇒ 敌人照样直线冲过来 ⇒ 摆不出 kill box ⇒ 才准建。
        // 谁把这两条改成 true，kill box 就回来了。
        Assert.False(SandbagSpec.IsSolid);
        Assert.False(SandbagSpec.CarvesNavHole);
    }

    [Fact]
    public void 沙袋不阻断近战_区别于围栏那层网()
    {
        Assert.False(SandbagSpec.BlocksMelee);

        // 一南一北隔着沙袋 → 照样能贴身砍（矮物跨得过去）。
        var sandbag = HalfCover.FromRect(1000, 1000, SandbagSpec.Width, SandbagSpec.Height,
            SandbagSpec.CoverChance, SandbagSpec.BlocksMelee);
        Assert.False(CoverLogic.MeleeBlocked(new[] { sandbag }, new Vector2(1030, 980), new Vector2(1030, 1044)));
    }

    // ── ② 沙袋提供 25% 掩体，且双向对称 ─────────────────────────

    [Fact]
    public void 摆好的沙袋提供百分之二十五远程无效_且敌人也能蹲在你的沙袋后面用()
    {
        var field = new CoverField();
        field.Add(1000, 1000, SandbagSpec.Width, SandbagSpec.Height,
            SandbagSpec.CoverChance, SandbagSpec.BlocksMelee);

        // 沙袋 x∈[1000,1060], y∈[1000,1024]。
        Vector2 player = new(1030, 1038);   // 玩家贴南侧
        Vector2 raider = new(1030, 986);    // 劫掠者贴北侧

        // 玩家躲自己的沙袋 → 25%。
        Assert.Equal(0.25f, field.ChanceFor(shooter: raider, target: player), 3);
        // **劫掠者蹲在玩家造的沙袋后面 → 一样 25%**（双向对称，这是它安全的另一半理由：摆不出必胜阵型）。
        Assert.Equal(0.25f, field.ChanceFor(shooter: player, target: raider), 3);
    }

    [Fact]
    public void 沙袋要紧贴才生效_摆了不站过去等于白摆()
    {
        var field = new CoverField();
        field.Add(1000, 1000, SandbagSpec.Width, SandbagSpec.Height, SandbagSpec.CoverChance);

        Vector2 enemy = new(1030, 900);
        Assert.Equal(0f, field.ChanceFor(enemy, new Vector2(1030, 1090)), 3);   // 离沙袋 66px：白摆
        Assert.Equal(0.25f, field.ChanceFor(enemy, new Vector2(1030, 1038)), 3); // 贴住（14px）：生效
    }

    // ── ③ 摆放校验 ───────────────────────────────────────────

    [Fact]
    public void 空地上可以摆()
    {
        Assert.Equal(SandbagSpec.PlacementResult.Ok,
            SandbagSpec.CanPlace(new Vector2(800, 800), Bounds(),
                new List<SandbagSpec.Box>(), new List<SandbagSpec.Box>()));
    }

    [Fact]
    public void 不能摆到营地外面()
    {
        Assert.Equal(SandbagSpec.PlacementResult.OutOfBounds,
            SandbagSpec.CanPlace(new Vector2(10, 10), Bounds(),
                new List<SandbagSpec.Box>(), new List<SandbagSpec.Box>()));
    }

    [Fact]
    public void 不能摆在墙里或桌子里()
    {
        var solids = new List<SandbagSpec.Box> { new(700, 700, 120, 74) }; // 工作台
        Assert.Equal(SandbagSpec.PlacementResult.BlockedBySolid,
            SandbagSpec.CanPlace(new Vector2(760, 740), Bounds(), solids, new List<SandbagSpec.Box>()));
    }

    [Fact]
    public void 不能在同一处堆两垛沙袋()
    {
        var existing = new List<SandbagSpec.Box> { SandbagSpec.BoxAt(new Vector2(800, 800)) };
        Assert.Equal(SandbagSpec.PlacementResult.OverlapsSandbag,
            SandbagSpec.CanPlace(new Vector2(810, 805), Bounds(), new List<SandbagSpec.Box>(), existing));

        // 挪开一点就能摆（可以排成一条沙袋线，但不能摞）。
        Assert.Equal(SandbagSpec.PlacementResult.Ok,
            SandbagSpec.CanPlace(new Vector2(880, 800), Bounds(), new List<SandbagSpec.Box>(), existing));
    }

    // ── ④ 配方与拆除回收（复用 SalvageLogic，不另造）──────────────

    [Fact]
    public void 沙袋配方存在_人人可造_不需要书和工具()
    {
        RecipeData? r = RecipeBook.Find(SandbagSpec.RecipeId);
        Assert.NotNull(r);
        Assert.Equal("沙袋", r!.DisplayName);
        Assert.Empty(r.RequiredBookIds);   // 往布袋里装土，不必先读一本书
        Assert.Empty(r.RequiredTools);     // 也不需要工作台上的家伙
        Assert.Equal(1, r.OutputQuantity); // 一次一垛（堆叠产物不可拆，见 SalvageLogic）
    }

    [Fact]
    public void 拆场上的沙袋返还一半材料_走的是impl_salvage的通用家具路径_没有另造一套()
    {
        // 场上的沙袋按家具拆（同工作台/柜子），走 impl-salvage 的通用目录——沙袋没有任何特例代码。
        Assert.True(SalvageLogic.CanSalvageFurniture("沙袋"));

        IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOfFurniture("沙袋");
        RecipeData r = RecipeBook.Find(SandbagSpec.RecipeId)!;

        // 50% 向下取整（严格 ≤ 一半 ⇒ 造→拆→造永远净亏，不存在无限刷）。
        foreach (var kv in r.MaterialCosts)
        {
            int got = yield.TryGetValue(kv.Key, out int q) ? q : 0;
            Assert.True(got * 2 <= kv.Value, $"{kv.Key}: 返还 {got} 超过成本 {kv.Value} 的一半");
        }
        // 布 2 → 1、石料 4 → 2。
        Assert.Equal(1, yield["cloth"]);
        Assert.Equal(2, yield["stone"]);

        // 建造成本与配方一致（拆的是"你当初造它花的料"，不是另一张表）。
        Assert.Equal(r.MaterialCosts["cloth"], FurnitureBuildCost.Of("沙袋")!["cloth"]);
        Assert.Equal(r.MaterialCosts["stone"], FurnitureBuildCost.Of("沙袋")!["stone"]);
    }
}
