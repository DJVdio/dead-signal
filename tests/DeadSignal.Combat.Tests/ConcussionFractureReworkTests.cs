using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 骨折/震荡效果重制（用户拍板口径）：
/// ①震荡=2~5s 硬打断 + 已积累冷却清零（打断结束后从零重走完整冷却）+ 打断期移速×0.1（Godot 层）；
///   例：3s 冷却武器冷却推进 2.9s 时吃 2.5s 震荡 → 下次出手 = 打断结束 + 3.0s 满冷却。
/// ②震荡抗性：吃震荡后打断+首轮冷却期间再次震荡概率×0.25（75% 抗性，防死锁）。
/// ③手部骨折 → 操作能力×0.7（含攻速，持久）；腿/脚骨折 → 移动×0.7（Godot 层，对决无位移）。
/// 取代旧的 ConcussionSpeedMult(×0.8) / HandFractureSpeedMult(×0.85) 永久叠乘（已删）。
/// </summary>
public class ConcussionFractureReworkTests
{
    // ---- 构造：单部位躯干靶体（无命中 roll、无甲），把时序/roll 收敛到可精确复现 ----

    private static Body SinglePartBody() => new(new[]
    {
        new BodyPart
        {
            Name = "躯干", VolumeWeight = 1, MaxHp = 50,
            Region = BodyRegion.Torso, MacroRegion = BodyMacroRegion.Torso,
            Category = BodyPartCategory.Vital,
        },
    });

    private static Weapon Blunt1(double interval) => new()
    {
        Name = "锤", DamageMin = 1, DamageMax = 1, Penetration = 0,
        DamageType = DamageType.Blunt, AttackInterval = interval,
    };

    private static Weapon Sharp1(double interval) => new()
    {
        Name = "刃", DamageMin = 1, DamageMax = 1, Penetration = 0,
        DamageType = DamageType.Sharp, AttackInterval = interval,
    };

