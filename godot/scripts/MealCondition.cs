using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「按条件播放用户写的台词」框架的**条件层**：
//   · MealWorldContext —— 喂给选择器的"世界只读快照"（相位/flags/角色状态/食物/士气）。
//   · PawnSnapshot     —— 单个角色供条件求值的**纯数据**状态（只读引擎真实状态，不发明新状态）。
//   · BubblePredicate / BubbleCondition —— json 里用户写的谓词与与/或组合。
//   · BubbleTrigger    —— 说完一句后要改的 flag。
// ★所有谓词只读引擎真实状态或用户自填的 flag/阈值；**无**任何关系/性格/好感类谓词。

/// <summary>
/// 单个角色供条件求值的只读状态快照——**纯数据**（不含 Godot/Body 引用），
/// 故条件求值可脱离 Godot 单测。字段皆映射引擎已有的真实状态（见 <see cref="PawnInspection"/>），
/// 不发明"剧痛/击晕"等引擎没有的状态。由 <see cref="FromInspection"/> 从检视快照拍出。
/// </summary>
public sealed class PawnSnapshot
{
    /// <summary>角色显示名（对应气泡 speaker，用于"某具名角色处于某状态"谓词）。</summary>
    public string Name { get; init; } = "";

    public bool IsDead { get; init; }
    public bool IsUnconscious { get; init; }
    public bool IsBlind { get; init; }

    /// <summary>饥饿刻度序号（0=饿死 … 5=正常 … 6=吃撑），同 <see cref="HungerState.Value"/>。</summary>
    public int HungerStage { get; init; } = (int)HungerLevel.Sated;

    /// <summary>存在被切除的手（断手）。</summary>
    public bool HasSeveredHand { get; init; }

    /// <summary>存在被切除的脚（断脚）。</summary>
    public bool HasSeveredFoot { get; init; }

    /// <summary>存在任意被切除/损毁的部位。</summary>
    public bool HasAnySevered { get; init; }

    /// <summary>存在失能部位（IsDisabled）。</summary>
    public bool HasDisabled { get; init; }

    /// <summary>存在骨折部位。</summary>
    public bool HasFractured { get; init; }

    /// <summary>存在流血伤口。</summary>
    public bool HasBleeding { get; init; }

    /// <summary>重度失血（血量档位 ≥ 中度，即失血过半，见 <see cref="BloodLossTier"/>）。</summary>
    public bool HasHeavyBloodLoss { get; init; }

    /// <summary>
    /// 由只读检视快照拍出条件求值用的角色状态。仅读 <see cref="PawnInspection"/> 已有字段/部位标记，
    /// 断手/断脚按部位区域（Hand/Foot）+ 切除标记归纳，绝不新增引擎没有的状态。
    /// </summary>
    public static PawnSnapshot FromInspection(PawnInspection insp) => new PawnSnapshot
    {
        Name = insp.DisplayName,
        IsDead = insp.IsDead,
        IsUnconscious = insp.IsUnconscious,
        IsBlind = insp.IsFullyBlind,
        HungerStage = insp.HungerStage,
        HasSeveredHand = insp.Parts.Any(p => p.Region == BodyRegion.Hand && (p.IsSevered || p.IsDestroyed)),
        HasSeveredFoot = insp.Parts.Any(p => p.Region == BodyRegion.Foot && (p.IsSevered || p.IsDestroyed)),
        HasAnySevered = insp.Parts.Any(p => p.IsSevered || p.IsDestroyed),
        HasDisabled = insp.Parts.Any(p => p.IsDisabled),
        HasFractured = insp.Parts.Any(p => p.IsFractured),
        HasBleeding = insp.Parts.Any(p => p.IsBleeding),
        HasHeavyBloodLoss = insp.BloodTier >= BloodLossTier.Moderate,
    };

    /// <summary>该角色是否处于某"状态名"（谓词 value）。未知状态名恒不匹配（防御）。</summary>
    public bool HasState(string? state) => (state ?? "").Trim().ToLowerInvariant() switch
    {
        "dead" => IsDead,
        "unconscious" => IsUnconscious,
        "blind" => IsBlind,
        "severed_hand" => HasSeveredHand,
        "severed_foot" => HasSeveredFoot,
        "severed" => HasAnySevered,
        "disabled" => HasDisabled,
        "fractured" => HasFractured,
        "bleeding" => HasBleeding,
        "heavy_blood_loss" => HasHeavyBloodLoss,
        _ => false,
    };
}

/// <summary>
/// 喂给气泡选择器的"世界只读快照"。全部为死数据——选择器不回抓 Godot 节点，条件求值可单测。
/// </summary>
public sealed class MealWorldContext
{
    /// <summary>当前相位："dawn" | "dusk"（对应气泡 phase）。</summary>
    public string Phase { get; init; } = "any";

    /// <summary>当前剧情 flags（谓词只读它）。</summary>
    public StoryFlags Flags { get; init; } = new StoryFlags();

