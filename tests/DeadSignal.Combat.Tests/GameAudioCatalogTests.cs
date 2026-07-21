using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class GameAudioCatalogTests
{
    private static string Root()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(string relative)
        => File.ReadAllText(Path.Combine(Root(), relative));

    private static string LocalAsset(string resourcePath)
        => Path.Combine(Root(), resourcePath.Replace("res://", "godot/"));

    private static void AssertWav(string resourcePath, long minimumBytes)
    {
        string path = LocalAsset(resourcePath);
        byte[] header = File.ReadAllBytes(path)[..44];
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(header, 8, 4));
        Assert.Equal(2, BitConverter.ToInt16(header, 22));
        Assert.Equal(44_100, BitConverter.ToInt32(header, 24));
        Assert.Equal(16, BitConverter.ToInt16(header, 34));
        Assert.True(new FileInfo(path).Length >= minimumBytes, $"音频过短或为空：{path}");
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
    public void FormalAudioLibraryCoversEveryAdaptiveStateAndCue()
    {
        foreach (MusicMood mood in Enum.GetValues<MusicMood>().Where(x => x != MusicMood.Silence))
            AssertWav(GameAudioCatalog.MusicAsset(mood), 4_000_000);

        foreach (AmbienceMood mood in Enum.GetValues<AmbienceMood>().Where(x => x != AmbienceMood.Silence))
            AssertWav(GameAudioCatalog.AmbienceAsset(mood), 2_500_000);

        foreach (AudioCue cue in Enum.GetValues<AudioCue>())
            for (int variant = 0; variant < GameAudioCatalog.SfxVariantCount; variant++)
                AssertWav(GameAudioCatalog.SfxAsset(cue, variant), 10_000);
    }

    [Fact]
    public void FormalAssetCatalogRejectsSilenceAndInvalidVariants()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameAudioCatalog.MusicAsset(MusicMood.Silence));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameAudioCatalog.AmbienceAsset(AmbienceMood.Silence));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameAudioCatalog.SfxAsset(AudioCue.Loot, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameAudioCatalog.SfxAsset(AudioCue.Loot, GameAudioCatalog.SfxVariantCount));
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
        string runtime = Read("godot/scripts/GameAudioRuntime.cs");

        Assert.Contains("AudioRuntime=\"*res://scenes/GameAudio.tscn\"", project);
        Assert.Contains("GameAudioCatalog.MuzzleCue", vfx);
        Assert.Contains("GameAudioCatalog.ImpactCue", vfx);
        Assert.Contains("GameAudioRuntime.PlayNoise", noise);
        Assert.Contains("AudioCue.ZombieGroan", zombie);
        Assert.Contains("GameAudioCatalog.MusicFor", camp);
        Assert.Contains("GameAudioCatalog.AmbienceFor", camp);
        Assert.Contains("GameAudioCatalog.MusicAsset", runtime);
        Assert.Contains("GameAudioCatalog.AmbienceAsset", runtime);
        Assert.Contains("GameAudioCatalog.SfxAsset", runtime);
        Assert.Contains("LoadOneShot", runtime);
        Assert.Contains("LoadLoop", runtime);
    }
}
