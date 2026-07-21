using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 游戏内素材署名（<see cref="CreditsContent"/>）的合规护栏。
///
/// <para>
/// 物品图标全部取自 game-icons.net，授权是 <b>CC BY 3.0</b>——用与改都自由，<b>但署名是硬条件</b>。
/// 这几条断言存在的唯一理由：谁哪天顺手把署名文字删了/改没了，测试当场红，而不是等到有人来问才发现。
/// </para>
/// </summary>
public class CreditsContentTests
{
    private static string AllText()
        => string.Join("\n", CreditsContent.Sections.SelectMany(s => new[] { s.Title, s.License }.Concat(s.Lines)));

    [Fact]
    public void 署名里必须点名game_icons与CC_BY_3()
    {
        string text = AllText();

        Assert.Contains("game-icons.net", text);
        Assert.Contains("CC BY 3.0", text);
    }

    [Fact]
    public void 署名里必须列出图标作者()
    {
        string text = AllText();

        // game-icons.net 要求署名到作者。抽查主力几位（Lorc 与 Delapouite 两人就占了我们用到的绝大多数图）。
        Assert.Contains("Lorc", text);
        Assert.Contains("Delapouite", text);
    }

    [Fact]
    public void CC_BY要求标明改动_署名里必须有改动说明()
    {
        Assert.Contains(
            CreditsContent.Sections,
            s => s.Lines.Any(l => l.Contains("改动说明")));
    }

    [Fact]
    public void 每个分节的授权行都不为空()
    {
        // 授权那一行是这一页存在的理由。允许分节没有正文，但**不允许没有授权**。
        Assert.All(CreditsContent.Sections, s => Assert.False(string.IsNullOrWhiteSpace(s.License)));
        Assert.All(CreditsContent.Sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
    }

    [Fact]
    public void 头像的CC0出处也在_虽然它没有署名义务()
    {
        string text = AllText();

        Assert.Contains("OpenGameArt", text);
        Assert.Contains("CC0", text);
    }

    [Fact]
    public void 署名数量与当前图标和头像目录一致()
    {
        string text = AllText();

        Assert.Contains("物品图标（180 张）", text);
        Assert.Contains("泛用幸存者头像（13 张）", text);
        Assert.Contains("具名幸存者头像（7 张）", text);
    }
}
