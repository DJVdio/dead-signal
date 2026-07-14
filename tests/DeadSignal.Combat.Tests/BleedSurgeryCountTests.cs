using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【T58】「**这样也能防止过多的伤口浪费手术时间**」（用户原话）的**跑通两层**验收。
///
/// <para>
/// 🔴 本项目最狠的教训：**纯逻辑绿 ≠ 功能生效**。所以这里不测 <c>BleedModel</c> 的合并函数
/// （那已经在 <c>BleedLethalityTests</c> 里钉过），而是**从战斗引擎一路走到手术系统**：
/// 引擎侧 <see cref="CombatEffectResolver"/> 真的打了三刀 → <see cref="Body"/> 真的合并成一处
/// → 消费层 <c>HealthMapping.SeedFromBody</c> 真的只出**一台**手术，且这台手术的严重度**随等级走**。
/// </para>
/// </summary>
public class BleedSurgeryCountTests
{
    private static Weapon Blade() => new()
    {
        Name = "刀", DamageMin = 1, DamageMax = 1, Penetration = 0, DamageType = DamageType.Sharp,
    };

    private static CombatResult Hit(Body body, string part, double dmg) => new()
    {
        HitPart = body.Parts[part],
        FinalDamage = dmg,
        FinalDamageType = DamageType.Sharp,
        InitialAttackRoll = dmg,
    };

    /// <summary>
    /// 🔴 **同一个部位砍三刀 = 一台手术，不是三台**（用户要的那条）。
    /// 而且这台手术是一台**大流血**的手术（三个小口子合成一个大口子），不是三台轻飘飘的小手术。
    /// </summary>
    [Fact]
    public void ThreeCutsOnOnePart_ProduceOneSurgery_NotThree()
    {
        var body = HumanBody.NewBody();
        // 流血概率门：0.0 ⇒ 每刀必挂（本测试要的是"挂上之后合并成几处"，不是"挂不挂得上"）
        var fx = new CombatEffectResolver(new SequenceRandomSource(0.0, 0.0, 0.0));

        // 胸部 MaxHp = 20；每刀 5 点 = 25% ⇒ 小流血。砍三刀：小 → 中 → 大。
        for (int i = 0; i < 3; i++)
        {
            fx.Apply(body, Blade(), Hit(body, HumanBody.Chest, 5));
        }

        // ① 引擎层：合并成**一处**，等级封顶大流血
        Assert.Equal(1, body.BleedingWoundCount);
        Assert.Equal(BleedModel.BleedSeverity.Large, body.BleedSeverityOn(HumanBody.Chest));

        // ② 消费层（Godot 手术系统）：真的只建**一条** Bleeding 伤病 ⇒ **一台手术**
        HealthConditionSet set = HealthMapping.SeedFromBody(body);
        HealthCondition[] bleeds = set.Conditions
            .Where(c => c.Type == HealthConditionType.Bleeding).ToArray();

        Assert.Single(bleeds);
        Assert.Equal(HumanBody.Chest, bleeds[0].BodyPart);

        // ③ 而且难度/剩余抢救时间随**等级**走，不是随伤口个数走：
        //    大流血 severity 0.70 ⇒ 未手术每昼夜恶化 0.10 ⇒ **只剩 3 昼夜**。
        Assert.Equal(BleedModel.ConditionSeverityOf(BleedModel.BleedSeverity.Large), bleeds[0].Severity, 9);
        Assert.True(bleeds[0].LethalBleed);
    }

    /// <summary>
    /// 对照组：**三个不同部位**各挨一刀 ⇒ 三处出血 ⇒ **三台手术**（合并只发生在同一部位内）。
    /// 这条防止"合并"被误实现成"全身只留一处伤"。
    /// </summary>
    [Fact]
    public void ThreeCutsOnThreeParts_StillProduceThreeSurgeries()
    {
        var body = HumanBody.NewBody();
        var fx = new CombatEffectResolver(new SequenceRandomSource(0.0, 0.0, 0.0));

        fx.Apply(body, Blade(), Hit(body, HumanBody.Chest, 5));
        fx.Apply(body, Blade(), Hit(body, HumanBody.LeftLeg, 5));
        fx.Apply(body, Blade(), Hit(body, HumanBody.RightLeg, 5));

        Assert.Equal(3, body.BleedingWoundCount);

        HealthConditionSet set = HealthMapping.SeedFromBody(body);
        Assert.Equal(3, set.Conditions.Count(c => c.Type == HealthConditionType.Bleeding));
    }

    /// <summary>
    /// 🔴 手术的**严重度真的分了三档**（不再是所有伤口一个平摊的 0.35）——
    /// 这是「手术时间/抢救窗随**等级**走，不随**伤口个数**走」的落地证据。
    /// </summary>
    [Fact]
    public void SurgerySeverity_ScalesWithBleedLevel_NotWoundCount()
    {
        double[] severities = new[]
        {
            BleedModel.BleedSeverity.Small,
            BleedModel.BleedSeverity.Medium,
            BleedModel.BleedSeverity.Large,
        }.Select(lvl =>
        {
            var b = HumanBody.NewBody();
            b.RegisterBleed(HumanBody.Chest, lvl);
            return HealthMapping.SeedFromBody(b).Conditions
                .Single(c => c.Type == HealthConditionType.Bleeding).Severity;
        }).ToArray();

        Assert.True(severities[0] < severities[1], "小流血的手术必须比中流血轻");
        Assert.True(severities[1] < severities[2], "中流血的手术必须比大流血轻");

        // 未手术的致命失血每昼夜恶化 0.10、到 1.0 死亡 ⇒ 大流血只剩 3 昼夜可救。
        Assert.Equal(0.70, severities[2], 9);
    }
}
