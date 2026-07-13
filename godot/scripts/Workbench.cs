using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ApparelSlots.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 工作台的三工具插槽状态：装/卸卡尺·锯片·烧杯，某类配方需对应工具装上才解锁。
// **只管"哪把工具装上了没有"**——不碰配方判定（那是 CraftingLogic 的事，本类只出 InstalledTools 供其消费）。

/// <summary>
/// 工作台的三个工具插槽（用户拍板）。装进对应工具才解锁那一类配方：
/// 卡尺解锁精工/弓弩类、锯片解锁木工类、烧杯解锁化学类。工具本身是可装可卸的库存物件。
/// </summary>
public enum ToolSlot
{
    /// <summary>卡尺：精工/弓弩类配方（如自制弓）。</summary>
    Calipers,

    /// <summary>锯片：木工类配方（如椅子）。</summary>
    SawBlade,

    /// <summary>烧杯：化学类配方（如火药、鞣制药水）。</summary>
    Beaker,
}

/// <summary>工具插槽中文显示名（供 UI/日志读取）。</summary>
public static class ToolSlotExtensions
{
    /// <summary>单一事实源在 <see cref="DisplayNames"/>。</summary>
    public static string Label(this ToolSlot slot) => DisplayNames.Of(slot);
}

/// <summary>
/// 单座工作台的工具装配态（纯逻辑，无 Godot 依赖）：三个 <see cref="ToolSlot"/> 各"是否装了对应工具"。
/// 配方系统按 <see cref="InstalledTools"/> 判定工具门槛（见 <see cref="CraftingLogic"/>）；
/// 装/卸走 <see cref="InstallTool"/>/<see cref="RemoveTool"/>，幂等。
/// </summary>
public sealed class WorkbenchState
{
    private readonly HashSet<ToolSlot> _installed = new();

    /// <summary>把对应工具装进某槽（幂等；返回本次是否发生装入）。</summary>
    public bool InstallTool(ToolSlot slot) => _installed.Add(slot);

    /// <summary>从某槽卸下工具（幂等；返回本次是否确实卸下）。</summary>
    public bool RemoveTool(ToolSlot slot) => _installed.Remove(slot);

    /// <summary>某槽当前是否已装对应工具。</summary>
    public bool HasTool(ToolSlot slot) => _installed.Contains(slot);

    /// <summary>当前已装工具的槽集合只读快照（供 <see cref="CraftingLogic"/> 判定工具门槛）。</summary>
    public IReadOnlySet<ToolSlot> InstalledTools => _installed.ToHashSet();
}
