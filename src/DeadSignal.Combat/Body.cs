namespace DeadSignal.Combat;

/// <summary>一次 HP 变化的记录。</summary>
public readonly record struct HpChange(
    string PartName,
    double HpBefore,
    double HpAfter,
    bool ReachedZeroThisHit);

/// <summary>切除结果：被移除的部位（含后代）及是否致死。</summary>
public sealed class SeverResult
{
    public IReadOnlyList<string> RemovedParts { get; init; } = Array.Empty<string>();
    public bool CausedDeath { get; init; }
}

/// <summary>
/// MaxHp 磨损结果：0HP 部位受实质钝伤→上限永久降低；上限归 0 则部位永久损毁（连带后代）。
/// </summary>
public readonly record struct MaxHpErosion(
    string PartName,
    double MaxHpBefore,
    double MaxHpAfter,
    bool Destroyed,
    IReadOnlyList<string> DestroyedParts,
    bool CausedDeath);

/// <summary>失血分级（按储血量余量百分比）。阈值拟定待调。</summary>
public enum BloodLossTier
{
    /// <summary>≥75%：无失血影响。</summary>
    None,

    /// <summary>&lt;75%：轻度出血——攻速/移速小幅降低。</summary>
    Mild,

    /// <summary>&lt;50%：中度出血——debuff 加重。</summary>
    Moderate,

    /// <summary>&lt;25%：重度出血——昏迷（丧失行动）。</summary>
    Severe,

    /// <summary>=0：出血致死。</summary>
    Dead,
}

/// <summary>
/// 角色运行时战斗状态：每部位独立 HP（含可磨损的 MaxHp）+ 树形连带 + 致死/致残/致盲判定
/// + 储血量失血系统（流血扣储血、不扣部位 HP；分级 debuff/昏迷/出血致死）。
/// 部位定义来自 <see cref="HumanBody"/>（数据驱动）；本类只管状态演变。
/// </summary>
public sealed class Body
{
    private readonly Dictionary<string, BodyPart> _parts;
    private readonly Dictionary<string, double> _hp;
    private readonly Dictionary<string, double> _maxHp;
    private readonly Dictionary<string, List<string>> _children;
    private readonly HashSet<string> _severed = new();
    private readonly HashSet<string> _destroyed = new();
    private readonly HashSet<string> _disabled = new();
    private readonly HashSet<string> _bleeding = new();
    private readonly HashSet<string> _fractured = new();

    public Body(IEnumerable<BodyPart> parts, double bloodMax = 100)
    {
        _parts = parts.ToDictionary(p => p.Name);
        _hp = _parts.Values.ToDictionary(p => p.Name, p => p.MaxHp);
        _maxHp = _parts.Values.ToDictionary(p => p.Name, p => p.MaxHp);
        _children = _parts.Keys.ToDictionary(n => n, _ => new List<string>());
        foreach (var p in _parts.Values)
        {
            if (p.Parent is string parent && _children.ContainsKey(parent))
            {
                _children[parent].Add(p.Name);
            }
        }

        BloodMax = bloodMax;
        Blood = bloodMax;
    }

    // ---- 储血量失血系统（阈值/流速拟定待调）----

    /// <summary>储血量上限（拟定待调，可与体型/体力挂钩）。</summary>
    public double BloodMax { get; private set; }

    /// <summary>当前储血量。</summary>
    public double Blood { get; private set; }

    /// <summary>每处伤口每秒失血量（拟定待调）；多处伤口叠加流速。</summary>
    public double BleedRatePerWound { get; set; } = 0.8;

    /// <summary>设定储血量上限并回满（拟定期用于按体型/难度调参）。</summary>
    public void SetBloodMax(double max)
    {
        BloodMax = Math.Max(0, max);
        Blood = BloodMax;
        BledOut = false;
    }

    /// <summary>是否已出血致死。</summary>
    public bool BledOut { get; private set; }

    public double BloodRatio => BloodMax > 0 ? Blood / BloodMax : 0;

    public BloodLossTier BloodTier =>
        Blood <= 0 ? BloodLossTier.Dead
        : BloodRatio < 0.25 ? BloodLossTier.Severe
        : BloodRatio < 0.50 ? BloodLossTier.Moderate
        : BloodRatio < 0.75 ? BloodLossTier.Mild
        : BloodLossTier.None;

