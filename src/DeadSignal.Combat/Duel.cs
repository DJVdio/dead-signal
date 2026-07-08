namespace DeadSignal.Combat;

/// <summary>一把挂载的武器（主手/副手）。</summary>
public sealed class WeaponMount
{
    public Weapon Weapon { get; init; } = null!;

    /// <summary>是否需要手持（true=握持武器，双手皆失则不可用；false=天生武器如爪击）。</summary>
    public bool RequiresHand { get; init; } = true;
}

/// <summary>对决一方的配置（数据驱动）。</summary>
public sealed class DuelFighter
{
    public string Name { get; init; } = "";
    public IReadOnlyList<WeaponMount> Weapons { get; init; } = Array.Empty<WeaponMount>();
    public IReadOnlyList<ArmorLayer> Armor { get; init; } = Array.Empty<ArmorLayer>();

    /// <summary>部位→挂载装备名，用于切除时的掉落战报。</summary>
    public IReadOnlyDictionary<string, string> Equipment { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// 持握态（单手/双手/双持）。默认单手。表达攻速系数：双手 +15%、双持 ×0.70。
    /// 与旧字段 <see cref="DualWielding"/> 的关系见 <see cref="EffectiveGrip"/>。
    /// </summary>
    public GripMode Grip { get; init; } = GripMode.OneHanded;

    /// <summary>
    /// 【旧字段/向后兼容】双持简写。新代码用 <see cref="Grip"/>；此 bool 保留是为不破坏 Sim 等既有调用方
    /// （如 DuelReport 的"双持枪手"）。当 <see cref="Grip"/> 仍为默认单手时，此 bool=true 等价于双持。
    /// </summary>
    public bool DualWielding { get; init; }

    /// <summary>解析生效持握态：显式 <see cref="Grip"/> 优先；仅当其为默认单手且旧 <see cref="DualWielding"/>=true 时回落双持。</summary>
    public GripMode EffectiveGrip =>
        Grip == GripMode.OneHanded && DualWielding ? GripMode.DualWield : Grip;

    /// <summary>身体工厂（默认人类；丧尸等亦复用人体结构）。</summary>
    public Func<Body> BodyFactory { get; init; } = HumanBody.NewBody;
}

/// <summary>
/// 一次对决事件。攻击事件 <see cref="Weapon"/> 非空；失血状态事件（分级/昏迷/致死）<see cref="Weapon"/> 为空、
/// 用 <see cref="Tags"/> 携带 "失血:*"。结构化，供上层渲染战报。
/// </summary>
public sealed record DuelEvent(
    double Time,
    string Attacker,
    string Defender,
    string Weapon,
    string Part,
    string PenetrationDesc,
    int Damage,
    DamageType ArrivedType,
    double PartMaxHp,
    IReadOnlyList<string> Tags);

public enum DuelEndReason
{
    VitalDown,
    Bleedout,
    Stalemate,
    Timeout,
}

public sealed class DuelResult
{
    public IReadOnlyList<DuelEvent> Events { get; init; } = Array.Empty<DuelEvent>();
    public string? Winner { get; init; }
    public string? Loser { get; init; }
    public double DurationSeconds { get; init; }
    public int TotalActions { get; init; }
    public DuelEndReason EndReason { get; init; }
}

/// <summary>对决数值参数（全部拟定待调）。</summary>
public sealed class DuelConfig
{
    /// <summary>每处伤口每秒失血量（写入 Body.BleedRatePerWound）。</summary>
    public double BleedRatePerWound { get; init; } = 1.5;

    /// <summary>对决用储血量上限（拟定待调，比默认 100 略低以让失血分级/昏迷/致死真实出现）。</summary>
    public double BloodMax { get; init; } = 70;

    /// <summary>失血推进步长（秒）：有出血时按此细分时间轴以获得分级/昏迷/致死的合理时点。</summary>
    public double BleedStep { get; init; } = 0.5;

    /// <summary>轻度出血攻速系数。</summary>
    public double MildSpeedMult { get; init; } = 0.9;

    /// <summary>中度出血攻速系数（debuff 加重）。</summary>
    public double ModerateSpeedMult { get; init; } = 0.7;

