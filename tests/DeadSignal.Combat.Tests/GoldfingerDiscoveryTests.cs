using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 金手指帮根据地「发现点」解析纯逻辑单测：discoveryId → (flag/日记书/环境叙事)，
// 已发现（flag 已置）返回 null 防重复，克莉丝汀尸体按支线 flag（去复仇而死）分支措辞。
// 全部脱 Godot：只用 StoryFlags + GoldfingerDiscovery 的纯字符串状态。
public class GoldfingerDiscoveryTests
{
    [Fact]
    public void UnknownDiscoveryId_ReturnsNull()
    {
        Assert.Null(GoldfingerDiscovery.Resolve("no_such_point", new StoryFlags()));
    }

    [Fact]
    public void ChristineCorpse_FirstFind_GrantsDiaryAAndCorpseFlag()
    {
        var f = new StoryFlags();
        DiscoveryResult? r = GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, f);

        Assert.NotNull(r);
        Assert.Equal(GoldfingerDiscovery.ChristineCorpseFlag, r!.Value.StoryFlag);
        Assert.Equal(GoldfingerDiscovery.DiaryABookId, r.Value.BookId);
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Title));
        Assert.False(string.IsNullOrWhiteSpace(r.Value.Narrative));
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
        var f = new StoryFlags();
        f.Set(GoldfingerDiscovery.ChristineCorpseFlag, "true");
        Assert.Null(GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, f));

        f.Set(GoldfingerDiscovery.GordonHangedFlag, "true");
        Assert.Null(GoldfingerDiscovery.Resolve(GoldfingerDiscovery.GordonHangedId, f));
    }

    [Fact]
    public void ChristineCorpse_NarrativeBranchesOnLeftForRevengeFlag()
    {
        // 去复仇而死：点名措辞；通用措辞：无名遗体。两者正文必须不同。
        DiscoveryResult generic =
            GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, new StoryFlags())!.Value;

        var revenge = new StoryFlags();
        revenge.Set(GoldfingerDiscovery.ChristineLeftForRevengeFlag, "true");
        DiscoveryResult named =
            GoldfingerDiscovery.Resolve(GoldfingerDiscovery.ChristineCorpseId, revenge)!.Value;

        Assert.NotEqual(generic.Narrative, named.Narrative);
        // 两分支产物一致（同 flag、同书），只叙事措辞不同。
        Assert.Equal(generic.StoryFlag, named.StoryFlag);
        Assert.Equal(generic.BookId, named.BookId);
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
