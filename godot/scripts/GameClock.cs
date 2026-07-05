using System;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class GameClock : Node
{
    public struct Config
    {
        public double DayLengthSeconds;
        public double NightLengthSeconds;
        public bool StartAtNight;
        public Color DayColor;
        public Color NightColor;
        public double TwilightFraction;
        public double TravelTimeSeconds;
        public double WarningBufferSeconds;
    }

    private Config _cfg;

    private static readonly double[] Speeds = { 1.0, 3.0, 8.0 };
    public int SpeedIndex { get; private set; } = 0;

    public bool Paused => Engine.TimeScale == 0;

    private bool _userPaused;

    public int Day { get; private set; } = 1;
    public bool IsNight => CurrentPhase is DayPhase.NightPrep or DayPhase.NightAct;

    public DayPhase CurrentPhase { get; private set; } = DayPhase.DayPrep;

    private double _phaseElapsed;
    private double _travelElapsed;
    private bool _warningFired;

    public event Action<DayPhase>? OnPhaseChanged;
    public event Action? OnExploreWarning;

    public double CurrentSpeed => Speeds[SpeedIndex];

    public double GetExploreTimeRemaining()
    {
        if (CurrentPhase != DayPhase.DayExplore)
            return 0;
        return Math.Max(0, _cfg.DayLengthSeconds - _phaseElapsed);
    }

    public void Configure(Config cfg)
    {
        _cfg = cfg;
        _cfg.TravelTimeSeconds = cfg.TravelTimeSeconds > 0 ? cfg.TravelTimeSeconds : 120;
        _cfg.WarningBufferSeconds = cfg.WarningBufferSeconds > 0 ? cfg.WarningBufferSeconds : 300;
        _phaseElapsed = 0;
        _travelElapsed = 0;
        _warningFired = false;
        _userPaused = false;
        CurrentPhase = DayPhase.DayExplore;
        Day = 1;
        Engine.TimeScale = 1;
    }

    public void TransitionTo(DayPhase phase)
    {
        CurrentPhase = phase;

        switch (phase)
        {
            case DayPhase.DayTravel:
                _travelElapsed = 0;
                break;
            case DayPhase.DayExplore:
                _warningFired = false;
                break;
            case DayPhase.NightPrep:
                _phaseElapsed = 0;
                break;
            case DayPhase.DayPrep:
                _phaseElapsed = 0;
                Day += 1;
                break;
        }

        OnPhaseChanged?.Invoke(phase);
        ApplyPhaseTimeScale();
    }

    public override void _Ready() => ApplyPhaseTimeScale();

    public override void _Process(double delta)
    {
        if (delta <= 0)
            return;

        switch (CurrentPhase)
        {
            case DayPhase.DayTravel:
                _travelElapsed += delta;
                _phaseElapsed += delta;
                if (_travelElapsed >= _cfg.TravelTimeSeconds)
                    TransitionTo(DayPhase.DayExplore);
                break;

            case DayPhase.DayExplore:
            {
                _phaseElapsed += delta;
                double remaining = _cfg.DayLengthSeconds - _phaseElapsed;
                if (!_warningFired && remaining <= _cfg.WarningBufferSeconds && _cfg.WarningBufferSeconds > 0)
                {
                    _warningFired = true;
                    OnExploreWarning?.Invoke();
                }
                if (remaining <= 0)
                    TransitionTo(DayPhase.DayReturn);
                break;
            }

            case DayPhase.NightAct:
                _phaseElapsed += delta;
                if (_phaseElapsed >= _cfg.NightLengthSeconds)
                    TransitionTo(DayPhase.DayPrep);
                break;
        }
    }

    public void DebugSkipToPhaseEnd()
    {
        switch (CurrentPhase)
        {
            case DayPhase.DayPrep:
                TransitionTo(DayPhase.DayExplore);
                break;
            case DayPhase.DayTravel:
                TransitionTo(DayPhase.DayExplore);
                break;
            case DayPhase.DayExplore:
                TransitionTo(DayPhase.DayReturn);
                break;
            case DayPhase.DayReturn:
                TransitionTo(DayPhase.NightPrep);
                break;
            case DayPhase.NightPrep:
                TransitionTo(DayPhase.NightAct);
                break;
            case DayPhase.NightAct:
                TransitionTo(DayPhase.DayPrep);
                break;
        }
    }

    public void TogglePause()
    {
        if (CurrentPhase is DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep)
            return;
        _userPaused = !_userPaused;
        ApplyPhaseTimeScale();
    }

    public void SetSpeedIndex(int index)
    {
        SpeedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
        if (CurrentPhase is DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep)
            return;
        _userPaused = false;
        ApplyPhaseTimeScale();
    }

    private void ApplyPhaseTimeScale()
    {
        if (CurrentPhase is DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep)
        {
            Engine.TimeScale = 0;
            return;
        }

        if (_userPaused)
        {
            Engine.TimeScale = 0;
            return;
        }

        Engine.TimeScale = CurrentPhase == DayPhase.DayTravel ? 8.0 : Speeds[SpeedIndex];
    }

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
        Color otherStart = IsNight ? _cfg.DayColor : _cfg.NightColor;
        Color otherEnd = IsNight ? _cfg.DayColor : _cfg.NightColor;

        if (frac < tw)
        {
            float t = (float)(frac / tw);
            return otherStart.Lerp(baseColor, t);
        }
        if (frac > 1 - tw)
        {
            float t = (float)((frac - (1 - tw)) / tw);
            return baseColor.Lerp(otherEnd, t);
        }
        return baseColor;
    }

    public string ClockString()
    {
        double phaseLen = IsNight ? _cfg.NightLengthSeconds : _cfg.DayLengthSeconds;
        double frac = phaseLen > 0 ? _phaseElapsed / phaseLen : 0;
        double startHour = IsNight ? 18.0 : 6.0;
        double hourOfDay = (startHour + frac * 12.0) % 24.0;
        int h = (int)hourOfDay;
        int m = (int)((hourOfDay - h) * 60);
        return $"{h:00}:{m:00}";
    }

    public string SpeedLabel() => Paused ? "暂停" : $"{(int)Speeds[SpeedIndex]}x";
}
