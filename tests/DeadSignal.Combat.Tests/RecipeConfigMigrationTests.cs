using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【配方数值外置 · godot 侧配置范式的零漂移 A/B 焊死】config-recipes 单。
/// <para>
/// <see cref="RecipeBook"/> 每条配方的<b>可调数字</b>（产量 <c>OutputQuantity</c> / 工时 <c>WorkMinutes</c> /
/// 材料成本 <c>MaterialCosts</c>）已从 C# 字面量搬到 <c>godot/data/config/recipes.json</c>；配方经私有工厂
/// <c>RecipeBook.R</c> 读 <c>GameConfigCatalog.Section&lt;RecipeConfig&gt;().Get(id)</c> 取数字，
/// <b>结构（id/显示名/类别/产物键/工具/书/制作者门槛）仍写死代码</b>。本文件钉死「搬家没搬错一个数」：
/// </para>
/// <list type="number">
///   <item><b>接线活着 + 懒加载</b>：首次取配方触发 Bootstrapper 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：代表配方的产量/工时/材料逐项 == 迁移前原始字面量。</item>
///   <item><b>取用点确实读 catalog</b>：全 56 条配方的三项数字 == section.Get(id)（证明委托到配置、非残留字面量）。</item>
///   <item><b>完整性</b>：recipes.json 恰覆盖 RecipeBook.All 全部 id、无多无少。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，逐配方逐字段相等（含材料字典）⇒ 加载器不丢数据。</item>
///   <item><b>盘上文件反射加载</b>：GameConfigFiles 定位的 recipes.json 经 FromJson / Loader.Parse 解析 == golden。</item>
///   <item><b>缺文件 / 坏 json / 缺 id fail-fast</b>：不软回落。</item>
/// </list>
/// <para>
/// ⚠️ 配方<b>不进 Sim 战斗结算</b>（制作非对决）⇒ 不跑 Sim MD5；位级往返 + 字面锚定 + 全量取用点比对即零漂移铁证。
/// </para>
/// </summary>
public sealed class RecipeConfigMigrationTests
{
    // ── 迁移前 RecipeBook 里的原始字面量（golden）——A/B 的"旧硬编码"一侧。改 recipes.json 会让本表变红。──
    // 代表集覆盖：单料 / 多料 / 产量>1 / 常量 id / 羽毛键 / 武器零件键 / 门槛配方。
    private static readonly (string Id, int Out, int Min, (string, int)[] Mats)[] Golden =
    {
        ("bone_knife", 1, 45, new[] { ("bone", 2), ("cloth", 1) }),
        ("cloth_vest", 1, 90, new[] { ("cloth", 4) }),
        ("horror_armor", 1, 240, new[] { ("bone", 6), ("leather", 3), ("rope", 2) }),
        ("glue", 2, 60, new[] { ("bone", 4), ("fuel", 1) }),
        ("wood_from_scrap", 4, 40, new[] { ("scrap_wood", 4), ("glue", 1) }),
        ("ammo_short", 8, 45, new[] { ("bullet_parts", 1), ("gunpowder", 1) }),
        ("ammo_arrow_stick", 4, 20, new[] { ("wood", 1), ("feather", 1) }),
        ("heavy_crossbow", 1, 320, new[] { ("wood", 4), ("iron", 4), ("rope", 2), ("weapon_parts", 3) }),
        // 常量 id 配方（ButcherStation.TableRecipeId="butcher_table"）——[DECISION] 木板→木料落地值。
        ("butcher_table", 1, 60, new[] { ("wood", 3), ("nails", 4) }),
    };

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        var r = RecipeBook.Find("bone_knife");   // 首次访问触发懒加载
        Assert.NotNull(r);
        Assert.True(GameConfigCatalog.IsInitialized, "首次取配方后 catalog 应已初始化（Bootstrapper 懒加载生效）");
    }

    // ── 字面值锚定（A/B）：代表配方 × 产量/工时/材料 == 迁移前原始字面量 ──────────────────
    [Fact]
    public void Anchored_recipes_match_original_literals()
    {
        var sec = GameConfigCatalog.Section<RecipeConfig>();
        foreach (var (id, o, m, mats) in Golden)
        {
            var n = sec.Get(id);
            Assert.Equal(o, n.OutputQuantity);
            Assert.Equal(m, n.WorkMinutes);
            AssertMats(id, mats, n.MaterialCosts);
        }
    }

    // ── 取用点确实读 catalog：全 56 条配方三项数字 == section（证明委托到配置、不是残留字面量）──
    [Fact]
    public void Every_recipe_reads_numbers_from_catalog()
    {
        var sec = GameConfigCatalog.Section<RecipeConfig>();
        foreach (var r in RecipeBook.All)
        {
            var n = sec.Get(r.Id);
            Assert.Equal(n.OutputQuantity, r.OutputQuantity);
            Assert.Equal(n.WorkMinutes, r.WorkMinutes);
            AssertMatsEqual(r.Id, n.MaterialCosts, r.MaterialCosts);
        }
    }

    // ── 完整性：recipes.json 恰覆盖 RecipeBook.All 全部 id、无多无少 ─────────────────────
    [Fact]
    public void Config_covers_all_recipes_exactly()
    {
        var sec = GameConfigCatalog.Section<RecipeConfig>();
        var recipeIds = RecipeBook.All.Select(r => r.Id).ToHashSet();
        var configIds = sec.ById.Keys.ToHashSet();
        Assert.Equal(recipeIds.Count, RecipeBook.All.Count);   // RecipeBook 内无重复 id
        Assert.True(recipeIds.SetEquals(configIds),
            "recipes.json 与 RecipeBook.All 的 id 集应完全一致；多/缺：" +
            string.Join(",", recipeIds.Except(configIds).Concat(configIds.Except(recipeIds))));
    }

    // ── 往返保真：段序列化→反序列化，逐配方逐字段相等（含材料字典）──────────────────────
    [Fact]
    public void Section_survives_json_round_trip()
    {
        var golden = GameConfigCatalog.Section<RecipeConfig>();
        string json = JsonSerializer.Serialize(
            golden.ById.ToDictionary(kv => kv.Key, kv => kv.Value), GameConfigLoader.Options);
        var back = (RecipeConfig)new RecipeConfig().FromJson(json, GameConfigLoader.Options);
        Assert.Equal(golden.ById.Count, back.ById.Count);
        foreach (var kv in golden.ById)
        {
            var b = back.Get(kv.Key);
            Assert.Equal(kv.Value.OutputQuantity, b.OutputQuantity);
            Assert.Equal(kv.Value.WorkMinutes, b.WorkMinutes);
            AssertMatsEqual(kv.Key, kv.Value.MaterialCosts, b.MaterialCosts);
        }
    }

    // ── 盘上文件反射加载：recipes.json 经 FromJson / Loader.Parse == golden 锚点 ──────────
    [Fact]
    public void On_disk_recipes_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "recipes.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var loaded = (RecipeConfig)new RecipeConfig().FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        foreach (var (id, o, m, mats) in Golden)
        {
            var n = loaded.Get(id);
            Assert.Equal(o, n.OutputQuantity);
            Assert.Equal(m, n.WorkMinutes);
            AssertMats(id, mats, n.MaterialCosts);
        }
    }

    [Fact]
    public void Loader_reflection_discovers_recipes_section()
    {
        var cfg = GameConfigLoader.Parse(file => File.ReadAllText(Path.Combine(GameConfigFiles.LocateConfigDir(), file)));
        Assert.Equal(45, cfg.Recipes.Get("bone_knife").WorkMinutes);
    }

    // ── 缺文件 / 坏 json / 缺 id fail-fast（不软回落）──────────────────────────────────
    [Fact]
    public void Loader_missing_file_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => null!));

    [Fact]
    public void Loader_bad_json_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => "{ not valid json "));

    [Fact]
    public void Get_missing_id_fails_fast()
        => Assert.Throws<KeyNotFoundException>(() => GameConfigCatalog.Section<RecipeConfig>().Get("no_such_recipe"));

    // ── helpers ──────────────────────────────────────────────────────────────
    private static void AssertMats(string id, (string, int)[] expected, IReadOnlyDictionary<string, int> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        foreach (var (k, v) in expected)
        {
            Assert.True(actual.TryGetValue(k, out int a), $"{id}: recipes.json 缺材料键 {k}");
            Assert.Equal(v, a);
        }
    }

    private static void AssertMatsEqual(string id, IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
    {
        Assert.Equal(a.Count, b.Count);
        foreach (var kv in a)
        {
            Assert.True(b.TryGetValue(kv.Key, out int bv), $"{id}: 材料键 {kv.Key} 不一致");
            Assert.Equal(kv.Value, bv);
        }
    }
}
