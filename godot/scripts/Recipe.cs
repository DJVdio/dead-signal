using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 Workbench.cs / Materials.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 配方数据模型：配方 = 材料成本 + 需要工具槽 + 制作者解锁条件（读完的书）→ 产物。
// 通用技能系统已删——能力改由"每角色 authored 专属效果"与"读过的书"承载，配方门槛只看 工具/书/材料。
// 本文件只放**数据**（RecipeData 值对象 + RecipeBook 草稿表）；能不能做（CanCraft）与产出结算（Resolve）在 CraftingLogic。
// 材料/产物一律用**字符串 RefKey**（对齐 Materials 目录 / Item RefKey），不硬依赖枚举，解耦并发。

/// <summary>配方类别（供 UI 分组；与解锁工具大致对应，但工具门槛以 <see cref="RecipeData.RequiredTools"/> 为准）。</summary>
public enum RecipeCategory
{
    /// <summary>木工（锯片类，如椅子）。</summary>
    Woodwork,

    /// <summary>精工/弓弩（卡尺类，如自制弓）。</summary>
    Precision,

    /// <summary>化学（烧杯类，如火药、鞣制药水）。</summary>
    Chemistry,

    /// <summary>缝纫/纺织（如粗布背心）。</summary>
    Tailoring,

    /// <summary>杂项/工具（如骨刀）。</summary>
    Misc,
}

/// <summary>
/// 一张配方的不可变定义。产物由 <see cref="OutputKey"/>（Item RefKey）+ <see cref="OutputQuantity"/> 描述；
/// 解锁条件按**制作者**判定：需读完 <see cref="RequiredBookIds"/> 的全部书；
/// 且工作台已装 <see cref="RequiredTools"/> 的全部工具槽；且库存够付 <see cref="MaterialCosts"/>。
/// 通用技能门槛已删——配方只看 工具/书/材料 三类门槛。
/// <see cref="WorkMinutes"/>=夜间工时制的每配方工时（游戏分钟，拟定待调）：制作不再"点击即得"，
/// 而是下单入队后人在工作台逐段推进（见 <see cref="CraftingJob"/>）。默认 30 分（旧调用点/临时配方兜底）。
/// <see cref="RequiredCrafterGates"/>=**制作者门槛键**（如"道格且羁绊≥2 级"才能做的狗装备）：书门槛之外的另一类
/// 与制作者身份/剧情态挂钩的解锁，判定委托调用方（见 <see cref="CraftingLogic.CanCraft"/> 的 crafterGate 参数）。null/空＝无此类门槛。
/// </summary>
public sealed record RecipeData(
    string Id,
    string DisplayName,
    RecipeCategory Category,
    string OutputKey,
    int OutputQuantity,
    IReadOnlyDictionary<string, int> MaterialCosts,
    IReadOnlySet<ToolSlot> RequiredTools,
    IReadOnlyList<string> RequiredBookIds,
    int WorkMinutes = 30,
    IReadOnlyList<string>? RequiredCrafterGates = null);

/// <summary>
/// 内置配方表（**拟定草稿 draft**，工具/条件/材料数值用户后续调）。覆盖用户给的 6 个例子：
/// 骨刀 / 粗布背心 / 椅子 / 火药 / 鞣制药水 / 自制弓，各按拍板的"defining 条件"填工具槽 + 解锁 + 拟定材料。
/// 材料 RefKey 对齐 <see cref="Materials"/> 目录；产物 RefKey 为拟定名（gunpowder / tanning_solution 与材料同名=化学产出）。
/// </summary>
public static class RecipeBook
{
    /// <summary>
    /// 《野外生存指南》书 id（对齐 <see cref="BookLibrary.WildernessSurvivalGuide"/>）。
    /// <para>
    /// <b>解锁：骨刀 / 短弓 / 圈套陷阱 / 战争面具</b>（[SPEC-B21·T26] 按用户 wiki 书籍表重排）。
    /// 另有被动：不投任何手术耗材时手术加成点数 <b>+3</b>（[T68] 用户手改，原 +6；见 <c>HealthConditions</c>，非配方门槛）。
    /// </para>
    /// <para>
    /// <b>它是开局营地就有的书</b>（camp.json 的柜子里，同《裁缝手记》《土法化学笔记》《农场主的一百个问题》）——
    /// 这决定了挂在它名下的东西<b>开局读完书就能做</b>。对照：《木匠入门》/《进阶木匠技术》/《弓与箭之道》
    /// 都要出门搜刮（<c>ExplorationCache</c>）。往这本书上挂配方＝<b>放宽</b>，往那三本上挂＝<b>收紧</b>，别弄反。
    /// </para>
    /// <para>
    /// ⚠️ 用户表里还写着「削减木箭」（＝削尖的木箭 <c>ammo_arrow_stick</c>）——<b>暂未落地</b>：
    /// 它是全项目<b>唯一一条零门槛配方</b>，加书门槛是真实的开局节奏改动，已 [DECISION] 上抛待拍板。
    /// </para>
    /// </summary>
    public const string WildernessSurvivalGuideBookId = "wilderness_survival_guide";

    /// <summary>《裁缝手记》纺织书 id（对齐 <see cref="BookLibrary.TailorsNotes"/>）。粗布背心解锁读它。</summary>
    public const string TailorsNotesBookId = "tailors_notes";

    /// <summary>《土法化学笔记》化学书 id（对齐 <see cref="BookLibrary.FolkChemistryNotes"/>）。火药 / 鞣制药水解锁读它。</summary>
    public const string FolkChemistryNotesBookId = "folk_chemistry_notes";

    /// <summary>
    /// 《木匠入门》木工书 id（对齐 <see cref="BookLibrary.CarpentryBasics"/>）。<b>要出门搜刮</b>（<c>ExplorationCache</c>，商人也卖）。
    /// <para>
    /// <b>解锁：木椅 / 床 / 桌子 / 回收木料</b>——[SPEC-B21·T26] 用户把弓弩全搬走后，它<b>成了一本纯家具书</b>
    /// （另有被动：制作家具速度 +5%，见 <c>CraftWorkTime</c>）。<b>这是用户有意的</b>，不是被掏空的事故。
    /// </para>
    /// <para>
    /// 搬走的去向：<b>短弓 → 《野外生存指南》</b>（开局就有的书）；<b>反曲弓 / 长弓 → 《弓制作指南》</b>（要搜刮；
    /// [T59] 用户在 wiki 上把"造弓"从木工书里拆成单独一本，见 <see cref="BowCraftingGuideBookId"/>）；
    /// <b>单手轻弩 / 双手重弩 → 《机械之美》</b>（用户拍板的新书，见 <see cref="MechanicalBeautyBookId"/>）。
    /// </para>
    /// <para>
    /// ⇒ <b>本书现在真的只剩家具了</b>（弓弩一把不剩）。这是用户有意为之的终态，别再往里塞武器。
    /// </para>
    /// </summary>
    public const string CarpentryBasicsBookId = "carpentry_basics";

    /// <summary>
    /// 《进阶木匠技术》木工进阶书 id（对齐 <see cref="BookLibrary.AdvancedCarpentry"/>；前置＝《木匠入门》）。<b>只能搜刮</b>（<c>ExplorationCache</c>）。
    /// <para>
    /// <b>解锁：消防斧</b>（[批次25·T44]；「消防斧 + 造消防斧的书同馆」＝联合收割机仓库，见本表 axe 那条）。
    /// 另有被动两项：做家具<b>再快 5%</b>（与《木匠入门》那 5% <b>连乘</b>：两本都读过 = 0.95 × 0.95 = 0.9025，
    /// 见 <see cref="CraftWorkTime"/>）；<b>持消防斧攻速 +8%</b>（见 <c>MeleeBookEffect</c>）。
    /// </para>
    /// <para>
    /// ⚠️ [T59] <b>反曲弓 / 长弓 已从本书搬去《弓制作指南》</b>（见 <see cref="BowCraftingGuideBookId"/>）
    /// ⇒ <b>本书名下不再有任何弓弩</b>，它现在是"木工进阶书 + 斧头书"。
    /// 护栏：<c>CraftingTests.弓弩的阶梯_短弓开局书_进阶弓搜书_弩要机械之美</c> 断言两把弓 <b>不含</b>本书 id。
    /// </para>
    /// </summary>
    public const string AdvancedCarpentryBookId = "advanced_carpentry";

    /// <summary>
    /// 《弓制作指南》书 id（[T59] 用户在 wiki 上新加的书）。**造弓的书**。
    /// <para>🔴 <b>反曲弓 / 长弓 从《进阶木匠技术》挪到了这本。</b>
    /// <b>消防斧没挪</b>——它是木工工具，且「消防斧 + 造消防斧的书同馆」（联合收割机仓库）是 impl-axe/impl-worldgraph
    /// 依赖的既有设计，挪走会把它拆掉。</para>
    /// </summary>
    public const string BowCraftingGuideBookId = BookLibrary.BowCraftingGuideId;

