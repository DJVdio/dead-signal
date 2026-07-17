using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 四肢级骨折（用户拍板：「软组织不会骨折，只有四肢会」；「手指脚趾也算，但会直接视作上肢/下肢骨折，
/// 一个人身上最多有四处骨折」）。骨折从**部位级**改为**肢级**：
/// 手臂/手/手指→上肢、大腿/小腿/脚/脚趾→下肢，左右由部位名前缀判定；软组织（胸/腹/头/眼/面/耳）无所属肢 ⇒ 永不骨折。
/// 能力后果按肢：上肢骨折 ×操作/攻速、下肢骨折 ×移动，多肢乘算叠加。锐器（含被甲降解成的钝伤）恒不骨折。
/// </summary>
public class LimbFractureTests
{
    private static readonly Weapon BluntW = new() { Name = "钝", DamageType = DamageType.Blunt };
    private static readonly Weapon SharpW = new() { Name = "锐", DamageType = DamageType.Sharp };

    private static CombatResult Hit(Body body, string part, int dmg, DamageType finalType, double initialRoll = 1)
        => new()
        {
            HitPart = body.Parts[part],
            FinalDamage = dmg,
            FinalDamageType = finalType,
            InitialAttackRoll = initialRoll,
            Terminated = dmg == 0,
        };

    // ① 软组织被天然钝器打 · 永不骨折（胸/头/眼/耳，全为非四肢部位）。
    [Theory]
    [InlineData(HumanBody.Chest)]
    [InlineData(HumanBody.Head)]
    [InlineData(HumanBody.LeftEye)]
    [InlineData(HumanBody.LeftEar)]
    public void SoftTissue_NativeBlunt_NeverFractures(string part)
    {
        var body = HumanBody.NewBody();
        // dmg 拉满、骨折 roll 若发生一律喂 0.0（必触发）——只要真掷了就会骨折；断言它根本不掷。
        var res = Hit(body, part, dmg: 12, DamageType.Blunt, initialRoll: 20);
        var rng = new SequenceRandomSource(0.0, 3.0, 0.0, 0.0); // 富余：即便震荡/时长 roll 发生也够用
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
        Assert.Empty(body.FracturedLimbs);
        Assert.False(body.IsFractured(part));
    }

