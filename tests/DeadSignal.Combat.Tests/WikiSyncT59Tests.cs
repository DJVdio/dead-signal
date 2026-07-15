using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>T59 —— 把用户在 wiki 上的改动同步进代码</b>（「表赢代码」）的护栏。
///
/// <para>本批四件（用户明确改的，逐条对应 wiki 上的一格）：
/// <list type="number">
/// <item><b>书籍阅读时长全线砍</b>：全表 190h → 76h（《野外生存指南》2 整夜 → 1/3 夜）。</item>
/// <item><b>战争面具补上护眼</b>：用户把它的槽位从「面部」改成「面部 + 眼镜」。</item>
/// <item><b>新增「棉帽」</b>：头部槽，护 头/左耳/右耳，6/3，0.15kg。</item>
/// <item><b>蒲公英不再是食材</b>（但仍是药材）。</item>
/// </list>
/// 南丁格尔那两格（−15%/−10% + 加算改乘算）在 <c>NurseRecruitTests</c> 里，与既有断言同处一地。</para>
/// </summary>
public class WikiSyncT59Tests
{
    // ══════════════════ ① 书籍阅读时长（全线砍） ══════════════════

    /// <summary>
    /// 用户在 wiki 上把每本书的「读完要几小时」逐本改小了。<b>书是本作能力的唯一载体</b>
    /// （通用技能系统已删除 —— 能力只由 authored 专属效果 + 读过的书承载），
    /// 所以这一刀砍的是**整条能力曲线的节奏**：一本工具书从「两整夜」变成「三分之一夜」。
    /// 幅度很大，但这是用户的设计决定（表赢代码），此处只负责把它钉死、不做平衡评判。
    /// </summary>
    [Theory]
    [InlineData("wilderness_survival_guide", 4)]   // 野外生存指南 24 → 4
    [InlineData("farmer_hundred_questions", 4)]    // 农场主的一百个问题 24 → 4
    [InlineData("tailors_notes", 8)]               // 裁缝手记 20 → 8
    [InlineData("folk_chemistry_notes", 8)]        // 土法化学笔记 20 → 8
    [InlineData("carpentry_basics", 8)]            // 木匠入门 20 → 8
    [InlineData("advanced_carpentry", 12)]         // 进阶木匠技术 28 → 12
    [InlineData("way_of_bow_and_arrow", 12)]       // 弓与箭之道 18 → 12
    [InlineData("mechanical_beauty", 8)]           // 机械之美 24 → 8
    [InlineData("peak_hour", 6)]                    // [T71] 尖峰时刻 = 6（用户在 wiki 定的）
    public void 书籍阅读时长_与用户的表逐本一致(string bookId, double expectedHours)
    {
        BookData book = BookLibrary.All().Single(b => b.Id == bookId);
        Assert.Equal(expectedHours, book.ReadHours, 6);
    }

    /// <summary>
    /// 用户在 wiki 书籍表里把 <c>carpentry_basics</c> 的书名从「木匠入门」改成「从零到一学会木匠」
    /// （标题是玩家看得到的物品名，走 <see cref="BookData.Title"/> → <see cref="Item.Book"/>）。表赢代码，钉死新名。
    /// </summary>
    [Fact]
    public void 木匠入门书名_已按用户改为从零到一学会木匠()
    {
        BookData book = BookLibrary.All().Single(b => b.Id == "carpentry_basics");
        Assert.Equal("从零到一学会木匠", book.Title);
    }

