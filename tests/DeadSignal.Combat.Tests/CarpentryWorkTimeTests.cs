using System;
using System.Linq;
using System.Numerics;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 《木匠入门》「制作家具速度 +5%」（<see cref="CraftWorkTime"/>）+ 新家具「桌子」（<see cref="TableSpec"/>）的护栏。
///
/// <para>
/// <b>为什么需要一条新的工时轴</b>：工时制此前是**死数**（<c>Recipe.WorkMinutes × Times</c>），项目里唯一的"干活快慢"
/// 乘子（操作能力 × 道格光环 × 疲劳 × 山姆光环）全挂在**推进侧**（CampMain.TickCraftingWorktime 的 <c>mult</c>，
/// 乘的是"每分钟投入多少工时"）。书的加成挂在**总工时侧**（这张活总共要多少工时），两条轴互不覆盖、**天然连乘**。
/// </para>
///
/// <para><b>乘算，禁止加算</b>（项目铁律）：<see cref="CraftWorkTime.MultiplierFor"/> 把每一条工时加成**连乘**起来。</para>
/// </summary>
public class CarpentryWorkTimeTests
{
    private static readonly Func<string, bool> NoBooks = _ => false;
    private static readonly Func<string, bool> CarpentryRead =
        id => id == RecipeBook.CarpentryBasicsBookId;

    private static RecipeData R(string id) => RecipeBook.Find(id)!;

    // ──────────────── 零回归：没读书 = 一分钟都不能变 ────────────────

    [Fact]
    public void 没读木匠入门_全表工时逐张不变_零回归()
    {
        foreach (RecipeData r in RecipeBook.All)
        {
            Assert.Equal(r.WorkMinutes, CraftWorkTime.TotalMinutes(r, NoBooks, times: 1));
            Assert.Equal(r.WorkMinutes * 3, CraftWorkTime.TotalMinutes(r, NoBooks, times: 3));
            Assert.Equal(1.0, CraftWorkTime.MultiplierFor(r, NoBooks), 9);
        }
    }

    // ──────────────── 家具 +5%：工时 ×0.95 ────────────────

    [Fact]
    public void 读过木匠入门_做家具的工时乘0点95()
    {
        Assert.Equal(0.95, CraftWorkTime.MultiplierFor(R("bed"), CarpentryRead), 9);
        Assert.Equal(142, CraftWorkTime.TotalMinutes(R("bed"), CarpentryRead, times: 1));   // 150 × 0.95 = 142.5 → 向下取整
        Assert.Equal(142, CraftWorkTime.TotalMinutes(R("chair"), CarpentryRead, times: 1)); // 木椅同 150
        Assert.Equal(57, CraftWorkTime.TotalMinutes(R("bench"), CarpentryRead, times: 1));  // 板凳 60 × 0.95
    }

    [Fact]
    public void 批量下单_倍数与折扣同时生效()
    {
        // 先按倍数放大总工时，再乘折扣（285 = ⌊150 × 2 × 0.95⌋）——不是"每件各自取整再相加"。
        Assert.Equal(285, CraftWorkTime.TotalMinutes(R("bed"), CarpentryRead, times: 2));
    }

    [Fact]
    public void 折扣向下取整_且至少留一分钟()
    {
        Assert.Equal(0, CraftWorkTime.TotalMinutes(R("bed") with { WorkMinutes = 0 }, CarpentryRead, 1));  // 零工时配方仍是零
        Assert.Equal(1, CraftWorkTime.TotalMinutes(R("bed") with { WorkMinutes = 1 }, CarpentryRead, 1));  // ⌊0.95⌋=0 → 兜到 1
    }

    // ──────────────── 「家具类」的边界：谁吃这 5%，谁不吃 ────────────────

    [Fact]
    public void 家具类配方_包含板凳木椅沙发床桌子()
    {
        Assert.Equal(
            new[] { "bed", "bench", "chair", SofaSpec.RecipeId, TableSpec.RecipeId }.OrderBy(x => x),
            CraftWorkTime.FurnitureRecipeIds.OrderBy(x => x));
    }

