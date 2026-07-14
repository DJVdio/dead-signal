using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// [T46·impl-iron] 存档 v2 → v3 迁移：「废金属」+「金属锭」合并为「铁」。
///
/// <para>
/// <b>为什么这次破例写了迁移</b>（<see cref="SaveCodec"/> 的类注释说"只拒绝不迁移"）：
/// 老档里这两样是**纯数量**，合并是确定的算术（铁 = 废×1 + 锭×2），不存在"猜一个不知道的字段"的半兼容风险。
/// 直接作废老档反而会**吞掉玩家攒的金属**——而这次改动的起因恰恰是"金属锭是个拿不到的死物品"这个 bug，
/// 拿一个 bug 的修复去作废玩家的存档，说不过去。
/// </para>
/// </summary>
public class SaveIronMigrationTests
{
    /// <summary>手搓一份 <b>v2</b> 存档 JSON：库存里 废金属×N + 金属锭×M，外加一条不相干的材料（木料）当对照。</summary>
    private static string V2SaveWithMetals(int scrap, int ingot) => $$"""
    {
      "Version": 2,
      "Meta": { "Label": "老档" },
      "Camp": {
        "Inventory": [
          { "Category": "Material", "DisplayName": "木料", "Description": "劈好的木料。", "RefKey": "wood", "FoodQuantity": 0, "MaterialQuantity": 7 },
          { "Category": "Material", "DisplayName": "废金属", "Description": "锈迹斑斑的金属废料。", "RefKey": "scrap_metal", "FoodQuantity": 0, "MaterialQuantity": {{scrap}} },
          { "Category": "Material", "DisplayName": "金属锭", "Description": "熔炼提纯的金属锭。", "RefKey": "metal_ingot", "FoodQuantity": 0, "MaterialQuantity": {{ingot}} }
        ]
      }
    }
    """;

    // ---- 核心：老档不许掉物品 ----

    [Fact]
    public void V2存档能读出来_不再被版本闸门拒掉()
    {
        SaveLoadResult r = SaveCodec.Deserialize(V2SaveWithMetals(scrap: 5, ingot: 3));

        Assert.True(r.Ok, $"v2 老档应当能经迁移读出来，实际被拒：{r.Error}");
        Assert.Equal(SaveCodec.CurrentVersion, r.Data!.Version);
    }

    [Fact]
    public void 废金属与金属锭合并成一堆铁_数量为_废加锭乘换算率()
    {
        // 废 5 + 锭 3 ⇒ 铁 5 + 3×2 = 11
        SaveLoadResult r = SaveCodec.Deserialize(V2SaveWithMetals(scrap: 5, ingot: 3));
        Assert.True(r.Ok, r.Error);

        List<ItemSave> inv = r.Data!.Camp.Inventory;

        // 一堆铁，不是两堆——UI 上不该出现两行「铁」
        List<ItemSave> irons = inv.Where(i => i.RefKey == Materials.IronKey).ToList();
        Assert.Single(irons);
        Assert.Equal(5 + 3 * SaveMigration.IngotToIronRatio, irons[0].MaterialQuantity);
        Assert.Equal("铁", irons[0].DisplayName);

        // 🔴 老键必须彻底消失：留一个 scrap_metal 在库存里 = 一个扣不掉、卖不掉、只占负重的死物
        Assert.DoesNotContain(inv, i => i.RefKey == "scrap_metal" || i.RefKey == "metal_ingot");

        // 不相干的材料一根汗毛都不能动
        ItemSave wood = Assert.Single(inv, i => i.RefKey == "wood");
        Assert.Equal(7, wood.MaterialQuantity);
    }

    [Fact]
    public void 只有废金属没有金属锭的老档_也能迁移()
    {
        // 这才是**真实**老档的样子：金属锭没有任何获取途径 ⇒ 谁的存档里都不会有它（M 恒为 0）
        string v2 = """
        {
          "Version": 2,
          "Camp": { "Inventory": [
            { "Category": "Material", "DisplayName": "废金属", "Description": "锈的。", "RefKey": "scrap_metal", "FoodQuantity": 0, "MaterialQuantity": 9 }
          ] }
        }
        """;

        SaveLoadResult r = SaveCodec.Deserialize(v2);
        Assert.True(r.Ok, r.Error);

        ItemSave iron = Assert.Single(r.Data!.Camp.Inventory);
        Assert.Equal(Materials.IronKey, iron.RefKey);
        Assert.Equal(9, iron.MaterialQuantity);   // 1:1，一克不少
    }

