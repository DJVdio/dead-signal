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

    // ── [T69] 弓弩专属部位（用户在 wiki 上新加的 5 条改装引入）──
    // 弓与缠手是**两个不同部位**（用户拍板：复合弓臂 part=弓、弓臂缠手 part=缠手，可同装一把弓、互不占位）。

    /// <summary>弓臂（复合弓臂占它）。与 <see cref="LimbWrap"/> 是不同部位，可同装。</summary>
    Bow,

    /// <summary>弓弦（重磅弓弦占它）。</summary>
    String,

    /// <summary>弩身（弩盾占它）。</summary>
    CrossbowBody,

    /// <summary>
    /// 弓臂缠手（弓臂缠手占它）。<b>是独立于 <see cref="Bow"/> 的部位</b>——用户拍板"弓臂缠手与复合弓臂不互斥、可同装一把弓"。
    /// 显示名同 <see cref="Grip"/>（都叫"缠手"）：两者从不出现在同一把武器上（Grip 在枪/刃/钝，本值在弓），
    /// 部位冲突判据走枚举相等（非显示名），故显示重名无碍。
    /// </summary>
    LimbWrap,
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

    /// <summary>
    /// [T68] 弹丸飞行速度（<see cref="Weapon.FlightSpeed"/>，默认 560）。为弓弩改装「飞速 +12%」
    /// （复合弓臂/重磅弓弦，由 modweapon-mods 填）留的乘算通路——同轴与《弓与箭之道》的 +20% 连乘。
    /// </summary>
    FlightSpeed,

    /// <summary>[T53] 这把武器造成的伤口的流血速率乘数（锯齿剑刃 ×1.4 = 流血速度 +40%）。</summary>
    BleedRateMultiplier,

    /// <summary>[T53] 造成伤害时额外引发一处「小流血」的概率（钉子强化 = 0.25）。</summary>
    BleedOnHitChance,
}

