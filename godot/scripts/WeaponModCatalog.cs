using System.Collections.Generic;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 武器改装目录（数据，非逻辑）：三类武器（枪械/近战锐器/近战钝器）的改装清单，可扩。
/// 数值全为"拟定待调"（draft），靠 Sim 拉表校准方向；此处只定形态。
/// 合成走通用的 <see cref="WeaponMods.ApplyMods"/>，改装本身在这里作为数据列举。
/// <para>
/// <b>三种近战型态（利爪 / 创伤 / 刺刀）的数值口径</b>——它们是本目录的重头戏，用户点名要的：
/// 全部用**乘算**（不用加算）作用在**这把枪自己的枪托数值**上。理由：加算会让轻枪吃到不成比例的收益
/// （给手枪的枪托 +4 伤，等于把它从 3~6 拉到 7~10，一把手枪的柄砸得比步枪托还狠，荒唐），
/// 乘算则**天然随枪身量级缩放**——重枪改出来的近战件就是更重。这也与项目通则"百分比加成一律乘算"一致。
/// </para>
/// <para>
/// <b>三者落在哪一档</b>：基础枪托 DPS ≈ 2.7~2.8（介于拳脚 2.08 与匕首 2.86 之间，见 <c>WeaponTable</c> 枪托段），
/// 三种型态把它抬到 <b>3.4 ~ 4.5</b> —— 即"**一把像样的近战武器的下限档**（匕首 2.86 ＜ 它 ＜ 棍棒 4.79）"。
/// 刻意**够不到长剑/尖头锤那一档**：改装能让你"打空了还能打"，但不该让一把枪同时是全场最好的近战武器。
/// </para>
/// </summary>
public static class WeaponModCatalog
{
    private static IReadOnlyDictionary<string, int> Cost(params (string Key, int Qty)[] items)
    {
        var d = new Dictionary<string, int>();
        foreach (var (key, qty) in items)
        {
            d[key] = qty;
        }
        return d;
    }

    // ============ 枪械改装（按大类适用，故所有枪通用）============

