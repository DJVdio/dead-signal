using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 消费层**，不得引入任何 Godot 类型（被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间问题（弹道飞行/碰撞）归 Godot 实时层；本层只负责调用引擎既有 API（Resolve → 效果结算）。

/// <summary>
/// 原型期武器/护甲/部位数据工厂 + 一次攻击的封装。
/// 数值取自设计文档第 5 节（穿透口径直接照抄，其余为原型拟定，待蒙特卡洛拉表微调）。
/// 纯数据与规则调用，不含任何 Godot 类型，方便与 Sim 层共用。
/// </summary>
public static class CombatData
{
    // ---- 武器 ----

    /// <summary>手枪：中距离锐器，穿透 15%（文档：手枪 15%）。远程有误差角。</summary>
    public static Weapon Pistol() => new()
    {
        Name = "手枪",
        DamageMin = 8,
        DamageMax = 14,
        Penetration = 0.15,
        DamageType = DamageType.Sharp,
        TwoHanded = false,
        CanDualWield = true,
        IsRanged = true,
        BaseSpreadDegrees = 3,   // 拟定待调（Sim 手枪基线 3°）
        AttackInterval = 0.5,    // 拟定待调（秒/次；实时层攻速可另调）
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
        AttackInterval = 0.7,    // 拟定待调
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
        AttackInterval = 1.3,    // 拟定待调
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

    // ---- 部位（接入引擎细部位表：15 细部位，含 MaxHp/Region/Category/树形父子） ----

    /// <summary>新建一具满血人形躯体（幸存者与丧尸同用人体细部位表）。</summary>
    public static Body NewHumanoidBody() => HumanBody.NewBody();
}

/// <summary>弹道命中一具躯体时的处理决策。</summary>
public enum ProjectileContact
{
    /// <summary>结算这次命中（敌对，或不在架肩豁免内的友军误伤）。</summary>
    Hit,

    /// <summary>穿过继续飞（紧贴射手的同阵营队友——视作架在其肩上射击，不误伤）。</summary>
    PassThrough,
}

/// <summary>
/// 友军误伤判定（纯函数）。用户口径（拍板）：允许友军误伤，但当队友与射手**紧贴**
/// （在架肩间距阈值内）时视作架在其肩上射击、不造成友伤——弹道穿过该队友继续飞。
/// 敌对阵营正常命中；同阵营在紧贴阈值之外可被击中（真实向友伤）。
/// 阈值本身（像素/肩宽量级）由空间层给出，本函数只做纯比较，便于单测。
/// </summary>
public static class FriendlyFire
{
    /// <summary>
    /// 决定弹道命中某 Actor 时的处理。
    /// </summary>
    /// <param name="sameFaction">被命中者是否与射手同阵营。</param>
    /// <param name="distanceToShooter">被命中者与射手的间距（与阈值同单位）。</param>
    /// <param name="shoulderGraceDistance">架肩豁免间距阈值（含）；≤ 此距离的同阵营队友被穿过。</param>
    public static ProjectileContact Resolve(
        bool sameFaction, double distanceToShooter, double shoulderGraceDistance)
    {
        if (!sameFaction)
        {
            return ProjectileContact.Hit; // 敌对：正常命中
        }

        // 同阵营：紧贴（架肩）豁免则穿过，否则友伤命中。
        return distanceToShooter <= shoulderGraceDistance
            ? ProjectileContact.PassThrough
            : ProjectileContact.Hit;
    }
}

/// <summary>
/// 一次攻击的规则输出，供表现层浮字/血条使用。
/// 伤害已在结算时施加到防御方 <see cref="Body"/>；本结构只是把结果摊平给表现层读。
/// </summary>
public readonly struct AttackOutcome
{
    /// <summary>实际作用到部位的伤害（0 = 被甲完全挡下）。</summary>
    public readonly int Damage;
    public readonly string PartName;
    public readonly DamageType FinalType;

    /// <summary>被护甲完全挡下（伤害 0）。</summary>
    public readonly bool Blocked;
    /// <summary>本次命中造成了切除。</summary>
    public readonly bool Severed;
    /// <summary>本次命中触发了流血伤口。</summary>
    public readonly bool Bled;
    /// <summary>本次命中触发了震荡。</summary>
    public readonly bool Concussed;
    /// <summary>本次命中触发了骨折。</summary>
    public readonly bool Fractured;
    /// <summary>本次命中后防御方死亡（含斩首/开膛/失血致死）。</summary>
    public readonly bool Died;

    public AttackOutcome(
        int damage, string partName, DamageType finalType,
        bool blocked, bool severed, bool bled, bool concussed, bool fractured, bool died)
    {
        Damage = damage;
        PartName = partName;
        FinalType = finalType;
        Blocked = blocked;
        Severed = severed;
        Bled = bled;
        Concussed = concussed;
        Fractured = fractured;
        Died = died;
    }
}

/// <summary>
/// 把命中选择器 + 逐层结算 + 效果结算包成一次"攻击者打防御者躯体"的调用。
/// 消费引擎既有 API：<see cref="VolumeWeightedHitSelector"/> 选部位 → <see cref="CombatResolver"/>
/// 逐层护甲结算 → <see cref="CombatEffectResolver"/> 施加到 <see cref="Body"/>（扣 HP/流血/切除/震荡/骨折/致死）。
/// </summary>
public sealed class CombatEngine
{
    private readonly IRandomSource _rng;
    private readonly CombatResolver _resolver;
    private readonly VolumeWeightedHitSelector _hitSelector;
    private readonly CombatEffectResolver _effectResolver;