    /// <summary>重度出血昏迷（未死但丧失行动）。</summary>
    public bool IsUnconscious => !IsDead && BloodTier == BloodLossTier.Severe;

    public IReadOnlyCollection<string> BleedingWounds => _bleeding;

    public int BleedingWoundCount => _bleeding.Count;

    /// <summary>登记一个出血伤口（部位即使被移除，断口仍持续出血）。</summary>
    public void RegisterBleed(string part) => _bleeding.Add(part);

    /// <summary>止血/治疗接口：清除某伤口的持续出血。</summary>
    /// TODO(治疗): 由治疗系统调用。
    public void StopBleed(string part) => _bleeding.Remove(part);

    /// <summary>直接扣储血量；归 0 → 出血致死。</summary>
    public void LoseBlood(double amount)
    {
        if (amount <= 0 || BledOut)
        {
            return;
        }

        Blood = Math.Max(0, Blood - amount);
        if (Blood <= 0)
        {
            BledOut = true;
            IsDead = true;
        }
    }

    /// <summary>推进一段时间的持续失血：失血 = 伤口数 × 每伤口流速 × dt（扣储血，不扣部位 HP）。返回本次失血量。</summary>
    public double TickBleed(double dt)
    {
        if (dt <= 0 || _bleeding.Count == 0 || BledOut)
        {
            return 0;
        }

        double loss = _bleeding.Count * BleedRatePerWound * dt;
        LoseBlood(loss);
        return loss;
    }

    // ---- 骨折持久态（供角色面板"健康页签"查询；第一版只进不出、不做治疗/时间恢复）----

    public IReadOnlyCollection<string> FracturedParts => _fractured;

    /// <summary>标记某部位已骨折（由效果结算在触发骨折时调用）。持久保留。</summary>
    public void MarkFractured(string part) => _fractured.Add(part);

    /// <summary>消骨折/治疗接口：清除某部位的骨折标记（与 StopBleed 对称）。幂等：未骨折/部位名不存在均无副作用。</summary>
    /// TODO(治疗): 由骨折手术治愈时调用。
    public void HealFracture(string part) => _fractured.Remove(part);

    /// <summary>某部位当前是否处于骨折状态（持久，供健康页签展示）。</summary>
    public bool IsFractured(string partName) => _fractured.Contains(partName);

    public IReadOnlyDictionary<string, BodyPart> Parts => _parts;

    public bool IsDead { get; private set; }

    /// <summary>装备掉落接口：切除时以被移除部位名列表回调（真正的掉落挂载由上层消费）。</summary>
    /// TODO(装备): 上层订阅此回调，把这些部位挂载的装备实体掉落到地面。
    public Action<IReadOnlyList<string>>? EquipmentDropped;

    public double HpOf(string part) => _hp.TryGetValue(part, out double h) ? h : 0;

    /// <summary>部位当前最大 HP（可被磨损降低）。</summary>
    public double MaxHpOf(string part) => _maxHp.TryGetValue(part, out double m) ? m : 0;

    public bool IsSevered(string part) => _severed.Contains(part);

    /// <summary>部位是否被永久损毁（MaxHp 磨损归 0，碾碎/砸烂）。</summary>
    public bool IsDestroyed(string part) => _destroyed.Contains(part);

    /// <summary>部位是否已不存在（被切除或损毁）。</summary>
    public bool IsGone(string part) => _severed.Contains(part) || _destroyed.Contains(part);

    /// <summary>该部位是否失能（致残/致盲，或已切除/损毁）。</summary>
    public bool IsDisabled(string part) => _disabled.Contains(part) || IsGone(part);

    /// <summary>双眼皆失能（失明或已不存在）即全盲。</summary>
    public bool IsFullyBlind =>
        _parts.Values.Where(p => p.Category == BodyPartCategory.Eye).All(p => IsDisabled(p.Name))
        && _parts.Values.Any(p => p.Category == BodyPartCategory.Eye);

