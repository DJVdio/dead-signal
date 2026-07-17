using System;
using System.IO;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 🔴 <b>把 <c>docs/research/2026-07-14-goldfinger-calibration.md</c> 焊死在引擎上。</b>
///
/// <para><b>为什么要有这个门禁（真实事故·2026-07-17 查明）</b>：那份报告<b>出生即错</b>——
/// <c>991b777</c> 在<b>同一个 commit</b> 里重写了 <c>WeaponTable.cs</c>（163 行）<b>并且</b>重跑了报告，
/// 但报告是在武器表改完<b>之前</b>生成的、改完之后没再重跑就一起提交了 ⇒
/// <b>报告里那组数字，没有任何一个 committed 代码状态产生过</b>
/// （实测：把 991b777 检出到干净 worktree、<c>git clean -xfd</c> 后全新构建，它自己的代码跑出<b>消防斧逐波推进 85.3%</b>，
/// 而它自己提交的报告写 <b>60.9%</b>）。更糟的是 <c>GoldfingerGang.cs</c> 的 [T57]/[T63] <b>用户拍板注释照抄了那组假数</b>，
/// 于是「正面很贵＝60.9%」这个<b>根本没发生过的事实</b>被当成拍板依据写进了代码，<b>而没有任何测试能发现</b>——
/// 这份报告此前不在任何门禁覆盖内（"数值外置零漂移 A/B + Sim MD5" 是<b>人工在 test 之外</b>做的，且不含 goldfinger 模式）。</para>
///
/// <para>⚠️ <b>澄清一个曾经的误判</b>：这<b>不是</b>"后来引擎漂了"。991b777 / 6887fe6 / 151dc8f / 8c5ccdc / 414adee /
/// 816f63f / bd867a8 <b>逐个实测全部 85.3%</b> ⇒ 其后那批「数值外置·零漂移 A/B + Sim MD5」的声明<b>是对的</b>。
/// 锅在"报告没跟着代码重跑"，不在那些 commit。</para>
///
/// <para><b>这道门禁怎么工作</b>：拿 <see cref="GoldfingerCalibration.Measure"/>（报告自己用的<b>同一条码路</b>）实跑，
/// 用 <see cref="GoldfingerCalibration.Row"/>（报告自己用的<b>同一个排版函数</b>）排成行，
/// 跟<b>已提交的报告文件里的那一行逐字比对</b>。⇒ 引擎一改、或有人手改报告、或排版变了，<b>当场红</b>，
/// 而不是像这次一样等到有人去查才发现——<b>红了不要改这里的期望值，去重跑报告
/// （<c>dotnet run --project src/DeadSignal.Sim goldfinger</c>）、然后回 <c>GoldfingerGang.cs</c> 复核 [T57] 那段结论还成不成立。</b></para>
///
/// <para>⚠️ <b>只钉 [T57] 拍板所依赖的那三行（消防斧＝中期玩家口径）</b>，不钉整份报告：整份要 ~53s，
/// 会把 4s 的测试套件拖垮；这三行覆盖了拍板的三条依据（潜行可行 / 正面很贵 / 枪一响还是死）。
/// 每行 ~1s，合计 ~3s。数值本身<b>拟定待调</b>（改平衡属 authored 决策、须用户拍板，不是改这里）。</para>
/// </summary>
public class GoldfingerCalibrationDocTests
{
    private const string DocRelPath = "docs/research/2026-07-14-goldfinger-calibration.md";

    /// <summary>[T57] 的中期玩家口径＝3 人同持消防斧（这一关排在中期，必须按中期手牌算账）。</summary>
    private const string KitSection = "## 3 人同持「消防斧(中期)」";

