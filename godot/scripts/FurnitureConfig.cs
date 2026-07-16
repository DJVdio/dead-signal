using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（被 DeadSignal.Combat.Tests 与 WikiExtract 以 Link 编入，与 FurnitureBuildCost.cs 同宿主）。

/// <summary>
/// 家具建造数值段：<c>furniture.json</c>（家具键 → <see cref="FurnitureCost"/>＝建造材料清单 + 建造工时）。
/// <para>
/// 📐 照 <see cref="NightWatchConfig"/> 立的消费层配置范式抄成的平行段类——因是「id → POCO 字典」型子系统，
/// 载荷用 <c>ById</c> 字典 + <see cref="Get"/> fail-fast（参照纯库 <c>WeaponConfig</c>/<c>ArmorConfig</c>）。
/// 迁移三处改动：①<see cref="FileName"/>；②字典值类型换成 <see cref="FurnitureCost"/>；③<see cref="FromJson"/>
/// 反序列化目标类型。接口/加载器/宿主接线全不动（<see cref="GameConfigLoader.Parse"/> 反射自动发现本段）。
/// </para>
/// <para>
/// ⚠️ <b>只装数值、不装 authored 文案</b>：本段只搬 <see cref="FurnitureBuildCost"/> 的<b>建造成本数量 + 工时</b>；
/// 每件家具的<b>简介文案（Description）是 authored 内容</b>，留在 <c>FurnitureBuildCost.cs</c> 不外置——它已单独
/// 接线进 wiki「简介」列（与武器/护甲/书籍同口径，玩家可改、agent 手动同步回代码）。
/// </para>
/// </summary>
public sealed class FurnitureConfig : IGameConfigSection
{
    /// <summary>家具键 → 建造成本 + 工时。furniture.json 顶层就是这张裸字典（键 = camp.json prop 名 = 各 Spec.FurnitureKey）。</summary>
    public IReadOnlyDictionary<string, FurnitureCost> ById { get; init; } = new Dictionary<string, FurnitureCost>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "furniture.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
    {
        var byId = JsonSerializer.Deserialize<Dictionary<string, FurnitureCost>>(json, options)
            ?? throw new InvalidOperationException("furniture.json 反序列化为空（fail-fast）。");
        return new FurnitureConfig { ById = byId };
    }

    /// <summary>按家具键取建造数值，缺失 fail-fast（代码登记了该家具但 json 缺数据＝配置错，不静默）。</summary>
    public FurnitureCost Get(string key)
    {
        if (!ById.TryGetValue(key, out var c))
        {
            throw new KeyNotFoundException($"furniture.json 缺家具键「{key}」（消费层配置 fail-fast）。");
        }
        return c;
    }
}

/// <summary>一件家具的建造数值：材料清单（材料键→数量，对齐 <c>Materials</c> 目录）+ 建造工时（游戏分钟）。</summary>
public sealed record FurnitureCost(IReadOnlyDictionary<string, int> Cost, int BuildMinutes);