    public double ConcussionSpeedMult { get; init; } = 0.8;
    public double HandFractureSpeedMult { get; init; } = 0.85;
    public double MinSpeedMult { get; init; } = 0.25;
    public double MaxSimSeconds { get; init; } = 600;
    public EffectConfig Effects { get; init; } = EffectConfig.Default();
}

/// <summary>
/// 确定性 1v1 对决引擎：双方各持 Body（部位树+HP+效果+损毁+储血量），按各武器攻速排时间轴轮流出手，
/// 效果实际生效——流血 tick 扣储血（分级 debuff：轻/中度降攻速、重度昏迷、储血归零出血致死；不扣部位 HP），
/// 震荡/手部骨折降攻速，切除/损毁移除部位，致死部位归零或出血致死判死。
/// 全程单一可注入随机源 → 固定种子可复现。数值口径见 <see cref="DuelConfig"/>（拟定待调）。
/// </summary>
public sealed class DuelEngine
{
    private sealed class Runtime
    {
        public DuelFighter Def = null!;
        public Body Body = null!;
        public double[] NextTime = Array.Empty<double>();
        public double SpeedMult = 1.0;
        public BloodLossTier LastTier = BloodLossTier.None;
        public readonly List<string> LastDropped = new();
    }

    private readonly IRandomSource _rng;
    private readonly DuelConfig _cfg;
    private readonly CombatResolver _resolver;
    private readonly VolumeWeightedHitSelector _hit;
    private readonly CombatEffectResolver _effects;

    public DuelEngine(IRandomSource rng, DuelConfig? cfg = null)
    {
        _rng = rng;
        _cfg = cfg ?? new DuelConfig();
        _resolver = new CombatResolver(rng);
        _hit = new VolumeWeightedHitSelector(rng);
        _effects = new CombatEffectResolver(rng, _cfg.Effects);
    }

    public DuelResult Run(DuelFighter aDef, DuelFighter bDef)
    {
        var a = Init(aDef);
        var b = Init(bDef);
        var events = new List<DuelEvent>();

        double now = 0;
        int actions = 0;
        DuelEndReason reason = DuelEndReason.Timeout;
        Runtime? dead = null;

        while (true)
        {
            var pick = PickNext(a, b);
            double attackTime = pick?.time ?? double.PositiveInfinity;
            bool anyBleeding = AnyBleedingAlive(a, b);

            double eventTime;
            bool isAttack;
            if (anyBleeding && (pick is null || now + _cfg.BleedStep < attackTime))
            {
                eventTime = now + _cfg.BleedStep;
                isAttack = false;
            }
            else if (pick is not null)
            {
                eventTime = attackTime;
                isAttack = true;
            }
            else
            {
                reason = DuelEndReason.Stalemate;
                break;
            }

            double dt = Math.Max(0, eventTime - now);
            now = eventTime;

            TickBleed(a, dt, now, events);
            TickBleed(b, dt, now, events);

            dead = FirstDead(a, b);
            if (dead is not null)
            {
                reason = ReasonFor(dead);
                break;
            }

            if (now > _cfg.MaxSimSeconds)
            {
                reason = DuelEndReason.Timeout;
                break;
            }

            if (isAttack)
            {
                (Runtime actor, int mountIdx, double _) = pick!.Value;
                Runtime target = actor == a ? b : a;

                if (actor.Body.IsDead || actor.Body.IsUnconscious)
                {
                    // 昏迷/已死不能出手，推后此武器源，让时间轴继续（多半会流血而死）。
                    actor.NextTime[mountIdx] = now + _cfg.BleedStep;
                }
                else
                {
                    events.Add(DoAttack(actor, target, actor.Def.Weapons[mountIdx], now));
                    actions++;
                    actor.NextTime[mountIdx] = now + EffectiveInterval(actor, actor.Def.Weapons[mountIdx]);

                    dead = FirstDead(a, b);
                    if (dead is not null)
                    {
                        reason = ReasonFor(dead);
                        break;
                    }
                }
            }
        }

        string? winner = null, loser = null;
        if (dead is not null)
        {
            loser = dead.Def.Name;
            winner = (dead == a ? b : a).Def.Name;
        }

        return new DuelResult
        {
            Events = events,
            Winner = winner,
            Loser = loser,
            DurationSeconds = now,
            TotalActions = actions,
            EndReason = reason,
        };
    }

