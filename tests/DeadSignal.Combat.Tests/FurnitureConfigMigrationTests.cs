using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 家具建造成本/工时 furniture.json 的零漂移 A/B 焊死】config-furniture 单。
/// <para>
/// <see cref="FurnitureBuildCost"/> 的各家具「建造材料数量 + 建造工时」已从 C# 字面量搬到
/// <c>godot/data/config/furniture.json</c>（读 <see cref="FurnitureConfig"/> 段，消费层平行容器）。
/// <b>简介文案（Description）是 authored 内容、未外置</b>——留在代码、已接线 wiki「简介」列。
/// 本文件钉死「搬家没搬错一个数」，并给整套读取链上护栏：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 [ModuleInitializer]（TestGameConfigBootstrap）注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：14 件家具逐件断言 Cost 字典 + BuildMinutes == 迁移前原始字面量。</item>
///   <item><b>取用点确实读 catalog</b>：<see cref="FurnitureBuildCost.Of"/>/<see cref="FurnitureBuildCost.BuildMinutes"/> == catalog 段值。</item>
///   <item><b>键完整性 + 顺序</b>：代码目录 All 与 config.ById 键集/顺序一致（多一个少一个都红）。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，逐件 Cost/BuildMinutes 相等 ⇒ 加载器不丢数据（永久护栏）。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 furniture.json 经 FromJson 解析 == golden。</item>
///   <item><b>缺键 fail-fast</b>：<see cref="FurnitureConfig.Get"/> 遇未登记键抛（不静默）。</item>
///   <item><b>Description 未外置</b>：简介仍非空且不进 config 段（authored 结构保留的结构性证据）。</item>
/// </list>
/// <para>
/// ⚠️ 家具<b>不进 Sim</b>（Duel/Ballistics/Arena 不读家具），故零漂移靠位级往返 + 字面锚定即足，不跑 Sim MD5。
/// </para>
/// </summary>
public sealed class FurnitureConfigMigrationTests
{
    // 迁移前 FurnitureBuildCost 里的原始字面量（golden）——A/B 的"旧硬编码"一侧。改 furniture.json 里这些值会让本表变红。
    // 键 = camp.json prop 名（含各 Spec.FurnitureKey 的字面值），逐件锚定成本字典 + 工时。
    private static readonly IReadOnlyDictionary<string, (Dictionary<string, int> Cost, int Minutes)> Golden =
        new Dictionary<string, (Dictionary<string, int>, int)>
        {
            ["工作台"] = (new() { ["wood"] = 16, ["nails"] = 8 }, 180),
            ["改装台"] = (new() { ["wood"] = 8, ["iron"] = 4, ["components"] = 2, ["nails"] = 6 }, 200),
            ["烹饪台"] = (new() { ["stone"] = 8, ["wood"] = 6, ["iron"] = 3, ["nails"] = 4 }, 180),
            ["住宅-柜子"] = (new() { ["wood"] = 10, ["nails"] = 6 }, 120),
            ["住宅-衣柜"] = (new() { ["wood"] = 12, ["nails"] = 6 }, 140),
            ["住宅-展示柜"] = (new() { ["wood"] = 8, ["nails"] = 4 }, 100),
            ["床"] = (new() { ["wood"] = 12, ["cloth"] = 4, ["nails"] = 6 }, 150),
            ["桌子"] = (new() { ["wood"] = 8, ["nails"] = 4 }, 120),
            ["沙发"] = (new() { ["wood"] = 8, ["cloth"] = 6, ["nails"] = 4 }, 240),
            ["沙袋"] = (new() { ["cloth"] = 2, ["stone"] = 4 }, 30),
            ["陷阱"] = (new() { ["wood"] = 2, ["wire"] = 2, ["rope"] = 1 }, 40),
            ["捕鸟陷阱"] = (new() { ["wood"] = 2, ["rope"] = 2 }, 40),
            ["菜园"] = (new() { ["wood"] = 2 }, 60),
            ["简易宰杀点"] = (new() { ["wood"] = 1 }, 30),
            ["宰杀台"] = (new() { ["wood"] = 3, ["nails"] = 4 }, 60),
        };

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        var cost = FurnitureBuildCost.Of("工作台");   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取家具成本后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.NotNull(cost);
    }

    // ── 字面值锚定（A/B）：15 件家具逐件 Cost + BuildMinutes == 迁移前字面量 ──────────────
    [Fact]
    public void Costs_and_minutes_match_original_literals()
    {
        foreach (var (key, (cost, minutes)) in Golden)
        {
            Assert.Equal(cost, FurnitureBuildCost.Of(key));
            Assert.Equal(minutes, FurnitureBuildCost.BuildMinutes(key));
        }
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留字面量）────────────────────────
    [Fact]
    public void FurnitureBuildCost_reads_from_catalog_section()
    {
        var section = GameConfigCatalog.Section<FurnitureConfig>();
        foreach (string key in FurnitureBuildCost.All)
        {
            FurnitureCost c = section.Get(key);
            Assert.Equal(c.Cost, FurnitureBuildCost.Of(key));
            Assert.Equal(c.BuildMinutes, FurnitureBuildCost.BuildMinutes(key));
        }
    }

    // ── 键完整性 + 顺序：代码目录 All 与 config.ById 键集/顺序一致 ──────────────────────
    [Fact]
    public void All_keys_match_config_keys_in_order()
    {
        var section = GameConfigCatalog.Section<FurnitureConfig>();
        Assert.Equal(FurnitureBuildCost.All.ToList(), section.ById.Keys.ToList());
        // 与 golden 键集一致（多一件少一件都红）——集合无序比对。
        Assert.Equal(Golden.Keys.OrderBy(k => k), FurnitureBuildCost.All.OrderBy(k => k));
    }

    // ── 往返保真：加载器不丢数据（值无关，永久护栏）────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip()
    {
        var section = GameConfigCatalog.Section<FurnitureConfig>();
        string json = JsonSerializer.Serialize(section, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<FurnitureConfig>(json, GameConfigLoader.Options)!;
        Assert.Equal(section.ById.Keys.ToList(), back.ById.Keys.ToList());
        foreach (string key in section.ById.Keys)
        {
            Assert.Equal(section.Get(key).Cost, back.Get(key).Cost);
            Assert.Equal(section.Get(key).BuildMinutes, back.Get(key).BuildMinutes);
        }
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 furniture.json 经 FromJson == golden ──────
    [Fact]
    public void On_disk_furniture_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "furniture.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new FurnitureConfig();
        var loaded = (FurnitureConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        foreach (var (key, (cost, minutes)) in Golden)
        {
            Assert.Equal(cost, loaded.Get(key).Cost);
            Assert.Equal(minutes, loaded.Get(key).BuildMinutes);
        }
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Furniture 段 ────────────────────
    [Fact]
    public void Loader_reflection_discovers_furniture_section()
    {
        var cfg = GameConfigLoader.Parse(file => File.ReadAllText(Path.Combine(GameConfigFiles.LocateConfigDir(), file)));
        Assert.Equal(16, cfg.Furniture.Get("工作台").Cost["wood"]);
        Assert.Equal(180, cfg.Furniture.Get("工作台").BuildMinutes);
    }

    // ── 缺键 fail-fast（登记外的键不静默返回）─────────────────────────────────────
    [Fact]
    public void Get_missing_key_fails_fast()
        => Assert.Throws<KeyNotFoundException>(() => GameConfigCatalog.Section<FurnitureConfig>().Get("不存在的家具"));

    // ── 未登记的东西拆不动：Of/BuildMinutes 对目录外键返回 null（零回归）────────────────
    [Fact]
    public void Unlisted_furniture_returns_null()
    {
        Assert.Null(FurnitureBuildCost.Of("收音机"));
        Assert.Null(FurnitureBuildCost.BuildMinutes("废墟"));
        Assert.Null(FurnitureBuildCost.Of(null!));
    }

    // ── Description 未外置：简介仍非空且不进 config 段（authored 结构保留的结构性证据）────
    [Fact]
    public void Descriptions_stay_authored_not_in_config()
    {
        // 简介仍从代码取、非空。
        foreach (string key in FurnitureBuildCost.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(FurnitureBuildCost.Description(key)), $"{key} 简介不该空");
        }
        // config 段的 FurnitureCost 只有 Cost/BuildMinutes 两个字段，压根没有 Description ⇒ 简介不可能被外置进去。
        Assert.DoesNotContain(
            typeof(FurnitureCost).GetProperties(),
            p => p.Name.Contains("Description", StringComparison.OrdinalIgnoreCase));
    }
}
