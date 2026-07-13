namespace DeadSignal.Combat;

/// <summary>
/// 战斗结算引擎。实现设计文档第 5 节"护甲与逐层结算"的三段判定。
///
/// 规则口径（用户拍板，不得自行放宽/收窄）：
/// 1. 攻方在当前伤害区间内 roll；防方在 [0, 适用防御 × (1 − 攻方穿透)] 内 roll（下限 0，无保底）。
/// 2. 攻 ≥ 防：全额伤害，穿入下一层。
/// 3. 防/2 ≤ 攻 &lt; 防：半额伤害；锐器→钝器且穿透归零；天然钝器每层保留自身穿透。
/// 4. 攻 &lt; 防/2：无伤害，结算终止。
/// 5. 穿透所有层后，剩余伤害作用到部位时向上取整、最低 1；中途终止为 0（不适用保底）。
///
/// 关键口径（文档算例）：从第二层起，攻方 roll 区间 = [0, 上一层结算后的伤害值]。
/// 例（武器 2-12）：第一层攻 10 vs 防 11 → 半伤 5、转钝；
/// 第二层攻方在 [0, 5] 内 roll，攻 4 vs 防 2 → 全伤 4。
/// </summary>
public sealed class CombatResolver
{
    private readonly IRandomSource _rng;

    public CombatResolver(IRandomSource rng)
    {
        _rng = rng;
    }

    /// <summary>
    /// 结算<b>一发</b>攻击——含霰弹的多弹丸（<see cref="Weapon.PelletCount"/> &gt; 1）。
    ///
    /// <para><b>「8 颗弹丸单独计算」（用户原话）的落地方式</b>：循环 N 次，每颗弹丸
    /// ①<b>独立</b>调 <paramref name="selectPart"/> 选自己的命中部位（故一枪可同时中头、胸、左臂）；
    /// ②<b>独立</b>走一整条 <see cref="Resolve"/> 判定链（自己的攻 roll、自己的防 roll、自己的三段判定、
    /// 自己的穿透逐层生效）。**不是**"结算一次伤害再 ×N"——弹丸之间不共享任何 roll。</para>
    ///
    /// <para><b>向后兼容（零漂移）</b>：<see cref="Weapon.PelletCount"/> 默认 1 → 恰好一次
    /// <paramref name="selectPart"/> + 一次 <see cref="Resolve"/>，随机流消耗与既有"选部位→结算"路径
    /// <b>位级一致</b>，既有 Sim 基线不漂移。</para>
    ///
    /// <paramref name="selectPart"/> 每颗弹丸调一次（由调用方注入，通常是
    /// <see cref="VolumeWeightedHitSelector"/> 对当前存活部位的加权抽取——部位选择本身也走可注入随机源）。
    /// </summary>
    /// <param name="incomingDamageReduction">
    /// 防方**乘算减伤**（0=无减免，默认；见 <see cref="Resolve"/>）。每颗弹丸各自减免（对多弹丸等价于整发减免同一比例）。
    /// </param>
    public VolleyResult ResolveVolley(Weapon weapon, IReadOnlyList<ArmorLayer> layers, Func<BodyPart> selectPart,
        double incomingDamageReduction = 0.0)
    {
        int pellets = Math.Max(1, weapon.PelletCount);
        var results = new List<CombatResult>(pellets);

        for (int i = 0; i < pellets; i++)
        {
            // 每颗弹丸：先独立选自己的命中部位，再独立走完整判定链。
            BodyPart part = selectPart();
            results.Add(Resolve(weapon, layers, part, incomingDamageReduction));
        }

        return new VolleyResult { Pellets = results };
    }

