using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [A2]《护踝鞋具》配方补齐 —— armor.json 早有该护甲（authored [T72]），却无配方/无掉落 ⇒ 造不出的死物品。
/// 意图护栏（先红/结构性红 → 绿）：①配方存在且门槛=《尖峰时刻》；②没读书造不出、读了书能造；
/// ③材料成本已外置 recipes.json（非默认 0）；④产物落地成一件真护甲 Item.Armor(护踝鞋具)、且穿戴目录登记在脚槽。
///
/// 补齐前：RecipeBook.All 无 OutputKey=="ankle_guard" ⇒ 下面 Single(...) 抛异常（结构性红）；
/// CraftOutputFactory 的 ArmorOutputs 无此 key ⇒ 产物会落进"家具/杂项材料堆"分支变成戴不上的杂物（静默失效）。
/// </summary>
public class AnkleGuardRecipeTests
{
    private static RecipeData Recipe()
        => RecipeBook.All.Single(r => r.OutputKey == "ankle_guard");

    [Fact]
    public void 护踝鞋具配方_门槛是尖峰时刻()
    {
        RecipeData r = Recipe();
        Assert.Equal("护踝鞋具", r.DisplayName);
        Assert.Contains(BookLibrary.PeakHourId, r.RequiredBookIds);
    }

    [Fact]
    public void 没读尖峰时刻_造不出护踝鞋具()
    {
        CraftAvailability a = CraftingLogic.CanCraft(
            Recipe(),
            availableMaterial: _ => 99,
            isBookRead: _ => false,
            installedTools: new HashSet<ToolSlot>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void 读了尖峰时刻并有材料_可造护踝鞋具()
    {
        CraftAvailability a = CraftingLogic.CanCraft(
            Recipe(),
            availableMaterial: _ => 99,
            isBookRead: id => id == BookLibrary.PeakHourId,
            installedTools: new HashSet<ToolSlot>());
        Assert.True(a.CanCraft);
    }

    [Fact]
    public void 护踝鞋具_材料成本已外置非空()
    {
        RecipeData r = Recipe();
        Assert.NotEmpty(r.MaterialCosts);          // recipes.json 已配（否则 fail-fast）
        Assert.All(r.MaterialCosts.Values, q => Assert.True(q > 0));
        Assert.Equal(1, r.OutputQuantity);          // 一件占一只脚（成对，双侧要两件）
    }

    [Fact]
    public void 护踝鞋具产物_落地成真护甲_脚槽登记()
    {
        // 产物落成 Item.Armor（同名引用键），不落"家具/杂项材料堆"。
        List<Item> made = CraftOutputFactory.Create("ankle_guard", 1).ToList();
        Assert.Single(made);
        Assert.Equal("护踝鞋具", made[0].DisplayName);
        Assert.Equal(ItemCategory.Armor, made[0].Category);

        // 穿戴目录登记在脚槽（成对·与运动鞋同槽互斥，一件占一只脚）。
        ApparelCatalog.ApparelDef def = ApparelCatalog.Defs["护踝鞋具"];
        Assert.True(def.Paired);
        Assert.Contains(EquipSlot.LeftFoot, def.Slots);
        Assert.Contains(EquipSlot.RightFoot, def.Slots);
    }
}
