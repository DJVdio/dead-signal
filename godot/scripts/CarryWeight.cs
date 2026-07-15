using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat; // Loadout（负重上限/分段）+ ArmorTable（护甲重量单一事实源）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Item.cs / InventoryStore.cs / DogApparel.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 负重上限系统的消费层三件事：
//  ① ItemWeights —— 物品称重（引擎的 Weapon 没有 Weight 字段，护甲有；这里补齐武器/材料/食物/书的重量表）
//  ② ExpeditionBag —— 远征背包：**硬上限**（背不下就是拿不走），搜刮当场制造"拿什么、扔什么"的取舍
//  ③ CarryCapacity —— 把上限算式（Loadout.Capacity）与角色状态/authored 专属效果接起来
//
// 上限算式见 Loadout.CarryLimit：基础 80kg（全员统一，**无"力量"属性**）× 承载能力（残缺×饥饿）× authored 专属乘子（山姆）。
//
// 🔴 [T45·负重激活] 本文件另有三件事——原先缺的**那半本账**：
//  ④ GearWeight —— 一个人**穿在身上、握在手里**的东西有多沉（修复前：装备一克都不进账 ⇒ 出门负重恒 0kg）
//  ⑤ MemberLoad / ExpeditionLoad —— **逐人**的负重账（自己的装备 + 分摊到的战利品），出移速/攻速乘子
//  ⑥ ExpeditionBag.GearKg —— 出门那一刻就压在账上的装备重量（同时吃掉搬运余量）

/// <summary>
/// 物品重量表（kg，全部**拟定待调**）。护甲重量以引擎 <see cref="ArmorTable"/> 的 <c>Weight</c> 为单一事实源；
/// 武器/材料/食物/书的重量在本层定义（引擎 <c>Weapon</c> 无 Weight 字段，且背包重量本就是消费层概念）。
/// <para>
/// 定值原则：**大件压秤、零碎不压秤**——木料/石料/燃料这类笨重物资是负重的主要开销（一趟搬不走几件），
/// 布/钉子/铁丝/药品这类零碎近乎白送。这样"这桶燃料还是那把枪"才是个真问题，而捡颗钉子不必犹豫。
/// 白银是货币，几乎不占重（否则没法带钱去找商人）。
/// </para>
/// </summary>
public static class ItemWeights
{
    /// <summary>食物：每份（1 人 1 餐）的重量。</summary>
    public const double FoodPerPortionKg = 0.5;

    /// <summary>书：一本的重量。</summary>
    public const double BookKg = 0.5;

    /// <summary>未登记材料的兜底重量。</summary>
    public const double DefaultMaterialKg = 0.5;

    /// <summary>未登记武器的兜底重量。</summary>
    public const double DefaultWeaponKg = 2.0;

    /// <summary>未登记护甲的兜底重量。</summary>
    public const double DefaultArmorKg = 1.0;

    /// <summary>
    /// 弹药按发计重的**兜底**值（未单独登记的口径走它：长子弹、削尖木箭/自制箭/碳纤维箭）。
    /// <para>⚠ 「各口径统一」已被用户在数值表上推翻：短/中/鹿弹现各有自己的重量
    /// （见 <see cref="_materialKg"/> 里的 ammo_* 三行）——口径越大越沉，短子弹最轻。</para>
    /// </summary>
    public const double AmmoPerRoundKg = 0.03;

    /// <summary>材料重量（按 <see cref="MaterialDef.Key"/>；未登记走 <see cref="DefaultMaterialKg"/>）。</summary>
    private static readonly Dictionary<string, double> _materialKg = new()
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
        [Materials.IronKey] = 1.5,
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
        [Materials.WeaponPartsKey] = 0.5,
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
        ["beans"] = 0.6,         // 一把干豆
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

    /// <summary>武器重量（按 <c>WeaponTable</c> 的武器名；未登记走 <see cref="DefaultWeaponKg"/>）。</summary>
    private static readonly Dictionary<string, double> _weaponKg = new()
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
        // （比棍棒 1.5kg 还沉），荒谬。🔴 武器重量的真源就在本字典，Weapon 记录里根本没有重量字段。
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

    /// <summary>护甲重量（按护甲名，取自引擎 <see cref="ArmorTable"/> 的 <c>Weight</c>——单一事实源，不在本层复制数值）。</summary>
    private static readonly Dictionary<string, double> _armorKg = BuildArmorKg();

