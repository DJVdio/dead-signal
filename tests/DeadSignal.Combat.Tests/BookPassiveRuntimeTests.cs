using System.Collections.Generic;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class BookPassiveRuntimeTests
{
    [Fact]
    public void RecipeBook_ReducesEachPortionByOne_AndDefaultsNeutral()
    {
        var pot = new Dictionary<string, int> { ["ration"] = 1 };
        CookPlan plain = CookingLogic.Plan(true, pot, new HashSet<CookwareSlot>(), _ => 1);
        CookPlan reader = CookingLogic.Plan(true, pot, new HashSet<CookwareSlot>(), _ => 1,
            BookPassiveEffects.FoodCaloriesReduction(_ => true));

        Assert.Equal(16, plain.PortionCost);
        Assert.Equal(15, reader.PortionCost);
        Assert.Equal(1, plain.Portions);
        Assert.Equal(2, reader.Portions);
    }

    [Fact]
    public void SnareBook_AddsFivePointsToBaseCurve_WithoutChangingNoReaderPath()
    {
        Assert.Equal(TrapLogic.BaseChance, TrapLogic.ChanceOf(1), 10);
        Assert.Equal(TrapLogic.BaseChance + 0.05, TrapLogic.ChanceOf(1, 0.05), 10);
        Assert.Equal(TrapLogic.MinChance + 0.05, TrapLogic.ChanceOf(99, 0.05), 10);
    }

    [Fact]
    public void IrishChronicle_ReducesNewPotatoTimerByTwelveHours()
    {
        Assert.Equal(84.0, CropPlotLogic.InitialRemainingHours, 10);
        Assert.Equal(72.0, CropPlotLogic.InitialRemainingHoursFor(12), 10);

        var flags = new StoryFlags();
        Assert.Equal(1, CropPlotRuntime.CompletePlant(flags, "菜园#1", 12));
        Assert.Equal("72", flags.Get(CropPlotLogic.RemainingFlagFor("菜园#1:1")));
    }

    [Fact]
    public void CampWideEffects_AreBooleanGates_NotReaderCountStacks()
    {
        Assert.Equal(0.92, BookPassiveEffects.CampInfectionChanceMultiplier(new[] { true, true }), 10);
        Assert.Equal(0.05, BookPassiveEffects.SnareBaseChanceBonus(new[] { true, true }), 10);
    }

    [Fact]
    public void StreetFighting_IsAppliedAfterHitPartSelection_BeforeArmorResolution()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 10, DamageMax = 10, DamageType = DamageType.Sharp };
        var body = new Body(new[] { new BodyPart { Name = HumanBody.Head, MaxHp = 100, VolumeWeight = 1,
            MacroRegion = BodyMacroRegion.Head } });
        var engine = new CombatEngine(new SystemRandomSource(seed: 17));

        AttackOutcome hit = engine.ResolveHit(weapon, System.Array.Empty<ArmorLayer>(), body,
            hitDamageMultiplier: (w, p) => BookPassiveEffects.DamageMultiplier(w.Name, p.MacroRegion, _ => true));

        Assert.Equal(11.5, hit.Damage, 10);
    }
}
