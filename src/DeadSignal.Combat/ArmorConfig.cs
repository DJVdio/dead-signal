using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Combat;

/// <summary>
/// 护甲数值段：<c>armor.json</c>（id → <see cref="ArmorLayer"/>）。
/// <para>
/// 📐 照 <see cref="WeaponConfig"/> 立的范式抄成的平行段类——迁移单三处改动：①<see cref="FileName"/> 换成自己的 json；
/// ②载荷字典值类型换成 <see cref="ArmorLayer"/>；③<see cref="FromJson"/> 里换反序列化目标类型。接口/加载器/宿主接线全不动
/// （<see cref="CombatConfigLoader.Parse"/> 反射自动发现本段）。
/// </para>
/// </summary>
public sealed class ArmorConfig : IConfigSection
{
    /// <summary>id → 护甲层。armor.json 顶层就是这张裸字典。</summary>
    public IReadOnlyDictionary<string, ArmorLayer> ById { get; init; } = new Dictionary<string, ArmorLayer>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "armor.json";

    /// <inheritdoc/>
    public IConfigSection FromJson(string json, JsonSerializerOptions options)
    {
        var byId = JsonSerializer.Deserialize<Dictionary<string, ArmorLayer>>(json, options)
            ?? throw new InvalidOperationException("armor.json 反序列化为空（fail-fast）。");
        return new ArmorConfig { ById = byId };
    }

    /// <summary>按 id 取护甲层，缺失 fail-fast（战斗配置不允许静默缺数据）。</summary>
    public ArmorLayer Get(string id)
    {
        if (!ById.TryGetValue(id, out var a))
        {
            throw new KeyNotFoundException($"armor.json 缺护甲 id「{id}」（战斗配置 fail-fast）。");
        }
        return a;
    }
}
