using System.IO;
using System;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>耗子三级接线后的 Wiki/配置文案护栏：避免代码已生效、玩家页面却继续写「未接线」。</summary>
public sealed class RatWikiWiringTests
{
    [Fact]
    public void RatL3_DocsAndSchema_DoNotClaimUnwired()
    {
        string[] files =
        {
            "docs/wiki/data/characters.json",
            "docs/wiki/data/character-stats.json",
            "docs/wiki/data/bundle.js",
            "godot/scripts/PerkConfig.cs",
            "godot/data/config/perks.schema.json",
        };

        foreach (string file in files)
        {
            Assert.True(File.Exists(RepoFile(file)), $"缺少预期的耗子配置/文档文件：{file}");
        }

        string stats = File.ReadAllText(RepoFile("docs/wiki/data/character-stats.json"));
        Assert.DoesNotContain("3 级 黑暗隐匿点加成（未接线）", stats);
        Assert.DoesNotContain("3 级 破隐先手额外伤害（未接线）", stats);

        string character = File.ReadAllText(RepoFile("docs/wiki/data/characters.json"));
        Assert.DoesNotContain("这两条是引擎里还没有", character);
        Assert.DoesNotContain("L3 两条效果未接线", character);

        string config = File.ReadAllText(RepoFile("godot/scripts/PerkConfig.cs"));
        Assert.DoesNotContain("黑暗隐匿点（未接线）", config);
        Assert.DoesNotContain("破隐先手攻击额外伤害（未接线）", config);

        string schema = File.ReadAllText(RepoFile("godot/data/config/perks.schema.json"));
        Assert.DoesNotContain("黑暗隐匿点加成比例（未接线）", schema);
        Assert.DoesNotContain("破隐先手攻击额外伤害比例（未接线）", schema);
    }

    private static string RepoFile(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        return relative;
    }
}