    public CombatEngine(int? seed = null)
        : this(new SystemRandomSource(seed))
    {
    }

    /// <summary>测试用：注入确定性随机源。</summary>
    public CombatEngine(IRandomSource rng)
    {
        _rng = rng;
        _resolver = new CombatResolver(_rng);
        _hitSelector = new VolumeWeightedHitSelector(_rng);
        _effectResolver = new CombatEffectResolver(_rng);
    }

    /// <summary>
    /// 结算一次命中：在防御方当前尚存的部位中按体积加权选一处 → 逐层护甲结算 →
    /// 走效果结算施加到 <paramref name="defenderBody"/> → 摊平结果给表现层。
    /// </summary>
    public AttackOutcome ResolveHit(
        Weapon weapon,
        IReadOnlyList<ArmorLayer> defenderArmor,
        Body defenderBody)
    {
        // 只在尚存（未切除/未损毁）的部位里选，避免把伤害"打"到已消失的部位上。
        var candidates = defenderBody.Parts.Values
            .Where(p => !defenderBody.IsGone(p.Name))
            .ToList();

        BodyPart part = _hitSelector.Select(candidates);
        IReadOnlyList<ArmorLayer> ordered = CombatResolver.OrderOuterToInner(defenderArmor);
        CombatResult result = _resolver.Resolve(weapon, ordered, part);
        EffectOutcome fx = _effectResolver.Apply(defenderBody, weapon, result);

        bool bled = false, concussed = false, fractured = false;
        foreach (DamageEffect e in fx.Effects)
        {
            switch (e.Kind)
            {
                case DamageEffectKind.Bleed: bled = true; break;
                case DamageEffectKind.Concussion: concussed = true; break;
                case DamageEffectKind.Fracture: fractured = true; break;
            }
        }

        return new AttackOutcome(
            damage: result.FinalDamage,
            partName: part.Name,
            finalType: result.FinalDamageType,
            blocked: result.FinalDamage <= 0,
            severed: fx.SeveredParts.Count > 0,
            bled: bled,
            concussed: concussed,
            fractured: fractured,
            died: fx.CausedDeath || defenderBody.IsDead);
    }

    /// <summary>把准星方向（度）叠加一次远程误差角锥采样，返回实际射击方向（度）。近战 spread=0 即恒为准星方向。</summary>
    public double SampleShotDirectionDegrees(double aimDegrees, double spreadDegrees) =>
        Ballistics.SampleShotDirectionDegrees(aimDegrees, spreadDegrees, _rng);
}
