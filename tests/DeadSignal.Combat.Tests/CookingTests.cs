using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 烹饪机制（批次21·T14）：热量点 / 份数通式 / 炊具槽位减免 / 信息隐藏。
///
/// <para>
/// <b>边界是本机制的全部</b>——用户给的规则本身就是围着"恰好够/差一点/多一点"写的：
/// 恰好 16 做 1 份、15 做不了、31 做 1 份（浪费 15）、32 做 2 份；装齐锅+烤架后同一组边界整体挪到 12。
/// </para>
/// <para>
/// <b>还有一条测的是"不许说话"</b>：热量不够时 UI 必须**沉默**（不提示"还差几点"）。
/// 这不是 UX 疏漏，是用户要的试错乐趣，所以它得有测试钉着，免得将来被谁"好心修好"。
/// </para>
/// </summary>
public class CookingTests
{
    private static Dictionary<string, int> Pot(params (string Key, int Qty)[] items)
    {
        var d = new Dictionary<string, int>();
        foreach ((string k, int q) in items) d[k] = q;
        return d;
    }

    private static HashSet<CookwareSlot> Slots(params CookwareSlot[] s) => new(s);

    /// <summary>库存管够（只测热量账，不测库存不足那条）。</summary>
    private static Func<string, int> Plenty => _ => 999;

    // ════════════════ 热量点表（用户点名的三个是定值）════════════════

    [Fact]
    public void 用户点名的三个热量点是定值()
    {
        Assert.Equal(6, FoodCalories.Of("rat"));         // 老鼠 6 点
        Assert.Equal(2, FoodCalories.Of("rosehip"));     // 玫瑰果 2 点
        Assert.Equal(30, FoodCalories.Of("ration"));     // 军用单兵口粮 30 点
    }

    [Fact]
    public void 一份饭基础十六点_锅和烤架各减二()
    {
        Assert.Equal(16, CookingLogic.BasePortionCost);

        // ⚠️ 锅和烤架的减免是**两个独立的值**（初值恰好都是 2，但它们不是同一个数）——
        // 用户要在 wiki 上分别调它们；共用一个常量的话，改锅烤架会跟着变，那就调不动了。
        Assert.Equal(2, CookingLogic.PotDiscount);
        Assert.Equal(2, CookingLogic.GrillDiscount);
        Assert.Equal(CookingLogic.PotDiscount, CookingLogic.DiscountOf(CookwareSlot.Pot));
        Assert.Equal(CookingLogic.GrillDiscount, CookingLogic.DiscountOf(CookwareSlot.Grill));
    }

    [Fact]
    public void 每个炊具槽都有自己的减免值_没有一个是白装的()
    {
        // 将来加第三件炊具时，忘了在 DiscountOf 里给它一个数 ⇒ 这条红（它会白占一个槽却不省料）。
        foreach (CookwareSlot slot in Enum.GetValues<CookwareSlot>())
        {
            Assert.True(CookingLogic.DiscountOf(slot) > 0, $"{DisplayNames.Of(slot)} 装上去不省料——它凭什么占一个槽？");
        }
        Assert.Equal(0, CookingLogic.DiscountOf((CookwareSlot)99));   // 未知槽位不白送减免（fail-safe）
    }

    /// <summary>
    /// 锅与烤架的减免**可以互不相等**（结构上真的拆开了，不是换个名字的同一个常量）：
    /// 把两个值各自的贡献单独量出来 —— 装锅省的 = PotDiscount，装烤架省的 = GrillDiscount。
    /// </summary>
    [Fact]
    public void 锅与烤架的减免各自独立生效()
    {
        int bare = CookingLogic.PortionCost(Slots());
        Assert.Equal(bare - CookingLogic.PotDiscount, CookingLogic.PortionCost(Slots(CookwareSlot.Pot)));
        Assert.Equal(bare - CookingLogic.GrillDiscount, CookingLogic.PortionCost(Slots(CookwareSlot.Grill)));
        Assert.Equal(
            bare - CookingLogic.PotDiscount - CookingLogic.GrillDiscount,
            CookingLogic.PortionCost(Slots(CookwareSlot.Pot, CookwareSlot.Grill)));
    }

    [Fact]
    public void 每份需求有下限一_减免调过头也不会白送饭()
    {
        // 下限 1：将来谁把减免调到 8+8=16，每份需求会归零/变负 ⇒ 除零或无限出饭。这条钉住兜底。
        Assert.True(CookingLogic.PortionCost(Slots(CookwareSlot.Pot, CookwareSlot.Grill)) >= 1);
    }

