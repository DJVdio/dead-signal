using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（被 DeadSignal.Combat.Tests / DeadSignal.Sim / WikiExtract 以 Link 编入；godot 运行时直接编译）。

/// <summary>
/// 南方三问考验数值段：<c>southtrial.json</c>——三题总分的<b>通过门槛</b>（数值真源，原
/// <see cref="SouthTrial.PassThreshold"/> 的 <c>const int</c>）。
/// <para>
/// 📐 <b>照 config-consumer-pilot 立的 godot 侧配置范式</b>（对应样板 <c>NightWatchConfig</c> / <c>MilitaryConfig</c>）：
/// 改三处即成一段——①<see cref="FileName"/> 换自己的 json；②字段换成自己的数值；③<see cref="FromJson"/> 里换
/// 反序列化目标类型。其余（接口、加载器、宿主接线）全不动，<see cref="GameConfigLoader.Parse"/> 反射自动发现本段。
/// </para>
/// <para>
/// ⚠️ <b>只搬数值、不搬 authored 结构</b>：本段只装 <see cref="SouthTrial"/> 里<b>唯一的平衡可调数字</b>
/// （三问通过门槛）。三问的<b>题目/答案/分数映射、状态机与 flag 键、裁决/回绝文案草稿都是叙事/结构内容</b>，
/// <b>留在 SouthTrial.cs 不外置</b>。<see cref="SouthTrial.QuestionCount"/>（3 题）与
/// <see cref="SouthTrial.MaxScorePerQuestion"/>（每题满 2 分）是与 authored 题面/答案绑定的<b>结构常量</b>
/// （非平衡旋钮），亦留 const 不外置。
/// </para>
/// <para>
/// init 默认值＝迁移前的原始常量（proto 只用于反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/>
/// 加载的 json 值覆盖）。门槛为「占位待 author 校准」，由用户设计正式题目时定值。
/// </para>
/// </summary>
public sealed class SouthTrialConfig : IGameConfigSection
{
    /// <summary>三题总分通过门槛（满本值才通过；不满即失败。满分 = QuestionCount × MaxScorePerQuestion = 6）。</summary>
    public int PassThreshold { get; init; } = 5;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "southtrial.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<SouthTrialConfig>(json, options)
           ?? throw new InvalidOperationException("southtrial.json 反序列化为空（fail-fast）。");
}
