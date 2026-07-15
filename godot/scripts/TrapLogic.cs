using System;
using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;   // IRandomSource（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯逻辑**，不得引入任何 Godot 类型
//（与 SandbagSpec.cs / BedSpec.cs / PlacementRules.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（把陷阱立到场上 / 每相位掷点 / 把猎物塞进库存）归 Godot 消费层（CampMain.Traps.cs），本文件只出**规则 + 数值**。

/// <summary>
/// 玩家可建造、可自由摆放的<b>圈套陷阱</b>规格（批次21·T26）。<b>形态照抄 <see cref="SandbagSpec"/> / <see cref="BedSpec"/></b>
/// —— 同一条链路（配方产出一件"圈套陷阱" → 库存「摆放」→ 左键落位），不发明新的建造范式。
///
/// <para>═══ <b>陷阱凭什么能自由摆放？（同沙袋/床那条论证，别"统一"掉）</b> ═══
/// 用户拍板 <b>"墙不能建"</b> 是为了防 kill box：能砌实心墙就能用迷宫牵着敌人的寻路走。
/// 陷阱和沙袋/床一样<b>不阻挡移动、不改变寻路</b>（<see cref="IsSolid"/> / <see cref="CarvesNavHole"/> 恒 false）——
/// 它是一圈铁丝套加两根木桩，贴在地上，谁都能一脚跨过去（跨过要减速，见 <see cref="FurnitureTraversal"/>）。
/// ⇒ 摆不出 kill box，故获准自由摆放。谁把它改成实心的，kill box 就回来了。</para>
///
/// <para>═══ <b>但它仍守 64px 禁建带</b>（<see cref="PlaceSpec"/> 的 <c>AllowedAgainstDefenses</c> 取缺省 false）═══
/// 不挡路 ≠ 可以糊在防线上：墙根下那条道要留给砌墙工（围栏升级/修复的施工站位带）和逃命的人。
/// 沙袋是<b>唯一</b>拿到防线豁免的东西（它的本职就是垒在防线后当掩体），陷阱没有那个理由。</para>
///
/// <para><b>陷阱不是掩体</b>（区别于沙袋）：躲在一圈铁丝套后面挡不了枪。<see cref="CoverLogic"/> 不登记它。</para>
/// </summary>
public static class TrapSpec
{
    /// <summary>圈套陷阱配方 id（<see cref="RecipeBook"/>）。拆除返还也按这张配方的材料算（<see cref="SalvageLogic"/>）。</summary>
    public const string RecipeId = "snare_trap";

    /// <summary>库存里那件"圈套陷阱"的物品 key（配方产物；摆放时从库存扣一件）。</summary>
    public const string ItemKey = "snare_trap";

    /// <summary>家具类型名（<see cref="FurnitureBuildCost"/> 的键；场上实例名带流水号"陷阱#3"）。</summary>
    public const string FurnitureKey = "陷阱";

    /// <summary>场上每个陷阱的家具名前缀（实例名带流水号："陷阱#3"）。陷阱可重复摆放，故名字必须唯一。</summary>
    public const string FurnitureNamePrefix = FurnitureKey + "#";

    /// <summary>库存里的物品描述（黑色幽默文风，同批次15 的物品级 flavor）。</summary>
    public const string ItemDescription =
        "一圈铁丝，一根弹木，一个活结。它不问你饿不饿，也不问那只兔子做错了什么——它只是在那儿等着，日夜不休。";

    /// <summary>一个陷阱的占地（世界像素，拟定待调）：比沙袋还小的一片贴地物，够绊住一只兔子，绊不住一个人。</summary>
    public const float Width = 40f;
    public const float Height = 28f;

    /// <summary>
    /// <b>恒 false。</b>不建碰撞体 ⇒ 人和丧尸都能直接走过去（跨过时减速 25%，走 <see cref="FurnitureTraversal"/> 的缺省档）。
    /// 改成 true 之前请先回去读本类的类注：kill box 就是这么来的。
    /// </summary>
    public const bool IsSolid = false;

