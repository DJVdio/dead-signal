using System.Numerics;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

public class VisionLogicTests
{
    // ── 环境光相位映射 ─────────────────────────────────────────────────────
    [Theory]
    [InlineData(DayPhase.DayPrep)]
    [InlineData(DayPhase.DayTravel)]
    [InlineData(DayPhase.DayExplore)]
    [InlineData(DayPhase.DayReturn)]
    public void AmbientLight_DayPhases_AreFullBright(DayPhase phase)
    {
        Assert.Equal(VisionLogic.DaylightAmbient, VisionLogic.AmbientLight(phase, indoorsDark: false), 3);
    }

    [Theory]
    [InlineData(DayPhase.DawnMeal)]
    [InlineData(DayPhase.DuskMeal)]
    public void AmbientLight_TwilightPhases_AreDimmer(DayPhase phase)
    {
        Assert.Equal(VisionLogic.TwilightAmbient, VisionLogic.AmbientLight(phase, indoorsDark: false), 3);
    }

    [Theory]
    [InlineData(DayPhase.NightPrep)]
    [InlineData(DayPhase.NightAct)]
    public void AmbientLight_NightPhases_AreDarkest(DayPhase phase)
    {
        Assert.Equal(VisionLogic.NightAmbient, VisionLogic.AmbientLight(phase, indoorsDark: false), 3);
    }

    [Fact]
    public void AmbientLight_MonotoneAcrossDayCycle()
    {
        float day = VisionLogic.AmbientLight(DayPhase.DayExplore, false);
        float twilight = VisionLogic.AmbientLight(DayPhase.DuskMeal, false);
        float night = VisionLogic.AmbientLight(DayPhase.NightAct, false);
        Assert.True(day > twilight && twilight > night, "白昼 > 暮光 > 夜间");
    }

    [Fact]
    public void AmbientLight_IndoorsDark_OverridesPhase_AndIsDarkest()
    {
        // 室内无窗恒暗压过任意相位，且不亮于夜间
        float indoorsByDay = VisionLogic.AmbientLight(DayPhase.DayExplore, indoorsDark: true);
        float indoorsByNight = VisionLogic.AmbientLight(DayPhase.NightAct, indoorsDark: true);
        Assert.Equal(VisionLogic.IndoorsDarkAmbient, indoorsByDay, 3);
        Assert.Equal(VisionLogic.IndoorsDarkAmbient, indoorsByNight, 3);
        Assert.True(indoorsByDay <= VisionLogic.NightAmbient);
    }

    // ── 光源距离衰减单调性 ─────────────────────────────────────────────────
    [Fact]
    public void SourceContribution_Endpoints()
    {
        Assert.Equal(0.8f, VisionLogic.SourceContribution(0.8f, distance: 0f, radius: 100f), 3);
        Assert.Equal(0f, VisionLogic.SourceContribution(0.8f, distance: 100f, radius: 100f), 3);
        Assert.Equal(0f, VisionLogic.SourceContribution(0.8f, distance: 150f, radius: 100f), 3);
    }

    [Fact]
    public void SourceContribution_MonotoneDecreasingInDistance()
    {
        float prev = float.PositiveInfinity;
        for (int d = 0; d <= 120; d += 10)
        {
            float c = VisionLogic.SourceContribution(1.0f, d, radius: 100f);
            Assert.True(c <= prev + 1e-5f, $"距离 {d} 贡献 {c} 应不大于前一步 {prev}");
            prev = c;
        }
    }

    [Fact]
    public void SourceContribution_ZeroIntensity_IsZero()
    {
        Assert.Equal(0f, VisionLogic.SourceContribution(0f, 0f, 100f), 5);
    }

    [Fact]
    public void SourceContribution_ClampsIntensityToOne()
    {
        Assert.Equal(1f, VisionLogic.SourceContribution(5f, 0f, 100f), 3);
    }

    // ── 光照合成 ───────────────────────────────────────────────────────────
    [Fact]
    public void CombineLight_TakesMax_AndClamps()
    {
        Assert.Equal(0.7f, VisionLogic.CombineLight(0.2f, 0.7f), 3);
        Assert.Equal(0.5f, VisionLogic.CombineLight(0.5f, 0.1f), 3);
        Assert.Equal(1f, VisionLogic.CombineLight(0.9f, 5f), 3);
        Assert.Equal(0f, VisionLogic.CombineLight(-1f, -1f), 3);
    }

