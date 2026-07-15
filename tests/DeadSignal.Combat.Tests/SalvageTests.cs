using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 拆除回收（<see cref="SalvageLogic"/>）：**用户拍板的三条规则**。
/// <list type="number">
/// <item>拆除一件东西返还 <b>50% 的建造材料</b>。</item>
/// <item><b>木材例外</b>：建造吃 16 木料 ⇒ 拆得 <b>4 木料 + 4 废木料</b>（各 25%）。</item>
/// <item><b>4 废木料 + 1 胶水</b>，在**装了锯片的工作台**上 ⇒ 做回 <b>4 木料</b>。</item>
/// </list>
/// 设计意涵（三条合起来才成立）：木材**表面只回收 25%**，但走一趟"废木料 + 胶水"能补回另外 25% ——
/// 最终仍是 50%，**只是要额外付一份胶水**。⇒ 胶水是瓶颈，木材的完整回收要交**胶水税**。
/// </summary>
public class SalvageTests
{
    // ══════════════ 1. 通用规则：返还 50%，向下取整 ══════════════

    [Fact]
    public void GenericMaterial_RefundsHalf()
    {
        var cost = new Dictionary<string, int> { ["nails"] = 4, ["iron"] = 6 };

        IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOf(cost);

        Assert.Equal(2, yield["nails"]);
        Assert.Equal(3, yield["iron"]);
    }

    [Fact]
    public void GenericMaterial_OddCount_RoundsDown()
    {
        // 向下取整（拟定拍板）：3 → 1（不是 2）。拆下来的东西总有损耗，且这样才**绝不套利**。
        var cost = new Dictionary<string, int> { ["nails"] = 3 };

        Assert.Equal(1, SalvageLogic.YieldOf(cost)["nails"]);
    }

    [Fact]
    public void GenericMaterial_SingleUnit_RefundsNothing()
    {
        // 1 个钉子的一半是半个钉子 —— 现实里那叫木屑。拆小件不划算，这是有意的下限。
        var cost = new Dictionary<string, int> { ["nails"] = 1 };

        Assert.False(SalvageLogic.YieldOf(cost).ContainsKey("nails"));
    }

    [Fact]
    public void Yield_NeverExceedsHalfOfCost_NoArbitrage()
    {
        // 反套利硬约束：**任何**材料的返还都 ≤ 建造成本的一半 ⇒ 造→拆→造 永远净亏，不存在无限刷。
        // 木材要连废木料一起算（它能变回木材），故木材的"等效返还" = wood + scrap_wood。
        foreach (var recipe in RecipeBook.All)
        {
            IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOf(recipe.MaterialCosts);
            foreach (var (key, cost) in recipe.MaterialCosts)
            {
                int got = key == SalvageLogic.WoodKey
                    ? yield.GetValueOrDefault(SalvageLogic.WoodKey) + yield.GetValueOrDefault(SalvageLogic.ScrapWoodKey)
                    : yield.GetValueOrDefault(key);
                Assert.True(got * 2 <= cost, $"{recipe.Id} 的 {key} 返还 {got} 超过成本 {cost} 的一半");
            }
        }
    }

    [Fact]
    public void EmptyCost_YieldsNothing()
        => Assert.Empty(SalvageLogic.YieldOf(new Dictionary<string, int>()));

    // ══════════════ 2. 木材例外：25% 木材 + 25% 废木料（用户原话的 16 → 4 + 4）══════════════

    [Fact]
    public void Wood16_Yields4Wood_And4ScrapWood()
    {
        var cost = new Dictionary<string, int> { [SalvageLogic.WoodKey] = 16 };

        IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOf(cost);

        Assert.Equal(4, yield[SalvageLogic.WoodKey]);       // 25%
        Assert.Equal(4, yield[SalvageLogic.ScrapWoodKey]);  // 25%
    }

    [Fact]
    public void Wood_DoesNotRefundHalfDirectly()
    {
        // 木材**不走**通用 50%——直接返还只有 25%。另外 25% 是废木料，得再花胶水才变得回木材。
        var cost = new Dictionary<string, int> { [SalvageLogic.WoodKey] = 16 };

        Assert.NotEqual(8, SalvageLogic.YieldOf(cost)[SalvageLogic.WoodKey]);
    }

