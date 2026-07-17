namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 主菜单「读档」面板的**可选性判定**：磁盘枚举（哪些槽存在、各自的摘要/兼容性/是否损坏）
// 是 SaveManager 的活（碰 Godot），但「这一槽能不能点读取」是一条与引擎无关的纯规则，抽到这里单测。

/// <summary>
/// 主菜单读档面板的纯判定。
/// <para>
/// 冷启动读档的 producer（<see cref="MainMenu"/>）拿 <c>SaveManager.List()</c> 逐槽建行，
/// 每行的「读取」按钮灰不灰、点了会不会把 <c>CampMain.PendingColdLoadSlot</c> 设进去，全看 <see cref="IsSelectable"/>。
/// </para>
/// </summary>
public static class MainMenuSlots
{
    /// <summary>
    /// 这一槽能不能读（读取按钮灰不灰、能不能设进冷启动请求槽）。
    /// <para>
    /// <b>可读 = 版本兼容 且 未损坏</b>。两者任一不满足即置灰——玩家仍看得见这一行（存档没凭空消失），
    /// 但按钮打不开。与游戏内 <see cref="SavePanel"/> 的 <c>load.Disabled = !slot.Compatible</c> 同一口径
    /// （损坏档的 <c>Compatible</c> 本就为 false，此处显式连上 <paramref name="corrupted"/> 是把这层前提写死，防回归）。
    /// </para>
    /// </summary>
    public static bool IsSelectable(bool compatible, bool corrupted)
        => compatible && !corrupted;
}