    // ── 锥形曲线端点 ───────────────────────────────────────────────────────
    [Fact]
    public void ConeFor_FullLight_IsMaxRangeAndWidestAngle()
    {
        var cone = VisionLogic.ConeFor(1.0f);
        Assert.Equal(VisionLogic.BaseRange, cone.Range, 2);
        Assert.Equal(VisionLogic.DayHalfAngleDeg, cone.HalfAngleDeg, 2);
    }

    [Fact]
    public void ConeFor_Darkness_IsShortestRangeAndNarrowestAngle()
    {
        var cone = VisionLogic.ConeFor(0.0f);
        Assert.Equal(VisionLogic.BaseRange * VisionLogic.DarkRangeFactor, cone.Range, 2);
        Assert.Equal(VisionLogic.DarkHalfAngleDeg, cone.HalfAngleDeg, 2);
    }

    [Fact]
    public void ConeFor_RangeAndAngle_MonotoneInLight()
    {
        float prevRange = -1f, prevAngle = -1f;
        for (float l = 0f; l <= 1.0001f; l += 0.1f)
        {
            var cone = VisionLogic.ConeFor(l);
            Assert.True(cone.Range >= prevRange - 1e-3f, "视距随光照单调不减");
            Assert.True(cone.HalfAngleDeg >= prevAngle - 1e-3f, "锥角随光照单调不减");
            prevRange = cone.Range;
            prevAngle = cone.HalfAngleDeg;
        }
    }

    [Fact]
    public void ConeFor_ClampsOutOfRangeLight()
    {
        Assert.Equal(VisionLogic.ConeFor(0f).Range, VisionLogic.ConeFor(-2f).Range, 2);
        Assert.Equal(VisionLogic.ConeFor(1f).Range, VisionLogic.ConeFor(9f).Range, 2);
    }

    [Fact]
    public void ConeFor_CustomBaseRange_Scales()
    {
        var cone = VisionLogic.ConeFor(1.0f, baseRange: 220f);
        Assert.Equal(220f, cone.Range, 2);
    }

    // ── VisionCone.Scaled：按角色个体系数（batch5 道格/布鲁斯技能）────────
    [Fact]
    public void Scaled_AppliesRangeAndAngleMultipliers()
    {
        // 布鲁斯：视距 ×1.10；道格：视角 ×1.10（半角同步 ×1.10）
        var cone = VisionLogic.ConeFor(1.0f); // (BaseRange, DayHalfAngleDeg)=(300,60)
        var bruce = cone.Scaled(rangeMult: 1.10f);
        Assert.Equal(VisionLogic.BaseRange * 1.10f, bruce.Range, 2);
        Assert.Equal(VisionLogic.DayHalfAngleDeg, bruce.HalfAngleDeg, 2); // angleMult 默认 1

        var doug = cone.Scaled(rangeMult: 1f, angleMult: 1.10f);
        Assert.Equal(VisionLogic.BaseRange, doug.Range, 2);
        Assert.Equal(VisionLogic.DayHalfAngleDeg * 1.10f, doug.HalfAngleDeg, 2);
    }

    [Fact]
    public void Scaled_ClampsAngleTo180_AndRangeNonNegative()
    {
        var cone = new VisionLogic.VisionCone(range: 100f, halfAngleDeg: 120f);
        var wide = cone.Scaled(rangeMult: 1f, angleMult: 4f); // 480 → clamp 180
        Assert.Equal(180f, wide.HalfAngleDeg, 2);
        var neg = cone.Scaled(rangeMult: -1f);                // 负视距 → 0
        Assert.Equal(0f, neg.Range, 2);
    }

    // ── CanSee：遮挡短路 / 视距 / 角度边界 ─────────────────────────────────
    private static readonly VisionLogic.VisionCone Cone90 = new(range: 100f, halfAngleDeg: 45f);

