using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 远程射程衰减（Ballistics.RangedDamageFactor）+ 护甲按具体部位覆盖（ArmorLayer.CoversParts，支持左右分）。
/// 用户口径：远程武器 FalloffStart 内满伤 → 线性降到 MaxRange 处的 FalloffFloor → 超出 MaxRange 不可开火(0)。
/// 护甲默认覆盖全部位（向后兼容）；劳保手套仅覆盖手部大区域。
/// </summary>
public class RangeAndCoverageTests
{
    private static Weapon RangedProbe() => new()
    {
        Name = "测距枪",
        DamageMin = 10,
        DamageMax = 10,
        DamageType = DamageType.Sharp,
        IsRanged = true,
        MaxRange = 200,
        FalloffStart = 50,
        FalloffFloor = 0.5,
    };

    // ---- RangedDamageFactor 各段 ----

    [Fact]
    public void RangedFactor_WithinFalloffStart_IsFull()
    {
        var w = RangedProbe();
        Assert.Equal(1.0, Ballistics.RangedDamageFactor(0, w), 9);
        Assert.Equal(1.0, Ballistics.RangedDamageFactor(50, w), 9); // 恰在起点仍满伤
    }

    [Fact]
    public void RangedFactor_AtMaxRange_IsFloor()
    {
        var w = RangedProbe();
        Assert.Equal(0.5, Ballistics.RangedDamageFactor(200, w), 9);
    }

    [Fact]
    public void RangedFactor_Midway_LinearInterpolation()
    {
        // 起点50→最大200，中点125：t=0.5 → 1 - 0.5*(1-0.5) = 0.75
        var w = RangedProbe();
        Assert.Equal(0.75, Ballistics.RangedDamageFactor(125, w), 9);
    }

    [Fact]
    public void RangedFactor_BeyondMaxRange_IsZero()
    {
        var w = RangedProbe();
        Assert.Equal(0.0, Ballistics.RangedDamageFactor(200.01, w), 9);
        Assert.Equal(0.0, Ballistics.RangedDamageFactor(9999, w), 9);
    }

    [Fact]
    public void RangedFactor_NoRangeModel_IsFullEverywhere()
    {
        // 近战/未设 MaxRange 的武器：无射程模型，恒满伤，绝不返回 0。
        var melee = WeaponTable.Dagger();
        Assert.Null(melee.MaxRange);
        Assert.Equal(1.0, Ballistics.RangedDamageFactor(0, melee), 9);
        Assert.Equal(1.0, Ballistics.RangedDamageFactor(9999, melee), 9);
    }

    [Fact]
    public void InRange_TracksMaxRange()
    {
        var w = RangedProbe();
        Assert.True(Ballistics.InRange(200, w));
        Assert.False(Ballistics.InRange(201, w));
        Assert.True(Ballistics.InRange(9999, WeaponTable.Dagger())); // 近战无射程约束
    }

    [Fact]
    public void WeaponTable_AllRangedHaveRangeFields()
    {
        foreach (var w in WeaponTable.Arsenal())
        {
            if (!w.IsRanged) continue;
            Assert.True(w.MaxRange is > 0, $"{w.Name} 缺 MaxRange");
            Assert.True(w.FalloffStart is >= 0, $"{w.Name} 缺 FalloffStart");
            Assert.True(w.FalloffFloor is > 0 and <= 1, $"{w.Name} FalloffFloor 应在(0,1]");
            Assert.True(w.FalloffStart <= w.MaxRange, $"{w.Name} FalloffStart 不应超过 MaxRange");
        }
    }

    // ---- 护甲按具体部位覆盖（支持左右分）----

    private static BodyPart Part(string name) =>
        HumanBody.Parts().First(p => p.Name == name);

    private static readonly BodyPart LeftHandPart = Part(HumanBody.LeftHand);
    private static readonly BodyPart RightHandPart = Part(HumanBody.RightHand);
    private static readonly BodyPart LeftFingerPart = Part(HumanBody.LeftIndex);
    private static readonly BodyPart TorsoPart = Part(HumanBody.Chest);

