using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Recipe.cs / CraftingLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 工作台制作面板（CraftingPanel）的**纯展示逻辑**：把配方按"需要的工具槽"分组、给出工具需求文案与满足判定、
// 聚合库存里某材料的现有数量。面板本体（Godot 层）只做控件装配与事件发射，把这几个可测的算法抽到这里 Link 测。

/// <summary>
/// 一组"工具需求相同"的配方（供制作面板按工具分类列出）。<see cref="Tools"/> 为该组共同需要的工具槽
/// （按枚举序去重升序；空 = 无需工具）；<see cref="Recipes"/> 为落入该组的配方（保持声明顺序）。
/// </summary>
public sealed record RecipeToolGroup(IReadOnlyList<ToolSlot> Tools, IReadOnlyList<RecipeData> Recipes);

/// <summary>制作面板的纯展示/聚合算法（无状态、无副作用、无 Godot 依赖）。</summary>
public static class CraftingPanelFormat
{
    /// <summary>
    /// 把配方按"所需工具槽集合"分组：同一工具需求归一组，桶按首次出现顺序排列，组内配方保持传入顺序。
    /// 每组 <see cref="RecipeToolGroup.Tools"/> 已按枚举序升序去重（空集 = 无需工具组）。
    /// </summary>
    public static IReadOnlyList<RecipeToolGroup> GroupByTool(IEnumerable<RecipeData> recipes)
    {
        var order = new List<string>();
        var tools = new Dictionary<string, IReadOnlyList<ToolSlot>>();
        var members = new Dictionary<string, List<RecipeData>>();

        foreach (RecipeData r in recipes)
        {
            IReadOnlyList<ToolSlot> sortedTools = r.RequiredTools.OrderBy(t => (int)t).ToList();
            string sig = string.Join(",", sortedTools.Select(t => (int)t));
            if (!members.TryGetValue(sig, out List<RecipeData>? bucket))
            {
                bucket = new List<RecipeData>();
                members[sig] = bucket;
                tools[sig] = sortedTools;
                order.Add(sig);
            }
            bucket.Add(r);
        }

        return order.Select(sig => new RecipeToolGroup(tools[sig], members[sig])).ToList();
    }

    /// <summary>某组的工具需求中文文案："无需工具" / "需卡尺" / "需卡尺、烧杯"。</summary>
    public static string ToolRequirementLabel(IReadOnlyList<ToolSlot> tools)
        => tools.Count == 0 ? "无需工具" : "需" + string.Join("、", tools.Select(t => t.Label()));

    /// <summary>该组需要的工具是否已在工作台全部装上（空需求恒 true）。</summary>
    public static bool ToolsInstalled(IReadOnlyList<ToolSlot> groupTools, IReadOnlySet<ToolSlot> installed)
        => groupTools.All(installed.Contains);

    /// <summary>库存里某材料（按 <see cref="Item.RefKey"/> 匹配）的现有堆叠总量，供配方材料门槛判定。</summary>
    public static int MaterialCount(InventoryStore inventory, string key)
        => inventory.ByCategory(ItemCategory.Material).Where(i => i.RefKey == key).Sum(i => i.MaterialQuantity);

    /// <summary>
    /// 制作者「书门槛」查询：返回 <paramref name="recipe"/> 要求、但 <paramref name="isBookRead"/> 判为未读的书 id
    /// （按 <see cref="RecipeData.RequiredBookIds"/> 顺序、去重）。空 = 该制作者书门槛已满足（含无书门槛配方，如木椅/自制弓恒空）。
    /// 只看书、不看工具/材料——工具/材料非制作者相关，故供制作面板把"换制作者"对书门槛配方的影响显式化。
    /// </summary>
    public static IReadOnlyList<string> UnreadRequiredBooks(RecipeData recipe, Func<string, bool> isBookRead)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        if (isBookRead is null) throw new ArgumentNullException(nameof(isBookRead));

        var unread = new List<string>();
        foreach (string bookId in recipe.RequiredBookIds)
        {
            if (!unread.Contains(bookId) && !isBookRead(bookId))
            {
                unread.Add(bookId);
            }
        }
        return unread;
    }

    /// <summary>
    /// 制作者「书门槛」的人读提示："需读完《书名》、《书名》"；书门槛已满足（<see cref="UnreadRequiredBooks"/> 为空）返回 <c>null</c>。
    /// <paramref name="bookTitle"/> 把 book id 映射为书名（查不到时应由调用方兜底为 id），注入以免本纯逻辑依赖 Godot/BookLibrary。
    /// </summary>
    public static string? BookGateHint(RecipeData recipe, Func<string, bool> isBookRead, Func<string, string> bookTitle)
    {
        if (bookTitle is null) throw new ArgumentNullException(nameof(bookTitle));

        IReadOnlyList<string> unread = UnreadRequiredBooks(recipe, isBookRead);
        if (unread.Count == 0) return null;
        return "需读完" + string.Join("、", unread.Select(id => $"《{bookTitle(id)}》"));
    }
}
