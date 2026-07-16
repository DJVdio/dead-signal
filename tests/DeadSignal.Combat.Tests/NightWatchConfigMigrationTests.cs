using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · godot 侧配置范式的零漂移 A/B 焊死】config-consumer-pilot 单（样板子系统＝夜防潜行系数）。
/// <para>
/// <see cref="NightWatchContest"/> 的 5 个潜行力权重已从 C# <c>const float</c> 搬到
/// <c>godot/data/config/nightwatch.json</c>，静态取用点身体改成
/// <c>=&gt; GameConfigCatalog.Section&lt;NightWatchConfig&gt;().X</c>（消费层平行容器，镜像纯库 CombatConfig 范式）。
/// 本文件钉死「搬家没搬错一个数」，并给整套 godot 侧容器（<see cref="GameConfig"/>/<see cref="GameConfigCatalog"/>/
/// <see cref="GameConfigLoader"/>/<see cref="GameConfigFiles"/>/<see cref="IGameConfigSection"/>）上护栏：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 <c>[ModuleInitializer]</c>（TestGameConfigBootstrap）注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：5 个权重逐条位级断言 == 迁移前原始常量（float 用 <see cref="BitConverter.SingleToInt32Bits"/>）。</item>
///   <item><b>取用点确实读 catalog</b>：<see cref="NightWatchContest"/> 的静态属性 == catalog 段值（证明委托到配置、非残留常量）。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，逐字段位级相等 ⇒ 加载器不丢精度（值无关，永久护栏）。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 nightwatch.json 经 FromJson 解析 == golden。</item>
///   <item><b>缺文件/坏 json fail-fast</b>：GameConfigLoader.Parse 遇缺文件/坏 json 抛（不软回落）。</item>
///   <item><b>功能锚定</b>：ComputeStealth 用外置后的权重算出的合力值 == 手算 golden（证明公式吃的是 config 值）。</item>
/// </list>
/// <para>
/// ⚠️ 夜防数值经 <c>WatchCalibration</c> 进 Sim（watchcal/watchsweep 模式），但主 Duel 蒙特卡洛 MD5 不涉夜防；
/// 权重位级不变 ⇒ WatchCalibration 输出结构性不变。本文件的位级往返 + 字面锚定即零漂移铁证。
/// </para>
/// <para>
/// 📐 <b>后续消费层 config 迁移单（hunger/recipes/furniture/…）照此文件立范式</b>：字面值锚定 + 取用点读 catalog +
/// 往返保真 + 盘上文件反射加载 + fail-fast；只搬数值、保留 authored 结构（如本单未搬 ApparelStealth 服饰表）。
/// </para>
/// </summary>
public sealed class NightWatchConfigMigrationTests
{
    // 迁移前 NightWatchContest 里的原始常量（golden）——A/B 的"旧硬编码"一侧。改 nightwatch.json 里这些值会让本表变红。
    private const float GoldenDarkness = 1.0f;
    private const float GoldenApparel = 1.0f;
    private const float GoldenDistanceWeight = 0.6f;
    private const float GoldenDistanceReference = 300f;
    private const float GoldenCover = 0.5f;
    // 第二批外置（警戒力权重 + 未发现后果）的原始常量（golden）。改 nightwatch.json 里这些值会让本表变红。
    private const float GoldenVisionWeight = 1.0f;
    private const float GoldenHearingWeight = 0.6f;
    private const float GoldenHearingBaseRange = 220f;
    private const float GoldenPreemptive = 1.5f;
    private const int GoldenSilentTheftMin = 1;
    private const int GoldenSilentTheftMax = 4;

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        float w = NightWatchContest.StealthDarknessWeight;   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取潜行权重后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        BitEqual(GoldenDarkness, w);
    }

    // ── 字面值锚定（A/B）：5 个权重 × 位级 == 迁移前原始常量 ─────────────────────────
    [Fact]
    public void Stealth_weights_match_original_literals()
    {
        BitEqual(GoldenDarkness, NightWatchContest.StealthDarknessWeight);
        BitEqual(GoldenApparel, NightWatchContest.StealthApparelWeight);
        BitEqual(GoldenDistanceWeight, NightWatchContest.StealthDistanceWeight);
        BitEqual(GoldenDistanceReference, NightWatchContest.StealthDistanceReference);
        BitEqual(GoldenCover, NightWatchContest.StealthCoverWeight);
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void NightWatchContest_reads_from_catalog_section()
    {
        var section = GameConfigCatalog.Section<NightWatchConfig>();
        BitEqual(section.StealthDarknessWeight, NightWatchContest.StealthDarknessWeight);
        BitEqual(section.StealthApparelWeight, NightWatchContest.StealthApparelWeight);
        BitEqual(section.StealthDistanceWeight, NightWatchContest.StealthDistanceWeight);
        BitEqual(section.StealthDistanceReference, NightWatchContest.StealthDistanceReference);
        BitEqual(section.StealthCoverWeight, NightWatchContest.StealthCoverWeight);
    }

    // ── 第二批：警戒力权重 + 未发现后果 × 位级/直等 == 迁移前原始常量 ─────────────────
    [Fact]
    public void Alertness_and_outcome_values_match_original_literals()
    {
        BitEqual(GoldenVisionWeight, NightWatchContest.VisionWeight);
        BitEqual(GoldenHearingWeight, NightWatchContest.HearingWeight);
        BitEqual(GoldenHearingBaseRange, NightWatchContest.HearingBaseRange);
        BitEqual(GoldenPreemptive, NightWatchContest.PreemptiveStrikeMultiplier);
        Assert.Equal(GoldenSilentTheftMin, NightWatchContest.SilentTheftMinUnits);
        Assert.Equal(GoldenSilentTheftMax, NightWatchContest.SilentTheftMaxUnits);
    }

    [Fact]
    public void NightWatchContest_reads_alertness_and_outcomes_from_catalog()
    {
        var section = GameConfigCatalog.Section<NightWatchConfig>();
        BitEqual(section.VisionWeight, NightWatchContest.VisionWeight);
        BitEqual(section.HearingWeight, NightWatchContest.HearingWeight);
        BitEqual(section.HearingBaseRange, NightWatchContest.HearingBaseRange);
        BitEqual(section.PreemptiveStrikeMultiplier, NightWatchContest.PreemptiveStrikeMultiplier);
        Assert.Equal(section.SilentTheftMinUnits, NightWatchContest.SilentTheftMinUnits);
        Assert.Equal(section.SilentTheftMaxUnits, NightWatchContest.SilentTheftMaxUnits);
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var golden = new NightWatchConfig
        {
            StealthDarknessWeight = GoldenDarkness,
            StealthApparelWeight = GoldenApparel,
            StealthDistanceWeight = GoldenDistanceWeight,
            StealthDistanceReference = GoldenDistanceReference,
            StealthCoverWeight = GoldenCover,
            VisionWeight = GoldenVisionWeight,
            HearingWeight = GoldenHearingWeight,
            HearingBaseRange = GoldenHearingBaseRange,
            PreemptiveStrikeMultiplier = GoldenPreemptive,
            SilentTheftMinUnits = GoldenSilentTheftMin,
            SilentTheftMaxUnits = GoldenSilentTheftMax,
        };
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<NightWatchConfig>(json, GameConfigLoader.Options)!;
        AssertSectionBitEqual(golden, back);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 nightwatch.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_nightwatch_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "nightwatch.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new NightWatchConfig();
        var loaded = (NightWatchConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        BitEqual(GoldenDarkness, loaded.StealthDarknessWeight);
        BitEqual(GoldenApparel, loaded.StealthApparelWeight);
        BitEqual(GoldenDistanceWeight, loaded.StealthDistanceWeight);
        BitEqual(GoldenDistanceReference, loaded.StealthDistanceReference);
        BitEqual(GoldenCover, loaded.StealthCoverWeight);
        BitEqual(GoldenVisionWeight, loaded.VisionWeight);
        BitEqual(GoldenHearingWeight, loaded.HearingWeight);
        BitEqual(GoldenHearingBaseRange, loaded.HearingBaseRange);
        BitEqual(GoldenPreemptive, loaded.PreemptiveStrikeMultiplier);
        Assert.Equal(GoldenSilentTheftMin, loaded.SilentTheftMinUnits);
        Assert.Equal(GoldenSilentTheftMax, loaded.SilentTheftMaxUnits);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 NightWatch 段 ────────────────────
    [Fact]
    public void Loader_reflection_discovers_nightwatch_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        BitEqual(GoldenCover, cfg.NightWatch.StealthCoverWeight);
    }

    // ── 缺文件 / 坏 json fail-fast（不软回落）─────────────────────────────────────
    [Fact]
    public void Loader_missing_file_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => null!));

    [Fact]
    public void Loader_bad_json_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => "{ not valid json "));

    // ── 功能锚定：ComputeStealth 吃的是 config 值 ─────────────────────────────────
    [Fact]
    public void ComputeStealth_uses_externalized_weights()
    {
        // 光照 0（全黑，darkness 项满档=1×权重）、无服饰、距离=参考值（距离项满档=1×权重）、完全遮蔽（1×权重）。
        // 合力 = Darkness*1 + Apparel*0 + DistanceWeight*1 + Cover*1 = 1 + 0 + 0.6 + 0.5 = 2.1。
        float s = NightWatchContest.ComputeStealth(
            lightLevel: 0f, apparelStealthSum: 0f,
            distance: GoldenDistanceReference, coverWeight: 1f);
        BitEqual(GoldenDarkness + GoldenDistanceWeight * 1f + GoldenCover * 1f, s);
    }

    // ── 功能锚定（第二批）：警戒/后果值确实吃 config ─────────────────────────────────
    [Fact]
    public void ComputeAlertness_uses_externalized_vision_and_hearing_weights()
    {
        // 满档视力(1)+距离0(听力满档)、无岗哨加成/疲劳/效率折减 ⇒ 合力 = VisionWeight*1 + HearingWeight*1 = 1.0 + 0.6。
        float a = NightWatchContest.ComputeAlertness(visionAcuity: 1f, distance: 0f, hearingRange: GoldenHearingBaseRange);
        BitEqual(GoldenVisionWeight * 1f + GoldenHearingWeight * 1f, a);
    }

    [Fact]
    public void ComputeAlertness_sentinel_default_resolves_to_configured_hearing_base_range()
    {
        // 省略 hearingRange（走哨兵 -1 → 运行时解析为 HearingBaseRange）与显式传 HearingBaseRange 完全等价（迁移前默认参数语义）。
        float def = NightWatchContest.ComputeAlertness(visionAcuity: 0.3f, distance: 110f);
        float exp = NightWatchContest.ComputeAlertness(visionAcuity: 0.3f, distance: 110f, hearingRange: NightWatchContest.HearingBaseRange);
        BitEqual(exp, def);
    }

    [Fact]
    public void PreemptiveDamageMultiplier_reads_configured_multiplier()
    {
        BitEqual(GoldenPreemptive, NightWatchContest.PreemptiveDamageMultiplier(RaiderIntent.Killer));
        BitEqual(1f, NightWatchContest.PreemptiveDamageMultiplier(RaiderIntent.Looter));
    }

    [Fact]
    public void SilentTheftAmount_bounds_read_configured_min_max()
    {
        // 库存充足时：rng→0 取下界(min=1)，rng→1 端点 clamp 回上界(max=4)。
        int lo = NightWatchContest.SilentTheftAmount(stealableUnits: 100, new SequenceRandomSource(0.0));
        int hi = NightWatchContest.SilentTheftAmount(stealableUnits: 100, new SequenceRandomSource(0.999999));
        Assert.Equal(GoldenSilentTheftMin, lo);
        Assert.Equal(GoldenSilentTheftMax, hi);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    /// <summary>float 位级相等（比 == 更严）——证明"往返一位不差"。</summary>
    private static void BitEqual(float expected, float actual)
        => Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

    /// <summary>反射比对两个段实例的全部 float 属性（位级）。</summary>
    private static void AssertSectionBitEqual(NightWatchConfig a, NightWatchConfig b)
    {
        foreach (var p in typeof(NightWatchConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetValue(a) is float fa && p.GetValue(b) is float fb)
            {
                Assert.True(BitConverter.SingleToInt32Bits(fa) == BitConverter.SingleToInt32Bits(fb),
                    $"{p.Name} float 漂移：{fa} vs {fb}");
            }
            else if (p.GetValue(a) is int ia && p.GetValue(b) is int ib)
            {
                Assert.True(ia == ib, $"{p.Name} int 漂移：{ia} vs {ib}");
            }
        }
    }

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
