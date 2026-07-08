using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 武器改装目录（数据，非逻辑）：三类武器（枪械/近战锐器/近战钝器）的改装清单，可扩。
/// 数值全为"拟定待调"（draft），靠 Sim 拉表校准方向；此处只定形态。
/// 合成走通用的 <see cref="WeaponMods.ApplyMods"/>，改装本身在这里作为数据列举。
/// </summary>
public static class WeaponModCatalog
{
    // ============ 枪械改装（对标"猎枪"示例；按大类适用，故所有长枪通用）============

    /// <summary>轻质化枪托：全枪减重 → 射速↑、伤害略↓、枪托近战也更快。</summary>
    public static WeaponMod LightenedStock() => new()
    {
        Name = "轻质化枪托",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Stock,
        Note = "全枪减重：射速↑，单发伤害略↓。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.85),   // 射速↑（间隔↓）
            StatMod.Mul(WeaponStat.DamageMax, 0.92),        // 伤↓
            StatMod.Mul(WeaponStat.StockMeleeInterval, 0.9),
        },
    };

    /// <summary>截短枪管：机动近战化 → 射程↓、误差角↑、射速略↑。</summary>
    public static WeaponMod SawnOffBarrel() => new()
    {
        Name = "截短枪管",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Barrel,
        Note = "短管：射程↓、精度↓、贴脸出手略快。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.MaxRange, 0.6),
            StatMod.Mul(WeaponStat.FalloffStart, 0.6),
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, 1.4),  // 误差角↑
            StatMod.Mul(WeaponStat.AttackInterval, 0.9),     // 略快
        },
    };

    /// <summary>加长枪管：射程↑、更准、射速略↓。</summary>
    public static WeaponMod ExtendedBarrel() => new()
    {
        Name = "加长枪管",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Barrel,
        Note = "长管：射程↑、精度↑、出手略慢。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.MaxRange, 1.3),
            StatMod.Mul(WeaponStat.FalloffStart, 1.3),
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, 0.85),  // 误差角↓（更准）
            StatMod.Mul(WeaponStat.AttackInterval, 1.1),      // 略慢
        },
    };

    /// <summary>刺刀型：枪口挂刺刀 → 枪托近战改为锐击突刺（穿透高、伤更高）。占"枪口"部位，可与枪管改装共存。</summary>
    public static WeaponMod Bayonet() => new()
    {
        Name = "刺刀型",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Muzzle,
        Note = "挂刺刀：贴脸近战变锐击突刺，穿透/伤害↑。draft",
        StockMeleeTransform = b => new Weapon
        {
            Name = b.Name + "（刺刀）",
            DamageMin = (b.StockMeleeDamageMin ?? 3) + 3,        // draft
            DamageMax = (b.StockMeleeDamageMax ?? 6) + 6,        // draft
            Penetration = (b.StockMeleePenetration ?? 0.02) + 0.13,  // 锐击突刺穿透↑ draft
            DamageType = DamageType.Sharp,
            IsRanged = false,
            TwoHanded = b.TwoHanded,
            AttackInterval = (b.StockMeleeInterval ?? b.AttackInterval) * 0.9,  // 突刺略快 draft
        },
    };

    /// <summary>利爪型：枪托绑利刃 → 枪托近战改为锐击挥砍（比刺刀伤略高、穿透略低）。占"枪托"部位。</summary>
    public static WeaponMod ClawStock() => new()
    {
        Name = "利爪型",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Stock,
        Note = "枪托绑利刃：贴脸近战变锐击挥砍，伤↑。draft",
        StockMeleeTransform = b => new Weapon
        {
            Name = b.Name + "（利爪）",
            DamageMin = (b.StockMeleeDamageMin ?? 3) + 4,        // draft
            DamageMax = (b.StockMeleeDamageMax ?? 6) + 8,        // draft
            Penetration = (b.StockMeleePenetration ?? 0.02) + 0.08,  // 挥砍穿透略低于刺刀 draft
            DamageType = DamageType.Sharp,
            IsRanged = false,
            TwoHanded = b.TwoHanded,
            AttackInterval = (b.StockMeleeInterval ?? b.AttackInterval) * 1.0,  // draft
        },
    };

    /// <summary>创伤型：枪托改铁锤 → 枪托近战仍是钝击，但伤更高、更慢；换手代价是射击精度略降。占"枪托"部位。</summary>
    public static WeaponMod TraumaStock() => new()
    {
        Name = "创伤型",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Stock,
        Note = "枪托改铁锤：贴脸钝击伤↑、更慢；持握变差，射击精度略↓。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.StockMeleeDamageMin, 1.6),
            StatMod.Mul(WeaponStat.StockMeleeDamageMax, 1.6),
            StatMod.Mul(WeaponStat.StockMeleeInterval, 1.2),   // 更慢
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, 1.1),    // 射击精度略↓
        },
    };

    // ============ 近战锐器改装（对标"短剑"示例；剑/匕首/刺剑通用）============

    /// <summary>锯齿剑刃：撕裂伤 → 伤害上限↑（流血倾向暂借伤害表达，Weapon 无独立流血字段）。</summary>
    public static WeaponMod SerratedBlade() => new()
    {
        Name = "锯齿剑刃",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Blade,
        Note = "锯齿：撕裂伤↑（流血倾向暂借伤害上限表达）。draft",
        Stats = new[]
        {
            StatMod.Add(WeaponStat.DamageMax, 3),   // draft
        },
    };

    /// <summary>锋刃研磨：开刃 → 穿透↑。</summary>
    public static WeaponMod HonedEdge() => new()
    {
        Name = "锋刃研磨",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Blade,
        Note = "开刃：穿透↑。draft",
        Stats = new[]
        {
            StatMod.Add(WeaponStat.Penetration, 0.05),  // draft
        },
    };

    /// <summary>镂空剑刃：减重 → 攻速↑、伤略↓。</summary>
    public static WeaponMod FullerBlade() => new()
    {
        Name = "镂空剑刃",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Blade,
        Note = "镂空减重：攻速↑、伤略↓。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.88),
            StatMod.Mul(WeaponStat.DamageMax, 0.95),
        },
    };

    /// <summary>加重剑柄：配重 → 伤↑、攻速↓。</summary>
    public static WeaponMod WeightedHandle() => new()
    {
        Name = "加重剑柄",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Handle,
        Note = "配重柄：伤↑、攻速↓。draft",
        Stats = new[]
        {
            StatMod.Add(WeaponStat.DamageMin, 2),
            StatMod.Add(WeaponStat.DamageMax, 3),
            StatMod.Mul(WeaponStat.AttackInterval, 1.15),
        },
    };

    /// <summary>轻质化剑柄：减重 → 攻速↑、伤略↓。</summary>
    public static WeaponMod LightenedHandle() => new()
    {
        Name = "轻质化剑柄",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Handle,
        Note = "减重柄：攻速↑、伤略↓。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.85),
            StatMod.Mul(WeaponStat.DamageMax, 0.92),
        },
    };

    /// <summary>防滑缠手（锐器）：握持更稳 → 出手更利落（攻速↑）。近战必中，故"命中↑"在引擎里无近战字段，借攻速表达。</summary>
    public static WeaponMod GripWrapBlade() => new()
    {
        Name = "防滑缠手",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Grip,
        Note = "缠手防滑：出手更利落（攻速↑）。近战必中，命中项无引擎字段。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.95),
        },
    };

    // ============ 近战钝器改装（对标"棍棒"示例；棍/锤通用）============

    /// <summary>铁丝强化：缠铁丝加固 → 伤↑。</summary>
    public static WeaponMod WireWrap() => new()
    {
        Name = "铁丝强化",
        RequiredClass = WeaponClass.Blunt,
        Part = WeaponPart.Shaft,
        Note = "缠铁丝：伤↑。draft",
        Stats = new[]
        {
            StatMod.Add(WeaponStat.DamageMin, 1),
            StatMod.Add(WeaponStat.DamageMax, 2),
        },
    };

    /// <summary>钉子强化：钉刺 → 伤↑、并带一点穿透。</summary>
    public static WeaponMod NailStuds() => new()
    {
        Name = "钉子强化",
        RequiredClass = WeaponClass.Blunt,
        Part = WeaponPart.Shaft,
        Note = "钉刺：伤↑、穿透↑（钉尖破防）。draft",
        Stats = new[]
        {
            StatMod.Add(WeaponStat.DamageMax, 3),
            StatMod.Add(WeaponStat.Penetration, 0.03),
        },
    };

    /// <summary>防滑缠手（钝器）：握持更稳 → 攻速↑。</summary>
    public static WeaponMod GripWrapBlunt() => new()
    {
        Name = "防滑缠手",
        RequiredClass = WeaponClass.Blunt,
        Part = WeaponPart.Grip,
        Note = "缠手防滑：抡起更利落（攻速↑）。draft",
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.95),
        },
    };

    /// <summary>某武器大类的全部可用改装（供改装 UI 列举）。</summary>
    public static IReadOnlyList<WeaponMod> For(WeaponClass cls) => cls switch
    {
        WeaponClass.Firearm => new[]
        {
            LightenedStock(), SawnOffBarrel(), ExtendedBarrel(), Bayonet(), ClawStock(), TraumaStock(),
        },
        WeaponClass.Blade => new[]
        {
            SerratedBlade(), HonedEdge(), FullerBlade(), WeightedHandle(), LightenedHandle(), GripWrapBlade(),
        },
        WeaponClass.Blunt => new[]
        {
            WireWrap(), NailStuds(), GripWrapBlunt(),
        },
        _ => System.Array.Empty<WeaponMod>(),
    };

    /// <summary>某具体武器可用的全部改装（按其大类派生）。</summary>
    public static IReadOnlyList<WeaponMod> For(Weapon weapon) => For(WeaponMods.ClassOf(weapon));

    /// <summary>全部改装（含跨类同名"防滑缠手"两条）。</summary>
    public static IReadOnlyList<WeaponMod> All()
    {
        var all = new List<WeaponMod>();
        all.AddRange(For(WeaponClass.Firearm));
        all.AddRange(For(WeaponClass.Blade));
        all.AddRange(For(WeaponClass.Blunt));
        return all;
    }
}
