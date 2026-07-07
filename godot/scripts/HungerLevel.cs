namespace DeadSignal.Godot;

/// <summary>
/// 饥饿刻度（用户口径，数值化 0-6，数值越大越饱）：
/// 0 饿死 / 1 营养不良 / 2 极度饥饿 / 3 饥饿 / 4 有点饿 / 5 正常 / 6 吃撑。
/// 普通角色上限 5（正常）；"大胃袋"特质可把上限提到 6（吃撑，本轮只占位、不给 buff）。
/// 每次昼夜相位切换 -1，吃一餐 +1（餐后 clamp 到该角色的饥饿上限）；到 0 = 饿死。
/// 枚举值即刻度序号（<c>(int)</c> 直接可用），供 UI/快照读取。
/// </summary>
public enum HungerLevel
{
    Starved = 0,      // 饿死（终态）
    Malnourished = 1, // 营养不良
    Ravenous = 2,     // 极度饥饿
    Hungry = 3,       // 饥饿
    Peckish = 4,      // 有点饿
    Sated = 5,        // 正常
    Stuffed = 6,      // 吃撑（占位，本轮无 buff）
}

public static class HungerLevels
{
    /// <summary>中文显示名（UI 用）。</summary>
    public static string Label(this HungerLevel level) => level switch
    {
        HungerLevel.Starved => "饿死",
        HungerLevel.Malnourished => "营养不良",
        HungerLevel.Ravenous => "极度饥饿",
        HungerLevel.Hungry => "饥饿",
        HungerLevel.Peckish => "有点饿",
        HungerLevel.Sated => "正常",
        HungerLevel.Stuffed => "吃撑",
        _ => level.ToString(),
    };
}
