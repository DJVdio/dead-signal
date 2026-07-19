namespace DeadSignal.Godot;

// 纯规则，不引 Godot 类型；消费层（CampMain.Sofa）只负责把它落到场上。

/// <summary>
/// 玩家可制作、可摆放的<b>沙发</b>规格。沙发是木椅的升级档：仍是非实心、只能摆在室内的座位，
/// 但坐着读书时读速 ×1.12，并把下一次昼夜健康恢复速度 ×1.09。
/// </summary>
public static class SofaSpec
{
    /// <summary>沙发配方 id（<see cref="RecipeBook"/>）。</summary>
    public const string RecipeId = "sofa";

    /// <summary>库存物品 key（配方产物；摆放时从库存扣一件）。</summary>
    public const string ItemKey = "sofa";

    /// <summary>场上家具类型名（实例名为“沙发#1”等）。</summary>
    public const string FurnitureKey = "沙发";

    /// <summary>库存与 Wiki 使用的玩家可见描述。</summary>
    public const string ItemDescription =
        "一张沙发。木椅的升级版——坐下来读书快一点，骨头也好得快一点。";

    /// <summary>坐在沙发上读书的读速乘子（用户拍板：额外 12%）。</summary>
    public const double ReadingSpeedMultiplier = 1.12;

    /// <summary>坐在沙发上读书者的恢复速度乘子（用户拍板：提升 9%）。</summary>
    public const double RecoverySpeedMultiplier = 1.09;

    /// <summary>
    /// 由沙发座位占用比例折算的有效恢复乘子。占用比例 0..1（全夜坐满 = 1.0 ⇒ ×1.09）。
    /// 纯函数，无 Godot 依赖。
    /// </summary>
    public static double EffectiveHealMultiplier(double sofaFraction)
        => 1.0 + (RecoverySpeedMultiplier - 1.0) * System.Math.Clamp(sofaFraction, 0.0, 1.0);

    /// <summary>一张沙发的占地（世界像素，拟定待调）。</summary>
    public const float Width = 96f;

    /// <summary>一张沙发的占地高度（世界像素，拟定待调）。</summary>
    public const float Height = 48f;

    /// <summary>沙发不建碰撞体，人可以跨过去。</summary>
    public const bool IsSolid = false;

    /// <summary>沙发不挖导航洞，不能构成 kill box。</summary>
    public const bool CarvesNavHole = false;

    /// <summary>沙发是家具：只能摆在建筑内，且遵守围栏/大门禁建带。</summary>
    public static PlaceableSpec PlaceSpec => new(FurnitureKey, Width, Height, IsSolid);

    /// <summary>玩家摆下的沙发实例名判定。</summary>
    public static bool IsSofaFurniture(string? furnitureName)
        => furnitureName is not null && furnitureName.StartsWith(FurnitureKey + "#", System.StringComparison.Ordinal);
}
