using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【数值外置 · archery 段的零漂移 A/B 焊死】config-archery 单。
/// <para>
/// 弓弩子系统的数据已从 <see cref="Archery"/>/<see cref="ArrowTable"/> 的 C# 常量搬到
/// <c>godot/data/config/archery.json</c>：4 种箭（<see cref="ArrowDef"/>）+ 一组可调标量
/// （穿透封顶 / 散布下限 / 箭矢回收率 / 《弓与箭之道》三项被动）。<see cref="ArrowTable"/> 工厂身体改成
/// <c>=&gt; CombatCatalog.Section&lt;ArcheryConfig&gt;().Arrow("...")</c>、<see cref="Archery"/> 的常量改成读 config 的静态属性。
/// 本文件钉死「搬家没搬错一个数」，照 <see cref="WeaponConfigMigrationTests"/> 立的范式：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：首次取箭触发懒加载，catalog 初始化成功。</item>
///   <item><b>字面值锚定（A/B）</b>：每种箭的每类字段 + 每个标量常量 == 迁移前原始常量，<b>double 用位级相等</b>。</item>
///   <item><b>往返保真</b>：ArcheryConfig 段再序列化→反序列化，逐字段位级相等（加载器不丢精度，值无关永久护栏）。</item>
///   <item><b>完整性/顺序</b>：4 种箭全可取、<see cref="ArrowTable.All"/> 顺序不变（低端→高端）、可造 3 种、缺 id fail-fast。</item>
///   <item><b>派生量不入表</b>：<see cref="Archery.BookCooldownMult"/> 由 <see cref="Archery.BookAttackSpeedMult"/> 现算（不落 json）。</item>
/// </list>
/// <para>
/// 🔴 <b>另有 Sim MD5 前后一致的全量证明</b>：默认聚合 Sim（<c>WeaponTable.Arsenal</c>×护甲）<b>结构上不读</b>
/// <see cref="ArrowTable"/>/<see cref="Archery.Combine"/>，故箭数据外置对聚合 MD5 零影响（基线
/// <c>44c28a8efe62f118c2322ad2de38f432</c> 前后逐位不变，config-archery journal）。本文件是段级字段锚点补充。
/// </para>
/// </summary>
public sealed class ArcheryConfigMigrationTests
{
    // ── 接线 ─────────────────────────────────────────────────────────────
    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        var stick = ArrowTable.SharpenedStick();   // 首次访问触发懒加载
        Assert.True(CombatCatalog.IsInitialized, "首次取箭后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal("削尖的木箭", stick.Name);
    }

    [Fact]
    public void All_four_arrows_resolve_with_nonempty_name()
    {
        foreach (var key in AllArrowKeys)
        {
            var a = CombatCatalog.Section<ArcheryConfig>().Arrow(key);
            Assert.False(string.IsNullOrEmpty(a.Name), $"{key} 名字不应为空");
            Assert.Equal(key, a.Key);   // 字典键 == 箭自报的 Key
        }
        Assert.Equal(4, CombatCatalog.Section<ArcheryConfig>().Arrows.Count);
    }

    [Fact]
    public void Missing_arrow_key_fails_fast()
    {
        Assert.Throws<KeyNotFoundException>(() => CombatCatalog.Section<ArcheryConfig>().Arrow("ammo_arrow_no_such"));
    }

    // ── 字面值锚定（A/B）：4 种箭 × 每类字段 ──────────────────────────────
    [Fact]
    public void SharpenedStick_matches_original_literals()
    {
        var a = ArrowTable.SharpenedStick();
        Assert.Equal(ArrowKeys.SharpenedStick, a.Key);
        Assert.Equal("削尖的木箭", a.Name);
        Assert.True(a.Craftable);
        ExactlyEqual(0.75, a.DamageMult);
        ExactlyEqual(0.75, a.PenetrationMult);
        ExactlyEqual(0.75, a.RangeMult);
        ExactlyEqual(1.00, a.CooldownMult);
        ExactlyEqual(1.10, a.SpreadMult);
    }

    [Fact]
    public void Handmade_is_baseline_all_ones()
    {
        var a = ArrowTable.Handmade();
        Assert.Equal(ArrowKeys.Handmade, a.Key);
        Assert.True(a.Craftable);
        ExactlyEqual(1.00, a.DamageMult);
        ExactlyEqual(1.00, a.PenetrationMult);
        ExactlyEqual(1.00, a.RangeMult);
        ExactlyEqual(1.00, a.CooldownMult);
        ExactlyEqual(1.00, a.SpreadMult);
    }

    [Fact]
    public void Heavy_matches_original_literals()
    {
        var a = ArrowTable.Heavy();
        Assert.Equal(ArrowKeys.Heavy, a.Key);
        Assert.True(a.Craftable);
        ExactlyEqual(1.25, a.DamageMult);
        ExactlyEqual(1.75, a.PenetrationMult);   // 破甲专精一路加码
        ExactlyEqual(0.75, a.RangeMult);
        ExactlyEqual(1.10, a.CooldownMult);
        ExactlyEqual(1.25, a.SpreadMult);
    }

