using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 圈套陷阱（<see cref="TrapLogic"/> / <see cref="TrapSpec"/>）的规则护栏。
///
/// <para>用户原话：「<b>陷阱机制是每个相位都有 30% 的几率抓到老鼠或者兔子，每多放置一个，
/// 多的那个几率就会减小 5%，最低到 5%。</b>」</para>
/// </summary>
public class TrapTests
{
    // ───────────────────────── 几率递减阶梯（用户原话的直接编码）─────────────────────────

    [Theory]
    [InlineData(1, 0.30)]   // 第 1 个：30%（用户给定的基准）
    [InlineData(2, 0.25)]   // 第 2 个：每多放一个减 5%
    [InlineData(3, 0.20)]
    [InlineData(4, 0.15)]
    [InlineData(5, 0.10)]
    [InlineData(6, 0.05)]   // 第 6 个：撞到 5% 地板
    [InlineData(7, 0.05)]   // 地板之后恒为 5%……
    [InlineData(10, 0.05)]  // ……第 10 个也还是 5%，**绝不变负**
    [InlineData(999, 0.05)] // 极端值也不越过地板（防 30% − 5%×998 = −4960% 这种穿底）
    public void 第n个陷阱的几率_每多一个减5个百分点_封底5个百分点(int ordinal, double expected)
    {
        Assert.Equal(expected, TrapLogic.ChanceOf(ordinal), 10);
    }

    [Fact]
    public void 几率永不为负_也永不超过基准()
    {
        for (int n = 1; n <= 200; n++)
        {
            double p = TrapLogic.ChanceOf(n);
            Assert.InRange(p, TrapLogic.MinChance, TrapLogic.BaseChance);
        }
    }

    [Fact]
    public void 序号非正_视作没有这个陷阱_几率为零()
    {
        // 防御性：0 / 负序号不该被当成"第 1 个"白送 30%。
        Assert.Equal(0.0, TrapLogic.ChanceOf(0), 10);
        Assert.Equal(0.0, TrapLogic.ChanceOf(-3), 10);
    }

    // ───────────────────────── 每相位独立判定（不是"全营地一次判定"）─────────────────────────

    [Fact]
    public void 每个陷阱每相位各掷一次_三个陷阱就是三次独立判定()
    {
        // 三个陷阱 ⇒ 三次命中判定。给"全中"的序列：0.0 恒小于任何几率。
        // 每次命中后再掷一次**物种**（0.99 → 落在老鼠区间，见 RabbitShare）。
        var rng = new SequenceRandomSource(
            0.0, 0.99,   // 第 1 个：命中 → 老鼠
            0.0, 0.99,   // 第 2 个：命中 → 老鼠
            0.0, 0.99);  // 第 3 个：命中 → 老鼠

        IReadOnlyList<string> caught = TrapLogic.RollPhase(3, rng);

        Assert.Equal(3, caught.Count);
        Assert.All(caught, k => Assert.Equal(TrapLogic.RatKey, k));
        Assert.Equal(0, rng.Remaining);   // 序列恰好用尽 ⇒ 掷点次数与预期一致
    }

    [Fact]
    public void 命中判定按各自序号的几率_第6个起只剩地板值()
    {
        // 掷出 0.06：小于 30%/25%/20%/15%/10% ⇒ 前 5 个全中；但**不小于第 6 个的 5%** ⇒ 第 6 个空手。
        // 这正是"边际递减"在一次结算里的样子：同样的运气，第 6 个陷阱就是抓不到。
        var rng = new SequenceRandomSource(
            0.06, 0.99,   // 第 1 个（30%）命中 → 老鼠
            0.06, 0.99,   // 第 2 个（25%）命中 → 老鼠
            0.06, 0.99,   // 第 3 个（20%）命中 → 老鼠
            0.06, 0.99,   // 第 4 个（15%）命中 → 老鼠
            0.06, 0.99,   // 第 5 个（10%）命中 → 老鼠
            0.06);        // 第 6 个（5%）**未命中** ⇒ 不再掷物种

        IReadOnlyList<string> caught = TrapLogic.RollPhase(6, rng);

        Assert.Equal(5, caught.Count);
        Assert.Equal(0, rng.Remaining);   // 空手那个**没有**多掷物种点 ⇒ 随机流不被浪费
    }