    /// <summary>在场角色状态快照（存活/已死都可放入，供"存在角色已死"等谓词）。</summary>
    public IReadOnlyList<PawnSnapshot> Pawns { get; init; } = Array.Empty<PawnSnapshot>();

    /// <summary>营地食物存量。</summary>
    public int Food { get; init; }

    /// <summary>营地士气。</summary>
    public double Morale { get; init; }

    /// <summary>按名字找角色快照（不区分大小写）；找不到返回 null。</summary>
    public PawnSnapshot? PawnNamed(string? name) =>
        name is null ? null : Pawns.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 一个谓词（json 扁平对象，靠 <see cref="type"/> 区分）。只读世界状态，返回真/假。
/// 支持类型：
///   · "phase"           —— 相位等于 <see cref="value"/>（dawn/dusk）。
///   · "flag"            —— flag <see cref="key"/> 等于 <see cref="value"/>（value 省略=判"已设置"）。
///   · "any_pawn_state"  —— 存在某角色处于 <see cref="value"/> 状态（severed_hand/disabled/dead/…）。
///   · "pawn_state"      —— 具名角色 <see cref="name"/> 处于 <see cref="value"/> 状态。
///   · "food"            —— 食物 与 <see cref="amount"/> 按 <see cref="op"/> 比较（lt/lte/eq/gte/gt）。
///   · "morale"          —— 士气 与 <see cref="amount"/> 按 <see cref="op"/> 比较。
///   · "hunger"          —— 角色饥饿刻度 ≤ <see cref="stage"/>（<see cref="name"/> 省略=存在任一角色满足）。
/// 无法识别的 type 恒为假（保守，避免误播）。
/// </summary>
public sealed class BubblePredicate
{
    public string type { get; set; } = "";
    public string? key { get; set; }
    public string? value { get; set; }
    public string? name { get; set; }
    public string? op { get; set; }
    public double? amount { get; set; }
    public int? stage { get; set; }

    public bool Eval(MealWorldContext ctx) => (type ?? "").Trim().ToLowerInvariant() switch
    {
        "phase" => string.Equals(ctx.Phase, value, StringComparison.OrdinalIgnoreCase),
        "flag" => value is null ? ctx.Flags.Has(key ?? "") : ctx.Flags.Equals(key ?? "", value),
        "any_pawn_state" => ctx.Pawns.Any(p => p.HasState(value)),
        "pawn_state" => ctx.PawnNamed(name)?.HasState(value) ?? false,
        "food" => Compare(ctx.Food, op, amount),
        "morale" => Compare(ctx.Morale, op, amount),
        "hunger" => EvalHunger(ctx),
        _ => false,
    };

    private bool EvalHunger(MealWorldContext ctx)
    {
        int threshold = stage ?? (int)HungerLevel.Sated;
        if (name is null)
        {
            return ctx.Pawns.Any(p => p.HungerStage <= threshold);
        }
        var pawn = ctx.PawnNamed(name);
        return pawn != null && pawn.HungerStage <= threshold;
    }

    /// <summary>数值比较：op ∈ lt/lte/eq/gte/gt（缺省 lt）。阈值缺省 0。</summary>
    private static bool Compare(double actual, string? op, double? threshold)
    {
        double t = threshold ?? 0;
        return (op ?? "lt").Trim().ToLowerInvariant() switch
        {
            "lt" => actual < t,
            "lte" => actual <= t,
            "eq" => Math.Abs(actual - t) < 1e-9,
            "gte" => actual >= t,
            "gt" => actual > t,
            _ => false,
        };
    }
}

/// <summary>
/// 一条气泡的播放条件：<see cref="all"/> 全真 且 <see cref="any"/> 至少一真才合格。
/// 两组皆可省略/为空——空 <see cref="all"/> 视为真、空 <see cref="any"/> 视为真（即"无此约束"）。
/// 故完全无 condition 的气泡=通用池，任何上下文都合格（向后兼容现有无条件气泡）。
/// </summary>
public sealed class BubbleCondition
{
    /// <summary>与：全部谓词为真。</summary>
    public List<BubblePredicate>? all { get; set; }

    /// <summary>或：至少一个谓词为真。</summary>
    public List<BubblePredicate>? any { get; set; }

    public bool IsSatisfied(MealWorldContext ctx)
    {
        bool allOk = all is null || all.Count == 0 || all.All(p => p.Eval(ctx));
        bool anyOk = any is null || any.Count == 0 || any.Any(p => p.Eval(ctx));
        return allOk && anyOk;
    }
}

/// <summary>说完一句气泡后要施加的一个 flag 改动（推动剧情）。</summary>
public sealed class BubbleTrigger
{
    /// <summary>要写的 flag key。</summary>
    public string key { get; set; } = "";

    /// <summary>要写入的值（null/省略=清除该 flag）。</summary>
    public string? value { get; set; }
}
