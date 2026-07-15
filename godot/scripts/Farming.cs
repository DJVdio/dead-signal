using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeadSignal.Combat;   // IRandomSource（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型
//（与 TrapLogic.cs / SandbagSpec.cs / PlacementRules.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（把陷阱/菜园立到场上、每相位掷点、把猎物与收成塞进库存、关卡里铺采集点）归 Godot 消费层
//（CampMain.Farming.cs / TestExploration.cs），本文件只出**规则 + 数值**。

// ═══════════════════════════════════════════════════════════════════════════════════════
// 【T67】采集 / 种植 / 诱捕 —— 用户在 wiki 上搭的第三根支柱（前两根＝搜刮、制作）。
//
// 🔴 **这是一条链，不是几件独立的事**（用户改过一版，中间多插了「宰杀」）：
//     《农场主的一百个问题》→ 解锁【捕鸟陷阱】+【菜园】
//     【捕鸟陷阱】→ 鸟 →【宰杀】→ 鸟肉 + **羽毛** → **造箭**
//   用户在 wiki 上把**三种箭全部改成吃羽毛**（削尖的木箭 / 自制箭 / 重头箭），而「羽毛」此前**在代码库里不存在**
//   ⇒ 只同步箭配方 = 三种箭全部造不出来、弓变成烧火棍。**羽毛的来源必须先落地**，这就是本文件存在的理由。
//
// ⚠️ **羽毛不从陷阱直接出**（用户改的第二版）：陷阱只出**整只的鸟**，羽毛要上案板才拿得到
//    （见 <see cref="ButcheryLogic"/>）。故本文件<b>一个 feather 字样都不该有</b>——那是宰杀那一层的事。
//
// ⚠️ **没有开任何引擎新轴**：
//   · 陷阱的"要时间才有收获" ⇒ 走既有的**昼夜段钩子**（同 <see cref="TrapLogic"/>，掷点 2 次/天：白天 1 + 夜晚 1）
//   · 造陷阱/菜园的"要工时" ⇒ 走既有的 <see cref="CraftingJob"/>（配方 WorkMinutes 已工时化）
//   · 菜园的生长计时 ⇒ 存在既有的 <see cref="StoryFlags"/>（字符串 KV，**不加存档字段、不撞版本号**；
//     先例＝南丁格尔的主刀台数计数器、耗子的搜刮件数计数器）
//   · 采集交互 ⇒ 走既有的 **AddDiscoveryPoint 踏入链路**（impl-level-corpse 建立的范式）
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// <b>捕鸟陷阱</b>的规格（T67）。<b>形态照抄 <see cref="TrapSpec"/></b>（配方产出一件 → 库存「摆放」→ 左键落位），
/// 不发明新的建造范式。
///
/// <para>═══ <b>它和圈套陷阱是两件不同的东西，别"统一"掉</b> ═══
/// 圈套陷阱套的是<b>地上跑的</b>（老鼠 / 兔子）；捕鸟陷阱扣的是<b>天上飞的</b>（<b>鸟</b>，即原「鸽子」）。
/// 两者的产物<b>都要过案板</b>（老鼠 → 老鼠肉 + 碎皮革；鸟 → 鸟肉 + <b>羽毛</b>），
/// 而<b>只有鸟身上出羽毛</b> ⇒ **捕鸟陷阱是营地唯一可持续的箭矢原料来源**：
/// 它的位置在**军备**这条线上，不只在灶台上。（兔子是圈套的另一个产物，用户没让它进案板 ⇒ 仍可直接下锅。）
/// 两者的<b>几率递减各数各的</b>（各按自己的实例名前缀数），但它们<b>抢同一块地皮</b>
/// —— 院子就这么大，多扎一个网就少一个套子。这就是它们之间真正的取舍。</para>
///
/// <para>═══ <b>三条"我拍板的"，依据都写在这儿</b> ═══
/// <list type="bullet">
/// <item><b>占营地格子：占。</b>它是一张支在院子里的网，与圈套陷阱同样是**户外可摆放物**、同样守 64px 禁建带。
///       不占地皮＝白送，且会让"扎几个陷阱"失去取舍。</item>
/// <item><b>要工时：要。</b>走配方的 <c>WorkMinutes</c>（既有的 <see cref="CraftingJob"/> 工时化，**零新轴**）。</item>
/// <item><b>会不会被丧尸破坏：不会。</b>同圈套陷阱——"可破坏的家具"在本项目里是<b>不存在的轴</b>
///       （<see cref="CampStructure"/> 只管围栏/门这类**防御工事**）。给陷阱开耐久＝开引擎新轴，本单明令不开。</item>
/// </list></para>
/// </summary>
public static class BirdTrapSpec
{
    /// <summary>捕鸟陷阱配方 id（<see cref="RecipeBook"/>）。拆除返还也按这张配方的材料算。</summary>
    public const string RecipeId = "bird_trap";

    /// <summary>库存里那件"捕鸟陷阱"的物品 key（配方产物；摆放时从库存扣一件）。</summary>
    public const string ItemKey = "bird_trap";

    /// <summary>家具类型名（<see cref="FurnitureBuildCost"/> 的键；场上实例名带流水号"捕鸟陷阱#3"）。</summary>
    public const string FurnitureKey = "捕鸟陷阱";

    /// <summary>场上每个捕鸟陷阱的家具名前缀（实例名带流水号）。可重复摆放，故名字必须唯一。</summary>
    public const string FurnitureNamePrefix = FurnitureKey + "#";

    /// <summary>库存里的物品描述（黑色幽默文风，同批次15 的物品级 flavor）。</summary>
    public const string ItemDescription =
        "一张网，一根支棍，撒几粒不知道还能不能发芽的谷子。城里的鸽子早就不怕人了——它们只是不再有人喂。";

    /// <summary>占地（世界像素，拟定待调）：一张支起来的网，比圈套大一圈，仍是贴地物。</summary>
    public const float Width = 48f;
    public const float Height = 36f;

    /// <summary><b>恒 false</b>（同 <see cref="TrapSpec.IsSolid"/>）：不建碰撞体 ⇒ 摆不出 kill box。改它之前请先读 <see cref="TrapSpec"/> 的类注。</summary>
    public const bool IsSolid = false;

    /// <summary><b>恒 false</b>：不挖导航洞 ⇒ 寻路图不受影响。</summary>
    public const bool CarvesNavHole = false;

    /// <summary>放置规格：<b>非实心 + 允许户外</b>（网支在院子里，屋里没有鸟）；<b>仍守 64px 禁建带</b>（缺省 <c>AllowedAgainstDefenses: false</c>）。</summary>
    public static readonly PlaceableSpec PlaceSpec =
        new(FurnitureKey, Width, Height, IsSolid: IsSolid, AllowedOutdoors: true);

