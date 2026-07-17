using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [SPEC-B17] 部位细分（躯干→胸+腹、腿→大腿+小腿）纯结构性验证：
/// 新部位存在/树形连带/命中权重归一/切除解剖连带/骨折同档/覆盖任意子集/致命语义。
/// 细分为纯结构性（[SPEC-B17-修]）：胸腹沿原躯干通用档、大小腿沿原腿通用档，不做手工特化。
/// 数值皆"拟定待调"，测试锁规则形态。
/// </summary>
public class PartSubdivisionTests
{
    private static BodyPart Part(string name) => HumanBody.Parts().First(p => p.Name == name);

    // ---- 新部位存在且属性沿既有通用档 ----

    [Fact]
    public void Chest_And_Abdomen_ReplaceTorso_BothVitalTorsoRegion()
    {
        var names = HumanBody.Parts().Select(p => p.Name).ToHashSet();
        Assert.Contains(HumanBody.Chest, names);
        Assert.Contains(HumanBody.Abdomen, names);
        Assert.DoesNotContain("躯干", names); // 原整躯干部位已被拆掉

        foreach (var n in new[] { HumanBody.Chest, HumanBody.Abdomen })
        {
            var p = Part(n);
            Assert.Equal(BodyRegion.Torso, p.Region);              // 沿原躯干通用档
            Assert.Equal(BodyMacroRegion.Torso, p.MacroRegion);
            Assert.Equal(BodyPartCategory.Vital, p.Category);      // 致命语义沿躯干
        }
    }

    [Fact]
    public void Calves_Added_BothLegRegion_SameTierAsThigh()
    {
        var names = HumanBody.Parts().Select(p => p.Name).ToHashSet();
        Assert.Contains(HumanBody.LeftCalf, names);
        Assert.Contains(HumanBody.RightCalf, names);

        // 大/小腿同 Region.Leg / Macro.Leg / Limb（沿原腿通用档，"大小腿默认同档"）。
        foreach (var n in new[] { HumanBody.LeftLeg, HumanBody.LeftCalf, HumanBody.RightLeg, HumanBody.RightCalf })
        {
            var p = Part(n);
            Assert.Equal(BodyRegion.Leg, p.Region);
            Assert.Equal(BodyMacroRegion.Leg, p.MacroRegion);
            Assert.Equal(BodyPartCategory.Limb, p.Category);
        }
    }

    // ---- 树形连带：胸=根，头/双臂/腹挂胸；双腿挂腹；大腿→小腿→脚 ----

    [Fact]
    public void Tree_ChestIsRoot_HeadArmsAbdomenHangOnChest()
    {
        Assert.Null(Part(HumanBody.Chest).Parent);              // 胸=树根
        Assert.Equal(HumanBody.Chest, Part(HumanBody.Abdomen).Parent);
        Assert.Equal(HumanBody.Chest, Part(HumanBody.Head).Parent);
        Assert.Equal(HumanBody.Chest, Part(HumanBody.LeftArm).Parent);
        Assert.Equal(HumanBody.Chest, Part(HumanBody.RightArm).Parent);
    }

    [Fact]
    public void Tree_LegsHangOnAbdomen_ThighCalfFootChain()
    {
        Assert.Equal(HumanBody.Abdomen, Part(HumanBody.LeftLeg).Parent);   // 腿经骨盆/腹
        Assert.Equal(HumanBody.Abdomen, Part(HumanBody.RightLeg).Parent);
        Assert.Equal(HumanBody.LeftLeg, Part(HumanBody.LeftCalf).Parent);  // 大腿→小腿
        Assert.Equal(HumanBody.LeftCalf, Part(HumanBody.LeftFoot).Parent); // 小腿→脚
        Assert.Equal(HumanBody.RightCalf, Part(HumanBody.RightFoot).Parent);
    }

    // ---- 命中权重归一：细分不改各大区域体积权重合计（边际命中分布不变）----