    [Fact]
    public void Wood_RoundsDown_SmallBuildsYieldNothing()
    {
        // 木料 2 的板凳：25% = 0.5 → 拆了一地木屑，什么也不剩。狠，但符合"拆小件不划算"。
        var cost = new Dictionary<string, int> { [SalvageLogic.WoodKey] = 2 };

        IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOf(cost);

        Assert.False(yield.ContainsKey(SalvageLogic.WoodKey));
        Assert.False(yield.ContainsKey(SalvageLogic.ScrapWoodKey));
    }

    [Fact]
    public void MixedCost_WoodExceptionAndGenericHalf_Coexist()
    {
        var cost = new Dictionary<string, int> { [SalvageLogic.WoodKey] = 8, ["nails"] = 4 };

        IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOf(cost);

        Assert.Equal(2, yield[SalvageLogic.WoodKey]);       // 8 的 25%
        Assert.Equal(2, yield[SalvageLogic.ScrapWoodKey]);  // 8 的 25%
        Assert.Equal(2, yield["nails"]);                    // 4 的 50%（通用）
    }

    // ══════════════ 3. 废木料 → 木材：4 废木料 + 1 胶水，需锯片 ══════════════

    [Fact]
    public void ScrapWoodRecipe_FourScrapPlusOneGlue_MakesFourWood()
    {
        RecipeData? r = RecipeBook.Find(SalvageLogic.ScrapWoodRecipeId);

        Assert.NotNull(r);
        Assert.Equal(4, r!.MaterialCosts[SalvageLogic.ScrapWoodKey]);
        Assert.Equal(1, r.MaterialCosts[SalvageLogic.GlueKey]);
        Assert.Equal(SalvageLogic.WoodKey, r.OutputKey);
        Assert.Equal(4, r.OutputQuantity);
    }

    [Fact]
    public void ScrapWoodRecipe_RequiresSawBladeWorkbench()
    {
        // 用户原话：「在**有锯片的工作台**可以制作」。锯片＝既有的 ToolSlot.SawBlade（木工槽），未新造机制。
        RecipeData r = RecipeBook.Find(SalvageLogic.ScrapWoodRecipeId)!;

        Assert.Contains(ToolSlot.SawBlade, r.RequiredTools);
    }

