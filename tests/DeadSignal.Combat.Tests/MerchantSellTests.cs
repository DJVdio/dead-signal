using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 神秘商人**卖出侧**纯逻辑（用户拍板 [DECISION-RESOLVED]：白名单收购、基准价 60%）：
/// 收购白名单 <see cref="MerchantBuyList.CanSell"/> / 单位收购价 <see cref="MerchantBuyList.SellUnitPrice"/> /
/// 库存聚合行 <see cref="MerchantBuyList.SellableRows"/> / 卖出结算 <see cref="MerchantTrade.SellOne"/> /
/// 食物实扣 <see cref="InventoryStore.TrySpendFood"/>。
/// </summary>
public class MerchantSellTests
{
    private static InventoryStore Store(params Item[] items)
    {
        var s = new InventoryStore();
        s.AddRange(items);
        return s;
    }

    // —— 白名单：收购范围 ——

    [Fact]
    public void CanSell_Whitelist_FoodAndMaterialsAndMedical()
    {
        Assert.True(MerchantBuyList.CanSell(Item.Food(1)));                          // 食物
        Assert.True(MerchantBuyList.CanSell(Item.Material("wood", "木料", 1)));        // 材料
        Assert.True(MerchantBuyList.CanSell(Item.Material("bandage", "绷带", 1)));     // 医疗品
    }

    [Fact]
    public void CanSell_Rejects_WeaponsArmorBooksLightCurrency()
    {
        Assert.False(MerchantBuyList.CanSell(Item.Weapon("匕首")));
        Assert.False(MerchantBuyList.CanSell(Item.Armor("皮甲")));
        Assert.False(MerchantBuyList.CanSell(Item.Book("carpentry_basics", "木匠入门")));
        Assert.False(MerchantBuyList.CanSell(Item.Light("flashlight", "手电")));
        Assert.False(MerchantBuyList.CanSell(Item.Material(Materials.CurrencyKey, "白银", 5))); // 货币不可卖
        Assert.False(MerchantBuyList.CanSell(Item.Material("nonexistent_mat", "谜之物", 1)));   // 表外材料不收
    }

    // —— 价率：卖出 = 基准价 60% 向下取整 ——

    [Fact]
    public void SellUnitPrice_Is60PercentOfBase_Floored()
    {
        // first_aid_kit 基准 20 → 20*0.6=12；bandage 基准 5 → 5*0.6=3(3.0)；wood 基准 2 → 1(1.2 截 1)。
        Assert.Equal(12, MerchantBuyList.SellUnitPrice(Item.Material("first_aid_kit", "急救包", 1)));
        Assert.Equal(3, MerchantBuyList.SellUnitPrice(Item.Material("bandage", "绷带", 1)));
        Assert.Equal(1, MerchantBuyList.SellUnitPrice(Item.Material("wood", "木料", 1)));
        Assert.Equal(0, MerchantBuyList.SellUnitPrice(Item.Weapon("匕首"))); // 不收=0
    }

    [Fact]
    public void EveryWhitelistedItem_YieldsAtLeastOneSilver()
    {
        // 全部白名单材料基准价 ≥2 → 60% 向下取整 ≥1（无"卖了不给钱"的退化行）。
        foreach (MaterialDef def in Materials.All.Where(m => m.Category != MaterialCategory.Currency))
        {
            Item it = def.ToItem(1);
            if (MerchantBuyList.CanSell(it))
            {
                Assert.True(MerchantBuyList.SellUnitPrice(it) >= 1, $"{def.Key} 单位收购价应 ≥1");
            }
        }
        Assert.True(MerchantBuyList.SellUnitPrice(Item.Food(1)) >= 1);
    }

    // —— 库存聚合成卖出行 ——

