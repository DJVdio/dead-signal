using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 WeaponMod.cs / CraftingLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 改装武器的**身份登记处**。存在的理由，是项目里一条很硬的约束：
//   库存里的一件武器 **只存一个名字**（Item.RefKey = 武器名），战斗/装备/存档一律**按名回查** WeaponTable。
//   而改装出来的"步枪（刺刀型）"**不在 WeaponTable 里** ⇒ 按名回查必然落空。后果（改装落地前的真实 bug）：
//     · Pawn.EquipWeapon 查不到 ⇒ 改装完的枪**永远装备不上**；
//     · SaveMapper.WeaponByName 查不到 ⇒ 存档一读**那把枪就没了**；
//   本注册表就是补上的那条回查路径：**变体名 → 变体武器**。
//
// 它只需要存三样东西（ModdedWeaponSpec）：变体名 / 基础武器名 / 改装名列表 ——
// 因为改装合成是**纯函数**（WeaponMods.ApplyMods），有这三样就能把整把枪原样重算出来。
// 故存档只落这三个字符串，**不序列化任何武器数值**：日后调了改装数值，老存档里的枪自动跟着改，不会腐化成旧数值。

/// <summary>
/// 一把改装武器的**可序列化身份**：变体名 + 基础武器名 + 改装名列表。
/// 数值不入档——读档时按 <see cref="ModdedWeaponRegistry.Rebuild"/> 用当前规则重新合成。
/// <para>
/// 🔴 <b>耐久/剩余次数**不在这里**</b>（[T47]）。它是**武器实例上的可变状态**，而 spec 是**不可变的身份**：
/// spec 回答"这把枪是什么做的"，耐久回答"这一把还能撑几下"。混在一起会污染
/// <see cref="ModdedWeaponRegistry.Rebuild"/> 的纯函数语义（它是"这组合当前还合不合法"的判据，
/// 不该因为砍了三刀就给出不同答案）。耐久存在 <see cref="ModdedWeaponRegistry"/> 的耐久层里，单独入档。
/// </para>
/// </summary>
public sealed record ModdedWeaponSpec(
    string VariantName,
    string BaseWeaponName,
    IReadOnlyList<string> ModNames);

/// <summary>
/// 一次攻击之后，消耗型改装的结算结果（<see cref="ModdedWeaponRegistry.ConsumeUse"/> 的产物）。
/// </summary>
/// <param name="Changed">这把武器**变了**没有（有改装脱落 ⇒ true）。false ⇒ 调用方什么都不用做。</param>
/// <param name="WeaponName">
/// 结算后这件东西**现在叫什么**。改装脱落后可能是：
/// ① 另一个变体名（还剩别的改装）；② **基础武器名**（改装掉光了，回落成一把普通短剑）。
/// 调用方（营地层）据此换掉库存里的那件 <c>Item</c> 与角色手上的武器。
/// </param>
/// <param name="BrokenModNames">这一下用光并**脱落**的改装名（给玩家看的提示："短剑的锋刃研磨磨没了"）。</param>
public readonly record struct ModWearResult(
    bool Changed,
    string WeaponName,
    IReadOnlyList<string> BrokenModNames);

/// <summary>
/// 改装武器注册表（进程内全局，营地共享）：<b>变体名 → 变体 <see cref="Weapon"/></b> 的回查路径。
/// 装备 / 存档 / 负重 / UI 在 <c>WeaponTable</c> 里查不到某个武器名时，一律回落到这里。
/// <para>
/// <b>为什么是 static</b>：与"半身掩体场"同构——它是**营地这一局**的共享事实，而武器名回查发生在
/// Pawn / SaveMapper / CarryWeight 等彼此不相识的地方，穿一个实例过去要改一大片签名。
/// 代价是**换场景必须 <see cref="Clear"/>**（否则上一局的改装枪会漏进下一局），读档走 <see cref="Restore"/>。
/// </para>
/// </summary>
public static class ModdedWeaponRegistry
{
    /// <summary>基础武器名 → 武器（全表：常规 24 把 + 弓弩）。改装的 base 必须来自这里。</summary>
    private static readonly IReadOnlyDictionary<string, Weapon> _baseByName =
        WeaponTable.Arsenal()
            .Concat(WeaponTable.ArcheryArsenal())
            .GroupBy(w => w.Name)
            .ToDictionary(g => g.Key, g => g.First());

    private static readonly Dictionary<string, ModdedWeaponSpec> _specs = new();
    private static readonly Dictionary<string, Weapon> _resolved = new();

