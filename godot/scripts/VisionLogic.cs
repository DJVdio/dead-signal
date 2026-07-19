using System;
using System.Numerics;

namespace DeadSignal.Godot;

/// <summary>
/// 光照与视野纯逻辑（零 Godot 依赖，Link 进 DeadSignal.Combat.Tests）。批次4 API 权威源。
///
/// 模型：
///  - 环境光 <see cref="AmbientLight"/>：昼夜相位 → 基础光照 L∈[0,1]（室内无窗恒暗豁免相位）。
///  - 光源贡献 <see cref="SourceContribution"/>：单个光源按距离线性衰减的贡献（供调用方逐光源算，取最强）。
///  - 合成 <see cref="CombineLight"/>：局部光照 = max(环境光, 最强光源贡献)，clamp[0,1]。
///  - 锥形 <see cref="ConeFor"/>：L 越低 → 视距越短 + 锥角越窄；当前端点以 Wiki 配置为准。
///  - 判定 <see cref="CanSee"/>：occluded 短路 → 视距 → 半角，三重与。occluded 由 Godot 层 raycast 传入。
///  - 暴露代价 <see cref="ExposureRangeMultiplier"/>：黑暗中持光源者被他人发现的距离加成系数。
///
/// 空间执行（raycast 遮挡采样/遮暗渲染/揭示隐藏）归 Godot 运行时层，本类只出纯函数。
/// 坐标用 <see cref="System.Numerics.Vector2"/>（非 Godot.Vector2）以保零依赖；消费方转换即可。
/// 当前可调数值以 Wiki 配置为准；公式与空间判定保留在本文件。
/// </summary>
public static class VisionLogic
{
    // ── 环境光基线（当前值以 Wiki 配置为准）─────────────────────────────────
    /// <summary>白昼相位环境光（满档）。</summary>
    public const float DaylightAmbient = 1.0f;
    /// <summary>黎明/黄昏聚餐相位环境光（暮光）。</summary>
    public const float TwilightAmbient = 0.45f;
    /// <summary>夜间相位环境光（有微弱月光，非全黑）。</summary>
    public const float NightAmbient = 0.15f;
    /// <summary>室内无窗恒暗区域环境光（关卡标记，压过相位）。</summary>
    public const float IndoorsDarkAmbient = 0.10f;

    // ── 锥形曲线端点（当前值以 Wiki 配置为准）────────────────────────────────
    /// <summary>白天满档视距 R0（默认基准；ConeFor 重载可传自定义 baseRange 缩放）。</summary>
    public const float BaseRange = 300f;
    /// <summary>全黑时视距相对 R0 的下限系数。</summary>
    public const float DarkRangeFactor = 0.35f;
    /// <summary>白天满档锥形半角（全角约 120°）。</summary>
    public const float DayHalfAngleDeg = 60f;
    /// <summary>全黑时锥形半角（全角约 60°）。</summary>
    public const float DarkHalfAngleDeg = 30f;

    // ── 暴露代价 ─────────────────────────────────────────────────
    /// <summary>全黑中持满强度光源时，被发现距离的最大加成，当前值以 Wiki 配置为准。
    /// **历史报告/非配置源**：下方曾保留旧火把亮度与校准推算，仅用于追溯调参背景，不代表当前数值。</summary>
    public const float MaxExposureBonus = 0.7f;

    private const float Epsilon = 1e-4f;
    private const float RadToDeg = 180f / MathF.PI;

    /// <summary>朝向 + 视距 + 半角构成的视野锥。</summary>
    public readonly struct VisionCone
    {
        /// <summary>视距（世界单位）。</summary>
        public float Range { get; }
        /// <summary>半张角（度）；全张角 = 2×HalfAngleDeg。</summary>
        public float HalfAngleDeg { get; }

        public VisionCone(float range, float halfAngleDeg)
        {
            Range = range;
            HalfAngleDeg = halfAngleDeg;
        }

        /// <summary>
        /// 按角色个体系数缩放：视距×rangeMult、半角×angleMult（供角色技能接入；具体倍率以 Wiki 配置为准）。
        /// 与光照/基准 R0 正交——先 <see cref="ConeFor(float,float)"/> 出基础锥，再 .Scaled 叠个体系数。
        /// Range 下限 0；半角 clamp[0,180]（180 = 全向）。
        /// </summary>
        public VisionCone Scaled(float rangeMult, float angleMult = 1f)
        {
            float range = Math.Max(0f, Range * rangeMult);
            float half = Math.Clamp(HalfAngleDeg * angleMult, 0f, 180f);
            return new VisionCone(range, half);
        }
    }

