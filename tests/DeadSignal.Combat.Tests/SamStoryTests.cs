using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 山姆 authored 背景的可玩化落地（纯逻辑侧）：
///   ① 开局左手缺小指 + 无名指（"小英雄"的代价 —— 九岁救诺蒂被疯狗咬掉）→ 走引擎既有切除通则，不开豁免不加码；
///   ② 花衬衫（祖母尸体上那件）作为贴身层护甲进表；
///   ③ 祖母尸体的营地叙事调查点。
/// 数值皆"拟定待调"，测试锁的是规则形态与 authored 事实。
/// </summary>
public class SamStoryTests
{
    private static Body NewBody() => CombatData.NewHumanoidBody();

    // ---------------- ① 山姆开局左手残缺 ----------------

    [Fact]
    public void Sam_StartsWithLeftPinkyAndRingFingerSevered()
    {
        IReadOnlyList<string> severed = SurvivorBackstory.SeveredAtStart(SurvivorBackstory.Sam);

        Assert.Equal(2, severed.Count);
        Assert.Contains(HumanBody.LeftPinky, severed);
        Assert.Contains(HumanBody.LeftRing, severed);
    }

    [Fact]
    public void ApplyTo_Sam_SeversExactlyTwoLeftFingers_AndLeavesHandItself()
    {
        Body body = NewBody();

        SurvivorBackstory.ApplyTo(SurvivorBackstory.Sam, body);

        Assert.True(body.IsSevered(HumanBody.LeftPinky));
        Assert.True(body.IsSevered(HumanBody.LeftRing));
        // 手掌本体与其余三指健在：他丢的是两根手指，不是一只手。
        Assert.False(body.IsGone(HumanBody.LeftHand));
        Assert.False(body.IsGone(HumanBody.LeftThumb));
        Assert.False(body.IsGone(HumanBody.LeftIndex));
        Assert.False(body.IsGone(HumanBody.LeftMiddle));
        // 右手完好无损。
        Assert.False(body.IsGone(HumanBody.RightHand));
        Assert.False(body.IsGone(HumanBody.RightPinky));
        Assert.False(body.IsGone(HumanBody.RightRing));
        Assert.False(body.IsDead);
    }

    [Fact]
    public void ApplyTo_Sam_OperationPenalty_FollowsEngineFingerRule_NoExemptionNoExtra()
    {
        Body body = NewBody();

        SurvivorBackstory.ApplyTo(SurvivorBackstory.Sam, body);

        // 引擎通则：未失去的手按失去手指累加 −7%/指 → 两指 = −14% 操作能力（出手间隔 ÷0.86）。
        // 不为山姆开后门豁免，也不额外加惩罚。
        Assert.Equal(0.14, body.DisabilityModifiers.OperationPenalty, 6);
        // 手指不碰腿脚：移动能力不受影响。
        Assert.Equal(0.0, body.DisabilityModifiers.MobilityPenalty, 6);
    }

    [Fact]
    public void ApplyTo_Sam_StillHoldsWeaponsInBothHands()
    {
        Body body = NewBody();
        SurvivorBackstory.ApplyTo(SurvivorBackstory.Sam, body);

        // 持械约束只看"断手"，不看断指——左手还在，双持/双手武器一律照常。
        var loadout = new WeaponLoadout(
            leftHandLost: body.IsGone(HumanBody.LeftHand),
            rightHandLost: body.IsGone(HumanBody.RightHand));

        Assert.False(loadout.LeftHandLost);
        Assert.True(loadout.EquipToHand(CombatData.Pistol(), Hand.Right));
        Assert.True(loadout.EquipToHand(CombatData.Dagger(), Hand.Left));
    }

    [Fact]
    public void ApplyTo_Sam_LeftGloveSlotStillUsable()
    {
        Body body = NewBody();
        SurvivorBackstory.ApplyTo(SurvivorBackstory.Sam, body);
        IReadOnlySet<string> severed = body.Parts.Keys.Where(body.IsGone).ToHashSet();

        // 手套挂"手"槽（锚点=左手），左手健在 → 仍可戴；只是套里少了两根手指。
        Assert.True(ApparelSlots.IsSlotUsable(EquipSlot.LeftHand, severed));
        Assert.True(ApparelSlots.IsSlotUsable(EquipSlot.RightHand, severed));
    }