    /// <summary><b>恒 false。</b>不挖导航洞 ⇒ 寻路图不受影响。与 <see cref="IsSolid"/> 一起保证摆不出 kill box。</summary>
    public const bool CarvesNavHole = false;

    /// <summary>
    /// 放置规格（喂 <see cref="PlacementRules.CanPlace"/> / <c>CampMain.CheckFurniturePlacement</c>）。
    /// <b>非实心</b>，且 <c>AllowedAgainstDefenses</c> 取缺省 <c>false</c> ⇒ <b>老实守 64px 禁建带</b>（见类注）。
    /// </summary>
    /// <remarks>
    /// [T27] <b><c>AllowedOutdoors: true</c> —— 陷阱是本规则的一处豁免</b>。用户拍板「家具不能放到室外」，
    /// 但<b>陷阱不是家具</b>：它是一圈铁丝套加两根木桩，摆在院子里套猎物/绊丧尸的。
    /// 把它关进屋里等于<b>废掉这个机制</b>（屋里没有猎物，丧尸也不会先敲门）。
    /// 定位上它与<b>沙袋</b>同类（户外战术物件），不与床/桌子同类。
    /// ⚠️ 用户只点名了"家具"与"沙袋"，<b>陷阱是我按语义推定的</b>，已 [DECISION] 上抛——一行可改回。
    /// 注意它<b>仍守 64px 禁建带</b>（<c>AllowedAgainstDefenses</c> 取缺省 false），两条限制互相独立。
    /// </remarks>
    public static readonly PlaceableSpec PlaceSpec =
        new(FurnitureKey, Width, Height, IsSolid: IsSolid, AllowedOutdoors: true);

    /// <summary>这个家具名是不是一个玩家摆的陷阱（"陷阱#3" → true；"陷阱" / "沙袋#1" → false）。</summary>
    /// <remarks>
    /// 按<b>实例名前缀</b>认人：几率按"场上第 n 个"递减（<see cref="TrapLogic.ChanceOf"/>），
    /// 故消费层每相位都要数一遍场上有几个陷阱 —— 这就是那把尺子。
    /// </remarks>
    public static bool IsTrapFurniture(string? furnitureName)
        => furnitureName is not null
        && furnitureName.StartsWith(FurnitureNamePrefix, StringComparison.Ordinal);
}

/// <summary>
/// <b>圈套陷阱的捕猎判定</b> —— 用户原话「<b>陷阱机制是每个相位都有 30% 的几率抓到老鼠或者兔子，
/// 每多放置一个，多的那个几率就会减小 5%，最低到 5%。</b>」的唯一落点。
///
/// <para>═══ <b>规则形态（含糊处的落地口径，已上抛用户）</b> ═══
/// <list type="number">
/// <item><b>第 n 个陷阱的几率 = max(5%, 30% − 5%×(n−1))</b> ⇒ 30/25/20/15/10/5/5/5…（<see cref="ChanceOf"/>）。</item>
/// <item><b>每个陷阱每相位各掷一次，彼此独立</b>（不是"全营地一次判定"）——三个陷阱一个相位掷三次点。</item>
/// <item><b>边际递减</b>：新加的那个吃最低的那档，已放好的不受影响。</item>
/// <item>抓到的是<b>老鼠或兔子</b>（比例 <see cref="RabbitShare"/> <b>拟定待调</b>，用户未指定）。</item>
/// </list>
/// </para>
///
/// <para>═══ <b>为什么"第 n 个"不必绑定到具体某个陷阱</b>（一个省事的巧合，也是设计上的干净处）═══
/// 一个相位的期望产出 = 前 n 项几率之和，<b>与"哪个陷阱排第几"无关</b>（加法可交换）。
/// ⇒ 本模型只需要知道<b>场上有几个陷阱</b>，不需要给每个陷阱记住它的"出生序号"。
/// 好处是<b>拆掉一个陷阱</b>时不会留下"幽灵档位"：拆到只剩 2 个，那 2 个就吃 30%+25%，
/// 而不是"你拆的是第 1 个，所以剩下的还是 25%+20%"。存档也因此不必存序号——数一遍就有。
/// </para>
///
/// <para>═══ <b>陷阱是烹饪的稳定食材来源</b>（这条机制真正的经济位置）═══
/// 抓到的老鼠（<b>6 热量点</b>）与兔子（<b>11 热量点</b>）直接入库存，是 <see cref="FoodCalories"/> 里的正经食材
/// —— 陷阱因此成了营地<b>唯一不用出门、不担风险</b>的食物来源。它<b>喂不饱</b>一个营地（见 <see cref="ExpectedCatchesPerPhase"/>
/// 的算式：满地板 6 个陷阱一天掷 2 次点也就约 <b>2.1 只 ≈ 16 热量点 ≈ 约 1 份饭</b>），但它把"今天没搜到吃的"从<b>死局</b>变成了<b>苦日子</b>。
/// </para>
/// </summary>
public static class TrapLogic
{
    /// <summary>第 1 个陷阱的捕获几率（<b>用户给定</b>：30%）。</summary>
    public const double BaseChance = 0.30;