    /// <summary>这个家具名是不是一个玩家摆的捕鸟陷阱（"捕鸟陷阱#3" → true；"陷阱#1" → false）。</summary>
    /// <remarks>
    /// ⚠️ 与 <see cref="TrapSpec.IsTrapFurniture"/> <b>互斥且不相交</b>：圈套陷阱的前缀是"陷阱#"，
    /// 捕鸟陷阱是"捕鸟陷阱#" —— 后者<b>不以</b>前者开头（"捕鸟陷阱#1".StartsWith("陷阱#") == false），
    /// 故两个计数器各数各的，谁也不会把对方数进自己的递减档位。<b>有测试钉死这条隔离带。</b>
    /// </remarks>
    public static bool IsBirdTrapFurniture(string? furnitureName)
        => furnitureName is not null
        && furnitureName.StartsWith(FurnitureNamePrefix, StringComparison.Ordinal);
}

/// <summary>
/// <b>捕鸟陷阱的捕猎判定</b> —— 形态照抄 <see cref="TrapLogic"/>（每相位每陷阱独立掷点、第 n 个几率递减、撞地板即停），
/// <b>但数值另立一套</b>，且<b>只出整只的鸟</b>。
///
/// <para>═══ 🔴 <b>羽毛不在这儿出</b>（用户改的第二版规格）═══
/// 陷阱交出来的是一只<b>活生生的、带毛带骨的鸟</b>。要肉、要羽毛，都得<b>上案板</b>：
/// <c>鸟 →【宰杀】→ 鸟肉*1 + 羽毛*1</c>（<see cref="ButcheryLogic"/>）。
/// ⇒ 本类<b>不产出任何羽毛</b>，也<b>不该</b>产出——把羽毛塞回陷阱等于绕开宰杀那道工序，
/// 而那道工序正是用户这次改动的全部内容（它还顺带给了骨刀一个存在的理由）。</para>
///
/// <para>═══ ⚠️ <b>「鸽子/干豆保持能吃、不删」那条旧裁决：如今三块都被用户改写了</b>（本轮记一句）═══
/// 更早的裁决是"鸽子/干豆<b>保持能吃</b>、不删"，理由是**它们没有第二条命**（只是热量点而已，删了就成死物品）。
/// 用户后续的改动把这条裁决<b>整个拆散了</b>：
/// <list type="bullet">
/// <item>🔴 <b>鸟（原鸽子）的「不删」：更站得住了。</b> 它现在有了**第二条命**——是<b>整条弓箭线的原料源头</b>
///       （陷阱 → 鸟 → 宰杀 → 羽毛 → 三种箭）。⇒ 那条"别删"从<b>宽容</b>变成了<b>必需</b>：删掉鸟＝掐断弓箭线。</item>
/// <item>⚠️ <b>鸟/老鼠的「保持能吃」：被用户亲手撤销了。</b> 用户原话「**老鼠和鸟不能直接入锅了**」
///       ⇒ 它们已从 <see cref="FoodCalories"/> 移除。<b>不是我引申的，是规格改了。</b></item>
/// <item>🔴 <b>干豆：被用户拍板<b>整个删除</b>了。</b> 它没有第二条命（纯热量点），用户这次直接把它连根拔掉——
///       材料目录 / 重量登记 / 热量点 / 图标 / 搜刮点掉落 / 开局库存全线清除（护栏见 <c>BeansRemovalTests</c>）。
///       ⇒ 上面那条"删了就成死物品"的顾虑对干豆不再适用：它已经<b>不存在</b>，没有任何东西再引用它。</item>
/// </list></para>
///
/// <para>═══ <b>数值（拟定待调，用户未给）</b> ═══
/// 基准 <b>20%</b>（低于圈套的 30%：鸟比兔子机警，且它的产出是<b>食物+军备</b>两条线）；步长/地板沿用用户给圈套定的 5%/5%
/// ⇒ 20/15/10/5/5…，<b>第 4 个就撞地板</b>（比圈套更早，鸟本来就没那么多）。
/// 一个陷阱的每日期望 = 0.20 × 2 次/天（昼夜各一次）= <b>0.4 只/天</b>。
/// 过一遍简易宰杀点 ⇒ 0.4 份鸟肉 + <b>0.4 根羽毛</b>（宰杀台还有 20% 双倍 ⇒ ≈0.48 根）。
/// 一根羽毛 → <b>4 支削尖的木箭</b> ⇒ <b>一个捕鸟陷阱 ≈ 1.6 支木箭/天</b>：撑得起一个弓手，撑不起一个弓兵队。
/// （箭的真正瓶颈仍是**铁**，自制箭/重头箭都吃铁 —— 羽毛是<b>门槛</b>，不是产量天花板。）</para>
/// </summary>
public static class BirdTrapLogic
{
    /// <summary>第 1 个捕鸟陷阱的捕获几率（拟定待调）：20%，低于圈套陷阱的 30%。</summary>
    public const double BaseChance = 0.20;

    /// <summary>每多放一个，新加的那个比上一个低多少（沿用用户给圈套定的 5 个百分点）。</summary>
    public const double ChanceStep = 0.05;

    /// <summary>几率地板（沿用用户给圈套定的 5%）。递减撞到它就停。</summary>
    public const double MinChance = 0.05;

    /// <summary>
    /// 捕获物：<b>鸟</b>（<see cref="Materials"/> 目录键 —— ⚠️ <b>键仍是 <c>pigeon</c></b>，显示名才是「鸟」；
    /// 改名不改键的理由见 <c>Materials</c> 里那条注释：改键要迁存档，改显示名一行都不用迁）。
    /// <para>🔴 <b>它已经下不了锅了</b>（不在 <see cref="FoodCalories"/> 里）——要先宰杀。</para>
    /// </summary>
    public const string BirdKey = "pigeon";

    /// <summary>捕鸟陷阱一天掷几次点 = <b>2</b>（白天 1 + 夜晚 1，与圈套陷阱/吃饭同频）。
    /// <b>频率的唯一事实源在 <see cref="TrapLogic.RollsOnPhase"/></b>——两种陷阱掷点节律共用一张尺子；这里从谓词数出而非写死。
    /// <para>⚠️ 曾误按 8 个 <see cref="DayPhase"/> 逐个掷点（产出翻 4 倍，"捕鸟陷阱太强"的根因），已随圈套陷阱一并改回 2/天。</para></summary>
    public static int RollsPerDay => Enum.GetValues<DayPhase>().Count(TrapLogic.RollsOnPhase);

