using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 材料重量的零漂移 A/B 焊死】config-materials 单。
/// <para>
/// <c>ItemRegistry.Materials</c>（原 47 条、现 53 条 <c>Dictionary&lt;string,double&gt;</c> 字面表）的<b>重量数值</b>已搬到
/// <c>godot/data/config/materials.json</c>；<c>ItemRegistry.Materials</c> 现在启动时从
/// <c>GameConfigCatalog.Section&lt;MaterialConfig&gt;().Weights</c> <b>拷贝</b>成一份独立 <c>Dictionary</c>
/// （字段类型/引用语义保持不变——<c>CarryWeight._materialKg</c> 仍以别名引同一实例，见 <see cref="CarryCapacityTests"/>）。
/// 本文件钉死「搬家没搬错一个数」，并给字典型 config 段上护栏：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：首次访问 <c>ItemRegistry.Materials</c> 触发 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：53 条重量逐条位级断言 == 已确认配置值（double 用 <see cref="BitConverter.DoubleToInt64Bits"/>），且键集与条数完全一致。</item>
///   <item><b>取用点确实读 catalog</b>：<c>ItemRegistry.Materials</c> 每条 == catalog 段值（证明委托到配置、非残留字面）。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，逐条位级相等 ⇒ 加载器不丢精度（值无关，永久护栏）。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 materials.json 经 FromJson 解析 == golden。</item>
///   <item><b>反射驱动加载 + 缺文件/坏 json fail-fast</b>：GameConfigLoader.Parse 自动发现 Materials 段；遇缺文件/坏 json 抛（不软回落）。</item>
///   <item><b>功能锚定</b>：<c>ItemWeights.MaterialKg</c>（称重公式入口）吃的是 config 值；未登记回落 / ammo_ 兜底一字未动。</item>
/// </list>
/// <para>
/// ⚠️ 材料重量<b>不进 Sim 战斗结算</b>（<c>CombatResolver</c>/<c>Duel</c>/<c>Ballistics</c> 不引 <c>ItemRegistry</c>）——
/// 它只喂负重账（CarryWeight）与 wiki 抽取。故零漂移铁证＝<b>位级往返 + 字面锚定</b>，不涉 Sim MD5。
/// </para>
/// </summary>
public sealed class MaterialConfigMigrationTests
{
    // 迁移前 ItemRegistry.Materials 的原始字面（golden）——A/B 的"旧硬编码"一侧。改 materials.json 里这些值会让本表变红。
    private static readonly IReadOnlyDictionary<string, double> Golden = new Dictionary<string, double>
    {
        { "stone", 3.0 },
        { "fuel", 3.0 },
        { "wood", 1.0 },
        { "iron", 1.5 },
        { "scrap_wood", 1.0 },
        { "tanning_solution", 1.0 },
        { "rawhide", 1.0 },
        { "leather", 0.6 },
        { "rope", 0.15 },
        { "components", 0.5 },
        { "weapon_parts", 0.5 },
        { "first_aid_kit", 0.5 },
        { "glue", 0.5 },
        { "cloth", 0.3 },
        { "bone", 0.3 },
        { "wire", 0.25 },
        { "gunpowder", 0.3 },
        { "splint", 0.3 },
        { "nails", 0.05 },
        { "bandage", 0.1 },
        { "herbal_bandage", 0.1 },
        { "herbal_salve", 0.15 },
        { "needle_thread", 0.05 },
        { "antibiotics", 0.05 },
        { "medicine", 0.05 },
        { "dandelion", 0.05 },
        { "rosehip", 0.05 },
        { "laojunxu", 0.05 },
        { "bullet_parts", 0.05 },
        { "rabbit", 1.5 },
        { "rabbit_meat", 0.15 },
        { "ration", 1.0 },
        { "fish", 1.0 },
        { "flour", 1.0 },
        { "canned_food", 0.6 },
        { "rat", 0.3 },
        { "pigeon", 0.3 },
        { "potato", 0.3 },
        { "mushroom", 0.05 },
        { "kudzu_root", 0.2 },
        { "rhubarb", 0.2 },
        { "rat_meat", 0.15 },
        { "bird_meat", 0.15 },
        { "feather", 0.02 },
        { "leather_scrap", 0.2 },
        { "ammo_short", 0.01 },
        { "ammo_medium", 0.02 },
        { "ammo_buck", 0.05 },
        { "ammo_long", 0.03 },
        { "ammo_arrow_heavy", 0.05 },
        { "ammo_arrow_stick", 0.03 },
        { "ammo_arrow_handmade", 0.03 },
        { "ammo_arrow_carbon", 0.03 },
        { "dandelion_tea", 0.25 },
        { "rosehip_tea", 0.25 },
        { "silver", 0.01 },
        { "damaged_sniper_rifle", 7.5 },
    };

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        var mats = ItemRegistry.Materials;   // 首次访问触发懒加载（字段初始化即读 catalog）
        Assert.True(GameConfigCatalog.IsInitialized, "首次取材料重量后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.NotEmpty(mats);
    }

