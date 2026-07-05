using System;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 昼夜循环 + 时间档位 + 战术暂停的中枢。
///
/// 时间档位与暂停统一用 <see cref="Engine.TimeScale"/> 实现：
/// 档位 1x/3x/8x 直接设 TimeScale；暂停设 0（_Process/_PhysicsProcess 仍被调用但 delta=0，
/// 于是移动、AI、昼夜全部冻结，而 _Input/_UnhandledInput 不受 TimeScale 影响 —— 暂停中照常下指令）。
///
/// 因此本类所有推进都吃 <c>_Process(delta)</c> 的已缩放 delta，无需自管 tick 倍率。
/// </summary>
public sealed partial class GameClock : Node
{
    public struct Config
    {
        public double DayLengthSeconds;
        public double NightLengthSeconds;
        public bool StartAtNight;
        public Color DayColor;
        public Color NightColor;
        /// <summary>昼夜切换的过渡带占各自时段的比例（用于颜色渐变）。</summary>
        public double TwilightFraction;
    }

    private Config _cfg;

    // 时间档位（暂停时保留，恢复即回到该档）。
    private static readonly double[] Speeds = { 1.0, 3.0, 8.0 };
    public int SpeedIndex { get; private set; } = 0;
    public bool Paused { get; private set; } = false;

    public int Day { get; private set; } = 1;
    public bool IsNight { get; private set; }

    /// <summary>当前时段内已流逝秒数。</summary>
    private double _phaseElapsed;

    /// <summary>昼→夜切换时触发（bool = 切换后是否为夜晚）。</summary>
    public event Action<bool>? PhaseChanged;

    public double CurrentSpeed => Speeds[SpeedIndex];

    public void Configure(Config cfg)
    {
        _cfg = cfg;
        IsNight = cfg.StartAtNight;
        _phaseElapsed = 0;
        ApplyTimeScale();
    }

    public override void _Ready() => ApplyTimeScale();

    public override void _Process(double delta)
    {
        // delta 已被 Engine.TimeScale 缩放：暂停时为 0，8x 时为 8 倍。
        double phaseLen = IsNight ? _cfg.NightLengthSeconds : _cfg.DayLengthSeconds;
        _phaseElapsed += delta;
        while (_phaseElapsed >= phaseLen && phaseLen > 0)
        {
            _phaseElapsed -= phaseLen;
            AdvancePhase();
            phaseLen = IsNight ? _cfg.NightLengthSeconds : _cfg.DayLengthSeconds;
        }
    }

    private void AdvancePhase()
    {
        if (IsNight)
        {
            // 夜尽入昼，进入新的一天。
            IsNight = false;
            Day += 1;
        }
        else
        {
            IsNight = true;
        }
        PhaseChanged?.Invoke(IsNight);
    }

    /// <summary>Debug：直接跳到当前时段结束，触发一次昼夜切换。</summary>
    public void DebugSkipToPhaseEnd()
    {
        _phaseElapsed = 0;
        AdvancePhase();
    }

    // ---- 时间档位 / 暂停 ----

    public void TogglePause()
    {
        Paused = !Paused;
        ApplyTimeScale();
    }

    public void SetSpeedIndex(int index)
    {
        SpeedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
        // 选速度档隐含恢复运行。
        Paused = false;
        ApplyTimeScale();
    }

    private void ApplyTimeScale() => Engine.TimeScale = Paused ? 0.0 : Speeds[SpeedIndex];

    // ---- 表现辅助 ----

    /// <summary>当前应显示的环境色（昼夜之间在过渡带内线性渐变）。</summary>
    public Color CurrentAmbientColor()
    {
        double phaseLen = IsNight ? _cfg.NightLengthSeconds : _cfg.DayLengthSeconds;
        if (phaseLen <= 0)
        {
            return IsNight ? _cfg.NightColor : _cfg.DayColor;
        }

        double frac = _phaseElapsed / phaseLen;
        double tw = Mathf.Clamp((float)_cfg.TwilightFraction, 0.01f, 0.49f);

        Color baseColor = IsNight ? _cfg.NightColor : _cfg.DayColor;
        Color otherStart = IsNight ? _cfg.DayColor : _cfg.NightColor; // 上一时段末尾色
        Color otherEnd = IsNight ? _cfg.DayColor : _cfg.NightColor;   // 下一时段开头色

        if (frac < tw)
        {
            // 刚切入本时段：从上一时段色渐入本时段色。
            float t = (float)(frac / tw);
            return otherStart.Lerp(baseColor, t);
        }
        if (frac > 1 - tw)
        {
            // 时段末尾：从本时段色渐出到下一时段色。
            float t = (float)((frac - (1 - tw)) / tw);
            return baseColor.Lerp(otherEnd, t);
        }
        return baseColor;
    }

    /// <summary>形如 "06:30" 的时刻串。白天从 06:00 起，夜晚从 18:00 起，各自占满本时段。</summary>
    public string ClockString()
    {
        double phaseLen = IsNight ? _cfg.NightLengthSeconds : _cfg.DayLengthSeconds;
        double frac = phaseLen > 0 ? _phaseElapsed / phaseLen : 0;
        // 白天覆盖 06:00–18:00（12h），夜晚覆盖 18:00–06:00（12h）。
        double startHour = IsNight ? 18.0 : 6.0;
        double hourOfDay = (startHour + frac * 12.0) % 24.0;
        int h = (int)hourOfDay;
        int m = (int)((hourOfDay - h) * 60);
        return $"{h:00}:{m:00}";
    }

    public string SpeedLabel() => Paused ? "暂停" : $"{(int)Speeds[SpeedIndex]}x";
}
