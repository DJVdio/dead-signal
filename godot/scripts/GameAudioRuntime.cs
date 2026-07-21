using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 全局音频运行时：用确定性的程序化波形生成原创占位音效与氛围音乐，提供空间音效、总线和音乐交叉淡化。
/// 它只消费表现事件；不会读写生命、弹药、库存、噪音半径或随机结算。
/// </summary>
public sealed partial class GameAudioRuntime : Node
{
    private const int SampleRate = 22050;
    private const float SilentDb = -60f;
    private const float CrossfadeDbPerSecond = 28f;
    private static GameAudioRuntime? _instance;
    private static readonly Dictionary<AudioCue, AudioStreamWav> SfxCache = new();
    private static readonly Dictionary<MusicMood, AudioStreamWav> MusicCache = new();
    private static readonly Dictionary<AmbienceMood, AudioStreamWav> AmbienceCache = new();

    private AudioStreamPlayer _musicA = null!;
    private AudioStreamPlayer _musicB = null!;
    private AudioStreamPlayer _activeMusic = null!;
    private AudioStreamPlayer _fadingMusic = null!;
    private MusicMood _mood = MusicMood.Silence;
    private AmbienceMood _ambienceMood = AmbienceMood.Silence;
    private float _targetMusicDb = SilentDb;
    private AudioStreamPlayer _ambience = null!;
    private uint _playSequence;
    private bool _audioEnabled;

