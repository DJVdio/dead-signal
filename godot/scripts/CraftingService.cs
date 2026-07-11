using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不引入任何 Godot 类型（只引 DeadSignal.Combat 的 Weapon），
// 与 CraftingLogic.cs / WeaponMod.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测。
// 制作执行服务：把"只出契约不碰库存"的模型层（CraftingLogic.CanCraft/Resolve、WeaponMods.ApplyMods）
// 接到营地共享库存（InventoryStore），**实扣实产**——
//   Craft          —— CanCraft 判定 → Resolve 契约 → 跨堆扣材料 + 产出入库。
//   ApplyWeaponMod —— 消耗库存里的基础武器 → 按 key 取改装合成变体 → 变体作新武器入库。
// 通用技能系统已删——不再取制作者技能、不再回喂经验；配方门槛只看 工具/书/材料。
// 与 Pawn 解耦：只取"某书是否已读"谓词做入参，故本服务可无 Godot 依赖进单测。
// 营地接入（CampMain）自行把 (id => crafter.HasReadBook(id)) / WorkbenchState / InventoryStore 传进来。

/// <summary>一次 <see cref="CraftingService.Craft"/> 的结果（成功即已实扣实产）。</summary>
/// <param name="Success">是否制作成功（false 时库存未变，看 <see cref="Blocks"/> 原因）。</param>
/// <param name="Blocks">失败时未满足的门槛明细（成功为空）。</param>
/// <param name="Produced">产出的库存物品（成功时已 Add 进 inventory）。</param>
public sealed record CraftResult(
    bool Success,
    IReadOnlyList<CraftBlock> Blocks,
    IReadOnlyList<Item> Produced)
{
    /// <summary>造一个失败结果（带门槛原因，不含产物）。</summary>
    public static CraftResult Fail(IReadOnlyList<CraftBlock> blocks)
        => new(false, blocks, Array.Empty<Item>());
}

/// <summary>
/// 一次 <see cref="CraftingService.StartJob"/> 的结果（工时制开工）。成功即材料已扣（锁定），
/// 返回可推进的在制品 <see cref="CraftingJob"/>；产出留待 <see cref="CraftingService.CompleteJob"/>。
/// </summary>
/// <param name="Success">是否开工成功（false 时库存未变，看 <see cref="Blocks"/> 原因，<see cref="Job"/> 为 null）。</param>
/// <param name="Blocks">失败时未满足的门槛明细（成功为空）。</param>
/// <param name="Job">成功时的在制品（材料已扣，待推进工时）。</param>
public sealed record CraftStartResult(
    bool Success,
    IReadOnlyList<CraftBlock> Blocks,
    CraftingJob? Job)
{
    /// <summary>造一个失败结果（带门槛原因，不含在制品）。</summary>
    public static CraftStartResult Fail(IReadOnlyList<CraftBlock> blocks)
        => new(false, blocks, null);
}

/// <summary>一次 <see cref="CraftingService.ApplyWeaponMod"/> 的结果。</summary>
/// <param name="Success">是否改装成功（false 时库存未变）。</param>
/// <param name="FailureReason">失败原因文案（成功为 null）。</param>
/// <param name="Produced">产出的改装武器物品（成功时已入库）。</param>
/// <param name="Variant">改装后的武器变体（含锐击枪托覆盖等，供预览/结算）。</param>
public sealed record WeaponModResult(
    bool Success,
    string? FailureReason,
    Item? Produced,
    ModdedWeapon? Variant)
{
    public static WeaponModResult Fail(string reason)
        => new(false, reason, null, null);
}

/// <summary>制作 / 改装执行服务（把模型契约接到库存与技能，实扣实产）。无状态、静态。</summary>
public static class CraftingService
{
    // ======================== 跨堆材料纯逻辑（易错点，先红后绿单测覆盖）========================

    /// <summary>跨多堆合计某材料 key 的总数量（同 key 的 Material 物品数量相加；非材料/异 key 忽略）。</summary>
    public static int MaterialTotal(IEnumerable<Item> items, string key)
        => items.Where(i => i.Category == ItemCategory.Material && i.RefKey == key)
                .Sum(i => i.MaterialQuantity);

