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
/// </summary>
public sealed record ModdedWeaponSpec(
    string VariantName,
    string BaseWeaponName,
    IReadOnlyList<string> ModNames);

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

    /// <summary>按名取**基础**武器（WeaponTable 全表）；不在表里 ⇒ null。</summary>
    public static Weapon? BaseWeaponByName(string? name)
        => name is not null && _baseByName.TryGetValue(name, out Weapon? w) ? w : null;

    /// <summary>登记一把改装变体（幂等：同名重复登记覆盖）。返回其变体名，即库存 <c>Item.RefKey</c>。</summary>
    public static string Register(ModdedWeapon modded)
    {
        if (modded is null) throw new System.ArgumentNullException(nameof(modded));

        string variantName = modded.Weapon.Name;
        _specs[variantName] = new ModdedWeaponSpec(
            variantName,
            modded.BaseWeaponName,
            modded.AppliedMods.Select(m => m.Name).ToList());
        _resolved[variantName] = modded.Weapon;
        return variantName;
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

        // 有 spec 但没缓存（刚 Restore 完）⇒ 按当前规则重算一次。
        if (_specs.TryGetValue(name, out ModdedWeaponSpec? spec) && Rebuild(spec) is { } rebuilt)
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

    /// <summary>把一条 spec 用**当前**改装规则重新合成成武器；基础武器/改装名已不存在（表改过了）⇒ null。</summary>
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
            return WeaponMods.ApplyMods(baseWeapon, mods).Weapon;
        }
        catch (WeaponModException)
        {
            return null;   // 规则改严了，老组合已非法
        }
    }

    /// <summary>
    /// 某个变体名的**基础武器名**（"步枪（刺刀型）" → "步枪"）；不是已登记的变体 ⇒ null。
    /// 负重表等"按原厂武器名索引"的地方靠它回落——否则一把改装狙击枪会按"未登记武器"算成 2kg（比手枪还轻）。
    /// </summary>
    public static string? BaseNameOf(string? variantName)
        => variantName is not null && _specs.TryGetValue(variantName, out ModdedWeaponSpec? spec)
            ? spec.BaseWeaponName
            : null;

    /// <summary>全部已登记变体的 spec（供存档落盘）。</summary>
    public static IReadOnlyList<ModdedWeaponSpec> Specs => _specs.Values.ToList();

    /// <summary>读档：清空后按 spec 全量重建（数值用**当前**规则重算，见类注）。</summary>
    public static void Restore(IEnumerable<ModdedWeaponSpec>? specs)
    {
        Clear();
        if (specs is null) return;

        foreach (ModdedWeaponSpec spec in specs)
        {
            if (string.IsNullOrEmpty(spec?.VariantName)) continue;
            _specs[spec!.VariantName] = spec;
            if (Rebuild(spec) is { } w) _resolved[spec.VariantName] = w;
        }
    }

    /// <summary>清空（换场景必调；否则上一局的改装枪会漏进下一局）。</summary>
    public static void Clear()
    {
        _specs.Clear();
        _resolved.Clear();
    }
}
