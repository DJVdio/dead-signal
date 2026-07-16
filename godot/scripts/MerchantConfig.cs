using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Sim/Tests/WikiExtract 以 Link 编入）。

/// <summary>
/// 神秘商人经济数值段：<c>merchant.json</c>——买卖价率（数值真源）。
/// <para>
/// 照消费层 config 迁移范式（<see cref="NightWatchConfig"/> / <c>MilitaryConfig</c>）：单段独立文件，
/// <see cref="GameConfig"/> 挂一行，<see cref="GameConfigLoader.Parse"/> 反射自动加载。
/// </para>
/// <para>
/// ⚠️ <b>只搬价率、不搬调度/断商结构</b>：本段只装 <see cref="MerchantTrade"/> 的<b>买卖价率标量</b>
/// （原 <c>BuyRatePercent</c>=100 / <c>SellRatePercent</c>=60）。<c>MerchantSchedule</c> 到访间隔上下限
/// （minGap=1/maxGap=5）是构造器<b>默认参数值</b>（编译期 const）且属到访调度状态机结构，<b>留在代码不外置</b>；
/// <c>MerchantLineage</c> 断商/接班逻辑同为 authored 结构，不外置。
/// </para>
/// <para>
/// init 默认值＝迁移前的原始常量（proto 只用于反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/>
/// 加载的 json 值覆盖）。数值皆用户拍板（买 100%、卖 60%），改这里即改经济旋钮。
/// </para>
/// </summary>
public sealed class MerchantConfig : IGameConfigSection
{
    /// <summary>玩家从商人**买入**的价率（基准价的百分比；用户拍板 100%）。</summary>
    public int BuyRatePercent { get; init; } = 100;

    /// <summary>玩家**卖给**商人的价率（基准价的百分比；用户拍板 60%）。</summary>
    public int SellRatePercent { get; init; } = 60;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "merchant.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<MerchantConfig>(json, options)
           ?? throw new InvalidOperationException("merchant.json 反序列化为空（fail-fast）。");
}
