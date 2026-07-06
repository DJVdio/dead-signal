using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 假肢消费层：PawnInspection 假肢槽快照 + "装假肢→能力恢复"数值链路（脱离 Godot 的纯 C# 部分）。
/// 引擎侧假肢数学由 <see cref="AmputationTests"/> 覆盖；这里锁"面板读到的槽位状态"与"装后快照惩罚下降"。
/// </summary>
public class ProstheticInspectionTests
{
    private static Body FreshBody() => HumanBody.NewBody();

    private static ProstheticSlot Slot(PawnInspection snap, string unitPart) =>
        snap.ProstheticSlots.Single(s => s.UnitPartName == unitPart);

    [Fact]
    public void FreshBody_FourSlots_NoneAmputated()
    {
        PawnInspection snap = PawnInspection.FromBody(FreshBody(), null, null, "x");

        // 两手 + 两脚 = 4 个可装假肢的槽位。
        Assert.Equal(4, snap.ProstheticSlots.Count);
        Assert.All(snap.ProstheticSlots, s =>
        {
            Assert.False(s.IsAmputated);
            Assert.False(s.HasProsthetic);
            Assert.False(s.CanEquip);
        });
    }

    [Fact]
    public void SlotMetadata_HandsReplaceHand_FeetReplaceLeg()
    {
        PawnInspection snap = PawnInspection.FromBody(FreshBody(), null, null, "x");

        Assert.Equal(BodyRegion.Hand, Slot(snap, HumanBody.LeftHand).ReplacesRegion);
        Assert.Equal(BodyRegion.Hand, Slot(snap, HumanBody.RightHand).ReplacesRegion);
        Assert.Equal(BodyRegion.Leg, Slot(snap, HumanBody.LeftFoot).ReplacesRegion);
        Assert.Equal(BodyRegion.Leg, Slot(snap, HumanBody.RightFoot).ReplacesRegion);
    }

    [Fact]
    public void SeverHand_SlotAmputated_CanEquip()
    {
        Body body = FreshBody();
        body.Sever(HumanBody.LeftHand);
        body.RecalculatePenalties();

        PawnInspection snap = PawnInspection.FromBody(body, null, null, "x");
        ProstheticSlot left = Slot(snap, HumanBody.LeftHand);
        Assert.True(left.IsAmputated);
        Assert.False(left.HasProsthetic);
        Assert.True(left.CanEquip);
        // 右手完好：不算空槽。
        Assert.False(Slot(snap, HumanBody.RightHand).IsAmputated);
    }

    [Fact]
    public void SeverLeg_ConnectsFootGone_LegSlotAmputated()
    {
        Body body = FreshBody();
        body.Sever(HumanBody.LeftLeg); // 连带左脚 → 该腿槽（以脚为单位）算空
        body.RecalculatePenalties();

        PawnInspection snap = PawnInspection.FromBody(body, null, null, "x");
        ProstheticSlot leg = Slot(snap, HumanBody.LeftFoot);
        Assert.True(leg.IsAmputated);
        Assert.True(leg.CanEquip);
        Assert.Equal(BodyRegion.Leg, leg.ReplacesRegion);
    }

    [Fact]
    public void SeverHand_AttachWooden_SlotCovered_OperationNet375()
    {
        Body body = FreshBody();
        body.Sever(HumanBody.LeftHand);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Wooden, BodyRegion.Hand, "木制假肢"));

        PawnInspection snap = PawnInspection.FromBody(body, null, null, "x");
        ProstheticSlot left = Slot(snap, HumanBody.LeftHand);
        Assert.True(left.HasProsthetic);
        Assert.False(left.CanEquip);
        Assert.Equal(ProstheticGrade.Wooden, left.Grade);
        Assert.Equal("木制假肢", left.ProstheticName);
        // 装木手后净操作惩罚 0.5 → 0.375（引擎数学，经快照透出）。
        Assert.Equal(0.375, snap.OperationPenalty, 9);
    }

    [Fact]
    public void SeverLeg_AttachBionic_SlotCovered_MobilityNet125()
    {
        Body body = FreshBody();
        body.Sever(HumanBody.LeftLeg);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Bionic, BodyRegion.Leg, "仿生假肢"));

        PawnInspection snap = PawnInspection.FromBody(body, null, null, "x");
        ProstheticSlot leg = Slot(snap, HumanBody.LeftFoot);
        Assert.True(leg.HasProsthetic);
        Assert.Equal(ProstheticGrade.Bionic, leg.Grade);
        Assert.Equal(0.125, snap.MobilityPenalty, 9);
    }

    [Fact]
    public void BothHandsSevered_OneProsthetic_OnlyOneSlotCovered()
    {
        Body body = FreshBody();
        body.Sever(HumanBody.LeftHand);
        body.Sever(HumanBody.RightHand);
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Simple, BodyRegion.Hand, "简易假肢"));

        PawnInspection snap = PawnInspection.FromBody(body, null, null, "x");
        var covered = snap.ProstheticSlots.Where(s => s.ReplacesRegion == BodyRegion.Hand && s.HasProsthetic).ToList();
        var stillEmpty = snap.ProstheticSlots.Where(s => s.ReplacesRegion == BodyRegion.Hand && s.CanEquip).ToList();
        Assert.Single(covered);
        Assert.Single(stillEmpty);
        // 一手简易假肢恢复(0.5×0.5=0.25) + 另一手全失(0.5) = 净 0.75。
        Assert.Equal(0.75, snap.OperationPenalty, 9);
    }

    [Fact]
    public void Snapshot_ProstheticSlots_DetachedFromLiveBody()
    {
        Body body = FreshBody();
        body.Sever(HumanBody.LeftHand);
        PawnInspection snap = PawnInspection.FromBody(body, null, null, "x");
        Assert.True(Slot(snap, HumanBody.LeftHand).CanEquip);

        // 拍照后再装假肢：旧快照不应回改。
        body.AttachProsthetic(Prosthetic.OfGrade(ProstheticGrade.Wooden, BodyRegion.Hand));
        Assert.True(Slot(snap, HumanBody.LeftHand).CanEquip);
        Assert.False(Slot(snap, HumanBody.LeftHand).HasProsthetic);
    }
}
