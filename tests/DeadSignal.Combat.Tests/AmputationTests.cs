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

    // ---- 移动单元：以脚为单位（脚趾二级判定），切/毁腿连带脚 gone → 该侧 0.5；脚趾 -2%/根（该脚累加）----

    [Fact]
    public void SeverFoot_LegIntact_SideMobility50()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftFoot); // 整只脚没了 → 该侧 0.5
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void SeverLeg_TakesFootAsSide50()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftLeg); // 连带左脚（及脚趾）移除 → 该侧 0.5，不叠加
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void SeverFootAndOppositeLeg_Mobility100()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftFoot);
        body.Sever(HumanBody.RightLeg); // 连带右脚
        body.RecalculatePenalties();
        Assert.Equal(1.0, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void SeverOneToe_Mobility002_AccumulatesOnThatFoot()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftBigToe);
        body.RecalculatePenalties();
        Assert.Equal(0.02, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void SeverAllFiveToesSameFoot_FootIntact_Mobility010()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftBigToe);
        body.Sever(HumanBody.LeftToe2);
        body.Sever(HumanBody.LeftToe3);
        body.Sever(HumanBody.LeftToe4);
        body.Sever(HumanBody.LeftToe5);
        body.RecalculatePenalties();
        Assert.Equal(0.10, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void FiveToesOneFoot_PlusWholeOtherFoot_Mobility060()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftBigToe);
        body.Sever(HumanBody.LeftToe2);
        body.Sever(HumanBody.LeftToe3);
        body.Sever(HumanBody.LeftToe4);
        body.Sever(HumanBody.LeftToe5); // 左脚 5 趾 → 0.10（脚本体在）
        body.Sever(HumanBody.RightFoot); // 右脚整只 → 0.5
        body.RecalculatePenalties();
        Assert.Equal(0.60, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void SeverFootAfterToes_FootOverridesTo50()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftBigToe);
        body.Sever(HumanBody.LeftToe2);
        body.Sever(HumanBody.LeftFoot); // 断脚覆盖脚趾累加，锁 -50%
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    [Fact]
    public void FootRegionAndToesShareMacroRegion_Foot()
    {
        var parts = HumanBody.Parts();
        var toe = parts.First(p => p.Name == HumanBody.LeftBigToe);
        var sole = parts.First(p => p.Name == HumanBody.LeftFoot);
        Assert.Equal(BodyMacroRegion.Foot, toe.MacroRegion);
        Assert.Equal(BodyMacroRegion.Foot, sole.MacroRegion);
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
