using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（被 DeadSignal.Combat.Tests 与 WikiExtract 以 Link 编入；godot 运行时直接编译）。

/// <summary>
/// 电台主线（军方/结局）数值段：<c>military.json</c>——回复军方后到军方白天来袭的<b>间隔天数</b>（数值真源，
/// 原 <see cref="RadioMainline.MilitaryRaidDelayDays"/> 的 <c>const int</c>）。
/// <para>
/// 📐 <b>照 config-consumer-pilot 立的 godot 侧配置范式</b>（对应样板 <c>NightWatchConfig</c>）：改三处即成一段——
/// ①<see cref="FileName"/> 换自己的 json；②字段换成自己的数值；③<see cref="FromJson"/> 里换反序列化目标类型。
/// 其余（接口、加载器、宿主接线）全不动，<see cref="GameConfigLoader.Parse"/> 反射自动发现本段。
/// </para>
/// <para>
/// ⚠️ <b>只搬数值、不搬 authored 结构</b>：本段只装 <see cref="RadioMainline"/> 里<b>唯一的可调数字</b>
/// （军袭倒计时间隔）。电台主线的<b>状态机逻辑、flag 键、以及广播/抉择文案草稿都是叙事/结构内容</b>，
/// <b>留在 RadioMainline.cs 不外置</b>。南方三问门槛（PassThreshold 等）在 <c>SouthTrial.cs</c>，属另一子系统、不在本段。
/// </para>
/// <para>
/// init 默认值＝迁移前的原始常量（proto 只用于反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/>
/// 加载的 json 值覆盖）。数值皆「拟定待调」，由用户/目视校准。
/// </para>
/// </summary>
public sealed class MilitaryConfig : IGameConfigSection
{
    /// <summary>回复军方后到军方白天来袭的间隔天数（军袭期满 = 回复日 + 本值）。</summary>
    public int MilitaryRaidDelayDays { get; init; } = 2;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "military.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<MilitaryConfig>(json, options)
           ?? throw new InvalidOperationException("military.json 反序列化为空（fail-fast）。");
}
