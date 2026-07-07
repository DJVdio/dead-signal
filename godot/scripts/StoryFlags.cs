using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 这是"按条件播放用户写的台词"框架里的**剧情开关存储**：一组 key→值（字符串）。
// condition 谓词读它、bubble 的 triggers 写它——推动用户手写剧情的走向。
// ★系统不建任何关系/好感/性格模型：flag 的键名、取值、语义**全部来自 json 里用户填的数据**，
//   代码只负责"存/取/比较"，不预设任何具体 flag。

/// <summary>
/// 世界/剧情 flag 存储：key（字符串）→ 值（字符串）。缺省即"未设置"（null）。
/// 布尔/枚举/整数一律以字符串承载（如 "true" / "met" / "3"），比较按不区分大小写的字符串相等——
/// 保持数据驱动、避免代码预判取值域。可测、无副作用之外的状态。
/// </summary>
public sealed class StoryFlags
{
    private readonly Dictionary<string, string> _values;

    public StoryFlags()
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>用初始 key→值填充（如存档恢复）。</summary>
    public StoryFlags(IEnumerable<KeyValuePair<string, string>> initial) : this()
    {
        foreach (var kv in initial)
        {
            _values[kv.Key] = kv.Value;
        }
    }

    /// <summary>取某 key 的值；未设置返回 null。</summary>
    public string? Get(string key) =>
        key != null && _values.TryGetValue(key, out var v) ? v : null;

    /// <summary>该 key 是否已设置（任意值）。</summary>
    public bool Has(string key) => key != null && _values.ContainsKey(key);

    /// <summary>置某 key 为某值（覆盖）。value 为 null 视为清除该 key。</summary>
    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }
        if (value is null)
        {
            _values.Remove(key);
        }
        else
        {
            _values[key] = value;
        }
    }

    /// <summary>某 key 是否等于某值（不区分大小写；期望值为 null 时判"未设置"）。</summary>
    public bool Equals(string key, string? expected)
    {
        string? actual = Get(key);
        if (expected is null)
        {
            return actual is null;
        }
        return actual != null && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>当前已设置的 key 数（调试/存档用）。</summary>
    public int Count => _values.Count;

    /// <summary>导出快照（存档用）。</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
}
