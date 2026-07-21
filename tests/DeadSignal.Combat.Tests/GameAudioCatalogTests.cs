using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class GameAudioCatalogTests
{
    private static string Read(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    [Fact]
    public void EveryCueHasASafeAudibleProfile()
    {
        foreach (AudioCue cue in Enum.GetValues<AudioCue>())
        {
            AudioProfile p = GameAudioCatalog.Profile(cue);
            Assert.InRange(p.DurationSeconds, 0.05f, 2f);
            Assert.InRange(p.StartHz, 20f, 2000f);
            Assert.InRange(p.EndHz, 20f, 2000f);
            Assert.InRange(p.NoiseAmount, 0f, 1f);
            Assert.InRange(p.VolumeDb, -30f, 0f);
        }
    }

    [Fact]
    public void WeaponFamiliesKeepDistinctSignatures()
    {
        Assert.Equal(AudioCue.BowRelease,
            GameAudioCatalog.MuzzleCue(ProjectileVfxKind.Arrow, WeaponAttackAnimation.BowShot));
        Assert.Equal(AudioCue.CrossbowRelease,
            GameAudioCatalog.MuzzleCue(ProjectileVfxKind.Bolt, WeaponAttackAnimation.CrossbowRecoil));
        Assert.Equal(AudioCue.ShotgunShot,
            GameAudioCatalog.MuzzleCue(ProjectileVfxKind.Pellet, WeaponAttackAnimation.LongGunRecoil));
        Assert.Equal(AudioCue.RifleShot,
            GameAudioCatalog.MuzzleCue(ProjectileVfxKind.Bullet, WeaponAttackAnimation.LongGunRecoil));
        Assert.Equal(AudioCue.PistolShot,
            GameAudioCatalog.MuzzleCue(ProjectileVfxKind.Bullet, WeaponAttackAnimation.PistolRecoil));
    }

    [Fact]
    public void ImpactMaterialsKeepDistinctSignatures()
    {
        Assert.Equal(AudioCue.ArmorImpact, GameAudioCatalog.ImpactCue(ImpactVfxKind.Armor));
        Assert.Equal(AudioCue.FleshSharpImpact, GameAudioCatalog.ImpactCue(ImpactVfxKind.FleshSharp));
        Assert.Equal(AudioCue.FleshBluntImpact, GameAudioCatalog.ImpactCue(ImpactVfxKind.FleshBlunt));
        Assert.Equal(AudioCue.FatalImpact, GameAudioCatalog.ImpactCue(ImpactVfxKind.Fatal));
        Assert.Equal(AudioCue.StructureImpact, GameAudioCatalog.ImpactCue(ImpactVfxKind.Wall));
    }

    [Theory]
    [InlineData(true, true, true, true, true, MusicMood.Ending)]
    [InlineData(false, true, true, true, true, MusicMood.Horde)]
    [InlineData(false, false, true, true, false, MusicMood.Combat)]
    [InlineData(false, false, false, true, true, MusicMood.Exploration)]
    [InlineData(false, false, false, false, true, MusicMood.CampNight)]
    [InlineData(false, false, false, false, false, MusicMood.CampDay)]
    public void AdaptiveMusicUsesThreatPriority(bool ending, bool horde, bool combat, bool exploring,
        bool night, MusicMood expected)
        => Assert.Equal(expected, GameAudioCatalog.MusicFor(ending, horde, combat, exploring, night));

    [Fact]
    public void SewerAmbienceOverridesGenericInteriorAndNight()
    {
        Assert.Equal(AmbienceMood.Sewer, GameAudioCatalog.AmbienceFor(true, true, true, true));
        Assert.Equal(AmbienceMood.Interior, GameAudioCatalog.AmbienceFor(true, false, true, false));
        Assert.Equal(AmbienceMood.Outdoor, GameAudioCatalog.AmbienceFor(true, false, false, true));
        Assert.Equal(AmbienceMood.CampNight, GameAudioCatalog.AmbienceFor(false, false, false, true));
    }

    [Fact]
    public void RuntimeWiresAutoloadEventsAndAdaptiveState()
    {
        string project = Read("godot/project.godot");
        string vfx = Read("godot/scripts/CombatVfx.cs");
        string noise = Read("godot/scripts/NoiseCueOverlay.cs");
        string zombie = Read("godot/scripts/Zombie.cs");
        string camp = Read("godot/scripts/CampMain.cs");

        Assert.Contains("AudioRuntime=\"*res://scenes/GameAudio.tscn\"", project);
        Assert.Contains("GameAudioCatalog.MuzzleCue", vfx);
        Assert.Contains("GameAudioCatalog.ImpactCue", vfx);
        Assert.Contains("GameAudioRuntime.PlayNoise", noise);
        Assert.Contains("AudioCue.ZombieGroan", zombie);
        Assert.Contains("GameAudioCatalog.MusicFor", camp);
        Assert.Contains("GameAudioCatalog.AmbienceFor", camp);
    }
}