    private static Dictionary<string, double> BuildArmorKg()
    {
        var d = new Dictionary<string, double>();
        foreach (ArmorLayer layer in new[]
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
            ArmorTable.DogClothVest(), ArmorTable.DogLeatherVest(), ArmorTable.DogPocketVest(),
            ArmorTable.DogIronHelmet(), ArmorTable.DogWireHelmet(),
        })
        {
            d[layer.Name] = layer.Weight;
        }
        return d;
    }

    /// <summary>单件材料重量（未登记走兜底；弹药按 <see cref="AmmoPerRoundKg"/>）。</summary>
    public static double MaterialKg(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return DefaultMaterialKg;
        }

        if (_materialKg.TryGetValue(key, out double kg))
        {
            return kg;
        }

        return key.StartsWith("ammo_", StringComparison.Ordinal) ? AmmoPerRoundKg : DefaultMaterialKg;
    }

    /// <summary>
    /// 单件武器重量（按武器名；未登记走 <see cref="DefaultWeaponKg"/>）。
    ///
    /// <para>
    /// **改装变体**（"步枪（创伤型・加长枪管）"）不在本表里 —— 回落到它的**基础武器**重量
    /// （<see cref="ModdedWeaponRegistry.BaseNameOf"/>）。不这么做的话，一把改装狙击枪会按"未登记武器"
    /// 算成 2kg（比手枪还轻），负重系统当场失真。
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>[T47] 改装件的增减重在这里【真的进负重账】</b> —— 用户原话：
    /// 「<b>我希望重量在改装中是一个重要的因素</b>，玩家可以把一把武器改装得很强，但是一出门就进入负重 debuff」。
    /// <list type="bullet">
    /// <item>基础重 × 各改装件重量乘子（<see cref="ModdedWeaponRegistry.WeightMultiplierOf"/>，
    ///       多条改装**连乘**——百分比一律乘算，CLAUDE.md 铁律）。</item>
    /// <item>[carryweight2·枪重翻倍后] 满改装步枪 7.5 → <b>15.1875kg</b>（创伤型 +50% × 加长枪管 +35%）；
    ///       满改装狙击 9.0 → <b>18.225kg</b>。</item>
    /// <item>轻质化那一档是**减重**（−15% / −25%）：减重本身就是它唯一的收益，所以它必须真的减。</item>
    /// </list>
    /// ⚠️ <b>但代价的【形态】被用户后来澄清过，别照着上面那句原话去宣传</b>：三条线就是 <b>30/50/80，不改</b>
    /// ⇒ 满改装步枪出门（中期配置 24.99kg）**仍在 30kg 免罚线下、一分不罚**（余量只剩 5kg）。
    /// 改装的代价是「<b>它吃掉你的搜刮余量</b>」：免罚余量 12.7 → 5.0kg、硬余量 62.7 → 55.0kg。
    /// [carryweight2] 枪重翻倍后，**原厂中期步枪也搬不空最大点位（66kg，硬余量 62.7）了**——重武器出门余量更小
    /// 正是本轮翻倍的用户意图；满改装只是把这个差距再拉大（多留 7.69kg 货在原地）。
    /// 「要么带甲带枪、要么带货」这个取舍是通过**余量**实现的，不是"出门即减速"——它把选择权留给玩家。
    /// </para>
    /// <para>
    /// <b>原厂武器 / 未登记名 ⇒ 乘子恒 1.0</b> ⇒ 既有负重算式**逐位零变化**。
    /// 这里是改装重量进入负重系统的**唯一入口**（<c>GearWeight.OfWeapons</c> 把 <c>Weapon.Name</c> 原样喂进来），
    /// 所以 <c>ExpeditionBag</c> / <c>CampMain</c> / <c>Actor</c> 一个字都不用改（<c>impl-carryweight</c> 的 HANDOFF）。
    /// </para>
    /// </summary>
    public static double WeaponKg(string? name)
    {
        if (name is null) return DefaultWeaponKg;
        if (_weaponKg.TryGetValue(name, out double kg)) return kg;

        if (ModdedWeaponRegistry.BaseNameOf(name) is { } baseName
            && _weaponKg.TryGetValue(baseName, out double baseKg))
        {
            // ← [T47] 改装的重量代价就落在这一个乘子上。原厂武器走不到这条分支（上面已 return）。
            return baseKg * ModdedWeaponRegistry.WeightMultiplierOf(name);
        }
        return DefaultWeaponKg;
    }

    /// <summary>单件护甲重量（取引擎护甲表 Weight；未登记走兜底）。</summary>
    public static double ArmorKg(string? name)
        => name != null && _armorKg.TryGetValue(name, out double kg) ? kg : DefaultArmorKg;

    /// <summary>一件库存物品的重量（食物/材料按数量成堆计）。</summary>
    public static double Of(Item item)
    {
        if (item == null)
        {
            return 0;
        }

        return item.Category switch
        {
            ItemCategory.Food => FoodPerPortionKg * Math.Max(0, item.FoodQuantity),
            ItemCategory.Material => MaterialKg(item.RefKey) * Math.Max(0, item.MaterialQuantity),
            ItemCategory.Weapon => WeaponKg(item.RefKey),
            ItemCategory.Armor => ArmorKg(item.RefKey),
            ItemCategory.Book => BookKg,
            ItemCategory.Light => 0.5, // 手电/火把
            _ => 0,
        };
    }

    /// <summary>
    /// 一条战利品的重量——搜刮前预判"这堆背不背得下"用（与 <see cref="Of"/> 同口径）。
    /// 工具（calipers/sawblade/beaker）落地进的是营地工作台而非背包，**不计重**。
    /// </summary>
    public static double OfLoot(LootItem loot) => loot.Kind switch
    {
        LootKind.Food => FoodPerPortionKg * Math.Max(0, loot.Quantity),
        LootKind.Material => MaterialKg(loot.RefId) * Math.Max(0, loot.Quantity),
        LootKind.Weapon => WeaponKg(loot.RefId),
        LootKind.Armor => ArmorKg(loot.RefId),
        LootKind.Book => BookKg,
        LootKind.Tool => 0,
        _ => 0,
    };

    /// <summary>一堆库存物品的总重。</summary>
    public static double TotalOf(IEnumerable<Item> items)
        => items?.Sum(Of) ?? 0;

    /// <summary>一堆战利品的总重。</summary>
    public static double TotalOfLoot(IEnumerable<LootItem> loot)
        => loot?.Sum(OfLoot) ?? 0;

    /// <summary>可拆堆的战利品（食物/材料成堆）才谈得上"只拿得走几件"；其余是整件，要么拿要么不拿。</summary>
    public static bool IsStackable(LootItem loot)
        => loot.Kind is LootKind.Food or LootKind.Material;

    /// <summary>取该战利品的单件重量（成堆的取 1 件；整件的即其本身）。</summary>
    public static double UnitKgOf(LootItem loot) => loot.Kind switch
    {
        LootKind.Food => FoodPerPortionKg,
        LootKind.Material => MaterialKg(loot.RefId),
        _ => OfLoot(loot),
    };
}

