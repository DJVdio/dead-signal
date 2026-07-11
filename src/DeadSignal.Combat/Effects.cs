namespace DeadSignal.Combat;

/// <summary>
/// 伤害效果种类。设计文档第 5 节"伤害效果"。
/// 锐器：流血、切除；钝器：震荡、骨折（钝器效果隔着未破的甲也能生效）。
/// </summary>
public enum DamageEffectKind
{
    Bleed,
    Sever,
    Concussion,
    Fracture,
}

/// <summary>一个被触发的伤害效果实例。</summary>
public sealed class DamageEffect
{
    public DamageEffectKind Kind { get; init; }
    public string PartName { get; init; } = "";

    /// <summary>严重度 0~1（触发时的概率 p，或切除固定 1）。供叙事/后续减益强度使用。</summary>
    public double Severity { get; init; }

    /// <summary>
    /// 时序时长（秒）。仅 <see cref="DamageEffectKind.Concussion"/> 使用：本次震荡的硬打断时长
    /// （2~5s roll，拟定待调）。其余效果为 0。消费方（对决引擎/Godot 实时层）据此设打断时限与冷却清零。
    /// </summary>
    public double DurationSeconds { get; init; }
}

/// <summary>
/// 效果触发概率参数（全部**拟定待调**）。概率曲线口径（用户确认）：
/// 流血/骨折 = k × 单次伤害 / 部位MaxHP（相对部位血量占比，自适应部位大小）；
/// 震荡 = k × 初始武器roll / 部位MaxHP（头部 MaxHP 小→天然高概率、躯干大→低概率）。
/// </summary>
public sealed class EffectConfig
{
    public double BleedK { get; init; }
    public double BleedCap { get; init; }
    public double FractureK { get; init; }
    public double FractureCap { get; init; }
    public double ConcussionHeadK { get; init; }
    public double ConcussionHeadCap { get; init; }
    public double ConcussionTorsoK { get; init; }
    public double ConcussionTorsoCap { get; init; }

    /// <summary>0HP 部位受实质钝伤时 MaxHp 磨损量 = 本次伤害 × 此系数（拟定待调）。</summary>
    public double MaxHpErosionFactor { get; init; }

    // ---- 震荡时序 + 抗性（重制口径，用户拍板；数值全部拟定待调）----

    /// <summary>震荡硬打断最短时长（秒）。</summary>
    public double ConcussionMinSeconds { get; init; }

    /// <summary>震荡硬打断最长时长（秒）。触发时在 [Min,Max] 均匀 roll（默认待确认=均匀分布）。</summary>
    public double ConcussionMaxSeconds { get; init; }

    /// <summary>
    /// 未击穿护甲（被甲完全挡下=FinalDamage==0，「甲没破人被锤懵」）触发的震荡固定打断时长（秒，用户口径，拟定待调）。
    /// 击穿/无甲命中仍走 [Min,Max] roll。粒度取舍：只把"完全挡下"判为未击穿；减半但穿透仍算击穿走区间 roll。
    /// </summary>
    public double ConcussionBlockedSeconds { get; init; }

    /// <summary>
    /// 震荡抗性系数：吃到一次震荡后，其打断+首轮重走冷却期间，再次震荡的触发概率 ×此系数（0.25=75% 抗性，
    /// 防震荡死锁，用户口径）。1.0=无抗性。抗性窗覆盖打断本身+首轮冷却为默认推导（待确认）。
    /// </summary>
    public double ConcussionResistFactor { get; init; }

    /// <summary>震荡打断期间移动速度系数（Godot 实时层消费；对决引擎无位移故不用）。拟定待调=0.1（−90%）。</summary>
    public double ConcussionMoveSlowFactor { get; init; }

    /// <summary>单处**未治疗**手部骨折对操作能力的乘算系数（0.7=−30% 操作，含攻速，用户口径）。多处乘算叠加。</summary>
    public double HandFractureOperationMult { get; init; }

    /// <summary>单处**已治疗**（手术成功、愈合中）手部骨折的操作系数（0.85=−15%，减半惩罚，用户口径）。</summary>
    public double HandFractureHealedOperationMult { get; init; }

    /// <summary>单处**未治疗**腿/脚骨折对移动能力的乘算系数（0.7=−30% 移速，用户口径）。多处乘算叠加。</summary>
    public double LegFractureMobilityMult { get; init; }

