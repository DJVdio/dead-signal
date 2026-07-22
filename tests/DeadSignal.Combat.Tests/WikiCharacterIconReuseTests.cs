using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>Wiki 角色缩略图复用游戏已有肖像，禁止另画一套后悄悄走样。</summary>
public sealed class WikiCharacterIconReuseTests
{
    [Theory]
    [InlineData("sam", "sam")]
    [InlineData("nordi", "notty")]
    [InlineData("doug", "doug")]
    [InlineData("nightingale", "nightingale")]
    [InlineData("christine", "christine")]
    [InlineData("pete", "pete")]
    [InlineData("rat", "rat")]
    public void Wiki缩略图与游戏现有肖像逐字节相同(string wikiName, string portraitName)
    {
        string wiki = RepoFile($"godot/assets/items/characters/{wikiName}.png");
        string portrait = RepoFile($"godot/assets/portraits/named/{portraitName}.png");

        Assert.True(File.Exists(wiki), $"缺 Wiki 角色缩略图：{wikiName}");
        Assert.Equal(File.ReadAllBytes(portrait), File.ReadAllBytes(wiki));
    }

    [Fact]
    public void Wiki用裁切显示长肖像而不是强行压扁()
    {
        string html = File.ReadAllText(RepoFile("docs/wiki/index.html"));
        Assert.Contains("object-fit: cover", html);
        Assert.Contains("object-position: 50% 22%", html);
    }

    private static string RepoFile(string relative, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative)))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }
}
