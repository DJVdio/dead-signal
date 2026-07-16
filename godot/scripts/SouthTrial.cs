using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 RadioMainline.cs / ChristineRequestLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 南逃线「南方营地三问考验」（[SPEC-B11] 用户拍板；新矩阵推翻旧「南逃=唯一生路·三问无对错」正史）：
//   呼叫南方（RadioMainline.CallSouth，置 south_escape_open）后，南方营地经电台抛来三个问题，
//   主题＝对方在验"**这里的人值不值得救**"（与第二商人"我信这儿的人是善的"暗线呼应）。
//
// 门槛口径（用户拍板 [SPEC-B11] 新矩阵）：**三问有真对错门槛**——
//   每题三个答案分别记 0/1/2 分，三题总分满 <see cref="PassThreshold"/>（5，满分 6）分才**通过**；不满即**失败**。
//   · 通过 → 举家南逃 WIN（好结局；结局本体由 family-escape-win 另建，本类只置 <see cref="PassedFlag"/> 作入口）。
//   · 失败 → **不结束、继续游戏**：调用方（CampMain）经 <see cref="RadioMainline.ReopenAfterSouthFailure"/>
//     退回电台"持设备"态、解锁回复军方（坏结局军袭因此重新可达；南方已拒，不可再呼叫南方）。
//
// 通过后的启程路径（占位·family-escape-win 会替换成"举家南逃 WIN"本体）：备好物资回营地电台**启程确认**
//   （二次确认：只带背得动的物资、不可逆、须抢在第 40 天尸潮前）→ 播 CG③。
// 状态只存 StoryFlags 字符串（引擎无整数字段）：这里只做"读值→判定→写值"，可单测。
// ★三问题目/答案/分数皆 **占位待 author**（用户原话"你先写一点，后期我会设计了改"）：节拍中性占位，
//   score 映射亦为占位（每题恰覆盖 0/1/2），待用户设计正式题目时校准。

/// <summary>
/// 南方三问考验的纯逻辑：占位问题表、逐题回答记分、总分 → 通过/失败判定、通过入口 flag、CG③ 组装。
/// 进度存 <see cref="StepKey"/>（已答题数 0..3），逐题得分串存 <see cref="ScoresKey"/>（每位一题的 0/1/2 分）。
/// </summary>
public static class SouthTrial
{
    /// <summary>已答题数（"0".."3"；未设=0）。</summary>
    public const string StepKey = "south_trial_step";

    /// <summary>逐题得分数字串（如 "221"，每位一题选中答案的 <see cref="TrialAnswer.Score"/>）；供算总分。</summary>
    public const string ScoresKey = "south_trial_scores";

    /// <summary>三问已通过 flag（供 family-escape-win 挂"举家南逃 WIN"结局本体的入口）。</summary>
    public const string PassedFlag = "south_trial_passed";

    /// <summary>南逃已启程 flag（播 CG③ 前置，一次性去重，防重复触发终局）。</summary>
    public const string DepartedFlag = "south_departed";

    /// <summary>三问。</summary>
    public const int QuestionCount = 3;

    /// <summary>单题满分（每题三答记 0/1/2 分）。满分 = <see cref="QuestionCount"/> × 本值 = 6。</summary>
    public const int MaxScorePerQuestion = 2;

    /// <summary>通过门槛：三题总分满本值（5，满分 6）才通过；不满即失败。占位阈值，待 author 校准。</summary>
    public const int PassThreshold = 5;

    /// <summary>一个回答选项：措辞 + 其得分（0/1/2，占位待 author）。</summary>
    public readonly struct TrialAnswer
    {
        public readonly string Label;
        public readonly int Score;
        public TrialAnswer(string label, int score) { Label = label; Score = score; }
    }

    /// <summary>一道考题：南方（电台那头）引入白 + 问题正文 + 三个记分各异的回答。</summary>
    public readonly struct TrialQuestion
    {
        public readonly string SouthLine; // 南方（电台那头）的引入白
        public readonly string Prompt;    // 问题正文
        public readonly IReadOnlyList<TrialAnswer> Answers;
        public TrialQuestion(string southLine, string prompt, IReadOnlyList<TrialAnswer> answers)
        {
            SouthLine = southLine; Prompt = prompt; Answers = answers;
        }
    }

