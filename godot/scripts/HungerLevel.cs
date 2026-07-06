namespace DeadSignal.Godot;

/// <summary>
/// 饥饿状态阶梯（用户口径）：正常 → 有点饿 → 饥饿 → 非常饿 → 营养不良 → 饿死。
/// 本版**只记状态**，各级的实际效果（掉血/减益/致死）后续再定。
/// <c>Starved</c> 为终态（饿死），不因进食恢复。
/// </summary>
public enum HungerLevel
{
    Sated,        // 正常
    Peckish,      // 有点饿
    Hungry,       // 饥饿
    VeryHungry,   // 非常饿
    Malnourished, // 营养不良
    Starved,      // 饿死（终态）
}

public static class HungerLevels
{
    /// <summary>中文显示名（UI 用）。</summary>
    public static string Label(this HungerLevel level) => level switch
    {
        HungerLevel.Sated => "正常",
        HungerLevel.Peckish => "有点饿",
        HungerLevel.Hungry => "饥饿",
        HungerLevel.VeryHungry => "非常饿",
        HungerLevel.Malnourished => "营养不良",
        HungerLevel.Starved => "饿死",
        _ => level.ToString(),
    };
}
