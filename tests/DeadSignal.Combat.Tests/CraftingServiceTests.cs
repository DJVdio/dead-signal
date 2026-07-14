using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

// 制作执行服务（CraftingService）纯逻辑单测：
//   跨堆材料合计/够付/扣减（易错点）、Craft 实扣实产（成功/被门槛挡/批量材料不足）、
//   内置产物工厂（材料 vs 非材料）、ApplyWeaponMod 消耗基础武器入变体/冲突/缺武器（通用技能门槛已删）。

public class CrossStackMaterialTests
{
    private static Item Mat(string key, int qty) => Item.Material(key, key, qty);

    [Fact]
    public void MaterialTotal_SumsAcrossStacks_IgnoringOtherKeysAndCategories()
    {
        var items = new List<Item>
        {
            Mat("wood", 3), Mat("wood", 2), Mat("cloth", 5),
            Item.Food(10), Item.Weapon("匕首"),
        };
        Assert.Equal(5, CraftingService.MaterialTotal(items, "wood"));
        Assert.Equal(5, CraftingService.MaterialTotal(items, "cloth"));
        Assert.Equal(0, CraftingService.MaterialTotal(items, "stone"));
    }

    [Fact]
    public void HasEnough_TrueOnlyWhenEveryDemandMetAcrossStacks()
    {
        var items = new List<Item> { Mat("wood", 3), Mat("wood", 2), Mat("nails", 1) };
        Assert.True(CraftingService.HasEnough(items, new Dictionary<string, int> { ["wood"] = 5, ["nails"] = 1 }));
        Assert.False(CraftingService.HasEnough(items, new Dictionary<string, int> { ["wood"] = 6 }));
        Assert.False(CraftingService.HasEnough(items, new Dictionary<string, int> { ["nails"] = 2 }));
    }

    [Fact]
    public void Deduct_ConsumesAcrossStacks_DropsEmpty_ShrinksPartial_KeepsOthers()
    {
        var items = new List<Item>
        {
            Mat("wood", 3), Mat("wood", 2), Mat("cloth", 5), Item.Food(4),
        };
        // 需要 4 wood：耗尽首堆(3)、次堆扣 1 剩 1；cloth/食物不动。
        IReadOnlyList<Item> after = CraftingService.Deduct(items, new Dictionary<string, int> { ["wood"] = 4 });

        Assert.Equal(1, CraftingService.MaterialTotal(after, "wood"));
        Assert.Equal(5, CraftingService.MaterialTotal(after, "cloth"));
        Assert.Contains(after, i => i.Category == ItemCategory.Food); // 非材料保留
        // 只剩一个 wood 堆（耗尽的那堆被丢弃）。
        Assert.Single(after.Where(i => i.RefKey == "wood"));
    }

    [Fact]
    public void Deduct_ExactlyEmptiesStack_DropsIt()
    {
        var items = new List<Item> { Mat("wood", 2), Mat("wood", 2) };
        IReadOnlyList<Item> after = CraftingService.Deduct(items, new Dictionary<string, int> { ["wood"] = 4 });
        Assert.Empty(after.Where(i => i.RefKey == "wood"));
        Assert.Equal(0, CraftingService.MaterialTotal(after, "wood"));
    }
}

public class CraftExecutionTests
{
    // 一张自造配方：需 Beaker + 读 test_book，材料 wood×3/cloth×1，产出材料 gunpowder×2。
    private static RecipeData MakeRecipe(string output = "gunpowder", int outQty = 2) => new(
        Id: "svc_test",
        DisplayName: "测试物",
        Category: RecipeCategory.Chemistry,
        OutputKey: output,
        OutputQuantity: outQty,
        MaterialCosts: new Dictionary<string, int> { ["wood"] = 3, ["cloth"] = 1 },
        RequiredTools: new HashSet<ToolSlot> { ToolSlot.Beaker },
        RequiredBookIds: new List<string> { "test_book" });