    // ── 字面值锚定（A/B）：53 条 × 位级 == 已确认配置值，键集/条数完全一致 ────────────────
    [Fact]
    public void Weights_match_original_literals()
    {
        var w = GameConfigCatalog.Section<MaterialConfig>().Weights;
        Assert.Equal(Golden.Count, w.Count);
        Assert.True(Golden.Keys.ToHashSet().SetEquals(w.Keys), "材料键集与迁移前不一致（漏搬/多搬/改名）");
        foreach (var (key, gold) in Golden)
        {
            Assert.True(w.TryGetValue(key, out double actual), $"materials.json 缺键 {key}");
            BitEqual(gold, actual);
        }
    }

    // ── 取用点确实读 catalog（证明 ItemRegistry.Materials 委托到配置、不是残留字面）──────────
    [Fact]
    public void ItemRegistry_Materials_reads_from_catalog_section()
    {
        var section = GameConfigCatalog.Section<MaterialConfig>();
        var reg = ItemRegistry.Materials;
        Assert.Equal(section.Weights.Count, reg.Count);
        foreach (var (key, val) in section.Weights)
        {
            Assert.True(reg.TryGetValue(key, out double actual), $"ItemRegistry.Materials 缺键 {key}");
            BitEqual(val, actual);
        }
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var golden = new MaterialConfig { Weights = new Dictionary<string, double>(Golden) };
        string json = JsonSerializer.Serialize(golden.Weights, GameConfigLoader.Options);
        var back = (MaterialConfig)golden.FromJson(json, GameConfigLoader.Options);
        Assert.Equal(golden.Weights.Count, back.Weights.Count);
        foreach (var (key, val) in golden.Weights)
        {
            Assert.True(back.Weights.TryGetValue(key, out double actual), $"往返后缺键 {key}");
            BitEqual(val, actual);
        }
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 materials.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_materials_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "materials.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new MaterialConfig();
        var loaded = (MaterialConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        Assert.Equal(Golden.Count, loaded.Weights.Count);
        foreach (var (key, gold) in Golden)
        {
            Assert.True(loaded.Weights.TryGetValue(key, out double actual), $"盘上 materials.json 缺键 {key}");
            BitEqual(gold, actual);
        }
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Materials 段 ─────────────────────
    [Fact]
    public void Loader_reflection_discovers_materials_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        BitEqual(Golden["wood"], cfg.Materials.Weights["wood"]);
        BitEqual(Golden["silver"], cfg.Materials.Weights["silver"]);
    }

    // ── 缺文件 / 坏 json fail-fast（不软回落）─────────────────────────────────────
    [Fact]
    public void Loader_missing_file_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => null!));

    [Fact]
    public void Loader_bad_json_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => "{ not valid json "));

    // ── 功能锚定：称重公式入口 ItemWeights.MaterialKg 吃的是 config 值；回落/兜底一字未动 ──────
    [Fact]
    public void MaterialKg_uses_externalized_weights_and_keeps_fallbacks()
    {
        BitEqual(Golden["wood"], ItemWeights.MaterialKg("wood"));       // 1.0：登记值取自 config
        BitEqual(Golden["iron"], ItemWeights.MaterialKg("iron"));       // 1.5：const 键解析后的登记值
        BitEqual(Golden["feather"], ItemWeights.MaterialKg("feather")); // 0.02：全表最轻
        BitEqual(ItemWeights.DefaultMaterialKg, ItemWeights.MaterialKg("__unregistered__")); // 未登记回落 0.5
        BitEqual(Golden["ammo_long"], ItemWeights.MaterialKg("ammo_long"));                 // 审计项：显式登记 0.03
        BitEqual(ItemWeights.AmmoPerRoundKg, ItemWeights.MaterialKg("ammo_unregistered"));   // 未登记 ammo_ 仍走兜底 0.03
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    /// <summary>double 位级相等（比 == 更严）——证明"往返一位不差"。</summary>
    private static void BitEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
