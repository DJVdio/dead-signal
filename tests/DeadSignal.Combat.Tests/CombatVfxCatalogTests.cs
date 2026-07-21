using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class CombatVfxCatalogTests
{
    private static string Script(string name)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "godot", "scripts", name));
    }

    [Fact]
    public void EveryRangedArsenalWeaponHasAVisibleProjectileFamily()
    {
        foreach (Weapon weapon in WeaponTable.Arsenal().Where(w => w.IsRanged))
        {
            string ammo = weapon.Name.Contains('弓') || weapon.Name.Contains('弩')
                ? ArrowTable.All[0].Key
                : weapon.AmmoKey;
            ProjectileVfxKind kind = CombatVfxCatalog.ProjectileFor(weapon, ammo);
            Assert.True(Enum.IsDefined(kind), weapon.Name);
        }
    }

    [Fact]
    public void ShotgunArrowsAndCrossbowBoltsDoNotReuseTheBulletTracer()
    {
        Assert.Equal(ProjectileVfxKind.Pellet,
            CombatVfxCatalog.ProjectileFor(WeaponTable.ImprovisedShotgun(), WeaponTable.ImprovisedShotgun().AmmoKey));
        Assert.Equal(ProjectileVfxKind.Arrow,
            CombatVfxCatalog.ProjectileFor(WeaponTable.Longbow(), ArrowTable.All[0].Key));
        Assert.Equal(ProjectileVfxKind.Bolt,
            CombatVfxCatalog.ProjectileFor(WeaponTable.HeavyCrossbow(), ArrowTable.All[0].Key));
    }

    [Theory]
    [InlineData(true, 0, DamageType.Blunt, false, false, ImpactVfxKind.Armor)]
    [InlineData(false, 4, DamageType.Sharp, false, false, ImpactVfxKind.FleshSharp)]
    [InlineData(false, 4, DamageType.Blunt, false, false, ImpactVfxKind.FleshBlunt)]
    [InlineData(false, 20, DamageType.Sharp, true, false, ImpactVfxKind.Fatal)]
    [InlineData(false, 20, DamageType.Blunt, false, true, ImpactVfxKind.Fatal)]
    public void HitResultSelectsItsMaterialFeedback(bool blocked, double damage, DamageType type,
        bool severed, bool died, ImpactVfxKind expected)
    {
        var hit = new AttackOutcome(damage, "胸部", type, blocked, severed, false, false, false, died);
        Assert.Equal(expected, CombatVfxCatalog.ImpactFor(hit));
    }

    [Fact]
    public void CartesianProjectileHasASeparateVisibleIsoCompanion()
    {
        string projectile = Script("Projectile.cs");
        Assert.Contains("ProjectileVfx.Spawn", projectile);
        Assert.Contains("_visual.UpdateCartesian(GlobalPosition, _dir);", projectile);
        Assert.Contains("CombatVfxBurst.SpawnImpact", projectile);
        Assert.Contains("本节点留在不可见的 cartesian LogicLayer", projectile);
    }

    [Fact]
    public void RuntimeWiresMuzzleMeleeDeathLootDoorAndWorkCompletion()
    {
        string actor = Script("Actor.cs");
        string camp = Script("CampMain.cs");
        string level = Script("TestExploration.cs");

        Assert.Contains("CombatVfxBurst.SpawnMuzzle", actor);
        Assert.Contains("CombatVfxBurst.SpawnMelee", actor);
        Assert.Contains("CombatVfxBurst.SpawnDeath", actor);
        Assert.Contains("PlayScriptedMeleeVisual", camp);
        Assert.Contains("CombatVfxBurst.SpawnLoot", camp);
        Assert.Contains("CombatVfxBurst.SpawnWorkDust", camp);
        Assert.Contains("CombatVfxBurst.SpawnDoor", camp);
        Assert.Contains("CombatVfxBurst.SpawnDoor", level);
    }
}
