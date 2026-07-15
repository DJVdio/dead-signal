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
    /// <summary>
    /// 出血（部位 → **该部位那【唯一】一处出血**）。[T58 三级流血]
    ///
    /// <para>
    /// 演进史：HashSet（按部位去重，叠不起来）→ Dictionary&lt;string,int&gt;（裸计数，能叠但每处一模一样）
    /// → Dictionary&lt;string,List&lt;double&gt;&gt;（[T53] 伤口带速率乘数）
    /// → **Dictionary&lt;string, BleedWound&gt;（[T58] 每部位只有一处，带等级）**。
    /// </para>
    /// <para>
    /// 🔴 **为什么退回"每部位一处"**：用户拍板的三级流血里「**封顶大流血**」要求一个部位**只能有一个等级**
    /// （否则两处大流血就突破了封顶）。多刀砍同一处 ⇒ **即时合并**（<see cref="BleedModel.Merge"/>），
    /// 不是再挂一处。这同时解决了「一个部位攒一堆小伤口、每个都要单独做一台手术」——
    /// **三个小口合成一个大口 = 一台手术。**
    /// </para>
    /// </summary>
    private readonly Dictionary<string, BleedWound> _bleeding = new();
    private readonly HashSet<string> _fractured = new();
    private readonly HashSet<string> _treatedFractures = new();

    /// <param name="bloodMax">
    /// 储血上限。默认读 <see cref="BleedModel.DefaultBloodMax"/> —— **与 Sim 的 `DuelConfig` 同一个事实源**
    /// （[T53] 之前实机默认 100、Duel 用 70，两套数静默漂了很久）。
    /// </param>
    public Body(IEnumerable<BodyPart> parts, double bloodMax = BleedModel.DefaultBloodMax)
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

    /// <summary>
    /// 每处伤口每秒失血量；多处伤口叠加流速。战斗内实时失血由此驱动。
    /// 默认读 <see cref="BleedModel.DefaultBleedRatePerWound"/> —— **与 Sim 的 `DuelConfig` 同一个事实源**。
    /// <para>
    /// [T53] 查明：实机默认 0.55 与 `DuelConfig` 的 1.5 是**两套分裂的数**，「流血大幅加强」只加强了 Sim。
    /// 用户先拍"实机对齐到 Sim"，但**二次拍板否决了对齐**（原话「不对齐了」）——**两边一起回退到 100 / 0.55**
    /// （＝实机一直在跑的原默认值），并共读 <see cref="BleedModel.DefaultBleedRatePerWound"/> 这一个常量。
    /// 保留的是"两份事实源焊死"这个结构，回退的只是数值。详见 <see cref="BleedModel"/> 的口径长注。
    /// </para>
    /// </summary>
    public double BleedRatePerWound { get; set; } = BleedModel.DefaultBleedRatePerWound;

    /// <summary>
    /// **实体级**失血抗性倍率（1.0 = 常人）。丧尸填 <see cref="BleedModel.ZombieBleedRateMultiplier"/>（1/3，用户口径）。
    /// 挂在身体上而非武器上：这是"谁在流血"的属性，不是"谁在砍"的属性——精英丧尸/动物/其他敌人各自设定。
    /// </summary>
    public double BleedRateMultiplier { get; set; } = 1.0;

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

    /// <summary>正在出血的**部位**。[T58] 每部位恰好一处出血 ⇒ 这也就是"出血处数"。</summary>
    public IReadOnlyCollection<string> BleedingWounds => _bleeding.Keys;

    /// <summary>
    /// 出血处数 = **正在出血的部位数**（[T58] 每部位只有一处，合并制）。
    /// 供"身上还有没有没止住的口子"这类判定（如休养回血的闸门）使用。
    /// </summary>
    public int BleedingWoundCount => _bleeding.Count;

    /// <summary>该部位当前的出血**等级**（null = 该部位没有出血）。</summary>
    public BleedModel.BleedSeverity? BleedSeverityOn(string part)
        => _bleeding.TryGetValue(part, out BleedWound w) ? w.Severity : null;

    /// <summary>该部位那处出血的**流血速率乘数**（武器侧的轴，如锯齿剑刃 1.4）；无出血则 0。</summary>
    public double BleedRateMultiplierOn(string part)
        => _bleeding.TryGetValue(part, out BleedWound w) ? w.RateMultiplier : 0;

    /// <summary>该部位那处出血的**等级权重 × 速率乘数**（诊断/测试用；无出血则 0）。</summary>
    public double BleedRateOn(string part)
        => _bleeding.TryGetValue(part, out BleedWound w)
            ? BleedModel.SeverityRateOf(w.Severity) * w.RateMultiplier
            : 0;

    /// <summary>
    /// 登记一处出血（部位即使被切除，断口仍持续出血）。[T58 三级流血]
    ///
    /// <para>
    /// 🔴 **同一部位再挨一刀 = 【即时合并】，不是再挂一处**（用户规格：两小合中、两中合大、小+中合大、
    /// **封顶大流血**）。合并规则见 <see cref="BleedModel.Merge"/>。**每部位始终只有一处出血** ——
    /// 这既是"封顶"的唯一自洽读法，也直接兑现了用户那句「防止过多的伤口浪费手术时间」：
    /// 三个小口合成一个大口 ⇒ **一台手术，不是三台**。
    /// </para>
    /// <para>
    /// <b>合并时速率乘数取【较大者】</b>（这是我拍的实现细节，已写进 journal）：一道由普通刀伤和锯齿剑伤
    /// 并成的大口子，凶的那一半决定了它流得多快 —— 取平均会让"再补一刀普通刀"反而把伤口变得更温和，荒谬。
    /// </para>
    /// </summary>
    /// <param name="severity">这一次造成的出血等级（由 <see cref="BleedModel.SeverityOf"/> 按伤害/部位血量算出）。</param>
    /// <param name="rateMultiplier">
    /// 这一次的**流血速率乘数**（默认 1.0 = 普通伤口；锯齿剑刃 = <see cref="Weapon.BleedRateMultiplier"/> 1.4）。
    /// 负值按 0 处理（不允许"负流血"回血）。
    /// </param>
    public void RegisterBleed(string part, BleedModel.BleedSeverity severity, double rateMultiplier = 1.0)
    {
        double rate = Math.Max(0, rateMultiplier);
        if (_bleeding.TryGetValue(part, out BleedWound existing))
        {
            _bleeding[part] = new BleedWound(
                BleedModel.Merge(existing.Severity, severity),
                Math.Max(existing.RateMultiplier, rate));
            return;
        }

        _bleeding[part] = new BleedWound(severity, rate);
    }

    /// <summary>止血/治疗接口：清除某部位的持续出血（包扎一处 = 该部位所有伤口一起止住）。</summary>
    /// TODO(治疗): 由治疗系统调用。
    public void StopBleed(string part) => _bleeding.Remove(part);

    /// <summary>
    /// 休养回血（用户拍板 T53：「补——休养自然回血」）。把储血加回来，封顶 <see cref="BloodMax"/>。
    ///
    /// <para>
    /// 🔴 <b>为什么需要它</b>：此前**实机没有任何回血手段** —— <see cref="Blood"/> 只有 <see cref="LoseBlood"/>（只减）
    /// 与 <see cref="SetBloodMax"/>（回满）两条路径，而 <c>SetBloodMax</c> 在 Godot 层**一次都没被调用**
    /// （只有 Sim/Duel 的 init 在调）⇒ 幸存者的储血在整个战役里**单调递减、无恢复路径**，最终必然见底。
    /// 手术只"止住伤口"，**流掉的血不会回来**。
    /// </para>
    /// <para>
    /// <b>死人不回血</b>；已出血致死（<see cref="BledOut"/>）者不复活 —— 回血是休养，不是复活术。
    /// </para>
    /// </summary>
    /// <returns>实际回复的血量（可能因封顶而少于 <paramref name="amount"/>）。</returns>
    public double RecoverBlood(double amount)
    {
        if (amount <= 0 || IsDead || BledOut)
        {
            return 0;
        }

        double before = Blood;
        Blood = Math.Min(BloodMax, Blood + amount);
        return Blood - before;
    }

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

    /// <summary>
    /// 推进一段时间的持续失血（扣储血，不扣部位 HP）。返回本次失血量。
    ///
    /// <para>
    /// 失血 = Σ(每处伤口的**速率乘数** × 该部位的分级权重) × <see cref="BleedRatePerWound"/>
    /// × <see cref="BleedRateMultiplier"/>（受害者体质）× dt。
    /// **三根轴正交**：伤口乘数＝「谁砍的」（<see cref="Weapon.BleedRateMultiplier"/>，锯齿剑刃 1.4）、
    /// 分级权重＝「砍在哪」（部位）、体质倍率＝「谁在流」（丧尸 1/3）。分级见 <see cref="BleedModel"/>：
    /// 大部位深伤口全速且**能放干致死**；手/脚、指/趾等小伤口流速低，且**只能把血抽到
    /// <see cref="BleedModel.NonLethalBloodFloorRatio"/> 为止** —— 它们让人虚弱，但永远不是最后一根稻草。
    /// 断口（部位已被切除）按致命算（微小部位除外）。
    /// </para>
    /// </summary>
    public double TickBleed(double dt)
    {
        if (dt <= 0 || _bleeding.Count == 0 || BledOut)
        {
            return 0;
        }

        double lethalRate = 0, nonLethalRate = 0;
        foreach (var (part, wound) in _bleeding)
        {
            var tier = BleedModel.WoundTierOf(this, part);
            // [T58] 三根轴正交相乘：
            //   等级权重（小 0.3 / 中 1.0 / 大 3.0）＝「口子多大」
            // × 速率乘数（锯齿剑刃 1.4）      ＝「谁砍的」
            // × 部位分级权重（致命/轻微/微小）＝「砍在哪」
            double rate = BleedModel.SeverityRateOf(wound.Severity)
                          * wound.RateMultiplier
                          * BleedModel.RateWeightOf(tier);
            if (tier == BleedTier.Lethal)
            {
                lethalRate += rate;
            }
            else
            {
                nonLethalRate += rate;
            }
        }

        // 实体级失血抗性（丧尸 1/3）：只缩放流速，不改"小伤口永不致死"的下限语义。
        double perSecond = BleedRatePerWound * Math.Max(0, BleedRateMultiplier);

        double before = Blood;
        LoseBlood(lethalRate * perSecond * dt); // 致命伤口：无下限，可放干

        // 非致命伤口：只能抽到下限，抽不动就是抽不动（已被大出血压到下限以下时贡献 0）。
        double floor = BloodBleedFloor;
        double headroom = Math.Max(0, Blood - floor);
        if (headroom > 0)
        {
            LoseBlood(Math.Min(nonLethalRate * perSecond * dt, headroom));
        }

        return before - Blood;
    }

    /// <summary>非致命伤口的失血下限（绝对储血量）。</summary>
    private double BloodBleedFloor => BleedModel.NonLethalBloodFloorRatio * BloodMax;

    // ---- 骨折持久态（供角色面板"健康页签"查询；第一版只进不出、不做治疗/时间恢复）----

    public IReadOnlyCollection<string> FracturedParts => _fractured;

    /// <summary>标记某部位已骨折（由效果结算在触发骨折时调用）。持久保留。</summary>
    public void MarkFractured(string part) => _fractured.Add(part);

    /// <summary>消骨折/痊愈接口：清除某部位的骨折标记（含已治疗标记）。幂等：未骨折/部位名不存在均无副作用。</summary>
    /// TODO(治疗): 由骨折康复完成（痊愈）时调用。
    public void HealFracture(string part)
    {
        _fractured.Remove(part);
        _treatedFractures.Remove(part);
    }

    /// <summary>
    /// 标记某部位骨折**已治疗**（手术成功、进入愈合中）：能力惩罚由未治疗档减半（−30%→−15%，用户口径）。
    /// 由 Godot 医疗层在骨折手术成功时调用（愈合完成再调 <see cref="HealFracture"/> 归零）。幂等；仅对已骨折部位有意义。
    /// </summary>
    public void MarkFractureTreated(string part)
    {
        if (_fractured.Contains(part))
        {
            _treatedFractures.Add(part);
        }
    }

    /// <summary>某部位骨折是否已治疗（愈合中，惩罚减半）。</summary>
    public bool IsFractureTreated(string partName) => _treatedFractures.Contains(partName);

    /// <summary>某部位当前是否处于骨折状态（持久，供健康页签展示）。</summary>
    public bool IsFractured(string partName) => _fractured.Contains(partName);

    /// <summary>
    /// 手部骨折对操作能力的乘算系数（用户口径：单处手骨折 −30% 操作/含攻速；已治疗减半为 −15%）。
    /// 每处**尚存的手部**（Region==Hand）骨折乘一次系数（未治疗 <paramref name="untreatedMult"/> / 已治疗 <paramref name="treatedMult"/>），
    /// 多处乘算叠加，结果锁下限 <paramref name="floor"/>。不含手指/手臂骨折（仅 Region==Hand 计入，其余骨折为持久状态标记、无能力效果，待确认）。
    /// 与残疾净惩罚（断手/断指）相互独立叠乘，不改那套加性数学。
    /// </summary>
    public double HandFractureOperationFactor(double untreatedMult, double treatedMult, double floor)
        => FractureCapabilityFactor(untreatedMult, treatedMult, floor, BodyRegion.Hand);

    /// <summary>
    /// 腿/脚骨折对移动能力的乘算系数（用户口径：单处腿骨折 −30% 移速；已治疗减半为 −15%）。
    /// 每处尚存的腿（Region==Leg）或脚（Region==Foot）骨折乘一次系数（未治疗/已治疗），
    /// 多处乘算叠加，锁下限 <paramref name="floor"/>（脚归入腿部移动，待确认）。
    /// </summary>
    public double LegFractureMobilityFactor(double untreatedMult, double treatedMult, double floor)
        => FractureCapabilityFactor(untreatedMult, treatedMult, floor, BodyRegion.Leg, BodyRegion.Foot);

    private double FractureCapabilityFactor(double untreatedMult, double treatedMult, double floor, params BodyRegion[] regions)
    {
        double factor = 1.0;
        foreach (var partName in _fractured)
        {
            if (IsGone(partName) || !_parts.TryGetValue(partName, out var bp))
            {
                continue; // 已切除/损毁的部位骨折不再计能力（部位已不在）。
            }

            if (Array.IndexOf(regions, bp.Region) >= 0)
            {
                // 已治疗（愈合中）惩罚减半；未治疗满惩罚。
                factor *= _treatedFractures.Contains(partName) ? treatedMult : untreatedMult;
            }
        }

        return Math.Max(floor, factor);
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
    /// 切除部位：连带移除其所有后代（切手臂→连带手），触发装备掉落回调。
    /// 若被移除集合含致死部位（头/胸/腹）→ 角色死亡（斩首/开膛，不生成断肢实体）。
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

    // ---- 存档：状态快照与恢复（见 BodySnapshot） ----

    /// <summary>
    /// 导出全部**可变**状态（部位模板不含在内——那是代码里的数据表，由工厂重建）。
    /// </summary>
    public BodySnapshot Capture() => new()
    {
        Hp = new Dictionary<string, double>(_hp),
        MaxHp = new Dictionary<string, double>(_maxHp),
        Severed = _severed.ToList(),
        Destroyed = _destroyed.ToList(),
        Disabled = _disabled.ToList(),
        // [T58] 每部位只有一处出血 ⇒ 三条**平行**列表，各一条：部位名 / 速率乘数 / 等级(1小 2中 3大)。
        // 平行而非嵌套：老存档的 Bleeding 格式**逐字不变**（那时同部位会重复出现），仍能读（见 Restore）。
        Bleeding = _bleeding.Keys.ToList(),
        BleedingRates = _bleeding.Keys.Select(k => _bleeding[k].RateMultiplier).ToList(),
        BleedingLevels = _bleeding.Keys.Select(k => (int)_bleeding[k].Severity).ToList(),
        Fractured = _fractured.ToList(),
        TreatedFractures = _treatedFractures.ToList(),
        Blood = Blood,
        BloodMax = BloodMax,
        BleedRatePerWound = BleedRatePerWound,
        BleedRateMultiplier = BleedRateMultiplier,
        BledOut = BledOut,
        IsDead = IsDead,
        Prosthetics = _prosthetics.Select(p => new ProstheticSnapshot
        {
            Name = p.Name,
            Grade = p.Grade,
            ReplacesRegion = p.ReplacesRegion,
            RestoreRatio = p.RestoreRatio,
        }).ToList(),
    };

    /// <summary>
    /// 把快照覆盖回本体（读档）。调用前本体应是刚由工厂按模板造出的**全新** Body——
    /// 本方法只覆盖可变状态，不重建部位树。
    /// <para>
    /// 快照里没有的部位（模板演化新增的）保留模板默认值；快照里有而模板没有的（部位被删了）静默丢弃——
    /// 二者都不该在正常读档里发生（版本闸门先拦），此处只是不让它炸。
    /// </para>
    /// </summary>
    public void Restore(BodySnapshot s)
    {
        foreach (var kv in s.Hp)
        {
            if (_hp.ContainsKey(kv.Key)) _hp[kv.Key] = kv.Value;
        }
        foreach (var kv in s.MaxHp)
        {
            if (_maxHp.ContainsKey(kv.Key)) _maxHp[kv.Key] = kv.Value;
        }

        RefillSet(_severed, s.Severed);
        RefillSet(_destroyed, s.Destroyed);
        RefillSet(_disabled, s.Disabled);
        _bleeding.Clear();
        for (int i = 0; i < s.Bleeding.Count; i++)
        {
            // 速率乘数按下标对齐取；**老存档没有 BleedingRates（或长度不够）⇒ 回落 1.0（普通伤口）**，
            // 不是 0 —— 默认 0 会让老档里所有伤口当场变成"不流血"，等于静默治好了他们。
            double rate = i < s.BleedingRates.Count ? s.BleedingRates[i] : 1.0;

            // [T58] 等级按下标对齐取；**老存档没有 BleedingLevels ⇒ 回落成"小流血"**。
            // 老档里同一部位会**重复出现 N 次**（那时一处伤口一条）⇒ RegisterBleed 会把它们**逐个合并**：
            // 1 次 ⇒ 小、2 次 ⇒ 中、3 次（旧的封顶）⇒ 大。旧封顶速率 3×1.0 = 3.0 ＝ 新大流血速率 3.0，
            // **一分不差**。新档写的是不重复的部位名 + 真实等级，走的是同一条路径（合并对单条是恒等）。
            var level = i < s.BleedingLevels.Count && s.BleedingLevels[i] > 0
                ? (BleedModel.BleedSeverity)Math.Clamp(s.BleedingLevels[i], 1, 3)
                : BleedModel.BleedSeverity.Small;

            RegisterBleed(s.Bleeding[i], level, rate);
        }

        RefillSet(_fractured, s.Fractured);
        RefillSet(_treatedFractures, s.TreatedFractures);

        BloodMax = s.BloodMax;
        Blood = s.Blood;
        BleedRatePerWound = s.BleedRatePerWound;
        // 旧存档没有这个字段 → 反序列化出 0 → 回落成 1.0（常人）。存档 schema 因此无需版本闸门：
        // 被持久化的身体只有幸存者与狗，本来就都是 1.0；丧尸不进存档。
        BleedRateMultiplier = s.BleedRateMultiplier > 0 ? s.BleedRateMultiplier : 1.0;
        BledOut = s.BledOut;
        IsDead = s.IsDead;

        _prosthetics.Clear();
        foreach (var p in s.Prosthetics)
        {
            _prosthetics.Add(new Prosthetic
            {
                Name = p.Name,
                Grade = p.Grade,
                ReplacesRegion = p.ReplacesRegion,
                RestoreRatio = p.RestoreRatio,
            });
        }

        // 惩罚是派生量（切除部位 + 假肢 → 操作/移动净惩罚），恢复完重算一次即可，不必存。
        RecalculatePenalties();
    }

    private static void RefillSet(HashSet<string> target, List<string> source)
    {
        target.Clear();
        foreach (string s in source)
        {
            target.Add(s);
        }
    }
}
