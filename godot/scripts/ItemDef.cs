using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat; // ArmorTable（护甲重量单一事实源）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（与 CarryWeight.cs / Materials.cs 一样以 Link 编入单测）。
//
// ═══ [R6·物品单一登记入口] ═══
// 从前"加一件物品要改 5~7 处"，重量这一格散在 CarryWeight 的三张字典里（武器/护甲/材料各一张）。
// 三张表**各建各表**的代价是"漏登记 → 静默落兜底 → 数值悄悄错"：棉帽 0.15kg 曾被算成 1.0kg（6.7 倍）、
// 消防斧/雪镜同款、cotton_hat 一度根本没进产出表。本文件把三张重量表**合并成一处按类别分区的统一登记表**
// （<see cref="ItemRegistry"/>）——武器/护甲/材料的重量**从这一张表投影**，CarryWeight 只做薄读取。
//
// 🔴 **数据驱动组合，不是继承**：这里只放"所有物品共享的元数据"（当前＝重量 kg）。武器伤害/护甲防御这类
//   "分支特有"的数值**留在各自的战斗层表**（WeaponTable / ArmorTable，那是零依赖引擎，故意不含重量/图标）——
//   本表不把它们并进来，也不给引擎 Weapon 加父类。图标/槽位/配方引用等其余登记是**下一步**并入本表的扩展位。
//
// 🔴 **护甲重量继续从引擎 ArmorTable 派生**（<see cref="ItemRegistry.ArmorRoster"/> 读 <c>ArmorLayer.Weight</c>），
//   **不在本层复制护甲数值**——这条单一事实源不动，本表只是把它和武器/材料的重量收进同一个登记入口。

/// <summary>物品大类（重量登记按类别分区；同名不跨类，查重量各查各区）。</summary>
public enum ItemKind
{
    /// <summary>武器（按中文名——引擎 <c>Weapon</c> 记录里没有 Weight 字段，重量真源在本表）。</summary>
    Weapon,

    /// <summary>材料/食材/弹药（按 <c>MaterialDef.Key</c> 英文键）。</summary>
    Material,

    /// <summary>护甲（按护甲名；重量派生自引擎 <c>ArmorTable</c>）。</summary>
    Armor,
}

/// <summary>
/// 一件物品的**跨品类共享元数据**（当前只有重量 kg）。这是 R6「单一登记入口」的记录形态——
/// 武器/护甲/材料统一投影成 <see cref="ItemDef"/>，供负重账/UI 以同一口径遍历。
/// <para>图标/槽位/防御/配方引用等其余共享属性是本记录的**下一步扩展位**（本轮只并了重量这一刀）。</para>
/// </summary>
public readonly record struct ItemDef(ItemKind Kind, string Key, double WeightKg);

