using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 原型期武器/护甲/部位数据工厂 + 一次攻击的封装。
/// 数值取自设计文档第 5 节（穿透口径直接照抄，伤害区间为原型拟定，待蒙特卡洛拉表微调）。
/// 纯数据与规则调用，不含任何 Godot 类型，方便后续搬到独立 Sim 层。
/// </summary>
public static class CombatData
{
    // ---- 武器 ----

    /// <summary>手枪：中距离锐器，穿透 15%（文档：手枪 15%）。</summary>
    public static Weapon Pistol() => new()
    {
        Name = "手枪",
        DamageMin = 8,
        DamageMax = 14,
        Penetration = 0.15,
        DamageType = DamageType.Sharp,
        TwoHanded = false,
        CanDualWield = true,
    };

    /// <summary>匕首：近战锐器，穿透 9%（文档：匕首 9%）。</summary>
    public static Weapon Dagger() => new()
    {
        Name = "匕首",
        DamageMin = 4,
        DamageMax = 10,
        Penetration = 0.09,
        DamageType = DamageType.Sharp,
        TwoHanded = false,
        CanDualWield = true,
    };

    /// <summary>丧尸爪击：近战钝器，穿透 3%（文档：棍棒级 3%）。天然钝器逐层保留自身穿透。</summary>
    public static Weapon ZombieClaw() => new()
    {
        Name = "爪击",
        DamageMin = 3,
        DamageMax = 9,
        Penetration = 0.03,
        DamageType = DamageType.Blunt,
        TwoHanded = false,
        CanDualWield = false,
    };

    // ---- 护甲（从外到内已排序，Resolve 前仍会经 OrderOuterToInner 归一） ----

    /// <summary>幸存者：外套 + 贴身布衣两层。</summary>
    public static IReadOnlyList<ArmorLayer> SurvivorArmor() => new[]
    {
        new ArmorLayer { Name = "皮夹克", SharpDefense = 6, BluntDefense = 3, Weight = 3, Slot = ArmorSlot.Outer },
        new ArmorLayer { Name = "贴身布衣", SharpDefense = 2, BluntDefense = 1, Weight = 1, Slot = ArmorSlot.Skin },
    };

    /// <summary>丧尸：一层腐烂硬皮（对钝器略韧）。</summary>
    public static IReadOnlyList<ArmorLayer> ZombieHide() => new[]
    {
        new ArmorLayer { Name = "腐皮", SharpDefense = 1.5, BluntDefense = 3, Weight = 0, Slot = ArmorSlot.Skin },
    };

    // ---- 部位（体积加权，粒度到躯干/头/四肢，细部位后续填数据） ----

    public static IReadOnlyList<BodyPart> HumanoidParts() => new[]
    {
        new BodyPart { Name = "头部", VolumeWeight = 1.0 },
        new BodyPart { Name = "躯干", VolumeWeight = 5.0 },
        new BodyPart { Name = "左臂", VolumeWeight = 1.5 },
        new BodyPart { Name = "右臂", VolumeWeight = 1.5 },
        new BodyPart { Name = "左腿", VolumeWeight = 2.0 },
        new BodyPart { Name = "右腿", VolumeWeight = 2.0 },
    };
}

/// <summary>一次攻击的规则输出，供表现层浮字/血条使用。</summary>
public readonly struct AttackOutcome
{
    public readonly int Damage;
    public readonly string PartName;
    public readonly bool Terminated;
    public readonly DamageType FinalType;

    public AttackOutcome(int damage, string partName, bool terminated, DamageType finalType)
    {
        Damage = damage;
        PartName = partName;
        Terminated = terminated;
        FinalType = finalType;
    }
}

/// <summary>把 CombatResolver + 命中选择器包成一次"攻击者打防御者"的调用。</summary>
public sealed class CombatEngine
{
    private readonly IRandomSource _rng;
    private readonly CombatResolver _resolver;
    private readonly VolumeWeightedHitSelector _hitSelector;

    public CombatEngine(int? seed = null)
    {
        _rng = new SystemRandomSource(seed);
        _resolver = new CombatResolver(_rng);
        _hitSelector = new VolumeWeightedHitSelector(_rng);
    }

    /// <summary>结算一次命中：选部位 → 逐层护甲结算 → 返回作用到部位的伤害。</summary>
    public AttackOutcome ResolveHit(
        Weapon weapon,
        IReadOnlyList<ArmorLayer> defenderArmor,
        IReadOnlyList<BodyPart> defenderParts)
    {
        BodyPart part = _hitSelector.Select(defenderParts);
        IReadOnlyList<ArmorLayer> ordered = CombatResolver.OrderOuterToInner(defenderArmor);
        CombatResult result = _resolver.Resolve(weapon, ordered, part);
        return new AttackOutcome(result.FinalDamage, part.Name, result.Terminated, result.FinalDamageType);
    }
}
