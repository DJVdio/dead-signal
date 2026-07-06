using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 消费层**，不得引入任何 Godot 类型
// （与 CombatData.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 它是"角色面板 UI"读取战斗单位状态的唯一契约：把可变的引擎对象（Body/Weapon/Armor）
// 拍成一份**死数据快照**，UI 拿到后永远改不坏战斗数据。字段名/结构即数据契约，勿随意改。

/// <summary>
/// 单个部位在某一刻的只读状态。数值/标记全为拍照时的拷贝，与 live Body 脱钩。
/// </summary>
public sealed class PartStatus
{
    public string Name { get; init; } = "";
    public double Hp { get; init; }
    public double MaxHp { get; init; }
    public BodyRegion Region { get; init; }
    public BodyPartCategory Category { get; init; }

    /// <summary>父部位名（null = 根，如躯干）。</summary>
    public string? ParentName { get; init; }

    public bool IsSevered { get; init; }
    public bool IsDestroyed { get; init; }
    public bool IsDisabled { get; init; }
    public bool IsFractured { get; init; }
    public bool IsBleeding { get; init; }
}

/// <summary>持械信息快照（无武器时整个对象为 null）。</summary>
public sealed class WeaponInfo
{
    public string Name { get; init; } = "";
    public double DamageMin { get; init; }
    public double DamageMax { get; init; }
    public double Penetration { get; init; }
    public bool IsRanged { get; init; }
    public bool TwoHanded { get; init; }
    public double AttackInterval { get; init; }
}

/// <summary>单层护甲信息快照。</summary>
public sealed class ArmorInfo
{
    public string Name { get; init; } = "";
    public double SharpDefense { get; init; }
    public double BluntDefense { get; init; }
    public ArmorSlot Slot { get; init; }
}

/// <summary>
/// 一个战斗单位对外的只读检视快照。UI 只读它、永远拿不到可变的引擎对象引用。
/// 通过 <see cref="FromBody"/> 静态工厂由 live Body/Weapon/Armor 拍成，构造后即与源脱钩。
/// </summary>
public sealed class PawnInspection
{
    public string DisplayName { get; init; } = "";

    public bool IsDead { get; init; }
    public bool IsUnconscious { get; init; }
    public bool IsFullyBlind { get; init; }

    public BloodLossTier BloodTier { get; init; }
    public double BloodRatio { get; init; }

    /// <summary>操作能力惩罚净值 0~1（1 = 完全丧失）。UI 显示"操作能力 = (1−此值)×100%"。</summary>
    public double OperationPenalty { get; init; }

    /// <summary>移动能力惩罚净值 0~1（1 = 完全丧失）。UI 显示"移动能力 = (1−此值)×100%"。</summary>
    public double MobilityPenalty { get; init; }

    /// <summary>饥饿阶梯序号（0=正常 … 5=饿死），即拍照时 HungerLevel 的枚举序号。</summary>
    public int HungerStage { get; init; }

    /// <summary>饥饿等级中文名（拍照时 HungerLevel 的显示名）。纯字符串快照，UI 直接显示。</summary>
    public string HungerLabel { get; init; } = "正常";

    public IReadOnlyList<PartStatus> Parts { get; init; } = Array.Empty<PartStatus>();

    /// <summary>持械信息，无武器时为 null。</summary>
    public WeaponInfo? Weapon { get; init; }

    public IReadOnlyList<ArmorInfo> Armor { get; init; } = Array.Empty<ArmorInfo>();

    /// <summary>
    /// 由 live 战斗对象拍出一份只读快照。遍历 <see cref="Body.Parts"/> 逐部位取当前 HP/上限/
    /// 区域/分类/父名 + 切除/损毁/失能/骨折/流血标记；武器/护甲摊平字段。
    /// </summary>
    /// <param name="hungerStage">饥饿阶梯序号（0=正常…5=饿死），由调用方从 Pawn.Hunger 取。</param>
    /// <param name="hungerLabel">饥饿等级中文名，由调用方从 Pawn.Hunger 取；null 视为"正常"。</param>
    public static PawnInspection FromBody(
        Body body, Weapon? weapon, IReadOnlyList<ArmorLayer>? armor, string name,
        int hungerStage = 0, string? hungerLabel = null)
    {
        // 出血伤口按部位名登记（断口即使部位被移除仍会持续出血）——直接命中部位名即在流血。
        var bleeding = new HashSet<string>(body.BleedingWounds);

        var parts = body.Parts.Values.Select(p => new PartStatus
        {
            Name = p.Name,
            Hp = body.HpOf(p.Name),
            MaxHp = body.MaxHpOf(p.Name),
            Region = p.Region,
            Category = p.Category,
            ParentName = p.Parent,
            IsSevered = body.IsSevered(p.Name),
            IsDestroyed = body.IsDestroyed(p.Name),
            IsDisabled = body.IsDisabled(p.Name),
            IsFractured = body.IsFractured(p.Name),
            IsBleeding = bleeding.Contains(p.Name),
        }).ToList();

        WeaponInfo? weaponInfo = weapon is null ? null : new WeaponInfo
        {
            Name = weapon.Name,
            DamageMin = weapon.DamageMin,
            DamageMax = weapon.DamageMax,
            Penetration = weapon.Penetration,
            IsRanged = weapon.IsRanged,
            TwoHanded = weapon.TwoHanded,
            AttackInterval = weapon.AttackInterval,
        };

        var armorInfos = (armor ?? Array.Empty<ArmorLayer>()).Select(a => new ArmorInfo
        {
            Name = a.Name,
            SharpDefense = a.SharpDefense,
            BluntDefense = a.BluntDefense,
            Slot = a.Slot,
        }).ToList();

        return new PawnInspection
        {
            DisplayName = name,
            IsDead = body.IsDead,
            IsUnconscious = body.IsUnconscious,
            IsFullyBlind = body.IsFullyBlind,
            BloodTier = body.BloodTier,
            BloodRatio = body.BloodRatio,
            OperationPenalty = body.DisabilityModifiers.OperationPenalty,
            MobilityPenalty = body.DisabilityModifiers.MobilityPenalty,
            HungerStage = hungerStage,
            HungerLabel = hungerLabel ?? "正常",
            Parts = parts,
            Weapon = weaponInfo,
            Armor = armorInfos,
        };
    }
}
