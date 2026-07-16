using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Combat;

/// <summary>
/// 武器数值段：<c>weapons.json</c>（id → <see cref="Weapon"/>）。
/// <para>
/// 📐 <b>这就是后续 config 迁移单的照抄范式</b>——armor/ammo/archery/body 各建一个平行的段类
/// （<c>ArmorConfig</c>/<c>AmmoConfig</c>/…），改三处：①<see cref="FileName"/> 换成自己的 json；
/// ②载荷字典的值类型换成自己的 POCO；③<see cref="FromJson"/> 里换反序列化的目标类型。其余（接口、加载器、宿主接线）全不动。
/// </para>
/// </summary>
public sealed class WeaponConfig : IConfigSection
{
    /// <summary>id → 武器。weapons.json 顶层就是这张裸字典。</summary>
    public IReadOnlyDictionary<string, Weapon> ById { get; init; } = new Dictionary<string, Weapon>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "weapons.json";

    /// <inheritdoc/>
    public IConfigSection FromJson(string json, JsonSerializerOptions options)
    {
        var byId = JsonSerializer.Deserialize<Dictionary<string, Weapon>>(json, options)
            ?? throw new InvalidOperationException("weapons.json 反序列化为空（fail-fast）。");
        return new WeaponConfig { ById = byId };
    }

    /// <summary>按 id 取武器，缺失 fail-fast（战斗配置不允许静默缺数据）。</summary>
    public Weapon Get(string id)
    {
        if (!ById.TryGetValue(id, out var w))
        {
            throw new KeyNotFoundException($"weapons.json 缺武器 id「{id}」（战斗配置 fail-fast）。");
        }
        return w;
    }
}
