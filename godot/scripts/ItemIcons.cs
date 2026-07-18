using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 Item.cs / Materials.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 物品图标映射：物品引用键（RefKey）→ 图标文件路径。Godot 侧的纹理加载在 ItemIconTextures.cs。

/// <summary>
/// 一条物品图标映射（不可变值对象）。
/// <para>
/// <see cref="Slug"/> 是**图标文件名**（不含扩展名，只用 ASCII 小写与下划线）——而物品的引用键
/// （<see cref="Item.RefKey"/>）对武器/护甲/狗装来说是**中文名**（"匕首"/"皮夹克"/"布制狗衣"）。
/// 中文文件名在 Godot 的 .import 流程与跨平台归档里都是雷，所以这里隔一层：中文 id 只活在内存映射表中，
/// 落到磁盘的永远是 ASCII slug。
/// </para>
/// <para>
/// <see cref="Source"/> 记录该图标取自 game-icons.net 的哪个作者/哪张图（如 "lorc/plain-dagger"），
/// 供 <c>tools/icons/build_icons.sh</c> 按图重新生成 PNG，也是 CC-BY 署名的溯源依据
/// （见 <c>godot/assets/items/CREDITS.md</c>）。<b>多个物品共用同一张 Source 是允许的</b>——
/// game-icons 里弓只有三种剪影而我们有八把弓弩，同族先共用，日后换成专属图只需替换那张 PNG，代码不动。
/// </para>
/// </summary>
/// <param name="Category">图标所属目录分区（weapons/armor/materials/lights/books/food/furniture）。</param>
/// <param name="Slug">图标文件名（ASCII 小写+下划线，无扩展名），在全表内唯一。</param>
/// <param name="Source">素材出处：game-icons.net 仓库内的 "作者/图名"（无 .svg 后缀）。</param>
public readonly record struct IconDef(string Category, string Slug, string Source);

/// <summary>
/// **物品 → 图标的单一事实源**（纯 C#，无 Godot 依赖 ⇒ 可单测）。
///
/// <para>
/// <b>零侵入</b>：不给 <c>Item</c>/<c>Weapon</c>/<c>MaterialDef</c> 加任何 <c>IconPath</c> 字段——
/// 引擎类库 <c>DeadSignal.Combat</c> 是零依赖的战斗规则库，"这把匕首长什么样"不是它该知道的事。
/// 图标是**消费层的表现问题**，故整张映射表收在 godot 消费层这一个文件里，按 <c>(类别, RefKey)</c> 查。
/// </para>
///
/// <para>
/// <b>查不到怎么办</b>：一律回退 <see cref="PlaceholderPath"/>（一张问号占位图），
/// 绝不抛异常、绝不留空白——新加的物品在补图标之前照样能在库存里列出来，只是显示占位。
/// 缺哪些可用 <see cref="MissingFor"/> 盘点（供批量补图时对账）。
/// </para>
/// </summary>
public static class ItemIcons
{
    /// <summary>图标资源根目录（Godot 资源路径）。</summary>
    public const string Root = "res://assets/items";

    /// <summary>占位图标路径：任何查不到映射、或 PNG 尚未生成的物品都显示它。</summary>
    public const string PlaceholderPath = Root + "/placeholder.png";

    // 目录分区名（= godot/assets/items 下的子目录名）。
    private const string Weapons = "weapons";
    private const string Armor = "armor";
    private const string Mats = "materials";
    private const string Lights = "lights";
    private const string Books = "books";
    private const string Food = "food";
    private const string Furniture = "furniture";
    private const string Tools = "tools";

    /// <summary>食物是"份数"而非具名物品（<see cref="Item.RefKey"/> 恒为 null），故单独给一个固定 id 走查表。</summary>
    public const string FoodRefKey = "__food__";