    /// <summary>
    /// 三问**占位**（每题三答恰覆盖 0/1/2 分；主题＝验这里的人值不值得救，呼应第二商人善念暗线）。
    /// ★占位待 author：用户后期设计正式题目/答案/分数映射时替换本表（节拍中性，勿当已定稿）。
    /// </summary>
    public static readonly IReadOnlyList<TrialQuestion> Questions = new[]
    {
        new TrialQuestion(
            "南方（杂音里）：「……开门之前，我得问你们几句。老实答，我听得出人话里的假。」",
            "「你们那边，还剩多少能喘气的人？」（占位）",
            new[]
            {
                new TrialAnswer("「实打实告诉你，还有一些人，都还站得住。」", 2),
                new TrialAnswer("「不多，但还撑得下去。」", 1),
                new TrialAnswer("「……这个，现在不太方便说。」", 0),
            }),
        new TrialQuestion(
            "南方：「……嗯。再问一个。」",
            "「这一路，你们是靠什么活到现在的？」（占位）",
            new[]
            {
                new TrialAnswer("「自己种、自己修，没白拿过谁的东西。」", 2),
                new TrialAnswer("「该换该借，凑合着，也没对活人下过死手。」", 1),
                new TrialAnswer("「抢来的。乱世了，谁还讲这个。」", 0),
            }),
        new TrialQuestion(
            "南方：「最后一个。」",
            "「真放你们过来，是又一张要我们喂的嘴，还是能自己立起来？」（占位）",
            new[]
            {
                new TrialAnswer("「我们自己立得住，还能替你们搭把手。」", 2),
                new TrialAnswer("「先求一条命，往后的事往后再说。」", 1),
                new TrialAnswer("「你们最好别惹我们，别的少问。」", 0),
            }),
    };

    // —— 进度读写 ——

    /// <summary>已答题数（未设/无法解析 → 0）。</summary>
    public static int Step(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(StepKey), out int s) && s > 0 ? Math.Min(s, QuestionCount) : 0;

    /// <summary>考验是否已答满三问（可判通过/失败的前提）。</summary>
    public static bool IsComplete(StoryFlags flags) => Step(flags) >= QuestionCount;

    /// <summary>当前应答的考题（已答满 → null）。</summary>
    public static TrialQuestion? CurrentQuestion(StoryFlags flags)
    {
        int step = Step(flags);
        return step >= QuestionCount ? (TrialQuestion?)null : Questions[step];
    }

    /// <summary>
    /// 记录一次回答并推进一题：<paramref name="score"/>（该题选中答案的分）追加到得分串、进度 +1。
    /// 已答满则无操作。返回是否发生了推进。
    /// </summary>
    public static bool RecordAnswer(StoryFlags flags, int score)
    {
        if (flags == null) return false;
        int step = Step(flags);
        if (step >= QuestionCount) return false;
        int clamped = Math.Clamp(score, 0, MaxScorePerQuestion);
        string prev = flags.Get(ScoresKey) ?? string.Empty;
        flags.Set(ScoresKey, prev + clamped.ToString());
        flags.Set(StepKey, (step + 1).ToString());
        return true;
    }

    /// <summary>三题累计得分（读得分串逐位求和；未答/无记录 → 0）。</summary>
    public static int TotalScore(StoryFlags flags)
    {
        string s = flags?.Get(ScoresKey) ?? string.Empty;
        int sum = 0;
        foreach (char c in s)
            if (c >= '0' && c <= '9') sum += c - '0';
        return sum;
    }

    /// <summary>是否**通过**：答满三问且总分 ≥ <see cref="PassThreshold"/>。</summary>
    public static bool IsPassed(StoryFlags flags) => IsComplete(flags) && TotalScore(flags) >= PassThreshold;

    /// <summary>是否**失败**：答满三问但总分 &lt; <see cref="PassThreshold"/>。</summary>
    public static bool IsFailed(StoryFlags flags) => IsComplete(flags) && TotalScore(flags) < PassThreshold;

    // —— 通过入口 flag（family-escape-win 挂 WIN 结局本体）——

    /// <summary>是否已置"通过"入口 flag。</summary>
    public static bool HasPassed(StoryFlags flags) => flags != null && flags.Has(PassedFlag);

    /// <summary>一次性置"通过"入口 flag：首次返回 true，其后恒 false（供 family-escape-win 挂结局本体）。</summary>
    public static bool MarkPassed(StoryFlags flags)
    {
        if (flags == null || flags.Has(PassedFlag)) return false;
        flags.Set(PassedFlag, "true");
        return true;
    }

    // —— 启程去重（占位 WIN 路径；family-escape-win 会替换本体）——