/// <summary>
/// 🔴 [T45] **装备重量**——一个人**穿在身上、握在手里**的东西有多沉。
///
/// <para>═══ 这个类是为一条真断链写的 ═══
/// T45 之前，负重账（<see cref="ExpeditionBag"/>）**只算搜刮来的战利品**，出门那一刻背包是空的 ⇒
/// **玩家出门时负重恒为 0kg**，一把 7.5kg 的满改装步枪、一身 15kg 的板甲，对负重系统的贡献都是**零**。
/// 用户原话：「我希望重量在改装中是一个重要的因素，玩家可以把一把武器改装得很强，**但是一出门就进入负重 debuff**」——
/// 这句话在断链下**无论把改装重量写多大都不可能成立**。本类把装备接进同一本账。
/// </para>
///
/// <para><b>两个单一事实源，本类一个都不复制</b>：
/// 武器走 <see cref="ItemWeights.WeaponKg"/>（含改装变体回落基础武器），护甲走引擎 <c>ArmorLayer.Weight</c>。</para>
/// </summary>
public static class GearWeight
{
    /// <summary>
    /// 手里握着的武器总重。喂 <c>Pawn.HeldWeapons</c>（= <see cref="WeaponLoadout.HeldWeapons"/>）——
    /// <b>双手握一把的去重已在那里收口</b>，所以抱着重剑的人只算一次 3.0kg，不是两次。
    /// <para>
    /// 🔴 <b>改装件增重的唯一入口是 <see cref="ItemWeights.WeaponKg"/></b>（见该函数）：改装变体武器的
    /// <c>Weapon.Name</c>（如"步枪（创伤型·加长枪管）"）在这里被原样喂进去。<c>impl-weaponmod</c> 只要让
    /// <see cref="ItemWeights.WeaponKg"/> 对改装名返回**改装后的实重**，负重账就自动吃到——本类不必改一个字。
    /// </para>
    /// </summary>
    public static double OfWeapons(IEnumerable<Weapon>? held)
        => held?.Sum(w => Math.Max(0, ItemWeights.WeaponKg(w.Name))) ?? 0;

