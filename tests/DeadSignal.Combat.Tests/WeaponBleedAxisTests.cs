using DeadSignal.Combat;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【T53】武器影响流血这条轴 —— 让「锯齿剑刃（流血 +40%）」与「钉子强化（25% 几率小流血）」成立。
///
/// <para>
/// 改造前：伤口只是**计数**，失血 = Σ(伤口数 × 分级权重) × BleedRatePerWound × 受害者体质 ——
/// <c>Weapon</c> 类**一个流血字段都没有**，武器完全不参与流血。
/// 改造后：伤口变成**带属性的对象**（每处伤口自带一个流血速率乘数），由造成它的武器写入。
/// </para>
///
/// <para>
/// 🔴 <b>零漂移是硬要求</b>：新字段中性默认（<c>BleedRateMultiplier = 1.0</c>、<c>BleedOnHitChance = 0</c>），
/// 且 <c>BleedOnHitChance == 0</c> 时**一次随机都不消耗** ⇒ 既有武器的 Sim 结算路径逐字节不变。
/// 本文件下方 <see cref="Zero_drift"/> 区就是钉这条的护栏。
/// </para>
/// </summary>
public class WeaponBleedAxisTests
{
    private static Weapon Sharp(double bleedMult = 1.0, double bleedOnHit = 0.0) => new()
    {
        Name = "测试锐器",
        DamageMin = 10,
        DamageMax = 10,
        DamageType = DamageType.Sharp,
        AttackInterval = 1.0,
        BleedRateMultiplier = bleedMult,
        BleedOnHitChance = bleedOnHit,
    };

    private static Weapon Blunt(double bleedOnHit = 0.0, double bleedMult = 1.0) => new()
    {
        Name = "测试钝器",
        DamageMin = 10,
        DamageMax = 10,
        DamageType = DamageType.Blunt,
        AttackInterval = 1.0,
        BleedOnHitChance = bleedOnHit,
        BleedRateMultiplier = bleedMult,
    };

    // ---------------- 中性默认（零回归的地基）----------------

    [Fact]
    public void Weapon_bleed_fields_default_to_neutral()
    {
        var w = new Weapon { Name = "裸武器", AttackInterval = 1 };
        Assert.Equal(1.0, w.BleedRateMultiplier);   // 1.0 = 不改变流血
        Assert.Equal(0.0, w.BleedOnHitChance);      // 0 = 不额外造成流血，且不消耗随机
    }

