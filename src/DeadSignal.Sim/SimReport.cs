using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// 全局 namespace：与本目录所有 harness（GoldfingerCalibration/Arena/…均无 namespace 声明）一致，
// 免得它们调 SimReport 还要额外 using；其中 GoldfingerCalibration 被测试工程 Link，跨工程也要能无痛解析。

/// <summary>
/// 机器生成报告的<b>落盘唯一入口</b>：把「我是哪份代码跑出来的」这行**出处戳**焊在每份报告头上。
///
/// <para>
/// 🔴 <b>为什么存在（两起真实事故·别再来第三起）</b>：本仓已查实<b>两份</b> research 报告<b>出生即错</b>
/// （born-stale＝提交那一刻就与自己的代码对不上）：
/// <list type="bullet">
/// <item><c>2026-07-14-goldfinger-calibration.md</c>：<c>991b777</c> 在<b>同一个 commit</b> 里重写了
/// <c>WeaponTable.cs</c>（163 行）<b>并且</b>重跑了报告，但报告是在武器表改完<b>之前</b>生成的、之后没再重跑就一起提交
/// ⇒ 报告写「消防斧逐波推进 60.9%」，而<b>它自己那个 commit 的代码跑出 85.3%</b>。更糟：<c>GoldfingerGang.cs</c> 的
/// [T57] <b>用户拍板注释照抄了那组假数</b>，于是一个从没发生过的"事实"成了拍板依据。</item>
/// <item><c>2026-07-14-lanchester.md</c>：同病。报告与代码输出只差一行——有人<b>手改了一行描述</b>让报告"看起来是新的"，
/// 表格却还是旧的。</item>
/// </list>
/// 两次都靠<b>跨 commit 重跑做考古</b>才判定出来（隔离 worktree、<c>git clean -xfd</c>、全新构建、逐个 commit 探针）——
/// 各花掉一整轮。<b>根因只有一个：报告里没写「我是哪份代码跑的」。这一行字就能把那种考古变成 O(1) 的"读第一行"。</b>
/// </para>
///
/// <para>
/// 🔴 <b>戳里刻意<u>不放</u> wall-clock 生成时刻</b>——这不是疏漏，是硬约束：
/// CLAUDE.md §「数值与仿真纪律」明写零漂移 A/B 判据是<b>「输出逐字节/MD5 一致」</b>，
/// 且 <c>combat-cost</c> 那段的确定性证明就是<b>「固定种子、连跑两次 MD5 相同」</b>。
/// 塞一个每次都变的时间戳<b>会当场且永久废掉这条全项目纪律</b>（此后任何两次跑都不可能 MD5 相等），
/// 还会给每个后来做 A/B 的人埋一个"假漂移"陷阱。
/// <br/>⇒ 改用 <b>commit 日期</b>（<c>%cI</c>）：它由 commit 决定 ⇒ <b>同一状态两次跑逐字节相同、A/B 纪律毫发无损</b>，
/// 而"这报告多旧"照样一眼可读。且对<b>干净</b>工作区而言，<c>commit</c> 已<b>完全决定</b>报告内容
/// （<c>git checkout &lt;sha&gt;</c> 就能原样复现），按下回车的时刻本就不含信息。
/// </para>
///
/// <para>
/// 🔴 <b>结算路径脏时必须自曝其短</b>：born-stale 的成因正是"在脏树上跑完 → 继续改代码 → 没重跑就提交"。
/// 故结算脏时戳标 <c>settlement=dirty:N</c> 并列出脏文件清单。<b>但脏判定只看 Sim 结算路径</b>
/// （<see cref="SettlementScopePaths"/>：<c>src</c>/<c>godot/scripts</c>/<c>godot/data</c>），<b>不看整树、更绝不含 <c>docs/</c></b>——
/// 详见该字段注释：并发下整树永远脏会让裸 bool 退化成常量，而含 docs 会让 clean 戳永远拿不到。
/// </para>
/// </summary>
public static class SimReport
{
    /// <summary>机读戳的行首标记。日后写校验/门禁可直接 grep 它。</summary>
    public const string MachinePrefix = "<!-- sim-provenance ";

