using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HealthConditions.cs / CraftingLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// [SPEC-B14/终稿·护栏①] 感染疗程（持续用药指派）每昼夜**扣药决策**的纯逻辑：把"有没有指派/还有没有感染/够不够药/该不该扣药/本日用哪档药"
// 从 CampMain 运行时抽出，便于先红后绿单测。库存扣减/提示/清疗程等副作用由调用方按返回步态执行；治疗/感染进度的推进在
// HealthConditionSet.AdvanceInfectionRace（相位级竞速），本决策只定"本时段是否在用药 + 用哪档"。

/// <summary>一昼夜疗程扣药决策的结果步态。</summary>
public enum InfectionCourseStep
{
    /// <summary>未指派疗程（medKey 为空）：本时段未用药（感染仍会独立恶化，无减缓/无治疗进度）。</summary>
    NoCourse,

    /// <summary>已指派但病人当前无感染（被治愈/截肢清除）：调用方应静默清疗程。</summary>
    NoInfection,

    /// <summary>有感染但该药断货：调用方应清疗程 + 弹醒目断药提示（防"忘补药=冤死"）；本时段未用药。</summary>
    OutOfStock,

    /// <summary>正常扣 1 份药，本时段在用药：调用方按 <see cref="InfectionDoseDecision.Medicine"/> 驱动竞速（减缓恶化 + 累进治疗进度）。</summary>
    Dosed,
}

/// <summary>一昼夜扣药决策：步态 + 是否扣 1 份药 + 本时段用药档（供 <see cref="HealthConditionSet.AdvanceInfectionRace"/>）。</summary>
public readonly record struct InfectionDoseDecision(InfectionCourseStep Step, bool ConsumedDose, Medicine? Medicine)
{
    /// <summary>本时段是否在用药（据此对竞速传 medicated=true + 减缓/治疗档）。</summary>
    public bool Medicated => Step == InfectionCourseStep.Dosed;
}

/// <summary>感染疗程每昼夜扣药决策的纯函数（无副作用；不改库存、不推进竞速）。</summary>
public static class InfectionCourseLogic
{
    /// <summary>
    /// 判定 <paramref name="set"/> 本昼夜的疗程扣药：给指派药 <paramref name="medKey"/>（null=无疗程）与其库存 <paramref name="stock"/>。
    ///   · 无疗程 → <see cref="InfectionCourseStep.NoCourse"/>；· 无感染 → <see cref="InfectionCourseStep.NoInfection"/>；
    ///   · 缺药 → <see cref="InfectionCourseStep.OutOfStock"/>（不扣药、本时段未用药）；· 有感染且有药 → <see cref="InfectionCourseStep.Dosed"/>（扣 1 份、本时段用药）。
    /// 治疗/感染进度的推进不在此——由调用方据 <see cref="InfectionDoseDecision.Medicated"/> 调 <see cref="HealthConditionSet.AdvanceInfectionRace"/>。
    /// </summary>
    public static InfectionDoseDecision DecideDose(HealthConditionSet set, string? medKey, int stock)
    {
        if (string.IsNullOrEmpty(medKey))
        {
            return new InfectionDoseDecision(InfectionCourseStep.NoCourse, false, null);
        }
        bool hasInfection = set.Conditions.Any(c => c.Type == HealthConditionType.Infection);
        if (!hasInfection)
        {
            return new InfectionDoseDecision(InfectionCourseStep.NoInfection, false, null);
        }
        if (stock <= 0)
        {
            return new InfectionDoseDecision(InfectionCourseStep.OutOfStock, false, null);
        }
        return new InfectionDoseDecision(InfectionCourseStep.Dosed, true, MedicineCatalog.For(medKey));
    }
}
