using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 道格 &amp; 布鲁斯羁绊纯逻辑单测：等级阈值边界 / 各级技能开闭 / 伙伴死亡失效 / 光环距离边界 / 系数常量值。
/// 系数断言直接对齐 draft 常量（数值待调也不脆——校验的是「取到该常量」而非硬编码字面量）。
/// </summary>
public class DougBruceBondTests
{
    // ── 等级推进：EvaluateLevel 阈值边界 ──────────────────────────────────────
    [Theory]
    [InlineData(-3, 1)]   // 负数按 0，仍 1 级
    [InlineData(0, 1)]    // 入队即 1 级
    [InlineData(4, 1)]    // 未达 L2 阈值
    [InlineData(5, 2)]    // 恰达 L2（Level2Days=5）
    [InlineData(11, 2)]   // 未达 L3 阈值
    [InlineData(12, 3)]   // 恰达 L3（Level3Days=12）
    [InlineData(365, 3)]  // 远超上限仍 3 级封顶
    public void EvaluateLevel_CrossesThresholds(int daysBothAlive, int expected)
    {
        Assert.Equal(expected, DougBruceBond.EvaluateLevel(daysBothAlive));
    }

    [Fact]
    public void EvaluateLevel_ThresholdsAlignConstants()
    {
        Assert.Equal(1, DougBruceBond.EvaluateLevel(DougBruceBond.Level2Days - 1));
        Assert.Equal(2, DougBruceBond.EvaluateLevel(DougBruceBond.Level2Days));
        Assert.Equal(2, DougBruceBond.EvaluateLevel(DougBruceBond.Level3Days - 1));
        Assert.Equal(3, DougBruceBond.EvaluateLevel(DougBruceBond.Level3Days));
    }

    // ── 道格视野角（1 级自带，不依赖布鲁斯）──────────────────────────────────
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DougAngleMult_AtLeastLevel1_GivesBonus(int level)
    {
        Assert.Equal(DougBruceBond.DougAngleBonusMult, DougBruceBond.DougAngleMult(level));
    }

    [Fact]
    public void DougAngleMult_NoBond_IsNeutral()
    {
        Assert.Equal(1.0f, DougBruceBond.DougAngleMult(0));
    }

    // ── 布鲁斯视野角（1 级，依赖道格存活）────────────────────────────────────
    [Fact]
    public void BruceAngleMult_Level1_DougAlive_GivesBonus()
    {
        Assert.Equal(DougBruceBond.BruceAngleBonusMult, DougBruceBond.BruceAngleMult(1, dougAlive: true));
        Assert.Equal(DougBruceBond.BruceAngleBonusMult, DougBruceBond.BruceAngleMult(3, dougAlive: true));
    }

    [Fact]
    public void BruceAngleMult_DougDead_Neutral()
    {
        // 道格死则布鲁斯加成失效（不论等级多高）
        Assert.Equal(1.0f, DougBruceBond.BruceAngleMult(3, dougAlive: false));
    }

    // ── 布鲁斯视野距离（2 级解锁，依赖道格存活）──────────────────────────────
    [Fact]
    public void BruceRangeMult_UnlocksAtLevel2()
    {
        Assert.Equal(1.0f, DougBruceBond.BruceRangeMult(1, dougAlive: true));                       // L1 未解锁
        Assert.Equal(DougBruceBond.BruceRangeBonusMult, DougBruceBond.BruceRangeMult(2, true));      // L2 生效
        Assert.Equal(DougBruceBond.BruceRangeBonusMult, DougBruceBond.BruceRangeMult(3, true));      // L3 仍在
    }

    [Fact]
    public void BruceRangeMult_DougDead_Neutral()
    {
        Assert.Equal(1.0f, DougBruceBond.BruceRangeMult(2, dougAlive: false));
    }

    // ── 2 级：解锁道格为布鲁斯制作狗装备（用户 L2 修订替换原 1.25x 缠斗伤害）─────
    [Fact]
    public void CanCraftDogGear_UnlocksAtLevel2_BothAlive()
    {
        Assert.True(DougBruceBond.CanCraftDogGear(2, bothAlive: true));
        Assert.True(DougBruceBond.CanCraftDogGear(3, bothAlive: true));
    }

    [Fact]
    public void CanCraftDogGear_BelowLevel2_Locked()
    {
        Assert.False(DougBruceBond.CanCraftDogGear(1, bothAlive: true));
    }

    [Fact]
    public void CanCraftDogGear_EitherDead_Locked()
    {
        // 道格死＝无制作者；布鲁斯死＝狗装备无受益者：任一死皆不可制作（默认，待确认）
        Assert.False(DougBruceBond.CanCraftDogGear(2, bothAlive: false));
        Assert.False(DougBruceBond.CanCraftDogGear(3, bothAlive: false));
    }

    [Fact]
    public void DogGearUnlockLevel_IsLevel2()
    {
        Assert.Equal(2, DougBruceBond.DogGearUnlockLevel);
    }

    // ── 3 级光环（相依为命）：距离边界 / 一方死亡永失 ─────────────────────────
    [Fact]
    public void AuraActive_Level3_BothAlive_WithinRadius_Active()
    {
        var aura = DougBruceBond.AuraActive(3, bothAlive: true, distance: 100f, auraRadius: 160f);
        Assert.True(aura.IsActive);
        Assert.Equal(DougBruceBond.AuraProductionMult, aura.ProductionMult);
        Assert.Equal(DougBruceBond.AuraDamageTakenMult, aura.DamageTakenMult);
    }

    [Fact]
    public void AuraActive_DistanceBoundary_InclusiveThenDrops()
    {
        // 恰在半径上 → 含边界，激活
        Assert.True(DougBruceBond.AuraActive(3, true, distance: 160f, auraRadius: 160f).IsActive);
        // 略超半径 → 失活
        Assert.False(DougBruceBond.AuraActive(3, true, distance: 160.01f, auraRadius: 160f).IsActive);
    }

    [Fact]
    public void AuraActive_BelowLevel3_Inactive()
    {
        Assert.False(DougBruceBond.AuraActive(2, true, distance: 0f, auraRadius: 160f).IsActive);
    }

    [Fact]
    public void AuraActive_OneDead_Inactive_NeutralMults()
    {
        // 一方死亡即永失（bothAlive=false）
        var aura = DougBruceBond.AuraActive(3, bothAlive: false, distance: 0f, auraRadius: 160f);
        Assert.False(aura.IsActive);
        Assert.Equal(1.0f, aura.ProductionMult);
        Assert.Equal(1.0f, aura.DamageTakenMult);
    }

    // ── 站岗效率常量 ─────────────────────────────────────────────────────────
    [Fact]
    public void BruceGuardEfficiency_Is75Percent()
    {
        Assert.Equal(0.75f, DougBruceBond.BruceGuardEfficiency);
    }
}
