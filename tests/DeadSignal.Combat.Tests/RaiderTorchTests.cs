using System.Numerics;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class RaiderTorchTests
{
    [Fact]
    public void Torch_IsLitOnlyWhileInCombat_NotWhenRetreating()
    {
        Assert.False(RaiderTorchLogic.ShouldCarryTorch(hasLiveEnemy: false, retreating: false));
        Assert.True(RaiderTorchLogic.ShouldCarryTorch(hasLiveEnemy: true, retreating: false));
        Assert.False(RaiderTorchLogic.ShouldCarryTorch(hasLiveEnemy: true, retreating: true));
    }

    [Fact]
    public void PlaceTorch_UsesCatalogProfileAndPosition()
    {
        PlacedLight? light = RaiderTorchLogic.PlaceTorch(
            new Vector2(12, 34), hasLiveEnemy: true, retreating: false);

        Assert.True(light.HasValue);
        Assert.Equal(12f, light.Value.X);
        Assert.Equal(34f, light.Value.Y);
        Assert.Equal(LightSource.TorchKey, light.Value.Profile.Key);
    }

    [Fact]
    public void PlaceTorch_ReturnsNullOutsideCombat()
        => Assert.Null(RaiderTorchLogic.PlaceTorch(
            Vector2.Zero, hasLiveEnemy: false, retreating: false));
}