    /// <summary>
    /// 用户手上那 8 本真书合计 <b>64h</b>（原 178h，他这一刀砍掉约 64%）。任何人往回加时长，这条会红。
    /// <para>⚠️ 分开算，用户日后调哪一边都能立刻看出是谁的数变了：
    /// <list type="bullet">
    /// <item><b>64h</b> = 用户手上那 8 本的合计（表赢代码，逐本钉在上面的 Theory 里）。</item>
    /// <item>+8h [T59]《弓制作指南》—— 8h 是**我拟定的**（用户没给这个数）。</item>
    /// <item>+6h [T71]《尖峰时刻》—— 6h 是**用户在 wiki 定的**。</item>
    /// </list>
    /// ⇒ 全表现值 = 64 + 8 + 6 = <b>78h</b>。</para>
    /// </summary>
    [Fact]
    public void 书籍阅读时长_用户那8本合计64小时_加两本新书后78小时()
    {
        // ⚠️ [T59·二次澄清] **日记不计入** —— 它不是书，没有阅读工时（用户：书给角色读、日记给玩家读）。
        //    故这里只数**真正的书**（Manuals）：用户手上那 8 本 = 64h（把两本 agent 新加的书剔掉再数）。
        double userEight = BookLibrary.Manuals()
            .Where(b => b.Id != BookLibrary.BowCraftingGuideId && b.Id != BookLibrary.PeakHourId)
            .Sum(b => b.ReadHours);
        Assert.Equal(64.0, userEight, 6);

        Assert.Equal(8.0, BookLibrary.BowCraftingGuide().ReadHours, 6);   // 拟定值（用户没给）
        Assert.Equal(6.0, BookLibrary.PeakHour().ReadHours, 6);           // [T71] 用户在 wiki 定的
        Assert.Equal(78.0, BookLibrary.Manuals().Sum(b => b.ReadHours), 6);

        // 日记一小时都不该贡献。
        Assert.Equal(0.0, BookLibrary.Diaries().Sum(b => b.ReadHours), 6);
    }

    // ══════════════════ ② 战争面具：占了眼镜槽，就得护住眼睛 ══════════════════

    /// <summary>
    /// <b>用户把战争面具的槽位从「面部」改成了「面部 + 眼镜」。</b>
    ///
    /// <para>光加一个槽是**没有意义的**：眼镜槽上现存的两件（防暴头盔 / 防毒面具）
    /// <b>本来就都要占面部槽</b> ⇒ 它们与战争面具早已互斥。多占一个眼镜槽，
    /// 一条互斥关系都不会改变（游戏里没有任何「只占眼镜槽」的护目镜可被它挤掉）。
    /// ⇒ 这一格改动唯一说得通的读法是：<b>这张面具本来就该罩住眼睛</b>（"战争面具"的字面语义）。</para>
    ///
    /// <para>反过来说，若只占槽而不护眼，玩家付出了一个槽位却什么也没换到 —— 那才是纯负收益。</para>
    /// </summary>
    [Fact]
    public void 战争面具_占面部与眼镜两槽()
    {
        ApparelCatalog.ApparelDef mask = ApparelCatalog.Get("战争面具")!;
        Assert.Contains(EquipSlot.Face, mask.Slots);
        Assert.Contains(EquipSlot.Eyes, mask.Slots);
    }

    /// <summary>战争面具的覆盖：鼻 + 下巴 + <b>双眼</b>（本次补上的就是双眼）。</summary>
    [Fact]
    public void 战争面具_护住双眼()
    {
        ArmorLayer mask = ArmorTable.WarMask();
        Assert.NotNull(mask.CoversParts);
        Assert.Contains(HumanBody.LeftEye, mask.CoversParts!);
        Assert.Contains(HumanBody.RightEye, mask.CoversParts!);
        Assert.Contains(HumanBody.Nose, mask.CoversParts!);
        Assert.Contains(HumanBody.Chin, mask.CoversParts!);
    }

    /// <summary>
    /// 🔴 <b>「占了槽就得给防护」的通则护栏</b>：任何占用眼镜槽的穿戴品，都必须真的覆盖双眼。
    /// 这条是本次那个 bug 的一般形式 —— 占槽而不护，玩家的槽位就白付了。
    /// （防毒面具虽无护甲数值 <c>Layer=null</c>，但它**确实覆盖**双眼，故照样通过。）
    /// </summary>
    [Fact]
    public void 凡占眼镜槽者_必须覆盖双眼()
    {
        foreach (ApparelCatalog.ApparelDef def in ApparelCatalog.Defs.Values.Where(d => d.Slots.Contains(EquipSlot.Eyes)))
        {
            Assert.True(def.CoversParts is not null
                        && def.CoversParts.Contains(HumanBody.LeftEye)
                        && def.CoversParts.Contains(HumanBody.RightEye),
                $"「{def.Name}」占了眼镜槽却不覆盖双眼 —— 玩家付了一个槽位，什么也没换到");
        }
    }

    // ══════════════════ ③ 新增「棉帽」 ══════════════════

