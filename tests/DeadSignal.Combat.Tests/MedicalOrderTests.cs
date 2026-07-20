using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [批次21·impl-medicine] 「给谁用什么医疗物资」的可用性判定（<see cref="MedicalOrderLogic"/>）。
/// 这条规则原先散在 <c>MedicalPanel</c> 各按钮的 Disabled 表达式里且零单测；
/// 角色侧「选中角色 → 给他用药」的下令入口必须先能回答"能不能用、为什么不能用"，否则会给一个没病没伤的人开空面板。
/// 药效数值不在此（单一事实源仍是 MedicineCatalog / SurgeryCatalog），本层只出"能不能用 + 用途 + 拒绝理由"。
/// </summary>
public sealed class MedicalOrderTests
{
    private static HealthConditionSet Set(params HealthCondition[] cs)
    {
        var s = new HealthConditionSet();
        foreach (HealthCondition c in cs)
            s.Add(c);
        return s;
    }

    private static HealthCondition Bleed(string part = "躯干", double sev = 0.4)
        => new(HealthConditionType.Bleeding, sev, part);

    private static HealthCondition Fracture(string part = "左大腿", double sev = 0.5)
        => new(HealthConditionType.Fracture, sev, part, onLimb: true);

    private static HealthCondition Infection(string part = "左手", double sev = 0.3)
        => new(HealthConditionType.Infection, sev, part, onLimb: true);

    /// <summary>库存查询：给定 key→数量，其余为 0。</summary>
    private static Func<string, int> Stock(params (string Key, int Count)[] entries)
    {
        var d = entries.ToDictionary(e => e.Key, e => e.Count);
        return k => d.TryGetValue(k, out int n) ? n : 0;
    }

    /// <summary>满库存（每样 5 份）。</summary>
    private static readonly Func<string, int> Plenty = _ => 5;

    // ---- 分类：哪些材料算"可直接使用的医疗物资" ----

    [Fact]
    public void 感染三档药归类为感染疗程用药()
    {
        Assert.Equal(MedicalUseKind.InfectionCourse, MedicalOrderLogic.KindOf("antibiotics"));
        Assert.Equal(MedicalUseKind.InfectionCourse, MedicalOrderLogic.KindOf("herbal_salve"));
        Assert.Equal(MedicalUseKind.InfectionCourse, MedicalOrderLogic.KindOf("dandelion_tea"));
    }

    [Fact]
    public void 成药已删除_玫瑰果茶归类为恢复补剂()
    {
        Assert.Null(MedicalOrderLogic.KindOf("medicine"));
        Assert.Equal(MedicalUseKind.RecoveryTonic, MedicalOrderLogic.KindOf("rosehip_tea"));
    }

    [Fact]
    public void 五种手术耗材归类为手术耗材()
    {
        foreach (string k in new[] { "bandage", "herbal_bandage", "needle_thread", "splint", "first_aid_kit" })
            Assert.Equal(MedicalUseKind.SurgerySupply, MedicalOrderLogic.KindOf(k));
    }

    /// <summary>
    /// [用户口径] 「蒲公英、玫瑰果和老君须**应当属于材料**」——把这句话钉成可执行的护栏。
    /// 三味药材是**采来的原料**：得先熬成草药膏 / 蒲公英茶 / 玫瑰果茶 / 草药绷带，才能用在人身上。
    /// 它们在 <c>Materials</c> 目录里、落地为 <see cref="ItemCategory.Material"/> 的材料物品，
    /// 且**既不在 <c>MedicineCatalog</c> 也不在 <c>SurgeryCatalog</c>** —— 谁哪天想把它们做成"能直接吃的药"，这里就会红。
    /// </summary>
    [Theory]
    [InlineData("dandelion")]
    [InlineData("rosehip")]
    [InlineData("laojunxu")]
    public void 三味药材是材料而不是药(string key)
    {
        MaterialDef? def = Materials.Find(key);
        Assert.NotNull(def);

        // ① 它是材料：Materials 目录条目，进库存就是一件材料物品。
        Assert.Equal(ItemCategory.Material, def!.Value.ToItem().Category);

        // ② 它不是药、也不是手术耗材：两个目录都查不到它。
        Assert.Null(MedicineCatalog.For(key));
        Assert.Null(SurgeryCatalog.For(key));

        // ③ 于是它永远不会出现在"能给谁用什么"的下令入口里（面板/右键都问这把尺子）。
        Assert.False(MedicalOrderLogic.IsMedicalSupply(key));
    }

