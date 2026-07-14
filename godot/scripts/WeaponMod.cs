using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 武器改装部位。一把武器的每个部位最多承载一个改装（同部位不可叠两个 → <see cref="WeaponModException"/>）；
/// 不同部位可自由叠加。部位划分为"拟定待调"（draft），后续如需细分（如刺刀独立于枪口）可扩。
/// </summary>
public enum WeaponPart
{
    /// <summary>枪托（轻质化 / 利爪型绑刃 / 创伤型改铁锤）。</summary>
    Stock,

    /// <summary>枪管（截短 / 加长）。</summary>
    Barrel,

    /// <summary>枪口（刺刀）。独立于枪管，故刺刀可与截短/加长枪管共存。</summary>
    Muzzle,

    /// <summary>刃（锯齿 / 研磨 / 镂空）。</summary>
    Blade,

    /// <summary>柄（加重 / 轻质化）。</summary>
    Handle,

    /// <summary>缠手（防滑缠手）。柄与缠手视为两个部位，故可同时改。</summary>
    Grip,

    /// <summary>杆/头强化（棍棒：铁丝 / 钉子）。</summary>
    Shaft,
}

/// <summary>
/// 武器大类。改装按大类适用（而非按具体武器名），故同类武器（如所有长枪）共享一批改装、易扩。
/// 分类由武器现有字段派生：远程→<see cref="Firearm"/>；近战锐器→<see cref="Blade"/>；近战钝器→<see cref="Blunt"/>。
/// </summary>
public enum WeaponClass
{
    /// <summary>枪械（远程，带枪托近战 profile）。</summary>
    Firearm,

    /// <summary>近战锐器（剑/匕首/刺剑等）。</summary>
    Blade,

    /// <summary>近战钝器（棍棒/锤等）。</summary>
    Blunt,
}

/// <summary>可被改装增量作用的武器数值字段。</summary>
public enum WeaponStat
{
    DamageMin,
    DamageMax,
    Penetration,
    AttackInterval,
    BaseSpreadDegrees,
    MaxRange,
    FalloffStart,
    FalloffFloor,
    StockMeleeDamageMin,
    StockMeleeDamageMax,
    StockMeleeInterval,
    StockMeleePenetration,
    StockMeleeNoiseRadius,
}

/// <summary>
/// **近战型态**（用户点名的三种）：给枪装上近战件之后，"贴脸/打空时抡枪托"打出来的东西变成什么。
/// <para>
/// 一把枪**至多一种型态**（三选一，见 <see cref="WeaponMods.ApplyMods"/>）——枪口挂了刺刀又把枪托锯了改铁锤，
/// 那是两把不同的枪。型态决定枪托近战的**伤害类型**（<see cref="StockMeleeDamageTypeOf"/>），
/// 具体数值增量仍走各自的 <see cref="WeaponMod.Stats"/>。
/// </para>
/// </summary>
public enum MeleeForm
{
    /// <summary>利爪型：枪托绑上利刃 → 锐器**切割**。伤害最高，穿透中等，出手节奏不变。</summary>
    Claw,

    /// <summary>创伤型：枪托改成铁锤 → **钝伤加重**。单击最重、最慢、最响，代价是持握变差、射击精度下降。</summary>
    Trauma,

    /// <summary>刺刀型：枪口挂刺刀 → **刺击穿透**。全型态最高穿透、出手最快最安静，单击伤害不及利爪。</summary>
    Bayonet,
}

/// <summary>数值改装的运算方式。</summary>
public enum StatOp
{
    /// <summary>加法增量（可负）。作用于 null 字段时视为无效果（无法给"无射程"的近战加射程）。</summary>
    Add,

    /// <summary>乘法系数。作用于 null 字段时无效果。</summary>
    Mul,

    /// <summary>直接覆盖为定值（可给 null 字段赋值）。</summary>
    Set,
}