/// <summary>
/// 🔴 [R6] **物品重量的单一登记表**（按类别分区）。加一件新物品时，它的重量在这里登记**一处**：
/// 武器/材料是本表里的字面值，护甲从引擎 <see cref="ArmorTable"/> 派生（<b>不复制护甲数值</b>）。
/// <para><c>ItemWeights</c>（CarryWeight.cs）只从本表读取——三张旧字典 <c>_weaponKg</c>/<c>_materialKg</c>/<c>_armorKg</c>
/// 现在都是本表的薄别名，不再各存一份数值。</para>
/// </summary>
public static class ItemRegistry
{
    /// <summary>
    /// 武器重量（kg，按 <c>WeaponTable</c> 的武器中文名；未登记走 <c>ItemWeights.DefaultWeaponKg</c>）。
    /// 每把 <c>WeaponTable.Arsenal()</c> 武器必须在此登记——焊死测试
    /// <c>ItemWeight_EveryArsenalWeapon_HasExplicitWeightRegistered</c> 盯着，漏一把即红。
    /// </summary>
    public static readonly Dictionary<string, double> Weapons = new()
    {
        // 近战
        // [wiki 同步·用户重量手改一轮]：短剑/长剑/尖头锤/破甲锤加重（更贴近真实配重）、刺剑略降。
        // git 证实这些新值代码从没有过（＝用户新写，非陈旧格）。重剑/枪械/弩见下方 ⚠ 待同步（吃碰撞校准，已上抛）。
        ["匕首"] = 0.5,
        ["短剑"] = 1.6,          // wiki 同步（1.2 → 1.6）
        ["刺剑"] = 1.2,          // wiki 同步（1.3 → 1.2）
        ["长剑"] = 2.4,          // wiki 同步（1.8 → 2.4）
        // [wiki 同步·carryweight2] 重剑 3.0 → **3.2**（用户手改，与枪械/弩翻倍同批落地）。
        ["重剑"] = 3.2,
        ["草叉"] = 2.5,
        ["棍棒"] = 1.5,
        ["尖头锤"] = 4.0,        // wiki 同步（2.5 → 4.0）
        ["破甲锤"] = 4.5,        // wiki 同步（4.0 → 4.5）
        // [批次25·T44] 消防斧：头重杆轻的一件东西——与重剑同重，比尖头锤沉。拟定待调。
        ["消防斧"] = 3.0,
        // [T56] 骨刀：一片削出来的骨头——全表最轻的武器（比匕首 0.5kg 还轻）。拟定待调。
        // ⚠ 用户在数值表上**没填这一格**。不登记就落到 DefaultWeaponKg = 2.0 ⇒ 一把骨刀重 2kg
        // （比棍棒 1.5kg 还沉），荒谬。🔴 武器重量的真源就在本表，Weapon 记录里根本没有重量字段。
        ["骨刀"] = 0.4,

        // 枪械
        // [wiki 同步·carryweight2] 用户在 wiki 上把长枪重量整体加重（**翻倍**）：自制猎枪 3→7.5 / 霰弹 3.2→6 /
        //   步枪 4→7.5 / 狙击 6→9。这批曾因 impl-carryweight 免罚线(30/50/80)校准而暂缓，现随负重余量表
        //   重推一起落地：三条线 30/50/80 **不动**，代价通过"装备吃掉搜刮余量"体现（重武器出门余量更小）。
        //   核实：普通中期(步枪7.5+皮甲+军盔)出门 17.30kg **仍在 30kg 免罚线下**，"普通配置出门不罚"不变量成立。
        //   余量表见 Loadout.cs 顶部 + CarryLoadWiringTests / CarryCapacityTests。
        ["手枪"] = 1.0,
        ["冲锋枪"] = 3.0,
        ["自制猎枪"] = 7.5,
        ["自制霰弹枪"] = 6.0,
        // 「栓动猎枪」已按用户在数值表上的删除撤下（原 3.5kg）。
        ["步枪"] = 7.5,
        ["狙击枪"] = 9.0,

        // 弓弩
        ["短弓"] = 0.8,
        ["狩猎弓"] = 1.0,
        ["反曲弓"] = 1.2,
        ["竞技复合弓"] = 1.8,
        ["长弓"] = 1.5,
        ["单手轻弩"] = 2.0,
        // [wiki 同步·carryweight2] 复合弩 3→4.5 / 双手重弩 4.5→9（用户手改，与枪械翻倍同批落地）。
        ["复合弩"] = 4.5,
        ["双手重弩"] = 9.0,
    };

