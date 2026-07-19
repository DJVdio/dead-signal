using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 配方系统核心（Workbench 工具槽 + RecipeData + CraftingLogic 判定/结算）的纯逻辑单测。
// 覆盖：工具门槛、书门槛、材料不足、全满足、缺一项各分支，以及 Resolve 产出契约（通用技能门槛已删）。

public class WorkbenchTests
{
    [Fact]
    public void FreshBench_HasNoTools()
    {
        var bench = new WorkbenchState();
        Assert.False(bench.HasTool(ToolSlot.Calipers));
        Assert.False(bench.HasTool(ToolSlot.SawBlade));
        Assert.False(bench.HasTool(ToolSlot.Beaker));
        Assert.Empty(bench.InstalledTools);
    }

    [Fact]
    public void InstallTool_IsIdempotentAndReflected()
    {
        var bench = new WorkbenchState();
        Assert.True(bench.InstallTool(ToolSlot.SawBlade));
        Assert.False(bench.InstallTool(ToolSlot.SawBlade)); // 幂等：第二次不再"发生装入"
        Assert.True(bench.HasTool(ToolSlot.SawBlade));
        Assert.Contains(ToolSlot.SawBlade, bench.InstalledTools);
    }

    [Fact]
    public void RemoveTool_Uninstalls()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        Assert.True(bench.RemoveTool(ToolSlot.Beaker));
        Assert.False(bench.RemoveTool(ToolSlot.Beaker)); // 幂等
        Assert.False(bench.HasTool(ToolSlot.Beaker));
    }
}

public class RecipeBookTests
{
    [Fact]
    public void Draft_CoversTheSixExamples()
    {
        var ids = RecipeBook.All.Select(r => r.Id).ToHashSet();
        foreach (var expected in new[] { "bone_knife", "cloth_vest", "chair", "gunpowder", "tanning_solution", "handmade_bow" })
        {
            Assert.Contains(expected, ids);
        }
    }

    [Fact]
    public void BoneKnife_UnlockedByWildernessBook_NoTool()
    {
        var r = RecipeBook.Find("bone_knife")!;
        Assert.Contains(RecipeBook.WildernessSurvivalGuideBookId, r.RequiredBookIds);
        Assert.Empty(r.RequiredTools);
    }

    [Fact]
    public void ClothVest_UnlockedByTailorsNotes_NoTool()
    {
        var r = RecipeBook.Find("cloth_vest")!;
        Assert.Contains(RecipeBook.TailorsNotesBookId, r.RequiredBookIds);
        Assert.Empty(r.RequiredTools);
    }

    [Fact]
    public void Chair_RequiresSawBlade_AndCarpentryBasics()
    {
        var r = RecipeBook.Find("chair")!;
        Assert.Contains(ToolSlot.SawBlade, r.RequiredTools);
        Assert.Contains(RecipeBook.CarpentryBasicsBookId, r.RequiredBookIds); // 用户拍板：木椅也要读《木匠入门》
    }

    [Fact]
    public void Gunpowder_RequiresBeaker_And_FolkChemistryNotes()
    {
        var r = RecipeBook.Find("gunpowder")!;
        Assert.Contains(ToolSlot.Beaker, r.RequiredTools);
        Assert.Contains(RecipeBook.FolkChemistryNotesBookId, r.RequiredBookIds);
    }

    [Fact]
    public void TanningSolution_RequiresBeaker_And_FolkChemistryNotes()
    {
        var r = RecipeBook.Find("tanning_solution")!;
        Assert.Contains(ToolSlot.Beaker, r.RequiredTools);
        Assert.Contains(RecipeBook.FolkChemistryNotesBookId, r.RequiredBookIds);
    }

    [Fact]
    public void NewGateBooks_ExistInLibrary()
    {
        var ids = BookLibrary.All().Select(b => b.Id).ToHashSet();
        Assert.Contains(RecipeBook.TailorsNotesBookId, ids);
        Assert.Contains(RecipeBook.FolkChemistryNotesBookId, ids);
        Assert.Contains(RecipeBook.CarpentryBasicsBookId, ids); // 《木匠入门》须在书目里，否则木椅/自制弓永久不可制作
    }