    // ———————————————————————————— 映射表 ————————————————————————————
    // 键 = 物品引用键（武器/护甲/狗装=中文名；材料/光源=英文 key；书=书 id；家具=配方产物 key）。
    // 值 = 该图标的分区/文件名/素材出处。**改这张表就是改素材**：跑 tools/icons/build_icons.sh 重新生成 PNG。
    private static readonly IReadOnlyDictionary<string, IconDef> _byRefKey = new Dictionary<string, IconDef>
    {
        // —— 武器：近战（WeaponTable.Arsenal + 可制作的骨刀）——
        ["匕首"] = new(Weapons, "dagger", "lorc/plain-dagger"),
        ["短剑"] = new(Weapons, "short_sword", "delapouite/ancient-sword"),
        ["刺剑"] = new(Weapons, "rapier", "lorc/relic-blade"),
        ["长剑"] = new(Weapons, "long_sword", "lorc/broadsword"),
        ["重剑"] = new(Weapons, "great_sword", "delapouite/two-handed-sword"),
        ["草叉"] = new(Weapons, "pitchfork", "delapouite/pitchfork"),
        ["棍棒"] = new(Weapons, "club", "delapouite/wood-club"),
        ["尖头锤"] = new(Weapons, "spiked_mace", "lorc/spiked-mace"),
        ["破甲锤"] = new(Weapons, "warhammer", "delapouite/warhammer"),
        // [批次25·T44] 消防斧。素材取 lorc/wood-axe（劈柴斧剪影）——**刻意不用 battle-axe**：
        // 那是把奇幻双刃战斧，本作的消防斧是从柴房和农具棚里捡来的工具，不是从武器架上取的。
        ["消防斧"] = new(Weapons, "axe", "lorc/wood-axe"),
        ["骨刀"] = new(Weapons, "bone_knife", "delapouite/bone-knife"),

        // —— 武器：枪械 ——
        ["自制猎枪"] = new(Weapons, "improvised_hunting_gun", "lorc/blunderbuss"),
        ["手枪"] = new(Weapons, "pistol", "skoll/colt-m1911"),
        ["冲锋枪"] = new(Weapons, "smg", "delapouite/mp5"),
        ["步枪"] = new(Weapons, "rifle", "skoll/musket"),
        // 「栓动猎枪」已按用户在数值表上的删除撤下（原图标 weapons/hunting_shotgun）。
        ["狙击枪"] = new(Weapons, "sniper_rifle", "skoll/winchester-rifle"),
        ["自制霰弹枪"] = new(Weapons, "improvised_shotgun", "delapouite/sawed-off-shotgun"),

        // —— 武器：弓与弩（game-icons 只有 3 种弓弩剪影，8 把同族共用；slug 各自独立，日后可逐把换专属图）——
        ["短弓"] = new(Weapons, "short_bow", "lorc/pocket-bow"),
        ["反曲弓"] = new(Weapons, "recurve_bow", "delapouite/bow-string"),
        ["长弓"] = new(Weapons, "longbow", "delapouite/bow-string"),
        ["竞技复合弓"] = new(Weapons, "compound_bow", "delapouite/bow-string"),
        ["狩猎弓"] = new(Weapons, "hunting_bow", "lorc/pocket-bow"),
        ["单手轻弩"] = new(Weapons, "light_crossbow", "carl-olsen/crossbow"),
        ["双手重弩"] = new(Weapons, "heavy_crossbow", "carl-olsen/crossbow"),
        ["复合弩"] = new(Weapons, "compound_crossbow", "carl-olsen/crossbow"),

        // —— 护甲：人类 16 件（ArmorTable）——
        ["长袖布衣"] = new(Armor, "long_sleeve_shirt", "lucasms/shirt"),
        ["花衬衫"] = new(Armor, "floral_shirt", "delapouite/polo-shirt"),
        ["长裤"] = new(Armor, "trousers", "irongamer/armored-pants"),
        ["短裤"] = new(Armor, "shorts", "delapouite/shorts"),
        ["运动鞋"] = new(Armor, "sneakers", "delapouite/running-shoe"),
        ["劳保手套"] = new(Armor, "work_gloves", "delapouite/gloves"),
        ["皮革胸甲"] = new(Armor, "leather_cuirass", "lorc/leather-vest"),
        ["粗布背心"] = new(Armor, "cloth_vest", "lorc/armor-vest"),
        ["粗布外套"] = new(Armor, "cloth_coat", "delapouite/lab-coat"),
        ["布夹克"] = new(Armor, "cloth_jacket", "delapouite/sleeveless-jacket"),
        ["牛仔外套"] = new(Armor, "denim_jacket", "delapouite/moncler-jacket"),
        ["皮夹克"] = new(Armor, "leather_jacket", "delapouite/leather-armor"),
        ["皮甲"] = new(Armor, "leather_armor", "delapouite/chest-armor"),
        ["板甲"] = new(Armor, "plate_armor", "lorc/breastplate"),
        ["军用头盔"] = new(Armor, "military_helmet", "delapouite/custodian-helmet"),
        ["防暴头盔"] = new(Armor, "riot_helmet", "delapouite/full-motorcycle-helmet"),

        // [批次21·T26] 三件可制作穿戴品 + 战争面具。粗布三件与它们各自的搜刮版（长袖布衣/长裤/短裤）
        // **共用同一张 Source**（game-icons 里衬衫/裤子就那么几张剪影，同族先共用；slug 各自独立，日后可换专属图）。
        ["战争面具"] = new(Armor, "war_mask", "lorc/tribal-mask"),
        // [T59] 棉帽（用户在 wiki 上新加）——头槽的布甲。
        ["棉帽"] = new(Armor, "cotton_hat", "delapouite/winter-hat"),
        ["粗布衬衫"] = new(Armor, "coarse_shirt", "lucasms/shirt"),
        ["粗布短裤"] = new(Armor, "coarse_shorts", "delapouite/shorts"),
        ["粗布长裤"] = new(Armor, "coarse_trousers", "irongamer/armored-pants"),

        // [T68] 用户在 wiki 上新加的三件。
        ["恐怖装甲"] = new(Armor, "horror_armor", "lorc/bone-knife"),   // 骨片缝在皮衬上的胸甲——取骨系剪影
        ["墨镜"] = new(Armor, "sunglasses", "delapouite/sunglasses"),
        ["平光眼镜"] = new(Armor, "plain_glasses", "delapouite/spectacles"),
        // [T71] 自制简易墨镜（木缝雪镜）——护目镜剪影
        ["自制简易墨镜"] = new(Armor, "snow_goggles", "delapouite/goggles"),
        // [A2/T72] 护踝鞋具（高帮硬底，护脚踝到小腿）——护胫/护腿剪影
        ["护踝鞋具"] = new(Armor, "ankle_guard", "delapouite/leg-armor"),
        ["厚重裤子"] = new(Armor, "heavy_trousers", "irongamer/armored-pants"),
        ["厚重披风"] = new(Armor, "heavy_cape", "lorc/hooded-cloak"),
        ["雪地靴"] = new(Armor, "snow_boots", "delapouite/winter-boot"),

        // —— 护甲：布鲁斯的狗装五件套（DogGearCatalog；口袋狗衣不提供防护，只加负重）——
        ["布制狗衣"] = new(Armor, "dog_cloth_vest", "lorc/armor-vest"),
        ["皮制狗衣"] = new(Armor, "dog_leather_vest", "lorc/leather-vest"),
        ["口袋狗衣"] = new(Armor, "dog_pocket_vest", "delapouite/shoulder-bag"),
        ["铁皮头甲"] = new(Armor, "dog_iron_helmet", "lorc/visored-helm"),
        ["铁丝头甲"] = new(Armor, "dog_wire_helmet", "lorc/spiked-collar"),

        // —— 材料：基础造物料 ——
        ["wood"] = new(Mats, "wood", "delapouite/planks"),
        ["scrap_wood"] = new(Mats, "scrap_wood", "delapouite/wood-stick"),
        ["cloth"] = new(Mats, "cloth", "delapouite/rolled-cloth"),
        // [T46] 铁：废金属 + 金属锭合并。沿用原废金属的 metal-plate 图（一叠锈铁皮），
        // 金属锭那张 melting-metal（熔炉里的锭）随键一起退役——世界里已经没有"熔炼提纯"这一层了。
        [Materials.IronKey] = new(Mats, "iron", "delapouite/metal-plate"),
        ["nails"] = new(Mats, "nails", "delapouite/coiled-nail"),
        ["wire"] = new(Mats, "wire", "delapouite/wire-coil"),
        ["rawhide"] = new(Mats, "rawhide", "delapouite/animal-hide"),
        ["leather"] = new(Mats, "leather", "delapouite/leather-armor"),
        ["bone"] = new(Mats, "bone", "lorc/crossed-bones"),
        ["stone"] = new(Mats, "stone", "delapouite/stone-pile"),
        ["rope"] = new(Mats, "rope", "delapouite/jumping-rope"),
        ["components"] = new(Mats, "components", "lorc/cog"),

        // —— 材料：化学 ——
        ["gunpowder"] = new(Mats, "gunpowder", "delapouite/powder-bag"),
        ["tanning_solution"] = new(Mats, "tanning_solution", "lorc/bubbling-flask"),
        ["fuel"] = new(Mats, "fuel", "delapouite/jerrycan"),
        ["glue"] = new(Mats, "glue", "delapouite/honey-jar"),

        // —— 材料：医疗（手术耗材 + 药品 + 草药）——
        ["bandage"] = new(Mats, "bandage", "lorc/bandage-roll"),
        ["herbal_bandage"] = new(Mats, "herbal_bandage", "lorc/bandage-roll"),
        ["needle_thread"] = new(Mats, "needle_thread", "lorc/sewing-needle"),
        ["splint"] = new(Mats, "splint", "delapouite/foot-plaster"),
        ["first_aid_kit"] = new(Mats, "first_aid_kit", "delapouite/first-aid-kit"),
        ["antibiotics"] = new(Mats, "antibiotics", "delapouite/medicine-pills"),
        ["medicine"] = new(Mats, "medicine", "delapouite/medicines"),
        ["dandelion"] = new(Mats, "dandelion", "delapouite/dandelion-flower"),
        ["rosehip"] = new(Mats, "rosehip", "delapouite/raspberry"),
        ["kudzu_root"] = new(Mats, "kudzu_root", "delapouite/roots"),
        ["rhubarb"] = new(Mats, "rhubarb", "delapouite/herbs-bundle"),
        ["laojunxu"] = new(Mats, "laojunxu", "delapouite/herbs-bundle"),
        ["herbal_salve"] = new(Mats, "herbal_salve", "delapouite/covered-jar"),
        ["dandelion_tea"] = new(Mats, "dandelion_tea", "delapouite/coffee-cup"),
        ["rosehip_tea"] = new(Mats, "rosehip_tea", "lorc/coffee-mug"),

        // —— 材料：货币 ——
        ["silver"] = new(Mats, "silver", "delapouite/coins-pile"),

        // —— 材料：弹药与其原料 ——
        ["bullet_parts"] = new(Mats, "bullet_parts", "delapouite/machine-gun-magazine"),
        ["weapon_parts"] = new(Mats, "weapon_parts", "lorc/gear-hammer"),   // [批次21·T26] 武器零件（弩机/扳机组，只喂弩）
        ["damaged_sniper_rifle"] = new(Mats, "damaged_sniper_rifle", "skoll/winchester-rifle"), // 损坏的狙击枪（修复原料，图标暂用完好版）
        ["ammo_short"] = new(Mats, "ammo_short", "lorc/bullets"),
        ["ammo_medium"] = new(Mats, "ammo_medium", "delapouite/heavy-bullets"),
        ["ammo_long"] = new(Mats, "ammo_long", "lorc/supersonic-bullet"),
        ["ammo_buck"] = new(Mats, "ammo_buck", "delapouite/shotgun-rounds"),
        ["ammo_arrow_stick"] = new(Mats, "ammo_arrow_stick", "delapouite/plain-arrow"),
        ["ammo_arrow_handmade"] = new(Mats, "ammo_arrow_handmade", "lorc/arrow-cluster"),
        ["ammo_arrow_heavy"] = new(Mats, "ammo_arrow_heavy", "lorc/barbed-arrow"),
        ["ammo_arrow_carbon"] = new(Mats, "ammo_arrow_carbon", "delapouite/quiver"),

        // —— 光源（LightSource 目录：两件手持 + 两件固定）——
        ["flashlight"] = new(Lights, "flashlight", "delapouite/flashlight"),
        ["torch"] = new(Lights, "torch", "delapouite/torch"),
        ["lamp"] = new(Lights, "lamp", "delapouite/old-lantern"),
        ["campfire"] = new(Lights, "campfire", "lorc/campfire"),

        // —— 书（BookData）——
        ["wilderness_survival_guide"] = new(Books, "book_wilderness", "delapouite/book-cover"),
        ["farmer_hundred_questions"] = new(Books, "book_farming", "lorc/open-book"),
        ["tailors_notes"] = new(Books, "book_tailoring", "delapouite/notebook"),
        ["folk_chemistry_notes"] = new(Books, "book_chemistry", "delapouite/secret-book"),
        ["carpentry_basics"] = new(Books, "book_carpentry", "willdabeast/white-book"),
        ["advanced_carpentry"] = new(Books, "book_carpentry_adv", "willdabeast/black-book"),
        ["way_of_bow_and_arrow"] = new(Books, "book_archery", "delapouite/rule-book"),
        ["mechanical_beauty"] = new(Books, "book_mechanics", "lorc/gears"),   // [批次21·T26] 《机械之美》（弩的解锁书）
        ["bow_crafting_guide"] = new(Books, "book_bowcraft", "lorc/high-shot"),   // [T59] 《弓制作指南》（反曲弓/长弓的解锁书）
        ["peak_hour"] = new(Books, "book_peak_hour", "lorc/mountains"),   // [T71] 《尖峰时刻》（滑雪极限运动书，解锁自制简易墨镜）——群山剪影
        ["peak_hour_2"] = new(Books, "book_peak_hour_2", "lorc/mountains"),
        ["peak_hour_3"] = new(Books, "book_peak_hour_3", "lorc/mountains"),
        // [wiki-character-sync] 《枪械维修指南》：神秘商人的互斥书籍货品。
        ["gunsmith_repair_guide"] = new(Books, "book_gunsmith_repair", "lorc/gears"),
        ["goldfinger_diary_a"] = new(Books, "book_diary_a", "delapouite/book-pile"),
        ["goldfinger_diary_b"] = new(Books, "book_diary_b", "lorc/papers"),

        // —— 食物（无具名物品，整体一份口粮一个图标）——
        [FoodRefKey] = new(Food, "ration", "delapouite/canned-fish"),

        // —— 家具/工事（配方产物，落地为材料堆）——
        ["bench"] = new(Furniture, "bench", "delapouite/park-bench"),
        ["chair"] = new(Furniture, "chair", "delapouite/wooden-chair"),
        ["sofa"] = new(Furniture, "sofa", "delapouite/sofa"),
        ["sandbag"] = new(Furniture, "sandbag", "delapouite/concrete-bag"),
        ["bed"] = new(Furniture, "bed", "delapouite/bed"),
        ["table"] = new(Furniture, "table", "delapouite/table"),            // [批次21·T25] 桌子
        ["mod_bench"] = new(Furniture, "mod_bench", "lorc/anvil"),
        ["cook_station"] = new(Furniture, "cook_station", "delapouite/gas-stove"),
        ["cooking_pot"] = new(Furniture, "cooking_pot", "delapouite/cooking-pot"),
        ["cooking_grill"] = new(Furniture, "cooking_grill", "delapouite/barbecue"),
        ["snare_trap"] = new(Furniture, "snare_trap", "lorc/wolf-trap"),   // [批次21·T26] 圈套陷阱
        // —— [T67] 采集/种植/诱捕支柱的三件设施 ——
        ["bird_trap"] = new(Furniture, "bird_trap", "delapouite/bird-cage"),        // 捕鸟陷阱（→ 鸟 → 宰杀 → 羽毛 → 箭）
        ["crop_plot"] = new(Furniture, "crop_plot", "delapouite/plant-seed"),       // 菜园（种土豆）
        ["butcher_point"] = new(Furniture, "butcher_point", "delapouite/meat-hook"),// 简易宰杀点
        ["butcher_table"] = new(Furniture, "butcher_table", "delapouite/meat-cleaver"), // 宰杀台（升级）

        // —— 食材（MaterialCategory.Food，烹饪系统的原料；与那张泛化的「口粮份数」图标 FoodRefKey 不是一回事）——
        // 注意：材料键 "ration"（军用单兵口粮）与泛化口粮图标的 slug "ration" 撞名，故它的 slug 取 ration_military。
        ["rat"] = new(Food, "rat", "lorc/mouse"),
        ["pigeon"] = new(Food, "pigeon", "lorc/dove"),
        ["rabbit"] = new(Food, "rabbit", "delapouite/rabbit"),
        ["fish"] = new(Food, "fish", "delapouite/double-fish"),
        ["ration"] = new(Food, "ration_military", "delapouite/meal"),
        ["canned_food"] = new(Food, "canned_food", "delapouite/opened-food-can"),
        ["flour"] = new(Food, "flour", "delapouite/flour"),
        ["potato"] = new(Food, "potato", "delapouite/potato"),
        ["mushroom"] = new(Food, "mushroom", "delapouite/mushrooms"),

        // —— [T67] 宰杀链的四样新材料（**追加末尾不插队**）——
        // 「鸟」（键仍是 pigeon）图标不变：它还是那只鸽子，只是换了个名字。
        ["rat_meat"] = new(Food, "rat_meat", "delapouite/meat"),                 // 老鼠肉
        ["bird_meat"] = new(Food, "bird_meat", "delapouite/chicken-leg"),        // 鸟肉
        ["rabbit_meat"] = new(Food, "rabbit_meat", "delapouite/meat"),           // 兔子肉
        ["feather"] = new(Mats, "feather", "lorc/feather"),                      // 羽毛（三种箭的共同料）
        ["leather_scrap"] = new(Mats, "leather_scrap", "delapouite/rolled-cloth"), // 碎皮革（缝合成生皮）

        // —— 工作台工具（搜刮而来，落地时装进工作台的三个 ToolSlot；键对齐 camp.json 的 loot id）——
        ["calipers"] = new(Tools, "calipers", "delapouite/pencil-ruler"),
        ["sawblade"] = new(Tools, "sawblade", "lorc/circular-sawblade"),
        ["beaker"] = new(Tools, "beaker", "lorc/round-bottom-flask"),
    };

