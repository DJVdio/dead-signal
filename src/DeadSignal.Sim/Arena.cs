using DeadSignal.Combat;

/// <summary>
/// 极简多方竞技场（param-calibration 的 2v1 校准用）。复用 <see cref="DuelEngine"/> 同款伤害/失血/效果原语
/// （<see cref="CombatResolver"/>/<see cref="VolumeWeightedHitSelector"/>/<see cref="CombatEffectResolver"/> + <see cref="Body"/>），
/// 支持 N vs M：聚焦火力（打对方首个存活者），全局失血 tick。用于确认"道格+布鲁斯 2v1 稳赢"这类压制性结论。
///
/// 与 DuelEngine 的口径**已对齐**：震荡硬打断+冷却清零+抗性窗、手骨折的持久操作惩罚，两边同一套
/// （用户口径「sim 把震荡也要算上」）。此前 Arena 把震荡当"略去只会更保守"的简化省掉了，但那个假设是错的——
/// 钝器的杀伤本来就**建立在打断上**（棍棒对丧尸零流血，全靠 HP 磨），不给它震荡就等于只留代价不留收益：
/// 复跑显示"道格·棍棒 vs 丧尸"被低估到 19%（DuelEngine 同类对局是 30%+）。
/// 仍有意简化的只剩：不建模持握/双持系数（本对局无）。复用 DuelConfig 的失血/攻速节奏常量。
/// </summary>
public static class Arena
{
    public readonly record struct ArenaStat(double TeamAWinRate, double NoLossRate, double AvgDuration);

    /// <summary>
    /// 一场打完的<b>详细结局</b>（<see cref="RunDetailed"/> 用）：胜负 / 时长 之外，还带回 <b>A 队每个人的终局身体</b>。
    /// <para>
    /// 存在理由：<see cref="ArenaStat"/> 只回答"打不打得赢"，而<b>战斗本身就是成本</b> —— 赢了但断一只手、
    /// 躺三个人、把医疗物资烧光，跟毫发无伤地赢，在这个游戏里根本不是一回事。要把"惨胜"算出来，
    /// 就必须看到<b>活下来的人身上还剩什么</b>（骨折 / 切除 / 失能 / 失血），而不是只数一个胜率。
    /// </para>
    /// </summary>
    /// <param name="WinnerTeam">0=A 胜，1=B 胜，−1=超时判平。</param>
    /// <param name="TeamAEnd">A 队每人的终局身体快照（含死者）。</param>
    public readonly record struct ArenaOutcome(
        int WinnerTeam,
        bool AAllAlive,
        double Duration,
        IReadOnlyList<BodySnapshot> TeamAEnd);

    private sealed class Unit
    {
        public required DuelFighter Def;
        public required Body Body;

        /// <summary>本场生效的护甲，已归一为由外到内（入场解析一次；随机装束在此定死，不逐次命中重抽）。</summary>
        public required IReadOnlyList<ArmorLayer> Armor;
        public required int Team;
        public double NextTime;
        public double SpeedMult = 1.0;

        /// <summary>震荡硬打断截止时刻（绝对秒）；now &lt; 此值时该单位处打断态、不能出手。同 DuelEngine 口径。</summary>
        public double InterruptUntil;

        /// <summary>震荡抗性窗截止时刻（绝对秒）；窗内再次被震荡的触发概率 ×ConcussionResistFactor（防死锁）。</summary>
        public double ConcussionResistUntil;
    }

    public static ArenaStat Run(IReadOnlyList<DuelFighter> teamA, IReadOnlyList<DuelFighter> teamB, int seeds)
    {
        int aWins = 0, noLoss = 0;
        double durSum = 0;
        for (int seed = 0; seed < seeds; seed++)
        {
            var o = OneFight(teamA, teamB, new SystemRandomSource(30260712 + seed * 131), capture: false);
            durSum += o.Duration;
            if (o.WinnerTeam == 0) aWins++;
            if (o.WinnerTeam == 0 && o.AAllAlive) noLoss++;
        }
        return new ArenaStat((double)aWins / seeds, (double)noLoss / seeds, durSum / seeds);
    }

