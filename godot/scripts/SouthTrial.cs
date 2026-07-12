using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 RadioMainline.cs / ChristineRequestLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 南逃线「南方营地三问考验」（[SPEC-B11] 用户拍板："南逃考验可以是问三个问题"）：
//   呼叫南方（RadioMainline.CallSouth，置 south_escape_open）后，南方营地经电台抛来三个问题，
//   主题＝对方在验"**这里的人值不值得救**"（与第二商人"我信这儿的人是善的"暗线呼应）。
//
// 门槛口径（主 agent 拍板，[SPEC-B11] 授权）：**三问是叙事性拷问——任何选择都放行，无对错惩罚**，
//   但三次回答的整体基调（原则/务实/冷硬）会择出**启程旁白里的一句话变体**（南方临别的评语），
//   让玩家的答法在收束时有回响，而非白问。不设真门槛（真要设门槛属产品分叉，会另行上抛，此处不自裁）。
//
// 通过（三问答满）→ 南方"临时开放前往峡谷的路"；玩家备好物资后回营地电台**启程确认**（二次确认：
//   只带背得动的物资、不可逆、须抢在第 40 天尸潮前——尸潮 Arrived 后不可再逃）→ 播 CG③。
// 状态只存 StoryFlags 字符串（引擎无整数字段）：这里只做"读值→判定→写值"，可单测。
// 问题文本/答案/启程旁白皆 **draft 供用户改**（以 drafts §3 升正式，微调润色）。

/// <summary>
/// 南方三问考验的纯逻辑：问题表、逐题回答记录、整体基调 → 启程旁白变体、CG③ 组装。
/// 进度存 <see cref="StepKey"/>（已答题数 0..3），回答基调串存 <see cref="AnswersKey"/>。
/// </summary>
public static class SouthTrial
{
    /// <summary>已答题数（"0".."3"；未设=0）。达 <see cref="QuestionCount"/> 即考验通过。</summary>
    public const string StepKey = "south_trial_step";

    /// <summary>逐题回答的基调数字串（如 "021"，每位一题的 <see cref="Tone"/> 值）；供择启程变体。</summary>
    public const string AnswersKey = "south_trial_answers";

    /// <summary>南逃已启程 flag（播 CG③ 前置，一次性去重，防重复触发终局）。</summary>
    public const string DepartedFlag = "south_departed";

    /// <summary>三问。</summary>
    public const int QuestionCount = 3;

    /// <summary>单次回答的基调（决定启程旁白临别评语的变体；无对错，只影响措辞）。</summary>
    public enum Tone
    {
        /// <summary>原则/善念（自立、护弱、不掠活人）。</summary>
        Principled = 0,

        /// <summary>务实/灰度（该抢则抢但守底线、看情况）。</summary>
        Pragmatic = 1,

        /// <summary>冷硬/诚实（承认脏手、不粉饰）。</summary>
        Hard = 2,
    }

    /// <summary>整体基调 → 启程旁白临别评语变体。</summary>
    public enum DepartureVariant
    {
        Principled,
        Pragmatic,
        Hard,
    }

    /// <summary>一个回答选项：措辞 + 其基调。</summary>
    public readonly struct TrialAnswer
    {
        public readonly string Label;
        public readonly Tone Tone;
        public TrialAnswer(string label, Tone tone) { Label = label; Tone = tone; }
    }

    /// <summary>一道考题：南方引入白 + 问题正文 + 三个基调各异的回答。</summary>
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

    /// <summary>三问定稿（drafts §3 升正式；主题＝验这里的人值不值得救，呼应第二商人善念暗线）。</summary>
    public static readonly IReadOnlyList<TrialQuestion> Questions = new[]
    {
        new TrialQuestion(
            "南方（杂音里）：「……开门之前，我得问你们几句。老实答，我听得出人话里的假。」",
            "「这一路，你们是怎么活到现在的？抢来的，还是自己挣的？」",
            new[]
            {
                new TrialAnswer("「自己挣的。能种的种，能修的修——没抢过还有活路的人。」", Tone.Principled),
                new TrialAnswer("「该抢的时候也抢过。但没对还喘着气的人下过死手。」", Tone.Pragmatic),
                new TrialAnswer("「活着就得脏手。我不想骗你。」", Tone.Hard),
            }),
        new TrialQuestion(
            "南方：「……嗯。再问一个。」",
            "「你们那儿，有没有干不动活、却还留着的人？」",
            new[]
            {
                new TrialAnswer("「有。他们也是人，不是白吃饭的一张嘴。」", Tone.Principled),
                new TrialAnswer("「有过。留不留，看还撑不撑得住。」", Tone.Pragmatic),
                new TrialAnswer("「没有。撑不下去的，我们没能留住。」", Tone.Hard),
            }),
        new TrialQuestion(
            "南方：「最后一个。」",
            "「真放你们过来，你们是打算自己立起来，还是又一张要我们喂的嘴？」",
            new[]
            {
                new TrialAnswer("「我们自己会立起来，不赖着你们。」", Tone.Principled),
                new TrialAnswer("「先求一条命。往后的事，往后再说。」", Tone.Pragmatic),
                new TrialAnswer("「需要的话，我们也能替你们挡事。」", Tone.Hard),
            }),
    };

    // —— 进度读写 ——

    /// <summary>已答题数（未设/无法解析 → 0）。</summary>
    public static int Step(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(StepKey), out int s) && s > 0 ? Math.Min(s, QuestionCount) : 0;

