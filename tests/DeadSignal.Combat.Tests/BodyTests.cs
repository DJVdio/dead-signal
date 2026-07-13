using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class BodyTests
{
    [Fact]
    public void NewBody_AllPartsFullHp()
    {
        var body = HumanBody.NewBody();
        Assert.Equal(20, body.HpOf(HumanBody.Chest));   // 躯干细分：胸 20（[SPEC-B17]，Sim 校准）
        Assert.Equal(16, body.HpOf(HumanBody.Abdomen)); // 腹 16
        Assert.Equal(16, body.HpOf(HumanBody.LeftHand));
        Assert.False(body.IsDead);
    }

    [Fact]
    public void VitalPart_ZeroHp_CausesDeath()
    {
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.Chest, 20); // 胸=致死部位（沿原躯干档）
        Assert.Equal(0, body.HpOf(HumanBody.Chest));
        Assert.True(body.IsDead);
    }

    [Fact]
    public void LimbPart_ZeroHp_Disabled_NotDead()
    {
        var body = HumanBody.NewBody();
        body.ApplyDamage(HumanBody.LeftLeg, 12); // 大腿细分后 HP 12
        Assert.True(body.IsDisabled(HumanBody.LeftLeg));
        Assert.False(body.IsDead);
    }

    [Fact]
    public void BothEyes_Zero_IsFullyBlind()
    {
        var body = HumanBody.NewBody();
        Assert.False(body.IsFullyBlind);
        body.ApplyDamage(HumanBody.LeftEye, 6);
        Assert.False(body.IsFullyBlind); // 单眼盲不算全盲
        body.ApplyDamage(HumanBody.RightEye, 6);
        Assert.True(body.IsFullyBlind);
    }

    [Fact]
    public void Sever_Arm_TakesHandWithIt_AndDropsEquipment()
    {
        var body = HumanBody.NewBody();
        IReadOnlyList<string>? dropped = null;
        body.EquipmentDropped = parts => dropped = parts;

        var sr = body.Sever(HumanBody.LeftArm);

        Assert.Contains(HumanBody.LeftArm, sr.RemovedParts);
        Assert.Contains(HumanBody.LeftHand, sr.RemovedParts); // 连带
        Assert.False(sr.CausedDeath); // 四肢非致死
        Assert.True(body.IsSevered(HumanBody.LeftHand));
        Assert.NotNull(dropped);
        Assert.Contains(HumanBody.LeftHand, dropped!);
    }

    [Fact]
    public void Sever_VitalPart_CausesDeath()
    {
        var body = HumanBody.NewBody();
        var sr = body.Sever(HumanBody.Head);
        Assert.True(sr.CausedDeath);
        Assert.True(body.IsDead);
        Assert.Contains(HumanBody.LeftEye, sr.RemovedParts); // 头下细部位连带
    }

    [Fact]
    public void SeveredPart_ImmuneToFurtherDamage()
    {
        var body = HumanBody.NewBody();
        body.Sever(HumanBody.LeftHand);
        var change = body.ApplyDamage(HumanBody.LeftHand, 5);
        Assert.Equal(0, change.HpAfter);
        Assert.False(change.ReachedZeroThisHit);
    }

    [Fact]
    public void ErodeMaxHp_ReducesCeiling_AndDestroysAtZero()
    {
        var body = HumanBody.NewBody();
        var e1 = body.ErodeMaxHp(HumanBody.LeftHand, 4); // 16 → 12
        Assert.Equal(12, body.MaxHpOf(HumanBody.LeftHand), 9);
        Assert.False(e1.Destroyed);

        var e2 = body.ErodeMaxHp(HumanBody.LeftHand, 12); // 12 → 0，损毁
        Assert.True(e2.Destroyed);
        Assert.True(body.IsDestroyed(HumanBody.LeftHand));
        Assert.False(e2.CausedDeath); // 四肢
    }

    [Fact]
    public void ErodeMaxHp_VitalPart_ToZero_CausesDeath()
    {
        // 致死部位（胸，沿原躯干档）上限磨损归 0 = 死亡（锤烂胸腔）
        var body = HumanBody.NewBody();
        var er = body.ErodeMaxHp(HumanBody.Chest, 20); // 20 → 0
        Assert.True(er.Destroyed);
        Assert.True(er.CausedDeath);
        Assert.True(body.IsDead);
        Assert.True(body.IsDestroyed(HumanBody.Chest));
    }

    [Fact]
    public void DestroyedPart_DropsNoEquipment()
    {
        // 损毁不掉落装备（碾碎的手仍套手套里，随部位报废）
        var body = HumanBody.NewBody();
        bool dropped = false;
        body.EquipmentDropped = _ => dropped = true;
        body.ErodeMaxHp(HumanBody.LeftHand, 16); // 损毁
        Assert.True(body.IsDestroyed(HumanBody.LeftHand));
        Assert.False(dropped); // 未触发掉落回调
    }
}
