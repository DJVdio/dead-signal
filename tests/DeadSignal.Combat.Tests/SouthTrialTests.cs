using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 南方营地三问考验（<see cref="SouthTrial"/>）：叙事性拷问，任何选择都放行，
/// 三次回答基调择启程旁白临别一句；答满三问即通过；启程一次性去重。
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
    }

    [Fact]
    public void RecordAnswer_AdvancesOnePerAnswer_CompletesAtThree()
    {
        var flags = new StoryFlags();
        Assert.True(SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Principled));
        Assert.Equal(1, SouthTrial.Step(flags));
        Assert.True(SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Pragmatic));
        Assert.True(SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Hard));
        Assert.Equal(3, SouthTrial.Step(flags));
        Assert.True(SouthTrial.IsComplete(flags));
        Assert.Null(SouthTrial.CurrentQuestion(flags));
        // 答满后不再推进
        Assert.False(SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Principled));
    }

    [Fact]
    public void CurrentQuestion_TracksStep()
    {
        var flags = new StoryFlags();
        Assert.Equal(SouthTrial.Questions[0].Prompt, SouthTrial.CurrentQuestion(flags)!.Value.Prompt);
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Hard);
        Assert.Equal(SouthTrial.Questions[1].Prompt, SouthTrial.CurrentQuestion(flags)!.Value.Prompt);
    }

    // —— 三问定稿结构 ——

    [Fact]
    public void Questions_AreThree_EachHasThreeAnswers_AllNonBlank()
    {
        Assert.Equal(SouthTrial.QuestionCount, SouthTrial.Questions.Count);
        foreach (var q in SouthTrial.Questions)
        {
            Assert.False(string.IsNullOrWhiteSpace(q.SouthLine));
            Assert.False(string.IsNullOrWhiteSpace(q.Prompt));
            Assert.Equal(3, q.Answers.Count);
            foreach (var a in q.Answers)
                Assert.False(string.IsNullOrWhiteSpace(a.Label));
        }
    }

    // —— 启程变体：多数基调决定 ——

    [Fact]
    public void Variant_EmptyDefaultsPragmatic()
        => Assert.Equal(SouthTrial.DepartureVariant.Pragmatic, SouthTrial.Variant(new StoryFlags()));

    [Fact]
    public void Variant_MajorityPrincipled()
    {
        var flags = new StoryFlags();
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Principled);
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Principled);
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Hard);
        Assert.Equal(SouthTrial.DepartureVariant.Principled, SouthTrial.Variant(flags));
    }

    [Fact]
    public void Variant_MajorityHard()
    {
        var flags = new StoryFlags();
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Hard);
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Hard);
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Pragmatic);
        Assert.Equal(SouthTrial.DepartureVariant.Hard, SouthTrial.Variant(flags));
    }

    [Theory]
    [InlineData(SouthTrial.DepartureVariant.Principled)]
    [InlineData(SouthTrial.DepartureVariant.Pragmatic)]
    [InlineData(SouthTrial.DepartureVariant.Hard)]
    public void PartingLine_NonBlankForEachVariant(SouthTrial.DepartureVariant v)
        => Assert.False(string.IsNullOrWhiteSpace(SouthTrial.PartingLine(v)));

    // —— CG③ 组装：启程旁白 + SouthEscape ——

    [Fact]
    public void EscapeCg_ConcatenatesDepartureNarrationAndSouthEscape()
    {
        var flags = new StoryFlags();
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Principled);
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Principled);
        SouthTrial.RecordAnswer(flags, SouthTrial.Tone.Principled);
        var cg = SouthTrial.EscapeCg(flags);
        var narration = SouthTrial.DepartureNarration(SouthTrial.DepartureVariant.Principled);
        Assert.Equal(narration.Count + EndingCg.SouthEscape.Count, cg.Count);
        // 变体一句在旁白里
        Assert.Contains(SouthTrial.PartingLine(SouthTrial.DepartureVariant.Principled), cg);
        // 结尾 CG 段并入
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
        Assert.False(SouthTrial.RecordAnswer(null!, SouthTrial.Tone.Principled));
        Assert.False(SouthTrial.HasDeparted(null!));
        Assert.False(SouthTrial.MarkDeparted(null!));
    }
}