    private static (WorkbenchState bench, InventoryStore inv) Ready()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        var inv = new InventoryStore();
        // 跨两堆凑 wood=4，cloth=2。
        inv.Add(Item.Material("wood", "木料", 3));
        inv.Add(Item.Material("wood", "木料", 1));
        inv.Add(Item.Material("cloth", "布", 2));
        return (bench, inv);
    }

    [Fact]
    public void Craft_Success_DeductsAcrossStacks_ProducesMaterial()
    {
        var (bench, inv) = Ready();
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => true, bench, inv);

        Assert.True(r.Success);
        Assert.Empty(r.Blocks);
        // 扣了 wood×3（4→1）、cloth×1（2→1）。
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "wood"));
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "cloth"));
        // 产出 gunpowder×2（材料堆）。
        Assert.Equal(2, CraftingService.MaterialTotal(inv.Items, "gunpowder"));
        Assert.Contains(r.Produced, i => i.RefKey == "gunpowder" && i.MaterialQuantity == 2);
    }

    [Fact]
    public void Craft_Blocked_MissingTool_LeavesInventoryUntouched()
    {
        var (_, inv) = Ready();
        int before = inv.Count;
        var emptyBench = new WorkbenchState(); // 无 Beaker
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => true, emptyBench, inv);

        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.MissingTool);
        Assert.Equal(before, inv.Count); // 未扣未产
        Assert.Equal(0, CraftingService.MaterialTotal(inv.Items, "gunpowder"));
    }

    [Fact]
    public void Craft_Blocked_UnreadBook()
    {
        var (bench, inv) = Ready();
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => false, bench, inv);
        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.UnreadBook);
    }

    [Fact]
    public void Craft_Batch_InsufficientMaterial_Fails_NoMutation()
    {
        var (bench, inv) = Ready(); // wood=4, cloth=2；单份需 wood3/cloth1
        int before = inv.Count;
        // times=2 需 wood6 → 不够。
        CraftResult r = CraftingService.Craft(MakeRecipe(), _ => true, bench, inv, times: 2);
        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == CraftBlockReason.InsufficientMaterial && b.Key == "wood");
        Assert.Equal(before, inv.Count);
    }

    [Fact]
    public void Craft_DefaultOutput_NonMaterialKey_ProducesWeaponItems()
    {
        var (bench, inv) = Ready();
        // 产出 bone_knife（不在材料目录）×2 → 默认工厂造 2 件武器物品。
        CraftResult r = CraftingService.Craft(MakeRecipe(output: "bone_knife", outQty: 2), _ => true, bench, inv);
        Assert.True(r.Success);
        Assert.Equal(2, r.Produced.Count);
        Assert.All(r.Produced, i => Assert.Equal(ItemCategory.Weapon, i.Category));
        Assert.All(r.Produced, i => Assert.Equal("bone_knife", i.RefKey));
    }

    [Fact]
    public void Craft_CustomOutputFactory_IsUsed()
    {
        var (bench, inv) = Ready();
        CraftResult r = CraftingService.Craft(
            MakeRecipe(output: "cloth_vest", outQty: 1), _ => true, bench, inv,
            outputFactory: (key, qty) => Enumerable.Range(0, qty).Select(_ => Item.Armor(key)));
        Assert.True(r.Success);
        Assert.Single(r.Produced);
        Assert.Equal(ItemCategory.Armor, r.Produced[0].Category);
    }

    [Fact]
    public void Craft_RealRecipe_Gunpowder_EndToEnd()
    {
        var bench = new WorkbenchState();
        bench.InstallTool(ToolSlot.Beaker);
        var inv = new InventoryStore();
        inv.Add(Item.Material("stone", "石料", 2));
        inv.Add(Item.Material("fuel", "燃料", 2));

        RecipeData gp = RecipeBook.Find("gunpowder")!;
        CraftResult r = CraftingService.Craft(gp, _ => true, bench, inv);
        Assert.True(r.Success);
        Assert.Equal(2, CraftingService.MaterialTotal(inv.Items, "gunpowder")); // 产出 2
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "stone"));     // 扣 1
        Assert.Equal(1, CraftingService.MaterialTotal(inv.Items, "fuel"));      // 扣 1
    }
}

[Collection(ModdedWeaponRegistryCollection.Name)]
public class WeaponModExecutionTests
{
    private static InventoryStore InvWith(string weaponName)
    {
        var inv = new InventoryStore();
        inv.Add(Item.Weapon(weaponName));
        return inv;
    }

    // ===== 改装（批次21·T7）：已从"点一下白送"改成 改装台 + 材料 + 工时 的两段式 =====
    // StartWeaponModJob（过门槛 → 开工即扣：拿走基础武器 + 扣材料 → 出在制任务）
    // CompleteWeaponModJob（工时满 → 合成 → 登记进 ModdedWeaponRegistry → 变体入库）

