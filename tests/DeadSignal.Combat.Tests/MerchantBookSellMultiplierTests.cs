using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 《销售的本质》（读后卖价乘 1.03，<see cref="BookPassiveEffects.SellPriceMultiplier"/>）
/// 与克莉丝汀卖出价率覆盖的乘算通路。展示价与实付同源。
/// </summary>
public class MerchantBookSellMultiplierTests
{
    // ==================== SellPrice 乘算 ====================

    [Fact]
    public void SellPrice_默认1倍率_零回归()
    {
        Assert.Equal(1200, MerchantTrade.SellPrice(2000));                // 默认 60%
        Assert.Equal(1200, MerchantTrade.SellPrice(2000, null));          // null → 60%
        Assert.Equal(1200, MerchantTrade.SellPrice(2000, null, 1.0));     // 显式 1.0
        Assert.Equal(1400, MerchantTrade.SellPrice(2000, 70));            // 克莉丝汀 L3
        Assert.Equal(1400, MerchantTrade.SellPrice(2000, 70, 1.0));       // 克莉丝汀 L3 + 显式 1.0
    }

    [Fact]
    public void SellPrice_书1点03倍率_单用乘算()
    {
        // 2000 分 × 60% × 1.03 = 1236
        Assert.Equal(1236, MerchantTrade.SellPrice(2000, sellPriceMultiplier: 1.03));
        // 300 分 × 60% × 1.03 = 185.4 → (int)185
        Assert.Equal(185, MerchantTrade.SellPrice(300, sellPriceMultiplier: 1.03));
    }

    [Fact]
    public void SellPrice_书1点03与克莉丝汀70pct_乘算()
    {
        // 2000 分 × 70% × 1.03 = 1442
        Assert.Equal(1442, MerchantTrade.SellPrice(2000, 70, 1.03));
        // 300 分 × 70% × 1.03 = 216.3 → (int)216
        Assert.Equal(216, MerchantTrade.SellPrice(300, 70, 1.03));
    }

    [Fact]
    public void SellPrice_负价钳零_乘算不影响()
    {
        Assert.Equal(0, MerchantTrade.SellPrice(-5));
        Assert.Equal(0, MerchantTrade.SellPrice(-5, sellPriceMultiplier: 1.03));
        Assert.Equal(0, MerchantTrade.SellPrice(-5, 70, 1.03));
    }

    // ==================== SellUnitPrice 通路 ====================

    [Fact]
    public void SellUnitPrice_默认1倍率_零回归()
    {
        var kit = Item.Material("first_aid_kit", "急救包", 1); // 基准 2000 分
        Assert.Equal(1200, MerchantBuyList.SellUnitPrice(kit));
        Assert.Equal(1200, MerchantBuyList.SellUnitPrice(kit, null));
        Assert.Equal(1200, MerchantBuyList.SellUnitPrice(kit, sellPriceMultiplier: 1.0));
        Assert.Equal(1400, MerchantBuyList.SellUnitPrice(kit, 70));
        Assert.Equal(1400, MerchantBuyList.SellUnitPrice(kit, 70, 1.0));
    }

    [Fact]
    public void SellUnitPrice_书1点03倍率()
    {
        var kit = Item.Material("first_aid_kit", "急救包", 1); // 基准 2000 分
        // 2000 × 60% × 1.03 = 1236
        Assert.Equal(1236, MerchantBuyList.SellUnitPrice(kit, sellPriceMultiplier: 1.03));
        // 2000 × 70% × 1.03 = 1442
        Assert.Equal(1442, MerchantBuyList.SellUnitPrice(kit, 70, 1.03));
    }

    // ==================== SellableRows 展示价与 SellUnitPrice 同源 ====================

    [Fact]
    public void SellableRows_书倍率_展示价与实付同源()
    {
        var store = new InventoryStore();
        store.Add(Item.Material("first_aid_kit", "急救包", 2));
        store.Add(Item.Material("wood", "木料", 5));

        // 1.03 倍率下的行价
        var rows = MerchantBuyList.SellableRows(store, sellPriceMultiplier: 1.03);

        var kitRow = Assert.Single(rows, r => r.MaterialKey == "first_aid_kit");
        Assert.Equal(MerchantBuyList.SellUnitPrice(Item.Material("first_aid_kit", "急救包", 1), sellPriceMultiplier: 1.03), kitRow.UnitSellPrice);

        var woodRow = Assert.Single(rows, r => r.MaterialKey == "wood");
        Assert.Equal(MerchantBuyList.SellUnitPrice(Item.Material("wood", "木料", 1), sellPriceMultiplier: 1.03), woodRow.UnitSellPrice);
    }

    [Fact]
    public void SellableRows_书加克莉丝汀_展示价与实付同源()
    {
        var store = new InventoryStore();
        store.Add(Item.Material("first_aid_kit", "急救包", 2));

        var rows = MerchantBuyList.SellableRows(store, 70, 1.03);
        var kitRow = Assert.Single(rows);
        Assert.Equal(MerchantBuyList.SellUnitPrice(Item.Material("first_aid_kit", "急救包", 1), 70, 1.03), kitRow.UnitSellPrice);
    }

    [Fact]
    public void SellableRows_零回归_不传倍率()
    {
        var store = new InventoryStore();
        store.Add(Item.Material("first_aid_kit", "急救包", 2));

        var rows = MerchantBuyList.SellableRows(store);
        var kitRow = Assert.Single(rows);
        Assert.Equal(1200, kitRow.UnitSellPrice);
    }

    // ==================== SellOne 实付与展示价同源 ====================

    [Fact]
    public void SellOne_书倍率_入账价等于展示价()
    {
        var store = new InventoryStore();
        store.Add(Item.Material("first_aid_kit", "急救包", 2));

        // 预期入账 = SellUnitPrice
        Assert.Equal(SellStatus.Ok, MerchantTrade.SellOne(store, Item.Material("first_aid_kit", "急救包", 1), sellPriceMultiplier: 1.03));
        Assert.Equal(MerchantBuyList.SellUnitPrice(Item.Material("first_aid_kit", "急救包", 1), sellPriceMultiplier: 1.03), store.MaterialCount(Materials.CurrencyKey));
    }

    [Fact]
    public void SellOne_书加克莉丝汀_入账价等于展示价()
    {
        var store = new InventoryStore();
        store.Add(Item.Material("first_aid_kit", "急救包", 2));

        Assert.Equal(SellStatus.Ok, MerchantTrade.SellOne(store, Item.Material("first_aid_kit", "急救包", 1), sellRatePercentOverride: 70, sellPriceMultiplier: 1.03));
        Assert.Equal(MerchantBuyList.SellUnitPrice(Item.Material("first_aid_kit", "急救包", 1), 70, 1.03), store.MaterialCount(Materials.CurrencyKey));
    }

    [Fact]
    public void SellOne_书倍率_零回归_不传则默认()
    {
        var store = new InventoryStore();
        store.Add(Item.Material("first_aid_kit", "急救包", 2));

        Assert.Equal(SellStatus.Ok, MerchantTrade.SellOne(store, Item.Material("first_aid_kit", "急救包", 1)));
        Assert.Equal(1200, store.MaterialCount(Materials.CurrencyKey));
    }
}
