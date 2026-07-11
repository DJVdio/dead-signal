using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 聚餐交互重构的纯逻辑单测（先红后绿）：玩家手动分配结算 <see cref="FoodEconomy.ResolveManual"/>、
/// 分配校验 <see cref="FoodEconomy.IsAllocationValid"/>、面板预填 <see cref="FoodEconomy.Prefill"/>、
/// 以及吃饭动画期间坐/站气泡触发概率 <see cref="MealBubbleDelivery"/>。
/// 账目与既有净零模型同构：份数≥1 走第一餐（净零维持）、份数≥2 走补餐（净 +1）、每人每餐至多 2 份。
/// </summary>
public class MealAllocationTests
{
    private static FoodDiner D(int hunger, bool wounded = false, int cap = HungerState.DefaultCap)
        => new(hunger, wounded, cap);

    // ---------------- ResolveManual：份数→净变化（同构 Fed/SecondFed） ----------------

    [Fact]
    public void ResolveManual_TwoServings_MapsToFedAndSecondFed_NetPlusOne()
    {
        // 每人 2 份：Fed 且 SecondFed → 净 +1（吃两份回升一级）。
        var diners = new[] { D(3), D(4) };
        var r = FoodEconomy.ResolveManual(stock: 10, diners, new[] { 2, 2 });

        Assert.Equal(new[] { true, true }, r.First.Fed);
        Assert.Equal(new[] { true, true }, r.SecondFed);
        Assert.Equal(2, r.SecondCount);
        Assert.Equal(4, r.TotalConsumed);  // 2 人 × 2 份
        Assert.Equal(6, r.Remaining);
    }

    [Fact]
    public void ResolveManual_OneServing_FedButNoSecond_NetZero()
    {
        var diners = new[] { D(3) };
        var r = FoodEconomy.ResolveManual(stock: 5, diners, new[] { 1 });

        Assert.Equal(new[] { true }, r.First.Fed);
        Assert.Equal(new[] { false }, r.SecondFed);
        Assert.Equal(1, r.TotalConsumed);
        Assert.Equal(4, r.Remaining);
    }

    [Fact]
    public void ResolveManual_ZeroServings_NotFed_NetMinusOne()
    {
        // 0 份：既不 Fed 也不 SecondFed → 接入层 ResolvePhase(false) 净 -1（挨饿）。
        var diners = new[] { D(5), D(5) };
        var r = FoodEconomy.ResolveManual(stock: 5, diners, new[] { 0, 1 });

        Assert.Equal(new[] { false, true }, r.First.Fed);
        Assert.Equal(1, r.First.StarvedCount);
        Assert.Equal(1, r.TotalConsumed);
    }

    [Fact]
    public void ResolveManual_ClampsOverTwoServings_ToTwo()
    {
        // 玩家/脏输入给 5 份 → 防御性夹到 2 份。
        var diners = new[] { D(3) };
        var r = FoodEconomy.ResolveManual(stock: 10, diners, new[] { 5 });

        Assert.Equal(new[] { true }, r.First.Fed);
        Assert.Equal(new[] { true }, r.SecondFed);
        Assert.Equal(2, r.TotalConsumed);  // 至多 2 份
    }

    [Fact]
    public void ResolveManual_StarvedDiner_NotAllocatable_ForcedZero()
    {
        // 刻度 0（饿死终态）者即便被指派也强制 0 份（不浪费口粮救不活）。
        var diners = new[] { D(0), D(3) };
        var r = FoodEconomy.ResolveManual(stock: 10, diners, new[] { 2, 1 });

        Assert.False(r.First.Fed[0]);
        Assert.False(r.SecondFed[0]);
        Assert.True(r.First.Fed[1]);
        Assert.Equal(1, r.TotalConsumed);  // 只有 1 号吃了 1 份
    }