    /// <summary>
    /// <b>场上第 <paramref name="ordinal"/> 个捕鸟陷阱的单次捕获几率</b>（1-based）= <c>max(5%, 20% − 5%×(n−1))</c>。
    /// <para>20% / 15% / 10% / 5% / 5%… —— 第 4 个起撞地板。<paramref name="ordinal"/> ≤ 0 ⇒ 0（不白送基准几率）。</para>
    /// </summary>
    public static double ChanceOf(int ordinal)
    {
        if (ordinal <= 0)
        {
            return 0.0;
        }
        return Math.Max(MinChance, BaseChance - ChanceStep * (ordinal - 1));
    }

    /// <summary>
    /// <paramref name="trapCount"/> 个捕鸟陷阱在<b>一个相位</b>里的期望捕获数 = 前 n 项几率之和。
    /// <para>1 个 → 0.20；3 个 → 0.45；6 个 → 0.60；此后每多一个只 +0.05。</para>
    /// </summary>
    public static double ExpectedCatchesPerPhase(int trapCount)
    {
        double sum = 0.0;
        for (int n = 1; n <= trapCount; n++)
        {
            sum += ChanceOf(n);
        }
        return sum;
    }

    /// <summary>
    /// <b>一个相位的捕鸟结算</b>：场上 <paramref name="trapCount"/> 个陷阱各掷<b>一次</b>点，返回本相位捕到的鸟（材料键，可重复）。
    ///
    /// <para><b>掷点顺序（测试按此复现）</b>：逐个陷阱掷<b>一次命中点</b>；命中 ⇒ 追加一只鸟。
    /// <b>不掷第二次点</b>（没有"物种"这一说——网里只会有鸟），这与 <see cref="TrapLogic.RollPhase"/>
    /// （命中后还要掷老鼠/兔子）是<b>不同的随机流形状</b>，别照抄它的测试算例。</para>
    ///
    /// <para>没有陷阱（≤ 0）⇒ 空手而归，<b>一次点都不掷</b>。</para>
    /// </summary>
    public static IReadOnlyList<string> RollPhase(int trapCount, IRandomSource rng)
    {
        var caught = new List<string>();
        if (trapCount <= 0 || rng is null)
        {
            return caught;
        }

        for (int n = 1; n <= trapCount; n++)
        {
            if (rng.Range(0.0, 1.0) >= ChanceOf(n))
            {
                continue;   // 这个网本相位空着
            }
            caught.Add(BirdKey);
        }
        return caught;
    }
}

/// <summary>
/// <b>捕鸟陷阱的运行时编排（纯逻辑，可单测）</b> —— 镜像 <see cref="CropPlotRuntime"/> 的做法：把"一个昼夜段掷点 → 把鸟塞进库存"
/// 抽成一个不引 Godot 类型的纯函数，<b>让消费层（<c>CampMain.BirdTrap.cs</c>）和单测调同一段代码</b>，
/// 杜绝"消费层自己又写一遍 roll+入库"的第二事实源（那正是"纯逻辑绿≠功能生效"最容易埋雷的地方）。
/// <para>掷点频率不在这儿判——由 <see cref="TrapLogic.RollsOnPhase"/> 在消费层 gate；本函数只负责"<b>被叫到时</b>结算一个昼夜段"。</para>
/// </summary>
public static class BirdTrapRuntime
{
    /// <summary>
    /// <b>结算一个昼夜段</b>：场上 <paramref name="trapCount"/> 个捕鸟陷阱各掷一次点，捕到的鸟<b>逐只入库</b>（<paramref name="inventory"/>），
    /// 返回本段捕到的鸟（材料键，可空）。<b>一个陷阱都没有就彻底静默</b>（<see cref="BirdTrapLogic.RollPhase"/> 在 count≤0 时一次点都不掷）。
    /// </summary>
    /// <param name="trapCount">场上捕鸟陷阱数（消费层数 <see cref="BirdTrapSpec.IsBirdTrapFurniture"/> 得来）。</param>
    /// <param name="inventory">共享库存（真库存；测试注入真 <see cref="InventoryStore"/> 跑通两层）。</param>
    /// <param name="rng">可注入随机源（项目铁律：测试用 <see cref="SequenceRandomSource"/> 复现）。</param>
    public static IReadOnlyList<string> ResolveCatch(int trapCount, InventoryStore inventory, IRandomSource rng)
    {
        IReadOnlyList<string> caught = BirdTrapLogic.RollPhase(trapCount, rng);
        if (inventory is null || caught.Count == 0)
        {
            return caught;
        }
        foreach (IGrouping<string, string> g in caught.GroupBy(k => k))
        {
            if (Materials.Find(g.Key) is { } def)
            {
                inventory.Add(def.ToItem(g.Count()));
            }
        }
        return caught;
    }
}

