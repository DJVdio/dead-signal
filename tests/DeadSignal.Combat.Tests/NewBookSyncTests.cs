using System;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class NewBookSyncTests
{
    [Fact]
    public void Wiki新增的十六本书_全部进入目录且文案完整()
    {
        string[] ids =
        {
            BookLibrary.StreetFightingGuideId,
            BookLibrary.MedicalFacilityStandardsId,
            BookLibrary.EssenceOfSalesId,
            BookLibrary.HundredFamilyRecipesId,
            BookLibrary.LittleMouseDigsHolesId,
            BookLibrary.ItalianChronicleId,
            BookLibrary.FamilyFirstAidManualId,
            BookLibrary.SharpesAutobiographyId,
            BookLibrary.SpanishChronicleId,
            BookLibrary.DjangoUnchainedId,
            BookLibrary.IrishChronicleId,
            BookLibrary.BritishChronicleId,
            BookLibrary.VietnameseChronicleId,
            BookLibrary.StoneBreakingGuideId,
            BookLibrary.MalayChronicleId,
            BookLibrary.TurkishChronicleId,
        };

        var books = BookLibrary.All().ToDictionary(b => b.Id);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (string id in ids)
        {
            BookData book = books[id];
            Assert.False(string.IsNullOrWhiteSpace(book.Title));
            Assert.False(string.IsNullOrWhiteSpace(book.Body));
            Assert.False(string.IsNullOrWhiteSpace(book.Description));
            Assert.True(book.ReadHours > 0);
            Assert.DoesNotContain("待补", book.Body);
        }
    }

    [Fact]
    public void 街头格斗指南_只增强棍棒和匕首命中头部躯干的伤害()
    {
        Func<string, bool> read = id => id == BookLibrary.StreetFightingGuideId;
        Assert.Equal(1.15, BookPassiveEffects.DamageMultiplier("棍棒", BodyMacroRegion.Head, read), 9);
        Assert.Equal(1.15, BookPassiveEffects.DamageMultiplier("匕首", BodyMacroRegion.Torso, read), 9);
        Assert.Equal(1.0, BookPassiveEffects.DamageMultiplier("棍棒", BodyMacroRegion.Leg, read), 9);
        Assert.Equal(1.0, BookPassiveEffects.DamageMultiplier("短剑", BodyMacroRegion.Head, read), 9);
        Assert.Equal(1.0, BookPassiveEffects.DamageMultiplier("棍棒", BodyMacroRegion.Head, _ => false), 9);
    }

    [Fact]
    public void 两本全营地书_只要任一人读过且多人不叠加()
    {
        Assert.Equal(0.92, BookPassiveEffects.CampInfectionChanceMultiplier(new[] { false, true, true }), 9);
        Assert.Equal(1.0, BookPassiveEffects.CampInfectionChanceMultiplier(new[] { false, false }), 9);
        Assert.Equal(0.05, BookPassiveEffects.SnareBaseChanceBonus(new[] { true, true }), 9);
        Assert.Equal(0.0, BookPassiveEffects.SnareBaseChanceBonus(Array.Empty<bool>()), 9);
    }

    [Fact]
    public void 生活类书籍被动_按Wiki数字生效且未读零回归()
    {
        Assert.Equal(1.03, BookPassiveEffects.SellPriceMultiplier(id => id == BookLibrary.EssenceOfSalesId), 9);
        Assert.Equal(1, BookPassiveEffects.FoodCaloriesReduction(id => id == BookLibrary.HundredFamilyRecipesId));
        Assert.Equal(1.25, BookPassiveEffects.SurgerySpeedMultiplier(id => id == BookLibrary.FamilyFirstAidManualId), 9);
        Assert.Equal(12, BookPassiveEffects.PotatoGrowHoursReduction(id => id == BookLibrary.IrishChronicleId));

        Assert.Equal(1.0, BookPassiveEffects.SellPriceMultiplier(_ => false), 9);
        Assert.Equal(0, BookPassiveEffects.FoodCaloriesReduction(_ => false));
        Assert.Equal(1.0, BookPassiveEffects.SurgerySpeedMultiplier(_ => false), 9);
        Assert.Equal(0, BookPassiveEffects.PotatoGrowHoursReduction(_ => false));
    }

    [Fact]
    public void 意大利编年史只解锁弩盾改装()
    {
        Assert.False(BookPassiveEffects.WeaponModUnlocked("crossbow_shield", _ => false));
        Assert.True(BookPassiveEffects.WeaponModUnlocked("crossbow_shield",
            id => id == BookLibrary.ItalianChronicleId));
        Assert.True(BookPassiveEffects.WeaponModUnlocked("bayonet", _ => false));
    }

    [Fact]
    public void 战斗类书籍被动_严格限制武器和持握条件()
    {
        Func<string, bool> sharpe = id => id == BookLibrary.SharpesAutobiographyId;
        Assert.Equal(0.10, BookPassiveEffects.MeleeDodgeChance("短剑", sharpe), 9);
        Assert.Equal(0.10, BookPassiveEffects.MeleeDodgeChance("刺剑", sharpe), 9);
        Assert.Equal(0.0, BookPassiveEffects.MeleeDodgeChance("长剑", sharpe), 9);

        Func<string, bool> stone = id => id == BookLibrary.StoneBreakingGuideId;
        Assert.Equal(0.50, BookPassiveEffects.NaturalBluntDamageMultiplier(true, 0.05, stone), 9);
        Assert.Equal(1.0, BookPassiveEffects.NaturalBluntDamageMultiplier(true, 0.15, stone), 9);
        Assert.Equal(1.0, BookPassiveEffects.NaturalBluntDamageMultiplier(false, 0.05, stone), 9);

        Func<string, bool> malay = id => id == BookLibrary.MalayChronicleId;
        Assert.Equal(0.80, BookPassiveEffects.DualSharpAttackSpeedMultiplier(true, malay), 9);
        Assert.Equal(0.70, BookPassiveEffects.DualSharpAttackSpeedMultiplier(false, malay), 9);

        Func<string, bool> turkish = id => id == BookLibrary.TurkishChronicleId;
        Assert.Equal(0.90, BookPassiveEffects.MixedGripRangedSpreadMultiplier(true, turkish), 9);
        Assert.Equal(1.10, BookPassiveEffects.MixedGripMeleePenetrationMultiplier(true, turkish), 9);
        Assert.Equal(1.0, BookPassiveEffects.MixedGripRangedSpreadMultiplier(false, turkish), 9);
        Assert.Equal(1.0, BookPassiveEffects.MixedGripMeleePenetrationMultiplier(false, turkish), 9);
    }
}