    /// <summary>全表映射（引用键 → 图标定义），按声明顺序。批量生成 PNG 的脚本与 CREDITS 都以它为准。</summary>
    public static IReadOnlyDictionary<string, IconDef> All => _byRefKey;

    /// <summary>按引用键查图标定义；查不到返回 <c>null</c>。</summary>
    public static IconDef? Find(string? refKey)
        => refKey != null && _byRefKey.TryGetValue(refKey, out IconDef def) ? def : null;

    /// <summary>
    /// 按引用键取 Godot 资源路径（如 <c>res://assets/items/weapons/dagger.png</c>）。
    /// 查不到映射一律回退 <see cref="PlaceholderPath"/>——绝不抛异常。
    /// </summary>
    public static string PathFor(string? refKey)
    {
        IconDef? def = Find(refKey);
        return def is null ? PlaceholderPath : $"{Root}/{def.Value.Category}/{def.Value.Slug}.png";
    }

    /// <summary>
    /// 按一件库存物品取图标路径。食物没有引用键（<see cref="Item.RefKey"/> 为 null），
    /// 走 <see cref="FoodRefKey"/> 取那张统一的口粮图标。
    /// </summary>
    public static string PathFor(Item item)
    {
        if (item is null)
        {
            return PlaceholderPath;
        }
        return item.Category == ItemCategory.Food ? PathFor(FoodRefKey) : PathFor(item.RefKey);
    }

