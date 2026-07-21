using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 双班硬性日程 + 睡眠不足疲劳 debuff 纯逻辑单测（批次6，shift-sleep）。锁规则形态与当前拟定值：
///   · 班别 = 探险名单派生（探险=DayCrew / 其余=NightCrew）；亦可由 watch-contest 的 WatchDuty 桥接。
///   · 白天：探险队出门(Active) + 营地夜班强制睡(Sleeping·不可操作)；
///     夜里：夜班站岗/生产(Active) + 探险队强制睡(Sleeping)；昼夜边界的聚餐流程全员参加(Meal)。
///   · 生产工时相位 = 夜班在 NightAct 在勤；常规袭营仅夜间，白天仅 authored 事件可破日程。
///   · 名册与「谁被唤醒」归 watch-contest（NightWatchContest.RespondersFor/AwokenSleepers）；
///     shift-sleep 只出「睡着与否/可操作与否/被唤醒者的疲劳 debuff」——DebuffsForAwoken 消费其 AwokenSleepers 出 debuff。
/// 数值皆「拟定待调」。
/// </summary>
public class ShiftScheduleTests
{
    // ---- 相位分块 ----
    [Fact]
    public void BlockOf_DayPhases_AreDayBlock()
    {
        Assert.Equal(DayNightPhase.Day, ShiftSchedule.PhaseOf(DayPhase.DawnMeal));
        Assert.Equal(DayNightPhase.Day, ShiftSchedule.PhaseOf(DayPhase.DayPrep));
        Assert.Equal(DayNightPhase.Day, ShiftSchedule.PhaseOf(DayPhase.DayTravel));
        Assert.Equal(DayNightPhase.Day, ShiftSchedule.PhaseOf(DayPhase.DayExplore));
        Assert.Equal(DayNightPhase.Day, ShiftSchedule.PhaseOf(DayPhase.DayReturn));
    }

    [Fact]
    public void BlockOf_NightPhases_AreNightBlock()
    {
        Assert.Equal(DayNightPhase.Night, ShiftSchedule.PhaseOf(DayPhase.DuskMeal));
        Assert.Equal(DayNightPhase.Night, ShiftSchedule.PhaseOf(DayPhase.NightPrep));
        Assert.Equal(DayNightPhase.Night, ShiftSchedule.PhaseOf(DayPhase.NightAct));
    }

    // ---- 班别派生 ----
    [Fact]
    public void ShiftFor_ExpeditionMember_IsDayCrew()
    {
        var expedition = new HashSet<int> { 1, 3 };
        Assert.Equal(Shift.DayCrew, ShiftSchedule.ShiftFor(1, expedition));
        Assert.Equal(Shift.DayCrew, ShiftSchedule.ShiftFor(3, expedition));
    }

    [Fact]
    public void ShiftFor_NonExpedition_IsNightCrew()
    {
        var expedition = new HashSet<int> { 1, 3 };
        Assert.Equal(Shift.NightCrew, ShiftSchedule.ShiftFor(2, expedition));
        Assert.Equal(Shift.NightCrew, ShiftSchedule.ShiftFor(99, expedition));
    }

    [Fact]
    public void ShiftFor_EmptyExpedition_AllNightCrew()
    {
        var expedition = new HashSet<int>();
        Assert.Equal(Shift.NightCrew, ShiftSchedule.ShiftFor(1, expedition));
    }

    // ---- 与 watch-contest WatchDuty 桥接 ----
    [Fact]
    public void ShiftFromDuty_ExpeditionIsDayCrew_GuardAndProducerAreNightCrew()
    {
        Assert.Equal(Shift.DayCrew, ShiftSchedule.ShiftFromDuty(WatchDuty.Expedition));
        Assert.Equal(Shift.NightCrew, ShiftSchedule.ShiftFromDuty(WatchDuty.Guard));
        Assert.Equal(Shift.NightCrew, ShiftSchedule.ShiftFromDuty(WatchDuty.Producer));
    }

