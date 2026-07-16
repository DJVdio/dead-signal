using System;
using System.IO;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 电台主线（军方/结局）零漂移 A/B 焊死】config-military 单。
/// <para>
/// <see cref="RadioMainline"/> 里唯一的可调数字——回复军方后军袭倒计时的间隔天数——已从 C# <c>const int</c>
/// 搬到 <c>godot/data/config/military.json</c>，静态取用点身体改成
/// <c>=&gt; GameConfigCatalog.Section&lt;MilitaryConfig&gt;().MilitaryRaidDelayDays</c>（照 config-consumer-pilot 的
/// godot 侧配置范式，镜像纯库 CombatConfig）。本文件钉死「搬家没搬错这个数」，并给整套接线上护栏：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 <c>[ModuleInitializer]</c>（TestGameConfigBootstrap）注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：间隔天数 == 迁移前原始常量 2（int 精确相等）。</item>
///   <item><b>取用点确实读 catalog</b>：<see cref="RadioMainline.MilitaryRaidDelayDays"/> == catalog 段值（证明委托到配置、非残留常量）。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，字段相等 ⇒ 加载器不丢值（永久护栏）。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 military.json 经 FromJson 解析 == golden。</item>
///   <item><b>反射驱动加载 + 缺文件/坏 json fail-fast</b>：GameConfigLoader.Parse 自动发现 Military 段；遇缺文件/坏 json 抛。</item>
///   <item><b>功能锚定</b>：<see cref="RadioMainline.MilitaryRaidDue"/> 的到期算术吃的是 config 值（回复日 + 2）。</item>
/// </list>
/// <para>
/// ⚠️ 电台主线不进 Sim 战斗结算（结局触发是消费层状态机）——零漂移铁证为字面锚定 + 往返保真 + 功能锚定，不跑 Sim MD5。
/// 现有 <see cref="RadioMainline"/> 行为测试（RadioMainlineTests/SouthTrialTests）不受影响：catalog 返回 2 与旧 const 等值。
/// </para>
/// </summary>
public sealed class MilitaryConfigMigrationTests
{
    // 迁移前 RadioMainline 里的原始常量（golden）——A/B 的"旧硬编码"一侧。改 military.json 里这个值会让本表变红。
    private const int GoldenRaidDelayDays = 2;

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        int d = RadioMainline.MilitaryRaidDelayDays;   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取军袭间隔后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal(GoldenRaidDelayDays, d);
    }

    // ── 字面值锚定（A/B）：间隔天数 == 迁移前原始常量 ─────────────────────────────
    [Fact]
    public void RaidDelay_matches_original_literal()
        => Assert.Equal(GoldenRaidDelayDays, RadioMainline.MilitaryRaidDelayDays);

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void RadioMainline_reads_from_catalog_section()
    {
        var section = GameConfigCatalog.Section<MilitaryConfig>();
        Assert.Equal(section.MilitaryRaidDelayDays, RadioMainline.MilitaryRaidDelayDays);
    }

    // ── 往返保真：加载器不丢值（永久护栏）────────────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip()
    {
        var golden = new MilitaryConfig { MilitaryRaidDelayDays = GoldenRaidDelayDays };
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<MilitaryConfig>(json, GameConfigLoader.Options)!;
        Assert.Equal(golden.MilitaryRaidDelayDays, back.MilitaryRaidDelayDays);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 military.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_military_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "military.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new MilitaryConfig();
        var loaded = (MilitaryConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        Assert.Equal(GoldenRaidDelayDays, loaded.MilitaryRaidDelayDays);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Military 段 ──────────────────────
    [Fact]
    public void Loader_reflection_discovers_military_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        Assert.Equal(GoldenRaidDelayDays, cfg.Military.MilitaryRaidDelayDays);
    }

    // ── 缺文件 / 坏 json fail-fast（不软回落）─────────────────────────────────────
    [Fact]
    public void Loader_missing_file_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => null!));

    [Fact]
    public void Loader_bad_json_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => "{ not valid json "));

    // ── 功能锚定：军袭到期算术吃的是 config 值（回复日 + 2）─────────────────────────
    [Fact]
    public void MilitaryRaidDue_uses_externalized_delay()
    {
        const int replyDay = 10;
        // 间隔 = 2 ⇒ 期满于第 12 天：第 11 天未到、第 12 天正好到。
        Assert.False(RadioMainline.MilitaryRaidDue(replyDay, replyDay + GoldenRaidDelayDays - 1));
        Assert.True(RadioMainline.MilitaryRaidDue(replyDay, replyDay + GoldenRaidDelayDays));
    }

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
