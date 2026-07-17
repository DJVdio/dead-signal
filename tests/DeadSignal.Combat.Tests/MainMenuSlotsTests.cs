using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 主菜单读档面板的可选性判定护栏（<see cref="MainMenuSlots.IsSelectable"/>）。
/// 冷启动读档的 producer 靠它决定「读取」按钮灰不灰、能不能把槽设进 <c>CampMain.PendingColdLoadSlot</c>——
/// 判错就会把玩家丢进一个读不了/被半兼容改写过的世界，所以钉死：可读 = 版本兼容 且 未损坏。
/// </summary>
public class MainMenuSlotsTests
{
    [Fact]
    public void 兼容且未损坏的槽可选()
        => Assert.True(MainMenuSlots.IsSelectable(compatible: true, corrupted: false));

    [Fact]
    public void 损坏的槽不可选()
        => Assert.False(MainMenuSlots.IsSelectable(compatible: false, corrupted: true));

    [Fact]
    public void 版本不兼容的旧档不可选()
        => Assert.False(MainMenuSlots.IsSelectable(compatible: false, corrupted: false));

    [Fact]
    public void 即使标了兼容只要损坏就不可选()
        // corrupted 是硬否决：哪怕 compatible 意外为 true，损坏档也绝不能点开。
        => Assert.False(MainMenuSlots.IsSelectable(compatible: true, corrupted: true));
}
