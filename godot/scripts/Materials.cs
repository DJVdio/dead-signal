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

    /// <summary>医疗：绷带 / 针线 / 夹板 / 急救包 等手术耗材（按 <c>SurgeryCatalog</c> 计点），抗生素 / 成药 等药品（按 <c>MedicineCatalog</c> 消费）。</summary>
    Medical,

    /// <summary>货币：末日流通的交易媒介（白银，同 RimWorld 以白银计价），成堆持有，用 <see cref="MaterialDef.Key"/> 标识、按 <see cref="Item.MaterialQuantity"/> 计数。神秘商人交易的支付手段。</summary>
    Currency,
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
        new MaterialDef("wood", "木料", "劈好的木料，盖房、生火、打家具都指望它——文明退到这一步，好在树还在长。", MaterialCategory.Wood),
        new MaterialDef("scrap_cloth", "破布", "从旧衣物上撕下的碎布，它们原来的主人多半已经用不上了。", MaterialCategory.Cloth),
        new MaterialDef("cloth", "布料", "整幅的好布料，如今能穿得体面，也算一种奢侈。", MaterialCategory.Cloth),
        new MaterialDef("scrap_metal", "废金属", "锈迹斑斑的金属废料，在会变废为宝的人手里，什么都不算废。", MaterialCategory.Metal),
        new MaterialDef("metal_ingot", "金属锭", "熔炼提纯的金属锭，敲敲打打，就是一件能保命的家伙。", MaterialCategory.Metal),
        new MaterialDef("nails", "钉子", "一把铁钉，让两块木头白头偕老的秘诀。", MaterialCategory.Metal),
        new MaterialDef("wire", "铁丝", "成卷的铁丝，捆东西、设陷阱、修栅栏，居家末日必备。", MaterialCategory.Metal),
        new MaterialDef("rawhide", "生皮", "剥下来的生兽皮，还带着点腥味，鞣好了才好意思穿上身。", MaterialCategory.Leather),
        new MaterialDef("leather", "皮革", "鞣制好的皮革，结实耐磨，比它原来的主人耐久多了。", MaterialCategory.Leather),
        new MaterialDef("bone", "骨头", "动物的骨头，削一削是工具，熬一熬是胶——物尽其用，谁也别浪费。", MaterialCategory.Misc),
        new MaterialDef("gunpowder", "火药", "一包黑火药，能让子弹上膛，也能让手忙脚乱的人上天。", MaterialCategory.Chemical),
        new MaterialDef("tanning_solution", "鞣制药水", "鞣皮用的化学药水，气味感人，效果扎实。", MaterialCategory.Chemical),
        new MaterialDef("fuel", "燃料", "汽油或柴油，发电机和燃烧瓶都靠它——文明烧剩下的那点体面。", MaterialCategory.Chemical),
        new MaterialDef("stone", "石料", "凿下来的石块，砌墙够沉，砸头也够。", MaterialCategory.Misc),
        new MaterialDef("rope", "绳子", "结实的绳子，捆绑、攀爬、拖拽，用途多到不必细说。", MaterialCategory.Misc),
        new MaterialDef("components", "机械零件", "拆东西拆出来的各式零件，越精密的装置，越离不开这堆不起眼的小玩意。", MaterialCategory.Misc),
        // 医疗耗材（draft）：手术耗材据 Key 查 SurgeryCatalog 得点数/适用伤类；药品据 Key 查 MedicineCatalog。
        // —— 手术耗材（流血/骨折靠手术，不吃药）——
        new MaterialDef("bandage", "绷带", "干净的绷带，包扎伤口、为流血手术加分；干净的那种，越来越难找了。", MaterialCategory.Medical),
        new MaterialDef("needle_thread", "针线", "缝合伤口的针与线，为流血手术加分（可与绷带同用）；比缝扣子疼一点，手别抖。", MaterialCategory.Medical),
        new MaterialDef("splint", "夹板", "固定断骨的夹板，骨折手术的核心耗材——断了就得认，硬撑只会更歪。", MaterialCategory.Medical),
        new MaterialDef("first_aid_kit", "急救包", "齐备的急救包，独立完成流血/骨折手术，独占不与散件叠加——末日里最像样的安全感。", MaterialCategory.Medical),
        // [SPEC-B14-补] 草药绷带：老君须敷料裹入绷带，止血手术的上位替代（供点 25，普通绷带 15）。
        new MaterialDef("herbal_bandage", "草药绷带", "老君须药敷裹入的绷带，止血手术效果强于普通绷带——祖传偏方，意外地靠谱。", MaterialCategory.Medical),
        // —— 药品（感染/疾病）——
        new MaterialDef("antibiotics", "抗生素", "现代医学的智慧结晶——对抗伤口感染，需连服数日见效，一板难求。", MaterialCategory.Medical),
        new MaterialDef("medicine", "成药", "杂七杂八的成药，缓解发热痢疾等病症——治不了大病，但能让你多撑一天。", MaterialCategory.Medical),
        // —— [SPEC-B14] 草药医疗三档（原料 + 自制药）：治感染的民间方子，治疗效率远逊抗生素但可采集自制。——
        // 原料（野外探索点散布采集）：
        new MaterialDef("dandelion", "蒲公英", "田埂路边遍地的野草，晒干能煮茶，也是草药膏的配料——穷人的药房。", MaterialCategory.Medical),
        new MaterialDef("rosehip", "玫瑰果", "野蔷薇结的红果，酸得很有营养，草药膏的配料之一。", MaterialCategory.Medical),
        new MaterialDef("laojunxu", "老君须", "山野间的藤蔓草药，老辈人拿它敷溃烂的伤口——信则灵，何况你也没别的选。", MaterialCategory.Medical),
        // 自制药（配方产出，据 Key 查 MedicineCatalog 治感染）：
        new MaterialDef("herbal_salve", "草药膏", "蒲公英、玫瑰果与老君须捣制的抗菌药膏，治感染效率约抗生素的三成五，还能压一压恶化——须趁早，晚了就来不及。", MaterialCategory.Medical),
        new MaterialDef("dandelion_tea", "蒲公英茶", "奶奶的最爱——干蒲公英煮的苦茶，治疗效率仅一成半，单靠它赢不了，只能延缓，给你争取换药的时间。", MaterialCategory.Medical),
        // [SPEC-B14-补2] 玫瑰果茶：饮用后 24 游戏小时伤病恢复速度 +9pp。
        new MaterialDef("rosehip_tea", "玫瑰果茶", "酸酸甜甜，抚慰你受伤的身体——喝下后一整天，身子骨恢复得更快些。", MaterialCategory.Medical),
        // —— 货币（draft）：末日流通的硬通货，神秘商人交易媒介。量级/掉落来源待用户设计。——
        new MaterialDef(CurrencyKey, "白银", "末世前铸的白银，如今废土上唯一还认的硬通货——文明没了，人对闪光东西的贪心还在。可堆叠、散落世界可搜刮。", MaterialCategory.Currency),
    };

    /// <summary>货币材料标识键（白银）。持币量 = 库存中该键各堆 <see cref="Item.MaterialQuantity"/> 之和；交易走 <c>InventoryStore.TrySpendMaterial</c> 实扣。</summary>
    public const string CurrencyKey = "silver";

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