    // ---- 材料键藏在存档的好几个角落，一个都不能漏 ----

    [Fact]
    public void 容器藏物_尸体身上_远征背包里的废金属_也要一起迁移()
    {
        string v2 = """
        {
          "Version": 2,
          "Camp": {
            "Inventory": [],
            "ContainerLoot": {
              "柜子#1": [ { "Kind": "Material", "Quantity": 2, "RefId": "scrap_metal" } ]
            }
          },
          "Corpses": { "PhaseTick": 3, "NextId": 2, "Corpses": [
            { "ContainerId": "尸体#1", "Loot": [ { "Kind": "Material", "Quantity": 1, "RefId": "metal_ingot" } ] }
          ] },
          "Expedition": { "Bag": [ { "Kind": "Material", "Quantity": 4, "RefId": "scrap_metal" } ] }
        }
        """;

        SaveLoadResult r = SaveCodec.Deserialize(v2);
        Assert.True(r.Ok, r.Error);
        SaveData d = r.Data!;

        // 容器里搜了一半的废金属
        LootItem inCupboard = Assert.Single(d.Camp.ContainerLoot["柜子#1"]);
        Assert.Equal(Materials.IronKey, inCupboard.RefId);
        Assert.Equal(2, inCupboard.Quantity);

        // 尸体身上还没扒走的金属锭 ⇒ ×2
        LootItem onCorpse = Assert.Single(d.Corpses.Corpses[0].Loot);
        Assert.Equal(Materials.IronKey, onCorpse.RefId);
        Assert.Equal(1 * SaveMigration.IngotToIronRatio, onCorpse.Quantity);

        // 远征背包
        LootItem inBag = Assert.Single(d.Expedition.Bag);
        Assert.Equal(Materials.IronKey, inBag.RefId);
        Assert.Equal(4, inBag.Quantity);
    }

    // ---- 兜底：不许静默吞物品 ----

    [Fact]
    public void 迁移不了的更老版本_明确拒读_不装作读成功()
    {
        string v1 = """{ "Version": 1, "Camp": { "Inventory": [] } }""";

        SaveLoadResult r = SaveCodec.Deserialize(v1);

        Assert.False(r.Ok);
        Assert.Contains("v1", r.Error);   // 人话报错，不是异常堆栈
    }

    [Fact]
    public void 未来版本的存档_照旧拒读()
    {
        string vNext = $$"""{ "Version": {{SaveCodec.CurrentVersion + 1}}, "Camp": { "Inventory": [] } }""";

        Assert.False(SaveCodec.Deserialize(vNext).Ok);
    }

    /// <summary>
    /// 🔴 **静默失效护栏**：<c>SaveManager</c> 拿 <see cref="SaveCodec.IsCompatible"/> 去给存档列表的"读取"按钮置灰。
    /// 迁移写了、但 IsCompatible 仍对 v2 返回 false ⇒ 按钮永远是灰的，**迁移代码一行都跑不到**。
    /// </summary>
    [Fact]
    public void 可迁移的老档_读取按钮不能置灰()
    {
        Assert.True(SaveCodec.IsCompatible(V2SaveWithMetals(scrap: 1, ingot: 1)));
        Assert.False(SaveCodec.IsCompatible("""{ "Version": 1 }"""));   // 迁不动的照旧置灰
    }

    // ---- 往返：迁移过的档再存再读，必须稳定 ----

    [Fact]
    public void 迁移后的档再存再读_不再二次迁移_数量不变()
    {
        SaveData once = SaveCodec.Deserialize(V2SaveWithMetals(scrap: 5, ingot: 3)).Data!;

        SaveLoadResult twice = SaveCodec.Deserialize(SaveCodec.Serialize(once));
        Assert.True(twice.Ok, twice.Error);

        ItemSave iron = Assert.Single(twice.Data!.Camp.Inventory, i => i.RefKey == Materials.IronKey);
        Assert.Equal(11, iron.MaterialQuantity);   // 没有被二次 ×2
    }
}