    /// <summary>
    /// 短弓的书门槛＝《<b>野外生存指南</b>》（[SPEC-B21·T26]，用户在 wiki 书籍表里重排了解锁归属）。
    /// <para>
    /// ⚠️ <b>本条推翻了旧断言</b>「自制弓也要读《木匠入门》」——那是更早一轮的用户拍板，
    /// 现已被<b>表赢代码</b>的新表覆盖：用户写的《野外生存指南》效果列是「骨刀、短弓、削减木箭、圈套陷阱、战争面具」，
    /// 《木匠入门》那一列则只剩「木椅、床、桌子、废木料回收」——<b>一把弓都没有</b>。
    /// </para>
    /// <para>
    /// <b>这是放宽而非收紧</b>：《野外生存指南》在 camp.json 的开局柜子里，《木匠入门》要出门搜刮
    /// ⇒ 短弓从"搜到书才能做"变成"<b>开局读完书就能做</b>"。工具门槛（卡尺）<b>未动</b>——用户只重排了书。
    /// </para>
    /// </summary>
    [Fact]
    public void HandmadeBow_NoToolNeeded_RequiresWildernessSurvivalGuide()
    {
        var r = RecipeBook.Find("handmade_bow")!;
        // [用户后撤] 三把弓（短弓/反曲弓/长弓）的卡尺工具门槛已解除——短弓徒手可造。
        Assert.DoesNotContain(ToolSlot.Calipers, r.RequiredTools);
        Assert.Contains(RecipeBook.WildernessSurvivalGuideBookId, r.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.CarpentryBasicsBookId, r.RequiredBookIds);
    }

    /// <summary>
    /// 《野外生存指南》解锁的<b>五条</b>配方＝用户 wiki 书籍表那一行的逐条编码
    /// （骨刀 / 短弓 / <b>削尖的木箭</b> / 圈套陷阱 / 战争面具）。[SPEC-B21·T26]
    /// <para>
    /// <b>它是开局共享库存里就有的书</b>（camp.json 住宅-柜子 role=storage）⇒ 挂在它名下＝<b>开局读完就能做</b>。
    /// 这一行因此是「开局第一晚该读哪本书」那个选择的<b>全部赌注</b>：读它，一次拿到 刀＋弓＋箭＋陷阱＋面具 一整条生存线。
    /// </para>
    /// </summary>
    [Fact]
    public void 野外生存指南_解锁六条配方()
    {
        var byBook = RecipeBook.All
            .Where(r => r.RequiredBookIds.Contains(RecipeBook.WildernessSurvivalGuideBookId))
            .Select(r => r.Id)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(
            // [T68] 新增 horror_armor（恐怖装甲，骨+皮，呼应文案）——见 RecipeBook 对应注释。
            new[] { "ammo_arrow_stick", "bone_knife", "handmade_bow", "horror_armor", "snare_trap", "war_mask" }.OrderBy(x => x),
            byBook);
    }

    /// <summary>
    /// <b>弓的阶梯＝一条"要不要出门搜书"的曲线</b>（[SPEC-B21·T26] 用户拍板）：
    /// <list type="bullet">
    /// <item><b>短弓</b> ← 《野外生存指南》（<b>开局共享库存就有</b>）⇒ 开局读完书即可造，不必出门。</item>
    /// <item><b>反曲弓 / 长弓</b> ← 《<b>弓制作指南</b>》（<b>只能搜刮</b>：守林人小屋·阁楼）⇒ 想升级弓，<b>得出去搜书</b>。</item>
    /// <item><b>单手轻弩 / 双手重弩</b> ← 《<b>机械之美</b>》（用户拍板的新书；<b>只能靠这本书</b>，见下条测试）。</item>
    /// </list>
    ///
    /// <para>⚠️ <b>[T59] 中间那一档换了本书</b>：反曲弓/长弓原挂《进阶木匠技术》，现挂用户在 wiki 上新加的
    /// 《<b>弓制作指南</b>》（他把"造弓"从木工书里拆成了单独一本）。
    /// <b>这条测试要守的曲线一个字都没变</b> —— 那本书<b>照样只能搜刮</b>（守林人小屋·阁楼，比原来的联合收割机仓库还远）
    /// ⇒「想升级弓就得出门」原样成立。变的只是"出门去哪儿搜、搜哪本"。
    /// （<b>斧头仍留在《进阶木匠技术》</b>，那条"斧头与造斧头的书同馆"的设计没动。）</para>
    /// </summary>
    [Fact]
    public void 弓弩的阶梯_短弓开局书_进阶弓搜书_弩要机械之美()
    {
        Assert.Contains(RecipeBook.WildernessSurvivalGuideBookId, RecipeBook.Find("handmade_bow")!.RequiredBookIds);

        RecipeData recurve = RecipeBook.Find("recurve_bow")!;
        Assert.Contains(RecipeBook.BowCraftingGuideBookId, recurve.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.AdvancedCarpentryBookId, recurve.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.CarpentryBasicsBookId, recurve.RequiredBookIds);

        RecipeData longbow = RecipeBook.Find("longbow")!;
        Assert.Contains(RecipeBook.BritishChronicleBookId, longbow.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.BowCraftingGuideBookId, longbow.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.AdvancedCarpentryBookId, longbow.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.CarpentryBasicsBookId, longbow.RequiredBookIds);

        foreach (string id in new[] { "light_crossbow", "heavy_crossbow" })
        {
            RecipeData r = RecipeBook.Find(id)!;
            Assert.Contains(RecipeBook.MechanicalBeautyBookId, r.RequiredBookIds);
            Assert.DoesNotContain(RecipeBook.CarpentryBasicsBookId, r.RequiredBookIds);   // 已从入门书搬走
        }
    }