    /// <summary>
    /// 身上穿的护甲总重。喂 <c>Pawn.WornArmor</c>（= 11 槽投影出的生效层）——**逐件计**：
    /// 成对品（鞋/手套）两只 = 两层，各算各的重量（一只 0.25kg 的运动鞋，两只 0.5kg）。
    /// 天生层（丧尸腐皮，<c>Weight = 0</c>）不影响结果。
    /// </summary>
    public static double OfArmor(IEnumerable<ArmorLayer>? worn)
        => worn?.Sum(a => Math.Max(0, a.Weight)) ?? 0;

    /// <summary>某人身上的全部装备重量（持械 + 穿戴）。</summary>
    public static double Of(IEnumerable<Weapon>? held, IEnumerable<ArmorLayer>? worn)
        => OfWeapons(held) + OfArmor(worn);
}

/// <summary>
/// 开局幸存者的初始装备清单（<c>Pawn.Create</c> 的**单一事实源**）。
/// <para>
/// 存在理由：覆盖自检要能**在不起 Godot 的情况下算出"一个新幸存者出门有多重"**。若这份清单只写在
/// <c>Pawn.Create</c> 的方法体里（Godot 类型，进不了单测），测试就只能把名字**抄一遍**——两份事实源一漂移，
/// "出门负重 ≠ 0" 这条护栏就会在无声中失效。故把清单提到纯逻辑层，<c>Pawn.Create</c> 照着它穿。
/// </para>
/// </summary>
public static class SurvivorStartingKit
{
    /// <summary>开局三件基础衣物（鞋不分左右，但**一只占一只脚槽**，故发两只才护住双脚）。</summary>
    public static IReadOnlyList<(string Item, EquipSlot? Slot)> Apparel { get; } = new[]
    {
        (ArmorTable.LongSleeveShirt().Name, (EquipSlot?)null),
        (ArmorTable.Trousers().Name, (EquipSlot?)null),
        (ArmorTable.Sneakers().Name, (EquipSlot?)EquipSlot.LeftFoot),
        (ArmorTable.Sneakers().Name, (EquipSlot?)EquipSlot.RightFoot),
    };

    /// <summary>开局衣物的总重（kg）——不含武器。</summary>
    public static double ApparelKg
        => Apparel.Sum(a => ItemWeights.ArmorKg(a.Item));

    /// <summary>一个开局幸存者出门时身上的总重（kg）：衣物 + 初始武器（手枪 or 匕首）。</summary>
    public static double GearKg(bool usePistol)
        => ApparelKg + ItemWeights.WeaponKg(usePistol ? "手枪" : "匕首");
}

