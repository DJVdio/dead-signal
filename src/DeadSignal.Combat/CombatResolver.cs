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
/// 关键口径【T59 方案 E·用户拍板，原话】：「**每一层都要重新 roll 攻击值，和上一层穿透过来的伤害值
/// 取较小的那个作为这一层的实际值**」——
/// <code>
/// rolled = Range(武器.DamageMin, 武器.DamageMax)   // 区间【永远是武器的原始区间】，不收缩
/// atk    = min(rolled, 上一层结算后的伤害)          // 上一层带下来的伤害是【上限】
/// </code>
/// 例（武器 2-12）：第一层攻 10 vs 防 11 → 半伤 5、转钝；
/// 第二层重掷 [2,12] 得 8 → min(8, 5) = 5；攻 5 vs 防 2 → 全伤 5。
///
/// <b>旧口径（已废弃，是一个缺陷）</b>：「第二层起攻方在 [0, 上一层结算后的伤害值] 内 roll」
/// ⇒ 旧口径会让期望伤害随层数固定衰减，与该层防御力无关；现行口径由护甲配置与逐层重掷共同决定。
/// 详见 <c>LayerRerollMinTests</c>。
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
    /// <para><b>「多颗弹丸单独计算」（用户原话）的落地方式</b>：循环 N 次，每颗弹丸
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
    /// 现阶段非零来源由角色/装备 Wiki 配置决定；其余角色可为 0。这是引擎里**第一层"按比例减伤"**——
    /// 此前只有护甲的三段判定，没有乘算减伤。</para>
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

        double carriedDamage = 0;
        bool terminated = false;
        int penetrated = 0;
        double initialAttackRoll = 0;

        for (int i = 0; i < layers.Count; i++)
        {
            ArmorLayer layer = layers[i];

            // 【T59 方案 E·用户拍板】每一层都用**武器自己的原始伤害区间**重新 roll，
            // 再与**上一层穿透过来的伤害**取【较小者】作为本层的实际攻方值。
            //
            // 🔴 修掉的缺陷：旧口径是「第二层起在 `[0, 上一层结算后的伤害值]` 内 roll」
            //    ⇒ E[atk'] = carried / 2 ⇒ **每多一层伤害期望就 ×0.5**，且与该层的防御力、
            //    与武器、与伤害类型、与穿透**全部无关**。连一层**防御为 0** 的破布也照样砍半
            //    （防御 0 ⇒ defMax=0 ⇒ 必判 Full ⇒ carried 原样带下，但下一层仍在 [0,carried]
            //    重掷 ⇒ 白送固定衰减）。现行结算结果随护甲配置变化。
            //    还抗揍；两件破布让破甲锤减半，而一件皮甲对它**一点没减**。
            //
            // 新口径为什么对：掷高了 ⇒ 被 carried 卡住 ⇒ **伤害不会越穿越大**（carried 是上限）；
            //    掷低了 ⇒ 取 rolled ⇒ 才吃亏。⇒ **层数不再无条件减半**，衰减有下限
            //    （趋向武器伤害区间的下界），减伤重新由**防御力**说了算。
            //
            // ⚠️ 锐转钝之后仍用**武器的原始区间**重掷：转换改的是 `currentType`（→钝）与
            //    `currentPen`（→0），**不改武器的伤害区间**——`Weapon` 上只有一组
            //    DamageMin/DamageMax，它本就与伤害类型无关（挥得多重是武器的属性，不是伤型的）。
            //
            // rng 消耗次数与旧实现**逐次相同**（每层仍是 1 次攻 roll + 1 次防 roll），
            // 只是第 2 层起 roll 的**区间**变了 ⇒ 0 层/1 层的结算路径**逐位不变**。
            double rolled = _rng.Range(weapon.DamageMin, weapon.DamageMax);
            double atk = i == 0 ? rolled : Math.Min(rolled, carriedDamage);
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

            // ⚠️ 以下三段判定里的 `atk` 一律是**取 min 之后的实际值**（不是原始 rolled）——
            // Half 的 `atk / 2.0` 也据此折半。
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
            // 下一层：重掷武器原始区间，再与 carriedDamage 取 min（见循环开头）。
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
