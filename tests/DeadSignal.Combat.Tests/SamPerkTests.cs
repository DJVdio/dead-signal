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
///   · L1 减伤只作用山姆本人；L2 负重只作用山姆本人；L3 四项作用全营（含山姆）；
///   · 山姆死亡 → 等级归 0 → 一切效果（含全营光环）消失；
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

    // ---------- L1：受到伤害 −10%（仅山姆本人） ----------

    [Fact]
    public void Level1_DamageReduction_AppliesToSamOnly()
    {
        int lv = SamPerk.EvaluateLevel(1, samAlive: true);
        Assert.Equal(SamPerk.Level1DamageReduction, SamPerk.IncomingDamageReduction(lv, isSam: true));
        Assert.Equal(0.0, SamPerk.IncomingDamageReduction(lv, isSam: false)); // 别人没有
    }

    [Fact]
    public void Level1_DamageReduction_PersistsAtHigherLevels()
    {
        // 等级累进（同诺蒂 L3 保留 L2 自身加成、南丁格尔 L3 与 L2 叠加）：升到 2/3 级不丢 1 级的减伤。
        Assert.Equal(SamPerk.Level1DamageReduction, SamPerk.IncomingDamageReduction(2, isSam: true));
        Assert.Equal(SamPerk.Level1DamageReduction, SamPerk.IncomingDamageReduction(3, isSam: true));
    }

    [Fact]
    public void DamageReduction_GoneWhenSamDead()
    {
        Assert.Equal(0.0, SamPerk.IncomingDamageReduction(SamPerk.EvaluateLevel(6, samAlive: false), isSam: true));
    }

    // ---------- L2：负重 +15%（仅山姆本人）；L3：全营 +3%（含山姆，叠加） ----------

    [Fact]
    public void Level2_CarryBonus_SamOnly()
    {
        int lv = SamPerk.EvaluateLevel(SamPerk.Level2CampPopulation, samAlive: true); // L2
        Assert.Equal(1.0 + SamPerk.Level2CarryBonus, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6);
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: false), 6); // L2 时别人没有负重加成
    }

    [Fact]
    public void Level1_NoCarryBonus()
    {
        int lv = SamPerk.EvaluateLevel(1, samAlive: true);
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6);
    }

    [Fact]
    public void Level3_CarryBonus_CampWide_AndStacksMultiplicativelyForSam()
    {
        int lv = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true); // L3
        // 全营（含山姆）×1.03
        Assert.Equal(1.03, SamPerk.CarryCapacityMultiplier(lv, isSam: false), 6);
        // 山姆自己：L2 的 ×1.15（他自己的体格）与 L3 光环 ×1.03（他给全营的、含自己）**连乘** = ×1.1845
        // （**不是** 加算的 1+0.15+0.03 = 1.18 —— 百分比加成一律乘算，见通则）
        Assert.Equal(1.15 * 1.03, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6);
        Assert.NotEqual(1.18, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6); // 防加算回潮
    }

    [Fact]
    public void CarryBonus_GoneWhenSamDead()
    {
        int lv = SamPerk.EvaluateLevel(8, samAlive: false); // 0
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: true), 6);
        Assert.Equal(1.0, SamPerk.CarryCapacityMultiplier(lv, isSam: false), 6);
    }

    // ---------- L3 全营四项：负重 / 干活效率 / 身体恢复 / 感染上升 ----------

    [Fact]
    public void Level3_Aura_WorkSpeed_HealSpeed_InfectionWorsen()
    {
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);

        Assert.Equal(1.0 + SamPerk.AuraWorkSpeedBonus, SamPerk.CampWorkSpeedMultiplier(l3), 6);   // 干活快 3%（制作/建造/搜刮）
        Assert.Equal(1.0 + SamPerk.AuraHealSpeedBonus, SamPerk.CampHealSpeedMultiplier(l3), 6);   // 恢复快 3%
        Assert.Equal(1.0 - SamPerk.AuraInfectionWorsenReduction,
            SamPerk.CampInfectionWorsenMultiplier(l3), 6);                                        // 感染条上升慢 3%
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
        Assert.Equal(1.0 + SamPerk.AuraWorkSpeedBonus, SamPerk.CampWorkSpeedMultiplier(before), 6);
        Assert.Equal(1.0, SamPerk.CampWorkSpeedMultiplier(after), 6);
        Assert.Equal(1.0, SamPerk.CampHealSpeedMultiplier(after), 6);
        Assert.Equal(1.0, SamPerk.CampInfectionWorsenMultiplier(after), 6);
        // 山姆本人的 L2 负重仍在（5 人 ≥ 3），只是没了 +3% 光环。
        Assert.Equal(1.0 + SamPerk.Level2CarryBonus, SamPerk.CarryCapacityMultiplier(after, isSam: true), 6);
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

    // ---------- 引擎：护甲后的乘算减伤层（默认 0 = 零回归） ----------

    private static Weapon Blade(double dmg = 10) =>
        new() { Name = "试刀", DamageMin = dmg, DamageMax = dmg, DamageType = DamageType.Sharp, Penetration = 0 };

    private static BodyPart Chest() => new() { Name = "胸部", MaxHp = 40, VolumeWeight = 40 };

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
        var layer = new ArmorLayer { Name = "板甲", Slot = ArmorSlot.Plate, SharpDefense = 50, BluntDefense = 25 };
        var rng = new SequenceRandomSource(10, 40); // 攻 10 < 防 40 的一半 → 挡下、结算终止
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
    /// 叙事口径：他"从小帮祖母打理农庄"体现在每一档都比别人扛得住——同样背 32kg，别人已经开始拖速，他还是自由行动。
    /// </summary>
    [Fact]
    public void Loadout_SamL2_ShiftsAllThreeBands_NotJustTheCeiling()
    {
        double sam = Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(2, isSam: true));
        Assert.Equal(34.5, Loadout.FreeThresholdFor(sam), 6);   // 30 × 1.15
        Assert.Equal(57.5, Loadout.StrainThresholdFor(sam), 6); // 50 × 1.15

        // 同样背 32kg：常人已进轻度档，山姆仍无影响
        Assert.Equal(LoadoutTier.Encumbered, Loadout.TierOf(32, Loadout.CarryLimit()));
        Assert.Equal(LoadoutTier.Unencumbered, Loadout.TierOf(32, sam));
        Assert.Equal(1.0, Loadout.SpeedMultiplier(32, sam), 6);
    }

    /// <summary>三级时山姆自己吃「二级 ×1.15」×「全营 ×1.03」**连乘**，上限 94.76kg（≠ 加算的 ×1.18 = 94.4kg）。</summary>
    [Fact]
    public void Loadout_SamL3_ChainsMultiplicatively_NotAdditively()
    {
        double sam = Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(3, isSam: true));
        Assert.Equal(80.0 * 1.15 * 1.03, sam, 6);
        Assert.Equal(94.76, sam, 2);
        Assert.NotEqual(80.0 * 1.18, sam, 6); // 防加算回潮

        // 全营光环：别人只吃 ×1.03 → 82.4kg
        Assert.Equal(82.4, Loadout.CarryLimit(1.0, SamPerk.CarryCapacityMultiplier(3, isSam: false)), 6);
    }

    // ---------- 通则：百分比加成一律**乘算**，作用于当前实际值（残疾不被加成补偿） ----------

    [Fact]
    public void OperationAura_IsMultiplicative_NotAdditive()
    {
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);

        // 缺小拇指 + 无名指（−7%/指）→ 操作能力 0.86。光环是 **×1.03**，不是 **+0.03**。
        const double twoFingersGone = 0.86;
        Assert.Equal(0.86 * 1.03, SamPerk.OperationCapabilityWithAura(twoFingersGone, l3), 9); // = 0.8858
        Assert.NotEqual(0.89, SamPerk.OperationCapabilityWithAura(twoFingersGone, l3), 6);     // 加算会是 0.89 —— 把残缺补回去了
    }

    [Fact]
    public void OperationAura_HandlessMan_StaysAtZero()
    {
        // 用户口径的硬核心：一个手全没了的人，操作能力就是 0。乘算下 0 × 1.03 = 0，
        // 加算会让他凭空有 3% 操作能力 —— 荒谬。这条断言就是那道防线。
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);
        Assert.Equal(0.0, SamPerk.OperationCapabilityWithAura(0.0, l3), 9);
    }

    [Fact]
    public void SamsOwnAura_DoesNotCompensateHisOwnMissingFingers()
    {
        // 山姆自己缺两指（九岁救诺蒂被野狗咬掉）。他给全营的 3% 光环，对他自己也只能在**折损后的基数**上乘。
        // 英雄有代价，代价不该被自己的光环抹掉：加成后仍**严格低于**一个健全人的裸操作能力。
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);
        double samWithAura = SamPerk.OperationCapabilityWithAura(0.86, l3);
        Assert.True(samWithAura < 1.0, "缺两指的山姆即便吃满自己的光环，也不该达到健全人水平");
        Assert.Equal(0.8858, samWithAura, 4);
    }

    [Fact]
    public void OperationAura_HealthyMan_GetsFullThreePercent()
    {
        int l3 = SamPerk.EvaluateLevel(SamPerk.Level3CampPopulation, samAlive: true);
        Assert.Equal(1.03, SamPerk.OperationCapabilityWithAura(1.0, l3), 9); // 健全人 1.0 × 1.03
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

    // ---------- 工时：干活效率 +3%（制作 / 建造 / 挖掘同一条工时轴） ----------

    [Fact]
    public void WorkSpeed_Aura_SpeedsUpCraftingAndDigging()
    {
        // 工时轴（CraftingJob / RubbleSite）吃的是"本次流逝分钟"，光环把流逝分钟放大 3%。
        // 调用方按现成的小数预算模式（CampMain._craftMinuteBudget）累积余数，此处只锁乘子语义：
        // 100 分钟的活，在 L3 光环下等效投入 103 分钟工时。
        double mult = SamPerk.CampWorkSpeedMultiplier(3);
        var job = new CraftingJob("test_recipe", totalWorkMinutes: 103);
        job.Advance((int)(100 * mult), canWork: true);
        Assert.True(job.IsComplete, "L3 光环下 100 分钟应推满 103 工时的活");

        var plain = new CraftingJob("test_recipe", totalWorkMinutes: 103);
        plain.Advance((int)(100 * SamPerk.CampWorkSpeedMultiplier(2)), canWork: true);
        Assert.False(plain.IsComplete, "无光环时 100 分钟推不满 103 工时");
    }

    // ---------- 健康：身体恢复 +3% / 感染条上升 −3% ----------

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
    public void Level3_Aura_SpeedsUpHealing()
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
            healSpeedMultiplier: SamPerk.CampHealSpeedMultiplier(3));
        double auraHeal = beforeAura - cs.Severity;

        Assert.True(plainHeal > 0);
        Assert.Equal(plainHeal * (1.0 + SamPerk.AuraHealSpeedBonus), auraHeal, 9); // 恢复速度 ×1.03
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
    public void Level3_Aura_SlowsInfectionWorsening()
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
        Assert.Equal(plainGain * (1.0 - SamPerk.AuraInfectionWorsenReduction), auraGain, 9); // 上升速度 ×0.97
    }

    [Fact]
    public void Level3_Aura_StacksWithMedicineWorsenMultiplier()
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

        Assert.Equal(medGain * (1.0 - SamPerk.AuraInfectionWorsenReduction), bothGain, 9);
    }
}