    [Fact]
    public void 家具类配方_每一张都必须是木工类且产物是家具实体()
    {
        // 「家具类」= 木工活（Woodwork）× 产物真的会摆到营地地上。两个条件同时成立才算。
        foreach (string id in CraftWorkTime.FurnitureRecipeIds)
        {
            RecipeData r = RecipeBook.Find(id)!;
            Assert.Equal(RecipeCategory.Woodwork, r.Category);
            Assert.True(CraftWorkTime.IsFurnitureRecipe(r), $"{id} 该算家具");
        }
    }

    [Fact]
    public void 木工类但产物不是家具_不吃这5百分号()
    {
        // 废木料回收：Woodwork，但产物是**木料**（材料堆），不是家具。木匠再快也不该让"粘木板"变快 5%。
        RecipeData scrap = R("wood_from_scrap");
        Assert.Equal(RecipeCategory.Woodwork, scrap.Category);
        Assert.False(CraftWorkTime.IsFurnitureRecipe(scrap));
        Assert.Equal(scrap.WorkMinutes, CraftWorkTime.TotalMinutes(scrap, CarpentryRead, 1));
    }

    [Fact]
    public void 沙袋与两台设施_不算家具_读了木匠入门也不减工时()
    {
        // 沙袋 = 布 + 石料（往麻袋里铲土不是木工活）；改装台/烹饪台 = 固定锚点的大型设施，且都不是木工类。
        foreach (string id in new[] { "sandbag", "mod_bench", "cook_station" })
        {
            RecipeData r = R(id);
            Assert.False(CraftWorkTime.IsFurnitureRecipe(r), $"{id} 不该算家具");
            Assert.Equal(r.WorkMinutes, CraftWorkTime.TotalMinutes(r, CarpentryRead, 1));
        }
    }

    /// <summary>
    /// 短弓<b>已不归《木匠入门》管</b>（[SPEC-B21·T26]：用户在 wiki 表里把它挪去了《野外生存指南》），
    /// 但「读了木匠书 ⇒ 弓也提速 5%」这个坑<b>仍然必须堵住</b> —— 那 5% 是<b>家具</b>加成，
    /// 而弓不是家具。<b>门槛与加成是两回事，别混</b>（本条原本就是在钉这一点，归属变了它依然成立）。
    /// </summary>
    [Fact]
    public void 短弓不是家具_读了木匠入门也不吃这5百分号()
    {
        RecipeData bow = R("handmade_bow");
        Assert.DoesNotContain(RecipeBook.CarpentryBasicsBookId, bow.RequiredBookIds);   // 归属已挪走
        Assert.False(CraftWorkTime.IsFurnitureRecipe(bow));
        Assert.Equal(bow.WorkMinutes, CraftWorkTime.TotalMinutes(bow, CarpentryRead, 1));
    }

    [Fact]
    public void 读了别的书_家具工时照旧()
    {
        Func<string, bool> tailor = id => id == RecipeBook.TailorsNotesBookId;
        Assert.Equal(150, CraftWorkTime.TotalMinutes(R("bed"), tailor, 1));
    }

    // ──────────────── 《进阶木匠技术》：同样 +5%，与入门**连乘** ────────────────

    private static readonly Func<string, bool> AdvancedRead =
        id => id == RecipeBook.AdvancedCarpentryBookId;
    private static readonly Func<string, bool> BothCarpentryBooks =
        id => id == RecipeBook.CarpentryBasicsBookId || id == RecipeBook.AdvancedCarpentryBookId;

    [Fact]
    public void 读过进阶木匠技术_做家具也快5百分号()
    {
        Assert.Equal(0.95, CraftWorkTime.MultiplierFor(R("bed"), AdvancedRead), 9);
        Assert.Equal(142, CraftWorkTime.TotalMinutes(R("bed"), AdvancedRead, 1));
    }