    [Fact]
    public void SellableRows_AggregatesFoodAndMaterials_ExcludesNonWhitelist()
    {
        var store = Store(
            Item.Food(3),
            Item.Food(2),                                  // 食物两堆 → 合并 5 份一行
            Item.Material("wood", "木料", 4),
            Item.Material("wood", "木料", 1),              // 木料两堆 → 合并 5 一行
            Item.Material("bandage", "绷带", 2),
            Item.Weapon("匕首"),                            // 不收 → 不出行
            Item.Material(Materials.CurrencyKey, "白银", 50)); // 货币 → 不出行

        var rows = MerchantBuyList.SellableRows(store);
        Assert.Equal(3, rows.Count); // 食物 + 木料 + 绷带

        SellRow food = rows.Single(r => r.Category == ItemCategory.Food);
        Assert.Equal(5, food.OwnedCount);

        SellRow wood = rows.Single(r => r.MaterialKey == "wood");
        Assert.Equal(5, wood.OwnedCount);
        Assert.Equal(MerchantBuyList.SellUnitPrice(Item.Material("wood", "木料", 1)), wood.UnitSellPrice);

        Assert.DoesNotContain(rows, r => r.MaterialKey == Materials.CurrencyKey);
    }

    [Fact]
    public void SellableRows_Empty_WhenNothingSellable()
    {
        var store = Store(Item.Weapon("匕首"), Item.Material(Materials.CurrencyKey, "白银", 10));
        Assert.Empty(MerchantBuyList.SellableRows(store));
    }

    // —— 食物实扣：TrySpendFood ——

    [Fact]
    public void TrySpendFood_DeductsAcrossStacks_WhenEnough()
    {
        var store = Store(Item.Food(3), Item.Food(2)); // 合计 5
        Assert.True(store.TrySpendFood(4));
        Assert.Equal(1, store.TotalFood); // 扣第一堆3+第二堆1，余1
    }

    [Fact]
    public void TrySpendFood_Fails_AndLeavesStoreUntouched_WhenShort()
    {
        var store = Store(Item.Food(2));
        Assert.False(store.TrySpendFood(3));
        Assert.Equal(2, store.TotalFood);
    }

    // —— 卖出结算：SellOne ——

    [Fact]
    public void SellOne_Material_DeductsOne_AddsSilver()
    {
        var store = Store(Item.Material("first_aid_kit", "急救包", 2));
        SellStatus status = MerchantTrade.SellOne(store, Item.Material("first_aid_kit", "急救包", 1));

        Assert.Equal(SellStatus.Ok, status);
        Assert.Equal(1, store.MaterialCount("first_aid_kit"));                 // 2→1
        Assert.Equal(12, store.MaterialCount(Materials.CurrencyKey));          // 基准20 ×60% =12 入账
    }

    [Fact]
    public void SellOne_Food_DeductsOneUnit_AddsSilver()
    {
        var store = Store(Item.Food(3));
        SellStatus status = MerchantTrade.SellOne(store, Item.Food(1));

        Assert.Equal(SellStatus.Ok, status);
        Assert.Equal(2, store.TotalFood);                                     // 3→2
        Assert.Equal(MerchantBuyList.SellUnitPrice(Item.Food(1)), store.MaterialCount(Materials.CurrencyKey));
    }

    [Fact]
    public void SellOne_NotBuying_ForWeapon_ChangesNothing()
    {
        var store = Store(Item.Weapon("匕首"));
        SellStatus status = MerchantTrade.SellOne(store, Item.Weapon("匕首"));
        Assert.Equal(SellStatus.NotBuying, status);
        Assert.Equal(0, store.MaterialCount(Materials.CurrencyKey)); // 未入账
        Assert.Single(store.Items);                                  // 武器仍在
    }

    [Fact]
    public void SellOne_NoneOwned_WhenWhitelistedButAbsent()
    {
        var store = Store(); // 空库存
        SellStatus status = MerchantTrade.SellOne(store, Item.Material("bandage", "绷带", 1));
        Assert.Equal(SellStatus.NoneOwned, status);
        Assert.Equal(0, store.MaterialCount(Materials.CurrencyKey));
    }

    [Fact]
    public void SellOne_CurrencyItself_IsNotBuying()
    {
        var store = Store(Item.Material(Materials.CurrencyKey, "白银", 10));
        SellStatus status = MerchantTrade.SellOne(store, Item.Material(Materials.CurrencyKey, "白银", 1));
        Assert.Equal(SellStatus.NotBuying, status);
        Assert.Equal(10, store.MaterialCount(Materials.CurrencyKey)); // 白银未变
    }
}