    private Runtime Init(DuelFighter def)
    {
        var rt = new Runtime
        {
            Def = def,
            Body = def.BodyFactory(),
            NextTime = new double[def.Weapons.Count],
        };
        rt.Body.BleedRatePerWound = _cfg.BleedRatePerWound;
        rt.Body.SetBloodMax(_cfg.BloodMax);
        rt.Body.EquipmentDropped = parts => rt.LastDropped.AddRange(parts);
        return rt;
    }

    private static bool AnyBleedingAlive(Runtime a, Runtime b) =>
        (a.Body.BleedingWoundCount > 0 && !a.Body.IsDead) || (b.Body.BleedingWoundCount > 0 && !b.Body.IsDead);

    // 选出下一个出手：所有可用武器源（活着、未昏迷、有手/天生武器）中 NextTime 最小者，平局随机。
    private (Runtime actor, int mountIdx, double time)? PickNext(Runtime a, Runtime b)
    {
        var cands = new List<(Runtime rt, int idx, double t)>();
        foreach (var rt in new[] { a, b })
        {
            if (rt.Body.IsDead || rt.Body.IsUnconscious)
            {
                continue;
            }

            bool handAlive = !rt.Body.IsGone(HumanBody.LeftHand) || !rt.Body.IsGone(HumanBody.RightHand);
            // 操作能力尽失（断双手/等效残疾使净惩罚 ≥1）→ 无法进行任何武器攻击（含天生武器）。
            bool canOperate = rt.Body.DisabilityModifiers.OperationPenalty < 1.0;
            for (int i = 0; i < rt.Def.Weapons.Count; i++)
            {
                var m = rt.Def.Weapons[i];
                if ((m.RequiresHand && !handAlive) || !canOperate)
                {
                    continue;
                }

                cands.Add((rt, i, rt.NextTime[i]));
            }
        }

        if (cands.Count == 0)
        {
            return null;
        }

        double min = cands.Min(c => c.t);
        var tied = cands.Where(c => c.t <= min + 1e-9).ToList();
        var chosen = tied.Count == 1 ? tied[0] : tied[(int)Math.Min(tied.Count - 1, (int)_rng.Range(0, tied.Count))];
        return (chosen.rt, chosen.idx, chosen.t);
    }

    private double EffectiveInterval(Runtime rt, WeaponMount mount)
    {
        double baseInterval = mount.Weapon.AttackInterval > 0 ? mount.Weapon.AttackInterval : 1.0;
        // 持握态攻速系数：单手 1.0、双手 ×1.15（加速）、双持 ×0.70（减速），互斥（单一枚举）。
        double grip = DualWield.GripSpeedFactor(rt.Def.EffectiveGrip);
        double blood = BloodSpeedFactor(rt.Body.BloodTier);
        // 瞬态系数（持握/震荡/手骨折/失血）叠乘，锁下限 MinSpeedMult。
        double transient = Math.Max(_cfg.MinSpeedMult, grip * rt.SpeedMult * blood);
        // 残疾操作能力（断手/断指/假肢净惩罚，用户口径）另行叠乘、不吃 MinSpeedMult 下限：
        // 有效出手间隔 = 基础 / (瞬态系数 × 操作能力)，操作能力 = 1 − OperationPenalty。
        // 惩罚 0.5 → 间隔翻倍；操作能力 ≤0（惩罚 ≥1）→ 无法出手（间隔视为无穷，除零保护）。
        double operation = 1.0 - rt.Body.DisabilityModifiers.OperationPenalty;
        if (operation <= 0)
        {
            return double.PositiveInfinity;
        }

        return baseInterval / (transient * operation);
    }

    private double BloodSpeedFactor(BloodLossTier tier) => tier switch
    {
        BloodLossTier.Mild => _cfg.MildSpeedMult,
        BloodLossTier.Moderate => _cfg.ModerateSpeedMult,
        BloodLossTier.Severe => _cfg.ModerateSpeedMult, // 昏迷者不出手，此值一般用不到
        _ => 1.0,
    };

    private void TickBleed(Runtime rt, double dt, double now, List<DuelEvent> events)
    {
        if (dt > 0)
        {
            rt.Body.TickBleed(dt);
        }

        var tier = rt.Body.BloodTier;
        if ((int)tier > (int)rt.LastTier)
        {
            events.Add(BloodStatus(rt, tier, now));
            rt.LastTier = tier;
        }
    }