    /// <summary>
    /// <b>《木匠入门》现在真的只剩家具了</b>（[SPEC-B21·T26] 终态）——短弓 / 反曲弓 / 长弓 / 两把弩全部搬走。
    /// <para>用户把弓弩线整条从这本书上剥离，这是<b>有意为之</b>。这条断言防的是"日后谁顺手又往里塞一把武器"。</para>
    /// </summary>
    [Fact]
    public void 木匠入门_只剩家具_一把弓弩都没有()
    {
        var unlocked = RecipeBook.All
            .Where(r => r.RequiredBookIds.Contains(RecipeBook.CarpentryBasicsBookId))
            .Select(r => r.Id)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(new[] { "bed", "chair", "table" }.OrderBy(x => x), unlocked);
    }

    /// <summary>
    /// 🔴 <b>《机械之美》是两把可制作弩的唯一来路 —— 而它眼下没有任何投放点。</b>
    /// <para>
    /// 单手轻弩 / 双手重弩<b>搜刮不到</b>（全图 0 处投放，只能造）⇒ 书拿不到，这两把弩就<b>在游戏里不存在</b>。
    /// <b>"书从哪来"是设计决策，已 [DECISION] 上抛用户，代码不许自己塞进某个搜刮点。</b>
    /// </para>
    /// <para>
    /// 本条断言钉的是<b>依赖关系</b>（弩 ⇔ 这本书），<b>不是</b>"书拿得到"——后者眼下为假。
    /// 用户定了来源、往 <c>ExplorationCache</c> 加上投放后，请再补一条"这本书能搜到"的断言
    /// （照抄 <c>ArcheryCraftingTests.弓与箭之道_能搜到_否则50pct回收率永远拿不到</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void 机械之美_是两把可制作弩的唯一门槛()
    {
        var unlocked = RecipeBook.All
            .Where(r => r.RequiredBookIds.Contains(RecipeBook.MechanicalBeautyBookId))
            .Select(r => r.Id)
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(new[] { "heavy_crossbow", "light_crossbow" }, unlocked);

        // 书本身存在于书目里（否则这两条配方永久不可解锁，且 UI 会拿一个查不到的 id 去渲染）。
        Assert.Contains(RecipeBook.MechanicalBeautyBookId, BookLibrary.All().Select(b => b.Id));
    }

