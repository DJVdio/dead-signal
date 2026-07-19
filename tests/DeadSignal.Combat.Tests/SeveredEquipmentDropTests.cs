using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class SeveredEquipmentDropTests
{
    [Fact]
    public void SeveredArm_ClaimsHandSlotAndHandWeapon_Once()
    {
        var ledger = new SeveredEquipmentDropLedger();

        Assert.Equal(new[] { EquipSlot.LeftHand }, ledger.ClaimApparelSlots(new[] { HumanBody.LeftArm, HumanBody.LeftHand }));
        Assert.Equal(new[] { Hand.Left }, ledger.ClaimHands(new[] { HumanBody.LeftArm, HumanBody.LeftHand }));

        // Body.Sever 的重复/存档回放不能再掉同一只手套或武器。
        Assert.Empty(ledger.ClaimApparelSlots(new[] { HumanBody.LeftHand }));
        Assert.Empty(ledger.ClaimHands(new[] { HumanBody.LeftHand }));
    }

    [Fact]
    public void SeveredFinger_DoesNotClaimWholeHandEquipment()
    {
        var ledger = new SeveredEquipmentDropLedger();

        Assert.Empty(ledger.ClaimApparelSlots(new[] { HumanBody.LeftIndex }));
        Assert.Empty(ledger.ClaimHands(new[] { HumanBody.LeftIndex }));
        Assert.False(ledger.IsApparelSlotClaimed(EquipSlot.LeftHand));
        Assert.False(ledger.IsHandClaimed(Hand.Left));
    }

    [Fact]
    public void SeveredLegAndFoot_OnlyClaimMatchingFoot()
    {
        var ledger = new SeveredEquipmentDropLedger();

        Assert.Equal(new[] { EquipSlot.RightFoot }, ledger.ClaimApparelSlots(new[] { HumanBody.RightLeg, HumanBody.RightCalf, HumanBody.RightFoot }));
        Assert.Empty(ledger.ClaimHands(new[] { HumanBody.RightLeg, HumanBody.RightCalf, HumanBody.RightFoot }));
        Assert.Equal(new[] { EquipSlot.LeftFoot }, ledger.ClaimApparelSlots(new[] { HumanBody.LeftFoot }));
        Assert.Empty(ledger.ClaimApparelSlots(new[] { HumanBody.LeftFoot }));
    }

    [Fact]
    public void SideClaims_AreIndependent()
    {
        var ledger = new SeveredEquipmentDropLedger();

        Assert.Equal(new[] { EquipSlot.LeftHand, EquipSlot.RightHand },
            ledger.ClaimApparelSlots(new[] { HumanBody.LeftArm, HumanBody.LeftHand, HumanBody.RightArm, HumanBody.RightHand }));
        Assert.Equal(new[] { Hand.Left, Hand.Right },
            ledger.ClaimHands(new[] { HumanBody.LeftArm, HumanBody.LeftHand, HumanBody.RightArm, HumanBody.RightHand }));
    }
}
