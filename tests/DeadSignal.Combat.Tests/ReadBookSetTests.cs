using System.Collections.Generic;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 幸存者个人已读书集纯逻辑单测：配方书门槛由"营地全局已读"升级为"制作者本人已读"后，
/// 判据即读本对象。锁的是规则形态——某人读过某书 ⇔ 其 <see cref="ReadBookSet.HasRead"/> 命中，各人独立。
/// </summary>
public class ReadBookSetTests
{
    [Fact]
    public void Default_NothingRead()
    {
        var set = new ReadBookSet();
        Assert.False(set.HasRead("wilderness_survival_guide"));
        Assert.Empty(set.ReadBooks);
    }

    [Fact]
    public void MarkRead_ThenHasRead()
    {
        var set = new ReadBookSet();
        set.MarkRead("wilderness_survival_guide");
        Assert.True(set.HasRead("wilderness_survival_guide"));
        Assert.False(set.HasRead("farmer_hundred_questions")); // 只标了这本，别的仍未读
    }

    [Fact]
    public void MarkRead_IsIdempotent()
    {
        var set = new ReadBookSet();
        set.MarkRead("b1");
        set.MarkRead("b1");
        Assert.True(set.HasRead("b1"));
        Assert.Single(set.ReadBooks);
    }

    [Fact]
    public void TwoReaders_AreIndependent()
    {
        var a = new ReadBookSet();
        var b = new ReadBookSet();
        a.MarkRead("guide");
        Assert.True(a.HasRead("guide"));   // A 读过
        Assert.False(b.HasRead("guide"));  // B 没读——个人集彼此独立
    }

    /// <summary>
    /// 端到端语义：配方书门槛按"制作者本人已读"判定——读者 A 的已读集喂进 <see cref="CraftingLogic.CanCraft"/> 可制作，
    /// 没读的 B 被 <see cref="CraftBlockReason.UnreadBook"/> 挡下。其余门槛（材料/工具）全俱全，只隔离书这一项。
    /// </summary>
    [Fact]
    public void BookGate_IsPerCrafter()
    {
        var recipe = new RecipeData(
            Id: "bone_knife",
            DisplayName: "骨刀",
            Category: RecipeCategory.Misc,
            OutputKey: "bone_knife",
            OutputQuantity: 1,
            MaterialCosts: new Dictionary<string, int>(),
            RequiredTools: new HashSet<ToolSlot>(),
            RequiredBookIds: new List<string> { "wilderness_survival_guide" });

        var reader = new ReadBookSet();   // A 读过《野外生存指南》
        reader.MarkRead("wilderness_survival_guide");
        var stranger = new ReadBookSet(); // B 没读

        CraftAvailability aCan = CraftingLogic.CanCraft(
            recipe, _ => 0,
            bookId => reader.HasRead(bookId), new HashSet<ToolSlot>());
        CraftAvailability bBlocked = CraftingLogic.CanCraft(
            recipe, _ => 0,
            bookId => stranger.HasRead(bookId), new HashSet<ToolSlot>());

        Assert.True(aCan.CanCraft);                                              // A 本人读过 → 可制作
        Assert.False(bBlocked.CanCraft);                                        // B 没读 → 挡下
        Assert.Contains(bBlocked.Blocks, x => x.Reason == CraftBlockReason.UnreadBook);
    }
}