    // ---- 班别 × 相位睡眠矩阵 ----
    [Fact]
    public void PhaseStateFor_Day_DayCrewActive_NightCrewSleeping()
    {
        Assert.Equal(PawnPhaseState.Active, ShiftSchedule.PhaseStateFor(Shift.DayCrew, DayPhase.DayExplore));
        Assert.Equal(PawnPhaseState.Sleeping, ShiftSchedule.PhaseStateFor(Shift.NightCrew, DayPhase.DayExplore));
    }

    [Fact]
    public void PhaseStateFor_Night_NightCrewActive_DayCrewSleeping()
    {
        Assert.Equal(PawnPhaseState.Active, ShiftSchedule.PhaseStateFor(Shift.NightCrew, DayPhase.NightAct));
        Assert.Equal(PawnPhaseState.Sleeping, ShiftSchedule.PhaseStateFor(Shift.DayCrew, DayPhase.NightAct));
    }

    [Fact]
    public void PhaseStateFor_Meal_AllAttend()
    {
        Assert.Equal(PawnPhaseState.Meal, ShiftSchedule.PhaseStateFor(Shift.DayCrew, DayPhase.DawnMeal));
        Assert.Equal(PawnPhaseState.Meal, ShiftSchedule.PhaseStateFor(Shift.NightCrew, DayPhase.DawnMeal));
        Assert.Equal(PawnPhaseState.Meal, ShiftSchedule.PhaseStateFor(Shift.DayCrew, DayPhase.DuskMeal));
        Assert.Equal(PawnPhaseState.Meal, ShiftSchedule.PhaseStateFor(Shift.NightCrew, DayPhase.DuskMeal));
    }

    // ---- 可操作性 ----
    [Fact]
    public void CanOperate_OnlyWhenActive()
    {
        Assert.True(ShiftSchedule.CanOperate(Shift.NightCrew, DayPhase.NightAct));    // 夜班站岗
        Assert.True(ShiftSchedule.CanOperate(Shift.DayCrew, DayPhase.DayExplore));    // 探险队出门
        Assert.False(ShiftSchedule.CanOperate(Shift.NightCrew, DayPhase.DayExplore)); // 营地班白天强制睡
        Assert.False(ShiftSchedule.CanOperate(Shift.DayCrew, DayPhase.NightAct));     // 探险队夜里强制睡
        Assert.False(ShiftSchedule.CanOperate(Shift.DayCrew, DayPhase.DawnMeal));     // 聚餐是模态，不算常规可操作
    }

    // ---- 生产工时相位（craft-worktime 消费）----
    [Fact]
    public void IsWorkPhaseFor_NightCrewAtNightAct_True()
    {
        Assert.True(ShiftSchedule.IsWorkPhaseFor(Shift.NightCrew, DayPhase.NightAct));
    }

    [Fact]
    public void IsWorkPhaseFor_OtherCases_False()
    {
        Assert.False(ShiftSchedule.IsWorkPhaseFor(Shift.DayCrew, DayPhase.NightAct));     // 探险队夜里睡，不生产
        Assert.False(ShiftSchedule.IsWorkPhaseFor(Shift.NightCrew, DayPhase.DayExplore)); // 夜班白天睡，不生产
        Assert.False(ShiftSchedule.IsWorkPhaseFor(Shift.NightCrew, DayPhase.NightPrep));  // 夜里编排相位(暂停)不推进工时
        Assert.False(ShiftSchedule.IsWorkPhaseFor(Shift.NightCrew, DayPhase.DawnMeal));   // 聚餐不生产
    }

    // ---- 袭营时段合法性 ----
    [Fact]
    public void RaidAllowedIn_NightOnly_ForRegular()
    {
        Assert.True(ShiftSchedule.RaidAllowedIn(DayPhase.NightAct, authored: false));
        Assert.True(ShiftSchedule.RaidAllowedIn(DayPhase.NightPrep, authored: false));
        Assert.False(ShiftSchedule.RaidAllowedIn(DayPhase.DayExplore, authored: false));
        Assert.False(ShiftSchedule.RaidAllowedIn(DayPhase.DawnMeal, authored: false));
    }