    // ═══════════════════════════════════════════════════════════════════════════
    // 【T47】消耗型改装的**状态层**（锋刃研磨的数值与耐久以 Wiki 配置为准）
    //
    // 🔴 设计的核心一句话：**Rebuild 保持纯函数，状态挂在武器实例上，而"实例"就是变体名。**
    //
    // 为什么必须先解决"实例"：库存里的 Item 是 sealed record（值语义），一把改装武器**就是一个字符串**
    // （Item.RefKey = 变体名）。两把"短剑（锋刃研磨）" a == b 为 true，InventoryStore.Remove 按值移除"首个"
    // ⇒ **指定不了是哪一把**。所以"这一把还剩几次"根本没有容身之处。
    //
    // 解法：**带消耗型改装的武器，登记时拿一个唯一实例名**（"短剑（锋刃研磨）#1"、"#2"…）。
    //   · 变体名 = 实例 id：注册表 / 库存 RefKey / 存档 / 负重表**本来就全部按变体名索引**，一处都不用改语义。
    //   · 永久改装（其余 13 条）**行为完全不变**——名字照旧是"步枪（刺刀型）"，不带后缀、可共享。
    //     ⇒ 零回归：既有存档、既有测试、既有 UI 一个字都不用动。
    //   · 耐久表 _wear：变体名 → (改装名 → 还剩几次)。它**不进 ModdedWeaponSpec**（spec 是不可变身份）。
    //   · 用光 ⇒ 改装**脱落**：把它从 ModNames 里摘掉，重新登记成一个新变体（或回落成基础武器），
    //     由营地层把库存里那件 Item 与角色手上的武器换过去 —— 玩家**看得见**，不是静默失效。
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>耐久层：变体名 → (改装名 → 还剩几次攻击)。只有带消耗型改装的实例才有条目。</summary>
    private static readonly Dictionary<string, Dictionary<string, int>> _wear = new();

    /// <summary>实例序号（给带消耗型改装的变体发唯一名）。读档时会被推到已用过的最大值之上，见 <see cref="Restore"/>。</summary>
    private static int _instanceSeq;

    /// <summary>实例名后缀分隔符："短剑（锋刃研磨）<b>#</b>1"。</summary>
    private const char InstanceMarker = '#';

    /// <summary>按名取**基础**武器（WeaponTable 全表）；不在表里 ⇒ null。</summary>
    public static Weapon? BaseWeaponByName(string? name)
        => name is not null && _baseByName.TryGetValue(name, out Weapon? w) ? w : null;

    /// <summary>
    /// 登记一把改装变体。返回其变体名，即库存 <c>Item.RefKey</c>。
    /// <para>
    /// <b>永久改装</b>（无消耗型件）：变体名 = 合成时拼出来的名字（"步枪（刺刀型）"），**同名幂等覆盖** —— 行为与从前一致。
    /// </para>
    /// <para>
    /// <b>带消耗型改装</b>（锋刃研磨）：发一个**唯一实例名**（"短剑（锋刃研磨）#1"）并建耐久条目。
    /// 必须唯一，否则两把研磨过的短剑会共用一个次数计数器——砍了三下，两把一起钝。
    /// </para>
    /// </summary>
    public static string Register(ModdedWeapon modded)
    {
        if (modded is null) throw new System.ArgumentNullException(nameof(modded));

        var modNames = modded.AppliedMods.Select(m => m.Name).ToList();

        if (!modded.HasConsumableMod)
        {
            string plain = modded.Weapon.Name;
            _specs[plain] = new ModdedWeaponSpec(plain, modded.BaseWeaponName, modNames);
            _resolved[plain] = modded.Weapon;
            return plain;
        }

        // 消耗型：唯一实例名 + 耐久条目。武器本身也得改名（Item.RefKey == Weapon.Name 是全项目的隐含不变式）。
        string instanceName = modded.Weapon.Name + InstanceMarker + (++_instanceSeq);
        var spec = new ModdedWeaponSpec(instanceName, modded.BaseWeaponName, modNames);

        _specs[instanceName] = spec;
        _resolved[instanceName] = Rebuild(spec)
            ?? throw new WeaponModException($"改装变体「{instanceName}」刚合成出来却重算不回去 —— 目录与合成规则不一致");
        _wear[instanceName] = modded.ConsumableMods.ToDictionary(m => m.Name, m => m.UsesBeforeBreak!.Value);
        return instanceName;
    }