    /// <summary>
    /// 给库存塞够「加重剑柄」的料（铁 1）。
    /// <para>
    /// ⚠️ [T47] 从前这个 helper 塞的是「锋刃研磨」的石料 —— <b>用户已在 wiki 上把锋刃研磨的材料清空了</b>
    /// （它现在是 0 材料 / 60 工时的"出门前磨一次刀"）。⇒ 拿它当"材料门槛"的样本已经不成立
    /// （没料也照样能改）。换成一条**还要材料**的锐器改装：<b>加重剑柄（铁 ×1）</b>。
    /// </para>
    /// </summary>
    private const string BladeModWithCost = "加重剑柄";

    private static InventoryStore InvWithBladeAndIron(string weaponName)
    {
        var inv = InvWith(weaponName);
        inv.Add(Item.Material("iron", "铁", 4));
        return inv;
    }

    [Fact]
    public void WeaponMod_FullFlow_ConsumesBaseAndMaterials_ThenProducesVariant()
    {
        ModdedWeaponRegistry.Clear();
        var inv = InvWithBladeAndIron("短剑");

        WeaponModStartResult start = CraftingService.StartWeaponModJob(
            "短剑", new[] { BladeModWithCost }, inv, hasModBench: true);

        Assert.True(start.Success);
        Assert.NotNull(start.Job);
        Assert.True(start.Job!.TotalWorkMinutes > 0);                    // 改装是工时活，不是点击即得
        Assert.DoesNotContain(inv.Items, i => i.RefKey == "短剑");        // 开工即扣：基础武器已拿走
        Assert.Equal(3, inv.MaterialCount("iron"));                      // 开工即扣：铁 4-1

        // 工时满 → 完工
        start.Job.Advance(start.Job.TotalWorkMinutes, canWork: true);
        Assert.True(start.Job.IsComplete);

        WeaponModResult done = CraftingService.CompleteWeaponModJob(start.Job.RecipeId, inv);
        Assert.True(done.Success);
        Assert.NotNull(done.Produced);
        Assert.Contains(BladeModWithCost, done.Produced!.RefKey);
        Assert.Contains(inv.Items, i => i == done.Produced);

        // 数值确有改动（加重剑柄：伤害 +6%）
        Weapon baseW = WeaponTable.Arsenal().First(w => w.Name == "短剑");
        Assert.True(done.Variant!.Weapon.DamageMax > baseW.DamageMax);

        // 且已登记 ⇒ 装得上、存得住（此前变体名回查落空，是 P0）
        Assert.NotNull(ModdedWeaponRegistry.WeaponByName(done.Produced.RefKey));
    }

    /// <summary>
    /// 🔴 <b>[T47] 覆盖自检：消耗型改装走【真实的制作链】造出来之后，耐久层真的建起来了。</b>
    ///
    /// <para>这条防的是本项目的经典失效模式（<b>纯逻辑绿 ≠ 功能生效</b>）：
    /// <c>WeaponModWearTests</c> 里我是**手动 Register** 的；而游戏里玩家是走
    /// <c>StartWeaponModJob → CompleteWeaponModJob</c> 造出来的。**如果 CraftingService 那条路没经过
    /// 发唯一实例名的 Register，那么玩家真造出来的刀根本不带耐久 —— 砍一万下也不掉**，
    /// 单测却一路绿灯。所以这条必须走真链路。</para>
    /// </summary>
    [Fact]
    public void 锋刃研磨_走真实制作链造出来_耐久层真的建起来了_且砍三下就脱落()
    {
        ModdedWeaponRegistry.Clear();
        var inv = InvWith("短剑");   // 锋刃研磨 0 材料（用户清空）⇒ 只要有台子和工时

        WeaponModStartResult start = CraftingService.StartWeaponModJob(
            "短剑", new[] { "锋刃研磨" }, inv, hasModBench: true);
        Assert.True(start.Success);

        start.Job!.Advance(start.Job.TotalWorkMinutes, canWork: true);
        WeaponModResult done = CraftingService.CompleteWeaponModJob(start.Job.RecipeId, inv);

        Assert.True(done.Success);
        string variant = done.Produced!.RefKey!;

        // ① 唯一实例名（否则两把研磨过的刀会共用一个计数器）
        Assert.Contains("#", variant);
        // ② 库存里那件东西的 RefKey == 武器自己的名字（全项目隐含不变式）
        Assert.Equal(variant, ModdedWeaponRegistry.WeaponByName(variant)!.Name);
        // ③ 耐久层真的建起来了 —— 这才是"功能真的生效"
        Assert.Equal(3, ModdedWeaponRegistry.RemainingUses(variant, "锋刃研磨"));
        // ④ 穿透真的 +75%
        Weapon baseW = WeaponTable.Arsenal().First(w => w.Name == "短剑");
        Assert.Equal(baseW.Penetration * 1.75, done.Variant!.Weapon.Penetration, 6);

        // ⑤ 砍三下 ⇒ 脱落，回落成基础短剑
        Assert.False(ModdedWeaponRegistry.ConsumeUse(variant).Changed);
        Assert.False(ModdedWeaponRegistry.ConsumeUse(variant).Changed);
        ModWearResult broke = ModdedWeaponRegistry.ConsumeUse(variant);
        Assert.True(broke.Changed);
        Assert.Equal("短剑", broke.WeaponName);

        ModdedWeaponRegistry.Clear();
    }

