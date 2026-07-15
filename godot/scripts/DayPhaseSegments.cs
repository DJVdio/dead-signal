namespace DeadSignal.Godot;

/// <summary>
/// 🔴 <b>昼夜段分类的唯一事实源</b>——把 8 值 <see cref="DayPhase"/> 派生成「昼夜段」<see cref="PhaseBlock"/>
/// 与一组语义谓词。此前"相位"被劈成三套语义不同的分类（哪几个算夜里 / 哪几个是每日 tick 点 / 哪几个世界冻结），
/// 散在 9+ 处各写各的 inline，「改一处漏一处」正是陷阱产出翻 4 倍那类 bug 的根。现全部收口到本类。
///
/// <para>
/// <b>命名纪律</b>：8 值枚举一律叫「相位」<see cref="DayPhase"/>；3 值的白天/夜晚/聚餐一律叫「昼夜段」<see cref="PhaseBlock"/>。
/// 消费方需要「哪几个相位算 X」时调本类谓词，<b>不要再 inline 抄集合</b>。
/// </para>
///
/// <para>
/// <b>枚举保持 8 值不动</b>：<see cref="DayPhase"/> 必须留 8 值驱动 GameClock 状态机、尸体腐烂「每变一次 +1」的
/// 按相位步进计数（<c>CorpseDecay</c>/<c>CorpseYard</c>——<b>那是按 8 相位步进，不是按昼夜段，别收口进来</b>）、
/// <c>DisplayNames</c>、<see cref="VisionLogic"/>。本类<b>只加派生分类器，不改枚举</b>。
/// </para>
/// </summary>
public static class DayPhaseSegments
{
    /// <summary>
    /// 相位 → 昼夜段（唯一权威 switch）：聚餐段（黎明·黄昏聚餐）/ 夜晚段（NightPrep·NightAct）/ 白天段（其余 4 相）。
    /// <see cref="ShiftSchedule.BlockOf"/> 与所有段判定都从这里派生。
    /// </summary>
    public static PhaseBlock SegmentOf(DayPhase phase) => phase switch
    {
        DayPhase.DawnMeal or DayPhase.DuskMeal => PhaseBlock.Meal,
        DayPhase.NightPrep or DayPhase.NightAct => PhaseBlock.Night,
        _ => PhaseBlock.Day, // DayPrep/DayTravel/DayExplore/DayReturn
    };

    /// <summary>该相位是否属<b>夜晚段</b>（NightPrep·NightAct）。GameClock.IsNight / 视野遮暗 / 环境光皆据此。</summary>
    public static bool IsNight(DayPhase phase) => SegmentOf(phase) == PhaseBlock.Night;

    /// <summary>该相位是否属<b>聚餐段</b>（DawnMeal·DuskMeal）=<b>每日 tick 点</b>（两顿聚餐，一天 2 次）。
    /// 陷阱掷点 / 自动存档 / 饥饿结算 / 角色不重排皆据此——用户口中的「相位」在这些语境里指的正是昼夜段。</summary>
    public static bool IsMeal(DayPhase phase) => SegmentOf(phase) == PhaseBlock.Meal;

    /// <summary>该相位是否属<b>白天段</b>（DayPrep·DayTravel·DayExplore·DayReturn）。</summary>
    public static bool IsDay(DayPhase phase) => SegmentOf(phase) == PhaseBlock.Day;

    /// <summary>
    /// 该相位世界是否<b>冻结</b>（GameClock 置 <c>TimeScale=0</c>）：筹备/回营/夜间部署三个编排·过渡相位 + 两顿聚餐模态。
    /// <para>
    /// 集合 = {DayPrep, DayReturn, NightPrep, DawnMeal, DuskMeal}，等价于「非实时推进相位」——
    /// 真正在走的只有 DayTravel(8×) / DayExplore / NightAct 三个。GameClock 三处冻结判据全部收口到此。
    /// </para>
    /// </summary>
    public static bool IsFrozen(DayPhase phase) => phase is
        DayPhase.DayPrep or DayPhase.DayReturn or DayPhase.NightPrep
        or DayPhase.DawnMeal or DayPhase.DuskMeal;
}