    /// <summary>棉帽的数值＝用户在表里填的：护甲 6/3、0.15kg（与全部布类基线一致）。</summary>
    [Fact]
    public void 棉帽_数值与布类基线一致()
    {
        ArmorLayer hat = ArmorTable.CottonHat();
        Assert.Equal("棉帽", hat.Name);
        Assert.Equal(6, hat.SharpDefense);
        Assert.Equal(3, hat.BluntDefense);
        Assert.Equal(0.15, hat.Weight, 6);
    }

    /// <summary>棉帽占头槽、护 头 + 左右耳（用户给的覆盖）。</summary>
    [Fact]
    public void 棉帽_占头槽并护住头与双耳()
    {
        ApparelCatalog.ApparelDef hat = ApparelCatalog.Get("棉帽")!;
        Assert.Equal(new[] { EquipSlot.Head }, hat.Slots.ToArray());

        Assert.Contains(HumanBody.Head, hat.CoversParts!);
        Assert.Contains(HumanBody.LeftEar, hat.CoversParts!);
        Assert.Contains(HumanBody.RightEar, hat.CoversParts!);
    }

    /// <summary>
    /// 🔴 <b>两层登记必须都做到</b>（CLAUDE.md：「纯逻辑绿≠功能生效」）：
    /// 只进 <see cref="ArmorTable"/> 而不进 <see cref="ApparelCatalog"/> ⇒ 这顶帽子穿不上身，是个静默失效的死物品。
    /// </summary>
    [Fact]
    public void 棉帽_两处登记都在_不是死物品()
    {
        Assert.True(ApparelCatalog.IsApparel("棉帽"));

        // 目录里那一条，就是 ArmorTable 里那一件（层序与覆盖逐项对齐，不是另抄一份）。
        ArmorLayer table = ArmorTable.CottonHat();
        ApparelCatalog.ApparelDef def = ApparelCatalog.Get("棉帽")!;
        Assert.Equal(table.Slot, def.Layer);
        Assert.NotNull(table.CoversParts);
        Assert.Equal(table.CoversParts!.OrderBy(x => x), def.CoversParts!.OrderBy(x => x));
    }

    /// <summary>棉帽必须有配方 —— 否则它在游戏里根本拿不到（表里新加一件、代码里造不出来＝没落地）。</summary>
    [Fact]
    public void 棉帽_有配方且吃布_由裁缝手记解锁()
    {
        RecipeData hat = RecipeBook.All.Single(r => r.Id == "cotton_hat");

        Assert.Equal("棉帽", hat.DisplayName);
        Assert.Equal(RecipeCategory.Tailoring, hat.Category);
        Assert.Contains("cloth", hat.MaterialCosts.Keys);
        Assert.Contains(RecipeBook.TailorsNotesBookId, hat.RequiredBookIds);
    }

    // ══════════════════ ④ 蒲公英：不是饭，但还是药 ══════════════════

    /// <summary>用户把蒲公英从「食物与食材」表里删了 ⇒ 它<b>下不了锅</b>了。</summary>
    [Fact]
    public void 蒲公英_不再是食材()
    {
        Assert.False(FoodCalories.Has("dandelion"));
        Assert.Equal(0, FoodCalories.Of("dandelion"));
    }

    /// <summary>
    /// 🔴 <b>但它仍然是药材，整条感染药链一个字都不能动。</b>
    ///
    /// <para>抽取器给的建议是「从 <c>FoodCalories</c> + <c>Materials</c> 一并删掉」——<b>照做会炸</b>：
    /// 蒲公英茶是感染三档药之一，蒲公英还是草药膏的配料。把它从 <see cref="Materials"/> 删掉
    /// ⇒ 两个配方断料、感染最低档的那味药凭空消失。
    /// 用户删的是「食物」那一栏，<b>正确的读法是「它不该能当饭吃」，不是「它不该存在」</b>。</para>
    /// </summary>
    [Fact]
    public void 蒲公英_仍是药材且仍在感染药链里()
    {
        // 仍在材料目录，且归「医疗」类。
        Assert.True(Materials.Has("dandelion"));
        Assert.Equal(MaterialCategory.Medical, Materials.Find("dandelion")!.Value.Category);

        // 蒲公英茶仍是感染三档药之一。
        Medicine? tea = MedicineCatalog.For("dandelion_tea");
        Assert.NotNull(tea);
        Assert.Equal(HealthConditionType.Infection, tea!.Value.Treats);

        // 两个配方仍以蒲公英为料：蒲公英茶（2 株）与草药膏。
        RecipeData teaRecipe = RecipeBook.All.Single(r => r.Id == "dandelion_tea");
        Assert.Contains("dandelion", teaRecipe.MaterialCosts.Keys);

        Assert.Contains(RecipeBook.All,
            r => r.Id == "herbal_salve" && r.MaterialCosts.ContainsKey("dandelion"));
    }

