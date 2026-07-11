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
    public void Minor_bleed_wound_untreated_festers_into_infection_and_maims_the_limb()
    {
        // 小咬伤不失血死，但若放任 → 感染 → 坏疽截肢（肢体致残、非致死）。
        var (set, c) = SetWith(new HealthCondition(
            HealthConditionType.Bleeding, 0.35, "右手", onLimb: true, lethalBleed: false));
        var alwaysInfect = new SequenceRandomSource(Enumerable.Repeat(0.0, 40).ToArray());
        var maimed = new List<string>();
        for (int day = 0; day < 30 && maimed.Count == 0 && !set.IsDead; day++)
        {
            maimed.AddRange(set.TickDay(alwaysInfect, resting: false).MaimedParts);
        }
        Assert.Contains("右手", maimed);
        Assert.False(set.IsDead, "肢体只感染不致命失血：终局是截肢致残而非死亡");
    }

    // ---- 时间线提前：新感染更快封顶 ----

    [Fact]
    public void Fresh_infection_untreated_reaches_terminal_within_five_days()
    {
        // 感染时间线提前（draft）：初始严重度的躯干感染，未用药应在 5 昼夜内败血症致死。
        var (set, c) = SetWith(new HealthCondition(HealthConditionType.Infection, 0.20, "躯干", onLimb: false));
        bool died = false;
        int dayDied = -1;
        for (int day = 1; day <= 5 && !died; day++)
        {
            died = set.TickDay(NoInfection(), resting: false).AnyDeath;
            if (died) dayDied = day;
        }
        Assert.True(died, $"新感染应在 5 昼夜内封顶致死（实际未死）");
        Assert.True(dayDied <= 5);
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
