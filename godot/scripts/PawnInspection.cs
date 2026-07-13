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

    /// <summary>玩家可见的一行风味描述（黑色幽默；空串=无）。装备 UI 悬停展示用。</summary>
    public string Description { get; init; } = "";

    public double DamageMin { get; init; }
    public double DamageMax { get; init; }
    public double Penetration { get; init; }
    public bool IsRanged { get; init; }
    public bool TwoHanded { get; init; }
    public double AttackInterval { get; init; }

    /// <summary>由 live <see cref="Weapon"/> 拍一份只读快照（null → null）。装备 UI 展示某手持械用。</summary>
    public static WeaponInfo? From(Weapon? w) => w is null ? null : new WeaponInfo
    {
        Name = w.Name,
        Description = w.Description,
        DamageMin = w.DamageMin,
        DamageMax = w.DamageMax,
        Penetration = w.Penetration,
        IsRanged = w.IsRanged,
        TwoHanded = w.TwoHanded,
        AttackInterval = w.AttackInterval,
    };
}

/// <summary>一个穿戴槽的只读态：装了什么（<see cref="ItemName"/> 为 null = 空槽）+ 是否因断肢被禁用。</summary>
public sealed class ApparelSlotStatus
{
    public EquipSlot Slot { get; init; }

    /// <summary>该槽当前所装穿戴品标识（护甲名等），null = 空槽。</summary>
    public string? ItemName { get; init; }

    /// <summary>该侧肢体已切除 → 槽不可用（UI 灰显）。</summary>
    public bool IsDisabled { get; init; }
}

/// <summary>
/// 装备态只读快照：11 穿戴槽 + 左右手持械 + 握持态。与 <see cref="PawnInspection"/> 一样是死数据，
/// 由持有 live Pawn 的一侧（CampMain）从 Pawn 的**只读装备 API**（ApparelAt/WeaponInHand/Grip/
/// DisabledApparelSlots）拍出，喂给角色面板展示。本类纯 C#、无 Godot 依赖。
/// </summary>
public sealed class EquipmentSnapshot
{
    /// <summary>全部 11 个穿戴槽的当前态（顺序即 <see cref="EquipSlot"/> 声明序）。</summary>
    public IReadOnlyList<ApparelSlotStatus> Slots { get; init; } = Array.Empty<ApparelSlotStatus>();

    /// <summary>左手持械（空手 null）。双手握一把时左右手指向同一把。</summary>
    public WeaponInfo? LeftHandWeapon { get; init; }

    /// <summary>右手持械（空手 null）。</summary>
    public WeaponInfo? RightHandWeapon { get; init; }

    /// <summary>握持态（单手/双手/双持）。</summary>
    public GripMode Grip { get; init; }
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
/// 一个"可装假肢的肢体槽位"快照：对应一只手（操作单位）或一只脚（移动单位）。
/// 只有被切除/损毁（<see cref="IsAmputated"/>）且尚未被假肢覆盖（<see cref="HasProsthetic"/>）的槽才是空槽（<see cref="CanEquip"/>）。
/// 覆盖判定复刻引擎的贪心分配（同区高等级假肢优先抵扣一个失去的单位），故与净惩罚口径一致。
/// </summary>
public sealed class ProstheticSlot
{
    /// <summary>判定切除的单位部位名（手=手掌本体，腿=脚掌本体；如 "左手" / "左脚"）。</summary>
    public string UnitPartName { get; init; } = "";

    /// <summary>装假肢时传给 <see cref="Prosthetic.OfGrade"/> 的取代区域（手→Hand / 腿→Leg）。</summary>
    public BodyRegion ReplacesRegion { get; init; }

    /// <summary>该单位是否已失去（切除/损毁，含切腿连带脚）。空槽前提。</summary>
    public bool IsAmputated { get; init; }

    /// <summary>该失去单位是否已被一个假肢覆盖。</summary>
    public bool HasProsthetic { get; init; }

    /// <summary>已装假肢的等级（未装为 null）。</summary>
    public ProstheticGrade? Grade { get; init; }

    /// <summary>已装假肢的显示名（未装为 null）。</summary>
    public string? ProstheticName { get; init; }

    /// <summary>可装假肢：已失去且未被覆盖。</summary>
    public bool CanEquip => IsAmputated && !HasProsthetic;
}

/// <summary>
/// 单条感染的只读态：部位（系统性感染可空）+ 严重度 0..1。取自 <see cref="HealthConditionSet"/> 里
/// <see cref="HealthConditionType.Infection"/> 病状——UI 用它做头顶/卡牌/角色面板的常驻感染指示（此前仅医疗面板可见）。
/// </summary>
public sealed class InfectionStatus
{
    public string? BodyPart { get; init; }
    public double Severity { get; init; }
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

    /// <summary>饥饿刻度序号（0=饿死 … 5=正常 … 6=吃撑），即拍照时 <see cref="HungerLevel"/> 的枚举序号。</summary>
    public int HungerStage { get; init; }

    /// <summary>饥饿等级中文名（拍照时 HungerLevel 的显示名）。纯字符串快照，UI 直接显示。</summary>
    public string HungerLabel { get; init; } = "正常";

    public IReadOnlyList<PartStatus> Parts { get; init; } = Array.Empty<PartStatus>();

    /// <summary>当前感染病状（取自伤病集，无 = 空）。供常驻感染可见性（头顶 glyph / 卡牌病征点 / 角色面板健康行）。</summary>
    public IReadOnlyList<InfectionStatus> Infections { get; init; } = Array.Empty<InfectionStatus>();