    /// <summary>
    /// 这个变体上某条消耗型改装**还剩几次**；不是消耗型 / 不存在 ⇒ <c>null</c>。
    /// UI 拿它显示"锋刃研磨（还剩 2 次）"——**玩家必须看得见**，不然改装用光时会像 bug。
    /// </summary>
    public static int? RemainingUses(string? variantName, string? modName)
        => variantName is not null && modName is not null
           && _wear.TryGetValue(variantName, out Dictionary<string, int>? w)
           && w.TryGetValue(modName, out int n)
            ? n
            : null;

    /// <summary>这个变体上全部消耗型改装的剩余次数（改装名 → 次数）；没有消耗型件 ⇒ 空。</summary>
    public static IReadOnlyDictionary<string, int> RemainingUsesOf(string? variantName)
        => variantName is not null && _wear.TryGetValue(variantName, out Dictionary<string, int>? w)
            ? new Dictionary<string, int>(w)
            : new Dictionary<string, int>();

    /// <summary>
    /// **打了一下**：把这把武器上所有消耗型改装的次数各减 1；减到 0 的**当场脱落**。
    ///
    /// <para>返回 <see cref="ModWearResult"/>：
    /// <c>Changed=false</c> ⇒ 这把武器没有消耗型改装、或还没用光，调用方什么都不用做；
    /// <c>Changed=true</c> ⇒ 武器**变了**，调用方要把库存里那件 <c>Item</c> 和角色手上的武器换成
    /// <c>WeaponName</c>（可能是另一个变体名，也可能是**基础武器名**——改装掉光了），
    /// 并把 <c>BrokenModNames</c> 报给玩家看。</para>
    ///
    /// <para>脱落后**旧变体名当场注销**（spec/resolved/wear 全清）：实例名唯一 ⇒ 只有一个持有者 ⇒
    /// 换过去之后再没人引用它，留着只会让存档越滚越大。</para>
    /// </summary>
    public static ModWearResult ConsumeUse(string? variantName)
    {
        if (variantName is null
            || !_wear.TryGetValue(variantName, out Dictionary<string, int>? wear)
            || !_specs.TryGetValue(variantName, out ModdedWeaponSpec? spec))
        {
            return new ModWearResult(false, variantName ?? "", System.Array.Empty<string>());
        }

        var broken = new List<string>();
        foreach (string modName in wear.Keys.ToList())
        {
            int left = wear[modName] - 1;
            wear[modName] = left;
            if (left <= 0) broken.Add(modName);
        }

        if (broken.Count == 0)
        {
            return new ModWearResult(false, variantName, System.Array.Empty<string>());
        }

        // 有件脱落了 ⇒ 摘掉它，用**剩下的**改装重新登记一把（或回落成基础武器）。
        var survivors = spec.ModNames.Where(n => !broken.Contains(n)).ToList();
        var carryOver = wear.Where(kv => !broken.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        Unregister(variantName);

        if (survivors.Count == 0)
        {
            // 改装掉光了：回到一把干干净净的原厂武器（它本来就在 WeaponTable 里，不用登记）。
            return new ModWearResult(true, spec.BaseWeaponName, broken);
        }

        Weapon? baseWeapon = BaseWeaponByName(spec.BaseWeaponName);
        IReadOnlyList<WeaponMod> mods = baseWeapon is null
            ? System.Array.Empty<WeaponMod>()
            : WeaponModCatalog.For(baseWeapon).Where(m => survivors.Contains(m.Name)).ToList();

        if (baseWeapon is null || mods.Count != survivors.Count)
        {
            // 目录变了、剩下的改装已不认识 ⇒ 与读档同姿态：回落成基础武器，不让武器凭空消失。
            return new ModWearResult(true, spec.BaseWeaponName, broken);
        }

        ModdedWeapon remade = WeaponMods.ApplyMods(baseWeapon, mods);
        string newName = Register(remade);   // 仍带消耗型件 ⇒ 拿新实例名；否则就是普通变体名

        // 幸存的消耗型改装要把**已经磨掉的次数带过去**（Register 会按满次数建表，这里覆写成真实剩余）。
        if (carryOver.Count > 0 && _wear.TryGetValue(newName, out Dictionary<string, int>? fresh))
        {
            foreach (KeyValuePair<string, int> kv in carryOver) fresh[kv.Key] = kv.Value;
        }

        return new ModWearResult(true, newName, broken);
    }

    /// <summary>注销一个变体（spec + 解析缓存 + 耐久）。改装脱落后调用——旧实例名再没有持有者。</summary>
    private static void Unregister(string variantName)
    {
        _specs.Remove(variantName);
        _resolved.Remove(variantName);
        _wear.Remove(variantName);
    }

    /// <summary>
    /// 一把武器（含改装变体）的**重量倍率** —— 各改装 <see cref="WeaponMod.WeightMultiplier"/> **连乘**。
    /// 原厂武器 / 未登记的名字 ⇒ 1.0。
    /// <para>
    /// 🔴 用户原话「**我希望重量在改装中是一个重要的因素**」的落点：<c>ItemWeights.WeaponKg</c> 用它
    /// 把改装的增减重乘到基础武器重量上 ⇒ 改装的重量**真的进负重账**（而不是一个只存不用的 flavor 字段）。
    /// </para>
    /// </summary>
    public static double WeightMultiplierOf(string? variantName)
    {
        if (variantName is null || !_specs.TryGetValue(variantName, out ModdedWeaponSpec? spec)) return 1.0;
        if (BaseWeaponByName(spec.BaseWeaponName) is not { } baseWeapon) return 1.0;

        IReadOnlyList<WeaponMod> catalog = WeaponModCatalog.For(baseWeapon);
        double mult = 1.0;
        foreach (string modName in spec.ModNames)
        {
            WeaponMod? mod = catalog.FirstOrDefault(m => m.Name == modName);
            if (mod is not null) mult *= mod.WeightMultiplier;   // 百分比一律乘算（CLAUDE.md 铁律）
        }
        return mult;
    }

    /// <summary>
    /// [T69] 一把武器（含改装变体）的**护手挡格**否决几率——各改装取最大值。原厂武器 / 未登记 / 无护手挡格 ⇒ 0。
    /// 供 <c>Actor.ReceiveAttack</c> 把它连同"持械手部位集"喂进 <c>CombatEngine.ResolveHit</c>。
    /// </summary>
    public static double HandGuardNegateChanceOf(string? variantName)
        => ModsOf(variantName).Select(m => m.HandGuardNegateChance).DefaultIfEmpty(0.0).Max();

    /// <summary>
    /// [T69] 一把武器（含改装变体）的**弩盾**正面远程否决几率——各改装取最大值。原厂武器 / 未登记 / 无弩盾 ⇒ 0。
    /// 供 <c>Actor.ReceiveAttack</c> 判"正面锥内的远程攻击是否整发否决"。
    /// </summary>
    public static double FrontalRangedNegateChanceOf(string? variantName)
        => ModsOf(variantName).Select(m => m.FrontalRangedNegateChance).DefaultIfEmpty(0.0).Max();

    /// <summary>[T69] 弩盾正面锥半角（度）：取带弩盾那条改装的值；无弩盾则 60。</summary>
    public static double FrontalNegateHalfAngleDegOf(string? variantName)
        => ModsOf(variantName).Where(m => m.FrontalRangedNegateChance > 0)
            .Select(m => m.FrontalNegateHalfAngleDeg).DefaultIfEmpty(60.0).First();

    /// <summary>一把变体名对应的已装改装列表（原厂武器 / 未登记 ⇒ 空）。按 spec.ModNames 从当前 catalog 回查。</summary>
    private static IReadOnlyList<WeaponMod> ModsOf(string? variantName)
    {
        if (variantName is null || !_specs.TryGetValue(variantName, out ModdedWeaponSpec? spec))
            return System.Array.Empty<WeaponMod>();
        if (BaseWeaponByName(spec.BaseWeaponName) is not { } baseWeapon)
            return System.Array.Empty<WeaponMod>();

        IReadOnlyList<WeaponMod> catalog = WeaponModCatalog.For(baseWeapon);
        var mods = new List<WeaponMod>();
        foreach (string modName in spec.ModNames)
        {
            WeaponMod? mod = catalog.FirstOrDefault(m => m.Name == modName);
            if (mod is not null) mods.Add(mod);
        }
        return mods;
    }

    /// <summary>
    /// 按名回查一把**改装变体**武器。查得到 ⇒ true。
    /// <b>调用方约定</b>：先查 <c>WeaponTable</c>（原厂武器），落空了再问这里（改装变体）。
    /// </summary>
    public static bool TryResolve(string? name, out Weapon weapon)
    {
        weapon = null!;
        if (name is null) return false;

        if (_resolved.TryGetValue(name, out Weapon? cached))
        {
            weapon = cached;
            return true;
        }

        // 有 spec 但没缓存（刚 Restore 完）⇒ 按当前规则重算一次；
        // 组合已因规则收窄而非法 ⇒ 回落成基础武器（见 RebuildOrBase），不让这把武器凭空消失。
        if (_specs.TryGetValue(name, out ModdedWeaponSpec? spec) && RebuildOrBase(spec) is { } rebuilt)
        {
            _resolved[name] = rebuilt;
            weapon = rebuilt;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 按名回查武器：**先原厂表、后改装表**。这是"给我一个武器名，还我一把枪"的唯一正确入口，
    /// 装备 / 存档 / 负重都该走它。查不到 ⇒ null。
    /// </summary>
    public static Weapon? WeaponByName(string? name)
    {
        if (BaseWeaponByName(name) is { } stock) return stock;
        return TryResolve(name, out Weapon w) ? w : null;
    }

    /// <summary>
    /// **载入用的还原入口**：先按当前规则重合成（<see cref="Rebuild"/>）；<b>合成不出来就回落成基础武器</b>。
    ///
    /// <para>
    /// 🔴 <b>为什么要回落</b>（用户拍板，[T29]）：规则一收窄，老存档里的合法组合就可能变非法——
    /// 比如「短弓（截短枪管）」，在弓弩被划出枪械改装白名单之后就还原不出来了。
    /// 若照旧返回 null，那把弓会**从玩家背包里凭空消失**（变体进不了 <c>_resolved</c>），材料也不退。
    /// 回落之后：<b>弓还在，只是改装没了</b>——这才是"数据收窄"该有的降级姿态，不是"版本升级作废旧档"。
    /// </para>
    /// <para>
    /// 基础武器<b>本身</b>都查不到（那把武器被从 <c>WeaponTable</c> 删了，如栓动猎枪）⇒ 仍返回 null：
    /// 无处可落，只能认它没了。这两种失败是不同的事，别混。
    /// </para>
    /// </summary>
    public static Weapon? RebuildOrBase(ModdedWeaponSpec spec)
        => Rebuild(spec) ?? BaseWeaponByName(spec?.BaseWeaponName);

    /// <summary>
    /// 把一条 spec 用**当前**改装规则重新合成成武器；基础武器/改装名已不存在（表改过了）⇒ null。
    /// <para>
    /// ⚠️ 这是**严格**版，是"这条组合当前还合不合法"的判据。**载入路径别直接用它**——
    /// 用 <see cref="RebuildOrBase"/>，否则非法组合会让整把武器消失。
    /// </para>
    /// </summary>
    public static Weapon? Rebuild(ModdedWeaponSpec spec)
    {
        if (spec is null) return null;

        Weapon? baseWeapon = BaseWeaponByName(spec.BaseWeaponName);
        if (baseWeapon is null) return null;

        IReadOnlyList<WeaponMod> catalog = WeaponModCatalog.For(baseWeapon);
        var mods = new List<WeaponMod>();
        foreach (string modName in spec.ModNames)
        {
            WeaponMod? mod = catalog.FirstOrDefault(m => m.Name == modName);
            if (mod is null) return null;   // 改装被从目录里删了 ⇒ 这把枪还原不出来
            mods.Add(mod);
        }

        try
        {
            // 用 spec 自己的变体名重建 —— 对**永久改装**这与自动拼名逐字相同（零变化）；
            // 对**消耗型实例**（"短剑（锋刃研磨）#1"）则保住了 `Item.RefKey == Weapon.Name` 这条隐含不变式。
            return WeaponMods.ApplyMods(baseWeapon, mods, spec.VariantName).Weapon;
        }
        catch (WeaponModException)
        {
            return null;   // 规则改严了，老组合已非法
        }
    }

    /// <summary>
    /// 某个变体名的**基础武器名**（"步枪（刺刀型）" → "步枪"）；不是已登记的变体 ⇒ null。
    /// 负重表等"按原厂武器名索引"的地方靠它回落——否则一把改装武器会按"未登记武器"处理。
    /// </summary>
    public static string? BaseNameOf(string? variantName)
        => variantName is not null && _specs.TryGetValue(variantName, out ModdedWeaponSpec? spec)
            ? spec.BaseWeaponName
            : null;

    /// <summary>全部已登记变体的 spec（供存档落盘）。</summary>
    public static IReadOnlyList<ModdedWeaponSpec> Specs => _specs.Values.ToList();

    /// <summary>
    /// 全部消耗型改装的**剩余次数**快照（变体名 → 改装名 → 次数），供存档落盘。
    /// <b>与 <see cref="Specs"/> 分开两张表**是刻意的**</b>：spec 是不可变身份、耐久是可变状态（见 <see cref="ModdedWeaponSpec"/> 类注）。
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> Wear
        => _wear.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, int>)new Dictionary<string, int>(kv.Value));

    /// <summary>
    /// 读档：清空后按 spec 全量重建（数值用**当前**规则重算，见类注）。
    /// <para>
    /// 组合因规则收窄而变非法（如老档里「短弓（截短枪管）」）⇒ <b>回落成基础武器</b>而非丢弃，
    /// 玩家不会平白少一把武器（[T29] 用户拍板，见 <see cref="RebuildOrBase"/>）。
    /// </para>
    /// <para>
    /// [T47] <paramref name="wear"/> = 消耗型改装的剩余次数。
    /// <b>🔴 老存档没有这张表（存档 v3 之前根本没有消耗型改装）⇒ 缺条目一律补成「满次数」，不是 0</b>
    /// —— 默认 0 会让老档一读出来，所有研磨过的刀当场全部脱落，凭空没收玩家的东西。
    /// </para>
    /// <para>
    /// <b>实例序号要往前推</b>（<c>_instanceSeq</c> = 已见过的最大后缀）：不推的话，读档后新造的第一把
    /// 消耗型武器会拿到 "#1"，**顶掉存档里已有的 "#1"** —— 这与 <c>impl-traps</c> 踩过的
    /// <c>_trapSeq</c> 是同一个坑（读旧档后新家具撞名顶掉旧的）。
    /// </para>
    /// </summary>
    public static void Restore(
        IEnumerable<ModdedWeaponSpec>? specs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? wear = null)
    {
        Clear();
        if (specs is null) return;

        foreach (ModdedWeaponSpec spec in specs)
        {
            if (string.IsNullOrEmpty(spec?.VariantName)) continue;
            _specs[spec!.VariantName] = spec;
            if (RebuildOrBase(spec) is { } w) _resolved[spec.VariantName] = w;

            _instanceSeq = System.Math.Max(_instanceSeq, InstanceOrdinalOf(spec.VariantName));
            RestoreWearFor(spec, wear);
        }
    }

    /// <summary>
    /// 还原一个变体的耐久：存档里有就用存档的（夹进 1..满次数），没有就补**满次数**（老档兼容，见 <see cref="Restore"/>）。
    /// 该变体上根本没有消耗型改装 ⇒ 不建条目。
    /// </summary>
    private static void RestoreWearFor(
        ModdedWeaponSpec spec,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? wear)
    {
        if (BaseWeaponByName(spec.BaseWeaponName) is not { } baseWeapon) return;

        IReadOnlyList<WeaponMod> catalog = WeaponModCatalog.For(baseWeapon);
        Dictionary<string, int>? saved = wear is not null
            && wear.TryGetValue(spec.VariantName, out IReadOnlyDictionary<string, int>? s)
                ? new Dictionary<string, int>(s)
                : null;

        var entry = new Dictionary<string, int>();
        foreach (string modName in spec.ModNames)
        {
            WeaponMod? mod = catalog.FirstOrDefault(m => m.Name == modName);
            if (mod is null || !mod.IsConsumable) continue;

            int full = mod.UsesBeforeBreak!.Value;
            int left = saved is not null && saved.TryGetValue(modName, out int n) ? n : full;
            entry[modName] = System.Math.Clamp(left, 1, full);   // 存档里 ≤0 是不可能状态（该脱落了）⇒ 兜成 1
        }

        if (entry.Count > 0) _wear[spec.VariantName] = entry;
    }

    /// <summary>从实例名尾巴上解出序号（"短剑（锋刃研磨）#7" → 7）；不是实例名 ⇒ 0。</summary>
    private static int InstanceOrdinalOf(string variantName)
    {
        int at = variantName.LastIndexOf(InstanceMarker);
        return at >= 0 && int.TryParse(variantName[(at + 1)..], out int n) ? n : 0;
    }

    /// <summary>清空（换场景必调；否则上一局的改装枪会漏进下一局）。</summary>
    public static void Clear()
    {
        _specs.Clear();
        _resolved.Clear();
        _wear.Clear();
        _instanceSeq = 0;
    }
}
