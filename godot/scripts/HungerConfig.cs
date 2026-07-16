using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Sim/Tests/WikiExtract 以 Link 编入）。

/// <summary>
/// 饥饿能力惩罚数值段：<c>hunger.json</c>——<see cref="HungerState.PenaltyFor"/> 的<b>三档梯度惩罚</b>（数值真源）。
/// <para>
/// 📐 照 <see cref="NightWatchConfig"/> 范式（消费层平行容器，镜像纯库 <c>WeaponConfig</c>）：段类自报 <see cref="FileName"/>、
/// 自解析 <see cref="FromJson"/>，<see cref="GameConfigLoader.Parse"/> 反射 <see cref="GameConfig"/> 自动发现本段。
/// </para>
/// <para>
/// ⚠️ <b>只搬「梯度惩罚」这三个纯方法体数值</b>：<c>DefaultCap</c>(5)/<c>MaxCap</c>(6)/<c>StarvedValue</c>(0) 是
/// <b>编译期 const</b>——被 <c>CampMain.SpawnDougAndBruce</c>/<c>FoodEconomy.FoodDiner</c>/多处测试当<b>默认参数值</b>用，
/// 且是枚举刻度边界，<b>改不动、保留在 HungerState.cs 不外置</b>。<see cref="HungerState.PenaltyFor"/> 里的
/// <c>1.0</c>(饿死·满值兜底)/<c>0.0</c>(饱·无惩罚) 是 switch 两端的<b>语义边界</b>，非可调梯度，<b>保留字面</b>。
/// </para>
/// <para>
/// init 默认值＝迁移前原始字面量（proto 仅供反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/> 加载的
/// json 值覆盖）。数值皆「拟定待调」。
/// </para>
/// </summary>
public sealed class HungerConfig : IGameConfigSection
{
    /// <summary>营养不良(1)档能力惩罚净值 [0,1]（1=完全丧失）。</summary>
    public double MalnourishedPenalty { get; init; } = 0.45;

    /// <summary>极度饥饿(2)档能力惩罚净值 [0,1]。</summary>
    public double RavenousPenalty { get; init; } = 0.25;

    /// <summary>饥饿(3)档能力惩罚净值 [0,1]。</summary>
    public double HungryPenalty { get; init; } = 0.10;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "hunger.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<HungerConfig>(json, options)
           ?? throw new InvalidOperationException("hunger.json 反序列化为空（fail-fast）。");
}
