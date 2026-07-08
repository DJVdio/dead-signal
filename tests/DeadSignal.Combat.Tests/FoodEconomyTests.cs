using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 食物稀缺经济模型单测：够吃全喂 / 短缺分粮各策略 / 玩家指定 / 缺口计算 / 边界 / 短缺趋势预警。
/// 只验"谁吃到 / 谁挨饿 / 库存扣减"决策；饥饿→死那条链归 HungerState，不在此测。
/// </summary>
public class FoodEconomyTests
{
    private static FoodDiner D(int hunger, bool wounded = false) => new(hunger, wounded);

    // ---- 够吃：全员喂饱，无缺口 ----

    [Fact]
    public void Allocate_StockCoversAll_EveryoneFed_NoShortfall()
    {
        var diners = new[] { D(5), D(4), D(3) };
        var r = FoodEconomy.Allocate(stock: 5, diners);

        Assert.Equal(3, r.HeadCount);
        Assert.Equal(3, r.Demand);
        Assert.Equal(3, r.FedCount);
        Assert.Equal(0, r.StarvedCount);
        Assert.Equal(0, r.Shortfall);
        Assert.Equal(3, r.Consumed);
        Assert.Equal(2, r.Remaining);
        Assert.True(r.Sufficient);
        Assert.All(r.Fed, Assert.True);
    }

    [Fact]
    public void Allocate_StockExactlyEqualsDemand_AllFed_NothingLeft()
    {
        var diners = new[] { D(5), D(5), D(5) };
        var r = FoodEconomy.Allocate(stock: 3, diners);

        Assert.Equal(3, r.FedCount);
        Assert.Equal(0, r.Remaining);
        Assert.Equal(0, r.Shortfall);
        Assert.True(r.Sufficient);
    }

    // ---- 短缺：分粮策略 ----

    [Fact]
    public void Allocate_AsGiven_FeedsByListOrder_MatchesLegacyFirstN()
    {
        // 既有 ConsumeMeal 行为：前 N 名（原序）吃到。
        var diners = new[] { D(5), D(2), D(4) };
        var r = FoodEconomy.Allocate(stock: 2, diners, RationStrategy.AsGiven);

        Assert.Equal(new[] { true, true, false }, r.Fed);
        Assert.Equal(2, r.FedCount);
        Assert.Equal(1, r.StarvedCount);
    }

    [Fact]
    public void Allocate_HungriestFirst_FeedsLowestHungerValueFirst()
    {
        // 刻度：0号=5(正常) 1号=1(营养不良) 2号=3(饥饿)。库存 2 → 喂最饿两个：1号、2号。
        var diners = new[] { D(5), D(1), D(3) };
        var r = FoodEconomy.Allocate(stock: 2, diners, RationStrategy.HungriestFirst);

        Assert.Equal(new[] { false, true, true }, r.Fed);
        Assert.Equal(2, r.FedCount);
        Assert.Equal(1, r.StarvedCount);
    }

    [Fact]
    public void Allocate_HungriestFirst_StableOnTies_KeepsListOrder()
    {
        // 全同刻度 → 稳定回退原序：库存 1 喂 0 号。
        var diners = new[] { D(3), D(3), D(3) };
        var r = FoodEconomy.Allocate(stock: 1, diners, RationStrategy.HungriestFirst);

        Assert.Equal(new[] { true, false, false }, r.Fed);
    }

    [Fact]
    public void Allocate_WoundedFirst_FeedsWoundedBeforeHealthy_EvenIfLessHungry()
    {
        // 0号 健康且很饿(1)，1号 伤员但没那么饿(4)。库存 1 → 伤员优先：喂 1 号。
        var diners = new[] { D(1, wounded: false), D(4, wounded: true) };
        var r = FoodEconomy.Allocate(stock: 1, diners, RationStrategy.WoundedFirst);

        Assert.Equal(new[] { false, true }, r.Fed);
    }

    [Fact]
    public void Allocate_WoundedFirst_WithinWoundedGroup_HungriestFirst()
    {
        // 两伤员一健康：库存 1 → 伤员里更饿的(刻度1)先吃。
        var diners = new[] { D(4, wounded: true), D(1, wounded: true), D(0) };
        var r = FoodEconomy.Allocate(stock: 1, diners, RationStrategy.WoundedFirst);

        Assert.Equal(new[] { false, true, false }, r.Fed);
    }

    // ---- 玩家指定优先 ----

    [Fact]
    public void Allocate_PlayerPriority_OverridesStrategy()
    {
        // 玩家点名 2 号先吃（哪怕它最饱）；库存 1。playerPriority 覆盖 HungriestFirst。
        var diners = new[] { D(1), D(2), D(5) };
        var r = FoodEconomy.Allocate(stock: 1, diners, RationStrategy.HungriestFirst,
            playerPriority: new[] { 2 });

        Assert.Equal(new[] { false, false, true }, r.Fed);
    }