    /// <summary>
    /// 🔴🔴 <b>本批最重要的一条通则护栏：食材一旦被摘出食物表，就必须另有用途 —— 否则它是「死物品」。</b>
    ///
    /// <para><see cref="FoodCalories"/> 就是「下不下得了锅」的判据（<see cref="FoodCalories.Has"/>）。
    /// 把一样东西从它里面删掉，那样东西就<b>既不能吃、也不能煮</b>。
    /// 若它同时还不能卖、不是任何配方的料、也不是药 —— 那它就成了一件
    /// <b>只会占背包重量、永远派不上用场</b>的纯垃圾，而它还在探索点里继续刷新。
    /// 在一个把负重当核心代价的游戏里（30/50/80 三档惩罚），这是**实打实的伤害**，不是洁癖。</para>
    ///
    /// <para>蒲公英之所以能安全地被摘出去，正是因为它<b>另有一条医疗线</b>（药 + 两个配方）。
    /// 这条测试把那个前提变成全表通则：<b>凡「食物」类材料，要么下得了锅，要么另有出路。</b></para>
    /// </summary>
    [Fact]
    public void 食物类材料_要么下得了锅_要么另有用途_不许有死物品()
    {
        var deadItems = new List<string>();

        foreach (MaterialDef m in Materials.All.Where(m => m.Category == MaterialCategory.Food))
        {
            if (FoodCalories.Has(m.Key)) continue;                                    // 下得了锅
            if (MedicineCatalog.IsMedicine(m.Key)) continue;                          // 是药
            if (MerchantBuyList.CanSell(Item.Material(m.Key, m.DisplayName))) continue;  // 卖得掉
            if (RecipeBook.All.Any(r => r.MaterialCosts.ContainsKey(m.Key))) continue;   // 是别的东西的料
            // 🔴 [T67] **第五条出路：上得了案板。**
            // 这一条是被本护栏**当场抓出来的**，不是我提前想好的——用户把「老鼠/鸟」从食物表里摘走
            // （原话「老鼠和鸟不能直接入锅了，而是要先宰杀」），它们当场被判成死物品：既下不了锅、不是药、
            // 商人不收食材、也不是任何 RecipeData 的料。
            // ⚠️ 而它们**恰恰不是死物品** —— 它们是【宰杀】的原料（老鼠 → 老鼠肉 + 碎皮革；鸟 → 鸟肉 + 羽毛）。
            // 之所以第四条兜不住它们，是因为**宰杀不是一条 RecipeData**（RecipeData 只有单一 OutputKey，
            // 表达不了"一刀出两样东西"）⇒ 它自成一套（<see cref="ButcheryLogic"/>）。
            // ⇒ 护栏的**判据本身**要跟着世界的形状走：出路多了一条，就得在这里认它一条。
            if (ButcheryLogic.IsButcherable(m.Key)) continue;                            // 上得了案板

            deadItems.Add($"{m.DisplayName}（{m.Key}）");
        }

        Assert.True(deadItems.Count == 0,
            "这些「食物」类材料既下不了锅、又不是药、卖不掉、也不是任何配方的料 —— "
            + "它们只会占背包重量，是死物品：" + string.Join("、", deadItems));
    }

    // ══════════════ ⑤ [T59·裁决] 解锁图重构：《弓制作指南》 ══════════════

