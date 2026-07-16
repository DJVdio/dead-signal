using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 三结局 CG 定稿（<see cref="EndingCg"/>）+ 全灭结局路由（<see cref="EndingCg.ForGameOver"/>）。
/// CG 文本非空、分段承载；路由：军袭全灭上下文 &gt; 尸潮围攻 &gt; 普通全灭。
/// </summary>
public class EndingCgTests
{
    // —— 结局路由 ——

    [Fact]
    public void ForGameOver_PlainWipe_IsNormal()
        => Assert.Equal(EndingKind.Normal, EndingCg.ForGameOver(siegeActive: false, militaryRaidWipe: false));

    [Fact]
    public void ForGameOver_SiegeActive_IsHordeSiege()
        => Assert.Equal(EndingKind.HordeSiege, EndingCg.ForGameOver(siegeActive: true, militaryRaidWipe: false));

    [Fact]
    public void ForGameOver_MilitaryWipe_IsMilitaryWipe()
        => Assert.Equal(EndingKind.MilitaryWipe, EndingCg.ForGameOver(siegeActive: false, militaryRaidWipe: true));

    [Fact]
    public void ForGameOver_MilitaryWipe_TakesPrecedenceOverSiege()
        => Assert.Equal(EndingKind.MilitaryWipe, EndingCg.ForGameOver(siegeActive: true, militaryRaidWipe: true));

    // —— CG 文本非空 ——

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
        => Assert.Same(EndingCg.MilitaryRaidMassacre, EndingCg.MilitaryWipe);

    [Fact]
    public void ForKind_MapsToCorrectCg_NormalIsEmpty()
    {
        Assert.Same(EndingCg.HordeSiege, EndingCg.ForKind(EndingKind.HordeSiege));
        Assert.Same(EndingCg.MilitaryWipe, EndingCg.ForKind(EndingKind.MilitaryWipe));
        Assert.Empty(EndingCg.ForKind(EndingKind.Normal));
    }
}
