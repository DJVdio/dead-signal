using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class LightBatteryTests
{
    [Fact]
    public void HandheldProfilesHaveFiniteFuelAndFixedLightsAreInfinite()
    {
        LightProfile flashlight = LightSource.Find(LightSource.FlashlightKey)!.Value;
        LightProfile torch = LightSource.Find(LightSource.TorchKey)!.Value;
        LightProfile campfire = LightSource.Find(LightSource.CampfireKey)!.Value;

        Assert.Equal(LightFuelKind.Battery, flashlight.FuelKind);
        Assert.True(flashlight.ActiveSeconds > 0);
        Assert.Equal(LightFuelKind.Durability, torch.FuelKind);
        Assert.True(torch.ActiveSeconds > 0);
        Assert.Equal(LightFuelKind.None, campfire.FuelKind);
        Assert.Equal(0, campfire.ActiveSeconds);
    }

    [Fact]
    public void ChargeStartsFullAndDepletesAtZero()
    {
        LightProfile torch = LightSource.Find(LightSource.TorchKey)!.Value;
        var charge = new LightChargeState(torch);

        Assert.True(charge.IsLit);
        Assert.Equal(torch.ActiveSeconds, charge.RemainingSeconds, 6);
        Assert.False(charge.Consume(torch.ActiveSeconds - 1));
        Assert.True(charge.IsLit);
        Assert.True(charge.Consume(1));
        Assert.True(charge.IsDepleted);
        Assert.False(charge.IsLit);
        Assert.Equal(0, charge.RemainingSeconds);
    }

    [Fact]
    public void ConsumeClampsAndNeverRecharges()
    {
        LightProfile flashlight = LightSource.Find(LightSource.FlashlightKey)!.Value;
        var charge = new LightChargeState(flashlight);

        Assert.True(charge.Consume(double.MaxValue));
        Assert.Equal(0, charge.RemainingSeconds);
        Assert.False(charge.Consume(-10));
        Assert.Equal(0, charge.RemainingSeconds);
    }

    [Fact]
    public void FixedLightNeverRunsOut()
    {
        LightProfile campfire = LightSource.Find(LightSource.CampfireKey)!.Value;
        var charge = new LightChargeState(campfire);

        Assert.True(charge.IsLit);
        Assert.False(charge.Consume(double.MaxValue));
        Assert.True(charge.IsLit);
        Assert.Equal(0, charge.RemainingSeconds);
    }

    [Fact]
    public void HeldLightExhaustionTurnsOffLightButKeepsHandOccupied()
    {
        LightProfile torch = LightSource.Find(LightSource.TorchKey)!.Value;
        var loadout = new WeaponLoadout();
        var held = new HeldLightState();

        Assert.True(held.TryHold(torch, Hand.Left, loadout));
        Assert.True(held.IsActive);
        Assert.True(held.IsLit);
        Assert.True(held.Consume(torch.ActiveSeconds));
        Assert.True(held.IsActive);
        Assert.False(held.IsLit);
        Assert.Null(held.ActiveHeld);
        Assert.Equal(Hand.Left, held.HandUsed);
        Assert.True(HeldLightState.BlocksTwoHandedEquip(held));
    }

    [Fact]
    public void SaveRestorePreservesRemainingChargeAndOldSaveDefaultsFull()
    {
        LightProfile flashlight = LightSource.Find(LightSource.FlashlightKey)!.Value;
        var loadout = new WeaponLoadout();
        var held = new HeldLightState();
        Assert.True(held.TryHold(flashlight, Hand.Right, loadout));
        Assert.False(held.Consume(100));

        HeldLightSave save = SaveMapper.ToSave(held)!;
        var restored = new HeldLightState();
        SaveMapper.RestoreHeldLight(restored, save, new WeaponLoadout());
        Assert.Equal(held.RemainingSeconds, restored.RemainingSeconds, 6);
        Assert.True(restored.IsLit);

        var legacy = new HeldLightSave { LightKey = flashlight.Key, Hand = Hand.Right };
        var restoredLegacy = new HeldLightState();
        SaveMapper.RestoreHeldLight(restoredLegacy, legacy, new WeaponLoadout());
        Assert.Equal(flashlight.ActiveSeconds, restoredLegacy.RemainingSeconds, 6);
    }
}