    [Fact]
    public void 两本木工书都读过_是连乘0点9025_不是加算的0点90()
    {
        // 铁律：百分比一律乘算。0.95 × 0.95 = 0.9025，**不是** 1 − 0.05 − 0.05 = 0.90，也不是"后一条盖前一条"的 0.95。
        // ⚠️ **别靠某张配方的取整工时去区分连乘与加算**：两者只差 0.25%，⌊150 × 0.9025⌋ 与 ⌊150 × 0.90⌋ 都是 135（撞值）。
        // 真正能证伪的是**乘子本身**（未取整）——所以这里断言 MultiplierFor，取整值只作附带核对。
        Assert.Equal(0.9025, CraftWorkTime.MultiplierFor(R("bed"), BothCarpentryBooks), 9);
        Assert.NotEqual(0.90, CraftWorkTime.MultiplierFor(R("bed"), BothCarpentryBooks), 9); // 加算
        Assert.NotEqual(0.95, CraftWorkTime.MultiplierFor(R("bed"), BothCarpentryBooks), 9); // 后一条盖前一条
        Assert.Equal(135, CraftWorkTime.TotalMinutes(R("bed"), BothCarpentryBooks, 1));      // ⌊150 × 0.9025⌋
    }

    [Fact]
    public void 进阶木匠也只对家具生效_弓与废木料回收不吃()
    {
        foreach (string id in new[] { "handmade_bow", "wood_from_scrap", "sandbag" })
        {
            RecipeData r = R(id);
            Assert.Equal(r.WorkMinutes, CraftWorkTime.TotalMinutes(r, BothCarpentryBooks, 1));
        }
    }

    // ──────────────── 乘算护栏：将来加第二条加成必须连乘 ────────────────

    [Fact]
    public void 乘子是连乘_不是取最后一条()
    {
        // 结构性护栏：MultiplierFor 的实现必须是 m *= …（连乘）。这里用"同一条加成叠两次"的假想场景锁住语义：
        // 两条各 0.95 的加成 ⇒ 0.9025，绝不能是 0.95（后者覆盖前者）或 0.90（加算 1-0.05-0.05）。
        Assert.Equal(0.9025, CraftWorkTime.Chain(0.95, 0.95), 9);
        Assert.NotEqual(0.90, CraftWorkTime.Chain(0.95, 0.95), 9);
    }

    // ──────────────── 桌子：配方 / 家具目录 / 可跨越 / 放置 / 存档判据 ────────────────

    [Fact]
    public void 桌子配方_木工类_要锯片和木匠入门()
    {
        RecipeData t = R(TableSpec.RecipeId);
        Assert.Equal("桌子", t.DisplayName);
        Assert.Equal(RecipeCategory.Woodwork, t.Category);
        Assert.Equal(TableSpec.ItemKey, t.OutputKey);
        Assert.Contains(ToolSlot.SawBlade, t.RequiredTools);
        Assert.Contains(RecipeBook.CarpentryBasicsBookId, t.RequiredBookIds);
        Assert.True(t.WorkMinutes > 0);
        Assert.True(t.MaterialCosts["wood"] > 0);
    }

    [Fact]
    public void 桌子在家具目录里_故可拆可回收()
    {
        Assert.NotNull(FurnitureBuildCost.Of(TableSpec.FurnitureKey));
        Assert.NotNull(FurnitureBuildCost.BuildMinutes(TableSpec.FurnitureKey));

        // 建造成本/工时与配方一致（拆除返还按这张表算，两处分叉 = 拆出来的料对不上账）。
        RecipeData t = R(TableSpec.RecipeId);
        Assert.Equal(t.MaterialCosts, FurnitureBuildCost.Of(TableSpec.FurnitureKey));
        Assert.Equal(t.WorkMinutes, FurnitureBuildCost.BuildMinutes(TableSpec.FurnitureKey));
    }

    [Fact]
    public void 场上的桌子带流水号_也查得到成本与减速()
    {
        Assert.NotNull(FurnitureBuildCost.Of("桌子#7"));
        Assert.True(FurnitureTraversal.IsTraversable("桌子#7"));
        Assert.Equal(FurnitureTraversal.CrossingSpeedMultiplier, FurnitureTraversal.SpeedMultiplierOf("桌子#7"), 9);
    }

    [Fact]
    public void 桌子可跨越_跨过减速25百分号_与椅子同类()
    {
        Assert.True(FurnitureTraversal.IsTraversable(TableSpec.FurnitureKey));
        Assert.Equal(0.75, FurnitureTraversal.SpeedMultiplierOf(TableSpec.FurnitureKey), 9);
    }