    /// <summary>
    /// 三味药材熬出来的**四样成品**才是能用在人身上的东西（草药膏 / 蒲公英茶 / 玫瑰果茶 = 药与补剂，草药绷带 = 手术耗材）。
    /// 与上一条互为对照：**原料归材料、成品归医药**，wiki 分区照此切。
    /// </summary>
    [Theory]
    [InlineData("herbal_salve", MedicalUseKind.InfectionCourse)]
    [InlineData("dandelion_tea", MedicalUseKind.InfectionCourse)]
    [InlineData("rosehip_tea", MedicalUseKind.RecoveryTonic)]
    [InlineData("herbal_bandage", MedicalUseKind.SurgerySupply)]
    public void 草药熬出来的四样成品才是能直接用的(string key, MedicalUseKind expected)
    {
        Assert.True(MedicalOrderLogic.IsMedicalSupply(key));
        Assert.Equal(expected, MedicalOrderLogic.KindOf(key));
    }

    [Fact]
    public void 草药原料不是可用医疗物资_只是配方输入()
    {
        // Materials.cs 的 MaterialDef 表里，蒲公英/玫瑰果/老君须虽在 MaterialCategory.Medical 类目下，
        // 但它们是草药膏/茶的**配料**，不能直接往人身上用。
        foreach (string k in new[] { "dandelion", "rosehip", "laojunxu" })
        {
            Assert.Null(MedicalOrderLogic.KindOf(k));
            Assert.False(MedicalOrderLogic.IsMedicalSupply(k));
        }
        Assert.False(MedicalOrderLogic.IsMedicalSupply("wood"));
    }

    // ---- 单件判定：能不能给这个人用这个 ----

    [Fact]
    public void 有感染且有药_抗生素可用()
    {
        MedicalUseOption o = MedicalOrderLogic.Evaluate("antibiotics", Set(Infection()), stock: 2,
            rosehipActive: false, currentCourseKey: null);
        Assert.True(o.Usable);
        Assert.Equal(MedicalRefusal.None, o.Refusal);
        Assert.Equal(MedicalUseKind.InfectionCourse, o.Kind);
        Assert.Equal(2, o.Stock);
    }

    [Fact]
    public void 没有感染_抗生素无对症目标()
    {
        MedicalUseOption o = MedicalOrderLogic.Evaluate("antibiotics", Set(Bleed()), stock: 2,
            rosehipActive: false, currentCourseKey: null);
        Assert.False(o.Usable);
        Assert.Equal(MedicalRefusal.NoTarget, o.Refusal);
    }

    [Fact]
    public void 有感染但断货_抗生素缺货()
    {
        MedicalUseOption o = MedicalOrderLogic.Evaluate("antibiotics", Set(Infection()), stock: 0,
            rosehipActive: false, currentCourseKey: null);
        Assert.False(o.Usable);
        Assert.Equal(MedicalRefusal.OutOfStock, o.Refusal);
    }

    [Fact]
    public void 缺货优先于无对症目标_玩家先看见的是没药()
    {
        // 既没感染又没药 → 报"缺货"（玩家最该先解决的是补货；"没病"由整体 NeedsMedicalAttention 兜底）。
        MedicalUseOption o = MedicalOrderLogic.Evaluate("antibiotics", Set(), stock: 0,
            rosehipActive: false, currentCourseKey: null);
        Assert.Equal(MedicalRefusal.OutOfStock, o.Refusal);
    }

    [Fact]
    public void 已在同档疗程中_该药不可重复指派()
    {
        MedicalUseOption o = MedicalOrderLogic.Evaluate("antibiotics", Set(Infection()), stock: 3,
            rosehipActive: false, currentCourseKey: "antibiotics");
        Assert.False(o.Usable);
        Assert.Equal(MedicalRefusal.AlreadyActive, o.Refusal);
    }

    [Fact]
    public void 疗程中换另一档药_可用()
    {
        // 蒲公英茶先垫治疗进度、再换抗生素接力，是终稿明说的混合策略 ⇒ 换档必须可用。
        MedicalUseOption o = MedicalOrderLogic.Evaluate("herbal_salve", Set(Infection()), stock: 1,
            rosehipActive: false, currentCourseKey: "antibiotics");
        Assert.True(o.Usable);
    }

    [Fact]
    public void 成药不是医疗物资()
    {
        Assert.Equal(MedicalRefusal.NotMedical,
            MedicalOrderLogic.Evaluate("medicine", Set(Infection()), 1, false, null).Refusal);
    }

    [Fact]
    public void 玫瑰果茶_buff生效中不可再喝_过期后可再喝()
    {
        Assert.Equal(MedicalRefusal.AlreadyActive,
            MedicalOrderLogic.Evaluate("rosehip_tea", Set(Bleed()), 1, rosehipActive: true, currentCourseKey: null).Refusal);
        Assert.True(MedicalOrderLogic.Evaluate("rosehip_tea", Set(Bleed()), 1, rosehipActive: false, currentCourseKey: null).Usable);
    }

