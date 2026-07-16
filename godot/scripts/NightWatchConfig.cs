using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（被 Sim/Tests 以 Link 编入）。

/// <summary>
/// 夜防对抗数值段：<c>nightwatch.json</c>——袭击者<b>潜行力权重</b>（数值真源）。
/// <para>
/// 📐 <b>这是消费层 config 迁移单的照抄范式</b>（对应纯库的 <c>WeaponConfig</c>）：后续 hunger/recipes/…
/// 各建一个平行段类，改三处：①<see cref="FileName"/> 换自己的 json；②字段换成自己的数值；③<see cref="FromJson"/>
/// 里换反序列化目标类型。其余（接口、加载器、宿主接线）全不动。
/// </para>
/// <para>
/// ⚠️ <b>只搬数值、不搬 authored 结构</b>：本段装 <see cref="NightWatchContest"/> 的<b>潜行力权重标量</b>
/// （原 5 个 <c>public const float</c>）+ <b>警戒力权重与未发现后果标量</b>（第二批：视力/听力权重、听力半径、先手乘数、静默偷窃上下限）。
/// <c>ApparelStealth</c> 那张<b>逐件带风味注释的服饰潜行表是 authored 内容</b>，
/// <b>留在 NightWatchContest.cs 不外置</b>（外置会把手写风味与数值拆散、后续单照抄时也易误搬 authored 结构）。
/// </para>
/// <para>
/// init 默认值＝迁移前的原始常量（proto 只用于反射报出 <see cref="FileName"/>；运行时总被 <see cref="FromJson"/>
/// 加载的 json 值覆盖）。数值皆「拟定待调」，由 Sim/目视校准。
/// </para>
/// </summary>
public sealed class NightWatchConfig : IGameConfigSection
{
    /// <summary>黑暗项权重：局部光照越低越隐蔽（贡献 = 权重×(1-L)）。</summary>
    public float StealthDarknessWeight { get; init; } = 1.0f;

    /// <summary>服饰项权重：服饰潜行值合计的放大系数。</summary>
    public float StealthApparelWeight { get; init; } = 1.0f;

    /// <summary>距离项权重：越远越难被察觉。</summary>
    public float StealthDistanceWeight { get; init; } = 0.6f;

    /// <summary>距离项归一参考（世界像素）：距离≥此值时距离项达满档。</summary>
    public float StealthDistanceReference { get; init; } = 300f;

    /// <summary>遮蔽项权重：视野遮蔽物权重 [0,1] 的放大系数。</summary>
    public float StealthCoverWeight { get; init; } = 0.5f;

    // ── 警戒力权重 + 未发现后果（第二批外置：原 NightWatchContest.cs 的 6 个警戒/后果 const）─────
    /// <summary>视力项权重：满档清晰目击贡献的警戒量。</summary>
    public float VisionWeight { get; init; } = 1.0f;

    /// <summary>听力项权重：满档（贴身）听觉贡献的警戒量（弱于直接目视）。</summary>
    public float HearingWeight { get; init; } = 0.6f;

    /// <summary>听力基值半径（世界像素）：袭击者在此半径内可被"听见"，随距离线性衰减到 0。</summary>
    public float HearingBaseRange { get; init; } = 220f;

    /// <summary>杀戮意图潜行先手伤害乘数（用户口径 1.5x）。</summary>
    public float PreemptiveStrikeMultiplier { get; init; } = 1.5f;

    /// <summary>静默偷窃单位数下限（量级拟定）。</summary>
    public int SilentTheftMinUnits { get; init; } = 1;

    /// <summary>静默偷窃单位数上限（量级拟定）。</summary>
    public int SilentTheftMaxUnits { get; init; } = 4;

    /// <inheritdoc/>
    [JsonIgnore]
    public string FileName => "nightwatch.json";

    /// <inheritdoc/>
    public IGameConfigSection FromJson(string json, JsonSerializerOptions options)
        => JsonSerializer.Deserialize<NightWatchConfig>(json, options)
           ?? throw new InvalidOperationException("nightwatch.json 反序列化为空（fail-fast）。");
}
