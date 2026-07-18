using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Workbench.cs / CraftingLogic.cs / PlacementRules.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（烹饪台落地/挖导航洞/重烘焙/面板）归 Godot 消费层（CampMain / CookingPanel），本文件只出**判定与账**。
//
// ══════════ 烹饪：把生料变成饭 ══════════
// 用户 authored 说明（当前配方、热量、炊具减免与工时以 Wiki 配置为准）：
//   每种食物原材料都有对应热量；达到一份食物的需求后，烹饪台消耗工时制作食物。
//     烹饪台有两个槽位，安装锅或烤架会降低每份食物的热量需求。
//     玩家只能一味地放食材，当热量点不足一份食物时，不让制作。当热量点在一份到两份间时，只能制作一份，
//     并且不会给玩家提示。当热量点超过两份时，UI 上显示产物食物*2，能够制作两份。
//     总之，每种食材的热量点和制作一份食物需要的热量是玩家要自己探索试错的。」
//
// ⚠️⚠️ **信息隐藏是这个机制的全部乐趣所在，不是待修的 UX 缺陷** ⚠️⚠️
// 玩家在游戏里**永远看不到**：某种食材值几点、一份饭要几点、还差几点、零头浪费了几点。
// 他唯一能观察到的信号是**产物那一行的「食物 ×N」**（和"按钮点不点得动"）。
// 谁将来觉得"这里该给个进度条/该提示还差多少"——**那正是本机制要拒绝的东西**，动手前先回来读这段。
// （对照：本仓有「首次触发一行提示」框架 hint-system，**烹饪刻意不接它**。）
//
// 本文件的当前数值全部以 Wiki 配置为准；此处只保留规则形状与信息隐藏约束。

/// <summary>
/// 烹饪台的两个槽位（用户拍板：「烹饪台有两个槽位」）。装上就省料——每装一件，
/// **每份饭的热量需求少一点**（锅与烤架**各有各的减免值**，见 <see cref="CookingLogic.DiscountOf"/>）。
/// <para>
/// 结构照 <see cref="ToolSlot"/>/<see cref="WorkbenchState"/> 写（"一座设施 + 一组互不相同的槽"是同一个问题），
/// 故"两个槽"由**枚举只有两个值**天然保证：装不了两口锅，也不会冒出第三个槽。
/// </para>
/// </summary>
public enum CookwareSlot
{
    /// <summary>锅：能煮、能炖，汤汁不外溢。</summary>
    Pot,

    /// <summary>烤架：能烤、能熏，油脂不白流。</summary>
    Grill,
}

/// <summary>
/// 单座烹饪台的炊具装配态（纯逻辑，无 Godot 依赖）——<see cref="WorkbenchState"/> 的同构物。
/// 装/卸幂等；<see cref="Installed"/> 喂给 <see cref="CookingLogic.PortionCost"/> 算每份饭要多少热量。
/// </summary>
public sealed class CookStationState
{
    private readonly HashSet<CookwareSlot> _installed = new();

    /// <summary>装上一件炊具（幂等；返回本次是否发生装入——已装过则 false，调用方据此决定要不要扣库存）。</summary>
    public bool Install(CookwareSlot slot) => _installed.Add(slot);

    /// <summary>卸下一件炊具（幂等；返回本次是否确实卸下——据此决定要不要把它还回库存）。</summary>
    public bool Remove(CookwareSlot slot) => _installed.Remove(slot);

    /// <summary>某个槽当前装了炊具没有。</summary>
    public bool Has(CookwareSlot slot) => _installed.Contains(slot);

    /// <summary>已装炊具的只读快照（喂 <see cref="CookingLogic.PortionCost"/>）。</summary>
    public IReadOnlySet<CookwareSlot> Installed => _installed.ToHashSet();
}

