using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs / VisionLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 光源数据 + 光照贡献查询：批次4「光照与视野」的光源侧。
// 本文件只出「某位置能收到多强的光源贡献」(source contribution ∈ [0,1]) 与「持光源者的暴露代价」，
// **不**做环境光/相位映射(那是 VisionLogic.AmbientLight)、**不**合成局部光照(消费方自行
// VisionLogic.CombineLight(ambient, 本处贡献) 求 max)——故不反向依赖 vision-core，落地顺序解耦。
// 空间执行(Light2D/光晕/遮挡)归 Godot 运行时层，本类只出纯函数。数值皆「拟定待调」占位。

/// <summary>光源种类：手持(随持有者移动) / 固定(营地火堆·灯、关卡预置灯，位置恒定)。</summary>
public enum LightKind
{
    /// <summary>手持：手电 / 火把，占持有者一只手(见 <see cref="HeldLightState"/>)，随之移动。</summary>
    Handheld,

    /// <summary>固定：营地火堆 / 油灯、关卡预置灯，位置恒定(camp.json / 关卡数据落点)。</summary>
    Fixed,
}

/// <summary>手持光源的消耗轴：固定光源无限；手电吃电池；火把按燃烧耐久耗尽。</summary>
public enum LightFuelKind
{
    /// <summary>固定光源，永不耗尽。</summary>
    None,

    /// <summary>手电的可更换电池（当前以单件光源的剩余秒数表示）。</summary>
    Battery,

    /// <summary>火把的燃烧耐久（耗尽后仍占手，但不再发光）。</summary>
    Durability,
}

/// <summary>
/// 一类光源的静态属性(不可变值对象，拟定待调)。<see cref="Intensity"/> = 光源中心处贡献的局部光照等级
/// (∈[0,1]，供 VisionLogic.CombineLight 与环境光求 max)；<see cref="Radius"/> = 有效照亮半径(世界像素)，
/// 半径外贡献归 0。<see cref="Key"/> 是光源标识键(对齐 <see cref="LightCatalog"/> / Item RefKey / camp.json lights.key)。
/// </summary>
public readonly record struct LightProfile(
    string Key,
    string DisplayName,
    float Intensity,
    float Radius,
    LightKind Kind,
    string Description = "",
    LightFuelKind FuelKind = LightFuelKind.None,
    double ActiveSeconds = 0);

/// <summary>
/// 一件手持光源的运行时消耗状态。
/// <para>
/// 规则只记录“点亮了多久”，不偷偷把实时秒换成现实分钟：调用方传入已受游戏时标缩放的 delta，
/// 所以暂停不耗、3x/8x 会按游戏速度消耗。固定光源 <see cref="LightFuelKind.None"/> 永不耗尽。
/// </para>
/// </summary>
public sealed class LightChargeState
{
    public LightFuelKind FuelKind { get; }
    public double CapacitySeconds { get; }
    public double RemainingSeconds { get; private set; }

    public bool IsFinite => FuelKind != LightFuelKind.None && CapacitySeconds > 0;
    public bool IsDepleted => IsFinite && RemainingSeconds <= 0;
    public bool IsLit => !IsFinite || RemainingSeconds > 0;

    public LightChargeState(LightProfile profile, double? remainingSeconds = null)
    {
        FuelKind = profile.FuelKind;
        CapacitySeconds = Math.Max(0, profile.ActiveSeconds);
        RemainingSeconds = IsFinite
            ? Math.Clamp(remainingSeconds ?? CapacitySeconds, 0, CapacitySeconds)
            : 0;
    }

    /// <summary>消耗已缩放的游戏秒；返回本次是否刚好从有光变为耗尽。</summary>
    public bool Consume(double gameSeconds)
    {
        if (!IsFinite || gameSeconds <= 0 || double.IsNaN(gameSeconds))
            return false;

        bool wasLit = RemainingSeconds > 0;
        RemainingSeconds = Math.Max(0, RemainingSeconds - gameSeconds);
        return wasLit && RemainingSeconds <= 0;
    }
}

/// <summary>一盏落在世界某处的光源实例(固定光源的坐标，或某帧手持光源持有者的坐标) + 其 <see cref="LightProfile"/>。</summary>
public readonly record struct PlacedLight(float X, float Y, LightProfile Profile);

/// <summary>
/// 光源数据目录 + 光照贡献纯函数。<see cref="Find"/> 按键取 profile；<see cref="ContributionAt"/> 出单盏按距离衰减；
/// <see cref="StrongestContribution"/> 是给 vision-render / enemy-vision 的**查询入口**：给定位置返回最强光源贡献。
/// </summary>
public static class LightSource
{
    // ——光源标识键(对齐 Item.Light 的 RefKey / camp.json lights.key / 火把配方 OutputKey)——
    /// <summary>手电：拾取/投放获得的手持光源，聚焦亮束；默认一块电池可亮 3 个夜晚(1440 游戏秒)。</summary>
    public const string FlashlightKey = "flashlight";

    /// <summary>火把：可制作的手持光源(见 RecipeBook「火把」)，暖光、射程略短；一根可燃烧 1 个夜晚(480 游戏秒)。</summary>
    public const string TorchKey = "torch";

