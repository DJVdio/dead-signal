using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 CraftingLogic.cs / Recipe.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 拆除回收的判定与结算核心（纯函数）：给定"建造成本"，算出"拆了返还什么、各多少"。
// **本块不碰库存**——实扣实产由 SalvageService 去 InventoryStore 做（同 CraftingLogic/CraftingService 的分工）。

/// <summary>
/// 拆除回收规则（用户拍板，当前返还比例与配方产出以 Wiki 配置为准）：
/// <list type="number">
/// <item>拆除一件东西按 Wiki 配置返还建造材料。</item>
/// <item><b>木材例外</b>：返还拆分为木料与废木料两条产出，比例以 Wiki 配置为准。</item>
/// <item>废木料与胶水在装了锯片的工作台上可按 Wiki 配方做回木料（见 <see cref="ScrapWoodRecipeId"/>）。</item>
/// </list>
/// <para>
/// <b>三条合起来才是完整设计</b>：木材表面分流为木料与废木料，绕一趟"废木料 + 胶水"可补回部分木料——
/// 但要额外付胶水。⇒ 胶水是瓶颈资源，木材的完整回收要交「胶水税」：
/// 手里这点胶水，是拿去把废木料粘回木料，还是留着干别的（胶水吃燃料，而燃料同时是火把/发电机/火药/全部枪弹的命根子）？
/// </para>
/// <para>
/// <b>取整一律向下</b>（<see cref="Refund"/>）：返还不会超过配置比例 ⇒ 造→拆→造永远净亏，
/// <b>不存在无限刷</b>。小件拆了归零是有意的下限——拆小东西不划算。
/// </para>
/// </summary>
public static class SalvageLogic
{
    /// <summary>木料材料键（<see cref="Materials"/> 目录）。它是唯一走"例外规则"的材料。</summary>
    public const string WoodKey = "wood";

    /// <summary>废木料材料键：拆木结构掉出的碎料，本身盖不了东西，得先粘回木料。</summary>
    public const string ScrapWoodKey = "scrap_wood";

    /// <summary>胶水材料键：粘废木料的唯一途径，木材完整回收的"税"。</summary>
    public const string GlueKey = "glue";

    /// <summary>「回收木料」配方 id（投入、产出与工作台门槛以 Wiki 配置为准，需锯片工作台）。</summary>
    public const string ScrapWoodRecipeId = "wood_from_scrap";

    /// <summary>「胶水」配方 id（自己熬的和搜刮来的是同一样东西，见 <see cref="RecipeBook"/>）。</summary>
    public const string GlueRecipeId = "glue";

    /// <summary>通用返还率：当前值以 Wiki 配置为准。</summary>
    public const double RefundRate = 0.50;

    /// <summary>木材例外：直接还回木料的比例以 Wiki 配置为准。</summary>
    public const double WoodDirectRate = 0.25;

    /// <summary>木材例外：转为废木料的比例以 Wiki 配置为准（要花胶水才粘得回来）。</summary>
    public const double ScrapWoodRate = 0.25;

    /// <summary>拆解工时按建造工时比例计算；当前比例以 Wiki 配置为准——但**不是白拆**。</summary>
    public const double WorkMinutesRate = 0.50;

    /// <summary>拆解工时有 Wiki 配置的下限：再小的东西也得动手拆一会儿，不许"点击即得"。</summary>
    public const int MinWorkMinutes = 5;

    /// <summary>按比例返还并**向下取整**（这是"绝不套利"的那把锁）。</summary>
    private static int Refund(int cost, double rate) => (int)Math.Floor(cost * rate);

    /// <summary>
    /// 给定一份建造成本（材料键 → 数量），算出拆除的返还表。
    /// 通用材料按 Wiki 返还率处理；<see cref="WoodKey"/> 走例外，分流为木料与 <see cref="ScrapWoodKey"/>。
    /// 返还为 0 的材料**不出现在结果里**（一根钉子的一半是没有）。
    /// </summary>
    public static IReadOnlyDictionary<string, int> YieldOf(IReadOnlyDictionary<string, int> buildCost)
    {
        if (buildCost is null) throw new ArgumentNullException(nameof(buildCost));

        var yield = new Dictionary<string, int>();
        foreach (KeyValuePair<string, int> kv in buildCost)
        {
            if (kv.Value <= 0)
            {
                continue;
            }

            if (kv.Key == WoodKey)
            {
                // 木材例外：一半的一半还是木料，另一半的一半成了碎料。
                Give(yield, WoodKey, Refund(kv.Value, WoodDirectRate));
                Give(yield, ScrapWoodKey, Refund(kv.Value, ScrapWoodRate));
            }
            else
            {
                Give(yield, kv.Key, Refund(kv.Value, RefundRate));
            }
        }
        return yield;
    }

