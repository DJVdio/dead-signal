using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Recipe.cs / Workbench.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 配方系统的判定与结算核心（纯函数）：
//   CanCraft —— 逐条核对工具 / 已读书 / 材料三类门槛，输出"能不能做 + 缺什么（逐条原因）"。
//   Resolve  —— 能做时给出"要扣的材料 delta + 产出"，**由调用方去 InventoryStore 实扣实产**，本块不碰库存。
// 通用技能系统已删——配方不再看技能门槛、不再回喂经验。
// 所有外部依赖走**入参/委托**（材料计数 / 书已读谓词 / 已装工具集），不耦合 Pawn / InventoryStore / Godot，保证可测。

/// <summary>某条配方门槛未满足的原因类别。</summary>
public enum CraftBlockReason
{
    /// <summary>工作台缺少该类配方需要的工具（卡尺/锯片/烧杯）。</summary>
    MissingTool,

    /// <summary>制作者尚未读完某本解锁书。</summary>
    UnreadBook,

    /// <summary>库存材料不足。</summary>
    InsufficientMaterial,

    /// <summary>制作者身份/剧情态门槛未满足（如狗装备需"制作者是道格且羁绊≥2 级"）。</summary>
    CrafterLocked,
}

/// <summary>一条未满足门槛的明细（原因 + 人读说明 + 相关键）。</summary>
public readonly record struct CraftBlock(CraftBlockReason Reason, string Detail, string Key);

/// <summary>
/// 一次 <see cref="CraftingLogic.CanCraft"/> 的结果：能否制作 + 全部未满足门槛（逐条，供 UI 灰显/提示）。
/// <see cref="CanCraft"/>=true ⇔ <see cref="Blocks"/> 为空。
/// </summary>
public sealed record CraftAvailability(bool CanCraft, IReadOnlyList<CraftBlock> Blocks);

/// <summary>
/// <see cref="CraftingLogic.Resolve"/> 的产出契约（**纯数据，接入波据此实扣实产**）：
/// <see cref="MaterialDeltas"/> 为各材料的库存增量（消耗为负）；<see cref="OutputKey"/>×<see cref="OutputQuantity"/> 为产物。
/// </summary>
public sealed record CraftResolution(
    IReadOnlyDictionary<string, int> MaterialDeltas,
    string OutputKey,
    int OutputQuantity);

/// <summary>配方判定与结算（纯函数，无状态、无副作用）。</summary>
public static class CraftingLogic
{
    /// <summary>
    /// 判定 <paramref name="recipe"/> 当前能否制作，并列出全部未满足门槛。三类门槛全过 ⇒ 可制作。
    /// </summary>
    /// <param name="recipe">要判定的配方。</param>
    /// <param name="availableMaterial">材料 RefKey → 当前库存计数（未登记视为 0）。</param>
    /// <param name="isBookRead">制作者是否读完某 book id（解锁书判据）。</param>
    /// <param name="installedTools">工作台当前已装的工具槽集合。</param>
    /// <param name="crafterGate">
    /// **制作者门槛**判据（可选）：给一个门槛键（<see cref="RecipeData.RequiredCrafterGates"/> 里的键），返回
    /// <c>null</c>＝已满足、否则返回人读的阻塞说明（供 UI 灰显）。判定逻辑（谁是制作者、羁绊几级）由调用方持有，
    /// 本纯逻辑不认识具体门槛语义。**fail-closed**：配方带门槛但未传此委托 ⇒ 一律拦下（不误放）。
    /// </param>
    public static CraftAvailability CanCraft(
        RecipeData recipe,
        Func<string, int> availableMaterial,
        Func<string, bool> isBookRead,
        IReadOnlySet<ToolSlot> installedTools,
        Func<string, string?>? crafterGate = null)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        if (availableMaterial is null) throw new ArgumentNullException(nameof(availableMaterial));
        if (isBookRead is null) throw new ArgumentNullException(nameof(isBookRead));

        var blocks = new List<CraftBlock>();

        // 1) 工具门槛：每个需要的工具槽都得装上。
        foreach (ToolSlot tool in recipe.RequiredTools)
        {
            if (installedTools is null || !installedTools.Contains(tool))
            {
                blocks.Add(new CraftBlock(
                    CraftBlockReason.MissingTool,
                    $"需在工作台装上{tool.Label()}",
                    tool.ToString()));
            }
        }

        // 2) 书门槛：制作者须读完每本解锁书。
        foreach (string bookId in recipe.RequiredBookIds)
        {
            if (!isBookRead(bookId))
            {
                blocks.Add(new CraftBlock(
                    CraftBlockReason.UnreadBook,
                    $"制作者需先读完相关书籍（{bookId}）",
                    bookId));
            }
        }

        // 3) 材料门槛：每种材料库存须够付。
        foreach (KeyValuePair<string, int> cost in recipe.MaterialCosts)
        {
            int have = availableMaterial(cost.Key);
            if (have < cost.Value)
            {
                blocks.Add(new CraftBlock(
                    CraftBlockReason.InsufficientMaterial,
                    $"材料不足：{cost.Key} 需{cost.Value}、有{have}",
                    cost.Key));
            }
        }

        // 4) 制作者门槛（狗装备等）：逐键问 crafterGate。fail-closed——有门槛但无判据即整条拦下。
        if (recipe.RequiredCrafterGates is { Count: > 0 } gates)
        {
            foreach (string gateKey in gates)
            {
                string? detail = crafterGate is null
                    ? "制作者门槛未满足"
                    : crafterGate(gateKey);
                if (detail is not null)
                {
                    blocks.Add(new CraftBlock(CraftBlockReason.CrafterLocked, detail, gateKey));
                }
            }
        }

        return new CraftAvailability(blocks.Count == 0, blocks);
    }

    /// <summary>
    /// 给出一次制作的产出契约（不校验、不改库存——调用方应先 <see cref="CanCraft"/> 通过再调）：
    /// 材料按成本取负 delta、产物按配方产出。<paramref name="times"/> 为批量倍数（clamp 到 ≥1）。
    /// </summary>
    public static CraftResolution Resolve(RecipeData recipe, int times = 1)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        int mult = times < 1 ? 1 : times;

        var deltas = new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> cost in recipe.MaterialCosts)
        {
            deltas[cost.Key] = -cost.Value * mult;
        }

        return new CraftResolution(
            MaterialDeltas: deltas,
            OutputKey: recipe.OutputKey,
            OutputQuantity: recipe.OutputQuantity * mult);
    }
}
