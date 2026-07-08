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
    public void Watchtower_gives_range_sight_and_block_and_is_solid()
    {
        var s = GuardPostStats.For(GuardPostKind.Watchtower);
        Assert.True(s.RangeMultiplier > 1f);   // 远程 +射程
        Assert.True(s.SightMultiplier > 1f);   // +视野
        Assert.True(s.BlockChance > 0f);       // 围栏抵挡远程
        Assert.False(s.FirstStrike);
        Assert.True(s.IsSolid);
    }

    [Fact]
    public void RoofPlatform_gives_range_sight_no_block_and_is_solid()
    {
        var s = GuardPostStats.For(GuardPostKind.RoofPlatform);
        Assert.True(s.RangeMultiplier > 1f);   // 远程 +射程
        Assert.True(s.SightMultiplier > 1f);   // +视野
        Assert.Equal(0f, s.BlockChance);       // 屋顶无抵挡
        Assert.False(s.FirstStrike);
        Assert.True(s.IsSolid);
    }

    [Fact]
    public void Tower_and_roof_share_range_and_sight_multipliers()
    {
        // 哨塔与屋顶的射程/视野加成一致（皆 +10%）；差异仅在哨塔多一层围栏抵挡。
        var tower = GuardPostStats.For(GuardPostKind.Watchtower);
        var roof = GuardPostStats.For(GuardPostKind.RoofPlatform);
        Assert.Equal(tower.RangeMultiplier, roof.RangeMultiplier);
        Assert.Equal(tower.SightMultiplier, roof.SightMultiplier);
    }

    [Fact]
    public void HiddenPost_gives_firststrike_only_and_is_not_solid()
    {
        var s = GuardPostStats.For(GuardPostKind.HiddenPost);
        Assert.Equal(1f, s.RangeMultiplier);   // 暗哨不延长射程（其价值是首发）
        Assert.Equal(1f, s.SightMultiplier);
        Assert.Equal(0f, s.BlockChance);
        Assert.True(s.FirstStrike);
        Assert.False(s.IsSolid); // 暗哨=非碰撞标记，不挡路、不挖导航洞
    }

    // ---------------- D1 岗位加成纯算法 GuardPostMath ----------------

    [Fact]
    public void EffectiveRangeDistance_compresses_by_multiplier()
    {
        // 倍率 1.1：实际 110 距离被压回武器原生曲线的 100（仍在射程/满伤段内）。
        Assert.Equal(100.0, GuardPostMath.EffectiveRangeDistance(110.0, 1.10f), 3);
        // 倍率 1（非守卫/近战）→ 原样。
        Assert.Equal(110.0, GuardPostMath.EffectiveRangeDistance(110.0, 1f), 3);
        // 倍率 <=0 兜底原样，不除零。
        Assert.Equal(110.0, GuardPostMath.EffectiveRangeDistance(110.0, 0f), 3);
    }

    [Fact]
    public void EffectiveRangeDistance_extends_firing_reach_via_ballistics()
    {
        // +10% 岗位射程：原本超程（210>200）的距离在等效换算后落回射程内（190.9<=200）。
        var pistol = WeaponTable.Pistol(); // MaxRange=200
        Assert.False(Ballistics.InRange(210.0, pistol));
        Assert.True(Ballistics.InRange(GuardPostMath.EffectiveRangeDistance(210.0, 1.10f), pistol));
    }

    [Fact]
    public void EffectiveSight_scales_base_radius()
    {
        Assert.Equal(220f, GuardPostMath.EffectiveSight(200f, 1.10f), 3);
        Assert.Equal(200f, GuardPostMath.EffectiveSight(200f, 1f), 3);
    }

    [Fact]
    public void RangedBlocked_rolls_against_chance()
    {
        // blockChance=0（屋顶/暗哨/近战）→ 恒不免，且不消耗随机。
        Assert.False(GuardPostMath.RangedBlocked(0f, new SequenceRandomSource()));
        // 掷值 < 0.25 → 免掉；>= 0.25 → 不免。
        Assert.True(GuardPostMath.RangedBlocked(0.25f, new SequenceRandomSource(0.10)));
        Assert.False(GuardPostMath.RangedBlocked(0.25f, new SequenceRandomSource(0.40)));
    }

    [Fact]
    public void FirstStrikeReach_uses_maxrange_for_ranged_else_melee()
    {
        // 远程：MaxRange×射程倍率。
        Assert.Equal(220.0, GuardPostMath.FirstStrikeReach(isRanged: true, maxRange: 200.0, meleeRange: 32f, rangeMultiplier: 1.10f), 3);
        // 远程无 MaxRange（罕见）→ 无限远。
        Assert.Equal(double.PositiveInfinity, GuardPostMath.FirstStrikeReach(isRanged: true, maxRange: null, meleeRange: 32f, rangeMultiplier: 1f));
        // 近战：用 AttackRange，忽略 MaxRange。
        Assert.Equal(32.0, GuardPostMath.FirstStrikeReach(isRanged: false, maxRange: 200.0, meleeRange: 32f, rangeMultiplier: 1.10f), 3);
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
    }

    [Fact]
    public void Breach_consequence_is_heavier_than_guardsfell()
    {
        var breach = RaidResolution.ConsequenceFor(
            new RaidEvaluation { State = RaidState.Overrun, Reason = OverrunReason.Breached });
        var fell = RaidResolution.ConsequenceFor(
            new RaidEvaluation { State = RaidState.Overrun, Reason = OverrunReason.GuardsFell });
        Assert.True(breach.FoodLoss > fell.FoodLoss);
    }

    [Fact]
    public void CampResources_ApplyRaidLoss_deducts_and_clamps()
    {
        var res = new CampResources(food: 3);
        res.ApplyRaidLoss(foodLoss: 5); // 超过当前值，应夹到 0
        Assert.Equal(0, res.Food);
    }

    [Fact]
    public void CampResources_ApplyRaidLoss_partial()
    {
        var res = new CampResources(food: 10);
        res.ApplyRaidLoss(foodLoss: 4);
        Assert.Equal(6, res.Food);
    }
}
