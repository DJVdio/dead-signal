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
// 上限算式见 Loadout.Capacity：基础 20kg（全员统一，**无"力量"属性**）× 承载能力（残缺×饥饿）× authored 专属乘子（山姆）。

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

    /// <summary>弹药按发计重（各口径统一，拟定待调）。</summary>
    public const double AmmoPerRoundKg = 0.03;

    /// <summary>材料重量（按 <see cref="MaterialDef.Key"/>；未登记走 <see cref="DefaultMaterialKg"/>）。</summary>
    private static readonly Dictionary<string, double> _materialKg = new()
    {
        // —— 笨重物资：负重的大头，搬运本身就是代价 ——
        ["stone"] = 3.0,
        ["fuel"] = 3.0,
        ["wood"] = 2.0,
        ["metal_ingot"] = 2.0,
        ["scrap_metal"] = 1.5,
        // [批次20·拆除回收] 废木料：碎料，比整根木料（2.0）轻——但拆一段围栏掉 4 堆，扛回去也是 4kg。
        ["scrap_wood"] = 1.0,
        ["tanning_solution"] = 1.0,

        // —— 中等 ——
        ["rawhide"] = 0.8,
        ["leather"] = 0.5,
        ["rope"] = 0.5,
        ["components"] = 0.5,
        // [批次21·T26] 武器零件：弩机与扳机组是**淬过火的钢件**，比通用机括件（components 0.5）沉一档。
        // 造一把重弩要 3 个 ⇒ 1.8kg，光零件就顶得上一件皮夹克。数值拟定待调。
        [Materials.WeaponPartsKey] = 0.6,
        ["first_aid_kit"] = 0.5,
        // [批次20·拆除回收] 胶水：一罐骨胶。它不重——重的是它稀缺。
        ["glue"] = 0.5,

        // —— 零碎：几乎白送 ——
        ["cloth"] = 0.3,
        ["bone"] = 0.3,
        ["wire"] = 0.3,
        ["gunpowder"] = 0.3,
        ["splint"] = 0.3,
        ["nails"] = 0.2,
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
        ["rat"] = 0.3,           // 老鼠：轻，也确实不顶饱
        ["pigeon"] = 0.3,        // 鸽子：肉少骨头多
        ["potato"] = 0.3,        // 土豆
        ["mushroom"] = 0.05,     // 蘑菇：几乎白送（玫瑰果/蒲公英同档，见上方医疗原料那几行——它们同时也是食材）

        // 货币：带钱出门不该挤占背包
        ["silver"] = 0.01,
    };

    /// <summary>武器重量（按 <c>WeaponTable</c> 的武器名；未登记走 <see cref="DefaultWeaponKg"/>）。</summary>
    private static readonly Dictionary<string, double> _weaponKg = new()
    {
        // 近战
        ["匕首"] = 0.5,
        ["短剑"] = 1.2,
        ["刺剑"] = 1.3,
        ["长剑"] = 1.8,
        ["重剑"] = 3.0,
        ["草叉"] = 2.5,
        ["棍棒"] = 1.5,
        ["尖头锤"] = 2.5,
        ["破甲锤"] = 4.0,

        // 枪械
        ["手枪"] = 1.0,
        ["冲锋枪"] = 3.0,
        ["自制猎枪"] = 3.0,
        ["自制霰弹枪"] = 3.2,
        ["栓动猎枪"] = 3.5,
        ["步枪"] = 4.0,
        ["狙击枪"] = 6.0,

        // 弓弩
        ["短弓"] = 0.8,
        ["狩猎弓"] = 1.0,
        ["反曲弓"] = 1.2,
        ["竞技复合弓"] = 1.8,
        ["长弓"] = 1.5,
        ["单手轻弩"] = 2.0,
        ["复合弩"] = 3.0,
        ["双手重弩"] = 4.5,
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

    /// <summary>单件武器重量（未登记走兜底）。</summary>
    /// <summary>
    /// 单件武器重量（按武器名；未登记走 <see cref="DefaultWeaponKg"/>）。
    /// <para>
    /// **改装变体**（"步枪（刺刀型）"）不在本表里 —— 回落到它的**基础武器**重量
    /// （<see cref="ModdedWeaponRegistry.BaseNameOf"/>）。不这么做的话，一把改装狙击枪会按"未登记武器"
    /// 算成 2kg（比手枪还轻），负重系统当场失真。改装件本身的增重暂不建模（拟定待调）。
    /// </para>
    /// </summary>
    public static double WeaponKg(string? name)
    {
        if (name is null) return DefaultWeaponKg;
        if (_weaponKg.TryGetValue(name, out double kg)) return kg;

        if (ModdedWeaponRegistry.BaseNameOf(name) is { } baseName
            && _weaponKg.TryGetValue(baseName, out double baseKg))
        {
            return baseKg;
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
/// 远征背包：探索队这一趟能带回来的东西（**硬上限**）。
/// <para>
/// 为什么是硬上限而不是"超重减速"：软惩罚下"多背一点只是慢一点"，玩家总会选择全拿走——**没有取舍**。
/// 硬上限才逼出《这是我的战争》式的当场决定："这桶燃料和这把步枪，只能带一样。"
/// 软惩罚（<see cref="Loadout.SpeedMultiplier"/>）依然在上限**之内**生效：背得越满走得越慢，所以"背满"本身也有代价。
/// </para>
/// 容量由 <see cref="PartyCapacity"/> 汇总（队里每个人的 <see cref="CarryCapacity"/> + 布鲁斯口袋狗衣的 6kg）。
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

    /// <summary>已背在身上的重量（kg）。</summary>
    public double CarriedKg => ItemWeights.TotalOfLoot(_contents);

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

    /// <summary>当前负重给全队的移速乘子（30kg 内无罚；30~50 轻度；50~80 加重）。</summary>
    public double SpeedMultiplier => Loadout.SpeedMultiplier(CarriedKg, CapacityKg);

    /// <summary>当前负重给全队的出手间隔乘子（只有重度档才罚）。</summary>
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
    /// 狗的负重就此**统一进同一套账**——口袋狗衣不再是独立口径，就是给队伍背包 +6kg。
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
    /// 某人的负重上限（kg）。基准人 80kg；三档（30/50/80）随此上限按比例伸缩。
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

    /// <summary>负重对应的移速乘子（30kg 内无罚；30~50 轻度；50~80 加重）。</summary>
    public static double SpeedMultiplier(double carriedKg, double limitKg)
        => Loadout.SpeedMultiplier(carriedKg, limitKg);

    /// <summary>负重对应的出手间隔乘子（只有重度档才罚攻速）。</summary>
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
}
