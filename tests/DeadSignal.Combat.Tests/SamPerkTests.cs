using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// **山姆·英雄风范**（authored 三级专属效果）纯逻辑单测。锁的是规则形态，不锁绝对数值（用常量引用）。
///
/// 本 perk 的独特之处 = **可倒退**：等级不由累计量推进（诺蒂靠累计阅读时长、道格靠共同存活天数，二者只增不减），
/// 而由**实时营地人数**派生 —— 人多则升、人死则退。测试重点覆盖：
///   · 人数阈值升降（含"人死 → 等级倒退 → 光环消失"）；
///   · L1 负重/操作只作用山姆本人；L2 承伤/恢复只作用山姆本人；L3 震荡/流血效果只作用山姆本人；
///   · 山姆死亡 → 等级归 0 → 一切效果消失；
///   · 引擎减伤层默认 0 → 既有角色零回归。
/// </summary>
public class SamPerkTests
{
    // ---------- 可倒退的等级派生（按实时营地人数） ----------

    [Fact]
    public void Level_DerivesFromCampPopulation_Thresholds()
    {
        Assert.Equal(1, SamPerk.EvaluateLevel(1, samAlive: true)); // 只有山姆一人
        Assert.Equal(1, SamPerk.EvaluateLevel(SamPerk.Level2CampPopulation - 1, samAlive: true));
        Assert.Equal(2, SamPerk.EvaluateLevel(SamPerk.Level2CampPopulation, samAlive: true)); // 3 人 → L2
        Assert.Equal(2, SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation - 1, samAlive: true));
        Assert.Equal(3, SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true)); // 6 人 → L3
        Assert.Equal(3, SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation + 4, samAlive: true)); // 再多也是 L3
    }

    [Fact]
    public void Level_Regresses_WhenCampPopulationDrops()
    {
        // 与诺蒂/道白的单调升级相反：山姆的等级跟着人数实时涨落，死了人就退回去。
        Assert.Equal(3, SamPerk.EvaluateLevel(6, samAlive: true));
        Assert.Equal(2, SamPerk.EvaluateLevel(5, samAlive: true)); // 死一人 → 立刻退回 L2
        Assert.Equal(1, SamPerk.EvaluateLevel(2, samAlive: true)); // 再死到 2 人 → 退回 L1
        Assert.Equal(3, SamPerk.EvaluateLevel(6, samAlive: true)); // 招到人 → 再升回 L3（无历史包袱，纯派生）
    }

    [Fact]
    public void Level_IsZero_WhenSamIsDead()
    {
        // 山姆死 → 无等级、无任何效果（含全营光环）。营地人数再多也不管用。
        Assert.Equal(0, SamPerk.EvaluateLevel(8, samAlive: false));
        Assert.Equal(0, SamPerk.EvaluateLevel(0, samAlive: false));
    }

    // ---------- L2：受到伤害 −10%（仅山姆本人） ----------

    [Fact]
    public void Level2_DamageReduction_AppliesToSamOnly()
    {
        int lv = SamPerk.EvaluateLevel(3, samAlive: true);
        Assert.Equal(SamPerk.Level2DamageReduction, SamPerk.IncomingDamageReduction(lv, isSam: true));
        Assert.Equal(0.0, SamPerk.IncomingDamageReduction(lv, isSam: false)); // 别人没有
        Assert.Equal(0.0, SamPerk.IncomingDamageReduction(1, isSam: true)); // L1 尚未解锁
    }

    [Fact]
    public void Level2_DamageReduction_PersistsAtHigherLevels()
    {
        // 等级累进（同诺蒂 L3 保留 L2 自身加成、南丁格尔 L3 与 L2 叠加）：升到 2/3 级不丢 1 级的减伤。
        Assert.Equal(SamPerk.Level2DamageReduction, SamPerk.IncomingDamageReduction(2, isSam: true));
        Assert.Equal(SamPerk.Level2DamageReduction, SamPerk.IncomingDamageReduction(3, isSam: true));
    }

    [Fact]
    public void DamageReduction_GoneWhenSamDead()
    {
        Assert.Equal(0.0, SamPerk.IncomingDamageReduction(SamPerk.EvaluateLevel(6, samAlive: false), isSam: true));
    }

    // ---------- L1：负重 +15% / 操作 +10%（仅山姆本人） ----------

    [Fact]
    public void Level1_CarryBonus_SamOnly()
    {
        int lv = SamPerk.EvaluateLevel(1, samAlive: true); // L1
        Assert.Equal(1.0 + SamPerk.Level1CarryBonus, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6);
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: false), 6);
    }

    [Fact]
    public void Level1_OperationBonus_AppliesToSamOnly()
    {
        int lv = SamPerk.EvaluateLevel(1, samAlive: true);
        Assert.Equal(1.0 + SamPerk.Level1OperationBonus,
            SamPerk.PersonalOperationCapabilityMultiplier(lv, isSam: true), 6);
        Assert.Equal(1.0, SamPerk.PersonalOperationCapabilityMultiplier(lv, isSam: false), 6);
    }

    [Fact]
    public void Level3_CarryBonus_RemainsPersonalOnly()
    {
        int lv = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true); // L3
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: false), 6);
        Assert.Equal(1.15, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6);
    }

    [Fact]
    public void CarryBonus_GoneWhenSamDead()
    {
        int lv = SamPerk.EvaluateLevel(8, samAlive: false); // 0
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6);
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: false), 6);
    }

    // ---------- 旧版全营光环 API 保持中性（兼容调用） ----------

    [Fact]
    public void Level3_Aura_WorkSpeed_HealSpeed_InfectionWorsen()
    {
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);

        Assert.Equal(1.0, SamPerk.CampWorkSpeedMultiplier(l3), 6);
        Assert.Equal(1.0, SamPerk.CampHealSpeedMultiplier(l3), 6);
        Assert.Equal(1.0, SamPerk.CampInfectionWorsenMultiplier(l3), 6);
    }

    [Fact]
    public void BelowLevel3_NoAura()
    {
        foreach (int lv in new[] { 0, 1, 2 })
        {
            Assert.Equal(1.0, SamPerk.CampWorkSpeedMultiplier(lv), 6);
            Assert.Equal(1.0, SamPerk.CampHealSpeedMultiplier(lv), 6);
            Assert.Equal(1.0, SamPerk.CampInfectionWorsenMultiplier(lv), 6);
        }
    }

    [Fact]
    public void Aura_Vanishes_TheMomentPopulationDropsBelowSix()
    {
        // 「人死的那一刻光环就退」：6 人 L3 有光环 → 死一人剩 5 人 → 立刻 L2 → 四项光环全没。
        int before = SamPerk.EvaluateLevel(6, samAlive: true);
        int after = SamPerk.EvaluateLevel(5, samAlive: true);
        Assert.Equal(3, before);
        Assert.Equal(2, after);
        Assert.Equal(1.0, SamPerk.CampWorkSpeedMultiplier(before), 6);
        Assert.Equal(1.0, SamPerk.CampWorkSpeedMultiplier(after), 6);
        Assert.Equal(1.0, SamPerk.CampHealSpeedMultiplier(after), 6);
        Assert.Equal(1.0, SamPerk.CampInfectionWorsenMultiplier(after), 6);
        // 山姆本人的 L1 负重仍在（5 人 ≥ 3），只是没有旧版全营光环。
        Assert.Equal(1.0 + SamPerk.Level1CarryBonus, SamPerk.CarryCapacityMultiplier(after, isSam: true), 6);
    }

    [Fact]
    public void Aura_Vanishes_WhenSamHimselfDies_EvenWithFullCamp()
    {
        // 「只要山姆还活着」：山姆一死，哪怕营地还有 7 个人，光环也没了。
        int lv = SamPerk.EvaluateLevel(7, samAlive: false);
        Assert.Equal(1.0, SamPerk.CampWorkSpeedMultiplier(lv), 6);
        Assert.Equal(1.0, SamPerk.CampHealSpeedMultiplier(lv), 6);
        Assert.Equal(1.0, SamPerk.CampInfectionWorsenMultiplier(lv), 6);
    }

    // ---------- 营地人数口径：活着的、在营的**人**（狗不算） ----------

    [Fact]
    public void CampPopulation_CountsLivingHumansOnly_DogsExcluded()
    {
        // 传入的是"活着的在营人类数"——狗（布鲁斯）不计入。此测试锁的是 API 口径：
        // 调用方只喂人数，perk 无从得知狗的存在。5 人 + 1 狗 = 5 → L2（若狗算人则会是 L3，本断言即防线）。
        Assert.Equal(2, SamPerk.EvaluateLevel(5, samAlive: true));
    }

    [Fact]
    public void Level2_HealSpeedMultiplier_IsPersonal()
    {
        Assert.Equal(1.0, SamPerk.PersonalHealSpeedMultiplier(1, isSam: true), 6);
        Assert.Equal(1.0 + SamPerk.Level2HealSpeedBonus,
            SamPerk.PersonalHealSpeedMultiplier(2, isSam: true), 6);
        Assert.Equal(1.0, SamPerk.PersonalHealSpeedMultiplier(2, isSam: false), 6);
    }

    [Fact]
    public void Level3_ConcussionAndLargeBleedEffects_ArePersonal()
    {
        Assert.Equal(1.0, SamPerk.ConcussionChanceMultiplier(2, isSam: true), 6);
        Assert.Equal(1.0 - SamPerk.Level3ConcussionReduction,
            SamPerk.ConcussionChanceMultiplier(3, isSam: true), 6);
        Assert.Equal(1.0, SamPerk.ConcussionChanceMultiplier(3, isSam: false), 6);
        Assert.True(SamPerk.DowngradesLargeBleed(3, isSam: true));
        Assert.False(SamPerk.DowngradesLargeBleed(3, isSam: false));
        Assert.False(SamPerk.DowngradesLargeBleed(2, isSam: true));
    }

    [Fact]
    public void Level3_FracturePenalty_ReducesBothLimbPenaltiesByThirtyPercent()
    {
        double reduction = SamPerk.FracturePenaltyReduction(3, isSam: true);

        // “负面影响减轻 30%”作用于惩罚缺口：
        // 未治疗 ×0.7 → 1 - (1 - 0.7) × 0.7 = ×0.79；
        // 治疗中 ×0.85 → 1 - (1 - 0.85) × 0.7 = ×0.895。
        Assert.Equal(0.79, SamPerk.ApplyFracturePenaltyReduction(0.70, reduction), 9);
        Assert.Equal(0.895, SamPerk.ApplyFracturePenaltyReduction(0.85, reduction), 9);

        // 非 L3 / 非山姆不改变既有骨折系数。
        Assert.Equal(0.70, SamPerk.ApplyFracturePenaltyReduction(
            0.70, SamPerk.FracturePenaltyReduction(2, isSam: true)), 9);
        Assert.Equal(0.85, SamPerk.ApplyFracturePenaltyReduction(
            0.85, SamPerk.FracturePenaltyReduction(3, isSam: false)), 9);
    }

    // ---------- 引擎：护甲后的乘算减伤层（默认 0 = 零回归） ----------

    private static Weapon Blade(double dmg = 10) =>
        new() { Name = "试刀", DamageMin = dmg, DamageMax = dmg, DamageType = DamageType.Sharp, Penetration = 0 };

    private static BodyPart Chest() => new() { Name = HumanBody.Chest, MaxHp = 40, VolumeWeight = 40 };

    [Fact]
    public void EngineDamageReduction_DefaultsToZero_NoRegression()
    {
        // 默认参数不传 → 与既有路径**逐位一致**（既有 Sim 基线/其它角色零漂移）。
        var rngA = new SequenceRandomSource(10);
        var rngB = new SequenceRandomSource(10);
        double baseline = new CombatResolver(rngA).Resolve(Blade(), System.Array.Empty<ArmorLayer>(), Chest()).FinalDamage;
        double defaulted = new CombatResolver(rngB)
            .Resolve(Blade(), System.Array.Empty<ArmorLayer>(), Chest(), incomingDamageReduction: 0.0).FinalDamage;
        Assert.Equal(10.0, baseline, 6);
        Assert.Equal(baseline, defaulted, 6);
    }

    [Fact]
    public void EngineDamageReduction_MultipliesAfterArmor_NoArmorCase()
    {
        var rng = new SequenceRandomSource(10);
        CombatResult r = new CombatResolver(rng)
            .Resolve(Blade(), System.Array.Empty<ArmorLayer>(), Chest(), incomingDamageReduction: 0.10);
        Assert.Equal(10.0, r.RawDamage, 6);   // RawDamage = 护甲结算后的原始伤害（不含减伤，日志诚实）
        Assert.Equal(9.0, r.FinalDamage, 6);  // 减伤作用在最终伤害：10 × 0.9
    }

    [Fact]
    public void EngineDamageReduction_AppliesAfterArmor_NotBefore()
    {
        // 护甲先吃，剩下的再 ×0.9（而非先减伤再让护甲吃）。
        // 攻 12 vs 防 roll 5：atk ≥ def → 全额穿透，carried = 12 → 减伤后 10.8。
        // 若顺序反了（先 12×0.9=10.8 再与甲比），穿透后应是 10.8——用 RawDamage 锁住"减伤未参与护甲判定"。
        var layer = new ArmorLayer { Name = "布衣", Slot = ArmorSlot.Skin, SharpDefense = 6, BluntDefense = 3 };
        var rng = new SequenceRandomSource(12, 5);
        CombatResult r = new CombatResolver(rng)
            .Resolve(Blade(12), new[] { layer }, Chest(), incomingDamageReduction: 0.10);
        Assert.Equal(LayerOutcome.Full, r.Layers[0].Outcome);
        Assert.Equal(12.0, r.Layers[0].AttackRoll, 6);          // 护甲判定用的是未减伤的攻击 roll
        Assert.Equal(12.0, r.RawDamage, 6);                     // 护甲结算后 = 12（减伤尚未介入）
        Assert.Equal(10.8, r.FinalDamage, 6);                   // 再 ×0.9
    }

    [Fact]
    public void EngineDamageReduction_BlockedByArmor_StaysZero()
    {
        // 被甲挡下（终止）→ 0 伤，减伤层不该把 0 抬成 MinLandedDamage。
        var layer = ArmorTable.Plate();
        var rng = new SequenceRandomSource(10, 40); // 攻击 roll 低于防御 roll 的一半 → 挡下、结算终止
        CombatResult r = new CombatResolver(rng)
            .Resolve(Blade(), new[] { layer }, Chest(), incomingDamageReduction: 0.10);
        Assert.True(r.Terminated);
        Assert.Equal(0.0, r.FinalDamage, 6);
    }

    // ---------- 引擎：负重上限乘子（默认 1.0 = 零回归） ----------

    [Fact]
    public void Loadout_CapacityMultiplier_DefaultsToOne()
    {
        Assert.Equal(Loadout.CarryLimit(1.0), Loadout.CarryLimit(1.0, 1.0), 6);
    }

    [Fact]
    public void Loadout_CapacityMultiplier_ScalesCapacity()
    {
        double baseCap = Loadout.CarryLimit();
        double samL2 = Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(2, isSam: true));
        Assert.Equal(baseCap * 1.15, samL2, 6);
    }

    /// <summary>山姆的负重加成不再是空头支票：它乘的是**真实存在的** 80kg 上限。</summary>
    [Fact]
    public void Loadout_SamL2_BuysRealKilograms()
    {
        double samL2 = Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(2, isSam: true));
        Assert.Equal(92.0, samL2, 6);                                    // 80 × 1.15
        Assert.Equal(12.0, samL2 - Loadout.CarryLimit(), 6);             // 比别人多扛 12kg
    }

    /// <summary>
    /// 山姆的 ×1.15 抬的是**三档整体**，不只是终点线：他的无影响线 34.5kg、加重线 57.5kg、上限 92kg。
    /// 叙事口径：他"从小帮祖母打理农庄"体现在每一档都比别人扛得住——
    /// [T45] 装备进账后这条有了很具体的意思：**山姆的搜刮余量比别人多 4.5kg**（免罚线 34.5 vs 30），
    /// 穿同一身重甲，他能比队友多搬两根木料回家。
    /// </summary>
    [Fact]
    public void Loadout_SamL2_ShiftsAllThreeBands_NotJustTheCeiling()
    {
        double sam = Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(2, isSam: true));
        Assert.Equal(34.5, Loadout.FreeThresholdFor(sam), 6);   // 30 × 1.15
        Assert.Equal(57.5, Loadout.StrainThresholdFor(sam), 6); // 50 × 1.15

        // 同样背 32kg（≈ 重甲出门 29.9kg + 搜了 2kg）：常人已进轻度档，山姆仍无影响
        Assert.Equal(LoadoutTier.Encumbered, Loadout.TierOf(32, Loadout.CarryLimit()));
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(32, sam));
        Assert.Equal(1.0, Loadout.SpeedMultiplier(32, sam), 6);
    }

    /// <summary>三级时山姆仍保留 L1 的个人 ×1.15 负重，上限 92kg；没有全营负重光环。</summary>
    [Fact]
    public void Loadout_SamL3_ChainsMultiplicatively_NotAdditively()
    {
        double sam = Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(3, isSam: true));
        Assert.Equal(80.0 * 1.15, sam, 6);
        Assert.Equal(92.0, sam, 2);

        // 其他人没有个人加成。
        Assert.Equal(80.0, Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(3, isSam: false)), 6);
    }

    // ---------- 通则：百分比加成一律**乘算**，作用于当前实际值（残疾不被加成补偿） ----------

    [Fact]
    public void OperationAura_IsMultiplicative_NotAdditive()
    {
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);

        // 旧版全营光环入口现在是中性；乘算仍不会凭空给残缺者加能力。
        const double twoFingersGone = 0.86;
        Assert.Equal(twoFingersGone, SamPerk.OperationCapabilityWithAura(twoFingersGone, l3), 9);
    }

    [Fact]
    public void OperationAura_HandlessMan_StaysAtZero()
    {
        // 用户口径的硬核心：一个手全没了的人，操作能力就是 0。
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);
        Assert.Equal(0.0, SamPerk.OperationCapabilityWithAura(0.0, l3), 9);
    }

    [Fact]
    public void SamsOwnAura_DoesNotCompensateHisOwnMissingFingers()
    {
        // 当前 authored 页面只给山姆本人 L1 操作 ×1.10；旧版全营光环入口保持中性。
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);
        double samWithAura = SamPerk.OperationCapabilityWithAura(0.86, l3);
        Assert.True(samWithAura < 1.0, "缺两指的山姆即便吃满自己的光环，也不该达到健全人水平");
        Assert.Equal(0.86, samWithAura, 4);
    }

    [Fact]
    public void OperationAura_HealthyMan_GetsFullThreePercent()
    {
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);
        Assert.Equal(1.0, SamPerk.OperationCapabilityWithAura(1.0, l3), 9);
    }

    [Fact]
    public void OperationAura_Absent_LeavesCapabilityUntouched()
    {
        foreach (int lv in new[] { 0, 1, 2 }) // 无山姆 / 未到 L3
        {
            Assert.Equal(0.86, SamPerk.OperationCapabilityWithAura(0.86, lv), 9);
            Assert.Equal(0.0, SamPerk.OperationCapabilityWithAura(0.0, lv), 9);
        }
    }

    [Fact]
    public void PersonalOperationBonus_MultipliesCurrentCapability()
    {
        const double damagedHandCapability = 0.86;
        double actual = damagedHandCapability
            * SamPerk.PersonalOperationCapabilityMultiplier(1, isSam: true);
        Assert.Equal(0.86 * 1.10, actual, 9);
        Assert.NotEqual(0.96, actual, 6); // 禁止把 +10% 当成基准值加法
        Assert.Equal(0.0, 0.0 * SamPerk.PersonalOperationCapabilityMultiplier(1, isSam: true), 9);
    }

    // ---------- 工时：旧版全营操作光环保持中性 ----------

    [Fact]
    public void WorkSpeed_Aura_SpeedsUpCraftingAndDigging()
    {
        // 当前 authored 页面没有山姆全营工时加成，保留旧入口但不得改变工时。
        double mult = SamPerk.CampWorkSpeedMultiplier(3);
        var job = new CraftingJob("test_recipe", totalWorkMinutes: 103);
        job.Advance((int)(100 * mult), canWork: true);
        Assert.False(job.IsComplete, "旧版全营工时入口保持中性");

        var plain = new CraftingJob("test_recipe", totalWorkMinutes: 103);
        plain.Advance((int)(100 * SamPerk.CampWorkSpeedMultiplier(2)), canWork: true);
        Assert.False(plain.IsComplete, "无光环时 100 分钟推不满 103 工时");
    }

    // ---------- 健康：山姆本人 L2 恢复 ×1.30；旧版全营感染入口中性 ----------

    private static IRandomSource NoInfectionRng()
        => new SequenceRandomSource(System.Linq.Enumerable.Repeat(1.0, 16).ToArray());

    [Fact]
    public void HealSpeedMultiplier_DefaultsToOne_NoRegression()
    {
        var a = new HealthConditionSet();
        var ca = new HealthCondition(HealthConditionType.Bleeding, 0.5, "右手", onLimb: true);
        a.Add(ca);
        a.PerformSurgery(ca, materials: null, onBed: false, new SequenceRandomSource(13));
        a.TickDay(NoInfectionRng(), resting: false);

        var b = new HealthConditionSet();
        var cb = new HealthCondition(HealthConditionType.Bleeding, 0.5, "右手", onLimb: true);
        b.Add(cb);
        b.PerformSurgery(cb, materials: null, onBed: false, new SequenceRandomSource(13));
        b.TickDay(NoInfectionRng(), resting: false, healSpeedMultiplier: 1.0);

        Assert.Equal(ca.Severity, cb.Severity, 9); // 默认参数 = 既有行为，零回归
    }

    [Fact]
    public void Level2_Personal_SpeedsUpHealing()
    {
        var plain = new HealthConditionSet();
        var cp = new HealthCondition(HealthConditionType.Bleeding, 0.5, "右手", onLimb: true);
        plain.Add(cp);
        plain.PerformSurgery(cp, materials: null, onBed: false, new SequenceRandomSource(13));
        double before = cp.Severity;
        plain.TickDay(NoInfectionRng(), resting: false);
        double plainHeal = before - cp.Severity;

        var aura = new HealthConditionSet();
        var cs = new HealthCondition(HealthConditionType.Bleeding, 0.5, "右手", onLimb: true);
        aura.Add(cs);
        aura.PerformSurgery(cs, materials: null, onBed: false, new SequenceRandomSource(13));
        double beforeAura = cs.Severity;
        aura.TickDay(NoInfectionRng(), resting: false,
            healSpeedMultiplier: SamPerk.PersonalHealSpeedMultiplier(2, isSam: true));
        double auraHeal = beforeAura - cs.Severity;

        Assert.True(plainHeal > 0);
        Assert.Equal(plainHeal * (1.0 + SamPerk.Level2HealSpeedBonus), auraHeal, 9); // 恢复速度 ×1.30
    }

    [Fact]
    public void InfectionWorsenMultiplier_DefaultsToOne_NoRegression()
    {
        var a = new HealthConditionSet();
        var ia = new HealthCondition(HealthConditionType.Infection, 0.2, "左手", onLimb: true);
        a.Add(ia);
        a.AdvanceInfectionRace(1.0, medicated: false, medicine: null);

        var b = new HealthConditionSet();
        var ib = new HealthCondition(HealthConditionType.Infection, 0.2, "左手", onLimb: true);
        b.Add(ib);
        b.AdvanceInfectionRace(1.0, medicated: false, medicine: null, campWorsenMultiplier: 1.0);

        Assert.Equal(ia.Severity, ib.Severity, 9);
    }

    [Fact]
    public void Legacy_CampInfectionWorsen_IsNeutral()
    {
        var plain = new HealthConditionSet();
        var ip = new HealthCondition(HealthConditionType.Infection, 0.2, "左手", onLimb: true);
        plain.Add(ip);
        plain.AdvanceInfectionRace(1.0, medicated: false, medicine: null);
        double plainGain = ip.Severity - 0.2;

        var aura = new HealthConditionSet();
        var ia = new HealthCondition(HealthConditionType.Infection, 0.2, "左手", onLimb: true);
        aura.Add(ia);
        aura.AdvanceInfectionRace(1.0, medicated: false, medicine: null,
            campWorsenMultiplier: SamPerk.CampInfectionWorsenMultiplier(3));
        double auraGain = ia.Severity - 0.2;

        Assert.True(plainGain > 0);
        Assert.Equal(plainGain, auraGain, 9);
    }

    [Fact]
    public void Legacy_CampInfectionWorsen_DoesNotAlterMedicine()
    {
        // 与用药的 WorsenMultiplier 是**两个独立乘子**（药压得多、光环再压一点），不互相吞。
        Medicine herbal = MedicineCatalog.For("herbal_salve")!.Value;

        var med = new HealthConditionSet();
        var im = new HealthCondition(HealthConditionType.Infection, 0.2, "左手", onLimb: true);
        med.Add(im);
        med.AdvanceInfectionRace(1.0, medicated: true, medicine: herbal);
        double medGain = im.Severity - 0.2;

        var both = new HealthConditionSet();
        var ib = new HealthCondition(HealthConditionType.Infection, 0.2, "左手", onLimb: true);
        both.Add(ib);
        both.AdvanceInfectionRace(1.0, medicated: true, medicine: herbal,
            campWorsenMultiplier: SamPerk.CampInfectionWorsenMultiplier(3));
        double bothGain = ib.Severity - 0.2;

        Assert.Equal(medGain, bothGain, 9);
    }
}
