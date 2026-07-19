using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Combat;

/// <summary>
/// 身体部位数值段：<c>body.json</c>。承载 <see cref="HumanBody"/> 里**可调的数字**——
/// 每个部位的体积权重（命中倾向）与最大 HP（也是切除阈值：切除判据 <c>单次伤害 ≥ 部位 MaxHp</c>），
/// 以及残疾惩罚；具体比例以 Wiki 配置表为准。
/// <para>
/// ⚠️ <b>只外置数值，不外置结构</b>：部位名（<see cref="HumanBody.Chest"/> 等 const）、所属 Region/MacroRegion/Category
/// 分类、父子拓扑（切除连带的树形）都是<b>结构</b>，仍写死在 <see cref="HumanBody"/> 里。此段只提供两个可调数字
/// （<see cref="BodyPartStats.VolumeWeight"/>/<see cref="BodyPartStats.MaxHp"/>）与三个惩罚系数。
/// </para>
/// <para>
/// 📐 照 <see cref="WeaponConfig"/> 范式：段类自报 <see cref="FileName"/> + 自解析 <see cref="FromJson"/>，
/// json 为裸载荷。<see cref="CombatConfig"/> 挂一行、<see cref="CombatConfigLoader.Parse"/> 反射自动加载。
/// </para>
/// </summary>
public sealed class BodyConfig : IConfigSection
{
    /// <summary>部位名 → 该部位的可调数字（体积权重 + 最大 HP）。body.json 的 <c>parts</c> 子树。</summary>
    public IReadOnlyDictionary<string, BodyPartStats> Parts { get; init; } = new Dictionary<string, BodyPartStats>();

    /// <summary>残疾能力惩罚系数（单肢/每指/每趾）。body.json 的 <c>disability</c> 子树。</summary>
    public BodyDisability Disability { get; init; } = new();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "body.json";

    /// <inheritdoc/>
    public IConfigSection FromJson(string json, JsonSerializerOptions options)
    {
        var cfg = JsonSerializer.Deserialize<BodyConfig>(json, options)
            ?? throw new InvalidOperationException("body.json 反序列化为空（fail-fast）。");
        return cfg;
    }

    /// <summary>按部位名取可调数字，缺失 fail-fast（战斗配置不允许静默缺数据）。</summary>
    public BodyPartStats Part(string name)
    {
        if (!Parts.TryGetValue(name, out var s))
        {
            throw new KeyNotFoundException($"body.json 缺部位「{name}」（战斗配置 fail-fast）。");
        }
        return s;
    }
}

/// <summary>单个部位的可调数字（结构性字段——名/分类/拓扑——仍在 <see cref="HumanBody"/> 代码里）。</summary>
public sealed class BodyPartStats
{
    /// <summary>体积权重：命中按此加权随机分配。拟定待调。</summary>
    public double VolumeWeight { get; init; }

    /// <summary>最大 HP，同时是切除阈值（单次伤害 ≥ 此值 → 切除）。拟定待调。</summary>
    public double MaxHp { get; init; }
}

/// <summary>残疾能力惩罚系数（乘算通则的一部分，见 <see cref="Body.RecalculatePenalties"/>）。</summary>
public sealed class BodyDisability
{
    /// <summary>单手/单腿失去对应的能力惩罚，具体比例见 Wiki 配置表。</summary>
    public double SingleLimbPenalty { get; init; }

    /// <summary>未失去的手每失去一根手指对应的能力惩罚，具体比例见 Wiki 配置表。</summary>
    public double FingerPenalty { get; init; }

    /// <summary>未失去的脚每失去一根脚趾对应的能力惩罚，具体比例见 Wiki 配置表。</summary>
    public double ToePenalty { get; init; }
}
