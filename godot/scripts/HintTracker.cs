using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 StoryFlags.cs / GoldfingerDiscovery.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「首次触发一行提示」框架（用户拍板：不做教程关，重系统首次接触时弹一条可关闭的单行提示，不破坏硬核氛围）。
//   · 每个重系统一条封顶（宁缺毋滥）；文案指向"下一步怎么操作"，不解释大道理。
//   · 一次性：靠 StoryFlags 背书 `hint_*` 旗标持久去重（存档随 StoryFlags 一并走），第二次触发不再弹。
//   · 本类纯判定/取文案（无副作用）：ShouldShow 只读、MarkShown 才写 flag，由 CampMain 在弹完 toast 后调用 MarkShown。
// ★系统只提供"按条件播一次 authored 文案"的框架；文案本身是 draft 草稿，最终由用户改（见下表注释）。

/// <summary>
/// 首次触发提示的一次性判定 + 数据驱动提示表。<see cref="ShouldShow"/> 由 CampMain 在各系统首次事件处调用，
/// 为真则用 <c>CampToast</c> 弹 <see cref="Text"/> 的一行文案、随后调 <see cref="MarkShown"/> 落 flag 去重。
/// flag 键为 <c>hint_&lt;id&gt;</c>，持久随 <see cref="StoryFlags"/> 存档。未知 id 一律不弹（ShouldShow=false、Text=null）。
/// </summary>
public static class HintTracker
{
    // —— 提示 id（每个重系统首次接触一条）——
    /// <summary>首次有人受伤。</summary>
    public const string Injury = "injury";
    /// <summary>首次出现伤口感染。</summary>
    public const string Infection = "infection";
    /// <summary>首次出现骨折。</summary>
    public const string Fracture = "fracture";
    /// <summary>首次下单制作。</summary>
    public const string CraftOrder = "craft_order";
    /// <summary>首次入夜（昼→夜相变）。</summary>
    public const string Nightfall = "nightfall";
    /// <summary>首次排守卫岗。</summary>
    public const string GuardShift = "guard_shift";
    /// <summary>首次神秘商人来访。</summary>
    public const string Merchant = "merchant";
    /// <summary>首次开始读书。</summary>
    public const string Reading = "reading";
    /// <summary>首次开挖营地废墟（批次9，camp-rubble 接线）。</summary>
    public const string RubbleDig = "rubble_dig";

    /// <summary>某提示 id 对应的持久去重 flag 键（<c>hint_&lt;id&gt;</c>）。</summary>
    public static string FlagFor(string hintId) => "hint_" + hintId;

    /// <summary>
    /// 提示表：id → 一行文案。**全部为 draft 草稿（供用户改）**，每条一行、指向"下一步操作"而非解释机制。
    /// 文案与当前规则对齐：M=医疗面板；感染/骨折均在医疗面板处置（感染用抗生素、骨折做手术）；
    /// 制作按工时、夜间有人守台才推进；夜间视野收窄、光源扩野但暴露；守卫警戒力 vs 袭击者潜行力；
    /// 商人清晨离开、收购按六折（<c>MerchantTrade.SellRatePercent</c>=60）；读书解锁配方、缺前置读速极慢。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Table = new Dictionary<string, string>
    {
        // draft 待用户改 —— 首次有人受伤
        [Injury] = "有人受伤了。按 M 打开医疗面板，查看伤情。",
        // draft 待用户改 —— 首次感染
        [Infection] = "伤口感染了，会逐日恶化。用抗生素在医疗面板处置。",
        // draft 待用户改 —— 首次骨折（未治为畸形愈合永久致残，不是固定百分比减益，故不写具体数值）
        [Fracture] = "骨折不会自愈，要在医疗面板做手术；久拖会畸形愈合、永久致残。",
        // draft 待用户改 —— 首次下单制作
        [CraftOrder] = "已下单。制作要耗工时，得夜里有人守在工作台才会推进。",
        // draft 待用户改 —— 首次入夜
        [Nightfall] = "入夜后视野收窄。点亮光源能看得更远，但也会把自己暴露出去。",
        // draft 待用户改 —— 首次排岗
        [GuardShift] = "守卫的警戒力，对抗袭击者的潜行力——排班越强，越不容易被摸营。",
        // draft 待用户改 —— 首次商人来访
        [Merchant] = "神秘商人清晨就会离开。他收你的东西，只按六折算。",
        // draft 待用户改 —— 首次读书
        [Reading] = "读书能解锁配方。缺少前置书时，这本会读得非常慢。",
        // draft 待用户改 —— 首次开挖营地废墟（批次9）
        [RubbleDig] = "选中一名幸存者，右键点废墟就能开挖。挖净能腾出空地、翻出材料。",
    };

    /// <summary>提示表里的全部 id（调试/测试用；顺序即声明序）。</summary>
    public static IReadOnlyCollection<string> AllIds => (IReadOnlyCollection<string>)Table.Keys;

    /// <summary>取某提示 id 的一行文案；未知 id 返回 <c>null</c>。</summary>
    public static string? Text(string hintId) =>
        hintId != null && Table.TryGetValue(hintId, out var t) ? t : null;

    /// <summary>
    /// 该提示此刻是否应弹：id 在表内 **且** 尚未示过（<c>hint_&lt;id&gt;</c> flag 未置）。只读、无副作用；
    /// 未知 id 或已示过均返回 <c>false</c>。<paramref name="flags"/> 为 null 视作"从未示过"（仍要求 id 在表内）。
    /// </summary>
    public static bool ShouldShow(string hintId, StoryFlags flags)
    {
        if (hintId == null || !Table.ContainsKey(hintId))
            return false;
        return flags == null || !flags.Has(FlagFor(hintId));
    }

    /// <summary>
    /// 记录某提示已示（置 <c>hint_&lt;id&gt;</c> flag）。幂等：重复调用无碍。未知 id 不写（避免污染 flag 空间）。
    /// 由调用方在弹完 toast 后调用；<paramref name="flags"/> 为 null 时空操作。
    /// </summary>
    public static void MarkShown(string hintId, StoryFlags flags)
    {
        if (flags == null || hintId == null || !Table.ContainsKey(hintId))
            return;
        flags.Set(FlagFor(hintId), "true");
    }
}
