using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Tests / WikiExtract 以 Link 编入；godot 运行时直接编译）。

/// <summary>
/// 武器改装数值段：<c>weaponmods.json</c>——20 条改装的**可调数值真源**（原散在
/// <see cref="WeaponModCatalog"/> 各工厂里的 StatMod 乘子/加值、<c>WeightMultiplier</c>、防御否决几率、
/// 以及近战型态攻速折扣 <see cref="MeleeFormSpeed"/>）。
/// <para>
/// 🔴 <b>只搬「可调数字」，不搬结构</b>：一条 StatMod 的<b>运算方式</b>（Mul/Add/Set）、它作用于<b>哪个</b>
/// <c>WeaponStat</c>、以及每条改装的<b>适配武器白名单</b>/<c>WeaponPart</c> 归类/<c>Id</c> ——全是**结构**，
/// 仍写死在 <see cref="WeaponModCatalog"/>。本段只承载那些「用户会在 wiki 上调」的**乘子/加值/几率/角度**。
    /// 例：轻质化枪托的散布乘子——<c>StatMod.Mul(BaseSpreadDegrees, …)</c> 的 <c>Mul</c> 与
    /// <c>BaseSpreadDegrees</c> 留代码，具体乘子进本段。
/// </para>
/// <para>
/// ⚠️ <b>近战型态（刺刀/利爪/创伤/锋刃）的枪托五段数值不在本段</b>：它们是从 <see cref="WeaponTable"/> 实读的
    /// 「某个攻速档位的近战武器」（用户调那把刀，型态自动跟着变）——不是本 catalog 的字面量。本段只装那条链路上
    /// **唯一属于改装自己的可调数**：攻速折扣 <see cref="MeleeFormSpeed"/> 与各型态附带的散布乘子。
/// </para>
/// <para>
/// 📐 照 <see cref="NightWatchConfig"/> 的 godot 侧消费层范式（字典型子系统对齐纯库 <c>WeaponConfig</c>）：
/// 段自报 <see cref="FileName"/> + 自解析 <see cref="FromJson"/>，<see cref="GameConfigLoader"/> 反射自动发现。
/// init 默认值＝迁移前的原始字面量（proto 仅用于反射报出 <see cref="FileName"/>；运行时总被盘上 json 覆盖）。
/// 数值皆「拟定待调」。
/// </para>
/// </summary>
public sealed class WeaponModConfig : IGameConfigSection
{
    /// <summary>
    /// 近战型态攻速折扣：型态的枪托近战按该基准武器的倍速计算。具体值见 Wiki 配置表。
    /// [T68] 用户手改，刺刀/利爪/创伤/锋刃型态共用。
    /// </summary>
    public double MeleeFormSpeed { get; init; } = 0.85;

    /// <summary>改装 id → 该改装的可调数值。id 与 <see cref="WeaponModCatalog"/> 各工厂的 <c>Id</c> 一致。</summary>
    public IReadOnlyDictionary<string, WeaponModTuning> ById { get; init; }
        = new Dictionary<string, WeaponModTuning>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "weaponmods.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<WeaponModConfig>(json, options)
           ?? throw new InvalidOperationException("weaponmods.json 反序列化为空（fail-fast）。");

    /// <summary>取某条改装的可调数值；缺 id 直接抛（fail-fast，不软回落）。</summary>
    public WeaponModTuning Get(string id)
        => ById.TryGetValue(id, out var t)
            ? t
            : throw new InvalidOperationException($"weaponmods.json 缺改装「{id}」（fail-fast）。");

    /// <summary>取某条改装某个 StatMod 的**数值**（乘子/加值）；缺该 stat 直接抛。运算方式(Mul/Add/Set)不在此、留代码。</summary>
    public double Stat(string id, string statName)
        => Get(id).Stats.TryGetValue(statName, out var v)
            ? v
            : throw new InvalidOperationException($"weaponmods.json 改装「{id}」缺数值「{statName}」（fail-fast）。");
}

/// <summary>
/// 单条改装的可调数值（<b>纯数字，无结构</b>）。字段皆可选：某条改装不改重量 ⇒ 不写 <see cref="WeightMultiplier"/>
/// （默认 1.0）；不带防御否决 ⇒ 不写那三个字段（默认 0，恒短路）。<see cref="Stats"/> 只列该改装真正带的 StatMod
/// 数值（键 = <c>WeaponStat</c> 枚举名，值 = 乘子/加值——运算方式留代码）。
/// </summary>
public sealed class WeaponModTuning
{
    /// <summary>整把武器的重量倍率，具体值见 Wiki 配置表。不改重量的改装省略此键。</summary>
    public double WeightMultiplier { get; init; } = 1.0;

    /// <summary>该改装带的 StatMod 数值：<c>WeaponStat</c> 枚举名 → 乘子/加值。运算方式(Mul/Add/Set)与作用字段皆结构、留代码。</summary>
    public IReadOnlyDictionary<string, double> Stats { get; init; } = new Dictionary<string, double>();

    /// <summary>护手挡格：武器手受击时整发否决几率（0＝无此效果）。</summary>
    public double HandGuardNegateChance { get; init; } = 0.0;

    /// <summary>弩盾：正面锥内远程整发否决几率（0＝无此效果）。</summary>
    public double FrontalRangedNegateChance { get; init; } = 0.0;

    /// <summary>弩盾正面锥半角（度），全张角 = 2×此值。默认 60（正面 120°）。</summary>
    public double FrontalNegateHalfAngleDeg { get; init; } = 60.0;
}
