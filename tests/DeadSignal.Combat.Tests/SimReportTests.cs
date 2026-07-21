using System;
using System.Linq;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 🔴 焊死 <see cref="SimReport"/> 的出处戳判脏范围（<c>SimReport.SettlementScopePaths</c>）。
///
/// <para><b>为什么要这道焊缝（sweep-research-b 提的加固）</b>：整个"clean 戳能不能拿到"的命门，
/// 就是判脏范围<b>不含 <c>docs/</c></b>——报告自己落 <c>docs/research/</c>，若有人手一抖把 docs 纳入判脏，则：
/// ① 跑一次写脏自己、二次误报；② 更致命：<b>git-ops 提交代码后重跑报告时会永远因 docs 脏而标 dirty ⇒ clean 戳永远拿不到
/// ⇒ 静默退化回裸 bool</b>（读者学会无视、跟没有一样，正是我们要治的病）。而且它<b>不报错</b>，只默默退化。
/// 这道测试把这个隐性前提钉死：碰它就红。</para>
///
/// <para>反过来，判脏范围<b>必须含</b> Sim 真读得到的三处（<c>src</c>/<c>godot/scripts</c>/<c>godot/data</c>）——
/// 少任何一处就会对"改了参与结算的代码/配置"误报 clean，那才是 born-stale 病本身。</para>
/// </summary>
public class SimReportTests
{
    /// <summary>🔴 判脏范围绝不能含 docs/ 或 .tabb/——含了 clean 戳就永远拿不到、静默退化回裸 bool。</summary>
    [Theory]
    [InlineData("docs")]
    [InlineData(".tabb")]
    public void 判脏范围绝不含报告自身与黑板目录(string forbidden)
    {
        Assert.DoesNotContain(
            SimReport.SettlementScopePaths,
            p => p == forbidden
                 || p.StartsWith(forbidden + "/", StringComparison.Ordinal)
                 || p.StartsWith(forbidden + "\\", StringComparison.Ordinal));
    }

    /// <summary>
    /// 判脏范围必须覆盖 Sim 真读得到的三处结算源：<c>src</c>（引擎+harness）、<c>godot/scripts</c>（Link 进 Sim 的纯逻辑）、
    /// <c>godot/data</c>（外置数值 JSON，运行时读）。少一处就会对"改了结算代码/配置"误报 clean＝born-stale 病本身。
    /// </summary>
    [Theory]
    [InlineData("src")]
    [InlineData("godot/scripts")]
    [InlineData("godot/data")]
    public void 判脏范围必须覆盖全部Sim结算源(string required)
    {
        Assert.Contains(required, SimReport.SettlementScopePaths);
    }

    /// <summary>范围非空——空范围＝永远 clean＝比裸 bool 还坏（谁都骗）。</summary>
    [Fact]
    public void 判脏范围非空()
    {
        Assert.NotEmpty(SimReport.SettlementScopePaths);
    }

    [Theory]
    [InlineData("src/DeadSignal.Combat")]
    [InlineData("src/DeadSignal.Sim")]
    [InlineData("godot/data/config")]
    public void 核心报告输入指纹覆盖引擎Harness与战斗配置(string required)
    {
        Assert.Contains(required, SimReport.CoreCombatReportInputPaths);
    }
}
