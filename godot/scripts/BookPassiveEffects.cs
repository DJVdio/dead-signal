using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 纯逻辑：不得引 Godot 类型。书籍被动集中登记，调用方只传“谁读过书”和当次判定上下文。
public static class BookPassiveEffects
{
    public const double StreetFightingDamageMultiplier = 1.15;
    public const double MedicalFacilityInfectionMultiplier = 0.92;
    public const double SalesPriceMultiplier = 1.03;
    public const int FoodCaloriesReductionValue = 1;
    public const double SnareBaseChanceBonusValue = 0.05;
    public const double SurgerySpeedMultiplierValue = 1.25;
    public const double SharpesMeleeDodgeChance = 0.10;
    public const double StoneBreakingTriggerChance = 0.10;
    public const double StoneBreakingDamageMultiplier = 0.50;
    public const double MalayDualSharpAttackSpeedMultiplier = 0.80;
    public const double DefaultDualWieldAttackSpeedMultiplier = 0.70;
    public const double TurkishRangedSpreadMultiplier = 0.90;
    public const double TurkishMeleePenetrationMultiplier = 1.10;
    public const int IrishPotatoGrowHoursReduction = 12;

    private static bool Read(Func<string, bool>? isBookRead, string id)
        => isBookRead?.Invoke(id) == true;

    public static double DamageMultiplier(string weaponName, BodyMacroRegion hitRegion, Func<string, bool>? isBookRead)
    {
        bool rightWeapon = weaponName is "棍棒" or "匕首";
        bool rightRegion = hitRegion is BodyMacroRegion.Head or BodyMacroRegion.Torso;
        return rightWeapon && rightRegion && Read(isBookRead, BookLibrary.StreetFightingGuideId)
            ? StreetFightingDamageMultiplier
            : 1.0;
    }

    public static double CampInfectionChanceMultiplier(IEnumerable<bool>? readers)
        => readers?.Any(x => x) == true ? MedicalFacilityInfectionMultiplier : 1.0;

    public static double SnareBaseChanceBonus(IEnumerable<bool>? readers)
        => readers?.Any(x => x) == true ? SnareBaseChanceBonusValue : 0.0;

    public static double SellPriceMultiplier(Func<string, bool>? isBookRead)
        => Read(isBookRead, BookLibrary.EssenceOfSalesId) ? SalesPriceMultiplier : 1.0;

    public static int FoodCaloriesReduction(Func<string, bool>? isBookRead)
        => Read(isBookRead, BookLibrary.HundredFamilyRecipesId) ? FoodCaloriesReductionValue : 0;

    public static double SurgerySpeedMultiplier(Func<string, bool>? isBookRead)
        => Read(isBookRead, BookLibrary.FamilyFirstAidManualId) ? SurgerySpeedMultiplierValue : 1.0;

    public static int PotatoGrowHoursReduction(Func<string, bool>? isBookRead)
        => Read(isBookRead, BookLibrary.IrishChronicleId) ? IrishPotatoGrowHoursReduction : 0;

    /// <summary>意大利编年史解锁弩盾改装；其它改装保持原有零书门槛。</summary>
    public static bool WeaponModUnlocked(string modId, Func<string, bool>? isBookRead)
        => modId != "crossbow_shield" || Read(isBookRead, BookLibrary.ItalianChronicleId);

    public static double MeleeDodgeChance(string weaponName, Func<string, bool>? isBookRead)
        => weaponName is "短剑" or "刺剑" && Read(isBookRead, BookLibrary.SharpesAutobiographyId)
            ? SharpesMeleeDodgeChance
            : 0.0;

    public static double NaturalBluntDamageMultiplier(bool isNaturalBlunt, double roll, Func<string, bool>? isBookRead)
        => isNaturalBlunt
           && roll < StoneBreakingTriggerChance
           && Read(isBookRead, BookLibrary.StoneBreakingGuideId)
            ? StoneBreakingDamageMultiplier
            : 1.0;

    public static double DualSharpAttackSpeedMultiplier(bool bothWeaponsAreSharp, Func<string, bool>? isBookRead)
        => bothWeaponsAreSharp && Read(isBookRead, BookLibrary.MalayChronicleId)
            ? MalayDualSharpAttackSpeedMultiplier
            : DefaultDualWieldAttackSpeedMultiplier;

    public static double MixedGripRangedSpreadMultiplier(bool holdsMeleeAndRanged, Func<string, bool>? isBookRead)
        => holdsMeleeAndRanged && Read(isBookRead, BookLibrary.TurkishChronicleId)
            ? TurkishRangedSpreadMultiplier
            : 1.0;

    public static double MixedGripMeleePenetrationMultiplier(bool holdsMeleeAndRanged, Func<string, bool>? isBookRead)
        => holdsMeleeAndRanged && Read(isBookRead, BookLibrary.TurkishChronicleId)
            ? TurkishMeleePenetrationMultiplier
            : 1.0;
}
