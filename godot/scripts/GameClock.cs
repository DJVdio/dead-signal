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

    public int Day { get; private set; } = 0;
    public bool IsNight => CurrentPhase is DayPhase.NightPrep or DayPhase.NightAct;

    /// <summary>数据驱动的开局配置：true 时首日直接进夜晚推进（NightAct）。由 StartFirstDay 消费。</summary>
    public bool StartAtNight => _cfg.StartAtNight;

    /// <summary>
    /// 显式设定当前天数（供开局直接进 NightAct 时补首日 Day=1——该相位转换不自增 Day，否则 HUD 显示「第 0 天」）。
    /// </summary>
    public void SetDay(int day) => Day = Math.Max(0, day);

    public DayPhase CurrentPhase { get; private set; } = DayPhase.DayPrep;

    private double _phaseElapsed;
    private double _travelElapsed;
    private bool _warningFired;

    /// <summary>相位内已流逝的秒数（存档用；相位切换时归零）。</summary>
    public double PhaseElapsed => _phaseElapsed;

    /// <summary>路上已走的秒数（存档用）。</summary>
    public double TravelElapsed => _travelElapsed;

    /// <summary>“该回来了”的警告是否已在本相位发过（存档用；不存的话读档会重复弹一次）。</summary>
    public bool WarningFired => _warningFired;

    /// <summary>
    /// 读档：把时钟摆回存档那一刻。<b>不触发 <see cref="OnPhaseChanged"/></b>——
    /// 读档不是"相位切换"，世界是被摆回去的，不是走过去的；发事件会让订阅方（结算/刷怪/尸体腐化）
    /// 把一个已经结算过的相位再结算一遍。
    /// </summary>
    public void Restore(int day, DayPhase phase, double phaseElapsed, double travelElapsed, bool warningFired, int speedIndex)
    {
        Day = Math.Max(0, day);
        CurrentPhase = phase;
        _phaseElapsed = Math.Max(0, phaseElapsed);
        _travelElapsed = Math.Max(0, travelElapsed);
        _warningFired = warningFired;
        SpeedIndex = Math.Clamp(speedIndex, 0, Speeds.Length - 1);
        _userPaused = false;
        Engine.TimeScale = Speeds[SpeedIndex];
    }

    public event Action<DayPhase>? OnPhaseChanged;
    public event Action? OnExploreWarning;

    public double CurrentSpeed => Speeds[SpeedIndex];

    public double GetExploreTimeRemaining()
    {
        if (CurrentPhase != DayPhase.DayExplore)
            return 0;
        return Math.Max(0, _cfg.DayLengthSeconds - _phaseElapsed);
    }

    /// <summary>
    /// 天亮前还剩多少实时秒（不在夜里 → 0）。
    /// 供劫掠者判断「还来不来得及慢慢撬/慢慢拆」（<see cref="IntrusionLogic"/>：天快亮了就改砸）。
    /// 纯读，不改任何时钟状态。
    /// </summary>
    public double GetNightTimeRemaining()
        => IsNight ? Math.Max(0, _cfg.NightLengthSeconds - _phaseElapsed) : 0;

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
        // 首个 DayPrep 相位会 Day += 1 —— 起始置 0 使首日显示"第 1 天"（修 #3 off-by-one）
        Day = 0;
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
                // 夜晚实时流逝 NightLengthSeconds，到时进入黎明聚餐（再由聚餐结束绕回 DayPrep、Day+1）。
                _phaseElapsed += delta;
                if (_phaseElapsed >= _cfg.NightLengthSeconds)
                    TransitionTo(DayPhase.DawnMeal);
                break;
        }
    }

    public void TogglePause()
    {
        if (CurrentPhase is DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep
            or DayPhase.DawnMeal or DayPhase.DuskMeal)
            return;
        _userPaused = !_userPaused;
        ApplyPhaseTimeScale();
    }

    /// <summary>
    /// 显式设定暂停态（模态面板开合用：开面板停表、关面板还原）。
    /// <para>
    /// 与 <see cref="TogglePause"/> 不同，本方法<b>不看相位</b>——那几个"本来就不流动"的相位
    /// （筹备/聚餐/回营）里 Toggle 会直接 return，但面板还是得能把状态存下来再还原回去，
    /// 否则关面板时会把一个从没设过的值写回去。
    /// </para>
    /// </summary>
    public void SetPaused(bool paused)
    {
        _userPaused = paused;
        ApplyPhaseTimeScale();
    }

    public void SetSpeedIndex(int index)
    {
        SpeedIndex = Mathf.Clamp(index, 0, Speeds.Length - 1);
        if (CurrentPhase is DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep
            or DayPhase.DawnMeal or DayPhase.DuskMeal)
            return;
        _userPaused = false;
        ApplyPhaseTimeScale();
    }

    private void ApplyPhaseTimeScale()
    {
        // DawnMeal / DuskMeal 与三个编排/过渡相位同属强制暂停模态（聚餐气泡交流期间冻结世界）。
        if (CurrentPhase is DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep
            or DayPhase.DawnMeal or DayPhase.DuskMeal)
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

    private (int H, int M) ClockHm()
    {
        double phaseLen = IsNight ? _cfg.NightLengthSeconds : _cfg.DayLengthSeconds;
        double frac = phaseLen > 0 ? _phaseElapsed / phaseLen : 0;
        double startHour = IsNight ? 18.0 : 6.0;
        double hourOfDay = (startHour + frac * 12.0) % 24.0;
        int h = (int)hourOfDay;
        int m = (int)((hourOfDay - h) * 60);
        return (h, m);
    }

    public string ClockString()
    {
        var (h, m) = ClockHm();
        return $"{h:00}:{m:00}";
    }

    // HUD 脏检查用：当前显示时钟的"分钟键"（与 ClockString 同粒度，无字符串分配）。
    public int ClockMinuteKey()
    {
        var (h, m) = ClockHm();
        return h * 60 + m;
    }

    public string SpeedLabel() => Paused ? "暂停" : $"{(int)Speeds[SpeedIndex]}x";
}
