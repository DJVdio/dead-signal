using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · godot 侧配置范式的零漂移 A/B 焊死】config-horde 单（尸潮时限）。
/// <para>
/// <see cref="HordeTimeline"/> 的 8 个可调数值（到期日 + 到期终局围攻波次调度）已从 C# <c>const</c> 搬到
/// <c>godot/data/config/horde.json</c>，静态取用点身体改成
/// <c>=&gt; GameConfigCatalog.Section&lt;HordeConfig&gt;().X</c>（消费层平行容器，镜像 config-consumer-pilot 范式）。
/// 本文件钉死「搬家没搬错一个数」，并给整套 godot 侧容器上护栏（照 NightWatchConfigMigrationTests）：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 <c>[ModuleInitializer]</c>（TestGameConfigBootstrap）注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：8 个数值逐条位级断言 == 迁移前原始常量（float 用 <see cref="BitConverter.SingleToInt32Bits"/>、double 用 <see cref="BitConverter.DoubleToInt64Bits"/>、int 直等）。</item>
///   <item><b>取用点确实读 catalog</b>：<see cref="HordeTimeline"/> 的静态属性 == catalog 段值（证明委托到配置、非残留常量）。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，逐字段位级相等 ⇒ 加载器不丢精度。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 horde.json 经 FromJson 解析 == golden。</item>
///   <item><b>缺文件/坏 json fail-fast</b>：GameConfigLoader.Parse 遇缺文件/坏 json 抛（不软回落）。</item>
///   <item><b>功能锚定</b>：<see cref="HordeTimeline.WaveSize"/>／<see cref="HordeTimeline.Evaluate"/> 吃的是 config 值。</item>
/// </list>
/// <para>
/// ⚠️ 尸潮时限不进主 Duel 蒙特卡洛结算（那只算武器×护甲）——本文件的位级往返 + 字面锚定即零漂移铁证，不跑 Sim MD5。
/// 只搬「可调数值」、authored 旗标键（horde_sighted/horde_arrived/endgame_freeze）留 HordeTimeline.cs 不外置。
/// </para>
/// </summary>
public sealed class HordeConfigMigrationTests
{
    // 迁移前 HordeTimeline 里的原始常量（golden）——A/B 的"旧硬编码"一侧。改 horde.json 里这些值会让本表变红。
    private const int GoldenDeadlineDay = 40;
    private const float GoldenWaveBase = 8f;
    private const float GoldenWaveGrowth = 2f;
    private const float GoldenWaveCampFactor = 0.5f;
    private const int GoldenWaveCap = 60;
    private const double GoldenWaveInterval = 12.0;
    private const int GoldenWaveClearThreshold = 4;
    private const int GoldenMaxConcurrentSiege = 80;

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        int d = HordeTimeline.DeadlineDay;   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取尸潮时限后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal(GoldenDeadlineDay, d);
    }

    // ── 字面值锚定（A/B）：8 个数值 × 位级 == 迁移前原始常量 ─────────────────────────
    [Fact]
    public void Horde_values_match_original_literals()
    {
        Assert.Equal(GoldenDeadlineDay, HordeTimeline.DeadlineDay);
        BitEqual(GoldenWaveBase, HordeTimeline.WaveBase);
        BitEqual(GoldenWaveGrowth, HordeTimeline.WaveGrowth);
        BitEqual(GoldenWaveCampFactor, HordeTimeline.WaveCampFactor);
        Assert.Equal(GoldenWaveCap, HordeTimeline.WaveCap);
        BitEqual(GoldenWaveInterval, HordeTimeline.WaveInterval);
        Assert.Equal(GoldenWaveClearThreshold, HordeTimeline.WaveClearThreshold);
        Assert.Equal(GoldenMaxConcurrentSiege, HordeTimeline.MaxConcurrentSiege);
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void HordeTimeline_reads_from_catalog_section()
    {
        var section = GameConfigCatalog.Section<HordeConfig>();
        Assert.Equal(section.DeadlineDay, HordeTimeline.DeadlineDay);
        BitEqual(section.WaveBase, HordeTimeline.WaveBase);
        BitEqual(section.WaveGrowth, HordeTimeline.WaveGrowth);
        BitEqual(section.WaveCampFactor, HordeTimeline.WaveCampFactor);
        Assert.Equal(section.WaveCap, HordeTimeline.WaveCap);
        BitEqual(section.WaveInterval, HordeTimeline.WaveInterval);
        Assert.Equal(section.WaveClearThreshold, HordeTimeline.WaveClearThreshold);
        Assert.Equal(section.MaxConcurrentSiege, HordeTimeline.MaxConcurrentSiege);
    }

    // ── 往返保真：加载器不丢精度（值无关，永久护栏）────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip_bit_for_bit()
    {
        var golden = new HordeConfig
        {
            DeadlineDay = GoldenDeadlineDay,
            WaveBase = GoldenWaveBase,
            WaveGrowth = GoldenWaveGrowth,
            WaveCampFactor = GoldenWaveCampFactor,
            WaveCap = GoldenWaveCap,
            WaveInterval = GoldenWaveInterval,
            WaveClearThreshold = GoldenWaveClearThreshold,
            MaxConcurrentSiege = GoldenMaxConcurrentSiege,
        };
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<HordeConfig>(json, GameConfigLoader.Options)!;
        AssertSectionBitEqual(golden, back);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 horde.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_horde_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "horde.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new HordeConfig();
        var loaded = (HordeConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        Assert.Equal(GoldenDeadlineDay, loaded.DeadlineDay);
        BitEqual(GoldenWaveBase, loaded.WaveBase);
        BitEqual(GoldenWaveGrowth, loaded.WaveGrowth);
        BitEqual(GoldenWaveCampFactor, loaded.WaveCampFactor);
        Assert.Equal(GoldenWaveCap, loaded.WaveCap);
        BitEqual(GoldenWaveInterval, loaded.WaveInterval);
        Assert.Equal(GoldenWaveClearThreshold, loaded.WaveClearThreshold);
        Assert.Equal(GoldenMaxConcurrentSiege, loaded.MaxConcurrentSiege);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Horde 段 ────────────────────────
    [Fact]
    public void Loader_reflection_discovers_horde_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        Assert.Equal(GoldenDeadlineDay, cfg.Horde.DeadlineDay);
        Assert.Equal(GoldenMaxConcurrentSiege, cfg.Horde.MaxConcurrentSiege);
    }

    // ── 缺文件 / 坏 json fail-fast（不软回落）─────────────────────────────────────
    [Fact]
    public void Loader_missing_file_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => null!));

    [Fact]
    public void Loader_bad_json_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => "{ not valid json "));

    // ── 功能锚定：波次规模 / 相位判定吃的是 config 值 ─────────────────────────────
    [Fact]
    public void WaveSize_uses_externalized_numbers()
    {
        // 首波(waveIndex=0)、4 人营地：raw = WaveBase + 0*WaveGrowth + 4*WaveCampFactor = 8 + 0 + 2 = 10，clamp[1,WaveCap]=10。
        Assert.Equal(
            Math.Clamp((int)Math.Ceiling(GoldenWaveBase + 0 * GoldenWaveGrowth + 4 * GoldenWaveCampFactor), 1, GoldenWaveCap),
            HordeTimeline.WaveSize(waveIndex: 0, campSize: 4));
        // 极端 waveIndex 被 WaveCap 封顶（证明封顶值来自 config）。
        Assert.Equal(GoldenWaveCap, HordeTimeline.WaveSize(waveIndex: 1000, campSize: 100));
    }

    [Fact]
    public void Evaluate_and_DaysRemaining_use_externalized_deadline()
    {
        Assert.Equal(HordePhase.Arrived, HordeTimeline.Evaluate(GoldenDeadlineDay, sighted: false));
        Assert.Equal(HordePhase.Hidden, HordeTimeline.Evaluate(GoldenDeadlineDay - 1, sighted: false));
        Assert.Equal(0, HordeTimeline.DaysRemaining(GoldenDeadlineDay));
        Assert.Equal(1, HordeTimeline.DaysRemaining(GoldenDeadlineDay - 1));
    }

    [Fact]
    public void NextWave_clear_threshold_and_interval_use_externalized_numbers()
    {
        // 残敌 == WaveClearThreshold(含) ⇒ 该补下一波。
        var due = HordeTimeline.NextWave(waveIndex: 1, zombiesAlive: GoldenWaveClearThreshold,
            secondsSinceLastWave: 0, campSize: 4);
        Assert.True(due.ShouldSpawn);
        // 残敌远多于阈值、且未到强制间隔 ⇒ 不投。
        var notDue = HordeTimeline.NextWave(waveIndex: 1, zombiesAlive: GoldenWaveClearThreshold + 20,
            secondsSinceLastWave: GoldenWaveInterval - 1, campSize: 4);
        Assert.False(notDue.ShouldSpawn);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    /// <summary>float 位级相等（比 == 更严）。</summary>
    private static void BitEqual(float expected, float actual)
        => Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits(actual));

    /// <summary>double 位级相等。</summary>
    private static void BitEqual(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    /// <summary>反射比对两个段实例的全部数值属性（float/double 位级、int/long 直等）。</summary>
    private static void AssertSectionBitEqual(HordeConfig a, HordeConfig b)
    {
        foreach (var p in typeof(HordeConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            object? va = p.GetValue(a), vb = p.GetValue(b);
            switch (va)
            {
                case float fa when vb is float fb:
                    Assert.True(BitConverter.SingleToInt32Bits(fa) == BitConverter.SingleToInt32Bits(fb),
                        $"{p.Name} float 漂移：{fa} vs {fb}");
                    break;
                case double da when vb is double db:
                    Assert.True(BitConverter.DoubleToInt64Bits(da) == BitConverter.DoubleToInt64Bits(db),
                        $"{p.Name} double 漂移：{da} vs {db}");
                    break;
                case int ia when vb is int ib:
                    Assert.True(ia == ib, $"{p.Name} int 漂移：{ia} vs {ib}");
                    break;
            }
        }
    }

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