    /// <summary>
    /// 🔴 <b>脏判定只看这几个「Sim 结算路径」，不看整个工作区。</b>
    /// <para>
    /// sweep-research-b 实证：多 agent 并发下整树几乎永远脏 ⇒ 裸 dirty bool 退化成常量、读者学会无视＝跟没有一样；
    /// 且<b>「脏」≠「失效」</b>——只改注释的脏树，报告逐字节不变。所以只判 Sim <b>真读得到</b>的东西的<b>保守超集</b>：
    /// </para>
    /// <list type="bullet">
    /// <item><c>src</c>：DeadSignal.Combat 引擎 + DeadSignal.Sim harness 本身。</item>
    /// <item><c>godot/scripts</c>：大量纯逻辑 .cs 被 <c>DeadSignal.Sim.csproj</c> Link 进结算（GoldfingerGang/VisionLogic/各 Config）。</item>
    /// <item><c>godot/data</c>：数值已外置 JSON，Sim 运行时经 <c>CombatConfigFiles.Load</c>/<c>GameConfigFiles.Load</c> 读它 ⇒ 改 JSON 改输出。</item>
    /// </list>
    /// <para>
    /// 🔴 <b>取全 scripts/全 data 而非「精确 Linked 清单」是刻意偏保守</b>：多算几个 Sim 不读的文件只造成「假脏」
    /// （读者看清单自判、无害），<b>绝不造成「假 clean」</b>——假 clean 就是 born-stale 病本身（看着干净、其实数值变了）。
    /// </para>
    /// <para>
    /// 🔴 <b>这里绝不能含 <c>docs/</c> 或 <c>.tabb/</c></b>（<see cref="SimReportTests"/> 焊死这条）：报告自己落
    /// <c>docs/research/</c>，若把 docs 纳入判脏，则「跑一次写脏自己、二次误报」，更致命的是
    /// <b>git-ops 提交代码后重跑报告时永远因 docs 脏而标 dirty ⇒ clean 戳永远拿不到 ⇒ 静默退化回裸 bool</b>。
    /// 正因排除 docs，收口重跑时结算路径干净 ⇒ 戳才拿得到 clean。
    /// </para>
    /// </summary>
    public static readonly string[] SettlementScopePaths = { "src", "godot/scripts", "godot/data" };

    /// <summary>
    /// 三份核心战斗报告真正依赖的输入范围。范围包含纯战斗引擎、全部 Sim harness 与战斗 JSON；
    /// 不含结局/UI 等不会进入这些报告的 Godot 消费层，避免无关改动强迫重跑昂贵蒙特卡洛。
    /// </summary>
    public static readonly string[] CoreCombatReportInputPaths =
    {
        "src/DeadSignal.Combat",
        "src/DeadSignal.Sim",
        "godot/data/config",
    };

