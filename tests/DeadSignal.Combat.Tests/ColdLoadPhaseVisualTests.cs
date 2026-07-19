using System.Runtime.CompilerServices;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 冷启动读档的相位视觉接线护栏。CampMain 引 Godot 类型，不能 Link 进纯逻辑测试程序集，
/// 因此这里沿用项目既有的源码守卫范式，钉死「Restore 后只刷新视觉」而非广播重玩法相位事件。
/// </summary>
public sealed class ColdLoadPhaseVisualTests
{
    [Fact]
    public void 读档恢复时钟后只调用相位视觉再入点()
    {
        string save = Source("godot/scripts/CampMain.Save.cs");

        int restore = save.IndexOf("_clock.Restore(", StringComparison.Ordinal);
        int refresh = save.IndexOf("RefreshPhaseVisuals(_clock.CurrentPhase);", restore, StringComparison.Ordinal);

        Assert.True(restore >= 0, "ApplySave 必须先恢复 GameClock。\n");
        Assert.True(refresh > restore, "GameClock.Restore 后必须显式刷新相位视觉。\n");
        Assert.DoesNotContain("OnGamePhaseChanged(_clock.CurrentPhase)", save, StringComparison.Ordinal);
        Assert.DoesNotContain("_clock.TransitionTo(s.World.Phase)", save, StringComparison.Ordinal);
    }

    [Fact]
    public void 相位视觉再入点只改环境色与遮暗开关()
    {
        string visuals = Source("godot/scripts/CampMain.PhaseVisuals.cs");

        Assert.Contains("_ambient.Color = _clock.CurrentAmbientColor();", visuals, StringComparison.Ordinal);
        Assert.Contains("_campVisionMask?.SetEnabled(DayPhaseSegments.IsNight(phase));", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGamePhaseChanged", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("TransitionTo", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginMeal", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("AdvanceSurvivorsHealthDay", visuals, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadExplorationLevel", visuals, StringComparison.Ordinal);
    }

    private static string Source(string relativePath, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
        return File.ReadAllText(Path.Combine(root, relativePath));
    }
}