    private static DuelEvent BloodStatus(Runtime rt, BloodLossTier tier, double now)
    {
        string label = tier switch
        {
            BloodLossTier.Mild => "轻度",
            BloodLossTier.Moderate => "中度",
            BloodLossTier.Severe => "重度昏迷",
            BloodLossTier.Dead => "致死",
            _ => "无",
        };
        int pct = (int)Math.Round(rt.Body.BloodRatio * 100);
        return new DuelEvent(now, rt.Def.Name, "", "", "", "", 0, DamageType.Sharp, 0, new[] { $"失血:{label}:{pct}" });
    }

    private DuelEvent DoAttack(Runtime actor, Runtime target, WeaponMount mount, double now)
    {
        var alive = target.Body.Parts.Values.Where(p => !target.Body.IsGone(p.Name)).ToList();
        var part = _hit.Select(alive);
        var armor = CombatResolver.OrderOuterToInner(target.Def.Armor);

        double partMaxAtHit = target.Body.MaxHpOf(part.Name);
        var result = _resolver.Resolve(mount.Weapon, armor, part);

        target.LastDropped.Clear();
        var outcome = _effects.Apply(target.Body, mount.Weapon, result);

        var tags = new List<string>();

        if (outcome.SeveredParts.Count > 0)
        {
            tags.Add("切除:" + part.Name);
            foreach (var r in outcome.SeveredParts)
            {
                if (r != part.Name)
                {
                    tags.Add("连带:" + r);
                }
            }

            foreach (var r in target.LastDropped)
            {
                if (target.Def.Equipment.TryGetValue(r, out var eq))
                {
                    tags.Add("掉落:" + eq);
                }
            }
        }

        if (outcome.DestroyedParts.Count > 0)
        {
            tags.Add("损毁:" + part.Name);
            foreach (var r in outcome.DestroyedParts)
            {
                if (r != part.Name)
                {
                    tags.Add("连带:" + r);
                }
            }
        }
        else if (outcome.MaxHpEroded > 0)
        {
            tags.Add($"磨损:{part.Name}:{outcome.MaxHpEroded:0.#}");
        }

        foreach (var e in outcome.Effects)
        {
            switch (e.Kind)
            {
                case DamageEffectKind.Bleed:
                    if (!tags.Contains("流血:" + e.PartName))
                    {
                        tags.Add("流血:" + e.PartName);
                    }

                    break;
                case DamageEffectKind.Concussion:
                    target.SpeedMult = Math.Max(_cfg.MinSpeedMult, target.SpeedMult * _cfg.ConcussionSpeedMult);
                    tags.Add("震荡:" + e.PartName);
                    break;
                case DamageEffectKind.Fracture:
                    if (target.Body.Parts[e.PartName].Region == BodyRegion.Hand)
                    {
                        target.SpeedMult = Math.Max(_cfg.MinSpeedMult, target.SpeedMult * _cfg.HandFractureSpeedMult);
                    }

                    tags.Add("骨折:" + e.PartName);
                    break;
            }
        }

        if (target.Body.IsDead)
        {
            tags.Add("死亡");
        }

        return new DuelEvent(
            now, actor.Def.Name, target.Def.Name, mount.Weapon.Name, part.Name,
            DescribePenetration(result, armor.Count), result.FinalDamage, result.FinalDamageType,
            partMaxAtHit, tags);
    }

    private static string DescribePenetration(CombatResult result, int armorCount)
    {
        if (armorCount == 0)
        {
            return "直击";
        }

        if (result.Terminated)
        {
            var blocker = result.Layers.Count > 0 ? result.Layers[^1].LayerName : "护甲";
            return $"被{blocker}挡下";
        }

        var names = result.Layers
            .Where(l => l.Outcome != LayerOutcome.Blocked)
            .Select(l => l.LayerName)
            .ToList();
        return names.Count > 0 ? "穿透" + string.Join("、", names) : "穿透护甲";
    }

    private static Runtime? FirstDead(Runtime a, Runtime b)
    {
        if (a.Body.IsDead)
        {
            return a;
        }

        return b.Body.IsDead ? b : null;
    }

    private static DuelEndReason ReasonFor(Runtime rt) =>
        rt.Body.BledOut ? DuelEndReason.Bleedout : DuelEndReason.VitalDown;
}
