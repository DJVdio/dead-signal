using System;
using System.IO;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 南方三问考验（南逃 WIN 门槛）零漂移 A/B 焊死】config-cleanup 单。
/// <para>
/// <see cref="SouthTrial"/> 里唯一的平衡可调数字——三题总分的通过门槛——已从 C# <c>const int</c>
/// 搬到 <c>godot/data/config/southtrial.json</c>，静态取用点身体改成
/// <c>=&gt; GameConfigCatalog.Section&lt;SouthTrialConfig&gt;().PassThreshold</c>（照 config-consumer-pilot 的
/// godot 侧配置范式，对应样板 MilitaryConfig）。本文件钉死「搬家没搬错这个数」，并给整套接线上护栏：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 <c>[ModuleInitializer]</c>（TestGameConfigBootstrap）注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：门槛 == 迁移前原始常量 5（int 精确相等）。</item>
///   <item><b>取用点确实读 catalog</b>：<see cref="SouthTrial.PassThreshold"/> == catalog 段值（证明委托到配置、非残留常量）。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，字段相等 ⇒ 加载器不丢值（永久护栏）。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 southtrial.json 经 FromJson 解析 == golden。</item>
///   <item><b>反射驱动加载</b>：GameConfigLoader.Parse 自动发现 SouthTrial 段。</item>
///   <item><b>功能锚定</b>：<see cref="SouthTrial.IsPassed"/>/<see cref="SouthTrial.IsFailed"/> 的通过判定吃的是 config 值（满 5 分通过、4 分失败）。</item>
/// </list>
/// <para>
/// ⚠️ 南方三问不进 Sim 战斗结算（南逃 WIN 是消费层状态机）——零漂移铁证为字面锚定 + 往返保真 + 功能锚定，不跑 Sim MD5。
/// 现有 <see cref="SouthTrial"/> 行为测试（SouthTrialTests）不受影响：catalog 返回 5 与旧 const 等值。
/// </para>
/// </summary>
public sealed class SouthTrialConfigMigrationTests
{
    // 迁移前 SouthTrial 里的原始常量（golden）——A/B 的"旧硬编码"一侧。改 southtrial.json 里这个值会让本表变红。
    private const int GoldenPassThreshold = 5;

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        int t = SouthTrial.PassThreshold;   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取通过门槛后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal(GoldenPassThreshold, t);
    }

    // ── 字面值锚定（A/B）：门槛 == 迁移前原始常量 ─────────────────────────────────
    [Fact]
    public void PassThreshold_matches_original_literal()
        => Assert.Equal(GoldenPassThreshold, SouthTrial.PassThreshold);

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void SouthTrial_reads_from_catalog_section()
    {
        var section = GameConfigCatalog.Section<SouthTrialConfig>();
        Assert.Equal(section.PassThreshold, SouthTrial.PassThreshold);
    }

    // ── 往返保真：加载器不丢值（永久护栏）────────────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip()
    {
        var golden = new SouthTrialConfig { PassThreshold = GoldenPassThreshold };
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<SouthTrialConfig>(json, GameConfigLoader.Options)!;
        Assert.Equal(golden.PassThreshold, back.PassThreshold);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 southtrial.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_southtrial_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "southtrial.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new SouthTrialConfig();
        var loaded = (SouthTrialConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        Assert.Equal(GoldenPassThreshold, loaded.PassThreshold);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 SouthTrial 段 ────────────────────
    [Fact]
    public void Loader_reflection_discovers_southtrial_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        Assert.Equal(GoldenPassThreshold, cfg.SouthTrial.PassThreshold);
    }

    // ── 功能锚定：通过/失败判定吃的是 config 值（满 5 分通过、4 分失败）─────────────────
    [Fact]
    public void Verdict_uses_externalized_threshold()
    {
        // 满 5 分（2+2+1）：答满三问 ⇒ 通过。
        var pass = new StoryFlags();
        SouthTrial.RecordAnswer(pass, 2);
        SouthTrial.RecordAnswer(pass, 2);
        SouthTrial.RecordAnswer(pass, 1);
        Assert.True(SouthTrial.IsComplete(pass));
        Assert.Equal(GoldenPassThreshold, SouthTrial.TotalScore(pass));
        Assert.True(SouthTrial.IsPassed(pass));
        Assert.False(SouthTrial.IsFailed(pass));

        // 4 分（2+1+1）：答满三问但差 1 分 ⇒ 失败。
        var fail = new StoryFlags();
        SouthTrial.RecordAnswer(fail, 2);
        SouthTrial.RecordAnswer(fail, 1);
        SouthTrial.RecordAnswer(fail, 1);
        Assert.Equal(GoldenPassThreshold - 1, SouthTrial.TotalScore(fail));
        Assert.False(SouthTrial.IsPassed(fail));
        Assert.True(SouthTrial.IsFailed(fail));
    }

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