    /// <summary>
    /// 材料重量（kg，按 <c>MaterialDef.Key</c>；未登记走 <c>ItemWeights.DefaultMaterialKg</c>，
    /// 以 <c>ammo_</c> 开头的未登记口径走 <c>ItemWeights.AmmoPerRoundKg</c>）。
    /// <para>
    /// 定值原则：**大件压秤、零碎不压秤**——木料/石料/燃料这类笨重物资是负重的主要开销（一趟搬不走几件），
    /// 布/钉子/铁丝/药品这类零碎近乎白送。白银是货币，几乎不占重（否则没法带钱去找商人）。
    /// </para>
    /// </summary>
    public static readonly Dictionary<string, double> Materials = new()
    {
        // —— 笨重物资：负重的大头，搬运本身就是代价 ——
        ["stone"] = 3.0,
        ["fuel"] = 3.0,
        // [T68·用户手改] 木料 2.0 → **1.0**（**减半**）。这是本轮减重里影响最大的一格：
        // 家具/围栏配方动辄 8~16 木料 ⇒ 一趟能扛回来的建材**几乎翻倍**。用户是有意松绑建造节奏，不是手滑。
        ["wood"] = 1.0,
        // [T46] 铁（废金属 + 金属锭合并）：取 **1.5**，即原废金属的重量，**不取金属锭的 2.0**。
        // 理由：合并后铁的**实际供给 ≈ 原废金属的供给**——金属锭合并前**零获取途径**（是个拿不到的死物品，
        // 这正是本单在修的 bug），它那 2.0 从来没有人真的背过。取 2.0 等于借合并之名给全世界的金属**悄悄加重 33%**，
        // 而背包经济（尤其装备计入负重之后）对大宗材料的单位重量极其敏感。
        [DeadSignal.Godot.Materials.IronKey] = 1.5,
        // [批次20·拆除回收] 废木料：碎料，比整根木料（2.0）轻——但拆一段围栏掉 4 堆，扛回去也是 4kg。
        ["scrap_wood"] = 1.0,
        ["tanning_solution"] = 1.0,

        // —— 中等 ——
        // [T68·用户手改] 生皮 0.8 → **1.0**、皮革 0.5 → **0.6**：本轮**唯二加重**的两格
        // （其余全在减重）。方向是一致的：**皮线整体变沉** ⇒ 出门剥皮扛回来这件事本身要算账。
        ["rawhide"] = 1.0,
        ["leather"] = 0.6,
        // [T68·用户手改] 绳子 0.5 → **0.15**（**降到不足 1/3**）。绳子是弓/陷阱/家具的通用配料，
        // 原先 0.5 与皮革同档明显偏重（一捆麻绳不该跟一张鞣好的皮一样沉）。
        ["rope"] = 0.15,
        ["components"] = 0.5,
        // [批次21·T26] 武器零件：弩机与扳机组是**淬过火的钢件**，比通用机括件（components 0.5）沉一档。
        // [T68·用户手改] 0.6 → **0.5**：与通用机括件拉平（那"沉一档"的设定用户没要）。
        [DeadSignal.Godot.Materials.WeaponPartsKey] = 0.5,
        ["first_aid_kit"] = 0.5,
        // [批次20·拆除回收] 胶水：一罐骨胶。它不重——重的是它稀缺。
        ["glue"] = 0.5,

        // —— 零碎：几乎白送 ——
        ["cloth"] = 0.3,
        ["bone"] = 0.3,
        // [T68·用户手改] 铁丝 0.3 → **0.25**、钉子 0.2 → **0.05**（钉子降到 1/4——一把钉子本就该几乎不占分量）。
        ["wire"] = 0.25,
        ["gunpowder"] = 0.3,
        ["splint"] = 0.3,
        ["nails"] = 0.05,
        ["bandage"] = 0.1,
        ["herbal_bandage"] = 0.1,
        ["herbal_salve"] = 0.1,
        ["needle_thread"] = 0.05,
        ["antibiotics"] = 0.05,
        ["medicine"] = 0.05,
        ["dandelion"] = 0.05,
        ["rosehip"] = 0.05,
        ["laojunxu"] = 0.05,
        ["bullet_parts"] = 0.05,

        // —— [批次21·T14] 食材：**搬粮食是要占背包的**（原先一条都没登记 ⇒ 全落 DefaultMaterialKg 0.5，
        //    一只老鼠和一箱军用口粮一样重，那是个真 bug）。数值拟定待调。
        //    口径：按"这一份实物有多沉"定，**与热量点无关**（热量高的不一定沉——面粉沉但不如兔子顶饱）。
        //    ⚠️ 但两者确实正相关，这是有意的：口粮又重又顶饱（背一趟够两顿），蘑菇又轻又不顶饱
        //    ⇒ "背什么回来"本身就是一道要算的账（重量吃背包、热量喂人）。
        ["rabbit"] = 1.5,        // 一只野兔，带皮带骨
        ["ration"] = 1.0,        // 军用单兵口粮：整包罐头+压缩饼干+配件，全表最沉的一份食材
        ["fish"] = 1.0,          // 一条河鱼
        ["flour"] = 1.0,         // 一袋面粉
        ["canned_food"] = 0.6,   // 铁皮罐头，小而沉
        ["rat"] = 0.3,           // 老鼠：轻，也确实不顶饱（[T67] 它已下不了锅，要先宰杀——但整只的重量没变）
        ["pigeon"] = 0.3,        // 鸟（[T67] 原「鸽子」，只改显示名不改键）：肉少骨头多
        ["potato"] = 0.3,        // 土豆
        ["mushroom"] = 0.05,     // 蘑菇：几乎白送（玫瑰果/蒲公英同档，见上方医疗原料那几行——它们同时也是食材）

        // —— [T67] 宰杀链的四样新材料：**必须显式登记**，落到 DefaultMaterialKg = 0.5 的兜底上就是把设计交给了 bug ——
        //    （前车之鉴：wiki 上「铁」的 0.5kg 其实是兜底冒充设计决策，被 impl-iron 抓出来过。）
        ["rat_meat"] = 0.15,     // 老鼠肉：一只老鼠身上剔得出的肉，只有整只的一半——**宰杀是减重的**（骨头和皮留在案板上）
        ["bird_meat"] = 0.15,    // 鸟肉：同上
        ["feather"] = 0.02,      // 羽毛：一支箭的尾羽（三片飞羽）。**全表最轻的东西**——比蘑菇还轻一档，背一百根也不到 2kg。
                                 //       它本来就该是这样：真正贵的是**那只鸟**，不是它身上的毛。
        ["leather_scrap"] = 0.2, // 碎皮革：零碎皮子。攒四块（4 × 0.2 = 0.8kg）缝成一张生皮（1.0kg）——
                                 // 缝完**略重一点**（针脚+紧实），账是对的，也没给玩家开"缝皮减重"的套利口子

        // —— 弹药：按口径分重（用户手改，原先三种共用 AmmoPerRoundKg 0.03）——
        // 口径越大越沉：短子弹（手枪/冲锋枪）最轻，鹿弹（霰弹壳）最沉。
        // 长子弹与其余三种箭未单独登记 ⇒ 仍走 AmmoPerRoundKg 兜底（=0.03，与 wiki 弹药表一致）。
        ["ammo_short"] = 0.01,
        ["ammo_medium"] = 0.02,
        ["ammo_buck"] = 0.05,
        // 重头箭：用户在 wiki 弹药表上把它单独加重（箭头灌铅 ⇒ 0.03 → 0.05），不再走兜底。
        // 其余三种箭（削尖木箭/自制箭/碳纤维箭）表上仍是 0.03，正好等于兜底，无须登记。
        ["ammo_arrow_heavy"] = 0.05,

        // 货币：带钱出门不该挤占背包
        ["silver"] = 0.01,
    };