    [Fact]
    public void Carbon_matches_original_literals_and_is_not_craftable()
    {
        var a = ArrowTable.Carbon();
        Assert.Equal(ArrowKeys.Carbon, a.Key);
        Assert.False(a.Craftable);               // 只能搜刮
        ExactlyEqual(1.25, a.DamageMult);
        ExactlyEqual(1.25, a.PenetrationMult);
        ExactlyEqual(1.20, a.RangeMult);
        ExactlyEqual(0.90, a.CooldownMult);
        ExactlyEqual(0.70, a.SpreadMult);
    }

    // ── 字面值锚定（A/B）：可调标量常量 ──────────────────────────────────
    [Fact]
    public void Tunable_scalars_match_original_constants()
    {
        ExactlyEqual(0.95, Archery.MaxPenetration);
        ExactlyEqual(0.5, Archery.MinSpreadDegrees);
        ExactlyEqual(0.25, Archery.BaseArrowRecoveryRate);
        ExactlyEqual(0.50, Archery.SkilledArrowRecoveryRate);
        ExactlyEqual(1.0, Archery.BookRangeMult);          // [T68] 射程加成删除
        ExactlyEqual(1.2, Archery.BookFlightSpeedMult);    // [T68] 弹道速度 +20%
        ExactlyEqual(0.90, Archery.BookSpreadMult);
        ExactlyEqual(1.02, Archery.BookAttackSpeedMult);
    }

    [Fact]
    public void BookCooldownMult_is_derived_not_stored()
    {
        // 派生量：出手间隔乘子 = 1 / 攻速乘子，由代码现算，不落 archery.json。
        ExactlyEqual(1.0 / Archery.BookAttackSpeedMult, Archery.BookCooldownMult);
        Assert.True(Archery.BookCooldownMult < 1.0, "攻速+2% ⇒ 出手间隔必须变短");
    }

    // ── 完整性 / 顺序 ────────────────────────────────────────────────────
    [Fact]
    public void All_order_unchanged_low_to_high()
    {
        var all = ArrowTable.All;
        Assert.Equal(4, all.Count);
        Assert.Equal(ArrowKeys.SharpenedStick, all[0].Key);   // 低端
        Assert.Equal(ArrowKeys.Handmade, all[1].Key);
        Assert.Equal(ArrowKeys.Heavy, all[2].Key);
        Assert.Equal(ArrowKeys.Carbon, all[3].Key);           // 高端
    }

    [Fact]
    public void Craftable_excludes_carbon()
    {
        Assert.Equal(3, ArrowTable.Craftable().Count());
        Assert.DoesNotContain(ArrowTable.Craftable(), a => a.Key == ArrowKeys.Carbon);
        Assert.True(ArrowTable.IsArrow(ArrowKeys.Handmade));
        Assert.Null(ArrowTable.Find("ammo_arrow_no_such"));
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）─────────────────────
    [Fact]
    public void Every_arrow_survives_json_round_trip_bit_for_bit()
    {
        var section = CombatCatalog.Section<ArcheryConfig>();
        foreach (var (key, a) in section.Arrows)
        {
            string json = JsonSerializer.Serialize(a, CombatConfigLoader.Options);
            var back = JsonSerializer.Deserialize<ArrowDef>(json, CombatConfigLoader.Options)!;
            AssertArrowBitEqual(a, back, key);
        }
    }

    [Fact]
    public void Whole_section_survives_json_round_trip_bit_for_bit()
    {
        var section = CombatCatalog.Section<ArcheryConfig>();
        string json = JsonSerializer.Serialize(section, CombatConfigLoader.Options);
        var back = (ArcheryConfig)new ArcheryConfig().FromJson(json, CombatConfigLoader.Options);

        // 标量逐字段位级
        ExactlyEqual(section.MaxPenetration, back.MaxPenetration);
        ExactlyEqual(section.MinSpreadDegrees, back.MinSpreadDegrees);
        ExactlyEqual(section.BaseArrowRecoveryRate, back.BaseArrowRecoveryRate);
        ExactlyEqual(section.SkilledArrowRecoveryRate, back.SkilledArrowRecoveryRate);
        ExactlyEqual(section.BookRangeMult, back.BookRangeMult);
        ExactlyEqual(section.BookFlightSpeedMult, back.BookFlightSpeedMult);
        ExactlyEqual(section.BookSpreadMult, back.BookSpreadMult);
        ExactlyEqual(section.BookAttackSpeedMult, back.BookAttackSpeedMult);
        // 箭逐支位级
        Assert.Equal(section.Arrows.Count, back.Arrows.Count);
        foreach (var (key, a) in section.Arrows)
        {
            AssertArrowBitEqual(a, back.Arrow(key), key);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static readonly string[] AllArrowKeys =
    {
        ArrowKeys.SharpenedStick, ArrowKeys.Handmade, ArrowKeys.Heavy, ArrowKeys.Carbon,
    };

    /// <summary>double 位级相等（比 == 更严）——证明"往返一位不差"。</summary>
    private static void ExactlyEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    /// <summary>反射比对两支箭的全部可读属性（double 走位级、bool/string 照常）。</summary>
    private static void AssertArrowBitEqual(ArrowDef a, ArrowDef b, string key)
    {
        foreach (var p in typeof(ArrowDef).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? va = p.GetValue(a);
            object? vb = p.GetValue(b);
            if (va is double da && vb is double db)
            {
                Assert.True(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db),
                    $"{key}.{p.Name} double 漂移：{da} vs {db}");
            }
            else
            {
                Assert.True(Equals(va, vb), $"{key}.{p.Name} 不等：{va} vs {vb}");
            }
        }
    }
}
