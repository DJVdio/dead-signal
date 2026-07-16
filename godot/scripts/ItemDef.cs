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
    /// 🔴 <b>数值真源已外置 <c>godot/data/config/materials.json</c></b>（原 47 条字面表）——本字段启动时从
    /// <see cref="GameConfigCatalog"/> 的 <see cref="MaterialConfig"/> 段<b>拷贝</b>成一份独立
    /// <see cref="Dictionary{TKey,TValue}"/>：字段类型/引用语义保持不变（<c>CarryWeight._materialKg</c> 仍以别名引
    /// 同一实例、<see cref="All"/> 遍历它、<see cref="Has"/>/称重回落逻辑一字未动）。
    /// 定值原则（大件压秤·零碎白送·白银不占重）与 authored 决策（铁 1.5 非 2.0·T68 减重）见 materials.json 头注。
    /// </para>
    /// </summary>
    public static readonly Dictionary<string, double> Materials =
        new(GameConfigCatalog.Section<MaterialConfig>().Weights);

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
