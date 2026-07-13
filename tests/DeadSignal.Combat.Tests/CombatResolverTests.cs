using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

public class CombatResolverTests
{
    private static readonly BodyPart Chest = new() { Name = "胸部", VolumeWeight = 40 };

    /// <summary>
    /// 设计文档算例复现（武器 2-12）：
    /// 第一层 攻 10 vs 防 11 → 半伤 5、锐器转钝、穿透归零；
    /// 第二层 攻方在 [0,5] roll，攻 4 vs 防 2 → 全伤 4。
    /// 最终作用到部位 4 伤，类型钝器。
    /// </summary>
    [Fact]
    public void DocumentedExample_TwoTwelve_HalfThenFull()
    {
        var weapon = new Weapon
        {
            Name = "算例武器",
            DamageMin = 2,
            DamageMax = 12,
            Penetration = 0,
            DamageType = DamageType.Sharp,
        };
        var layers = new[]
        {
            new ArmorLayer { Name = "外层", Slot = ArmorSlot.Plate, SharpDefense = 12, BluntDefense = 6 },
            new ArmorLayer { Name = "内层", Slot = ArmorSlot.Skin, SharpDefense = 8, BluntDefense = 4 },
        };
        // 顺序: atk1, def1, atk2, def2
        var rng = new SequenceRandomSource(10, 11, 4, 2);
        var resolver = new CombatResolver(rng);

        var r = resolver.Resolve(weapon, layers, Chest);

        Assert.Equal(2, r.Layers.Count);

        var l0 = r.Layers[0];
        Assert.Equal(LayerOutcome.Half, l0.Outcome);
        Assert.True(l0.ConvertedToBlunt);
        Assert.Equal(DamageType.Sharp, l0.DamageTypeBefore);
        Assert.Equal(DamageType.Blunt, l0.DamageTypeAfter);
        Assert.Equal(5, l0.DamageAfterLayer, 9);

        var l1 = r.Layers[1];
        Assert.Equal(LayerOutcome.Full, l1.Outcome);
        Assert.Equal(0, l1.PenetrationUsed, 9); // 转钝后穿透归零
        Assert.Equal(4, l1.DamageAfterLayer, 9);
        Assert.Equal(4, l1.ApplicableDefense); // 用钝防

        Assert.False(r.Terminated);
        Assert.Equal(2, r.LayersPenetrated);
        Assert.Equal(DamageType.Blunt, r.FinalDamageType);
        Assert.Equal(4, r.RawDamage, 9);
        Assert.Equal(4, r.FinalDamage);
        Assert.Equal(0, rng.Remaining); // 恰好用掉 4 次 roll
    }

