namespace DeadSignal.Godot;

/// <summary>两段正式演出的镜头升高时间曲线；纯逻辑，供 headless 单测钉死节奏。</summary>
public static class CinematicOverview
{
    public const float HordeRiseStartSeconds = 1.4f;
    public const float HordeRiseDurationSeconds = 7.0f;
    public const float CanyonRiseDurationSeconds = 4.8f;
    public const float CanyonTargetZoom = 0.45f;

    /// <summary>把真实经过时间映射到 0..1 的 smoothstep，首尾速度均为零，避免镜头突然起落。</summary>
    public static float EasedProgress(float elapsedSeconds, float startSeconds, float durationSeconds)
    {
        if (durationSeconds <= 0f)
            return elapsedSeconds >= startSeconds ? 1f : 0f;
        float t = System.Math.Clamp((elapsedSeconds - startSeconds) / durationSeconds, 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