/// <summary>
/// **近战型态**（用户点名的<b>四种</b>）：给枪装上近战件之后，"贴脸/打空时抡枪托"打出来的东西变成什么。
/// <para>
/// 一把枪**至多一种型态**（见 <see cref="WeaponMods.ApplyMods"/> 的通用互斥判据——它认<b>任何</b>带 Form 的改装，
/// 不是硬编码那几种）——枪口挂了刺刀又把枪托锯了改铁锤，那是两把不同的枪。
/// 型态决定枪托近战的**伤害类型**（<see cref="StockMeleeDamageTypeOf"/>），具体数值增量仍走各自的 <see cref="WeaponMod.Stats"/>。
/// </para>
/// <para>
/// 🔴 <b>[T68] 前三种给 4 把重枪（刺刀/利爪/创伤，见 <c>GunsMeleeForm</c>），<see cref="Blade"/> 给 2 把短枪
/// （手枪/冲锋枪，见 <c>GunsBladeForm</c>）——两组白名单<b>不相交</b>。所以"手枪同时装锋刃型+刺刀型"这种事
/// **在白名单层就被挡死**（刺刀根本不进手枪的候选），型态互斥是第二道闸（防未来白名单变动）。</b>
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

    /// <summary>[T68] 锋刃型：枪托（握把）固定一把匕首 → 锐器**切割**。**给短枪（手枪/冲锋枪）的近战型态**——
    /// 短枪装不了刺刀/利爪/创伤（那三种是重枪专属），它换来一把 85% 攻速的匕首，近身出其不意。</summary>
    Blade,
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

    /// <summary>
    /// **能装在哪几把武器上**（武器名白名单；不在其中 → 合成时拒绝）。
    ///
    /// <para><b>🔴 从「武器大类」换成「逐把枪的白名单」是用户拍板的</b>：按大类卡，
    /// 没法表达"这个改装只能装步枪和霰弹枪"。白名单把约束的粒度落到**具体武器**上，
    /// 用户就能在数值表 wiki 上逐把勾。</para>
    ///
    /// <para><b>⚠️ 换过来时行为是零变化的</b>：每条改装的白名单 = 它原本那个大类的**全部**武器
    /// （<c>WeaponModCatalog.LegacyClassOf</c> + <c>AllOfClass</c>，有测试逐把钉死）。
    /// 必须零变化，因为老存档里的改装枪靠 <c>ModdedWeaponRegistry</c> 用**当前**规则重算——
    /// 规则一收严，老组合就变非法。收窄留给用户自己在 wiki 上做。</para>
    ///
    /// <para><b>🔴 [T29] 用户已收窄：弓弩不再吃枪械改装</b>。此前 <c>WeaponMods.ClassOf</c> 是
    /// <c>IsRanged ? Firearm : …</c> ⇒ **弓弩被误算作"枪械"** ⇒ 截短枪管真能装到短弓上（潜伏已久的 bug）。
    /// 迁移期如实保留了它，用户随后明令划掉 ⇒ 6 条枪械改装现在只认真枪（<c>WeaponModCatalog.AllGuns</c>）。
    /// 老档里的非法组合**不会**静默失效：<c>ModdedWeaponRegistry.RebuildOrBase</c> 回落成基础武器
    /// （弓还在，改装没了）。</para>
    ///
    /// <para><b>🔴 [T47] 白名单已成为「用户逐格勾的数据」</b>：14 条改装的白名单**各不相同**
    /// （见 <see cref="WeaponModCatalog"/>），**不要再拿 <c>AllOfClass</c> 去派生它** —— 那会覆盖用户的手勾。</para>
    /// </summary>
    public IReadOnlySet<string> FitsWeapons { get; init; } = new HashSet<string>();

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
    /// **这项改装让整把武器重多少倍**（1.0 = 不改重量；0.75 = 减重 25%；1.5 = 增重 50%）。
    ///
    /// <para><b>🔴 用户原话：「我希望重量在改装中是一个重要的因素」</b> —— 重量是这套改装设计的**核心代价轴**：
    /// 你可以把一把枪改得很强，但它会把你压进负重 debuff。故重量**不是 flavor 字段**，它真的进负重账
    /// （<c>ItemWeights.WeaponKg</c> → <c>ModdedWeaponRegistry.WeightMultiplierOf</c>）。</para>
    ///
    /// <para><b>为什么不放在 <see cref="Stats"/> 里</b>：引擎的 <see cref="global::DeadSignal.Combat.Weapon"/>
    /// **没有 Weight 字段**（武器重量是消费层概念，单一事实源在 <c>ItemWeights._weaponKg</c>）。
    /// 硬塞进 Weapon 会污染引擎、且会把改装变体拖进 Sim 结算路径（基线漂移）。</para>
    ///
    /// <para><b>多条改装 ⇒ 连乘</b>（CLAUDE.md 的乘算铁律）。</para>
    /// </summary>
    public double WeightMultiplier { get; init; } = 1.0;

    /// <summary>
    /// **消耗型改装**：还能撑几次攻击（<c>null</c> = 永久改装，绝大多数属此）。
    ///
    /// <para>用户点名的那条：<b>锋刃研磨 = 穿透 +75%，攻击三次后失去该改装</b>（<c>UsesBeforeBreak = 3</c>）。
    /// 用光时改装**脱落**——武器回到"没有这个改装"的状态，且玩家看得见（不是静默失效）。</para>
    ///
    /// <para><b>次数本身不是这里的状态</b>：本字段只是**目录里的规格**（"这种改装能用几次"），
    /// 每把武器**实例**各自剩几次是运行时状态，存在 <c>ModdedWeaponRegistry</c> 的耐久层里、并进存档。
    /// 目录（数据）与实例状态（运行时）分开，才让 <c>ModdedWeaponRegistry.Rebuild</c> 保持纯函数。</para>
    /// </summary>
    public int? UsesBeforeBreak { get; init; }

    /// <summary>这项改装是不是消耗型（装上去会用光、会脱落）。</summary>
    public bool IsConsumable => UsesBeforeBreak is > 0;

    /// <summary>
    /// **允许单手持有**（截短枪管：把长枪锯短到能单手抡）。<c>true</c> ⇒ 合成后 <c>TwoHanded = false</c>。
    ///
    /// <para><b>⚠️ 刻意不动 <c>CanDualWield</c></b>：用户原话只有「**允许单手持有**」四个字，
    /// 没说"允许双持"——那是**另一个独立字段**、另一件事（双持 = 两手各一把同型枪，弹药 ×2、散布 ×1.5625）。
    /// 而用户在冲锋枪那条上刚刚拍板过「**保双手，放弃双持**」⇒ 顺手打开双持会绕过他自己的裁决。
    /// 按字面落地：**能腾出一只手（拿火把/开门），但不能双持**。要双持，用户在表上再说一句即可。</para>
    ///
    /// <para>不做成 <see cref="StatMod"/>：持握是**结构字段**（bool），不是可加可乘的数值。</para>
    /// </summary>
    public bool AllowsOneHanded { get; init; }

    /// <summary>
    /// **近战型态**（利爪/创伤/刺刀）。非 <c>null</c> ⇒ 这条改装**重定义枪托近战**：伤害类型由型态决定
    /// （<see cref="WeaponMods.StockMeleeDamageTypeOf"/>），数值增量仍走 <see cref="Stats"/>。
    /// 一把枪至多一条带型态的改装（三选一，冲突见 <see cref="WeaponMods.ApplyMods"/>）。
    /// <c>null</c> = 这条改装不碰近战型态（枪管/轻质化枪托等）。
    /// </summary>
    public MeleeForm? Form { get; init; }

    /// <summary>面板说明（draft）。</summary>
    public string Note { get; init; } = "";

    /// <summary>玩家在游戏里看到的简介（flavor 文案）。单一事实源＝ wiki <c>docs/wiki/data/weapon-mods.json</c> 的 description 列。</summary>
    public string Description { get; init; } = "";

    // ═══════════════════════════════════════════════════════════════════════════
    // [T69] **防御型否决**（不是 StatMod —— 它改的不是武器数值，而是"持这把武器的人挨打时的一次整发否决"）
    //
    // 用户在 wiki 上加的两条改装带这种效果：护手挡格（近身武器）、弩盾（弩）。它们无法表达成
    // 对 Weapon 某个字段的加/乘/覆盖 —— 是**承伤入口的一次掷点**。故落成两个独立几率字段，
    // 由纯逻辑 <see cref="WeaponModDefense"/> 判定、消费层（CombatEngine.ResolveHit / Actor.ReceiveAttack）接线。
    //
    // 🔴 默认 0 = 无效果、恒不掷点（零漂移）：绝大多数改装不带否决，短路不动随机流。
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// **护手挡格**：持这把武器的手（连同手指）被选为受击部位时，按此几率把**整次攻击**判无效。
    /// 0 = 无此效果。护手挡格 = 0.5（用户 wiki）。判定见 <see cref="WeaponModDefense.HandGuardNegates"/>，
    /// 接线在 <c>CombatEngine.ResolveHit</c>（部位在那里选定、伤害在其后施加，故否决必须落在选部位之后）。
    /// </summary>
    public double HandGuardNegateChance { get; init; }

    /// <summary>
    /// **弩盾**：举着这把武器时，来自**正面锥内**（半角 <see cref="FrontalNegateHalfAngleDeg"/>）的**远程**攻击，
    /// 按此几率整发判无效。0 = 无此效果。弩盾 = 0.25（用户 wiki）。判定见
    /// <see cref="WeaponModDefense.FrontalRangedNegates"/>，接线在 <c>Actor.ReceiveAttack</c>（与半身掩体/哨塔围栏同层）。
    /// </summary>
    public double FrontalRangedNegateChance { get; init; }

    /// <summary>弩盾正面锥的**半角**（度）；全张角 = 2×此值。用户 wiki：正面 120° ⇒ 半角 60°。</summary>
    public double FrontalNegateHalfAngleDeg { get; init; } = 60.0;
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

    /// <summary>
    /// 这把变体的**重量倍率**（各改装 <see cref="WeaponMod.WeightMultiplier"/> **连乘**——百分比一律乘算）。
    /// 由 <c>ItemWeights.WeaponKg</c> 乘到基础武器重量上；<b>不进 <see cref="Weapon"/></b>（引擎无 Weight 字段）。
    /// </summary>
    public double WeightMultiplier => AppliedMods.Aggregate(1.0, (acc, m) => acc * m.WeightMultiplier);

    /// <summary>这把变体上的**消耗型改装**（会用光、会脱落的那些）。空 = 这把枪的改装都是永久的。</summary>
    public IReadOnlyList<WeaponMod> ConsumableMods => AppliedMods.Where(m => m.IsConsumable).ToList();

    /// <summary>这把变体带不带消耗型改装（带 ⇒ 注册表要给它一个**唯一实例名**，见 <c>ModdedWeaponRegistry</c>）。</summary>
    public bool HasConsumableMod => AppliedMods.Any(m => m.IsConsumable);

    /// <summary>[T69] 这把变体的**护手挡格**否决几率（取各改装最大值；无则 0）。见 <see cref="WeaponMod.HandGuardNegateChance"/>。</summary>
    public double HandGuardNegateChance => AppliedMods.Count == 0 ? 0.0 : AppliedMods.Max(m => m.HandGuardNegateChance);

    /// <summary>[T69] 这把变体的**弩盾**正面远程否决几率（取各改装最大值；无则 0）。见 <see cref="WeaponMod.FrontalRangedNegateChance"/>。</summary>
    public double FrontalRangedNegateChance => AppliedMods.Count == 0 ? 0.0 : AppliedMods.Max(m => m.FrontalRangedNegateChance);

    /// <summary>[T69] 弩盾正面锥半角（度）：取带弩盾的那条改装的值；无弩盾则默认 60。</summary>
    public double FrontalNegateHalfAngleDeg =>
        AppliedMods.Where(m => m.FrontalRangedNegateChance > 0).Select(m => m.FrontalNegateHalfAngleDeg).DefaultIfEmpty(60.0).First();

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
        MeleeForm.Blade => DamageType.Sharp,     // [T68] 锋刃型＝固定一把匕首，锐击切割
        MeleeForm.Trauma => DamageType.Blunt,    // 改铁锤，仍是钝的，只是更重
        _ => DamageType.Blunt,
    };

    /// <summary>
    /// 合成：把 <paramref name="mods"/> 依次施加到 <paramref name="baseWeapon"/>，产出新变体（不改原 base）。
    /// 校验：每个改装大类须匹配 base；同一部位不可叠两个改装；**至多一条带近战型态的改装**（利爪/创伤/刺刀三选一）。
    /// 任一校验失败抛 <see cref="WeaponModException"/>。
    /// <para>
    /// <paramref name="variantName"/>：**变体名覆盖**（默认 <c>null</c> = 按改装列表自动拼名，行为与从前完全一致）。
    /// 存在的理由：带**消耗型改装**的枪需要**唯一实例名**（两把"短剑（锋刃研磨）"必须能分辨谁砍了几下），
    /// 而注册表把变体名当作实例 key ⇒ 合成时就得能把这个 key 写进 <see cref="Weapon.Name"/>，
    /// 否则会出现「按名查得到、但查出来的枪自己叫另一个名字」的错位
    /// （<c>Item.RefKey == Weapon.Name</c> 是全项目的隐含不变式）。
    /// </para>
    /// </summary>
    public static ModdedWeapon ApplyMods(Weapon baseWeapon, IEnumerable<WeaponMod> mods, string? variantName = null)
    {
        var list = new List<WeaponMod>(mods);
        var cls = ClassOf(baseWeapon);
        var usedParts = new HashSet<WeaponPart>();
        var draft = WeaponDraft.From(baseWeapon);
        MeleeForm? form = null;

        foreach (var mod in list)
        {
            if (!mod.FitsWeapons.Contains(baseWeapon.Name))
            {
                throw new WeaponModException(
                    $"改装「{mod.Name}」装不到「{baseWeapon.Name}」上（它只能装：{string.Join("、", mod.FitsWeapons)}）");
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

            // 持握是**结构字段**（bool），不走 StatMod：截短枪管把长枪锯短到能单手抡。
            // 只解除双手，**不碰 CanDualWield**——用户说的是"允许单手持有"，双持是另一回事（见 WeaponMod.AllowsOneHanded）。
            if (mod.AllowsOneHanded)
            {
                draft.SetOneHanded();
            }

            foreach (var s in mod.Stats)
            {
                draft.Apply(s);
            }
        }

        var result = draft.Build(variantName ?? ComposeName(baseWeapon.Name, list));
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

        // [T53] 流血轴。中性默认（1.0 / 0）⇒ 没装流血改装的武器逐字段不变。
        private double _bleedRateMult = 1.0;
        private double _bleedOnHit;

        // [T68] 弹丸飞速。默认 560（＝旧全局常量）⇒ 没装飞速改装的武器逐字段不变。
        private double _flightSpeed = 560.0;

        /// <summary>近战型态改装重定义枪托伤害类型（利爪/刺刀=锐击，创伤=钝击）。</summary>
        public void SetStockMeleeDamageType(DamageType t) => _stockDamageType = t;

        /// <summary>解除"必须双手"（截短枪管）。<b>不动 <c>_canDualWield</c></b>——那是另一条独立规则。</summary>
        public void SetOneHanded() => _twoHanded = false;

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
            _bleedRateMult = w.BleedRateMultiplier,
            _bleedOnHit = w.BleedOnHitChance,
            _flightSpeed = w.FlightSpeed,
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
                case WeaponStat.BleedRateMultiplier: _bleedRateMult = ApplyNonNull(_bleedRateMult, s); break;
                case WeaponStat.BleedOnHitChance: _bleedOnHit = ApplyNonNull(_bleedOnHit, s); break;
                case WeaponStat.FlightSpeed: _flightSpeed = ApplyNonNull(_flightSpeed, s); break;
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

        /// <summary>
        /// 收尾成一把不可变 <see cref="Weapon"/>。所有字段在此**统一夹紧**（合法域由引擎定，改装不许越界）。
        /// <para>
        /// 🔴 <b>穿透 100% 上限（用户拍板：「穿透不能超过 100%」）</b>：<c>Penetration</c> 与
        /// <c>StockMeleePenetration</c> 都 <c>Clamp(0, 1)</c>。这是**唯一**的收口点 ——
        /// 改装的穿透是**乘算**的（穿透 +75% ⇒ ×1.75），叠满多条改装理论上能把它顶穿 100%，
        /// 全靠这里兜住。护栏见 <c>WeaponModPenetrationCapTests</c>：谁把这个 Clamp 拿掉，测试当场红。
        /// </para>
        /// </summary>
        public Weapon Build(string name) => new()
        {
            Name = name,
            DamageMin = System.Math.Max(0, _damageMin),
            DamageMax = System.Math.Max(System.Math.Max(0, _damageMin), _damageMax),
            Penetration = System.Math.Clamp(_penetration, 0, 1),   // ← 穿透 ≤ 100%（用户拍板）
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
            StockMeleePenetration = _stockPen is null ? null : System.Math.Clamp(_stockPen.Value, 0, 1),   // ← 同上，枪托侧的 100% 上限
            StockMeleeNoiseRadius = _stockNoise is null ? null : System.Math.Max(0, _stockNoise.Value),
            StockMeleeDamageType = _stockDamageType,
            // [T53] 流血轴，同样在此统一夹紧：速率乘数不为负（负流血=回血，荒谬）；小流血概率是概率 ⇒ [0,1]。
            BleedRateMultiplier = System.Math.Max(0, _bleedRateMult),
            BleedOnHitChance = System.Math.Clamp(_bleedOnHit, 0, 1),
            // [T68] 弹丸飞速：不为负（负速＝倒着飞，荒谬）。默认 560 透传 ⇒ 没装飞速改装的武器零漂移。
            FlightSpeed = System.Math.Max(0, _flightSpeed),
        };
    }
}
