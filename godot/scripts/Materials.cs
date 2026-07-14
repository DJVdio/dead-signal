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

    /// <summary>布：纺织物（破布与整幅布料已合并为单一材料「布」）。</summary>
    Cloth,

    /// <summary>金属：废金属 / 金属锭 / 钉子 / 铁丝等。</summary>
    Metal,

    /// <summary>皮：生皮 / 皮革等。</summary>
    Leather,

    /// <summary>化学：火药 / 鞣制药水 / 燃料等化学品。</summary>
    Chemical,

    /// <summary>杂项：骨头 / 石料 / 绳 / 零件等不归上述类的材料。</summary>
    Misc,

    /// <summary>
    /// 医疗：绷带 / 针线 / 夹板 / 急救包 等手术耗材（按 <c>SurgeryCatalog</c> 计点），抗生素 / 成药 等药品（按 <c>MedicineCatalog</c> 消费）。
    /// <para>
    /// ⚠️ <b>本类目里还躺着三味"药材"：蒲公英 / 玫瑰果 / 老君须。它们是【材料】，不是药</b>（用户口径）——
    /// 采来的原料，得经配方熬成草药膏 / 蒲公英茶 / 玫瑰果茶 / 草药绷带才能用在人身上，直接吃是没用的。
    /// 它们挂在 Medical 下只是因为**归口在医疗那条线**（和玫瑰果 / 蒲公英同时是食材、却不归 <see cref="Food"/> 一个道理）。
    /// </para>
    /// ⇒ <b>判"这东西是不是能直接用在人身上的医疗物资"一律问 <see cref="MedicalOrderLogic.IsMedicalSupply"/>，不要问这个类别</b>
    /// （三味药材在那里恒为 <c>false</c>，有单测钉死）。这条与 <see cref="Food"/> 上"判能不能煮要问 <see cref="FoodCalories.Has"/>"是同一个道理：
    /// <b>类别是归口，不是能力</b>。
    /// </summary>
    Medical,

    /// <summary>货币：末日流通的交易媒介（白银，同 RimWorld 以白银计价），成堆持有，用 <see cref="MaterialDef.Key"/> 标识、按 <see cref="Item.MaterialQuantity"/> 计数。神秘商人交易的支付手段。</summary>
    Currency,

    /// <summary>
    /// 食材（批次21·T14 烹饪）：**下得了锅的生料**（老鼠 / 罐头 / 军用单兵口粮 / 土豆…）。
    /// <para>
    /// 每种食材有一个**热量点**，但它<b>不是本目录的字段</b>——热量点在外挂表 <see cref="FoodCalories"/> 里，
    /// 因为"是不是食材"是**跨类别**的：玫瑰果 / 蒲公英归 <see cref="Medical"/>（草药那条线），却照样能下锅。
    /// ⇒ <b>判"能不能煮"一律问 <see cref="FoodCalories.Has"/>，不要问这个类别。</b>
    /// </para>
    /// <para>
    /// 与 <see cref="ItemCategory.Food"/>（成品「份数」，1 份 = 1 人 1 餐）也不是一回事：
    /// 食材是**生的**，得在烹饪台上烧成份数才吃得下。搜到的现成食物照旧直接进份数，不经这一层。
    /// </para>
    /// </summary>
    Food,

    /// <summary>
    /// 弹药：枪/弓的消耗品（子弹 / 霰弹 / 箭矢，键见 <c>AmmoKeys</c>）。
    /// 归入材料而非另立物品类别，是为了直接复用既有的**堆叠**（<see cref="Item.MaterialQuantity"/>）、
    /// **实扣**（<c>InventoryStore.TrySpendMaterial</c>）、**制作产出**（<c>CraftOutputFactory</c> 的材料分支）
    /// 与**搜刮投放**（loot 表已能投材料）四条链路——不必为弹药新造任何基础设施。
    /// </summary>
    Ammo,
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
        // [批次20·拆除回收] 废木料：拆木结构掉出来的碎料（用户拍板：拆 16 木料的东西 → 4 木料 + 4 废木料）。
        // 它**盖不了任何东西**——得先在锯片工作台上用胶水粘回木料（配方 wood_from_scrap）。木材那"另外 25%"就压在这堆碎料里。
        new MaterialDef("scrap_wood", "废木料", "拆下来的断料、劈裂的板子、带钉眼的短头。单看哪一块都不成器，可你手里也没别的木头了。", MaterialCategory.Wood),
        // 布：整幅好布与撕下的碎布不再区分（用户拍板）——末日里没人挑剔布的出身，能缝上就行。
        new MaterialDef("cloth", "布", "有整幅的，也有从旧衣裳上撕下来的。缝上身之后就没人分得清了——反正原来的主人也不会来认。", MaterialCategory.Cloth),
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
        // [批次20·拆除回收] 胶水：把废木料粘回木料的**唯一**途径 ⇒ 木材要完整回收，就得交这份「胶水税」。
        // **刻意稀缺**：只有一条产出配方（骨头 + 燃料 + 烧杯 + 化学书；配方与产物同名，见 Recipe.cs 的 glue），
        // 而燃料同时是火把/发电机/火药/全部枪弹的命根子。搜刮来的那一罐和自己熬的那一罐，是同一样东西。
        // 于是"这罐胶是拿去回收木料，还是留着熬火药"成了真的选择——胶水一旦遍地都是，这条税就白设计了。
        new MaterialDef("glue", "胶水", "一罐熬出来的骨胶，黏、稠、气味上不了台面。它粘得住断掉的木头，粘不回断掉的日子。", MaterialCategory.Chemical),
        new MaterialDef("stone", "石料", "凿下来的石块，砌墙够沉，砸头也够。", MaterialCategory.Misc),
        new MaterialDef("rope", "绳子", "结实的绳子，捆绑、攀爬、拖拽，用途多到不必细说。", MaterialCategory.Misc),
        new MaterialDef("components", "机械零件", "拆东西拆出来的各式零件，越精密的装置，越离不开这堆不起眼的小玩意。", MaterialCategory.Misc),
        // 医疗耗材（draft）：手术耗材据 Key 查 SurgeryCatalog 得点数/适用伤类；药品据 Key 查 MedicineCatalog。
        // —— 手术耗材（流血/骨折靠手术，不吃药）——
        new MaterialDef("bandage", "绷带", "一块破布有时也能救命", MaterialCategory.Medical),
        new MaterialDef("needle_thread", "针线", "能缝补你的伤口，却治愈不了你的心", MaterialCategory.Medical),
        new MaterialDef("splint", "夹板", "这会让它看上去直一些", MaterialCategory.Medical),
        new MaterialDef("first_aid_kit", "急救包", "“一包全搞定”——南丁格尔", MaterialCategory.Medical),
        // [SPEC-B14-补] 草药绷带：老君须敷料裹入绷带，止血手术的上位替代（供点 25，普通绷带 15）。
        new MaterialDef("herbal_bandage", "草药绷带", "传统医学，比普通绷带强", MaterialCategory.Medical),
        // —— 药品（感染/疾病）——
        new MaterialDef("antibiotics", "抗生素", "现代医学的结晶", MaterialCategory.Medical),
        new MaterialDef("medicine", "成药", "杂七杂八的成药，缓解发热痢疾等病症——治不了大病，但能让你多撑一天。", MaterialCategory.Medical),
        // —— [SPEC-B14] 草药医疗三档（原料 + 自制药）：治感染的民间方子，治疗效率远逊抗生素但可采集自制。——
        // 原料（野外探索点散布采集）：
        new MaterialDef("dandelion", "蒲公英", "田埂路边遍地的野草，晒干能煮茶，也是草药膏的配料——穷人的药房。", MaterialCategory.Medical),
        new MaterialDef("rosehip", "玫瑰果", "野蔷薇结的红果，酸得很有营养，草药膏的配料之一。", MaterialCategory.Medical),
        new MaterialDef("laojunxu", "老君须", "山野间的藤蔓草药，老辈人拿它敷溃烂的伤口——信则灵，何况你也没别的选。", MaterialCategory.Medical),
        // 自制药（配方产出，据 Key 查 MedicineCatalog 治感染）：
        new MaterialDef("herbal_salve", "草药膏", "死马当活马医", MaterialCategory.Medical),
        new MaterialDef("dandelion_tea", "蒲公英茶", "奶奶的最爱", MaterialCategory.Medical),
        // [SPEC-B14-补2] 玫瑰果茶：饮用后 24 游戏小时伤病恢复速度 +9pp。
        new MaterialDef("rosehip_tea", "玫瑰果茶", "酸酸甜甜，抚慰你受伤的身体——喝下后一整天，身子骨恢复得更快些。", MaterialCategory.Medical),
        // —— 货币（draft）：末日流通的硬通货，神秘商人交易媒介。量级/掉落来源待用户设计。——
        new MaterialDef(CurrencyKey, "白银", "末世前铸的白银，如今废土上唯一还认的硬通货——文明没了，人对闪光东西的贪心还在。可堆叠、散落世界可搜刮。", MaterialCategory.Currency),
        // —— [批次18] 子弹零件：四种子弹的**唯一**共同原料（用户拍板新增的材料）。——
        // 归「金属」而非「弹药」类：它不是弹药，是造弹药的料（弹药分区只列真能打出去的东西）。
        new MaterialDef(BulletPartsKey, "子弹零件", "弹壳、底火、弹头坯——枪匠时代留下的精密小玩意。火药你能自己熬，这些你只能捡；捡完了，枪就真的只是根铁棍。", MaterialCategory.Metal),

        // —— [批次21·T26] 武器零件：两把**弩**的 defining 材料（用户拍板新增的材料）。——
        //
        // ⚠️ **它不是「机械零件」，这是刻意的**（用户明确要另一种东西）：
        //   · 「机械零件」(components) —— 通用的机括小件，喂**改装台 / 自制枪 / 一堆杂活**；
        //   · 「武器零件」(weapon_parts) —— 弩机、扳机组、簧片这类**武器专用**的精密件，只喂弩。
        // 两者**互不争抢**：想造改装台又想造弩，不必在同一堆零件上做取舍。**别把它们又并回去。**
        //
        // 归「金属」类，同「子弹零件」——它们是同一种东西的两个方向：那个"只能捡、造不出来"的精密件。
        // **只能搜刮**（军械/机修点位，见 ExplorationCache）。**未来可考虑「拆枪回收零件」**——
        // 那是一套全新机制（拆武器回收），本单没做，要做另开一单。
        new MaterialDef(WeaponPartsKey, "武器零件", "弩机、扳机组、几片淬过火的簧——造枪的人早死绝了，留下的这些小东西还硬邦邦地不肯锈。你造不出它们，只能指望别人也没找到。", MaterialCategory.Metal),
        // —— [批次18] 弹药四种（用户拍板：短/中/长子弹 + 鹿弹；键对齐引擎 AmmoKeys）——
        // **稀缺梯度写在制作比里**（用户拍板）：1 个子弹零件 → 短 8 / 中 5 / 鹿 4 / 长 2 发。
        // 越强的枪，同一份原料能喂它的次数越少。枪的强度现在完全由这四行的供给量决定。
        new MaterialDef("ammo_short", "短子弹", "手枪和冲锋枪吃的小家伙。最不值钱的一种子弹——这话你打空弹匣之前也是这么说的。", MaterialCategory.Ammo),
        new MaterialDef("ammo_medium", "中子弹", "步枪和猎枪的口粮。一份零件只出五发，而步枪一次扣扳机就吞掉两发——算术很简单，心疼是真的。", MaterialCategory.Ammo),
        new MaterialDef("ammo_long", "长子弹", "又长又沉的狙击弹，一份零件只做得出两发。别拿它打丧尸，那是在用金子砸苍蝇。", MaterialCategory.Ammo),
        new MaterialDef("ammo_buck", "鹿弹", "红壳的鹿弹，一发里塞着八颗铅丸。贴脸时它是神，二十步外它只是响。", MaterialCategory.Ammo),
        // —— 箭：**4 种**（用户拍板），不是 1 种 ——
        // 子弹/霰弹是"1 个弹药类别 : 1 种材料"；箭是"1 个类别 : 4 种材料"——因为**只有箭会反过来改写武器的属性**
        // （最终属性 = 弓/弩的基础属性 ⊗ 箭的乘法修正，见引擎 <c>Archery.Combine</c>）。故这里没有一条笼统的「箭矢」，
        // 只有四种脾气各异的箭。键/显示名/描述的**单一真源**是引擎的 <c>ArrowTable</c>，此处逐条对齐（改那边就要改这边）。
        // 注意：`AmmoKeys.Arrow`（"ammo_arrow"）是**类别键**，只用于 `Weapon.AmmoKey` 表达"这武器吃箭"，
        // **它本身不是一种材料**，故不在本目录中——库存里躺的永远是下面这四种之一。
        new MaterialDef("ammo_arrow_stick", "削尖的木箭", "一根削尖的木棍，勉强算箭。它会飞，也会中，就是别指望它飞直，或者中得深。", MaterialCategory.Ammo),
        new MaterialDef("ammo_arrow_handmade", "自制箭", "木杆、铁头、布尾羽，手工出品。称不上精良，但每一支都长得一样——对一个弓手来说，这比精良更要紧。", MaterialCategory.Ammo),
        new MaterialDef("ammo_arrow_heavy", "重头箭", "箭头灌了铅，沉得手腕发酸。飞不远，抬手也慢，但扎上去的时候，护甲的意见就不太重要了。", MaterialCategory.Ammo),
        new MaterialDef("ammo_arrow_carbon", "碳纤维箭", "碳纤维箭杆，笔直、轻盈、贵得离谱。工厂早就停工了，用一支少一支——所以射出去之后，你一定会回去把它捡起来。", MaterialCategory.Ammo),
        // —— [批次21·T14] 食材：下得了锅的生料。热量点在 FoodCalories（外挂表），**不在这儿** ——
        // ⚠️ 描述里**一个热量数字都不许出现**：每种食材值几点是玩家要自己试出来的（用户拍板的核心玩法）。
        // 文案可以暗示"顶不顶饱"（"啃两口就没了"vs"顶一整天"），但绝不能报数——那是给 wiki 看的，不是给玩家看的。
        // 玫瑰果 / 蒲公英**不在此列**（它们归「医疗」，见上方草药三档）——但它们照样是食材：
        // 「是不是食材」问的是 FoodCalories.Has，不是这个类别。
        new MaterialDef("rat", "老鼠", "末日里最先繁荣起来的物种。抓它、剥它、炖它——你曾经以为自己这辈子都不会做这三件事。", MaterialCategory.Food),
        new MaterialDef("pigeon", "鸽子", "城市广场上那种鸽子，如今没人喂了，也没人拍照了。肉少，骨头多，但它是肉。", MaterialCategory.Food),
        new MaterialDef("rabbit", "兔子", "一只野兔，够一顿像样的。抓到它的那天，你会想起从前「运气不错」是个多么轻飘飘的词。", MaterialCategory.Food),
        new MaterialDef("fish", "鱼", "河里的鱼。没人知道那水现在还干不干净，但煮熟了，谁也不会先问这个问题。", MaterialCategory.Food),
        new MaterialDef("ration", "军用单兵口粮", "军方发的单兵口粮，密封、耐放、量足。包装上印着「一日份」——印它的那个人还相信会有下一日。", MaterialCategory.Food),
        new MaterialDef("canned_food", "罐头", "标签早泡烂了，里面是肉是豆全凭运气。铁皮鼓起来的那种别开——它已经替你尝过了。", MaterialCategory.Food),
        new MaterialDef("flour", "面粉", "一袋面粉。它做不成面包（谁还有烤箱），但撒进锅里能让一锅稀汤变得像顿饭。", MaterialCategory.Food),
        new MaterialDef("beans", "干豆", "一把干豆，硬得能崩掉牙。泡一夜、煮一晌，然后它就成了这个屋子里最像「正经饭」的东西。", MaterialCategory.Food),
        new MaterialDef("potato", "土豆", "从谁家后院刨出来的，发了点芽。削掉芽眼，剩下的部分依然是土豆——这就是土豆了不起的地方。", MaterialCategory.Food),
        new MaterialDef("mushroom", "蘑菇", "林子里采的。你认得这一种，你很确定你认得这一种。", MaterialCategory.Food),
    };

    /// <summary>货币材料标识键（白银）。持币量 = 库存中该键各堆 <see cref="Item.MaterialQuantity"/> 之和；交易走 <c>InventoryStore.TrySpendMaterial</c> 实扣。</summary>
    public const string CurrencyKey = "silver";

    /// <summary>
    /// 子弹零件标识键（对齐引擎 <c>BulletParts.Key</c>）——四种子弹的唯一共同原料。
    /// 制作比（1 个 → N 发）见 <c>BulletParts.YieldPer</c>：短 8 / 中 5 / 鹿 4 / 长 2。
    /// </summary>
    public const string BulletPartsKey = "bullet_parts";

    /// <summary>
    /// [批次21·T26] 武器零件标识键 —— **两把弩的 defining 材料**（单手轻弩 2 / 双手重弩 3，见 <see cref="RecipeBook"/>）。
    /// <para>
    /// ⚠️ <b>与「机械零件」(<c>components</c>) 是两种东西，别合并</b>：那个是通用机括件（喂改装台/自制枪/杂活），
    /// 这个是<b>武器专用</b>的精密件（只喂弩）。用户拍板新建它，正是为了让<b>弩与改装台不争抢同一堆零件</b>。
    /// </para>
    /// <para><b>只能搜刮</b>（军械/机修点位）；没有配方。未来可考虑"拆枪回收零件"，那是另一套机制，本单未做。</para>
    /// </summary>
    public const string WeaponPartsKey = "weapon_parts";

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