    /// <summary>南逃是否已启程（CG③ 已触发过）。</summary>
    public static bool HasDeparted(StoryFlags flags) => flags != null && flags.Has(DepartedFlag);

    /// <summary>一次性置"已启程"：首次返回 true，其后恒 false（防重复播 CG③）。</summary>
    public static bool MarkDeparted(StoryFlags flags)
    {
        if (flags == null || flags.Has(DepartedFlag)) return false;
        flags.Set(DepartedFlag, "true");
        return true;
    }

    // —— 启程旁白 + CG③ 组装（占位；family-escape-win 会替换成举家南逃 WIN 本体）——

    /// <summary>南逃启程旁白（占位·drafts §3.4）。每段一屏。</summary>
    public static IReadOnlyList<string> DepartureNarration() => new[]
    {
        "你们把能带的都捆上了——只有背得动的那些。剩下的，留给这座城，和它身后追来的东西。",
        "南方电台最后撂下一句：「……记住你们说过的话。峡谷那头，没人再给你们兜底。」",
        "峡谷的另一头是什么，南边的人不肯说，只说「比这里强」。你不知道该不该信。可留下来，只有一种结局。",
        "队伍走出营门时，没人回头。身后，倒计时还在走。",
    };

    /// <summary>
    /// CG③ 完整播放序列＝启程旁白 ＋ <see cref="EndingCg.SouthEscape"/> 结尾段。
    /// 供 CampMain 启程确认后一次性喂 EndingPanel（占位；family-escape-win 会以"举家南逃 WIN"替换）。
    /// </summary>
    public static IReadOnlyList<string> EscapeCg(StoryFlags flags)
    {
        var list = new List<string>(DepartureNarration());
        list.AddRange(EndingCg.SouthEscape);
        return list;
    }

    // —— 南方考验前后 draft 文案（供 CampMain 薄接线；供用户改）——

    /// <summary>三问通过后，南方的裁决回话（开路口径 + 启程须知）。</summary>
    public const string VerdictTitle = "南方的回话";

    /// <summary>南方裁决正文（通过后放行；告知路已开、回电台启程、尸潮不等人）。</summary>
    public const string VerdictNarrative =
        "电台那头沉默了很久，久到你以为信号断了。\n\n" +
        "「……行。」南方的声音回来了，「路给你们留着。能带走的，只有背得动的那些——别指望把家搬过来。」\n\n" +
        "「准备好了，就从这台电台再知会我们一声，我们给你们开路。」\n\n" +
        "（南逃之路已开启：备好要带的物资，回到营地电台启程。切记——尸潮不等人，得赶在第 40 天之前出发。）";

    /// <summary>三问失败后，南方的回绝话（占位待 author）。</summary>
    public const string FailureTitle = "南方的回绝";

    /// <summary>
    /// 南方回绝正文（占位·失败不结束游戏，退回电台可回复军方或死守）。
    /// ★占位待 author：只描述"南方拒收、路封了"，不发明深剧情。
    /// </summary>
    public const string FailureNarrative =
        "电台那头静了很久。\n\n" +
        "「……不行。」南方的声音冷下来，「你们那边，我信不过。这条路，我不能给你们开。」\n\n" +
        "信号断了，再呼，只剩一片杂音。南边那扇门，关上了。\n\n" +
        "（南逃之路已封。你们只能另作打算——电台还能发，剩下的活路，得自己去选。）";

    /// <summary>回营地电台后、启程入口的提示（考验通过、尚未启程时）。</summary>
    public const string DeparturePrompt =
        "南方给你们留的路还开着。要现在就启程南逃吗？\n\n只能带走背得动的物资；一旦出发，就不再回头。";

    /// <summary>启程选项：出发。</summary>
    public const string DepartOptionLabel = "启程南逃，前往峡谷的路";

    /// <summary>启程选项：再等等（继续备货/推进）。</summary>
    public const string DepartDeferLabel = "再等等，还没准备好";

    /// <summary>启程二次确认文案（不可逆）。</summary>
    public const string DepartConfirmPrompt =
        "一旦走出营门，就再回不来了。带上的，只有背得动的那些。\n\n你确定现在启程南逃吗？";

    /// <summary>尸潮已至、错过启程窗口的兜底文案标题。</summary>
    public const string TooLateTitle = "太迟了";

    /// <summary>尸潮已至、路走不成了的兜底正文。</summary>
    public const string TooLateNarrative =
        "尸潮已经压到了营墙下。南方给的路，早封了。\n\n峡谷的路，走不成了——现在能做的，只有守住这里，守到最后。";
}
