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
    // 买入按 BuyRatePercent，卖出按 SellRatePercent 折算；具体值以 Wiki 配置为准。
    // 折算在**分**上做，避免小数银被截成整银。
    // 末端 /100 只在**分**这一最小刻度上取整（0.01 银缺口归商人=合规消费点），不再吞掉小数银。

    // 【数值外置】原两个 `public const int` 已搬到 merchant.json（消费层配置范式，见 MerchantConfig）。
    // 二者仅方法体内用（BuyPrice/SellPrice）+ cref + 运行时读，非默认参数值/const-expr 上下文 ⇒ 安全改静态属性。
    // ⚠️ MerchantSchedule 的到访间隔上下限（minGap=1/maxGap=5）是构造器**默认参数值**（编译期 const），
    //    且属到访调度状态机结构（用户口径：调度是结构留代码）⇒ 不外置。
    /// <summary>玩家从商人**买入**的价率（当前值以 Wiki 配置为准）。</summary>
    public static int BuyRatePercent => GameConfigCatalog.Section<MerchantConfig>().BuyRatePercent;

    /// <summary>玩家**卖给**商人的价率（当前值以 Wiki 配置为准）。</summary>
    public static int SellRatePercent => GameConfigCatalog.Section<MerchantConfig>().SellRatePercent;

    /// <summary>某基准价（**分**）的**买入价**（分）= 基准价 × <see cref="BuyRatePercent"/>%（≥0）。</summary>
    public static int BuyPrice(int baseCents) => Math.Max(0, baseCents) * BuyRatePercent / 100;

    /// <summary>
    /// 某基准价（**分**）的**卖出价**（玩家所得，分）= 基准价 × 卖出价率% × 卖价乘数（分级取整，≥0）。
    /// <paramref name="sellRatePercentOverride"/> 非空时用它替代默认 <see cref="SellRatePercent"/>（克莉丝汀 L3 传 70）；
    /// 默认 <c>null</c> ⇒ 走 <see cref="SellRatePercent"/>(60)，零回归。
    /// <paramref name="sellPriceMultiplier"/> 书籍被动卖价乘数（《销售的本质》读后=1.03，默认 1.0），
    /// 与 <paramref name="sellRatePercentOverride"/> 乘算，仅最终取整一次；展示价与实付同源。
    /// </summary>
    public static int SellPrice(int baseCents, int? sellRatePercentOverride = null, double sellPriceMultiplier = 1.0)
        => (int)(Math.Max(0, baseCents) * (sellRatePercentOverride ?? SellRatePercent) / 100.0 * sellPriceMultiplier);

    /// <summary>
    /// 应用买入折扣后的**实付买价**（分）= <paramref name="price"/> × (1 − <paramref name="buyDiscount"/>)，分级向下取整、≥0。
    /// 克莉丝汀 L2 传 0.0625；<paramref name="buyDiscount"/>=0（默认）⇒ 原价，零回归。折扣 clamp 到 [0,1]。
    /// </summary>
    public static int EffectiveBuyPrice(int price, double buyDiscount = 0.0)
        => (int)(Math.Max(0, price) * (1.0 - Math.Clamp(buyDiscount, 0.0, 1.0)));

    /// <summary>
    /// 判定能否买下 <paramref name="offer"/>（不改状态）：先看售罄，再看持币 <paramref name="currencyOwned"/> 是否≥单价。
    /// 钱不够时 <see cref="PurchaseCheck.Shortfall"/> = 单价 − 持币（正数缺口，供 UI 提示）。
    /// </summary>
    /// <param name="buyDiscount">克莉丝汀 L2 买入折扣（默认 0＝原价，零回归）：判定按 <see cref="EffectiveBuyPrice"/> 折后价比价。</param>
    public static PurchaseCheck Check(MerchantOffer offer, int currencyOwned, double buyDiscount = 0.0)
    {
        if (offer.SoldOut)
        {
            return new PurchaseCheck(PurchaseStatus.SoldOut, 0);
        }

        int price = EffectiveBuyPrice(offer.Price, buyDiscount);
        if (currencyOwned < price)
        {
            return new PurchaseCheck(PurchaseStatus.NotEnoughMoney, price - currencyOwned);
        }

        return new PurchaseCheck(PurchaseStatus.Ok, 0);
    }

    /// <summary>
    /// 实扣实产地买下一件：从 <paramref name="store"/> 按持币判定，成交则实扣 <paramref name="offer"/>.Price 个货币
    /// （货币键 <paramref name="currencyKey"/>，默认白银）+ 追加商品拷贝 + 库存减一，返回 <see cref="PurchaseStatus.Ok"/>；
    /// 售罄/钱不够则不改任何状态，返回对应状态。判定与实扣同源（<see cref="Check"/> + <see cref="InventoryStore.TrySpendMaterial"/>），不会半途扣钱不给货。
    /// </summary>
    /// <param name="buyDiscount">克莉丝汀 L2 买入折扣（默认 0＝原价，零回归）：实扣按 <see cref="EffectiveBuyPrice"/> 折后价。</param>
    public static PurchaseStatus Buy(InventoryStore store, MerchantOffer offer, string? currencyKey = null, double buyDiscount = 0.0)
    {
        currencyKey ??= Materials.CurrencyKey;
        int owned = store.MaterialCount(currencyKey);
        PurchaseCheck check = Check(offer, owned, buyDiscount);
        if (!check.CanBuy)
        {
            return check.Status;
        }

        // 判定已保证足额，实扣必成；仍以返回值兜底防御。
        if (!store.TrySpendMaterial(currencyKey, EffectiveBuyPrice(offer.Price, buyDiscount)))
        {
            return PurchaseStatus.NotEnoughMoney;
        }

        store.Add(offer.Good);
        offer.TakeOne();
        return PurchaseStatus.Ok;
    }

    /// <summary>
    /// 卖出一单位 <paramref name="unit"/> 给商人（用户拍板：白名单收购、按 Wiki 配置价率计）：
    /// 不在收购白名单 → <see cref="SellStatus.NotBuying"/>；白名单内但库存无此物 → <see cref="SellStatus.NoneOwned"/>；
    /// 成交则从 <paramref name="store"/> 实扣一单位（食物扣 1 份 / 材料扣 1 个）并把 <see cref="MerchantBuyList.SellUnitPrice"/> 白银入账，返回 <see cref="SellStatus.Ok"/>。
    /// 判定与实扣同源，不会半途扣物不给钱。<paramref name="currencyKey"/> 默认白银。
    /// </summary>
    /// <param name="sellRatePercentOverride">克莉丝汀 L3 卖出价率覆盖（默认 null＝走 <see cref="SellRatePercent"/>=60，零回归；她在营 L3 传 70）。</param>
    /// <param name="sellPriceMultiplier">书籍被动卖价乘数（默认 1.0），实付价 <see cref="MerchantBuyList.SellUnitPrice"/> 使用同值，展示与实付同源。</param>
    public static SellStatus SellOne(InventoryStore store, Item unit, string? currencyKey = null, int? sellRatePercentOverride = null, double sellPriceMultiplier = 1.0)
    {
        if (store == null || unit == null || !MerchantBuyList.CanSell(unit))
        {
            return SellStatus.NotBuying;
        }

        int unitPrice = MerchantBuyList.SellUnitPrice(unit, sellRatePercentOverride, sellPriceMultiplier);
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
