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

    public static EffectConfig Default() => new()
    {
        BleedK = 1.0,
        BleedCap = 0.90,
        FractureK = 0.8,
        FractureCap = 0.60,
        ConcussionHeadK = 0.9,
        ConcussionHeadCap = 0.85,
        ConcussionTorsoK = 0.25,
        ConcussionTorsoCap = 0.40,
        MaxHpErosionFactor = 1.0,
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

    public EffectOutcome Apply(Body body, Weapon weapon, CombatResult result)
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
            double p = Clamp01Cap(k * result.InitialAttackRoll / maxHp, cap);
            if (_rng.Range(0, 1) < p)
            {
                effects.Add(new DamageEffect { Kind = DamageEffectKind.Concussion, PartName = partName, Severity = p });
            }
        }

        // 5. 骨折（天然钝器、造成伤害、任意部位）
        if (nativeBlunt && dmg > 0)
        {
            double p = Clamp01Cap(_cfg.FractureK * dmg / maxHp, _cfg.FractureCap);
            if (_rng.Range(0, 1) < p)
            {
                effects.Add(new DamageEffect { Kind = DamageEffectKind.Fracture, PartName = partName, Severity = p });
            }
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