/// <summary>
/// 🔴 [T45] **一个队员这一趟的负重账**：他自己的装备 + 他分摊到的队伍战利品，对着**他自己的上限**分档。
///
/// <para><b>为什么是逐人而不是全队一个数</b>：用户要的是「**你**把**你的**枪改装得很强 ⇒ **你**一出门就慢」。
/// 若只按全队总账分档，一个人的满改装会被队友摊薄（3 人队里 +3.5kg 只抬高全队比例 1.5%），
/// 那条代价就消失了。逐人分档下，**背板甲的那个人自己走得慢，队友不受连累**。</para>
///
/// <para><b>战利品怎么分</b>：狗先驮满它那份（口袋狗衣 8kg，见 <c>DogGearCatalog.PocketVestCapacity</c>），
/// 剩下的按**各人运力占比**摊给人——背得动的人多背。这不是物理模拟，是"队伍会自己把货分匀"的合理近似；
/// 它让狗衣的 8kg 真正**从人的肩膀上卸下来**，而不只是一个抽象的上限数字。</para>
/// </summary>
public readonly record struct MemberLoad
{
    /// <summary>他身上的装备（持械 + 穿戴），kg。</summary>
    public double GearKg { get; init; }

    /// <summary>他分摊到的战利品，kg。</summary>
    public double LootShareKg { get; init; }

    /// <summary>他自己的负重上限（<c>CarryCapacity.For</c>：残缺 × 饥饿 × 山姆乘子），kg。</summary>
    public double LimitKg { get; init; }

    /// <summary>他实际背着的总重（kg）。</summary>
    public double CarriedKg => GearKg + LootShareKg;

    /// <summary>他当前的档位。</summary>
    public LoadoutTier Tier => Loadout.TierOf(CarriedKg, LimitKg);

    /// <summary>他的移速乘子（1.0 = 无罚）。<b>Actor 的移动链乘算消费它</b>。</summary>
    public double SpeedMultiplier => Loadout.SpeedMultiplier(CarriedKg, LimitKg);

    /// <summary>他的出手间隔乘子（1.0 = 无罚，&lt;1 = 攻速变慢）。<b>Actor 的出手间隔消费它</b>。</summary>
    public double AttackSpeedMultiplier => Loadout.AttackSpeedMultiplier(CarriedKg, LimitKg);

    /// <summary>没有负重账（在营地 / 不在探索队）——全 1.0，零回归。</summary>
    public static MemberLoad None => default; // Gear=0, Loot=0, Limit=0 ⇒ Ratio=0 ⇒ 两个乘子皆 1.0
}

/// <summary>探索队负重账的分摊算式（纯函数；<c>CampMain</c> 的出门/搜刮/丢弃三处都走它，不另造数学）。</summary>
public static class ExpeditionLoad
{
    /// <summary>队伍装备总重（kg）——把每个人的 <see cref="GearWeight.Of"/> 加起来，喂 <c>ExpeditionBag.SetGear</c>。</summary>
    public static double PartyGearKg(IEnumerable<double>? memberGearKg)
        => Math.Max(0, memberGearKg?.Sum() ?? 0);

    /// <summary>压在**人**肩上的战利品（kg）＝ 总战利品 − 狗驮走的那部分（狗先驮满）。</summary>
    public static double LootOnHumans(double lootKg, double dogCapacityKg)
        => Math.Max(0, Math.Max(0, lootKg) - Math.Max(0, dogCapacityKg));

    /// <summary>某人分摊到的战利品（kg）＝ 人肩上的战利品 × 他的运力占比。全队运力为 0（都断手了）⇒ 平摊不了，返 0。</summary>
    public static double LootShareFor(double lootOnHumansKg, double memberLimitKg, double totalMemberLimitKg)
        => totalMemberLimitKg <= 0
            ? 0
            : Math.Max(0, lootOnHumansKg) * (Math.Max(0, memberLimitKg) / totalMemberLimitKg);

    /// <summary>拼出某人的完整负重账。</summary>
    public static MemberLoad For(
        double gearKg, double lootKg, double dogCapacityKg, double memberLimitKg, double totalMemberLimitKg)
        => new()
        {
            GearKg = Math.Max(0, gearKg),
            LootShareKg = LootShareFor(LootOnHumans(lootKg, dogCapacityKg), memberLimitKg, totalMemberLimitKg),
            LimitKg = Math.Max(0, memberLimitKg),
        };
}

/// <summary>
/// 远征背包：探索队这一趟能带回来的东西（**硬上限**）。
/// <para>
/// 为什么是硬上限而不是"超重减速"：软惩罚下"多背一点只是慢一点"，玩家总会选择全拿走——**没有取舍**。
/// 硬上限才逼出《这是我的战争》式的当场决定："这桶燃料和这把步枪，只能带一样。"
/// 软惩罚（<see cref="Loadout.SpeedMultiplier"/>）依然在上限**之内**生效：背得越满走得越慢，所以"背满"本身也有代价。
/// </para>
/// 容量由 <see cref="PartyCapacity"/> 汇总（队里每个人的 <see cref="CarryCapacity"/> + 布鲁斯口袋狗衣的 8kg）。
/// </summary>
public sealed class ExpeditionBag
{
    private readonly List<LootItem> _contents = new();

    public ExpeditionBag(double capacityKg)
    {
        CapacityKg = Math.Max(0, capacityKg);
    }

    /// <summary>本趟的负重上限（kg）。</summary>
    public double CapacityKg { get; private set; }