    /// <summary>
    /// 对部位施加伤害（扣 HP，下限 0）。归零触发对应后果：致死部位→死亡、四肢→致残、眼→该眼失明。
    /// 已不存在（切除/损毁）的部位不再受伤。
    /// </summary>
    public HpChange ApplyDamage(string partName, double amount)
    {
        if (!_parts.TryGetValue(partName, out var part) || IsGone(partName) || amount <= 0)
        {
            double cur = HpOf(partName);
            return new HpChange(partName, cur, cur, false);
        }

        double before = _hp[partName];
        double after = Math.Max(0, before - amount);
        _hp[partName] = after;
        bool reachedZero = before > 0 && after <= 0;

        if (after <= 0)
        {
            switch (part.Category)
            {
                case BodyPartCategory.Vital:
                    IsDead = true;
                    break;
                case BodyPartCategory.Limb:
                case BodyPartCategory.Eye:
                    _disabled.Add(partName);
                    break;
                case BodyPartCategory.Minor:
                    break;
            }
        }

        return new HpChange(partName, before, after, reachedZero);
    }

    /// <summary>
    /// 永久磨损部位 MaxHp（0HP 部位受实质钝伤时调用）。上限降至 0 → 部位永久损毁、连带后代一并损毁；
    /// 致死部位损毁 = 死亡（锤烂头颅/胸腔）。损毁不掉落装备（碾碎的手仍套在手套里，随部位报废）。
    /// </summary>
    public MaxHpErosion ErodeMaxHp(string partName, double amount)
    {
        if (!_parts.ContainsKey(partName) || IsGone(partName) || amount <= 0)
        {
            double cur = MaxHpOf(partName);
            return new MaxHpErosion(partName, cur, cur, false, Array.Empty<string>(), false);
        }

        double before = _maxHp[partName];
        double after = Math.Max(0, before - amount);
        _maxHp[partName] = after;
        if (_hp[partName] > after)
        {
            _hp[partName] = after; // 当前 HP 不超过新上限
        }

        if (after <= 0)
        {
            var (removed, death) = RemoveSubtree(partName, markDestroyed: true);
            return new MaxHpErosion(partName, before, after, true, removed, death);
        }

        return new MaxHpErosion(partName, before, after, false, Array.Empty<string>(), false);
    }

    /// <summary>
    /// 切除部位：连带移除其所有后代（切上臂→连带手），触发装备掉落回调。
    /// 若被移除集合含致死部位（头/颈/躯干）→ 角色死亡（斩首/开膛，不生成断肢实体）。
    /// </summary>
    public SeverResult Sever(string partName)
    {
        if (!_parts.ContainsKey(partName))
        {
            return new SeverResult();
        }

        var (removed, death) = RemoveSubtree(partName, markDestroyed: false);
        EquipmentDropped?.Invoke(removed);
        return new SeverResult { RemovedParts = removed, CausedDeath = death };
    }

    /// <summary>移除部位子树并标记（切除 or 损毁），返回被移除部位与是否致死。装备掉落由调用方决定。</summary>
    private (List<string> RemovedParts, bool CausedDeath) RemoveSubtree(string partName, bool markDestroyed)
    {
        var removed = new List<string>();
        CollectSubtree(partName, removed);

        bool death = false;
        foreach (var r in removed)
        {
            if (markDestroyed)
            {
                _destroyed.Add(r);
            }
            else
            {
                _severed.Add(r);
            }

            _disabled.Add(r);
            _hp[r] = 0;
            _maxHp[r] = 0;
            if (_parts[r].Category == BodyPartCategory.Vital)
            {
                death = true;
            }
        }

        if (death)
        {
            IsDead = true;
        }

        return (removed, death);
    }

    private void CollectSubtree(string part, List<string> acc)
    {
        if (IsGone(part))
        {
            return;
        }

        acc.Add(part);
        foreach (var child in _children[part])
        {
            CollectSubtree(child, acc);
        }
    }

    // ---- 残疾能力惩罚与假肢（切除/损毁部位 → 操作/移动净惩罚；假肢部分恢复）----

    private const double SingleLimbPenalty = 0.5; // 单肢（一手/一腿）= 全局能力的 50%。

    private readonly List<Prosthetic> _prosthetics = new();

    /// <summary>操作/移动能力净惩罚（0.0~1.0）。由 <see cref="RecalculatePenalties"/> 依切除部位 + 假肢重算。</summary>
    public DisabilityModifiers DisabilityModifiers { get; } = new();

