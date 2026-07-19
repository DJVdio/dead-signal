using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 已撤销的系统不应再以规划、入口或残留消费逻辑的形式回归。
/// 这组护栏只扫生产代码和设计文档；测试自身的断言文字不算产品入口。
/// </summary>
public sealed class RetiredSystemRemovalTests
{
    private static readonly Regex RetiredToken = new(
        "m" + "orale|" + "\u58eb\u6c14",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void ProductionCodeAndDesignDocsContainNoRetiredEntry()
    {
        string root = RepoRoot();
        string[] roots =
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "godot", "scripts"),
            Path.Combine(root, "docs"),
        };

        var files = roots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => IsTextFile(path));

        foreach (string file in files)
        {
            string text = File.ReadAllText(file);
            Assert.False(
                RetiredToken.IsMatch(text),
                $"已撤销系统残留在生产/文档入口：{Path.GetRelativePath(root, file)}");
        }
    }

    [Fact]
    public void HungerStateRemainsIndependent()
    {
        string path = Path.Combine(RepoRoot(), "godot", "scripts", "HungerState.cs");
        Assert.True(File.Exists(path), "饥饿系统纯逻辑仍必须保留");
        Assert.Contains("class HungerState", File.ReadAllText(path));
    }

    private static bool IsTextFile(string path)
        => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    private static string RepoRoot([CallerFilePath] string here = "")
    {
        DirectoryInfo? dir = new(Path.GetDirectoryName(here)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "godot")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录");
    }
}