    [Fact]
    public void NoArmor_DirectHit_UsesWeaponRoll()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 10, DamageType = DamageType.Sharp };
        var rng = new SequenceRandomSource(7);
        var r = new CombatResolver(rng).Resolve(weapon, Array.Empty<ArmorLayer>(), Chest);

        Assert.Empty(r.Layers);
        Assert.False(r.Terminated);
        Assert.Equal(0, r.LayersPenetrated);
        Assert.Equal(7, r.RawDamage, 9);
        Assert.Equal(7, r.FinalDamage);
        Assert.Equal(DamageType.Sharp, r.FinalDamageType);
    }

    [Fact]
    public void FullPenetration_DefenseCollapsesToZero_AlwaysFull()
    {
        var weapon = new Weapon { Name = "狙击", DamageMin = 5, DamageMax = 5, Penetration = 1.0, DamageType = DamageType.Sharp };
        var layer = new ArmorLayer { Name = "板甲", Slot = ArmorSlot.Plate, SharpDefense = 30, BluntDefense = 12 };
        // 穿透 100% → defMax 0 → def 只能是 0
        var rng = new SequenceRandomSource(5, 0);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { layer }, Chest);

        Assert.Single(r.Layers);
        Assert.Equal(LayerOutcome.Full, r.Layers[0].Outcome);
        Assert.Equal(5, r.FinalDamage);
        Assert.Equal(1, r.LayersPenetrated);
    }

    [Fact]
    public void ZeroPenetration_HighDefense_BlocksAndTerminatesAtZero()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 5, Penetration = 0, DamageType = DamageType.Sharp };
        var layer = new ArmorLayer { Name = "板甲", Slot = ArmorSlot.Plate, SharpDefense = 20, BluntDefense = 10 };
        // 攻 5 vs 防 11 → 5 < 5.5 → 无伤终止
        var rng = new SequenceRandomSource(5, 11);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { layer }, Chest);

        Assert.True(r.Terminated);
        Assert.Equal(LayerOutcome.Blocked, r.Layers[0].Outcome);
        Assert.Equal(0, r.FinalDamage); // 终止不适用最低 1 伤保底
        Assert.Equal(0, r.RawDamage, 9);
        Assert.Equal(0, r.LayersPenetrated);
    }

    [Fact]
    public void TerminatedLayer_StopsSubsequentLayers()
    {
        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 5, Penetration = 0, DamageType = DamageType.Sharp };
        var layers = new[]
        {
            new ArmorLayer { Name = "外", Slot = ArmorSlot.Plate, SharpDefense = 20, BluntDefense = 10 },
            new ArmorLayer { Name = "内", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2 },
        };
        // 第一层就被挡：仅消耗 atk1, def1；第二层不应再 roll
        var rng = new SequenceRandomSource(5, 11);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.Single(r.Layers); // 只记录被挡的第一层
        Assert.True(r.Terminated);
        Assert.Equal(0, rng.Remaining); // 未触及第二层的 roll
    }

    [Fact]
    public void ThreeLayers_DamageDecaysEachFullLayer()
    {
        var weapon = new Weapon { Name = "重剑", DamageMin = 10, DamageMax = 20, Penetration = 0, DamageType = DamageType.Sharp };
        var layers = new[]
        {
            new ArmorLayer { Name = "L1", Slot = ArmorSlot.Plate, SharpDefense = 4, BluntDefense = 2 },
            new ArmorLayer { Name = "L2", Slot = ArmorSlot.Outer, SharpDefense = 4, BluntDefense = 2 },
            new ArmorLayer { Name = "L3", Slot = ArmorSlot.Skin, SharpDefense = 4, BluntDefense = 2 },
        };
        // atk1=15 (>=1 full → carried15), atk2 in[0,15]=8 (full → 8), atk3 in[0,8]=3 (full → 3)
        var rng = new SequenceRandomSource(15, 1, 8, 1, 3, 1);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.Equal(3, r.LayersPenetrated);
        Assert.All(r.Layers, l => Assert.Equal(LayerOutcome.Full, l.Outcome));
        Assert.Equal(15, r.Layers[0].DamageAfterLayer, 9);
        Assert.Equal(8, r.Layers[1].DamageAfterLayer, 9);
        Assert.Equal(3, r.Layers[2].DamageAfterLayer, 9);
        Assert.Equal(3, r.FinalDamage);
    }

    [Fact]
    public void FractionalCarry_KeepsDecimal_NoCeil()
    {
        // [SPEC-B14-补6 伤害不取整] 用户裁决"伤害也改小数"：末端不再向上取整。
        var weapon = new Weapon { Name = "小刀", DamageMin = 0.6, DamageMax = 0.6, Penetration = 0, DamageType = DamageType.Sharp };
        var layer = new ArmorLayer { Name = "布", Slot = ArmorSlot.Skin, SharpDefense = 1, BluntDefense = 0.5 };
        // 攻 0.6 vs 防 1.0 → 0.6 >= 0.5 半伤 0.3、转钝；末层 → **保留 0.3**（旧模型 ceil 成 1）
        var rng = new SequenceRandomSource(0.6, 1.0);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { layer }, Chest);

        Assert.Equal(0.3, r.RawDamage, 9);
        Assert.Equal(0.3, r.FinalDamage, 9); // 小数伤害不再取整
        Assert.Equal(DamageType.Blunt, r.FinalDamageType);
        Assert.True(r.Layers[0].ConvertedToBlunt);
    }

    [Fact]
    public void LandedHit_FloorsToMinimum_NotZero()
    {
        // 命中即生效的下限 0.01：无甲直击极小伤害不退化成 0（防空砍死锁）。
        var weapon = new Weapon { Name = "钝针", DamageMin = 0.004, DamageMax = 0.004, Penetration = 0, DamageType = DamageType.Blunt };
        var rng = new SequenceRandomSource(0.004);
        var r = new CombatResolver(rng).Resolve(weapon, System.Array.Empty<ArmorLayer>(), Chest);

        Assert.Equal(CombatResolver.MinLandedDamage, r.FinalDamage, 9); // 0.004 → 兜到 0.01
        Assert.True(r.FinalDamage > 0);
    }

    [Fact]
    public void SharpToBlunt_PenetrationDropsToZeroNextLayer()
    {
        // 锐器带穿透，转钝后下一层穿透必须为 0
        var weapon = new Weapon { Name = "长剑", DamageMin = 10, DamageMax = 10, Penetration = 0.5, DamageType = DamageType.Sharp };
        var layers = new[]
        {
            new ArmorLayer { Name = "外", Slot = ArmorSlot.Plate, SharpDefense = 40, BluntDefense = 20 },
            new ArmorLayer { Name = "内", Slot = ArmorSlot.Skin, SharpDefense = 40, BluntDefense = 20 },
        };
        // 层1: 适用锐防 40, defMax=40*0.5=20; 攻10 vs 防15 → 10>=7.5 半伤5 转钝 穿透→0
        // 层2: 钝防 20, 穿透0 → defMax=20; 攻 in[0,5]=3 vs 防 2 → 全伤3
        var rng = new SequenceRandomSource(10, 15, 3, 2);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.Equal(0.5, r.Layers[0].PenetrationUsed, 9);
        Assert.True(r.Layers[0].ConvertedToBlunt);
        Assert.Equal(0, r.Layers[1].PenetrationUsed, 9);
        Assert.Equal(20, r.Layers[1].ApplicableDefense); // 钝防
        Assert.Equal(3, r.FinalDamage);
    }

    [Fact]
    public void NaturalBlunt_KeepsPenetrationEachLayer()
    {
        // 天然钝器半伤时不转换、每层保留自身穿透
        var weapon = new Weapon { Name = "破甲锤", DamageMin = 10, DamageMax = 10, Penetration = 0.2, DamageType = DamageType.Blunt };
        var layers = new[]
        {
            new ArmorLayer { Name = "外", Slot = ArmorSlot.Plate, SharpDefense = 40, BluntDefense = 20 },
            new ArmorLayer { Name = "内", Slot = ArmorSlot.Skin, SharpDefense = 20, BluntDefense = 10 },
        };
        // 层1: 钝防20 穿透0.2 defMax=16; 攻10 vs 防15 → 半伤5 不转换 穿透仍0.2
        // 层2: 钝防10 穿透0.2 defMax=8; 攻 in[0,5]=3 vs 防2 → 全伤3
        var rng = new SequenceRandomSource(10, 15, 3, 2);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.False(r.Layers[0].ConvertedToBlunt);
        Assert.Equal(0.2, r.Layers[0].PenetrationUsed, 9);
        Assert.Equal(0.2, r.Layers[1].PenetrationUsed, 9); // 天然钝器保留穿透
        Assert.Equal(DamageType.Blunt, r.FinalDamageType);
        Assert.Equal(3, r.FinalDamage);
    }

    [Fact]
    public void OrderOuterToInner_SortsPlateOuterSkin()
    {
        var input = new[]
        {
            new ArmorLayer { Name = "布衣", Slot = ArmorSlot.Skin },
            new ArmorLayer { Name = "板甲", Slot = ArmorSlot.Plate },
            new ArmorLayer { Name = "皮甲", Slot = ArmorSlot.Outer },
        };
        var ordered = CombatResolver.OrderOuterToInner(input);

        Assert.Equal("板甲", ordered[0].Name);
        Assert.Equal("皮甲", ordered[1].Name);
        Assert.Equal("布衣", ordered[2].Name);
    }
}
