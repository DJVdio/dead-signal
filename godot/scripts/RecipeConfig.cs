using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Sim/Tests/WikiExtract 以 Link 编入）。

/// <summary>
/// 一张配方的<b>可调数值</b>（外置到 <c>recipes.json</c> 的值单元）。
/// <para>
/// 🔴 <b>只装"数字/成本"，不装配方结构</b>：产物 <b>数量</b> <see cref="OutputQuantity"/>、<b>工时</b>
/// <see cref="WorkMinutes"/>、<b>材料成本</b> <see cref="MaterialCosts"/>（键=吃什么料，量=吃多少）。
/// 配方的<b>身份/结构</b>——id、显示名、类别、产物键（<c>OutputKey</c>）、工具槽、书门槛、制作者门槛——
/// 仍留在 <see cref="RecipeBook"/> 代码里（它们决定"这是哪条配方"，不是"这条配方调多重"）。
/// </para>
/// </summary>
public sealed class RecipeNumbers
{
    /// <summary>一次制作产出的<b>数量</b>（对齐 <c>RecipeData.OutputQuantity</c>）。</summary>
    public int OutputQuantity { get; init; }

    /// <summary>每配方<b>工时</b>（游戏分钟，对齐 <c>RecipeData.WorkMinutes</c>）。</summary>
    public int WorkMinutes { get; init; }

    /// <summary>
    /// <b>材料成本</b>：材料 RefKey → 数量（对齐 <c>RecipeData.MaterialCosts</c>）。
    /// 键=吃什么料、量=吃多少；"吃什么料"随量一并外置，因为一份成本脱了键无从调（供设计者按料名改量）。
    /// </summary>
    public IReadOnlyDictionary<string, int> MaterialCosts { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// 配方数值段：<c>recipes.json</c>（id → <see cref="RecipeNumbers"/>）。
/// <para>
/// 📐 照 config-consumer-pilot 立的 godot 侧配置范式（对应纯库的 <c>WeaponConfig</c>）：id→POCO 字典型子系统，
/// <see cref="ById"/> 装裸字典、<see cref="Get"/> 缺 id fail-fast。<see cref="RecipeBook"/> 各配方身体读
/// <c>GameConfigCatalog.Section&lt;RecipeConfig&gt;().Get(id)</c> 取数字，配方结构仍写死在代码。
/// </para>
/// <para>
/// 数值皆「拟定待调」——制作成本/产量/工时由设计校准，改这里即改配方数值，不动代码。缺配置游戏 fail-fast。
/// </para>
/// </summary>
public sealed class RecipeConfig : IGameConfigSection
{
    /// <summary>id → 配方数值。recipes.json 顶层就是这张裸字典。</summary>
    public IReadOnlyDictionary<string, RecipeNumbers> ById { get; init; } = new Dictionary<string, RecipeNumbers>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "recipes.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
    {
        var byId = JsonSerializer.Deserialize<Dictionary<string, RecipeNumbers>>(json, options)
            ?? throw new InvalidOperationException("recipes.json 反序列化为空（fail-fast）。");
        return new RecipeConfig { ById = byId };
    }

    /// <summary>按配方 id 取数值，缺失 fail-fast（配方数值不允许静默缺数据）。</summary>
    public RecipeNumbers Get(string id)
    {
        if (!ById.TryGetValue(id, out var n))
        {
            throw new KeyNotFoundException($"recipes.json 缺配方 id「{id}」（配方数值 fail-fast）。");
        }
        return n;
    }
}
