using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【武器改装可调数值外置 · godot 侧配置范式的零漂移 A/B 焊死】config-weaponmods 单。
/// <para>
/// <see cref="WeaponModCatalog"/> 20 条改装的可调数字（各 StatMod 乘子/加值、<c>WeightMultiplier</c>、护手挡格/弩盾
/// 否决几率与半角、近战型态攻速折扣 <c>MeleeFormSpeed</c>）已从 C# 字面量搬到
/// <c>godot/data/config/weaponmods.json</c>，工厂身体改成 <c>S(id,stat)</c>/<c>T(id)</c>/<c>Cfg.MeleeFormSpeed</c> 读
/// <see cref="WeaponModConfig"/> 段。运算方式(Mul/Add/Set)、作用的 <see cref="WeaponStat"/>、适配武器白名单/
/// <see cref="WeaponPart"/>/<c>Id</c> 是**结构**、仍在代码。本文件钉死「搬家没搬错一个数」。
/// </para>
/// <para>
/// 🔴 <b>零漂移＝结构性证明 + 位级 A/B</b>：<c>WeaponModCatalog</c> 只被 Tests+WikiExtract Link，<b>Sim 零引用</b>
/// （grep 确认无 WeaponModCatalog/ModdedWeapon/ApplyMods）⇒ 改装不进 Sim 结算路径 ⇒ 既有武器×护甲的 Sim 输出
/// 根本读不到本段，无需 Sim MD5（与 ammo/archery 箭外置同理）。故这里的位级字面锚定 + 往返保真即零漂移铁证。
/// </para>
/// </summary>
public sealed class WeaponModConfigMigrationTests
{
    // ── golden：迁移前 WeaponModCatalog 里的原始字面量（A/B 的"旧硬编码"一侧，独立转写）。
    //    改 weaponmods.json 里任一数会让本表变红。Weight=null ⇒ 该改装不改重量(默认1.0，未外置)。
    private sealed record Golden(
        double? Weight,
        (WeaponStat Stat, double Val)[] Stats,
        double? HandGuard = null,
        double? Frontal = null,
        double? HalfAngle = null);

    private const double GoldenMeleeFormSpeed = 0.85;