    public override void _Ready()
    {
        _instance = this;
        _audioEnabled = DisplayServer.GetName() != "headless";
        ProcessMode = ProcessModeEnum.Always;
        EnsureBus("Music", -1.5f);
        EnsureBus("SFX", 0f);
        EnsureBus("Ambience", -2f);

        _musicA = NewMusicPlayer("AdaptiveMusicA");
        _musicB = NewMusicPlayer("AdaptiveMusicB");
        _activeMusic = _musicA;
        _fadingMusic = _musicB;
        _ambience = new AudioStreamPlayer
        {
            Name = "EnvironmentAmbience",
            Bus = "Ambience",
            VolumeDb = SilentDb,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(_ambience);
    }

    public override void _ExitTree()
    {
        if (_instance != this) return;
        _instance = null;
        ReleasePlayer(_musicA);
        ReleasePlayer(_musicB);
        ReleasePlayer(_ambience);
        foreach (AudioStreamWav stream in SfxCache.Values) stream.Dispose();
        foreach (AudioStreamWav stream in MusicCache.Values) stream.Dispose();
        foreach (AudioStreamWav stream in AmbienceCache.Values) stream.Dispose();
        SfxCache.Clear();
        MusicCache.Clear();
        AmbienceCache.Clear();
    }

    private static void ReleasePlayer(AudioStreamPlayer? player)
    {
        if (player is null || !IsInstanceValid(player)) return;
        player.Stop();
        player.Stream = null;
    }

    public override void _Process(double delta)
    {
        float step = CrossfadeDbPerSecond * (float)Math.Max(0, delta);
        _activeMusic.VolumeDb = Mathf.MoveToward(_activeMusic.VolumeDb, _targetMusicDb, step);
        _fadingMusic.VolumeDb = Mathf.MoveToward(_fadingMusic.VolumeDb, SilentDb, step);
        if (_fadingMusic.Playing && _fadingMusic.VolumeDb <= SilentDb + 0.5f)
            _fadingMusic.Stop();
    }

    public static void SetMusic(MusicMood mood)
    {
        if (_instance is null || !IsInstanceValid(_instance) || !_instance._audioEnabled || _instance._mood == mood) return;
        _instance.SwitchMusic(mood);
    }

    public static void SetAmbience(AmbienceMood mood)
    {
        if (_instance is null || !IsInstanceValid(_instance) || !_instance._audioEnabled || _instance._ambienceMood == mood) return;
        _instance.SwitchAmbience(mood);
    }

    public static void PlayWorld(AudioCue cue, Vector2 cartPosition, float gainDb = 0f)
    {
        if (_instance is null || !IsInstanceValid(_instance) || !_instance._audioEnabled) return;
        _instance.PlaySpatial(cue, Iso.Project(cartPosition), gainDb);
    }

    /// <summary>供已经位于 faux-iso 坐标系的表现节点使用，避免重复投影。</summary>
    public static void PlayIso(AudioCue cue, Vector2 isoPosition, float gainDb = 0f)
    {
        if (_instance is null || !IsInstanceValid(_instance) || !_instance._audioEnabled) return;
        _instance.PlaySpatial(cue, isoPosition, gainDb);
    }

    public static void PlayUi(AudioCue cue, float gainDb = 0f)
    {
        if (_instance is null || !IsInstanceValid(_instance) || !_instance._audioEnabled) return;
        _instance.PlayGlobal(cue, gainDb);
    }

    /// <summary>把已经真实发出的玩法噪音映射到听感；武器和门由其 VFX 入口发声，避免重复播放。</summary>
    public static void PlayNoise(Node source, Vector2 cartPosition, RatNoiseSource noiseSource)
    {
        AudioCue? cue = noiseSource switch
        {
            RatNoiseSource.Footstep when source is Dog => AudioCue.FootstepDog,
            RatNoiseSource.Footstep when source is Zombie => AudioCue.FootstepZombie,
            RatNoiseSource.Footstep => AudioCue.FootstepHuman,
            RatNoiseSource.Lockpick => AudioCue.Lockpick,
            RatNoiseSource.SilentDismantle => AudioCue.Work,
            RatNoiseSource.Breach => AudioCue.StructureImpact,
            _ => null,
        };
        if (cue.HasValue) PlayWorld(cue.Value, cartPosition);
    }

    private AudioStreamPlayer NewMusicPlayer(string name)
    {
        var player = new AudioStreamPlayer
        {
            Name = name,
            Bus = "Music",
            VolumeDb = SilentDb,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(player);
        return player;
    }

    private void SwitchMusic(MusicMood mood)
    {
        _mood = mood;
        AudioStreamPlayer next = _activeMusic == _musicA ? _musicB : _musicA;
        _fadingMusic = _activeMusic;
        _activeMusic = next;
        _targetMusicDb = GameAudioCatalog.MusicVolumeDb(mood);

        _activeMusic.Stop();
        _activeMusic.VolumeDb = SilentDb;
        if (mood != MusicMood.Silence)
        {
            _activeMusic.Stream = MusicCache.TryGetValue(mood, out AudioStreamWav? cached)
                ? cached
                : MusicCache[mood] = BuildMusic(mood);
            _activeMusic.Play();
        }
    }

    private void SwitchAmbience(AmbienceMood mood)
    {
        _ambienceMood = mood;
        _ambience.Stop();
        if (mood == AmbienceMood.Silence)
        {
            _ambience.VolumeDb = SilentDb;
            return;
        }
        _ambience.Stream = AmbienceCache.TryGetValue(mood, out AudioStreamWav? cached)
            ? cached
            : AmbienceCache[mood] = BuildAmbience(mood);
        _ambience.VolumeDb = GameAudioCatalog.AmbienceVolumeDb(mood);
        _ambience.Play();
    }

    private void PlaySpatial(AudioCue cue, Vector2 isoPosition, float gainDb)
    {
        AudioProfile profile = GameAudioCatalog.Profile(cue);
        var player = new AudioStreamPlayer2D
        {
            Stream = Sfx(cue),
            Position = isoPosition,
            Bus = "SFX",
            VolumeDb = profile.VolumeDb + gainDb,
            MaxDistance = 2200f,
            Attenuation = 1.15f,
            PitchScale = VariationPitch(),
        };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    private void PlayGlobal(AudioCue cue, float gainDb)
    {
        AudioProfile profile = GameAudioCatalog.Profile(cue);
        var player = new AudioStreamPlayer
        {
            Stream = Sfx(cue),
            Bus = "SFX",
            VolumeDb = profile.VolumeDb + gainDb,
            PitchScale = VariationPitch(),
        };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    private float VariationPitch()
    {
        _playSequence++;
        return 0.96f + (_playSequence % 7) * 0.013f;
    }

    private static AudioStreamWav Sfx(AudioCue cue)
        => SfxCache.TryGetValue(cue, out AudioStreamWav? stream)
            ? stream
            : SfxCache[cue] = BuildSfx(cue, GameAudioCatalog.Profile(cue));

    private static AudioStreamWav BuildSfx(AudioCue cue, AudioProfile profile)
    {
        int count = Math.Max(64, (int)(SampleRate * profile.DurationSeconds));
        var pcm = new short[count];
        uint noiseState = 0x9e3779b9u ^ ((uint)cue + 1u) * 2654435761u;
        double phase = 0;
        double secondaryPhase = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            float hz = Mathf.Lerp(profile.StartHz, profile.EndHz, t);
            phase += Math.Tau * hz / SampleRate;
            secondaryPhase += Math.Tau * hz * 1.503 / SampleRate;
            float tone = Wave(profile.Waveform, phase) * 0.72f
                         + Wave(AudioWaveform.Sine, secondaryPhase) * 0.28f;
            noiseState = noiseState * 1664525u + 1013904223u;
            float noise = ((noiseState >> 8) / 16777215f) * 2f - 1f;
            float attack = Mathf.Min(1f, t * 45f);
            float envelope = attack * Mathf.Pow(1f - t, profile.DecayPower);
            float transient = i < 24 ? (1f - i / 24f) * 0.28f : 0f;
            float sample = (tone * (1f - profile.NoiseAmount) + noise * profile.NoiseAmount + transient)
                           * envelope * 0.78f;
            pcm[i] = (short)Math.Clamp((int)(sample * short.MaxValue), short.MinValue, short.MaxValue);
        }
        return Wav(pcm, loop: false);
    }

    private static AudioStreamWav BuildMusic(MusicMood mood)
    {
        const int seconds = 12;
        int count = SampleRate * seconds;
        var pcm = new short[count];
        (float root, float[] chord, float pulse, float grit) = mood switch
        {
            MusicMood.Menu => (55f, new[] { 1f, 1.2f, 1.498f }, 0.05f, 0.10f),
            MusicMood.CampDay => (65.4f, new[] { 1f, 1.25f, 1.5f }, 0.025f, 0.045f),
            MusicMood.CampNight => (49f, new[] { 1f, 1.189f, 1.498f }, 0.04f, 0.12f),
            MusicMood.Exploration => (46.25f, new[] { 1f, 1.189f, 1.414f }, 0.08f, 0.14f),
            MusicMood.Combat => (43.65f, new[] { 1f, 1.122f, 1.498f }, 2.25f, 0.18f),
            MusicMood.Horde => (36.7f, new[] { 1f, 1.059f, 1.414f }, 3.1f, 0.26f),
            MusicMood.Ending => (41.2f, new[] { 1f, 1.189f, 1.498f }, 0.12f, 0.08f),
            _ => (55f, new[] { 1f }, 0f, 0f),
        };

        uint noiseState = 0x51ed270bu ^ (uint)mood * 0x9e3779b9u;
        for (int i = 0; i < count; i++)
        {
            double time = i / (double)SampleRate;
            double loopPhase = Math.Tau * time / seconds;
            double padEnvelope = 0.72 + 0.28 * Math.Sin(loopPhase - Math.PI / 2);
            double sample = 0;
            for (int c = 0; c < chord.Length; c++)
            {
                double hz = root * chord[c];
                double drift = 1.0 + 0.0025 * Math.Sin(loopPhase * (c + 1));
                sample += Math.Sin(Math.Tau * hz * drift * time + c * 1.7) / chord.Length;
            }
            double beat = pulse <= 0 ? 0 : Math.Pow(Math.Max(0, Math.Sin(Math.Tau * pulse * time)), 12);
            double lowPulse = Math.Sin(Math.Tau * root * 0.5 * time) * beat;
            noiseState = noiseState * 1664525u + 1013904223u;
            double noise = (((noiseState >> 8) / 16777215.0) * 2.0 - 1.0) * grit;
            double slowNoise = noise * (0.35 + 0.65 * Math.Sin(loopPhase * 2 + 0.4));
            double value = sample * padEnvelope * 0.20 + lowPulse * 0.22 + slowNoise * 0.045;
            pcm[i] = (short)Math.Clamp((int)(value * short.MaxValue), short.MinValue, short.MaxValue);
        }
        return Wav(pcm, loop: true);
    }

    private static AudioStreamWav BuildAmbience(AmbienceMood mood)
    {
        const int seconds = 8;
        int count = SampleRate * seconds;
        var pcm = new short[count];
        uint state = 0x7f4a7c15u ^ (uint)mood * 0x9e3779b9u;
        double filtered = 0;
        double[] drips = { 0.55, 1.72, 3.04, 3.48, 5.26, 6.91 };
        for (int i = 0; i < count; i++)
        {
            double time = i / (double)SampleRate;
            state = state * 1664525u + 1013904223u;
            double white = ((state >> 8) / 16777215.0) * 2.0 - 1.0;
            filtered = filtered * 0.993 + white * 0.007;
            double value = filtered * (mood is AmbienceMood.Interior or AmbienceMood.Sewer ? 0.24 : 0.42);

            if (mood == AmbienceMood.CampNight)
                value += Math.Sin(Math.Tau * 3150 * time) * Math.Pow(Math.Max(0, Math.Sin(Math.Tau * 0.72 * time)), 28) * 0.035;
            else if (mood == AmbienceMood.Interior)
                value += Math.Sin(Math.Tau * 58 * time) * 0.025;
            else if (mood == AmbienceMood.Sewer)
            {
                value += Math.Sin(Math.Tau * 43 * time) * 0.018;
                foreach (double at in drips)
                {
                    double age = time - at;
                    if (age is >= 0 and < 0.22)
                    {
                        double env = Math.Exp(-age * 22);
                        value += Math.Sin(Math.Tau * (920 - age * 2100) * age) * env * 0.24;
                        // 延迟反射形成短回声，和脚步的空间衰减一起兑现逼仄管道听感。
                    }
                    double echoAge = time - at - 0.11;
                    if (echoAge is >= 0 and < 0.18)
                        value += Math.Sin(Math.Tau * 710 * echoAge) * Math.Exp(-echoAge * 26) * 0.07;
                }
            }
            pcm[i] = (short)Math.Clamp((int)(value * short.MaxValue), short.MinValue, short.MaxValue);
        }
        return Wav(pcm, loop: true);
    }

    private static float Wave(AudioWaveform waveform, double phase) => waveform switch
    {
        AudioWaveform.Sine => (float)Math.Sin(phase),
        AudioWaveform.Square => Math.Sin(phase) >= 0 ? 1f : -1f,
        AudioWaveform.Triangle => (float)(2.0 / Math.PI * Math.Asin(Math.Sin(phase))),
        _ => 0f,
    };

    private static AudioStreamWav Wav(short[] pcm, bool loop)
    {
        var data = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, data, 0, data.Length);
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            Data = data,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = pcm.Length,
        };
    }

    private static void EnsureBus(string name, float volumeDb)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusVolumeDb(index, volumeDb);
    }
}
