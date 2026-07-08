using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 Item.cs / BookData.cs / InventoryStore.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 材料目录：配方系统消耗的一批基础材料的**数据表**（拟定草稿，标 draft，用户后续调）。
// 只描述"这是一种什么材料"（标识名 / 显示名 / 简述 / 类别）；配方=材料+工具+制作者条件→产物，均为配方波的事，本块不碰。
// 材料落地为库存物品走 Item.Material（引用键=材料标识名 Key）；搜刮接入（材料进容器 loot 表）也是后续波。

/// <summary>材料的类别（用于 UI 分组与配方筛选）。</summary>
public enum MaterialCategory
{
    /// <summary>木：木料等。</summary>
    Wood,

    /// <summary>布：破布 / 布料等纺织物。</summary>
    Cloth,

    /// <summary>金属：废金属 / 金属锭 / 钉子 / 铁丝等。</summary>
    Metal,

    /// <summary>皮：生皮 / 皮革等。</summary>
    Leather,

    /// <summary>化学：火药 / 鞣制药水 / 燃料等化学品。</summary>
    Chemical,

    /// <summary>杂项：骨头 / 石料 / 绳 / 零件等不归上述类的材料。</summary>
    Misc,

    /// <summary>医疗：绷带 / 止血药 / 抗生素 / 夹板 等药品耗材（治疗系统按 <c>MedicineCatalog</c> 消费）。</summary>
    Medical,
}

/// <summary>
/// 一条材料的目录定义（不可变值对象）。<see cref="Item.Material"/> 用 <see cref="Key"/> 作引用键指向它。
/// <see cref="ToItem"/> 从本定义造一堆对应的库存材料物品（供搜刮/配方产物落地时用，类比 <see cref="BookData.ToItem"/>）。
/// </summary>
public readonly record struct MaterialDef(string Key, string DisplayName, string Description, MaterialCategory Category)
{
    /// <summary>由本定义造一堆材料物品（<paramref name="quantity"/> 份，clamp 到 ≥0；显示名/描述取自目录）。</summary>
    public Item ToItem(int quantity = 1) => Item.Material(Key, DisplayName, quantity, Description);
}

/// <summary>
/// 内置材料目录（**拟定草稿 draft**，数值/条目用户后续调）。覆盖生存造物常识里的基础材料：
/// 木 / 布 / 金属 / 皮 / 化学 / 杂项。配方波据 <see cref="MaterialDef.Key"/> 引用材料；
/// 搜刮波可用 <see cref="Find"/> / <see cref="All"/> 把材料塞进容器 loot / 落地入 <see cref="InventoryStore"/>。
/// </summary>
public static class Materials
{
    // draft：以下条目与简述均为占位草稿，最终由用户调（对标配方例子 + 生存常识起的一批基础材料）。
    private static readonly IReadOnlyList<MaterialDef> _all = new[]
    {
        new MaterialDef("wood", "木料", "劈好的木料，制作与建造的通用基材。", MaterialCategory.Wood),
        new MaterialDef("scrap_cloth", "破布", "从旧衣物撕下的碎布，可缝补或搓绳。", MaterialCategory.Cloth),
        new MaterialDef("cloth", "布料", "整幅的布料，缝制衣物与护具的原料。", MaterialCategory.Cloth),
        new MaterialDef("scrap_metal", "废金属", "锈蚀的金属废料，回炉或直接加工成零件。", MaterialCategory.Metal),
        new MaterialDef("metal_ingot", "金属锭", "熔炼提纯后的金属锭，打造利器与工具。", MaterialCategory.Metal),
        new MaterialDef("nails", "钉子", "一把铁钉，固定木结构的紧固件。", MaterialCategory.Metal),
        new MaterialDef("wire", "铁丝", "成卷的铁丝，捆扎、布设陷阱皆可。", MaterialCategory.Metal),
        new MaterialDef("rawhide", "生皮", "剥下的生兽皮，需鞣制才能久用。", MaterialCategory.Leather),
        new MaterialDef("leather", "皮革", "鞣制好的皮革，耐磨的护具与绑带原料。", MaterialCategory.Leather),
        new MaterialDef("bone", "骨头", "动物骨骼，可削成骨器或熬胶。", MaterialCategory.Misc),
        new MaterialDef("gunpowder", "火药", "黑火药，复装弹药与爆破的核心。", MaterialCategory.Chemical),
        new MaterialDef("tanning_solution", "鞣制药水", "鞣皮用的化学药水，把生皮变成皮革。", MaterialCategory.Chemical),
        new MaterialDef("fuel", "燃料", "可燃的汽油/柴油，发电机与燃烧瓶的燃料。", MaterialCategory.Chemical),
        new MaterialDef("stone", "石料", "凿下的石块，砌墙与打磨的粗料。", MaterialCategory.Misc),
        new MaterialDef("rope", "绳子", "结实的绳索，捆绑、攀爬与制作的通用件。", MaterialCategory.Misc),
        new MaterialDef("components", "机械零件", "拆解得来的杂项零件，精密装置的关键。", MaterialCategory.Misc),
        // 医疗耗材（draft）：治疗系统据材料 Key 查 MedicineCatalog 得治疗语义（治哪种病状 + 药效）。
        new MaterialDef("bandage", "绷带", "干净的绷带，包扎伤口、辅助止血。", MaterialCategory.Medical),
        new MaterialDef("styptic", "止血药", "强效止血粉，快速封住流血的伤口。", MaterialCategory.Medical),
        new MaterialDef("antibiotics", "抗生素", "对抗伤口感染的抗菌药物，需连服数日见效。", MaterialCategory.Medical),
        new MaterialDef("splint", "夹板", "固定骨折部位的夹板，为断骨愈合争取时间。", MaterialCategory.Medical),
        new MaterialDef("medicine", "成药", "杂七杂八的成药，缓解发热痢疾等病症。", MaterialCategory.Medical),
    };

    private static readonly IReadOnlyDictionary<string, MaterialDef> _byKey =
        _all.ToDictionary(m => m.Key);

    /// <summary>全部内置材料定义（按目录声明顺序）。</summary>
    public static IReadOnlyList<MaterialDef> All => _all;

    /// <summary>该材料标识名是否在目录中。</summary>
    public static bool Has(string key) => key != null && _byKey.ContainsKey(key);

    /// <summary>按标识名查一条材料定义；查不到返回 <c>null</c>。</summary>
    public static MaterialDef? Find(string key)
        => key != null && _byKey.TryGetValue(key, out MaterialDef def) ? def : null;

    /// <summary>按类别筛材料（供 UI 分组 / 配方筛选）。</summary>
    public static IEnumerable<MaterialDef> InCategory(MaterialCategory category)
        => _all.Where(m => m.Category == category);
}
