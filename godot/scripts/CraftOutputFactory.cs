using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不引入任何 Godot 类型（与 CraftingLogic.cs / CraftingService.cs 一样可被 Tests 以 Link 编入单测）。
// 制作产物分类工厂：修 CraftingService 的默认工厂遗留（默认把非材料产物一律当武器占位）。
// 按产物 key 把内置配方的产出各造对类别——武器 / 护甲 / 材料 / 家具杂项——供营地接入传 outputFactory。
// 家具类（木椅 / 板凳等）不在武器/护甲/材料集 → 自动落末尾"家具/杂项"分支，新增家具配方无需改本表。

/// <summary>
/// 把配方产物 key（<see cref="RecipeData.OutputKey"/>）+ 数量 造成正确类别的库存 <see cref="Item"/>：
/// 火药/鞣制药水=材料堆，骨刀/自制弓=武器，粗布背心=护甲，木椅等=杂项材料堆（暂无家具类别）。
/// 显示名沿本仓惯例取中文（武器/护甲 refKey 亦用中文名，对齐 WeaponTable/ArmorTable 命名；这些草稿产物尚不在表中，作占位引用键，装备校验属下游）。
/// </summary>
public static class CraftOutputFactory
{
    // 产物 key → 大类（草稿，随配方增补）。材料类产物（gunpowder/tanning_solution）不列此表——走 Materials 目录判定。
    private static readonly IReadOnlySet<string> WeaponOutputs = new HashSet<string>
    {
        "bone_knife", "handmade_bow", "improvised_hunting_gun", "improvised_shotgun",
        // 弓弩（可制作的 4 把进阶款；"handmade_bow" 就是「短弓」，键沿用未改）。
        // 竞技复合弓/狩猎弓/复合弩**不在此列，也不该在**——它们没有配方，只能搜刮。
        "recurve_bow", "longbow", "light_crossbow", "heavy_crossbow",
        // [批次25·T44] 消防斧。漏登记它 ⇒ 造出来的消防斧会静默落进最后那条"家具/杂项"分支，
        // 变成一堆**不能装备**的杂物材料（不报错、不崩，只是永远拿不起来）。
        "axe",
    };
    // 箭（4 种）不必登记：它们的产物 key 同时是**材料键**（ammo_arrow_*），
    // 走上面 Materials.Has(outputKey) 那条分支自动落地为一堆材料。
    // 护甲类产物：粗布背心 / 布夹克 + 布鲁斯狗装备五件套（DogGearCatalog 键）。落地为 Item.Armor，
    // RefKey=产物 key（狗装备穿戴走 DogApparelSlots 按此键查 DogGearCatalog）。
    // [批次21·T26] 战争面具 + 粗布衬衫/短裤/长裤：同为护甲产物（RefKey=中文显示名，对齐 ArmorTable/ApparelCatalog）。
    private static readonly IReadOnlySet<string> ArmorOutputs = new HashSet<string>(
        new[]
        {
            "cloth_vest", "cloth_jacket",
            "war_mask", "coarse_shirt", "coarse_shorts", "coarse_trousers",
        }.Concat(DogGearCatalog.AllKeys));
    // 光源类产物（火把）：落地为 Item.Light，refKey=产物 key（对齐 LightSource 目录）。手电不可制作，不列此表。
    private static readonly IReadOnlySet<string> LightOutputs = new HashSet<string> { "torch" };

    /// <summary>产物工厂：传给 <see cref="CraftingService.Craft"/> 的 outputFactory，按 key 分类造 <paramref name="quantity"/> 件产物。</summary>
    public static IEnumerable<Item> Create(string outputKey, int quantity)
    {
        int qty = quantity < 0 ? 0 : quantity;

        // 材料产物（火药/鞣制药水，key 同时是材料标识名）：造一堆该材料。
        if (Materials.Has(outputKey))
        {
            yield return Materials.Find(outputKey)!.Value.ToItem(qty);
            yield break;
        }

        // 显示名取配方名（中文，如「骨刀」「木椅」）；查不到退化为 key。
        string display = RecipeBook.All.FirstOrDefault(r => r.OutputKey == outputKey)?.DisplayName ?? outputKey;

        if (WeaponOutputs.Contains(outputKey))
        {
            for (int i = 0; i < qty; i++) yield return Item.Weapon(display);
            yield break;
        }
        if (ArmorOutputs.Contains(outputKey))
        {
            for (int i = 0; i < qty; i++) yield return Item.Armor(display);
            yield break;
        }
        if (LightOutputs.Contains(outputKey))
        {
            // 光源产物（火把）：refKey=产物 key 指向 LightSource 目录，显示名取配方名（中文「火把」）。
            for (int i = 0; i < qty; i++) yield return Item.Light(outputKey, display);
            yield break;
        }

        // 家具/杂项（木椅/沙袋等）：暂无家具类别，作杂项材料堆入库（显示名取配方名，描述取 flavor）。
        yield return Item.Material(outputKey, display, qty, FurnitureFlavor(outputKey));
    }

    /// <summary>家具/杂项产物的物品描述（黑色幽默 flavor）。未收录者留空——空描述在库存面板里只是不显示那一行。</summary>
    private static string FurnitureFlavor(string outputKey) => outputKey switch
    {
        SandbagSpec.ItemKey => SandbagSpec.ItemDescription,
        // [批次21·T14] 烹饪台与两件炊具。⚠️ 描述里**不写热量数字**——"装上省几点"是玩家要自己试出来的
        //（见 CookingLogic 顶部那段：食材热量点与每份需求全程对玩家隐藏）。
        CookStation.ItemKey => CookStation.ItemDescription,
        CookStation.PotItemKey => "一口砸扁又敲圆的铁锅。锅底那层黑是历任主人共同的作品——他们都不在了，锅还在。",
        CookStation.GrillItemKey => "几根铁丝架成的烤架。它做不出什么讲究的东西，但它让火不再白烧。",
        TrapSpec.ItemKey => TrapSpec.ItemDescription,   // [批次21·T26] 圈套陷阱（造出来进库存 → 玩家自己摆）
        // [批次21·T25] 桌子（纯家具）。⚠️ **床是顺手补的**：BedSpec.ItemDescription 写好了却**全仓无人引用**
        // ⇒ 造出来的床在库存里一句描述都没有（本表是它唯一的落点）。
        BedSpec.ItemKey => BedSpec.ItemDescription,
        TableSpec.ItemKey => TableSpec.ItemDescription,
        _ => "",
    };
}