    private static void Give(Dictionary<string, int> yield, string key, int amount)
    {
        if (amount <= 0)
        {
            return;
        }
        yield[key] = yield.TryGetValue(key, out int had) ? had + amount : amount;
    }

    // ======================== 墙：不可拆（用户拍板，三条一起看）========================
    //
    // ① **墙不能建**——玩家不能新建围墙，不能自由划线布局。
    // ② **只能升级开局自带的围栏**（基础围栏 → 加固 → 铁皮 → 全金属；成本见 StructureBuildCost）。
    // ③ **墙不可拆，只能砸**（走破防那条路：BreachLogic / StructureDamage）——**零回收**。
    //
    // ⚠️ **①「不能建」的理由不是"没做"，是刻意的设计防御**：可自由摆墙 ⇒ 玩家能搭 kill box
    //（用墙的迷宫牵着敌人寻路，把防御变成一道几何题），会**架空视野锥 / 噪音 / 包抄 / 掩体 / 岗哨**
    // 这一整套系统。「没有墙可摆」是个终局解法，比事后打补丁修寻路高明——寻路 bug 修不完。
    // **不要"好心"把建墙加回来**，也不要为了体验友好给墙开"拆一半回来"的后门。
    //
    // ③「不可拆」的设计意涵：**围墙是不可逆的投入**。建错了位置 ⇒ 只能砸掉，材料一点回不来
    // ⇒ 选址是一个必须想清楚的决策，而不是"先建了再说，反正能拆"。

    /// <summary>
    /// 这处结构拆得动吗？<b>围栏一律拆不动</b>（只能砸，零回收——见上方注释块）；
    /// <b>门与大门可拆</b>（它们是装上去的东西，卸得下来）。
    /// </summary>
    public static bool CanSalvageStructure(StructureTier tier)
        => CampStructureTable.KindOf(tier) != CampStructureKind.Fence;

    /// <summary>
    /// 拆一处营地结构的返还表——按 <see cref="StructureBuildCost"/> 的建造成本折半。
    /// <b>围栏返回空表</b>（拆不动，只能砸；调用方应先问 <see cref="CanSalvageStructure"/>）。
    /// </summary>
    public static IReadOnlyDictionary<string, int> YieldOfStructure(StructureTier tier)
        => CanSalvageStructure(tier)
            ? YieldOf(StructureBuildCost.Of(tier))
            : new Dictionary<string, int>();

    // ======================== 家具（camp.json 的 props：工作台/柜子/床…）========================

    /// <summary>
    /// 拆一件营地家具的返还表（按 <see cref="FurnitureBuildCost"/> 的建造成本折半）。
    /// 目录里没有的家具（收音机、废墟、尸体这类不是"造出来"的东西）返回空表。
    /// </summary>
    public static IReadOnlyDictionary<string, int> YieldOfFurniture(string furnitureKey)
        => FurnitureBuildCost.Of(furnitureKey) is { } cost ? YieldOf(cost) : new Dictionary<string, int>();

    /// <summary>这件家具拆得动吗（在 <see cref="FurnitureBuildCost"/> 目录里即可拆）。</summary>
    public static bool CanSalvageFurniture(string furnitureKey)
        => FurnitureBuildCost.Of(furnitureKey) is not null;

    /// <summary>拆一件配方产物的返还表。</summary>
    public static IReadOnlyDictionary<string, int> YieldOfRecipe(RecipeData recipe)
        => YieldOf((recipe ?? throw new ArgumentNullException(nameof(recipe))).MaterialCosts);

    // ======================== 可拆判定 ========================

    /// <summary>
    /// 这张配方的产物拆得动吗？<b>只有单件产物可拆，判定依据是 <see cref="RecipeData.OutputQuantity"/>。</b>
    /// <para>
    /// 堆叠产物一律不可拆（子弹/箭/药茶）：若单件拆分仍按整份成本返还，
    /// 反复拆解就会形成套利口子。单件产物没有这个问题（返还受 Wiki 配置与取整规则约束）。
    /// </para>
    /// </summary>
    public static bool CanSalvage(RecipeData recipe)
        => recipe is not null && recipe.OutputQuantity == 1;