/// <summary>
/// <b>食材热量点表</b>（单一事实源；每条带**稳定 id** = 材料键 <see cref="MaterialDef.Key"/>）。
///
/// <para>═══ <b>为什么热量点是外挂表，而不是 MaterialDef 上的一个字段</b> ═══
/// 因为**一样东西可以既是材料又是食材**：玫瑰果（<c>rosehip</c>）是草药膏/玫瑰果茶的配料
/// （<see cref="MaterialCategory.Medical"/>，归医疗那条线），同时也是一条热量食材——
/// 它下得了锅，也进得了药。把热量点做成 <see cref="MaterialDef"/> 的字段就得给全表 40 多种材料
/// 都挂一个"热量"（木料的热量？铁丝的热量？），而"是不是食材"这件事**本来就是跨类别的**。
/// ⇒ 「是不是食材」的定义就是<b>「在不在这张表里」</b>（<see cref="Has"/>）。</para>
///
/// <para>⚠️ <b>这张表的数值给用户/wiki 看，绝不给游戏内 UI 看</b>——见本文件顶部那段。
/// wiki 是设计表（开发者调数值的地方），游戏内 UI 是玩家的试错场，两者不矛盾。</para>
/// </summary>
public static class FoodCalories
{
    /// <summary>一条食材的热量点定义（不可变）。<paramref name="Key"/> = 材料键（稳定 id，抽 wiki 用它）。</summary>
    /// <param name="Key">材料键（对齐 <see cref="Materials"/> 目录，稳定 id）。</param>
    /// <param name="Calories">热量点（这一个单位的食材能贡献多少热量）。</param>
    public readonly record struct FoodDef(string Key, int Calories);

    // 当前热量值与每份需求以 Wiki 配置为准；玩家只能通过产物数量和按钮状态探索。
    // **声明顺序刻意不按热量排序**——面板按显示名排，绝不按热量排（按热量排 = 把答案免费告诉玩家）。
    private static readonly IReadOnlyList<FoodDef> _all = new[]
    {
        // ——— 荤 ———
        // 🔴 [T67] **老鼠（rat）与鸟（pigeon）已从本表移除** —— 用户原话「**老鼠和鸟不能直接入锅了，而是要先宰杀**」。
        //    它们**没有从游戏里消失**（仍是 Materials 目录项、仍有重量图标、陷阱照旧抓、掉落表照旧掉），
        //    删的是"能直接下锅"这一条 ⇒ 现在灶上点不到它们，得先过一遍案板（<see cref="ButcheryLogic"/>）。
        //    走法与 [T59] 蒲公英**完全同源**（那次是"从食物表删、材料保留"），判据也是同一句：
        //    **「是不是食材」问的是 FoodCalories.Has，不是 MaterialCategory。**
        //    ⚠️ 热量点**一点没蒸发**：宰杀只改变材料形态，肉的热量配置沿用设计表。
        //    ⚠️ 兔子现在也进入宰杀链：宰杀台把 rabbit 变成 rabbit_meat，热量值以 Wiki 配置为准。
        new FoodDef("rabbit_meat", 11),      // 兔子肉（宰杀台产物，热量值以 Wiki 配置为准）
        new FoodDef("fish", 8),              // 鱼

        // ——— 存粮 / 罐头 ———
        new FoodDef("ration", 30),           // 军用单兵口粮（热量值以 Wiki 配置为准）
        new FoodDef("canned_food", 16),      // 罐头（热量值以 Wiki 配置为准）
        new FoodDef("flour", 10),            // 面粉

        // ——— 野菜 / 采集（与医疗原料共用同一批材料键，见类注）———
        new FoodDef("potato", 4),            // 土豆（[T67] 菜畦种出来的也是它——同一个键，不新造"自种土豆"）
        new FoodDef("mushroom", 3),          // 蘑菇
        new FoodDef("kudzu_root", 6),        // 葛根（《尖峰时刻·三》识别后可采）
        new FoodDef("rhubarb", 3),           // 大黄（《尖峰时刻·三》识别后可采）

        // ——— [T67] 宰杀出来的肉（**追加末尾不插队**）———
        // 热量点是从被宰的那只动物身上**原样继承**的，宰杀不创造也不毁灭热量（它只是把肉从骨头和皮上取下来）。
        new FoodDef("rat_meat", 6),          // 老鼠肉 ← 继承老鼠的热量配置
        new FoodDef("bird_meat", 5),         // 鸟肉   ← 继承鸟（原「鸽子」）的热量配置
        new FoodDef("rosehip", 2),           // 玫瑰果（同时是医疗原料；本表不碰它的类别）

        // 🔴 [T59] **蒲公英已从本表移除**（用户在 wiki 的「食物与食材」页把它删了）⇒ 它**下不了锅**了。
        //    它**没有**从游戏里消失：仍是 MaterialCategory.Medical 的药材，仍是蒲公英茶（感染三档药之一）
        //    与草药膏的配料，探索点也照旧掉它。用户删的是"食物"那一栏——
        //    正确的读法是「**它不该能当饭吃**」，不是「它不该存在」。
        //    （抽取器建议的是"从 FoodCalories + Materials 一并删掉"，照做会当场炸掉整条感染药链。）
        //    护栏见 WikiSyncT59Tests.蒲公英_仍是药材且仍在感染药链里。
    };

