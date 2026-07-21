namespace DeadSignal.Godot;

/// <summary>
/// 白天/黑夜两个玩法相位内部的流程节点。历史类型名为 <c>DayPhase</c>，为保持存档与调用兼容暂不改名；
/// 枚举值本身只是流程节点。相位归属一律由 <see cref="DayPhaseSegments.PhaseOf"/> 判定。
/// </summary>
public enum DayPhase
{
    DawnMeal,
    DayPrep,
    DayTravel,
    DayExplore,
    DayReturn,
    DuskMeal,
    NightPrep,
    NightAct
}