    /// <summary>
    /// 某个物品键（武器名/护甲名/家具键）对应的**建造配方**；没有配方 ⇒ <c>null</c>。
    /// 搜刮来的军用枪、碳纤维箭、牛仔外套都没有配方——没有"建造成本"可依，自然拆不出东西。
    /// </summary>
    public static RecipeData? RecipeFor(string itemKey)
    {
        if (string.IsNullOrEmpty(itemKey))
        {
            return null;
        }

        foreach (RecipeData r in RecipeBook.All)
        {
            if (r.OutputKey == itemKey && CanSalvage(r))
            {
                return r;
            }
        }
        return null;
    }

    /// <summary>该物品键拆得动吗（有单件产物配方即可拆）。</summary>
    public static bool CanSalvageKey(string itemKey) => RecipeFor(itemKey) is not null;

    // ======================== 拆解工时 ========================

    /// <summary>建造工时 → 拆解工时（一半，下限 <see cref="MinWorkMinutes"/>）。</summary>
    public static int WorkMinutesForBuildMinutes(int buildMinutes)
        => Math.Max(
            MinWorkMinutes,
            (int)Math.Round(Math.Max(0, buildMinutes) * WorkMinutesRate, MidpointRounding.AwayFromZero));

    /// <summary>拆一件配方产物要花的工时（游戏分钟）。</summary>
    public static int WorkMinutesOf(RecipeData recipe)
        => WorkMinutesForBuildMinutes((recipe ?? throw new ArgumentNullException(nameof(recipe))).WorkMinutes);

    /// <summary>拆一处结构要花的工时（游戏分钟）。围栏拆不动，故返回 0。</summary>
    public static int WorkMinutesOfStructure(StructureTier tier)
        => CanSalvageStructure(tier)
            ? WorkMinutesForBuildMinutes(StructureBuildCost.BuildMinutes(tier))
            : 0;

    /// <summary>拆一件家具要花的工时（游戏分钟）；不在目录里的家具返回 0。</summary>
    public static int WorkMinutesOfFurniture(string furnitureKey)
        => FurnitureBuildCost.BuildMinutes(furnitureKey) is { } minutes
            ? WorkMinutesForBuildMinutes(minutes)
            : 0;

    // ======================== 工时队列复用（拆解与制作共用一条在制任务槽）========================
    //
    // 拆解**不另起一条工时队列**：它借 CraftingJob 表达（任务 id = "salvage:<物品键>"）。
    // 这样一来，工人在不在台、被袭营拉走、操作能力/疲劳/光环系数、进度推进与中断续作——整条链路原样复用；
    // 而且拆解与制作**天然互斥**（一座工作台一次只干一件事），这本就是对的语义。

    /// <summary>拆解任务的 id 前缀（区别于配方 id：<c>RecipeBook.Find</c> 查不到它，调用方据此分流到拆解结算）。</summary>
    public const string JobIdPrefix = "salvage:";

    /// <summary>把物品键包成一条拆解任务 id（喂 <see cref="CraftingJob"/>）。</summary>
    public static string JobIdFor(string itemKey) => JobIdPrefix + itemKey;

    /// <summary>这条在制任务是拆解（而非制作）吗。</summary>
    public static bool IsSalvageJob(string jobId)
        => jobId is not null && jobId.StartsWith(JobIdPrefix, StringComparison.Ordinal);

    /// <summary>从拆解任务 id 取回在拆之物的目标串；不是拆解任务则 <c>null</c>。</summary>
    public static string? TargetKeyOf(string jobId)
        => IsSalvageJob(jobId) ? jobId.Substring(JobIdPrefix.Length) : null;

    // 目标串的三种形态（调用方据此分流到三条清场路径）：
    //   "door#<结构下标>" = 门 / 大门（**围栏永远不会出现在这里**——墙不可拆）
    //   "prop#<家具名>"   = 营地家具（工作台 / 柜子…）
    //   其余              = 库存里的一件物品（武器 / 护甲 / 家具产物…）

    /// <summary>门/大门的目标串前缀。</summary>
    public const string StructureTargetPrefix = "door#";

    /// <summary>营地家具的目标串前缀。</summary>
    public const string FurnitureTargetPrefix = "prop#";

    /// <summary>目标串是门吗？是则给出结构下标；否则 <c>null</c>。</summary>
    public static int? StructureIndexOf(string target)
        => target is not null
            && target.StartsWith(StructureTargetPrefix, StringComparison.Ordinal)
            && int.TryParse(target.Substring(StructureTargetPrefix.Length), out int index)
            ? index
            : null;

    /// <summary>目标串是家具吗？是则给出家具名；否则 <c>null</c>。</summary>
    public static string? FurnitureNameOf(string target)
        => target is not null && target.StartsWith(FurnitureTargetPrefix, StringComparison.Ordinal)
            ? target.Substring(FurnitureTargetPrefix.Length)
            : null;
}