/// <summary>
/// <b>营地菜园（种植区）</b>的规格（T67）—— 《农场主的一百个问题》解锁。
///
/// <para>═══ 🔴 <b>用户拍板的种植设定（一字不改地落地）</b> ═══
/// 「菜园最多种 <b>16 颗</b>植物。目前先只支持土豆：种一颗吃 <b>1 土豆</b>，成熟需 <b>84 游戏小时</b>（连续墙钟计时、
///   昼夜都走、<b>零维护</b>），收获 <b>50% 出 2 / 50% 出 1</b>。种植动作 <b>0.15 游戏小时/颗</b>（人力，走工时化）。」
/// 期望产出 1.5、净 <b>+0.5/颗</b>、下行最差回本（<b>永不亏种子</b>）。用户框架：种植成本低 ⇒ 产出不能非常高、也不能过于低。</para>
///
/// <para>═══ 🔴 <b>硬约束：它绝不能变成"无限食物"</b>（设计红线；四道闸换了形态但仍在）═══
/// <list type="number">
/// <item><b>每颗要种薯</b>：下种吃掉 <see cref="CropPlotLogic.SeedCost"/> = <b>土豆 1</b>。50/50 的下限也是 1（回本）⇒ <b>永不亏种子、零风险</b>。</item>
/// <item><b>要地皮</b>：菜园是**户外可摆放物**，占院子（守 64px 禁建带），<b>和两种陷阱抢同一块地</b>。</item>
/// <item><b>要工时</b>：种一颗要 <see cref="CropPlotLogic.PlantActionGameHours"/> = 0.15 游戏小时的<b>人力动作</b>（走既有 <see cref="CraftingJob"/> 工时化，零新轴）。满种 16 颗 = 2.4 小时一次性人力。</item>
/// <item>🔴 <b>16 颗上限 + 每颗收完要重新下种</b>（见 <see cref="MaxPlants"/>、<see cref="GardenConsumedOnHarvest"/>）。
///       <b>菜园本身不消失</b>（它是持久种植区，同"种下去就不用管"的口径）；消失的是<b>每颗收完那一格</b>——空出后要再吃 1 土豆 + 再等 84h 才有下一茬。</item>
/// </list></para>
///
/// <para>═══ <b>算给你看：它为什么喂不饱营地</b>（和 <see cref="FoodCalories"/> 对得上的账）═══
/// 满种 16 颗：每颗净 +0.5 土豆 ⇒ 一个周期净 <b>+8 土豆 = +32 热量点</b>（土豆 4 点/颗）。
/// 周期 = <b>84 游戏小时 = 3.5 个昼夜</b>（游戏钟一昼夜 = 24 游戏小时，见 <see cref="CropPlotLogic.GameHoursPerDayNightCycle"/>）
/// ⇒ <b>日均净 ≈ 9.1 热量点/昼夜</b>。一个人一天两餐 = <b>2 × 16 = 32 点</b>
/// ⇒ <b>满种一整座菜园的净产出只养得起 ≈ 2/7 个人</b>——是"睡后收入"式的稳定小补，<b>喂不饱营地</b>，
/// 也<b>关不掉饥饿系统</b>（5 人营地一天要 ~160 点，菜园覆盖不到 6%）。搜刮/宰杀依旧是主粮来源。</para>
///
/// <para>═══ <b>为什么种土豆</b>（不是我发明的作物；目前先只支持土豆，留扩展口）═══
/// <b>土豆已经存在</b>（<see cref="Materials"/> 里的 <c>potato</c>，菜窖/农庄的战利品），且它的 flavor 就写着
/// 「从谁家后院刨出来的，<b>发了点芽</b>」——<b>那句话本身就是一颗种薯</b>。**零登记成本，且世界观已自洽。**
/// 不新造"种子"材料（那是纯粹的概念膨胀：种薯就是土豆）。以后加别的作物 ⇒ 追加 <see cref="CropPlotLogic.CropKey"/> 一类的作物表即可。</para>
/// </summary>
public static class CropPlotSpec
{
    /// <summary>菜园配方 id（<see cref="RecipeBook"/>；<b>内部键仍是 crop_plot，不迁存档</b>——改显示名不改键）。</summary>
    public const string RecipeId = "crop_plot";

    /// <summary>库存里那件"菜园"的物品 key（配方产物；摆放时从库存扣一件）。</summary>
    public const string ItemKey = "crop_plot";

    /// <summary>家具类型名（用户口径「菜园」）；场上实例名带流水号"菜园#3"。内部键 crop_plot 不变——改显示名不迁存档。
    /// 与 <c>Recipe.cs</c> 的 DisplayName、<c>FurnitureBuildCost</c>["菜园"]、wiki 配方表三处同名（拆除按名归一）。</summary>
    public const string FurnitureKey = "菜园";

    /// <summary>场上每座菜园的家具名前缀（实例名带流水号）。</summary>
    public const string FurnitureNamePrefix = FurnitureKey + "#";

    /// <summary>库存里的物品描述。</summary>
    public const string ItemDescription =
        "翻好的一小块地，垄沟笔直——那本书上说，地是不骗人的。它没说的是：地也不着急。";

    /// <summary>占地（世界像素，拟定待调）：比陷阱大，一小块菜园。</summary>
    public const float Width = 64f;
    public const float Height = 48f;

    /// <summary><b>恒 false</b>：一块菜地，谁都能一脚跨过去。不建碰撞体 ⇒ 摆不出 kill box。</summary>
    public const bool IsSolid = false;

    /// <summary>放置规格：非实心 + 允许户外（<b>屋里种不出土豆</b>）；仍守 64px 禁建带。</summary>
    public static readonly PlaceableSpec PlaceSpec =
        new(FurnitureKey, Width, Height, IsSolid: IsSolid, AllowedOutdoors: true);

    /// <summary>这个家具名是不是一座玩家的菜园（"菜园#3" → true）。</summary>
    public static bool IsCropPlotFurniture(string? furnitureName)
        => furnitureName is not null
        && furnitureName.StartsWith(FurnitureNamePrefix, StringComparison.Ordinal);

    /// <summary>一座菜园最多同时种多少颗（<b>用户拍板：16</b>）。= <see cref="CropPlotLogic.MaxPlants"/>。</summary>
    public const int MaxPlants = 16;

    /// <summary>
    /// 🔴 <b>菜园本身不因收获消失</b>（false）——它是<b>持久种植区</b>（同用户"种下去就不用管"的口径）。
    /// 消失的是<b>每颗收完那一格</b>：空出后要重新下种（再吃 1 土豆 + 再等 84h）。
    /// 防"无限食物"改由<b>每颗种薯 + 16 上限 + 84h 连续等待</b>三道闸承担，不再靠"整座菜园消失"。
    /// </summary>
    public const bool GardenConsumedOnHarvest = false;
}

/// <summary>
/// <b>菜园的种植 / 生长 / 收获判定</b>（纯函数；计时器存在既有的 <see cref="StoryFlags"/> 里，<b>不加存档字段、不撞版本号</b>）。
///
/// <para>═══ <b>成熟 = 84 游戏小时连续墙钟倒计时</b>（用户拍板：种下就不用管、一直走时间、零维护）═══
/// 存<b>剩余游戏小时</b>（84.0 → … → 0），消费层每帧把 <c>delta</c> 按当前相位折成游戏小时喂进 <see cref="Tick"/>
/// （白天 <c>delta/720×12</c>、夜晚 <c>delta/480×12</c>，见 <see cref="GameClock"/>.ClockHm——一整昼夜 = 24 游戏小时）。
/// <b>昼夜都走、不按相位、不暂停、不需要浇水看护</b>。存档天然覆盖（<c>SaveData.StoryFlags</c>，先例＝南丁格尔计数器）。</para>
/// </summary>
public static class CropPlotLogic
{
    /// <summary>下种一颗要吃掉的土豆数（<b>种薯</b>）：<b>1</b>（用户拍板）。50/50 下限也是 1 ⇒ <b>永不亏种子</b>。</summary>
    public const int SeedCost = 1;

    /// <summary>一座菜园最多同时种多少颗（<b>用户拍板：16</b>）。= <see cref="CropPlotSpec.MaxPlants"/>。</summary>
    public const int MaxPlants = CropPlotSpec.MaxPlants;

    // ── 成熟计时（连续游戏墙钟，零维护）──────────────────────────────────────────────