    /// <summary>
    /// 昼夜相位 → 环境光 L∈[0,1]。indoorsDark（室内无窗关卡标记）为真则恒暗，压过相位。
    /// </summary>
    public static float AmbientLight(DayPhase phase, bool indoorsDark)
    {
        if (indoorsDark)
            return IndoorsDarkAmbient;

        // 🔴 昼夜段分类走唯一事实源 DayPhaseSegments，不再 inline 抄相位集合（白天=满档 / 聚餐=暮光 / 夜晚=微月）。
        return DayPhaseSegments.SegmentOf(phase) switch
        {
            PhaseBlock.Meal => TwilightAmbient,
            PhaseBlock.Night => NightAmbient,
            _ => DaylightAmbient, // PhaseBlock.Day
        };
    }

    /// <summary>
    /// 单个光源在给定距离处的光照贡献：距离 0 处为满强度 intensity，达到 radius 及以外为 0，中间线性衰减。
    /// 调用方对每个光源各算一次，取最强传给 <see cref="CombineLight"/>。返回值 clamp[0,1]，随距离单调不增。
    /// </summary>
    public static float SourceContribution(float intensity, float distance, float radius)
    {
        float clampedIntensity = Math.Clamp(intensity, 0f, 1f);
        if (clampedIntensity <= 0f)
            return 0f;
        if (radius <= 0f)
            return distance <= Epsilon ? clampedIntensity : 0f;

        float t = 1f - Math.Max(0f, distance) / radius;
        if (t <= 0f)
            return 0f;
        if (t > 1f)
            t = 1f;
        return clampedIntensity * t;
    }

    /// <summary>局部光照合成：max(环境光, 最强光源贡献)，clamp[0,1]。</summary>
    public static float CombineLight(float ambient, float strongestSourceContribution)
    {
        float l = Math.Max(ambient, strongestSourceContribution);
        return Math.Clamp(l, 0f, 1f);
    }

    /// <summary>局部光照 L → 视野锥（默认基准 <see cref="BaseRange"/>）。</summary>
    public static VisionCone ConeFor(float lightLevel) => ConeFor(lightLevel, BaseRange);

    /// <summary>
    /// 局部光照 L → 视野锥，视距按 baseRange 缩放（不同观察者可传各自满档视距，如丧尸/袭击者/玩家）。
    /// L=1 → (baseRange, DayHalfAngleDeg)；L=0 → (baseRange×DarkRangeFactor, DarkHalfAngleDeg)；中间线性。
    /// </summary>
    public static VisionCone ConeFor(float lightLevel, float baseRange)
    {
        float l = Math.Clamp(lightLevel, 0f, 1f);
        float rangeFactor = Lerp(DarkRangeFactor, 1f, l);
        float range = Math.Max(0f, baseRange) * rangeFactor;
        float halfAngle = Lerp(DarkHalfAngleDeg, DayHalfAngleDeg, l);
        return new VisionCone(range, halfAngle);
    }

    /// <summary>
    /// 观察者能否看见目标。occluded（Godot 层 raycast 判障碍遮蔽）为真直接短路 false；
    /// 否则要求目标落在视距内且落在朝向 ±HalfAngle 的锥内。facing 无需归一化（内部处理）。
    /// 观察者与目标重合（距离≈0）视为可见。
    /// </summary>
    public static bool CanSee(Vector2 observer, Vector2 facing, Vector2 target, VisionCone cone, bool occluded)
    {
        if (occluded)
            return false;

        Vector2 toTarget = target - observer;
        float dist = toTarget.Length();
        if (dist > cone.Range)
            return false;
        if (dist <= Epsilon)
            return true;

        float facingLen = facing.Length();
        if (facingLen <= Epsilon)
            return true; // 无明确朝向 → 视为全向（仅受视距约束）

        float cos = Vector2.Dot(facing / facingLen, toTarget / dist);
        cos = Math.Clamp(cos, -1f, 1f);
        float angleDeg = MathF.Acos(cos) * RadToDeg;
        return angleDeg <= cone.HalfAngleDeg + Epsilon;
    }

    /// <summary>
    /// 暴露代价：黑暗中持光源者被他人发现的距离加成系数（≥1）。
    /// carriedLightIntensity=0（不持光）→ 1.0；越黑（ambient 越低）+ 光越强 → 系数越大，上限 1+<see cref="MaxExposureBonus"/>。
    /// 白昼（ambient=1）持光无加成（返回 1.0，光在白天无暴露意义）。
    /// 调用方用它放大针对持光者的有效侦测半径。
    /// </summary>
    public static float ExposureRangeMultiplier(float ambientLight, float carriedLightIntensity)
    {
        float intensity = Math.Clamp(carriedLightIntensity, 0f, 1f);
        if (intensity <= 0f)
            return 1f;
        float darkness = 1f - Math.Clamp(ambientLight, 0f, 1f);
        return 1f + MaxExposureBonus * intensity * darkness;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
