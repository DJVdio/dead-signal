using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

public sealed class PawnRoleManager
{
    private readonly List<Pawn> _allPawns;
    private readonly GameClock _clock;

    public HashSet<int> ExpeditionIds { get; } = new();
    public Dictionary<int, int> GuardAssignments { get; } = new();

    /// <summary>夜间读书指派（pawnId → bookId）。与守卫互斥：同人若两处都在，守卫优先（读书让位）。仅 NightAct 生效。</summary>
    public Dictionary<int, string> ReadingAssignments { get; } = new();

    public event Action? RolesChanged;

    public PawnRoleManager(List<Pawn> allPawns, GameClock clock)
    {
        _allPawns = allPawns;
        _clock = clock;
        _clock.OnPhaseChanged += OnPhaseChanged;
        ApplyPhase(clock.CurrentPhase);
    }

    public void SetExpeditionIds(HashSet<int> ids)
    {
        ExpeditionIds.Clear();
        foreach (var id in ids)
            ExpeditionIds.Add(id);
        ApplyPhase(_clock.CurrentPhase);
    }

    public void SetGuardAssignments(Dictionary<int, int> assignments)
    {
        GuardAssignments.Clear();
        foreach (var kv in assignments)
            GuardAssignments[kv.Key] = kv.Value;
        ApplyPhase(_clock.CurrentPhase);
    }

    public void SetReadingAssignments(Dictionary<int, string> assignments)
    {
        ReadingAssignments.Clear();
        foreach (var kv in assignments)
            ReadingAssignments[kv.Key] = kv.Value;
        ApplyPhase(_clock.CurrentPhase);
    }

    private void OnPhaseChanged(DayPhase phase) => ApplyPhase(phase);

    private void ApplyPhase(DayPhase phase)
    {
        // 聚餐为过渡模态，不重排角色：保留前一相位的探险队/守卫身份。
        // 尤其 DuskMeal 夹在 DayReturn 与睡眠过渡之间，若在此清空 Expedition 身份，
        // 聚餐后的 CampMain.StartSleepTransition 就筛不到探险队、无人走床位。
        if (phase is DayPhase.DawnMeal or DayPhase.DuskMeal)
            return;

        foreach (var p in _allPawns)
            p.Role = PawnRole.Idle;

        switch (phase)
        {
            case DayPhase.DayPrep:
                break;
            case DayPhase.DayTravel:
            case DayPhase.DayExplore:
                if (ExpeditionIds.Count == 0)
                    break;
                foreach (var p in _allPawns)
                    p.Role = ExpeditionIds.Contains(p.Id) ? PawnRole.Expedition : PawnRole.Sleeping;
                break;
            case DayPhase.DayReturn:
                // 探险队成员保留 Expedition 角色，交给 CampMain.StartSleepTransition
                // 播完“走到床位”过渡动画后再由它设 Sleeping；留守者直接 Sleeping。
                // 注意：顶部循环已把所有 Role 重置为 Idle，因此不能靠 p.Role 判断谁是探险队
                // （那会让守卫恒真、探险队被立刻抹成 Sleeping）——必须用 ExpeditionIds 这个真实身份源。
                foreach (var p in _allPawns)
                    p.Role = ExpeditionIds.Contains(p.Id) ? PawnRole.Expedition : PawnRole.Sleeping;
                ExpeditionIds.Clear();
                break;
            case DayPhase.NightPrep:
                break;
            case DayPhase.NightAct:
                // 守卫与读书同为夜间指派角色（一人一个）：守卫优先，剩下被指派读书者置 Reading。
                var guarded = new HashSet<int>(GuardAssignments.Values);
                foreach (var p in _allPawns)
                {
                    if (guarded.Contains(p.Id))
                        p.Role = PawnRole.Guard;
                    else if (ReadingAssignments.ContainsKey(p.Id))
                        p.Role = PawnRole.Reading;
                }
                break;
        }

        RolesChanged?.Invoke();
    }
}