    /// <summary>一颗成熟要多少<b>游戏小时</b>（<b>用户拍板：84</b>）。连续倒计时，昼夜都走。</summary>
    public const double GrowGameHours = 84.0;

    /// <summary>
    /// 一整昼夜有多少游戏小时（<b>24</b>）：游戏钟白天 6:00→18:00 = 12h、夜晚 18:00→6:00 = 12h
    /// （见 <see cref="GameClock"/>.ClockHm）。⇒ <b>84 游戏小时 = 3.5 个昼夜</b>。
    /// </summary>
    public const double GameHoursPerDayNightCycle = 24.0;

    /// <summary>成熟要跨多少个昼夜循环（= 84 / 24 = <b>3.5</b>）。给"几天成熟"的直观视角。</summary>
    public static double MaturesInDayNightCycles => GrowGameHours / GameHoursPerDayNightCycle;

    /// <summary>作物材料键（<see cref="Materials"/> 的 <c>potato</c>）。目前先只支持土豆，留扩展口。</summary>
    public const string CropKey = "potato";

    /// <summary><see cref="StoryFlags"/> 里那把计时器的键前缀：<c>crop_plant_remaining:菜园#3:5</c> → 剩余游戏小时（按颗）。</summary>
    public const string RemainingFlagPrefix = "crop_plant_remaining:";

    /// <summary>某一颗的计时器键（按<b>实例名</b>；拔掉/收掉一颗就清它那条 flag，不留幽灵计时器）。</summary>
    public static string RemainingFlagFor(string plantInstanceName) => RemainingFlagPrefix + plantInstanceName;

    /// <summary>刚下种：剩余游戏小时 = <see cref="GrowGameHours"/> = 84。</summary>
    public static double InitialRemainingHours => GrowGameHours;

    /// <summary>
    /// 走过 <paramref name="elapsedGameHours"/> 游戏小时：剩余 − 流逝，<b>不跌破 0</b>
    /// （熟了就一直熟着等你来收，不会烂在地里——烂菜是引擎新轴，本单不开）。负流逝按 0 处理。
    /// </summary>
    public static double Tick(double remainingGameHours, double elapsedGameHours)
        => Math.Max(0.0, remainingGameHours - Math.Max(0.0, elapsedGameHours));

    /// <summary>熟了没（剩余游戏小时 ≤ 0）。</summary>
    public static bool IsRipe(double remainingGameHours) => remainingGameHours <= 0.0;

    /// <summary>还要几个昼夜（向上取整，给面板显示用）= ceil(剩余小时 / 24)。</summary>
    public static int DaysLeft(double remainingGameHours)
        => (int)Math.Ceiling(Math.Max(0.0, remainingGameHours) / GameHoursPerDayNightCycle);

    // ── 种植动作（人力，走既有 CraftingJob 工时化；零新轴）──────────────────────────────

    /// <summary>制作队列里"种一颗"任务 id 的前缀（同 <c>cook:</c>／<c>salvage:</c> 的分流范式）：<c>plant:菜园#3</c>。</summary>
    public const string PlantJobPrefix = "plant:";

    /// <summary>种一颗的人力动作耗时（<b>用户拍板：0.15 游戏小时/颗</b>）。占用一个幸存者 0.15h。</summary>
    public const double PlantActionGameHours = 0.15;

    /// <summary>种一颗动作折成 <see cref="CraftingJob"/> 的工时（游戏分钟）= 0.15 × 60 = <b>9</b>。</summary>
    public const int PlantWorkMinutes = (int)(PlantActionGameHours * 60);   // = 9

    // ── 收获（三段分布，随机走可注入 IRandomSource、可复现）────────────────────────────────
    // 🔴 <b>用户最终拍板</b>（原始 50/50 出 2/1，几经增/减后定在此档）：<b>50% 出 2 / 25% 出 3 / 25% 出 1</b>
    //    ⇒ 期望 <b>2.0</b>、净 <b>+1.0/颗</b>。下行仍是<b>回本</b>（最差 25% 出 1 = 种薯 1，永不亏种子）。
    //    满种日均净 ≈ 18 点 ⇒「勉强养活大半个人」（约 4/7 人）——正是用户口径。

    /// <summary>收获出 <b>2 颗</b>的几率：<b>50%</b>（用户拍板）。落点区间 [0, 0.50)。</summary>
    public const double Out2Chance = 0.50;

    /// <summary>收获出 <b>3 颗</b>的几率：<b>25%</b>。落点区间 [0.50, 0.75)。</summary>
    public const double Out3Chance = 0.25;

    /// <summary>收获出 <b>1 颗</b>的几率：<b>25%</b>（= 种薯 <see cref="SeedCost"/> ⇒ 回本，永不亏种子）。落点区间 [0.75, 1)。</summary>
    public const double Out1Chance = 0.25;

    /// <summary>收获下限：<b>1 颗</b>（= 种薯 <see cref="SeedCost"/> ⇒ 最差回本，永不亏种子）。</summary>
    public const int LowYield = 1;

    /// <summary>收获中档：<b>2 颗</b>（最常见，50%）。</summary>
    public const int MidYield = 2;

    /// <summary>收获上限：<b>3 颗</b>（25%）。</summary>
    public const int HighYield = 3;

    /// <summary>三段分布第一道边界：<c>rng &lt; 0.50 ⇒ 2 颗</c>。</summary>
    public const double Out2Cutoff = Out2Chance;                 // 0.50

    /// <summary>三段分布第二道边界：<c>0.50 ≤ rng &lt; 0.75 ⇒ 3 颗；rng ≥ 0.75 ⇒ 1 颗</c>。</summary>
    public const double Out3Cutoff = Out2Chance + Out3Chance;    // 0.75

    /// <summary>
    /// <b>收一颗</b>：掷一次点，按三段分布定产量（可注入随机、可复现）：
    /// <c>[0,0.50) ⇒ 2 颗 · [0.50,0.75) ⇒ 3 颗 · [0.75,1) ⇒ 1 颗</c>。
    /// <paramref name="rng"/> 为 null ⇒ 保底给下限（不白送高产）。
    /// </summary>
    public static int RollHarvest(IRandomSource rng)
    {
        if (rng is null)
        {
            return LowYield;
        }
        double roll = rng.Range(0.0, 1.0);
        if (roll < Out2Cutoff) return MidYield;    // [0, 0.50)    → 2
        if (roll < Out3Cutoff) return HighYield;   // [0.50, 0.75) → 3
        return LowYield;                           // [0.75, 1)    → 1
    }