    /// <summary>已装备的假肢（取代被切除肢体，恢复部分能力）。</summary>
    public IReadOnlyList<Prosthetic> Prosthetics => _prosthetics;

    /// <summary>
    /// 装备假肢：假肢取代一个已失去的对应肢体（手→操作 / 腿→移动），部分恢复该肢惩罚。装后重算净惩罚。
    /// 假肢无 HP、不可再被切除（被取代部位已 IsGone，Sever/ApplyDamage 天然对其 no-op）。
    /// </summary>
    public void AttachProsthetic(Prosthetic prosthetic)
    {
        _prosthetics.Add(prosthetic);
        RecalculatePenalties();
    }

    /// <summary>
    /// 重算残疾净惩罚。口径：单手/单腿失去各 -50%（两手/两腿累加至 -100%）；未失去的手按其失去手指累加
    /// -7%/指（该手上限 -50%，断手 -50% 覆盖手指累加）；手部失去时手指一并消失、不计额外。
    /// 假肢按等级恢复"单肢能力 × RestoreRatio"（即全局 -50% × RestoreRatio），逐个抵扣一只失去的对应肢体。
    /// </summary>
    private const double FingerPenalty = 0.07; // 每根手指 -7%（该手累加）。
    private const double ToePenalty = 0.02;     // 每根脚趾 -2%（该脚累加）。

    public void RecalculatePenalties()
    {
        // 操作：以手为单位（假肢=手），未失去的手按失去手指累加 -7%/指。
        DisabilityModifiers.OperationPenalty =
            LimbPenalty(BodyRegion.Hand, BodyRegion.Hand, BodyRegion.Finger, FingerPenalty);
        // 移动：以脚为单位（脚趾挂脚下，切/毁腿连带脚 gone → 该侧 0.5），假肢作用于整腿（ReplacesRegion=Leg）。
        // 未失去的脚按失去脚趾累加 -2%/趾。
        DisabilityModifiers.MobilityPenalty =
            LimbPenalty(BodyRegion.Foot, BodyRegion.Leg, BodyRegion.Toe, ToePenalty);
    }

    /// <summary>
    /// 计算某能力净惩罚。以 <paramref name="unitRegion"/> 部位为单位（手/脚）：该单位失去（切除/损毁）→ -50%；
    /// 未失去时按其失去子部位 <paramref name="digitRegion"/> 累加 <paramref name="digitPenalty"/>/个（该单位上限 -50%，
    /// 单位失去覆盖子部位累加）。假肢按 <paramref name="restoreRegion"/> 匹配（手→Hand / 脚→Leg），
    /// 恢复 = 单肢能力(50%) × RestoreRatio，逐个抵扣一个失去的单位。总上限锁 100%。
    /// </summary>
    private double LimbPenalty(BodyRegion unitRegion, BodyRegion restoreRegion, BodyRegion digitRegion, double digitPenalty)
    {
        var units = _parts.Values.Where(p => p.Region == unitRegion).ToList();
        var restores = _prosthetics
            .Where(p => p.ReplacesRegion == restoreRegion)
            .Select(p => p.RestoreRatio)
            .OrderByDescending(r => r) // 优先用高等级假肢抵扣，抵扣结果与顺序无关（净惩罚下限锁 0）
            .ToList();
        int restoreIdx = 0;

        double total = 0;
        foreach (var unit in units)
        {
            double penalty;
            if (IsGone(unit.Name))
            {
                penalty = SingleLimbPenalty;
                if (restoreIdx < restores.Count)
                {
                    // 假肢恢复 = 单肢能力 × RestoreRatio；净惩罚 = -50% + 恢复，下限锁 0。
                    penalty = Math.Max(0, SingleLimbPenalty - restores[restoreIdx] * SingleLimbPenalty);
                    restoreIdx++;
                }
            }
            else
            {
                int digitsGone = _parts.Values.Count(p =>
                    p.Region == digitRegion && p.Parent == unit.Name && IsGone(p.Name));
                penalty = Math.Min(SingleLimbPenalty, digitsGone * digitPenalty); // 单位失去 -50% 覆盖子部位累加
            }

            total += penalty;
        }

        return Math.Min(1.0, total); // 两侧全失 = -100%，上限锁 100%。
    }
}