    /// <summary>是否存在任一感染（供 UI 一眼判定）。</summary>
    public bool HasInfection => Infections.Count > 0;

    /// <summary>感染最高严重度 0..1（无感染=0）。供 UI 按严重度着色（越重越红），呼应 spec"抗生素赌局"的可治窗口。</summary>
    public double MaxInfectionSeverity
    {
        get
        {
            double max = 0;
            foreach (InfectionStatus i in Infections)
            {
                if (i.Severity > max) max = i.Severity;
            }
            return max;
        }
    }

    /// <summary>可装假肢的肢体槽（两手 + 两脚，共 4 个）；UI 据此为空槽提供装假肢入口。</summary>
    public IReadOnlyList<ProstheticSlot> ProstheticSlots { get; init; } = Array.Empty<ProstheticSlot>();

    /// <summary>持械信息，无武器时为 null。</summary>
    public WeaponInfo? Weapon { get; init; }

    public IReadOnlyList<ArmorInfo> Armor { get; init; } = Array.Empty<ArmorInfo>();

    /// <summary>
    /// 由 live 战斗对象拍出一份只读快照。遍历 <see cref="Body.Parts"/> 逐部位取当前 HP/上限/
    /// 区域/分类/父名 + 切除/损毁/失能/骨折/流血标记；武器/护甲摊平字段。
    /// </summary>
    /// <param name="hungerStage">饥饿刻度序号（0=饿死…5=正常…6=吃撑），由调用方从 Pawn.Hunger 取；默认正常(5)。</param>
    /// <param name="hungerLabel">饥饿等级中文名，由调用方从 Pawn.Hunger 取；null 视为"正常"。</param>
    /// <param name="conditions">该单位当前伤病集（由调用方从 Pawn.Health.Conditions 传；非 Pawn/无档=null）。仅抽取感染做常驻可见性，
    /// 出血/骨折仍走 Body 的逐部位判定（<see cref="PartStatus.IsBleeding"/>/<see cref="PartStatus.IsFractured"/>），不重复。</param>
    public static PawnInspection FromBody(
        Body body, Weapon? weapon, IReadOnlyList<ArmorLayer>? armor, string name,
        int hungerStage = (int)HungerLevel.Sated, string? hungerLabel = null,
        IReadOnlyList<HealthCondition>? conditions = null)
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

        WeaponInfo? weaponInfo = WeaponInfo.From(weapon);

        var armorInfos = (armor ?? Array.Empty<ArmorLayer>()).Select(a => new ArmorInfo
        {
            Name = a.Name,
            SharpDefense = a.SharpDefense,
            BluntDefense = a.BluntDefense,
            Slot = a.Slot,
        }).ToList();

        var prostheticSlots = new List<ProstheticSlot>();
        // 操作单位=手（假肢取代 Hand）；移动单位=脚（假肢取代 Leg，切腿连带脚 gone → 该腿槽算空）。
        AddProstheticSlots(body, BodyRegion.Hand, BodyRegion.Hand, prostheticSlots);
        AddProstheticSlots(body, BodyRegion.Foot, BodyRegion.Leg, prostheticSlots);

        // 感染抽取（只映射引擎真实病状，不发明）：从伤病集取 Infection 型，供常驻可见性。
        var infections = (conditions ?? Array.Empty<HealthCondition>())
            .Where(c => c.Type == HealthConditionType.Infection)
            .Select(c => new InfectionStatus { BodyPart = c.BodyPart, Severity = c.Severity })
            .ToList();

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
            Infections = infections,
            ProstheticSlots = prostheticSlots,
            Weapon = weaponInfo,
            Armor = armorInfos,
        };
    }

    /// <summary>
    /// 组装某类肢体的假肢槽。<paramref name="unitRegion"/> 为惩罚单位（Hand/Foot），
    /// <paramref name="replacesRegion"/> 为假肢取代区域（Hand/Leg）。按 <see cref="Body.Parts"/> 的迭代序
    /// 遍历单位（与引擎 <see cref="Body.RecalculatePenalties"/> 同序），对失去的单位贪心分配同区假肢
    /// （高等级优先，与引擎一致），使"哪个槽被覆盖/哪个仍空"与净惩罚口径吻合。
    /// </summary>
    private static void AddProstheticSlots(
        Body body, BodyRegion unitRegion, BodyRegion replacesRegion, List<ProstheticSlot> outSlots)
    {
        var gradesForGone = body.Prosthetics
            .Where(pr => pr.ReplacesRegion == replacesRegion)
            .OrderByDescending(pr => pr.RestoreRatio)
            .ToList();
        int coverIdx = 0;

        foreach (var unit in body.Parts.Values.Where(p => p.Region == unitRegion))
        {
            bool amputated = body.IsGone(unit.Name);
            bool covered = false;
            ProstheticGrade? grade = null;
            string? name = null;
            if (amputated && coverIdx < gradesForGone.Count)
            {
                covered = true;
                grade = gradesForGone[coverIdx].Grade;
                name = gradesForGone[coverIdx].Name;
                coverIdx++;
            }

            outSlots.Add(new ProstheticSlot
            {
                UnitPartName = unit.Name,
                ReplacesRegion = replacesRegion,
                IsAmputated = amputated,
                HasProsthetic = covered,
                Grade = grade,
                ProstheticName = name,
            });
        }
    }
}