/// <summary>单条数值改装项：对某字段做一次 加/乘/覆盖。数值全为"拟定待调"（draft），靠 Sim 拉表校准。</summary>
public readonly record struct StatMod(WeaponStat Stat, StatOp Op, double Value)
{
    public static StatMod Add(WeaponStat stat, double v) => new(stat, StatOp.Add, v);
    public static StatMod Mul(WeaponStat stat, double v) => new(stat, StatOp.Mul, v);
    public static StatMod Set(WeaponStat stat, double v) => new(stat, StatOp.Set, v);
}

/// <summary>
/// 一项武器改装（数据，非逻辑）。= 名称 + 适用大类 + 占用部位 + 一组数值增量（+ 可选的"枪托近战型"变换）。
/// 合成逻辑 <see cref="WeaponMods.ApplyMods"/> 通用；具体改装全部作为数据放在 <see cref="WeaponModCatalog"/>。
/// </summary>
public sealed class WeaponMod
{
    /// <summary>
    /// **稳定内部 id**（ASCII 蛇形，如 <c>bayonet</c>/<c>claw_stock</c>/<c>trauma_stock</c>）。
    /// <para>
    /// 存在的理由：用户在**本地 wiki 网页**上调改装数值，改完由 agent 同步回本 catalog ——
    /// 靠这个 id 定位是哪一条。**中文名（<see cref="Name"/>）不能当 id**：它是给玩家看的字，随时可能改；
    /// 而且"防滑缠手"跨锐器/钝器**同名两条**，按名索引会撞。id 一旦定下就不要再改。
    /// </para>
    /// </summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>适用大类（不匹配 → 合成时拒绝）。</summary>
    public WeaponClass RequiredClass { get; init; }

    /// <summary>占用部位（同部位已被占 → 合成时拒绝）。</summary>
    public WeaponPart Part { get; init; }

    /// <summary>该改装的数值增量列表（按序施加）。</summary>
    public IReadOnlyList<StatMod> Stats { get; init; } = System.Array.Empty<StatMod>();

    /// <summary>
    /// 装这项改装要付的材料（RefKey → 数量，对齐 <see cref="Materials"/> 目录）。空 = 白送（不该有）。
    /// 由 <see cref="WeaponModLogic.CanApply"/> 核对库存、<see cref="WeaponModLogic.Resolve"/> 出扣料 delta。
    /// </summary>
    public IReadOnlyDictionary<string, int> MaterialCosts { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// 装这项改装的工时（游戏分钟）。走既有 <see cref="CraftingJob"/> 工时制——改装**不是点击即得**，
    /// 得有人站在改装台前把活干完。拟定待调。
    /// </summary>
    public int WorkMinutes { get; init; } = 60;

    /// <summary>
    /// **近战型态**（利爪/创伤/刺刀）。非 <c>null</c> ⇒ 这条改装**重定义枪托近战**：伤害类型由型态决定
    /// （<see cref="WeaponMods.StockMeleeDamageTypeOf"/>），数值增量仍走 <see cref="Stats"/>。
    /// 一把枪至多一条带型态的改装（三选一，冲突见 <see cref="WeaponMods.ApplyMods"/>）。
    /// <c>null</c> = 这条改装不碰近战型态（枪管/轻质化枪托等）。
    /// </summary>
    public MeleeForm? Form { get; init; }

    /// <summary>面板说明（draft）。</summary>
    public string Note { get; init; } = "";
}

/// <summary>改装合成失败（部位冲突 / 大类不适用 / 枪托近战型冲突）。</summary>
public sealed class WeaponModException : System.Exception
{
    public WeaponModException(string message) : base(message) { }
}

/// <summary>
/// 改装后的武器变体（合成产物，不改动原 base）。
/// <para>
/// <b>关键不变式：<see cref="Weapon"/> 是无损的——改装的全部效果（含刺刀/利爪的锐击枪托）都已烧进这把
/// 普通 <see cref="global::DeadSignal.Combat.Weapon"/> 里，没有任何旁挂状态。</b> 这正是它能入库
/// （库存 <c>Item</c> 只存一个名字）、能装备、能存档、能进战斗结算的原因；此前的"旁挂 override"一入库就丢。
/// </para>
/// <see cref="BaseWeaponName"/> + <see cref="AppliedMods"/> 是这把变体的**可序列化身份**：存档只存这两样，
/// 读档时按名重合成即可还原（见 <see cref="ModdedWeaponRegistry"/>）。
/// </summary>
public sealed class ModdedWeapon
{
    public Weapon Weapon { get; }

