using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Tests / WikiExtract 以 Link 编入；godot 运行时直接编译）。

/// <summary>
/// 材料重量数值段：<c>materials.json</c>——材料重量（kg）的<b>数值真源</b>（原
/// <c>ItemRegistry.Materials</c> 那张 <c>Dictionary&lt;string,double&gt;</c> 字面表，47 条 id→double）。
/// <para>
/// 📐 <b>字典型子系统的照抄范式</b>（对应纯库的 <c>WeaponConfig</c>/<c>ArmorConfig</c>）：裸载荷 json 顶层即
/// key→weight 映射，<see cref="FromJson"/> 反序列化成 <see cref="IReadOnlyDictionary{TKey,TValue}"/> 装进
/// <see cref="Weights"/>；缺 json 由 <c>GameConfigLoader.Parse</c> fail-fast。
/// </para>
/// <para>
/// ⚠️ <b>只搬数值、不搬结构</b>：本段只装可调的<b>重量标量</b>。材料的<b>身份/类别/分类</b>
/// （<see cref="MaterialCategory"/> 枚举、<c>Component</c> 分类、材料 id 与显示名/描述）是 authored 结构，
/// 留在 <see cref="Materials"/>/<see cref="MaterialDef"/> 不外置。武器重量（<c>ItemRegistry.Weapons</c>）与
/// 护甲重量（派生自引擎 <c>ArmorTable</c>）不属本段，各归各处（武器重量仍在 <c>ItemRegistry.Weapons</c> 字面表）。
/// </para>
/// </summary>
public sealed class MaterialConfig : IGameConfigSection
{
    /// <summary>材料重量表（key = <see cref="MaterialDef.Key"/> 英文键，value = 重量 kg）。</summary>
    public IReadOnlyDictionary<string, double> Weights { get; init; } = new Dictionary<string, double>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "materials.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => new MaterialConfig
        {
            Weights = JsonSerializer.Deserialize<Dictionary<string, double>>(json, options)
                      ?? throw new InvalidOperationException("materials.json 反序列化为空（fail-fast）。"),
        };
}
