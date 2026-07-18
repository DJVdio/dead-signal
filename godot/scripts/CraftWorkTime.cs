using System;
using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 CraftingLogic.cs / CraftingService.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。

/// <summary>
/// <b>制作工时的乘算轴</b> —— 《木匠入门》「制作家具速度 +5%」的落点，也是<b>今后一切"这活干得快/慢"</b>的唯一入口。
///
/// <para>═══ <b>项目里其实有两条工时轴，别混</b> ═══
/// <list type="number">
/// <item><b>总工时轴（本类）</b>：这张活<b>总共</b>要投多少工时。它是**下单那一刻**按"谁来做、做什么"一次算定的
///       （<see cref="CraftingService.StartJob"/> ⇒ <see cref="CraftingJob.TotalWorkMinutes"/>）。
///       书的加成挂在这儿：<b>木匠做家具，这活本身就更省工</b>。</item>
/// <item><b>推进轴（既有，不在本类）</b>：每流逝一分钟<b>投进去</b>多少工时 ——
///       操作能力 × 道格光环 × 疲劳 × 山姆光环，全程连乘（见 <c>CampMain.TickCraftingWorktime</c>）。
///       它按帧变动（人饿了、断了手、被拉去打仗），故不能烧进总工时。</item>
/// </list>
/// 两条轴<b>互不覆盖、天然连乘</b>：一个改"活有多大"，一个改"干得多快"。
/// 谁要加新的工时加成，先想清楚它是哪一条 —— <b>与人的状态有关的进推进轴，与活/手艺有关的进这儿</b>。
/// </para>
///
/// <para>═══ <b>乘算，禁止加算</b>（项目铁律，见 CLAUDE.md）═══
/// <see cref="MultiplierFor"/> 把每一条加成 <c>*=</c> 连乘。<b>《进阶木匠技术》那 5% 已经落地</b>
/// （见 <see cref="AdvancedCarpentryFurnitureMultiplier"/>）⇒ 两本都读过 = <c>0.95 × 0.95 = 0.9025</c>，
/// <b>不是</b>加算的 <c>1 − 0.05 − 0.05 = 0.90</c>。
/// 新加一条加成 = 在 <see cref="MultiplierFor"/> 里再乘一行，<b>绝不允许写成 <c>return</c> 覆盖前一条</b>。
/// </para>
/// </summary>
public static class CraftWorkTime
{
    /// <summary>
    /// 《木匠入门》做<b>家具</b>时的工时乘子（+5% 速度 ⇒ 工时 ×0.95）。
    /// 只对 <see cref="FurnitureRecipeIds"/> 那几张生效 —— 门槛与加成是两回事：这本书还解锁<b>回收木料</b>，
    /// 但那不是家具，木匠拆木头并不更快。
    /// <para>⚠️ [SPEC-B21·T26] 本书<b>名下已无任何弓弩</b>（用户把它清成纯家具书，见 <c>RecipeBook.CarpentryBasicsBookId</c>）
    /// —— 旧注释拿"这本书同时解锁自制弓"举例，那个例子已不存在。</para>
    /// </summary>
    public const double CarpentryFurnitureMultiplier = 0.95;

    /// <summary>
    /// 《进阶木匠技术》做<b>家具</b>时的工时乘子（同样 +5% 速度 ⇒ ×0.95）。
    /// <b>与《木匠入门》那条连乘</b>：两本都读过 = 0.95 × 0.95 = <b>0.9025</b>，不是加算的 0.90，也不是"后一条盖前一条"的 0.95。
    /// （进阶书的前置就是入门书，所以"只读进阶、没读入门"在正常流程里也读得极慢，但规则上仍各算各的。）
    /// </summary>
    public const double AdvancedCarpentryFurnitureMultiplier = 0.95;

