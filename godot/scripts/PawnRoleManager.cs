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
    public HashSet<int> ProductionAssignments { get; } = new();

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

    public void SetProductionAssignments(IEnumerable<int> pawnIds)
    {
        ProductionAssignments.Clear();
        foreach (int id in pawnIds ?? Enumerable.Empty<int>())
            ProductionAssignments.Add(id);
        ApplyPhase(_clock.CurrentPhase);
    }

    private void OnPhaseChanged(DayPhase phase) => ApplyPhase(phase);

    private void ApplyPhase(DayPhase phase)
    {
        // 聚餐为过渡模态，不重排角色：保留前一相位的探险队/守卫身份。
        // 尤其 DuskMeal 夹在 DayReturn 与睡眠过渡之间，若在此清空 Expedition 身份，
        // 聚餐后的 CampMain.StartSleepTransition 就筛不到探险队、无人走床位。
        if (DayPhaseSegments.IsMeal(phase))
            return;

        foreach (var p in _allPawns)
            p.Role = PawnRole.Idle;

        switch (phase)
        {
            case DayPhase.DayPrep:
                foreach (var p in _allPawns)
                    if (ProductionAssignments.Contains(p.Id)) p.Role = PawnRole.Producing;
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
                foreach (var p in _allPawns)
                    if (ProductionAssignments.Contains(p.Id)) p.Role = PawnRole.Producing;
                break;
            case DayPhase.NightAct:
                // 守卫与读书同为夜间指派角色（一人一个）：守卫优先，剩下被指派读书者置 Reading。
                var guarded = new HashSet<int>(GuardAssignments.Values);
                foreach (var p in _allPawns)
                {
                    if (guarded.Contains(p.Id))
                        p.Role = PawnRole.Guard;
                    else if (ProductionAssignments.Contains(p.Id))
                        p.Role = PawnRole.Producing;
                    else if (ReadingAssignments.ContainsKey(p.Id))
                        p.Role = PawnRole.Reading;
                }
                break;
        }

        // [批次21·impl-bedrest] 卧床养病的令**跨相位持续**：顶部循环每次都把 Role 抹成 Idle，
        // 所以躺着的人必须在这里重新贴回 Bedrest —— 否则一到相位切换他就自己爬起来了。
        // 优先级：盖过 Guard/Reading/Sleeping/Idle（躺着的人不站岗、不生产、不读书，**这就是养病的代价**），
        // 但**不碰探险队**：人已经在野外了，够不着（下令时 BedrestLogic.CanOrderBedrest 也拦着不让给外出者下令）。
        foreach (var p in _allPawns)
        {
            if (p.BedrestOrdered && !ExpeditionIds.Contains(p.Id))
                p.Role = PawnRole.Bedrest;
        }

        // 手术接近/进行中的两人被医疗流程独占：床上病人保留 Bedrest，其余参与者维持 Idle 作为空间姿态，
        // 但 Pawn.IsControllable 会因 SurgeryOccupied 闸门禁止玩家与其它任务并发占用。
        foreach (var p in _allPawns)
        {
            if (p.SurgeryOccupied && p.Role != PawnRole.Bedrest)
                p.Role = PawnRole.Idle;
        }

        RolesChanged?.Invoke();
    }
}
