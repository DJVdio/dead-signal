using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 补餐回升机制单测（用户拍板"应该能补回来"）：一个相位内吃第二餐 = 净 +1，回升 clamp 到该角色上限 Cap；
/// 物资充裕时用双倍食物把掉档者喂回，饿死(0)仍是终态不复活。
/// 两层：饥饿刻度层（ResolvePhase 第一餐 → Feed 第二餐的相位序列）+ 供餐分配层（先保第一餐、余粮补最饿）。
/// 数值皆"拟定待调"，测试锁的是规则形态。
/// </summary>
public class MealRecoveryTests
{
    private static FoodDiner D(int hunger, bool wounded = false, int cap = HungerState.DefaultCap)
        => new(hunger, wounded, cap);

    // ================= 刻度层：一个相位内的"第一餐维持 + 第二餐回升" =================

    [Fact]
    public void PhaseSecondMeal_FallenPawn_NetPlusOne_Recovers()
    {
        // 掉档到饥饿(3)者：本相位第一餐 ResolvePhase(true) 净零维持在 3，第二餐 Feed() +1 → 4。整相净 +1。
        var h = new HungerState(value: (int)HungerLevel.Hungry); // 3
        Assert.False(h.ResolvePhase(ate: true));                 // 第一餐：净零维持
        Assert.Equal((int)HungerLevel.Hungry, h.Value);          // 仍 3
        h.Feed();                                                // 第二餐（补餐）：+1 回升
        Assert.Equal((int)HungerLevel.Peckish, h.Value);         // 4
    }

    [Fact]
    public void PhaseSecondMeal_ClampsToCap_NormalCapFive_NoOverfeed()
    {
        // 已在上限(正常5, Cap5)：第一餐维持 5，第二餐不越过 5（普通角色吃不到吃撑6）。
        var h = new HungerState(value: (int)HungerLevel.Sated, cap: 5);
        h.ResolvePhase(ate: true);
        h.Feed();
        Assert.Equal(5, h.Value);
    }

    [Fact]
    public void PhaseSecondMeal_BigStomach_CapSix_ReachesStuffed()
    {
        // 大胃袋 Cap=6：正常(5)第一餐维持 5，第二餐 +1 → 吃撑(6)，仍不越过 6。
        var h = new HungerState(value: (int)HungerLevel.Sated, cap: HungerState.MaxCap);
        h.ResolvePhase(ate: true);
        h.Feed();
        Assert.Equal((int)HungerLevel.Stuffed, h.Value); // 6
        h.Feed();
        Assert.Equal(6, h.Value);
    }

    [Fact]
    public void PhaseSecondMeal_StarvedTerminal_NoRevive()
    {
        // 饿死(0)是终态：哪怕喂第二餐也不复活。
        var h = new HungerState(value: (int)HungerLevel.Starved);
        h.ResolvePhase(ate: true); // 进餐前已亡：维持 0
        h.Feed();                  // 补餐也不复活
        Assert.Equal(0, h.Value);
        Assert.True(h.IsStarved);
    }

    [Fact]
    public void MultiPhase_DoubleRations_ClimbsBackToCap()
    {
        // 掉到极度饥饿(2)：连续两相各吃两餐（第一餐维持 + 第二餐 +1）→ 2→3→4，两相回升到有点饿。
        var h = new HungerState(value: (int)HungerLevel.Ravenous); // 2
        h.ResolvePhase(ate: true); h.Feed(); // 相1：2→3
        Assert.Equal((int)HungerLevel.Hungry, h.Value);
        h.ResolvePhase(ate: true); h.Feed(); // 相2：3→4
        Assert.Equal((int)HungerLevel.Peckish, h.Value);
    }

    // ================= 分配层：先保第一餐、余粮补餐（最饿优先、每人至多1餐、不浪费） =================