    /// <summary>
    /// <b>「家具类配方」的唯一事实源</b>：板凳 / 木椅 / 床 / 桌子。
    ///
    /// <para>
    /// <b>怎么界定的</b>（两个条件同时成立，见 <see cref="IsFurnitureRecipe"/> 的护栏测试）：
    /// ① <see cref="RecipeCategory.Woodwork"/>（木工活 —— 木匠的手艺只对木工活有用）；
    /// ② 产物<b>真的会摆到营地地上</b>（是家具实体，不是材料堆）。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不直接拿 <see cref="FurnitureBuildCost"/> 那张目录当判据</b>（第一直觉，但是错的）：
    /// 那张表是"<b>拆得动的东西</b>"的目录，里头有<b>沙袋</b>（布 + 石料，往麻袋里铲土不是木工活）、
    /// 有<b>改装台 / 烹饪台</b>（固定锚点的大型设施，铁与石头的活），却<b>没有木椅和板凳</b>
    /// （它们是 <c>role="seat"</c> 的座位，不进那张表）—— 拿它当"家具"用，会让木匠铲沙袋快 5%、做椅子却不快。
    /// </para>
    ///
    /// <para><b>新增一件木家具时</b>：往这儿加一行 id 即可（护栏测试会替你核对它确实是 Woodwork + 有家具实体）。</para>
    /// </summary>
    public static readonly IReadOnlySet<string> FurnitureRecipeIds = new HashSet<string>
    {
        "bench",             // 板凳（无书门槛的最低档家具）
        "chair",             // 木椅
        SofaSpec.RecipeId,    // 沙发（升级座位）
        BedSpec.RecipeId,    // 床
        TableSpec.RecipeId,  // 桌子
    };

    /// <summary>这张配方算不算「家具」（吃不吃木匠的那 5%）。</summary>
    public static bool IsFurnitureRecipe(RecipeData recipe)
        => recipe is not null && FurnitureRecipeIds.Contains(recipe.Id);

    /// <summary>
    /// <b>这张活的工时乘子</b>（1.0 = 没有任何加成 ⇒ 与工时制上线那天逐分钟一致）。
    /// <para>加成一律 <c>*=</c> 连乘 —— 见类注的铁律。</para>
    /// </summary>
    /// <param name="recipe">配方。</param>
    /// <param name="isBookRead">"某书 id 是否已读"谓词（按<b>制作者本人</b>判，与配方的书门槛同一个谓词）。</param>
    public static double MultiplierFor(RecipeData recipe, Func<string, bool> isBookRead)
    {
        if (recipe is null || isBookRead is null)
        {
            return 1.0;
        }

        double mult = 1.0;

        if (IsFurnitureRecipe(recipe))
        {
            // 两本木工书各给 +5% 速度，**连乘**（都读过 ⇒ 0.9025）。别改成加算，也别写成 if/else 只取一本。
            if (isBookRead(RecipeBook.CarpentryBasicsBookId))
            {
                mult *= CarpentryFurnitureMultiplier;
            }
            if (isBookRead(RecipeBook.AdvancedCarpentryBookId))
            {
                mult *= AdvancedCarpentryFurnitureMultiplier;
            }
        }

        // ⚠️ 今后的工时加成在这里**继续 *= 连乘**（别 return 覆盖上面几条）。

        return mult;
    }

    /// <summary>
    /// <b>一件在制品的总工时</b>（游戏分钟）= ⌊配方工时 × 批量 × <see cref="MultiplierFor"/>⌋。
    ///
    /// <para>
    /// <b>向下取整</b>（同拆除返还的既有口径）：折扣的零头归玩家，不归系统。
    /// <b>先按批量放大再乘折扣</b>，不是"每件各自取整再相加" —— 后者会把零头吃掉好几次。
    /// <b>兜底 ≥1 分钟</b>：一张 1 分钟的活打完折不该变成"点一下就有"。
    /// </para>
    /// <para><b>零回归</b>：无加成时乘子恰为 1.0 ⇒ <c>⌊WorkMinutes × times × 1.0⌋</c> 与旧式 <c>WorkMinutes × times</c> 逐分钟相等。</para>
    /// </summary>
    public static int TotalMinutes(RecipeData recipe, Func<string, bool> isBookRead, int times = 1)
    {
        if (recipe is null)
        {
            return 0;
        }

        int mult = times < 1 ? 1 : times;
        int baseMinutes = recipe.WorkMinutes < 0 ? 0 : recipe.WorkMinutes;
        int total = baseMinutes * mult;
        if (total <= 0)
        {
            return 0;   // 零工时配方（旧调用点兜底）：打折还是零，别兜出个 1 来
        }

        int scaled = (int)Math.Floor(total * MultiplierFor(recipe, isBookRead));
        return scaled < 1 ? 1 : scaled;
    }