    /// <summary>单处**已治疗**腿/脚骨折的移动系数（0.85=−15%，减半惩罚，用户口径）。</summary>
    public double LegFractureHealedMobilityMult { get; init; }

    /// <summary>多处骨折乘算叠加后的能力系数下限（防止叠到过低，拟定待确认）。</summary>
    public double FractureCapabilityFloor { get; init; }

    // ---- 命中减速（通用，RimWorld stagger 式；用户口径「未破防也会短暂减速 是 所有角色都是这样的（参考rimworld）」）----

    /// <summary>
    /// 命中减速时长（秒，拟定待调=1.0）。**任何攻击命中任何角色、无论是否击穿护甲**都施加，只降移速、
    /// 短暂即恢复。与震荡严格区分：减速=轻量通用（无攻击打断、无冷却清零、无概率 roll——命中即触发，
    /// 重复命中刷新时长、不叠幅度）；震荡=既有重效果（<see cref="ConcussionMinSeconds"/> 一系）不变。
    /// 移动层效果——对决引擎 <see cref="DuelEngine"/> 不建模位移，故减速对对决胜率零影响，引擎只透传数值。
    /// </summary>
    public double StaggerSeconds { get; init; }

    /// <summary>命中减速期间移速系数（Godot 实时层消费，拟定待调=0.6=−40%）。比震荡打断 ×0.1 轻。</summary>
    public double StaggerSpeedMult { get; init; }

    public static EffectConfig Default() => new()
    {
        BleedK = 1.0,
        BleedCap = 0.90,
        FractureK = 0.4,   // 拟定待调（0.8→0.4：破甲锤骨折率 60%（96% 撞封顶、武器区分度被抹平）→ ~46%，封顶只在重锤打小部位出现；棍棒 ~17%）
        FractureCap = 0.60,
        ConcussionHeadK = 0.9,
        ConcussionHeadCap = 0.85,
        ConcussionTorsoK = 0.25,
        ConcussionTorsoCap = 0.40,
        MaxHpErosionFactor = 1.0,
        ConcussionMinSeconds = 2.0,
        ConcussionMaxSeconds = 5.0,
        ConcussionBlockedSeconds = 1.0,
        ConcussionResistFactor = 0.25,
        ConcussionMoveSlowFactor = 0.1,
        HandFractureOperationMult = 0.7,
        HandFractureHealedOperationMult = 0.85,
        LegFractureMobilityMult = 0.7,
        LegFractureHealedMobilityMult = 0.85,
        FractureCapabilityFloor = 0.2,
        StaggerSeconds = 1.0,     // 拟定待调
        StaggerSpeedMult = 0.6,   // 拟定待调（−40%，比震荡打断 ×0.1 轻）
    };
}

/// <summary>一次命中的效果结算产物。</summary>
public sealed class EffectOutcome
{
    public HpChange Damage { get; init; }
    public IReadOnlyList<DamageEffect> Effects { get; init; } = Array.Empty<DamageEffect>();
    public bool CausedDeath { get; init; }
    public IReadOnlyList<string> SeveredParts { get; init; } = Array.Empty<string>();

    /// <summary>本次对 0HP 部位造成的 MaxHp 磨损量（0 = 未磨损）。</summary>
    public double MaxHpEroded { get; init; }

    /// <summary>被磨损的部位名（null = 未磨损）。</summary>
    public string? ErodedPart { get; init; }

    /// <summary>因 MaxHp 归 0 而永久损毁的部位（含连带后代；不掉落装备）。</summary>
    public IReadOnlyList<string> DestroyedParts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 本次命中施加的通用减速时长（秒；命中即置=<see cref="EffectConfig.StaggerSeconds"/>，含被甲完全挡下 dmg==0）。
    /// 不进 <see cref="Effects"/> 概率型效果列表（不耗 roll、不产生 Concussion）；由 Godot 实时层消费到移速。0=引擎未配置。
    /// </summary>
    public double StaggerSeconds { get; init; }
}