    /// <summary>基础武器名（合成前的 <see cref="global::DeadSignal.Combat.Weapon.Name"/>）。存档据此重合成。</summary>
    public string BaseWeaponName { get; }

    /// <summary>已施加的改装（按施加顺序）。</summary>
    public IReadOnlyList<WeaponMod> AppliedMods { get; }

    public ModdedWeapon(Weapon weapon, string baseWeaponName, IReadOnlyList<WeaponMod> appliedMods)
    {
        Weapon = weapon;
        BaseWeaponName = baseWeaponName;
        AppliedMods = appliedMods;
    }

    /// <summary>这把变体的近战型态（利爪/创伤/刺刀）；无近战型改装时 <c>null</c>。</summary>
    public MeleeForm? Form => AppliedMods.Select(m => m.Form).FirstOrDefault(f => f is not null);

    /// <summary>改装后生效的枪托近战 profile。<b>直接就是 <see cref="Weapon"/> 自己的</b>——型态已烧进武器。</summary>
    public Weapon? EffectiveMeleeProfile() => Weapon.MeleeProfile();

    /// <summary>只关心武器主体的调用方可直接把结果当 <see cref="global::DeadSignal.Combat.Weapon"/> 用。</summary>
    public static implicit operator Weapon(ModdedWeapon m) => m.Weapon;
}

/// <summary>
/// 武器改装合成引擎（纯逻辑，无 Godot 依赖）：base 武器 + 改装列表 → 改装后武器变体。
/// 消耗基础武器改成新变体、一把可叠多个不同部位改装（用户拍板）。本文件只做数据模型 + 合成；
/// "改装作为配方（消耗材料/工具/条件）"由后续配方系统接入，不在此。
/// </summary>
public static class WeaponMods
{
    /// <summary>按武器现有字段派生大类。</summary>
    public static WeaponClass ClassOf(Weapon w) =>
        w.IsRanged ? WeaponClass.Firearm
        : w.DamageType == DamageType.Sharp ? WeaponClass.Blade
        : WeaponClass.Blunt;

    /// <summary>近战型态 → 枪托近战的伤害类型（用户语义：刺刀=刺击穿透、利爪=锐器切割 ⇒ 皆锐击；创伤=钝伤加重 ⇒ 仍钝击）。</summary>
    public static DamageType StockMeleeDamageTypeOf(MeleeForm form) => form switch
    {
        MeleeForm.Claw => DamageType.Sharp,      // 绑刃挥砍
        MeleeForm.Bayonet => DamageType.Sharp,   // 刺刀突刺（引擎只有 Sharp/Blunt 两型，"刺击"归锐击，其穿透优势由 Penetration 表达）
        MeleeForm.Trauma => DamageType.Blunt,    // 改铁锤，仍是钝的，只是更重
        _ => DamageType.Blunt,
    };

