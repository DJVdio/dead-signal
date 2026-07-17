using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 失血致命性分级（用户口径）：
/// 「流血应当是短时间内危险致死的，多个严重流血伤口可能会导致这场战斗还没打出胜负就流血致死了」
/// 但同时 —— 小伤口（手/脚/指/趾）**不致命**，只溃烂感染（既有 <c>LethalBleed=false</c> 口径）。
/// 本文件钉死：①部位分级 ②同部位多伤口可叠加 ③非致命伤口有失血下限、绝不放干
/// ④断口出血一律致命（微小部位除外）⑤Sim 的 <see cref="DuelEngine"/> 确实消费这套（不是又退回只计数）。
/// </summary>
public class BleedLethalityTests
{
    // ---- ① 部位分级：大部位致命，手脚轻微，指趾微小 ----

    [Theory]
    [InlineData(HumanBody.Chest)]
    [InlineData(HumanBody.Abdomen)]
    [InlineData(HumanBody.Head)]
    [InlineData(HumanBody.LeftArm)]
    [InlineData(HumanBody.LeftLeg)]
    public void LethalParts_CanBleedToDeath(string part)
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(part, BleedModel.BleedSeverity.Medium);
        body.TickBleed(1000);

        Assert.True(body.BledOut, $"{part} 是大部位，深伤口必须能失血致死");
        Assert.True(body.IsDead);
    }

    [Theory]
    [InlineData(HumanBody.LeftHand)]   // 手 → 非致命（只溃烂感染）
    [InlineData(HumanBody.RightFoot)]  // 脚 → 非致命
    [InlineData(HumanBody.RightIndex)] // 指 → 微小
    [InlineData(HumanBody.LeftBigToe)] // 趾 → 微小
    [InlineData(HumanBody.LeftEar)]    // 耳 → 微小
    public void NonLethalParts_NeverBleedOut_EvenGivenForever(string part)
    {
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(part, BleedModel.BleedSeverity.Medium);
        body.TickBleed(100_000); // 放到天荒地老

        Assert.False(body.BledOut, $"{part} 是小部位，绝不该失血致死");
        Assert.False(body.IsDead);
        Assert.False(body.IsUnconscious, $"{part} 的小伤口连昏迷都不该造成");
        Assert.Equal(BleedModel.NonLethalBloodFloorRatio, body.BloodRatio, 6); // 抽到下限即止
    }

    [Fact]
    public void NonLethalWounds_CannotFinishOffAnAlreadyBleedingVictim()
    {
        // 大部位伤口已把血压到下限以下 → 小伤口不再贡献任何失血（它们永远不能是最后一根稻草）。
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.TickBleed(60); // 躯干把血抽到 40%（已低于 50% 下限）
        double after = body.Blood;

        body.StopBleed(HumanBody.Chest); // 躯干包扎住了
        body.RegisterBleed(HumanBody.LeftHand, BleedModel.BleedSeverity.Medium);
        body.TickBleed(1000);

        Assert.Equal(after, body.Blood, 6); // 一滴都没再掉
        Assert.False(body.IsDead);
    }

    // ---- ② 同部位多伤口叠加（用户：「多个严重流血伤口」）----

    /// <summary>
    /// 🔴 [T58] 躯干连中三刀 ⇒ **不是三处伤口，而是一处升级到封顶的大流血**（两中合大、大+中仍是大）。
    /// 这就是用户「防止过多的伤口浪费手术时间」的落地：**三刀 = 一台手术，不是三台。**
    /// </summary>
    [Fact]
    public void SamePart_HitRepeatedly_MergesIntoOneEscalatingWound()
    {
        var body = HumanBody.NewBody();
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);

        Assert.Equal(1, body.BleedingWoundCount);
        Assert.Equal(BleedModel.BleedSeverity.Large, body.BleedSeverityOn(HumanBody.Chest));
    }

    [Fact]
    public void StackedWounds_MultiplyBleedRate()
    {
        var one = HumanBody.NewBody();
        one.BleedRatePerWound = 1.0;
        one.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        one.TickBleed(2);

        var three = HumanBody.NewBody();
        three.BleedRatePerWound = 1.0;
        three.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        three.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        three.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        three.TickBleed(2);

        // 按 BloodMax 算失血量，不写死 100（储血上限可调）。
        Assert.Equal(3 * (one.BloodMax - one.Blood), three.BloodMax - three.Blood, 6); // 流速严格 ×3
    }

    /// <summary>
    /// [T58] 旧的「单部位最多 3 处伤口」封顶，已被**三级流血的合并 + 封顶大流血**取代：
    /// 同一部位砍 20 刀，**始终只有一处出血**，等级封顶在**大流血**（速率 3.0 —— 与旧的 3 处伤口封顶**完全相等**）。
    /// </summary>
    [Fact]
    public void SamePart_MergesIntoOneWound_CappedAtLarge()
    {
        var body = HumanBody.NewBody();
        for (int i = 0; i < 20; i++)
        {
            body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        }

        Assert.Equal(1, body.BleedingWoundCount);                                  // 永远只有一处
        Assert.Equal(BleedModel.BleedSeverity.Large, body.BleedSeverityOn(HumanBody.Chest)); // 封顶大流血
        Assert.Equal(3.0, body.BleedRateOn(HumanBody.Chest), 9);                   // ＝旧的 3 处伤口封顶速率
    }

    /// <summary>[T58] 用户的四条合并规则，逐条钉死（含用户没明说、由"封顶"唯一确定的「大+小 = 大」）。</summary>
    [Theory]
    [InlineData(BleedModel.BleedSeverity.Small, BleedModel.BleedSeverity.Small, BleedModel.BleedSeverity.Medium)]  // 两个小 ⇒ 中
    [InlineData(BleedModel.BleedSeverity.Medium, BleedModel.BleedSeverity.Medium, BleedModel.BleedSeverity.Large)] // 两个中 ⇒ 大
    [InlineData(BleedModel.BleedSeverity.Small, BleedModel.BleedSeverity.Medium, BleedModel.BleedSeverity.Large)]  // 一小一中 ⇒ 大
    [InlineData(BleedModel.BleedSeverity.Medium, BleedModel.BleedSeverity.Small, BleedModel.BleedSeverity.Large)]  // 顺序无关
    [InlineData(BleedModel.BleedSeverity.Large, BleedModel.BleedSeverity.Small, BleedModel.BleedSeverity.Large)]   // 封顶：大+小 = 大
    [InlineData(BleedModel.BleedSeverity.Large, BleedModel.BleedSeverity.Large, BleedModel.BleedSeverity.Large)]   // 封顶：大+大 = 大
    public void MergeRules_MatchUserSpec(BleedModel.BleedSeverity a, BleedModel.BleedSeverity b, BleedModel.BleedSeverity expected)
    {
        Assert.Equal(expected, BleedModel.Merge(a, b));

        var body = HumanBody.NewBody();
        body.RegisterBleed(HumanBody.Chest, a);
        body.RegisterBleed(HumanBody.Chest, b);
        Assert.Equal(expected, body.BleedSeverityOn(HumanBody.Chest));
        Assert.Equal(1, body.BleedingWoundCount); // 合并 ⇒ 永远只有一处 ⇒ **一台手术，不是两台**
    }

    /// <summary>[T58] 分级门槛：锐伤即小流血；&gt;30% 部位血量 ⇒ 中；&gt;60% ⇒ 大。</summary>
    [Theory]
    [InlineData(0.01, BleedModel.BleedSeverity.Small)]   // 剐蹭（穿甲后只渗进去一点点）
    [InlineData(0.30, BleedModel.BleedSeverity.Small)]   // 恰好 30% ⇒ **不算**中（规格是"大于"）
    [InlineData(0.31, BleedModel.BleedSeverity.Medium)]
    [InlineData(0.60, BleedModel.BleedSeverity.Medium)]  // 恰好 60% ⇒ **不算**大
    [InlineData(0.61, BleedModel.BleedSeverity.Large)]
    [InlineData(1.50, BleedModel.BleedSeverity.Large)]
    public void SeverityThresholds_MatchUserSpec(double ratio, BleedModel.BleedSeverity expected)
    {
        Assert.Equal(expected, BleedModel.SeverityOf(ratio * 20, 20));
    }

    [Fact]
    public void StopBleed_ClearsAllWoundsOnThatPart()
    {
        var body = HumanBody.NewBody();
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.StopBleed(HumanBody.Chest); // 包扎一处 = 整个部位止血

        Assert.Equal(0, body.BleedingWoundCount);
    }

    [Fact]
    public void BleedingWounds_StillReportsDistinctParts_ForHealthMapping()
    {
        // [T58] 每部位只有一处出血 ⇒ "出血处数" ≡ "出血部位数"。战后伤病仍按部位建档（一部位一条）。
        var body = HumanBody.NewBody();
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium); // 合并进上一处，不新增
        body.RegisterBleed(HumanBody.LeftLeg, BleedModel.BleedSeverity.Medium);

        Assert.Equal(2, body.BleedingWoundCount);           // 两个部位 = 两处出血 = **两台手术**
        Assert.Equal(2, body.BleedingWounds.Count);
        Assert.Contains(HumanBody.Chest, body.BleedingWounds);
        Assert.Contains(HumanBody.LeftLeg, body.BleedingWounds);
    }

    // ---- ③ 用户靶心：多个严重伤口在一场战斗内放干 ----

    /// <summary>
    /// 🔴 **[T53] 这条测试记录的是一个【当前不成立】的用户口径 —— 已 [DECISION] 上抛，别当普通失败去"修绿"。**
    ///
    /// <para>
    /// 用户口径 A（旧）：「流血应当是**短时间内危险致死**的，**多个严重流血伤口可能会导致这场战斗还没打出胜负就流血致死**」。
    /// 用户口径 B（新，T53 二次拍板）：「**不对齐了**」⇒ 流血口径 = **100 / 0.55**（游戏一直在跑的那套）。
    /// </para>
    /// <para>
    /// **两条口径互斥，由同一个数字控制**：
    /// <list type="bullet">
    /// <item>70 / 1.5（热）⇒ 三处重伤 **15.6s** 放干 ＜ 均场 24~55s ⇒ 口径 A **成立**，但丧尸围攻变断崖（2 只 16.6%、4 只 0%）。</item>
    /// <item>100 / 0.55（实机）⇒ 三处重伤 **60.7s** 放干 ＞ 均场 ⇒ **一场仗打完都流不死** ⇒ 口径 A **不成立**。</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ **关键事实**：口径 A **在实机里从来就没成立过** —— 100/0.55 一直是游戏跑的值。
    /// 这条测试此前"绿"，是因为它断言的是 <c>DuelConfig</c>（**模拟器**），不是游戏。
    /// **它验证的是仪器，不是产品** —— 与本单查出的根因是同一类 bug。
    /// </para>
    /// <para>
    /// 现在它如实记录**实机真相**（60.7s）。若将来用户要把口径 A 找回来，**不要简单调热流血**
    /// （那会把丧尸围攻打回断崖）—— 见 journal 的解耦建议：**让失血速率对伤口数次线性叠加/封顶**
    /// （生理上血管流量有上限），单挑多伤口仍快、群殴时伤口数爆炸却不会线性放大失血 ⇒ **打断平方律**。
    /// </para>
    /// </summary>
    [Fact]
    public void ThreeSevereWounds_BleedOut_ButNoLongerWithinATypicalFight()
    {
        var cfg = new DuelConfig();
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = cfg.BleedRatePerWound;
        body.SetBloodMax(cfg.BloodMax);
        body.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.LeftLeg, BleedModel.BleedSeverity.Medium);
        body.RegisterBleed(HumanBody.RightArm, BleedModel.BleedSeverity.Medium);

        double t = 0;
        while (!body.IsDead && t < 600)
        {
            body.TickBleed(0.1);
            t += 0.1;
        }

        Assert.True(body.BledOut); // 放干这件事本身仍然会发生 —— 只是慢

        // 🔴 如实记录**实机真相**：100 储血 ÷ (3 伤口 × 0.55/s) ≈ **60.7s**。
        // 这【超过】典型对决时长（24~55s）⇒ **「还没打出胜负就流血致死」当前不成立**（见上方类注与 [DECISION]）。
        // 下界仍钉"不是秒杀"（否则伤害与护甲就没意义了）。
        Assert.InRange(t, 55, 70);
    }

    // ---- ④ 断口出血：一律致命（微小部位除外）----

    [Fact]
    public void SeveredLimb_StumpBleed_IsAlwaysLethal_EvenOnAHand()
    {
        // 手掌的**划伤**不致命，但手被**整只砍掉**的断口是会把人放干的。
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.Sever(HumanBody.LeftHand);
        body.RegisterBleed(HumanBody.LeftHand, BleedModel.BleedSeverity.Medium);
        body.TickBleed(1000);

        Assert.True(body.BledOut, "断手的断口必须能失血致死");
    }

    [Fact]
    public void SeveredFinger_StumpBleed_IsNotLethal()
    {
        // 但断掉一根手指不会把人流血流死（微小部位仍是微小部位）。
        var body = HumanBody.NewBody();
        body.BleedRatePerWound = 1.0;
        body.Sever(HumanBody.RightIndex);
        body.RegisterBleed(HumanBody.RightIndex, BleedModel.BleedSeverity.Medium);
        body.TickBleed(100_000);

        Assert.False(body.BledOut, "断一根手指不该失血致死");
    }

    // ---- ⑤ 口径统一：引擎与 Godot 战后伤病系统共用同一套部位分级 ----

    [Fact]
    public void BleedModel_IsTheSingleSourceOfTruth_ForLethalParts()
    {
        var body = HumanBody.NewBody();
        foreach (var part in body.Parts.Values)
        {
            bool lethal = BleedModel.IsLethalPart(body, part.Name);
            bool expected = part.Region is BodyRegion.Torso or BodyRegion.Head or BodyRegion.Neck
                or BodyRegion.Arm or BodyRegion.Leg;
            Assert.Equal(expected, lethal);
        }
    }

    [Fact]
    public void RateWeights_AreOrdered_LethalGreaterThanMinorGreaterThanMicro()
    {
        Assert.True(BleedModel.RateWeightOf(BleedTier.Lethal) > BleedModel.RateWeightOf(BleedTier.Minor));
        Assert.True(BleedModel.RateWeightOf(BleedTier.Minor) > BleedModel.RateWeightOf(BleedTier.Micro));
        Assert.True(BleedModel.RateWeightOf(BleedTier.Micro) > 0);
    }

    // ---- ⑥ 消费护栏：Sim 的对决引擎确实吃到这套（变异测试用：摘掉消费逻辑此测必红）----

    [Fact]
    public void DuelEngine_ActuallyKillsByBleedout_NotJustCountsWounds()
    {
        // 匕首（低伤高频锐器）杀丧尸有一条**放血赛道**——若 Duel 不消费流血，这个比例会塌到 **0**。
        // 本条守的就是"Duel 真的在消费流血"这件事，不是某个具体比例。
        //
        // 🔴 [T58 漂移·如实记录] 三级流血之后这条赛道**变窄了**：**41/200 → 20/200（10%）**。
        //    原因：匕首打丧尸每刀进肉约 3.9 点、部位 MaxHp 20 ⇒ 只占 19.6% ⇒ **落在小流血（0.3）那一档**
        //    （没到 30% 的中流血门槛），而旧制挂上的是一处满速率（1.0）的伤口。
        //    ⇒ **"拿匕首划两刀站着等丧尸流干"从主要打法退成 1/10 的场次** —— 这正是用户要的方向
        //    （「护甲/浅伤不该被流血放大」），也是"丧尸围攻不再是死局"（2 只丧尸从改前的准死局抬回可打）
        //    的同一枚硬币的另一面。🔴 **围攻胜率的具体标定值不在这里抄第二份**——权威值以
        //    `BleedModel.SeverityRateOf` 的长注为准（`Effects.cs` 的读法 A/B 对照表亦明写"勿另行维护第二份数"），
        //    最新实测表见 `docs/research/2026-07-14-lanchester.md`（Sim `lanchester` 模式机器生成）。
        //    阈值随之下调，**不是悄悄放宽，是新口径下的真值**。
        var zombie = Fighter("丧尸", WeaponTable.ZombieClaw(), ArmorTable.ZombieHide().ToArray());
        int bleedout = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var r = new DuelEngine(new SystemRandomSource(20260713 + seed * 131))
                .Run(Fighter("我方", WeaponTable.Dagger(), Array.Empty<ArmorLayer>()), zombie);
            if (r.EndReason == DuelEndReason.Bleedout)
            {
                bleedout++;
            }
        }

        Assert.True(bleedout > 10, $"对决必须真的出现失血致死（放血赛道不能塌到 0），实际只有 {bleedout}/200");
    }

    // ---- ⑦ 丧尸的流血速度只有常人的 1/3（用户口径）----

    [Fact]
    public void ZombieBody_BleedsAtOneThirdTheRate_OfALivingPerson()
    {
        // 用户原话：「丧尸的流血速度只有 1/3，没那么容易流血致死」。行尸走肉，血液循环本就不像活人。
        var human = HumanBody.NewBody();
        human.BleedRatePerWound = 1.0;
        human.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        human.TickBleed(10);

        var zombie = HumanBody.NewZombieBody();
        zombie.BleedRatePerWound = 1.0;
        zombie.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        zombie.TickBleed(10);

        // 按 BloodMax 算失血量，不写死 100（储血上限是可调数值 BleedModel.DefaultBloodMax）。
        double humanLost = human.BloodMax - human.Blood;
        double zombieLost = zombie.BloodMax - zombie.Blood;

        Assert.Equal(BleedModel.ZombieBleedRateMultiplier, zombie.BleedRateMultiplier, 9);
        Assert.Equal(humanLost * BleedModel.ZombieBleedRateMultiplier, zombieLost, 9);
    }

    [Fact]
    public void BleedResistance_IsAnEntityTrait_NotHardcodedToTheName()
    {
        // 结构性字段，不是 if (name == "丧尸")。将来精英丧尸/动物/其他敌人各自填各自的值。
        var custom = HumanBody.NewBody();
        custom.BleedRateMultiplier = 0.5;
        custom.BleedRatePerWound = 1.0;
        custom.RegisterBleed(HumanBody.Chest, BleedModel.BleedSeverity.Medium);
        custom.TickBleed(10);

        // 断言失血量而非绝对血量（储血上限可调）：1.0 权重 × 0.5 抗性 × 1.0/s × 10s = 5
        Assert.Equal(5, custom.BloodMax - custom.Blood, 9);
    }

    [Fact]
    public void ZombieBleedResistance_AppliesToTheNonLethalFloorToo()
    {
        // 抗性只改流速，不改"小伤口永不致死"的下限语义。
        var zombie = HumanBody.NewZombieBody();
        zombie.BleedRatePerWound = 1.0;
        zombie.RegisterBleed(HumanBody.RightIndex, BleedModel.BleedSeverity.Medium);
        zombie.TickBleed(100_000);

        Assert.False(zombie.BledOut);
        Assert.Equal(BleedModel.NonLethalBloodFloorRatio, zombie.BloodRatio, 6);
    }

    [Fact]
    public void SharpWeapons_DoNotBleedZombiesOut_AsEasilyAsPeople()
    {
        // 端到端：同一把匕首，砍活人 vs 砍丧尸，出血致死占比必须明显拉开。
        // 这条正是"流血加强了但锐器对丧尸不会暴涨"的机械保证。
        double zombieBleedShare = BleedoutShare(HumanBody.NewZombieBody);
        double humanBleedShare = BleedoutShare(HumanBody.NewBody);

        Assert.True(
            humanBleedShare > zombieBleedShare * 1.3,
            $"丧尸不该像人一样容易被放血致死：人 {humanBleedShare:P1} vs 丧尸 {zombieBleedShare:P1}");
    }

    private static double BleedoutShare(Func<Body> victimBody)
    {
        var attacker = Fighter("我方", WeaponTable.Dagger(), Array.Empty<ArmorLayer>());
        var victim = new DuelFighter
        {
            Name = "靶子",
            Weapons = new[] { new WeaponMount { Weapon = WeaponTable.ZombieClaw(), RequiresHand = false } },
            Armor = ArmorTable.ZombieHide().ToArray(),
            BodyFactory = victimBody,
        };

        int bleedout = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            var r = new DuelEngine(new SystemRandomSource(20260713 + seed * 131)).Run(attacker, victim);
            if (r.EndReason == DuelEndReason.Bleedout && r.Loser == "靶子")
            {
                bleedout++;
            }
        }

        return bleedout / 400.0;
    }

    private static DuelFighter Fighter(string name, Weapon w, ArmorLayer[] armor) => new()
    {
        Name = name,
        Weapons = new[] { new WeaponMount { Weapon = w, RequiresHand = false } },
        Armor = armor,
    };
}