    /// <summary>默认夜晚长度，与 <c>godot/data/daynight.json</c> 的 480 秒一致；仅作为光源初始耐久档。</summary>
    public const double DefaultNightSeconds = 480;

    /// <summary>手电一块电池的初始可用时长（3 个默认夜晚）。</summary>
    public const double FlashlightBatterySeconds = DefaultNightSeconds * 3;

    /// <summary>火把一根的燃烧时长（1 个默认夜晚）。</summary>
    public const double TorchDurabilitySeconds = DefaultNightSeconds;

    /// <summary>油灯：营地/关卡固定光源，中范围暖光。</summary>
    public const string LampKey = "lamp";

    /// <summary>火堆：营地固定光源，大范围强光。</summary>
    public const string CampfireKey = "campfire";

    // draft：以下强度/半径均为占位草稿，最终由 Sim/用户目视校准(对齐 SPEC-B4 白天满档、黑暗~0.35 环境光的量级)。
    private static readonly IReadOnlyDictionary<string, LightProfile> _byKey = ToMap(new[]
    {
        new LightProfile(FlashlightKey, "手电", 0.90f, 340f, LightKind.Handheld, "让光成为手臂的延伸。", LightFuelKind.Battery, FlashlightBatterySeconds),
        new LightProfile(TorchKey, "火把", 0.50f, 240f, LightKind.Handheld, "黑暗中的唯一慰藉，祈祷他不要灭掉。", LightFuelKind.Durability, TorchDurabilitySeconds),   // 亮度 T21 用户手改：0.70 → 0.50
        new LightProfile(LampKey, "油灯", 0.75f, 300f, LightKind.Fixed, "挂在营地里的油灯，照亮一小片起居，也照亮「我们还没散伙」这件事。"),
        new LightProfile(CampfireKey, "火堆", 0.95f, 460f, LightKind.Fixed, "驱散黑暗、烤暖人心。"),
    });

    private static IReadOnlyDictionary<string, LightProfile> ToMap(IReadOnlyList<LightProfile> list)
    {
        var d = new Dictionary<string, LightProfile>();
        foreach (var p in list)
        {
            d[p.Key] = p;
        }
        return d;
    }

    /// <summary>按键查一类光源；查不到返回 <c>null</c>。</summary>
    public static LightProfile? Find(string key)
        => key != null && _byKey.TryGetValue(key, out var p) ? p : null;

    /// <summary>该键是否为已登记的光源。</summary>
    public static bool Has(string key) => key != null && _byKey.ContainsKey(key);

    /// <summary>
    /// 单盏光源在距其 <paramref name="distance"/>(世界像素)处的贡献光照等级 ∈[0,<see cref="LightProfile.Intensity"/>]。
    /// 衰减曲线**委托 vision-core 的单一真源** <see cref="VisionLogic.SourceContribution"/>(中心满/半径外0/线性)，
    /// 避免两套曲线；本类只提供「按 profile 取参」的便捷包装。
    /// </summary>
    public static float ContributionAt(LightProfile profile, float distance)
        => VisionLogic.SourceContribution(profile.Intensity, distance, profile.Radius);

    /// <summary>单盏光源对 (<paramref name="px"/>,<paramref name="py"/>) 的贡献(内部算欧氏距离后走 <see cref="ContributionAt"/>)。</summary>
    public static float ContributionAt(PlacedLight light, float px, float py)
    {
        float dx = light.X - px;
        float dy = light.Y - py;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        return ContributionAt(light.Profile, dist);
    }

    /// <summary>
    /// **查询入口**(vision-render / enemy-vision 调用)：给定位置返回一批光源里最强的一盏贡献 ∈[0,1]。
    /// 无光源或全在半径外 → 0。消费方再 VisionLogic.CombineLight(环境光, 本返回值) 得该处局部光照等级 L。
    /// </summary>
    public static float StrongestContribution(float px, float py, IEnumerable<PlacedLight> lights)
    {
        float best = 0f;
        if (lights == null)
        {
            return best;
        }
        foreach (var light in lights)
        {
            float c = ContributionAt(light, px, py);
            if (c > best)
            {
                best = c;
            }
        }
        return best;
    }

    /// <summary>
    /// 持光源者的**暴露代价**便捷包装(SPEC-B4：持光者黑暗中被发现距离提升)：返回敌方感知距离放大倍数(≥1)。
    /// 数学**委托 vision-core 单一真源** <see cref="VisionLogic.ExposureRangeMultiplier"/>(白天≈1、越黑越亮越大)，
    /// 本类只做「从 profile/键取光强」的取参。<paramref name="held"/>=null(未持光) → 1。
    /// enemy-vision 也可直接调 <see cref="VisionLogic.ExposureRangeMultiplier"/>(ambient, HeldLightState.Held.Intensity)。
    /// </summary>
    public static float ExposureDetectionMultiplier(LightProfile? held, float ambient)
        => held is null ? 1f : VisionLogic.ExposureRangeMultiplier(ambient, held.Value.Intensity);

    /// <summary>便捷重载：只知「是否持某键光源」时用(键无效则按未持光)。</summary>
    public static float ExposureDetectionMultiplier(string? heldKey, float ambient)
        => ExposureDetectionMultiplier(heldKey is not null ? Find(heldKey) : null, ambient);
}