    /// <summary>
    /// <b>「武器零件」与「机械零件」是两种东西 —— 别合并</b>（[SPEC-B21·T26] 用户拍板新建前者）。
    /// <para>
    /// 用户特意要新材料，<b>正是为了让弩与改装台不争抢同一堆零件</b>：
    /// <list type="bullet">
    /// <item><b>机械零件</b> <c>components</c> → 改装台 / 自制枪 / 杂活（通用机括件）</item>
    /// <item><b>武器零件</b> <c>weapon_parts</c> → <b>只喂弩</b>（弩机/扳机组/簧片）</item>
    /// </list>
    /// 这条断言就是那个隔离带的看门人：谁哪天把弩改回吃 <c>components</c>，或让改装台吃上 <c>weapon_parts</c>，它会红。
    /// </para>
    /// </summary>
    [Fact]
    public void 武器零件与机械零件不争抢_弩只吃武器零件_改装台只吃机械零件()
    {
        Assert.True(Materials.Has(Materials.WeaponPartsKey));
        Assert.NotEqual(Materials.WeaponPartsKey, "components");

        foreach (string id in new[] { "light_crossbow", "heavy_crossbow" })
        {
            IReadOnlyDictionary<string, int> cost = RecipeBook.Find(id)!.MaterialCosts;
            Assert.True(cost.ContainsKey(Materials.WeaponPartsKey), $"{id} 该吃武器零件");
            Assert.False(cost.ContainsKey("components"), $"{id} 不该再吃机械零件（那是改装台的料）");
        }

        IReadOnlyDictionary<string, int> bench = RecipeBook.Find("mod_bench")!.MaterialCosts;
        Assert.True(bench.ContainsKey("components"));
        Assert.False(bench.ContainsKey(Materials.WeaponPartsKey), "改装台不该吃武器零件（那是弩的料）");
    }

    /// <summary>
    /// <b>武器零件只能搜刮，没有配方</b> —— 同「子弹零件」那条：造不出来的精密件才有稀缺可言。
    /// <para>未来若要做"拆枪回收零件"，那是一套全新机制（拆武器回收），本单没做，届时这条断言要一起改。</para>
    /// </summary>
    [Fact]
    public void 武器零件没有配方_只能搜刮()
    {
        Assert.DoesNotContain(RecipeBook.All, r => r.OutputKey == Materials.WeaponPartsKey);
    }

    /// <summary>
    /// 《弓与箭之道》从"<b>只给被动加成</b>"变成"<b>解锁自制箭 + 那四项加成</b>"（[SPEC-B21·T26] 用户拍板）。
    /// <para>
    /// ⚠️ <b>重头箭用户没提 ⇒ 一个字没动</b>（保持零书门槛，只要卡尺）。别顺手"统一"成"好箭都归这本书"——
    /// 那是引申，不是用户说的。
    /// </para>
    /// <para>
    /// 三种箭因此摊成一条清晰的曲线：削尖的木箭（开局书）→ 重头箭（无书，只要卡尺）→ 自制箭（<b>要搜到这本书</b>）。
    /// </para>
    /// </summary>
    [Fact]
    public void 弓与箭之道_解锁自制箭_但重头箭不动()
    {
        Assert.Contains(RecipeBook.WayOfBowBookId, RecipeBook.Find("ammo_arrow_handmade")!.RequiredBookIds);

        RecipeData heavy = RecipeBook.Find("ammo_arrow_heavy")!;
        Assert.Empty(heavy.RequiredBookIds);                       // 用户没提 ⇒ 保持零书门槛
        Assert.Contains(ToolSlot.Calipers, heavy.RequiredTools);   // 工具门槛照旧
    }

    [Fact]
    public void Chair_RealCanCraft_GatedByCarpentryBasics()
    {
        var r = RecipeBook.Find("chair")!;
        var sawblade = new HashSet<ToolSlot> { ToolSlot.SawBlade };

        // 有锯片、材料够、没读《木匠入门》→ 卡书。
        var noBook = CraftingLogic.CanCraft(r, _ => 99, _ => false, sawblade);
        Assert.False(noBook.CanCraft);
        Assert.Contains(noBook.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);

        // 锯片 + 读《木匠入门》→ 过。
        var ok = CraftingLogic.CanCraft(
            r, _ => 99, id => id == RecipeBook.CarpentryBasicsBookId, sawblade);
        Assert.True(ok.CanCraft);
    }

    [Fact]
    public void Find_Unknown_ReturnsNull()
    {
        Assert.Null(RecipeBook.Find("no_such_recipe"));
    }

    [Fact]
    public void AllRecipes_HavePositiveWorkMinutes()
    {
        // 工时制：每配方须标正工时（拟定待调），否则夜间生产瞬间完工失去意义。
        Assert.All(RecipeBook.All, r => Assert.True(r.WorkMinutes > 0, $"{r.Id} 工时应为正"));
    }

