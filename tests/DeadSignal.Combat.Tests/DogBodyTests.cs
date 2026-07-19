using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class DogBodyTests
{
    [Fact]
    public void DogBody_HasNoHumanHandsOrArms()
    {
        Body body = DogBody.NewBody();

        Assert.DoesNotContain(HumanBody.LeftArm, body.Parts.Keys);
        Assert.DoesNotContain(HumanBody.RightArm, body.Parts.Keys);
        Assert.DoesNotContain(HumanBody.LeftHand, body.Parts.Keys);
        Assert.DoesNotContain(HumanBody.RightHand, body.Parts.Keys);
        Assert.DoesNotContain(body.Parts.Values, p => p.Region == BodyRegion.Hand);
    }

    [Fact]
    public void DogBody_UsesSharedArmorAnchorNames_AndFourLegs()
    {
        Body body = DogBody.NewBody();

        Assert.Contains(HumanBody.Chest, body.Parts.Keys);
        Assert.Contains(HumanBody.Abdomen, body.Parts.Keys);
        Assert.Contains(HumanBody.Head, body.Parts.Keys);
        Assert.Equal(4, body.Parts.Values.Count(p => p.Region == BodyRegion.Leg));
        Assert.Equal(0, body.DisabilityModifiers.OperationPenalty);
    }

    [Fact]
    public void DogBody_CanLoseALegWithoutBeingKilled()
    {
        Body body = DogBody.NewBody();
        body.Sever(HumanBody.LeftLeg);
        body.RecalculatePenalties();

        Assert.True(body.IsGone(HumanBody.LeftLeg));
        Assert.False(body.IsDead);
        Assert.True(body.DisabilityModifiers.MobilityPenalty > 0);
    }
}
