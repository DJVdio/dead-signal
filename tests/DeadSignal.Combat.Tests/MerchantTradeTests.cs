using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 神秘商人货架 + 交易判定/结算纯逻辑：默认货架（《木匠入门》×1）、买入判定（钱够/不够/售罄）、
/// 实扣实产（扣白银+入书+库存减一）、以及货币持有量的库存实扣（<see cref="InventoryStore.MaterialCount"/> / <see cref="InventoryStore.TrySpendMaterial"/>）。
/// </summary>
public class MerchantTradeTests
{
    private static InventoryStore StoreWithCoins(int amount)
    {
        var store = new InventoryStore();
        if (amount > 0)
        {
            store.Add(Item.Material(Materials.CurrencyKey, "白银", amount));
        }

        return store;
    }

    // —— 货架 ——

    [Fact]
    public void DefaultShelf_SellsOnlyCarpentryBasics_OneInStock()
    {
        MerchantShelf shelf = MerchantShelf.Default();
        Assert.Single(shelf.Offers);
        MerchantOffer offer = shelf.Offers[0];
        Assert.Equal(ItemCategory.Book, offer.Good.Category);
        Assert.Equal("carpentry_basics", offer.Good.RefKey);
        Assert.Equal("木匠入门", offer.Good.DisplayName);
        Assert.Equal(1, offer.Stock);
        Assert.Equal(MerchantShelf.CarpentryBasicsPrice, offer.Price);
        Assert.False(offer.SoldOut);
    }

    [Fact]
    public void Shelf_IsExtensible_ViaAdd()
    {
        var shelf = new MerchantShelf();
        Assert.True(shelf.AllSoldOut); // 空货架
        shelf.Add(new MerchantOffer(Item.Material("scrap_metal", "废金属", 3), price: 5, stock: 2));
        shelf.Add(new MerchantOffer(Item.Weapon("匕首"), price: 40, stock: 1));
        Assert.Equal(2, shelf.Offers.Count);
        Assert.False(shelf.AllSoldOut);
    }

    // —— 货币持有量：库存实扣 ——

    [Fact]
    public void MaterialCount_SumsAcrossStacks()
    {
        var store = new InventoryStore();
        store.Add(Item.Material(Materials.CurrencyKey, "白银", 12));
        store.Add(Item.Material(Materials.CurrencyKey, "白银", 8));
        Assert.Equal(20, store.MaterialCount(Materials.CurrencyKey));
        Assert.Equal(0, store.MaterialCount("nonexistent"));
    }

    [Fact]
    public void TrySpendMaterial_DeductsAcrossStacks_WhenEnough()
    {
        var store = new InventoryStore();
        store.Add(Item.Material(Materials.CurrencyKey, "白银", 12));
        store.Add(Item.Material(Materials.CurrencyKey, "白银", 8)); // 合计 20
        Assert.True(store.TrySpendMaterial(Materials.CurrencyKey, 15));
        Assert.Equal(5, store.MaterialCount(Materials.CurrencyKey)); // 扣掉第一堆12+第二堆3，余5
    }

    [Fact]
    public void TrySpendMaterial_Fails_AndLeavesStoreUntouched_WhenShort()
    {
        var store = StoreWithCoins(10);
        Assert.False(store.TrySpendMaterial(Materials.CurrencyKey, 11));
        Assert.Equal(10, store.MaterialCount(Materials.CurrencyKey)); // 原样不动
    }

    // —— 交易判定（不改状态）——

    [Fact]
    public void Check_Ok_WhenStockedAndAffordable()
    {
        MerchantOffer offer = MerchantShelf.Default().Offers[0];
        PurchaseCheck check = MerchantTrade.Check(offer, currencyOwned: 30);
        Assert.True(check.CanBuy);
        Assert.Equal(PurchaseStatus.Ok, check.Status);
        Assert.Equal(0, check.Shortfall);
    }

    [Fact]
    public void Check_NotEnoughMoney_ReportsShortfall()
    {
        MerchantOffer offer = MerchantShelf.Default().Offers[0]; // 价30
        PurchaseCheck check = MerchantTrade.Check(offer, currencyOwned: 18);
        Assert.False(check.CanBuy);
        Assert.Equal(PurchaseStatus.NotEnoughMoney, check.Status);
        Assert.Equal(12, check.Shortfall); // 30 - 18
    }

    [Fact]
    public void Check_SoldOut_TakesPrecedenceOverMoney()
    {
        var offer = new MerchantOffer(Item.Book("carpentry_basics", "木匠入门"), price: 30, stock: 0);
        PurchaseCheck check = MerchantTrade.Check(offer, currencyOwned: 999);
        Assert.Equal(PurchaseStatus.SoldOut, check.Status);
    }

    // —— 交易结算：实扣实产 ——

    [Fact]
    public void Buy_Success_DeductsCoins_AddsBook_DecrementsStock()
    {
        var store = StoreWithCoins(50);
        MerchantOffer offer = MerchantShelf.Default().Offers[0]; // 价30/库存1

        PurchaseStatus status = MerchantTrade.Buy(store, offer);

        Assert.Equal(PurchaseStatus.Ok, status);
        Assert.Equal(20, store.MaterialCount(Materials.CurrencyKey)); // 50-30
        Assert.Contains(store.Books, b => b.RefKey == "carpentry_basics"); // 书进库
        Assert.Equal(0, offer.Stock);
        Assert.True(offer.SoldOut);
    }

    [Fact]
    public void Buy_NotEnoughMoney_ChangesNothing()
    {
        var store = StoreWithCoins(20); // < 30
        MerchantOffer offer = MerchantShelf.Default().Offers[0];

        PurchaseStatus status = MerchantTrade.Buy(store, offer);

        Assert.Equal(PurchaseStatus.NotEnoughMoney, status);
        Assert.Equal(20, store.MaterialCount(Materials.CurrencyKey)); // 未扣
        Assert.Empty(store.Books); // 未给书
        Assert.Equal(1, offer.Stock); // 库存未动
    }

    [Fact]
    public void Buy_SoldOut_ChangesNothing_OnSecondPurchase()
    {
        var store = StoreWithCoins(100);
        MerchantOffer offer = MerchantShelf.Default().Offers[0]; // 库存1

        Assert.Equal(PurchaseStatus.Ok, MerchantTrade.Buy(store, offer));
        // 第二次买：已售罄
        PurchaseStatus second = MerchantTrade.Buy(store, offer);
        Assert.Equal(PurchaseStatus.SoldOut, second);
        Assert.Equal(70, store.MaterialCount(Materials.CurrencyKey)); // 仍只扣了一次30
        Assert.Single(store.Books); // 只拿到一本
    }
}