    /// <summary>
    /// 🔴 [T45] 队伍**装备**总重（kg）：出门那一刻就压在账上的东西——每个队员手里的武器 + 身上 11 槽的护甲。
    /// <para>
    /// 修复前这本账<b>只算战利品</b>（<c>CarriedKg => TotalOfLoot(_contents)</c>），出门是空包 ⇒ 负重恒 0kg ⇒
    /// 一把满改装步枪对负重的贡献是零 ⇒ 用户要的「一出门就 debuff」<b>在代码里不可能发生</b>。
    /// </para>
    /// <para>
    /// 装备<b>同时吃掉搬运余量</b>（<see cref="FreeKg"/> 从 <see cref="CarriedKg"/> 扣）——这是有意的：
    /// 穿板甲出门 = 又慢、又背不回东西。这是一本账，不是两本。
    /// </para>
    /// 默认 0 ⇒ 不喂装备时行为与修复前<b>逐位一致</b>（既有 <c>ExpeditionBag</c> 测试零回归）。
    /// </summary>
    public double GearKg { get; private set; }

    /// <summary>背包里**搜刮来的**那部分（kg，不含装备）。</summary>
    public double LootKg => ItemWeights.TotalOfLoot(_contents);

    /// <summary>已背在身上的总重（kg）＝ 装备 + 战利品。</summary>
    public double CarriedKg => GearKg + LootKg;

    /// <summary>
    /// 重设队伍装备总重（<c>CampMain.SyncExpeditionLoad</c> 每帧刷——关内可能断手掉甲/捡起武器，
    /// 装备账必须跟着当下的身体和装备走）。负数按 0 钳制。
    /// </summary>
    public void SetGear(double gearKg) => GearKg = Math.Max(0, gearKg);

    /// <summary>还能再背多少（kg，不为负）。</summary>
    public double FreeKg => Math.Max(0, CapacityKg - CarriedKg);

    /// <summary>背包里的东西（回营时由调用方经 <c>LootApplication.Apply</c> 落地进共享库存）。</summary>
    public IReadOnlyList<LootItem> Contents => _contents;

    /// <summary>装满了（一颗钉子也塞不下了）。</summary>
    public bool IsFull => FreeKg <= 0;

    /// <summary>0~1+ 的负重比例（喂 <see cref="Loadout.SpeedMultiplier"/> / <see cref="Loadout.TierOf"/>）。</summary>
    public double Ratio => CapacityKg > 0 ? CarriedKg / CapacityKg : 0;

    /// <summary>当前档位（轻装 / 负重 / 重负 / 超载）——UI 要一眼可见，玩家靠它决定还拿不拿。</summary>
    public LoadoutTier Tier => Loadout.TierOf(CarriedKg, CapacityKg);

    /// <summary>
    /// 全队总账的移速乘子（30kg 内无罚；30~50 轻度；50~80 加重，按队伍总运力伸缩）。
    /// <para>⚠️ [T45] <b>真正作用到角色身上的是逐人的 <see cref="MemberLoad.SpeedMultiplier"/></b>
    /// （用户要的是"你把你的枪改装得很强 ⇒ 你走得慢"，不是全队摊薄）。本属性是**队伍总账的概览**，
    /// 供 HUD/面板显示与既有校准测试使用。</para>
    /// </summary>
    public double SpeedMultiplier => Loadout.SpeedMultiplier(CarriedKg, CapacityKg);

    /// <summary>全队总账的出手间隔乘子（**从免罚线 30kg 起就线性掉**：50kg −20%、80kg −50%）。逐人口径同上，见 <see cref="MemberLoad.AttackSpeedMultiplier"/>。</summary>
    public double AttackSpeedMultiplier => Loadout.AttackSpeedMultiplier(CarriedKg, CapacityKg);

    /// <summary>
    /// 上限中途变化（关内断了手 / 饿掉一格 / 狗跑了 / 队友死了）→ 重设容量。
    /// 已背的东西**不会凭空消失**：允许 <see cref="CarriedKg"/> 超过新上限，
    /// 由 <see cref="Loadout.SpeedMultiplier"/> 的 Overloaded 陡峭减速兜底（走得动，但很难看）。
    /// </summary>
    public void SetCapacity(double capacityKg) => CapacityKg = Math.Max(0, capacityKg);

    /// <summary>预判：这条战利品背不背得下（不改状态）。</summary>
    public bool CanFit(LootItem loot) => ItemWeights.OfLoot(loot) <= FreeKg + Epsilon;

