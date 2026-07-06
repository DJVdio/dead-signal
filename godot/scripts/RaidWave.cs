using System;

namespace DeadSignal.Godot;

/// <summary>
/// 袭营波次规模计算（纯逻辑、无 Godot 依赖，可 Link 进单测）。
/// 规模随天数与营地规模递增，再乘叙事事件传入的强度系数 <paramref name="intensity"/>。
/// 数值全部"拟定待调"占位——用于走通防御战流程，正式曲线待用 Sim 校准。
/// </summary>
public static class RaidWave
{
    public const float Base = 3f;          // 基础波次规模
    public const float DayFactor = 0.6f;   // 每过一天的增量
    public const float CampFactor = 1.0f;  // 每名在营幸存者的增量（营地越大越招祸）
    public const int MinCount = 1;
    public const int MaxCount = 40;        // 规模封顶（防后期天数把数值拉爆）

    /// <summary>
    /// 计算一次袭营的丧尸数量。
    /// </summary>
    /// <param name="day">当前天数（≥1）。</param>
    /// <param name="campSize">在营幸存者数量。</param>
    /// <param name="intensity">叙事事件强度系数（1=常规；&gt;1 加剧，&lt;1 减弱）。</param>
    public static int ZombieCount(int day, int campSize, float intensity = 1f)
    {
        day = Math.Max(1, day);
        campSize = Math.Max(0, campSize);
        intensity = Math.Max(0f, intensity);

        float raw = (Base + day * DayFactor + campSize * CampFactor) * intensity;
        int count = (int)Math.Ceiling(raw);
        return Math.Clamp(count, MinCount, MaxCount);
    }
}
