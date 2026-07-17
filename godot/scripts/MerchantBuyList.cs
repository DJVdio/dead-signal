using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Materials.cs / MerchantShelf.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 神秘商人**收购表**（白名单，用户拍板 [DECISION-RESOLVED] 商人卖出侧）：商人只收购**基础物资**——
// 食物 + 材料（含医疗品，医疗品即 MaterialCategory.Medical）；武器/护甲/书/光源/货币一律不收。
// 每项**基准价**以**整银**字面量入表（可读；拟定待调，跟 Materials 目录同为代码驱动数据表先例），
// 经 <see cref="Silver.FromWhole"/> 在 <see cref="BasePriceOf"/> 边界转**分**；实际收购价（玩家所得，分）
// = 基准价 × <see cref="MerchantTrade.SellRatePercent"/>%（走 <see cref="MerchantTrade.SellPrice"/> 助手，
// 60% 分级取整——[SPEC-B14-补6] 后不再截成整银，如木料基准 2 银→收购 1.20 银=120 分）。

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
    /// <summary>食物每份基准价（**整银**，拟定待调；经 <see cref="BasePriceOf"/> 转分）。</summary>
    public const int FoodBasePricePerUnit = 6;

    // 材料基准价表（key→基准价**整银**，拟定待调）。不在表中的材料商人不收（货币 silver 亦不入表 → 不可卖）。
    // [SPEC-B14-补6] 分制后收购价保 2dp（基准 1 银即得 0.60 银=60 分），不再有整除截零退化。
    private static readonly IReadOnlyDictionary<string, int> _materialBasePrice = new Dictionary<string, int>
    {
        // —— 基础材料 ——
        ["wood"] = 2,
        // 布：破布(2) 与布料(5) 合并后取 3——不取均值 3.5，因合并后布是全表最易得的纺织物（供给≈两者之和），压向低价一侧。
        ["cloth"] = 3,
        // [T46] 铁：废金属(3) 与金属锭(8) 合并后取 **3**，同布的先例——不取均值，压向**低价一侧**。
        // 理由比布还硬：金属锭合并前**没有任何获取途径**（零产出、零掉落、商人不卖）⇒ 它的 8 银从来没有人真的收到过，
        // 合并后铁的实际供给 = 原废金属的供给，一分不多。按 8 银收，等于凭空给玩家的旧废料涨价 167%。
        ["iron"] = 3,
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
        // —— [T67] 宰杀链的两样可交易材料 ——
        // 🔴 **老鼠肉 / 鸟肉刻意不入表**：食材（rat/potato/mushroom…）本来就一个都不在这张表里
        //    ——商人不收吃的，这是既有口径，不为新肉破例（否则"打猎换钱"会绕开整个饥饿系统）。
        ["leather_scrap"] = 2,   // 碎皮革：低于生皮（4）——四块才缝得成一张，卖价自然该在它下面
        // 羽毛：本想标 1 银（它值钱的地方在箭上，不在商人那儿），但 **1 银在这张表里是不合法的** ——
        // 🔴 这张表有一条**结构性价格地板 = 2 银**：卖出按 60% 折算（`SellRatePercent`），
        //    基准 1 银 ⇒ 卖 0.60 银 < 1 银 ⇒ 撞上 `MerchantSellTests.EveryWhitelistedItem_YieldsAtLeastOneSilver`
        //    那条"白名单里的东西卖出至少值 1 银"的护栏。⇒ **凡入表者，基准价 ≥ 2**。
        //    （这条地板是被那个护栏当场抓出来的，不是我拍脑袋定的；它同时解释了为什么全表最便宜的几样都正好是 2。）
        ["feather"] = 2,
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

    /// <summary>某物品的**单位基准价**（**分**；食物=每份；材料=表内价；不可收购=0）。整银表值经 <see cref="Silver.FromWhole"/> 转分。</summary>
    public static int BasePriceOf(Item item)
    {
        if (item == null)
        {
            return 0;
        }
        if (item.Category == ItemCategory.Food)
        {
            return Silver.FromWhole(FoodBasePricePerUnit);
        }
        if (item.Category == ItemCategory.Material && item.RefKey != null
            && _materialBasePrice.TryGetValue(item.RefKey, out int price))
        {
            return Silver.FromWhole(price);
        }
        return 0;
    }

    /// <summary>
    /// 某物品的**单位收购价**（玩家所得，**分**）= 基准价 × 卖出价率% 分级取整（走 <see cref="MerchantTrade.SellPrice"/>）。不可收购=0。
    /// <paramref name="sellRatePercentOverride"/> 非空时用它替代默认 60%（克莉丝汀 L3 在营传 70）；默认 <c>null</c> ⇒ 60%，零回归。
    /// </summary>
    public static int SellUnitPrice(Item item, int? sellRatePercentOverride = null)
        => MerchantTrade.SellPrice(BasePriceOf(item), sellRatePercentOverride);

    /// <summary>
    /// 把库存里可收购之物聚合成卖出面板行：食物合并为一行（总份数）；材料按键聚合（每键一行，量=该键总持有）。
    /// 只列白名单内且持有量&gt;0 者，按"食物在前、材料按库存首次出现顺序"排列。
    /// </summary>
    /// <param name="sellRatePercentOverride">克莉丝汀 L3 卖出价率覆盖（默认 null＝60%，零回归；她在营 L3 传 70），展示价须与 <see cref="MerchantTrade.SellOne"/> 实付一致。</param>
    public static IReadOnlyList<SellRow> SellableRows(InventoryStore store, int? sellRatePercentOverride = null)
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
            rows.Add(new SellRow(ItemCategory.Food, null, "食物", totalFood, SellUnitPrice(foodUnit, sellRatePercentOverride)));
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
                rows.Add(new SellRow(ItemCategory.Material, it.RefKey, it.DisplayName, owned, SellUnitPrice(it, sellRatePercentOverride)));
            }
        }

        return rows;
    }
}