    /// <summary>
    /// 护甲花名册——重量登记的**唯一护甲清单**。加一件护甲到 <see cref="ArmorTable"/> 后必须补进这里，
    /// 否则称重时静默落 <c>ItemWeights.DefaultArmorKg</c>（=1.0kg）兜底（棉帽 0.15→1.0 的 6.7 倍 bug 就是这么来的）。
    /// 焊死测试 <c>ItemWeight_EveryArmorTableLayer_IsRegistered</c> 反射枚举 ArmorTable 的单层护甲方法，漏一件即红。
    /// </summary>
    public static readonly IReadOnlyList<ArmorLayer> ArmorRoster = new[]
    {
        ArmorTable.LongSleeveShirt(), ArmorTable.FloralShirt(), ArmorTable.Trousers(), ArmorTable.Sneakers(),
        ArmorTable.Shorts(), ArmorTable.ChestPlate(), ArmorTable.CoarseClothVest(), ArmorTable.CoarseClothCoat(),
        ArmorTable.ClothJacket(), ArmorTable.DenimJacket(), ArmorTable.LeatherJacket(), ArmorTable.Leather(),
        ArmorTable.Plate(), ArmorTable.WorkGloves(),
        ArmorTable.MilitaryHelmet(), ArmorTable.RiotHelmet(),
        // [T68] 🔴 **补登记 5 件漏网的**（战争面具 / 棉帽 / 粗布衬衫 / 粗布短裤 / 粗布长裤）：
        // 它们从落地那天起就没进过这张表 ⇒ 一律落到 DefaultArmorKg = **1.0kg**。
        // 后果不是"差一点"：一顶 0.15kg 的棉帽被算成 1kg（**6.7 倍**），一张 0.3kg 的战争面具算成 1kg（3.3 倍）。
        // 负重刚接线（装备计入负重账）⇒ 这个兜底值正在**凭空吃掉玩家的背包**。
        // 📌 纪律：**兜底值不是设计决策**——它冒充设计决策，就是 bug。
        ArmorTable.WarMask(), ArmorTable.CottonHat(),
        ArmorTable.CoarseClothShirt(), ArmorTable.CoarseShorts(), ArmorTable.CoarseTrousers(),
        // [T68] 用户新加的三件。
        ArmorTable.HorrorArmor(), ArmorTable.Sunglasses(), ArmorTable.PlainGlasses(),
        // [T71] 自制简易墨镜（木缝雪镜，0.1kg）——显式登记，别落 DefaultArmorKg=1.0（0.1kg 木镜算 1kg 是 10 倍）。
        ArmorTable.SelfMadeSnowGoggles(),
        ArmorTable.AnkleGuard(),   // [T72] 护踝鞋具（成对·脚槽，0.75kg）——追加末尾，重量真源在 ArmorLayer.Weight
        ArmorTable.BallisticVest(),   // [警察局] 防弹背心（贴身层·护胸腹，2.5kg 拟定待Sim校准）——追加末尾，重量真源在 ArmorLayer.Weight
        ArmorTable.DogClothVest(), ArmorTable.DogLeatherVest(), ArmorTable.DogPocketVest(),
        ArmorTable.DogIronHelmet(), ArmorTable.DogWireHelmet(),
    };

    /// <summary>
    /// 护甲重量（kg，按护甲名，取自引擎 <see cref="ArmorTable"/> 的 <c>Weight</c>——单一事实源，不在本层复制数值）。
    /// 从 <see cref="ArmorRoster"/> 投影而来。
    /// </summary>
    public static readonly Dictionary<string, double> Armor =
        ArmorRoster.ToDictionary(layer => layer.Name, layer => layer.Weight);

    /// <summary>
    /// 三个分区的统一投影——供负重账/UI/覆盖测试以同一口径遍历全部已登记物品的重量。
    /// （护甲的 <see cref="ItemDef.Key"/> 是护甲名，与武器/材料同为字符串键，但 <see cref="ItemDef.Kind"/> 区分品类。）
    /// </summary>
    public static IEnumerable<ItemDef> All =>
        Weapons.Select(kv => new ItemDef(ItemKind.Weapon, kv.Key, kv.Value))
            .Concat(Materials.Select(kv => new ItemDef(ItemKind.Material, kv.Key, kv.Value)))
            .Concat(Armor.Select(kv => new ItemDef(ItemKind.Armor, kv.Key, kv.Value)));
}
