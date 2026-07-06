using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// D 守卫防御战纯逻辑单测：岗位属性表、袭营波次规模、胜负/后果结算、物资失血。
/// 全部脱 Godot（Link 编入本工程），把"拟定待调"数值方向锁死。
/// </summary>
public sealed class GuardDefenseTests
{
    // ---------------- D1 岗位属性 ----------------

    [Fact]
    public void Watchtower_gives_range_only_and_is_solid()
    {
        var s = GuardPostStats.For(GuardPostKind.Watchtower);
        Assert.True(s.RangeBonus > 0);
        Assert.Equal(0f, s.SightBonus);
        Assert.False(s.FirstStrike);
        Assert.True(s.IsSolid);
    }

    [Fact]
    public void RoofPlatform_gives_sight_only_and_is_solid()
    {
        var s = GuardPostStats.For(GuardPostKind.RoofPlatform);
        Assert.Equal(0f, s.RangeBonus);
        Assert.True(s.SightBonus > 0);
        Assert.False(s.FirstStrike);
        Assert.True(s.IsSolid);
    }

    [Fact]
    public void RoofPlatform_sight_counts_toward_engage_distance()
    {
        // 屋顶平台的视野加成折进"有效交战距离"（真正延长开火距离，非仅提前锁定）。
        var roof = GuardPostStats.For(GuardPostKind.RoofPlatform);
        var tower = GuardPostStats.For(GuardPostKind.Watchtower);
        Assert.Equal(roof.SightBonus, roof.EngageRangeBonus);   // 屋顶：交战距离加成 = 视野
        Assert.Equal(tower.RangeBonus, tower.EngageRangeBonus);  // 哨塔：交战距离加成 = 射程
        Assert.True(roof.EngageRangeBonus > 0);
    }

    [Fact]
    public void HiddenPost_has_no_engage_bonus()
    {
        // 暗哨不延长交战距离（其价值是首发，不是远射）。
        Assert.Equal(0f, GuardPostStats.For(GuardPostKind.HiddenPost).EngageRangeBonus);
    }

    [Fact]
    public void HiddenPost_gives_firststrike_and_is_not_solid()
    {
        var s = GuardPostStats.For(GuardPostKind.HiddenPost);
        Assert.Equal(0f, s.RangeBonus);
        Assert.Equal(0f, s.SightBonus);
        Assert.True(s.FirstStrike);
        Assert.False(s.IsSolid); // 暗哨=非碰撞标记，不挡路、不挖导航洞
    }

    // ---------------- D3 波次规模 ----------------

    [Fact]
    public void ZombieCount_grows_with_day_and_camp_size()
    {
        int early = RaidWave.ZombieCount(day: 1, campSize: 2);
        int laterDay = RaidWave.ZombieCount(day: 10, campSize: 2);
        int biggerCamp = RaidWave.ZombieCount(day: 1, campSize: 8);
        Assert.True(laterDay > early, "天数越大波次越大");
        Assert.True(biggerCamp > early, "营地越大波次越大");
    }

    [Fact]
    public void ZombieCount_scales_with_intensity()
    {
        int normal = RaidWave.ZombieCount(day: 5, campSize: 4, intensity: 1f);
        int harsh = RaidWave.ZombieCount(day: 5, campSize: 4, intensity: 2f);
        Assert.True(harsh > normal);
    }

    [Fact]
    public void ZombieCount_is_clamped_to_bounds()
    {
        Assert.Equal(RaidWave.MinCount, RaidWave.ZombieCount(day: 1, campSize: 0, intensity: 0f));
        Assert.Equal(RaidWave.MaxCount, RaidWave.ZombieCount(day: 999, campSize: 999, intensity: 10f));
    }

    [Fact]
    public void ZombieCount_never_below_one_for_valid_raid()
    {
        Assert.True(RaidWave.ZombieCount(day: 1, campSize: 1) >= 1);
    }

    // ---------------- D4 胜负结算 ----------------

    [Fact]
    public void AllZombiesDead_is_defended()
    {
        var e = RaidResolution.Evaluate(zombiesRemaining: 0, guardsAlive: 2, breached: false);
        Assert.Equal(RaidState.Defended, e.State);
    }

    [Fact]
    public void GuardsFell_with_zombies_left_is_overrun()
    {
        var e = RaidResolution.Evaluate(zombiesRemaining: 3, guardsAlive: 0, breached: false);
        Assert.Equal(RaidState.Overrun, e.State);
        Assert.Equal(OverrunReason.GuardsFell, e.Reason);
    }

    [Fact]
    public void Breach_is_overrun_even_with_guards_alive()
    {
        var e = RaidResolution.Evaluate(zombiesRemaining: 3, guardsAlive: 2, breached: true);
        Assert.Equal(RaidState.Overrun, e.State);
        Assert.Equal(OverrunReason.Breached, e.Reason);
    }

    [Fact]
    public void Breach_takes_priority_over_all_zombies_cleared()
    {
        // 破防判定优先：即便同帧丧尸恰好清零，破防仍算损失（人已受害）。
        var e = RaidResolution.Evaluate(zombiesRemaining: 0, guardsAlive: 2, breached: true);
        Assert.Equal(RaidState.Overrun, e.State);
        Assert.Equal(OverrunReason.Breached, e.Reason);
    }

    [Fact]
    public void Ongoing_when_zombies_and_guards_both_alive()
    {
        var e = RaidResolution.Evaluate(zombiesRemaining: 2, guardsAlive: 2, breached: false);
        Assert.Equal(RaidState.Ongoing, e.State);
    }

    // ---------------- D4 后果 ----------------

    [Fact]
    public void Defended_has_no_consequence()
    {
        var c = RaidResolution.ConsequenceFor(
            RaidResolution.Evaluate(0, 2, false));
        Assert.Equal(0, c.FoodLoss);
        Assert.Equal(0, c.MoraleLoss);
    }

    [Fact]
    public void Breach_consequence_is_heavier_than_guardsfell()
    {
        var breach = RaidResolution.ConsequenceFor(
            new RaidEvaluation { State = RaidState.Overrun, Reason = OverrunReason.Breached });
        var fell = RaidResolution.ConsequenceFor(
            new RaidEvaluation { State = RaidState.Overrun, Reason = OverrunReason.GuardsFell });
        Assert.True(breach.FoodLoss >= fell.FoodLoss);
        Assert.True(breach.MoraleLoss > fell.MoraleLoss);
    }

    [Fact]
    public void CampResources_ApplyRaidLoss_deducts_and_clamps()
    {
        var res = new CampResources(food: 3, morale: 10, moralePenaltyPerMissingMeal: 4, moraleMax: 100);
        res.ApplyRaidLoss(foodLoss: 5, moraleLoss: 25); // 均超过当前值，应夹到 0
        Assert.Equal(0, res.Food);
        Assert.Equal(0, res.Morale);
    }

    [Fact]
    public void CampResources_ApplyRaidLoss_partial()
    {
        var res = new CampResources(food: 10, morale: 80, moralePenaltyPerMissingMeal: 4, moraleMax: 100);
        res.ApplyRaidLoss(foodLoss: 4, moraleLoss: 20);
        Assert.Equal(6, res.Food);
        Assert.Equal(60, res.Morale);
    }
}
