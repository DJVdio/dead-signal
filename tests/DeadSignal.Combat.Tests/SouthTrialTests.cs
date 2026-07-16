using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 南方营地三问考验（<see cref="SouthTrial"/>）：**有对错门槛**——每题三答分别记 0/1/2 分，
/// 三题总分满 <see cref="SouthTrial.PassThreshold"/>（5）分才通过；不满即失败。
/// 通过 → 举家南逃 WIN 入口（置 <see cref="SouthTrial.PassedFlag"/>，结局本体由 family-escape-win 建）；
/// 失败 → 见 <see cref="RadioMainlineTests"/>：解锁回复军方、继续游戏。
/// 三问内容为**占位待 author**（[SPEC-B11] 新矩阵，用户拍板推翻旧"无对错"）。
/// </summary>
public class SouthTrialTests
{
    // —— 初始/进度 ——

    [Fact]
    public void FreshFlags_StepZero_NotComplete_FirstQuestion()
    {
        var flags = new StoryFlags();
        Assert.Equal(0, SouthTrial.Step(flags));
        Assert.False(SouthTrial.IsComplete(flags));
        Assert.NotNull(SouthTrial.CurrentQuestion(flags));
        Assert.Equal(0, SouthTrial.TotalScore(flags));
        Assert.False(SouthTrial.IsPassed(flags));
        Assert.False(SouthTrial.IsFailed(flags));
    }

    [Fact]
    public void RecordAnswer_AdvancesOnePerAnswer_CompletesAtThree()
    {
        var flags = new StoryFlags();
        Assert.True(SouthTrial.RecordAnswer(flags, 2));
        Assert.Equal(1, SouthTrial.Step(flags));
        Assert.True(SouthTrial.RecordAnswer(flags, 1));
        Assert.True(SouthTrial.RecordAnswer(flags, 2));
        Assert.Equal(3, SouthTrial.Step(flags));
        Assert.True(SouthTrial.IsComplete(flags));
        Assert.Null(SouthTrial.CurrentQuestion(flags));
        // 答满后不再推进
        Assert.False(SouthTrial.RecordAnswer(flags, 2));
    }

    [Fact]
    public void CurrentQuestion_TracksStep()
    {
        var flags = new StoryFlags();
        Assert.Equal(SouthTrial.Questions[0].Prompt, SouthTrial.CurrentQuestion(flags)!.Value.Prompt);
        SouthTrial.RecordAnswer(flags, 0);
        Assert.Equal(SouthTrial.Questions[1].Prompt, SouthTrial.CurrentQuestion(flags)!.Value.Prompt);
    }

    // —— 三问占位结构 ——

    [Fact]
    public void Questions_AreThree_EachHasThreeAnswers_ScoredZeroOneTwo_AllNonBlank()
    {
        Assert.Equal(SouthTrial.QuestionCount, SouthTrial.Questions.Count);
        foreach (var q in SouthTrial.Questions)
        {
            Assert.False(string.IsNullOrWhiteSpace(q.SouthLine));
            Assert.False(string.IsNullOrWhiteSpace(q.Prompt));
            Assert.Equal(3, q.Answers.Count);
            var scores = new System.Collections.Generic.List<int>();
            foreach (var a in q.Answers)
            {
                Assert.False(string.IsNullOrWhiteSpace(a.Label));
                scores.Add(a.Score);
            }
            // 每题三答恰好覆盖 0/1/2 分（占位门槛的硬不变量）
            scores.Sort();
            Assert.Equal(new[] { 0, 1, 2 }, scores);
        }
    }

    [Fact]
    public void PassThreshold_IsFiveOfSixMax()
    {
        Assert.Equal(5, SouthTrial.PassThreshold);
        // 满分 = 每题 2 分 × 3 题
        Assert.Equal(6, SouthTrial.QuestionCount * SouthTrial.MaxScorePerQuestion);
    }

    // —— 对错门槛：满 5 通过 / 不满失败 ——

    [Fact]
    public void TotalScore_SumsRecordedAnswers()
    {
        var flags = new StoryFlags();
        SouthTrial.RecordAnswer(flags, 2);
        SouthTrial.RecordAnswer(flags, 1);
        SouthTrial.RecordAnswer(flags, 2);
        Assert.Equal(5, SouthTrial.TotalScore(flags));
    }

