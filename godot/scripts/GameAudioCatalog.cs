using System;

namespace DeadSignal.Godot;

/// <summary>所有可听事件。名字描述声音语义，不携带玩法半径、伤害或命中规则。</summary>
public enum AudioCue
{
    FootstepHuman,
    FootstepDog,
    FootstepZombie,
    PistolShot,
    RifleShot,
    ShotgunShot,
    BowRelease,
    CrossbowRelease,
    MeleeLight,
    MeleeHeavy,
    ArmorImpact,
    FleshSharpImpact,
    FleshBluntImpact,
    FatalImpact,
    StructureImpact,
    DoorOpen,
    DoorClose,
    Lockpick,
    Work,
    Loot,
    Death,
    ZombieGroan,
}

public enum MusicMood
{
    Silence,
    Menu,
    CampDay,
    CampNight,
    Exploration,
    Combat,
    Horde,
    Ending,
}

public enum AmbienceMood
{
    Silence,
    CampDay,
    CampNight,
    Outdoor,
    Interior,
    Sewer,
}

public enum AudioWaveform
{
    Sine,
    Triangle,
    Square,
}

/// <summary>程序化一次性音效的稳定配方；调整此表只改变听感，不改变任何游戏规则。</summary>
public readonly record struct AudioProfile(
    float DurationSeconds,
    float StartHz,
    float EndHz,
    float NoiseAmount,
    float DecayPower,
    float VolumeDb,
    AudioWaveform Waveform);

public static class GameAudioCatalog
{
    public static AudioProfile Profile(AudioCue cue) => cue switch
    {
        AudioCue.FootstepHuman => new(0.12f, 105, 72, 0.55f, 2.8f, -17, AudioWaveform.Triangle),
        AudioCue.FootstepDog => new(0.08f, 155, 110, 0.32f, 3.2f, -22, AudioWaveform.Triangle),
        AudioCue.FootstepZombie => new(0.16f, 82, 50, 0.68f, 2.2f, -18, AudioWaveform.Square),
        AudioCue.PistolShot => new(0.22f, 178, 64, 0.76f, 3.8f, -5, AudioWaveform.Square),
        AudioCue.RifleShot => new(0.31f, 128, 42, 0.82f, 3.1f, -3, AudioWaveform.Square),
        AudioCue.ShotgunShot => new(0.38f, 96, 34, 0.94f, 2.8f, -2, AudioWaveform.Square),
        AudioCue.BowRelease => new(0.18f, 420, 135, 0.16f, 2.6f, -13, AudioWaveform.Triangle),
        AudioCue.CrossbowRelease => new(0.20f, 310, 92, 0.28f, 3.0f, -11, AudioWaveform.Square),
        AudioCue.MeleeLight => new(0.16f, 285, 105, 0.48f, 2.5f, -13, AudioWaveform.Triangle),
        AudioCue.MeleeHeavy => new(0.28f, 132, 48, 0.65f, 2.2f, -9, AudioWaveform.Square),
        AudioCue.ArmorImpact => new(0.30f, 1180, 510, 0.18f, 2.9f, -9, AudioWaveform.Sine),
        AudioCue.FleshSharpImpact => new(0.15f, 195, 72, 0.72f, 2.7f, -12, AudioWaveform.Triangle),
        AudioCue.FleshBluntImpact => new(0.22f, 104, 45, 0.58f, 2.3f, -11, AudioWaveform.Sine),
        AudioCue.FatalImpact => new(0.42f, 74, 31, 0.77f, 1.9f, -8, AudioWaveform.Square),
        AudioCue.StructureImpact => new(0.27f, 88, 38, 0.84f, 2.0f, -10, AudioWaveform.Triangle),
        AudioCue.DoorOpen => new(0.48f, 150, 63, 0.42f, 1.5f, -14, AudioWaveform.Triangle),
        AudioCue.DoorClose => new(0.24f, 92, 40, 0.68f, 2.6f, -11, AudioWaveform.Square),
        AudioCue.Lockpick => new(0.17f, 1320, 760, 0.20f, 2.2f, -20, AudioWaveform.Sine),
        AudioCue.Work => new(0.25f, 142, 61, 0.66f, 2.1f, -16, AudioWaveform.Triangle),
        AudioCue.Loot => new(0.24f, 660, 1040, 0.04f, 1.8f, -17, AudioWaveform.Sine),
        AudioCue.Death => new(0.52f, 92, 29, 0.58f, 1.7f, -12, AudioWaveform.Triangle),
        AudioCue.ZombieGroan => new(0.92f, 86, 47, 0.27f, 1.25f, -18, AudioWaveform.Square),
        _ => throw new ArgumentOutOfRangeException(nameof(cue), cue, null),
    };

    public static AudioCue MuzzleCue(ProjectileVfxKind projectile, WeaponAttackAnimation attack) => attack switch
    {
        WeaponAttackAnimation.BowShot => AudioCue.BowRelease,
        WeaponAttackAnimation.CrossbowRecoil => AudioCue.CrossbowRelease,
        WeaponAttackAnimation.LongGunRecoil when projectile == ProjectileVfxKind.Pellet => AudioCue.ShotgunShot,
        WeaponAttackAnimation.LongGunRecoil => AudioCue.RifleShot,
        _ => AudioCue.PistolShot,
    };

    public static AudioCue MeleeCue(WeaponAttackAnimation attack)
        => attack == WeaponAttackAnimation.HeavySwing ? AudioCue.MeleeHeavy : AudioCue.MeleeLight;

    public static AudioCue ImpactCue(ImpactVfxKind impact) => impact switch
    {
        ImpactVfxKind.Armor => AudioCue.ArmorImpact,
        ImpactVfxKind.FleshSharp => AudioCue.FleshSharpImpact,
        ImpactVfxKind.FleshBlunt => AudioCue.FleshBluntImpact,
        ImpactVfxKind.Fatal => AudioCue.FatalImpact,
        ImpactVfxKind.Wall => AudioCue.StructureImpact,
        _ => AudioCue.StructureImpact,
    };

    /// <summary>音乐优先级：结局＞尸潮＞交战＞探索＞营地昼夜。</summary>
    public static MusicMood MusicFor(bool ending, bool horde, bool combat, bool exploring, bool night)
    {
        if (ending) return MusicMood.Ending;
        if (horde) return MusicMood.Horde;
        if (combat) return MusicMood.Combat;
        if (exploring) return MusicMood.Exploration;
        return night ? MusicMood.CampNight : MusicMood.CampDay;
    }

    public static float MusicVolumeDb(MusicMood mood) => mood switch
    {
        MusicMood.Menu => -23,
        MusicMood.CampDay => -27,
        MusicMood.CampNight => -25,
        MusicMood.Exploration => -24,
        MusicMood.Combat => -19,
        MusicMood.Horde => -16,
        MusicMood.Ending => -22,
        _ => -60,
    };

    public static AmbienceMood AmbienceFor(bool exploring, bool sewer, bool indoorsDark, bool night)
    {
        if (sewer) return AmbienceMood.Sewer;
        if (exploring) return indoorsDark ? AmbienceMood.Interior : AmbienceMood.Outdoor;
        return night ? AmbienceMood.CampNight : AmbienceMood.CampDay;
    }

    public static float AmbienceVolumeDb(AmbienceMood mood) => mood switch
    {
        AmbienceMood.Sewer => -21,
        AmbienceMood.Interior => -28,
        _ when mood != AmbienceMood.Silence => -31,
        _ => -60,
    };
}
