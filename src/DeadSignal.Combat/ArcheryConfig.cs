using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Combat;

/// <summary>
/// 弓弩数值段：<c>archery.json</c>。<b>照 <see cref="WeaponConfig"/> 范式</b>，但载荷不是一张裸字典——
/// 它同时装两类数据：①<see cref="Arrows"/>（箭定义 <see cref="ArrowDef"/>，<c>materialKey → 箭</c>）；
/// ②一组<b>射手/弓弩全局可调常量</b>（穿透封顶、散布下限、箭矢回收率、《弓与箭之道》三项被动加成）。
/// <para>
/// 🔴 <b>弓/弩本身不在这里</b>——那 8 把是 <see cref="Weapon"/>，早已随 weapons.json 外置（config-skeleton 单）。
/// 本段只装「箭的数据」与「Combine 用到的可调标量」；<see cref="Archery.Combine"/> 的<b>纯函数逻辑不外置</b>（箭反写弓的规则形态）。
/// </para>
/// <para>
/// <b>派生量不入表</b>：<c>BookCooldownMult = 1/BookAttackSpeedMult</c> 是攻速的倒数，由 <see cref="Archery"/> 现算，
/// 不落 json（避免两处真源打架）。
/// </para>
/// </summary>
public sealed class ArcheryConfig : IConfigSection
{
    // ── 全局可调标量（Combine 与回收率读它）──────────────────────────────
    /// <summary>穿透 clamp 上限（挡住「复合弩 × 重头箭」叠乘失控）。</summary>
    public double MaxPenetration { get; init; }

    /// <summary>散布角 clamp 下限（度）——不许任何 弓×箭 组合变「绝对精准」。</summary>
    public double MinSpreadDegrees { get; init; }

    /// <summary>箭矢回收率·基础，具体值由 Wiki 配置提供。</summary>
    public double BaseArrowRecoveryRate { get; init; }

    /// <summary>箭矢回收率·读过《弓与箭之道》后，具体值由 Wiki 配置提供。</summary>
    public double SkilledArrowRecoveryRate { get; init; }

    /// <summary>《弓与箭之道》·射程加成（[T68] 已中和，具体值由 Wiki 配置提供）。</summary>
    public double BookRangeMult { get; init; }

    /// <summary>《弓与箭之道》·弹道速度加成，具体值由 Wiki 配置提供。</summary>
    public double BookFlightSpeedMult { get; init; }

    /// <summary>《弓与箭之道》·散布（锥形角）加成，具体值由 Wiki 配置提供。</summary>
    public double BookSpreadMult { get; init; }

    /// <summary>《弓与箭之道》·攻速加成；出手间隔的倒数在 <see cref="Archery.BookCooldownMult"/> 现算。</summary>
    public double BookAttackSpeedMult { get; init; }

    /// <summary>4 种箭：材料键 → 箭定义。archery.json 里 <c>Arrows</c> 这张字典。</summary>
    public IReadOnlyDictionary<string, ArrowDef> Arrows { get; init; } = new Dictionary<string, ArrowDef>();

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "archery.json";

    /// <inheritdoc/>
    public IConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<ArcheryConfig>(json, options)
           ?? throw new InvalidOperationException("archery.json 反序列化为空（fail-fast）。");

    /// <summary>按材料键取一种箭，缺失 fail-fast（战斗配置不允许静默缺数据）。</summary>
    public ArrowDef Arrow(string key)
    {
        if (!Arrows.TryGetValue(key, out var a))
        {
            throw new KeyNotFoundException($"archery.json 缺箭 id「{key}」（战斗配置 fail-fast）。");
        }
        return a;
    }
}