    private static readonly IReadOnlyDictionary<string, int> _byKey = _all.ToDictionary(f => f.Key, f => f.Calories);

    /// <summary>全部食材（按声明顺序；**不是热量顺序**）。</summary>
    public static IReadOnlyList<FoodDef> All => _all;

    /// <summary>这个材料键是不是食材（＝下不下得了锅）。</summary>
    public static bool Has(string? key) => key is not null && _byKey.ContainsKey(key);

    /// <summary>某食材一个单位值几点热量；不是食材 ⇒ 0（不抛，调用方按"不是食材"处理）。</summary>
    public static int Of(string? key)
        => key is not null && _byKey.TryGetValue(key, out int c) ? c : 0;
}

/// <summary>
/// <b>烹饪台</b>（新营地设施）：在**工作台**上造出来，饭只能在它上面做。
///
/// <para>═══ ⚠️ <b>它是「固定位置」设施，玩家摆不了</b>（用户拍板，别改回可摆放）═══
/// 用户原话：「改装台、烹饪台**不允许跨越**，但是他们是营地内**固定位置**。改装台放在**车间**，
/// **烹饪台放在厨房**。」⇒ 配方在工作台上做完之后，这座灶**自动砌在厨房的固定锚点上**
/// （<see cref="AnchorX"/>／<see cref="AnchorY"/>），**不进库存**——一座砌起来的灶不是能揣兜里的东西。
/// 玩家<b>没有"放置"这个动作</b>，故也就不存在"放置时校验贴不贴围栏"那回事。
/// （与改装台同构，见 <c>WeaponModLogic.BenchAnchorX</c>；与沙袋恰恰相反——沙袋是**战术物件**，可自由摆。）</para>
///
/// <para>═══ <b>正因为固定，锚点反而更要自检</b> ═══
/// 它<b>实心、挖导航洞、不可跨越</b>，而玩家**摆不了也挪不动**——锚点若压进防线禁建带，
/// 那就是一条玩家**永远无法纠正**的死路（围栏从此修不了）。故 <see cref="Spec"/> 保留，
/// 其唯一用途是**设计期自检**（`FixedFacilityAnchorTests` 拿真 camp.json 几何过 <see cref="PlacementRules.CanPlace"/>），
/// 而**不是**运行时放置校验。</para>
/// </summary>
public static class CookStation
{
    /// <summary>烹饪台配方 id（在**工作台**上做）。完工不进库存 —— 直接砌在厨房锚点上。</summary>
    public const string RecipeId = "cook_station";

    /// <summary>
    /// 配方产物键。⚠️ <b>它不是一件"库存物品"</b>——完工由营地层分流成"在厨房锚点砌一座灶"
    /// （见 CampMain 的 <c>CompleteCookStationBuild</c>）。这个键只用来给制作面板那一行认图标/名字。
    /// </summary>
    public const string ItemKey = "cook_station";

    /// <summary>场上那座烹饪台的家具名（= <see cref="FurnitureBuildCost"/> 的键 = 可点击容器名，两处必须同名）。</summary>
    public const string PropName = "烹饪台";

