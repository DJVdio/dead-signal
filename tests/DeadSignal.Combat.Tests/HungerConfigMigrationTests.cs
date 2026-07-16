using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 饥饿能力惩罚三档梯度的零漂移 A/B 焊死】config-hunger 单（照 config-consumer-pilot 范式）。
/// <para>
/// <see cref="HungerState.PenaltyFor"/> 的三档梯度惩罚（营养不良 0.45 / 极度饥饿 0.25 / 饥饿 0.10）已从 C# switch
/// 字面量搬到 <c>godot/data/config/hunger.json</c>，取用点身体改成读 <c>GameConfigCatalog.Section&lt;HungerConfig&gt;()</c>。
/// 本文件钉死「搬家没搬错一个数」并给 hunger 段上护栏（double 位级）。
/// </para>
/// <para>
/// ⚠️ <b>饿死档 1.0（满值兜底）/ 饱档 0.0（无惩罚）是 switch 两端语义边界，保留字面不外置</b>；
/// <c>DefaultCap</c>(5)/<c>MaxCap</c>(6)/<c>StarvedValue</c>(0) 是编译期 const（被当默认参数值/枚举边界），亦不外置。
/// 本文件同时锚定这两端边界未被搬动。
/// </para>
/// <para>
/// 饥饿不进主 Duel 蒙特卡洛（那只算武器×护甲），故无需 Sim MD5；double 位级往返 + 字面锚定即零漂移铁证。
/// </para>
/// </summary>
public sealed class HungerConfigMigrationTests
{
    // 迁移前 HungerState.PenaltyFor 里的原始梯度字面量（golden）——A/B 的"旧硬编码"一侧。改 hunger.json 里这些值会让本表变红。
    private const double GoldenMalnourished = 0.45;
    private const double GoldenRavenous = 0.25;
    private const double GoldenHungry = 0.10;

    // switch 两端语义边界（未外置，保留字面）——一并锚定它们没被误搬/误改。
    private const double GoldenStarvedBound = 1.0; // 饿死：满值兜底
    private const double GoldenSatedBound = 0.0;   // 饱档（有点饿/正常/吃撑）：无惩罚

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        double p = HungerState.PenaltyFor((int)HungerLevel.Malnourished); // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取饥饿惩罚后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        BitEqual(GoldenMalnourished, p);
    }

    // ── 字面值锚定（A/B）：三档梯度 × 位级 == 迁移前原始字面量 ─────────────────────────
    [Fact]
    public void Penalties_match_original_literals()
    {
        BitEqual(GoldenMalnourished, HungerState.PenaltyFor((int)HungerLevel.Malnourished));
        BitEqual(GoldenRavenous, HungerState.PenaltyFor((int)HungerLevel.Ravenous));
        BitEqual(GoldenHungry, HungerState.PenaltyFor((int)HungerLevel.Hungry));
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void PenaltyFor_reads_from_catalog_section()
    {
        var s = GameConfigCatalog.Section<HungerConfig>();
        BitEqual(s.MalnourishedPenalty, HungerState.PenaltyFor((int)HungerLevel.Malnourished));
        BitEqual(s.RavenousPenalty, HungerState.PenaltyFor((int)HungerLevel.Ravenous));
        BitEqual(s.HungryPenalty, HungerState.PenaltyFor((int)HungerLevel.Hungry));
    }

    // ── 两端语义边界未被搬动（饿死满值兜底 / 饱档无惩罚，仍是硬字面）──────────────────
    [Fact]
    public void Semantic_bounds_stay_literal()
    {
        BitEqual(GoldenStarvedBound, HungerState.PenaltyFor((int)HungerLevel.Starved));
        BitEqual(GoldenSatedBound, HungerState.PenaltyFor((int)HungerLevel.Peckish));
        BitEqual(GoldenSatedBound, HungerState.PenaltyFor((int)HungerLevel.Sated));
        BitEqual(GoldenSatedBound, HungerState.PenaltyFor((int)HungerLevel.Stuffed));
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏，double 位级）────────────────────────
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var golden = new HungerConfig
        {
            MalnourishedPenalty = GoldenMalnourished,
            RavenousPenalty = GoldenRavenous,
            HungryPenalty = GoldenHungry,
        };
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<HungerConfig>(json, GameConfigLoader.Options)!;
        AssertSectionBitEqual(golden, back);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 hunger.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_hunger_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "hunger.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new HungerConfig();
        var loaded = (HungerConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        BitEqual(GoldenMalnourished, loaded.MalnourishedPenalty);
        BitEqual(GoldenRavenous, loaded.RavenousPenalty);
        BitEqual(GoldenHungry, loaded.HungryPenalty);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Hunger 段 ────────────────────
    [Fact]
    public void Loader_reflection_discovers_hunger_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        BitEqual(GoldenHungry, cfg.Hunger.HungryPenalty);
    }

    // ── 功能锚定：CombineCapability 吃的是外置后的饥饿惩罚 ─────────────────────────
    [Fact]
    public void CombineCapability_uses_externalized_penalty()
    {
        // 无残疾 + 饥饿(3)档：有效能力 = (1-0) × (1-0.10) = 0.90。
        double eff = HungerState.CombineCapability(0.0, HungerState.PenaltyFor((int)HungerLevel.Hungry));
        BitEqual(1.0 - GoldenHungry, eff);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    /// <summary>double 位级相等（比 == 更严）——证明"往返一位不差"。</summary>
    private static void BitEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    /// <summary>反射比对两个段实例的全部 double 属性（位级）。</summary>
    private static void AssertSectionBitEqual(HungerConfig a, HungerConfig b)
    {
        foreach (var p in typeof(HungerConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetValue(a) is double da && p.GetValue(b) is double db)
            {
                Assert.True(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db),
                    $"{p.Name} double 漂移：{da} vs {db}");
            }
        }
    }

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
