using System;
using System.IO;
using System.Text.Json;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【消费层数值外置 · 神秘商人经济零漂移 A/B 焊死】config-stragglers 单。
/// <para>
/// <see cref="MerchantTrade"/> 的两个买卖价率——买入 100% / 卖出 60%——已从 C# <c>const int</c>
/// 搬到 <c>godot/data/config/merchant.json</c>（商人子系统首次迁移，新建 <see cref="MerchantConfig"/> 段），
/// 静态取用点身体改成 <c>=&gt; GameConfigCatalog.Section&lt;MerchantConfig&gt;().X</c>（照 config-consumer-pilot 的
/// godot 侧配置范式，镜像纯库 CombatConfig）。本文件钉死「搬家没搬错这两个数」，并给接线上护栏：
/// </para>
/// <list type="number">
///   <item><b>接线活着</b>：宿主 <c>[ModuleInitializer]</c>（TestGameConfigBootstrap）注册的 Bootstrapper 让 catalog 懒加载成功。</item>
///   <item><b>字面值锚定（A/B）</b>：买/卖价率 == 迁移前原始常量 100 / 60（int 精确相等）。</item>
///   <item><b>取用点确实读 catalog</b>：<see cref="MerchantTrade.BuyRatePercent"/>/<see cref="MerchantTrade.SellRatePercent"/> == catalog 段值。</item>
///   <item><b>往返保真</b>：段序列化→反序列化，字段相等 ⇒ 加载器不丢值（永久护栏）。</item>
///   <item><b>反射加载盘上文件</b>：GameConfigFiles 定位的 merchant.json 经 FromJson 解析 == golden。</item>
///   <item><b>反射驱动加载 + 缺文件/坏 json fail-fast</b>：GameConfigLoader.Parse 自动发现 Merchant 段；遇缺文件/坏 json 抛。</item>
///   <item><b>功能锚定</b>：<see cref="MerchantTrade.BuyPrice"/>/<see cref="MerchantTrade.SellPrice"/> 的折算吃的是 config 值（买全价、卖六折）。</item>
/// </list>
/// <para>
/// ⚠️ 商人不进 Sim 战斗结算（交易是消费层判定）——零漂移铁证为字面锚定 + 往返保真 + 功能锚定，不跑 Sim MD5。
/// 现有 <see cref="MerchantTrade"/> 行为测试（MerchantTradeTests/MerchantSellTests）不受影响：catalog 返回 100/60 与旧 const 等值。
/// </para>
/// <para>
/// 🔴 <b>只搬价率、不搬调度/断商结构</b>：<c>MerchantSchedule</c> 到访间隔上下限（minGap=1/maxGap=5）是构造器
/// <b>默认参数值</b>（编译期 const）+ 属到访调度状态机结构 ⇒ 不外置；<c>MerchantLineage</c> 断商/接班同为 authored 结构。
/// </para>
/// </summary>
public sealed class MerchantConfigMigrationTests
{
    // 迁移前 MerchantTrade 里的原始常量（golden）——A/B 的"旧硬编码"一侧。改 merchant.json 里这些值会让本表变红。
    private const int GoldenBuyRatePercent = 100;
    private const int GoldenSellRatePercent = 60;

    [Fact]
    public void Catalog_is_wired_and_lazy_loaded()
    {
        int buy = MerchantTrade.BuyRatePercent;   // 首次访问触发懒加载
        Assert.True(GameConfigCatalog.IsInitialized, "首次取商人价率后 catalog 应已初始化（Bootstrapper 懒加载生效）");
        Assert.Equal(GoldenBuyRatePercent, buy);
    }

    // ── 字面值锚定（A/B）：买/卖价率 == 迁移前原始常量 ─────────────────────────────
    [Fact]
    public void Rates_match_original_literals()
    {
        Assert.Equal(GoldenBuyRatePercent, MerchantTrade.BuyRatePercent);
        Assert.Equal(GoldenSellRatePercent, MerchantTrade.SellRatePercent);
    }

    // ── 取用点确实读 catalog（证明委托到配置、不是残留 const）──────────────────────
    [Fact]
    public void MerchantTrade_reads_from_catalog_section()
    {
        var section = GameConfigCatalog.Section<MerchantConfig>();
        Assert.Equal(section.BuyRatePercent, MerchantTrade.BuyRatePercent);
        Assert.Equal(section.SellRatePercent, MerchantTrade.SellRatePercent);
    }

    // ── 往返保真：加载器不丢值（永久护栏）────────────────────────────────────────
    [Fact]
    public void Section_survives_json_round_trip()
    {
        var golden = new MerchantConfig { BuyRatePercent = GoldenBuyRatePercent, SellRatePercent = GoldenSellRatePercent };
        string json = JsonSerializer.Serialize(golden, GameConfigLoader.Options);
        var back = JsonSerializer.Deserialize<MerchantConfig>(json, GameConfigLoader.Options)!;
        Assert.Equal(golden.BuyRatePercent, back.BuyRatePercent);
        Assert.Equal(golden.SellRatePercent, back.SellRatePercent);
    }

    // ── 反射加载盘上文件：GameConfigFiles 定位的 merchant.json 经 FromJson == golden ─────────
    [Fact]
    public void On_disk_merchant_json_parses_to_golden()
    {
        string dir = GameConfigFiles.LocateConfigDir();
        string path = Path.Combine(dir, "merchant.json");
        Assert.True(File.Exists(path), $"应存在盘上配置 {path}");
        var proto = new MerchantConfig();
        var loaded = (MerchantConfig)proto.FromJson(File.ReadAllText(path), GameConfigLoader.Options);
        Assert.Equal(GoldenBuyRatePercent, loaded.BuyRatePercent);
        Assert.Equal(GoldenSellRatePercent, loaded.SellRatePercent);
    }

    // ── 反射驱动加载：GameConfigLoader.Parse 自动发现 Merchant 段 ──────────────────────
    [Fact]
    public void Loader_reflection_discovers_merchant_section()
    {
        var cfg = GameConfigLoader.Parse(ReadTextFrom(GameConfigFiles.LocateConfigDir()));
        Assert.Equal(GoldenBuyRatePercent, cfg.Merchant.BuyRatePercent);
        Assert.Equal(GoldenSellRatePercent, cfg.Merchant.SellRatePercent);
    }

    // ── 缺文件 / 坏 json fail-fast（不软回落）─────────────────────────────────────
    [Fact]
    public void Loader_missing_file_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => null!));

    [Fact]
    public void Loader_bad_json_fails_fast()
        => Assert.Throws<InvalidOperationException>(() => GameConfigLoader.Parse(_ => "{ not valid json "));

    // ── 功能锚定：买卖折算吃的是 config 值（买全价、卖六折，均在「分」上分级取整）─────────
    [Fact]
    public void BuyPrice_and_SellPrice_use_externalized_rates()
    {
        // 基准 300 分（3.00 银）：买入 300×100/100 = 300；卖出 300×60/100 = 180。
        Assert.Equal(300 * GoldenBuyRatePercent / 100, MerchantTrade.BuyPrice(300));
        Assert.Equal(300 * GoldenSellRatePercent / 100, MerchantTrade.SellPrice(300));
    }

    // 把「config 目录」封成一个 readText 委托（供反射驱动加载测试）。
    private static Func<string, string> ReadTextFrom(string dir)
        => file => File.ReadAllText(Path.Combine(dir, file));
}
