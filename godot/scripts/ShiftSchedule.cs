using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载「双班硬性日程 + 睡眠不足疲劳 debuff」的全部规则（批次6，shift-sleep）：
//   · 班别 Shift = 探险名单派生：当日出探险=DayCrew（白天出门），其余=NightCrew（夜里站岗/生产）。
//   · 硬日程：白天营地夜班强制睡（不可操作）、夜里探险队强制睡；昼夜边界的聚餐流程全员参加（吃完继续睡）。
//   · 常规袭营仅夜间；白天仅 authored 剧情事件可破日程。
//   · 被唤醒者次相位吃疲劳 debuff（效率/攻速/视距/视角系数降）。
// ★职责划分（与 watch-contest 的 NightWatchContest.cs 对齐）：名册与「谁被唤醒」归 watch-contest
//   （RaidTier/WatchDuty/WatchMember/RespondersFor/AwokenSleepers 皆在其文件）；shift-sleep 只出
//   「睡着与否/可操作与否/被唤醒者的疲劳 debuff」——DebuffsForAwoken 消费其 AwokenSleepers 名册出 debuff。
// 运行时接线（相位调度/操作锁定 UI/次相位 debuff 施加与过期）归 night-response。数值全部「拟定待调」。

/// <summary>班别：白天出门探险 <see cref="DayCrew"/> / 夜里站岗生产 <see cref="NightCrew"/>。每 Pawn 一个班别，由探险名单派生。</summary>
public enum Shift
{
    /// <summary>白天班：当日出探险者。白天 Active（出门），夜里强制睡。</summary>
    DayCrew,

    /// <summary>夜里班：留守站岗/生产者（非探险者）。夜里 Active，白天强制睡。</summary>
    NightCrew,
}

/// <summary>玩法相位只有白天与黑夜；聚餐由独立流程谓词判断，不是第三相位。</summary>
public enum DayNightPhase
{
    Day,
    Night,
}

/// <summary>某班别在某相位的日程态：强制睡（不可操作）/在勤活动/聚餐参加（模态）。</summary>
public enum PawnPhaseState
{
    /// <summary>强制睡眠：不可操作、不可下令；唯一合法唤醒是袭营响应分级（由 watch-contest 名册裁定）。</summary>
    Sleeping,

    /// <summary>在勤：正常可操作（探险出门 / 夜班站岗生产）。</summary>
    Active,

    /// <summary>聚餐参加：模态过渡，全员参加（吃完继续原班别睡眠/活动），非常规可操作态。</summary>
    Meal,
}

/// <summary>
/// 睡眠不足疲劳 debuff（被唤醒的睡眠者的**次相位**代价）。全为能力系数 ∈(0,1]，1=无衰减；
/// 与饥饿/残疾（<see cref="HungerState.CombineCapability"/>）及视野（<see cref="VisionLogic.VisionCone.Scaled"/>）
/// 管道正交叠乘——消费方把系数乘到对应轴（效率/攻速/视距/视角），本结构不碰那些引擎。数值「拟定待调」。
/// </summary>
public readonly struct FatigueDebuff
{
    /// <summary>生产/操作效率系数（乘到工作速度/操作能力；&lt;1 变慢）。</summary>
    public float EfficiencyMult { get; init; }

    /// <summary>攻速系数（乘到攻击频率；&lt;1 出手更慢 → 有效攻击间隔 = 基础 ÷ (能力×本系数)）。</summary>
    public float AttackSpeedMult { get; init; }

    /// <summary>视距系数（喂 <see cref="VisionLogic.VisionCone.Scaled"/> 的 rangeMult；&lt;1 看得更近）。</summary>
    public float VisionRangeMult { get; init; }

    /// <summary>视角系数（喂 <see cref="VisionLogic.VisionCone.Scaled"/> 的 angleMult；&lt;1 视野更窄）。</summary>
    public float VisionAngleMult { get; init; }

    /// <summary>是否生效（false = 无 debuff，各轴皆 1）。</summary>
    public bool IsActive { get; init; }

    /// <summary>无 debuff 的恒等系数（各轴 1、未激活）。</summary>
    public static FatigueDebuff None => new()
    {
        EfficiencyMult = 1f,
        AttackSpeedMult = 1f,
        VisionRangeMult = 1f,
        VisionAngleMult = 1f,
        IsActive = false,
    };
}

/// <summary>
/// 双班硬日程 + 睡眠 debuff 的纯规则（无 Godot 依赖、Link 进单测）。全静态纯函数 + draft 常量。
/// </summary>
public static class ShiftSchedule
{
    // ---- 疲劳 debuff 常量（拟定待调）----
    /// <summary>被唤醒者次相位生产/操作效率系数。</summary>
    public const float FatigueEfficiencyMult = 0.80f;
    /// <summary>被唤醒者次相位攻速系数。</summary>
    public const float FatigueAttackSpeedMult = 0.85f;
    /// <summary>被唤醒者次相位视距系数。</summary>
    public const float FatigueVisionRangeMult = 0.85f;
    /// <summary>被唤醒者次相位视角系数。</summary>
    public const float FatigueVisionAngleMult = 0.90f;