    /// <summary>[T71]《尖峰时刻》书 id（对齐 <see cref="BookLibrary.PeakHourId"/>）——解锁 <c>snow_goggles</c>（自制简易墨镜）。</summary>
    public const string PeakHourBookId = BookLibrary.PeakHourId;

    /// <summary>《尖峰时刻·二》书 id——解锁厚重裤子与厚重披风。</summary>
    public const string PeakHourTwoBookId = BookLibrary.PeakHourTwoId;

    /// <summary>《尖峰时刻·三》书 id——解锁雪地靴与葛根/大黄的野外识别。</summary>
    public const string PeakHourThreeBookId = BookLibrary.PeakHourThreeId;

    /// <summary>
    /// 《弓与箭之道》书 id（对齐 <see cref="BookLibrary.WayOfBowAndArrowId"/>）。<b>只能搜刮</b>（<c>ExplorationCache</c>）。
    /// <para>
    /// <b>解锁：自制箭</b>（[SPEC-B21·T26] 用户拍板 —— 此前本书<b>一条配方都不解锁</b>，只有被动）。
    /// 另有被动四项：箭矢回收率 25% → 50%、<b>弹道速度 +20%</b>、锥形角 −10%、攻速 +2%
    /// （数值真源 <c>godot/data/config/archery.json</c>；<b>原「射程 +10%」已被弹道速度取代</b>，
    /// 该文件里 <c>BookRangeMult</c> 现为 1＝射程无加成，见 <c>Archery</c>）。
    /// </para>
    /// <para>
    /// ⚠️ <b>重头箭用户没提 ⇒ 没动</b>（仍是零书门槛，只要卡尺）。别顺手"统一"成"好箭都归这本书"——那是引申。
    /// </para>
    /// </summary>
    public const string WayOfBowBookId = BookLibrary.WayOfBowAndArrowId;

    /// <summary>
    /// 《机械之美》书 id（对齐 <see cref="BookLibrary.MechanicalBeautyId"/>）。<b>书名是用户给的</b>
    /// （原话：「《机械之美》用武器零件造」），正文待用户 authored。
    /// <para>
    /// <b>解锁：单手轻弩 / 双手重弩</b>（[SPEC-B21·T26] 从《木匠入门》挪来）。弩机是机械活，
    /// 两条配方的 defining 材料是<b>武器零件</b>（<c>weapon_parts</c>，轻弩 2 / 重弩 3）—— 正好呼应书名
    /// （用户原话「《机械之美》用武器零件造」；<b>不是「机械零件」</b>，两者是用户特意要的区分，见本表 light_crossbow 那条）。
    /// </para>
    /// <para>
    /// <b>投放点：联合收割机仓库·收割机驾驶室</b>（[T31] 用户拍板，<b>全局唯一一本</b>；
    /// 真源与挪动理由见 <c>ExplorationCache.MechanicalBeautyBookId</c> 的注释）。两把弩<b>搜刮不到、只能造</b>
    /// ⇒ <b>找不到这本书就没有弩</b>。投放点<b>拟定待调</b>（用户可在 wiki 上改）。
    /// </para>
    /// </summary>
    public const string MechanicalBeautyBookId = BookLibrary.MechanicalBeautyId;

    /// <summary>
    /// 狗装备制作者门槛键（批次5）：满足＝**制作者是道格且与布鲁斯羁绊≥2 级**（消费 <see cref="DougBruceBond.CanCraftDogGear"/>）。
    /// 五件狗装备配方均带此门槛（<see cref="RecipeData.RequiredCrafterGates"/>）；判定委托营地层（见 CampMain 制作接线）。
    /// </summary>
    public const string DogGearCrafterGate = "doug_bond_l2";

    /// <summary>
    /// 改装台「一台就够」门槛键（批次21·T7）：满足＝**营地里还没有改装台**。营地已有一台时本配方灰掉，
    /// 免得玩家把材料喂给第二台毫无用处的案子。判定委托营地层（见 CampMain 制作接线的 crafterGate）。
    /// </summary>
    public const string ModBenchAbsentGate = "mod_bench_absent";

    /// <summary>
    /// [T67] 《农场主的一百个问题》书 id（对齐 <see cref="BookLibrary.FarmerHundredQuestions"/>）。
    /// <para>
    /// 🔴 <b>它名下的两条配方＝【捕鸟陷阱】与【菜园】</b>（此前这本书的 <c>grantsRecipeStub</c> 只是个桩，
    /// 一条配方都不解锁）。
    /// </para>
    /// <para>
    /// ⚠️ <b>它和《野外生存指南》一样，开局就在营地共享库存里</b>（<c>camp.json</c> 住宅·柜子，两本同架）
    /// ⇒ 往它名下挂配方是<b>放宽</b>，不是收紧。但它<b>确实新增了一道"要花 4 小时读书"的闸</b>：
    /// <b>没读它 ⇒ 没有捕鸟陷阱 ⇒ 没有鸟 ⇒ 没有羽毛 ⇒ 一支箭都造不出来。</b>
    /// 于是"开局先读哪本"成了真选择：<b>《野外生存指南》给你弓，《农场主》给你箭</b>，两本都读 8 小时。
    /// </para>
    /// </summary>
    public const string FarmerHundredQuestionsBookId = "farmer_hundred_questions";

    /// <summary>
    /// [T67] <b>烹饪台"在场"门槛键</b>（用户："<b>蒲公英茶和玫瑰果茶应该在烹饪台制作</b>"）。
    /// 满足＝<b>营地里已经有一座烹饪台</b>。
    /// <para>
    /// ⚠️ 注意它与 <see cref="CookStation.AbsentGate"/> <b>方向相反</b>：那个是"还没有烹饪台"（防重复建造），
    /// 这个是"<b>已经有</b>烹饪台"（茶得在灶上煮）。两者都走既有的 <see cref="RecipeData.RequiredCrafterGates"/>
    /// 机制 —— <b>没有为"在某个设施上制作"新开引擎轴</b>，判定仍委托营地层的 gate 解析。
    /// </para>
    /// <para>
    /// 🔴 <b>茶不吃热量点</b>：它走的是<b>配方系统</b>（<see cref="RecipeBook"/> → <see cref="CraftingLogic"/>），
    /// 不是<b>做饭系统</b>（<see cref="CookingLogic"/> 的 16 点/份、锅 −2、烤架 −2）。
    /// 这两套本来就是分开的——烹饪台在这里只是一个<b>"你得站在灶边才能煮"的门槛</b>，不是一台把材料变成"份数"的机器。
    /// ⇒ <b>烹饪系统一行都不用改</b>，茶也不会变成饭。它仍是药（<see cref="MedicineCatalog"/>）。
    /// </para>
    /// </summary>
    public const string CookStationPresentGate = "cook_station_present";

    private static IReadOnlySet<ToolSlot> Tools(params ToolSlot[] slots) => new HashSet<ToolSlot>(slots);
    private static IReadOnlyList<string> Books(params string[] ids) => new List<string>(ids);

    /// <summary>
    /// 配方工厂：<b>结构写在代码</b>（id/显示名/类别/产物键/工具/书/制作者门槛），<b>数字读 recipes.json</b>
    /// （产量/工时/材料成本，经 <see cref="RecipeConfig.Get"/> 缺 id fail-fast）。数值真源从此在 json、不在代码。
    /// </summary>
    private static RecipeData R(
        string id,
        string displayName,
        RecipeCategory category,
        string outputKey,
        IReadOnlySet<ToolSlot> requiredTools,
        IReadOnlyList<string> requiredBookIds,
        IReadOnlyList<string>? requiredCrafterGates = null)
    {
        RecipeNumbers n = GameConfigCatalog.Section<RecipeConfig>().Get(id);
        return new RecipeData(
            Id: id,
            DisplayName: displayName,
            Category: category,
            OutputKey: outputKey,
            OutputQuantity: n.OutputQuantity,
            MaterialCosts: n.MaterialCosts,
            RequiredTools: requiredTools,
            RequiredBookIds: requiredBookIds,
            WorkMinutes: n.WorkMinutes,
            RequiredCrafterGates: requiredCrafterGates);
    }

