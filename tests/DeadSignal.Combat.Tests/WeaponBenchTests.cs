using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class WeaponBenchTests
{
    [Fact]
    public void 武器台是真实独立设施_稳定身份与槽键不冒充工作台()
    {
        Assert.Equal("武器台", WeaponBench.FurnitureKey);
        Assert.Equal("weapon_bench", WeaponBench.RecipeId);
        Assert.Equal("weaponbench:main", FacilityJobKeys.MainWeaponBench);
        Assert.NotEqual(FacilityJobKeys.MainWorkbench, FacilityJobKeys.MainWeaponBench);
        Assert.True(WeaponBench.Spec.IsSolid);
    }

    [Fact]
    public void 武器台建造数值由配方与家具配置共同登记()
    {
        RecipeData recipe = Assert.IsType<RecipeData>(RecipeBook.Find(WeaponBench.RecipeId));

        Assert.Equal(WeaponBench.ItemKey, recipe.OutputKey);
        Assert.Equal(recipe.WorkMinutes, FurnitureBuildCost.BuildMinutes(WeaponBench.FurnitureKey));
        Assert.Equal(recipe.MaterialCosts, FurnitureBuildCost.Of(WeaponBench.FurnitureKey));
    }

    [Theory]
    [InlineData("bone_knife")]
    [InlineData("handmade_bow")]
    [InlineData("improvised_hunting_gun")]
    [InlineData("improvised_shotgun")]
    [InlineData("recurve_bow")]
    [InlineData("longbow")]
    [InlineData("light_crossbow")]
    [InlineData("heavy_crossbow")]
    [InlineData("axe")]
    [InlineData("repair_sniper_rifle")]
    [InlineData("improvised_pistol")]
    [InlineData("dentist_pistol")]
    public void 每条武器制造配方都路由武器台(string recipeId)
    {
        Assert.NotNull(RecipeBook.Find(recipeId));
        Assert.True(WeaponBench.IsWeaponRecipe(recipeId));
    }

    [Theory]
    [InlineData("weapon_bench")]
    [InlineData("ammo_short")]
    [InlineData("war_mask")]
    [InlineData("cook_station")]
    public void 设施弹药护甲不会误路由武器台(string recipeId)
        => Assert.False(WeaponBench.IsWeaponRecipe(recipeId));
}
