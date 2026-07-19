using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 狙击枪修理体系测试（wiki-character-sync）：神秘商人互斥刷新（损坏的狙击枪 / 《枪械维修指南》）+
/// 配方修复验证（材料消耗/书门槛/产物正确）。
/// </summary>
public class GunsmithRepairTests
{
    // ==================== 书 ====================

    [Fact]
    public void GunsmithRepairGuide_ExistsInLibrary()
    {
        BookData? book = BookLibrary.All().SingleOrDefault(b => b.Id == BookLibrary.GunsmithRepairGuideId);
        Assert.NotNull(book);
        Assert.Equal("枪械维修指南", book.Title);
        Assert.False(book.IsDiary);
        Assert.True(book.ReadHours > 0);
    }

    [Fact]
    public void GunsmithRepairGuide_GrantsRepairRecipeStub()
    {
        BookData book = BookLibrary.All().Single(b => b.Id == BookLibrary.GunsmithRepairGuideId);
        Assert.Contains("repair_sniper_rifle", book.GrantsRecipeStub);
    }

    [Fact]
    public void GunsmithRepairGuide_IsManual_NotDiary()
    {
        BookData book = BookLibrary.All().Single(b => b.Id == BookLibrary.GunsmithRepairGuideId);
        Assert.Contains(BookLibrary.Manuals(), b => b.Id == book.Id);
        Assert.DoesNotContain(BookLibrary.Diaries(), b => b.Id == book.Id);
    }

    // ==================== 配方 ====================

    [Fact]
    public void RepairSniperRifleRecipe_Exists()
    {
        RecipeData recipe = RecipeBook.All.SingleOrDefault(r => r.Id == "repair_sniper_rifle");
        Assert.NotNull(recipe);
        Assert.Equal("狙击枪", recipe.DisplayName);
    }