    [Fact]
    public void CanSee_Occluded_ShortCircuitsFalse_EvenIfInFrontAndClose()
    {
        // 正前方 1 单位、明明在锥内视距内，但被遮挡 → false
        bool seen = VisionLogic.CanSee(
            observer: Vector2.Zero, facing: new Vector2(1, 0),
            target: new Vector2(1, 0), Cone90, occluded: true);
        Assert.False(seen);
    }

    [Fact]
    public void CanSee_InFront_WithinRange_IsVisible()
    {
        bool seen = VisionLogic.CanSee(
            Vector2.Zero, new Vector2(1, 0), new Vector2(50, 0), Cone90, occluded: false);
        Assert.True(seen);
    }

    [Fact]
    public void CanSee_BeyondRange_IsNotVisible()
    {
        bool seen = VisionLogic.CanSee(
            Vector2.Zero, new Vector2(1, 0), new Vector2(150, 0), Cone90, occluded: false);
        Assert.False(seen);
    }

    [Fact]
    public void CanSee_Behind_IsNotVisible()
    {
        bool seen = VisionLogic.CanSee(
            Vector2.Zero, new Vector2(1, 0), new Vector2(-50, 0), Cone90, occluded: false);
        Assert.False(seen);
    }

    [Fact]
    public void CanSee_AngleBoundary_JustInsideVisible_JustOutsideNot()
    {
        // 半角 45°：44° 在锥内、46° 在锥外
        float r = 50f;
        var inside = AngleVec(44f, r);
        var outside = AngleVec(46f, r);
        Assert.True(VisionLogic.CanSee(Vector2.Zero, new Vector2(1, 0), inside, Cone90, false));
        Assert.False(VisionLogic.CanSee(Vector2.Zero, new Vector2(1, 0), outside, Cone90, false));
    }

    [Fact]
    public void CanSee_SamePosition_IsVisible()
    {
        bool seen = VisionLogic.CanSee(
            Vector2.Zero, new Vector2(1, 0), Vector2.Zero, Cone90, occluded: false);
        Assert.True(seen);
    }

    [Fact]
    public void CanSee_ZeroFacing_TreatedAsOmnidirectional()
    {
        // 无朝向时仅受视距约束：背后目标也可见
        bool behind = VisionLogic.CanSee(
            Vector2.Zero, Vector2.Zero, new Vector2(-50, 0), Cone90, occluded: false);
        Assert.True(behind);
        bool far = VisionLogic.CanSee(
            Vector2.Zero, Vector2.Zero, new Vector2(-150, 0), Cone90, occluded: false);
        Assert.False(far);
    }

    // ── 暴露代价 ───────────────────────────────────────────────────────────
    [Fact]
    public void Exposure_NoLight_IsOne()
    {
        Assert.Equal(1f, VisionLogic.ExposureRangeMultiplier(ambientLight: 0.1f, carriedLightIntensity: 0f), 3);
    }

    [Fact]
    public void Exposure_FullDaylight_NoBonusEvenWithLight()
    {
        Assert.Equal(1f, VisionLogic.ExposureRangeMultiplier(ambientLight: 1.0f, carriedLightIntensity: 1.0f), 3);
    }

    [Fact]
    public void Exposure_FullDark_FullLight_IsMaxBonus()
    {
        float m = VisionLogic.ExposureRangeMultiplier(ambientLight: 0.0f, carriedLightIntensity: 1.0f);
        Assert.Equal(1f + VisionLogic.MaxExposureBonus, m, 3);
    }

    [Fact]
    public void Exposure_DarkerAmbient_IncreasesBonus()
    {
        float bright = VisionLogic.ExposureRangeMultiplier(0.6f, 1.0f);
        float dark = VisionLogic.ExposureRangeMultiplier(0.2f, 1.0f);
        Assert.True(dark > bright, "越黑暴露越大");
        Assert.True(bright >= 1f);
    }

    // 单位圆上取 facing=+X 方向偏离 deg 度、长度 r 的目标点
    private static Vector2 AngleVec(float deg, float r)
    {
        float rad = deg * MathF.PI / 180f;
        return new Vector2(MathF.Cos(rad) * r, MathF.Sin(rad) * r);
    }
}