    /// <summary>
    /// 按**配方产物**取图标路径。配方的产物键与库存物品的引用键**不总是同一个东西**：
    /// 材料/光源/家具/狗装的产物键就是引用键（<c>glue</c>/<c>torch</c>/<c>bench</c>/<c>布制狗衣</c>），
    /// 但武器/护甲落地时走 <c>Item.Weapon(配方中文名)</c>，引用键是**中文名**而产物键是内部英文键
    /// （<c>bone_knife</c> → 「骨刀」，<c>handmade_bow</c> → 「短弓」）。
    /// 所以这里先按产物键查，查不到再按显示名查，都查不到才回退占位图。
    /// </summary>
    public static string PathForOutput(string? outputKey, string? displayName)
    {
        IconDef? def = Find(outputKey) ?? Find(displayName);
        return def is null ? PlaceholderPath : $"{Root}/{def.Value.Category}/{def.Value.Slug}.png";
    }

    /// <summary>
    /// 对账用：给定一批物品引用键，返回其中**还没有图标映射**的那些（供批量补图时盘点，也供未来升级成硬门禁）。
    /// </summary>
    public static IReadOnlyList<string> MissingFor(IEnumerable<string> refKeys)
        => refKeys is null
            ? Array.Empty<string>()
            : refKeys.Where(k => k != null && !_byRefKey.ContainsKey(k)).Distinct().ToList();
}
