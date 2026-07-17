using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 神秘商人交易的**克莉丝汀「巧舌如簧」价率通路**（<see cref="ChristinePerk"/> L2 买入折扣 / L3 卖出价率 60→70）
/// 落到 <see cref="MerchantTrade"/> / <see cref="MerchantBuyList"/> 的纯逻辑护栏。
/// <para>零回归焊死：不传折扣/覆盖 = 原价 / 60%，与既有卖出侧行为逐分一致（见 <see cref="MerchantSellTests"/>）。</para>
/// </summary>
public class MerchantChristineTests
{
    private static InventoryStore Store(params Item[] items)
    {
        var s = new InventoryStore();
        s.AddRange(items);
        return s;
    }

    // ==================== 买入折扣：EffectiveBuyPrice ====================

    [Fact]
    public void EffectiveBuyPrice_默认无折扣_原价()
    {
        Assert.Equal(3000, MerchantTrade.EffectiveBuyPrice(3000));           // 默认 0 折扣
        Assert.Equal(3000, MerchantTrade.EffectiveBuyPrice(3000, 0.0));
    }

    [Fact]
    public void EffectiveBuyPrice_6点25pct折扣_分级向下取整()
    {
        // 3000 分 ×(1−0.0625)=2812.5 → (int) 2812（向下取整，0.01 银缺口归商人=合规消费点）。
        Assert.Equal(2812, MerchantTrade.EffectiveBuyPrice(3000, ChristinePerk.Level2BuyDiscount));
    }

    [Fact]
    public void Buy_克莉丝汀L2折扣_实扣折后价()
    {
        var offer = new MerchantOffer(Item.Book("carpentry_basics", "木匠入门"), price: 3000, stock: 1);

        // 折扣买入：实扣 2812，余 188。
        var discounted = Store(Item.Material(Materials.CurrencyKey, "白银", 3000));
        Assert.Equal(PurchaseStatus.Ok, MerchantTrade.Buy(discounted, offer, buyDiscount: ChristinePerk.Level2BuyDiscount));
        Assert.Equal(188, discounted.MaterialCount(Materials.CurrencyKey));

        // 零回归：无折扣实扣原价 3000，余 0。
        var offer2 = new MerchantOffer(Item.Book("carpentry_basics", "木匠入门"), price: 3000, stock: 1);
        var full = Store(Item.Material(Materials.CurrencyKey, "白银", 3000));
        Assert.Equal(PurchaseStatus.Ok, MerchantTrade.Buy(full, offer2));
        Assert.Equal(0, full.MaterialCount(Materials.CurrencyKey));
    }

    [Fact]
    public void Check_折后价_钱够即可买_缺口按折后价()
    {
        var offer = new MerchantOffer(Item.Book("carpentry_basics", "木匠入门"), price: 3000, stock: 1);
        // 持币 2900：原价买不起（差 100），折后价 2812 买得起。
        Assert.False(MerchantTrade.Check(offer, currencyOwned: 2900).CanBuy);
        Assert.True(MerchantTrade.Check(offer, currencyOwned: 2900, buyDiscount: ChristinePerk.Level2BuyDiscount).CanBuy);
    }

    // ==================== 卖出价率：SellPrice / SellUnitPrice / SellableRows 覆盖 ====================

    [Fact]
    public void SellPrice_覆盖70pct_否则默认60pct()
    {
        Assert.Equal(1200, MerchantTrade.SellPrice(2000));                 // 默认 60%
        Assert.Equal(1200, MerchantTrade.SellPrice(2000, null));           // null → 默认 60%
        Assert.Equal(1400, MerchantTrade.SellPrice(2000, 70));             // 克莉丝汀 L3 → 70%
    }

    [Fact]
    public void SellUnitPrice_覆盖70pct_逐分正确()
    {
        var kit = Item.Material("first_aid_kit", "急救包", 1); // 基准 2000 分
        Assert.Equal(1200, MerchantBuyList.SellUnitPrice(kit));            // 默认 60%
        Assert.Equal(1400, MerchantBuyList.SellUnitPrice(kit, 70));        // 70%
    }

    [Fact]
    public void SellableRows_覆盖70pct_展示价跟着涨()
    {
        var store = Store(Item.Material("first_aid_kit", "急救包", 2));
        var rows60 = MerchantBuyList.SellableRows(store);
        var rows70 = MerchantBuyList.SellableRows(store, 70);
        Assert.Equal(1200, System.Linq.Enumerable.Single(rows60).UnitSellPrice);
        Assert.Equal(1400, System.Linq.Enumerable.Single(rows70).UnitSellPrice);
    }

    [Fact]
    public void SellOne_克莉丝汀L3价率_入账70pct()
    {
        // 70% 卖出：2000×70% = 1400 入账。
        var store = Store(Item.Material("first_aid_kit", "急救包", 2));
        Assert.Equal(SellStatus.Ok, MerchantTrade.SellOne(store, Item.Material("first_aid_kit", "急救包", 1), sellRatePercentOverride: 70));
        Assert.Equal(1400, store.MaterialCount(Materials.CurrencyKey));

        // 零回归：默认 60% → 1200。
        var store60 = Store(Item.Material("first_aid_kit", "急救包", 2));
        Assert.Equal(SellStatus.Ok, MerchantTrade.SellOne(store60, Item.Material("first_aid_kit", "急救包", 1)));
        Assert.Equal(1200, store60.MaterialCount(Materials.CurrencyKey));
    }
}