/// <summary>
/// 效果触发规则。输入一次 <see cref="CombatResult"/>，对 <see cref="Body"/> 施加伤害并按概率触发效果。
///
/// 门控口径（用户终裁）：锐器被护甲降解成钝伤后【无任何状态效果】——不流血/切除/震荡/骨折，只造成伤害。
/// 效果资格看【实际到达部位的伤害类型】。
/// - 流血/切除：仅当伤害仍以锐器抵达部位（result.FinalDamageType==Sharp）。降解成钝的锐器不流血/不切除。
/// - 切除：实际造成伤害 &gt;0 且（单次伤害 ≥ 部位MaxHP，或命中已 0HP 部位）。
///   被甲完全挡下（dmg==0）不切除，部位保住。致死部位达成条件 = 直接死亡（不生成断肢）。
/// - 震荡：仅天然钝器（weapon.DamageType==Blunt）。锐器降解的钝伤不触发。
///   无论是否造成伤害（含被甲完全挡下），仅头/躯干类部位；输入用初始武器 roll（"钝器隔甲生效"）。
/// - 骨折：仅天然钝器，且造成伤害（dmg&gt;0），任意部位。锐器降解的钝伤不触发。
/// - MaxHp 磨损（【非状态效果】，不走概率门控）：对**当前 HP=0** 的部位造成**实质钝伤**
///   （FinalDamageType==Blunt，含锐转钝的降解伤害与天然钝器）且穿甲后 dmg&gt;0 时，永久降低其 MaxHp；
///   被甲完全挡下（dmg==0）不磨损。MaxHp 归 0 → 部位永久损毁（连带后代、致死部位=死亡、不掉落装备）。
///   锐器（未降解）对 0HP 部位造成实质伤害仍走切除，不磨损。
///
/// rng 调用顺序（供测试复现）：切除/磨损为确定性判定不耗 roll；随后依次 流血→震荡→骨折 各最多一次 roll。
/// </summary>
public sealed class CombatEffectResolver
{
    private readonly IRandomSource _rng;
    private readonly EffectConfig _cfg;

    public CombatEffectResolver(IRandomSource rng, EffectConfig? cfg = null)
    {
        _rng = rng;
        _cfg = cfg ?? EffectConfig.Default();
    }