    /// <summary>给定材料堆与需求 map（正数=需要多少），跨堆合计是否够付（缺任一即 false）。</summary>
    public static bool HasEnough(IEnumerable<Item> items, IReadOnlyDictionary<string, int> demand)
    {
        List<Item> list = items.ToList();
        foreach (KeyValuePair<string, int> kv in demand)
        {
            if (kv.Value > 0 && MaterialTotal(list, kv.Key) < kv.Value)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 跨堆扣减：给定当前材料堆列表 + 需求 map（正数=消耗量），按加入顺序逐堆扣，返回**扣减后应保留**的材料堆。
    /// 消耗殆尽的堆丢弃；部分消耗的堆改成剩余数量的新堆（<see cref="Item"/> 不可变，故重造）；非需求材料原样保留。
    /// 需求超过库存时按尽力扣减（调用方须先 <see cref="HasEnough"/> 保证够付）。
    /// </summary>
    public static IReadOnlyList<Item> Deduct(IReadOnlyList<Item> materialStacks, IReadOnlyDictionary<string, int> demand)
    {
        var need = new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> kv in demand)
        {
            if (kv.Value > 0)
            {
                need[kv.Key] = kv.Value;
            }
        }

        var result = new List<Item>();
        foreach (Item item in materialStacks)
        {
            if (item.Category != ItemCategory.Material || item.RefKey is null
                || !need.TryGetValue(item.RefKey, out int remaining) || remaining <= 0)
            {
                result.Add(item);
                continue;
            }

            int take = Math.Min(remaining, item.MaterialQuantity);
            need[item.RefKey] = remaining - take;
            int left = item.MaterialQuantity - take;
            if (left > 0)
            {
                result.Add(Item.Material(item.RefKey, item.DisplayName, left, item.Description));
            }
            // left == 0 → 该堆耗尽，丢弃。
        }
        return result;
    }

    // ======================== 制作执行 ========================

    /// <summary>
    /// 执行一次制作：先 <see cref="CraftingLogic.CanCraft"/> 判三门槛（工具/书/材料，材料按 <paramref name="times"/> 放大校验），
    /// 不可制作则原样返回失败（不动库存）；可制作则 <see cref="CraftingLogic.Resolve"/> 出契约 →
    /// 跨堆从 <paramref name="inventory"/> 扣材料 → 造产物入库，返回成功结果。
    /// </summary>
    /// <param name="recipe">配方。</param>
    /// <param name="isBookRead">"某书 id 是否已读"谓词（营地传 <c>id =&gt; crafter.HasReadBook(id)</c>）。</param>
    /// <param name="workbench">工作台工具装配态（工具门槛判据）。</param>
    /// <param name="inventory">营地共享库存（实扣材料、实产物品）。</param>
    /// <param name="times">批量倍数（clamp 到 ≥1）。</param>
    /// <param name="outputFactory">
    /// 产物工厂（可选）：(outputKey, quantity) → 若干库存物品。null 用内置默认
    /// （材料 key 走 <see cref="Materials"/> 造一堆材料，否则退化为按 key 造武器物品若干件）。
    /// 营地/数据可覆盖以精确处理护甲/书/家具等非材料产物。
    /// </param>
    public static CraftResult Craft(
        RecipeData recipe,
        Func<string, bool> isBookRead,
        WorkbenchState workbench,
        InventoryStore inventory,
        int times = 1,
        Func<string, int, IEnumerable<Item>>? outputFactory = null,
        Func<string, string?>? crafterGate = null)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        if (isBookRead is null) throw new ArgumentNullException(nameof(isBookRead));
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));

        int mult = times < 1 ? 1 : times;

        // 即时路径 = 开工扣料 + 立即产出（工时制的 StartJob 复用同一扣料半段，只是延后产出）。
        if (!TryDeductMaterials(recipe, isBookRead, workbench, inventory, mult, crafterGate, out IReadOnlyList<CraftBlock> blocks))
        {
            return CraftResult.Fail(blocks);
        }

        CraftResolution resolution = CraftingLogic.Resolve(recipe, mult);
        IReadOnlyList<Item> produced = ProduceOutput(
            resolution.OutputKey, resolution.OutputQuantity, inventory, outputFactory);
        return new CraftResult(true, Array.Empty<CraftBlock>(), produced);
    }

    /// <summary>
    /// 三门槛判定（工具/书/材料，材料按 <paramref name="mult"/> 放大二次校验）→ 通过则跨堆**实扣材料**（锁定），返回 true；
    /// 任一门槛不满足则不动库存，<paramref name="blocks"/> 出缺项，返回 false。
    /// <see cref="Craft"/>（即时）与 <see cref="StartJob"/>（工时制开工）共用此扣料半段——语义一致：开工即扣。
    /// </summary>
    private static bool TryDeductMaterials(
        RecipeData recipe,
        Func<string, bool> isBookRead,
        WorkbenchState workbench,
        InventoryStore inventory,
        int mult,
        Func<string, string?>? crafterGate,
        out IReadOnlyList<CraftBlock> blocks)
    {
        IReadOnlySet<ToolSlot> installed = workbench?.InstalledTools ?? new HashSet<ToolSlot>();
        List<Item> materials = inventory.ByCategory(ItemCategory.Material).ToList();

        // 门槛判定：材料计数跨堆合计；书查谓词；工具查工作台；制作者门槛（狗装备）查 crafterGate。
        CraftAvailability availability = CraftingLogic.CanCraft(
            recipe,
            k => MaterialTotal(materials, k),
            isBookRead,
            installed,
            crafterGate);
        if (!availability.CanCraft)
        {
            blocks = availability.Blocks;
            return false;
        }

        CraftResolution resolution = CraftingLogic.Resolve(recipe, mult);

        // 批量放大后可能材料不够（CanCraft 只按单份校验材料）→ 二次跨堆校验，缺则失败。
        var demand = resolution.MaterialDeltas.ToDictionary(kv => kv.Key, kv => -kv.Value);
        if (!HasEnough(materials, demand))
        {
            blocks = demand
                .Where(kv => MaterialTotal(materials, kv.Key) < kv.Value)
                .Select(kv => new CraftBlock(
                    CraftBlockReason.InsufficientMaterial,
                    $"材料不足：{kv.Key} 需{kv.Value}、有{MaterialTotal(materials, kv.Key)}",
                    kv.Key))
                .ToList();
            return false;
        }

        // 实扣：跨堆扣减后，把库存里的材料堆整体替换为剩余堆（非材料物品不动）。
        IReadOnlyList<Item> remainingMaterials = Deduct(materials, demand);
        foreach (Item m in materials)
        {
            inventory.Remove(m);
        }
        foreach (Item m in remainingMaterials)
        {
            inventory.Add(m);
        }

        blocks = Array.Empty<CraftBlock>();
        return true;
    }

    /// <summary>按产物 key×数量造对类别物品入库并返回（内置默认或调用方传入的 outputFactory）。</summary>
    private static IReadOnlyList<Item> ProduceOutput(
        string outputKey,
        int quantity,
        InventoryStore inventory,
        Func<string, int, IEnumerable<Item>>? outputFactory)
    {
        var produced = new List<Item>();
        Func<string, int, IEnumerable<Item>> factory = outputFactory ?? DefaultOutput;
        foreach (Item item in factory(outputKey, quantity))
        {
            inventory.Add(item);
            produced.Add(item);
        }
        return produced;
    }

    // ======================== 工时制：开工 / 完工 ========================

    /// <summary>
    /// 开一件在制品（夜间工时制）：同 <see cref="Craft"/> 的三门槛+批量材料校验，通过则**开工即扣材料（锁定，防重复下单）**
    /// 并返回可推进的 <see cref="CraftingJob"/>（总工时 = 配方 <see cref="RecipeData.WorkMinutes"/> × <paramref name="times"/>，拟定待调）；
    /// 产出留待进度满后调 <see cref="CompleteJob"/>。不通过原样失败、不动库存。
    /// 遗留（待确认）：任务中途取消/失败是否返还已扣材料——当前不返还（开工即消耗）。
    /// </summary>
    public static CraftStartResult StartJob(
        RecipeData recipe,
        Func<string, bool> isBookRead,
        WorkbenchState workbench,
        InventoryStore inventory,
        int times = 1,
        Func<string, string?>? crafterGate = null)
    {
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        if (isBookRead is null) throw new ArgumentNullException(nameof(isBookRead));
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));

        int mult = times < 1 ? 1 : times;
        if (!TryDeductMaterials(recipe, isBookRead, workbench, inventory, mult, crafterGate, out IReadOnlyList<CraftBlock> blocks))
        {
            return CraftStartResult.Fail(blocks);
        }

        var job = new CraftingJob(recipe.Id, recipe.WorkMinutes * mult, mult);
        return new CraftStartResult(true, Array.Empty<CraftBlock>(), job);
    }

    /// <summary>
    /// 完工产出：把在制品 <paramref name="job"/> 的产物按 <paramref name="recipe"/>（应与 job.RecipeId 对应）造好入库并返回。
    /// **不校验进度**——调用方应在 <see cref="CraftingJob.IsComplete"/> 时才调。产物数量 = 配方产量 × job.Times，
    /// 分类走 <paramref name="outputFactory"/>（营地传 <see cref="CraftOutputFactory.Create"/>）。材料已在 <see cref="StartJob"/> 时扣除，此处只产不扣。
    /// </summary>
    public static IReadOnlyList<Item> CompleteJob(
        CraftingJob job,
        RecipeData recipe,
        InventoryStore inventory,
        Func<string, int, IEnumerable<Item>>? outputFactory = null)
    {
        if (job is null) throw new ArgumentNullException(nameof(job));
        if (recipe is null) throw new ArgumentNullException(nameof(recipe));
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));

        CraftResolution resolution = CraftingLogic.Resolve(recipe, job.Times);
        return ProduceOutput(resolution.OutputKey, resolution.OutputQuantity, inventory, outputFactory);
    }

    /// <summary>
    /// 内置产物工厂：产物 key 是材料（在 <see cref="Materials"/> 目录）→ 造一堆该材料（单堆 quantity 份）；
    /// 否则退化为按 key 造 <paramref name="quantity"/> 件武器物品（草稿产物名多不在武器表，作占位引用键，装备校验属下游）。
    /// 护甲/书/家具等精确分类由营地/数据传 outputFactory 覆盖。
    /// </summary>
    private static IEnumerable<Item> DefaultOutput(string outputKey, int quantity)
    {
        int qty = quantity < 0 ? 0 : quantity;
        if (Materials.Has(outputKey))
        {
            MaterialDef def = Materials.Find(outputKey)!.Value;
            yield return def.ToItem(qty);
            yield break;
        }
        for (int i = 0; i < qty; i++)
        {
            yield return Item.Weapon(outputKey);
        }
    }

    // ======================== 改装执行 ========================

    /// <summary>
    /// 执行一次武器改装（MVP：只消耗基础武器→产出变体入库，材料/工具/技能成本待接 recipe）：
    /// 从 <paramref name="inventory"/> 找到 RefKey==<paramref name="baseWeaponKey"/> 的武器物品 →
    /// 由 <see cref="WeaponTable.Arsenal"/> 按名取 base <see cref="Weapon"/> → 按 <paramref name="modKeys"/>（改装名）
    /// 从 <see cref="WeaponModCatalog"/> 取对应大类的改装 → <see cref="WeaponMods.ApplyMods"/> 合成变体（捕获同部位/跨类冲突）→
    /// 消耗那件基础武器、把变体作新武器物品入库。任一步不成立返回失败（库存不变）。
    /// </summary>
    /// <param name="baseWeaponKey">基础武器的库存引用键（= WeaponTable 武器名）。</param>
    /// <param name="modKeys">要施加的改装名列表（对齐 <see cref="WeaponModCatalog"/> 中该大类改装的 Name）。</param>
    /// <param name="inventory">营地共享库存（消耗基础武器、入库变体）。</param>
    public static WeaponModResult ApplyWeaponMod(
        string baseWeaponKey,
        IReadOnlyList<string> modKeys,
        InventoryStore inventory)
    {
        if (inventory is null) throw new ArgumentNullException(nameof(inventory));
        if (string.IsNullOrEmpty(baseWeaponKey))
        {
            return WeaponModResult.Fail("未指定基础武器");
        }

        Item? baseItem = inventory.ByCategory(ItemCategory.Weapon)
            .FirstOrDefault(i => i.RefKey == baseWeaponKey);
        if (baseItem is null)
        {
            return WeaponModResult.Fail($"库存中没有「{baseWeaponKey}」");
        }

        Weapon? baseWeapon = WeaponTable.Arsenal().FirstOrDefault(w => w.Name == baseWeaponKey);
        if (baseWeapon is null)
        {
            return WeaponModResult.Fail($"未知武器「{baseWeaponKey}」（不在武器表）");
        }

        // 按大类取该武器可用改装，用名字对齐（同名跨类的"防滑缠手"靠大类天然消歧）。
        IReadOnlyList<WeaponMod> catalog = WeaponModCatalog.For(baseWeapon);
        var mods = new List<WeaponMod>();
        foreach (string key in modKeys ?? Array.Empty<string>())
        {
            WeaponMod? mod = catalog.FirstOrDefault(m => m.Name == key);
            if (mod is null)
            {
                return WeaponModResult.Fail($"「{baseWeaponKey}」没有改装「{key}」");
            }
            mods.Add(mod);
        }

        ModdedWeapon variant;
        try
        {
            variant = WeaponMods.ApplyMods(baseWeapon, mods);
        }
        catch (WeaponModException ex)
        {
            return WeaponModResult.Fail(ex.Message);
        }

        // 消耗基础武器 → 变体入库（变体名带改装后缀，作新武器引用键）。
        inventory.Remove(baseItem);
        Item produced = Item.Weapon(variant.Weapon.Name, baseItem.Description);
        inventory.Add(produced);

        return new WeaponModResult(true, null, produced, variant);
    }
}
