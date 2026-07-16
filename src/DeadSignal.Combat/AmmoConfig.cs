using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Combat;

/// <summary>
/// 弹药数值段：<c>ammo.json</c>（弹药键 → <see cref="AmmoDef"/>）。
/// <para>
/// 📐 照 <see cref="WeaponConfig"/> 立的范式抄成——段类自报 <see cref="FileName"/> + 自解析 <see cref="FromJson"/>，
/// 加载器/宿主接线全不动（<see cref="CombatConfigLoader.Parse"/> 反射自动发现）。
/// </para>
/// <para>
/// 🔴 <b>只收四种子弹</b>（短/中/长/鹿弹）——它们是「1 个子弹零件 → N 发」的可造弹药。
/// 箭（<see cref="AmmoKeys.Arrow"/>）是<b>类别键、不吃子弹零件</b>，<b>不进本文件</b>；查它的产出走
/// <see cref="YieldPerBulletPart"/> 的缺省 0（＝迁移前 <c>BulletParts.YieldPer</c> 的 <c>_ =&gt; 0</c>，零漂移）。
/// 弹药键（<c>ammo_short</c> 等）是 json 的裸字典键，仍是引擎里的 <see cref="AmmoKeys"/> <c>const string</c>（标识符不外置）。
/// </para>
/// </summary>
public sealed class AmmoConfig : IConfigSection
{
    /// <summary>弹药键 → 弹药数值。ammo.json 顶层就是这张裸字典。</summary>
    public IReadOnlyDictionary<string, AmmoDef> ById { get; init; } = new Dictionary<string, AmmoDef>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "ammo.json";

    /// <inheritdoc/>
    public IConfigSection FromJson(string json, JsonSerializerOptions options)
    {
        var byId = JsonSerializer.Deserialize<Dictionary<string, AmmoDef>>(json, options)
            ?? throw new InvalidOperationException("ammo.json 反序列化为空（fail-fast）。");
        return new AmmoConfig { ById = byId };
    }

    /// <summary>按弹药键取数值，缺失 fail-fast（战斗配置不允许静默缺数据）。</summary>
    public AmmoDef Get(string ammoKey)
    {
        if (!ById.TryGetValue(ammoKey, out var d))
        {
            throw new KeyNotFoundException($"ammo.json 缺弹药键「{ammoKey}」（战斗配置 fail-fast）。");
        }
        return d;
    }

    /// <summary>
    /// 1 个子弹零件能造出多少发该弹药；<b>非子弹弹药（箭）或未知键返回 0</b>——
    /// 保留迁移前 <c>BulletParts.YieldPer</c> 的 <c>_ =&gt; 0</c> 语义（故此处**故意宽松**、不 fail-fast）。
    /// </summary>
    public int YieldPerBulletPart(string ammoKey) =>
        ById.TryGetValue(ammoKey, out var d) ? d.YieldPerBulletPart : 0;
}
