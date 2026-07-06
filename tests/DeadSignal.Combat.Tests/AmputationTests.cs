using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 切除惩罚与假肢恢复（设计文档 §5 / next-steps §4）。数值口径（用户拍板）：
/// 一手操作 -50%、一腿移动 -50%、两手/两腿 -100%、一指 -7%（该手累加，上限 -50%）、
/// 断手 -50% 覆盖手指累加、上臂连带手按手计 -50%；假肢恢复 = 单肢能力(50%) × RestoreRatio，
/// 净惩罚 = -50% + 恢复（木 -37.5% / 简易 -25% / 仿生 -12.5%）。
/// </summary>
public class AmputationTests
{
    [Fact]
    public void SeverOneHand_Operation50()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftHand);
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverBothHands_Operation100()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftHand);
        body.Sever(HumanBody.RightHand);
        body.RecalculatePenalties();
        Assert.Equal(1.0, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverOneLeg_Mobility50()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftLeg);
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void SeverBothLegs_Mobility100()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftLeg);
        body.Sever(HumanBody.RightLeg);
        body.RecalculatePenalties();
        Assert.Equal(1.0, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void SeverOneFinger_Operation07_AccumulatesOnThatHand()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftIndex);
        body.RecalculatePenalties();
        Assert.Equal(0.07, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverAllFiveFingersSameHand_Operation35()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftThumb);
        body.Sever(HumanBody.LeftIndex);
        body.Sever(HumanBody.LeftMiddle);
        body.Sever(HumanBody.LeftRing);
        body.Sever(HumanBody.LeftPinky);
        body.RecalculatePenalties();
        Assert.Equal(0.35, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverHandAfterFingers_HandOverridesTo50()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftThumb);
        body.Sever(HumanBody.LeftIndex);
        body.Sever(HumanBody.LeftMiddle);
        body.Sever(HumanBody.LeftRing);
        body.Sever(HumanBody.LeftPinky);
        body.Sever(HumanBody.LeftHand); // 断手覆盖手指累加，锁 -50%
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverArm_TakesHandAsHand50()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftArm); // 连带手，按手计 -50%
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverHand_WoodenProsthetic_Net375()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftHand);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Wooden, BodyRegion.Hand));
        Assert.Equal(0.375, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverHand_SimpleProsthetic_Net25()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftHand);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Simple, BodyRegion.Hand));
        Assert.Equal(0.25, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverHand_BionicProsthetic_Net125()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftHand);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Bionic, BodyRegion.Hand));
        Assert.Equal(0.125, body.DisabilityModifiers.OperationPenalty, 9);
    }

    [Fact]
    public void SeverLeg_WoodenProsthetic_Net375()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftLeg);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Wooden, BodyRegion.Leg));
        Assert.Equal(0.375, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void HandRegionAndFingersShareMacroRegion_Hand()
    {
        var parts = HumanBody.Parts();
        var finger = parts.First(p => p.Name == HumanBody.LeftIndex);
        var palm = parts.First(p => p.Name == HumanBody.LeftHand);
        Assert.Equal(BodyMacroRegion.Hand, finger.MacroRegion);
        Assert.Equal(BodyMacroRegion.Hand, palm.MacroRegion);
    }
}