    /// <summary>「营地里还没有烹饪台」的配方门槛键（一座就够；已有一座时配方灰掉，判定委托营地层）。</summary>
    public const string AbsentGate = "cook_station_absent";

    /// <summary>物品描述（黑色幽默文风，同 <see cref="SandbagSpec.ItemDescription"/>）。</summary>
    public const string ItemDescription =
        "一座砌起来的灶台，熏得漆黑。往里扔什么它都不会评价——它只负责把生的变成熟的，把熟的变成一顿饭。";

    // ———————————————————————— 厨房锚点（固定位置）————————————————————————
    //
    // 「厨房」= **住宅**（camp.json `buildings` 里 `[400,430,460,360]`，即 x∈[400,860] / y∈[430,790]）的西侧一角。
    // camp.json 里没有单独的"厨房"房间（只有 住宅/仓库/空牛棚 三座建筑）——同"空牛棚改造成车间"的既有处置，
    // **厨房＝住宅里的那个角**，不新建建筑（用户拍板口径的轻量落地，与改装台一致）。
    //
    // 选点（实测几何，改锚点前请把这几条重算一遍）：
    //   · 落在住宅内，西墙一侧、「住宅-柜子」(下沿 y=514) 与「住宅-展示柜」(上沿 y=676) **之间**的空当；
    //   · 与柜子留 46px、与展示柜留 64px、与「住宅-座椅A」(x 起 600) 留 76px ⇒ 不压任何既有 prop；
    //   · 距最近围栏内沿 **≥118px**（围栏外沿 x=300、厚 22 ⇒ 内沿 322；灶西沿 440）≫ 禁建带 64px ⇒ 远在安全区；
    //   · 两座大门（北 [1100,300]、南 [1100,1478]）都在几百像素之外。
    // 以上由 `FixedFacilityAnchorTests` 拿**真 camp.json** 逐条钉死——改了坐标它会立刻红。

    /// <summary>厨房锚点 X（世界像素，矩形左上角；住宅西侧）。</summary>
    public const float AnchorX = 440f;

    /// <summary>厨房锚点 Y（世界像素，矩形左上角；柜子与展示柜之间的空当）。</summary>
    public const float AnchorY = 560f;

    /// <summary>锅的物品 key（配方产物；装进 <see cref="CookwareSlot.Pot"/> 槽）。</summary>
    public const string PotItemKey = "cooking_pot";

    /// <summary>烤架的物品 key（配方产物；装进 <see cref="CookwareSlot.Grill"/> 槽）。</summary>
    public const string GrillItemKey = "cooking_grill";

    /// <summary>某个槽对应的库存物品 key（装槽时扣它、卸槽时还它）。</summary>
    public static string ItemKeyOf(CookwareSlot slot) => slot switch
    {
        CookwareSlot.Pot => PotItemKey,
        CookwareSlot.Grill => GrillItemKey,
        _ => "",
    };

    /// <summary>烹饪台占地（世界像素，拟定待调）：一座灶台，比工作台窄、比改装台厚。</summary>
    public const float Width = 84f;
    public const float Height = 52f;

    /// <summary><b>恒 true。</b>实心家具——人和丧尸都撞不过去，也**不可跨越**（用户拍板；灶是砌起来的）。</summary>
    public const bool IsSolid = true;

    /// <summary><b>恒 true。</b>要在导航图上挖洞 ⇒ 改变寻路。正因为玩家挪不动它，锚点才必须过设计期自检。</summary>
    public const bool CarvesNavHole = true;

    /// <summary>
    /// 放置规格。
    /// <para>
    /// ⚠️ <b>它喂的是「设计期自检」，不是运行时放置校验</b>——玩家摆不了烹饪台（固定锚点，见类注）。
    /// 保留它，是因为固定设施的锚点<b>更</b>需要被 <see cref="PlacementRules"/> 那套禁建带守住：
    /// 玩家摆错了能重摆，设计者摆错了玩家只能重开一局。
    /// </para>
    /// <para><b>刻意不填 <c>AllowedAgainstDefenses</c></b>（缺省 false = 受约束）：豁免只给恒不挡路的沙袋。</para>
    /// </summary>
    /// <remarks>
    /// [T27] <c>AllowedOutdoors: true</c>：同改装台 —— 本 spec 的<b>唯一用途是设计期自检</b>（固定锚点，玩家摆不了），
    /// 而「家具不能放到室外」那条是<b>约束玩家放置</b>的。authored 固定设施不该被它回头卡住自己。禁建带那条照守不误。
    /// </remarks>
    public static PlaceableSpec Spec => new(PropName, Width, Height, IsSolid: IsSolid, AllowedOutdoors: true);

