using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 金手指帮根据地「发现点」解析纯逻辑单测：discoveryId → (flag/日记书/环境叙事)，
// 已发现（flag 已置）返回 null 防重复。根据地有两具尸体（用户拍板，设计文档 §8.7）：
//   · 帮众尸体（被克莉丝汀反杀）：恒在、各分支可见，配日记A；
//   · 克莉丝汀本人尸体：仅复仇线（christine_left_for_revenge）成立，点名叙事、不再另给书。
// 全部脱 Godot：只用 StoryFlags + GoldfingerDiscovery 的纯字符串状态。
public class GoldfingerDiscoveryTests
{
    [Fact]
    public void UnknownDiscoveryId_ReturnsNull()
    {
        Assert.Null(GoldfingerDiscovery.Resolve("no_such_point", new StoryFlags()));
    }

    [Fact]
    public void GangMemberCorpse_FirstFind_GrantsDiaryAAndFlag()
    {
        var f = new StoryFlags();
        DiscoveryResult? r = GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GangMemberCorpseId, f);

        Assert.NotNull(r);
        Assert.Equal(GoldfingerDiscovery.GangMemberCorpseFlag, r!.Value.StoryFlag);
        Assert.Equal(GoldfingerDiscovery.DiaryABookId, r.Value.BookId);
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Title));
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Narrative));
    }

    [Fact]
    public void GangMemberCorpse_VisibleRegardlessOfRevenge()
    {
        // 帮众尸体时间线早于探索，不按克莉丝汀去向门控——复仇与否都能解析且产物一致。
        DiscoveryResult noRevenge =
            GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GangMemberCorpseId, new StoryFlags())!.Value;

        var revenge = new StoryFlags();
        revenge.Set(GoldfingerDiscovery.ChristineLeftForRevengeFlag, "true");
        DiscoveryResult withRevenge =
            GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GangMemberCorpseId, revenge)!.Value;

        Assert.Equal(noRevenge.StoryFlag, withRevenge.StoryFlag);
        Assert.Equal(noRevenge.BookId, withRevenge.BookId);
        Assert.Equal(noRevenge.Narrative, withRevenge.Narrative);
    }

    [Fact]
    public void ChristineCorpse_OnlyResolvableInRevengeLine()
    {
        // 非复仇线：克莉丝汀本人尸体点不成立（关卡也不铺出），Resolve 返回 null。
        Assert.Null(GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, new StoryFlags()));

        // 复仇线：成立，置克莉丝汀尸体 flag、不再另给书（空 BookId），有点名叙事。
        var revenge = new StoryFlags();
        revenge.Set(GoldfingerDiscovery.ChristineLeftForRevengeFlag, "true");
        DiscoveryResult? r = GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, revenge);

        Assert.NotNull(r);
        Assert.Equal(GoldfingerDiscovery.ChristineCorpseFlag, r!.Value.StoryFlag);
        Assert.Equal(GoldfingerDiscovery.NoBookId, r.Value.BookId);
        Assert.True(string.IsNullOrEmpty(r.Value.BookId));
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Narrative));
    }

    [Fact]
    public void GangAndChristineCorpse_AreDistinctNarratives()
    {
        var revenge = new StoryFlags();
        revenge.Set(GoldfingerDiscovery.ChristineLeftForRevengeFlag, "true");

        DiscoveryResult gang =
            GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GangMemberCorpseId, revenge)!.Value;
        DiscoveryResult christine =
            GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, revenge)!.Value;

        Assert.NotEqual(gang.Narrative, christine.Narrative);
        Assert.NotEqual(gang.StoryFlag, christine.StoryFlag);
    }

    [Fact]
    public void GordonHanged_FirstFind_GrantsDiaryBAndHangedFlag()
    {
        var f = new StoryFlags();
        DiscoveryResult? r = GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GordonHangedId, f);

        Assert.NotNull(r);
        Assert.Equal(GoldfingerDiscovery.GordonHangedFlag, r!.Value.StoryFlag);
        Assert.Equal(GoldfingerDiscovery.DiaryBBookId, r.Value.BookId);
    }

    [Fact]
    public void AlreadyDiscovered_ReturnsNull()
    {
        // 帮众尸体已发现 → null。
        var f = new StoryFlags();
        f.Set(GoldfingerDiscovery.GangMemberCorpseFlag, "true");
        Assert.Null(GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GangMemberCorpseId, f));

        // 克莉丝汀本人尸体：复仇线成立但已发现 → null。
        var revenge = new StoryFlags();
        revenge.Set(GoldfingerDiscovery.ChristineLeftForRevengeFlag, "true");
        revenge.Set(GoldfingerDiscovery.ChristineCorpseFlag, "true");
        Assert.Null(GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, revenge));

        // 哥顿上吊尸已发现 → null。
        var g = new StoryFlags();
        g.Set(GoldfingerDiscovery.GordonHangedFlag, "true");
        Assert.Null(GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GordonHangedId, g));
    }

    [Fact]
    public void DiaryBookIds_MatchBookLibraryEntries()
    {
        // 发现点给的书 id 必须在内置书目里解析得到（否则 LootApplication 会静默跳过）。
        var snapshot = System.Linq.Enumerable.ToDictionary(BookLibrary.All(), b => b.Id);
        Assert.True(snapshot.ContainsKey(GoldfingerDiscovery.DiaryABookId));
        Assert.True(snapshot.ContainsKey(GoldfingerDiscovery.DiaryBBookId));
    }
}