    /// <summary>一颗的期望产出 = 0.50 × 2 + 0.25 × 3 + 0.25 × 1 = <b>2.0</b>。</summary>
    public static double ExpectedYieldPerPlant
        => Out2Chance * MidYield + Out3Chance * HighYield + Out1Chance * LowYield;

    /// <summary>一颗的期望<b>净</b>产出（扣种薯）= 2.0 − 1 = <b>+1.0</b>。护栏：必须 &gt; 0（否则种地纯亏）。</summary>
    public static double NetExpectedYieldPerPlant => ExpectedYieldPerPlant - SeedCost;

    /// <summary>满种 16 颗一个周期的期望净产出（土豆颗数）= 16 × 1.0 = <b>16</b>。</summary>
    public static double NetExpectedYieldPerGarden => MaxPlants * NetExpectedYieldPerPlant;

    /// <summary>
    /// 满种菜园一个周期（84h）的期望<b>净热量点</b> = 16 颗 × 土豆 4 点 = <b>64 点</b>。
    /// <b>护栏盯着它</b>：为正（种地不是纯亏）且日均 ≈ 18 点（64 / 3.5 昼夜）仍<b>喂不饱一个人</b>（一天两餐 32 点）
    /// ——「勉强养活大半个人」（约 4/7 人，用户口径）；见 <see cref="CropPlotSpec"/> 类注的账。
    /// </summary>
    public static double NetExpectedCaloriesPerGarden => NetExpectedYieldPerGarden * FoodCalories.Of(CropKey);
}

/// <summary>
/// <b>菜园的消费层编排</b>（种下 / 每帧生长 / 收获入库）——<b>纯逻辑</b>，只吃 <see cref="StoryFlags"/>（计时器存这儿，
/// 存档天然覆盖，不加字段）+ <see cref="InventoryStore"/>（实扣种薯、实产土豆）+ <see cref="IRandomSource"/>（收获掷点可复现）。
///
/// <para>═══ 🔴 <b>它是"绿但死"失效模式的解药</b> ═══
/// <see cref="CropPlotLogic"/> 是纯规则（一颗要多久、收几颗）；<b>本类才是"运行时真的调了它、结果真的进了库存"的那一层</b>。
/// Godot 侧 <c>CampMain.Farming.cs</c> 只把鼠标事件/每帧 delta 接到本类的方法上（同 <c>CampMain.Traps.cs</c> 之于 <see cref="TrapLogic"/>）；
/// 覆盖自检（<c>CropPlotRuntimeTests</c>）直接拿真 <see cref="InventoryStore"/> + <see cref="StoryFlags"/> 跑通"种→84h→收→库存 +1~3"两层。</para>
///
/// <para>═══ <b>每颗一格、按实例名+槽号存计时器</b> ═══
/// 一座菜园 <see cref="CropPlotLogic.MaxPlants"/>=16 颗，每颗一条 flag：<c>crop_plant_remaining:菜园#3:5 = 剩余游戏小时</c>。
/// 下种占下一个空槽、收掉即清那条 flag（不留幽灵计时器）。<b>两份事实源焊死</b>：容量/成熟/种薯/收获分布全从 <see cref="CropPlotLogic"/> 读，
/// 本类不硬编码第二份。</para>
/// </summary>
public static class CropPlotRuntime
{
    /// <summary>某座菜园全部计时器 flag 的公共前缀：<c>crop_plant_remaining:菜园#3:</c>。</summary>
    private static string PlotPrefix(string plotName) => CropPlotLogic.RemainingFlagPrefix + plotName + ":";

    /// <summary>某座菜园第 <paramref name="slot"/> 格（1-based）的计时器 flag 键。</summary>
    private static string SlotFlag(string plotName, int slot)
        => CropPlotLogic.RemainingFlagFor($"{plotName}:{slot}");