    [Fact]
    public void ScrapWoodRecipe_WithoutSawBlade_IsBlocked()
    {
        RecipeData r = RecipeBook.Find(SalvageLogic.ScrapWoodRecipeId)!;
        var bare = new WorkbenchState(); // 空工作台：没装锯片

        CraftAvailability a = CraftingLogic.CanCraft(
            r, _ => 999, _ => true, bare.InstalledTools);

        Assert.False(a.CanCraft);
        Assert.Contains(a.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
    }

    [Fact]
    public void ScrapWoodRecipe_NeedsNoBook()
    {
        // 拆除是"建错了地方"的退出机制——不该被一本还没搜到的书卡死。粘木板是苦力活，不是手艺活。
        Assert.Empty(RecipeBook.Find(SalvageLogic.ScrapWoodRecipeId)!.RequiredBookIds);
    }

    // ══════════════ 三条规则合起来：木材最终仍回收 50%，但要付"胶水税" ══════════════

    [Fact]
    public void WoodRecovery_FullLoop_Reaches50Percent_AtTheCostOfOneGlue()
    {
        // 建造吃 16 木料 → 拆得 4 木料 + 4 废木料 → 4 废木料 + **1 胶水** 做回 4 木料 → 合计 8 木料 = 50%。
        IReadOnlyDictionary<string, int> yield =
            SalvageLogic.YieldOf(new Dictionary<string, int> { [SalvageLogic.WoodKey] = 16 });

        RecipeData r = RecipeBook.Find(SalvageLogic.ScrapWoodRecipeId)!;
        int loops = yield[SalvageLogic.ScrapWoodKey] / r.MaterialCosts[SalvageLogic.ScrapWoodKey];

        int totalWood = yield[SalvageLogic.WoodKey] + loops * r.OutputQuantity;
        int glueTax = loops * r.MaterialCosts[SalvageLogic.GlueKey];

        Assert.Equal(8, totalWood);  // 16 的 50%——**和别的材料一样**，只是绕了一圈
        Assert.Equal(1, glueTax);    // 绕这一圈的代价：一份胶水
    }

    // ══════════════ 4. 可拆判定 ══════════════

    [Fact]
    public void SingleOutputRecipe_IsSalvageable()
        => Assert.True(SalvageLogic.CanSalvage(RecipeBook.Find("chair")!));

    [Fact]
    public void StackedOutputRecipe_IsNotSalvageable_NoAmmoDuplication()
    {
        // 1 子弹零件 → 8 发短子弹。若"拆 1 发"也按整份成本返还，8 发拆完就白赚 4 个零件 —— 无限刷。
        // 故**堆叠产物一律不可拆**（子弹/箭/药茶）。
        Assert.False(SalvageLogic.CanSalvage(RecipeBook.Find("ammo_short")!));
        Assert.False(SalvageLogic.CanSalvage(RecipeBook.Find("ammo_arrow_stick")!));
    }

    [Fact]
    public void ItemWithoutRecipe_IsNotSalvageable()
    {
        // 搜刮来的军用枪/碳纤维箭没有配方 —— 没有"建造成本"可依，拆了只是一地零件。
        Assert.Null(SalvageLogic.RecipeFor("突击步枪"));
        Assert.False(SalvageLogic.CanSalvageKey("突击步枪"));
    }

    [Fact]
    public void CraftableItem_IsSalvageableByKey()
        => Assert.True(SalvageLogic.CanSalvageKey("chair"));

    // ══════════════ 5. 拆解工时（拆比造快一半，但不是白拆）══════════════

    [Fact]
    public void SalvageWorkMinutes_IsHalfOfBuildTime()
    {
        RecipeData chair = RecipeBook.Find("chair")!; // 建造 150 分
        Assert.Equal(75, SalvageLogic.WorkMinutesOf(chair));
    }

    [Fact]
    public void SalvageWorkMinutes_HasFloor()
    {
        // 再小的东西也得拆一会儿（下限 5 分）——不能出现 0 工时的"点击即得"。
        Assert.True(SalvageLogic.WorkMinutesOf(RecipeBook.Find("torch")!) >= SalvageLogic.MinWorkMinutes);
        Assert.Equal(SalvageLogic.MinWorkMinutes, SalvageLogic.WorkMinutesForBuildMinutes(0));
    }

    // ══════════════ 6. 墙：不可拆，只能砸（用户拍板，零回收）══════════════

    [Fact]
    public void Walls_CannotBeSalvaged_OnlySmashed()
    {
        // 用户原话：「墙不可拆」「墙只能破坏」。**四档围栏一个都不许拆。**
        // 这不是体验疏漏：围墙是**不可逆的投入**——建错了位置只能砸掉，材料一点回不来 ⇒ 选址必须想清楚。
        foreach (StructureTier tier in System.Enum.GetValues<StructureTier>())
        {
            if (CampStructureTable.KindOf(tier) != CampStructureKind.Fence)
            {
                continue;
            }

            Assert.False(SalvageLogic.CanSalvageStructure(tier), $"{tier} 是墙，不该拆得动");
            Assert.Empty(SalvageLogic.YieldOfStructure(tier)); // 零回收
        }
    }

    [Fact]
    public void SalvagingAWall_FailsAndGrantsNothing()
    {
        var inv = new InventoryStore();

        SalvageResult r = SalvageService.SalvageStructure(StructureTier.FenceBasic, inv);

        Assert.False(r.Success);
        Assert.Empty(inv.Items); // 一根木料都不掉
    }

    // ══════════════ 7. 门 / 大门：可拆（它们是装上去的东西，卸得下来）══════════════

    [Fact]
    public void Doors_AndGates_AreSalvageable()
    {
        foreach (StructureTier tier in System.Enum.GetValues<StructureTier>())
        {
            CampStructureKind kind = CampStructureTable.KindOf(tier);
            if (kind is CampStructureKind.Door or CampStructureKind.Gate)
            {
                Assert.True(SalvageLogic.CanSalvageStructure(tier), $"{tier} 该拆得动");
                Assert.NotEmpty(SalvageLogic.YieldOfStructure(tier));
            }
        }
    }

    [Fact]
    public void SalvagingAWoodenDoor_RefundsHalf_WithTheWoodException()
    {
        // 木门 = 木料 8 + 钉子 4 ⇒ 木料 2（25%）+ 废木料 2（25%）+ 钉子 2（50%）。
        var inv = new InventoryStore();

        SalvageResult r = SalvageService.SalvageStructure(StructureTier.DoorWood, inv);

        Assert.True(r.Success);
        Assert.Equal(2, inv.MaterialCount(SalvageLogic.WoodKey));
        Assert.Equal(2, inv.MaterialCount(SalvageLogic.ScrapWoodKey));
        Assert.Equal(2, inv.MaterialCount("nails"));
    }

    [Fact]
    public void EveryStructureTier_HasBuildCostAndBuildTime()
    {
        // 围栏的成本表仍在——但它服务的是**升级**（基础→加固→铁皮→全金属），不是新建，更不是拆除。
        foreach (StructureTier tier in System.Enum.GetValues<StructureTier>())
        {
            Assert.NotEmpty(StructureBuildCost.Of(tier));
            Assert.True(StructureBuildCost.BuildMinutes(tier) > 0);
            Assert.All(StructureBuildCost.Of(tier), kv => Assert.True(Materials.Has(kv.Key), $"{tier} 用了不存在的材料 {kv.Key}"));
        }
    }

    // ══════════════ 8. 家具：可拆（用户例子里那 16 木料就落在工作台上）══════════════

    [Fact]
    public void Workbench_Costs16Wood_MatchesTheUsersExample()
        => Assert.Equal(16, FurnitureBuildCost.Of("工作台")![SalvageLogic.WoodKey]);

    [Fact]
    public void SalvagingTheWorkbench_Yields4Wood_And4ScrapWood()
    {
        // 用户原话：「建造需要 16 木材，拆除会获得 4 木材和 4 废木料」。
        IReadOnlyDictionary<string, int> yield = SalvageLogic.YieldOfFurniture("工作台");

        Assert.Equal(4, yield[SalvageLogic.WoodKey]);
        Assert.Equal(4, yield[SalvageLogic.ScrapWoodKey]);
    }

    [Fact]
    public void SalvagingFurniture_PutsMaterialsInTheStore()
    {
        var inv = new InventoryStore();

        SalvageResult r = SalvageService.SalvageFurniture("住宅-柜子", inv);

        Assert.True(r.Success);
        Assert.Equal(2, inv.MaterialCount(SalvageLogic.WoodKey));      // 木料 10 → 25% = 2
        Assert.Equal(2, inv.MaterialCount(SalvageLogic.ScrapWoodKey)); // 木料 10 → 25% = 2
        Assert.Equal(3, inv.MaterialCount("nails"));                   // 钉子 6 → 50% = 3
    }

    [Fact]
    public void ThingsThatWereNeverBuilt_CannotBeSalvaged()
    {
        // 收音机/废墟/尸体不是"造出来"的东西——没有建造成本可依，拆不动（同"没有配方的物品拆不了"）。
        Assert.False(SalvageLogic.CanSalvageFurniture("收音机"));
        Assert.False(SalvageLogic.CanSalvageFurniture("祖母的尸体"));
        Assert.False(SalvageService.SalvageFurniture("收音机", new InventoryStore()).Success);
    }

    [Fact]
    public void EveryFurniture_UsesRealMaterials()
    {
        foreach (string key in FurnitureBuildCost.All)
        {
            Assert.All(FurnitureBuildCost.Of(key)!, kv => Assert.True(Materials.Has(kv.Key), $"{key} 用了不存在的材料 {kv.Key}"));
            Assert.True(FurnitureBuildCost.BuildMinutes(key) > 0);
        }
    }

    // ══════════════ 9. 物品：装备 / 武器 / 狗装备（凡是有配方的都能拆）══════════════

    [Fact]
    public void Apparel_Weapons_AndDogGear_AreAllSalvageable()
    {
        // 用户澄清：「拆除指的不仅仅是墙，也可以是物品」⇒ 规则是**制作的逆运算**，一套逻辑通吃。
        Assert.True(SalvageLogic.CanSalvageKey("cloth_vest"));            // 缝制的护甲
        Assert.True(SalvageLogic.CanSalvageKey("cloth_jacket"));          // 布夹克
        Assert.True(SalvageLogic.CanSalvageKey("handmade_bow"));          // 短弓
        Assert.True(SalvageLogic.CanSalvageKey("improvised_hunting_gun")); // 自制猎枪
        Assert.True(SalvageLogic.CanSalvageKey("torch"));                  // 火把
        Assert.True(SalvageLogic.CanSalvageKey("布制狗衣"));                // 狗装备
    }

    [Fact]
    public void SalvagingAClothJacket_RefundsHalfTheCloth()
    {
        // 布夹克 = 布 6 ⇒ 拆得布 3（布不是木材，走通用 50%）。
        var inv = new InventoryStore();
        inv.Add(Item.Armor("cloth_jacket"));

        SalvageResult r = SalvageService.Salvage("cloth_jacket", inv);

        Assert.True(r.Success);
        Assert.Equal(3, inv.MaterialCount("cloth"));
    }

    [Fact]
    public void UncraftableHighEndGear_IsProtected()
    {
        // 搜刮来的高端货（军用步枪/碳纤维箭/竞技复合弓）**拆不了** —— 你不知道它是怎么造的，
        // 自然也不知道怎么完好地拆。这同时保护了不可制作物的稀有性（否则玩家会拆掉捡到的步枪换材料）。
        Assert.False(SalvageLogic.CanSalvageKey("ammo_arrow_carbon"));
        Assert.False(SalvageLogic.CanSalvageKey("竞技复合弓"));
        Assert.False(SalvageLogic.CanSalvageKey("突击步枪"));
    }

    // ══════════════ 7. 新材料：废木料 / 胶水 ══════════════

    [Fact]
    public void ScrapWood_AndGlue_AreInTheMaterialCatalog()
    {
        Assert.True(Materials.Has(SalvageLogic.ScrapWoodKey));
        Assert.True(Materials.Has(SalvageLogic.GlueKey));
        Assert.Equal("废木料", Materials.Find(SalvageLogic.ScrapWoodKey)!.Value.DisplayName);
        Assert.Equal("胶水", Materials.Find(SalvageLogic.GlueKey)!.Value.DisplayName);
        Assert.Equal(MaterialCategory.Wood, Materials.Find(SalvageLogic.ScrapWoodKey)!.Value.Category);
        Assert.Equal(MaterialCategory.Chemical, Materials.Find(SalvageLogic.GlueKey)!.Value.Category);
    }

    [Fact]
    public void NewMaterials_HaveFlavorText()
    {
        Assert.False(string.IsNullOrWhiteSpace(Materials.Find(SalvageLogic.ScrapWoodKey)!.Value.Description));
        Assert.False(string.IsNullOrWhiteSpace(Materials.Find(SalvageLogic.GlueKey)!.Value.Description));
    }

    [Fact]
    public void NewMaterials_HaveRegisteredWeights_NotTheFallback()
    {
        // 负重上限 80kg 刚落地：新材料必须有自己的重量，不能吃 0.5kg 兜底。
        Assert.Equal(1.0, ItemWeights.MaterialKg(SalvageLogic.ScrapWoodKey));
        Assert.Equal(0.5, ItemWeights.MaterialKg(SalvageLogic.GlueKey));
        // 🔴 [T68] 用户把整根木料从 2.0 减到 1.0kg ⇒ 与废木料（碎料 1.0）**打平了**。
        //    原不变量"废木料严格轻于整根木料"被木料减重抹平 ⇒ 改为**不重于**（≤）。
        //    （废木料本身没被用户改动；这是木料减半的连带后果，非本单主动调整。）
        Assert.True(ItemWeights.MaterialKg(SalvageLogic.ScrapWoodKey) <= ItemWeights.MaterialKg(SalvageLogic.WoodKey));
    }

    // ══════════════ 8. 胶水的获取：能造，但刻意贵（"别让胶水遍地都是"）══════════════

    [Fact]
    public void GlueRecipe_Exists_AndEatsFuel()
    {
        // 熬一锅胶：骨头 + **燃料**。燃料同时是火把/发电机/火药/全部枪弹的命根子 ——
        // 「这罐胶是拿去回收木料，还是留着熬火药」由此成为真的选择，而不是白设计的税。
        RecipeData? r = RecipeBook.Find(SalvageLogic.GlueRecipeId);

        Assert.NotNull(r);
        Assert.Equal(SalvageLogic.GlueKey, r!.OutputKey);
        Assert.True(r.MaterialCosts["fuel"] > 0, "胶水必须吃燃料——否则它就不是瓶颈资源");
        Assert.True(r.MaterialCosts["bone"] > 0);
    }

    [Fact]
    public void GlueRecipe_IsGatedByBeakerAndChemistryBook()
    {
        // 前期做不出胶水（没烧杯、没读化学书）→ 开局那几段拆错位置的墙，木料**就是回不满**。
        RecipeData r = RecipeBook.Find(SalvageLogic.GlueRecipeId)!;

        Assert.Contains(ToolSlot.Beaker, r.RequiredTools);
        Assert.Contains(RecipeBook.FolkChemistryNotesBookId, r.RequiredBookIds);
    }

    [Fact]
    public void Glue_IsNotEverywhere_ScarcityGuard()
    {
        // 守门测试：胶水**只有一条**配方产出；任何人日后想再加一条"胶水速成"，先过这一关。
        Assert.Single(RecipeBook.All.Where(r => r.OutputKey == SalvageLogic.GlueKey));
    }

    [Fact]
    public void GlueRecipe_IsNamedAfterItsProduct_CraftedAndScavengedLookLikeOneThing()
    {
        // **自己熬的和搜刮来的，是同一样东西**——它们本来就共用同一个材料键（glue），
        // 但配方从前叫「熬骨胶」，制作菜单里于是冒出一个玩家在库存里从没见过的名字，
        // 看上去像是两种材料。⇒ 配方名 = 产物名 = 「胶水」，界面上也归一。
        RecipeData r = RecipeBook.Find(SalvageLogic.GlueRecipeId)!;

        Assert.Equal("胶水", Materials.Find(SalvageLogic.GlueKey)!.Value.DisplayName);
        Assert.Equal("胶水", r.DisplayName);
    }

    [Fact]
    public void NoRecipe_IsStillCalledBoneGlue()
    {
        // 谁把它改回「熬骨胶」，这里就红——那正是玩家分不清"造的"和"搜的"的根源。
        Assert.DoesNotContain(RecipeBook.All, r => r.DisplayName.Contains("熬骨胶"));
    }

    // ══════════════ 9. 实扣实产（SalvageService：判定纯函数，库存由服务实动）══════════════

    [Fact]
    public void Salvage_RemovesTheItem_AndAddsRefundedMaterials()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon("chair")); // 一件造出来的东西（键=配方 OutputKey）

