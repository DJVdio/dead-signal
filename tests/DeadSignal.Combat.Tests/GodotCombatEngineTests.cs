using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// Godot 消费层 <see cref="CombatEngine"/> 接入引擎 <see cref="Body"/> 的行为验证。
/// 仅测消费侧接线（选部位 → 逐层结算 → 效果结算施加到 Body → 摊平 AttackOutcome），不测引擎规则本身。
/// </summary>
public class GodotCombatEngineTests
{
    private static readonly IReadOnlyList<ArmorLayer> NoArmor = System.Array.Empty<ArmorLayer>();

    /// <summary>单部位躯体：把命中锁定到指定部位，便于确定性断言。</summary>
    private static Body SinglePartBody(string name, double maxHp, BodyPartCategory category) =>
        new(new[]
        {
            new BodyPart
            {
                Name = name, VolumeWeight = 1, MaxHp = maxHp,
                Region = BodyRegion.Hand, Category = category, Parent = null,
            },
        });

    [Fact]
    public void ResolveHit_AppliesDamageIntoBody()
    {
        var body = SinglePartBody("手", maxHp: 30, BodyPartCategory.Limb);
        double before = body.HpOf("手");
        var engine = new CombatEngine(new SystemRandomSource(seed: 1));
        var weapon = new Weapon { Name = "刀", DamageMin = 5, DamageMax = 5, DamageType = DamageType.Sharp };

        var outcome = engine.ResolveHit(weapon, NoArmor, body);

        Assert.Equal("手", outcome.PartName);
        Assert.True(outcome.Damage > 0);
        Assert.False(outcome.Blocked);
        Assert.True(body.HpOf("手") < before); // 伤害确实施加到了 Body
    }

    [Fact]
    public void ResolveHit_BlockedByArmor_LeavesBodyUntouched()
    {
        var body = SinglePartBody("手", maxHp: 30, BodyPartCategory.Limb);
        double before = body.HpOf("手");
        var engine = new CombatEngine(new SystemRandomSource(seed: 7));
        var weapon = new Weapon { Name = "小刀", DamageMin = 1, DamageMax = 1, DamageType = DamageType.Sharp };
        // 超厚甲：攻 1 远小于防/2 → 结算终止、伤害 0。
        var armor = new[] { new ArmorLayer { Name = "钢板", SharpDefense = 100, BluntDefense = 100, Slot = ArmorSlot.Plate } };

        var outcome = engine.ResolveHit(weapon, armor, body);

        Assert.True(outcome.Blocked);
        Assert.Equal(0, outcome.Damage);
        Assert.Equal(before, body.HpOf("手"));
        Assert.False(outcome.Died);
    }

    [Fact]
    public void ResolveHit_SharpOverMaxHp_SeversLimb_NotDead()
    {
        var body = SinglePartBody("手", maxHp: 10, BodyPartCategory.Limb);
        var engine = new CombatEngine(new SystemRandomSource(seed: 3));
        // 无甲 + 单击 ≥ 部位 MaxHp → 必切除；四肢切除致残不致死。
        var weapon = new Weapon { Name = "斧", DamageMin = 40, DamageMax = 40, DamageType = DamageType.Sharp };

        var outcome = engine.ResolveHit(weapon, NoArmor, body);

        Assert.True(outcome.Severed);
        Assert.False(outcome.Died);
        Assert.True(body.IsGone("手"));
    }

    [Fact]
    public void ResolveHit_VitalPartOverMaxHp_KillsBody()
    {
        var body = SinglePartBody("头", maxHp: 10, BodyPartCategory.Vital);
        var engine = new CombatEngine(new SystemRandomSource(seed: 3));
        var weapon = new Weapon { Name = "斧", DamageMin = 40, DamageMax = 40, DamageType = DamageType.Sharp };

        var outcome = engine.ResolveHit(weapon, NoArmor, body);

        Assert.True(outcome.Died);
        Assert.True(body.IsDead);
    }

    [Fact]
    public void ResolveHit_SkipsGoneParts()
    {
        // 一根手臂 + 挂在其下的手；先把手臂切掉（连带手），再打这具躯体：
        // 唯一尚存部位是躯干，命中必落在躯干，不会打到已消失的手/臂。
        var body = new Body(new[]
        {
            new BodyPart { Name = "躯干", VolumeWeight = 5, MaxHp = 50, Region = BodyRegion.Torso, Category = BodyPartCategory.Vital, Parent = null },
            new BodyPart { Name = "臂", VolumeWeight = 3, MaxHp = 12, Region = BodyRegion.Arm, Category = BodyPartCategory.Limb, Parent = "躯干" },
            new BodyPart { Name = "手", VolumeWeight = 1, MaxHp = 8, Region = BodyRegion.Hand, Category = BodyPartCategory.Limb, Parent = "臂" },
        });
        body.Sever("臂");
        Assert.True(body.IsGone("手"));

        var engine = new CombatEngine(new SystemRandomSource(seed: 11));
        var weapon = new Weapon { Name = "刀", DamageMin = 3, DamageMax = 3, DamageType = DamageType.Sharp };

        for (int i = 0; i < 20 && !body.IsDead; i++)
        {
            var outcome = engine.ResolveHit(weapon, NoArmor, body);
            Assert.Equal("躯干", outcome.PartName); // 绝不会选中已切除的臂/手
        }
    }

    [Fact]
    public void SampleShotDirectionDegrees_ZeroSpread_ReturnsAim()
    {
        var engine = new CombatEngine(new SystemRandomSource(seed: 1));
        Assert.Equal(42.0, engine.SampleShotDirectionDegrees(42.0, 0));
    }

    [Fact]
    public void SampleShotDirectionDegrees_StaysWithinCone()
    {
        var engine = new CombatEngine(new SystemRandomSource(seed: 5));
        for (int i = 0; i < 200; i++)
        {
            double d = engine.SampleShotDirectionDegrees(90.0, 6.0);
            Assert.InRange(d, 90.0 - 6.0, 90.0 + 6.0);
        }
    }
}