    [Fact]
    public void WorkGlove_Left_CoversLeftHandAndFingers_NotRightNorTorso()
    {
        ArmorLayer left = ArmorTable.WorkGlove(leftHand: true);
        Assert.Equal("左手套", left.Name);
        Assert.True(left.Covers(LeftHandPart));            // 左手
        Assert.True(left.Covers(LeftFingerPart));          // 连带左手指
        Assert.False(left.Covers(RightHandPart));          // 不防右手
        Assert.False(left.Covers(Part(HumanBody.RightIndex))); // 不防右手指
        Assert.False(left.Covers(TorsoPart));              // 不防躯干
    }

    [Fact]
    public void WorkGlove_Right_CoversRightHandOnly()
    {
        ArmorLayer right = ArmorTable.WorkGlove(leftHand: false);
        Assert.Equal("右手套", right.Name);
        Assert.True(right.Covers(RightHandPart));
        Assert.False(right.Covers(LeftHandPart));
    }

    [Fact]
    public void LegacyArmor_NoCoversParts_CoversEverything()
    {
        // 现有护甲不填 CoversParts → 默认全覆盖（向后兼容）。
        var jacket = ArmorTable.SurvivorArmor()[0];
        Assert.Null(jacket.CoversParts);
        foreach (BodyPart p in HumanBody.Parts())
        {
            Assert.True(jacket.Covers(p));
        }
    }

    private static ArmorLayer LeftGloveHeavy() => new()
    {
        Name = "左手套", Slot = ArmorSlot.Skin, SharpDefense = 20, BluntDefense = 10,
        CoversParts = HumanBody.SubtreeNames(HumanBody.LeftHand),
    };

    [Fact]
    public void Resolve_LeftGlove_ReducesLeftHandHit()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 5, Penetration = 0, DamageType = DamageType.Sharp };
        // 命中左手：左手套生效。攻5 vs 防11 → 5 < 5.5 → 挡下、终止。
        var rng = new SequenceRandomSource(5, 11);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { LeftGloveHeavy() }, LeftHandPart);

        Assert.Single(r.Layers);
        Assert.True(r.Terminated);
        Assert.Equal(0, r.FinalDamage);
    }

    [Fact]
    public void Resolve_LeftGlove_DoesNotReduceRightHandHit()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 5, Penetration = 0, DamageType = DamageType.Sharp };
        // 命中右手：左手套不覆盖右手 → 被过滤 → 无甲直击，仅一次武器 roll。
        var rng = new SequenceRandomSource(5);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { LeftGloveHeavy() }, RightHandPart);

        Assert.Empty(r.Layers);
        Assert.False(r.Terminated);
        Assert.Equal(5, r.FinalDamage);
        Assert.Equal(0, rng.Remaining); // 未触及手套防御 roll
    }

    [Fact]
    public void Resolve_LeftGlove_SkippedOnTorsoHit()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 5, Penetration = 0, DamageType = DamageType.Sharp };
        var rng = new SequenceRandomSource(5);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { LeftGloveHeavy() }, TorsoPart);

        Assert.Empty(r.Layers);
        Assert.False(r.Terminated);
        Assert.Equal(5, r.FinalDamage);
    }

    [Fact]
    public void Resolve_MixedCoverage_OnlyMatchingLayersApply()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 5, Penetration = 0, DamageType = DamageType.Sharp };
        var fullBody = new ArmorLayer { Name = "皮夹克", Slot = ArmorSlot.Outer, SharpDefense = 6, BluntDefense = 3 }; // 全覆盖
        // 命中躯干：只应用皮夹克（全覆盖），左手套被过滤 → 只 1 层。
        // 层1 皮夹克 锐防6 穿透0：攻5 vs 防3 → 5>=3 全伤5、穿入。末层 → 5 伤。
        var rng = new SequenceRandomSource(5, 3);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { fullBody, LeftGloveHeavy() }, TorsoPart);

        Assert.Single(r.Layers);
        Assert.Equal("皮夹克", r.Layers[0].LayerName);
        Assert.Equal(5, r.FinalDamage);
    }
}