    [Fact]
    public void OtherSurvivors_HaveNoStartingAmputation()
    {
        Assert.Empty(SurvivorBackstory.SeveredAtStart(SurvivorBackstory.Nordi));
        Assert.Empty(SurvivorBackstory.SeveredAtStart("克莉丝汀"));

        Body body = NewBody();
        SurvivorBackstory.ApplyTo(SurvivorBackstory.Nordi, body);
        Assert.Equal(0.0, body.DisabilityModifiers.OperationPenalty, 6);
        Assert.False(body.IsSevered(HumanBody.LeftPinky));
    }

    // ---------------- ② 花衬衫 ----------------

    [Fact]
    public void FloralShirt_IsSkinLayer_SameNumbersAsLongSleeveShirt()
    {
        ArmorLayer shirt = ArmorTable.FloralShirt();
        ArmorLayer baseline = ArmorTable.LongSleeveShirt();

        Assert.Equal("花衬衫", shirt.Name);
        Assert.Equal(ArmorSlot.Skin, shirt.Slot);
        Assert.Equal(6, shirt.SharpDefense);
        Assert.Equal(3, shirt.BluntDefense);
        Assert.Equal(baseline.Weight, shirt.Weight);
        Assert.False(string.IsNullOrWhiteSpace(shirt.Description));
    }

    [Fact]
    public void FloralShirt_CoversTorsoAndBothArms()
    {
        IReadOnlySet<string> covers = ArmorTable.FloralShirt().CoversParts!;   // 非 null：贴身层按部位覆盖，非"全身"兼容口径
        IReadOnlySet<string> baseline = ArmorTable.LongSleeveShirt().CoversParts!;

        Assert.Contains(HumanBody.Chest, covers);
        Assert.Contains(HumanBody.Abdomen, covers);
        Assert.Contains(HumanBody.LeftArm, covers);
        Assert.Contains(HumanBody.RightArm, covers);
        Assert.Equal(baseline.Count, covers.Count);
    }

    [Fact]
    public void FloralShirt_HasFlavorInDescriptionTable()
    {
        Assert.Equal(ArmorTable.FloralShirt().Description, ArmorTable.DescriptionOf("花衬衫"));
    }

    [Fact]
    public void FloralShirt_OccupiesSkinLayerSlot_MutuallyExclusiveWithLongSleeveShirt()
    {
        Assert.True(ApparelCatalog.IsApparel("花衬衫"));
        ApparelCatalog.ApparelDef floral = ApparelCatalog.Get("花衬衫")!;
        ApparelCatalog.ApparelDef longSleeve = ApparelCatalog.Get("长袖布衣")!;
        Assert.Equal(longSleeve.Slots, floral.Slots);
        Assert.Equal(ArmorSlot.Skin, floral.Layer);

        // 同槽 → 换上花衬衫顶掉长袖布衣（从祖母身上扒下来的那件，真穿得上）。
        var slots = new ApparelSlots();
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "长袖布衣"));
        Assert.Equal(EquipOutcome.Equipped, ApparelCatalog.Equip(slots, "花衬衫", replace: true));
        Assert.Equal("花衬衫", slots.ItemAt(EquipSlot.SkinLayer));
    }

    // ---------------- ③ 祖母的尸体（营地叙事调查点） ----------------

    [Fact]
    public void GrandmotherCorpse_IsRegisteredAsCampNarrativeSpot()
    {
        NarrativeSpot? spot = NarrativeSpotRegistry.ById(NarrativeSpotRegistry.GrandmotherCorpseId);

        Assert.NotNull(spot);
        Assert.Equal(NarrativeSpotRegistry.CampDestination, spot!.Destination);
        Assert.False(spot.Repeatable); // 一次性：看过一次就够了
        Assert.NotEmpty(spot.Pages);
        Assert.All(spot.Pages, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        Assert.Contains(spot, NarrativeSpotRegistry.ForDestination(NarrativeSpotRegistry.CampDestination));
    }

    [Fact]
    public void GrandmotherCorpse_ResolvesOnce_ThenDeduped()
    {
        var flags = new StoryFlags();

        NarrativeSpotResult? first = NarrativeSpotRegistry.Resolve(NarrativeSpotRegistry.GrandmotherCorpseId, flags);
        Assert.NotNull(first);

        flags.Set(first!.Value.StoryFlag, "true");
        Assert.Null(NarrativeSpotRegistry.Resolve(NarrativeSpotRegistry.GrandmotherCorpseId, flags));
    }
}