    /// <summary>标准疲劳 debuff（各轴取上述拟定系数）。被唤醒的睡眠者次相位施加。</summary>
    public static FatigueDebuff StandardFatigue => new()
    {
        EfficiencyMult = FatigueEfficiencyMult,
        AttackSpeedMult = FatigueAttackSpeedMult,
        VisionRangeMult = FatigueVisionRangeMult,
        VisionAngleMult = FatigueVisionAngleMult,
        IsActive = true,
    };

    /// <summary>流程节点归入白天/黑夜两相位。
    /// 🔴 分类逻辑已收口到唯一事实源 <see cref="DayPhaseSegments.PhaseOf"/>，本方法只做转发别名（保留既有调用点/测试名）。</summary>
    public static DayNightPhase PhaseOf(DayPhase phase) => DayPhaseSegments.PhaseOf(phase);

    /// <summary>班别派生：在当日探险名单内 → <see cref="Shift.DayCrew"/>；否则 <see cref="Shift.NightCrew"/>（站岗/生产）。</summary>
    public static Shift ShiftFor(int pawnId, IReadOnlyCollection<int> expeditionIds)
        => expeditionIds != null && expeditionIds.Contains(pawnId) ? Shift.DayCrew : Shift.NightCrew;

    /// <summary>watch-contest 夜间班别 <see cref="WatchDuty"/> → 昼夜班桥接：探险队=DayCrew，守卫/生产者=NightCrew。</summary>
    public static Shift ShiftFromDuty(WatchDuty duty)
        => duty == WatchDuty.Expedition ? Shift.DayCrew : Shift.NightCrew;

    /// <summary>
    /// 班别 × 内部流程节点的日程态：聚餐 → 全员 <see cref="PawnPhaseState.Meal"/>；
    /// 白天块 → DayCrew 在勤(出门)、NightCrew 强制睡；夜里块 → NightCrew 在勤、DayCrew 强制睡。
    /// </summary>
    public static PawnPhaseState PhaseStateFor(Shift shift, DayPhase phase)
    {
        if (DayPhaseSegments.IsMeal(phase))
            return PawnPhaseState.Meal;

        switch (PhaseOf(phase))
        {
            case DayNightPhase.Day:
                return shift == Shift.DayCrew ? PawnPhaseState.Active : PawnPhaseState.Sleeping;
            case DayNightPhase.Night:
            default:
                return shift == Shift.NightCrew ? PawnPhaseState.Active : PawnPhaseState.Sleeping;
        }
    }

    /// <summary>是否可操作/下令：仅在勤（<see cref="PawnPhaseState.Active"/>）为真；睡眠(不可操作)与聚餐(模态)为假。</summary>
    public static bool CanOperate(Shift shift, DayPhase phase)
        => PhaseStateFor(shift, phase) == PawnPhaseState.Active;

    /// <summary>
    /// 该班别在该相位是否处于「可生产工时相位」（craft-worktime 消费）：夜班在 <see cref="DayPhase.NightAct"/>
    /// （唯一实时推进的夜相；NightPrep 为暂停编排相位）在勤时才推进工时。睡眠/聚餐/白天皆不生产（人不在工作台）。
    /// </summary>
    public static bool IsWorkPhaseFor(Shift shift, DayPhase phase)
        => shift == Shift.NightCrew && phase == DayPhase.NightAct;

    /// <summary>常规袭营仅夜间（夜里块）合法；<paramref name="authored"/> 剧情事件可破日程、任意相位放行。</summary>
    public static bool RaidAllowedIn(DayPhase phase, bool authored)
        => authored || (DayPhaseSegments.IsNight(phase) && !DayPhaseSegments.IsMeal(phase));

    /// <summary>被唤醒者的次相位 debuff：<paramref name="woken"/> → <see cref="StandardFatigue"/>；否则 <see cref="FatigueDebuff.None"/>。</summary>
    public static FatigueDebuff DebuffFor(bool woken)
        => woken ? StandardFatigue : FatigueDebuff.None;

    /// <summary>
    /// 消费 watch-contest 的 <see cref="NightWatchContest.AwokenSleepers"/> 名册，出「成员 Id → 次相位疲劳 debuff」映射。
    /// 名册（谁被唤醒）归 watch-contest，本函数只给每个被唤醒的睡眠者贴上统一 <see cref="StandardFatigue"/>；
    /// 未被唤醒者不入表（无疲劳代价）。night-response 据此在被唤醒者的次个在勤相位施加、过一相位后清除。
    /// </summary>
    public static IReadOnlyDictionary<string, FatigueDebuff> DebuffsForAwoken(RaidTier tier, IEnumerable<WatchMember> roster)
        => NightWatchContest.AwokenSleepers(tier, roster).ToDictionary(m => m.Id, _ => StandardFatigue);
}
