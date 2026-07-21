using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 三份会被设计讨论直接引用的核心战斗报告必须能证明自己来自哪份代码，且与当前结算输入逐字节同源。
/// 数字变化时应重跑报告并复核结论，不能手改测试期望或只改报告中的一两格。
/// </summary>
public class CoreCalibrationReportTrustTests
{
    private static readonly string[] Reports =
    {
        "docs/research/2026-07-13-weapon-recalib.md",
        "docs/research/2026-07-13-shotgun-calibration.md",
        "docs/research/2026-07-13-zombie-cloth.md",
    };

    [Fact]
    public void 三份核心报告都有Commit戳且对应当前结算输入()
    {
        string expectedInputSha = SimReport.CoreCombatReportInputFingerprint();
        Assert.Matches("^[0-9a-f]{64}$", expectedInputSha);

        foreach (string relative in Reports)
        {
            string path = FindRepoFile(relative);
            string document = File.ReadAllText(path);
            string firstLine = document.Split('\n', 2)[0];
            Match stamp = Regex.Match(firstLine,
                "^<!-- sim-provenance commit=(?<commit>[0-9a-f]{40}) commit-date=(?<date>[^ ]+) " +
                "settlement=(?<settlement>clean|dirty:[0-9]+) input-sha256=(?<input>[0-9a-f]{64}) " +
                "report-sha256=(?<report>[0-9a-f]{64}) -->$");

            Assert.True(stamp.Success, $"{relative} 缺少完整的 commit/结算状态/输入与正文指纹出处戳，请用对应 Sim 模式整份重跑。");
            Assert.Equal(expectedInputSha, stamp.Groups["input"].Value);

            int bodyStart = document.IndexOf("\n# ", StringComparison.Ordinal);
            Assert.True(bodyStart >= 0, $"{relative} 找不到机器生成正文标题。");
            string body = document[(bodyStart + 1)..];
            Assert.Equal(stamp.Groups["report"].Value, SimReport.ReportBodyFingerprint(body));
        }
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"找不到报告 {relative}");
    }
}