    /// <summary>营地里有没有一座烹饪台（<paramref name="furnitureNames"/> = 场上家具名，营地层给）。</summary>
    public static bool HasStation(IEnumerable<string>? furnitureNames)
        => furnitureNames is not null && furnitureNames.Any(n => TypeOf(n) == PropName);

    /// <summary>实例名 → 类型名（"烹饪台#2" → "烹饪台"）：同 <see cref="FurnitureBuildCost"/> 的归一口径。</summary>
    private static string TypeOf(string? name)
    {
        if (name is null) return "";
        int hash = name.IndexOf('#');
        return hash >= 0 ? name[..hash] : name;
    }
}

/// <summary>某次烹饪下不了单的原因。</summary>
public enum CookBlockReason
{
    /// <summary>营地里还没有烹饪台。</summary>
    NoStation,

    /// <summary>一样食材都没放。</summary>
    NothingInThePot,

    /// <summary>库存里没有那么多这种食材。</summary>
    InsufficientIngredient,

    /// <summary>
    /// 热量凑不够一份饭。
    /// <para>⚠️ <b>这条原因永远不许出现在玩家眼前</b>——玩家只该看到"按钮按不动"，
    /// 不该看到还差多少。见 <see cref="CookingLogic.PlayerFacingText"/>。</para>
    /// </summary>
    NotEnoughCalories,
}

/// <summary>一条下不了单的明细（原因 + 相关键；<b>说明文案只给开发者看</b>，别直接喂给玩家）。</summary>
public readonly record struct CookBlock(CookBlockReason Reason, string DevDetail, string Key);

/// <summary>
/// 一次烹饪下单的判定结果：能不能做 + 做几份 + 拦在哪。
/// <see cref="Portions"/> 是**唯一允许显示给玩家**的数字（产物那行的「食物 ×N」）。
/// </summary>
public sealed record CookPlan(
    bool CanCook,
    int Portions,
    int TotalCalories,
    int PortionCost,
    IReadOnlyList<CookBlock> Blocks);

/// <summary>烹饪的判定与结算（纯函数，无状态、无副作用）。</summary>
public static class CookingLogic
{
    /// <summary>一份饭的基础热量需求（当前值以 Wiki 配置为准）。</summary>
    public const int BasePortionCost = 16;

    // ———————————————————————— 炊具减免：**每个槽各一个独立值** ————————————————————————
    //
    // Wiki 当前配置分别维护锅与烤架的减免——即使两个值恰好相等，它们也**不是同一个数**。
    // 曾经这里是一个共用常量 `CookwareDiscount`，看着更"DRY"，实际是个坑：
    // 用户要在 wiki 上调烹饪数值，共用常量意味着他**动不了其中一个**（改锅，烤架跟着变）。
    // ⇒ 拆成两个独立值。**初值不变、行为不变**（纯结构调整，既有测试原样绿）。
    // 新增炊具槽时：在 CookwareSlot 里加一个值 + 在 DiscountOf 里给它一个数（switch 逐值穷举，漏了会被编译器/测试抓到）。

    /// <summary>装上「锅」时，每份饭省下的热量（当前值以 Wiki 配置为准；可与烤架分别调）。</summary>
    public const int PotDiscount = 2;

    /// <summary>装上「烤架」时，每份饭省下的热量（当前值以 Wiki 配置为准；可与锅分别调）。</summary>
    public const int GrillDiscount = 2;