        SalvageResult result = SalvageService.Salvage("chair", inv);

        Assert.True(result.Success);
        Assert.Empty(inv.Items.Where(i => i.RefKey == "chair"));
        Assert.Equal(1, inv.MaterialCount(SalvageLogic.WoodKey));       // 木料 4 → 25% = 1
        Assert.Equal(1, inv.MaterialCount(SalvageLogic.ScrapWoodKey));  // 木料 4 → 25% = 1
        Assert.Equal(1, inv.MaterialCount("nails"));                    // 钉子 2 → 50% = 1
    }

    [Fact]
    public void Salvage_UnsalvageableItem_LeavesInventoryUntouched()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon("突击步枪"));

        SalvageResult result = SalvageService.Salvage("突击步枪", inv);

        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
        Assert.Single(inv.Items); // 库存原样不动
    }

    [Fact]
    public void Salvage_ItemNotInInventory_Fails()
    {
        var inv = new InventoryStore();

        Assert.False(SalvageService.Salvage("chair", inv).Success);
    }

    [Fact]
    public void EquippedGear_IsNotSalvageable_UntilTakenOff()
    {
        // **"正穿着的装备怎么拆"这个问题不需要新规则**：装备上身时就已从共享库存移除（CampMain 装备接线），
        // 所以它压根不在库存里 ⇒ 库存面板上没有它、也就没有「拆解」按钮 ⇒ **天然是"先卸下才能拆"**。
        // 这里把这条钉死：库存里没有的东西，服务层一律拆不动（不会凭空从某人身上扒下来拆掉）。
        var inv = new InventoryStore();

        SalvageResult r = SalvageService.Salvage("cloth_jacket", inv);

        Assert.False(r.Success);
        Assert.Empty(inv.Items);
    }

    [Fact]
    public void Salvage_ReportsWorkMinutes_ForTheJobQueue()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon("chair"));

        SalvageResult result = SalvageService.Salvage("chair", inv);

        Assert.Equal(75, result.WorkMinutes); // 木椅建造 150 → 拆 75
    }

    // ══════════════ 10. 工时队列复用：拆解借 CraftingJob 表达（一座工作台一次只干一件事）══════════════

    [Fact]
    public void SalvageJob_ReusesTheCraftingQueue_AndRoundTripsTheItemKey()
    {
        string jobId = SalvageLogic.JobIdFor("chair");

        Assert.True(SalvageLogic.IsSalvageJob(jobId));
        Assert.Equal("chair", SalvageLogic.TargetKeyOf(jobId));
        Assert.Null(RecipeBook.Find(jobId)); // 拆解任务 id 查不到配方 —— 调用方据此分流到拆解结算
    }

    [Fact]
    public void CraftingJobId_IsNotMistakenForSalvage()
    {
        Assert.False(SalvageLogic.IsSalvageJob("chair"));
        Assert.Null(SalvageLogic.TargetKeyOf("chair"));
    }

    [Fact]
    public void SalvageJob_AdvancesOnlyWhenWorkable_AndCompletes()
    {
        var job = new CraftingJob(SalvageLogic.JobIdFor("chair"), 75);

        Assert.Equal(0, job.Advance(30, canWork: false)); // 人被袭营拉走：不推进，不丢进度
        Assert.False(job.IsComplete);

        job.Advance(50, canWork: true);
        Assert.False(job.IsComplete);
        job.Advance(50, canWork: true);
        Assert.True(job.IsComplete);
        Assert.Equal(75, job.ElapsedWorkMinutes); // 封顶总工时
    }

    // ══════════════ 11. 开工/完工两段（对齐制作的"开工即扣、完工才出货"）══════════════

    [Fact]
    public void StartSalvage_TakesTheItemAway_ButRefundsNothingYet()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon("chair"));

        SalvageResult started = SalvageService.StartSalvage("chair", inv);

        Assert.True(started.Success);
        Assert.Equal(75, started.WorkMinutes);
        Assert.Empty(inv.Items);                                  // 东西已拿走（锁定，防重复下单）
        Assert.Equal(0, inv.MaterialCount(SalvageLogic.WoodKey)); // 但材料还没到手——得先把它拆完
    }

    [Fact]
    public void CompleteSalvage_PaysOutTheRefund()
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon("chair"));
        SalvageService.StartSalvage("chair", inv);

        SalvageResult done = SalvageService.CompleteSalvage("chair", inv);

        Assert.True(done.Success);
        Assert.Equal(1, inv.MaterialCount(SalvageLogic.WoodKey));
        Assert.Equal(1, inv.MaterialCount(SalvageLogic.ScrapWoodKey));
        Assert.Equal(1, inv.MaterialCount("nails"));
    }
}