    /// <summary>
    /// 同 <see cref="Run"/>，但<b>逐场</b>带回 A 队的终局身体（<see cref="ArenaOutcome"/>）——用于算"赢下来要付什么代价"。
    /// <para>
    /// ⚠️ <b>与 <see cref="Run"/> 随机流逐字节相同</b>：同样的种子序列、同样的 <see cref="OneFight"/>；
    /// 唯一区别是打完之后多 <c>Capture()</c> 一次（纯读，不碰随机源）。所以既有校准（dogcal / endgamecal…）的
    /// 输出零漂移。
    /// </para>
    /// </summary>
    public static IReadOnlyList<ArenaOutcome> RunDetailed(
        IReadOnlyList<DuelFighter> teamA, IReadOnlyList<DuelFighter> teamB, int seeds)
    {
        var outcomes = new List<ArenaOutcome>(seeds);
        for (int seed = 0; seed < seeds; seed++)
        {
            outcomes.Add(OneFight(teamA, teamB, new SystemRandomSource(30260712 + seed * 131), capture: true));
        }
        return outcomes;
    }

    /// <summary>
    /// 打<b>一场</b>（外部喂随机源）。存在理由：<b>连续遭遇</b>——关卡里的敌人不是一次性全扑上来的，
    /// 玩家是一波一波推进的，而<b>伤是累积的</b>（第一波挨的那一刀，第二波还在身上）。
    /// 要建模这个，就得把"上一波打完的身体"喂进下一波，故需要一个逐场可控的入口。
    /// </summary>
    public static ArenaOutcome RunOnce(
        IReadOnlyList<DuelFighter> teamA, IReadOnlyList<DuelFighter> teamB, IRandomSource rng) =>
        OneFight(teamA, teamB, rng, capture: true);

