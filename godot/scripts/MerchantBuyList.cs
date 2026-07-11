using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Materials.cs / MerchantShelf.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 神秘商人**收购表**（白名单，用户拍板 [DECISION-RESOLVED] 商人卖出侧）：商人只收购**基础物资**——
// 食物 + 材料（含医疗品，医疗品即 MaterialCategory.Medical）；武器/护甲/书/光源/货币一律不收。
// 每项**基准价**入表（拟定待调，跟 Materials 目录同为代码驱动数据表先例）；实际收购价（玩家所得）
// = 基准价 × <see cref="MerchantTrade.SellRatePercent"/>%（走 <see cref="MerchantTrade.SellPrice"/> 助手，60% 向下取整）。

/// <summary>卖出面板的一条可收购行（按物品/材料键聚合）：类别、材料键（食物为 null）、显示名、库存持有量、单位收购价。</summary>
public readonly record struct SellRow(ItemCategory Category, string? MaterialKey, string DisplayName, int OwnedCount, int UnitSellPrice)
{
    /// <summary>本行的"一单位"代表物品（供成交时按单位扣物）：食物=1 份、材料=1 个。</summary>
    public Item UnitItem => Category == ItemCategory.Food
        ? Item.Food(1, DisplayName)
        : Item.Material(MaterialKey ?? string.Empty, DisplayName, 1);
}

/// <summary>
/// 商人收购白名单 + 基准价表。<see cref="CanSell"/> 判白名单；<see cref="SellUnitPrice"/> 出单位收购价（基准价 60%）；
/// <see cref="SellableRows"/> 把玩家库存里可收购之物聚合成面板行（食物按总份数、材料按键聚合）。数据表拟定待调。
/// </summary>
public static class MerchantBuyList
{
    /// <summary>食物每份基准价（拟定待调）。</summary>
    public const int FoodBasePricePerUnit = 6;

    // 材料基准价表（key→基准价，拟定待调）。不在表中的材料商人不收（货币 silver 亦不入表 → 不可卖）。
    // 均设 ≥2 以保证 ×60% 向下取整后单位收购价 ≥1（不出现卖了不给钱的退化行）。
    private static readonly IReadOnlyDictionary<string, int> _materialBasePrice = new Dictionary<string, int>
    {
        // —— 基础材料 ——
        ["wood"] = 2,
        ["scrap_cloth"] = 2,
        ["cloth"] = 5,
        ["scrap_metal"] = 3,
        ["metal_ingot"] = 8,
        ["nails"] = 2,
        ["wire"] = 3,
        ["rawhide"] = 4,
        ["leather"] = 7,
        ["bone"] = 2,
        ["gunpowder"] = 10,
        ["tanning_solution"] = 6,
        ["fuel"] = 8,
        ["stone"] = 2,
        ["rope"] = 3,
        ["components"] = 12,
        // —— 医疗品 ——
        ["bandage"] = 5,
        ["needle_thread"] = 6,
        ["splint"] = 8,
        ["first_aid_kit"] = 20,
        ["antibiotics"] = 15,
        ["medicine"] = 10,
    };

    /// <summary>某物品是否在收购白名单：食物恒可；材料须在收购表内；其余类别（武器/护甲/书/光源/货币）不收。</summary>
    public static bool CanSell(Item item) => BasePriceOf(item) > 0;

    /// <summary>某物品的**单位基准价**（食物=每份；材料=表内价；不可收购=0）。</summary>
    public static int BasePriceOf(Item item)
    {
        if (item == null)
        {
            return 0;
        }
        if (item.Category == ItemCategory.Food)
        {
            return FoodBasePricePerUnit;
        }
        if (item.Category == ItemCategory.Material && item.RefKey != null
            && _materialBasePrice.TryGetValue(item.RefKey, out int price))
        {
            return price;
        }
        return 0;
    }

    /// <summary>某物品的**单位收购价**（玩家所得）= 基准价 × 60% 向下取整（走 <see cref="MerchantTrade.SellPrice"/>）。不可收购=0。</summary>
    public static int SellUnitPrice(Item item) => MerchantTrade.SellPrice(BasePriceOf(item));

    /// <summary>
    /// 把库存里可收购之物聚合成卖出面板行：食物合并为一行（总份数）；材料按键聚合（每键一行，量=该键总持有）。
    /// 只列白名单内且持有量&gt;0 者，按"食物在前、材料按库存首次出现顺序"排列。
    /// </summary>
    public static IReadOnlyList<SellRow> SellableRows(InventoryStore store)
    {
        var rows = new List<SellRow>();
        if (store == null)
        {
            return rows;
        }

        int totalFood = store.TotalFood;
        if (totalFood > 0)
        {
            var foodUnit = Item.Food(1);
            rows.Add(new SellRow(ItemCategory.Food, null, "食物", totalFood, SellUnitPrice(foodUnit)));
        }

        // 材料按键聚合，保持库存首次出现顺序（去重）。
        var seen = new HashSet<string>();
        foreach (Item it in store.Items)
        {
            if (it.Category != ItemCategory.Material || it.RefKey == null || !CanSell(it))
            {
                continue;
            }
            if (!seen.Add(it.RefKey))
            {
                continue;
            }
            int owned = store.MaterialCount(it.RefKey);
            if (owned > 0)
            {
                rows.Add(new SellRow(ItemCategory.Material, it.RefKey, it.DisplayName, owned, SellUnitPrice(it)));
            }
        }

        return rows;
    }
}
