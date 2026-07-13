using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Item.cs / InventoryStore.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 神秘商人货架：数据驱动的售卖条目表（商品 Item + 单价 + 库存），第一版只有《木匠入门》，架子可扩展。
// 判定/结算（钱够不够、售罄、实扣实产）在 MerchantTrade.cs；本文件只管货架结构与库存增减。

/// <summary>
/// 货架上的一条售卖条目：一件商品（<see cref="Good"/>）、单价（<see cref="Price"/>，**分**计，见 <see cref="Silver"/>）、
/// 剩余库存（<see cref="Stock"/>，卖一件减一，减到 0 即 <see cref="SoldOut"/> 下架）。库存可变（跨来访保留，默认不补货）。
/// </summary>
public sealed class MerchantOffer
{
    /// <summary>商品物品（书/材料/武器…）。购买成功时向库存追加它的一份拷贝（record 值语义）。</summary>
    public Item Good { get; }

    /// <summary>单价（**分**，draft 待调；1 白银=100 分，见 <see cref="Silver"/>）。</summary>
    public int Price { get; }

    /// <summary>剩余库存（≥0）。购买成功 <see cref="TakeOne"/> 减一。</summary>
    public int Stock { get; private set; }

    /// <summary>是否已售罄（库存为 0）。</summary>
    public bool SoldOut => Stock <= 0;

    public MerchantOffer(Item good, int price, int stock)
    {
        Good = good ?? throw new ArgumentNullException(nameof(good));
        Price = Math.Max(0, price);
        Stock = Math.Max(0, stock);
    }

    /// <summary>库存减一（clamp 到 ≥0）。由 <c>MerchantTrade.Buy</c> 结算成功时调用，勿在别处散调。</summary>
    internal void TakeOne() => Stock = Math.Max(0, Stock - 1);
}

/// <summary>
/// 神秘商人的货架：一批 <see cref="MerchantOffer"/>。数据驱动、可扩展（<see cref="Add"/> 追加新条目）；
/// 第一版 <see cref="Default"/> 只有《木匠入门》×1。库存跨来访保留（保守默认：卖完不补货，待用户确认）。
/// </summary>
public sealed class MerchantShelf
{
    /// <summary>《木匠入门》售价（**整银**，draft 待调；建货架时经 <see cref="Silver.FromWhole"/> 转分）——它已从探索点撤下，神秘商人是唯一来源。</summary>
    public const int CarpentryBasicsPrice = 30;

    private readonly List<MerchantOffer> _offers = new();

    /// <summary>当前全部售卖条目（只读视图，按加入顺序；含已售罄条目，UI 据 <see cref="MerchantOffer.SoldOut"/> 标下架）。</summary>
    public IReadOnlyList<MerchantOffer> Offers => _offers;

    /// <summary>货架是否已全部售罄（每条都 SoldOut；空货架也算）。</summary>
    public bool AllSoldOut => _offers.All(o => o.SoldOut);

    /// <summary>追加一条售卖条目（架子可扩展的挂点）。</summary>
    public void Add(MerchantOffer offer) => _offers.Add(offer ?? throw new ArgumentNullException(nameof(offer)));

    /// <summary>
    /// 第一版货架（draft）：只卖《木匠入门》×1。书 id = <c>carpentry_basics</c>（同 <see cref="BookLibrary.CarpentryBasics"/>）。
    /// 日后要卖别的，往这里 <see cref="Add"/> 新 <see cref="MerchantOffer"/> 即可（数据驱动，逻辑零改）。
    /// </summary>
    public static MerchantShelf Default()
    {
        BookData book = BookLibrary.CarpentryBasics();
        var shelf = new MerchantShelf();
        shelf.Add(new MerchantOffer(book.ToItem(), Silver.FromWhole(CarpentryBasicsPrice), stock: 1));
        return shelf;
    }
}