    /// <summary>从测试程序集所在目录往上走，找到仓库根（含 <see cref="DocRelPath"/> 的那一层）。</summary>
    private static string FindDoc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, DocRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"从 {AppContext.BaseDirectory} 逐层往上都没找到 {DocRelPath}——" +
            "报告是这道门禁的对账基准，它不该消失。");
    }

    /// <summary>取报告里「消防斧」小节下、以 <paramref name="rowTitle"/> 开头的那一行（原样，不去空白）。</summary>
    private static string DocRow(string rowTitle)
    {
        string[] lines = File.ReadAllLines(FindDoc());
        int start = Array.FindIndex(lines, l => l.StartsWith(KitSection, StringComparison.Ordinal));
        Assert.True(start >= 0, $"报告里找不到小节「{KitSection}」——报告结构被改了？");

        for (int i = start + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            if (lines[i].TrimStart().StartsWith(rowTitle, StringComparison.Ordinal))
            {
                return lines[i];
            }
        }

        throw new InvalidOperationException($"报告的「{KitSection}」小节里找不到「{rowTitle}」那一行。");
    }

    /// <summary>「消防斧(中期)」＝报告 kits 表里的同一把（<c>WeaponTable.Axe()</c>）——别另造一把，否则对账就假了。</summary>
    private static Weapon Kit() => WeaponTable.Axe();

    /// <summary>
    /// 🔴 <b>[T57] 依据①「潜行清哨才是那条可行的路」</b>：报告里这一行必须与引擎实跑逐字一致。
    /// </summary>
    [Fact]
    public void 报告的消防斧逐个清哨行必须与引擎实跑逐字一致()
    {
        string expected = GoldfingerCalibration.Row(
            "逐个清哨 1×8",
            GoldfingerCalibration.OneByOne.Count,
            GoldfingerCalibration.Measure(Kit(), GoldfingerCalibration.OneByOne, injured: true));

        Assert.Equal(expected, DocRow("逐个清哨"));
    }

    /// <summary>
    /// 🔴 <b>[T57] 依据②「正面仍然很贵」</b>——这条正是被 born-stale 报告写坏的那一格（报告曾写 60.9%、真值 85.3%）。
    /// <para>⚠️ 读这一行时记住 §2 通则③<b>「胜率不是成本」</b>：胜率只说"能不能站着走出这一场"，
    /// 同一行右边的<b>阵亡 / 永久残缺 / 惨胜 / 全身而退</b>才是账单。</para>
    /// </summary>
    [Fact]
    public void 报告的消防斧逐波推进行必须与引擎实跑逐字一致()
    {
        string expected = GoldfingerCalibration.Row(
            "逐波推进 2→3→3",
            GoldfingerCalibration.PushWaves.Count,
            GoldfingerCalibration.Measure(Kit(), GoldfingerCalibration.PushWaves, injured: true));

        Assert.Equal(expected, DocRow("逐波推进"));
    }

    /// <summary>
    /// 🔴 <b>[T57] 依据③＝authored 红线「枪一响还是死」</b>：惊动全据点必须仍是近乎必死。
    /// <para>这一格塌了（比如涨到几十个百分点）就意味着"开枪没代价"，整关的噪音设计失去意义 ⇒ <b>必须上抛用户，不是改期望值。</b></para>
    /// </summary>
    [Fact]
    public void 报告的消防斧惊动全据点行必须与引擎实跑逐字一致()
    {
        string expected = GoldfingerCalibration.Row(
            "惊动全据点 8",
            GoldfingerCalibration.AllAtOnce.Count,
            GoldfingerCalibration.Measure(Kit(), GoldfingerCalibration.AllAtOnce, injured: true));

        Assert.Equal(expected, DocRow("惊动全据点"));
    }

    /// <summary>
    /// 波次分组＝<c>SpawnGoldfingerGuards</c> 的空间布点语义（近入口 2 / 中段 3 / 深处 3），且三种打法都用满 8 人编制。
    /// 分组一改，上面三行的口径就变了 ⇒ 这里先钉住，免得报告"数对了但打法悄悄换了"。
    /// </summary>
    [Fact]
    public void 三种打法的波次分组是满编八人且纵深分组不变()
    {
        Assert.Equal(new[] { 2, 3, 3 }, GoldfingerCalibration.PushWaves.Select(w => w.Count).ToArray());
        Assert.Equal(8, GoldfingerCalibration.PushWaves.Sum(w => w.Count));

        Assert.Single(GoldfingerCalibration.AllAtOnce);
        Assert.Equal(8, GoldfingerCalibration.AllAtOnce[0].Count);

        Assert.Equal(8, GoldfingerCalibration.OneByOne.Count);
        Assert.All(GoldfingerCalibration.OneByOne, w => Assert.Single(w));
    }
}