    /// <summary>
    /// 🔴🔴 <b>本仓最容易踩、代价最大的一条规则：书籍的「效果」列是<u>给玩家看的描述</u>，
    /// <u>不是</u>权威解锁清单。权威只有一处 —— 配方自己的 <see cref="RecipeData.RequiredBookIds"/>。</b>
    ///
    /// <para><b>证据（这不是理论风险，是差点发生的事故）</b>：用户在 wiki 上把《土法化学笔记》的效果列
    /// 改写成了「解锁胶水、火药、鞣制药水」——<b>那行字里没有那两把枪、也没有任何一种子弹</b>。
    /// 若哪个同步 agent 老实"照着效果列同步代码"，<b>枪和弹药会当场从游戏里静默消失</b>。
    /// 同理，《进阶木匠技术》的新文案里没有消防斧/反曲弓/长弓 —— 照抄就是把它们全删掉。</para>
    ///
    /// <para>⇒ <b>同步规则（已裁决）：</b>
    /// <list type="bullet">
    /// <item><b>「效果」列的增删字句，永远不许反向删代码里的解锁关系。</b></item>
    /// <item>但<b>"新增一本书"这种结构性改动要落</b>（那是设计，不是描述）——《弓制作指南》就是这么落的。</item>
    /// </list>
    /// 本测试把"效果列删了字、但解锁必须还在"逐条钉死，谁照抄谁当场红。</para>
    /// </summary>
    [Fact]
    public void 效果列是描述不是权威解锁清单_照抄它会静默删掉枪和弹药()
    {
        // 《土法化学笔记》的效果列已被用户改成只剩三样，但这些解锁**一个都不许少**。
        foreach (string recipeId in new[]
                 { "improvised_hunting_gun", "improvised_shotgun", "bullet_parts", "ammo_short", "ammo_medium" })
        {
            RecipeData r = RecipeBook.All.Single(x => x.Id == recipeId);
            Assert.Contains(RecipeBook.FolkChemistryNotesBookId, r.RequiredBookIds);
        }
    }

    /// <summary>
    /// 解锁图重构后的三条归属（[T59] 主 agent 代拍板）：
    /// <b>弓 → 《弓制作指南》（新书）；消防斧 → 仍留《进阶木匠技术》；弩 → 仍留《机械之美》。</b>
    /// <para>消防斧**刻意不挪**：它是木工工具，且「消防斧 + 造消防斧的书同馆」（联合收割机仓库）是既有设计。</para>
    /// </summary>
    [Fact]
    public void 解锁图重构_弓归新书_消防斧仍归进阶木匠_弩仍归机械之美()
    {
        RecipeData recurve = RecipeBook.All.Single(r => r.Id == "recurve_bow");
        RecipeData longbow = RecipeBook.All.Single(r => r.Id == "longbow");
        RecipeData axe = RecipeBook.All.Single(r => r.Id == "axe");

        // 两把弓：挪到新书。
        Assert.Contains(RecipeBook.BowCraftingGuideBookId, recurve.RequiredBookIds);
        Assert.Contains(RecipeBook.BowCraftingGuideBookId, longbow.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.AdvancedCarpentryBookId, recurve.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.AdvancedCarpentryBookId, longbow.RequiredBookIds);

        // 🔴 消防斧没挪（挪走会拆掉"消防斧与造消防斧的书同馆"那个设计）。
        Assert.Contains(RecipeBook.AdvancedCarpentryBookId, axe.RequiredBookIds);
        Assert.DoesNotContain(RecipeBook.BowCraftingGuideBookId, axe.RequiredBookIds);
    }

    /// <summary>
    /// 🔴 <b>新书必须捡得到 —— 否则那两把弓就是造不出来的死物品。</b>
    /// <para>反曲弓/长弓原本挂在《进阶木匠技术》上，而那本书**有**投放点。把它们挪到一本没人能捡到的新书上，
    /// 等于把两把弓从游戏里删掉（《机械之美》当初正是这么把两把弩锁死的）。这条护栏就是防这个。</para>
    /// </summary>
    [Fact]
    public void 弓制作指南_必须有投放点_否则弓造不出来()
    {
        // 守林人小屋·阁楼（"弓箭的家"）：护林员的狩猎弓、箭，和他做弓的书。
        CacheResult? attic = ExplorationCache.Resolve(ExplorationCache.RangersCabinAtticId, new StoryFlags());
        Assert.NotNull(attic);

        bool placed = attic!.Value.Loot.Any(
            l => l.Kind == LootKind.Book && l.RefId == BookLibrary.BowCraftingGuideId);

        Assert.True(placed,
            "《弓制作指南》全图零投放 ⇒ 捡不到书 ⇒ 反曲弓/长弓永远造不出来（《机械之美》踩过这个坑）");
    }
}
