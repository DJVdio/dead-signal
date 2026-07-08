namespace DeadSignal.Godot;

/// <summary>
/// 开局起始相位决策纯逻辑（无 Godot 依赖，仅用 Godot-free 的 DayPhase 枚举）。
/// 用户拍板：「游戏应当从第一天晚上开始」——由数据 daynight.json 的 startAtNight 驱动：
///  - startAtNight=true  ⇒ 首日直接进 NightAct 夜晚推进（不走 NightPrep 编排面板）。
///    NightAct 相位转换不会自增 Day（只有 TransitionTo(DayPrep) 会 +1），故需显式把首日置 1，
///    否则 HUD 会显示「第 0 天」。
///  - startAtNight=false ⇒ 保持原白天编排：进 DayPrep，由 TransitionTo(DayPrep) 把 Day 从 0 自增到 1。
/// </summary>
public static class StartupPhaseLogic
{
    public readonly struct Decision
    {
        /// <summary>首日应进入的起始相位。</summary>
        public readonly DayPhase Phase;

        /// <summary>
        /// 是否需在进入起始相位前显式把 Day 置 1。
        /// NightAct 分支为 true（该相位不自增 Day）；DayPrep 分支为 false（由相位自增负责 0→1）。
        /// </summary>
        public readonly bool SetDayToOne;

        public Decision(DayPhase phase, bool setDayToOne)
        {
            Phase = phase;
            SetDayToOne = setDayToOne;
        }
    }

    public static Decision Resolve(bool startAtNight)
        => startAtNight
            ? new Decision(DayPhase.NightAct, setDayToOne: true)
            : new Decision(DayPhase.DayPrep, setDayToOne: false);
}
