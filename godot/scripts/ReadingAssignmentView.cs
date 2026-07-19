using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

/// <summary>
/// 读物显示选项（逐角色逐书）：含已读标记和阅读进度。
/// 纯 C# 数据模型，无 Godot 依赖，可被测试工程以 Link 方式编入单测。
/// </summary>
public readonly struct BookDisplayOption
{
    public string BookId { get; init; }
    public string Title { get; init; }
    public bool IsRead { get; init; }
    public double ReadHours { get; init; }
    public double RequiredHours { get; init; }
    public string? PrerequisiteBookId { get; init; }
    public string? PrerequisiteTitle { get; init; }
}

/// <summary>
/// 读者选项（供 ReadingAssignmentView 使用，无 Godot 依赖）。
/// </summary>
public readonly struct ReaderOption
{
    public int Id { get; init; }
    public string Name { get; init; }
}

/// <summary>
/// 读书面板的完整视图状态：角色列表 + 逐角色逐书显示项。
/// 消费方（CampMain）在打开面板前组装此结构，由 ReadingPanel 消费展示。
/// </summary>
public readonly struct ReadingAssignmentView
{
    public IReadOnlyList<ReaderOption> Pawns { get; init; }
    public IReadOnlyList<BookDisplayOption> Books { get; init; }

    /// <summary>
    /// 给定角色是否已读完某书（判前置是否满足、展示"已读"标记）。
    /// </summary>
    public Func<int, string, bool>? ReaderHasReadBook { get; init; }
    public Func<int, string, double>? ReaderHoursOnBook { get; init; }
}