    /// <summary>整条拿走：背得下才拿，**背不下一件都不拿**（不偷偷截半）。</summary>
    public bool TryAdd(LootItem loot)
    {
        if (!CanFit(loot))
        {
            return false;
        }

        _contents.Add(loot);
        return true;
    }

    /// <summary>
    /// 成堆的能拿几件拿几件（"这堆木头只搬得动两根"）。返回实际拿走的件数（0＝一件也背不下）。
    /// 整件物品（武器/护甲/书）不可拆：背得下算 1、背不下算 0。
    /// </summary>
    public int AddAsManyAsFit(LootItem loot)
    {
        if (!ItemWeights.IsStackable(loot))
        {
            return TryAdd(loot) ? 1 : 0;
        }

        double unit = ItemWeights.UnitKgOf(loot);
        if (unit <= 0)
        {
            return TryAdd(loot) ? loot.Quantity : 0;
        }

        int fits = (int)Math.Floor((FreeKg + Epsilon) / unit);
        int take = Math.Min(Math.Max(0, loot.Quantity), Math.Max(0, fits));
        if (take <= 0)
        {
            return 0;
        }

        _contents.Add(loot.Kind == LootKind.Food
            ? LootItem.Food(take)
            : LootItem.Material(loot.RefId, take));
        return take;
    }

    /// <summary>扔掉（取舍的另一半：腾地方换更值钱的）。</summary>
    public bool Drop(LootItem loot) => _contents.Remove(loot);

    /// <summary>回营落地后清空。</summary>
    public void Clear() => _contents.Clear();

    private const double Epsilon = 1e-9;

    /// <summary>
    /// 一支探索队这一趟的总运力：每个队员的上限之和 + 布鲁斯口袋狗衣的驮运容量。
    /// 狗的负重就此**统一进同一套账**——口袋狗衣不再是独立口径，就是给队伍背包 +8kg（<see cref="DogGearCatalog.PocketVestCapacity"/>，T29 用户手改 6→8）。
    /// </summary>
    /// <param name="memberCapacitiesKg">每个人的上限（各自走 <see cref="CarryCapacity.For"/>）。</param>
    /// <param name="dogCapacityKg">随队狗的驮运容量（<c>DogApparelSlots.TotalCarryCapacity()</c>；没带狗＝0）。</param>
    public static double PartyCapacity(IEnumerable<double> memberCapacitiesKg, double dogCapacityKg)
        => Math.Max(0, memberCapacitiesKg?.Sum() ?? 0) + Math.Max(0, dogCapacityKg);
}

/// <summary>背包里一条战利品的显示名（UI 用；材料查 <see cref="Materials"/> 的中文名，成堆带 ×N）。</summary>
public static class LootDisplay
{
    public static string NameOf(LootItem loot) => loot.Kind switch
    {
        LootKind.Food => loot.Quantity > 1 ? $"食物 ×{loot.Quantity}" : "食物",
        LootKind.Material => loot.Quantity > 1
            ? $"{MaterialName(loot.RefId)} ×{loot.Quantity}"
            : MaterialName(loot.RefId),
        LootKind.Tool => $"{loot.RefId}（工具）",
        _ => loot.RefId,
    };

    private static string MaterialName(string key)
        => Materials.Find(key)?.DisplayName ?? key;
}

/// <summary>
/// 把负重上限算式接到角色身上（引擎 <see cref="Loadout"/> 的消费层门面）。
/// **无"力量"属性**：80kg 基数全员统一，个体差异只来自身体状态（残缺/饥饿）与 authored 专属效果（山姆）。
/// </summary>
public static class CarryCapacity
{
    /// <summary>
    /// 某人的负重上限（kg）。基准人 80kg；三档（30/50/80）随此上限按比例伸缩。[T45] 账里含装备，不只是战利品。
    /// </summary>
    /// <param name="operationCapability">
    /// 该角色**当前实际**的操作能力 0~1（残缺 × 饥饿已折算完，直接喂 <c>Pawn.OperationCapability</c>）——
    /// 断了手、饿着肚子的人背不动东西，与战斗出手间隔同源口径，不另造数学。
    /// </param>
    /// <param name="authoredMultiplier">
    /// authored 专属效果乘子（<c>CampMain.SamCarryCapacityMultFor(pawn)</c>）：
    /// 山姆 L2 他自己 ×1.15、L3 全营 ×1.03，山姆本人**连乘** ×1.15×1.03（≠ 加算的 ×1.18）。
    /// </param>
    public static double For(double operationCapability, double authoredMultiplier = 1.0)
        => Loadout.CarryLimit(operationCapability, authoredMultiplier);