    /// <summary>
    /// 合成：把 <paramref name="mods"/> 依次施加到 <paramref name="baseWeapon"/>，产出新变体（不改原 base）。
    /// 校验：每个改装大类须匹配 base；同一部位不可叠两个改装；**至多一条带近战型态的改装**（利爪/创伤/刺刀三选一）。
    /// 任一校验失败抛 <see cref="WeaponModException"/>。
    /// </summary>
    public static ModdedWeapon ApplyMods(Weapon baseWeapon, IEnumerable<WeaponMod> mods)
    {
        var list = new List<WeaponMod>(mods);
        var cls = ClassOf(baseWeapon);
        var usedParts = new HashSet<WeaponPart>();
        var draft = WeaponDraft.From(baseWeapon);
        MeleeForm? form = null;

        foreach (var mod in list)
        {
            if (mod.RequiredClass != cls)
            {
                throw new WeaponModException(
                    $"改装「{mod.Name}」不适用于{ClassLabel(cls)}「{baseWeapon.Name}」（需{ClassLabel(mod.RequiredClass)}）");
            }

            if (!usedParts.Add(mod.Part))
            {
                throw new WeaponModException(
                    $"部位「{PartLabel(mod.Part)}」已有改装，不能再叠加「{mod.Name}」");
            }

            if (mod.Form is { } f)
            {
                if (form is not null)
                {
                    throw new WeaponModException(
                        $"「{mod.Name}」与已有的近战型态「{DisplayNames.Of(form.Value)}」冲突（一把枪只能有一种近战型态）");
                }
                form = f;
                draft.SetStockMeleeDamageType(StockMeleeDamageTypeOf(f));
            }

            foreach (var s in mod.Stats)
            {
                draft.Apply(s);
            }
        }

        var result = draft.Build(ComposeName(baseWeapon.Name, list));
        return new ModdedWeapon(result, baseWeapon.Name, list);
    }

    /// <summary>拼接变体名："短剑（锯齿剑刃・防滑缠手）"。无改装时原样返回。</summary>
    private static string ComposeName(string baseName, IReadOnlyList<WeaponMod> mods)
    {
        if (mods.Count == 0)
        {
            return baseName;
        }
        return baseName + "（" + string.Join("・", mods.Select(m => m.Name)) + "）";
    }

    private static string ClassLabel(WeaponClass c) => DisplayNames.Of(c);

    private static string PartLabel(WeaponPart p) => DisplayNames.Of(p);

    /// <summary>
    /// 可变武器草稿：从 <see cref="Weapon"/> 取全部字段进可变槽，施加增量后 <see cref="Build"/> 回不可变 Weapon。
    /// 因 <see cref="Weapon"/> 是 init-only sealed class（非 record，无 with 表达式），复制-改-重建走此草稿。
    /// </summary>
    private sealed class WeaponDraft
    {
        // 主体
        private double _damageMin, _damageMax, _penetration, _attackInterval, _baseSpread;
        private double? _maxRange, _falloffStart, _falloffFloor;
        // 枪托近战
        private double? _stockMin, _stockMax, _stockInterval, _stockPen, _stockNoise;
        private DamageType _stockDamageType;
        // 结构（不受数值改装影响，直接透传；名称由合成阶段拼接，见 Build 入参）
        private DamageType _damageType;
        private bool _twoHanded, _canDualWield, _isRanged;
        private int _burstCount = 1;
        private double _burstInterval;

        /// <summary>近战型态改装重定义枪托伤害类型（利爪/刺刀=锐击，创伤=钝击）。</summary>
        public void SetStockMeleeDamageType(DamageType t) => _stockDamageType = t;

        public static WeaponDraft From(Weapon w) => new()
        {
            _damageMin = w.DamageMin,
            _damageMax = w.DamageMax,
            _penetration = w.Penetration,
            _attackInterval = w.AttackInterval,
            _baseSpread = w.BaseSpreadDegrees,
            _maxRange = w.MaxRange,
            _falloffStart = w.FalloffStart,
            _falloffFloor = w.FalloffFloor,
            _stockMin = w.StockMeleeDamageMin,
            _stockMax = w.StockMeleeDamageMax,
            _stockInterval = w.StockMeleeInterval,
            _stockPen = w.StockMeleePenetration,
            _stockNoise = w.StockMeleeNoiseRadius,
            _stockDamageType = w.StockMeleeDamageType,
            _damageType = w.DamageType,
            _twoHanded = w.TwoHanded,
            _canDualWield = w.CanDualWield,
            _isRanged = w.IsRanged,
            _burstCount = w.BurstCount,
            _burstInterval = w.BurstInterval,
        };