    /// <summary>某个炊具槽装上后省几点热量。⚠️ <b>这个数永远不许显示给玩家</b>（见本文件顶部）。</summary>
    public static int DiscountOf(CookwareSlot slot) => slot switch
    {
        CookwareSlot.Pot => PotDiscount,
        CookwareSlot.Grill => GrillDiscount,
        _ => 0,   // 未知槽位不白送减免（fail-safe）
    };

    /// <summary>每份饭的工时（游戏分钟，拟定待调）：做两份就干两份的活，没有"一锅端"的规模效应。</summary>
    public const int WorkMinutesPerPortion = 45;

    /// <summary>
    /// 当前每份饭要多少热量：基础需求**逐件**减去已装炊具各自的减免，具体值以 Wiki 配置为准。
    /// <para>保留下限，防止减免把需求变成非正数。</para>
    /// </summary>
    public static int PortionCost(IReadOnlySet<CookwareSlot>? installed)
    {
        int cost = BasePortionCost;
        foreach (CookwareSlot slot in installed ?? (IReadOnlySet<CookwareSlot>)new HashSet<CookwareSlot>())
        {
            cost -= DiscountOf(slot);   // 逐件减：锅和烤架各有各的数，不再共用一个常量
        }
        return Math.Max(1, cost);
    }

    /// <summary>锅里这堆东西一共多少热量（非食材的键**贡献 0**，不抛——UI 本来就只列食材）。</summary>
    public static int TotalCalories(IReadOnlyDictionary<string, int>? pot)
    {
        if (pot is null) return 0;
        int sum = 0;
        foreach (KeyValuePair<string, int> kv in pot)
        {
            if (kv.Value <= 0) continue;
            sum += FoodCalories.Of(kv.Key) * kv.Value;
        }
        return sum;
    }

    /// <summary>
    /// <b>份数 = ⌊总热量 ÷ 每份需求⌋</b>（**不封顶**）。
    /// <para>
    /// 用户举的「一到两份之间只能做一份」「超过两份能做两份」就是这条通式的实例，
    /// 不是两条特例规则；具体热量与产出数量以 Wiki 配置为准。
    /// </para>
    /// <para>
    /// <b>零头静默浪费</b>：不足下一份的余量**直接没了**——不返还、不提示、不入账。
    /// 这是刻意的（"炖过头的那锅汤"），也是玩家试错的唯一代价。
    /// </para>
    /// </summary>
    public static int Portions(int totalCalories, int portionCost)
    {
        if (totalCalories <= 0 || portionCost <= 0) return 0;
        return totalCalories / portionCost;   // int 除法 = 向下取整
    }

    /// <summary>
    /// 下这一单能不能做、做几份。
    /// </summary>
    /// <param name="hasStation">营地里有没有烹饪台（营地层用 <see cref="CookStation.HasStation"/> 算好传进来）。</param>
    /// <param name="pot">玩家往锅里放的东西：材料键 → 投入个数。</param>
    /// <param name="installed">烹饪台上装了哪些炊具。</param>
    /// <param name="availableMaterial">材料键 → 当前库存持有数（用来拦"放得比有的还多"）。</param>
    public static CookPlan Plan(
        bool hasStation,
        IReadOnlyDictionary<string, int>? pot,
        IReadOnlySet<CookwareSlot>? installed,
        Func<string, int> availableMaterial)
    {
        if (availableMaterial is null) throw new ArgumentNullException(nameof(availableMaterial));

        var blocks = new List<CookBlock>();
        int cost = PortionCost(installed);

        if (!hasStation)
        {
            blocks.Add(new CookBlock(CookBlockReason.NoStation, "营地里还没有烹饪台", CookStation.ItemKey));
        }

        var used = new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> kv in pot ?? new Dictionary<string, int>())
        {
            if (kv.Value <= 0) continue;
            used[kv.Key] = kv.Value;

            if (!FoodCalories.Has(kv.Key))
            {
                // UI 只列食材，走到这里说明调用方传了非食材 —— 当"没放"处理，不静默给它热量。
                blocks.Add(new CookBlock(
                    CookBlockReason.InsufficientIngredient, $"「{kv.Key}」不是食材", kv.Key));
                continue;
            }

            int have = availableMaterial(kv.Key);
            if (have < kv.Value)
            {
                blocks.Add(new CookBlock(
                    CookBlockReason.InsufficientIngredient,
                    $"库存不足：{kv.Key} 要{kv.Value}、有{have}", kv.Key));
            }
        }

