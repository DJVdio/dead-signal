using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 沙发（升级版座位）的纯逻辑护栏：配方/放置入口/座位类型，以及用户拍板的两条乘算效果。
/// 空间落位与寻路由 Godot 消费层接线；这些测试锁规则形状，防止“配方能做但效果没接上”。
/// </summary>
public sealed class SofaTests
{
    [Fact]
    public void 沙发是升级座位_读速乘十二恢复乘九()
    {
        Assert.Equal(1.12, SofaSpec.ReadingSpeedMultiplier, precision: 10);
        Assert.Equal(1.09, SofaSpec.RecoverySpeedMultiplier, precision: 10);
        Assert.False(SofaSpec.PlaceSpec.IsSolid);
        Assert.False(SofaSpec.PlaceSpec.AllowedOutdoors);

        double chair = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.0);
        double sofa = ReadingSpeed.Effective(1.0, selfBonus: 0.0, hasSeat: true, campWideMult: 1.0,
            seatMultiplier: SofaSpec.ReadingSpeedMultiplier);

        Assert.Equal(chair * 1.12, sofa, precision: 10);
        Assert.Equal(1.0, ReadingSpeed.Effective(1.0, 0.0, hasSeat: false, campWideMult: 1.0,
            seatMultiplier: SofaSpec.ReadingSpeedMultiplier) / ReadingSpeed.NoSeatMultiplier, precision: 10);
    }

    [Fact]
    public void 座位登记保留沙发类型_拆除后不可再认领()
    {
        var seats = new SeatRegistry();
        int sofa = seats.Add(10, 20, SeatKind.Sofa);
        int chair = seats.Add(100, 200, SeatKind.Standard);

        Assert.Equal(2, seats.Count);
        Assert.Equal(SeatKind.Sofa, seats.KindOf(sofa));
        Assert.Equal(sofa, seats.ClaimNearest(0, 0));
        Assert.Equal(SeatKind.Sofa, seats.KindOf(sofa));

        seats.Remove(sofa);
        Assert.Equal(1, seats.Count);
        Assert.True(seats.IsOccupied(sofa));
        Assert.Equal(chair, seats.ClaimNearest(0, 0));
        Assert.Equal(SeatKind.Standard, seats.KindOf(99));
    }

    [Fact]
    public void 沙发配方走进阶木匠书_并进入可摆放与木匠工时链()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == SofaSpec.RecipeId);

        Assert.Equal(SofaSpec.ItemKey, recipe.OutputKey);
        Assert.Contains(RecipeBook.AdvancedCarpentryBookId, recipe.RequiredBookIds);
        Assert.Contains(ToolSlot.SawBlade, recipe.RequiredTools);
        Assert.True(CraftWorkTime.IsFurnitureRecipe(recipe));
        Assert.True(PlaceableItems.IsPlaceable(SofaSpec.ItemKey));
        Assert.Equal(SofaSpec.ItemDescription,
            CraftOutputFactory.Create(SofaSpec.ItemKey, 1).Single().Description);
    }

    [Fact]
    public void 沙发可拆_成本与配方同源()
    {
        Assert.NotNull(FurnitureBuildCost.Of(SofaSpec.FurnitureKey));
        Assert.Contains("升级版", FurnitureBuildCost.Description(SofaSpec.FurnitureKey));
        Assert.Equal(FurnitureBuildCost.Of(SofaSpec.FurnitureKey), RecipeBook.All
            .Single(r => r.Id == SofaSpec.RecipeId).MaterialCosts);
    }

    [Fact]
    public void 恢复加成按座位占用比例折算_零占用为零回归()
    {
        Assert.Equal(1.0, SofaSpec.EffectiveHealMultiplier(0.0), precision: 10);
    }

    [Fact]
    public void 恢复加成按座位占用比例折算_全夜坐满得一点零九()
    {
        Assert.Equal(1.09, SofaSpec.EffectiveHealMultiplier(1.0), precision: 10);
    }

    [Fact]
    public void 恢复加成按座位占用比例折算_坐半夜得一半加成()
    {
        double full = SofaSpec.EffectiveHealMultiplier(1.0);    // 1.09
        double half = SofaSpec.EffectiveHealMultiplier(0.5);    // 1.045
        Assert.Equal(1.0 + (full - 1.0) * 0.5, half, precision: 10);
        Assert.Equal(1.045, half, precision: 10);
    }

    [Fact]
    public void 恢复加成按座位占用比例折算_占用比例越界修正()
    {
        Assert.Equal(1.09, SofaSpec.EffectiveHealMultiplier(2.0), precision: 10);
        Assert.Equal(1.0, SofaSpec.EffectiveHealMultiplier(-0.5), precision: 10);
    }
}
