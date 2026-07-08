using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 伤病随时间恶化 + 手术/用药 纯逻辑单测（无 Godot 依赖，Link 编入）。
/// 覆盖：出血拖久致死、开放伤口按几率感染、感染恶化→致残(肢体)/致死(躯干)；
/// **手术机制**（点数池=基础15+床10+材料，急救包独占，roll[0,池]=恢复效率，≤10失败，材料成功失败都耗）；
/// **恢复效率驱动愈合**（手术成功后流血/骨折按效率逐日愈合，未手术不自愈、出血继续恶化）；
/// 感染/疾病仍走药品 TreatIllness（抗生素/成药、医疗技能越高越有效、休养减缓恶化）；战斗态只读映射。
/// 数值皆"拟定待调 draft"，测试锁的是规则形态与当前拟定值。
/// </summary>
public class HealthConditionsTests
{
    // 让感染 roll 不触发的高值（Range(0,1) 返回 1 → 1 < 任何 chance(<1) 为假）。
    private static IRandomSource NoInfection(int ticks = 64)
        => new SequenceRandomSource(Enumerable.Repeat(1.0, ticks).ToArray());

    // 手术效率 roll（走 IRandomSource，值即效率）。
    private static IRandomSource Roll(double value) => new SequenceRandomSource(value);

    // 断言"手术不 roll"：空序列，一旦被取值即抛（证明 NotAllowed/定值段不消耗 rng）。
    private static IRandomSource NoRoll() => new SequenceRandomSource();

    // 建一个含单条伤的集，返回 (set, condition)。
    private static (HealthConditionSet set, HealthCondition c) SetWith(HealthCondition c)
    {
        var set = new HealthConditionSet();
        set.Add(c);
        return (set, c);
    }

    private static HealthCondition Bleed(double sev = 0.4) =>
        new(HealthConditionType.Bleeding, sev, "右手", onLimb: true);

    private static HealthCondition Frac(double sev = 0.6) =>
        new(HealthConditionType.Fracture, sev, "右大腿", onLimb: true);

    // ---- 出血：拖久恶化→失血过多致死（未手术不自愈）----

    [Fact]
    public void Bleeding_untreated_worsens_each_day_and_eventually_kills()
    {
        var (set, bleed) = SetWith(new HealthCondition(HealthConditionType.Bleeding, 0.2, "左上臂", onLimb: true));

        double last = bleed.Severity;
        bool died = false;
        for (int day = 0; day < 20 && !died; day++)
        {
            HealthTickResult r = set.TickDay(NoInfection(), resting: false);
            died = r.AnyDeath;
            if (!died)
            {
                Assert.True(bleed.Severity > last, "未处理出血应逐日加重");
                last = bleed.Severity;
            }
        }
        Assert.True(died, "出血拖到严重度封顶应失血过多致死");
        Assert.True(set.IsDead);
    }

    // ================= 手术点数池 =================

