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

    // ================= 手术即刻 5% 恢复 + 隔日可重做（重 roll 覆盖）=================

    [Fact]
    public void Successful_surgery_grants_immediate_5pct_heal()
    {
        var (set, c) = SetWith(Frac(0.6));
        set.PerformSurgery(c, new[] { "splint" }, onBed: true, Roll(30)); // 池50 成功
        Assert.Equal(0.55, c.Severity, 3); // 0.60 - 即刻5% = 0.55（未经 TickDay）
    }

    [Fact]
    public void Barely_successful_surgery_also_grants_immediate_5pct()
    {
        var (set, c) = SetWith(Bleed(0.5)); // 右手，池30(bandage)
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(11)); // 擦边成功(roll11>10)
        Assert.True(r.Success);
        Assert.Equal(11, r.Efficiency);
        Assert.Equal(0.45, c.Severity, 3); // 擦边成功也拿即刻5%
    }

    [Fact]
    public void Failed_first_surgery_grants_no_immediate_heal()
    {
        var (set, c) = SetWith(Bleed(0.5));
        SurgeryResult r = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(5)); // 失败
        Assert.False(r.Success);
        Assert.Equal(0.5, c.Severity, 3); // 失败无即刻恢复
    }

    [Fact]
    public void Redo_surgery_blocked_within_one_day()
    {
        var (set, c) = SetWith(Bleed(0.5));
        set.PerformSurgery(c, new[] { "bandage", "needle_thread" }, onBed: true, Roll(20)); // 首次成功 eff20
        Assert.False(set.CanRedoSurgery(c)); // 同日不可重做

        set.TickDay(NoInfection(), resting: false); // 距上次手术 1 昼夜（=一天，未超过）
        Assert.False(set.CanRedoSurgery(c));
        SurgeryResult tooSoon = set.PerformSurgery(c, new[] { "bandage" }, onBed: false, NoRoll(), /*surgeonBookBonus*/0);
        Assert.Equal(SurgeryStatus.RedoTooSoon, tooSoon.Status);
        Assert.Empty(tooSoon.ConsumedMaterials);      // 冷却内零消耗
        Assert.Equal(20, c.RecoveryEfficiency);       // 效率不变
    }

    [Fact]
    public void Redo_surgery_after_more_than_one_day_overwrites_efficiency_both_directions()
    {
        // 低→高覆盖
        var (s1, c1) = SetWith(Bleed(0.5));
        s1.PerformSurgery(c1, new[] { "bandage", "needle_thread" }, onBed: true, Roll(12)); // eff12（擦边）
        s1.TickDay(NoInfection(), resting: false);
        s1.TickDay(NoInfection(), resting: false);   // 距上次手术 2 昼夜 > 1
        Assert.True(s1.CanRedoSurgery(c1));
        SurgeryResult up = s1.PerformSurgery(c1, new[] { "bandage", "needle_thread" }, onBed: true, Roll(50));
        Assert.True(up.Success);
        Assert.Equal(50, c1.RecoveryEfficiency);      // 覆盖为高值

        // 高→低覆盖（双向风险）
        var (s2, c2) = SetWith(Bleed(0.5));
        s2.PerformSurgery(c2, new[] { "bandage", "needle_thread" }, onBed: true, Roll(50)); // eff50
        s2.TickDay(NoInfection(), resting: false);
        s2.TickDay(NoInfection(), resting: false);
        s2.PerformSurgery(c2, new[] { "bandage", "needle_thread" }, onBed: true, Roll(12)); // 重做刷到低值
        Assert.Equal(12, c2.RecoveryEfficiency);      // 高值被刷掉
    }

    [Fact]
    public void Redo_surgery_failure_keeps_old_efficiency_and_wastes_materials()
    {
        var (set, c) = SetWith(Bleed(0.5));
        set.PerformSurgery(c, new[] { "bandage", "needle_thread" }, onBed: true, Roll(40)); // eff40
        set.TickDay(NoInfection(), resting: false);
        set.TickDay(NoInfection(), resting: false);
        double sevBefore = c.Severity;
        SurgeryResult redo = set.PerformSurgery(c, new[] { "bandage", "needle_thread" }, onBed: true, Roll(5)); // 重做失败
        Assert.Equal(SurgeryStatus.Failed, redo.Status);
        Assert.Equal(40, c.RecoveryEfficiency);       // 保守：保留旧愈合效率
        Assert.Equal(sevBefore, c.Severity, 3);       // 不破坏闭口进度、无即刻5%
        Assert.Equal(new[] { "bandage", "needle_thread" }, redo.ConsumedMaterials); // 材料白费
    }

    // ---- 睡床 +10pp 加算恢复速度（与即刻5%独立叠加）----

    [Fact]
    public void Sleeping_in_bed_adds_10_percentage_points_additively_to_heal()
    {
        var (bed, cb) = SetWith(Bleed(0.5));
        bed.PerformSurgery(cb, new[] { "bandage" }, onBed: false, Roll(30)); // eff30，即刻5% → 0.45
        bed.TickDay(NoInfection(), resting: true, restedInBed: true);

        var (floor, cf) = SetWith(Bleed(0.5));
        floor.PerformSurgery(cf, new[] { "bandage" }, onBed: false, Roll(30)); // eff30 → 0.45
        floor.TickDay(NoInfection(), resting: true, restedInBed: false);

        Assert.True(cb.Severity < cf.Severity, "睡床应比不睡床愈合更快");
        // 加算(非乘算)：差 = BleedHealPerDay(0.20) × (10/100) × RestHealBonus(1.5) = 0.03
        Assert.Equal(0.03, cf.Severity - cb.Severity, 3);
    }

    [Fact]
    public void Bed_bonus_and_immediate_5pct_stack_exactly()
    {
        var (set, c) = SetWith(Bleed(0.5));
        set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(30)); // 即刻5% → 0.45
        set.TickDay(NoInfection(), resting: true, restedInBed: true);
        // 0.50 - 即刻5%(0.05) - 睡床愈合[0.20×((30+10)/100)×1.5=0.12] = 0.33
        Assert.Equal(0.33, c.Severity, 3);
    }

    [Fact]
    public void Not_in_bed_gets_no_bonus_default_behavior_unchanged()
    {
        var (set, c) = SetWith(Bleed(0.5));
        set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(30)); // → 0.45
        set.TickDay(NoInfection(), resting: true); // restedInBed 默认 false
        // 0.45 - [0.20×(30/100)×1.5=0.09] = 0.36（无睡床加算）
        Assert.Equal(0.36, c.Severity, 3);
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
    public void Operated_bleeding_heals_not_worsens()
    {
        var (set, c) = SetWith(Bleed(0.5));
        set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(20)); // 效率20
        double before = c.Severity;
        // 止血=不再失血恶化：severity 下降（用 NoInfection 排除感染扰动）。
        set.TickDay(NoInfection(), resting: false);
        Assert.True(c.Severity < before, "已手术出血应愈合而非恶化");
    }

    // ---- 止血≠无菌：已手术伤口在"未愈合闭口"窗口内仍保留感染窗（降率）----

    [Fact]
    public void Operated_bleeding_still_infects_within_open_wound_window()
    {
        // 术后 severity 仍高于闭口阈值 → 止血≠无菌，坏运气仍会感染（roll=0 必中）。
        var (set, c) = SetWith(Bleed(0.5));
        set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(20)); // 效率20，术后仍开口
        HealthTickResult tick = set.TickDay(new SequenceRandomSource(0.0), resting: false);
        Assert.Contains(tick.Events, e => e.ContractedInfection);
        Assert.Contains(set.Conditions, x => x.Type == HealthConditionType.Infection && x.BodyPart == c.BodyPart);
    }

    [Fact]
    public void Operated_bleeding_below_closed_threshold_no_longer_infects()
    {
        // 已愈合到闭口阈值以下 → 伤口封闭，不再有感染窗（roll=0 也不中）。
        var (set, c) = SetWith(Bleed(0.08)); // 低于闭口阈值 0.15
        set.PerformSurgery(c, new[] { "bandage" }, onBed: false, Roll(25)); // 池30 → roll25 成功
        HealthTickResult tick = set.TickDay(new SequenceRandomSource(0.0), resting: false);
        Assert.DoesNotContain(tick.Events, e => e.ContractedInfection);
        Assert.DoesNotContain(set.Conditions, x => x.Type == HealthConditionType.Infection);
    }

    // ---- 部分伤口只感染不致命失血（小锐器/咬伤类）----

    [Fact]
    public void Minor_bleed_wound_never_bleeds_to_death()
    {
        // 非致命失血伤口（LethalBleed=false）：拖再久也不失血死，severity 封顶在上限之下。
        var (set, c) = SetWith(new HealthCondition(
            HealthConditionType.Bleeding, 0.35, "右手", onLimb: true, lethalBleed: false));
        for (int day = 0; day < 30; day++)
        {
            HealthTickResult r = set.TickDay(NoInfection(), resting: false);
            Assert.False(r.AnyDeath, "非致命失血伤口不应失血死");
        }
        Assert.False(set.IsDead);
        Assert.True(c.Severity < 1.0, "非致命失血伤口 severity 应封顶在 1.0 之下");
    }

    [Fact]
    public void Minor_bleed_wound_untreated_festers_into_infection_then_kills_if_ignored()
    {
        // 小咬伤不失血死，但若放任 → 感染（TickDay 新生）→ 竞速封顶**致死**（补7：放任不治终究死，肢体也不自动截肢）。
        var (set, c) = SetWith(new HealthCondition(
            HealthConditionType.Bleeding, 0.35, "右手", onLimb: true, lethalBleed: false));
        var alwaysInfect = new SequenceRandomSource(Enumerable.Repeat(0.0, 40).ToArray());
        // 先在感染窗内新生感染。
        for (int day = 0; day < HealthConditionSet.InfectionWindowDays && !set.Has(HealthConditionType.Infection); day++)
            set.TickDay(alwaysInfect, resting: false);
        Assert.Contains(set.Conditions, x => x.Type == HealthConditionType.Infection && x.BodyPart == "右手");
        // 再放任感染竞速到封顶致死。
        InfectionRaceResult rr = default;
        for (int day = 0; day < 20 && rr.Outcome == ConditionOutcome.None && !rr.Cured; day++)
            rr = set.AdvanceInfectionRace(1.0, medicated: false, medicine: null);
        Assert.Equal(ConditionOutcome.Death, rr.Outcome);
        Assert.True(set.IsDead, "放任感染不治终究致死（保留狠辣，别软化）");
    }

    // ---- 播种分类：小部位=只感染不致命、大部位=致命失血 ----

    [Fact]
    public void SeedFromBody_classifies_small_parts_as_minor_and_large_as_lethal_bleed()
    {
        Body body = HumanBody.NewBody();
        body.RegisterBleed("左大腿"); // 大部位 → 致命失血
        body.RegisterBleed("右手");   // 小部位（咬伤/小锐器类）→ 只感染不致命
        body.RegisterBleed("躯干");   // 要害 → 致命失血

        HealthConditionSet set = HealthMapping.SeedFromBody(body);

        Assert.True(set.Conditions.Single(c => c.BodyPart == "左大腿").LethalBleed);
        Assert.False(set.Conditions.Single(c => c.BodyPart == "右手").LethalBleed);
        Assert.True(set.Conditions.Single(c => c.BodyPart == "躯干").LethalBleed);
    }

    // ================= 医疗三调（失血减慢 / 感染按伤口大小博弈 / 愈合提速）=================

    // ---- 1 失血减慢：致命失血伤口死亡时间线适度后移（仍必死）----

    [Fact]
    public void Lethal_bleed_death_timeline_is_slower_but_still_certain()
    {
        var (set, c) = SetWith(new HealthCondition(HealthConditionType.Bleeding, 0.35, "左大腿", onLimb: true));
        // 放宽操作窗：第 5 昼夜不应就死（旧 0.15/日在此已死）。
        for (int day = 0; day < 5; day++)
        {
            set.TickDay(NoInfection(), resting: false);
        }
        Assert.False(set.IsDead, "失血减慢后，致命伤第 5 昼夜不应已失血死（操作窗放宽）");
        // 但仍必死：继续拖到封顶。
        for (int day = 0; day < 10 && !set.IsDead; day++)
        {
            set.TickDay(NoInfection(), resting: false);
        }
        Assert.True(set.IsDead, "致命失血伤口终究失血致死，狠度不降");
    }

    // ---- 2 感染按伤口大小博弈：越小越不易感染 + 感染窗口期后闭合 ----

    [Fact]
    public void Infection_proneness_scales_down_with_wound_size()
    {
        Body body = HumanBody.NewBody();
        body.RegisterBleed("左大腿");     // 大部位
        body.RegisterBleed("右手");       // 中（手掌）
        body.RegisterBleed("右手食指");   // 微（指）
        HealthConditionSet set = HealthMapping.SeedFromBody(body);

        double leg = set.Conditions.Single(c => c.BodyPart == "左大腿").InfectionProneness;
        double hand = set.Conditions.Single(c => c.BodyPart == "右手").InfectionProneness;
        double finger = set.Conditions.Single(c => c.BodyPart == "右手食指").InfectionProneness;

        Assert.True(leg > hand, "大伤口比手部更易感染");
        Assert.True(hand > finger, "手部比指部更易感染（越小越不易）");
        Assert.True(finger > 0, "微伤仍有非零感染几率（值得赌但会翻车）");
    }

    [Fact]
    public void Open_wound_infection_window_closes_after_a_few_days()
    {
        // 小伤放任不再 100% 坏疽：感染仅限伤口新鲜期(InfectionWindowDays)内，窗口过后即使坏运气也不再感染。
        var (set, c) = SetWith(new HealthCondition(
            HealthConditionType.Bleeding, 0.35, "右手", onLimb: true, lethalBleed: false));
        // 撑过整个感染窗（用 NoInfection 保证窗内未中招）。
        for (int day = 0; day < HealthConditionSet.InfectionWindowDays; day++)
        {
            set.TickDay(NoInfection(), resting: false);
        }
        // 窗口已闭：即便 roll=0 也不再感染。
        HealthTickResult after = set.TickDay(new SequenceRandomSource(0.0), resting: false);
        Assert.DoesNotContain(after.Events, e => e.ContractedInfection);
        Assert.DoesNotContain(set.Conditions, x => x.Type == HealthConditionType.Infection);
    }

    // ---- 3 愈合提速：骨折治疗后愈合明显更快 ----

    // ---- 微小伤自愈（仅"很小的伤"：微小部位/擦伤级）：新鲜期内低概率感染，窗口结束自行闭合 ----

    [Fact]
    public void Tiny_wound_self_heals_after_window_when_uninfected()
    {
        // 微小伤（selfHealing）：撑过感染窗未中招 → 自行闭合、无成本移除（赌赢）。
        var (set, c) = SetWith(new HealthCondition(
            HealthConditionType.Bleeding, 0.35, "右手食指", onLimb: true, lethalBleed: false, selfHealing: true));
        bool removed = false;
        for (int day = 0; day <= HealthConditionSet.InfectionWindowDays && !removed; day++)
        {
            set.TickDay(NoInfection(), resting: false);
            removed = !set.Conditions.Contains(c);
        }
        Assert.True(removed, "微小伤新鲜期结束应自行闭合移除");
        Assert.False(set.IsDead);
        Assert.DoesNotContain(set.Conditions, x => x.Type == HealthConditionType.Infection);
    }

    [Fact]
    public void Tiny_wound_that_got_infected_self_closes_but_infection_persists()
    {
        // 微小伤赌输：新鲜期内中招 → 伤口仍自行闭合，但感染作为独立病状继续（要吃抗生素/否则坏疽）。
        var (set, c) = SetWith(new HealthCondition(
            HealthConditionType.Bleeding, 0.35, "右手食指", onLimb: true, lethalBleed: false, selfHealing: true));
        var alwaysInfect = new SequenceRandomSource(Enumerable.Repeat(0.0, 20).ToArray());
        // resting 放缓感染，使伤口自愈闭合时感染尚未封顶（否则同 tick 截肢会连带清掉感染，掩盖"感染独立续走"）。
        for (int day = 0; day <= HealthConditionSet.InfectionWindowDays; day++)
        {
            set.TickDay(alwaysInfect, resting: true);
        }
        Assert.DoesNotContain(c, set.Conditions); // 微小伤已自行闭合
        Assert.Contains(set.Conditions, x => x.Type == HealthConditionType.Infection && x.BodyPart == "右手食指");
    }

    [Fact]
    public void Self_healed_wound_gets_synced_to_body_via_presence_stopbleed()
    {
        // 接入波(Pawn.AdvanceHealthDay)以"该部位已无活跃出血条目"作**单一路径**从 Body 止血（手术/自愈/截肢清理同走此路）。
        // 此处验证该契约：微小伤自愈后 Health 不再有该出血条目 → 同款判定即可把 Body 侧出血止住。
        Body body = HumanBody.NewBody();
        body.RegisterBleed("右手食指");
        HealthConditionSet set = HealthMapping.SeedFromBody(body); // 微小部位 → 自愈档
        for (int day = 0; day <= HealthConditionSet.InfectionWindowDays; day++)
        {
            set.TickDay(NoInfection(), resting: false);
        }

        // 复刻接入波的存在性同步判定（Pawn.cs:AdvanceHealthDay）。
        foreach (string part in body.BleedingWounds.ToList())
        {
            if (!set.Conditions.Any(c => c.Type == HealthConditionType.Bleeding && c.BodyPart == part))
            {
                body.StopBleed(part);
            }
        }

        Assert.DoesNotContain("右手食指", body.BleedingWounds);
    }

    [Fact]
    public void Medium_small_wound_does_not_self_heal_must_be_operated()
    {
        // 中等非致命小伤（手/脚一般咬伤）不自愈：撑过感染窗后仍在，必须手术闭口。
        var (set, c) = SetWith(new HealthCondition(
            HealthConditionType.Bleeding, 0.35, "右手", onLimb: true, lethalBleed: false, selfHealing: false));
        for (int day = 0; day < HealthConditionSet.InfectionWindowDays + 3; day++)
        {
            set.TickDay(NoInfection(), resting: false);
        }
        Assert.Contains(c, set.Conditions); // 未手术不自愈，伤口仍在
        Assert.False(set.IsDead);
    }

    [Fact]
    public void SeedFromBody_marks_only_very_small_or_abrasion_wounds_as_self_healing()
    {
        Body body = HumanBody.NewBody();
        body.RegisterBleed("右手食指"); // 微小部位 → 自愈
        body.RegisterBleed("右手");     // 手（中等小伤）→ 不自愈
        body.RegisterBleed("左大腿");   // 大部位 → 不自愈
        HealthConditionSet set = HealthMapping.SeedFromBody(body); // 默认 severity 0.35

        Assert.True(set.Conditions.Single(c => c.BodyPart == "右手食指").SelfHealing);
        Assert.False(set.Conditions.Single(c => c.BodyPart == "右手").SelfHealing);
        Assert.False(set.Conditions.Single(c => c.BodyPart == "左大腿").SelfHealing);

        // 擦伤级（低初始严重度）在大部位上也自愈。
        Body grazed = HumanBody.NewBody();
        grazed.RegisterBleed("左大腿");
        HealthConditionSet low = HealthMapping.SeedFromBody(grazed, bleedingSeverity: 0.1);
        Assert.True(low.Conditions.Single().SelfHealing, "擦伤级低严重度伤口应自愈");
    }

    // ---- 骨折治疗档回写 Body：三态（未治 -30% / 术后 -15% / 痊愈 0）端到端到 Body 能力系数 ----

    // 复刻接入波(Pawn.AdvanceHealthDay)的骨折回写：手术成功→MarkFractureTreated；愈合清除→HealFracture（存在性扫描）。
    private static void SyncFractureToBody(HealthConditionSet set, Body body)
    {
        foreach (HealthCondition c in set.Conditions)
        {
            if (c.Type == HealthConditionType.Fracture && c.BodyPart != null && c.IsOperated)
            {
                body.MarkFractureTreated(c.BodyPart);
            }
        }
        foreach (string part in body.FracturedParts.ToList())
        {
            if (!set.Conditions.Any(c => c.Type == HealthConditionType.Fracture && c.BodyPart == part))
            {
                body.HealFracture(part);
            }
        }
    }

    [Fact]
    public void Fracture_treatment_tristate_flows_to_body_capability()
    {
        // untreatedMult 0.7(-30%) / treatedMult 0.85(-15%) / floor 0.1；愈合后无骨折 = 1.0(0%)。
        Body body = HumanBody.NewBody();
        body.MarkFractured("右手"); // 手骨折（Region==Hand 计入操作能力系数）
        HealthConditionSet set = HealthMapping.SeedFromBody(body);
        HealthCondition frac = set.Conditions.Single(c => c.Type == HealthConditionType.Fracture && c.BodyPart == "右手");

        // ① 未治：-30%
        SyncFractureToBody(set, body);
        Assert.True(body.IsFractured("右手"));
        Assert.False(body.IsFractureTreated("右手"));
        Assert.Equal(0.7, body.HandFractureOperationFactor(0.7, 0.85, 0.1), 3);

        // ② 手术成功 → 接入波 MarkFractureTreated：-15%
        SurgeryResult r = set.PerformSurgery(frac, new[] { "splint" }, onBed: true, Roll(30));
        Assert.True(r.Success);
        SyncFractureToBody(set, body);
        Assert.True(body.IsFractureTreated("右手"));
        Assert.Equal(0.85, body.HandFractureOperationFactor(0.7, 0.85, 0.1), 3);

        // ③ 康复完成 → 接入波 HealFracture：0%
        for (int day = 0; day < 40 && set.Conditions.Contains(frac); day++)
        {
            set.TickDay(NoInfection(), resting: true);
            SyncFractureToBody(set, body);
        }
        Assert.False(body.IsFractured("右手"));
        Assert.Equal(1.0, body.HandFractureOperationFactor(0.7, 0.85, 0.1), 3);
    }

    [Fact]
    public void Operated_fracture_heals_noticeably_faster()
    {
        // 骨折(seed0.6) 夹板在床(池40) roll30=效率30 + 卧床：应在 8 昼夜内愈合（旧 0.12/日需 ~11 昼夜，红）。
        var (set, c) = SetWith(Frac(0.6));
        SurgeryResult r = set.PerformSurgery(c, new[] { "splint" }, onBed: true, Roll(30));
        Assert.True(r.Success);
        bool healed = false;
        for (int day = 0; day < 8 && !healed; day++)
        {
            set.TickDay(NoInfection(), resting: true);
            healed = !set.Conditions.Contains(c);
        }
        Assert.True(healed, "愈合提速后，齐装+卧床骨折应在 8 昼夜内愈合");
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
    public void Limb_infection_untreated_kills_unless_amputated()
    {
        // [SPEC-B14-补7] 肢体感染放任到 100% 也**致死**（不再自动截肢）；玩家须主动截肢才能保命。
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Infection, 0.5, "左手", onLimb: true));
        InfectionRaceResult rr = default;
        for (int day = 0; day < 20 && rr.Outcome == ConditionOutcome.None && !rr.Cured; day++)
            rr = set.AdvanceInfectionRace(1.0, medicated: false, medicine: null);
        Assert.Equal(ConditionOutcome.Death, rr.Outcome);
        Assert.True(set.IsDead, "肢体感染放任封顶应致死（补7 取消自动截肢）");
    }

    [Fact]
    public void Torso_infection_untreated_kills()
    {
        // [SPEC-B14/终稿] 非肢体感染封顶=败血症致死（走 AdvanceInfectionRace）。
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Infection, 0.5, "躯干", onLimb: false));
        InfectionRaceResult rr = default;
        for (int day = 0; day < 20 && rr.Outcome == ConditionOutcome.None && !rr.Cured; day++)
            rr = set.AdvanceInfectionRace(1.0, medicated: false, medicine: null);
        Assert.Equal(ConditionOutcome.Death, rr.Outcome);
        Assert.True(set.IsDead);
    }

    // ================= 疾病：药品治疗路径（感染改竞速后此处仅疾病）=================

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

    // 注：旧 Resting_slows_infection_progression 已随补3独立竞速删除（感染进度不再受休养影响，
    // 替代断言见 Resting_no_longer_slows_infection_progress）。疾病休养减缓仍保留（下方 Disease 相关）。

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

    // ============ [SPEC-B14/终稿·三档双效] 感染双进度竞速：治疗效率 + 恶化减缓 ============
    // 内核：Severity=感染/死亡进度、CureProgress=治疗进度；每时间片 dt 天推进 AdvanceInfectionRace。
    // 用药期间：感染进度按档 ×WorsenMultiplier 减缓、治疗进度按 Efficacy×基准速率 累进；先到 1.0 者胜。全程 double 不取整。

    private static HealthCondition FreshInfection(double progress = 0.0, string part = "右上臂", bool onLimb = true)
        => new(HealthConditionType.Infection, progress, part, onLimb);

    // 一整日(dt=1)推进感染竞速：medKey=null 为未用药。返回 (天数, 是否治愈, 是否输(死/残))。
    private static (int days, bool cured, bool lost) RunDailyRace(HealthConditionSet set, string? medKey, int maxDays = 20)
    {
        for (int d = 1; d <= maxDays; d++)
        {
            Medicine? med = medKey is null ? null : MedicineCatalog.For(medKey);
            InfectionRaceResult rr = set.AdvanceInfectionRace(1.0, medKey is not null, med);
            if (rr.Cured) return (d, true, false);
            if (rr.Outcome is ConditionOutcome.Death or ConditionOutcome.Maim) return (d, false, true);
        }
        return (maxDays, false, false);
    }

    // 三档双效目录值（用户原话非拟定）：治疗效率 100/35/15，恶化减缓 ×0.50/0.75/0.85。
    [Fact]
    public void Infection_remedies_have_dual_effect_catalogue_values()
    {
        Medicine abx = MedicineCatalog.For("antibiotics")!.Value;
        Medicine salve = MedicineCatalog.For("herbal_salve")!.Value;
        Medicine tea = MedicineCatalog.For("dandelion_tea")!.Value;
        Assert.Equal(1.00, abx.Efficacy, 3); Assert.Equal(0.50, abx.WorsenMultiplier, 3);
        Assert.Equal(0.35, salve.Efficacy, 3); Assert.Equal(0.75, salve.WorsenMultiplier, 3);
        Assert.Equal(0.15, tea.Efficacy, 3); Assert.Equal(0.85, tea.WorsenMultiplier, 3);
    }

    // 未用药一时间片：感染进度按满速累积、无治疗进度。
    [Fact]
    public void Untreated_race_step_accrues_infection_at_full_rate_no_cure()
    {
        double ri = 1.0 / 6.0;
        var set = new HealthConditionSet(); var inf = FreshInfection(0.30); set.Add(inf);
        set.AdvanceInfectionRace(1.0, medicated: false, medicine: null);
        Assert.Equal(0.30 + ri, inf.Severity, 6); // 满速
        Assert.Equal(0.0, inf.CureProgress, 6);
    }

    // 用药一时间片：感染进度按档减缓、治疗进度按效率累进（抗生素 ×0.50 减缓 + 1.00 效率）。
    [Fact]
    public void Medicated_race_step_slows_worsening_and_builds_cure_by_tier()
    {
        double ri = 1.0 / 6.0, rate = HealthConditionSet.CureProgressBaseRate;
        var set = new HealthConditionSet(); var inf = FreshInfection(0.30); set.Add(inf);
        set.AdvanceInfectionRace(1.0, medicated: true, MedicineCatalog.For("antibiotics"));
        Assert.Equal(0.30 + ri * 0.50, inf.Severity, 6); // 恶化 ×0.50
        Assert.Equal(1.00 * rate, inf.CureProgress, 6);  // 治疗 1.00×基准
    }

    // 恶化减缓按档单调：抗生素(×0.50) 比 草药膏(×0.75) 比 蒲公英茶(×0.85) 压得更狠（用药日感染涨得更少）。
    [Fact]
    public void Worsening_slowdown_is_monotonic_by_tier()
    {
        double Step(string key)
        {
            var set = new HealthConditionSet(); var inf = FreshInfection(0.30); set.Add(inf);
            set.AdvanceInfectionRace(1.0, true, MedicineCatalog.For(key));
            return inf.Severity;
        }
        double abx = Step("antibiotics"), salve = Step("herbal_salve"), tea = Step("dandelion_tea"), none = 0.30 + 1.0 / 6.0;
        Assert.True(abx < salve && salve < tea && tea < none, "恶化减缓须 抗生素<草药膏<蒲公英茶<未用药");
    }

    // 治疗进度跨药持续累计（先茶垫场、后抗生素接力，进度不清零）。
    [Fact]
    public void Cure_progress_accumulates_across_different_medicines()
    {
        double rate = HealthConditionSet.CureProgressBaseRate;
        var set = new HealthConditionSet(); var inf = FreshInfection(0.10); set.Add(inf);
        set.AdvanceInfectionRace(1.0, true, MedicineCatalog.For("dandelion_tea"));  // +0.15×rate
        set.AdvanceInfectionRace(1.0, true, MedicineCatalog.For("antibiotics"));    // +1.00×rate，累计
        Assert.Equal((0.15 + 1.00) * rate, inf.CureProgress, 6);
    }

    // 不取整通则：连续 3 时间片茶治疗进度精确累计无截断（0.15×0.67×3）。
    [Fact]
    public void Cure_progress_keeps_full_decimal_precision_no_rounding()
    {
        double rate = HealthConditionSet.CureProgressBaseRate;
        var set = new HealthConditionSet(); var inf = FreshInfection(0.05); set.Add(inf);
        for (int i = 0; i < 3; i++) set.AdvanceInfectionRace(1.0, true, MedicineCatalog.For("dandelion_tea"));
        Assert.Equal(3 * 0.15 * rate, inf.CureProgress, 9); // 9 位精度：不得中途 round/floor
    }

    // 相位级=整日等价（不取整）：8 个 dt=1/8 片累计 == 1 个 dt=1 片（进度累积无粒度损失）。
    [Fact]
    public void Phase_level_steps_sum_equals_one_daily_step()
    {
        var daily = new HealthConditionSet(); var cd = FreshInfection(0.20); daily.Add(cd);
        daily.AdvanceInfectionRace(1.0, true, MedicineCatalog.For("herbal_salve"));

        var phased = new HealthConditionSet(); var cp = FreshInfection(0.20); phased.Add(cp);
        for (int i = 0; i < 8; i++) phased.AdvanceInfectionRace(1.0 / 8.0, true, MedicineCatalog.For("herbal_salve"));

        Assert.Equal(cd.Severity, cp.Severity, 6);
        Assert.Equal(cd.CureProgress, cp.CureProgress, 6);
    }

    // 治疗抢先：治疗进度先到顶 → 清除感染（Cured），条目移除。
    [Fact]
    public void Cure_reaching_full_clears_infection_treatment_wins_ties()
    {
        var set = new HealthConditionSet(); var inf = FreshInfection(0.0); set.Add(inf);
        var (days, cured, lost) = RunDailyRace(set, "antibiotics");
        Assert.True(cured && !lost);
        Assert.DoesNotContain(set.Conditions, c => c.Type == HealthConditionType.Infection);
    }

    // 锚点：抗生素从新鲜感染通常 1~2 天痊愈（用户原话）。
    [Fact]
    public void Antibiotics_cure_fresh_infection_within_one_to_two_days()
    {
        var set = new HealthConditionSet(); var inf = FreshInfection(0.0, "躯干", onLimb: false); set.Add(inf);
        var (days, cured, _) = RunDailyRace(set, "antibiotics");
        Assert.True(cured);
        Assert.InRange(days, 1, 2);
    }

    // 未用药约第 6 昼夜致死（T_i=6，与失血 7 天死线错开）。
    [Fact]
    public void Untreated_infection_kills_around_day_six()
    {
        var set = new HealthConditionSet(); var inf = FreshInfection(0.0, "躯干", onLimb: false); set.Add(inf);
        var (days, cured, lost) = RunDailyRace(set, null);
        Assert.True(lost && !cured);
        Assert.InRange(days, 5, 7);
    }

    // 草药膏须趁早：从新鲜感染开工能赢；感染已高(0.6)才开工则来不及，输。
    [Fact]
    public void Herbal_salve_wins_only_if_started_early()
    {
        var early = new HealthConditionSet(); early.Add(FreshInfection(0.0, "躯干", onLimb: false));
        var (_, curedEarly, _) = RunDailyRace(early, "herbal_salve");
        Assert.True(curedEarly, "草药膏从新鲜感染开工应赢");

        var late = new HealthConditionSet(); late.Add(FreshInfection(0.6, "躯干", onLimb: false));
        var (_, curedLate, lostLate) = RunDailyRace(late, "herbal_salve");
        Assert.True(!curedLate && lostLate, "草药膏在感染已高时开工来不及，应输");
    }

    // 蒲公英茶单用赢不了，但拖延死线（比未用药更晚死）并攒下可观治疗进度（混合策略垫场）。
    [Fact]
    public void Dandelion_tea_alone_loses_but_delays_death_and_banks_progress()
    {
        var untreated = new HealthConditionSet(); untreated.Add(FreshInfection(0.0, "躯干", onLimb: false));
        var (dNone, _, _) = RunDailyRace(untreated, null);

        var teaSet = new HealthConditionSet(); var teaInf = FreshInfection(0.0, "躯干", onLimb: false); teaSet.Add(teaInf);
        var (dTea, curedTea, lostTea) = RunDailyRace(teaSet, "dandelion_tea");
        Assert.True(!curedTea && lostTea, "纯茶赢不了");
        Assert.True(dTea > dNone, "茶应把死线拖得比未用药更晚");
    }

    // 混合策略成立：先茶 3 日垫场，再换抗生素接力 → 治愈（跨药累计的意义）。
    [Fact]
    public void Mixed_strategy_tea_then_antibiotics_wins()
    {
        var set = new HealthConditionSet(); var inf = FreshInfection(0.0, "躯干", onLimb: false); set.Add(inf);
        for (int d = 0; d < 3; d++) set.AdvanceInfectionRace(1.0, true, MedicineCatalog.For("dandelion_tea"));
        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Infection); // 3 日茶未死未愈
        var (_, cured, _) = RunDailyRace(set, "antibiotics");
        Assert.True(cured, "茶垫场后换抗生素应接力治愈");
    }

    // [SPEC-B14-补7] 感染到 100% **一律立刻死亡**（肢体不再自动坏疽截肢——想活得玩家主动截肢）。
    [Fact]
    public void Infection_terminal_kills_regardless_of_limb()
    {
        foreach (var (part, onLimb) in new[] { ("左大腿", true), ("躯干", false) })
        {
            var set = new HealthConditionSet(); set.Add(FreshInfection(0.0, part, onLimb));
            InfectionRaceResult rr = default;
            for (int d = 0; d < 12 && rr.Outcome == ConditionOutcome.None && !rr.Cured; d++)
                rr = set.AdvanceInfectionRace(1.0, false, null);
            Assert.Equal(ConditionOutcome.Death, rr.Outcome);
            Assert.NotEqual(ConditionOutcome.Maim, rr.Outcome); // 不再自动截肢
            Assert.True(set.IsDead);
        }
    }

    // [SPEC-B14-补7] 主动截肢手术（可选保命手段）：成功=移除感染部位+双条清零；调用方据此 Body.Sever。
    [Fact]
    public void Amputation_success_clears_infection_on_the_limb()
    {
        var set = new HealthConditionSet();
        var inf = new HealthCondition(HealthConditionType.Infection, 0.7, "左大腿", onLimb: true);
        set.Add(inf);
        // 池=基础15+床10+急救包60=85 → RollRange[0,85]；给 roll=50(>失败阈值10) → 成功。
        SurgeryResult r = set.PerformAmputation(inf, new[] { "first_aid_kit" }, onBed: true,
            new SequenceRandomSource(new[] { 50.0 }), operationCapability: 1.0);
        Assert.Equal(SurgeryStatus.Success, r.Status);
        Assert.DoesNotContain(set.Conditions, c => c.Type == HealthConditionType.Infection);
    }

    // 截肢失败：耗材照耗、感染保留（未中止竞速），需重来。
    [Fact]
    public void Amputation_failure_keeps_infection_and_consumes_materials()
    {
        var set = new HealthConditionSet();
        var inf = new HealthCondition(HealthConditionType.Infection, 0.7, "左大腿", onLimb: true);
        set.Add(inf);
        // 徒手(无耗材) + roll 落在失败区(≤10)。徒手池=15 → RollRange[0,15]，取 0 → 失败。
        SurgeryResult r = set.PerformAmputation(inf, materials: null, onBed: false,
            new SequenceRandomSource(new[] { 0.0 }), operationCapability: 1.0);
        Assert.Equal(SurgeryStatus.Failed, r.Status);
        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Infection); // 感染保留
    }

    // 截肢只能针对感染的肢体：非肢体感染抛异常（无肢可截）。
    [Fact]
    public void Amputation_rejects_non_limb_infection()
    {
        var set = new HealthConditionSet();
        var inf = new HealthCondition(HealthConditionType.Infection, 0.7, "躯干", onLimb: false);
        set.Add(inf);
        Assert.Throws<System.ArgumentException>(() =>
            set.PerformAmputation(inf, null, false, new SequenceRandomSource(new[] { 0.99 })));
    }

    // [SPEC-B14-补8] 安装假肢也是手术：走点数池/耗材/成败流程（纯 roll，装配由调用方在成功时做）。
    [Fact]
    public void Prosthetic_surgery_succeeds_with_high_roll()
    {
        var set = new HealthConditionSet();
        // 池=基础15+床10+急救包60=85 → RollRange[0,85]；roll=50>10 → 成功。
        SurgeryResult r = set.PerformProstheticSurgery(new[] { "first_aid_kit" }, onBed: true,
            new SequenceRandomSource(new[] { 50.0 }), operationCapability: 1.0);
        Assert.Equal(SurgeryStatus.Success, r.Status);
    }

    [Fact]
    public void Prosthetic_surgery_fails_with_low_roll_and_consumes_supplies()
    {
        var set = new HealthConditionSet();
        // 池=基础15+床10+绷带15=40 → RollRange[0,40]；roll=0≤10 → 失败；止血耗材照耗。
        SurgeryResult r = set.PerformProstheticSurgery(new[] { "bandage" }, onBed: true,
            new SequenceRandomSource(new[] { 0.0 }), operationCapability: 1.0);
        Assert.Equal(SurgeryStatus.Failed, r.Status);
        Assert.Contains("bandage", r.ConsumedMaterials);
    }

    [Fact]
    public void Prosthetic_surgery_not_allowed_when_pool_too_low()
    {
        var set = new HealthConditionSet();
        // 徒手 + 操作能力残缺 → 池 <15 → 门槛未过（不 roll、零消耗）。
        SurgeryResult r = set.PerformProstheticSurgery(materials: null, onBed: false,
            new SequenceRandomSource(System.Array.Empty<double>()), operationCapability: 0.3);
        Assert.Equal(SurgeryStatus.NotAllowed, r.Status);
        Assert.Empty(r.ConsumedMaterials);
    }

    // 感染以人为单位：已有感染时，另一开放伤口本昼夜不再新开感染条（忽略并入）。
    [Fact]
    public void Only_one_infection_race_per_pawn()
    {
        var set = new HealthConditionSet();
        set.Add(FreshInfection(0.3, "右手", onLimb: true));            // 既有感染
        set.Add(new HealthCondition(HealthConditionType.Bleeding, 0.9, "左大腿", onLimb: true)); // 另一开放大伤口
        set.TickDay(new SequenceRandomSource(new[] { 0.0, 0.0, 0.0, 0.0 }), resting: false); // 必中感染 rng
        Assert.Single(set.Conditions.Where(x => x.Type == HealthConditionType.Infection));
    }

    // ---- 疗程指派（护栏①）每昼夜扣药决策纯逻辑 ----

    [Fact]
    public void DecideDose_no_assignment_is_no_course()
    {
        var set = new HealthConditionSet(); set.Add(FreshInfection(0.3));
        InfectionDoseDecision d = InfectionCourseLogic.DecideDose(set, null, 5);
        Assert.Equal(InfectionCourseStep.NoCourse, d.Step);
        Assert.False(d.ConsumedDose); Assert.False(d.Medicated);
    }

    [Fact]
    public void DecideDose_no_infection_reports_and_does_not_dose()
    {
        var set = new HealthConditionSet(); // 无感染
        InfectionDoseDecision d = InfectionCourseLogic.DecideDose(set, "antibiotics", 5);
        Assert.Equal(InfectionCourseStep.NoInfection, d.Step);
        Assert.False(d.ConsumedDose);
    }

    [Fact]
    public void DecideDose_out_of_stock_breaks_without_dosing()
    {
        var set = new HealthConditionSet(); set.Add(FreshInfection(0.3));
        InfectionDoseDecision d = InfectionCourseLogic.DecideDose(set, "antibiotics", 0);
        Assert.Equal(InfectionCourseStep.OutOfStock, d.Step);
        Assert.False(d.ConsumedDose); Assert.False(d.Medicated);
    }

    [Fact]
    public void DecideDose_dosed_consumes_and_medicates_with_tier()
    {
        var set = new HealthConditionSet(); set.Add(FreshInfection(0.3));
        InfectionDoseDecision d = InfectionCourseLogic.DecideDose(set, "herbal_salve", 3);
        Assert.Equal(InfectionCourseStep.Dosed, d.Step);
        Assert.True(d.ConsumedDose); Assert.True(d.Medicated);
        Assert.Equal(0.35, d.Medicine!.Value.Efficacy, 3); // 本时段用草药膏档驱动竞速
    }

    // 显示分档：坏疽降为高进度呈现层（0-33 初期 / 34-66 扩散 / 67-99 濒坏疽）。
    [Fact]
    public void Infection_stage_word_bands_by_progress()
    {
        Assert.Equal("初期", HealthConditionSet.InfectionStageWord(0.10));
        Assert.Equal("扩散", HealthConditionSet.InfectionStageWord(0.50));
        Assert.Equal("濒坏疽", HealthConditionSet.InfectionStageWord(0.80));
    }

    // 草药膏/蒲公英茶治感染，成药仍只治疾病：目录归类正确、不串轴。
    [Fact]
    public void Herbal_remedies_registered_for_infection_only()
    {
        Assert.Equal(HealthConditionType.Infection, MedicineCatalog.For("herbal_salve")!.Value.Treats);
        Assert.Equal(HealthConditionType.Infection, MedicineCatalog.For("dandelion_tea")!.Value.Treats);
        Assert.Equal(0.35, MedicineCatalog.For("herbal_salve")!.Value.Efficacy, 3);
        Assert.Equal(0.15, MedicineCatalog.For("dandelion_tea")!.Value.Efficacy, 3);
        Assert.Equal(1.00, MedicineCatalog.For("antibiotics")!.Value.Efficacy, 3);
        // 成药只治疾病：对感染 TreatIllness 无效（感染改走竞速，不从此路一次性消退）。
        var set = new HealthConditionSet(); var inf = FreshInfection(0.5, "右手"); set.Add(inf);
        Assert.Equal(TreatmentStatus.NoEffect, set.TreatIllness(inf, MedicineCatalog.For("medicine")).Status);
    }

    // 新草药材料与产物均在 Medical 类目录中（供 UI 分组 / 配方引用 / 掉落投放）。
    [Fact]
    public void Herbal_materials_and_products_exist_in_medical_category()
    {
        foreach (string key in new[] { "dandelion", "rosehip", "laojunxu", "herbal_salve", "dandelion_tea" })
        {
            Assert.True(Materials.Has(key), $"草药物品缺失：{key}");
            Assert.Equal(MaterialCategory.Medical, Materials.Find(key)!.Value.Category);
        }
    }

    // 草药膏/蒲公英茶配方存在、无书门槛（民间方子人人会）、工时制、材料对齐目录。
    [Fact]
    public void Herbal_recipes_are_bookless_worktime_and_use_catalogued_ingredients()
    {
        RecipeData salve = RecipeBook.Find("herbal_salve")!;
        Assert.Empty(salve.RequiredBookIds);
        Assert.Empty(salve.RequiredTools);
        Assert.True(salve.WorkMinutes > 0);
        Assert.Equal("herbal_salve", salve.OutputKey);
        foreach (string ing in new[] { "dandelion", "rosehip", "laojunxu" })
        {
            Assert.True(salve.MaterialCosts.ContainsKey(ing), $"草药膏配方缺原料：{ing}");
            Assert.True(Materials.Has(ing));
        }

        RecipeData tea = RecipeBook.Find("dandelion_tea")!;
        Assert.Empty(tea.RequiredBookIds);
        Assert.Empty(tea.RequiredTools);
        Assert.True(tea.WorkMinutes > 0);
        Assert.Equal("dandelion_tea", tea.OutputKey);
        Assert.True(tea.MaterialCosts.ContainsKey("dandelion"), "蒲公英茶只用蒲公英，不引入水资源");
        Assert.Single(tea.MaterialCosts); // 最简：仅蒲公英
    }

    // ---- [SPEC-B14-补] 草药绷带：止血手术供点 25（普通绷带上位替代）----

    [Fact]
    public void Herbal_bandage_gives_25_surgery_points_for_bleeding()
    {
        SurgerySupply hb = SurgeryCatalog.For("herbal_bandage")!.Value;
        Assert.Equal(25, hb.Points);                     // 用户原话：草药绷带 25（普通绷带 15）
        Assert.True(hb.CanTreat(HealthConditionType.Bleeding));
        Assert.False(hb.CanTreat(HealthConditionType.Fracture));
        Assert.False(hb.Exclusive);                       // 非独占（散件）
        Assert.Equal(15, SurgeryCatalog.For("bandage")!.Value.Points); // 普通绷带 15 不变
        Assert.True(Materials.Has("herbal_bandage"));
        Assert.Equal(MaterialCategory.Medical, Materials.Find("herbal_bandage")!.Value.Category);
    }

    [Fact]
    public void Herbal_bandage_recipe_is_laojunxu_plus_bandage_worktime_bookless()
    {
        RecipeData r = RecipeBook.Find("herbal_bandage")!;
        Assert.Empty(r.RequiredBookIds);
        Assert.Empty(r.RequiredTools);
        Assert.True(r.WorkMinutes > 0);
        Assert.Equal("herbal_bandage", r.OutputKey);
        Assert.True(r.MaterialCosts.ContainsKey("laojunxu"));
        Assert.True(r.MaterialCosts.ContainsKey("bandage"));
    }

    // ---- [SPEC-B14-补2] 玫瑰果茶：24h 恢复 +9pp（与睡床加成同族加算）----

    [Fact]
    public void Rosehip_tea_bonus_speeds_post_surgery_healing_additively()
    {
        // 同一术后出血伤，带 +9pp 恢复加成的当昼夜愈合应比不带更多（加算进恢复效率）。
        HealthCondition Operated() { var c = Bleed(0.5); return c; }

        var withBonus = new HealthConditionSet(); var cb = Operated(); withBonus.Add(cb);
        var noBonus = new HealthConditionSet(); var cn = Operated(); noBonus.Add(cn);
        // 先各进入同等愈合态（内部 setter 可及：源以 Link 编入本测试程序集）。
        cb.SetRecoveryEfficiency(50); cn.SetRecoveryEfficiency(50);

        withBonus.TickDay(NoInfection(), resting: false, restedInBed: false, infectionChanceMultiplier: 1.0, extraHealBonusPct: Pawn_RosehipBonus);
        noBonus.TickDay(NoInfection(), resting: false, restedInBed: false, infectionChanceMultiplier: 1.0, extraHealBonusPct: 0.0);

        Assert.True(cb.Severity < cn.Severity, "玫瑰果茶恢复加成应加快术后愈合");
    }

    private const double Pawn_RosehipBonus = 9.0;

    [Fact]
    public void Rosehip_tea_material_and_recipe_exist()
    {
        Assert.True(Materials.Has("rosehip_tea"));
        Assert.Equal(MaterialCategory.Medical, Materials.Find("rosehip_tea")!.Value.Category);
        RecipeData r = RecipeBook.Find("rosehip_tea")!;
        Assert.Empty(r.RequiredBookIds);
        Assert.Empty(r.RequiredTools);
        Assert.True(r.WorkMinutes > 0);
        Assert.Equal("rosehip_tea", r.OutputKey);
        Assert.True(r.MaterialCosts.ContainsKey("rosehip"));
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
