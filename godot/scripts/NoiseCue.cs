using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DeadSignal.Godot;

/// <summary>一条发给玩家提示层的噪音事件。它不改变 NoiseLogic 的唤醒判定。</summary>
public readonly record struct NoiseCue(
    Vector2 Origin,
    double Radius,
    NoiseKind Kind,
    RatNoiseSource Source);

/// <summary>
/// 噪音表现层事件总线。生产者只发布事件，Godot overlay 自行订阅；纯逻辑测试可以使用同一通路。
/// </summary>
public static class NoiseCueFeed
{
    public static event Action<NoiseCue>? Published;

    public static void Publish(NoiseCue cue)
        => Published?.Invoke(cue);

    /// <summary>测试/场景销毁时清理订阅，避免静态事件持有旧节点。</summary>
    public static void Reset()
        => Published = null;
}

/// <summary>短时噪音提示的有界缓冲，供 HUD 或 overlay 取快照。</summary>
public sealed class NoiseCueBuffer : IDisposable
{
    private readonly int _capacity;
    private readonly Queue<NoiseCue> _items = new();
    private bool _disposed;

    public NoiseCueBuffer(int capacity = 32)
    {
        _capacity = Math.Max(1, capacity);
        NoiseCueFeed.Published += OnPublished;
    }

    public IReadOnlyList<NoiseCue> Snapshot()
        => _items.ToList();

    public void Clear()
        => _items.Clear();

    private void OnPublished(NoiseCue cue)
    {
        if (_disposed)
        {
            return;
        }

        _items.Enqueue(cue);
        while (_items.Count > _capacity)
        {
            _items.Dequeue();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NoiseCueFeed.Published -= OnPublished;
        _disposed = true;
    }
}

/// <summary>面向玩家的噪音来源短文案；不把英文枚举名泄漏到 HUD。</summary>
public static class NoiseCueText
{
    public static string Describe(NoiseCue cue)
    {
        string source = cue.Source switch
        {
            RatNoiseSource.Footstep => "脚步",
            RatNoiseSource.DoorOpen => "开门",
            RatNoiseSource.Lockpick => "撬锁",
            RatNoiseSource.SilentDismantle => "拆除",
            RatNoiseSource.WeaponAttack => "攻击",
            RatNoiseSource.Breach => "破防",
            _ => "声响",
        };
        return $"{source}（半径 {Math.Round(cue.Radius):0}）";
    }
}
