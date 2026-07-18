namespace DeadSignal.Godot;

public sealed partial class CampMain
{
    /// <summary>
    /// 只刷新当前相位的营地表现：环境色与夜间视野遮暗。
    /// <para>
    /// 这是可安全重入的视觉边界，专供自然相位切换和读档恢复共用；
    /// 不得在此结算聚餐、健康日、陷阱或关卡加载，也不得触发相位事件。
    /// </para>
    /// </summary>
    private void RefreshPhaseVisuals(DayPhase phase)
    {
        _ambient.Color = _clock.CurrentAmbientColor();
        _campVisionMask?.SetEnabled(DayPhaseSegments.IsNight(phase));
    }
}
