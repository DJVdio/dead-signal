using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 MerchantShelf.cs / InventoryStore.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 神秘商人交易判定 + 结算：钱够/不够/已售罄的纯判定 + 一次买入的实扣实产（扣货币、入商品、库存减一）。

/// <summary>一次买入的判定结果。</summary>
public enum PurchaseStatus
{
    /// <summary>可购买：库存有货且持币足额。</summary>
    Ok,

    /// <summary>钱不够：库存有货但持币不足（差额见 <see cref="PurchaseCheck.Shortfall"/>）。</summary>
    NotEnoughMoney,

    /// <summary>已售罄：该条目库存为 0。</summary>
    SoldOut,
}

/// <summary>买入判定（纯，不改任何状态）。<see cref="Shortfall"/> 仅 <see cref="PurchaseStatus.NotEnoughMoney"/> 时为正（缺口）。</summary>
public readonly record struct PurchaseCheck(PurchaseStatus Status, int Shortfall)
{
    /// <summary>是否可成交。</summary>
    public bool CanBuy => Status == PurchaseStatus.Ok;
}

/// <summary>一次卖出（玩家把物品卖给商人）的结算结果。</summary>
public enum SellStatus
{
    /// <summary>成交：已扣一单位物品、白银入账。</summary>
    Ok,

    /// <summary>该物品不在收购白名单（武器/护甲/书/光源/货币）。</summary>
    NotBuying,

    /// <summary>白名单内但库存里已无此物可卖（持有量为 0）。</summary>
    NoneOwned,
}

/// <summary>
/// 神秘商人交易的判定与结算。判定 <see cref="Check"/> 纯函数（钱够/不够/售罄）；
/// 结算 <see cref="Buy"/> 走 <see cref="InventoryStore"/> 实扣实产（扣货币 + 入商品 + 库存减一），先判定后落地。
/// </summary>
public static class MerchantTrade
{
    // —— 价率（拟定待调；用户拍板原话：「玩家卖东西给商人是60%的价格，买东西是100%的价格。」）——
    // 基准价一律以**分**计（[SPEC-B14-补6]：白银 2dp，1 银=100 分，见 <see cref="Silver"/>）。
    // 买入按 BuyRatePercent（100%=等于基准价），卖出按 SellRatePercent（60%）折算；
    // 折算在**分**上做（如基准 3.00 银=300 分，卖出 300×60/100=180 分=1.80 银，不再被截成整银 1）。
    // 末端 /100 只在**分**这一最小刻度上取整（0.01 银缺口归商人=合规消费点），不再吞掉小数银。

    /// <summary>玩家从商人**买入**的价率（基准价的百分比；用户拍板 100%）。</summary>
    public const int BuyRatePercent = 100;

    /// <summary>玩家**卖给**商人的价率（基准价的百分比；用户拍板 60%）。</summary>
    public const int SellRatePercent = 60;

    /// <summary>某基准价（**分**）的**买入价**（分）= 基准价 × <see cref="BuyRatePercent"/>%（≥0）。</summary>
    public static int BuyPrice(int baseCents) => Math.Max(0, baseCents) * BuyRatePercent / 100;

    /// <summary>某基准价（**分**）的**卖出价**（玩家所得，分）= 基准价 × <see cref="SellRatePercent"/>%（分级取整，≥0）。</summary>
    public static int SellPrice(int baseCents) => Math.Max(0, baseCents) * SellRatePercent / 100;

    /// <summary>
    /// 判定能否买下 <paramref name="offer"/>（不改状态）：先看售罄，再看持币 <paramref name="currencyOwned"/> 是否≥单价。
    /// 钱不够时 <see cref="PurchaseCheck.Shortfall"/> = 单价 − 持币（正数缺口，供 UI 提示）。
    /// </summary>
    public static PurchaseCheck Check(MerchantOffer offer, int currencyOwned)
    {
        if (offer.SoldOut)
        {
            return new PurchaseCheck(PurchaseStatus.SoldOut, 0);
        }

        if (currencyOwned < offer.Price)
        {
            return new PurchaseCheck(PurchaseStatus.NotEnoughMoney, offer.Price - currencyOwned);
        }

        return new PurchaseCheck(PurchaseStatus.Ok, 0);
    }

    /// <summary>
    /// 实扣实产地买下一件：从 <paramref name="store"/> 按持币判定，成交则实扣 <paramref name="offer"/>.Price 个货币
    /// （货币键 <paramref name="currencyKey"/>，默认白银）+ 追加商品拷贝 + 库存减一，返回 <see cref="PurchaseStatus.Ok"/>；
    /// 售罄/钱不够则不改任何状态，返回对应状态。判定与实扣同源（<see cref="Check"/> + <see cref="InventoryStore.TrySpendMaterial"/>），不会半途扣钱不给货。
    /// </summary>
    public static PurchaseStatus Buy(InventoryStore store, MerchantOffer offer, string? currencyKey = null)
    {
        currencyKey ??= Materials.CurrencyKey;
        int owned = store.MaterialCount(currencyKey);
        PurchaseCheck check = Check(offer, owned);
        if (!check.CanBuy)
        {
            return check.Status;
        }

        // 判定已保证足额，实扣必成；仍以返回值兜底防御。
        if (!store.TrySpendMaterial(currencyKey, offer.Price))
        {
            return PurchaseStatus.NotEnoughMoney;
        }

        store.Add(offer.Good);
        offer.TakeOne();
        return PurchaseStatus.Ok;
    }

    /// <summary>
    /// 卖出一单位 <paramref name="unit"/> 给商人（用户拍板：白名单收购、按基准价 60% 计）：
    /// 不在收购白名单 → <see cref="SellStatus.NotBuying"/>；白名单内但库存无此物 → <see cref="SellStatus.NoneOwned"/>；
    /// 成交则从 <paramref name="store"/> 实扣一单位（食物扣 1 份 / 材料扣 1 个）并把 <see cref="MerchantBuyList.SellUnitPrice"/> 白银入账，返回 <see cref="SellStatus.Ok"/>。
    /// 判定与实扣同源，不会半途扣物不给钱。<paramref name="currencyKey"/> 默认白银。
    /// </summary>
    public static SellStatus SellOne(InventoryStore store, Item unit, string? currencyKey = null)
    {
        if (store == null || unit == null || !MerchantBuyList.CanSell(unit))
        {
            return SellStatus.NotBuying;
        }

        int unitPrice = MerchantBuyList.SellUnitPrice(unit);
        bool spent = unit.Category == ItemCategory.Food
            ? store.TrySpendFood(1)
            : store.TrySpendMaterial(unit.RefKey!, 1);
        if (!spent)
        {
            return SellStatus.NoneOwned;
        }

        currencyKey ??= Materials.CurrencyKey;
        string coinName = Materials.Find(currencyKey)?.DisplayName ?? "白银";
        store.Add(Item.Material(currencyKey, coinName, unitPrice));
        return SellStatus.Ok;
    }
}
