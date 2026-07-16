using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 诱捕命中率的零漂移 A/B 焊死】config-niche 单（照 config-hunger / config-consumer-pilot 范式）。
/// <para>
/// <see cref="TrapLogic"/>（圈套）与 <see cref="BirdTrapLogic"/>（捕鸟陷阱）的<b>命中率常量</b>——基础命中率 / 每多一个的递减 /
/// 命中率地板，以及圈套的兔鼠分配比例——已从 C# <c>const</c> 搬到 <c>godot/data/config/farming.json</c>，取用点身体改成读
/// <c>GameConfigCatalog.Section&lt;FarmingConfig&gt;()</c>。本文件钉死「搬家没搬错一个数」并给 farming 段上护栏（double 位级）。
/// </para>
/// <para>
/// ⚠️ <b>种植（菜园）那批常量不外置</b>：<c>MaxPlants</c>(16) / <c>GrowGameHours</c>(84) / <c>SeedCost</c>(1) /
/// <c>PlantActionGameHours</c>(0.15) 是编译期 const（<c>PlantWorkMinutes</c> 派生自它），改静态属性会破编译期约束，保留字面。
/// </para>
/// <para>
/// 诱捕不进主 Duel 蒙特卡洛（那只算武器×护甲，Sim 不 Link TrapLogic/Farming），故无需 Sim MD5；
/// double 位级往返 + 字面锚定 + 递减语义锚定即零漂移铁证。
/// </para>
/// </summary>
public sealed class FarmingConfigMigrationTests
{
    // 迁移前 TrapLogic/BirdTrapLogic 里的原始命中率字面量（golden）——A/B 的"旧硬编码"一侧。改 farming.json 里这些值会让本表变红。
    private const double GoldenSnareBase = 0.30;
    private const double GoldenSnareStep = 0.05;
    private const double GoldenSnareMin = 0.05;
    private const double GoldenSnareRabbit = 0.30;
    private const double GoldenBirdBase = 0.20;
    private const double GoldenBirdStep = 0.05;
    private const double GoldenBirdMin = 0.05;

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        double b = TrapLogic.BaseChance; // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取诱捕命中率后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        BitEqual(GoldenSnareBase, b);
        Assert.Equal("farming.json", GameConfigCatalog.Section<FarmingConfig>().FileName);
    }

    // ── 字面值锚定（A/B）：7 个命中率常量 × 位级 == 迁移前原始字面量 ────────────────────
    [Fact]
    public void Trap_constants_match_original_literals()
    {
        BitEqual(GoldenSnareBase, TrapLogic.BaseChance);
        BitEqual(GoldenSnareStep, TrapLogic.ChanceStep);
        BitEqual(GoldenSnareMin, TrapLogic.MinChance);
        BitEqual(GoldenSnareRabbit, TrapLogic.RabbitShare);
        BitEqual(GoldenBirdBase, BirdTrapLogic.BaseChance);
        BitEqual(GoldenBirdStep, BirdTrapLogic.ChanceStep);
        BitEqual(GoldenBirdMin, BirdTrapLogic.MinChance);
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void Trap_logic_reads_from_catalog_section()
    {
        var s = GameConfigCatalog.Section<FarmingConfig>();
        BitEqual(s.SnareBaseChance, TrapLogic.BaseChance);
        BitEqual(s.SnareChanceStep, TrapLogic.ChanceStep);
        BitEqual(s.SnareMinChance, TrapLogic.MinChance);
        BitEqual(s.SnareRabbitShare, TrapLogic.RabbitShare);
        BitEqual(s.BirdTrapBaseChance, BirdTrapLogic.BaseChance);
        BitEqual(s.BirdTrapChanceStep, BirdTrapLogic.ChanceStep);
        BitEqual(s.BirdTrapMinChance, BirdTrapLogic.MinChance);
    }

    // ── 递减语义锚定：证明 base/step/min 真被 ChanceOf 读到（30/25/…/地板 5%）────────────
    [Fact]
    public void Snare_chance_decays_from_externalized_constants()
    {
        BitEqual(GoldenSnareBase, TrapLogic.ChanceOf(1));                       // 第 1 个 = base 30%
        BitEqual(GoldenSnareBase - GoldenSnareStep, TrapLogic.ChanceOf(2));     // 第 2 个 = 25%
        BitEqual(GoldenSnareMin, TrapLogic.ChanceOf(100));                      // 撞地板 5%
    }

    [Fact]
    public void Bird_trap_chance_decays_from_externalized_constants()
    {
        BitEqual(GoldenBirdBase, BirdTrapLogic.ChanceOf(1));                    // 第 1 个 = base 20%
        BitEqual(GoldenBirdBase - GoldenBirdStep, BirdTrapLogic.ChanceOf(2));   // 第 2 个 = 15%
        BitEqual(GoldenBirdMin, BirdTrapLogic.ChanceOf(100));                   // 撞地板 5%
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏，double 位级）────────────────────────
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var golden = new FarmingConfig
        {
            SnareBaseChance = GoldenSnareBase,
            SnareChanceStep = GoldenSnareStep,
            SnareMinChance = GoldenSnareMin,
            SnareRabbitShare = GoldenSnareRabbit,
            BirdTrapBaseChance = GoldenBirdBase,
            BirdTrapChanceStep = GoldenBirdStep,
            BirdTrapMinChance = GoldenBirdMin,
        };
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<FarmingConfig>(json, GameConfigLoader.Options)!;
        foreach (var p in typeof(FarmingConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetValue(golden) is double da && p.GetValue(back) is double db)
            {
                Assert.True(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db),
                    $"{p.Name} double 漂移：{da} vs {db}");
            }
        }
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 farming.json 经 FromJson == golden ────────
    [Fact]
    public void On_disk_farming_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "farming.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new FarmingConfig();
        var loaded = (FarmingConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        BitEqual(GoldenSnareBase, loaded.SnareBaseChance);
        BitEqual(GoldenSnareStep, loaded.SnareChanceStep);
        BitEqual(GoldenSnareMin, loaded.SnareMinChance);
        BitEqual(GoldenSnareRabbit, loaded.SnareRabbitShare);
        BitEqual(GoldenBirdBase, loaded.BirdTrapBaseChance);
        BitEqual(GoldenBirdStep, loaded.BirdTrapChanceStep);
        BitEqual(GoldenBirdMin, loaded.BirdTrapMinChance);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Farming 段 ────────────────────
    [Fact]
    public void Loader_reflection_discovers_farming_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        BitEqual(GoldenSnareBase, cfg.Farming.SnareBaseChance);
        BitEqual(GoldenBirdBase, cfg.Farming.BirdTrapBaseChance);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    /// <summary>double 位级相等（比 == 更严）——证明"往返一位不差"。</summary>
    private static void BitEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