    [Fact]
    public void RaidAllowedIn_Authored_BreaksSchedule_AnyPhase()
    {
        Assert.True(ShiftSchedule.RaidAllowedIn(DayPhase.DayExplore, authored: true));
        Assert.True(ShiftSchedule.RaidAllowedIn(DayPhase.DawnMeal, authored: true));
    }

    // ---- 疲劳 debuff 形态 ----
    [Fact]
    public void FatigueDebuff_None_IsIdentity()
    {
        var none = FatigueDebuff.None;
        Assert.False(none.IsActive);
        Assert.Equal(1f, none.EfficiencyMult);
        Assert.Equal(1f, none.AttackSpeedMult);
        Assert.Equal(1f, none.VisionRangeMult);
        Assert.Equal(1f, none.VisionAngleMult);
    }

    [Fact]
    public void StandardFatigue_AllAxesDegraded_AndActive()
    {
        var f = ShiftSchedule.StandardFatigue;
        Assert.True(f.IsActive);
        Assert.True(f.EfficiencyMult < 1f && f.EfficiencyMult > 0f);
        Assert.True(f.AttackSpeedMult < 1f && f.AttackSpeedMult > 0f);
        Assert.True(f.VisionRangeMult < 1f && f.VisionRangeMult > 0f);
        Assert.True(f.VisionAngleMult < 1f && f.VisionAngleMult > 0f);
        Assert.Equal(ShiftSchedule.FatigueEfficiencyMult, f.EfficiencyMult);
        Assert.Equal(ShiftSchedule.FatigueAttackSpeedMult, f.AttackSpeedMult);
        Assert.Equal(ShiftSchedule.FatigueVisionRangeMult, f.VisionRangeMult);
        Assert.Equal(ShiftSchedule.FatigueVisionAngleMult, f.VisionAngleMult);
    }

    [Fact]
    public void DebuffFor_WokenGetsStandardFatigue_ElseNone()
    {
        Assert.Equal(ShiftSchedule.StandardFatigue, ShiftSchedule.DebuffFor(woken: true));
        Assert.False(ShiftSchedule.DebuffFor(woken: false).IsActive);
    }

    // ---- 被唤醒者的 debuff：消费 watch-contest 的 AwokenSleepers 名册 ----
    [Fact]
    public void DebuffsForAwoken_NightHigh_ExpeditionWoken_GetsFatigue()
    {
        // 夜里袭营名册：守卫醒/生产者醒/探险队睡。高危 → 探险队被唤醒。
        var roster = new[]
        {
            new WatchMember("guard", WatchDuty.Guard, asleep: false),
            new WatchMember("prod", WatchDuty.Producer, asleep: false),
            new WatchMember("expA", WatchDuty.Expedition, asleep: true),
            new WatchMember("expB", WatchDuty.Expedition, asleep: true),
        };
        var debuffs = ShiftSchedule.DebuffsForAwoken(RaidTier.High, roster);
        Assert.Equal(2, debuffs.Count);                  // 两名探险队被唤醒
        Assert.True(debuffs.ContainsKey("expA"));
        Assert.True(debuffs.ContainsKey("expB"));
        Assert.True(debuffs["expA"].IsActive);
        Assert.Equal(ShiftSchedule.StandardFatigue, debuffs["expA"]);
        Assert.False(debuffs.ContainsKey("guard"));      // 在勤者无疲劳代价
        Assert.False(debuffs.ContainsKey("prod"));
    }

    [Fact]
    public void DebuffsForAwoken_NightLowAndMid_NoSleeperWoken_NoDebuff()
    {
        var roster = new[]
        {
            new WatchMember("guard", WatchDuty.Guard, asleep: false),
            new WatchMember("prod", WatchDuty.Producer, asleep: false),
            new WatchMember("exp", WatchDuty.Expedition, asleep: true),
        };
        // 低危：仅守卫参战，睡眠探险队不动。
        Assert.Empty(ShiftSchedule.DebuffsForAwoken(RaidTier.Low, roster));
        // 中危：除睡觉探险队外全部；夜里唯一睡眠者是探险队 → 无人被唤醒。
        Assert.Empty(ShiftSchedule.DebuffsForAwoken(RaidTier.Medium, roster));
    }
}