        if (used.Count == 0)
        {
            blocks.Add(new CookBlock(CookBlockReason.NothingInThePot, "锅是空的", ""));
        }

        int total = TotalCalories(used);
        int portions = Portions(total, cost);
        if (used.Count > 0 && portions < 1)
        {
            // ⚠️ 这条**只进开发者日志，不进玩家 UI**——玩家看到的只是按钮按不动。见 PlayerFacingText。
            blocks.Add(new CookBlock(
                CookBlockReason.NotEnoughCalories, $"热量不够一份：有{total}、每份要{cost}", ""));
        }

        bool ok = blocks.Count == 0;
        return new CookPlan(ok, ok ? portions : 0, total, cost, blocks);
    }

    /// <summary>
    /// <b>给玩家看的那句话</b>——注意它**故意什么都不解释**。
    ///
    /// <para>
    /// 「没有烹饪台」「锅是空的」「库里没那么多」这三条是**玩家自己看得见的事实**（营地里没那座灶、
    /// 他一样没放、库存数字就摆在那儿），说出来不泄露任何东西；
    /// 而 <see cref="CookBlockReason.NotEnoughCalories"/> —— <b>热量不够 —— 一个字都不许说</b>：
    /// 说了就等于把热量配置直接送给玩家，用户要的试错乐趣当场归零。
    /// </para>
    /// <para>热量不够时返回 <c>null</c> = <b>什么都不提示，按钮灰着就完了</b>。</para>
    /// </summary>
    public static string? PlayerFacingText(IReadOnlyList<CookBlock> blocks)
    {
        if (blocks is null || blocks.Count == 0) return null;

        foreach (CookBlock b in blocks)
        {
            switch (b.Reason)
            {
                case CookBlockReason.NoStation:
                    return "做饭得有个灶——先在工作台上把烹饪台砌出来。";
                case CookBlockReason.NothingInThePot:
                    return "空锅煮不出饭。";
                case CookBlockReason.InsufficientIngredient:
                    return "库里没有那么多。";
                case CookBlockReason.NotEnoughCalories:
                    continue;   // ← 沉默。这是整个机制的支点，别"顺手补个提示"。
            }
        }
        return null;
    }

    // ———————————————————————— 烹饪任务 id（复用制作那条工时队列）————————————————————————
    //
    // 烹饪是**工时活**（用户："消耗一些时间"），故它和制作/拆解/改装一样挤进 CampMain 那条单任务队列。
    // 队列只认一个字符串（CraftingJob.RecipeId），按 SalvageLogic / WeaponModLogic 的成例用前缀标身份。
    // 食材在**开工时就扣掉了**（同"开工即扣"的既有语义）⇒ 完工只需知道**做几份**，故 id 里只编份数。

    /// <summary>烹饪任务 id 前缀（与 <c>salvage:</c> / <c>weaponmod:</c> 命名空间互不重叠）。</summary>
    public const string JobIdPrefix = "cook:";

    /// <summary>把"这一锅做 N 份"编成一条任务 id。</summary>
    public static string JobIdFor(int portions) => JobIdPrefix + Math.Max(0, portions);

    /// <summary>这条任务 id 是不是一次烹饪。</summary>
    public static bool IsCookJob(string? jobId)
        => jobId is not null && jobId.StartsWith(JobIdPrefix, StringComparison.Ordinal);

    /// <summary>解回"这一锅做几份"；不是烹饪任务 ⇒ null。</summary>
    public static int? PortionsOf(string? jobId)
    {
        if (!IsCookJob(jobId)) return null;
        return int.TryParse(jobId!.AsSpan(JobIdPrefix.Length), out int n) && n >= 0 ? n : null;
    }

    /// <summary>这一单的总工时（游戏分钟）= 每份工时 × 份数。</summary>
    public static int WorkMinutesFor(int portions) => WorkMinutesPerPortion * Math.Max(0, portions);
}
