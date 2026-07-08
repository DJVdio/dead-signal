using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 伤病随时间恶化 + 治疗 纯逻辑单测（无 Godot 依赖，Link 编入）。
/// 覆盖：出血拖久致死、开放伤口按几率感染、感染恶化→致残(肢体)/致死(躯干)、骨折需固定+时间愈合、
/// 未固定骨折畸形致残、治疗判定（上药+高医疗技能→好转、无药/低技能→乏力、休养加速）、药品目录、战斗态只读映射。
/// 数值皆"拟定待调 draft"，测试锁的是规则形态与当前拟定值。
/// </summary>
public class HealthConditionsTests
{
    // 让感染 roll 不触发的高值（Range(0,1) 返回 1 → 1 < 任何 chance(<1) 为假）。
    private static IRandomSource NoInfection(int ticks = 32)
        => new SequenceRandomSource(Enumerable.Repeat(1.0, ticks).ToArray());

    // ---- 出血：拖久恶化→失血过多致死 ----

    [Fact]
    public void Bleeding_untreated_worsens_each_day_and_eventually_kills()
    {
        var set = new HealthConditionSet();
        var bleed = new HealthCondition(HealthConditionType.Bleeding, severity: 0.2, bodyPart: "左上臂", onLimb: true);
        set.Add(bleed);

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

    [Fact]
    public void Bleeding_staunched_by_styptic_and_medic_is_removed()
    {
        var set = new HealthConditionSet();
        var bleed = new HealthCondition(HealthConditionType.Bleeding, severity: 0.5, bodyPart: "右手", onLimb: true);
        set.Add(bleed);

        Medicine? styptic = MedicineCatalog.For("styptic");
        Assert.NotNull(styptic);
        TreatmentResult tr = set.Treat(bleed, styptic, SkillLevel.Adept);

        Assert.True(tr.SeverityAfter < tr.SeverityBefore);
        Assert.Equal(TreatmentStatus.Staunched, tr.Status); // 止血药+中级医疗 → 完全止血
        Assert.True(tr.Removed);
        Assert.DoesNotContain(bleed, set.Conditions); // 止住的出血从伤病集移除
    }

    // ---- 感染：开放伤口按几率感染，链式恶化 ----

    [Fact]
    public void Open_bleeding_wound_can_contract_infection_on_a_bad_roll()
    {
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Bleeding, severity: 0.8, bodyPart: "左大腿", onLimb: true));

        // roll=0 → 必定命中感染几率；一昼夜后应新增一条同部位感染。
        var rng = new SequenceRandomSource(0.0);
        HealthTickResult r = set.TickDay(rng, resting: false);