    /// <summary>
    /// 出处戳（机读一行 + 人读一段）。<b>确定性</b>：只由 commit 与结算路径是否脏决定，不含 wall-clock。
    /// </summary>
    public static string Stamp(string reportBodySha256 = "unknown")
    {
        // 机读定位用**完整 sha**（sweep-research-b：date 不唯一定位 commit，做 worktree 考古须能直接 checkout/worktree add
        // 到确切状态）；人读行用短 sha 更好认。
        string fullSha = Git("rev-parse", "HEAD");
        string shortSha = Git("rev-parse", "--short", "HEAD");
        string date = Git("show", "-s", "--format=%cI", "HEAD");
        string inputSha = CoreCombatReportInputFingerprint();

        string dirt = Git(new[] { "status", "--porcelain", "--" }
            .Concat(SettlementScopePaths).ToArray());

        if (fullSha.Length == 0)
        {
            // 拿不到 git（不在仓库里跑 / 没装 git）——**照样出戳**，但明说"出处不明"，绝不假装干净。
            return $"{MachinePrefix}commit=unknown settlement=unknown -->\n"
                + "> ⚠️ **本报告出处不明**：生成时拿不到 git 信息（不在仓库里跑？）⇒ 无法判断它由哪份代码跑出 ⇒ **别拿它当依据**。\n";
        }

        string[] dirtyFiles = dirt.Length == 0
            ? Array.Empty<string>()
            : dirt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        bool dirty = dirtyFiles.Length > 0;

        // 机读行：完整 sha（机器定位）+ commit-date（人读多旧）+ 结算脏文件数（不止 bool，好让日后门禁按数值判）。
        string machine =
            $"{MachinePrefix}commit={fullSha} commit-date={date} settlement={(dirty ? $"dirty:{dirtyFiles.Length}" : "clean")}" +
            $" input-sha256={inputSha} report-sha256={reportBodySha256} -->";
        string sha = shortSha;

        if (!dirty)
        {
            return machine + "\n"
                + $"> 📌 本报告由 commit `{sha}`（{Day(date)}）生成，**Sim 结算路径干净**"
                + "（src/ + godot/scripts/ + godot/data/ 无未提交改动；docs/ 与 .tabb/ 脏不影响 Sim 输出）"
                + $" ⇒ `git checkout {sha}` 后重跑即可**原样复现**；对不上就是引擎变了、该重跑并复核结论。\n";
        }

        // 脏：列出**结算脏文件清单**（sweep-research-b 的 A+B 融合——既警告又给证据，读者自己判断这些改动碰没碰结算）。
        //   封顶 20 行防戳过长；证据在手，读者不必猜"是不是只改了注释"。
        const int cap = 20;
        string list = string.Join("\n", dirtyFiles.Take(cap).Select(f => $">   {f}"));
        if (dirtyFiles.Length > cap)
        {
            list += $"\n>   …另 {dirtyFiles.Length - cap} 个";
        }

        return machine + "\n"
            + $"> 🔴 **生成时 Sim 结算路径有 {dirtyFiles.Length} 处未提交改动**，本报告反映的代码状态"
            + $"**没有任何 commit 保存过**，与 commit `{sha}`（{Day(date)}）的实际输出**可能不同**：\n"
            + list + "\n"
            + "> 这正是 `991b777` 那次 born-stale 的成因（报告在代码改完前生成、之后没重跑就一起提交，"
            + "写 60.9%、真值 85.3%，还污染了 [T57] 拍板注释）。\n"
            + "> ⚠️ **但「脏」未必「失效」**：若上面全是注释/无关改动，数值仍可信——**自己看清单判断，或在干净工作区重跑后再引用。**\n";
    }

    /// <summary>把报告落盘：<b>戳在最前</b>，正文原样。<b>所有 harness 都该走这里</b>，别各写各的 File.WriteAllText。</summary>
    public static void Write(string path, string body)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string normalizedBody = body.TrimEnd('\r', '\n') + "\n";
        File.WriteAllText(path, Stamp(ReportBodyFingerprint(normalizedBody)) + normalizedBody);
    }

    /// <summary>报告正文的确定性 SHA-256；用于发现报告表格被手改、截断或只重算了一部分。</summary>
    public static string ReportBodyFingerprint(string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    /// <summary>
    /// 计算核心战斗报告输入的确定性 SHA-256。路径和文件内容都参与摘要；新增、删除、改名或改内容都会使门禁变红。
    /// </summary>
    public static string CoreCombatReportInputFingerprint()
    {
        string root = Git("rev-parse", "--show-toplevel");
        if (root.Length == 0)
        {
            return "unknown";
        }

        string listed = Git(new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--" }
            .Concat(CoreCombatReportInputPaths).ToArray());
        string[] files = listed.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            return "unknown";
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string relative in files)
        {
            string normalized = relative.Replace('\\', '/');
            byte[] pathBytes = Encoding.UTF8.GetBytes(normalized);
            hash.AppendData(pathBytes);
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(File.ReadAllBytes(Path.Combine(root, relative)));
            hash.AppendData(new byte[] { 0 });
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>取 ISO 日期里的 <c>yyyy-MM-dd</c>（人读用；拿不到就原样回退，绝不抛）。</summary>
    private static string Day(string iso) => iso.Length >= 10 ? iso[..10] : iso;

    /// <summary>跑一条 git，取 stdout。<b>任何失败一律回空字符串</b>——出处戳不该把 harness 跑挂。</summary>
    private static string Git(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            string? repoRoot = FindRepositoryRoot();
            if (repoRoot is not null)
            {
                psi.WorkingDirectory = repoRoot;
            }
            foreach (string a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using Process? p = Process.Start(psi);
            if (p is null)
            {
                return string.Empty;
            }

            string stdout = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(10_000))
            {
                return string.Empty;
            }

            return p.ExitCode == 0 ? stdout.Trim() : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string? FindRepositoryRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                string marker = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }
}
