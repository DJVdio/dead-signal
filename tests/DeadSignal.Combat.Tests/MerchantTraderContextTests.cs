using System.Text.RegularExpressions;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public sealed class MerchantTraderContextTests
{
    private static string CampMainSource()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "godot", "scripts", "CampMain.cs"));
    }

    [Fact]
    public void MerchantSession_KeepsActualArriverAndClearsItOnCloseOrDeparture()
    {
        string source = CampMainSource();

        Assert.Contains("private Pawn? _merchantTrader", source);
        Assert.Contains("OpenMerchantPanel(arriver)", source);
        Assert.Matches(new Regex(@"void OpenMerchantPanel\(Pawn trader\)[\s\S]*?_merchantTrader\s*=\s*trader", RegexOptions.CultureInvariant), source);
        Assert.Matches(new Regex(@"void CloseMerchant\(\)[\s\S]*?_merchantTrader\s*=\s*null", RegexOptions.CultureInvariant), source);
        Assert.Matches(new Regex(@"void OnMerchantGone\(\)[\s\S]*?_merchantTrader\s*=\s*null", RegexOptions.CultureInvariant), source);
    }

    [Fact]
    public void DisplayAndSettlement_UseSameActualTraderBookMultiplier()
    {
        string source = CampMainSource();

        Assert.Contains("BookPassiveEffects.SellPriceMultiplier(_merchantTrader.HasReadBook)", source);
        Assert.Matches(new Regex(@"SellableRows\(_inventory,\s*sellRate,\s*sellPriceMultiplier\)", RegexOptions.CultureInvariant), source);
        Assert.Matches(new Regex(@"SellOne\(_inventory,\s*row\.UnitItem,[\s\S]*?sellPriceMultiplier:\s*sellPriceMultiplier", RegexOptions.CultureInvariant), source);
    }

    [Fact]
    public void ReaderAndChristineBonuses_MultiplyAndDisplayedPriceEqualsPaidPrice()
    {
        var store = new InventoryStore();
        store.Add(Item.Material("first_aid_kit", "急救包", 1));
        double multiplier = BookPassiveEffects.SellPriceMultiplier(id => id == BookLibrary.EssenceOfSalesId);
        SellRow displayed = Assert.Single(MerchantBuyList.SellableRows(store, 70, multiplier));

        Assert.Equal(1442, displayed.UnitSellPrice); // 2000 × 70% × 1.03，最终只取整一次
        Assert.Equal(SellStatus.Ok, MerchantTrade.SellOne(
            store, displayed.UnitItem, sellRatePercentOverride: 70, sellPriceMultiplier: multiplier));
        Assert.Equal(displayed.UnitSellPrice, store.MaterialCount(Materials.CurrencyKey));
    }
}
