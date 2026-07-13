using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 CraftingService.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 拆除执行服务：把"只出返还表不碰库存"的 SalvageLogic 接到营地共享库存（InventoryStore），**实扣实产**——
//   Salvage          —— 从库存移除那件东西 → 按其建造配方算返还 → 材料入库。
//   SalvageStructure —— 拆一段围栏/大门/门：只把返还入库（结构本体的移除/导航重烘焙归 Godot 消费层）。
// 分工同 CraftingLogic/CraftingService：判定与算术在纯函数那边，这边只负责"真的把东西从库存里拿走、把材料放进去"。

/// <summary>一次拆除的结果（成功即已实扣实产）。</summary>
/// <param name="Success">是否拆除成功（false 时库存原样未动，看 <see cref="FailureReason"/>）。</param>
/// <param name="FailureReason">失败原因文案（成功为 null）。</param>
/// <param name="Refunded">返还的材料（成功时已 Add 进 inventory）。</param>
/// <param name="WorkMinutes">这次拆解该花的工时（游戏分钟，供工时制队列排 <see cref="SalvageJob"/>）。</param>
public sealed record SalvageResult(
    bool Success,
    string? FailureReason,
    IReadOnlyList<Item> Refunded,
    int WorkMinutes)
{
    /// <summary>造一个失败结果（不含返还，库存未动）。</summary>
    public static SalvageResult Fail(string reason)
        => new(false, reason, Array.Empty<Item>(), 0);
}

/// <summary>拆除执行（把返还表接到库存，实扣实产）。无状态、静态。</summary>
public static class SalvageService
{
    /// <summary>
    /// **开工**（工时制）：把那件东西从库存里拿走（锁定，防重复下单），返回该拆多久。
    /// 材料**留待完工**才返还（见 <see cref="CompleteSalvage"/>）——语义对齐制作的"开工即扣料、完工才出货"。
    /// <para>
    /// 拆不动 ⇒ 库存原样不动，返回失败：没有单件产物配方（搜刮来的军用枪：不知道它怎么造的，也就无从拆起），
    /// 或库存里根本没有这么一件。
    /// </para>
    /// </summary>
    /// <param name="itemKey">在拆之物的 <see cref="Item.RefKey"/>（武器名/护甲名/家具键）。</param>
    /// <param name="inventory">营地共享库存（本步只**移除**那件东西）。</param>
    public static SalvageResult StartSalvage(string itemKey, InventoryStore inventory)
    {
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));

        RecipeData? recipe = SalvageLogic.RecipeFor(itemKey);
        if (recipe is null)
        {
            return SalvageResult.Fail($"拆不出什么来：{itemKey} 没有建造配方");
        }

        Item? target = inventory.Items.FirstOrDefault(
            i => i.RefKey == itemKey && i.Category != ItemCategory.Material);
        if (target is null)
        {
            return SalvageResult.Fail($"库存里没有这件东西：{itemKey}");
        }

        inventory.Remove(target);
        return new SalvageResult(true, null, Array.Empty<Item>(), SalvageLogic.WorkMinutesOf(recipe));
    }

    /// <summary>
    /// **完工**：按建造配方把返还材料入库（那件东西已在 <see cref="StartSalvage"/> 时移除）。
    /// 查不到配方（数据被改坏）⇒ 失败，不凭空造材料。
    /// </summary>
    public static SalvageResult CompleteSalvage(string itemKey, InventoryStore inventory)
    {
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));

        RecipeData? recipe = SalvageLogic.RecipeFor(itemKey);
        if (recipe is null)
        {
            return SalvageResult.Fail($"拆解完工但配方丢失：{itemKey}");
        }

        IReadOnlyList<Item> refunded = Grant(SalvageLogic.YieldOfRecipe(recipe), inventory);
        return new SalvageResult(true, null, refunded, SalvageLogic.WorkMinutesOf(recipe));
    }

    /// <summary>
    /// 即时拆一件东西（<see cref="StartSalvage"/> + <see cref="CompleteSalvage"/> 一步走完）：
    /// 移除该物品 → 返还材料入库。任一条不满足 ⇒ 库存原样不动，返回失败。
    /// 营地走工时制（分两步），本方法供不需要工时的调用点与单测使用。
    /// </summary>
    public static SalvageResult Salvage(string itemKey, InventoryStore inventory)
    {
        SalvageResult started = StartSalvage(itemKey, inventory);
        return started.Success ? CompleteSalvage(itemKey, inventory) : started;
    }

    /// <summary>
    /// 拆一处营地结构（门 / 大门）：按 <see cref="StructureBuildCost"/> 折半的返还入库。
    /// <para>
    /// <b>围栏拆不动</b>（用户拍板：墙不能建、不能拆、只能砸——零回收；理由见 <see cref="SalvageLogic"/> 里
    /// 那段 kill box 注释）：传围栏进来一律失败，不返还任何东西。
    /// </para>
    /// <b>结构本体的移除</b>（去实心、重烘焙导航）归 Godot 消费层——本服务只管材料落袋。
    /// </summary>
    public static SalvageResult SalvageStructure(StructureTier tier, InventoryStore inventory)
    {
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));

        if (!SalvageLogic.CanSalvageStructure(tier))
        {
            return SalvageResult.Fail("墙拆不了——只能砸掉，而砸掉什么也剩不下。");
        }

        IReadOnlyList<Item> refunded = Grant(SalvageLogic.YieldOfStructure(tier), inventory);
        return new SalvageResult(true, null, refunded, SalvageLogic.WorkMinutesOfStructure(tier));
    }

    /// <summary>
    /// 拆一件营地家具（工作台 / 柜子…）：按 <see cref="FurnitureBuildCost"/> 折半的返还入库。
    /// 不在目录里的东西（收音机 / 废墟 / 尸体——不是造出来的）拆不动，返回失败。
    /// <b>家具本体的移除</b>（去实心、重烘焙导航）归 Godot 消费层。
    /// </summary>
    public static SalvageResult SalvageFurniture(string furnitureKey, InventoryStore inventory)
    {
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));

        if (!SalvageLogic.CanSalvageFurniture(furnitureKey))
        {
            return SalvageResult.Fail($"拆不出什么来：{furnitureKey} 不是造出来的东西");
        }

        IReadOnlyList<Item> refunded = Grant(SalvageLogic.YieldOfFurniture(furnitureKey), inventory);
        return new SalvageResult(true, null, refunded, SalvageLogic.WorkMinutesOfFurniture(furnitureKey));
    }

    /// <summary>把一张返还表落地成库存里的材料堆（未登记在 <see cref="Materials"/> 目录的键会被跳过——目录是唯一真源）。</summary>
    private static IReadOnlyList<Item> Grant(IReadOnlyDictionary<string, int> yield, InventoryStore inventory)
    {
        var produced = new List<Item>();
        foreach (KeyValuePair<string, int> kv in yield)
        {
            MaterialDef? def = Materials.Find(kv.Key);
            if (def is null || kv.Value <= 0)
            {
                continue;
            }

            Item stack = def.Value.ToItem(kv.Value);
            inventory.Add(stack);
            produced.Add(stack);
        }
        return produced;
    }
}
