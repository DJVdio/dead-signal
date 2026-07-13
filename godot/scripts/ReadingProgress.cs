using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ReadBookSet.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 单个读者对每本书的累计阅读进度（游戏内小时）。读书是耗时活动、只夜晚、可多夜完成——
// 故进度**跨夜持久、不清零**：没读完的书下夜接着读。读满 BookData.ReadHours 即算读完（上层再置 ReadBookSet/已读）。

/// <summary>
/// 单个幸存者的**逐书阅读进度**（纯逻辑，无 Godot 依赖）：按 book id 累计已投入的游戏内小时。
/// 与 <see cref="ReadBookSet"/>（读完的离散集合）互补——本类记"读到哪了"，读满即完成。
/// 跨夜持久：<see cref="Advance"/> 只增不减，不同书互相独立。
/// </summary>
public sealed class ReadingProgress
{
    private readonly Dictionary<string, double> _hours = new();

    /// <summary>某书已累计投入的阅读小时（未读过为 0）。</summary>
    public double HoursOn(string bookId) => _hours.TryGetValue(bookId, out double h) ? h : 0.0;

    /// <summary>推进某书的阅读进度（累加游戏内小时；跨夜持久，不清零）。<paramref name="hours"/> ≤ 0 无副作用。</summary>
    public void Advance(string bookId, double hours)
    {
        if (hours <= 0) return;
        _hours[bookId] = HoursOn(bookId) + hours;
    }

    /// <summary>某书是否已读满（累计小时 ≥ 该书 <see cref="BookData.ReadHours"/>）。</summary>
    public bool IsComplete(string bookId, double bookReadHours) => HoursOn(bookId) >= bookReadHours;

    /// <summary>
    /// 导出全部进度（存档用）。<see cref="HoursOn"/> 只能按已知书名逐本查，读档时我们并不知道
    /// 这个读者半途而废过哪几本——故必须能整本字典倒出来。
    /// </summary>
    public IReadOnlyDictionary<string, double> Snapshot() => new Dictionary<string, double>(_hours);

    /// <summary>读档：覆盖全部进度（先清空，再灌入）。</summary>
    public void Restore(IEnumerable<KeyValuePair<string, double>> entries)
    {
        _hours.Clear();
        foreach (var kv in entries)
        {
            _hours[kv.Key] = kv.Value;
        }
    }
}
