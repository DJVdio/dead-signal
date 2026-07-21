using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class CinematicOverviewTests
{
    [Fact]
    public void CameraRise_IsSlowMonotonicAndSettlesAtBothEnds()
    {
        Assert.Equal(0f, CinematicOverview.EasedProgress(0f, 1f, 4f));
        Assert.Equal(0f, CinematicOverview.EasedProgress(1f, 1f, 4f));
        float quarter = CinematicOverview.EasedProgress(2f, 1f, 4f);
        float middle = CinematicOverview.EasedProgress(3f, 1f, 4f);
        float threeQuarter = CinematicOverview.EasedProgress(4f, 1f, 4f);
        Assert.True(0f < quarter && quarter < middle);
        Assert.Equal(0.5f, middle, 4);
        Assert.True(middle < threeQuarter && threeQuarter < 1f);
        Assert.Equal(1f, CinematicOverview.EasedProgress(5f, 1f, 4f));
    }

    [Fact]
    public void CanyonOverview_IsAVisibleMultiSecondPullUp()
    {
        Assert.InRange(CinematicOverview.CanyonRiseDurationSeconds, 4f, 8f);
        Assert.InRange(CinematicOverview.CanyonTargetZoom, 0.35f, 0.6f);
        Assert.True(CinematicOverview.HordeRiseDurationSeconds > CinematicOverview.CanyonRiseDurationSeconds);
    }
}