    /// <summary>
    /// 连乘一串工时乘子（<b>护栏</b>：把"加成必须连乘"这条铁律变成一个可测的函数，
    /// 免得日后有人在 <see cref="MultiplierFor"/> 里写成加算或覆盖 —— 见 <c>CarpentryWorkTimeTests</c>）。
    /// </summary>
    public static double Chain(params double[] multipliers)
    {
        double m = 1.0;
        foreach (double x in multipliers ?? Array.Empty<double>())
        {
            m *= x;
        }
        return m;
    }
}

/// <summary>
/// <b>玩家能自己往地上摆的东西</b> —— 库存面板「摆放」按钮的<b>唯一事实源</b>
/// （<c>StashPanel.AddPlaceButton</c> 与 <c>CampMain.OnStashPlaceRequested</c> 共读这一张表）。
///
/// <para>
/// ⚠️ <b>这张表是补一个真洞补出来的</b>：<b>床</b>此前<b>没有「摆放」按钮</b> —— <c>StashPanel</c> 里硬写着
/// <c>key == SandbagSpec.ItemKey</c>，而 <c>CampMain.OnStashPlaceRequested</c> 那边<b>已经</b>接好了床的分支。
/// 于是玩家造出来的床<b>躺在库存里摆不下去</b>（按钮压根不长出来），而代码看上去两边都"做完了"。
/// 一张表两处硬编码 = 迟早分叉；现在两处都问这一张表。
/// </para>
///
/// <para>
/// <b>固定锚点设施（改装台 / 烹饪台）刻意不在此列</b>：用户拍板它们造完自动立在车间/厨房、玩家挪不动，
/// 压根不进库存。给它们加回摆放按钮 = 把已经撤掉的 kill box 风险重新引进来（它们<b>实心、挖导航洞、不可跨越</b>）。
/// </para>
/// </summary>
public static class PlaceableItems
{
    private static readonly IReadOnlySet<string> Keys = new HashSet<string>
    {
        SandbagSpec.ItemKey,   // 沙袋：半身掩体，恒不挡路
        BedSpec.ItemKey,       // 床：养病，人要走上去躺下 ⇒ 非实心
        TableSpec.ItemKey,     // 桌子：纯家具，可跨越（跨过减速 25%）
        SofaSpec.ItemKey,      // 沙发：木椅升级档，非实心室内座位
        CropPlotSpec.ItemKey,  // [T72] 菜园：户外持久种植区（造→摆→种→收→拆），非实心可跨越
        TrapSpec.ItemKey,      // [T75] 圈套陷阱：户外贴地矮物（造→摆→每昼夜段掷点→收猎物→拆），非实心可跨越。**此前漏登记 ⇒ 摆放按钮不长出来，机制静默失效**
        BirdTrapSpec.ItemKey,  // [T75] 捕鸟陷阱：同上，纯逻辑早就在、消费层却整条未接 ⇒ 玩家根本摆不出来。这次一并接通
        ButcherStation.PointItemKey,  // [T67] 简易宰杀点：实心宰杀设施，配方产出一件 → 库存「摆放」→ 室内落位（升级为宰杀台走完工分流顶替，不进本表）。此前整条未接 ⇒ HasButcherPoint 恒 false、宰杀/羽毛/皮革链全断
    };

    /// <summary>库存里这件东西能不能「摆放」到地上。</summary>
    public static bool IsPlaceable(string? itemKey)
        => itemKey is { Length: > 0 } && Keys.Contains(itemKey);

    /// <summary>全部可摆放物的键（供 UI / 测试遍历）。</summary>
    public static IEnumerable<string> All => Keys;
}