    private static readonly IReadOnlyDictionary<string, Golden> GoldenById = new Dictionary<string, Golden>
    {
        // ── 枪械（7）──
        ["lightened_stock"] = new(0.85, new[] { (WeaponStat.BaseSpreadDegrees, 1.10) }),
        ["sawn_off_barrel"] = new(0.80, new[]
        {
            (WeaponStat.MaxRange, 0.80), (WeaponStat.FalloffStart, 0.80), (WeaponStat.Penetration, 0.85),
            (WeaponStat.BaseSpreadDegrees, 1.25), (WeaponStat.DamageMin, 0.90), (WeaponStat.DamageMax, 0.90),
        }),
        ["extended_barrel"] = new(1.35, new[]
        {
            (WeaponStat.MaxRange, 1.20), (WeaponStat.FalloffStart, 1.20), (WeaponStat.BaseSpreadDegrees, 0.80),
            (WeaponStat.AttackInterval, 1.10), (WeaponStat.Penetration, 1.10),
        }),
        ["bayonet"] = new(1.10, new[] { (WeaponStat.BaseSpreadDegrees, 1.03) }),
        ["claw_stock"] = new(1.30, new[] { (WeaponStat.BaseSpreadDegrees, 1.10) }),
        ["trauma_stock"] = new(1.50, new[] { (WeaponStat.BaseSpreadDegrees, 0.97) }),
        ["blade_stock"] = new(1.05, Array.Empty<(WeaponStat, double)>()),
        // ── 近战锐器（6）──
        ["serrated_blade"] = new(null, new[] { (WeaponStat.Penetration, 0.80), (WeaponStat.BleedRateMultiplier, 1.40) }),
        ["honed_edge"] = new(null, new[] { (WeaponStat.Penetration, 1.75) }),
        ["fuller_blade"] = new(0.75, new[]
        {
            (WeaponStat.AttackInterval, 0.85), (WeaponStat.DamageMin, 0.91), (WeaponStat.DamageMax, 0.91),
        }),
        ["weighted_handle"] = new(1.18, new[] { (WeaponStat.DamageMin, 1.06), (WeaponStat.DamageMax, 1.06) }),
        ["lightened_handle"] = new(0.88, new[] { (WeaponStat.AttackInterval, 0.97) }),
        ["grip_wrap_blade"] = new(null, new[] { (WeaponStat.AttackInterval, 0.95) }),
        // ── 近战钝器（2）──
        ["wire_wrap"] = new(1.12, new[] { (WeaponStat.DamageMin, 1.15), (WeaponStat.DamageMax, 1.15) }),
        ["nail_studs"] = new(null, new[] { (WeaponStat.Penetration, 0.03), (WeaponStat.BleedOnHitChance, 0.25) }),
        // ── 近身锐器·防御型（1）──
        ["handguard"] = new(1.10, Array.Empty<(WeaponStat, double)>(), HandGuard: 0.50),
        // ── 弓弩专属（4）──
        ["limb_wrap"] = new(null, new[] { (WeaponStat.AttackInterval, 0.95) }),
        ["compound_limbs"] = new(1.15, new[]
        {
            (WeaponStat.AttackInterval, 1.06), (WeaponStat.DamageMin, 1.08), (WeaponStat.DamageMax, 1.08),
            (WeaponStat.Penetration, 1.08), (WeaponStat.FlightSpeed, 1.12),
        }),
        ["heavy_bowstring"] = new(null, new[]
        {
            (WeaponStat.AttackInterval, 1.06), (WeaponStat.DamageMin, 1.04), (WeaponStat.DamageMax, 1.04),
            (WeaponStat.BaseSpreadDegrees, 0.92), (WeaponStat.FlightSpeed, 1.12), (WeaponStat.FalloffFloor, 1.18),
        }),
        ["crossbow_shield"] = new(1.50, Array.Empty<(WeaponStat, double)>(), Frontal: 0.25, HalfAngle: 60.0),
    };

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        double w = WeaponModCatalog.LightenedStock().WeightMultiplier;   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取改装数值后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        BitEqual(0.85, w);
    }

    // ── 字面值锚定（A/B）：20 条改装的每个外置数字 × 位级 == 迁移前原始字面量 ──────────────
    [Fact]
    public void Every_mod_value_matches_original_literal()
    {
        var all = WeaponModCatalog.All();
        Assert.Equal(GoldenById.Count, all.Count);   // 恰 20 条，无多无缺
        foreach (var mod in all)
        {
            Assert.True(GoldenById.TryGetValue(mod.Id, out var g), $"golden 缺改装「{mod.Id}」");
            if (g!.Weight is double w) BitEqual(w, mod.WeightMultiplier);
            foreach (var (stat, val) in g.Stats) BitEqual(val, StatValue(mod.Stats, stat));
            if (g.HandGuard is double hg) BitEqual(hg, mod.HandGuardNegateChance);
            if (g.Frontal is double fr) BitEqual(fr, mod.FrontalRangedNegateChance);
            if (g.HalfAngle is double ha) BitEqual(ha, mod.FrontalNegateHalfAngleDeg);
        }
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────────
    [Fact]
    public void Catalog_reads_from_config_section()
    {
        var sec = GameConfigCatalog.Section<WeaponModConfig>();
        BitEqual(sec.Get("crossbow_shield").WeightMultiplier, WeaponModCatalog.CrossbowShield().WeightMultiplier);
        BitEqual(sec.Get("crossbow_shield").FrontalRangedNegateChance, WeaponModCatalog.CrossbowShield().FrontalRangedNegateChance);
        BitEqual(sec.Get("handguard").HandGuardNegateChance, WeaponModCatalog.Handguard().HandGuardNegateChance);
        BitEqual(sec.Stat("lightened_stock", "BaseSpreadDegrees"),
            StatValue(WeaponModCatalog.LightenedStock().Stats, WeaponStat.BaseSpreadDegrees));
        BitEqual(sec.Stat("nail_studs", "Penetration"),
            StatValue(WeaponModCatalog.NailStuds().Stats, WeaponStat.Penetration));
    }

    // ── MeleeFormSpeed 功能锚定：型态枪托出手间隔 == 基准武器攻速 ÷ 0.85（吃的是外置的折扣）───
    [Fact]
    public void MeleeFormSpeed_externalized_drives_stock_interval()
    {
        double interval = StatValue(WeaponModCatalog.Bayonet().Stats, WeaponStat.StockMeleeInterval);
        BitEqual(WeaponTable.Rapier().AttackInterval / GoldenMeleeFormSpeed, interval);
        BitEqual(GoldenMeleeFormSpeed, GameConfigCatalog.Section<WeaponModConfig>().MeleeFormSpeed);
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）─────────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var sec = GameConfigCatalog.Section<WeaponModConfig>();
        string json = JsonSerializer.Serialize(sec, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<WeaponModConfig>(json, GameConfigLoader.Options)!;
        BitEqual(sec.MeleeFormSpeed, back.MeleeFormSpeed);
        Assert.Equal(sec.ById.Count, back.ById.Count);
        foreach (var (id, t) in sec.ById)
        {
            var b = back.Get(id);
            BitEqual(t.WeightMultiplier, b.WeightMultiplier);
            BitEqual(t.HandGuardNegateChance, b.HandGuardNegateChance);
            BitEqual(t.FrontalRangedNegateChance, b.FrontalRangedNegateChance);
            BitEqual(t.FrontalNegateHalfAngleDeg, b.FrontalNegateHalfAngleDeg);
            Assert.Equal(t.Stats.Count, b.Stats.Count);
            foreach (var (k, v) in t.Stats) BitEqual(v, b.Stats[k]);
        }
    }

    // ── 反射加载盘上文件：weaponmods.json 经 FromJson == golden ─────────────────────────
    [Fact]
    public void On_disk_weaponmods_json_parses_to_golden()
    {
        string path = Path.Combine(GameConfigFiles.LocateConfigDir(), "weaponmods.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var loaded = (WeaponModConfig)new WeaponModConfig().FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        BitEqual(GoldenMeleeFormSpeed, loaded.MeleeFormSpeed);
        Assert.Equal(GoldenById.Count, loaded.ById.Count);
        foreach (var (id, g) in GoldenById)
        {
            var t = loaded.Get(id);
            if (g.Weight is double w) BitEqual(w, t.WeightMultiplier);
            foreach (var (stat, val) in g.Stats) BitEqual(val, loaded.Stat(id, stat.ToString()));
            if (g.HandGuard is double hg) BitEqual(hg, t.HandGuardNegateChance);
            if (g.Frontal is double fr) BitEqual(fr, t.FrontalRangedNegateChance);
            if (g.HalfAngle is double ha) BitEqual(ha, t.FrontalNegateHalfAngleDeg);
        }
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 WeaponMods 段 ──────────────────────
    [Fact]
    public void Loader_reflection_discovers_weaponmods_section()
    {
        var cfg = GameConfigLoader.Parse(f => File.ReadAllText(Path.Combine(GameConfigFiles.LocateConfigDir(), f)));
        BitEqual(GoldenMeleeFormSpeed, cfg.WeaponMods.MeleeFormSpeed);
        Assert.Equal(GoldenById.Count, cfg.WeaponMods.ById.Count);
    }

    // ── 缺 id / 缺数值 fail-fast（不软回落）────────────────────────────────────────────
    [Fact]
    public void Get_missing_id_fails_fast()
        => Assert.Throws<InvalidOperationException>(
            () => GameConfigCatalog.Section<WeaponModConfig>().Get("does_not_exist"));

    [Fact]
    public void Stat_missing_key_fails_fast()
        => Assert.Throws<InvalidOperationException>(
            () => GameConfigCatalog.Section<WeaponModConfig>().Stat("lightened_stock", "DoesNotExist"));

    // ── helpers ────────────────────────────────────────────────────────────────────
    /// <summary>取某 StatMod 列表里作用于 <paramref name="stat"/> 的那条的数值（唯一命中）。</summary>
    private static double StatValue(IReadOnlyList<StatMod> stats, WeaponStat stat)
    {
        foreach (var s in stats)
        {
            if (s.Stat == stat) return s.Value;
        }
        throw new Xunit.Sdk.XunitException($"StatMod 列表里无作用于 {stat} 的项");
    }

    /// <summary>double 位级相等（比 == 更严）——证明"一位不差"。</summary>
    private static void BitEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}
