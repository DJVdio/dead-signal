using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不引入任何 Godot 类型（与 CraftingLogic.cs / CraftingService.cs 一样可被 Tests 以 Link 编入单测）。
// 制作产物分类工厂：修 CraftingService 的默认工厂遗留（默认把非材料产物一律当武器占位）。
// 按产物 key 把 6 个内置配方的产出各造对类别——武器 / 护甲 / 材料 / 家具杂项——供营地接入传 outputFactory。

/// <summary>
/// 把配方产物 key（<see cref="RecipeData.OutputKey"/>）+ 数量 造成正确类别的库存 <see cref="Item"/>：
/// 火药/鞣制药水=材料堆，骨刀/自制弓=武器，粗布背心=护甲，木椅等=杂项材料堆（暂无家具类别）。
/// 显示名沿本仓惯例取中文（武器/护甲 refKey 亦用中文名，对齐 WeaponTable/ArmorTable 命名；这些草稿产物尚不在表中，作占位引用键，装备校验属下游）。
/// </summary>
public static class CraftOutputFactory
{
    // 产物 key → 大类（草稿，随配方增补）。材料类产物（gunpowder/tanning_solution）不列此表——走 Materials 目录判定。
    private static readonly IReadOnlySet<string> WeaponOutputs = new HashSet<string> { "bone_knife", "handmade_bow" };
    private static readonly IReadOnlySet<string> ArmorOutputs = new HashSet<string> { "cloth_vest" };

    /// <summary>产物工厂：传给 <see cref="CraftingService.Craft"/> 的 outputFactory，按 key 分类造 <paramref name="quantity"/> 件产物。</summary>
    public static IEnumerable<Item> Create(string outputKey, int quantity)
    {
        int qty = quantity < 0 ? 0 : quantity;

        // 材料产物（火药/鞣制药水，key 同时是材料标识名）：造一堆该材料。
        if (Materials.Has(outputKey))
        {
            yield return Materials.Find(outputKey)!.Value.ToItem(qty);
            yield break;
        }

        // 显示名取配方名（中文，如「骨刀」「木椅」）；查不到退化为 key。
        string display = RecipeBook.All.FirstOrDefault(r => r.OutputKey == outputKey)?.DisplayName ?? outputKey;

        if (WeaponOutputs.Contains(outputKey))
        {
            for (int i = 0; i < qty; i++) yield return Item.Weapon(display);
            yield break;
        }
        if (ArmorOutputs.Contains(outputKey))
        {
            for (int i = 0; i < qty; i++) yield return Item.Armor(display);
            yield break;
        }

        // 家具/杂项（木椅等）：暂无家具类别，作杂项材料堆入库（显示名取配方名）。
        yield return Item.Material(outputKey, display, qty);
    }
}