    [Fact]
    public void 没命中就不掷物种点_一个陷阱全空手()
    {
        var rng = new SequenceRandomSource(0.9);   // 0.9 ≥ 30% ⇒ 空
        Assert.Empty(TrapLogic.RollPhase(1, rng));
        Assert.Equal(0, rng.Remaining);
    }

    [Fact]
    public void 没有陷阱_不掷点也不产出()
    {
        var rng = new SequenceRandomSource();      // 空序列：只要掷一次点就会抛异常
        Assert.Empty(TrapLogic.RollPhase(0, rng));
        Assert.Empty(TrapLogic.RollPhase(-1, rng));
    }

    // ───────────────────────── 产出：老鼠 或 兔子 ─────────────────────────

    [Fact]
    public void 物种按兔子占比二选一()
    {
        // RabbitShare 以下 → 兔子；以上 → 老鼠。
        var lucky = new SequenceRandomSource(0.0, 0.0);            // 命中 → 物种点 0.0 < RabbitShare ⇒ 兔子
        Assert.Equal(new[] { TrapLogic.RabbitKey }, TrapLogic.RollPhase(1, lucky));

        var plain = new SequenceRandomSource(0.0, 0.999);          // 命中 → 物种点 ≥ RabbitShare ⇒ 老鼠
        Assert.Equal(new[] { TrapLogic.RatKey }, TrapLogic.RollPhase(1, plain));
    }

    [Fact]
    public void 产出的两个键_都是既有食材_能直接下锅()
    {
        // 陷阱是烹饪的稳定食材来源：抓到的东西必须同时是**材料目录项**（能入库存）
        // 与**食材**（有热量点，下得了锅）。任一边没登记，这条经济链就断了。
        foreach (string key in new[] { TrapLogic.RatKey, TrapLogic.RabbitKey })
        {
            Assert.True(Materials.Has(key), $"{key} 不在材料目录里，抓到了也入不了库存");
            Assert.True(FoodCalories.Has(key), $"{key} 不是食材，抓到了也下不了锅");
        }

        // 用户给定的热量：老鼠 6 / 兔子 11。兔子更值钱 ⇒ 它该是更稀有的那个（RabbitShare < 0.5）。
        Assert.Equal(6, FoodCalories.Of(TrapLogic.RatKey));
        Assert.Equal(11, FoodCalories.Of(TrapLogic.RabbitKey));
        Assert.True(TrapLogic.RabbitShare < 0.5, "兔子比老鼠值钱，不该比老鼠还常见");
    }

    // ───────────────────────── 期望产出（经济平衡的关键数字）─────────────────────────

    [Fact]
    public void 每相位期望捕获数_是前n项几率之和()
    {
        Assert.Equal(0.00, TrapLogic.ExpectedCatchesPerPhase(0), 10);
        Assert.Equal(0.30, TrapLogic.ExpectedCatchesPerPhase(1), 10);
        Assert.Equal(0.75, TrapLogic.ExpectedCatchesPerPhase(3), 10);   // .30+.25+.20
        Assert.Equal(1.05, TrapLogic.ExpectedCatchesPerPhase(6), 10);   // +.15+.10+.05
        // 第 7 个起每个只加 5% ⇒ 边际收益被摁死在地板上（这正是递减机制的意图）。
        Assert.Equal(1.10, TrapLogic.ExpectedCatchesPerPhase(7), 10);
    }

    [Fact]
    public void 一天八个相位_一个陷阱的期望产出()
    {
        // 一天 8 相位（DayPhase 8 个值），每相位各判一次 ⇒ 一个陷阱每天期望 0.30 × 8 = 2.4 只。
        Assert.Equal(8, System.Enum.GetValues<DayPhase>().Length);
        Assert.Equal(2.4, TrapLogic.ExpectedCatchesPerPhase(1) * TrapLogic.PhasesPerDay, 10);
    }

    // ───────────────────────── 陷阱作为可放置物 ─────────────────────────

    [Fact]
    public void 陷阱不实心_不挖导航洞_摆不出killbox()
    {
        // 与沙袋/床同论证：实心家具是"墙"的后门。陷阱是贴地矮物，恒不挡路。
        Assert.False(TrapSpec.IsSolid);
        Assert.False(TrapSpec.CarvesNavHole);
    }

