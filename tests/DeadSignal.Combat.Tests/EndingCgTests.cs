using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 正史结局 CG 文本定稿守护（<see cref="EndingCg"/>）：CG 文本非空、分段承载。
/// <para>🔴 曾有 3 条路由测试（<c>ForGameOver_PlainWipe_IsNormal</c> / <c>ForGameOver_SiegeActive_IsHordeSiege</c> /
/// <c>ForKind_MapsToCorrectCg</c>）随 <c>EndingKind</c>/<c>ForGameOver</c>/<c>ForKind</c> 全灭结局路由**整条退役**
/// （[用户裁决·选项B]）：那条路由生产不可达（军袭/尸潮均走南逃谢幕、不经全灭判定；<c>_siegeActive</c>/<c>_militaryRaidWipeContext</c>
/// 恒 false），测试靠**手喂生产达不到的入参**才绿 —— 典型"死路由测绿"幻觉，连路由带测试一并删除。</para>
/// <para>**authored CG 文案零受影响**：下方 <see cref="AllCgs_AreNonEmpty_AndSegmentsNonBlank"/> 仍逐段守护
/// CG①(HordeSiege 7 段) / CG-A(MilitaryRaidMassacre 6 段) 等全部正文非空——路由删了，文案守护留着。</para>
/// </summary>
public class EndingCgTests
{
    // —— CG 文本非空（authored 正文守护：路由退役后，这是 CG① / CG-A 文案仍在册的护栏）——

    [Fact]
    public void AllCgs_AreNonEmpty_AndSegmentsNonBlank()
    {
        foreach (var cg in new[]
                 {
                     EndingCg.HordeSiege, EndingCg.MilitaryWipe, EndingCg.SouthEscape,
                     EndingCg.MilitaryRaidMassacre, EndingCg.SouthEscapeFarewell, // 南逃谢幕 CG-A/CG-B
                 })
        {
            Assert.NotEmpty(cg);
            foreach (var seg in cg)
                Assert.False(string.IsNullOrWhiteSpace(seg));
        }
    }

    [Fact]
    public void MilitaryWipe_IsAliasOf_MilitaryRaidMassacre()
        // 旧名 MilitaryWipe 已重写为 CG-A 屠营文本（军人屠营+半残南逃），与新名同引用。
        // 路由退役后本别名仅剩单测引用，保留待 CG-A 文案接线的 [DECISION] 裁完再定去留。
        => Assert.Same(EndingCg.MilitaryRaidMassacre, EndingCg.MilitaryWipe);
}