    /// <param name="concussionResistFactor">
    /// 目标当前的震荡抗性系数（默认 1.0=无抗性）。&lt;1 时震荡触发概率按此打折（防死锁，见
    /// <see cref="EffectConfig.ConcussionResistFactor"/>）。抗性窗由调用方（对决/实时层）按其时序判定后传入。
    /// 无论是否抗性，只要天然钝器命中震荡部位就照旧消耗一次震荡 roll（保持 rng 顺序稳定可复现）。
    /// </param>
    public EffectOutcome Apply(Body body, Weapon weapon, CombatResult result, double concussionResistFactor = 1.0)
    {
        string partName = result.HitPart.Name;
        BodyPart part = result.HitPart;
        int dmg = result.FinalDamage;
        double maxHp = body.MaxHpOf(partName); // 命中时的（可能已磨损的）部位上限
        // 效果资格看【实际到达部位的伤害类型】：锐器被降解成钝伤后无任何效果（不流血/切除/震荡/骨折）。
        // 流血/切除仅当伤害仍以锐器抵达；震荡/骨折仅天然钝器。
        bool sharp = result.FinalDamageType == DamageType.Sharp;
        bool bluntArriving = result.FinalDamageType == DamageType.Blunt; // 含锐转钝的降解 + 天然钝器
        bool nativeBlunt = weapon.DamageType == DamageType.Blunt;

        var effects = new List<DamageEffect>();

        double hpBefore = body.HpOf(partName);
        bool wasZeroBefore = hpBefore <= 0 && !body.IsGone(partName);

        // 1. 施加伤害
        HpChange change = body.ApplyDamage(partName, dmg);

        // 2. 切除判定（锐器抵达、造成伤害）
        bool severed = false;
        var severedParts = new List<string>();
        bool death = body.IsDead;
        if (sharp && dmg > 0 && !body.IsGone(partName)
            && (dmg >= maxHp || wasZeroBefore))
        {
            var sr = body.Sever(partName);
            severed = true;
            severedParts.AddRange(sr.RemovedParts);
            death = death || sr.CausedDeath;
            effects.Add(new DamageEffect { Kind = DamageEffectKind.Sever, PartName = partName, Severity = 1.0 });
            // 切除即重创出血：附带流血（不再走概率 roll），登记为持续出血伤口。
            effects.Add(new DamageEffect { Kind = DamageEffectKind.Bleed, PartName = partName, Severity = 1.0 });
            body.RegisterBleed(partName);
        }

        // 2b. MaxHp 磨损（非状态效果）：0HP 部位受实质钝伤（降解钝伤/天然钝器）→ 永久磨损上限，归 0 则损毁。
        double erodedAmount = 0;
        string? erodedPart = null;
        var destroyedParts = new List<string>();
        if (bluntArriving && dmg > 0 && wasZeroBefore && !severed && !body.IsGone(partName))
        {
            var er = body.ErodeMaxHp(partName, dmg * _cfg.MaxHpErosionFactor);
            erodedAmount = er.MaxHpBefore - er.MaxHpAfter;
            erodedPart = partName;
            if (er.Destroyed)
            {
                destroyedParts.AddRange(er.DestroyedParts);
                death = death || er.CausedDeath;
            }
        }

        // 3. 流血（锐器抵达、造成伤害、未切除）
        if (sharp && dmg > 0 && !severed)
        {
            double p = Clamp01Cap(_cfg.BleedK * dmg / maxHp, _cfg.BleedCap);
            if (_rng.Range(0, 1) < p)
            {
                effects.Add(new DamageEffect { Kind = DamageEffectKind.Bleed, PartName = partName, Severity = p });
                body.RegisterBleed(partName);
            }
        }

        // 4. 震荡（天然钝器、头/躯干类部位、无论是否造成伤害）
        if (nativeBlunt && part.ConcussionProne)
        {
            bool torso = part.Region == BodyRegion.Torso;
            double k = torso ? _cfg.ConcussionTorsoK : _cfg.ConcussionHeadK;
            double cap = torso ? _cfg.ConcussionTorsoCap : _cfg.ConcussionHeadCap;
            // 抗性期内触发概率打折（×resistFactor，防震荡死锁）；无抗性时 factor=1.0 概率不变。
            double p = Clamp01Cap(k * result.InitialAttackRoll / maxHp, cap) * concussionResistFactor;
            if (_rng.Range(0, 1) < p)
            {
                // 未击穿护甲（被甲完全挡下 dmg==0）→ 固定短打断 1s（用户口径），不消耗时长 roll；
                // 击穿/无甲（dmg>0）→ 时长 roll（2~5s，拟定待调），走同一 IRandomSource 保持可复现。
                double dur = dmg > 0
                    ? _rng.Range(_cfg.ConcussionMinSeconds, _cfg.ConcussionMaxSeconds)
                    : _cfg.ConcussionBlockedSeconds;
                effects.Add(new DamageEffect
                {
                    Kind = DamageEffectKind.Concussion, PartName = partName, Severity = p, DurationSeconds = dur,
                });
            }
        }

        // 5. 骨折（天然钝器、造成伤害、任意部位）
        if (nativeBlunt && dmg > 0)
        {
            double p = Clamp01Cap(_cfg.FractureK * dmg / maxHp, _cfg.FractureCap);
            if (_rng.Range(0, 1) < p)
            {
                effects.Add(new DamageEffect { Kind = DamageEffectKind.Fracture, PartName = partName, Severity = p });
                body.MarkFractured(partName); // 落到 Body 持久态，供健康页签查询
            }
        }

        // 切除/损毁使肢体或手指消失 → 重算残疾净惩罚（操作/移动）。幂等，仅在有部位移除时触发。
        if (severed || destroyedParts.Count > 0)
        {
            body.RecalculatePenalties();
        }

        return new EffectOutcome
        {
            Damage = change,
            Effects = effects,
            CausedDeath = death,
            SeveredParts = severedParts,
            MaxHpEroded = erodedAmount,
            ErodedPart = erodedPart,
            DestroyedParts = destroyedParts,
            // 命中减速（通用）：命中即触发，无论破防与否、无概率 roll、不打断/不清冷却。仅透传时长供实时层消费移速。
            StaggerSeconds = _cfg.StaggerSeconds,
        };
    }

    private static double Clamp01Cap(double v, double cap) => Math.Clamp(v, 0, Math.Min(1, cap));
}

/// <summary>
/// 效果持续 tick 钩子（供实时层按 dt 推进）。流血失血已由 <see cref="Body.TickBleed"/> 实现；
/// 本接口留给震荡时限衰减、骨折恢复等后续时序效果。
/// </summary>
/// TODO(时序效果): 震荡时限、治疗恢复等。
public interface IEffectTickSink
{
    void Tick(Body body, double deltaSeconds);
}