        public void Apply(StatMod s)
        {
            switch (s.Stat)
            {
                case WeaponStat.DamageMin: _damageMin = ApplyNonNull(_damageMin, s); break;
                case WeaponStat.DamageMax: _damageMax = ApplyNonNull(_damageMax, s); break;
                case WeaponStat.Penetration: _penetration = ApplyNonNull(_penetration, s); break;
                case WeaponStat.AttackInterval: _attackInterval = ApplyNonNull(_attackInterval, s); break;
                case WeaponStat.BaseSpreadDegrees: _baseSpread = ApplyNonNull(_baseSpread, s); break;
                case WeaponStat.MaxRange: _maxRange = ApplyNullable(_maxRange, s); break;
                case WeaponStat.FalloffStart: _falloffStart = ApplyNullable(_falloffStart, s); break;
                case WeaponStat.FalloffFloor: _falloffFloor = ApplyNullable(_falloffFloor, s); break;
                case WeaponStat.StockMeleeDamageMin: _stockMin = ApplyNullable(_stockMin, s); break;
                case WeaponStat.StockMeleeDamageMax: _stockMax = ApplyNullable(_stockMax, s); break;
                case WeaponStat.StockMeleeInterval: _stockInterval = ApplyNullable(_stockInterval, s); break;
                case WeaponStat.StockMeleePenetration: _stockPen = ApplyNullable(_stockPen, s); break;
                case WeaponStat.StockMeleeNoiseRadius: _stockNoise = ApplyNullable(_stockNoise, s); break;
            }
        }

        private static double ApplyNonNull(double cur, StatMod s) => s.Op switch
        {
            StatOp.Add => cur + s.Value,
            StatOp.Mul => cur * s.Value,
            StatOp.Set => s.Value,
            _ => cur,
        };

        /// <summary>可空字段：Add/Mul 对 null 无效果（无法缩放"不存在的射程"），Set 可赋值。</summary>
        private static double? ApplyNullable(double? cur, StatMod s) => s.Op switch
        {
            StatOp.Set => s.Value,
            StatOp.Add => cur.HasValue ? cur.Value + s.Value : null,
            StatOp.Mul => cur.HasValue ? cur.Value * s.Value : null,
            _ => cur,
        };

        public Weapon Build(string name) => new()
        {
            Name = name,
            DamageMin = System.Math.Max(0, _damageMin),
            DamageMax = System.Math.Max(System.Math.Max(0, _damageMin), _damageMax),
            Penetration = System.Math.Clamp(_penetration, 0, 1),
            DamageType = _damageType,
            TwoHanded = _twoHanded,
            CanDualWield = _canDualWield,
            IsRanged = _isRanged,
            BaseSpreadDegrees = System.Math.Max(0, _baseSpread),
            AttackInterval = System.Math.Max(0.01, _attackInterval),
            BurstCount = System.Math.Max(1, _burstCount),
            BurstInterval = System.Math.Max(0, _burstInterval),
            MaxRange = _maxRange is null ? null : System.Math.Max(0, _maxRange.Value),
            FalloffStart = _falloffStart is null ? null : System.Math.Max(0, _falloffStart.Value),
            FalloffFloor = _falloffFloor is null ? null : System.Math.Clamp(_falloffFloor.Value, 0.01, 1),
            StockMeleeDamageMin = _stockMin is null ? null : System.Math.Max(0, _stockMin.Value),
            StockMeleeDamageMax = _stockMax is null ? null : System.Math.Max(0, _stockMax.Value),
            StockMeleeInterval = _stockInterval is null ? null : System.Math.Max(0.01, _stockInterval.Value),
            StockMeleePenetration = _stockPen is null ? null : System.Math.Clamp(_stockPen.Value, 0, 1),
            StockMeleeNoiseRadius = _stockNoise is null ? null : System.Math.Max(0, _stockNoise.Value),
            StockMeleeDamageType = _stockDamageType,
        };
    }
}