    [Fact]
    public void AllocatePhaseMeal_Surplus_FeedsSecondToLowestStageFirst()
    {
        // 刻度[5,3,4] 均 Cap5，库存 5。第一餐全喂（消耗3、余2）；余粮补掉档者(1号=3,2号=4)，最饿优先。
        var diners = new[] { D(5), D(3), D(4) };
        var r = FoodEconomy.AllocatePhaseMeal(stock: 5, diners, allowSeconds: true);

        Assert.Equal(new[] { true, true, true }, r.First.Fed); // 第一餐全喂
        Assert.Equal(new[] { false, true, true }, r.SecondFed); // 补餐给两名掉档者
        Assert.Equal(2, r.SecondCount);
        Assert.Equal(2, r.SecondConsumed);
        Assert.Equal(5, r.TotalConsumed); // 3 第一餐 + 2 补餐
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void AllocatePhaseMeal_LimitedSurplus_OnlyHungriestGetsSecond()
    {
        // 刻度[5,3,4]，库存 4。第一餐消耗3、余1；补餐只够1份 → 给最饿(1号=3)。
        var diners = new[] { D(5), D(3), D(4) };
        var r = FoodEconomy.AllocatePhaseMeal(stock: 4, diners, allowSeconds: true);

        Assert.Equal(new[] { false, true, false }, r.SecondFed);
        Assert.Equal(1, r.SecondCount);
        Assert.Equal(4, r.TotalConsumed);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void AllocatePhaseMeal_EachPawnAtMostOneSecond()
    {
        // 单个掉档者(3)、库存充裕(9)：第一餐1份、补餐至多1份（不重复补同一人），余7。
        var diners = new[] { D(3) };
        var r = FoodEconomy.AllocatePhaseMeal(stock: 9, diners, allowSeconds: true);

        Assert.Equal(new[] { true }, r.SecondFed);
        Assert.Equal(1, r.SecondCount);
        Assert.Equal(2, r.TotalConsumed); // 1 第一餐 + 1 补餐（就此封顶，不再第三餐）
        Assert.Equal(7, r.Remaining);
    }

    [Fact]
    public void AllocatePhaseMeal_FirstMealShortfall_NoSecondsAtAll()
    {
        // 库存 2 < 3 人第一餐需求：第一餐已耗尽库存（2人吃到、1人挨饿），无余粮补餐。
        var diners = new[] { D(3), D(3), D(3) };
        var r = FoodEconomy.AllocatePhaseMeal(stock: 2, diners, allowSeconds: true);

        Assert.Equal(2, r.First.FedCount);
        Assert.Equal(1, r.First.StarvedCount);
        Assert.All(r.SecondFed, Assert.False); // 没保住全员第一餐 → 一律不补
        Assert.Equal(0, r.SecondCount);
        Assert.Equal(2, r.TotalConsumed);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void AllocatePhaseMeal_EveryoneAtCap_NoFoodWastedOnSeconds()
    {
        // 全员已在上限(正常5,Cap5)、库存充裕(10)：补餐只补"掉档(刻度<Cap)"者 → 无人可补，余粮不浪费。
        var diners = new[] { D(5), D(5), D(5) };
        var r = FoodEconomy.AllocatePhaseMeal(stock: 10, diners, allowSeconds: true);

        Assert.All(r.SecondFed, Assert.False);
        Assert.Equal(0, r.SecondCount);
        Assert.Equal(3, r.TotalConsumed); // 只花第一餐
        Assert.Equal(7, r.Remaining);
    }

    [Fact]
    public void AllocatePhaseMeal_SecondsDisabled_LeavesSurplusUntouched()
    {
        // allowSeconds=false（玩家关补餐囤粮）：即便有掉档者+余粮也不补，余量=第一餐后余量。
        var diners = new[] { D(5), D(3), D(4) };
        var r = FoodEconomy.AllocatePhaseMeal(stock: 5, diners, allowSeconds: false);

        Assert.All(r.SecondFed, Assert.False);
        Assert.Equal(0, r.SecondCount);
        Assert.Equal(3, r.TotalConsumed);
        Assert.Equal(2, r.Remaining);
    }

    [Fact]
    public void AllocatePhaseMeal_BigStomach_EligibleBelowCapSix()
    {
        // 大胃袋 Cap6、正常(5)：5<6 属"掉档"可补，库存 2 → 第一餐1份 + 补餐1份，余0。
        var diners = new[] { D(5, cap: HungerState.MaxCap) };
        var r = FoodEconomy.AllocatePhaseMeal(stock: 2, diners, allowSeconds: true);

        Assert.Equal(new[] { true }, r.SecondFed);
        Assert.Equal(2, r.TotalConsumed);
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void AllocatePhaseMeal_ZeroDiners_NoConsumeNoSeconds()
    {
        var r = FoodEconomy.AllocatePhaseMeal(stock: 7, diners: null, allowSeconds: true);
        Assert.Empty(r.SecondFed);
        Assert.Equal(0, r.SecondCount);
        Assert.Equal(0, r.TotalConsumed);
        Assert.Equal(7, r.Remaining);
    }

    // ================= 库存实扣：按第一餐+补餐总消耗扣减，不越界 =================

    [Fact]
    public void Consume_DeductsExactTotal_IncludingSeconds()
    {
        var res = new CampResources(food: 5);
        int taken = res.Consume(4); // 例如 3 第一餐 + 1 补餐
        Assert.Equal(4, taken);
        Assert.Equal(1, res.Food);
    }

    [Fact]
    public void Consume_ClampsAtZero_NoNegative()
    {
        var res = new CampResources(food: 2);
        int taken = res.Consume(5);
        Assert.Equal(2, taken); // 只扣得到 2
        Assert.Equal(0, res.Food);
    }
}