    [Fact]
    public void HitWeights_MacroRegionTotals_PreservedAfterSubdivision()
    {
        var byMacro = HumanBody.Parts()
            .GroupBy(p => p.MacroRegion)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.VolumeWeight));

        // 躯干大区域合计 = 胸20+腹16 = 36（= 原整躯干）；腿大区域 = 大腿7+小腿5 ×2 = 24（= 原双腿 12×2）。
        Assert.Equal(36, byMacro[BodyMacroRegion.Torso], 9);
        Assert.Equal(24, byMacro[BodyMacroRegion.Leg], 9);
    }

    // ---- 切除按解剖：截大腿 → 连带远端小腿+脚+趾；小腿同理 ----

    [Fact]
    public void SeverThigh_TakesCalfFootToes()
    {
        var body = HumanBody.NewBody();
        var sr = body.Sever(HumanBody.LeftLeg);
        Assert.Contains(HumanBody.LeftLeg, sr.RemovedParts);
        Assert.Contains(HumanBody.LeftCalf, sr.RemovedParts);  // 连带远端小腿
        Assert.Contains(HumanBody.LeftFoot, sr.RemovedParts);  // 连带脚
        Assert.Contains(HumanBody.LeftBigToe, sr.RemovedParts); // 连带趾
        Assert.False(sr.CausedDeath);                          // 腿非致死
        Assert.True(body.IsSevered(HumanBody.LeftCalf));
    }

    [Fact]
    public void SeverCalf_TakesFootToes_ThighStays_SideMobility50()
    {
        var body = HumanBody.NewBody();
        var sr = body.Sever(HumanBody.LeftCalf);
        Assert.Contains(HumanBody.LeftCalf, sr.RemovedParts);
        Assert.Contains(HumanBody.LeftFoot, sr.RemovedParts);
        Assert.DoesNotContain(HumanBody.LeftLeg, sr.RemovedParts); // 大腿保住（近端）
        Assert.False(body.IsGone(HumanBody.LeftLeg));

        // 脚随小腿一并 gone → 该侧移动 -50%（与截整条腿一致）。
        body.RecalculatePenalties();
        Assert.Equal(0.5, body.DisabilityModifiers.MobilityPenalty, 9);
    }

    // ---- 骨折同档：大腿/小腿骨折都归并到同一条**下肢**移动惩罚（[SPEC-FRAC-LIMB]）----

    [Fact]
    public void CalfFracture_CountsAsLowerLimbMobilityPenalty_SameAsThigh()
    {
        var thighBody = HumanBody.NewBody();
        thighBody.MarkFractured(HumanBody.LeftLeg);
        double thighFactor = thighBody.LowerLimbFractureMobilityFactor(0.7, 0.85, 0.1);

        var calfBody = HumanBody.NewBody();
        calfBody.MarkFractured(HumanBody.LeftCalf);
        double calfFactor = calfBody.LowerLimbFractureMobilityFactor(0.7, 0.85, 0.1);

        Assert.Equal(thighFactor, calfFactor, 9); // 大小腿骨折同属左下肢：同一乘算系数
        Assert.Equal(0.7, calfFactor, 9);         // 单条未治疗下肢骨折 ×0.7
    }

    // ---- 覆盖任意子集（[SPEC-B17-补] 装备取舍）：护甲可只覆盖胸、不覆盖腹 ----

    [Fact]
    public void Coverage_SupportsArbitrarySubdividedSubset_ChestOnly()
    {
        // 示范"胸甲更轻便但不防腹部"：仅覆盖胸的护甲，命中腹时被过滤。
        var chestOnly = new ArmorLayer
        {
            Name = "胸甲(示范)", Slot = ArmorSlot.Plate, SharpDefense = 20, BluntDefense = 10,
            CoversParts = new HashSet<string> { HumanBody.Chest },
        };
        Assert.True(chestOnly.Covers(Part(HumanBody.Chest)));
        Assert.False(chestOnly.Covers(Part(HumanBody.Abdomen)));

        var weapon = new Weapon { Name = "匕首", DamageMin = 5, DamageMax = 5, Penetration = 0, DamageType = DamageType.Sharp };
        // 命中腹：胸甲被过滤 → 无甲直击。
        var rng = new SequenceRandomSource(5);
        var r = new CombatResolver(rng).Resolve(weapon, new[] { chestOnly }, Part(HumanBody.Abdomen));
        Assert.Empty(r.Layers);
        Assert.False(r.Terminated);
        Assert.Equal(5, r.FinalDamage);
    }

    // ---- 致命语义：胸/腹归零皆致死（沿原躯干档）----

    [Theory]
    [InlineData("胸", 20)]
    [InlineData("腹", 16)]
    public void ZeroingChestOrAbdomen_CausesDeath(string part, int hp)
    {
        var body = HumanBody.NewBody();
        body.ApplyDamage(part, hp);
        Assert.Equal(0, body.HpOf(part));
        Assert.True(body.IsDead);
    }
}
