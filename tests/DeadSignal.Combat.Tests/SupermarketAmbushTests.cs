using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 超市「幸存者骗局」纯逻辑单测（[SPEC-B13]）：接触对话一次性、拒绝后内圈敌对可闯入、去重不重复刷敌。
// 全部脱 Godot：只用 StoryFlags + SupermarketAmbush 的纯字符串/旗标状态（空间执行=ChoicePanel/Raider 在 Godot 层）。
public class SupermarketAmbushTests
{
    [Fact]
    public void ShouldOfferContact_FreshFlags_True()
    {
        Assert.True(SupermarketAmbush.ShouldOfferContact(new StoryFlags()));
    }

    [Fact]
    public void ShouldOfferContact_AfterResolved_False()
    {
        var f = new StoryFlags();
        f.Set(SupermarketAmbush.ScamResolvedFlag, "true");
        Assert.False(SupermarketAmbush.ShouldOfferContact(f));
    }

    [Fact]
    public void InnerRingFight_OnlyAfterRefuse_AndNotYetSprung()
    {
        // 未拒绝：内圈不生成战斗（骗局未走拒绝分支）。
        var fresh = new StoryFlags();
        Assert.False(SupermarketAmbush.ShouldSpawnInnerRingFight(fresh));

        // 拒绝后：内圈占着敌人，踏入即开战。
        var refused = new StoryFlags();
        refused.Set(SupermarketAmbush.RefusedFlag, "true");
        Assert.True(SupermarketAmbush.ShouldSpawnInnerRingFight(refused));

        // 已生成过战斗：去重，不再刷敌。
        refused.Set(SupermarketAmbush.AmbushSprungFlag, "true");
        Assert.False(SupermarketAmbush.ShouldSpawnInnerRingFight(refused));
    }

    [Fact]
    public void TrustPath_ResolvedButNotRefused_InnerRingNeverSpawnsSeparately()
    {
        // 轻信路线：接触即伏击（Godot 层置 ScamResolved + AmbushSprung，不置 Refused）——内圈闯入点不再另生成战斗。
        var trusted = new StoryFlags();
        trusted.Set(SupermarketAmbush.ScamResolvedFlag, "true");
        trusted.Set(SupermarketAmbush.AmbushSprungFlag, "true");
        Assert.False(SupermarketAmbush.ShouldOfferContact(trusted));
        Assert.False(SupermarketAmbush.ShouldSpawnInnerRingFight(trusted));
    }

    [Fact]
    public void ChoiceValues_TrustAndRefuse_AreDistinct()
    {
        Assert.NotEqual(SupermarketAmbush.ChoiceTrust, SupermarketAmbush.ChoiceRefuse);
    }

    [Fact]
    public void AmbushRaiderCount_IsPositive()
    {
        Assert.True(SupermarketAmbush.AmbushRaiderCount >= 3);
    }

    [Fact]
    public void DraftTexts_AllNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(SupermarketAmbush.ContactTitle));
        Assert.False(string.IsNullOrWhiteSpace(SupermarketAmbush.ContactPrompt));
        Assert.False(string.IsNullOrWhiteSpace(SupermarketAmbush.TrustLabel));
        Assert.False(string.IsNullOrWhiteSpace(SupermarketAmbush.RefuseLabel));
        Assert.False(string.IsNullOrWhiteSpace(SupermarketAmbush.AmbushSprungNarrative));
        Assert.False(string.IsNullOrWhiteSpace(SupermarketAmbush.RefuseWarningNarrative));
        Assert.False(string.IsNullOrWhiteSpace(SupermarketAmbush.InnerRingBreachNarrative));
    }
}