    [Fact]
    public void RepairSniperRifleRecipe_RequiresGunsmithRepairGuide()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        Assert.Contains(RecipeBook.GunsmithRepairGuideBookId, recipe.RequiredBookIds);
    }

    [Fact]
    public void RepairSniperRifleRecipe_RequiresCalipers()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        Assert.Contains(ToolSlot.Calipers, recipe.RequiredTools);
    }

    [Fact]
    public void RepairSniperRifleRecipe_ConsumesDamagedSniperRifle()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        var costs = recipe.MaterialCosts;
        Assert.Contains(Materials.DamagedSniperRifleKey, costs.Keys);
        Assert.True(costs[Materials.DamagedSniperRifleKey] >= 1);
    }

    [Fact]
    public void RepairSniperRifleRecipe_ConsumesWeaponParts()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        var costs = recipe.MaterialCosts;
        Assert.Contains(Materials.WeaponPartsKey, costs.Keys);
        Assert.True(costs[Materials.WeaponPartsKey] >= 1);
    }

    [Fact]
    public void RepairSniperRifleRecipe_OutputsWorkingWeapon()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        // CraftOutputFactory traverses RecipeBook to find DisplayName → Item.Weapon("狙击枪")
        Assert.Equal("repair_sniper_rifle", recipe.OutputKey);
        Assert.Equal(1, recipe.OutputQuantity);
    }

    // ==================== 配方可制作判定 ====================

    [Fact]
    public void RepairSniperRifle_CanCraft_WithAllPrerequisites()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        var material = new System.Collections.Generic.Dictionary<string, int>
        {
            [Materials.DamagedSniperRifleKey] = 1,
            [Materials.WeaponPartsKey] = 3,
        };

        CraftAvailability result = CraftingLogic.CanCraft(
            recipe,
            key => material.GetValueOrDefault(key, 0),
            bookId => bookId == RecipeBook.GunsmithRepairGuideBookId,
            new System.Collections.Generic.HashSet<ToolSlot> { ToolSlot.Calipers });

        Assert.True(result.CanCraft, $"配方应可制作，阻塞原因：{string.Join(", ", result.Blocks.Select(b => b.Detail))}");
    }

    [Fact]
    public void RepairSniperRifle_Blocked_WithoutGunsmithRepairGuide()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        var material = new System.Collections.Generic.Dictionary<string, int>
        {
            [Materials.DamagedSniperRifleKey] = 1,
            [Materials.WeaponPartsKey] = 3,
        };

        CraftAvailability result = CraftingLogic.CanCraft(
            recipe,
            key => material.GetValueOrDefault(key, 0),
            _ => false, // 未读书
            new System.Collections.Generic.HashSet<ToolSlot> { ToolSlot.Calipers });

        Assert.False(result.CanCraft);
        Assert.Contains(result.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void RepairSniperRifle_Blocked_WithoutCalipers()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");
        var material = new System.Collections.Generic.Dictionary<string, int>
        {
            [Materials.DamagedSniperRifleKey] = 1,
            [Materials.WeaponPartsKey] = 3,
        };

        CraftAvailability result = CraftingLogic.CanCraft(
            recipe,
            key => material.GetValueOrDefault(key, 0),
            bookId => bookId == RecipeBook.GunsmithRepairGuideBookId,
            new System.Collections.Generic.HashSet<ToolSlot>()); // 无卡尺

        Assert.False(result.CanCraft);
        Assert.Contains(result.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
    }

    [Fact]
    public void RepairSniperRifle_Blocked_WithoutMaterials()
    {
        RecipeData recipe = RecipeBook.All.Single(r => r.Id == "repair_sniper_rifle");

        CraftAvailability result = CraftingLogic.CanCraft(
            recipe,
            _ => 0, // 无材料
            bookId => bookId == RecipeBook.GunsmithRepairGuideBookId,
            new System.Collections.Generic.HashSet<ToolSlot> { ToolSlot.Calipers });

        Assert.False(result.CanCraft);
        Assert.Contains(result.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial);
    }

    // ==================== 互斥刷新（神秘商人货架） ====================

    [Fact]
    public void MerchantShelf_MutualExclusion_EachVariantHasExactlyOneGunsmithItem()
    {
        // 用确定序列强制产生两种变体：0.0 → Range(0,2)=0.0<1.0 → 损坏狙击枪
        var rngA = new SequenceRandomSource(0.0);
        MerchantShelf shelfA = MerchantShelf.Default(rngA);
        int gunsmithCountA = shelfA.Offers.Count(o =>
            o.Good.RefKey == Materials.DamagedSniperRifleKey
            || o.Good.RefKey == BookLibrary.GunsmithRepairGuideId);
        Assert.Equal(1, gunsmithCountA);

        // 1.5 → Range(0,2)=1.5≥1.0 → 维修指南
        var rngB = new SequenceRandomSource(1.5);
        MerchantShelf shelfB = MerchantShelf.Default(rngB);
        int gunsmithCountB = shelfB.Offers.Count(o =>
            o.Good.RefKey == Materials.DamagedSniperRifleKey
            || o.Good.RefKey == BookLibrary.GunsmithRepairGuideId);
        Assert.Equal(1, gunsmithCountB);
    }

    [Fact]
    public void MerchantShelf_MutualExclusion_NoVariantHasBoth()
    {
        // 即使连跑多次，同一货架上也不可能同时出现两者
        for (int i = 0; i < 20; i++)
        {
            var rng = new SequenceRandomSource(0.0);
            MerchantShelf shelf = MerchantShelf.Default(rng);
            bool hasDamagedSniper = shelf.Offers.Any(o => o.Good.RefKey == Materials.DamagedSniperRifleKey);
            bool hasRepairGuide = shelf.Offers.Any(o => o.Good.RefKey == BookLibrary.GunsmithRepairGuideId);
            Assert.False(hasDamagedSniper && hasRepairGuide, "不能同时出现损坏的狙击枪和维修指南");
        }
    }

    [Fact]
    public void MerchantShelf_DefaultWithRng_ContainsCarpentryBasics()
    {
        var rng = new SequenceRandomSource(0.0);
        MerchantShelf shelf = MerchantShelf.Default(rng);
        Assert.Contains(shelf.Offers, o => o.Good.RefKey == "carpentry_basics");
    }

    [Fact]
    public void MerchantShelf_DefaultWithoutRng_HasNoGunsmithItems()
    {
        // 向后兼容：无参 Default() 不包含互斥项
        MerchantShelf shelf = MerchantShelf.Default();
        Assert.DoesNotContain(shelf.Offers, o =>
            o.Good.RefKey == Materials.DamagedSniperRifleKey
            || o.Good.RefKey == BookLibrary.GunsmithRepairGuideId);
    }

    [Fact]
    public void MerchantShelf_DamagedSniperVariant_HasCorrectPrice()
    {
        var rng = new SequenceRandomSource(0.0); // Range(0,2)=0.0<1.0 → 损坏狙击枪
        MerchantShelf shelf = MerchantShelf.Default(rng);
        MerchantOffer? offer = shelf.Offers.FirstOrDefault(o => o.Good.RefKey == Materials.DamagedSniperRifleKey);
        Assert.NotNull(offer);
        Assert.Equal(Silver.FromWhole(MerchantShelf.DamagedSniperRiflePrice), offer.Price);
    }

    [Fact]
    public void MerchantShelf_RepairGuideVariant_HasCorrectPrice()
    {
        var rng = new SequenceRandomSource(1.5); // Range(0,2) = 1.5 ≥ 1.0 → false → 维修指南
        MerchantShelf shelf = MerchantShelf.Default(rng);
        MerchantOffer? offer = shelf.Offers.FirstOrDefault(o => o.Good.RefKey == BookLibrary.GunsmithRepairGuideId);
        Assert.NotNull(offer);
        Assert.Equal(Silver.FromWhole(MerchantShelf.GunsmithRepairGuidePrice), offer.Price);
    }

    // ==================== CraftOutputFactory 注册 ====================

    [Fact]
    public void RepairSniperRifle_IsRegisteredInWeaponOutputs()
    {
        // 检查 CraftOutputFactory 是否能把 repair_sniper_rifle 输出为武器
        System.Collections.Generic.IEnumerable<Item> outputs = CraftOutputFactory.Create("repair_sniper_rifle", 1);
        Item first = Assert.Single(outputs);
        Assert.Equal(ItemCategory.Weapon, first.Category);
        Assert.Equal("狙击枪", first.RefKey);
    }
}
