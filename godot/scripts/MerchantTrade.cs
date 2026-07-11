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

/// <summary>
/// 神秘商人交易的判定与结算。判定 <see cref="Check"/> 纯函数（钱够/不够/售罄）；
/// 结算 <see cref="Buy"/> 走 <see cref="InventoryStore"/> 实扣实产（扣货币 + 入商品 + 库存减一），先判定后落地。
/// </summary>
public static class MerchantTrade
{
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
}