    [Fact]
    public void Furniture_TakesLongerThan_SmallItems()
    {
        // 家具（木椅）工时应显著高于小件（火把/骨刀）——拟定档次待调。
        int chair = RecipeBook.Find("chair")!.WorkMinutes;
        int torch = RecipeBook.Find("torch")!.WorkMinutes;
        int knife = RecipeBook.Find("bone_knife")!.WorkMinutes;
        Assert.True(chair > torch, "木椅工时应长于火把");
        Assert.True(chair > knife, "木椅工时应长于骨刀");
    }

    [Fact]
    public void Bench_LowTierChair_NoBookNoTool()
    {
        var r = RecipeBook.Find("bench")!;
        Assert.Equal("板凳", r.DisplayName);
        Assert.Empty(r.RequiredBookIds);   // 无书门槛
        Assert.Empty(r.RequiredTools);     // 无工具槽 —— 开局即可做
        Assert.Equal(RecipeCategory.Woodwork, r.Category);
    }

    [Fact]
    public void Bench_CheaperThanChair()
    {
        var bench = RecipeBook.Find("bench")!;
        var chair = RecipeBook.Find("chair")!;
        int benchWood = bench.MaterialCosts.TryGetValue("wood", out int bw) ? bw : 0;
        int chairWood = chair.MaterialCosts.TryGetValue("wood", out int cw) ? cw : 0;
        Assert.True(benchWood < chairWood, "板凳应比木椅便宜（低级椅）");
        Assert.False(bench.MaterialCosts.ContainsKey("nails")); // 打折：去掉钉子
    }

    [Fact]
    public void Bench_AnyoneCanCraftAtStart_NoBookNoToolNeeded()
    {
        var r = RecipeBook.Find("bench")!;
        // 材料够、一本书没读、工作台空工具 → 仍可制作（人人可造、开局即可）。
        var avail = CraftingLogic.CanCraft(
            r, _ => 99, _ => false, new HashSet<ToolSlot>());
        Assert.True(avail.CanCraft);
        Assert.Empty(avail.Blocks);

        // 材料不足才卡（且只卡材料，不卡书/工具）。
        var noMat = CraftingLogic.CanCraft(
            r, _ => 0, _ => false, new HashSet<ToolSlot>());
        Assert.False(noMat.CanCraft);
        Assert.All(noMat.Blocks, b => Assert.Equal(CraftBlockReason.InsufficientMaterial, b.Reason));
    }
}

public class CraftingLogicTests
{
    // 一张全门槛俱全的合成配方，逐项拆分测各分支（工具/书/材料三类门槛）。
    private static readonly RecipeData FullGate = new(
        Id: "test_full",
        DisplayName: "测试全门槛物",
        Category: RecipeCategory.Chemistry,
        OutputKey: "test_out",
        OutputQuantity: 2,
        MaterialCosts: new Dictionary<string, int> { ["wood"] = 3, ["cloth"] = 1 },
        RequiredTools: new HashSet<ToolSlot> { ToolSlot.Beaker },
        RequiredBookIds: new List<string> { "test_book" });

    private static CraftAvailability Eval(
        RecipeData recipe,
        Dictionary<string, int>? mats = null,
        HashSet<string>? readBooks = null,
        HashSet<ToolSlot>? tools = null)
    {
        mats ??= new Dictionary<string, int> { ["wood"] = 10, ["cloth"] = 10 };
        readBooks ??= new HashSet<string> { "test_book" };
        tools ??= new HashSet<ToolSlot> { ToolSlot.Beaker };
        return CraftingLogic.CanCraft(
            recipe,
            k => mats.TryGetValue(k, out var v) ? v : 0,
            b => readBooks.Contains(b),
            tools);
    }

    [Fact]
    public void AllSatisfied_CanCraft()
    {
        var a = Eval(FullGate);
        Assert.True(a.CanCraft);
        Assert.Empty(a.Blocks);
    }

