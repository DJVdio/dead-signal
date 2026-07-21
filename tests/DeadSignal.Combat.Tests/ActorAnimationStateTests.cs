using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

public sealed class ActorAnimationStateTests
{
    private static string Script(string name)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeadSignal.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "godot", "scripts", name));
    }

    [Fact]
    public void TransientCombatAndMovementOverrideLoopingActivities()
    {
        Assert.Equal(ActorAnimationState.Hit,
            ActorAnimationCatalog.Resolve(true, true, true, PawnRole.Producing, false, false, PawnVisualActivity.Working));
        Assert.Equal(ActorAnimationState.Attack,
            ActorAnimationCatalog.Resolve(true, true, false, PawnRole.Producing, false, false, PawnVisualActivity.Working));
        Assert.Equal(ActorAnimationState.Walk,
            ActorAnimationCatalog.Resolve(true, false, false, PawnRole.Sleeping, false, false, PawnVisualActivity.Lying));
    }

    [Theory]
    [InlineData(PawnRole.Sleeping, false, ActorAnimationState.Lie)]
    [InlineData(PawnRole.Bedrest, false, ActorAnimationState.Lie)]
    [InlineData(PawnRole.Producing, false, ActorAnimationState.Work)]
    [InlineData(PawnRole.Reading, false, ActorAnimationState.ReadStanding)]
    [InlineData(PawnRole.Reading, true, ActorAnimationState.ReadSeated)]
    public void RoleResolvesToExpectedLoop(PawnRole role, bool hasSeat, ActorAnimationState expected)
        => Assert.Equal(expected,
            ActorAnimationCatalog.Resolve(false, false, false, role, false, hasSeat, PawnVisualActivity.None));

    [Fact]
    public void StationingDoesNotFakeWorkOrFootstepsWhenTemporarilyStopped()
        => Assert.Equal(ActorAnimationState.Idle,
            ActorAnimationCatalog.Resolve(false, false, false, PawnRole.Producing, true, false, PawnVisualActivity.None));

    [Fact]
    public void EveryArsenalWeaponHasTheIntendedAttackFamily()
    {
        var expected = new Dictionary<string, WeaponAttackAnimation>
        {
            ["匕首"] = WeaponAttackAnimation.KnifeThrust,
            ["骨刀"] = WeaponAttackAnimation.KnifeThrust,
            ["刺剑"] = WeaponAttackAnimation.KnifeThrust,
            ["短剑"] = WeaponAttackAnimation.SwordSlash,
            ["长剑"] = WeaponAttackAnimation.SwordSlash,
            ["重剑"] = WeaponAttackAnimation.HeavySwing,
            ["消防斧"] = WeaponAttackAnimation.HeavySwing,
            ["棍棒"] = WeaponAttackAnimation.HeavySwing,
            ["尖头锤"] = WeaponAttackAnimation.HeavySwing,
            ["破甲锤"] = WeaponAttackAnimation.HeavySwing,
            ["草叉"] = WeaponAttackAnimation.PolearmThrust,
            ["手枪"] = WeaponAttackAnimation.PistolRecoil,
            ["自制手枪"] = WeaponAttackAnimation.PistolRecoil,
            ["牙医小手枪"] = WeaponAttackAnimation.PistolRecoil,
            ["自制猎枪"] = WeaponAttackAnimation.LongGunRecoil,
            ["冲锋枪"] = WeaponAttackAnimation.LongGunRecoil,
            ["步枪"] = WeaponAttackAnimation.LongGunRecoil,
            ["狙击枪"] = WeaponAttackAnimation.LongGunRecoil,
            ["自制霰弹枪"] = WeaponAttackAnimation.LongGunRecoil,
            ["短弓"] = WeaponAttackAnimation.BowShot,
            ["反曲弓"] = WeaponAttackAnimation.BowShot,
            ["长弓"] = WeaponAttackAnimation.BowShot,
            ["竞技复合弓"] = WeaponAttackAnimation.BowShot,
            ["狩猎弓"] = WeaponAttackAnimation.BowShot,
            ["单手轻弩"] = WeaponAttackAnimation.CrossbowRecoil,
            ["双手重弩"] = WeaponAttackAnimation.CrossbowRecoil,
            ["复合弩"] = WeaponAttackAnimation.CrossbowRecoil,
        };

        foreach (var pair in expected)
            Assert.Equal(pair.Value, ActorAnimationCatalog.AttackFor(pair.Key));

        Assert.Equal(
            expected.Keys.OrderBy(x => x),
            WeaponTable.Arsenal().Select(w => w.Name).OrderBy(x => x));

        Assert.Equal(WeaponAttackAnimation.LongGunRecoil,
            ActorAnimationCatalog.AttackFor("步枪（刺刀型）#7"));
        Assert.Equal(WeaponAttackAnimation.Bite, ActorAnimationCatalog.AttackFor("撕咬"));
        Assert.Equal(WeaponAttackAnimation.Unarmed, ActorAnimationCatalog.AttackFor(null));
    }

    [Fact]
    public void RuntimeSignalsOnlyDeliveredAttacksAndCarriesPaperDollThroughThePose()
    {
        string actor = Script("Actor.cs");
        string sprite = Script("ActorSprite.cs");

        Assert.Contains("VisualAttackSequence++;", actor);
        Assert.Contains("OnAttackDelivered(AttackWeapon);", actor);
        Assert.True(actor.IndexOf("VisualAttackSequence++;", StringComparison.Ordinal)
                    < actor.IndexOf("OnAttackDelivered(AttackWeapon);", StringComparison.Ordinal));
        Assert.Contains("DrawSetTransform(pose.Offset, pose.Rotation, pose.Scale);", sprite);
        Assert.Contains("DrawWornEquipment(r, directionColumn);", sprite);
        Assert.Contains("DrawHeldEquipmentPass(r, directionColumn, behindBody: false);", sprite);
    }

    [Fact]
    public void MealAndSurgeryPublishTheirRealSeatedWorkingAndLyingStates()
    {
        string camp = Script("CampMain.cs");

        Assert.Contains("p.VisualActivity = PawnVisualActivity.Sitting;", camp);
        Assert.Contains("patient!.VisualActivity = PawnVisualActivity.Lying;", camp);
        Assert.Contains("PawnVisualActivity.Working", camp);
        Assert.Contains("VisualActivity = PawnVisualActivity.None;", camp);
    }
}
