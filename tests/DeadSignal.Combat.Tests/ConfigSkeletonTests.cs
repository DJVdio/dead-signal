using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【数值外置骨架 · 高并发扩展契约】config-skeleton 单。
/// <para>
/// 钉死"让后续 armor/ammo/archery/body 各单能高度并行、最小化共享文件争用"的架构不变量：
/// </para>
/// <list type="number">
///   <item><b>加载器反射驱动</b>：<see cref="CombatConfigLoader.Parse"/> 自动发现并加载 <see cref="CombatConfig"/> 上
///     <b>每一个</b> <see cref="IConfigSection"/> 段——加新子系统<b>不用改加载器主体</b>。</item>
///   <item><b>泛型段访问</b>：<c>CombatCatalog.Section&lt;T&gt;()</c> 让新子系统在自己的 Table 文件里取用，
///     <b>不用往 CombatCatalog 加方法</b>。</item>
///   <item><b>段自描述</b>：段类自报 <see cref="IConfigSection.FileName"/> + 自解析 <see cref="IConfigSection.FromJson"/>，
///     json 保持裸载荷。</item>
///   <item><b>fail-fast</b>：缺文件/空即抛，不软回落。</item>
/// </list>
/// </summary>
public sealed class ConfigSkeletonTests
{
    private static Func<string, string> RealReadText()
    {
        string dir = CombatConfigFiles.LocateConfigDir();
        return file => File.ReadAllText(Path.Combine(dir, file));
    }

    [Fact]
    public void Parse_is_reflection_driven_loads_every_section()
    {
        var config = CombatConfigLoader.Parse(RealReadText());

        // 反射：CombatConfig 上每个 IConfigSection 段都应被装配成非默认（已加载）实例。
        var sectionProps = typeof(CombatConfig)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => typeof(IConfigSection).IsAssignableFrom(p.PropertyType))
            .ToList();

        Assert.NotEmpty(sectionProps);
        foreach (var p in sectionProps)
        {
            Assert.IsAssignableFrom<IConfigSection>(p.GetValue(config));
        }

        // weapons 段确实装满（30 把）——证明"发现→读盘→反序列化"整条自动链走通。
        Assert.Equal(30, config.Weapons.ById.Count);
    }

    [Fact]
    public void Parse_fails_fast_on_missing_file()
    {
        // readText 对 weapons.json 返回 null ⇒ 缺文件 fail-fast（不软回落成空表）。
        Assert.Throws<InvalidOperationException>(
            () => CombatConfigLoader.Parse(_ => null!));
    }

    [Fact]
    public void Parse_fails_fast_on_malformed_json()
    {
        Assert.Throws<InvalidOperationException>(
            () => CombatConfigLoader.Parse(_ => "{ not valid json "));
    }

    [Fact]
    public void Section_generic_access_and_filename()
    {
        var weapons = CombatCatalog.Section<WeaponConfig>();
        Assert.Equal("weapons.json", weapons.FileName);
        Assert.Equal("匕首", weapons.Get("dagger").Name);
        Assert.Throws<KeyNotFoundException>(() => weapons.Get("nope"));
    }

    [Fact]
    public void Weapon_config_from_json_round_trips_bit_for_bit()
    {
        // 段自解析：把 catalog 的裸字典再序列化，喂回 FromJson，逐把武器位级相等。
        var original = CombatCatalog.Section<WeaponConfig>();
        string json = JsonSerializer.Serialize(
            (Dictionary<string, Weapon>)original.ById.ToDictionary(kv => kv.Key, kv => kv.Value),
            CombatConfigLoader.Options);

        var reloaded = (WeaponConfig)new WeaponConfig().FromJson(json, CombatConfigLoader.Options);
        Assert.Equal(original.ById.Count, reloaded.ById.Count);
        foreach (var (id, w) in original.ById)
        {
            var b = reloaded.ById[id];
            Assert.Equal(w.Name, b.Name);
            Assert.True(BitConverter.DoubleToInt64Bits(w.Penetration) == BitConverter.DoubleToInt64Bits(b.Penetration),
                $"{id}.Penetration 位级漂移");
            Assert.Equal(w.DamageType, b.DamageType);
        }
    }
}