    [Fact]
    public void 陷阱可跨越_跨过要减速()
    {
        // 矮物 ⇒ 走 FurnitureTraversal 的缺省"可跨越"（与椅子/沙袋同类），不进不可跨越的作业台名册。
        Assert.True(FurnitureTraversal.IsTraversable(TrapSpec.FurnitureKey));
        Assert.True(FurnitureTraversal.IsTraversable($"{TrapSpec.FurnitureNamePrefix}7"));
        Assert.Equal(
            FurnitureTraversal.CrossingSpeedMultiplier,
            FurnitureTraversal.SpeedMultiplierOf($"{TrapSpec.FurnitureNamePrefix}7"));
    }

    [Fact]
    public void 陷阱守禁建带_不许贴着围栏和大门()
    {
        // 陷阱不许糊在防线上（同床/改装台）：那条 64px 带子要留给砌墙工与逃命的人。
        // 缺省 AllowedAgainstDefenses=false ⇒ 走 PlacementRules 的受约束那一侧。
        Assert.False(TrapSpec.PlaceSpec.AllowedAgainstDefenses);

        var bounds = new PlacementRules.Box(0, 0, 1000, 1000);
        var fence = new[] { new PlacementRules.Box(500, 0, 20, 1000) };   // 一段竖围栏
        var none = System.Array.Empty<PlacementRules.Box>();

        // [T27] 陷阱有**户外豁免**（AllowedOutdoors:true —— 它是院子里的铁丝套，不是家具），
        // 故室内区传空表也照样放得下：这正好把"陷阱不受室内约束"一并钉死。
        Assert.True(TrapSpec.PlaceSpec.AllowedOutdoors);

        // 贴着围栏（缓冲带内）→ 拒。**户外豁免不等于防线豁免**，两条限制互相独立。
        Assert.Equal(
            PlacementVerdict.TooCloseToDefenses,
            PlacementRules.CanPlace(TrapSpec.PlaceSpec, new System.Numerics.Vector2(460, 500), bounds, fence, none, none, none));

        // 离远点 → 放得下（露天，且没有任何室内区 —— 陷阱不挑地方）。
        Assert.Equal(
            PlacementVerdict.Ok,
            PlacementRules.CanPlace(TrapSpec.PlaceSpec, new System.Numerics.Vector2(200, 500), bounds, fence, none, none, none));
    }

    [Fact]
    public void 场上陷阱数_按实例名前缀数出来()
    {
        // 几率按"第 n 个"递减 ⇒ 消费层必须数得出场上有几个陷阱。实例名带流水号（"陷阱#3"），同沙袋。
        Assert.True(TrapSpec.IsTrapFurniture("陷阱#1"));
        Assert.True(TrapSpec.IsTrapFurniture("陷阱#12"));
        Assert.False(TrapSpec.IsTrapFurniture("陷阱"));      // 不带号的类型名不是场上实例
        Assert.False(TrapSpec.IsTrapFurniture("沙袋#1"));
        Assert.False(TrapSpec.IsTrapFurniture(null));
    }

    [Fact]
    public void 陷阱可拆_建造成本与配方一致()
    {
        // 拆除返还走 SalvageLogic 的通用规则，而它按 FurnitureBuildCost 算 ⇒ 两处成本必须一致，
        // 否则"造一个拆一个"就成了刷材料的永动机。
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == TrapSpec.RecipeId);
        IReadOnlyDictionary<string, int>? build = FurnitureBuildCost.Of(TrapSpec.FurnitureKey);

        Assert.NotNull(build);
        Assert.Equal(recipe.MaterialCosts.OrderBy(kv => kv.Key), build!.OrderBy(kv => kv.Key));
    }

    // ───────────────────────── Sim 零漂移的结构性护栏 ─────────────────────────

    [Fact]
    public void 陷阱不参与战斗结算_Sim读不到它()
    {
        // 结构性证明的运行期护栏：TrapLogic/TrapSpec 全在 godot 消费层，
        // 引擎（CombatResolver/Duel/Ballistics/Arena）根本不引用它们 ⇒ Sim 的结算路径读不到陷阱。
        // 这条断言钉的是"它不该长出战斗侧的字段"——谁哪天给陷阱加了伤害，这里就会提醒他先想清楚。
        System.Type spec = typeof(TrapSpec);
        Assert.Null(spec.GetProperty("Damage"));
        Assert.Null(spec.GetField("Damage"));
        Assert.Equal("DeadSignal.Godot", spec.Namespace);   // 消费层，不在 DeadSignal.Combat 引擎里
    }
}