    /// <summary>负重对应的移速乘子（15kg 内无罚；15~25 轻度；25~40 加重）。</summary>
    public static double SpeedMultiplier(double carriedKg, double limitKg)
        => Loadout.SpeedMultiplier(carriedKg, limitKg);

    /// <summary>负重对应的出手间隔乘子（**从免罚线 30kg 起就线性掉**：50kg −20%、80kg −50%）。</summary>
    public static double AttackSpeedMultiplier(double carriedKg, double limitKg)
        => Loadout.AttackSpeedMultiplier(carriedKg, limitKg);

    /// <summary>当前档位（UI 一眼可见：玩家靠 30/50/80 三条线做决策）。</summary>
    public static LoadoutTier TierOf(double carriedKg, double limitKg)
        => Loadout.TierOf(carriedKg, limitKg);

    /// <summary>档位中文名。单一事实源在 <see cref="DisplayNames"/>。</summary>
    public static string TierLabel(LoadoutTier tier) => DisplayNames.Of(tier);

    /// <summary>档位的一行后果说明（UI 悬浮/副标题用）。</summary>
    public static string TierEffect(LoadoutTier tier) => tier switch
    {
        LoadoutTier.Unencumbered => "行动自如",
        LoadoutTier.Encumbered => "移动变慢",
        LoadoutTier.Strained => "移动明显变慢，出手也慢了",
        _ => "几乎走不动——快扔点东西",
    };

    /// <summary>UI 用的一行「背了多少 / 上限多少（档位）」。</summary>
    public static string Format(double carriedKg, double limitKg)
        => $"{carriedKg:0.0} / {limitKg:0.0} kg（{TierLabel(TierOf(carriedKg, limitKg))}）";

    // ———————————— [T45] HUD：**超重了、慢了多少**，必须一眼看见 ————————————
    // 修复前 HUD 只有一行「背包 12.5 / 30.0 kg（负重）」——玩家看得到自己背了多少，
    // **看不到这让他慢了多少**。而"慢了多少"恰恰是负重系统唯一的实际后果。

    /// <summary>
    /// 惩罚短语（无惩罚 ⇒ 空串，HUD 保持干净）：「移速 −14%」「移速 −27%，攻速 −6%」。
    /// 乘子四舍五入到整百分点；不足 1% 的（如 −0.4%）不显示，避免 HUD 抖动。
    /// </summary>
    public static string PenaltyText(double carriedKg, double limitKg)
    {
        int movePct = (int)Math.Round((1.0 - Loadout.SpeedMultiplier(carriedKg, limitKg)) * 100);
        int atkPct = (int)Math.Round((1.0 - Loadout.AttackSpeedMultiplier(carriedKg, limitKg)) * 100);

        if (movePct <= 0 && atkPct <= 0)
        {
            return "";
        }

        return atkPct > 0 ? $"移速 −{movePct}%，攻速 −{atkPct}%" : $"移速 −{movePct}%";
    }

    /// <summary>
    /// HUD 一行的**队伍总账**：「负重 17.3+10.0 = 27.3 / 40.0 kg（重负）」——
    /// 装备与战利品**分开显示**，玩家才看得出"这 17.3kg 是我自己穿上去的、不是捡来的"。
    /// </summary>
    public static string FormatBag(double gearKg, double lootKg, double capacityKg)
        => $"装备 {gearKg:0.0} + 战利品 {lootKg:0.0} = " + Format(gearKg + lootKg, capacityKg);

    /// <summary>
    /// HUD 一行的**某个人**：「诺蒂 负重 −5%」。<paramref name="name"/> 为空则只出档位与惩罚。
    /// 无惩罚 ⇒ 返回空串（调用方据此整段省略）。
    /// </summary>
    public static string FormatMember(string name, MemberLoad load)
    {
        string penalty = PenaltyText(load.CarriedKg, load.LimitKg);
        return penalty.Length == 0
            ? ""
            : $"{name} {TierLabel(load.Tier)}（{penalty}）";
    }
}
