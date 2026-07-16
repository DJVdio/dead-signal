using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Sim/Tests/WikiExtract 以 Link 编入）。

/// <summary>
/// 诱捕命中率数值段：<c>farming.json</c>——<see cref="TrapLogic"/>（圈套抓鼠兔）/ <see cref="BirdTrapLogic"/>（捕鸟陷阱抓鸽）
/// 的<b>基础命中率 / 每多一个的递减 / 命中率地板</b>，外加圈套的兔鼠分配比例（数值真源）。
/// <para>
/// 📐 照 <see cref="HungerConfig"/> 范式（消费层平行容器，镜像纯库 <c>WeaponConfig</c>）：段类自报 <see cref="FileName"/>、
/// 自解析 <see cref="FromJson"/>，<see cref="GameConfigLoader.Parse"/> 反射 <see cref="GameConfig"/> 自动发现本段。
/// </para>
/// <para>
/// ⚠️ <b>只搬「命中率」这几个纯方法体数值</b>（<see cref="TrapLogic.ChanceOf"/> / <see cref="BirdTrapLogic.ChanceOf"/> /
/// <see cref="TrapLogic.Roll"/> 的兔鼠分配都在方法体里读它们）。<b>种植（菜园）那批常量不外置</b>：
/// <c>CropPlotSpec.MaxPlants</c>(16) / <c>CropPlotLogic.GrowGameHours</c>(84) / <c>SeedCost</c>(1) /
/// <c>PlantActionGameHours</c>(0.15) 是<b>编译期 const</b>——被 <c>PlantWorkMinutes = (int)(PlantActionGameHours*60)</c>
/// 这类派生 const、以及别处当默认值引用，改静态属性会破编译期约束（同 HungerState.DefaultCap 陷阱），保留字面。
/// </para>
/// <para>
/// init 默认值＝迁移前原始字面量（proto 仅供反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/> 加载的
/// json 值覆盖）。数值皆「用户给定」/「拟定待调」，见各字段与 <see cref="TrapLogic"/> 注释。
/// </para>
/// </summary>
public sealed class FarmingConfig : IGameConfigSection
{
    /// <summary>圈套第 1 个陷阱的捕获几率（用户给定 30%）。</summary>
    public double SnareBaseChance { get; init; } = 0.30;

    /// <summary>圈套每多一个陷阱的命中递减（用户给定 5 个百分点）。</summary>
    public double SnareChanceStep { get; init; } = 0.05;

    /// <summary>圈套命中率地板（用户给定 5%）——递减撞到即停。</summary>
    public double SnareMinChance { get; init; } = 0.05;

    /// <summary>圈套抓到兔子的比例（其余为鼠）。拟定待调。</summary>
    public double SnareRabbitShare { get; init; } = 0.30;

    /// <summary>捕鸟陷阱第 1 个的捕获几率（拟定待调 20%）。</summary>
    public double BirdTrapBaseChance { get; init; } = 0.20;

    /// <summary>捕鸟陷阱每多一个的命中递减（5 个百分点）。</summary>
    public double BirdTrapChanceStep { get; init; } = 0.05;

    /// <summary>捕鸟陷阱命中率地板（5%）——递减撞到即停。</summary>
    public double BirdTrapMinChance { get; init; } = 0.05;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "farming.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<FarmingConfig>(json, options)
           ?? throw new InvalidOperationException("farming.json 反序列化为空（fail-fast）。");
}
