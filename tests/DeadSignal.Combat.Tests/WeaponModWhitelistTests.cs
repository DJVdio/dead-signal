using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 改装的装配约束：**从「武器大类」换成「逐把枪的白名单」**（用户拍板）。
///
/// <para><b>为什么要换</b>：按大类卡，用户没法表达"这个改装只能装步枪和霰弹枪"。
/// 白名单让约束的粒度落到**具体武器**上，他就能在 wiki 上逐把勾。</para>
///
/// <para><b>🔴 迁移的硬要求是「行为零变化」</b>：把每条改装原来的大类**展开成等价的武器名清单**。
/// 老存档里的改装枪靠 <c>ModdedWeaponRegistry.Rebuild</c> 用**当前规则**重算 —— 规则一旦收严，
/// 老组合就变非法、那把枪会**静默失效**（<c>Rebuild</c> 返回 null，变体进不了 <c>_resolved</c>）。
/// 所以迁移这一步**一个组合都不许少**，收窄留给用户自己在 wiki 上做。</para>
/// </summary>
public class WeaponModWhitelistTests
{
    // ── 白名单的基本语义 ──

    [Fact]
    public void 白名单里有这把枪_才装得上()
    {
        WeaponMod mod = WeaponModCatalog.LightenedStock();
        Assert.Contains(WeaponTable.Rifle().Name, mod.FitsWeapons);

        // 不在白名单里的（长剑）→ 装不上
        Assert.DoesNotContain(WeaponTable.Longsword().Name, mod.FitsWeapons);
        Assert.Throws<WeaponModException>(
            () => WeaponMods.ApplyMods(WeaponTable.Longsword(), new[] { mod }));
    }

    [Fact]
    public void 目录按武器名过滤_不再按大类()
    {
        // 步枪拿得到枪械改装
        var forRifle = WeaponModCatalog.For(WeaponTable.Rifle()).Select(m => m.Name).ToList();
        Assert.Contains("轻质化枪托", forRifle);

        // 长剑拿不到枪械改装，只拿得到刃类的
        var forSword = WeaponModCatalog.For(WeaponTable.Longsword()).Select(m => m.Name).ToList();
        Assert.DoesNotContain("轻质化枪托", forSword);
        Assert.NotEmpty(forSword);
    }

    // ── 🔴 迁移护栏：行为必须零变化 ──

    [Fact]
    public void 迁移零变化_每条改装的白名单_恰好等于它原本那个大类的全部武器()
    {
        // 原规则：mod 适用于某把武器 ⟺ WeaponMods.ClassOf(武器) == mod.RequiredClass
        // 新规则：mod 适用于某把武器 ⟺ mod.FitsWeapons.Contains(武器名)
        // 两者必须对**每一把武器**给出同样的答案 —— 否则老存档里的改装枪会静默失效。
        IReadOnlyList<Weapon> all = WeaponModCatalog.AllModdableWeapons();

        foreach (WeaponMod mod in WeaponModCatalog.All())
        {
            WeaponClass legacyClass = WeaponModCatalog.LegacyClassOf(mod);

            foreach (Weapon w in all)
            {
                bool oldAllows = WeaponMods.ClassOf(w) == legacyClass;
                bool newAllows = mod.FitsWeapons.Contains(w.Name);
                Assert.True(oldAllows == newAllows,
                    $"迁移改变了行为：改装「{mod.Name}」对「{w.Name}」—— 旧规则 {oldAllows}，新白名单 {newAllows}");
            }
        }
    }

    [Fact]
    public void 迁移零变化_弓弩仍在枪械改装的白名单里_这是引擎的现行行为()
    {
        // ⚠️ 这条**不是**在为一个荒唐的行为背书，而是在钉死"迁移没有偷偷改行为"。
        //
        // 引擎的 WeaponMods.ClassOf 是 `IsRanged ? Firearm : ...` ⇒ **弓弩也被算作"枪械"**
        // ⇒ 现在真的能把「截短枪管」装到短弓上（实测过）。这是个**潜伏已久的 bug**。
        //
        // 白名单迁移把它**暴露出来**，并且给了用户一个亲手收窄的入口（在 wiki 上把弓从白名单里划掉）。
        // 但迁移这一步**不能自己改行为** —— 否则老存档里"装了枪管的弓"会静默失效。
        WeaponMod barrel = WeaponModCatalog.SawnOffBarrel();
        Assert.Contains(WeaponTable.ShortBow().Name, barrel.FitsWeapons);
        Assert.Contains(WeaponTable.HeavyCrossbow().Name, barrel.FitsWeapons);

        // 装得上（不抛）—— 与迁移前一致
        WeaponMods.ApplyMods(WeaponTable.ShortBow(), new[] { barrel });
    }

    // ── 存档兼容 ──

    [Fact]
    public void 存档_白名单收窄后_老改装枪还原不出来_但不崩()
    {
        // 这是"收窄"的代价，必须心里有数：Rebuild 拿当前规则重算，组合非法就返回 null。
        // 引擎本来就为这种情况留了退路（不抛异常、不崩），用户在 wiki 上收窄白名单前该知道这一点。
        WeaponMod mod = WeaponModCatalog.LightenedStock();
        Weapon rifle = WeaponTable.Rifle();

        // 正常：能还原
        var spec = new ModdedWeaponSpec("测试变体", rifle.Name, new[] { mod.Name });
        Assert.NotNull(ModdedWeaponRegistry.Rebuild(spec));

        // 基础武器根本不存在 ⇒ 还原不出来，但返回 null 而不是抛
        var bad = new ModdedWeaponSpec("坏变体", "不存在的枪", new[] { mod.Name });
        Assert.Null(ModdedWeaponRegistry.Rebuild(bad));
    }

    // ── 数据完整性 ──

    [Fact]
    public void 全表改装_白名单都不为空()
    {
        foreach (WeaponMod mod in WeaponModCatalog.All())
        {
            Assert.True(mod.FitsWeapons.Count > 0,
                $"改装「{mod.Name}」的白名单是空的 —— 那它哪把武器都装不上，等于废件");
        }
    }

    [Fact]
    public void 全表改装_白名单里的名字_都是真实存在的武器()
    {
        var known = WeaponModCatalog.AllModdableWeapons().Select(w => w.Name).ToHashSet();
        foreach (WeaponMod mod in WeaponModCatalog.All())
        {
            foreach (string name in mod.FitsWeapons)
            {
                Assert.True(known.Contains(name),
                    $"改装「{mod.Name}」的白名单里有「{name}」，但武器表里没有这把武器（写错了名字？）");
            }
        }
    }
}
