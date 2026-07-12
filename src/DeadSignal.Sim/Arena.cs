using DeadSignal.Combat;

/// <summary>
/// 极简多方竞技场（param-calibration 的 2v1 校准用）。复用 <see cref="DuelEngine"/> 同款伤害/失血/效果原语
/// （<see cref="CombatResolver"/>/<see cref="VolumeWeightedHitSelector"/>/<see cref="CombatEffectResolver"/> + <see cref="Body"/>），
/// 支持 N vs M：聚焦火力（打对方首个存活者），全局失血 tick。用于确认"道格+布鲁斯 2v1 稳赢"这类压制性结论。
///
/// 与 DuelEngine 的差异（有意简化，对 2v1 压制判定无实质影响）：不建模震荡硬打断窗口（丧尸爪击为锐器不致震荡，
/// 己方不吃打断；反向丧尸被道格棍棒震荡只对己方有利，略去只会让结论更保守）；不建模持握/双持系数（本对局无）。
/// 复用 DuelConfig 的失血/攻速节奏常量。
/// </summary>
public static class Arena
{
    public readonly record struct ArenaStat(double TeamAWinRate, double NoLossRate, double AvgDuration);

    private sealed class Unit
    {
        public required DuelFighter Def;
        public required Body Body;
        public required int Team;
        public double NextTime;
        public double SpeedMult = 1.0;
    }

    public static ArenaStat Run(IReadOnlyList<DuelFighter> teamA, IReadOnlyList<DuelFighter> teamB, int seeds)
    {
        int aWins = 0, noLoss = 0;
        double durSum = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            var (winnerTeam, aAllAlive, dur) = OneFight(teamA, teamB, new SystemRandomSource(30260712 + seed * 131));
            durSum += dur;
            if (winnerTeam == 0) aWins++;
            if (winnerTeam == 0 && aAllAlive) noLoss++;
        }
        return new ArenaStat((double)aWins / seeds, (double)noLoss / seeds, durSum / seeds);
    }

    private static (int winnerTeam, bool aAllAlive, double dur) OneFight(
        IReadOnlyList<DuelFighter> teamA, IReadOnlyList<DuelFighter> teamB, IRandomSource rng)
    {
        var cfg = new DuelConfig();
        var resolver = new CombatResolver(rng);
        var hit = new VolumeWeightedHitSelector(rng);
        var effects = new CombatEffectResolver(rng, cfg.Effects);

        var units = new List<Unit>();
        foreach (var d in teamA) units.Add(MakeUnit(d, 0, cfg));
        foreach (var d in teamB) units.Add(MakeUnit(d, 1, cfg));

        double now = 0;
        int aStart = teamA.Count;
        while (true)
        {
            bool aAlive = units.Any(u => u.Team == 0 && Fightable(u));
            bool bAlive = units.Any(u => u.Team == 1 && Fightable(u));
            if (!aAlive || !bAlive)
            {
                bool aAllAlive = units.Where(u => u.Team == 0).All(u => !u.Body.IsDead);
                int winner = aAlive ? 0 : (bAlive ? 1 : -1);
                return (winner, aAllAlive && winner == 0, now);
            }
            if (now > cfg.MaxSimSeconds)
            {
                return (-1, false, now); // 超时判平（2v1 极少发生）
            }

            // 下一个出手者：Fightable 中 NextTime 最小。
            Unit? next = units.Where(Fightable).OrderBy(u => u.NextTime).FirstOrDefault();
            double attackTime = next?.NextTime ?? double.PositiveInfinity;

            bool anyBleeding = units.Any(u => !u.Body.IsDead && u.Body.BleedingWoundCount > 0);
            double eventTime;
            bool isAttack;
            if (anyBleeding && now + cfg.BleedStep < attackTime)
            {
                eventTime = now + cfg.BleedStep;
                isAttack = false;
            }
            else if (next != null)
            {
                eventTime = attackTime;
                isAttack = true;
            }
            else
            {
                return (-1, false, now);
            }

            double dt = System.Math.Max(0, eventTime - now);
            now = eventTime;
            foreach (var u in units)
            {
                if (dt > 0 && !u.Body.IsDead) u.Body.TickBleed(dt);
            }

            if (isAttack && next != null && Fightable(next))
            {
                var target = units.Where(u => u.Team != next.Team && Fightable(u))
                                  .OrderBy(u => u.Team == 0 ? 0 : 0) // 聚焦：首个存活敌人（保持列表序）
                                  .FirstOrDefault();
                if (target != null)
                {
                    Attack(next, target, resolver, hit, effects, cfg, rng);
                }
                next.NextTime = now + EffectiveInterval(next, cfg);
            }
        }
    }

    private static bool Fightable(Unit u) =>
        !u.Body.IsDead && !u.Body.IsUnconscious && u.Body.DisabilityModifiers.OperationPenalty < 1.0;

    private static Unit MakeUnit(DuelFighter d, int team, DuelConfig cfg)
    {
        var body = d.BodyFactory();
        body.BleedRatePerWound = cfg.BleedRatePerWound;
        body.SetBloodMax(cfg.BloodMax);
        var u = new Unit { Def = d, Body = body, Team = team };
        u.NextTime = EffectiveInterval(u, cfg);
        return u;
    }

    private static double EffectiveInterval(Unit u, DuelConfig cfg)
    {
        var w = u.Def.Weapons[0].Weapon;
        double baseInterval = w.AttackInterval > 0 ? w.AttackInterval : 1.0;
        double blood = u.Body.BloodTier switch
        {
            BloodLossTier.Mild => cfg.MildSpeedMult,
            BloodLossTier.Moderate => cfg.ModerateSpeedMult,
            _ => 1.0,
        };
        double transient = System.Math.Max(cfg.MinSpeedMult, u.SpeedMult * blood);
        double operation = 1.0 - u.Body.DisabilityModifiers.OperationPenalty;
        if (operation <= 0) return double.PositiveInfinity;
        return baseInterval / (transient * operation);
    }

    private static void Attack(Unit actor, Unit target, CombatResolver resolver,
        VolumeWeightedHitSelector hit, CombatEffectResolver effects, DuelConfig cfg, IRandomSource rng)
    {
        // 整次闪避（同 DuelEngine 口径）。
        if (target.Def.DodgeChance > 0 && rng.Range(0.0, 1.0) < target.Def.DodgeChance)
        {
            return;
        }
        var w = actor.Def.Weapons[0].Weapon;
        var alive = target.Body.Parts.Values.Where(p => !target.Body.IsGone(p.Name)).ToList();
        if (alive.Count == 0) return;
        var part = hit.Select(alive);
        var armor = CombatResolver.OrderOuterToInner(target.Def.Armor);
        var result = resolver.Resolve(w, armor, part);
        effects.Apply(target.Body, w, result);
    }
}
