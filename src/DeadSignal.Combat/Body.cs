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
}