    [Fact]
    public void 每种食材都登记了重量_不吃兜底默认值()
    {
        // 原先 10 种新食材一条都没进 ItemWeights._materialKg ⇒ 全落兜底 0.5kg
        //（一只老鼠和一箱军用口粮一样重）。这是个真 bug，这条钉住它不再回来。
        foreach (FoodCalories.FoodDef def in FoodCalories.All)
        {
            double kg = ItemWeights.MaterialKg(def.Key);
            Assert.True(kg > 0, $"{def.Key} 没有重量");
            Assert.NotEqual(ItemWeights.DefaultMaterialKg, kg);   // 没登记就会掉进这个兜底值
        }

        // 顺带钉住量级：军用单兵口粮（全表最顶饱）必须明显比玫瑰果沉——背粮食是要占背包的。
        Assert.True(ItemWeights.MaterialKg("ration") > ItemWeights.MaterialKg("rosehip") * 10);
    }

    [Fact]
    public void 每条食材都有稳定id且是目录里真实存在的材料()
    {
        // 稳定 id = 材料键：wiki 的抽取脚本按它定位，散落的硬编码抽不出来。
        Assert.NotEmpty(FoodCalories.All);
        foreach (FoodCalories.FoodDef def in FoodCalories.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(def.Key));
            Assert.True(def.Calories > 0, $"{def.Key} 的热量点必须为正");
            Assert.True(Materials.Has(def.Key), $"食材「{def.Key}」不在 Materials 目录里（下不了锅也进不了库存）");
        }
        Assert.Equal(FoodCalories.All.Count, FoodCalories.All.Select(f => f.Key).Distinct().Count());
    }

    [Fact]
    public void 玫瑰果同时是食材和医疗原料_两边共存不打架()
    {
        // impl-medicine 把玫瑰果归「材料·医疗」；它同时是用户点名的 2 点食材。
        // 热量点是**外挂表**（不是 MaterialDef 的字段）⇒ 一个东西既能进药也能下锅，两边零冲突。
        Assert.Equal(MaterialCategory.Medical, Materials.Find("rosehip")!.Value.Category);
        Assert.True(FoodCalories.Has("rosehip"));
    }

    [Fact]
    public void 不是食材的东西一律零热量_不抛()
    {
        Assert.False(FoodCalories.Has("wood"));
        Assert.Equal(0, FoodCalories.Of("wood"));
        Assert.Equal(0, FoodCalories.Of("does_not_exist"));
        Assert.Equal(0, FoodCalories.Of(null));
    }

    // ════════════════ 每份需求：槽位减免 ════════════════

    [Fact]
    public void 空槽十六点_装一件十四点_装两件十二点()
    {
        Assert.Equal(16, CookingLogic.PortionCost(Slots()));
        Assert.Equal(14, CookingLogic.PortionCost(Slots(CookwareSlot.Pot)));
        Assert.Equal(14, CookingLogic.PortionCost(Slots(CookwareSlot.Grill)));
        Assert.Equal(12, CookingLogic.PortionCost(Slots(CookwareSlot.Pot, CookwareSlot.Grill)));
    }

    [Fact]
    public void 炊具槽装卸幂等_装不了两口锅()
    {
        var station = new CookStationState();
        Assert.True(station.Install(CookwareSlot.Pot));
        Assert.False(station.Install(CookwareSlot.Pot));      // 第二口锅装不进去（HashSet 天然保证"两个槽"）
        Assert.True(station.Has(CookwareSlot.Pot));
        Assert.Single(station.Installed);

        Assert.True(station.Remove(CookwareSlot.Pot));
        Assert.False(station.Remove(CookwareSlot.Pot));       // 卸过了就没了
        Assert.Empty(station.Installed);
    }

    // ════════════════ 份数通式：⌊总热量 ÷ 每份需求⌋，不封顶 ════════════════

    [Theory]
    // 裸台（每份 16）——任务书点名的四条边界
    [InlineData(16, 16, 1)]   // 恰好一份
    [InlineData(15, 16, 0)]   // 差一点 → 做不了
    [InlineData(31, 16, 1)]   // 一到两份之间 → 只做一份（浪费 15，不提示）
    [InlineData(32, 16, 2)]   // 够两份 → 两份
    // 装齐锅+烤架（每份 12）——同一组边界整体挪到 12
    [InlineData(12, 12, 1)]
    [InlineData(11, 12, 0)]
    [InlineData(23, 12, 1)]
    [InlineData(24, 12, 2)]
    public void 份数等于总热量整除每份需求(int total, int cost, int expected)
        => Assert.Equal(expected, CookingLogic.Portions(total, cost));

    [Fact]
    public void 份数不封顶_通式一路往上()
    {
        // 用户举的"1~2份"/"超过两份"是这条通式的实例，不是两条特例：
        Assert.Equal(3, CookingLogic.Portions(48, 16));
        Assert.Equal(10, CookingLogic.Portions(160, 16));
        Assert.Equal(100, CookingLogic.Portions(1600, 16));
    }

    [Fact]
    public void 零和负数不出份数()
    {
        Assert.Equal(0, CookingLogic.Portions(0, 16));
        Assert.Equal(0, CookingLogic.Portions(-5, 16));
        Assert.Equal(0, CookingLogic.Portions(100, 0));    // 每份 0 点：不许除零、也不许白送
    }

    // ════════════════ 下单判定：恰好/差一点/浪费/两份，走真食材 ════════════════

    [Fact]
    public void 裸台恰好十六点_做一份()
    {
        // 老鼠 6×2 = 12 + 玫瑰果 2×2 = 4 → 16 点，正好一份。
        CookPlan plan = CookingLogic.Plan(true, Pot(("rat", 2), ("rosehip", 2)), Slots(), Plenty);

        Assert.True(plan.CanCook);
        Assert.Equal(16, plan.TotalCalories);
        Assert.Equal(16, plan.PortionCost);
        Assert.Equal(1, plan.Portions);
    }

    [Fact]
    public void 裸台十五点_做不了_而且一个字都不说()
    {
        // 兔子 11 + 土豆 4 = 15 点，差一点（裸台每份 16）。
        // （[T59] 原本这里用「蒲公英 1」当那 1 点零头；蒲公英已不是食材，改用别的食材凑同一个 15 点——
        //   这条测的是**热量算术与沉默**，不是蒲公英。）
        CookPlan plan = CookingLogic.Plan(
            true, Pot(("rabbit", 1), ("potato", 1)), Slots(), Plenty);

        Assert.False(plan.CanCook);
        Assert.Equal(15, plan.TotalCalories);
        Assert.Equal(0, plan.Portions);
        Assert.Contains(plan.Blocks, b => b.Reason == CookBlockReason.NotEnoughCalories);

        // ★信息隐藏的支点：玩家侧**沉默**——不提示"还差 1 点"，只有按钮灰着。
        Assert.Null(CookingLogic.PlayerFacingText(plan.Blocks));
    }

    [Fact]
    public void 裸台三十一点_只做一份_零头十五点静默蒸发()
    {
        // 罐头 16 + 兔子 11 + 土豆 4 = 31 点。
        // （[T59] 原本是「口粮 30 + 蒲公英 1」；蒲公英已不是食材，换一组食材凑同一个 31 点。）
        CookPlan plan = CookingLogic.Plan(
            true, Pot(("canned_food", 1), ("rabbit", 1), ("potato", 1)), Slots(), Plenty);

        Assert.True(plan.CanCook);
        Assert.Equal(31, plan.TotalCalories);
        Assert.Equal(1, plan.Portions);          // 31 / 16 = 1（不是 1.9 份，也不是"攒着下次用"）

        // 浪费的 15 点：既不返还，也**不在任何给玩家的文案里出现**。
        Assert.Null(CookingLogic.PlayerFacingText(plan.Blocks));
    }

    [Fact]
    public void 裸台三十二点_做两份()
    {
        // 口粮 30 + 玫瑰果 2×1 = 32。
        CookPlan plan = CookingLogic.Plan(true, Pot(("ration", 1), ("rosehip", 1)), Slots(), Plenty);

        Assert.True(plan.CanCook);
        Assert.Equal(32, plan.TotalCalories);
        Assert.Equal(2, plan.Portions);          // ← 玩家唯一能观察到的信号：产物那行变成「食物 ×2」
    }

    [Fact]
    public void 装齐锅和烤架后_十二点就做一份_二十三点仍是一份_二十四点两份()
    {
        var full = Slots(CookwareSlot.Pot, CookwareSlot.Grill);

        // 12 点（老鼠 2 只）→ 裸台做不了的量，装齐炊具就够一份了。
        CookPlan twelve = CookingLogic.Plan(true, Pot(("rat", 2)), full, Plenty);
        Assert.True(twelve.CanCook);
        Assert.Equal(12, twelve.TotalCalories);
        Assert.Equal(12, twelve.PortionCost);
        Assert.Equal(1, twelve.Portions);

        // 23 点（老鼠 3 + 罐头 0…用兔子 11 + 老鼠 2×6 = 23）→ 一到两份之间，只做一份。
        CookPlan twentyThree = CookingLogic.Plan(true, Pot(("rabbit", 1), ("rat", 2)), full, Plenty);
        Assert.Equal(23, twentyThree.TotalCalories);
        Assert.Equal(1, twentyThree.Portions);

        // 24 点（老鼠 4 只）→ 两份。
        CookPlan twentyFour = CookingLogic.Plan(true, Pot(("rat", 4)), full, Plenty);
        Assert.Equal(24, twentyFour.TotalCalories);
        Assert.Equal(2, twentyFour.Portions);
    }

    [Fact]
    public void 装齐炊具后十一点做不了_同样沉默()
    {
        // 老鼠 1（6） + 蘑菇 1（3） + 玫瑰果 1（2） = 11 → 每份 12，还差 1。
        // （[T59] 原本用「蒲公英 1」凑那 1 点；蒲公英已不是食材，换成蘑菇+玫瑰果凑同一个 11 点。）
        CookPlan plan = CookingLogic.Plan(
            true, Pot(("rat", 1), ("mushroom", 1), ("rosehip", 1)),
            Slots(CookwareSlot.Pot, CookwareSlot.Grill), Plenty);

        Assert.False(plan.CanCook);
        Assert.Equal(11, plan.TotalCalories);
        Assert.Equal(0, plan.Portions);
        Assert.Null(CookingLogic.PlayerFacingText(plan.Blocks));
    }

    // ════════════════ 门槛：没灶 / 空锅 / 库存不够 ════════════════

    [Fact]
    public void 没有烹饪台_做不了()
    {
        CookPlan plan = CookingLogic.Plan(false, Pot(("ration", 2)), Slots(), Plenty);

        Assert.False(plan.CanCook);
        Assert.Contains(plan.Blocks, b => b.Reason == CookBlockReason.NoStation);
        // 这条**可以**说：营地里有没有那座灶，玩家自己看得见，说了不泄露任何热量信息。
        Assert.Equal("做饭得有个灶——先在工作台上把烹饪台砌出来。", CookingLogic.PlayerFacingText(plan.Blocks));
    }

    [Fact]
    public void 空锅做不了()
    {
        CookPlan plan = CookingLogic.Plan(true, Pot(), Slots(), Plenty);

        Assert.False(plan.CanCook);
        Assert.Equal(0, plan.Portions);
        Assert.Contains(plan.Blocks, b => b.Reason == CookBlockReason.NothingInThePot);
        Assert.Equal("空锅煮不出饭。", CookingLogic.PlayerFacingText(plan.Blocks));
    }

    [Fact]
    public void 放得比库存还多_拦下()
    {
        CookPlan plan = CookingLogic.Plan(
            true, Pot(("ration", 5)), Slots(), key => key == "ration" ? 2 : 0);

        Assert.False(plan.CanCook);
        Assert.Contains(plan.Blocks, b => b.Reason == CookBlockReason.InsufficientIngredient);
        Assert.Equal("库里没有那么多。", CookingLogic.PlayerFacingText(plan.Blocks));
    }

    [Fact]
    public void 往锅里塞木头_不给它热量()
    {
        // 木料不是食材：不该被静默当成 0 热量的合法投入，而该明确拦下（UI 本来也不会列它）。
        CookPlan plan = CookingLogic.Plan(true, Pot(("wood", 10)), Slots(), Plenty);

        Assert.False(plan.CanCook);
        Assert.Equal(0, plan.TotalCalories);
    }

    // ════════════════ 烹饪台设施 ════════════════

    [Fact]
    public void 烹饪台是实心家具_要挖导航洞_不许贴防线()
    {
        Assert.True(CookStation.IsSolid);
        Assert.True(CookStation.CarvesNavHole);

        // 没有贴防线的豁免（缺省 false）——它挡路，摆得刁钻就是一堵墙。沙袋才有豁免（它恒不挡路）。
        Assert.False(CookStation.Spec.AllowedAgainstDefenses);
        Assert.True(CookStation.Spec.IsSolid);
        Assert.Equal(CookStation.PropName, CookStation.Spec.TypeName);
    }

    [Fact]
    public void 场上有没有烹饪台_按家具名归一判定()
    {
        Assert.False(CookStation.HasStation(new[] { "工作台", "改装台" }));
        Assert.True(CookStation.HasStation(new[] { "工作台", "烹饪台" }));
        Assert.True(CookStation.HasStation(new[] { "烹饪台#2" }));   // 实例名带流水号也算
        Assert.False(CookStation.HasStation(null));
    }

    [Fact]
    public void 烹饪台与锅与烤架都有配方_都造得出来()
    {
        RecipeData? station = RecipeBook.Find(CookStation.RecipeId);
        Assert.NotNull(station);
        Assert.Equal(CookStation.ItemKey, station!.OutputKey);
        Assert.True(station.WorkMinutes > 0);
        Assert.NotEmpty(station.MaterialCosts);

        foreach (CookwareSlot slot in Enum.GetValues<CookwareSlot>())
        {
            string itemKey = CookStation.ItemKeyOf(slot);
            RecipeData? r = RecipeBook.All.FirstOrDefault(x => x.OutputKey == itemKey);
            Assert.True(r is not null, $"{DisplayNames.Of(slot)} 没有配方——用户要求锅/烤架各自要能造");
            Assert.True(r!.WorkMinutes > 0);
            Assert.NotEmpty(r.MaterialCosts);
        }
    }

    [Fact]
    public void 烹饪台是固定位置设施_不是可摆放的库存物品()
    {
        // 用户拍板（本批次内改口）：「改装台、烹饪台**不允许跨越**，但他们是营地内**固定位置**…**烹饪台放在厨房**」
        // ⇒ 它**不进库存、玩家摆不了**，造完自动砌在厨房锚点上。
        // 谁要把"摆放"加回来，先回去读 CookStation 的类注——它实心、挖导航洞、玩家挪不动，
        // 可自由摆放 = kill box 的后门重新打开。
        Assert.True(CookStation.AnchorX > 0);
        Assert.True(CookStation.AnchorY > 0);

        // 配方带「一座就够」门槛（第二座灶毫无用处，别让玩家把料喂进去）。
        RecipeData station = RecipeBook.Find(CookStation.RecipeId)!;
        Assert.NotNull(station.RequiredCrafterGates);
        Assert.Contains(CookStation.AbsentGate, station.RequiredCrafterGates!);
    }

    [Fact]
    public void 烹饪台可拆_成本表里有它()
    {
        Assert.NotNull(FurnitureBuildCost.Of(CookStation.PropName));
        Assert.NotNull(FurnitureBuildCost.BuildMinutes(CookStation.PropName));
        Assert.NotNull(FurnitureBuildCost.Of($"{CookStation.PropName}#2"));   // 实例名归一
    }

    [Fact]
    public void 炊具槽有中文名_不会把英文枚举名甩给玩家()
    {
        Assert.Equal("锅", DisplayNames.Of(CookwareSlot.Pot));
        Assert.Equal("烤架", DisplayNames.Of(CookwareSlot.Grill));
        Assert.Equal(DisplayNames.Unknown, DisplayNames.Of((CookwareSlot)99));
    }

    // ════════════════ 工时任务 id（挤进制作那条队列）════════════════

    [Fact]
    public void 烹饪任务id编份数_可解回()
    {
        string id = CookingLogic.JobIdFor(2);
        Assert.True(CookingLogic.IsCookJob(id));
        Assert.Equal(2, CookingLogic.PortionsOf(id));

        // 与制作/拆解/改装的任务 id 命名空间互不重叠（同一条队列，靠前缀分身份）。
        Assert.False(CookingLogic.IsCookJob("chair"));
        Assert.False(CookingLogic.IsCookJob(SalvageLogic.JobIdFor("chair")));
        Assert.False(CookingLogic.IsCookJob("weaponmod:步枪|刺刀型"));   // 改装那条队列（字面量：不耦合并发中的 impl-gunmod 文件）
        Assert.Null(CookingLogic.PortionsOf("chair"));
        Assert.Null(CookingLogic.PortionsOf(null));
    }

    [Fact]
    public void 工时按份数线性_做两份就干两份的活()
    {
        Assert.Equal(CookingLogic.WorkMinutesPerPortion, CookingLogic.WorkMinutesFor(1));
        Assert.Equal(CookingLogic.WorkMinutesPerPortion * 2, CookingLogic.WorkMinutesFor(2));
        Assert.Equal(0, CookingLogic.WorkMinutesFor(0));
    }

    // ════════════════ 信息隐藏的总闸（这条红了，说明有人"好心"把答案说出去了）════════════════

    [Fact]
    public void 玩家侧文案里永远不出现热量数字()
    {
        // 遍历一批"热量不够"的锅：任一给玩家的文案里都不许出现数字（16 / 12 / 还差几点…）。
        foreach (int rats in Enumerable.Range(0, 3))
        {
            CookPlan plan = CookingLogic.Plan(true, Pot(("rat", rats)), Slots(), Plenty);
            string? text = CookingLogic.PlayerFacingText(plan.Blocks);
            if (text is null) continue;
            Assert.DoesNotContain(text, char.IsDigit);
            Assert.DoesNotContain("热量", text);
        }
    }
}