    // draft：以下配方的工具/书/制作者门槛写死为结构；产量/工时/材料成本外置 recipes.json（可调数值）。
    private static readonly IReadOnlyList<RecipeData> _all = new[]
    {
        // 骨刀：读完《野外生存指南》解锁；削骨即成，无需工具台工具。
        R("bone_knife", "骨刀", RecipeCategory.Misc, "bone_knife", Tools(), Books(WildernessSurvivalGuideBookId)),

        // 粗布背心：读过《裁缝手记》解锁；缝制布甲。
        R("cloth_vest", "粗布背心", RecipeCategory.Tailoring, "cloth_vest", Tools(), Books(TailorsNotesBookId)),

        // 布夹克：同读《裁缝手记》解锁；比粗布背心多两条袖子（覆盖双臂）、料更足，故材料/工时都翻一档（拟定待调）。
        // 牛仔外套不可制作——厚牛仔布不在材料表，只能靠搜刮（见 camp.json 住宅-衣柜）。
        R("cloth_jacket", "布夹克", RecipeCategory.Tailoring, "cloth_jacket", Tools(), Books(TailorsNotesBookId)),

        // 板凳（低级椅）：家具梯度最低档——无书门槛、无工具槽，**人人可造、开局即可做**（用户拍板：开局可做板凳，不必做中级木椅）。
        // 材料是木椅的打折版（仅 wood 2、去 nails，拟定待调）。产物 key 不在武器/护甲/材料集 → 走 CraftOutputFactory 家具/杂项分支落地。
        // 座位功能**暂同木椅**（低配版，读书座位无差异）；板凳/木椅/沙发的档次差异（耐久/舒适/加成）待设计。
        R("bench", "板凳", RecipeCategory.Woodwork, "bench", Tools(), Books()),

        // 椅子：锯片类木工 + 读过《木匠入门》解锁（用户拍板：木椅/自制弓也要读木工书）。
        R("chair", "木椅", RecipeCategory.Woodwork, "chair", Tools(ToolSlot.SawBlade), Books(CarpentryBasicsBookId)),

        // ── [批次21·impl-bedrest] 床：养病的物质基础 ──
        // 开局只有 2 张（camp.json 的 床#1/床#2），**第三张起要自己造**。床位稀缺是有意的：
        // 一张床只躺一个人，躺着的那个不站岗不生产（见 BedrestLogic）——玩家得反复决定"这张床今晚归谁"。
        // 走**沙袋那条链**（产出材料 → 库存「摆放」→ 左键落位），不发明新的建造范式。
        // 材料：木料 12（床架）+ 布 4（褥子）+ 钉子 6。比木椅重得多——它是个能让人躺平的大件。
        // 门槛同木椅：锯片 + 读过《木匠入门》（用户拍板"木椅/自制弓也要读木工书"，床是更大的木工活，同理）。
        // 工时 150 分。数值全部拟定待调。拆除走通用规则（SalvageLogic 50% 向下取整 ⇒ 木料 6 + 布 2 + 钉子 3）。
        R("bed", "床", RecipeCategory.Woodwork, "bed", Tools(ToolSlot.SawBlade), Books(CarpentryBasicsBookId)),

        // ── [批次21·T25] 桌子：用户在书籍表里点名的那张配方（《木匠入门》解锁"木椅、床、桌子、废木料回收"）──
        // ⚠️ **桌子目前没有任何玩法作用**：营地里没有"桌子"这个概念（聚餐是模态相位，不需要一张桌子；
        // camp.json 里也没有桌子这件 prop），本作又**没有心情系统** ⇒ 不许给它编一个"吃饭+心情"出来。
        // 它现在是一件**纯家具**：可造、可摆、**可跨越（跨过减速 25%）**、可拆。用处待用户定，见 TableSpec 类注。
        // 材料：木 8（一块面 + 四条腿）+ 钉 4。工时 120 分——比木椅（150）省事：一张平板不用弯靠背。
        // 门槛同木椅/床：锯片 + 读过《木匠入门》。数值全部拟定待调。
        // 读过《木匠入门》的人做它只要 ⌊120 × 0.95⌋ = 114 分（CraftWorkTime 的家具工时轴）。
        R(TableSpec.RecipeId, "桌子", RecipeCategory.Woodwork, TableSpec.ItemKey, Tools(ToolSlot.SawBlade), Books(CarpentryBasicsBookId)),

        // ── [批次20·掩体] 沙袋：**用户拍板"可自由建造摆放"**——项目里第一件玩家能自己往地上摆的防御工事 ──
        // 为什么沙袋能建而**墙不能建**（"墙不能建"是用户为防 kill box 拍的板，别以为规则不一致而"统一"掉）：
        // 沙袋**不阻挡移动、不改变寻路**（SandbagSpec.IsSolid/CarvesNavHole 恒 false）⇒ 敌人照样直线冲过来、
        // 不会被墙的迷宫牵着绕 ⇒ **摆不出 kill box**；它只给 25% 远程无伤，**而且敌人也能蹲在你的沙袋后面用**
        // （CoverLogic 双向对称）。玩家能经营防御位置，但摆不出必胜阵型。完整论证见 SandbagSpec 的类注。
        // 材料：布 2（袋子）+ 石料 4（往里装的东西）。**无书无工具门槛**——往麻袋里铲土不是手艺活，
        // 开局第一夜就该能垒起来。工时 30 分：一个人铲一垛的量。全部数值拟定待调。
        // 拆除走通用规则（SalvageLogic：50% 向下取整 ⇒ 布 1 + 石料 2），不设特例。
        R("sandbag", "沙袋", RecipeCategory.Misc, "sandbag", Tools(), Books()),

        // ── [批次20·拆除回收] 回收木料：**用户拍板的第三条规则**（4 废木料 + 1 胶水 → 4 木料，需锯片工作台）──
        // 拆一件吃 16 木料的东西，只直接掉回 4 木料 + 4 废木料；这条配方就是那"另外 25%"的赎回券——
        // 走一趟它，木材的总回收率才追平别的材料的 50%，**代价是一份胶水**（见 Materials 的 glue：它吃燃料，稀缺是刻意的）。
        // **不设书门槛**（拆除是"建错了地方"的退出机制，不该被一本还没搜到的书卡死；粘木板是苦力活，不是手艺活）。
        // 工时 40 分：比造把椅子快，但也不是白得——你得站在锯台前把一堆碎料拼齐、刨平、压住。数值拟定待调。
        R("wood_from_scrap", "回收木料", RecipeCategory.Woodwork, "wood", Tools(ToolSlot.SawBlade), Books()),

        // ── [批次20·拆除回收] 胶水：**唯一**的产出途径（守门测试盯着这一点）──
        // 三重稀缺是设计而非疏漏：① 吃**燃料**——火把/发电机/火药/全部枪弹都在抢同一桶油；
        // ② 吃**骨头**——得先有动物或尸骨；③ 要烧杯槽 + 《土法化学笔记》——开局这两样都没有。
        // ⇒ 前几天你拆错位置的墙，木料**就是回不满**。这正是「胶水税」该有的痛感。
        //
        // 🔴 [批次22] **配方名必须与产物同名**。它从前叫「熬骨胶」——那是做法，不是东西；
        //    于是制作菜单里挂着一个玩家在库存里从没见过的名字，看着像是和搜刮来的「胶水」两回事。
        //    其实两者从第一天起就是同一个材料键（glue）。名字归一后，造的和搜的才真是一样东西。
        R("glue", "胶水", RecipeCategory.Chemistry, "glue", Tools(ToolSlot.Beaker), Books(FolkChemistryNotesBookId)),

        // 火药：烧杯类化学 + 读过《土法化学笔记》解锁。
        R("gunpowder", "火药", RecipeCategory.Chemistry, "gunpowder", Tools(ToolSlot.Beaker), Books(FolkChemistryNotesBookId)),

        // 鞣制药水：烧杯类化学 + 读过《土法化学笔记》解锁。
        R("tanning_solution", "鞣制药水", RecipeCategory.Chemistry, "tanning_solution", Tools(ToolSlot.Beaker), Books(FolkChemistryNotesBookId)),

        // ── 短弓（原名「自制弓」）：卡尺类精工 + 读过《**野外生存指南**》解锁 ──
        // ⚠️ [SPEC-B21·T26] **书门槛从《木匠入门》挪到了《野外生存指南》**（用户在 wiki 书籍表里重排了解锁归属，
        // 表赢代码）：那本书的效果列现在写的是「骨刀、短弓、削减木箭、圈套陷阱、战争面具」，
        // 而《木匠入门》那一列只剩「木椅、床、桌子、废木料回收」—— **一把弓都没有**。
        // 这推翻了更早一轮的拍板（"木椅/弓也要读木工书"），以新表为准。
        //
        // **这是放宽而非收紧**：《野外生存指南》在 camp.json 的开局柜子里，《木匠入门》要出门搜刮
        // ⇒ 短弓从"搜到书才能做"变成"**开局读完书就能做**"。一本开局的野外生存书教你削一张最朴素的弓
        // （木料 2 + 绳 1 = 一根木头 + 一根弦），比"得先学会做椅子"自然得多。
        // 🔴 **[T68·用户手改] 三把弓（短弓 / 反曲弓 / 长弓）的「卡尺」工具门槛已解除。**
        //    （上一行那句"工具门槛未动"到此作废——用户这一轮就是来动它的。）
        //    **为什么这三把、而不是全部**：卡尺是**精工**工具（量直径、找平直度）。
        //      · **弓是木工活**：削一根木杆、上一根弦——它要的是刀和手，不是游标卡尺。
        //      · **弩是机械活**：弩机、扳机组、弓臂张力 ⇒ 卡尺**保留**（两把弩仍要）。
        //      · **箭也保留卡尺**（自制箭 / 重头箭）：箭杆的**平直度**直接决定散布，这正是卡尺量的东西——
        //        用户明确只解禁了弓，没解禁箭。⇒ **"能做弓"与"能做箭"是两道门**，别顺手一起拆了。
        //    **后果（有意的）**：短弓从此**只要一本开局就有的书**——零工具、零搜刮 ⇒ **第一天就能有一把弓**。
        //    但它射出去的箭仍然要卡尺（自制箭）或至少要那本书（削尖的木箭）⇒ 弓不会因此变成白送的胜利。
        // **改名说明**：用户拍板的 5 把弓是「短弓/反曲弓/长弓/竞技复合弓/狩猎弓」，没有「自制弓」——
        // 留着它就是凭空多出第 6 把弓。而「木料 2 + 绳 1」＝一根木头 + 一根弦，本来就是最朴素的短弓，
        // 故这条配方由「短弓」承接（它此前是**悬空引用**：有配方，WeaponTable 却查不到武器数值）。
        // Id / OutputKey 仍是 handmade_bow（内部键，改它会无谓地波及 CraftOutputFactory 与一批既有测试）。
        R("handmade_bow", "短弓", RecipeCategory.Precision, "handmade_bow", Tools(), Books(WildernessSurvivalGuideBookId)),

        // 自制猎枪（批次18b，用户拍板新增；旧「土制枪」已删）：唯一**能自己造**的枪。
        // 工具槽取卡尺类精工（同自制弓——枪管/击发机构是精工活）；书门槛取《土法化学笔记》而非木工书：
        // 该书本就解锁"火药"，"懂土法化学 → 能自己攒枪"这条链最自然，且不必新造一本枪匠书（会带出新的投放/掉落缺口）。
        // 材料全部取自现有 Materials 目录（未新造材料）：铁 4（枪管）+ 木料 2（枪托）+ 机械零件 2（击发机构）。
        // （[T46] 废金属与金属锭合并成「铁」后，原「金属锭 2」按 1 锭 = 2 铁折成 铁 4，见下方自制霰弹枪那条的换算论证。）
        // 工时 240 分（短弓 120 / 木椅 150），造一把枪本就该比削张弓费事——但**不是全表最长**：
        // 双手重弩 320 分才是（见下方 heavy_crossbow）。数值皆拟定待调，真源 godot/data/config/recipes.json。
        R("improvised_hunting_gun", "自制猎枪", RecipeCategory.Precision, "improvised_hunting_gun", Tools(ToolSlot.Calipers), Books(FolkChemistryNotesBookId)),

        // 自制霰弹枪（多弹丸武器）：**全表最好造的枪**——它就是一根钢管 + 击针，没有线膛、没有精密瞄具。
        // 故比自制猎枪更便宜也更快：**铁只要 3**（猎枪要 4：线膛与精密件费料）、机械零件只要 1 个（简单击发）、
        // 工时 150 分（猎枪 240）。代价全在数值上：射程最短、衰减最重、扩散最大、对披甲目标几乎无效。
        // ⚠️ [T46] 这条"更便宜"的立论**原本挂在材料等级上**（霰弹用废金属 / 猎枪用需熔炼提纯的金属锭）。
        //    废金属与金属锭合并成「铁」之后那层区分没有了 ⇒ 立论改挂在**用量**上（铁 3 vs 铁 4），排序不变。
        //    这也是"1 锭 = 2 铁"这个换算率的由来：若按 1:1 直接相加，猎枪只要铁 2，**反而比霰弹枪还便宜**，档位当场倒挂。
        // 同样需卡尺精工 + 读过《土法化学笔记》（懂火药的人才造得了枪）。材料全取自现有 Materials 目录，未新造。
        // 枪本身不吃火药——火药是**弹药**（鹿弹 ammo_buck）的成本，见下方弹药配方。数值皆拟定待调。
        R("improvised_shotgun", "自制霰弹枪", RecipeCategory.Precision, "improvised_shotgun", Tools(ToolSlot.Calipers), Books(FolkChemistryNotesBookId)),

        // ── [批次21·T7/T10] 改装台：**武器改造的唯一场所**（用户拍板「在工作台可以制作改装台，在改装台落地武器改造」）──
        // 在**工作台**上造出来 → 进库存 → 由玩家**自己摆到营地里**（同沙袋的"造→摆"两段式）。
        // 它是**实心家具**（挖导航洞、真挡路），故放置受 PlacementRules 约束：**不许贴着围栏/大门**（64px 禁建带）。
        // 这正是用户原话要的东西：「为了防止玩家使用改装台、椅子等家具阻挡寻路，**放置的时候**就不允许贴着大门和围栏」
        // —— 要的是给放置**加约束**，不是取消放置；kill box 由禁建带正面挡住（见 WeaponModLogic.BenchSpec）。
        // 工具槽取**卡尺**（精工：改枪管、装刺刀都是找基准面的活，同自制猎枪/霰弹枪/弓弩那条线）。
        // **不设书门槛**：项目通例是不为一个新系统凭空造新书（会带出新的书籍投放缺口，见上文自制猎枪的同款论证）——
        // 改枪的门槛已经由"先得凑齐料造出这台工作案"承担了。
        // 材料：木料 8（台面/支腿）+ 铁 4（台钳与卡具）+ 机械零件 2（虎钳丝杠）+ 钉子 6。工时 200 分。
        // RequiredCrafterGates: 一台就够 —— 已有改装台时本配方灰掉（判定委托营地层，见 CampMain 的 crafterGate）。
        // 拆除走通用规则（FurnitureBuildCost["改装台"] 折半返还）。数值皆拟定待调。
        // ⚠ **位置有讲究**：本条**追加在既有卡尺类配方之后**，不插到表头——`CraftingPanelFormat.GroupByTool`
        // 按"工具需求的首次出现顺序"分桶（无工具 → 锯片 → 烧杯 → 卡尺），把一条卡尺配方插到最前面会把整个桶序搅乱。
        R("mod_bench", "改装台", RecipeCategory.Precision, "mod_bench", Tools(ToolSlot.Calipers), Books(), Books(ModBenchAbsentGate)),

        // ══════════════ [批次18] 子弹零件 + 四种子弹（用户拍板）══════════════
        // 产物 key 同时是 Materials 目录项 → CraftOutputFactory 走材料分支落地为可堆叠材料堆。
        //
        // 【稀缺梯度＝用户拍板的制作比】1 个「子弹零件」→ **短 8 / 中 5 / 鹿 4 / 长 2** 发。
        // 同一份原料，能喂手枪 8 次、喂步枪 5 次（而步枪一次扣扳机吞 2 发 → 实际只够 2 次半）、
        // 喂狙击枪 2 次。**越强的枪，同一份料能打的次数越少** —— 这就是"强，但打不起"的算式。
        //
        // 【后勤代价的两条腿】
        //  ① 子弹零件：弹壳/底火/弹头坯——**没法用土办法糊弄的精密件**，只能靠搜刮。
        //     它是四种子弹的**唯一共同瓶颈**，不提供制作兜底。
        //  ② 火药：每炉弹药还要 1 包火药，而火药 = 石料1 + **燃料1**（见上面 gunpowder 那条）。
        //     燃料同时是火把/发电机的命根子 → **「多打两枪」和「今晚有没有灯」落进同一个预算。**
        //     这正是用户要的：**不削枪的数值，用后勤代价平衡。**
        //
        // 工具/书门槛四条统一：烧杯类化学 + 《土法化学笔记》（该书本就解锁火药与自制猎枪——
        // 懂土法化学 → 能攒枪、也能攒弹，这条链最自然，不必新造一本枪匠书）。数值皆拟定待调。

        // 短子弹（手枪/冲锋枪）：1 零件 + 1 火药 → **8 发**。最便宜的枪弹。
        R("ammo_short", "短子弹", RecipeCategory.Chemistry, "ammo_short", Tools(ToolSlot.Beaker), Books(FolkChemistryNotesBookId)),

        // 中子弹（自制猎枪/步枪）：1 零件 + 1 火药 → **5 发**。
        // 步枪二连发（weapons.json 步枪 BurstCount=2）→ 一炉只够它扣 2 次半扳机。
        // 步枪是全表穿透第二高的枪（0.7，仅次于狙击 0.95）——它的强，代价就在这行。
        // ⚠ 原注释此处写着「它 93.5% 的命中」：**全仓查无出处**（无报告、无 harness），且 Sim 的 Duel 根本不输出
        //   命中率、也不建模弹药 ⇒ 已删。要引枪的强度数，去 docs/research 拿带日期的 Sim 报告，别留裸数字。
        R("ammo_medium", "中子弹", RecipeCategory.Chemistry, "ammo_medium", Tools(ToolSlot.Beaker), Books(FolkChemistryNotesBookId)),

        // 鹿弹（自制霰弹枪）：1 零件 + 1 火药 → **4 发**。一发塞 8 颗铅丸，用料本就比中子弹重。
        R("ammo_buck", "鹿弹", RecipeCategory.Chemistry, "ammo_buck", Tools(ToolSlot.Beaker), Books(FolkChemistryNotesBookId)),

        // 长子弹（狙击枪）：1 零件 + 1 火药 → **只有 2 发**。全表最贵的一发子弹。
        // 拿它打丧尸＝用金子砸苍蝇；它存在的意义是"这一枪必须命中一个人"。
        R("ammo_long", "长子弹", RecipeCategory.Chemistry, "ammo_long", Tools(ToolSlot.Beaker), Books(FolkChemistryNotesBookId)),

        // ==================== 箭（3 种可制作；碳纤维箭无配方，只能搜刮） ====================
        //
        // 箭一律**不吃火药、不吃子弹零件** —— 这是弓弩的立身之本。枪弹的原料稀缺且与别的东西竞争；
        // 箭只吃木料/废金属/金属锭，而且**射出去还能捡回来一些**。枪强而打不起，弓弩弱而打得久 —— 两条路子由此分野。
        //
        // ⚠ **但箭绝不便宜**，这是刻意的。用户拍板的回收率是 **25%**（读过《弓与箭之道》才 50%）——
        // 射出四支只捡回一支，箭是**持续消耗品**。若造箭近乎白送，"跑回战场把箭捡回来"就不值得玩家冒一次险，
        // 回收率这条机制也就白设计了。故除了应急用的木箭，**每一支箭都要吃到金属**（废金属 / 金属锭）。
        //
        // ══════════ 三种箭的门槛梯度（[SPEC-B21·T26] 用户按 wiki 书籍表重排，**推翻了下面这条旧口径**）══════════
        // ⚠️ 旧注释写的是「**造箭一律不要书**：削一根箭是苦力活……《弓与箭之道》管的是回收率，不是配方」——**已作废**。
        // 用户在书籍表里明写：《野外生存指南》解锁「削减木箭」，《弓与箭之道》解锁「自制箭」。表赢代码。
        //
        // 新梯度＝一条**"要走多远才配得上更好的箭"**的曲线：
        //   削尖的木箭 → 《野外生存指南》（**开局共享库存就有**，读完即可造）
        //   重头箭     → **无书门槛**（只要卡尺）——⚠️ 用户没提它，**一个字没动**，别顺手"统一"
        //   自制箭     → 《弓与箭之道》（**只能搜刮**）⇒ 基线箭反而成了要出门换来的东西
        // 材料/工具门槛全部维持原样，本轮**只重排书**。

        // 削尖的木箭：**便宜好用的主力箭**（用户手改后的定位）。木料 1 + 羽毛 1 → **4 支**，无工具槽。
        // 伤害 ×0.75、破甲 ×0.75，样样差一档但都不致残；代价集中在**射程 ×0.75**（全表最短）——新营地唯一撑得起弓手的箭。
        //
        // ⚠️ [SPEC-B21·T26] **它不再是零门槛配方了**：加了《野外生存指南》书门槛（用户拍板）。
        // **这不会卡死开局**，三条理由（核实过，别再当成收紧）：
        //   ① 这本书**开局就在共享库存里**（camp.json 住宅-柜子 role=storage），不用搜刮，只需读完（24 小时）；
        //   ② **短弓本身已经要这同一本书** ⇒ 没读书的人根本没弓可射，给箭加同一道门槛**不多锁任何东西**；
        //   ③ 就算搜到成品弓却还没读书，**重头箭仍是零书门槛**（只要卡尺，营地展示柜里就有）。
        // 它真正的作用是把"开局第一晚读哪本书"变成一个**真选择**：读它 ⇒ 一次拿到 骨刀＋短弓＋木箭＋陷阱＋战争面具。
        // ⚠️ [T67] **三种箭全部改成吃羽毛**（用户在 wiki 上亲手改的），值按【当前 wiki JSON】逐行核对落地。
        //    🔴 羽毛的唯一来源是【宰杀鸟】（<see cref="ButcheryLogic"/>）⇒ **没有捕鸟陷阱就没有箭**。
        //    这就是本单"先做羽毛来源，再同步箭配方"的全部理由：先前羽毛在代码里不存在，直接同步会让三种箭全造不出来。
        R("ammo_arrow_stick", "削尖的木箭", RecipeCategory.Precision, "ammo_arrow_stick", Tools(), Books(WildernessSurvivalGuideBookId)),

        // 自制箭：**基线**（修正系数全 1.00）。木杆 + 铁头 + 羽毛尾羽，要卡尺找平直度。
        // ⚠️ [T67] **尾羽从"布"改成了"羽毛"**（配料 木料1+铁1+布1 → 木料1+铁1+羽毛1，用户手改）——
        //    连带把旧 flavor 里的「布尾羽」也改了（下方那句"木杆 + 铁头"）。真羽毛比撕块布当尾羽像样多了。
        // ⚠️ [SPEC-B21·T26] 《弓与箭之道》书门槛（用户拍板）——本书此前只有四项被动，这是它解锁的唯一配方。
        R("ammo_arrow_handmade", "自制箭", RecipeCategory.Precision, "ammo_arrow_handmade", Tools(ToolSlot.Calipers), Books(WayOfBowBookId)),

        // 重头箭：**破甲专精**（用户原话「破甲能力更高，但射程和攻速有所削弱」）。箭头灌实心金属，箭尾一样要羽毛稳向。
        // ⚠️ [T67] 配料 木料2+铁2 → 木料1+铁1+羽毛1，产出 3 → **2**（用户手改：一批只出两支，破甲箭本就该金贵）。
        R("ammo_arrow_heavy", "重头箭", RecipeCategory.Precision, "ammo_arrow_heavy", Tools(ToolSlot.Calipers), Books()),

        // 碳纤维箭：**没有配方，这是有意的**。工厂早就停工了——它只能搜刮（超市/守林人小屋/金手指帮/河边小屋）。
        // 稀缺是它唯一的、也是足够的代价：四项全优还更准，谁也不该造得出来。

        // ==================== 弓弩（4 把可制作；竞技复合弓/狩猎弓/复合弩无配方，只能搜刮） ====================
        //
        // ⚠️ [SPEC-B21·T26] **书门槛已按用户的 wiki 书籍表重排，下面这条旧口径作废**：
        //    旧：「以下 4 把一律卡尺 + 《木匠入门》（同一本书管全部弓弩——不新造"弩匠书"）」
        //    新：**《木匠入门》名下不该再有任何弓弩**（用户把它的效果列写成了「木椅、床、桌子、废木料回收」＝纯家具书）。
        //
        // 现在的**弓弩的阶梯**（一条"要走多远才配得上更好的弓"的曲线）：
        //    短弓（handmade_bow，见上）→ 《野外生存指南》＝**开局共享库存就有**，读完即可造
        //    反曲弓 / 长弓             → 《**弓制作指南**》＝**只能搜刮**（守林人小屋·阁楼）⇒ 想升级弓，得出去把书找回来
        //    单手轻弩 / 双手重弩       → 《**机械之美**》＝**只能搜刮**（联合收割机仓库·收割机驾驶室）
        //
        // ⚠️ [T59] 中间那一档**换过一次书**：反曲弓/长弓原挂《进阶木匠技术》，用户在 wiki 上把"造弓"
        //    拆成单独一本《弓制作指南》。曲线一个字没变（照样只能搜刮），变的只是搜哪本、去哪搜。
        //    **消防斧没跟着挪**——它留在《进阶木匠技术》，「斧头与造斧头的书同馆」的设计没动。
        //
        // 护栏：`CraftingTests.弓弩的阶梯_短弓开局书_进阶弓搜书_弩要机械之美` 钉着这三档的书门槛——
        // 谁把哪一把搬回木工书，它会当场红。
        //
        // 梯度还体现在**材料贵贱与工时**上（[SPEC-B21·T26] 那轮只重排书，材料/工具没动；
        // 弩的材料后来随《机械之美》一起改过，见下方 light_crossbow）。

        // 反曲弓：标准均衡款。比短弓多一根木料 + 一块皮革（贴片/握把）。
        R("recurve_bow", "反曲弓", RecipeCategory.Precision, "recurve_bow", Tools(), Books(BowCraftingGuideBookId)),

        // 长弓：射程之王。一张比人还高的弓 → 料最多的**纯木**配方（木料 5 + 绳 2），工时 240。
        R("longbow", "长弓", RecipeCategory.Precision, "longbow", Tools(), Books(BowCraftingGuideBookId)),

        // ── 两把弩：[SPEC-B21·T26 追加] 书门槛 → 《**机械之美**》（用户拍板的新书；书名是他给的）──
        //
        // 用户原话：「**《机械之美》用武器零件造**」—— 两条都已落地：
        //   ① 书门槛 = 《机械之美》（搜刮书，投放在**联合收割机仓库·收割机驾驶室**，[T31] 用户拍板）
        //      ⚠ **别往加油站放**：修车棚的机修语义也贴，但那里有条身份护栏「燃油大户·**无武器/书**」
        //        （NewVillageGasCacheTests，批次13 定的）。理由与挪动经过见 ExplorationCache.MechanicalBeautyBookId。
        //   ② **defining 材料 = 「武器零件」**（`weapon_parts`，用户拍板**新建**的材料，见 Materials.WeaponPartsKey）
        //
        // ⚠️ **「武器零件」≠「机械零件」，这是用户特意要的区分，别把它们并回去**：
        //   · 机械零件 `components` —— 通用机括件，喂**改装台 / 自制枪 / 一堆杂活**
        //   · 武器零件 `weapon_parts` —— 弩机、扳机组、簧片，**只喂弩**
        //   ⇒ 两者**互不争抢**：想造改装台又想造弩，不必在同一堆零件上做取舍。
        //
        // 材料改动（拟定待调）：**机械零件 → 武器零件，且提量成主料**（轻弩 1→**2**、重弩 2→**3**）——
        // "用武器零件造"要名副其实，零件就不能只是配角。其余材料（木/铁/绳）与工时**一个字没动**。
        R("light_crossbow", "单手轻弩", RecipeCategory.Precision, "light_crossbow", Tools(ToolSlot.Calipers), Books(MechanicalBeautyBookId)),

        // 双手重弩：**全表最贵、最费时的配方**（320 分 ＞ 自制猎枪 240）。钢制弩臂 → 吃铁最多（铁 4）。
        // 它的回报是 65% 穿透（可制作里最高）—— 想打穿板甲，就得先付出这个代价。
        R("heavy_crossbow", "双手重弩", RecipeCategory.Precision, "heavy_crossbow", Tools(ToolSlot.Calipers), Books(MechanicalBeautyBookId)),

        // ══════════════ [批次21·T14] 烹饪台 + 两件炊具（用户拍板的新设施）══════════════
        //
        // 【为什么烹饪台**不设书门槛、不设工具槽**】做饭是生存的基本盘，不是手艺活——
        // 让"今晚有没有饭吃"被一本还没搜到的书卡死，是把玩家饿死在一条与设计无关的岔路上。
        // 同沙袋（往麻袋里铲土）、火把（木棒裹布）的既有论证：能不能开局就做，看的是"这活要不要手艺"。
        // 它的门槛压在**材料**上（石料 8 砌灶膛是全表最重的一笔石料开销）与**工时 180 分**上。
        //
        // ⚠️ **固定位置，玩家摆不了**（用户拍板：「改装台、烹饪台不允许跨越，但他们是营地内固定位置…烹饪台放在厨房」）：
        // 完工**不进库存**，直接砌在厨房锚点上（CookStation.AnchorX/Y；见 CampMain.CompleteCookStationBuild）。
        // 它实心、挖导航洞、不可跨越 ⇒ 锚点由 FixedFacilityAnchorTests 做**设计期**自检（玩家没有"放置"这个动作）。
        // RequiredCrafterGates：一座就够 —— 已有烹饪台时本配方灰掉（判定委托营地层，同改装台）。
        // 拆除走通用规则（SalvageLogic：50% 向下取整；木料那份再分半走废木料）。数值皆拟定待调。
        R(CookStation.RecipeId, CookStation.PropName, RecipeCategory.Misc, CookStation.ItemKey, Tools(), Books(), Books(CookStation.AbsentGate)),

        // 锅：装进烹饪台的槽位 ⇒ 每份饭省 2 点热量（用户拍板）。一口砸扁再敲圆的铁锅。
        // 无书无工具（敲锅不是手艺活），但吃**金属锭**——它是"省料"的投资，本身就得先付一笔料。
        R("cooking_pot", "锅", RecipeCategory.Misc, CookStation.PotItemKey, Tools(), Books()),

        // 烤架：同样 -2 点。比锅便宜（几根铁丝架在火上就是烤架），但一样占掉一个槽 ⇒
        // 两个槽都装满 = 每份饭只要 12 点，这是玩家能拿到的**唯一**一档"省料"，且要付两份材料 + 两份工时。
        R("cooking_grill", "烤架", RecipeCategory.Misc, CookStation.GrillItemKey, Tools(), Books()),

        // 火把（手持光源，批次4 光照）：木棒裹布蘸燃油即成——基础求生造物，无书门槛、无工具槽、开局可做。
        // 产物 key="torch"（对齐 LightSource.TorchKey），经 CraftOutputFactory 落地为 Item.Light（非武器/护甲/材料）。
        // 材料拟定待调：木料 1 + 布 1 + 燃料 1。手电不可制作（拾取/投放获得）。
        R("torch", "火把", RecipeCategory.Misc, "torch", Tools(), Books()),

        // ── [SPEC-B14] 草药医疗自制药（无书门槛：民间方子人人会；无工具槽；工时制）──────────────
        // 产物 key=herbal_salve / dandelion_tea，同时是 Materials 目录项 → CraftOutputFactory 走材料分支落地为 Item.Material，
        // 据 Key 查 MedicineCatalog 治感染（草药膏 0.45 / 蒲公英茶 0.10 治疗效率）。材料/工时皆拟定待调。

        // 草药膏：蒲公英 1 + 玫瑰果 1 + 老君须 1 捣制，工时 ~40 分。
        R("herbal_salve", "草药膏", RecipeCategory.Misc, "herbal_salve", Tools(), Books()),

        // 蒲公英茶：蒲公英 2 煮制（最简，不引入"水"新资源），工时 ~15 分。
        // ⚠️ [T67] **加"烹饪台在场"门槛**（用户："蒲公英茶和玫瑰果茶应该在烹饪台制作"）——茶要在灶上煮。
        //    它仍是**配方**（不吃 CookingLogic 的热量点），烹饪台在这里只是道门槛，见 <see cref="CookStationPresentGate"/>。
        R("dandelion_tea", "蒲公英茶", RecipeCategory.Misc, "dandelion_tea", Tools(), Books(), Books(CookStationPresentGate)),   // [T67] 烹饪台在场

        // [SPEC-B14-补 / T72] 草药绷带：老君须 1 + 绷带 1，工时 ~20 分。止血手术供点 20（普通绷带上位替代）。[T72] **额外**再降该处感染几率 ×0.75（止血+消炎并存，见 SurgeryCatalog）。
        R("herbal_bandage", "草药绷带", RecipeCategory.Misc, "herbal_bandage", Tools(), Books()),

        // [SPEC-B14-补2] 玫瑰果茶：玫瑰果 2 煮制，工时 ~15 分。饮用后 24 游戏小时伤病恢复速度 +9pp（见 Pawn 恢复加成 buff）。
        // ⚠️ [T67] 同蒲公英茶，加"烹饪台在场"门槛（用户拍板）。
        R("rosehip_tea", "玫瑰果茶", RecipeCategory.Misc, "rosehip_tea", Tools(), Books(), Books(CookStationPresentGate)),   // [T67] 烹饪台在场

        // ── 布鲁斯（狗）装备五件套（批次5，道格 2 级解锁）────────────────────────────
        // 制作者门槛＝道格且羁绊≥2 级（RequiredCrafterGates: DogGearCrafterGate，判定委托营地层）；
        // 无书门槛、无工具槽（道格手工为伙伴打造）。材料/工时/护甲值皆**拟定待调**。
        // 产物 key＝DogGearCatalog 键，经 CraftOutputFactory 落地为 Item.Armor（穿戴走 DogApparelSlots）。

        // 布制狗衣：身体贴身甲（低防·轻）。缝纫小件，布为主。
        R("dog_cloth_vest", "布制狗衣", RecipeCategory.Tailoring, "布制狗衣", Tools(), Books(), Books(DogGearCrafterGate)),

        // 皮制狗衣：身体外套甲（中防·稍重）。皮革为主 + 绳绑带。
        R("dog_leather_vest", "皮制狗衣", RecipeCategory.Tailoring, "皮制狗衣", Tools(), Books(), Books(DogGearCrafterGate)),

        // 口袋狗衣：身体无甲，缝多口袋给布鲁斯携带容量（探索负重）。布 + 绳（背带）。
        R("dog_pocket_vest", "口袋狗衣", RecipeCategory.Tailoring, "口袋狗衣", Tools(), Books(), Books(DogGearCrafterGate)),

        // 铁皮头甲：头部高防甲（重）。废金属敲打成盔 + 皮革内衬。
        R("dog_iron_helmet", "铁皮头甲", RecipeCategory.Misc, "铁皮头甲", Tools(), Books(), Books(DogGearCrafterGate)),

        // 铁丝头甲：头部轻便甲（防护弱于铁皮）。铁丝编笼 + 布衬。
        R("dog_wire_helmet", "铁丝头甲", RecipeCategory.Misc, "铁丝头甲", Tools(), Books(), Books(DogGearCrafterGate)),

        // ══════════════ [批次21·T26] 圈套陷阱 + 战争面具（《野外生存指南》）══════════════
        // 三条新配方**追加在表尾、不插队**：它们全是**无工具**配方，落进 CraftingPanelFormat.GroupByTool
        // 的第一个桶（无工具），桶序由各工具需求的**首次出现**决定 ⇒ 追加不会搅乱分桶（同 mod_bench 那条的注意事项）。

        // ── 圈套陷阱：营地里**唯一不用出门、不担风险**的食物来源（正文见 TrapSpec / TrapLogic 类注）──
        // 一圈铁丝套 + 一根弹木 + 一段绳子。**无工具门槛**——扎个活结不是手艺活，读过《野外生存指南》就会。
        // 造出来进库存 → 玩家自己摆到营地里（同沙袋/床的"造→摆"两段式）。它**不实心、不挖导航洞**
        // （摆不出 kill box），但**守 64px 禁建带**（不许糊在防线上）。工时 40 分。数值皆拟定待调。
        // 拆除走通用规则（FurnitureBuildCost["陷阱"] 折半返还），两处成本由测试钉死一致——否则造一个拆一个就是永动机。
        R("snare_trap", "圈套陷阱", RecipeCategory.Misc, "snare_trap", Tools(), Books(WildernessSurvivalGuideBookId)),

        // ── 战争面具：⚠️ **作用待用户定** ──
        // 用户在《野外生存指南》的「效果」列里点了它的名，但**没说它是什么、干什么用**。
        // 落地取**最保守的那一种**：一件占「面部」槽的护甲（骨与皮缝的面罩，护鼻与下巴，不遮眼——你还得看得见）。
        // **刻意不发明玩法效果**（"吓退丧尸"一类本作没有这个机制，不凭空造）。数值拟定待调。
        // 若用户想要的是别的东西（图腾/仪式道具/士气物），改这一条 + ArmorTable.WarMask() 即可，其余不动。
        R("war_mask", "战争面具", RecipeCategory.Misc, "war_mask", Tools(), Books(WildernessSurvivalGuideBookId)),

        // ══════════════ [批次21·T26] 粗布衬衫 / 短裤 / 长裤（《裁缝手记》）══════════════
        // 照既有「粗布背心 / 粗布外套」的模型来：同一本书、同样无工具、成本只吃布。
        // 补的是**贴身层与裤装槽的可制作缺口**——在此之前，长袖布衣/长裤/短裤全都只能搜刮，
        // 一件被砍烂就再也补不回来。数值对齐同槽既有件（拟定待调）。
        R("coarse_shirt", "粗布衬衫", RecipeCategory.Tailoring, "coarse_shirt", Tools(), Books(TailorsNotesBookId)),

        // ══════════════ [T59] 棉帽（用户在 wiki 上新加的一件）══════════════
        // 数值层由我拟定（用户只给了护甲 6/3 与 0.15kg，没给配方）：**照最小号布衣那一档来**——
        //   布 ×2 / 40 分钟，与「粗布短裤」（同为小件、同为 2 布 40 分）完全同档，**不另立新数**。
        // 依据：布类配方的成本按「用了多少布」走，而布的用量与成衣重量同阶（短裤 0.1kg=2布、衬衫/长裤 0.15kg=3布）。
        //   棉帽 0.15kg 但只罩一个头 + 两只耳朵（全是小部位）⇒ 取**下限档 2 布**，不给它 3 布：
        //   一顶帽子不该和一条长裤一样费布。工时同理取 40 分（全表最短的那一档）。
        // 书：《裁缝手记》—— 它是布类成衣的那本书，棉帽是布类成衣，不新开门槛。
        R("cotton_hat", "棉帽", RecipeCategory.Tailoring, "cotton_hat", Tools(), Books(TailorsNotesBookId)),

        // 粗布短裤：布最省、工时最短——护得也最少（只护大腿，小腿裸着，同既有「短裤」的取舍）。
        R("coarse_shorts", "粗布短裤", RecipeCategory.Tailoring, "coarse_shorts", Tools(), Books(TailorsNotesBookId)),

        // 粗布长裤：多一段布，多护两条小腿。
        R("coarse_trousers", "粗布长裤", RecipeCategory.Tailoring, "coarse_trousers", Tools(), Books(TailorsNotesBookId)),

        // ══════════════ [批次25·T44] 消防斧（《进阶木匠技术》）══════════════
        //
        // **为什么挂《进阶木匠技术》而不是别的书**（三本都逐一排除过，不是随手挑的）：
        //   · 《野外生存指南》＝**开局营地就有**（见本类顶部那段）⇒ 挂它等于"开局第一天就能造出一把长剑档武器"
        //     （消防斧 DPS 2.79 ≈ 长剑 2.81）。这会一口气抹平武器荒——那是设计里刻意的压力，不能顺手拆掉。
        //   · 《木匠入门》＝[SPEC-B21·T26追加3] 已被用户清成**纯家具书终态**（木椅/床/桌子，再无武器），
        //     `CraftingTests` 里有断言钉死这件事 ⇒ **往它上面挂武器会当场打红**，这是设计意图不是误报。
        //   · 《进阶木匠技术》＝**搜刮书**（联合收割机仓库·阁楼铁皮箱，全局最深处）⇒ 消防斧进来语义自洽，
        //     且**门槛与它的强度匹配**：要造消防斧，先出门把书找到。
        //     ⚠️ [T59] 立论里原有的"它已经是木工里的武器书（反曲弓/长弓挂在它名下）"这一条**已不成立**：
        //        两把弓已搬去《弓制作指南》⇒ **消防斧现在是本书名下唯一的武器**。结论不变（搜刮书 = 门槛匹配），
        //        但别再拿"弓也在这儿"当理由。
        //
        // 工具槽取**锯片**（做斧柄是锯木头的活；对照 两把弩走卡尺＝弩机找基准面的精工，斧柄不是。
        // ⚠ 别拿弓来对照：[T68] 用户已解除三把弓的卡尺门槛，弓现在零工具槽）。
        // ⚠️ 锯片在 `CraftingPanelFormat.GroupByTool` 的桶序里**早已出现过**（木椅/床），故本条追加到表尾
        //    不会搅乱分桶——同 mod_bench / 陷阱那两条的注意事项。
        //
        // 材料：铁 3（消防斧钢头）+ 木料 2（斧柄）。工时 180 分（介于反曲弓 180 与长弓 240 之间）。数值皆拟定待调。
        // [T46·impl-iron] 已接手 impl-axe 留的占位：原 `scrap_metal 3` → `iron 3`（废金属 1:1 折铁，用量不变）。
        R("axe", "消防斧", RecipeCategory.Woodwork, "axe", Tools(ToolSlot.SawBlade), Books(AdvancedCarpentryBookId)),

        // ══════════════ [T68] 恐怖装甲（用户在 wiki 上新加；**配方由我拟定**）══════════════
        //
        // 🔴 **为什么它必须有配方**：用户只给了数值（20/10、3kg、装甲层）和一句文案，**没给获取途径**。
        //    不配方、不投放 = 一件**永远拿不到的死物品**（"金属锭零获取途径"那个 bug 的原样重演）。
        //
        // **材料＝骨头 + 皮革，依据是用户自己的文案**：「每一片防护都来自于没做够防护的人」——
        //    那些"没做够防护的人"留下的，正是**骨头**和**皮**。这不是我的引申，是把那句话直译成材料表。
        //    ⇒ 骨头 6（缝在正面的骨板）+ 皮革 3（衬底）+ 绳子 2（把骨板一片片捆上去）。工时 240（全护甲表最长）。
        //
        // **书＝《野外生存指南》**（不新造书——书是 authored 内容，代码不许自己起名，CLAUDE.md 铁律）。
        //    它已经带着**战争面具**（骨 2 + 皮革 1）——同一门手艺：拿骨片和生皮缝护具。
        //    恐怖装甲就是战争面具的"大哥"，挂同一本书语义自洽，不必新开门槛。
        //
        // ⚠️ **它是本作第一件可制作的「装甲层」护甲**。此前装甲层三件（皮革胸甲 / 皮甲 / 板甲）**全部只能搜刮**
        //    ⇒ armor 层完全靠运气。它把那个洞堵上：**你可以自己造一件，代价是它比搜到的都弱**
        //    （20/10 ＜ 皮革胸甲 25/12.5），而且**只护胸+腹**（皮甲还护双臂）。
        //    真正的门槛不在书（那本开局就有），在**材料**：皮革要么搜刮、要么**自产**（`tan_leather`：碎皮革→生皮→鞣制→皮革，
        //    一张皮革 = 宰 4 只老鼠 + 1 份鞣制药水）——两条路都得实打实攒，攒 3 张仍是出门的分量。
        R("horror_armor", "恐怖装甲", RecipeCategory.Misc, "horror_armor", Tools(), Books(WildernessSurvivalGuideBookId)),

        // ══════════════ [T68] 墨镜 / 平光眼镜：**故意没有配方** ══════════════
        // 磨一片镜片是工业活（模具、抛光、光学面），末日营地里一个拿骨头缝甲的人做不出来——
        // 给它配方才是不自洽的那一边。⇒ **只能搜刮**：投放见 `ExplorationCache`
        //   · 墨镜 → 超市（卖场的太阳镜转架）
        //   · 平光眼镜 → 住宅区（谁家床头柜上的那副）
        // 它们是全作**第一批以掉落形式投放的护甲**（`LootKind.Armor` 一直支持，此前从没人用过）。

        // ── [T71] 自制简易墨镜（木缝雪镜）：**恰恰相反，它有配方** ──
        // 上面那条说"磨镜片是工业活所以墨镜不可造"；这件不磨镜片——它是**在一片木头上留两条缝**的
        // 因纽特式雪镜（用户 authored：木制眼罩·避免雪盲）。削木片一个营地里的人做得来 ⇒ 给它配方是自洽的。
        // 读《尖峰时刻》(滑雪极限运动书)解锁；产物走 CraftOutputFactory 的 ArmorOutputs 落成 Item.Armor(自制简易墨镜)。
        // 材料 wood1+rope1、工时 60min＝拟定（小件、木+绑带），书才是真门槛。追加不插队（配方序不进 Sim 战斗随机流）。
        R("snow_goggles", "自制简易墨镜", RecipeCategory.Misc, "snow_goggles", Tools(), Books(PeakHourBookId)),

        // ══════════════ [T67] 采集/种植/诱捕支柱：5 条新配方（**追加表尾不插队**）══════════════
        //
        // 全部**无工具门槛**（落进 GroupByTool 第一个桶，追加不搅乱分桶——同 mod_bench / 陷阱那几条）。
        // 三样"造→摆"设施（捕鸟陷阱/菜园/宰杀点）走沙袋那条两段式链；宰杀台/缝合生皮是普通产物配方。

        // ── 捕鸟陷阱：《农场主的一百个问题》解锁。抓鸟（→ 宰杀 → 鸟肉 + 羽毛 → 箭）。正文见 BirdTrapSpec/BirdTrapLogic。──
        // 一张网 + 两根木桩 + 一段绳（同圈套陷阱的量级）。不实心、不挖导航洞、守 64px 禁建带。
        R(BirdTrapSpec.RecipeId, "捕鸟陷阱", RecipeCategory.Misc, BirdTrapSpec.ItemKey, Tools(), Books(FarmerHundredQuestionsBookId)),

        // ── 菜园：《农场主的一百个问题》解锁。翻一小块地种土豆。正文（含"绝不能变无限食物"的四道闸）见 CropPlotSpec/CropPlotLogic。──
        // 造这件"菜园"只吃木料（垄框/农具），**种薯（土豆 1）是下种时另扣的**（见 CropPlotLogic.SeedCost，不写在这条配方里
        // —— 配方产的是"一块翻好的地"，种什么是摆下之后的事）。工时 60 分。
        R(CropPlotSpec.RecipeId, "菜园", RecipeCategory.Misc, CropPlotSpec.ItemKey, Tools(), Books(FarmerHundredQuestionsBookId)),

        // ── 简易宰杀点：用户"木材*1"。一块板 + 一个钩子。营地一座就够（AbsentGate 灰掉重复建造）。──
        // 🔴 **无书门槛**（用户没给它挂书）——但它的**价值由刀决定**：没有匕首/骨刀，槽是空的，宰不了。
        //    ⇒ 它天然与"骨刀"配对：骨刀（《野外生存指南》，开局就有）成了营地第一把上得了案板的刀。
        R(ButcherStation.PointRecipeId, "简易宰杀点", RecipeCategory.Misc, ButcherStation.PointItemKey, Tools(), Books(), Books(ButcherStation.AbsentGate)),   // 营地还没有宰杀设施才做得出（一座就够）

        // ── 宰杀台（升级）：用户"木板*3+钉子*4"。🔴🔴 **「木板」在材料表里不存在**（只有「木料」wood / 「废木料」scrap_wood）──
        //    ⇒ 我按**木料*3+钉子*4**落地（木料是唯一的结构性木材），**已 [DECISION] 上抛用户确认**（一行可改）。
        //    +50% 宰杀速度、20% 双倍产出（数值在 ButcheryLogic）。
        // 「升级」不新开引擎轴：它是一条**要求"营地已有简易宰杀点"**的配方（UpgradeGate），造出来落位时把简易点顶掉（消费层做）。
        R(ButcherStation.TableRecipeId, "宰杀台", RecipeCategory.Misc, ButcherStation.TableItemKey, Tools(), Books(), Books(ButcherStation.UpgradeGate)),   // 要求营地已有简易宰杀点

        // ── 缝合生皮：把宰杀老鼠攒下的「碎皮革」缝成成幅的「生皮」。──
        // 🔴 **它给「生皮」补上了游戏里的第一条生产线**：核实过 `rawhide` 此前**零掉落、零配方产出**，只能找商人买。
        //    碎皮革 4 → 生皮 1（重量账：4 × 0.2 = 0.8kg → 1.0kg，缝完略重，无套利）。无书门槛，工时 30 分。
        //    这条链的下游现在**真的通了**（见紧邻的 `tan_leather`）：生皮 +（鞣制药水，化学书）→ 皮革 → 皮甲/恐怖装甲。
        //    宰杀于是喂到了护甲线上。
        R("leather_stitch", "缝合生皮", RecipeCategory.Misc, "rawhide", Tools(), Books()),

        // ── 鞣制皮革：生皮 + 鞣制药水 → 皮革。这条链**此前压根没实现**（审计确认）──
        // 🔴 **它接通了两条断链**：① `rawhide`（生皮）此前**无任何消费方**——能产（`leather_stitch`）能买，却没处用；
        //    ② `tanning_solution`（鞣制药水）此前**无任何消费方**——同样能造能买、却无处消耗。二者都在这条配方里第一次有了去处。
        //    ⇒ 「碎皮革 →（缝）生皮 →（鞣）皮革」全线打通，"自产皮革"从此可行。
        // **数值拟定待调**（新数值，报依据）：生皮 1 + 鞣制药水 1 → 皮革 1，工时 60 分。
        //    · 重量账**无套利**：生皮 1.0kg + 鞣制药水 1.0kg（消耗掉）→ 皮革 0.6kg（越鞣越轻，不凭空增重）。
        //    · 门槛梯度守住**皮革的稀缺**：一张皮革 = 4 碎皮革（宰 4 只老鼠）+ 1 鞣制药水（燃料 1 + 石 1 + 化学书）。
        //      恐怖装甲吃 3 张皮革 ⇒ 12 只老鼠 + 3 份药水 —— 攒起来仍是实打实的出门，可制作但不廉价。
        //    · **不再挂书**：鞣制药水的化学书门槛已在药水那步收过一次；鞣这一步是手工活，同 `leather_stitch` 零书门槛。
        R("tan_leather", "鞣制皮革", RecipeCategory.Misc, "leather", Tools(), Books()),

        // ══════════════ [A2] 护踝鞋具 recipe —— **配方补齐**（armor.json 早有该护甲，却无获取途径）══════════════
        // 🔴 **为什么它必须有配方**：`ankle_guard`（护踝鞋具，authored [T72]，护小腿+脚含趾）此前**只在 armor.json 里**，
        //    没配方、没掉落投放 ⇒ 一件**永远拿不到的死物品**（同"金属锭零获取途径"那个 bug 的原样重演）。
        // **书＝《尖峰时刻》**（滑雪极限运动书，wiki 已定它解锁护踝鞋具）——不新造书（书是 authored，CLAUDE.md 铁律）。
        //    语义自洽：同一本极限运动书既教你做木缝雪镜、也教你做护住脚踝小腿的高帮硬底鞋具。
        // **材料＝皮革 2 + 绳 1、工时 80min＝拟定待调**（照恐怖装甲拟定先例；护踝 0.75kg、只护一双脚，
        //    比恐怖装甲 3kg 轻得多 ⇒ 量级取其零头：皮革做鞋body、绳做绑带高帮）。追加表尾不插队（配方序不进 Sim 战斗随机流）。
        //    产物走 CraftOutputFactory 的 ArmorOutputs 落成 Item.Armor(护踝鞋具)，穿戴登记见 ApparelSlots(脚槽·成对)。
        R("ankle_guard", "护踝鞋具", RecipeCategory.Misc, "ankle_guard", Tools(), Books(PeakHourBookId)),

        // Wiki 新增：尖峰时刻·二的两件保暖护甲。材料/工时是暂定值，等 Wiki 数值表继续校准。
        R("heavy_trousers", "厚重裤子", RecipeCategory.Tailoring, "heavy_trousers", Tools(), Books(PeakHourTwoBookId)),
        R("heavy_cape", "厚重披风", RecipeCategory.Tailoring, "heavy_cape", Tools(), Books(PeakHourTwoBookId)),

        // Wiki 新增：尖峰时刻·三解锁的雪地靴。
        R("snow_boots", "雪地靴", RecipeCategory.Misc, "snow_boots", Tools(), Books(PeakHourThreeBookId)),
    };

    private static readonly IReadOnlyDictionary<string, RecipeData> _byId = ToMap(_all);
    private static IReadOnlyDictionary<string, RecipeData> ToMap(IReadOnlyList<RecipeData> list)
    {
        var d = new Dictionary<string, RecipeData>();
        foreach (var r in list)
        {
            d[r.Id] = r;
        }
        return d;
    }

    /// <summary>全部内置配方（按声明顺序）。</summary>
    public static IReadOnlyList<RecipeData> All => _all;

    /// <summary>按配方 id 查一张配方；查不到返回 <c>null</c>。</summary>
    public static RecipeData? Find(string id)
        => id != null && _byId.TryGetValue(id, out RecipeData? r) ? r : null;
}