        Assert.Contains(r.Events, e => e.ContractedInfection);
        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Infection && c.BodyPart == "左大腿");
    }

    [Fact]
    public void Limb_infection_untreated_maims_the_limb_not_kills()
    {
        var set = new HealthConditionSet();
        var infection = new HealthCondition(HealthConditionType.Infection, severity: 0.5, bodyPart: "左手", onLimb: true);
        set.Add(infection);

        var maimed = new System.Collections.Generic.List<string>();
        for (int day = 0; day < 20 && !set.IsDead && maimed.Count == 0; day++)
        {
            HealthTickResult r = set.TickDay(NoInfection(), resting: false);
            maimed.AddRange(r.MaimedParts);
        }

        Assert.False(set.IsDead, "肢体感染封顶应致残而非致死");
        Assert.Contains("左手", maimed);
        Assert.DoesNotContain(infection, set.Conditions); // 致残后该感染消解（肢体已失）
    }

    [Fact]
    public void Torso_infection_untreated_kills()
    {
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Infection, severity: 0.5, bodyPart: "躯干", onLimb: false));

        bool died = false;
        for (int day = 0; day < 20 && !died; day++)
        {
            died = set.TickDay(NoInfection(), resting: false).AnyDeath;
        }
        Assert.True(died, "非肢体感染封顶应致死（败血症）");
        Assert.True(set.IsDead);
    }

    [Fact]
    public void Antibiotics_and_high_medical_skill_cure_infection_faster_than_low()
    {
        HealthCondition MakeInf() => new(HealthConditionType.Infection, severity: 0.9, bodyPart: "右上臂", onLimb: true);
        Medicine? abx = MedicineCatalog.For("antibiotics");

        var expert = new HealthConditionSet(); var ci = MakeInf(); expert.Add(ci);
        expert.Treat(ci, abx, SkillLevel.Expert);
        double expertAfter = ci.Severity;

        var novice = new HealthConditionSet(); var cn = MakeInf(); novice.Add(cn);
        novice.Treat(cn, abx, SkillLevel.Novice);
        double noviceAfter = cn.Severity;

        Assert.True(expertAfter < noviceAfter, "医疗技能越高，单次治疗降severity越多");
    }

    [Fact]
    public void Resting_slows_infection_progression()
    {
        HealthCondition MakeInf() => new(HealthConditionType.Infection, severity: 0.3, bodyPart: "右上臂", onLimb: true);

        var rest = new HealthConditionSet(); var cr = MakeInf(); rest.Add(cr);
        rest.TickDay(NoInfection(), resting: true);

        var noRest = new HealthConditionSet(); var cn = MakeInf(); noRest.Add(cn);
        noRest.TickDay(NoInfection(), resting: false);

        Assert.True(cr.Severity < cn.Severity, "休养减缓感染恶化");
    }

    // ---- 骨折：需固定 + 时间愈合；未固定→畸形致残 ----

    [Fact]
    public void Fracture_needs_splint_to_heal_over_time()
    {
        var set = new HealthConditionSet();
        var frac = new HealthCondition(HealthConditionType.Fracture, severity: 0.8, bodyPart: "右大腿", onLimb: true);
        set.Add(frac);

        Medicine? splint = MedicineCatalog.For("splint");
        TreatmentResult tr = set.Treat(frac, splint, SkillLevel.Novice);
        Assert.Equal(TreatmentStatus.Immobilized, tr.Status);
        Assert.True(frac.Immobilized);

        bool healed = false;
        for (int day = 0; day < 20 && !healed; day++)
        {
            set.TickDay(NoInfection(), resting: true);
            healed = !set.Conditions.Contains(frac);
        }
        Assert.True(healed, "固定+休养足够多天后骨折应愈合并移除");
    }

    [Fact]
    public void Fracture_left_unsplinted_worsens_to_permanent_maim()
    {
        var set = new HealthConditionSet();
        var frac = new HealthCondition(HealthConditionType.Fracture, severity: 0.6, bodyPart: "左大腿", onLimb: true);
        set.Add(frac);

        var maimed = new System.Collections.Generic.List<string>();
        for (int day = 0; day < 40 && maimed.Count == 0; day++)
        {
            maimed.AddRange(set.TickDay(NoInfection(), resting: false).MaimedParts);
        }
        Assert.Contains("左大腿", maimed);
        Assert.False(set.IsDead, "骨折不致死");
    }

    [Fact]
    public void Fracture_does_not_bleed_or_kill()
    {
        var set = new HealthConditionSet();
        set.Add(new HealthCondition(HealthConditionType.Fracture, severity: 0.9, bodyPart: "躯干", onLimb: false));
        // 非肢体骨折封顶不致残也不致死（仅封顶停住）。
        for (int day = 0; day < 10; day++)
        {
            HealthTickResult r = set.TickDay(NoInfection(), resting: false);
            Assert.False(r.AnyDeath);
            Assert.Empty(r.MaimedParts);
        }
    }

    // ---- 治疗：无药/无治 → 按恶化规则往致残/死走 ----

    [Fact]
    public void Wrong_medicine_has_no_effect_on_condition()
    {
        var set = new HealthConditionSet();
        var infection = new HealthCondition(HealthConditionType.Infection, severity: 0.5, bodyPart: "右手", onLimb: true);
        set.Add(infection);

        // 绷带治不了感染。
        TreatmentResult tr = set.Treat(infection, MedicineCatalog.For("bandage"), SkillLevel.Expert);
        Assert.Equal(TreatmentStatus.NoEffect, tr.Status);
        Assert.Equal(0.5, infection.Severity, 3);
    }

    [Fact]
    public void Bare_hands_can_partially_staunch_bleeding_without_medicine()
    {
        var set = new HealthConditionSet();
        var bleed = new HealthCondition(HealthConditionType.Bleeding, severity: 0.6, bodyPart: "右手", onLimb: true);
        set.Add(bleed);

        TreatmentResult tr = set.Treat(bleed, medicine: null, SkillLevel.Novice);
        Assert.True(tr.SeverityAfter < tr.SeverityBefore, "徒手按压可部分止血");
        Assert.True(bleed.Severity > 0, "徒手不足以完全止血");
    }

    [Fact]
    public void Tending_suppresses_worsening_for_that_day()
    {
        var set = new HealthConditionSet();
        var bleed = new HealthCondition(HealthConditionType.Bleeding, severity: 0.5, bodyPart: "右手", onLimb: true);
        set.Add(bleed);

        set.Treat(bleed, MedicineCatalog.For("bandage"), SkillLevel.Novice);
        double afterTend = bleed.Severity;
        set.TickDay(NoInfection(), resting: false); // 当日已处置 → 不再恶化
        Assert.True(bleed.Severity <= afterTend + 1e-9, "已处置当日不恶化");
    }

    // ---- 药品目录 ----

    [Fact]
    public void Medicine_catalog_maps_material_keys_to_treatment_semantics()
    {
        Assert.Equal(HealthConditionType.Bleeding, MedicineCatalog.For("bandage")!.Value.Treats);
        Assert.Equal(HealthConditionType.Bleeding, MedicineCatalog.For("styptic")!.Value.Treats);
        Assert.Equal(HealthConditionType.Infection, MedicineCatalog.For("antibiotics")!.Value.Treats);
        Assert.Equal(HealthConditionType.Fracture, MedicineCatalog.For("splint")!.Value.Treats);
        Assert.Null(MedicineCatalog.For("wood")); // 非药品
    }

    [Fact]
    public void Medicine_items_exist_in_materials_catalog_under_medical_category()
    {
        foreach (string key in new[] { "bandage", "styptic", "antibiotics", "splint" })
        {
            Assert.True(Materials.Has(key), $"药品缺失：{key}");
            Assert.Equal(MaterialCategory.Medical, Materials.Find(key)!.Value.Category);
        }
    }

    // ---- 与战斗既有状态的只读映射 ----

    [Fact]
    public void SeedFromBody_reads_bleeding_and_fracture_states_without_mutating_body()
    {
        Body body = HumanBody.NewBody();
        body.RegisterBleed("左上臂"); // 战斗产出的出血伤口
        body.MarkFractured("右大腿"); // 战斗产出的骨折

        HealthConditionSet set = HealthMapping.SeedFromBody(body);

        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Bleeding && c.BodyPart == "左上臂" && c.OnLimb);
        Assert.Contains(set.Conditions, c => c.Type == HealthConditionType.Fracture && c.BodyPart == "右大腿" && c.OnLimb);
        // 只读：Body 状态未被改动。
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
        Assert.False(c.OnLimb); // 躯干非肢体 → 出血封顶致死
    }
}
