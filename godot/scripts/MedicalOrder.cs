using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HealthConditions.cs / InfectionCourseLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// [批次21·impl-medicine·T11] 「给谁用什么医疗物资」的**可用性判定**——玩家侧用药下令的决策层。
//   · 药效/手术数值一概不在此：单一事实源仍是 MedicineCatalog / SurgeryCatalog / HealthConditionSet（HealthConditions.cs）。
//     本层只回答三件事：这材料是不是能直接用的医疗物资、它的用途是哪一类、**现在能不能给这个人用（不能则为什么）**。
//   · 为什么要抽出来：这套规则原先散在 MedicalPanel 的按钮 Disabled 表达式里（have<=0 || isCurrent 之类，MedicalPanel.cs:348/453/571/624），
//     CampMain 的 handler 再各自复查一遍库存——没有一处说得清"给这个人能用什么"。而角色侧「选中角色 → 给他用药」的下令入口
//     必须先能回答它：右键点一个没病没伤的人，该告诉玩家"他好得很"，而不是弹一张空面板。
//   · 消耗与生效由调用方做（CampMain 扣 InventoryStore、调 HealthConditionSet 的对应方法）——本层零副作用、纯函数。

/// <summary>一件医疗物资的用途类别（决定它走哪条生效链路）。</summary>
public enum MedicalUseKind
{
    /// <summary>感染疗程用药（抗生素/草药膏/蒲公英茶）：指派后每昼夜黎明自动扣一份，推进治疗进度与感染进度的竞速（<see cref="HealthConditionSet.AdvanceInfectionRace"/>）。</summary>
    InfectionCourse,

    /// <summary>疾病单发用药（成药）：当场扣一份，severity 消退（<see cref="HealthConditionSet.TreatIllness"/>）。</summary>
    DiseaseDose,

    /// <summary>恢复补剂（玫瑰果茶）：当场扣一份，一段时间内加算术后愈合的恢复效率（不治病，只加速养伤）。</summary>
    RecoveryTonic,

    /// <summary>手术耗材（绷带/草药绷带/针线/夹板/急救包）：手术时投入，供手术点数（<see cref="SurgeryCatalog"/>）。</summary>
    SurgerySupply,
}

/// <summary>不能用的原因（UI 直接拿去当禁用理由与提示词，不再各处硬编码）。</summary>
public enum MedicalRefusal
{
    /// <summary>可用。</summary>
    None,

    /// <summary>库存为零。</summary>
    OutOfStock,

    /// <summary>没有对症的伤病（如没感染却要用抗生素、没骨折却要用夹板）。</summary>
    NoTarget,

    /// <summary>已在生效中：同一档药的疗程正在跑 / 恢复补剂的加成尚未过期。再用一份是白扔。</summary>
    AlreadyActive,

    /// <summary>病人已死。</summary>
    PatientDead,

    /// <summary>该材料根本不是能直接用的医疗物资（木头、或蒲公英这类只能入配方的草药原料）。</summary>
    NotMedical,
}

/// <summary>「给这个病人用这件医疗物资」的一条判定（纯数据，零副作用）。</summary>
/// <param name="MaterialKey">材料标识名（对齐 <see cref="MaterialDef.Key"/>）。</param>
/// <param name="Kind">用途类别；非医疗物资时为 null。</param>
/// <param name="Stock">营地当前库存份数。</param>
/// <param name="Refusal">不可用的原因；<see cref="MedicalRefusal.None"/>=可用。</param>
public readonly record struct MedicalUseOption(string MaterialKey, MedicalUseKind? Kind, int Stock, MedicalRefusal Refusal)
{
    /// <summary>现在能不能给这个人用。</summary>
    public bool Usable => Refusal == MedicalRefusal.None;
}

/// <summary>玩家侧用药下令的可用性判定（纯函数：不改库存、不改伤病集、不推进任何进度）。</summary>
public static class MedicalOrderLogic
{
    /// <summary>恢复补剂（玫瑰果茶）的材料 key。数值（加成百分点/时长）不在此——那在消费层的 Pawn 上。</summary>
    public const string RecoveryTonicKey = "rosehip_tea";

    /// <summary>可直接使用的成品医疗物资全表（**不含**蒲公英/玫瑰果/老君须这类只能入配方的草药原料）。顺序=面板展示顺序：先手术耗材、再药、最后补剂。</summary>
    private static readonly string[] _supplies =
    {
        "bandage", "herbal_bandage", "needle_thread", "splint", "first_aid_kit", // 手术耗材（SurgeryCatalog）
        "antibiotics", "herbal_salve", "dandelion_tea",                          // 感染三档（MedicineCatalog）
        "medicine",                                                              // 疾病成药（MedicineCatalog）
        RecoveryTonicKey,                                                        // 恢复补剂
    };

    /// <summary>该材料的用途类别；不是可直接使用的医疗物资（草药原料/非医疗材料）→ null。</summary>
    public static MedicalUseKind? KindOf(string? materialKey)
    {
        if (string.IsNullOrEmpty(materialKey))
        {
            return null;
        }
        if (materialKey == RecoveryTonicKey)
        {
            return MedicalUseKind.RecoveryTonic;
        }
        if (SurgeryCatalog.For(materialKey) is not null)
        {
            return MedicalUseKind.SurgerySupply;
        }
        return MedicineCatalog.For(materialKey) switch
        {
            { Treats: HealthConditionType.Infection } => MedicalUseKind.InfectionCourse,
            { Treats: HealthConditionType.Disease } => MedicalUseKind.DiseaseDose,
            _ => null,
        };
    }