    /// <summary>每多放一个陷阱，新加的那个比上一个低多少（<b>用户给定</b>：5 个百分点）。</summary>
    public const double ChanceStep = 0.05;

    /// <summary>几率地板（<b>用户给定</b>：最低 5%）。<b>递减撞到它就停</b>，绝不继续往下走成负数。</summary>
    public const double MinChance = 0.05;

    /// <summary>捕获物：老鼠（<see cref="Materials"/> 目录键；<see cref="FoodCalories"/> 里值 6 点热量）。</summary>
    public const string RatKey = "rat";

    /// <summary>捕获物：兔子（<see cref="Materials"/> 目录键；<see cref="FoodCalories"/> 里值 11 点热量）。</summary>
    public const string RabbitKey = "rabbit";

    /// <summary>
    /// 抓到的是兔子的概率（其余是老鼠）。<b>拟定待调 —— 用户只说了"老鼠或者兔子"，没给比例。</b>
    /// <para>
    /// 取 30% 的理由：兔子值 11 点热量、老鼠值 6 点（<b>用户给定的定值</b>），兔子近乎两只老鼠 ⇒ 它该是那个"走运"的结果，
    /// 而不是常态。这也贴着材料表里兔子的那句 flavor：「抓到它的那天，你会想起从前『运气不错』是个多么轻飘飘的词」。
    /// </para>
    /// <para>期望热量 = 0.3 × 11 + 0.7 × 6 = <b>7.5 点/只</b>。</para>
    /// </summary>
    public const double RabbitShare = 0.30;

    /// <summary>
    /// <b>陷阱在这个相位掷不掷点</b>——只在<b>两个昼夜段边界</b>各掷一次：白天段（<see cref="DayPhase.DawnMeal"/>）+
    /// 夜晚段（<see cref="DayPhase.DuskMeal"/>）⇒ <b>一天 2 次</b>（用户拍板：白天 1 次 + 夜晚 1 次，与吃饭/饥饿同频）。
    /// <para>🔴 <b>这是掷点频率的唯一事实源</b>：消费层（<c>CampMain.ResolveTrapsForPhase</c> / 捕鸟陷阱同款）
    /// 只在本谓词为真时才结算，<see cref="RollsPerDay"/> 也由它数出来 ⇒ <b>触发点 / 每日期望 / 常量三处焊死同一条规则</b>，
    /// 不会各写各的。谁改这行，一天掷几次点就跟着变，期望产出的算式不会悄悄失真。</para>
    /// <para><b>为什么不是每个 <see cref="DayPhase"/> 都掷</b>：<see cref="DayPhase"/> 有 8 个值（含出行/探索/返程等中间相位），
    /// 但用户口中的"相位"指的是<b>昼夜段</b>（一天 2 次，同两顿聚餐）。早期误按 8 个 <see cref="DayPhase"/> 逐个掷点，
    /// 让陷阱产出<b>翻了 4 倍</b>（这正是"捕鸟陷阱太强"的根因）——已改回 2 次/天。</para>
    /// </summary>
    public static bool RollsOnPhase(DayPhase phase) => phase is DayPhase.DawnMeal or DayPhase.DuskMeal;