    [Fact]
    public void 玫瑰果茶_无伤无病时无意义()
    {
        // 补剂只加速"术后流血/骨折的逐日愈合"（HealthConditions.TickDay 的 extraHealBonusPct 轴 → EfficiencyPoolBonusPct）。
        // 一个没伤的人喝了什么也不会发生 ⇒ 不给这个下令入口，别让玩家白扔一份茶。
        MedicalUseOption o = MedicalOrderLogic.Evaluate("rosehip_tea", Set(Infection()), 1, false, null);
        Assert.Equal(MedicalRefusal.NoTarget, o.Refusal);
    }

    [Fact]
    public void 止血耗材需要有流血伤_夹板需要有骨折()
    {
        Assert.True(MedicalOrderLogic.Evaluate("bandage", Set(Bleed()), 1, false, null).Usable);
        Assert.Equal(MedicalRefusal.NoTarget, MedicalOrderLogic.Evaluate("bandage", Set(Fracture()), 1, false, null).Refusal);

        Assert.True(MedicalOrderLogic.Evaluate("splint", Set(Fracture()), 1, false, null).Usable);
        Assert.Equal(MedicalRefusal.NoTarget, MedicalOrderLogic.Evaluate("splint", Set(Bleed()), 1, false, null).Refusal);
    }

    [Fact]
    public void 急救包兼治流血与骨折()
    {
        Assert.True(MedicalOrderLogic.Evaluate("first_aid_kit", Set(Bleed()), 1, false, null).Usable);
        Assert.True(MedicalOrderLogic.Evaluate("first_aid_kit", Set(Fracture()), 1, false, null).Usable);
    }

    [Fact]
    public void 感染的肢体也算止血耗材的目标_截肢要关合残端()
    {
        // [SPEC-B14-补7] MedicalPanel 的截肢入口复用止血耗材关合残端（supplyType 走 Bleeding）
        // ⇒ 只有感染的肢体时，绷带仍该是"能用"的。
        Assert.True(MedicalOrderLogic.Evaluate("bandage", Set(Infection()), 1, false, null).Usable);
    }

    [Fact]
    public void 死人不再给任何医疗下令()
    {
        var dead = Set(Bleed());
        MedicalUseOption o = MedicalOrderLogic.Evaluate("bandage", dead, 5, false, null, alive: false);
        Assert.Equal(MedicalRefusal.PatientDead, o.Refusal);
    }

    [Fact]
    public void 非医疗物资一律不可用()
    {
        MedicalUseOption o = MedicalOrderLogic.Evaluate("wood", Set(Bleed()), 99, false, null);
        Assert.False(o.Usable);
        Assert.Equal(MedicalRefusal.NotMedical, o.Refusal);
    }

    // ---- 整表枚举：面板/右键菜单直接消费 ----

    [Fact]
    public void 枚举只列成品医疗物资_不列草药原料()
    {
        IReadOnlyList<MedicalUseOption> all = MedicalOrderLogic.OptionsFor(Set(Bleed()), Plenty, false, null);
        var keys = all.Select(o => o.MaterialKey).ToList();

        Assert.Equal(9, keys.Count); // 5 手术耗材 + 3 感染药 + 玫瑰果茶
        Assert.DoesNotContain("dandelion", keys);
        Assert.DoesNotContain("rosehip", keys);
        Assert.DoesNotContain("laojunxu", keys);
    }

    [Fact]
    public void 枚举出的可用项_正是对症且有货的那些()
    {
        // 病人：一处流血 + 一处感染；库存：只有绷带和蒲公英茶。
        HealthConditionSet set = Set(Bleed(), Infection());
        Func<string, int> stock = Stock(("bandage", 2), ("dandelion_tea", 1));

        var usable = MedicalOrderLogic.OptionsFor(set, stock, rosehipActive: false, currentCourseKey: null)
            .Where(o => o.Usable).Select(o => o.MaterialKey).OrderBy(k => k).ToList();

        Assert.Equal(new[] { "bandage", "dandelion_tea" }, usable);
    }

    [Fact]
    public void 无伤无病者不需要医治()
    {
        Assert.False(MedicalOrderLogic.NeedsMedicalAttention(Set(), hasEquippableProstheticSlot: false));
        Assert.True(MedicalOrderLogic.NeedsMedicalAttention(Set(Bleed()), hasEquippableProstheticSlot: false));
    }

    [Fact]
    public void 缺肢者即使无伤病也需要进医务_可装假肢()
    {
        // [SPEC-B14-补8] MedicalPanel.AddProstheticInstallSection 已有这条口径
        // （无伤病但有可装假肢的空槽 → 面板照开），此处把它固化成可测规则。
        Assert.True(MedicalOrderLogic.NeedsMedicalAttention(Set(), hasEquippableProstheticSlot: true));
    }
}
