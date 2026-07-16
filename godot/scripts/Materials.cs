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

    /// <summary>金属：铁 / 钉子 / 铁丝等（废金属与金属锭已合并为单一材料「铁」）。</summary>
    Metal,

    /// <summary>
    /// [波1·item2] 精密零件：机械零件 / 子弹零件 / 武器零件——三味"枪匠时代留下、造不出来、只能捡"的精密件。
    /// <para>
    /// 从 <see cref="Metal"/>（子弹零件 / 武器零件）与 <see cref="Misc"/>（机械零件）里拆出来单列一类：
    /// 它们不是普通铁料，也不是骨石绳那类有机杂料，而是同一种脾气的东西——
    /// <b>火药你能自己熬，这些你只能捡；捡完了，枪就真的只是根铁棍。</b>
    /// 迁移由 <c>MaterialsTests.精密零件三味_归入Component类_且已离开Misc与Metal</c> 钉死（迁彻底、只装这三味）。
    /// </para>
    /// </summary>
    Component,

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
        new MaterialDef("wood", "木料", "树的尸体。", MaterialCategory.Wood),
        // [批次20·拆除回收] 废木料：拆木结构掉出来的碎料（用户拍板：拆 16 木料的东西 → 4 木料 + 4 废木料）。
        // 它**盖不了任何东西**——得先在锯片工作台上用胶水粘回木料（配方 wood_from_scrap）。木材那"另外 25%"就压在这堆碎料里。
        new MaterialDef("scrap_wood", "废木料", "拆下来的断料、劈裂的板子、带钉眼的短头。单看哪一块都不成器，可你手里也没别的了。", MaterialCategory.Wood),
        // 布：整幅好布与撕下的碎布不再区分（用户拍板）——末日里没人挑剔布的出身，能缝上就行。
        new MaterialDef("cloth", "布", "“扯一块好布，奶奶给你做新衣裳”——奶奶", MaterialCategory.Cloth),
        // [T46] 铁：「废金属」与「金属锭」已合并为单一材料（用户拍板：「不区分废金属和金属锭，统一为 铁」）。
        // 合并同时**修掉一个真 bug**：金属锭此前**没有任何获取途径**（零配方产出、零 loot、商人不卖）⇒
        // 吃它的十来样东西（自制猎枪/重头箭/重弩/锅/全金属围栏/铸铁大门/金属门/加长枪管/刺刀型/创伤型…）**一个都造不出来**。
        // 换算：1 废金属 = 1 铁，1 金属锭 = 2 铁（见 SaveMigration.IngotToIronRatio；老档按此迁移）。
        new MaterialDef(IronKey, "铁", "“Fe”——诺蒂", MaterialCategory.Metal),
        new MaterialDef("nails", "钉子", "一把铁钉，让两块木头白头偕老的秘诀。", MaterialCategory.Metal),
        new MaterialDef("wire", "铁丝", "成卷的铁丝，比想象中更实用。", MaterialCategory.Metal),
        new MaterialDef("rawhide", "生皮", "曾经包裹着血肉。", MaterialCategory.Leather),
        new MaterialDef("leather", "皮革", "鞣制好的皮革，结实耐磨，比它原来的主人耐久多了。", MaterialCategory.Leather),
        new MaterialDef("bone", "骨头", "物尽其用，谁也别浪费，也别浪费谁。", MaterialCategory.Misc),
        new MaterialDef("gunpowder", "火药", "一包黑火药，能让子弹上膛，也能让手忙脚乱的人上天。", MaterialCategory.Chemical),
        new MaterialDef("tanning_solution", "鞣制药水", "鞣皮用的化学药水，气味感人。", MaterialCategory.Chemical),
        new MaterialDef("fuel", "燃料", "易燃易爆炸。", MaterialCategory.Chemical),
        // [批次20·拆除回收] 胶水：把废木料粘回木料的**唯一**途径 ⇒ 木材要完整回收，就得交这份「胶水税」。
        // **刻意稀缺**：只有一条产出配方（骨头 + 燃料 + 烧杯 + 化学书；配方与产物同名，见 Recipe.cs 的 glue），
        // 而燃料同时是火把/发电机/火药/全部枪弹的命根子。搜刮来的那一罐和自己熬的那一罐，是同一样东西。
        // 于是"这罐胶是拿去回收木料，还是留着熬火药"成了真的选择——胶水一旦遍地都是，这条税就白设计了。
        new MaterialDef("glue", "胶水", "一罐熬出来的骨胶，黏、稠、气味上不了台面，但它够粘。", MaterialCategory.Chemical),
        new MaterialDef("stone", "石料", "够沉，够硬。", MaterialCategory.Misc),
        new MaterialDef("rope", "绳子", "结实的绳子，捆绑、攀爬、拖拽，用途多到不必细说。", MaterialCategory.Misc),
        new MaterialDef("components", "机械零件", "拆东西拆出来的各式零件，越精密的装置，越离不开这堆不起眼的小玩意。", MaterialCategory.Component),
        // 医疗耗材（draft）：手术耗材据 Key 查 SurgeryCatalog 得点数/适用伤类；药品据 Key 查 MedicineCatalog。
        // —— 手术耗材（流血/骨折靠手术，不吃药）——
        new MaterialDef("bandage", "绷带", "一块破布有时也能救命", MaterialCategory.Medical),
        new MaterialDef("needle_thread", "针线", "能缝补你的伤口，却治愈不了你的心", MaterialCategory.Medical),
        new MaterialDef("splint", "夹板", "这会让它看上去直一些", MaterialCategory.Medical),
        new MaterialDef("first_aid_kit", "急救包", "“一包全搞定”——南丁格尔", MaterialCategory.Medical),
        // [SPEC-B14 / T72·A2叠加] 草药绷带：老君须敷料裹入绷带，止血手术的上位替代（供点 20，普通绷带 15）。[T72] **额外**再降该处感染几率 ×0.75（止血+消炎并存，见 SurgeryCatalog）。
        new MaterialDef("herbal_bandage", "草药绷带", "传统医学，据说能消炎杀菌", MaterialCategory.Medical),
        // —— 药品（感染/疾病）——
        new MaterialDef("antibiotics", "抗生素", "现代医学的结晶", MaterialCategory.Medical),
        new MaterialDef("medicine", "成药", "杂七杂八的成药，缓解发热痢疾等病症——治不了大病，但能让你多撑一天。", MaterialCategory.Medical),
        // —— [SPEC-B14] 草药医疗三档（原料 + 自制药）：治感染的民间方子，治疗效率远逊抗生素但可采集自制。——
        // 原料（野外探索点散布采集）：
        new MaterialDef("dandelion", "蒲公英", "田埂路边遍地的野草，晒干能煮茶，也是草药膏的配料。", MaterialCategory.Medical),
        new MaterialDef("rosehip", "玫瑰果", "野蔷薇结的红果，酸得很有营养，有药用价值。", MaterialCategory.Medical),
        new MaterialDef("laojunxu", "老君须", "山野间的藤蔓草药，老辈人拿它敷溃烂的伤口——信则灵，何况你也没别的选。", MaterialCategory.Medical),
        // 自制药（配方产出，据 Key 查 MedicineCatalog 治感染）：
        new MaterialDef("herbal_salve", "草药膏", "死马当活马医", MaterialCategory.Medical),
        new MaterialDef("dandelion_tea", "蒲公英茶", "奶奶的最爱", MaterialCategory.Medical),
        // [SPEC-B14-补2] 玫瑰果茶：饮用后 24 游戏小时伤病恢复速度 +9pp。
        new MaterialDef("rosehip_tea", "玫瑰果茶", "酸酸甜甜，抚慰你受伤的身体。", MaterialCategory.Medical),
        // —— 货币（draft）：末日流通的硬通货，神秘商人交易媒介。量级/掉落来源待用户设计。——
        new MaterialDef(CurrencyKey, "白银", "闪闪亮亮，末世前铸的白银，如今废土上唯一还认的硬通货。", MaterialCategory.Currency),
        // —— [批次18] 子弹零件：四种子弹的**唯一**共同原料（用户拍板新增的材料）。——
        // 归「精密零件」而非「弹药」类：它不是弹药，是造弹药的料（弹药分区只列真能打出去的东西）。
        // [波1·item2] 与武器零件一同从「金属」迁入 Component——它们是精密件，不是普通铁料。
        new MaterialDef(BulletPartsKey, "子弹零件", "弹壳、底火、弹头坯——灾前留下的精密小玩意。", MaterialCategory.Component),

        // —— [批次21·T26] 武器零件：两把**弩**的 defining 材料（用户拍板新增的材料）。——
        //
        // ⚠️ **它不是「机械零件」，这是刻意的**（用户明确要另一种东西）：
        //   · 「机械零件」(components) —— 通用的机括小件，喂**改装台 / 自制枪 / 一堆杂活**；
        //   · 「武器零件」(weapon_parts) —— 弩机、扳机组、簧片这类**武器专用**的精密件，只喂弩。
        // 两者**互不争抢**：想造改装台又想造弩，不必在同一堆零件上做取舍。**别把它们又并回去。**
        //
        // 归「精密零件」类，同「子弹零件」——它们是同一种东西的两个方向：那个"只能捡、造不出来"的精密件。
        // [波1·item2] 与子弹零件一同从「金属」迁入 Component。
        // **只能搜刮**（军械/机修点位，见 ExplorationCache）。**未来可考虑「拆枪回收零件」**——
        // 那是一套全新机制（拆武器回收），本单没做，要做另开一单。
        new MaterialDef(WeaponPartsKey, "武器零件", "弩机、扳机组、几片淬过火的簧——造枪的人早死绝了，留下的这些小东西还硬邦邦地不肯锈。你造不出它们，只能指望别人也没找到。", MaterialCategory.Component),
        // —— [批次18] 弹药四种（用户拍板：短/中/长子弹 + 鹿弹；键对齐引擎 AmmoKeys）——
        // **稀缺梯度写在制作比里**（用户拍板）：1 个子弹零件 → 短 8 / 中 5 / 鹿 4 / 长 2 发。
        // 越强的枪，同一份原料能喂它的次数越少。枪的强度现在完全由这四行的供给量决定。
        new MaterialDef("ammo_short", "短子弹", "穿透力弱，指的可不是对血肉。", MaterialCategory.Ammo),
        new MaterialDef("ammo_medium", "中子弹", "太标准了，一枪一眼的标准。", MaterialCategory.Ammo),
        new MaterialDef("ammo_long", "长子弹", "又长又沉，被他打的猎物不会留下弹孔，只需祈祷留下完尸。", MaterialCategory.Ammo),
        new MaterialDef("ammo_buck", "鹿弹", "红壳的鹿弹，一发里塞着八颗铅丸。", MaterialCategory.Ammo),
        // —— 箭：**4 种**（用户拍板），不是 1 种 ——
        // 子弹/霰弹是"1 个弹药类别 : 1 种材料"；箭是"1 个类别 : 4 种材料"——因为**只有箭会反过来改写武器的属性**
        // （最终属性 = 弓/弩的基础属性 ⊗ 箭的乘法修正，见引擎 <c>Archery.Combine</c>）。故这里没有一条笼统的「箭矢」，
        // 只有四种脾气各异的箭。键/显示名/描述的**单一真源**是引擎的 <c>ArrowTable</c>，此处逐条对齐（改那边就要改这边）。
        // 注意：`AmmoKeys.Arrow`（"ammo_arrow"）是**类别键**，只用于 `Weapon.AmmoKey` 表达"这武器吃箭"，
        // **它本身不是一种材料**，故不在本目录中——库存里躺的永远是下面这四种之一。
        new MaterialDef("ammo_arrow_stick", "削尖的木箭", "一根削尖的木棍，会飞。", MaterialCategory.Ammo),
        new MaterialDef("ammo_arrow_handmade", "自制箭", "木杆、铁头、羽毛尾翎，手工出品。", MaterialCategory.Ammo),
        new MaterialDef("ammo_arrow_heavy", "重头箭", "什么叫他射了一根长矛过来？", MaterialCategory.Ammo),
        new MaterialDef("ammo_arrow_carbon", "碳纤维箭", "碳纤维箭杆，笔直、轻盈，现代高分子材料学的得意之作。", MaterialCategory.Ammo),
        // —— [批次21·T14] 食材：下得了锅的生料。热量点在 FoodCalories（外挂表），**不在这儿** ——
        // ⚠️ 描述里**一个热量数字都不许出现**：每种食材值几点是玩家要自己试出来的（用户拍板的核心玩法）。
        // 文案可以暗示"顶不顶饱"（"啃两口就没了"vs"顶一整天"），但绝不能报数——那是给 wiki 看的，不是给玩家看的。
        // 玫瑰果 / 蒲公英**不在此列**（它们归「医疗」，见上方草药三档）——但它们照样是食材：
        // 「是不是食材」问的是 FoodCalories.Has，不是这个类别。
        // 🔴 [T67] **老鼠与鸟不再下得了锅** —— 用户原话「老鼠和鸟不能直接入锅了，而是要先宰杀」。
        //    它们仍是 MaterialCategory.Food（**它们没有离开食物链，只是多了一道工序**），
        //    但**已从 FoodCalories 移除** ⇒ `FoodCalories.Has` 为 false ⇒ 灶上点不到它们。
        //    ⚠️ 这条"类别留着、热量表除名"的走法**不是我发明的**：蒲公英是同一个先例
        //    （[T59] 用户把它从食物表删了，它仍是材料、仍是药）。判据是那句既有口径：
        //    **「是不是食材」问的是 FoodCalories.Has，不是这个类别。**
        //    它们现在的去处是【宰杀】（见 <see cref="ButcheryLogic"/>）：老鼠 → 老鼠肉 + 碎皮革；鸟 → 鸟肉 + 羽毛。
        new MaterialDef("rat", "老鼠", "末日里最先繁荣起来的物种。抓它、剥它、炖它——你曾经以为自己这辈子都不会做这三件事。", MaterialCategory.Food),
        // ⚠️ [T67] **「鸽子」已改名为「鸟」**（用户原话：「就是鸽子，但是换名字叫鸟」）——**改的是显示名，不是物品**。
        //    🔴 **材料键仍是 `pigeon`**，这是**有意的**：本项目材料的代码主键是**英文键**（中文名只是显示名，
        //    与武器表 `_weaponKg` 那种"中文名当主键"的字典不是一回事）⇒ 库存/存档/掉落表/图标/商人表**全部按 `pigeon` 索引**，
        //    改键才会引发存档迁移，改显示名**一行都不用迁**（老档里存的是 `pigeon`，读出来照样显示「鸟」）。
        //    ⇒ **别"顺手"把键也改成 `bird`** —— 那会把老档里的鸟凭空变没，换来的只是一个更好看的英文单词。
        new MaterialDef("pigeon", "鸟", "城市广场上那种鸽子，如今没人喂了，也没人拍照了。肉少，骨头多，但它是肉——前提是你肯动刀。", MaterialCategory.Food),
        new MaterialDef("rabbit", "兔子", "兔兔这么可爱！", MaterialCategory.Food),
        new MaterialDef("fish", "鱼", "河里的鱼。没人知道那水现在还干不干净，但煮熟了，谁也不会先问这个问题。", MaterialCategory.Food),
        new MaterialDef("ration", "军用单兵口粮", "军方发的单兵口粮，密封、耐放、量足。包装上印着「一日份」——印它的那个人还相信会有下一日。", MaterialCategory.Food),
        new MaterialDef("canned_food", "罐头", "铁皮鼓起来的那种别开。", MaterialCategory.Food),
        new MaterialDef("flour", "面粉", "一袋面粉。", MaterialCategory.Food),
        new MaterialDef("potato", "土豆", "他妈的！土在哪！！！", MaterialCategory.Food),
        new MaterialDef("mushroom", "蘑菇", "你认得这一种，你最好确定你认得这一种。", MaterialCategory.Food),

        // ════════════════ [T67] 宰杀链的四样新材料（**追加末尾不插队**，Sim 随机流纪律）════════════════
        //
        // 用户拍板的新链：**捕鸟陷阱 → 鸟 →【宰杀】→ 鸟肉 + 羽毛 → 造箭**；**老鼠 →【宰杀】→ 老鼠肉 + 碎皮革**。
        // 每一样都**必须有消费方**，否则就是死物品（今天已抓出「金属锭」「骨刀」两个前车之鉴）：
        //   · 老鼠肉 / 鸟肉 → **下得了锅**（FoodCalories）✓
        //   · 羽毛       → **三种箭的共同料**（削尖的木箭 / 自制箭 / 重头箭）✓
        //   · 碎皮革     → **缝合成生皮**（`leather_stitch` 配方）✓ —— 见下方它自己的注释

        // 老鼠肉：热量点 **6**，**原封不动**继承老鼠身上那个「用户给定的定值」（宰杀只是把肉从骨头上取下来，不创造也不毁灭热量）。
        new MaterialDef("rat_meat", "老鼠肉", "你已经不记得上一次挑食是什么时候了。", MaterialCategory.Food),

        // 鸟肉：热量点 **5**，同样原样继承自「鸟」（原「鸽子」）。
        new MaterialDef("bird_meat", "鸟肉", "两条细腿，一小块胸脯。", MaterialCategory.Food),

        // 羽毛：**箭的尾羽**。三种箭全都吃它（用户在 wiki 上亲手改的），也就是说——
        // **没有鸟，就没有箭**。弓不是靠木头喂活的，是靠一张网和一把刀。
        new MaterialDef("feather", "羽毛", "一小把飞羽，理顺了扎在箭尾上。它曾经的用途和现在的用途，说到底是同一件事：让一样东西飞直。", MaterialCategory.Misc),

        // 碎皮革：宰杀老鼠的副产物。
        // 🔴 **它不是「皮革」，也不是「生皮」的重复品** —— 三者是一条**工序链**上的三个阶段，不是三个同义词：
        //   碎皮革（rat 大小的零碎皮子，**缝起来才成幅**）→ 生皮（成幅的生兽皮，**鞣过才能穿**）→ 皮革（鞣好的成品）。
        // ⚠️ **它给「生皮」补上了游戏里的第一条生产线**：核实过——`rawhide` 此前**没有任何掉落点、没有任何配方产出**，
        //    只能从商人手里买（`MerchantBuyList` 里 4 银）。⇒ 碎皮革不是概念膨胀，它是在**接一条断掉的链**。
        new MaterialDef("leather_scrap", "碎皮革", "一小块一小块的皮子，边缘参差不齐。攒够了，缝起来，也就还是一张皮——只是多了几十道针脚。", MaterialCategory.Leather),
    };

    /// <summary>羽毛的材料标识键 —— 三种箭（削尖的木箭 / 自制箭 / 重头箭）的<b>共同料</b>，唯一来源是【宰杀鸟】。</summary>
    public const string FeatherKey = "feather";

    /// <summary>碎皮革的材料标识键 —— 宰杀老鼠的副产物；<b>缝合成生皮</b>（<c>leather_stitch</c>）。</summary>
    public const string LeatherScrapKey = "leather_scrap";

    /// <summary>老鼠肉的材料标识键 —— 宰杀老鼠的主产物（6 热量点，继承自老鼠）。</summary>
    public const string RatMeatKey = "rat_meat";

    /// <summary>鸟肉的材料标识键 —— 宰杀鸟的主产物（5 热量点，继承自鸟）。</summary>
    public const string BirdMeatKey = "bird_meat";

    /// <summary>
    /// [T46] 铁的材料标识键 —— 金属加工的**唯一**原料（钉子/铁丝/武器零件是另算的成品件，不由它派生）。
    /// <para>
    /// <b>「废金属」与「金属锭」已于此合并</b>（用户拍板）。合并前金属锭是个 <b>死物品</b>：
    /// 没有任何配方产出它、掉落表里一条都没有、商人也不卖 ⇒ 凡是吃金属锭的配方**全都造不出来**。
    /// 所以这次合并不只是"少一层材料深度"，而是**把一批本来不可达的内容变成可达**。
    /// </para>
    /// <para>
    /// 换算率 <b>1 废 = 1 铁 / 1 锭 = 2 铁</b>（<see cref="SaveMigration.IngotToIronRatio"/>）：
    /// 纯废金属配方的数量**原样不动**（玩家今天能造的东西成本零突变），而 ×2 是唯一能保住全部 authored 档位序的系数
    /// （自制猎枪 铁4 &gt; 自制霰弹枪 铁3；重头箭 铁2 &gt; 自制箭 铁1；重弩 铁4 &gt; 轻弩 铁2）——
    /// 若按 1:1 直接相加，这几组会**全部倒挂**（重头箭反而比普通箭便宜）。
    /// </para>
    /// </summary>
    public const string IronKey = "iron";

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
