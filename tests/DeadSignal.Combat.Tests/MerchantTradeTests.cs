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
        Assert.Equal("从零到一学会木匠", offer.Good.DisplayName); // [T59] 用户在 wiki 把书名从「木匠入门」改成此名
        Assert.Equal(1, offer.Stock);
        // [SPEC-B14-补6] 分制：货架价以分计——30 银 = 3000 分。
        Assert.Equal(Silver.FromWhole(MerchantShelf.CarpentryBasicsPrice), offer.Price);
        Assert.Equal(3000, offer.Price);
        Assert.False(offer.SoldOut);
    }

    [Fact]
    public void Shelf_IsExtensible_ViaAdd()
    {
        var shelf = new MerchantShelf();
        Assert.True(shelf.AllSoldOut); // 空货架
        shelf.Add(new MerchantOffer(Item.Material("iron", "铁", 3), price: 5, stock: 2));
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

    // —— 价率（用户拍板：卖给商人=基准价60%、从商人买=100%）——

    [Fact]
    public void Rates_MatchUserRuling_Buy100_Sell60()
    {
        Assert.Equal(100, MerchantTrade.BuyRatePercent);
        Assert.Equal(60, MerchantTrade.SellRatePercent);
    }

    [Fact]
    public void BuyPrice_IsFullBasePrice_At100Percent()
    {
        Assert.Equal(30, MerchantTrade.BuyPrice(30));
        Assert.Equal(0, MerchantTrade.BuyPrice(0));
        Assert.Equal(0, MerchantTrade.BuyPrice(-5)); // 负价 clamp 到 0
    }

    [Theory]
    // 基准价以**分**计（[SPEC-B14-补6]），60% 只在分级取整：1000→600、300→180、700→420、1→0(0.6 分截 0)。
    [InlineData(1000, 600)]
    [InlineData(300, 180)]
    [InlineData(700, 420)]
    [InlineData(1, 0)]
    [InlineData(0, 0)]
    public void SellPrice_Is60Percent_CentGranularity(int baseCents, int expected)
    {
        Assert.Equal(expected, MerchantTrade.SellPrice(baseCents));
    }

    [Fact]
    public void SellPrice_KeepsTwoDecimals_NoWholeSilverFloor()
    {
        // [SPEC-B14-补6] 核心精度断言：基准 3.00 银（300 分）× 60% = 1.80 银（180 分），
        // 不再被旧整除截成整银 1（用户原话「基准3银→卖1.80」）。
        Assert.Equal(180, MerchantTrade.SellPrice(Silver.FromWhole(3)));
        Assert.Equal("1.80", Silver.Format(MerchantTrade.SellPrice(Silver.FromWhole(3))));
        // 木料基准 2 银（200 分）→ 1.20 银（120 分），旧模型会截成 1。
        Assert.Equal(120, MerchantTrade.SellPrice(Silver.FromWhole(2)));
    }

    [Fact]
    public void SellPrice_IsCheaperThanBuyPrice_ForSameBase()
    {
        // 同一基准价：玩家卖出所得 < 买入所付（商人压价 → 杜绝无损倒卖套利）。
        Assert.True(MerchantTrade.SellPrice(5000) < MerchantTrade.BuyPrice(5000));
    }

    // —— 交易判定（不改状态）——

    [Fact]
    public void Check_Ok_WhenStockedAndAffordable()
    {
        MerchantOffer offer = MerchantShelf.Default().Offers[0]; // 价3000分（30银）
        PurchaseCheck check = MerchantTrade.Check(offer, currencyOwned: offer.Price);
        Assert.True(check.CanBuy);
        Assert.Equal(PurchaseStatus.Ok, check.Status);
        Assert.Equal(0, check.Shortfall);
    }

    [Fact]
    public void Check_NotEnoughMoney_ReportsShortfall()
    {
        MerchantOffer offer = MerchantShelf.Default().Offers[0]; // 价3000分（30银）
        int shortfall = 1200; // 差 12.00 银
        PurchaseCheck check = MerchantTrade.Check(offer, currencyOwned: offer.Price - shortfall);
        Assert.False(check.CanBuy);
        Assert.Equal(PurchaseStatus.NotEnoughMoney, check.Status);
        Assert.Equal(shortfall, check.Shortfall); // 缺口以分计
    }

    [Fact]
    public void Check_SoldOut_TakesPrecedenceOverMoney()
    {
        var offer = new MerchantOffer(Item.Book("carpentry_basics", "木匠入门"), price: Silver.FromWhole(30), stock: 0);
        PurchaseCheck check = MerchantTrade.Check(offer, currencyOwned: 99900);
        Assert.Equal(PurchaseStatus.SoldOut, check.Status);
    }

    // —— 交易结算：实扣实产 ——

    [Fact]
    public void Buy_Success_DeductsCoins_AddsBook_DecrementsStock()
    {
        var store = StoreWithCoins(5000); // 50.00 银
        MerchantOffer offer = MerchantShelf.Default().Offers[0]; // 价3000分（30银）/库存1

        PurchaseStatus status = MerchantTrade.Buy(store, offer);

        Assert.Equal(PurchaseStatus.Ok, status);
        Assert.Equal(2000, store.MaterialCount(Materials.CurrencyKey)); // 5000-3000（分）
        Assert.Contains(store.Books, b => b.RefKey == "carpentry_basics"); // 书进库
        Assert.Equal(0, offer.Stock);
        Assert.True(offer.SoldOut);
    }

    [Fact]
    public void Buy_NotEnoughMoney_ChangesNothing()
    {
        var store = StoreWithCoins(2000); // 20.00 银 < 30 银
        MerchantOffer offer = MerchantShelf.Default().Offers[0];

        PurchaseStatus status = MerchantTrade.Buy(store, offer);

        Assert.Equal(PurchaseStatus.NotEnoughMoney, status);
        Assert.Equal(2000, store.MaterialCount(Materials.CurrencyKey)); // 未扣
        Assert.Empty(store.Books); // 未给书
        Assert.Equal(1, offer.Stock); // 库存未动
    }

    [Fact]
    public void Buy_SoldOut_ChangesNothing_OnSecondPurchase()
    {
        var store = StoreWithCoins(10000); // 100.00 银
        MerchantOffer offer = MerchantShelf.Default().Offers[0]; // 库存1

        Assert.Equal(PurchaseStatus.Ok, MerchantTrade.Buy(store, offer));
        // 第二次买：已售罄
        PurchaseStatus second = MerchantTrade.Buy(store, offer);
        Assert.Equal(PurchaseStatus.SoldOut, second);
        Assert.Equal(7000, store.MaterialCount(Materials.CurrencyKey)); // 仍只扣了一次3000分
        Assert.Single(store.Books); // 只拿到一本
    }
}