    /// <summary>
    /// 结算一次命中（<b>单颗</b>弹丸/单发）。<paramref name="layers"/> 须按从外到内顺序排列
    /// （可用 <see cref="OrderOuterToInner"/> 归一）。多弹丸走 <see cref="ResolveVolley"/>。
    /// </summary>
    /// <param name="incomingDamageReduction">
    /// <b>防方乘算减伤层</b>（比例 0..1，<b>默认 0＝无减免</b>）：作用在**护甲三段判定之后**——
    /// 护甲先按既有规则吃一遍（攻/防 roll、半伤转钝、挡下终止都不受本参数影响），
    /// **穿透后剩下的伤害**再 ×(1−reduction)。被甲挡下(<see cref="CombatResult.Terminated"/>)时仍是 0 伤，
    /// 减伤不会把 0 抬回 <see cref="MinLandedDamage"/>。
    ///
    /// <para><see cref="CombatResult.RawDamage"/> 保持为**护甲结算后、减伤前**的值（战报诚实：能看出"甲挡了多少、
    /// 体格又免了多少"），减伤只体现在 <see cref="CombatResult.FinalDamage"/>。</para>
    ///
    /// <para><b>零回归</b>：不传即 0 → 不乘任何东西、不消耗随机流，与既有路径**逐位一致**（既有 Sim 基线不漂移）。
    /// 现阶段唯一的非零来源是**山姆 1 级"比常人耐揍"−10%**（<c>SamPerk.IncomingDamageReduction</c>）；
    /// 其余角色恒 0。这是引擎里**第一层"按比例减伤"**——此前只有护甲的三段判定，没有乘算减伤。</para>
    /// </param>
    public CombatResult Resolve(Weapon weapon, IReadOnlyList<ArmorLayer> layers, BodyPart part,
        double incomingDamageReduction = 0.0)
    {
        // 只应用覆盖命中「该具体部位」的护甲层。默认全覆盖（CoversParts=null）→ 现有护甲行为不变；
        // 局部护甲（如左手套仅左手）在其它部位命中（含右手）时被过滤，等效于该部位无此层。
        if (layers.Any(l => !l.Covers(part)))
        {
            layers = layers.Where(l => l.Covers(part)).ToList();
        }

        var log = new List<LayerResolution>(layers.Count);

        DamageType currentType = weapon.DamageType;
        double currentPen = Math.Clamp(weapon.Penetration, 0, 1);

        // 第一层攻方在武器原始区间 roll；此后每层在 [0, 上一层结算后的伤害值] roll。
        double atkMin = weapon.DamageMin;
        double atkMax = weapon.DamageMax;

        double carriedDamage = 0;
        bool terminated = false;
        int penetrated = 0;
        double initialAttackRoll = 0;

        for (int i = 0; i < layers.Count; i++)
        {
            ArmorLayer layer = layers[i];

            double atk = _rng.Range(atkMin, atkMax);
            if (i == 0)
            {
                initialAttackRoll = atk;
            }
            double applicableDef = layer.DefenseFor(currentType);
            double defMax = Math.Max(0, applicableDef * (1 - currentPen));
            double def = _rng.Range(0, defMax);

            LayerOutcome outcome;
            bool converted = false;
            DamageType typeBefore = currentType;
            double penUsed = currentPen;

            if (atk >= def)
            {
                outcome = LayerOutcome.Full;
                carriedDamage = atk;
            }
            else if (atk >= def / 2.0)
            {
                outcome = LayerOutcome.Half;
                carriedDamage = atk / 2.0;
                if (currentType == DamageType.Sharp)
                {
                    // 锐器转钝、穿透归零。天然钝器不进入此分支的转换（保留自身穿透）。
                    currentType = DamageType.Blunt;
                    currentPen = 0;
                    converted = true;
                }
            }
            else
            {
                outcome = LayerOutcome.Blocked;
                carriedDamage = 0;
                terminated = true;
            }

            log.Add(new LayerResolution
            {
                LayerIndex = i,
                LayerName = layer.Name,
                AttackRoll = atk,
                DefenseRoll = def,
                ApplicableDefense = applicableDef,
                PenetrationUsed = penUsed,
                DamageTypeBefore = typeBefore,
                DamageTypeAfter = currentType,
                ConvertedToBlunt = converted,
                Outcome = outcome,
                DamageAfterLayer = carriedDamage,
            });

            if (terminated)
            {
                break;
            }

            penetrated++;
            // 下一层攻方区间 = [0, 本层结算后的伤害值]。
            atkMin = 0;
            atkMax = carriedDamage;
        }

        double rawDamage;
        double finalDamage;

        // 防方乘算减伤（护甲之后的**独立一层**）：0=无减免 → 乘 1.0，行为与既有逐位一致。
        double keep = 1.0 - Math.Clamp(incomingDamageReduction, 0, 1);

        if (terminated)
        {
            // 被甲挡下：0 伤。减伤层不介入（不把 0 抬成 MinLandedDamage）。
            rawDamage = 0;
            finalDamage = 0;
        }
        else if (layers.Count == 0)
        {
            // 无甲直击：武器伤害直接作用到部位。
            rawDamage = _rng.Range(weapon.DamageMin, weapon.DamageMax);
            initialAttackRoll = rawDamage;
            finalDamage = LandedMin(rawDamage * keep);
        }
        else
        {
            rawDamage = carriedDamage;
            finalDamage = LandedMin(rawDamage * keep);
        }

        return new CombatResult
        {
            HitPart = part,
            FinalDamageType = currentType,
            RawDamage = rawDamage,
            FinalDamage = finalDamage,
            Terminated = terminated,
            Layers = log,
            LayersPenetrated = penetrated,
            InitialAttackRoll = initialAttackRoll,
        };
    }

    /// <summary>命中即生效的伤害下限（[SPEC-B14-补6 伤害不取整] 用户裁决"伤害也改小数"）：
    /// 不再向上取整，仅对已命中（穿透）的伤害兜一个 <see cref="MinLandedDamage"/> 下限，防 0 伤空砍。</summary>
    private static double LandedMin(double raw) => Math.Max(MinLandedDamage, raw);

    /// <summary>命中最小有效伤害（0.01=白银同精度的最小刻度，防止穿透后 0 伤/极小值退化）。</summary>
    public const double MinLandedDamage = 0.01;

    /// <summary>把护甲层按 <see cref="ArmorSlot"/> 从外（Plate）到内（Skin）排序。</summary>
    public static IReadOnlyList<ArmorLayer> OrderOuterToInner(IEnumerable<ArmorLayer> layers) =>
        layers.OrderBy(l => (int)l.Slot).ToList();
}