    [Fact]
    public void MissingTool_Blocks()
    {
        var a = Eval(FullGate, tools: new HashSet<ToolSlot>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
    }

    [Fact]
    public void UnreadBook_Blocks()
    {
        var a = Eval(FullGate, readBooks: new HashSet<string>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void InsufficientMaterial_Blocks()
    {
        var a = Eval(FullGate, mats: new Dictionary<string, int> { ["wood"] = 1, ["cloth"] = 1 });
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial && b.Key == "wood");
    }

    [Fact]
    public void MissingMaterialEntry_TreatedAsZero()
    {
        var a = Eval(FullGate, mats: new Dictionary<string, int>()); // 完全没登记 wood/cloth
        Assert.False(a.CanCraft);
        Assert.Equal(2, a.Blocks.Count(b => b.Reason == CraftBlockReason.InsufficientMaterial));
    }

    [Fact]
    public void AllGatesFail_ListsEveryReason()
    {
        var a = Eval(
            FullGate,
            mats: new Dictionary<string, int>(),
            readBooks: new HashSet<string>(),
            tools: new HashSet<ToolSlot>());
        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial);
    }

    [Fact]
    public void Resolve_ProducesNegativeMaterialDeltasAndOutput()
    {
        var res = CraftingLogic.Resolve(FullGate);
        Assert.Equal(-3, res.MaterialDeltas["wood"]);
        Assert.Equal(-1, res.MaterialDeltas["cloth"]);
        Assert.Equal("test_out", res.OutputKey);
        Assert.Equal(2, res.OutputQuantity);
    }

    [Fact]
    public void Resolve_BatchMultipliesEverything()
    {
        var res = CraftingLogic.Resolve(FullGate, times: 3);
        Assert.Equal(-9, res.MaterialDeltas["wood"]);
        Assert.Equal(6, res.OutputQuantity);
    }

    [Fact]
    public void Resolve_ClampsNonPositiveTimesToOne()
    {
        var res = CraftingLogic.Resolve(FullGate, times: 0);
        Assert.Equal(-3, res.MaterialDeltas["wood"]);
        Assert.Equal(2, res.OutputQuantity);
    }

    [Fact]
    public void RealRecipe_BoneKnife_GatedByBookOnly()
    {
        var r = RecipeBook.Find("bone_knife")!;
        // 材料够、书未读 → 只卡书。
        var blocked = CraftingLogic.CanCraft(
            r,
            _ => 99,
            _ => false,
            new HashSet<ToolSlot>());
        Assert.False(blocked.CanCraft);
        Assert.All(blocked.Blocks, b => Assert.Equal(CraftBlockReason.UnreadBook, b.Reason));

        // 读完书 → 过（无工具门槛）。
        var ok = CraftingLogic.CanCraft(
            r,
            _ => 99,
            _ => true,
            new HashSet<ToolSlot>());
        Assert.True(ok.CanCraft);
    }

    [Fact]
    public void RealRecipe_ClothVest_GatedByTailorsNotes()
    {
        var r = RecipeBook.Find("cloth_vest")!;
        // 材料够、没读《裁缝手记》→ 卡书。
        var blocked = CraftingLogic.CanCraft(
            r, _ => 99, id => id != RecipeBook.TailorsNotesBookId, new HashSet<ToolSlot>());
        Assert.False(blocked.CanCraft);
        Assert.Contains(blocked.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);

        // 读过《裁缝手记》→ 过（无工具门槛）。
        var ok = CraftingLogic.CanCraft(
            r, _ => 99, id => id == RecipeBook.TailorsNotesBookId, new HashSet<ToolSlot>());
        Assert.True(ok.CanCraft);
    }

    [Fact]
    public void RealRecipe_Gunpowder_GatedByBeakerAndFolkChemistryNotes()
    {
        var r = RecipeBook.Find("gunpowder")!;
        var beaker = new HashSet<ToolSlot> { ToolSlot.Beaker };

        // 有烧杯、没读《土法化学笔记》→ 卡书。
        var noBook = CraftingLogic.CanCraft(
            r, _ => 99, id => id != RecipeBook.FolkChemistryNotesBookId, beaker);
        Assert.False(noBook.CanCraft);
        Assert.Contains(noBook.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);

        // 读了书、没装烧杯 → 卡工具。
        var noTool = CraftingLogic.CanCraft(
            r, _ => 99, id => id == RecipeBook.FolkChemistryNotesBookId, new HashSet<ToolSlot>());
        Assert.False(noTool.CanCraft);
        Assert.Contains(noTool.Blocks, b => b.Reason == CraftBlockReason.MissingTool);

        // 烧杯 + 读书 → 过。
        var ok = CraftingLogic.CanCraft(
            r, _ => 99, id => id == RecipeBook.FolkChemistryNotesBookId, beaker);
        Assert.True(ok.CanCraft);
    }
}

// 制作者「书门槛」查询/提示纯逻辑：供制作面板把"换制作者对书门槛配方的影响"显式化
// （仅看书，不看工具/材料——工具/材料非制作者相关）。零规则改动，只加可测的查询与人读提示。
public class CrafterBookGateTests
{
    private static readonly RecipeData TwoBookRecipe = new(
        Id: "test_two_books",
        DisplayName: "双书物",
        Category: RecipeCategory.Chemistry,
        OutputKey: "test_out",
        OutputQuantity: 1,
        MaterialCosts: new Dictionary<string, int>(),
        RequiredTools: new HashSet<ToolSlot>(),
        RequiredBookIds: new List<string> { "book_a", "book_b" });