    /// <summary>考验是否已答满三问（通过）。</summary>
    public static bool IsComplete(StoryFlags flags) => Step(flags) >= QuestionCount;

    /// <summary>当前应答的考题（已答满 → null）。</summary>
    public static TrialQuestion? CurrentQuestion(StoryFlags flags)
    {
        int step = Step(flags);
        return step >= QuestionCount ? (TrialQuestion?)null : Questions[step];
    }

    /// <summary>
    /// 记录一次回答并推进一题。<paramref name="tone"/> 追加到基调串、进度 +1。
    /// 已答满则无操作。返回是否发生了推进。
    /// </summary>
    public static bool RecordAnswer(StoryFlags flags, Tone tone)
    {
        if (flags == null) return false;
        int step = Step(flags);
        if (step >= QuestionCount) return false;
        string prev = flags.Get(AnswersKey) ?? string.Empty;
        flags.Set(AnswersKey, prev + ((int)tone).ToString());
        flags.Set(StepKey, (step + 1).ToString());
        return true;
    }

    /// <summary>南逃是否已启程（CG③ 已触发过）。</summary>
    public static bool HasDeparted(StoryFlags flags) => flags != null && flags.Has(DepartedFlag);

    /// <summary>一次性置"已启程"：首次返回 true，其后恒 false（防重复播 CG③）。</summary>
    public static bool MarkDeparted(StoryFlags flags)
    {
        if (flags == null || flags.Has(DepartedFlag)) return false;
        flags.Set(DepartedFlag, "true");
        return true;
    }

    // —— 启程旁白变体 ——

    /// <summary>
    /// 据回答基调串择整体变体：取出现次数最多的基调；并列时偏原则→务实→冷硬（低枚举值优先）。
    /// 无记录（空串）默认 <see cref="DepartureVariant.Pragmatic"/>（中性）。
    /// </summary>
    public static DepartureVariant Variant(StoryFlags flags)
    {
        string ans = flags?.Get(AnswersKey) ?? string.Empty;
        int p = 0, r = 0, h = 0;
        foreach (char c in ans)
        {
            if (c == '0') p++;
            else if (c == '1') r++;
            else if (c == '2') h++;
        }
        if (p == 0 && r == 0 && h == 0) return DepartureVariant.Pragmatic;
        if (p >= r && p >= h) return DepartureVariant.Principled;
        if (r >= h) return DepartureVariant.Pragmatic;
        return DepartureVariant.Hard;
    }

    /// <summary>南方临别评语（据整体基调；drafts §3 变体，呼应第二商人"我信这儿的人是善的"暗线）。</summary>
    public static string PartingLine(DepartureVariant v) => v switch
    {
        DepartureVariant.Principled => "南方电台最后撂下一句：「……你们这样的人，本该早点来的。」",
        DepartureVariant.Hard       => "南方电台最后撂下一句：「……我不知道你们算不算好人。但至少，你们没骗我。」",
        _                            => "南方电台最后撂下一句：「……记住你们说过的话。峡谷那头，没人再给你们兜底。」",
    };

    // —— 启程旁白 + CG③ 组装 ——

    /// <summary>
    /// 南逃启程旁白（drafts §3.4 升正式），据变体在中段插入南方临别一句。每段一屏。
    /// </summary>
    public static IReadOnlyList<string> DepartureNarration(DepartureVariant variant) => new[]
    {
        "你们把能带的都捆上了——只有背得动的那些。剩下的，留给这座城，和它身后追来的东西。",
        PartingLine(variant),
        "峡谷的另一头是什么，南边的人不肯说，只说「比这里强」。你不知道该不该信。可留下来，只有一种结局。",
        "队伍走出营门时，没人回头。身后，倒计时还在走。",
    };

    /// <summary>
    /// CG③ 完整播放序列＝启程旁白（含三问变体一句）＋ <see cref="EndingCg.SouthEscape"/> 结尾段。
    /// 供 CampMain 启程确认后一次性喂 EndingPanel。
    /// </summary>
    public static IReadOnlyList<string> EscapeCg(StoryFlags flags)
    {
        var list = new List<string>(DepartureNarration(Variant(flags)));
        list.AddRange(EndingCg.SouthEscape);
        return list;
    }

    // —— 南方考验前后 draft 文案（供 CampMain 薄接线；供用户改）——

    /// <summary>三问全部答完后，南方的裁决回话（开路口径 + 启程须知）。</summary>
    public const string VerdictTitle = "南方的回话";

    /// <summary>南方裁决正文（无对错放行；告知路已开、回电台启程、尸潮不等人）。</summary>
    public const string VerdictNarrative =
        "电台那头沉默了很久，久到你以为信号断了。\n\n" +
        "「……行。」南方的声音回来了，「路给你们留着。能带走的，只有背得动的那些——别指望把家搬过来。」\n\n" +
        "「准备好了，就从这台电台再知会我们一声，我们给你们开路。」\n\n" +
        "（南逃之路已开启：备好要带的物资，回到营地电台启程。切记——尸潮不等人，得赶在第 40 天之前出发。）";

    /// <summary>回营地电台后、启程入口的提示（尚未启程时）。</summary>
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
        "尸潮已经压到了营墙下。南方给的三天，早过了。\n\n峡谷的路，走不成了——现在能做的，只有守住这里，守到最后。";
}