    [Fact]
    public void StartWeaponModJob_WithoutModBench_Fails_NoMutation()
    {
        var inv = InvWithBladeAndIron("短剑");
        int before = inv.Count;

        WeaponModStartResult r = CraftingService.StartWeaponModJob(
            "短剑", new[] { BladeModWithCost }, inv, hasModBench: false);

        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == WeaponModBlockReason.NoModBench);
        Assert.Equal(before, inv.Count);
        Assert.Contains(inv.Items, i => i.RefKey == "短剑");   // 基础武器仍在
        Assert.Equal(4, inv.MaterialCount("iron"));            // 材料一分没动
    }

    [Fact]
    public void StartWeaponModJob_WithoutMaterials_Fails_NoMutation()
    {
        var inv = InvWith("短剑");   // 有台子、有剑，就是没料
        int before = inv.Count;

        // ⚠️ 样本必须是一条**真要材料**的改装（加重剑柄＝铁×1）。用锋刃研磨会误绿：
        //    用户已把它的材料清空，没料也照样能改。
        WeaponModStartResult r = CraftingService.StartWeaponModJob(
            "短剑", new[] { BladeModWithCost }, inv, hasModBench: true);

        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == WeaponModBlockReason.InsufficientMaterial);
        Assert.Equal(before, inv.Count);
        Assert.Contains(inv.Items, i => i.RefKey == "短剑");
    }

    [Fact]
    public void StartWeaponModJob_SamePartConflict_Fails_NoMutation()
    {
        var inv = InvWithBladeAndIron("短剑");
        int before = inv.Count;

        // 锯齿剑刃 与 锋刃研磨 都占"刃"部位 → 冲突。
        WeaponModStartResult r = CraftingService.StartWeaponModJob(
            "短剑", new[] { "锯齿剑刃", "锋刃研磨" }, inv, hasModBench: true);

        Assert.False(r.Success);
        Assert.Contains(r.Blocks, b => b.Reason == WeaponModBlockReason.InvalidCombination);
        Assert.Equal(before, inv.Count);
        Assert.Contains(inv.Items, i => i.RefKey == "短剑"); // 基础武器仍在
    }

    [Fact]
    public void StartWeaponModJob_MissingBaseWeapon_Fails()
    {
        var inv = new InventoryStore(); // 空
        WeaponModStartResult r = CraftingService.StartWeaponModJob(
            "短剑", new[] { "锋刃研磨" }, inv, hasModBench: true);

        Assert.False(r.Success);
        Assert.Contains("短剑", r.FailureText);
    }

    [Fact]
    public void StartWeaponModJob_UnknownModForClass_Fails()
    {
        var inv = InvWithBladeAndIron("短剑");
        // 铁丝强化 是**棍棒独有**的改装（用户拍板），装不到"短剑"上。
        WeaponModStartResult r = CraftingService.StartWeaponModJob(
            "短剑", new[] { "铁丝强化" }, inv, hasModBench: true);

        Assert.False(r.Success);
        Assert.Contains(inv.Items, i => i.RefKey == "短剑"); // 未消耗
    }
}