    private static ArenaOutcome OneFight(
        IReadOnlyList<DuelFighter> teamA, IReadOnlyList<DuelFighter> teamB, IRandomSource rng, bool capture)
    {
        var cfg = new DuelConfig();
        var resolver = new CombatResolver(rng);
        var hit = new VolumeWeightedHitSelector(rng);
        var effects = new CombatEffectResolver(rng, cfg.Effects);

        var units = new List<Unit>();
        foreach (var d in teamA) units.Add(MakeUnit(d, 0, cfg, rng));
        foreach (var d in teamB) units.Add(MakeUnit(d, 1, cfg, rng));

        double now = 0;
        int aStart = teamA.Count;

        // A 队终局身体：只在 capture 时才 Capture()（纯读、不碰随机源）；Run 走 capture:false ⇒ 一分额外开销都不付。
        IReadOnlyList<BodySnapshot> SnapA() => capture
            ? units.Where(u => u.Team == 0).Select(u => u.Body.Capture()).ToList()
            : Array.Empty<BodySnapshot>();

        while (true)
        {
            bool aAlive = units.Any(u => u.Team == 0 && Fightable(u));
            bool bAlive = units.Any(u => u.Team == 1 && Fightable(u));
            if (!aAlive || !bAlive)
            {
                bool aAllAlive = units.Where(u => u.Team == 0).All(u => !u.Body.IsDead);
                int winner = aAlive ? 0 : (bAlive ? 1 : -1);
                return new ArenaOutcome(winner, aAllAlive && winner == 0, now, SnapA());
            }
            if (now > cfg.MaxSimSeconds)
            {
                return new ArenaOutcome(-1, false, now, SnapA()); // 超时判平（2v1 极少发生）
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
                return new ArenaOutcome(-1, false, now, SnapA());
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
                    Attack(next, target, resolver, hit, effects, cfg, rng, now);
                }
                next.NextTime = now + EffectiveInterval(next, cfg);
            }
        }
    }

    private static bool Fightable(Unit u) =>
        !u.Body.IsDead && !u.Body.IsUnconscious && u.Body.DisabilityModifiers.OperationPenalty < 1.0;

    private static Unit MakeUnit(DuelFighter d, int team, DuelConfig cfg, IRandomSource rng)
    {
        var body = d.BodyFactory();
        body.BleedRatePerWound = cfg.BleedRatePerWound;
        body.SetBloodMax(cfg.BloodMax);
        // 挂了 ArmorFactory 的（丧尸的「生前装束」）在此逐只现抽——一波 M 只丧尸各穿各的，且每场重抽。
        // 无工厂者走静态 Armor、不碰随机源 → 既有基线随机流零漂移。
        var u = new Unit
        {
            Def = d, Body = body, Team = team,
            Armor = CombatResolver.OrderOuterToInner(d.RollArmor(rng)),
        };
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

        // 手部骨折的持久操作/攻速惩罚（与 DuelEngine.EffectiveInterval 同一套口径）。
        double fractureOp = u.Body.HandFractureOperationFactor(
            cfg.Effects.HandFractureOperationMult, cfg.Effects.HandFractureHealedOperationMult,
            cfg.Effects.FractureCapabilityFloor);

        return baseInterval / (transient * operation * fractureOp);
    }

    /// <summary>
    /// 施加一次震荡（与 <see cref="DuelEngine"/> 同口径）：目标进入硬打断，已积累的冷却清零、
    /// 打断结束后从零重走一次完整冷却；抗性窗覆盖「打断 + 首轮重走冷却」，窗内再次被震荡的概率打折（防死锁）。
    /// </summary>
    private static void ApplyConcussion(Unit target, double now, double durationSeconds, DuelConfig cfg)
    {
        target.InterruptUntil = System.Math.Max(target.InterruptUntil, now + System.Math.Max(0, durationSeconds));
        target.NextTime = target.InterruptUntil + EffectiveInterval(target, cfg);
        target.ConcussionResistUntil = System.Math.Max(target.ConcussionResistUntil, target.NextTime);
    }

    private static void Attack(Unit actor, Unit target, CombatResolver resolver,
        VolumeWeightedHitSelector hit, CombatEffectResolver effects, DuelConfig cfg, IRandomSource rng, double now)
    {
        // 整次闪避（同 DuelEngine 口径）。
        if (target.Def.DodgeChance > 0 && rng.Range(0.0, 1.0) < target.Def.DodgeChance)
        {
            return;
        }
        var w = actor.Def.Weapons[0].Weapon;
        var armor = target.Armor; // 入场已解析并归一（随机装束在 MakeUnit 定死，逐次命中不重抽）

        // 多弹丸（霰弹 PelletCount=8）：一发同时打出 N 颗，每颗**各自**选部位、各自逐层结算、各自触发效果。
        // PelletCount 默认 1 → 恰好一次（选部位 → 结算 → 效果），与改造前逐行等价，既有团队战基线零漂移。
        // 注意：本 Arena 是**无空间**模型，8 颗全部落在同一个目标身上（贴脸口径）——真实的"一枪扫到多只丧尸"
        // 由 Godot 空间层的 N 枚独立 Projectile 各自碰撞给出，引擎侧不建模。
        int pellets = Math.Max(1, w.PelletCount);
        for (int i = 0; i < pellets; i++)
        {
            var alive = target.Body.Parts.Values.Where(p => !target.Body.IsGone(p.Name)).ToList();
            if (alive.Count == 0) return;

            var part = hit.Select(alive);
            var result = resolver.Resolve(w, armor, part);
            // 抗性窗内（吃过震荡、打断+首轮冷却尚未走完）再次被震荡的概率打折，防死锁（同 DuelEngine）。
            double concResist = now < target.ConcussionResistUntil ? cfg.Effects.ConcussionResistFactor : 1.0;
            var outcome = effects.Apply(target.Body, w, result, concResist);

            // 震荡消费：硬打断 + 冷却清零（钝器的杀伤本来就建立在打断上，不消费等于只留代价不留收益）。
            foreach (var e in outcome.Effects)
            {
                if (e.Kind == DamageEffectKind.Concussion)
                {
                    ApplyConcussion(target, now, e.DurationSeconds, cfg);
                }
            }

            if (target.Body.IsDead) return; // 目标已死：剩余弹丸不再结算
        }
    }
}