    [Fact]
    public void UnreadRequiredBooks_AllRead_Empty()
    {
        var unread = CraftingPanelFormat.UnreadRequiredBooks(TwoBookRecipe, _ => true);
        Assert.Empty(unread);
    }

    [Fact]
    public void UnreadRequiredBooks_NoneRead_ReturnsAllInOrder()
    {
        var unread = CraftingPanelFormat.UnreadRequiredBooks(TwoBookRecipe, _ => false);
        Assert.Equal(new[] { "book_a", "book_b" }, unread);
    }

    [Fact]
    public void UnreadRequiredBooks_PartialRead_ReturnsOnlyUnread()
    {
        var unread = CraftingPanelFormat.UnreadRequiredBooks(TwoBookRecipe, id => id == "book_a");
        Assert.Equal(new[] { "book_b" }, unread);
    }

    [Fact]
    public void UnreadRequiredBooks_NoBookGate_Empty()
    {
        // 无书门槛配方（RequiredBookIds 为空）：书门槛恒满足，即使全没读过书也返回空。
        var noBookRecipe = new RecipeData(
            Id: "test_no_book",
            DisplayName: "无书门槛物",
            Category: RecipeCategory.Misc,
            OutputKey: "test_out",
            OutputQuantity: 1,
            MaterialCosts: new Dictionary<string, int>(),
            RequiredTools: new HashSet<ToolSlot>(),
            RequiredBookIds: new List<string>());
        Assert.Empty(CraftingPanelFormat.UnreadRequiredBooks(noBookRecipe, _ => false));
    }

    [Fact]
    public void UnreadRequiredBooks_RealRecipe_TracksCrafter()
    {
        var gp = RecipeBook.Find("gunpowder")!;
        // 没读《土法化学笔记》→ 列它；读了 → 空。
        Assert.Equal(
            new[] { RecipeBook.FolkChemistryNotesBookId },
            CraftingPanelFormat.UnreadRequiredBooks(gp, _ => false));
        Assert.Empty(CraftingPanelFormat.UnreadRequiredBooks(gp, _ => true));
    }

    [Fact]
    public void BookGateHint_Met_ReturnsNull()
    {
        Assert.Null(CraftingPanelFormat.BookGateHint(TwoBookRecipe, _ => true, id => id));
    }

    [Fact]
    public void BookGateHint_OneUnread_UsesTitleInBrackets()
    {
        string? hint = CraftingPanelFormat.BookGateHint(
            TwoBookRecipe, id => id == "book_a", id => id == "book_b" ? "土法化学笔记" : id);
        Assert.Equal("需读完《土法化学笔记》", hint);
    }

    [Fact]
    public void BookGateHint_TwoUnread_JoinsWithSeparator()
    {
        string? hint = CraftingPanelFormat.BookGateHint(
            TwoBookRecipe, _ => false, id => id == "book_a" ? "甲书" : "乙书");
        Assert.Equal("需读完《甲书》、《乙书》", hint);
    }

    [Fact]
    public void BookGateHint_MissingTitle_FallsBackToId()
    {
        string? hint = CraftingPanelFormat.BookGateHint(TwoBookRecipe, id => id == "book_a", id => id);
        Assert.Equal("需读完《book_b》", hint);
    }

    [Fact]
    public void UnreadRequiredBooks_NullArgs_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => CraftingPanelFormat.UnreadRequiredBooks(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => CraftingPanelFormat.UnreadRequiredBooks(TwoBookRecipe, null!));
    }
}