    [Fact]
    public void ResolveManual_StockShortfall_FirstServingsBeforeSeconds()
    {
        // 库存 3，两人各点 2 份（想要 4 份）→ 先保两人各第一餐（消 2），余 1 只够补一人第二份。
        var diners = new[] { D(3), D(4) };
        var r = FoodEconomy.ResolveManual(stock: 3, diners, new[] { 2, 2 });

        Assert.Equal(new[] { true, true }, r.First.Fed);   // 两人都拿到第一餐
        Assert.Equal(1, r.SecondCount);                    // 只补得起一份第二餐
        Assert.True(r.SecondFed[0]);                       // 按原序先补 0 号
        Assert.False(r.SecondFed[1]);
        Assert.Equal(3, r.TotalConsumed);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ResolveManual_ZeroDiners_StockUntouched()
    {
        var r = FoodEconomy.ResolveManual(stock: 7, System.Array.Empty<FoodDiner>(), System.Array.Empty<int>());
        Assert.Equal(0, r.First.HeadCount);
        Assert.Equal(7, r.Remaining);
        Assert.Empty(r.First.Fed);
    }

    // ---------------- IsAllocationValid：确认按钮门禁 ----------------

    [Fact]
    public void IsAllocationValid_WithinBoundsAndStock_True()
    {
        var diners = new[] { D(3), D(4) };
        Assert.True(FoodEconomy.IsAllocationValid(stock: 4, diners, new[] { 2, 2 }));
    }

    [Fact]
    public void IsAllocationValid_ExceedsStock_False()
    {
        var diners = new[] { D(3), D(4) };
        Assert.False(FoodEconomy.IsAllocationValid(stock: 3, diners, new[] { 2, 2 })); // 需 4 份 > 库存 3
    }

    [Fact]
    public void IsAllocationValid_OverTwoServings_False()
    {
        var diners = new[] { D(3) };
        Assert.False(FoodEconomy.IsAllocationValid(stock: 10, diners, new[] { 3 })); // 超过每餐 2 份上限
    }

    [Fact]
    public void IsAllocationValid_NegativeServings_False()
    {
        var diners = new[] { D(3) };
        Assert.False(FoodEconomy.IsAllocationValid(stock: 10, diners, new[] { -1 }));
    }

    [Fact]
    public void IsAllocationValid_AllocatesToStarvedDiner_False()
    {
        var diners = new[] { D(0), D(3) };
        Assert.False(FoodEconomy.IsAllocationValid(stock: 10, diners, new[] { 1, 1 })); // 给饿死者分配非法
    }

    [Fact]
    public void IsAllocationValid_ServingsLengthMismatch_False()
    {
        var diners = new[] { D(3), D(4) };
        Assert.False(FoodEconomy.IsAllocationValid(stock: 10, diners, new[] { 1 })); // 漏项
    }

    // ---------------- Prefill：预填 = 现行自动分粮策略 ----------------

    [Fact]
    public void Prefill_EqualsAutoStrategy_FirstMealThenSecondsForHungriest()
    {
        // 库存 5，3 人：先保三人第一餐（消 3），余 2 补最饿两人第二餐（1 号刻度1、2 号刻度2）。
        var diners = new[] { D(5), D(1), D(2) };
        int[] pre = FoodEconomy.Prefill(stock: 5, diners);

        // 校验与 AllocatePhaseMeal 折算一致。
        var phase = FoodEconomy.AllocatePhaseMeal(5, diners, allowSeconds: true, RationStrategy.HungriestFirst);
        int[] expected = Enumerable.Range(0, diners.Length)
            .Select(i => (phase.First.Fed[i] ? 1 : 0) + (phase.SecondFed[i] ? 1 : 0))
            .ToArray();
        Assert.Equal(expected, pre);

        // 三人都至少第一餐；最饿的 1、2 号补到第二餐（各 2 份），最饱的 0 号只第一餐。
        Assert.Equal(1, pre[0]);
        Assert.Equal(2, pre[1]);
        Assert.Equal(2, pre[2]);
    }

    [Fact]
    public void Prefill_IsCleanAllocation_AcceptedByValidator()
    {
        var diners = new[] { D(5), D(1), D(2) };
        int[] pre = FoodEconomy.Prefill(stock: 5, diners);
        Assert.True(FoodEconomy.IsAllocationValid(5, diners, pre));
    }

    // ---------------- MealBubbleDelivery：坐/站气泡触发概率 ----------------

    [Fact]
    public void DeliveryChance_Standing_IsHalfOfSeated()
    {
        double seated = MealBubbleDelivery.DeliveryChance(seated: true);
        double standing = MealBubbleDelivery.DeliveryChance(seated: false);
        Assert.Equal(seated * MealBubbleDelivery.StandingBubbleFactor, standing, 9);
        Assert.Equal(0.5, MealBubbleDelivery.StandingBubbleFactor, 9);
    }

    [Fact]
    public void RollDelivered_Seated_AlwaysDelivers()
    {
        // 坐着基础概率 1.0 → 任何 roll 都播出。
        Assert.True(MealBubbleDelivery.RollDelivered(seated: true, new SequenceRandomSource(0.99)));
    }

    [Fact]
    public void RollDelivered_Standing_DeliversBelowHalf_MissesAtOrAbove()
    {
        // 站着概率 0.5：roll 0.4 < 0.5 → 播；roll 0.6 ≥ 0.5 → 漏听。
        Assert.True(MealBubbleDelivery.RollDelivered(seated: false, new SequenceRandomSource(0.4)));
        Assert.False(MealBubbleDelivery.RollDelivered(seated: false, new SequenceRandomSource(0.6)));
    }
}