    [Fact]
    public void 桌子是半身掩体_25百分号远程无伤_且不拦近战()
    {
        // 用户拍板：「躲在**桌子**/椅子/沙袋后，被【远程】攻击有 25% 无伤概率」——桌子是他点了名的掩体。
        // 与沙袋同档（都走 CoverLogic 的默认 25%）；**不拦近战**（矮物，绕过去就能砍——同沙袋/桌椅的既有口径）。
        Assert.Equal(CoverLogic.DefaultCoverChance, TableSpec.CoverChance);
        Assert.Equal(SandbagSpec.CoverChance, TableSpec.CoverChance);
        Assert.False(TableSpec.BlocksMelee);
    }

    [Fact]
    public void 桌子掩体的方向性与双向对称_照搬CoverLogic既有规则()
    {
        // 掩体只在"落在射击者与目标连线上"时生效（绕后即绕掉），且敌我双向对称——这些都是 CoverLogic 的既有规则，
        // 桌子只是往里加了一块矩形，不新增任何例外。这里用一张桌子的实际尺寸跑一遍，确认它确实被算进去了。
        var field = new CoverField();
        field.Add(400f, 300f, TableSpec.Width, TableSpec.Height, TableSpec.CoverChance, TableSpec.BlocksMelee);

        // 桌子占 x∈[400,472]、y∈[300,348]。目标要**贴着桌子但不在桌面上**（CoverLogic：站在桌子上 ≠ 躲在桌子后）。
        var target = new Vector2(436f, 360f);            // 桌子下沿外 12px：紧贴着它
        var infront = new Vector2(436f, 100f);           // 射击者在桌子另一侧 ⇒ 子弹得先穿过桌子
        var behind = new Vector2(436f, 600f);            // 射击者绕到目标背后 ⇒ 桌子不在连线上，白摆

        Assert.Equal(TableSpec.CoverChance, field.ChanceFor(infront, target));
        Assert.Equal(0f, field.ChanceFor(behind, target));
    }

    [Fact]
    public void 床不是掩体_用户原话里没有床()
    {
        // 用户的掩体原话点名的是「桌子 / 椅子 / 沙袋」——**没有床**。躲在床后面挡枪这件事，他没说过，我们就不发明。
        // 结构性证据：BedSpec 里压根没有掩体这个概念（没有 CoverChance/BlocksMelee 字段），
        // 消费层 SpawnBed 也不碰 _coverField —— 想给床开掩体，得先在 BedSpec 里造出这两个字段来（改不动是刻意的）。
        Assert.Empty(typeof(BedSpec).GetFields().Where(f => f.Name.Contains("Cover")));
    }

    [Fact]
    public void 桌子非实心_不挖导航洞_摆不出killbox()
    {
        Assert.False(TableSpec.IsSolid);
        Assert.False(TableSpec.CarvesNavHole);
    }

    [Fact]
    public void 桌子放置_守64px禁建带_不许贴围栏()
    {
        var bounds = new PlacementRules.Box(0, 0, 1000, 1000);
        var fence = new PlacementRules.Box(500, 0, 20, 1000);      // 一段南北向围栏
        var defenses = new[] { fence };
        var none = Array.Empty<PlacementRules.Box>();

        Assert.False(TableSpec.PlaceSpec.AllowedAgainstDefenses); // 缺省受约束（沙袋是唯一豁免）

        // [T27] 本组测的是**禁建带**，不是"家具只能室内"那条 —— 故把整片测试区当成室内，
        // 免得桌子先撞上 OutdoorsNotAllowed 而测不到想测的东西。室内那条另有专测。
        var indoors = new[] { bounds };

        // 贴着围栏放 → 拒（缓冲带 64px）
        Assert.Equal(
            PlacementVerdict.TooCloseToDefenses,
            PlacementRules.CanPlace(TableSpec.PlaceSpec, new System.Numerics.Vector2(560, 500), bounds, defenses, none, none, indoors));

        // 离远点 → 放得下
        Assert.Equal(
            PlacementVerdict.Ok,
            PlacementRules.CanPlace(TableSpec.PlaceSpec, new System.Numerics.Vector2(800, 500), bounds, defenses, none, none, indoors));
    }

    [Fact]
    public void 桌子进存档的判据_只认玩家摆的那些带流水号的()
    {
        Assert.True(TableSpec.IsTableFurniture("桌子#3"));
        Assert.False(TableSpec.IsTableFurniture("桌子"));   // 类型名不是场上实例
        Assert.False(TableSpec.IsTableFurniture("床#3"));
        Assert.False(TableSpec.IsTableFurniture(null));
    }