    private static DuelFighter Fighter(string name, Weapon w) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = w, RequiresHand = false } },
        Armor = System.Array.Empty<ArmorLayer>(),
        BodyFactory = SinglePartBody,
    };

    private static IReadOnlyList<double> AttackTimes(DuelResult r, string attacker) =>
        r.Events.Where(e => e.Attacker == attacker && e.Weapon.Length > 0).Select(e => e.Time).ToList();

    [Fact]
    public void Concussion_ClearsAccumulatedCooldown_ReentersFullAfterInterrupt()
    {
        // 精确算例（用户原话）：B 用 3.0s 冷却武器，冷却推进 2.9s 时被 A 震荡 2.5s。
        // 期望 B 下次出手 = 2.9(受击) + 2.5(打断) + 3.0(重走满冷却) = 8.4（而非无震荡时的 3.0）。
        // 且证明「无永久攻速衰减」：若旧 ×0.8 永久叠乘仍在，重走冷却=3.0/0.8=3.75 → 5.4+3.75=9.15≠8.4。
        var cfg = new DuelConfig
        {
            MaxSimSeconds = 8.41, // 恰好含 B 的 8.4 出手，随后 A 的 8.7 触发超时跳出
            Effects = ConcussionEffects(concussionAlways: true, durationSeconds: 2.5, resistFactor: 0.25),
        };

        var a = Fighter("A", Blunt1(2.9)); // A 首击落在 t=2.9（冷却前置：首发=一个冷却）
        var b = Fighter("B", Sharp1(3.0)); // B 本应 t=3.0 首击

        // 精确 roll 序列（单部位无甲，逐次攻击消耗：伤害 → 震荡 → [时长] → 骨折 或 伤害）：
        //  A@2.9: 伤害1.0, 震荡0.0(触发), 时长2.5, 骨折0.99(不触发)
        //  A@5.8: 伤害1.0, 震荡0.99(抗性期 p=0.25，不触发), 骨折0.99
        //  B@8.4: 伤害1.0, 流血0.99(不触发)
        var rng = new SequenceRandomSource(
            1.0, 0.0, 2.5, 0.99,
            1.0, 0.99, 0.99,
            1.0, 0.99);

        var r = new DuelEngine(rng, cfg).Run(a, b);

        var bTimes = AttackTimes(r, "B");
        Assert.NotEmpty(bTimes);
        Assert.Equal(8.4, bTimes[0], 6); // 冷却清零 + 从零重走满冷却
        Assert.Equal(new[] { 2.9, 5.8 }, AttackTimes(r, "A").Select(t => System.Math.Round(t, 6)));
        Assert.Contains(r.Events, e => e.Attacker == "A" && e.Tags.Any(t => t.StartsWith("震荡")));
        Assert.Equal(0, rng.Remaining); // 序列精确用尽（抗性期第二次震荡未触发→无第二次时长 roll）
    }

    // 震荡必触发（Torso K 拉满、cap=1）+ 确定性时长 + 指定抗性系数，其余走默认。
    private static EffectConfig ConcussionEffects(bool concussionAlways, double durationSeconds, double resistFactor)
    {
        var d = EffectConfig.Default();
        return new EffectConfig
        {
            BleedK = d.BleedK, BleedCap = d.BleedCap,
            FractureK = d.FractureK, FractureCap = d.FractureCap,
            ConcussionHeadK = d.ConcussionHeadK, ConcussionHeadCap = d.ConcussionHeadCap,
            ConcussionTorsoK = concussionAlways ? 10000 : d.ConcussionTorsoK,
            ConcussionTorsoCap = concussionAlways ? 1.0 : d.ConcussionTorsoCap,
            MaxHpErosionFactor = d.MaxHpErosionFactor,
            ConcussionMinSeconds = durationSeconds,
            ConcussionMaxSeconds = durationSeconds,
            ConcussionResistFactor = resistFactor,
            ConcussionMoveSlowFactor = d.ConcussionMoveSlowFactor,
            HandFractureOperationMult = d.HandFractureOperationMult,
            LegFractureMobilityMult = d.LegFractureMobilityMult,
            FractureCapabilityFloor = d.FractureCapabilityFloor,
        };
    }

    // ---- 手部骨折 → 攻速×0.7（对决消费点）----

    private static DuelFighter DaggerAttacker(System.Func<Body> body) => new()
    {
        Name = "A",
        Weapons = new[] { new WeaponMount { Weapon = new Weapon
        {
            Name = "匕首", DamageMin = 4, DamageMax = 14, Penetration = 0.09,
            DamageType = DamageType.Sharp, AttackInterval = 0.7,
        } } },
        Armor = System.Array.Empty<ArmorLayer>(),
        BodyFactory = body,
    };

    private static DuelFighter Dummy() => new()
    {
        Name = "B",
        Weapons = System.Array.Empty<WeaponMount>(),
        Armor = System.Array.Empty<ArmorLayer>(),
    };

    [Fact]
    public void HandFracture_SlowsAttackSpeed_ByThirty()
    {
        // 单处手骨折 → 操作×0.7 → 有效间隔 = 0.7 / 0.7 = 1.0（默认 EffectConfig）。
        Body Fractured()
        {
            var b = HumanBody.NewBody();
            b.MarkFractured(HumanBody.RightHand); // 右手骨折（未切除，匕首仍可握）
            return b;
        }

        var r = new DuelEngine(new SystemRandomSource(7)).Run(DaggerAttacker(Fractured), Dummy());
        var t = AttackTimes(r, "A");
        Assert.True(t.Count >= 2);
        Assert.Equal(1.0, t[1] - t[0], 6); // 未接入时该间隔仍是 0.7（红）
    }

    [Fact]
    public void LegFracture_DoesNotSlowAttackSpeed()
    {
        // 腿骨折只影响移动（Godot 层），不碰操作/攻速 → 间隔仍为基础 0.7。
        Body LegBroken()
        {
            var b = HumanBody.NewBody();
            b.MarkFractured(HumanBody.LeftLeg);
            return b;
        }

        var r = new DuelEngine(new SystemRandomSource(7)).Run(DaggerAttacker(LegBroken), Dummy());
        var t = AttackTimes(r, "A");
        Assert.True(t.Count >= 2);
        Assert.Equal(0.7, t[1] - t[0], 6);
    }

    // ---- Body 骨折能力系数（乘算叠加 + 下限 + 区域区分）----

    [Fact]
    public void HandFractureOperationFactor_MultipliesPerHand_WithFloor()
    {
        var b = HumanBody.NewBody();
        Assert.Equal(1.0, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9); // 无骨折

        b.MarkFractured(HumanBody.RightHand);
        Assert.Equal(0.7, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9); // 单手未治疗

        b.MarkFractured(HumanBody.LeftHand);
        Assert.Equal(0.49, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9); // 双手乘算

        Assert.Equal(0.2, b.HandFractureOperationFactor(0.1, 0.85, 0.2), 9); // 0.1²=0.01 → 锁下限 0.2
    }

    [Fact]
    public void FractureTreated_HalvesPenalty_HealedClearsIt()
    {
        // 用户口径：未治疗手骨折 ×0.7(−30%)；已治疗(手术成功愈合中) ×0.85(−15%)；痊愈 →1.0。
        var b = HumanBody.NewBody();
        b.MarkFractured(HumanBody.RightHand);
        Assert.Equal(0.7, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9); // 未治疗

        b.MarkFractureTreated(HumanBody.RightHand);
        Assert.True(b.IsFractureTreated(HumanBody.RightHand));
        Assert.Equal(0.85, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9); // 已治疗减半

        b.HealFracture(HumanBody.RightHand); // 痊愈
        Assert.False(b.IsFractured(HumanBody.RightHand));
        Assert.False(b.IsFractureTreated(HumanBody.RightHand));
        Assert.Equal(1.0, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9);
    }

    [Fact]
    public void MarkFractureTreated_NoOp_WhenNotFractured()
    {
        // 未骨折的部位标记"已治疗"无效（治疗需先有骨折）。
        var b = HumanBody.NewBody();
        b.MarkFractureTreated(HumanBody.RightHand);
        Assert.False(b.IsFractureTreated(HumanBody.RightHand));
        Assert.Equal(1.0, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9);
    }

    [Fact]
    public void LegFractureMobilityFactor_CountsLegAndFoot_NotHand()
    {
        var b = HumanBody.NewBody();
        b.MarkFractured(HumanBody.LeftLeg);
        Assert.Equal(0.7, b.LegFractureMobilityFactor(0.7, 0.85, 0.2), 9);

        b.MarkFractured(HumanBody.LeftFoot); // 脚归入腿部移动
        Assert.Equal(0.49, b.LegFractureMobilityFactor(0.7, 0.85, 0.2), 9);

        // 手骨折不进移动系数；腿骨折不进操作系数（区域互不串扰）。
        b.MarkFractured(HumanBody.RightHand);
        Assert.Equal(0.49, b.LegFractureMobilityFactor(0.7, 0.85, 0.2), 9);
        Assert.Equal(0.7, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9);
    }

    [Fact]
    public void SeveredHandFracture_NotCounted()
    {
        // 已切除的手其骨折标记不再计入能力（部位已不存在）。
        var b = HumanBody.NewBody();
        b.MarkFractured(HumanBody.RightHand);
        b.Sever(HumanBody.RightHand);
        Assert.Equal(1.0, b.HandFractureOperationFactor(0.7, 0.85, 0.2), 9);
    }
}
