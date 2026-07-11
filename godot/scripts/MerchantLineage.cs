using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 StoryFlags.cs / ChristineRequestLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 神秘商人**接替链**的状态机纯逻辑（用户拍板 [SPEC-B7] 第5条原话）：
//   第一商人在任 → 死于营地(零掉落，杜绝杀商套利) → 第二商人接替(首访播 authored 台词) → 第二商人再死 → 今后永无商人。
// 状态只存在 StoryFlags 的字符串里（引擎无整数字段），跟 ChristineRequestLogic/GoldfingerDiscovery 先例：只做"读值→判定→写值"，可单测。
// ★死亡地点判定：商人为 Neutral、只在营地生成出现，故"死于营地"=商人死亡即等价（若日后商人会出现在别处，需在此加地点门控）。
// ★零掉落不在本类：商人 Pawn 不携货架库存、本作角色死亡也无自动尸体掉落，故"零掉落"由现状保证；本类只推接替/断商状态。
// ★台词为草稿，最终由用户手写；本类只保证状态推进与"该不该播首访台词"可跑、可测。

/// <summary>商人接替链的当前阶段。</summary>
public enum MerchantLineageStage
{
    /// <summary>第一商人在任（默认；flag 未设置即此）。</summary>
    First,

    /// <summary>第一商人已死于营地，第二商人接替在任。</summary>
    Second,

    /// <summary>两位商人皆死于营地——今后永无商人（调度不再排访）。</summary>
    Extinct,
}

/// <summary>
/// 商人接替链状态判定/推进。主状态存于 flag <see cref="StageKey"/>（未设=First / "second" / "extinct"）；
/// 第二商人首访开场白是否播过存于 <see cref="SecondIntroPlayedKey"/>。
/// 商人死于营地经 <see cref="OnMerchantDiedAtCamp"/> 推进；调度侧先查 <see cref="MerchantsAvailable"/> 决定还排不排访。
/// </summary>
public static class MerchantLineage
{
    /// <summary>接替链主状态 flag（未设=First / "second" / "extinct"）。</summary>
    public const string StageKey = "merchant_lineage";

    /// <summary>第二商人首访开场白已播 flag（保证只播一次）。</summary>
    public const string SecondIntroPlayedKey = "merchant_second_intro_played";

    private const string SecondValue = "second";
    private const string ExtinctValue = "extinct";

    /// <summary>
    /// 第二商人首访开场白（**文案草稿，供用户改**）。叙事要点（用户口径）：
    /// 协会说这个营地专屠商人、想放弃这个点；他相信这里的人是善良的；故力排众议来此跑商。
    /// </summary>
    public const string SecondMerchantIntroLine =
        "「协会说这个营地专屠商人，想把这条线从图上划掉。……可我不信。我信这儿的人，骨子里是善的。所以这一趟，我是顶着所有人的反对来的。」";

    /// <summary>当前接替链阶段（flag 未设/无法识别 → <see cref="MerchantLineageStage.First"/>）。</summary>
    public static MerchantLineageStage Stage(StoryFlags flags)
    {
        string? v = flags?.Get(StageKey);
        if (string.Equals(v, ExtinctValue, StringComparison.OrdinalIgnoreCase))
        {
            return MerchantLineageStage.Extinct;
        }
        if (string.Equals(v, SecondValue, StringComparison.OrdinalIgnoreCase))
        {
            return MerchantLineageStage.Second;
        }
        return MerchantLineageStage.First;
    }

    /// <summary>今后是否还会有商人来访：<see cref="MerchantLineageStage.Extinct"/> 即永久断商（调度据此不再排访）。</summary>
    public static bool MerchantsAvailable(StoryFlags flags) => Stage(flags) != MerchantLineageStage.Extinct;

    /// <summary>当前在任者是否为**第二（接替）商人**（用于差异化台词/外观）。</summary>
    public static bool IsSecondMerchant(StoryFlags flags) => Stage(flags) == MerchantLineageStage.Second;

    /// <summary>
    /// 商人死于营地时推进接替链：First→Second（第二商人将接替）、Second→Extinct（今后永无商人）；
    /// Extinct 幂等（无操作）。返回死亡后的新阶段。零掉落由调用侧现状保证（本类不碰库存/掉落）。
    /// </summary>
    public static MerchantLineageStage OnMerchantDiedAtCamp(StoryFlags flags)
    {
        if (flags == null)
        {
            return MerchantLineageStage.First;
        }
        switch (Stage(flags))
        {
            case MerchantLineageStage.First:
                flags.Set(StageKey, SecondValue);
                return MerchantLineageStage.Second;
            case MerchantLineageStage.Second:
                flags.Set(StageKey, ExtinctValue);
                return MerchantLineageStage.Extinct;
            default:
                return MerchantLineageStage.Extinct;
        }
    }

    /// <summary>第二商人首访是否该播开场白：处于 <see cref="MerchantLineageStage.Second"/> 且 <see cref="SecondIntroPlayedKey"/> 未置。</summary>
    public static bool ShouldPlaySecondIntro(StoryFlags flags)
        => Stage(flags) == MerchantLineageStage.Second && !(flags?.Has(SecondIntroPlayedKey) ?? false);

    /// <summary>标记第二商人开场白已播（此后 <see cref="ShouldPlaySecondIntro"/> 恒 false，只播一次）。</summary>
    public static void MarkSecondIntroPlayed(StoryFlags flags) => flags?.Set(SecondIntroPlayedKey, "true");
}