    [Fact]
    public void Score5_ExactlyThreshold_Passes()
    {
        var flags = ScoredTrial(2, 2, 1); // = 5
        Assert.True(SouthTrial.IsComplete(flags));
        Assert.True(SouthTrial.IsPassed(flags));
        Assert.False(SouthTrial.IsFailed(flags));
    }

    [Fact]
    public void Score6_AboveThreshold_Passes()
    {
        var flags = ScoredTrial(2, 2, 2); // = 6
        Assert.True(SouthTrial.IsPassed(flags));
        Assert.False(SouthTrial.IsFailed(flags));
    }

    [Fact]
    public void Score4_BelowThreshold_Fails()
    {
        var flags = ScoredTrial(2, 2, 0); // = 4
        Assert.True(SouthTrial.IsComplete(flags));
        Assert.False(SouthTrial.IsPassed(flags));
        Assert.True(SouthTrial.IsFailed(flags));
    }

    [Fact]
    public void Score0_AllWorst_Fails()
    {
        var flags = ScoredTrial(0, 0, 0);
        Assert.False(SouthTrial.IsPassed(flags));
        Assert.True(SouthTrial.IsFailed(flags));
    }

    [Fact]
    public void Incomplete_NeitherPassNorFail_EvenIfHighScore()
    {
        var flags = new StoryFlags();
        SouthTrial.RecordAnswer(flags, 2);
        SouthTrial.RecordAnswer(flags, 2); // 只答两题，分够但未答满
        Assert.Equal(4, SouthTrial.TotalScore(flags));
        Assert.False(SouthTrial.IsComplete(flags));
        Assert.False(SouthTrial.IsPassed(flags));
        Assert.False(SouthTrial.IsFailed(flags));
    }

    // —— 通过 flag（family-escape-win 入口）——

    [Fact]
    public void MarkPassed_OnceOnly()
    {
        var flags = ScoredTrial(2, 2, 2);
        Assert.False(SouthTrial.HasPassed(flags));
        Assert.True(SouthTrial.MarkPassed(flags));
        Assert.True(SouthTrial.HasPassed(flags));
        Assert.False(SouthTrial.MarkPassed(flags)); // 二次不再触发
    }

    // —— CG③ 组装（占位启程旁白 + SouthEscape 结尾段）——

    [Fact]
    public void EscapeCg_ConcatenatesDepartureNarrationAndSouthEscape()
    {
        var cg = SouthTrial.EscapeCg(new StoryFlags());
        var narration = SouthTrial.DepartureNarration();
        Assert.Equal(narration.Count + EndingCg.SouthEscape.Count, cg.Count);
        Assert.Contains(EndingCg.SouthEscape[EndingCg.SouthEscape.Count - 1], cg);
    }

    // —— 启程去重 ——

    [Fact]
    public void MarkDeparted_OnceOnly()
    {
        var flags = new StoryFlags();
        Assert.False(SouthTrial.HasDeparted(flags));
        Assert.True(SouthTrial.MarkDeparted(flags));
        Assert.True(SouthTrial.HasDeparted(flags));
        Assert.False(SouthTrial.MarkDeparted(flags)); // 二次不再触发
    }

    // —— null 容错 ——

    [Fact]
    public void NullFlags_SafeDefaults()
    {
        Assert.Equal(0, SouthTrial.Step(null!));
        Assert.False(SouthTrial.IsComplete(null!));
        Assert.Equal(0, SouthTrial.TotalScore(null!));
        Assert.False(SouthTrial.IsPassed(null!));
        Assert.False(SouthTrial.IsFailed(null!));
        Assert.False(SouthTrial.RecordAnswer(null!, 2));
        Assert.False(SouthTrial.HasPassed(null!));
        Assert.False(SouthTrial.MarkPassed(null!));
        Assert.False(SouthTrial.HasDeparted(null!));
        Assert.False(SouthTrial.MarkDeparted(null!));
    }

    // —— helper ——

    /// <summary>造一个答满三题的 flags，三题得分依次为参数。</summary>
    private static StoryFlags ScoredTrial(int a, int b, int c)
    {
        var flags = new StoryFlags();
        SouthTrial.RecordAnswer(flags, a);
        SouthTrial.RecordAnswer(flags, b);
        SouthTrial.RecordAnswer(flags, c);
        return flags;
    }
}
