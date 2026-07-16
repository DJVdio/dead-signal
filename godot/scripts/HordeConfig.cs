using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Sim/Tests/WikiExtract 以 Link 编入）。

/// <summary>
/// 尸潮时限数值段：<c>horde.json</c>——尸潮到期日 + 到期终局围攻波次调度数值（数值真源）。
/// <para>
/// 📐 <b>照 config-consumer-pilot 立的 godot 侧配置范式</b>（对应纯库 <c>WeaponConfig</c>、平行样板 <c>NightWatchConfig</c>）：
/// 段类自报 <see cref="FileName"/> + 自解析 <see cref="FromJson"/>，<see cref="GameConfig"/> 加一行即接入，
/// <see cref="GameConfigLoader"/> 反射自动发现，加载器/宿主接线零改动。
/// </para>
/// <para>
/// ⚠️ <b>只搬「可调数值」、不搬 authored 语义</b>：本段只装 <see cref="HordeTimeline"/> 的 8 个可调标量
/// （原 <c>public const</c>）。三个剧情旗标键（<see cref="HordeTimeline.SightedFlag"/> 等）是 authored 字符串常量、
/// 且被 <c>LookoutSighting</c> 当编译期 const 引用，<b>留在 HordeTimeline.cs 不外置</b>。
/// </para>
/// <para>
/// init 默认值＝迁移前的原始常量（proto 只用于反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/>
/// 加载的 json 值覆盖）。数值皆「拟定待调」，由 Sim（endgame/wallcal）与试玩校准。
/// </para>
/// </summary>
public sealed class HordeConfig : IGameConfigSection
{
    /// <summary>时限天数：day &gt;= 此值 → 尸潮抵达终局围攻。</summary>
    public int DeadlineDay { get; init; } = 40;

    /// <summary>首波规模（比常规袭营大，压迫感）。</summary>
    public float WaveBase { get; init; } = 8f;

    /// <summary>每波规模递增。</summary>
    public float WaveGrowth { get; init; } = 2f;

    /// <summary>波次随在营人数微增（营地越大越招祸）。</summary>
    public float WaveCampFactor { get; init; } = 0.5f;

    /// <summary>单波渲染上限（防 Godot 实例爆炸；封顶不封"无限轮次"）。</summary>
    public int WaveCap { get; init; } = 60;

    /// <summary>强制下一波的最长间隔秒：即便残敌仍多，超此即补投，不给喘息。</summary>
    public double WaveInterval { get; init; } = 12.0;

    /// <summary>残敌降到此数(含)及以下即补下一波（不必全清就压上来）。</summary>
    public int WaveClearThreshold { get; init; } = 4;

    /// <summary>场上丧尸并发硬上限：投放前按此把本波规模 clamp 到 上限−残敌，防 day40 无界实体堆积。</summary>
    public int MaxConcurrentSiege { get; init; } = 80;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "horde.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<HordeConfig>(json, options)
           ?? throw new InvalidOperationException("horde.json 反序列化为空（fail-fast）。");
}
