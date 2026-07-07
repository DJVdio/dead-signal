using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 友军误伤「紧贴架肩豁免」纯决策逻辑（<see cref="FriendlyFire.Resolve"/>）。
/// 用户口径：允许友伤，但紧贴射手的同阵营队友视作架肩射击、弹道穿过不误伤。
/// </summary>
public class FriendlyFireTests
{
    private const double Grace = 30.0; // 架肩豁免阈值占位（与实时层同单位）

    [Fact]
    public void Enemy_AlwaysHit_RegardlessOfDistance()
    {
        Assert.Equal(ProjectileContact.Hit, FriendlyFire.Resolve(hostile: true, distanceToShooter: 0, Grace));
        Assert.Equal(ProjectileContact.Hit, FriendlyFire.Resolve(hostile: true, distanceToShooter: 5, Grace));
        Assert.Equal(ProjectileContact.Hit, FriendlyFire.Resolve(hostile: true, distanceToShooter: 999, Grace));
    }

    [Fact]
    public void FriendlyAdjacent_PassesThrough()
    {
        // 紧贴（阈值内）：架肩射击，穿过不误伤。
        Assert.Equal(ProjectileContact.PassThrough, FriendlyFire.Resolve(hostile: false, distanceToShooter: 0, Grace));
        Assert.Equal(ProjectileContact.PassThrough, FriendlyFire.Resolve(hostile: false, distanceToShooter: Grace * 0.5, Grace));
    }

    [Fact]
    public void FriendlyFar_TakesFriendlyFire()
    {
        // 紧贴阈值之外的队友：可被击中（真实向友伤）。
        Assert.Equal(ProjectileContact.Hit, FriendlyFire.Resolve(hostile: false, distanceToShooter: Grace * 2, Grace));
    }

    [Fact]
    public void FriendlyAtThreshold_IsExempt_InclusiveBoundary()
    {
        // 边界含阈值本身（≤ 视作紧贴）。
        Assert.Equal(ProjectileContact.PassThrough, FriendlyFire.Resolve(hostile: false, distanceToShooter: Grace, Grace));
    }
}
