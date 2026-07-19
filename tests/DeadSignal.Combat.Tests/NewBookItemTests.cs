using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class NewBookItemTests
{
    [Fact]
    public void 西班牙编年史解锁自制手枪且接管自制猎枪和自制霰弹枪()
    {
        var spanish = BookLibrary.SpanishChronicleId;

        RecipeData pistol = RecipeBook.Find("improvised_pistol");
        Assert.NotNull(pistol);
        Assert.Contains(spanish, pistol!.RequiredBookIds);

        RecipeData huntingGun = RecipeBook.Find("improvised_hunting_gun");
        Assert.NotNull(huntingGun);
        Assert.Contains(spanish, huntingGun!.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.FolkChemistryNotesBookId, huntingGun.RequiredBookIds);

        RecipeData shotgun = RecipeBook.Find("improvised_shotgun");
        Assert.NotNull(shotgun);
        Assert.Contains(spanish, shotgun!.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.FolkChemistryNotesBookId, shotgun.RequiredBookIds);
    }

    [Fact]
    public void 被解救的姜戈解锁牙医小手枪牛仔帽和马靴()
    {
        var django = BookLibrary.DjangoUnchainedId;

        RecipeData dentist = RecipeBook.Find("dentist_pistol");
        Assert.NotNull(dentist);
        Assert.Contains(django, dentist!.RequiredBookIds);

        RecipeData hat = RecipeBook.Find("cowboy_hat");
        Assert.NotNull(hat);
        Assert.Contains(django, hat!.RequiredBookIds);

        RecipeData boots = RecipeBook.Find("riding_boots");
        Assert.NotNull(boots);
        Assert.Contains(django, boots!.RequiredBookIds);
    }

    [Fact]
    public void 越南编年史解锁简易装甲()
    {
        var viet = BookLibrary.VietnameseChronicleId;

        RecipeData armor = RecipeBook.Find("simple_armor");
        Assert.NotNull(armor);
        Assert.Contains(viet, armor!.RequiredBookIds);
    }

    [Fact]
    public void 英国编年史接管长弓()
    {
        var british = BookLibrary.BritishChronicleId;

        RecipeData longbow = RecipeBook.Find("longbow");
        Assert.NotNull(longbow);
        Assert.Contains(british, longbow!.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.BowCraftingGuideBookId, longbow.RequiredBookIds);
    }

    [Fact]
    public void 枪械维修指南解锁狙击枪步枪和手枪且都走武器台()
    {
        string guide = BookLibrary.GunsmithRepairGuideId;
        foreach (string id in new[] { "repair_sniper_rifle", "rifle", "pistol" })
        {
            RecipeData recipe = RecipeBook.Find(id)!;
            Assert.NotNull(recipe);
            Assert.Contains(guide, recipe.RequiredBookIds);
            Assert.True(WeaponBench.IsWeaponRecipe(id));
        }
    }

    [Fact]
    public void 全部新增武器追加末尾且入WeaponTable()
    {
        var arsenal = WeaponTable.Arsenal().Select(w => w.Name).ToList();
        Assert.Contains("自制手枪", arsenal);
        Assert.Contains("牙医小手枪", arsenal);
        Assert.Equal(27, arsenal.Count);
        Assert.Equal("牙医小手枪", arsenal[^1]);
    }

    [Fact]
    public void 全部新增护甲入ArmorTable()
    {
        Assert.Equal("牛仔帽", ArmorTable.CowboyHat().Name);
        Assert.Equal("马靴", ArmorTable.RidingBoots().Name);
        Assert.Equal("简易装甲", ArmorTable.SimpleArmor().Name);
    }

    [Fact]
    public void 全部新增护甲登记ApparelCatalog()
    {
        Assert.True(ApparelCatalog.IsApparel("牛仔帽"), "牛仔帽必须在 ApparelCatalog 里");
        Assert.True(ApparelCatalog.IsApparel("马靴"), "马靴必须在 ApparelCatalog 里");
        Assert.True(ApparelCatalog.IsApparel("简易装甲"), "简易装甲必须在 ApparelCatalog 里");
    }

    [Fact]
    public void 全部新增护甲登记ItemRegistry花名册()
    {
        Assert.Contains(ItemRegistry.ArmorRoster, a => a.Name == "牛仔帽");
        Assert.Contains(ItemRegistry.ArmorRoster, a => a.Name == "马靴");
        Assert.Contains(ItemRegistry.ArmorRoster, a => a.Name == "简易装甲");
    }

    [Fact]
    public void 全部新增武器登记重量()
    {
        Assert.True(ItemRegistry.Weapons.ContainsKey("自制手枪"));
        Assert.True(ItemRegistry.Weapons.ContainsKey("牙医小手枪"));
    }

    [Fact]
    public void 六本编年史全部已进入All书目()
    {
        var allIds = BookLibrary.All().Select(b => b.Id).ToHashSet();
        Assert.Contains(BookLibrary.SpanishChronicleId, allIds);
        Assert.Contains(BookLibrary.DjangoUnchainedId, allIds);
        Assert.Contains(BookLibrary.VietnameseChronicleId, allIds);
        Assert.Contains(BookLibrary.BritishChronicleId, allIds);
        Assert.Contains(BookLibrary.ItalianChronicleId, allIds);
    }

    [Fact]
    public void 新增武器不进任何DPS生成套_保Sim零漂移()
    {
        var leatherSetNames = WeaponDps.LeatherArmorSet().Select(a => a.Name).ToList();
        Assert.DoesNotContain("自制手枪", leatherSetNames);
        Assert.DoesNotContain("牙医小手枪", leatherSetNames);
    }
}