    /// <summary>轻质化枪托：全枪减重 → 射速↑、伤害略↓、枪托近战也更快。</summary>
    public static WeaponMod LightenedStock() => new()
    {
        Id = "lightened_stock",
        Name = "轻质化枪托",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Stock,
        Note = "全枪减重：射速↑，单发伤害略↓。draft",
        MaterialCosts = Cost(("wood", 1), ("scrap_metal", 1)),
        WorkMinutes = 50,
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
        Id = "sawn_off_barrel",
        Name = "截短枪管",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Barrel,
        Note = "短管：射程↓、精度↓、贴脸出手略快。draft",
        MaterialCosts = Cost(("scrap_metal", 1)),
        WorkMinutes = 40,
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
        Id = "extended_barrel",
        Name = "加长枪管",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Barrel,
        Note = "长管：射程↑、精度↑、出手略慢。draft",
        MaterialCosts = Cost(("metal_ingot", 1), ("scrap_metal", 1)),
        WorkMinutes = 70,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.MaxRange, 1.3),
            StatMod.Mul(WeaponStat.FalloffStart, 1.3),
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, 0.85),  // 误差角↓（更准）
            StatMod.Mul(WeaponStat.AttackInterval, 1.1),      // 略慢
        },
    };

    // ---- 三种近战型态（利爪 / 创伤 / 刺刀）：一把枪三选一 ----
    // 部位安排使这条规则**双重成立**：利爪与创伤都占「枪托」⇒ 天然互斥；刺刀占「枪口」，
    // 与前两者不同部位，故还要靠 MeleeForm 的"至多一种型态"规则挡住（见 WeaponMods.ApplyMods）。

    /// <summary>
    /// 刺刀型（枪口）：**刺击穿透**。全型态最高穿透（20%）、出手最快最安静，单击伤害不及利爪。
    /// 用户语义"刺刀=刺击穿透"；引擎只有 Sharp/Blunt 两型 ⇒ 归**锐击**，"刺"的特性由**穿透**表达。
    /// </summary>
    public static WeaponMod Bayonet() => new()
    {
        Id = "bayonet",
        Name = "刺刀型",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Muzzle,
        Form = MeleeForm.Bayonet,
        Note = "枪口挂刺刀：贴脸变锐击突刺，穿透 20%（全型态最高）、出手最快最安静，单击伤害不及利爪。draft",
        MaterialCosts = Cost(("metal_ingot", 1), ("scrap_metal", 2), ("rope", 1)),
        WorkMinutes = 90,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.StockMeleeDamageMin, 1.35),
            StatMod.Mul(WeaponStat.StockMeleeDamageMax, 1.35),
            StatMod.Set(WeaponStat.StockMeleePenetration, 0.20),  // 突刺集中破甲：全型态最高
            StatMod.Mul(WeaponStat.StockMeleeInterval, 0.85),     // 捅比抡快
            StatMod.Mul(WeaponStat.StockMeleeNoiseRadius, 0.8),   // 捅比砸安静
        },
    };

    /// <summary>
    /// 利爪型（枪托）：**锐器切割**。三型态里**单击伤害最高**（×1.50），穿透中等（10%），出手节奏不变。
    /// </summary>
    public static WeaponMod ClawStock() => new()
    {
        Id = "claw_stock",
        Name = "利爪型",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Stock,
        Form = MeleeForm.Claw,
        Note = "枪托绑利刃：贴脸变锐击挥砍，伤害最高、穿透中等（10%）、节奏不变。draft",
        MaterialCosts = Cost(("scrap_metal", 3), ("leather", 1), ("nails", 2)),
        WorkMinutes = 80,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.StockMeleeDamageMin, 1.50),
            StatMod.Mul(WeaponStat.StockMeleeDamageMax, 1.50),
            StatMod.Set(WeaponStat.StockMeleePenetration, 0.10),  // 切割穿透低于突刺
            // 节奏与噪音不变（绑一片刃不改变你抡它的方式）
        },
    };

    /// <summary>
    /// 创伤型（枪托）：**钝伤加重**。单击最重（×1.60）、最慢、最响，且**代价最实在**——
    /// 枪托改成铁疙瘩后持握变差，**射击精度也跟着降**（唯一一条会削弱枪本职工作的近战型态）。
    /// </summary>
    public static WeaponMod TraumaStock() => new()
    {
        Id = "trauma_stock",
        Name = "创伤型",
        RequiredClass = WeaponClass.Firearm,
        Part = WeaponPart.Stock,
        Form = MeleeForm.Trauma,
        Note = "枪托改铁锤：贴脸钝击最重、更慢更响；持握变差 → 射击精度↓（唯一有射击代价的型态）。draft",
        MaterialCosts = Cost(("metal_ingot", 1), ("scrap_metal", 2), ("nails", 4)),
        WorkMinutes = 100,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.StockMeleeDamageMin, 1.60),
            StatMod.Mul(WeaponStat.StockMeleeDamageMax, 1.60),
            StatMod.Add(WeaponStat.StockMeleePenetration, 0.03),  // 铁疙瘩略破甲
            StatMod.Mul(WeaponStat.StockMeleeInterval, 1.25),     // 更慢
            StatMod.Mul(WeaponStat.StockMeleeNoiseRadius, 1.2),   // 更响
            StatMod.Mul(WeaponStat.BaseSpreadDegrees, 1.1),       // ← 代价：射击精度↓
        },
    };

    // ============ 近战锐器改装（对标"短剑"示例；剑/匕首/刺剑通用）============

    /// <summary>锯齿剑刃：撕裂伤 → 伤害上限↑（流血倾向暂借伤害表达，Weapon 无独立流血字段）。</summary>
    public static WeaponMod SerratedBlade() => new()
    {
        Id = "serrated_blade",
        Name = "锯齿剑刃",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Blade,
        Note = "锯齿：撕裂伤↑（流血倾向暂借伤害上限表达）。draft",
        MaterialCosts = Cost(("scrap_metal", 2)),
        WorkMinutes = 50,
        Stats = new[]
        {
            StatMod.Add(WeaponStat.DamageMax, 3),   // draft
        },
    };

    /// <summary>锋刃研磨：开刃 → 穿透↑。</summary>
    public static WeaponMod HonedEdge() => new()
    {
        Id = "honed_edge",
        Name = "锋刃研磨",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Blade,
        Note = "开刃：穿透↑。draft",
        MaterialCosts = Cost(("stone", 2)),
        WorkMinutes = 30,
        Stats = new[]
        {
            StatMod.Add(WeaponStat.Penetration, 0.05),  // draft
        },
    };

    /// <summary>镂空剑刃：减重 → 攻速↑、伤略↓。</summary>
    public static WeaponMod FullerBlade() => new()
    {
        Id = "fuller_blade",
        Name = "镂空剑刃",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Blade,
        Note = "镂空减重：攻速↑、伤略↓。draft",
        MaterialCosts = Cost(("scrap_metal", 1)),
        WorkMinutes = 45,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.88),
            StatMod.Mul(WeaponStat.DamageMax, 0.95),
        },
    };

    /// <summary>加重剑柄：配重 → 伤↑、攻速↓。</summary>
    public static WeaponMod WeightedHandle() => new()
    {
        Id = "weighted_handle",
        Name = "加重剑柄",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Handle,
        Note = "配重柄：伤↑、攻速↓。draft",
        MaterialCosts = Cost(("scrap_metal", 2), ("nails", 2)),
        WorkMinutes = 45,
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
        Id = "lightened_handle",
        Name = "轻质化剑柄",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Handle,
        Note = "减重柄：攻速↑、伤略↓。draft",
        MaterialCosts = Cost(("wood", 1), ("leather", 1)),
        WorkMinutes = 40,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.85),
            StatMod.Mul(WeaponStat.DamageMax, 0.92),
        },
    };

    /// <summary>防滑缠手（锐器）：握持更稳 → 出手更利落（攻速↑）。近战必中，故"命中↑"在引擎里无近战字段，借攻速表达。</summary>
    public static WeaponMod GripWrapBlade() => new()
    {
        Id = "grip_wrap_blade",
        Name = "防滑缠手",
        RequiredClass = WeaponClass.Blade,
        Part = WeaponPart.Grip,
        Note = "缠手防滑：出手更利落（攻速↑）。近战必中，命中项无引擎字段。draft",
        MaterialCosts = Cost(("cloth", 2), ("rope", 1)),
        WorkMinutes = 20,
        Stats = new[]
        {
            StatMod.Mul(WeaponStat.AttackInterval, 0.95),
        },
    };

    // ============ 近战钝器改装（对标"棍棒"示例；棍/锤通用）============

    /// <summary>铁丝强化：缠铁丝加固 → 伤↑。</summary>
    public static WeaponMod WireWrap() => new()
    {
        Id = "wire_wrap",
        Name = "铁丝强化",
        RequiredClass = WeaponClass.Blunt,
        Part = WeaponPart.Shaft,
        Note = "缠铁丝：伤↑。draft",
        MaterialCosts = Cost(("wire", 2)),
        WorkMinutes = 30,
        Stats = new[]
        {
            StatMod.Add(WeaponStat.DamageMin, 1),
            StatMod.Add(WeaponStat.DamageMax, 2),
        },
    };

    /// <summary>钉子强化：钉刺 → 伤↑、并带一点穿透。</summary>
    public static WeaponMod NailStuds() => new()
    {
        Id = "nail_studs",
        Name = "钉子强化",
        RequiredClass = WeaponClass.Blunt,
        Part = WeaponPart.Shaft,
        Note = "钉刺：伤↑、穿透↑（钉尖破防）。draft",
        MaterialCosts = Cost(("nails", 4)),
        WorkMinutes = 30,
        Stats = new[]
        {
            StatMod.Add(WeaponStat.DamageMax, 3),
            StatMod.Add(WeaponStat.Penetration, 0.03),
        },
    };

    /// <summary>防滑缠手（钝器）：握持更稳 → 攻速↑。</summary>
    public static WeaponMod GripWrapBlunt() => new()
    {
        Id = "grip_wrap_blunt",
        Name = "防滑缠手",
        RequiredClass = WeaponClass.Blunt,
        Part = WeaponPart.Grip,
        Note = "缠手防滑：抡起更利落（攻速↑）。draft",
        MaterialCosts = Cost(("cloth", 2), ("rope", 1)),
        WorkMinutes = 20,
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