    [Fact]
    public void Allocate_PlayerPriority_IgnoresOutOfRangeAndDuplicates_FillsRestInOrder()
    {
        // 优先名单含越界(9)/重复(1)：有效为 [1]，其余按原序垫后 → 库存 2 喂 1 号、0 号。
        var diners = new[] { D(5), D(5), D(5) };
        var r = FoodEconomy.Allocate(stock: 2, diners, RationStrategy.AsGiven,
            playerPriority: new[] { 9, 1, 1 });

        Assert.Equal(new[] { true, true, false }, r.Fed);
    }

    // ---- 缺口计算 ----

    [Fact]
    public void Allocate_Shortfall_EqualsUnservedDemand()
    {
        var diners = Enumerable.Repeat(D(3), 5).ToArray();
        var r = FoodEconomy.Allocate(stock: 2, diners, RationStrategy.AsGiven);

        Assert.Equal(5, r.Demand);
        Assert.Equal(2, r.FedCount);
        Assert.Equal(3, r.StarvedCount);
        Assert.Equal(3, r.Shortfall); // 每人 1 份 → 缺口=挨饿人数
        Assert.False(r.Sufficient);
    }

    // ---- 每人多份口粮 ----

    [Fact]
    public void Allocate_RationPerCapitaGreaterThanOne_NeedsFullRationToBeFed()
    {
        // 每人需 2 份，库存 3，2 人 → 只喂得起 1 人，余 1 份（凑不满第二份）。
        var diners = new[] { D(3), D(3) };
        var r = FoodEconomy.Allocate(stock: 3, diners, RationStrategy.AsGiven, rationPerCapita: 2);

        Assert.Equal(4, r.Demand);
        Assert.Equal(1, r.FedCount);
        Assert.Equal(2, r.Consumed);
        Assert.Equal(1, r.Remaining);
        Assert.Equal(2, r.Shortfall); // 1 人挨饿 × 2 份
        Assert.Equal(new[] { true, false }, r.Fed);
    }

    // ---- 边界 ----

    [Fact]
    public void Allocate_ZeroStock_NobodyFed_AllStarve()
    {
        var diners = new[] { D(1), D(2) };
        var r = FoodEconomy.Allocate(stock: 0, diners);

        Assert.Equal(0, r.FedCount);
        Assert.Equal(2, r.StarvedCount);
        Assert.Equal(0, r.Remaining);
        Assert.All(r.Fed, Assert.False);
    }

    [Fact]
    public void Allocate_NegativeStock_TreatedAsZero()
    {
        var r = FoodEconomy.Allocate(stock: -10, new[] { D(3) });
        Assert.Equal(0, r.Stock);
        Assert.Equal(0, r.FedCount);
    }

    [Fact]
    public void Allocate_ZeroDiners_NoDemand_StockUntouched()
    {
        var r = FoodEconomy.Allocate(stock: 7, System.Array.Empty<FoodDiner>());
        Assert.Equal(0, r.HeadCount);
        Assert.Equal(0, r.Demand);
        Assert.Equal(7, r.Remaining);
        Assert.Empty(r.Fed);
        Assert.True(r.Sufficient);
    }

    [Fact]
    public void Allocate_NullDiners_TreatedAsZeroDiners()
    {
        var r = FoodEconomy.Allocate(stock: 3, diners: null);
        Assert.Equal(0, r.HeadCount);
        Assert.Equal(3, r.Remaining);
    }

    // ---- 短缺趋势预警 ----

    [Fact]
    public void PhasesUntilShortfall_FullPhasesBeforeAnyoneStarves()
    {
        // 库存 10，4 人每相各 1 份 → 每相消耗 4，撑 2 整相（第 3 相开始有人挨饿）。
        Assert.Equal(2, FoodEconomy.PhasesUntilShortfall(stock: 10, headCount: 4));
    }

    [Fact]
    public void PhasesUntilShortfall_NoDiners_NeverShortfall()
    {
        Assert.Equal(int.MaxValue, FoodEconomy.PhasesUntilShortfall(stock: 5, headCount: 0));
    }

    [Fact]
    public void DaysUntilShortfall_TwoPhasesPerDay()
    {
        // 每相消耗 2，库存 9 → 4 整相 → 2 整昼夜。
        Assert.Equal(4, FoodEconomy.PhasesUntilShortfall(stock: 9, headCount: 2));
        Assert.Equal(2, FoodEconomy.DaysUntilShortfall(stock: 9, headCount: 2));
    }

    [Fact]
    public void DemandFor_HeadCountTimesRation()
    {
        Assert.Equal(3, FoodEconomy.DemandFor(3));
        Assert.Equal(6, FoodEconomy.DemandFor(3, rationPerCapita: 2));
        Assert.Equal(0, FoodEconomy.DemandFor(0));
    }
}