    // ② 四肢部位被天然钝器打 · 标记对应肢骨折。
    [Theory]
    [InlineData(HumanBody.LeftArm, "左上肢")]
    [InlineData(HumanBody.RightHand, "右上肢")]
    [InlineData(HumanBody.LeftLeg, "左下肢")]
    [InlineData(HumanBody.RightFoot, "右下肢")]
    public void LimbPart_NativeBlunt_MarksOwningLimb(string part, string limbName)
    {
        var body = HumanBody.NewBody();
        var res = Hit(body, part, dmg: 3, DamageType.Blunt, initialRoll: 5);
        var rng = new SequenceRandomSource(0.0); // 四肢 → 掷骨折 roll，0.0 必触发
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture && e.PartName == limbName);
        Assert.Equal(new[] { limbName }, body.FracturedLimbs);
        Assert.True(body.IsFractured(part));
        Assert.True(body.IsFractured(limbName));
    }

    // ③ 打中右手拇指 ⇒ 右上肢骨折（不是「拇指骨折」）。
    [Fact]
    public void RightThumbHit_BecomesRightUpperLimbFracture_NotThumb()
    {
        var body = HumanBody.NewBody();
        var res = Hit(body, HumanBody.RightThumb, dmg: 2, DamageType.Blunt, initialRoll: 3);
        var rng = new SequenceRandomSource(0.0);
        var outcome = new CombatEffectResolver(rng).Apply(body, BluntW, res);

        Assert.Contains(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture && e.PartName == "右上肢");
        Assert.Equal(new[] { "右上肢" }, body.FracturedLimbs);
        // 整肢裁定：右手/右臂/其它右手手指都读作骨折；细部位名不单独登记。
        Assert.True(body.IsFractured(HumanBody.RightHand));
        Assert.True(body.IsFractured(HumanBody.RightArm));
        Assert.True(body.IsFractured(HumanBody.RightIndex));
        Assert.DoesNotContain("右手拇指", body.FracturedLimbs);
    }

    // ④ 一人最多 4 处骨折（幂等：反复砸同一肢不增计数）。
    [Fact]
    public void AtMostFourFractures_SameLimbIdempotent()
    {
        var body = HumanBody.NewBody();
        // 四条肢各来一发（分别用肢内不同细部位命中，验证归并）：
        body.MarkFractured(HumanBody.RightThumb);  // 右上肢
        body.MarkFractured(HumanBody.LeftPinky);   // 左上肢
        body.MarkFractured(HumanBody.LeftBigToe);  // 左下肢
        body.MarkFractured(HumanBody.RightCalf);   // 右下肢
        Assert.Equal(4, body.FracturedLimbs.Count);

        // 反复砸同一肢（不同部位、不同次数）—— 计数不增（整肢一处，最多 4）。
        body.MarkFractured(HumanBody.RightHand);
        body.MarkFractured(HumanBody.RightArm);
        body.MarkFractured(HumanBody.RightIndex);
        body.MarkFractured(HumanBody.LeftFoot);
        body.MarkFractured(HumanBody.LeftLeg);
        Assert.Equal(4, body.FracturedLimbs.Count);
        Assert.Equal(
            new[] { "右上肢", "左上肢", "左下肢", "右下肢" }.OrderBy(x => x),
            body.FracturedLimbs.OrderBy(x => x));
    }

    // ⑤ 锐器（未降解 & 降解成钝）恒不骨折。
    [Fact]
    public void Sharp_NeverFractures_EvenOnLimb()
    {
        var body = HumanBody.NewBody();
        // 锐器抵达四肢：即便喂 0.0，也不该掷骨折（骨折仅天然钝器）。
        var res = Hit(body, HumanBody.LeftArm, dmg: 6, DamageType.Sharp, initialRoll: 8);
        var rng = new SequenceRandomSource(0.0); // 若掷骨折会被消耗；这里应留给流血 roll
        var outcome = new CombatEffectResolver(rng).Apply(body, SharpW, res);
        Assert.DoesNotContain(outcome.Effects, e => e.Kind == DamageEffectKind.Fracture);
        Assert.Empty(body.FracturedLimbs);

        // 被甲降解成的钝伤（武器锐、FinalDamageType=Blunt）同样不骨折。
        var body2 = HumanBody.NewBody();
        var res2 = Hit(body2, HumanBody.LeftArm, dmg: 6, DamageType.Blunt, initialRoll: 8);
        var outcome2 = new CombatEffectResolver(new SequenceRandomSource()).Apply(body2, SharpW, res2);
        Assert.DoesNotContain(outcome2.Effects, e => e.Kind == DamageEffectKind.Fracture);
        Assert.Empty(body2.FracturedLimbs);
    }

    // ⑥ 上肢骨折⇒操作惩罚、下肢骨折⇒移动惩罚；左右两上肢都折时乘算叠加；两轴互不串扰。
    [Fact]
    public void UpperHitsOperation_LowerHitsMobility_MultiplicativeAcrossLimbs()
    {
        var body = HumanBody.NewBody();
        body.MarkFractured(HumanBody.LeftHand);  // 左上肢
        body.MarkFractured(HumanBody.RightArm);  // 右上肢
        Assert.Equal(0.49, body.UpperLimbFractureOperationFactor(0.7, 0.85, 0.2), 9); // 0.7×0.7
        Assert.Equal(1.0, body.LowerLimbFractureMobilityFactor(0.7, 0.85, 0.2), 9);   // 无下肢骨折

        body.MarkFractured(HumanBody.LeftFoot);  // 左下肢（脚归并到下肢）
        Assert.Equal(0.7, body.LowerLimbFractureMobilityFactor(0.7, 0.85, 0.2), 9);
        Assert.Equal(0.49, body.UpperLimbFractureOperationFactor(0.7, 0.85, 0.2), 9); // 下肢不串扰操作
    }

    // ⑦ 存档迁移走**真实读档路径**（Body.Restore）：旧 part 级骨折名映射到肢级；软组织旧骨折被丢弃。
    [Fact]
    public void SaveMigration_LegacyPartLevelFractures_MapToLimbs_ViaRestore()
    {
        // 旧存档：part 级骨折名（旧规则允许软组织骨折 ⇒ 存了「胸」）。
        var legacy = new BodySnapshot
        {
            Fractured = new() { HumanBody.RightThumb, HumanBody.LeftCalf, HumanBody.Chest },
            TreatedFractures = new() { HumanBody.RightThumb }, // 旧的拇指已上夹板
        };

        var body = HumanBody.NewBody();
        body.Restore(legacy); // 真实读档入口

        Assert.True(body.IsFractured("右上肢"));
        Assert.True(body.IsFractured("左下肢"));
        Assert.False(body.IsFractured("胸")); // 软组织旧骨折被迁移丢弃（新规则免疫）
        Assert.Equal(2, body.FracturedLimbs.Count);
        Assert.True(body.IsFractureTreated("右上肢")); // 拇指的治疗档归到右上肢

        // Capture→Restore 往返以肢显示名进行，无损。
        var round = HumanBody.NewBody();
        round.Restore(body.Capture());
        Assert.Equal(
            body.FracturedLimbs.OrderBy(x => x),
            round.FracturedLimbs.OrderBy(x => x));
        Assert.True(round.IsFractureTreated("右上肢"));
    }
}
