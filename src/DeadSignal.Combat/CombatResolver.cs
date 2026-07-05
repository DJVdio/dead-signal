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
    /// 结算一次命中。<paramref name="layers"/> 须按从外到内顺序排列
    /// （可用 <see cref="OrderOuterToInner"/> 归一）。
    /// </summary>
    public CombatResult Resolve(Weapon weapon, IReadOnlyList<ArmorLayer> layers, BodyPart part)
    {
        var log = new List<LayerResolution>(layers.Count);

        DamageType currentType = weapon.DamageType;
        double currentPen = Math.Clamp(weapon.Penetration, 0, 1);

        // 第一层攻方在武器原始区间 roll；此后每层在 [0, 上一层结算后的伤害值] roll。
        double atkMin = weapon.DamageMin;
        double atkMax = weapon.DamageMax;

        double carriedDamage = 0;
        bool terminated = false;
        int penetrated = 0;

        for (int i = 0; i < layers.Count; i++)
        {
            ArmorLayer layer = layers[i];

            double atk = _rng.Range(atkMin, atkMax);
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
        int finalDamage;

        if (terminated)
        {
            rawDamage = 0;
            finalDamage = 0;
        }
        else if (layers.Count == 0)
        {
            // 无甲直击：武器伤害直接作用到部位。
            rawDamage = _rng.Range(weapon.DamageMin, weapon.DamageMax);
            finalDamage = CeilMin1(rawDamage);
        }
        else
        {
            rawDamage = carriedDamage;
            finalDamage = CeilMin1(rawDamage);
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
        };
    }

    /// <summary>向上取整、最低 1 伤。</summary>
    private static int CeilMin1(double raw) => Math.Max(1, (int)Math.Ceiling(raw));

    /// <summary>把护甲层按 <see cref="ArmorSlot"/> 从外（Plate）到内（Skin）排序。</summary>
    public static IReadOnlyList<ArmorLayer> OrderOuterToInner(IEnumerable<ArmorLayer> layers) =>
        layers.OrderBy(l => (int)l.Slot).ToList();
}