    /// <summary>陷阱一天掷几次点 = 满足 <see cref="RollsOnPhase"/> 的相位数 = <b>2</b>（白天 1 + 夜晚 1）。
    /// <b>每日期望</b>的换算系数（<c>ExpectedCatchesPerPhase(n) × RollsPerDay</c>）。<b>从谓词数出而非写死</b> ⇒ 与触发点焊死。</summary>
    public static int RollsPerDay => Enum.GetValues<DayPhase>().Count(RollsOnPhase);

    /// <summary>
    /// <b>场上第 <paramref name="ordinal"/> 个陷阱的单次捕获几率</b>（1-based）= <c>max(5%, 30% − 5%×(n−1))</c>。
    /// <para>30% / 25% / 20% / 15% / 10% / 5% / 5% / 5%… —— 第 6 个起撞到地板，此后<b>再多放也是 5%</b>。</para>
    /// <para><paramref name="ordinal"/> ≤ 0（没有这个陷阱）⇒ 0，<b>不白送基准几率</b>。</para>
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
    /// <paramref name="trapCount"/> 个陷阱在<b>一个相位</b>里的期望捕获数 = 前 n 项几率之和。
    /// <para>1 个 → 0.30；3 个 → 0.75；6 个 → 1.05；此后每多一个只 +0.05（边际收益被摁在地板上）。</para>
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
    /// <b>一个相位的捕猎结算</b>：场上 <paramref name="trapCount"/> 个陷阱各掷一次点，返回本相位抓到的猎物（材料键）。
    ///
    /// <para><b>掷点顺序（测试按此复现）</b>：逐个陷阱 —— 先掷<b>命中</b>点，<b>命中了才</b>再掷一次<b>物种</b>点。
    /// 空手的陷阱<b>不掷物种点</b>（不浪费随机流，也让 <see cref="SequenceRandomSource"/> 的算例写得干净）。</para>
    ///
    /// <para>没有陷阱（≤ 0）⇒ 直接空手而归，<b>一次点都不掷</b>。</para>
    /// </summary>
    /// <param name="trapCount">场上陷阱数（消费层数 <see cref="TrapSpec.IsTrapFurniture"/> 得来）。</param>
    /// <param name="rng">可注入随机源（项目铁律：测试用 <see cref="SequenceRandomSource"/> 复现）。</param>
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
                continue;   // 这个陷阱本相位空着——不掷物种点
            }
            caught.Add(rng.Range(0.0, 1.0) < RabbitShare ? RabbitKey : RatKey);
        }
        return caught;
    }
}

/// <summary>
/// <b>圈套陷阱的运行时编排（纯逻辑，可单测）</b> —— 镜像 <see cref="CropPlotRuntime"/> / <see cref="BirdTrapRuntime"/>：
/// 把"一个昼夜段掷点 → 把老鼠/兔子塞进库存"抽成一个不引 Godot 类型的纯函数，<b>让消费层（<c>CampMain.Traps.cs</c>）和单测调同一段代码</b>，
/// 杜绝消费层自己又写一遍 roll+入库的第二事实源。掷点频率不在这儿判——由 <see cref="RollsOnPhase"/> 在消费层 gate。
/// </summary>
public static class TrapRuntime
{
    /// <summary>
    /// <b>结算一个昼夜段</b>：场上 <paramref name="trapCount"/> 个圈套陷阱各掷一次点，捕到的老鼠/兔子<b>逐只入库</b>，返回本段捕到的猎物（材料键，可空）。
    /// <b>一个陷阱都没有就彻底静默</b>（<see cref="TrapLogic.RollPhase"/> 在 count≤0 时一次点都不掷）。
    /// </summary>
    public static IReadOnlyList<string> ResolveCatch(int trapCount, InventoryStore inventory, IRandomSource rng)
    {
        IReadOnlyList<string> caught = TrapLogic.RollPhase(trapCount, rng);
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
