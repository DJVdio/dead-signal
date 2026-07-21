namespace DeadSignal.Godot;

/// <summary>
/// 🔴 <b>白天/黑夜两相位的唯一事实源</b>——把内部 <see cref="DayPhase"/> 流程节点映射到 <see cref="DayNightPhase"/>
/// 与一组语义谓词。此前同一组流程节点被误称为多相位，并把聚餐错误建成第三种相位，
/// 散在 9+ 处各写各的 inline，「改一处漏一处」正是陷阱产出翻 4 倍那类 bug 的根。现全部收口到本类。
///
/// <para>
/// <b>命名纪律</b>：玩法相位只有白天/黑夜；历史类型名 <see cref="DayPhase"/> 仅表示状态机流程节点。
/// 聚餐是边界流程事件，不是第三相位。消费方需要判断节点语义时调本类谓词，<b>不要再 inline 抄集合</b>。
/// </para>
///
/// <para>
/// <see cref="DayPhase"/> 的多个枚举值继续驱动筹备、旅行、探索、回营、聚餐与守夜的先后流程；
/// 它们不是额外玩法相位。尸体腐烂等世界结算只在白天/黑夜边界推进。
/// </para>
/// </summary>
public static class DayPhaseSegments
{
    /// <summary>
    /// 流程节点 → 白天/黑夜（唯一权威 switch）：黄昏聚餐开启黑夜，清晨聚餐开启白天。
    /// <see cref="ShiftSchedule.PhaseOf"/> 与所有段判定都从这里派生。
    /// </summary>
    public static DayNightPhase PhaseOf(DayPhase phase) => phase switch
    {
        DayPhase.DuskMeal or DayPhase.NightPrep or DayPhase.NightAct => DayNightPhase.Night,
        _ => DayNightPhase.Day, // DawnMeal/DayPrep/DayTravel/DayExplore/DayReturn
    };

    /// <summary>该流程节点是否属<b>黑夜相位</b>（DuskMeal·NightPrep·NightAct）。GameClock.IsNight / 视野遮暗 / 环境光皆据此。</summary>
    public static bool IsNight(DayPhase phase) => PhaseOf(phase) == DayNightPhase.Night;

    /// <summary>该流程节点是否为聚餐边界事件。聚餐不是第三相位；它分别开启白天与黑夜。</summary>
    public static bool IsMeal(DayPhase phase) => phase is DayPhase.DawnMeal or DayPhase.DuskMeal;

    /// <summary>该流程节点是否属于白天相位（含清晨聚餐）。</summary>
    public static bool IsDay(DayPhase phase) => PhaseOf(phase) == DayNightPhase.Day;

    /// <summary>
    /// 该流程节点的世界是否<b>冻结</b>（GameClock 置 <c>TimeScale=0</c>）：筹备/回营/夜间部署三个编排·过渡流程 + 两顿聚餐模态。
    /// <para>
    /// 集合 = {DayPrep, DayReturn, NightPrep, DawnMeal, DuskMeal}，等价于「非实时推进流程节点」——
    /// 真正在走的只有 DayTravel(8×) / DayExplore / NightAct 三个。GameClock 三处冻结判据全部收口到此。
    /// </para>
    /// </summary>
    public static bool IsFrozen(DayPhase phase) => phase is
        DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep
        or DayPhase.DawnMeal or DayPhase.DuskMeal;
}