    [Fact]
    public void Surgery_barehanded_no_bed_pool_is_base_15()
    {
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, materials: null, onBed: false, Roll(13));
        Assert.Equal(15, r.PointPool);           // 徒手无床 = 基础 15
        Assert.True(r.Success);
        Assert.Equal(13, r.Efficiency);          // roll 13 → 效率 13%
        Assert.Empty(r.ConsumedMaterials);
    }

    [Fact]
    public void Surgery_on_bed_adds_10()
    {
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, materials: null, onBed: true, Roll(20));
        Assert.Equal(25, r.PointPool);           // 15 + 床 10
    }

    [Fact]
    public void Bleeding_bandage_and_needle_each_add_15_and_stack()
    {
        var (s1, c1) = SetWith(Bleed());
        Assert.Equal(30, s1.PerformSurgery(c1, new[] { "bandage" }, onBed: false, Roll(20)).PointPool);

        var (s2, c2) = SetWith(Bleed());
        Assert.Equal(30, s2.PerformSurgery(c2, new[] { "needle_thread" }, onBed: false, Roll(20)).PointPool);

        var (s3, c3) = SetWith(Bleed());
        Assert.Equal(45, s3.PerformSurgery(c3, new[] { "bandage", "needle_thread" }, onBed: false, Roll(20)).PointPool);

        var (s4, c4) = SetWith(Bleed());
        Assert.Equal(55, s4.PerformSurgery(c4, new[] { "bandage", "needle_thread" }, onBed: true, Roll(20)).PointPool);
    }

    [Fact]
    public void Fracture_splint_adds_25()
    {
        var (set, c) = SetWith(Frac());
        Assert.Equal(40, set.PerformSurgery(c, new[] { "splint" }, onBed: false, Roll(20)).PointPool);
    }

    [Fact]
    public void First_aid_kit_adds_60_for_both_wound_types()
    {
        var (sb, cb) = SetWith(Bleed());
        Assert.Equal(75, sb.PerformSurgery(cb, new[] { "first_aid_kit" }, onBed: false, Roll(50)).PointPool);

        var (sf, cf) = SetWith(Frac());
        Assert.Equal(75, sf.PerformSurgery(cf, new[] { "first_aid_kit" }, onBed: false, Roll(50)).PointPool);
    }

    [Fact]
    public void Non_applicable_materials_are_ignored_and_not_consumed()
    {
        // 夹板不适用于流血 → 忽略、不消耗、不加分。
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, new[] { "splint", "bandage" }, onBed: false, Roll(20));
        Assert.Equal(30, r.PointPool);                    // 只有 bandage 计入
        Assert.Equal(new[] { "bandage" }, r.ConsumedMaterials);
    }

    // ---- 急救包独占校验 ----

    [Fact]
    public void First_aid_kit_cannot_stack_with_bandage_on_bleeding()
    {
        var (set, c) = SetWith(Bleed());
        Assert.Throws<System.ArgumentException>(() =>
            set.PerformSurgery(c, new[] { "first_aid_kit", "bandage" }, onBed: false, Roll(50)));
    }

    [Fact]
    public void First_aid_kit_cannot_stack_with_splint_on_fracture()
    {
        var (set, c) = SetWith(Frac());
        Assert.Throws<System.ArgumentException>(() =>
            set.PerformSurgery(c, new[] { "first_aid_kit", "splint" }, onBed: false, Roll(50)));
    }

    [Fact]
    public void Surgery_rejects_infection_and_disease()
    {
        var set = new HealthConditionSet();
        var inf = new HealthCondition(HealthConditionType.Infection, 0.5, "右手", onLimb: true);
        set.Add(inf);
        Assert.Throws<System.ArgumentException>(() =>
            set.PerformSurgery(inf, new[] { "bandage" }, onBed: false, Roll(5)));
    }

    // ================= roll 边界：≤10 失败 =================

    [Fact]
    public void Roll_at_or_below_10_fails_and_does_not_operate()
    {
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(10)); // 池30，roll=10 → 失败
        Assert.False(r.Success);
        Assert.Equal(0, r.Efficiency);            // 失败不赋恢复效率
        Assert.Equal(10, r.Roll);
        Assert.False(c.IsOperated);               // 未进入愈合态
        Assert.Equal(0, c.RecoveryEfficiency);
    }

    [Fact]
    public void Roll_of_11_succeeds_with_efficiency_equal_to_roll()
    {
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(11));
        Assert.True(r.Success);
        Assert.Equal(11, r.Efficiency);
        Assert.True(c.IsOperated);
        Assert.Equal(11, c.RecoveryEfficiency);
    }

    [Fact]
    public void Roll_is_floored_to_integer_efficiency()
    {
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(13.9)); // 池30
        Assert.Equal(13, r.Efficiency); // 取整（下取）
    }

    // ================= 材料消耗（成功+失败都扣）=================

    [Fact]
    public void Materials_consumed_on_success()
    {
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage", "needle_thread" }, onBed: false, Roll(20));
        Assert.True(r.Success);
        Assert.Equal(new[] { "bandage", "needle_thread" }, r.ConsumedMaterials);
    }

    [Fact]
    public void Materials_consumed_on_failure_too()
    {
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage", "needle_thread" }, onBed: false, Roll(5)); // 失败
        Assert.False(r.Success);
        Assert.Equal(new[] { "bandage", "needle_thread" }, r.ConsumedMaterials); // 失败也照扣，重做需再取
    }

    // ================= 医疗书加点（施术者已读医疗书 → +治疗点，靠知识非技能）=================

    [Fact]
    public void Surgeon_book_bonus_adds_into_pool()
    {
        var (s0, c0) = SetWith(Bleed());
        Assert.Equal(15, s0.PerformSurgery(c0, materials: null, onBed: false, Roll(13), surgeonBookBonus: 0).PointPool);

        var (s1, c1) = SetWith(Bleed());
        SurgeryResult r = s1.PerformSurgery(c1, materials: null, onBed: false, Roll(13), surgeonBookBonus: 5); // 野外生存指南 +5
        Assert.Equal(20, r.PointPool); // 15 + 医疗书 5
        Assert.True(r.Success);
    }

    [Fact]
    public void Medical_book_registry_maps_book_ids_to_points()
    {
        Assert.Equal(5, MedicalBookPoints.For("wilderness_survival_guide")); // 《野外生存指南》draft +5
        Assert.True(MedicalBookPoints.IsMedicalBook("wilderness_survival_guide"));
        Assert.Equal(0, MedicalBookPoints.For("farmer_hundred_questions"));   // 非医疗书 0
        Assert.False(MedicalBookPoints.IsMedicalBook("farmer_hundred_questions"));
        Assert.Equal(0, MedicalBookPoints.For("does_not_exist"));
        Assert.Equal(0, MedicalBookPoints.For(null!));
        // 接入波：施术者已读书 ∩ 表 求和。
        Assert.Equal(5, MedicalBookPoints.SumFor(new[] { "wilderness_survival_guide", "farmer_hundred_questions" }));
        Assert.Equal(0, MedicalBookPoints.SumFor(new string[0]));
    }

    // ================= 自体手术 ×0.60 / 操作能力 ×cap / 叠乘 =================

    [Fact]
    public void Self_surgery_scales_pool_by_0_60()
    {
        // raw = 15 + 医疗书35 = 50；自体 ×0.60 → 30。
        var (self, cs) = SetWith(Bleed());
        Assert.Equal(30, self.PerformSurgery(cs, null, onBed: false, Roll(20), surgeonBookBonus: 35, selfSurgery: true).PointPool);

        var (other, co) = SetWith(Bleed());
        Assert.Equal(50, other.PerformSurgery(co, null, onBed: false, Roll(20), surgeonBookBonus: 35, selfSurgery: false).PointPool);
    }

    [Fact]
    public void Operation_capability_scales_pool()
    {
        // raw = 15 + 医疗书45 = 60；操作能力 0.5 → 30。
        var (half, ch) = SetWith(Bleed());
        Assert.Equal(30, half.PerformSurgery(ch, null, onBed: false, Roll(20), surgeonBookBonus: 45, operationCapability: 0.5).PointPool);

        var (full, cf) = SetWith(Bleed());
        Assert.Equal(60, full.PerformSurgery(cf, null, onBed: false, Roll(20), surgeonBookBonus: 45, operationCapability: 1.0).PointPool);
    }

    [Fact]
    public void Self_and_capability_multiply()
    {
        // raw = 15 + 医疗书35 = 50；×操作能力0.5 ×自体0.6 = 15（恰好过门槛）。
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, null, onBed: false, Roll(13),
            surgeonBookBonus: 35, selfSurgery: true, operationCapability: 0.5);
        Assert.Equal(15, r.PointPool); // 50 × 0.5 × 0.6 = 15
        Assert.True(r.Success);
    }

    // ================= P<15 门槛：不允许手术（零消耗零改动，文案模糊）=================

    [Fact]
    public void Self_surgery_without_materials_is_blocked_by_threshold()
    {
        // 死局修复：自体徒手无床 = 15 × 0.60 = 9 < 15 → 现状不支持。
        var (set, c) = SetWith(Bleed(0.5));
        SurgeryResult r = set.PerformSurgery(c, null, onBed: false, NoRoll(), selfSurgery: true);
        Assert.Equal(SurgeryStatus.NotAllowed, r.Status);
        Assert.Equal(9, r.PointPool);
        Assert.Equal("现状不支持进行这场手术", r.PlayerMessage);
        Assert.Equal(SurgeryResult.NotAllowedMessage, r.PlayerMessage);
        Assert.False(c.IsOperated);        // 不改病状
        Assert.Equal(0.5, c.Severity, 3);
    }

    [Fact]
    public void Below_threshold_consumes_no_materials_and_does_not_roll()
    {
        // 有材料但操作能力过低把池压到 <15：材料不消耗、不 roll、不改病状。
        var (set, c) = SetWith(Bleed(0.5));
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, NoRoll(), operationCapability: 0.1);
        Assert.Equal(SurgeryStatus.NotAllowed, r.Status);
        Assert.Equal(3, r.PointPool);      // (15+15)×0.1 = 3
        Assert.Empty(r.ConsumedMaterials); // 门槛未过零消耗，绷带保留
        Assert.False(c.IsOperated);
    }

    [Fact]
    public void Self_on_bed_reaches_threshold_exactly_15()
    {
        // 自体 + 床 = (15+10) × 0.60 = 15 恰好可做。
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, null, onBed: true, Roll(13), selfSurgery: true);
        Assert.Equal(15, r.PointPool);
        Assert.Equal(SurgeryStatus.Success, r.Status);
        Assert.Equal(13, r.Efficiency);
    }

    [Fact]
    public void Pool_of_14_is_not_allowed_15_is_the_floor()
    {
        // 操作能力 0.9 把徒手池 15 压到 round(13.5)=14 < 15 → 不允许。
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, null, onBed: false, NoRoll(), operationCapability: 0.9);
        Assert.Equal(14, r.PointPool);
        Assert.Equal(SurgeryStatus.NotAllowed, r.Status);
    }

    // ================= 分段 roll（可 >100%）=================

    [Fact]
    public void RollRange_segments_are_continuous()
    {
        Assert.Equal((0, 50), HealthConditionSet.RollRange(50));    // P≤100
        Assert.Equal((0, 100), HealthConditionSet.RollRange(100));  // 边界
        Assert.Equal((50, 100), HealthConditionSet.RollRange(150)); // 100<P≤200
        Assert.Equal((100, 100), HealthConditionSet.RollRange(200));// 退化定值，无方差
        Assert.Equal((100, 150), HealthConditionSet.RollRange(250));// P>200
        Assert.Equal((100, 200), HealthConditionSet.RollRange(300));
    }

    [Fact]
    public void Middle_segment_cannot_fail_even_at_minimum_roll()
    {
        // P=150 → roll∈[50,100]，下限 50 > 10，不可能失败（堆点数越稳）。
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, null, onBed: false, Roll(50), surgeonBookBonus: 135); // raw=150
        Assert.Equal(150, r.PointPool);
        Assert.True(r.Success);
        Assert.Equal(50, r.Efficiency);
    }

    [Fact]
    public void Pool_of_200_is_deterministic_efficiency_100_without_rolling()
    {
        // P=200 → [100,100] 定值：不消耗 rng（NoRoll 不被取值）。
        var (set, c) = SetWith(Bleed());
        SurgeryResult r = set.PerformSurgery(c, null, onBed: false, NoRoll(), surgeonBookBonus: 185); // raw=200
        Assert.Equal(200, r.PointPool);
        Assert.True(r.Success);
        Assert.Equal(100, r.Efficiency);
    }

    [Fact]
    public void High_segment_efficiency_can_exceed_100_and_is_not_clamped()
    {
        // P=250 → roll∈[100,150]；roll130 → 恢复效率 130%（不封顶 100）。
        var (set, c) = SetWith(Frac());
        SurgeryResult r = set.PerformSurgery(c, null, onBed: false, Roll(130), surgeonBookBonus: 235); // raw=250
        Assert.Equal(250, r.PointPool);
        Assert.True(r.Success);
        Assert.Equal(130, r.Efficiency);
        Assert.Equal(130, c.RecoveryEfficiency); // 病状记 130，未被 clamp 到 100
    }

    [Fact]
    public void Overload_efficiency_heals_faster_than_100()
    {
        var (over, co) = SetWith(Frac(0.8));
        over.PerformSurgery(co, null, onBed: false, Roll(130), surgeonBookBonus: 235); // eff 130
        over.TickDay(NoInfection(), resting: false);

        var (norm, cn) = SetWith(Frac(0.8));
        norm.PerformSurgery(cn, null, onBed: false, NoRoll(), surgeonBookBonus: 185); // P=200 → eff 100
        norm.TickDay(NoInfection(), resting: false);

        Assert.True(co.Severity < cn.Severity, "效率>100% 应比 100% 愈合更快");
    }

    // ================= 恢复效率驱动愈合 =================

    [Fact]
    public void Operated_bleeding_heals_over_days_and_is_removed()
    {
        var (set, c) = SetWith(Bleed(0.35));
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage", "needle_thread" }, onBed: true, Roll(40));
        Assert.True(r.Success);

        bool healed = false;
        for (int day = 0; day < 30 && !healed; day++)
        {
            set.TickDay(NoInfection(), resting: true);
            healed = !set.Conditions.Contains(c);
        }
        Assert.True(healed, "手术成功的出血应逐日愈合并移除");
        Assert.False(set.IsDead);
    }

    [Fact]
    public void Operated_bleeding_neither_worsens_nor_infects()
    {
        var (set, c) = SetWith(Bleed(0.5));
        set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(20)); // 效率20
        double before = c.Severity;
        // roll=0 若还会感染就会新增感染条；已手术应完全不 roll 感染、且 severity 下降。
        HealthTickResult tick = set.TickDay(new SequenceRandomSource(0.0), resting: false);
        Assert.True(c.Severity < before, "已手术出血应愈合而非恶化");
        Assert.DoesNotContain(set.Conditions, x => x.Type == HealthConditionType.Infection);
        Assert.DoesNotContain(tick.Events, e => e.ContractedInfection);
    }

    [Fact]
    public void Failed_surgery_leaves_bleeding_untreated_and_it_keeps_worsening()
    {
        var (set, c) = SetWith(Bleed(0.5));
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(5)); // 失败
        Assert.False(r.Success);

        double before = c.Severity;
        set.TickDay(NoInfection(), resting: false);
        Assert.True(c.Severity > before, "手术失败=未处置，出血继续恶化（需重做）");
    }

    [Fact]
    public void Operated_fracture_heals_and_is_removed()
    {
        var (set, c) = SetWith(Frac(0.8));
        set.PerformSurgery(c, new[] { "splint" }, onBed: true, Roll(30));
        bool healed = false;
        for (int day = 0; day < 40 && !healed; day++)
        {
            set.TickDay(NoInfection(), resting: true);
            healed = !set.Conditions.Contains(c);
        }
        Assert.True(healed, "手术成功的骨折应逐日愈合并移除");
    }

    [Fact]
    public void Fracture_left_unoperated_worsens_to_permanent_maim()
    {
        var (set, c) = SetWith(new HealthCondition(HealthConditionType.Fracture, 0.6, "左大腿", onLimb: true));
        var maimed = new List<string>();
        for (int day = 0; day < 40 && maimed.Count == 0; day++)
        {
            maimed.AddRange(set.TickDay(NoInfection(), resting: false).MaimedParts);
        }
        Assert.Contains("左大腿", maimed);
        Assert.False(set.IsDead, "骨折不致死");
    }

    [Fact]
    public void Higher_efficiency_heals_faster_than_lower()
    {
        // 高效率（急救包+床，池85，roll80）vs 低效率（夹板，池40，roll20），同起点单昼夜比较。
        var (hi, chi) = SetWith(Frac(0.8));
        hi.PerformSurgery(chi, new[] { "first_aid_kit" }, onBed: true, Roll(80));
        hi.TickDay(NoInfection(), resting: false);

        var (lo, clo) = SetWith(Frac(0.8));
        lo.PerformSurgery(clo, new[] { "splint" }, onBed: false, Roll(20));
        lo.TickDay(NoInfection(), resting: false);

        Assert.True(chi.Severity < clo.Severity, "恢复效率越高，单昼夜愈合越多");
    }

    // ================= 感染：开放伤口按几率感染，链式恶化（未手术）=================

    [Fact]
    public void Open_bleeding_wound_can_contract_infection_on_a_bad_roll()
    {
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Bleeding, 0.8, "左大腿", onLimb: true));

        var rng = new SequenceRandomSource(0.0); // roll=0 必命中感染
        HealthTickResult r = set.TickDay(rng, resting: false);

        Assert.Contains(r.Events, e => e.ContractedInfection);
        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Infection && c.BodyPart == "左大腿");
    }

    [Fact]
    public void Limb_infection_untreated_maims_the_limb_not_kills()
    {
        var set = new HealthConditionSet();
        var infection = new HealthCondition(HealthConditionType.Infection, 0.5, "左手", onLimb: true);
        set.Add(infection);

        var maimed = new List<string>();
        for (int day = 0; day < 20 && !set.IsDead && maimed.Count == 0; day++)
        {
            maimed.AddRange(set.TickDay(NoInfection(), resting: false).MaimedParts);
        }
        Assert.False(set.IsDead, "肢体感染封顶应致残而非致死");
        Assert.Contains("左手", maimed);
        Assert.DoesNotContain(infection, set.Conditions);
    }

    [Fact]
    public void Torso_infection_untreated_kills()
    {
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Infection, 0.5, "躯干", onLimb: false));

        bool died = false;
        for (int day = 0; day < 20 && !died; day++)
        {
            died = set.TickDay(NoInfection(), resting: false).AnyDeath;
        }
        Assert.True(died, "非肢体感染封顶应致死（败血症）");
        Assert.True(set.IsDead);
    }

    // ================= 感染/疾病：药品治疗路径（保留，与手术无关）=================

    [Fact]
    public void Antibiotics_reduce_infection_severity()
    {
        HealthCondition MakeInf() => new(HealthConditionType.Infection, 0.9, "右上臂", onLimb: true);
        Medicine? abx = MedicineCatalog.For("antibiotics");

        var set = new HealthConditionSet(); var ci = MakeInf(); set.Add(ci);
        double before = ci.Severity;
        set.TreatIllness(ci, abx);

        // 疗效固定基数（通用技能已删）：单次降 severity = Potency(0.5) × 0.8 = 0.4。
        Assert.True(ci.Severity < before, "抗生素降低感染 severity");
        Assert.Equal(before - 0.5 * 0.8, ci.Severity, 3);
    }

    [Fact]
    public void Medicine_cures_disease()
    {
        var set = new HealthConditionSet();
        var disease = new HealthCondition(HealthConditionType.Disease, 0.4, bodyPart: null, onLimb: false);
        set.Add(disease);
        TreatmentResult tr = set.TreatIllness(disease, MedicineCatalog.For("medicine"));
        Assert.Equal(TreatmentStatus.Cured, tr.Status); // 0.4 - 0.6*0.8 = -0.08 ≤ 0 → 治愈
        Assert.True(tr.Removed);
        Assert.DoesNotContain(disease, set.Conditions);
    }

    [Fact]
    public void Resting_slows_infection_progression()
    {
        HealthCondition MakeInf() => new(HealthConditionType.Infection, 0.3, "右上臂", onLimb: true);

        var rest = new HealthConditionSet(); var cr = MakeInf(); rest.Add(cr);
        rest.TickDay(NoInfection(), resting: true);

        var noRest = new HealthConditionSet(); var cn = MakeInf(); noRest.Add(cn);
        noRest.TickDay(NoInfection(), resting: false);

        Assert.True(cr.Severity < cn.Severity, "休养减缓感染恶化");
    }

    [Fact]
    public void TreatIllness_has_no_effect_on_wrong_type_or_on_bleeding()
    {
        var set = new HealthConditionSet();
        var infection = new HealthCondition(HealthConditionType.Infection, 0.5, "右手", onLimb: true);
        set.Add(infection);
        // 成药治不了感染。
        Assert.Equal(TreatmentStatus.NoEffect,
            set.TreatIllness(infection, MedicineCatalog.For("medicine")).Status);

        // 流血不吃药（走手术）：TreatIllness 对它无效。
        var bleed = Bleed(0.5); set.Add(bleed);
        Assert.Equal(TreatmentStatus.NoEffect,
            set.TreatIllness(bleed, MedicineCatalog.For("antibiotics")).Status);
        Assert.Equal(0.5, bleed.Severity, 3);
    }

    // ================= 目录：手术耗材 / 药品 =================

    [Fact]
    public void Surgery_catalog_maps_supplies_to_points_and_wound_types()
    {
        Assert.Equal(15, SurgeryCatalog.For("bandage")!.Value.Points);
        Assert.True(SurgeryCatalog.For("bandage")!.Value.CanTreat(HealthConditionType.Bleeding));
        Assert.Equal(15, SurgeryCatalog.For("needle_thread")!.Value.Points);
        Assert.Equal(25, SurgeryCatalog.For("splint")!.Value.Points);
        Assert.True(SurgeryCatalog.For("splint")!.Value.CanTreat(HealthConditionType.Fracture));

        SurgerySupply kit = SurgeryCatalog.For("first_aid_kit")!.Value;
        Assert.Equal(60, kit.Points);
        Assert.True(kit.Exclusive);
        Assert.True(kit.CanTreat(HealthConditionType.Bleeding));
        Assert.True(kit.CanTreat(HealthConditionType.Fracture));

        Assert.Null(SurgeryCatalog.For("antibiotics")); // 药品不是手术耗材
        Assert.Null(SurgeryCatalog.For("wood"));
    }

    [Fact]
    public void Medicine_catalog_only_covers_infection_and_disease()
    {
        Assert.Equal(HealthConditionType.Infection, MedicineCatalog.For("antibiotics")!.Value.Treats);
        Assert.Equal(HealthConditionType.Disease, MedicineCatalog.For("medicine")!.Value.Treats);
        Assert.Null(MedicineCatalog.For("bandage")); // 手术耗材不是药品
        Assert.Null(MedicineCatalog.For("splint"));
        Assert.Null(MedicineCatalog.For("wood"));
    }

    [Fact]
    public void Surgery_and_medicine_items_exist_in_materials_medical_category()
    {
        foreach (string key in new[] { "bandage", "needle_thread", "splint", "first_aid_kit", "antibiotics", "medicine" })
        {
            Assert.True(Materials.Has(key), $"医疗物品缺失：{key}");
            Assert.Equal(MaterialCategory.Medical, Materials.Find(key)!.Value.Category);
        }
    }

    // ================= 与战斗既有状态的只读映射 =================

    [Fact]
    public void SeedFromBody_reads_bleeding_and_fracture_states_without_mutating_body()
    {
        Body body = HumanBody.NewBody();
        body.RegisterBleed("左上臂");
        body.MarkFractured("右大腿");

        HealthConditionSet set = HealthMapping.SeedFromBody(body);

        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Bleeding && c.BodyPart == "左上臂" && c.OnLimb);
        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Fracture && c.BodyPart == "右大腿" && c.OnLimb);
        Assert.Contains("左上臂", body.BleedingWounds);
        Assert.True(body.IsFractured("右大腿"));
    }

    [Fact]
    public void SeedFromBody_marks_vital_parts_as_not_on_limb()
    {
        Body body = HumanBody.NewBody();
        body.RegisterBleed("躯干");
        HealthConditionSet set = HealthMapping.SeedFromBody(body);
        HealthCondition c = set.Conditions.Single();
        Assert.False(c.OnLimb);
    }
}