    [Fact]
    public void Existing_arsenal_weapons_are_all_neutral_on_the_bleed_axis()
    {
        // 既有武器一把都不该带流血属性（否则 Sim 基线会漂）。只有改装件才写这两个字段。
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            Assert.Equal(1.0, w.BleedRateMultiplier);
            Assert.Equal(0.0, w.BleedOnHitChance);
        }
    }

    // ---------------- 伤口从「计数」变成「带属性」 ----------------

    [Fact]
    public void Wound_carries_its_own_bleed_rate_multiplier()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;

        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium, 1.4); // 锯齿剑划的：流得更快
        body.TickBleed(10);

        // 致命部位权重 1.0 ⇒ 失血 = 1.4（乘数） × 1.0（权重） × 1.0（速率） × 10s = 14
        Assert.Equal(14.0, body.BloodMax - body.Blood, 6);
    }

    [Fact]
    public void Default_registration_still_means_rate_one()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;

        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium); // 不带乘数 = 老调用点
        body.TickBleed(10);

        Assert.Equal(10.0, body.BloodMax - body.Blood, 6);
    }

    [Fact]
    /// <summary>
    /// [T58] 同一部位再挨一刀**不再叠加成两处伤口**，而是**合并成一处更高级的**：
    /// 中 + 中 ⇒ **大**（速率 3.0）。速率乘数取**较大者**（锯齿剑刃 1.4 —— 并出来的那道大口子里，
    /// 凶的那一半决定它流得多快）。
    /// </summary>
    public void Wounds_on_the_same_part_merge_and_keep_the_nastier_multiplier()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;

        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium, 1.0);
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium, 1.4);
        body.TickBleed(10);

        Assert.Equal(1, body.BleedingWoundCount);                                   // **一处**，不是两处
        Assert.Equal(BleedModel.BleedSeverity.Large, body.BleedSeverityOn(HumanBody.Chest));
        Assert.Equal(1.4, body.BleedRateMultiplierOn(HumanBody.Chest), 9);          // 取较大者
        Assert.Equal(42.0, body.BloodMax - body.Blood, 6);                          // 大(3.0) × 1.4 × 10
    }

    [Fact]
    public void Wound_rate_still_respects_part_tier_weight()
    {
        // 乘数不绕过分级：手 = 轻微（权重 0.5）⇒ 1.4 的锯齿伤口在手上只有 1.4 × 0.5 = 0.7
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;

        body.RegisterBleed(HumanBody.LeftHand, BleedModel.BleedSeverity.Medium, 1.4);
        body.TickBleed(10);

        Assert.Equal(7.0, body.BloodMax - body.Blood, 6);
    }

    [Fact]
    public void Non_lethal_floor_still_holds_for_a_fast_bleeding_small_wound()
    {
        // 「小伤口永不致死」这条铁律不能被武器乘数击穿：手上的伤口再快也只能抽到 50% 下限。
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;

        body.RegisterBleed(HumanBody.LeftHand, BleedModel.BleedSeverity.Medium, 99.0); // 荒谬的高乘数
        body.TickBleed(100_000);

        Assert.False(body.IsDead);
        Assert.Equal(BleedModel.NonLethalBloodFloorRatio, body.BloodRatio, 6);
    }

    /// <summary>
    /// [T58] 旧的「单部位 3 处伤口封顶」→ **合并 + 封顶大流血**：同一部位砍 10 刀仍只有**一处**出血，
    /// 等级封顶在大流血。速率乘数（锯齿剑刃 1.4）在合并中**取较大者**保留。
    /// </summary>
    [Fact]
    public void Same_part_merges_to_one_wound_capped_at_large()
    {
        var body = HumanBody.NewBody();
        for (int i = 0; i < 10; i++)
        {
            body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium, 1.4);
        }

        Assert.Equal(1, body.BleedingWoundCount);
        Assert.Equal(BleedModel.BleedSeverity.Large, body.BleedSeverityOn(HumanBody.Chest));
        Assert.Equal(1.4, body.BleedRateMultiplierOn(HumanBody.Chest), 9); // 锯齿剑刃的乘数活下来了
    }

    // ---------------- 锯齿剑刃：武器把乘数写进它造成的伤口 ----------------

    /// <summary>
    /// 🔴 <b>[T58] 本条的旧意图已被三级流血取代，这里钉的是新意图。</b>
    ///
    /// <para><b>旧意图</b>（<c>impl-bleed</c> 那单，第一版流血轴）：伤口是"无差别的一处"，
    /// 它的流血速率<b>就等于</b>武器的流血乘子 ⇒ 锯齿剑刃砍出的伤口速率 = <b>1.4</b>。</para>
    ///
    /// <para><b>新意图</b>（[T58] 三级流血）：伤口<b>有了等级</b>（小 0.3 / 中 1.0 / 大 3.0），
    /// 武器的乘子<b>作用在等级速率之上</b> ⇒ <b>实际速率 = 等级速率 × 武器乘子</b>。
    /// 两根轴正交：<b>等级＝口子多大</b>（伤害占部位血量的比例决定）、<b>乘子＝谁砍的</b>（锯齿剑刃 1.4）。</para>
    ///
    /// <para>下面把<b>三档全钉上</b> —— 以后谁改速率标定（0.3/1.0/3.0）、或把乘子挪到别的位置相乘，
    /// 都会在这里当场红。</para>
    /// </summary>
    [Theory]
    // 伤害 / 胸部MaxHp=20 ⇒ 比例 ⇒ 等级 ⇒ 期望失血（× 锯齿 1.4 × 10 秒）
    [InlineData(5.0, BleedModel.BleedSeverity.Small, 4.2)]    // 25%  ⇒ 小(0.3) × 1.4 × 10 = 4.2
    [InlineData(8.0, BleedModel.BleedSeverity.Medium, 14.0)]  // 40%  ⇒ 中(1.0) × 1.4 × 10 = 14.0
    [InlineData(15.0, BleedModel.BleedSeverity.Large, 42.0)]  // 75%  ⇒ 大(3.0) × 1.4 × 10 = 42.0
    public void Sharp_weapon_multiplier_scales_the_severity_rate(
        double damage, BleedModel.BleedSeverity expectedLevel, double expectedBloodLost)
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;

        // [T58 读法B] 流血仍掷概率门：0.0 ⇒ 必过。
        var fx = new CombatEffectResolver(new SequenceRandomSource(new[] { 0.0 }));
        var result = new CombatResult
        {
            HitPart = body.Parts[HumanBody.Chest], // MaxHp = 20
            FinalDamage = damage,
            FinalDamageType = DamageType.Sharp,
            InitialAttackRoll = damage,
        };

        fx.Apply(body, Sharp(bleedMult: 1.4), result);

        Assert.Equal(1, body.BleedingWoundCount);
        Assert.Equal(expectedLevel, body.BleedSeverityOn(HumanBody.Chest));
        Assert.Equal(1.4, body.BleedRateMultiplierOn(HumanBody.Chest), 9); // 乘子确实被盖在了伤口上

        body.TickBleed(10);
        // 实际速率 = 等级速率 × 武器乘子（**不是**只看乘子，也**不是**只看等级）
        Assert.Equal(expectedBloodLost, body.BloodMax - body.Blood, 6);
    }

    /// <summary>
    /// 对照组：**普通刀**（乘子 1.0）在同一档上流得比锯齿剑刃慢 40% —— 证明乘子这根轴真的在起作用，
    /// 而不是等级速率一个人说了算。
    /// </summary>
    [Fact]
    public void Plain_blade_bleeds_slower_than_serrated_at_the_same_severity()
    {
        double Lost(double bleedMult)
        {
            var body = HumanBody.NewBody();
            body.BleedRatePerWound = 1.0;
            var fx = new CombatEffectResolver(new SequenceRandomSource(new[] { 0.0 }));
            fx.Apply(body, Sharp(bleedMult: bleedMult), new CombatResult
            {
                HitPart = body.Parts[HumanBody.Chest],
                FinalDamage = 15, // 75% ⇒ 大流血
                FinalDamageType = DamageType.Sharp,
                InitialAttackRoll = 15,
            });
            body.TickBleed(10);
            return body.BloodMax - body.Blood;
        }

        Assert.Equal(30.0, Lost(1.0), 6); // 大(3.0) × 普通刀(1.0) × 10
        Assert.Equal(42.0, Lost(1.4), 6); // 大(3.0) × 锯齿(1.4) × 10 ⇒ 正好 +40%
    }

    [Fact]
    public void Severed_stump_also_carries_the_weapon_multiplier()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        double maxHp = body.MaxHpOf(HumanBody.LeftArm);

        var fx = new CombatEffectResolver(new SequenceRandomSource(Array.Empty<double>()));
        var result = new CombatResult
        {
            HitPart = body.Parts[HumanBody.LeftArm],
            FinalDamage = maxHp, // 一刀砍下来
            FinalDamageType = DamageType.Sharp,
            InitialAttackRoll = maxHp,
        };

        fx.Apply(body, Sharp(bleedMult: 1.4), result);

        Assert.True(body.IsGone(HumanBody.LeftArm));
        // [T58] 断口一律**大流血**（血管全断，本就是封顶那一档）。
        Assert.Equal(BleedModel.BleedSeverity.Large, body.BleedSeverityOn(HumanBody.LeftArm));
        body.TickBleed(10);
        Assert.Equal(42.0, body.BloodMax - body.Blood, 6); // 大(3.0) × 锯齿(1.4) × 10：断口也按锯齿的速率放血
    }

    // ---------------- 钉子强化：钝器 25% 几率造成「小流血」 ----------------

    [Fact]
    public void Blunt_weapon_with_bleed_on_hit_can_cause_a_small_wound()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;

        // 钝器打躯干的 roll 顺序：① 小流血 ② 震荡 ③ 骨折。只让第一个中。
        var fx = new CombatEffectResolver(new SequenceRandomSource(new[] { 0.1, 0.99, 0.99 }));
        var result = new CombatResult
        {
            HitPart = body.Parts[HumanBody.Chest],
            FinalDamage = 5,
            FinalDamageType = DamageType.Blunt,
            InitialAttackRoll = 5,
        };

        fx.Apply(body, Blunt(bleedOnHit: 0.25), result);

        Assert.Equal(1, body.BleedingWoundCount);

        // [T58] 「小流血」现在就是 BleedSeverity.Small 这一级（不再是"速率减半的普通伤口"）。
        Assert.Equal(BleedModel.BleedSeverity.Small, body.BleedSeverityOn(HumanBody.Chest));
        body.TickBleed(10);
        Assert.Equal(10.0 * BleedModel.SeverityRateOf(BleedModel.BleedSeverity.Small), body.BloodMax - body.Blood, 6);
    }

    [Fact]
    public void Blunt_weapon_bleed_on_hit_misses_the_roll_no_wound()
    {
        var body = HumanBody.NewBody();
        // ① 小流血 roll = 0.9 ≥ 0.25 ⇒ 不中；后面是震荡/骨折的 roll。
        var fx = new CombatEffectResolver(new SequenceRandomSource(new[] { 0.9, 0.99, 0.99 }));
        var result = new CombatResult
        {
            HitPart = body.Parts[HumanBody.Chest],
            FinalDamage = 5,
            FinalDamageType = DamageType.Blunt,
            InitialAttackRoll = 5,
        };

        fx.Apply(body, Blunt(bleedOnHit: 0.25), result);

        Assert.Equal(0, body.BleedingWoundCount);
    }

    [Fact]
    public void Bleed_on_hit_needs_actual_damage()
    {
        // 被甲完全挡下（dmg = 0）⇒ 钉子没扎进去 ⇒ 不流血（小流血 roll 被 dmg>0 挡在门外，一次都不抽）。
        // 序列里只留震荡那一发（震荡与 dmg 无关，照抽）——若小流血也抽了随机，序列会错位/耗尽而变红。
        var body = HumanBody.NewBody();
        var fx = new CombatEffectResolver(new SequenceRandomSource(new[] { 0.99 }));
        var result = new CombatResult
        {
            HitPart = body.Parts[HumanBody.Chest],
            FinalDamage = 0,
            FinalDamageType = DamageType.Blunt,
            InitialAttackRoll = 5,
        };

        fx.Apply(body, Blunt(bleedOnHit: 0.25), result);

        Assert.Equal(0, body.BleedingWoundCount);
    }

    /// <summary>
    /// 🔴 [T58]「小流血」**统一到了正式语义**：它就是 <see cref="BleedModel.BleedSeverity.Small"/> 这一级
    /// （旧的 <c>SmallWoundRateMultiplier = 0.5</c>「速率减半的普通伤口」已**退役**）。
    /// ⇒ 钉子强化扎出的口子会和别的小流血**按同一套规则合并**（同一处扎两下 ⇒ 中流血）。
    /// </summary>
    [Fact]
    public void Small_bleed_is_the_smallest_tier_and_merges_like_any_other()
    {
        Assert.True(BleedModel.SeverityRateOf(BleedModel.BleedSeverity.Small)
                    < BleedModel.SeverityRateOf(BleedModel.BleedSeverity.Medium));
        Assert.True(BleedModel.SeverityRateOf(BleedModel.BleedSeverity.Small) > 0);

        var small = HumanBody.NewBody();
        var medium = HumanBody.NewBody();
        small.BleedRatePerWound = 1.0;
        medium.BleedRatePerWound = 1.0;

        small.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Small);
        medium.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);

        small.TickBleed(5);
        medium.TickBleed(5);
        Assert.True(small.BloodMax - small.Blood < medium.BloodMax - medium.Blood);

        // 钉子扎同一处两下 ⇒ 两个小流血 ⇒ **合并成中流血**（仍是一处、一台手术）。
        var twice = HumanBody.NewBody();
        twice.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Small);
        twice.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Small);
        Assert.Equal(BleedModel.BleedSeverity.Medium, twice.BleedSeverityOn(HumanBody.Chest));
        Assert.Equal(1, twice.BleedingWoundCount);
    }

    // ---------------- 🔴 零漂移护栏 ----------------

    /// <summary>数一共抽了几次随机（零漂移的结构性证明工具）。</summary>
    private sealed class CountingRandomSource : IRandomSource
    {
        private readonly IRandomSource _inner;

        public CountingRandomSource(IRandomSource inner) => _inner = inner;

        public int Draws { get; private set; }

        public double Range(double min, double max)
        {
            Draws++;
            return _inner.Range(min, max);
        }
    }

    [Fact]
    public void Zero_drift_no_rng_consumed_when_bleed_on_hit_is_zero()
    {
        // 🔴 零漂移的**结构性证明**：BleedOnHitChance == 0（＝所有既有武器）时，新代码**一次随机都不能抽** ——
        // 否则整条随机流错位，Sim 基线必漂。做法：同一击分别用 chance=0 与 chance=0.25 跑，数抽了几次。
        // 判据：**chance=0.25 恰好比 chance=0 多抽 1 次**（就是那一发小流血 roll）⇒
        //       chance=0 时该分支被前置短路**完全跳过**，既有随机流一位不动。
        static int DrawsFor(double chance)
        {
            var body = HumanBody.NewBody();
            var rng = new CountingRandomSource(new SystemRandomSource(seed: 1234));
            var fx = new CombatEffectResolver(rng);
            var result = new CombatResult
            {
                HitPart = body.Parts[HumanBody.Chest],
                FinalDamage = 5,
                FinalDamageType = DamageType.Blunt,
                InitialAttackRoll = 5,
            };

            fx.Apply(body, Blunt(bleedOnHit: chance), result);
            return rng.Draws;
        }

        Assert.Equal(DrawsFor(0.0) + 1, DrawsFor(0.25));
    }

    [Fact]
    /// <summary>
    /// [T58] 多部位汇总：失血 = Σ(等级权重 × 速率乘数 × 部位分级权重) × BleedRatePerWound × dt。
    /// 三根轴正交相乘（口子多大 / 谁砍的 / 砍在哪）。
    /// </summary>
    public void Multi_part_bleed_sums_severity_times_part_weight()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 0.55; // 实机默认值

        // 胸部连中两刀（中 + 中 ⇒ **合并成大**，速率 3.0；部位分级=致命，权重 1.0）
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.LeftHand, BleedModel.BleedSeverity.Medium);   // 轻微，权重 0.5
        body.RegisterBleed(HumanBody.RightIndex, BleedModel.BleedSeverity.Medium); // 微小，权重 0.2

        body.TickBleed(3);

        // 致命 3.0 × 1.0 × 0.55 × 3 = 4.95 ；非致命 (1.0×0.5 + 1.0×0.2) × 0.55 × 3 = 1.155
        Assert.Equal(4.95 + 1.155, body.BloodMax - body.Blood, 6);
    }

    // ---------------- 存档 ----------------

    [Fact]
    public void Wound_rates_survive_a_save_round_trip()
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium, 1.4);
        body.RegisterBleed(HumanBody.LeftLeg, BleedModel.BleedSeverity.Medium, 0.5);

        BodySnapshot snap = body.Capture();

        var restored = HumanBody.NewBody();
        restored.Restore(snap);
        restored.BleedRatePerWound = 1.0;

        Assert.Equal(2, restored.BleedingWoundCount);
        restored.TickBleed(10);
        Assert.Equal((1.4 + 0.5) * 10, restored.BloodMax - restored.Blood, 6);
    }

    /// <summary>
    /// 🔴 [T58] **老存档迁移**：T58 之前同一部位会重复出现 N 次（那时一处伤口一条，最多 3 处）。
    /// <see cref="Body.Restore"/> 把重复项**逐个按小流血合并**：1 次 ⇒ 小、**2 次 ⇒ 中**、3 次（旧封顶）⇒ 大。
    /// 旧封顶速率（3 处 × 1.0 = 3.0）＝ 新大流血速率（3.0）⇒ **老档里最重的那档流血一分不差**。无需版本闸门。
    /// </summary>
    [Fact]
    public void Old_save_merges_repeated_parts_into_severity_levels()
    {
        var snap = new BodySnapshot
        {
            Bleeding = new List<string> { HumanBody.Chest, HumanBody.Chest }, // 老档：躯干 2 处伤口
            BleedingRates = new List<double>(),  // 老档：字段缺失 ⇒ 回落 1.0
            BleedingLevels = new List<int>(),    // 老档：字段缺失 ⇒ 回落"小流血"再合并
            BloodMax = 100,
            Blood = 100,
            BleedRatePerWound = 1.0,
            BleedRateMultiplier = 1.0,
        };

        var body = HumanBody.NewBody();
        body.Restore(snap);

        Assert.Equal(1, body.BleedingWoundCount); // 合并成一处
        Assert.Equal(BleedModel.BleedSeverity.Medium, body.BleedSeverityOn(HumanBody.Chest)); // 小+小 ⇒ 中
        body.TickBleed(10);
        Assert.Equal(10.0, body.BloodMax - body.Blood, 6); // 中(1.0) × 1.0 × 10
    }

    /// <summary>[T58] 老档里"旧封顶"（同一部位 3 处伤口）⇒ 大流血，速率与旧制**完全相等**。</summary>
    [Fact]
    public void Old_save_three_wounds_on_one_part_becomes_large_at_identical_rate()
    {
        var snap = new BodySnapshot
        {
            Bleeding = new List<string> { HumanBody.Chest, HumanBody.Chest, HumanBody.Chest },
            BleedingRates = new List<double>(),
            BleedingLevels = new List<int>(),
            BloodMax = 100,
            Blood = 100,
            BleedRatePerWound = 1.0,
            BleedRateMultiplier = 1.0,
        };

        var body = HumanBody.NewBody();
        body.Restore(snap);

        Assert.Equal(BleedModel.BleedSeverity.Large, body.BleedSeverityOn(HumanBody.Chest));
        body.TickBleed(10);
        Assert.Equal(30.0, body.BloodMax - body.Blood, 6); // 旧：3 处 × 1.0 × 10 = 30 —— **逐位相同**
    }
}
