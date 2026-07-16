using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// <b>脚本 CG 的实时时基播放器</b>——一串定时步骤的顺序队列，<b>用真实墙钟 delta 推进</b>
/// （<see cref="Time.GetTicksMsec"/>，不吃 <see cref="Engine.TimeScale"/>），并 <see cref="Node.ProcessModeEnum.Always"/>
/// 保证暂停时仍收到 <see cref="_Process"/>。
///
/// <para>
/// <b>为什么必须自带时基</b>：脚本 CG 在 <c>TimeScale=0</c>（游戏逻辑冻结）下播放，若用引擎缩放 delta，
/// Tween / <c>_Process(delta)</c> 全部为 0——CG 自己也会被冻住。故本播放器仿 <see cref="CameraController"/>
/// 用 <c>GetTicksMsec</c> 自算真实 delta，令相机/演出角色在冻结世界里照常运动。
/// </para>
///
/// <para>
/// <b>用法</b>：<c>new CinematicSequence().Then(时长, onEnter, onTick, onExit)…Play(onComplete)</c>。
/// 每步 <c>onTick(t)</c> 收到 0→1 的线性进度；<c>onEnter/onExit</c> 各在步首步尾调一次。
/// 全部走完 → 调 <c>onComplete</c> 并自毁（<see cref="Node.QueueFree"/>）。空队列 Play 立即完成。
/// </para>
/// </summary>
public sealed partial class CinematicSequence : Node
{
    private readonly struct Step
    {
        public readonly float Duration;
        public readonly Action? OnEnter;
        public readonly Action<float>? OnTick;
        public readonly Action? OnExit;

        public Step(float duration, Action? onEnter, Action<float>? onTick, Action? onExit)
        {
            Duration = Mathf.Max(0f, duration);
            OnEnter = onEnter;
            OnTick = onTick;
            OnExit = onExit;
        }
    }

    private readonly List<Step> _steps = new();
    private int _index = -1;        // 当前步（-1=未开播）
    private float _elapsed;         // 当前步已历时（真实秒）
    private bool _entered;          // 当前步 OnEnter 是否已调
    private ulong _lastTick;
    private bool _running;
    private Action? _onComplete;

    public CinematicSequence()
    {
        // 暂停（TimeScale=0 不等于 SceneTree.Paused，但一并覆盖）下仍收 _Process。
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>追加一步（链式）。<paramref name="onTick"/> 收 0→1 线性进度。</summary>
    public CinematicSequence Then(float seconds, Action? onEnter = null, Action<float>? onTick = null, Action? onExit = null)
    {
        _steps.Add(new Step(seconds, onEnter, onTick, onExit));
        return this;
    }

    /// <summary>开播（须已入树）。走完全部步骤后调 <paramref name="onComplete"/> 并自毁。</summary>
    public void Play(Action? onComplete)
    {
        _onComplete = onComplete;
        _lastTick = Time.GetTicksMsec();
        _index = 0;
        _elapsed = 0f;
        _entered = false;
        _running = true;
        if (_steps.Count == 0)
        {
            Finish();
        }
    }

    public override void _Process(double _)
    {
        if (!_running)
        {
            return;
        }

        ulong now = Time.GetTicksMsec();
        float rdelta = (now - _lastTick) / 1000f;
        _lastTick = now;
        if (rdelta > 0.1f)
        {
            rdelta = 0.1f; // 卡顿/首帧兜底，避免一帧跳过整步
        }

        // 一帧内可能跨越多步（极短步/掉帧）：循环消化直到时间用尽或队列走完。
        while (_running && _index < _steps.Count)
        {
            Step step = _steps[_index];
            if (!_entered)
            {
                _entered = true;
                step.OnEnter?.Invoke();
            }

            _elapsed += rdelta;
            float t = step.Duration <= 0f ? 1f : Mathf.Clamp(_elapsed / step.Duration, 0f, 1f);
            step.OnTick?.Invoke(t);

            if (t < 1f)
            {
                return; // 本步未满，等下一帧
            }

            // 本步走满：结算 OnExit，进入下一步；把超出的时间带入下一步（rdelta 复用剩余量）。
            step.OnExit?.Invoke();
            rdelta = _elapsed - step.Duration; // 溢出时间续给下一步
            if (rdelta < 0f)
            {
                rdelta = 0f;
            }
            _index++;
            _elapsed = 0f;
            _entered = false;
        }

        if (_running && _index >= _steps.Count)
        {
            Finish();
        }
    }

    private void Finish()
    {
        _running = false;
        Action? cb = _onComplete;
        _onComplete = null;
        cb?.Invoke();
        QueueFree();
    }
}