    [Fact]
    public void 桌子是可摆放物_床与沙袋也是()
    {
        // 床此前**漏了**「摆放」按钮（StashPanel 只认沙袋）⇒ 造出来的床摆不下去。这条同时是那个洞的回归护栏。
        Assert.True(PlaceableItems.IsPlaceable(TableSpec.ItemKey));
        Assert.True(PlaceableItems.IsPlaceable(BedSpec.ItemKey));
        Assert.True(PlaceableItems.IsPlaceable(SandbagSpec.ItemKey));

        // 固定锚点设施**不是**可摆放物（摆放按钮会把已撤掉的 kill box 风险引回来）。
        Assert.False(PlaceableItems.IsPlaceable(WeaponModLogic.BenchItemKey));
        Assert.False(PlaceableItems.IsPlaceable(CookStation.ItemKey));
        Assert.False(PlaceableItems.IsPlaceable("wood"));
    }

    [Fact]
    public void 桌子产物_落地为一件可摆放的杂项堆_且有描述()
    {
        Item item = CraftOutputFactory.Create(TableSpec.ItemKey, 1).Single();
        Assert.Equal(TableSpec.ItemKey, item.RefKey);
        Assert.Equal("桌子", item.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(item.Description));
    }

    // ──────────────── 真正的落点：开工时的在制品总工时必须吃到这条轴 ────────────────

    private static (WorkbenchState Bench, InventoryStore Inv) Woodshop()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.SawBlade);
        var inv = new InventoryStore();
        inv.Add(Item.Material("wood", "木料", 100));
        inv.Add(Item.Material("cloth", "布", 40));
        inv.Add(Item.Material("nails", "钉子", 60));
        return (bench, inv);
    }

    [Fact]
    public void 开工_读过木匠入门的人做床_在制品总工时已打折()
    {
        (WorkbenchState bench, InventoryStore inv) = Woodshop();
        CraftStartResult start = CraftingService.StartJob(R("bed"), CarpentryRead, bench, inv);

        Assert.True(start.Success);
        Assert.Equal(142, start.Job!.TotalWorkMinutes); // ⌊150 × 0.95⌋
    }

    [Fact]
    public void 开工_没读木匠入门的人做板凳_总工时一分不减()
    {
        // 板凳是唯一无书门槛的家具（开局就能做）⇒ 它能把"读没读书"这一个变量单独拎出来比。
        // 零回归：书没读过 ⇒ 与工时制上线那天一模一样（60 分）。
        (WorkbenchState bench, InventoryStore inv) = Woodshop();
        CraftStartResult plain = CraftingService.StartJob(R("bench"), NoBooks, bench, inv);
        Assert.True(plain.Success);
        Assert.Equal(60, plain.Job!.TotalWorkMinutes);

        (WorkbenchState bench2, InventoryStore inv2) = Woodshop();
        CraftStartResult read = CraftingService.StartJob(R("bench"), CarpentryRead, bench2, inv2);
        Assert.True(read.Success);
        Assert.Equal(57, read.Job!.TotalWorkMinutes); // ⌊60 × 0.95⌋
    }

    [Fact]
    public void 开工_做桌子_门槛与工时都对得上()
    {
        (WorkbenchState bench, InventoryStore inv) = Woodshop();

        // 没读木匠入门 ⇒ 桌子做不了（书是门槛）。
        CraftStartResult blocked = CraftingService.StartJob(R(TableSpec.RecipeId), NoBooks, bench, inv);
        Assert.False(blocked.Success);
        Assert.Contains(blocked.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);

        // 读了 ⇒ 做得了，且总工时是打过折的那个数。
        CraftStartResult ok = CraftingService.StartJob(R(TableSpec.RecipeId), CarpentryRead, bench, inv);
        Assert.True(ok.Success);
        Assert.Equal(CraftWorkTime.TotalMinutes(R(TableSpec.RecipeId), CarpentryRead, 1), ok.Job!.TotalWorkMinutes);
        Assert.True(ok.Job.TotalWorkMinutes < R(TableSpec.RecipeId).WorkMinutes);
    }
}
