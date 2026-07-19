using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 纯 C#：武器台身份、固定锚点与配方路由。Godot 侧只负责画实体和寻路。
public static class WeaponBench
{
    public const string FurnitureKey = "武器台";
    public const string RecipeId = "weapon_bench";
    public const string ItemKey = "weapon_bench";
    public const string AbsentGate = "weapon_bench_absent";

    public const float Width = 104f;
    public const float Height = 68f;

    // 车间（空牛棚）内的独立固定锚点；与改装台、两堆草垛均不重叠。
    public const float AnchorX = 1515f;
    public const float AnchorY = 1130f;

    public static PlaceableSpec Spec =>
        new(FurnitureKey, Width, Height, IsSolid: true, AllowedOutdoors: true);

    /// <summary>真正产出武器的配方；弹药与改装不属于武器制造，分别留在工作台/改装台。</summary>
    private static readonly IReadOnlySet<string> WeaponRecipeIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "bone_knife",
        "handmade_bow",
        "improvised_hunting_gun",
        "improvised_shotgun",
        "recurve_bow",
        "longbow",
        "light_crossbow",
        "heavy_crossbow",
        "axe",
        "repair_sniper_rifle",
        "improvised_pistol",
        "dentist_pistol",
        "rifle",
        "pistol",
    };

    public static bool IsWeaponRecipe(string? recipeId)
        => recipeId is not null && WeaponRecipeIds.Contains(recipeId);

    public static IReadOnlySet<string> RecipeIds => WeaponRecipeIds;
}