    /// <summary>该材料是否为可直接使用的成品医疗物资（草药原料返回 false——它们只能进配方）。</summary>
    public static bool IsMedicalSupply(string? materialKey) => KindOf(materialKey) is not null;

    /// <summary>
    /// 判定「现在能不能给这个病人用这件医疗物资」。判定序：非医疗物资 → 病人已死 → 缺货 → 已在生效中 → 无对症目标 → 可用。
    /// <para>缺货排在无对症目标之前是有意的：玩家最先该看见的是"没药了"（那是要去搜刮/去做的事）；
    /// "这人没病"由 <see cref="NeedsMedicalAttention"/> 在开面板之前就兜住了。</para>
    /// </summary>
    /// <param name="materialKey">要用的材料。</param>
    /// <param name="set">病人的伤病集（只读）。</param>
    /// <param name="stock">营地当前该材料库存份数。</param>
    /// <param name="rosehipActive">病人身上的恢复补剂加成是否仍生效（生效中再喝一份是白扔）。</param>
    /// <param name="currentCourseKey">病人当前指派的感染疗程药档（null=无疗程）；同一档不可重复指派，换档可以。</param>
    /// <param name="alive">病人是否活着。</param>
    public static MedicalUseOption Evaluate(
        string materialKey,
        HealthConditionSet set,
        int stock,
        bool rosehipActive,
        string? currentCourseKey,
        bool alive = true)
    {
        MedicalUseKind? kind = KindOf(materialKey);
        if (kind is null)
        {
            return new MedicalUseOption(materialKey, null, stock, MedicalRefusal.NotMedical);
        }
        if (!alive || set.IsDead)
        {
            return new MedicalUseOption(materialKey, kind, stock, MedicalRefusal.PatientDead);
        }
        if (stock <= 0)
        {
            return new MedicalUseOption(materialKey, kind, stock, MedicalRefusal.OutOfStock);
        }

        MedicalRefusal refusal = kind switch
        {
            // 同一档疗程已在跑 → 重复指派无意义；换另一档是终稿明说的混合策略（先垫治疗进度再接力），必须放行。
            MedicalUseKind.InfectionCourse when currentCourseKey == materialKey => MedicalRefusal.AlreadyActive,
            MedicalUseKind.InfectionCourse when !Has(set, HealthConditionType.Infection) => MedicalRefusal.NoTarget,

            MedicalUseKind.DiseaseDose when !Has(set, HealthConditionType.Disease) => MedicalRefusal.NoTarget,

            MedicalUseKind.RecoveryTonic when rosehipActive => MedicalRefusal.AlreadyActive,
            // 补剂只加速术后流血/骨折的逐日愈合 ⇒ 没有这两类伤的人喝了什么也不会发生，别让他白扔一份。
            MedicalUseKind.RecoveryTonic when !Has(set, HealthConditionType.Bleeding) && !Has(set, HealthConditionType.Fracture)
                => MedicalRefusal.NoTarget,

            MedicalUseKind.SurgerySupply when !HasSurgeryTargetFor(set, materialKey) => MedicalRefusal.NoTarget,

            _ => MedicalRefusal.None,
        };
        return new MedicalUseOption(materialKey, kind, stock, refusal);
    }

    /// <summary>
    /// 枚举全部成品医疗物资对这个病人的判定（面板/右键菜单直接消费；<c>.Where(o =&gt; o.Usable)</c> 即"现在能给他用的东西"）。
    /// </summary>
    /// <param name="stockOf">库存查询（材料 key → 份数）。</param>
    public static IReadOnlyList<MedicalUseOption> OptionsFor(
        HealthConditionSet set,
        Func<string, int> stockOf,
        bool rosehipActive,
        string? currentCourseKey,
        bool alive = true)
        => _supplies
            .Select(k => Evaluate(k, set, stockOf(k), rosehipActive, currentCourseKey, alive))
            .ToList();

    /// <summary>
    /// 这个人现在需不需要进医务（决定"选中角色 → 医务"这条下令要不要给、右键点他要不要开面板）：
    /// 有任何伤病，或有可安装假肢的缺肢空槽（MedicalPanel 已有此口径：无伤病但缺肢照样开面板）。
    /// **与库存无关**——没药也要让玩家看见"他伤成什么样了"。
    /// </summary>
    public static bool NeedsMedicalAttention(HealthConditionSet set, bool hasEquippableProstheticSlot)
        => set.Conditions.Count > 0 || hasEquippableProstheticSlot;

    private static bool Has(HealthConditionSet set, HealthConditionType type)
        => set.Conditions.Any(c => c.Type == type);

    /// <summary>
    /// 该手术耗材在这个病人身上有没有可下刀的目标：按 <see cref="SurgeryCatalog"/> 的适用伤类找对应病状；
    /// 止血类耗材另计**感染的肢体**——截肢要靠它关合残端（MedicalPanel 截肢区复用的正是止血耗材）。
    /// </summary>
    private static bool HasSurgeryTargetFor(HealthConditionSet set, string materialKey)
    {
        if (SurgeryCatalog.For(materialKey) is not SurgerySupply sup)
        {
            return false;
        }
        bool matchesWound = set.Conditions.Any(c => sup.CanTreat(c.Type));
        bool closesStump = sup.CanTreat(HealthConditionType.Bleeding)
            && set.Conditions.Any(c => c.Type == HealthConditionType.Infection && c.OnLimb);
        return matchesWound || closesStump;
    }
}