    /// <summary>这座菜园当前已占用（种着东西的）格数。</summary>
    public static int PlantedCount(StoryFlags flags, string plotName)
    {
        if (flags is null || string.IsNullOrEmpty(plotName))
        {
            return 0;
        }
        int n = 0;
        for (int slot = 1; slot <= CropPlotLogic.MaxPlants; slot++)
        {
            if (flags.Has(SlotFlag(plotName, slot)))
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>下一个空格（1..16）；满了返回 <b>0</b>。</summary>
    public static int NextFreeSlot(StoryFlags flags, string plotName)
    {
        if (flags is null || string.IsNullOrEmpty(plotName))
        {
            return 0;
        }
        for (int slot = 1; slot <= CropPlotLogic.MaxPlants; slot++)
        {
            if (!flags.Has(SlotFlag(plotName, slot)))
            {
                return slot;
            }
        }
        return 0;   // 满种
    }

    /// <summary>这座菜园还有空格能下种吗。</summary>
    public static bool HasFreeSlot(StoryFlags flags, string plotName) => NextFreeSlot(flags, plotName) != 0;

    /// <summary>库里的种薯（土豆）够种一颗吗（<see cref="CropPlotLogic.SeedCost"/>=1）。</summary>
    public static bool HasSeed(InventoryStore inventory)
        => inventory is not null && inventory.MaterialCount(CropPlotLogic.CropKey) >= CropPlotLogic.SeedCost;

    /// <summary>
    /// <b>能不能下这一单种植</b>：要有空格 + 库里有种薯。给不了时用 <paramref name="reason"/> 出人话（消费层直接弹）。
    /// </summary>
    public static bool CanPlant(StoryFlags flags, InventoryStore inventory, string plotName, out string? reason)
    {
        if (!HasFreeSlot(flags, plotName))
        {
            reason = "这块菜园已经种满了（16 颗）——等收了再补种。";
            return false;
        }
        if (!HasSeed(inventory))
        {
            reason = "没有种薯——种土豆得先有一颗土豆下地。";
            return false;
        }
        reason = null;
        return true;
    }

    /// <summary>
    /// <b>下种开工（扣料）</b>——同 <c>cook:</c> 的「开工即扣料锁定」：从库里扣掉 <see cref="CropPlotLogic.SeedCost"/> 颗种薯。
    /// 成功返回 true（消费层随后起一条 <c>plant:菜园#N</c> 工时任务）。扣不动（库空）返回 false。
    /// </summary>
    public static bool BeginPlant(InventoryStore inventory)
        => inventory is not null && inventory.TrySpendMaterial(CropPlotLogic.CropKey, CropPlotLogic.SeedCost);

    /// <summary>
    /// <b>种植工时满、完工</b>：把下一个空格的计时器置成 <see cref="CropPlotLogic.InitialRemainingHours"/>=84（开始 84 游戏小时倒计时）。
    /// 返回落种的格号；满种（理论上不该发生，开工前已 <see cref="CanPlant"/> 校验过）返回 0（种薯已扣，但这里没格可落——消费层据此如实报）。
    /// </summary>
    public static int CompletePlant(StoryFlags flags, string plotName)
    {
        int slot = NextFreeSlot(flags, plotName);
        if (slot == 0)
        {
            return 0;
        }
        flags.Set(SlotFlag(plotName, slot),
            CropPlotLogic.InitialRemainingHours.ToString("R", CultureInfo.InvariantCulture));
        return slot;
    }

    /// <summary>
    /// <b>每帧生长（零维护、昼夜都走）</b>：把<b>全场每一颗</b>在长的作物的剩余游戏小时按 <paramref name="elapsedGameHours"/> 递减
    /// （走 <see cref="CropPlotLogic.Tick"/>，不跌破 0）。由消费层每帧把 <c>delta</c> 折成游戏小时喂进来（<see cref="GameClock"/>）。
    /// <para>≤0 或无计时器 ⇒ 一个字节都不动（空闲营地零开销）。</para>
    /// </summary>
    public static void TickGrowth(StoryFlags flags, double elapsedGameHours)
    {
        if (flags is null || elapsedGameHours <= 0.0)
        {
            return;
        }
        // 先拍键快照（下面要 Set，不能边遍历边改）。跨所有菜园的每一格。
        var keys = flags.Snapshot().Keys
            .Where(k => k.StartsWith(CropPlotLogic.RemainingFlagPrefix, StringComparison.Ordinal))
            .ToList();
        foreach (string key in keys)
        {
            if (TryReadHours(flags, key, out double remaining))
            {
                double next = CropPlotLogic.Tick(remaining, elapsedGameHours);
                flags.Set(key, next.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }

    /// <summary>这座菜园里熟了的格号清单（剩余 ≤ 0）。</summary>
    public static IReadOnlyList<int> RipeSlots(StoryFlags flags, string plotName)
    {
        var ripe = new List<int>();
        if (flags is null || string.IsNullOrEmpty(plotName))
        {
            return ripe;
        }
        for (int slot = 1; slot <= CropPlotLogic.MaxPlants; slot++)
        {
            if (TryReadHours(flags, SlotFlag(plotName, slot), out double remaining) && CropPlotLogic.IsRipe(remaining))
            {
                ripe.Add(slot);
            }
        }
        return ripe;
    }

    /// <summary>这座菜园熟了几颗（可直接收几颗）。</summary>
    public static int RipeCount(StoryFlags flags, string plotName) => RipeSlots(flags, plotName).Count;

    /// <summary>这座菜园里还在长（未熟）的格数。</summary>
    public static int GrowingCount(StoryFlags flags, string plotName) => PlantedCount(flags, plotName) - RipeCount(flags, plotName);

    /// <summary>面板/悬停用：这座菜园里最快熟的那一颗还剩几个昼夜（没有在长的返回 0）。</summary>
    public static int SoonestDaysLeft(StoryFlags flags, string plotName)
    {
        int best = int.MaxValue;
        for (int slot = 1; slot <= CropPlotLogic.MaxPlants; slot++)
        {
            if (TryReadHours(flags, SlotFlag(plotName, slot), out double remaining) && !CropPlotLogic.IsRipe(remaining))
            {
                best = Math.Min(best, CropPlotLogic.DaysLeft(remaining));
            }
        }
        return best == int.MaxValue ? 0 : best;
    }

    /// <summary>
    /// <b>收掉这座菜园所有熟了的颗</b>：每颗掷一次 <see cref="CropPlotLogic.RollHarvest"/>（可注入随机、可复现），
    /// <b>实产土豆入 <see cref="InventoryStore"/></b> 并清掉那格的计时器（腾出空格可重新下种）。
    /// 返回 (收了几颗, 一共入库几个土豆)。没有熟的 ⇒ (0,0)，一次点都不掷。
    /// </summary>
    public static (int Plants, int Potatoes) HarvestRipe(
        StoryFlags flags, InventoryStore inventory, string plotName, IRandomSource rng)
    {
        if (flags is null || inventory is null)
        {
            return (0, 0);
        }
        IReadOnlyList<int> ripe = RipeSlots(flags, plotName);
        if (ripe.Count == 0)
        {
            return (0, 0);
        }

        int totalPotatoes = 0;
        foreach (int slot in ripe)
        {
            int yield = CropPlotLogic.RollHarvest(rng);   // 走真随机源，逐颗独立掷点
            totalPotatoes += yield;
            flags.Set(SlotFlag(plotName, slot), null);    // 收掉 ⇒ 清计时器（腾格）
        }

        if (totalPotatoes > 0 && Materials.Find(CropPlotLogic.CropKey) is { } def)
        {
            inventory.Add(def.ToItem(totalPotatoes));     // 实产入库
        }
        return (ripe.Count, totalPotatoes);
    }

    /// <summary>
    /// <b>整座菜园被拆走/移除</b>时清干净它名下所有格的计时器（不留幽灵计时器；未收的作物随菜园一起没了——同"拆家具东西不退"）。
    /// </summary>
    public static void ClearPlot(StoryFlags flags, string plotName)
    {
        if (flags is null || string.IsNullOrEmpty(plotName))
        {
            return;
        }
        for (int slot = 1; slot <= CropPlotLogic.MaxPlants; slot++)
        {
            string key = SlotFlag(plotName, slot);
            if (flags.Has(key))
            {
                flags.Set(key, null);
            }
        }
    }

    /// <summary>
    /// <b>一帧实时秒 → 游戏小时</b>（消费层每帧喂给 <see cref="TickGrowth"/> 的换算，抽出来单测）：
    /// 一个昼/夜相位铺 12 游戏小时（同 <see cref="GameClock"/>.ClockHm），故 <c>= realSeconds / phaseLengthSeconds × 12</c>。
    /// <para>白天相位长 720s ⇒ 每实时秒 = 12/720 游戏小时；夜晚 480s ⇒ 12/480。≤0 或相位长非正 ⇒ 0（冻结相位 delta=0 天然不长）。</para>
    /// </summary>
    public static double GameHoursForElapsed(double realSeconds, double phaseLengthSeconds)
    {
        if (realSeconds <= 0.0 || phaseLengthSeconds <= 0.0)
        {
            return 0.0;
        }
        return realSeconds / phaseLengthSeconds * (CropPlotLogic.GameHoursPerDayNightCycle / 2.0);
    }

    /// <summary>读一条计时器 flag 的剩余游戏小时；键不存在或解析不了 ⇒ false（不误当它熟了）。</summary>
    private static bool TryReadHours(StoryFlags flags, string key, out double hours)
    {
        hours = 0.0;
        string? raw = flags.Get(key);
        return raw is not null
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out hours);
    }
}

/// <summary>
/// <b>野外采集</b>（T67）—— 土豆 / 蘑菇的「<b>在地上采</b>」交互。
///
/// <para>═══ <b>它和"搜刮箱"是两件事</b>（此前土豆/蘑菇<b>只是搜刮箱里的战利品</b>）═══
/// 搜刮＝翻别人的柜子（<see cref="ExplorationCache"/>，逐件计时的 <see cref="LootSession"/>）；
/// 采集＝<b>弯腰在地上薅一把就走</b>（<b>不进搜刮会话、不逐件计时</b>）。
/// 前者是"这屋子里的人留下了什么"，后者是"<b>这片地自己长出了什么</b>"——后者是本项目此前<b>完全没有</b>的那一类收益。</para>
///
/// <para>═══ <b>交互范式零发明</b> ═══
/// 走既有的 <b>AddDiscoveryPoint 踏入链路</b>（impl-level-corpse 建立的范式）：踏进去 ⇒ <c>OnExplorationDiscovery</c>
/// 收到一个 id ⇒ 本类 <see cref="Resolve"/> 出"给什么、给多少" ⇒ 消费层实产入库。<b>没有新 UI、没有新节点类型。</b></para>
///
/// <para>═══ <b>《野外生存指南》的作用</b>（用户把采集挂在这本书名下）═══
/// <b>不读书也采得到</b>（弯腰薅一把不需要执照），但<b>读过的人采得更多</b>：产量 <b>×1.5</b>（<b>乘算</b>，向下取整）
/// —— 书教的是"哪一丛底下还有、哪一朵别碰"。
/// 这与蘑菇的 flavor 严丝合缝：「<b>你认得这一种，你很确定你认得这一种</b>」——那句"很确定"里的心虚，正是没读书的人。
/// ⚠️ <b>不做"毒蘑菇"</b>：中毒是引擎新轴（本单明令不开新轴）。书的作用只落在<b>产量</b>上。</para>
/// </summary>
public static class ForageLogic
{
    /// <summary>读过《野外生存指南》的采集产量乘子（<b>乘算</b>，项目铁律）。1.5 ⇒ 采 2 变 3、采 3 变 4。</summary>
    public const double GuideYieldMultiplier = 1.5;

    /// <summary>《野外生存指南》书 id（对齐 <see cref="RecipeBook.WildernessSurvivalGuideBookId"/>，不抄字符串）。</summary>
    public const string GuideBookId = RecipeBook.WildernessSurvivalGuideBookId;

    /// <summary>一处采集点：id（踏入触发用）、产什么、基准产多少、面板/地图上的中文标签。</summary>
    public readonly record struct ForageSpot(string Id, string MaterialKey, int BaseQuantity, string Label);

    // ── 采集点清单（**追加末尾不插队**：新点一律加在最后，别打乱既有随机流/测试算例）──
    //
    // 布点原则：**采集点长在"地自己会长东西"的地方**，不长在屋里、不长在城里。
    // · 蘑菇 → 阴湿背光处（守林人小屋后山坡的柴堆背阴、林下腐叶）
    // · 土豆 → 有人种过的地（农庄的菜地 —— 那家人的地还在，只是没人回来收了）
    // ⚠️ **刻意不进下水道**：impl-sewer 已在下水道的**搜刮点**里放了蘑菇（水线上潮气最重的地方，
    //    `ExplorationCache.cs` 的 Sewer 段）。**那是它的地盘，我不去它那儿再铺一层采集点**——
    //    同一处出两遍蘑菇既冗余又会让下水道的收成翻倍，越过它定的"很少量"口径。

    /// <summary>守林人小屋·后山坡林下腐叶（蘑菇）。</summary>
    public const string RangersCabinMushroomId = "forage_rangers_mushroom";

    /// <summary>守林人小屋·柴堆背阴（蘑菇）。</summary>
    public const string RangersCabinWoodpileMushroomId = "forage_rangers_woodpile_mushroom";

    /// <summary>斯图尔特庄园·后院菜地（土豆）。</summary>
    public const string StuartGardenPotatoId = "forage_stuart_potato";

    /// <summary>斯图尔特庄园·地头垄尾（土豆，刨剩下的）。</summary>
    public const string StuartFurrowPotatoId = "forage_stuart_furrow_potato";

    /// <summary>全部采集点（<b>按声明顺序，新点追加末尾</b>）。</summary>
    public static readonly IReadOnlyList<ForageSpot> All = new[]
    {
        new ForageSpot(RangersCabinMushroomId,         "mushroom", 2, "林下蘑菇"),
        new ForageSpot(RangersCabinWoodpileMushroomId, "mushroom", 2, "柴堆背阴的蘑菇"),
        new ForageSpot(StuartGardenPotatoId,           "potato",   3, "菜地里的土豆"),
        new ForageSpot(StuartFurrowPotatoId,           "potato",   2, "垄尾漏刨的土豆"),
    };

    /// <summary>这个 id 是不是一处采集点（消费层用它把踏入事件路由过来）。</summary>
    public static bool IsForageSpot(string? id) => Find(id) is not null;

    /// <summary>按 id 找采集点；没有返回 null。</summary>
    public static ForageSpot? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        foreach (var s in All)
        {
            if (string.Equals(s.Id, id, StringComparison.Ordinal))
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>
    /// <b>采一把</b>：返回 (材料键, 数量)。读过《野外生存指南》⇒ 数量 <b>×1.5 乘算、向下取整</b>；不读书也有基准量。
    /// <para>未知 id ⇒ 数量 0（消费层据此静默跳过，不白送东西）。</para>
    /// </summary>
    public static (string MaterialKey, int Quantity) Resolve(string? spotId, bool hasWildernessGuide)
    {
        var spot = Find(spotId);
        if (spot is null)
        {
            return (string.Empty, 0);
        }

        int qty = spot.Value.BaseQuantity;
        if (hasWildernessGuide)
        {
            qty = (int)Math.Floor(qty * GuideYieldMultiplier);   // 乘算（项目铁律），向下取整
        }
        return (spot.Value.MaterialKey, Math.Max(0, qty));
    }

    /// <summary>同上，但直接吃一份"读过的书"集合（消费层手里就是这个）。</summary>
    public static (string MaterialKey, int Quantity) Resolve(string? spotId, ReadBookSet? readBooks)
        => Resolve(spotId, readBooks is not null && readBooks.HasRead(GuideBookId));
}
