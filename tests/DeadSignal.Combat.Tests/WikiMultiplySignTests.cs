using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 🔴 <b>wiki 里的乘号一律用 ASCII 星号 <c>*</c>，不用 <c>×</c></b>（用户拍板：「并且规定 wiki 中的乘全都用 *」）。
///
/// <para><b>为什么这条必须有护栏，而不是「改一次就完了」</b>：wiki 的数据不是手写的，是
/// <c>tools/WikiExtract</c> <b>从 C# 代码生成的</b>。只要有人在抽取器里写下一句
/// <c>$"{材料名}×{数量}"</c>，下一次重跑就把 <c>×</c> 写回 JSON —— 而且**不会有任何人发现**，
/// 因为那是一次"正常的重跑"。所以真正要钉死的是**生成器**，不是生成物。</para>
///
/// <para><b>这里为什么连 JSON 也一起查</b>：<c>TableMerge</c> 的语义是**表赢代码** —— 行值一律以
/// 磁盘上的 JSON 为准。所以光把抽取器改成 <c>*</c>，表里旧的 <c>×</c> 依然会赢、一个字都不会变
/// （还会每跑一次报一条永远修不掉的假漂移）。**两边必须同时是 <c>*</c>，这条规则才真的成立。**</para>
///
/// <para>⚠️ <b>唯一不算乘号的 <c>×</c>：网页上行尾那个「删除」叉</b>（<c>index.html</c> 的删除钮图标）。
/// 那是个图标，不是乘法，<b>不许被这条规则误伤</b> —— 下面有一条测试专门把它钉住，
/// 免得哪天有人拿 sed 一刀切把删除钮也变成 <c>*</c>。</para>
/// </summary>
public class WikiMultiplySignTests
{
    private const char Times = '×';

    /// <summary>从**本源文件路径**往上找仓库根（不用 <c>AppContext.BaseDirectory</c>——它随输出目录跑）。</summary>
    private static DirectoryInfo RepoRoot([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "WikiExtract")))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, "从测试源文件向上未找到仓库根（tools/WikiExtract）");
        return dir!;
    }

    private static IEnumerable<string> LinesWithTimes(string path)
        => File.ReadAllLines(path)
            .Select((text, i) => (text, no: i + 1))
            .Where(l => l.text.Contains(Times))
            .Select(l => $"{Path.GetFileName(path)}:{l.no}: {l.text.Trim()}");

    /// <summary>
    /// <b>生成器</b>：抽取器源码里一个 <c>×</c> 都不许有。
    /// <para>包含注释与 hint 文案 —— 因为 hint 是**会被写进 JSON 给用户看的**（列说明），
    /// 而注释里的 <c>×</c> 是"下一个人照着抄"的种子。全清掉，这条规则才守得住。</para>
    /// </summary>
    [Fact]
    public void 抽取器源码里不许出现乘号叉()
    {
        DirectoryInfo root = RepoRoot();
        string[] offenders = Directory
            .EnumerateFiles(Path.Combine(root.FullName, "tools", "WikiExtract"), "*.cs", SearchOption.TopDirectoryOnly)
            .SelectMany(LinesWithTimes)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "wiki 的乘号一律用 ASCII `*`。抽取器里还留着 `×`——重跑一次就会把它写回 JSON：\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// <b>生成物</b>：wiki 的数据里一个 <c>×</c> 都不许有（材料列、改装件的数值改动、倍率单位、角色专属效果…）。
    /// <para><c>bundle.js</c> 是 JSON 的打包降级数据源，一并查——它跟 JSON 必须一致。</para>
    /// </summary>
    [Fact]
    public void wiki数据里不许出现乘号叉()
    {
        DirectoryInfo root = RepoRoot();
        string dataDir = Path.Combine(root.FullName, "docs", "wiki", "data");
        Assert.True(Directory.Exists(dataDir), $"wiki 数据目录不该消失：{dataDir}");

        string[] offenders = Directory
            .EnumerateFiles(dataDir, "*.json", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(dataDir, "*.js", SearchOption.TopDirectoryOnly))
            .OrderBy(p => p, StringComparer.Ordinal)
            .SelectMany(LinesWithTimes)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "wiki 的乘号一律用 ASCII `*`（材料写成 `铁*3`，倍率写成 `*1.2`）。这些地方还是 `×`：\n"
            + string.Join("\n", offenders.Take(40)));
    }

    /// <summary>
    /// <b>前端</b>：<c>mult</c> 列（倍率）在网页上是**现拼**出来的——JSON 里存的是数字 <c>0.75</c>，
    /// 那个乘号是渲染时加的。所以它在抽取器和 JSON 里都搜不到，**必须单独钉一条**，
    /// 否则倍率列在网页上照样显示 <c>×0.75</c>。
    /// </summary>
    [Fact]
    public void 网页渲染倍率列用星号()
    {
        string html = File.ReadAllText(Path.Combine(RepoRoot().FullName, "docs", "wiki", "index.html"));

        Assert.DoesNotContain("\"×\" + Number(v)", html);
        Assert.Contains("\"*\" + Number(v)", html);
    }

    /// <summary>
    /// 🔴 <b>反向护栏：行尾「删除」钮的那个叉不是乘号，不许被改成 <c>*</c></b>。
    /// <para>这条是防**过度修正**的：上面三条都在赶尽杀绝 <c>×</c>，很容易让下一个人（或一条 sed）
    /// 顺手把删除钮的图标也换掉，于是网页上每行行尾冒出一个 <c>*</c>——那是图标，不是乘法。</para>
    /// </summary>
    [Fact]
    public void 删除钮的叉不是乘号不许误伤()
    {
        string html = File.ReadAllText(Path.Combine(RepoRoot().FullName, "docs", "wiki", "index.html"));

        Assert.Contains("tomb ? \"↩\" : \"×\"", html);
    }
}
