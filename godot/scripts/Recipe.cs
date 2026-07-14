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
    /// <summary>《野外生存指南》书 id（对齐 <see cref="BookLibrary.WildernessSurvivalGuide"/>）。骨刀解锁读它。</summary>
    public const string WildernessSurvivalGuideBookId = "wilderness_survival_guide";

    /// <summary>《裁缝手记》纺织书 id（对齐 <see cref="BookLibrary.TailorsNotes"/>）。粗布背心解锁读它。</summary>
    public const string TailorsNotesBookId = "tailors_notes";

    /// <summary>《土法化学笔记》化学书 id（对齐 <see cref="BookLibrary.FolkChemistryNotes"/>）。火药 / 鞣制药水解锁读它。</summary>
    public const string FolkChemistryNotesBookId = "folk_chemistry_notes";

    /// <summary>《木匠入门》木工书 id（对齐 <see cref="BookLibrary.CarpentryBasics"/>）。木椅 / 自制弓解锁读它（一本管两条，同构化学书）。</summary>
    public const string CarpentryBasicsBookId = "carpentry_basics";

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

    private static IReadOnlySet<ToolSlot> Tools(params ToolSlot[] slots) => new HashSet<ToolSlot>(slots);
    private static IReadOnlyDictionary<string, int> Cost(params (string Key, int Qty)[] items)
    {
        var d = new Dictionary<string, int>();
        foreach (var (key, qty) in items)
        {
            d[key] = qty;
        }
        return d;
    }
    private static IReadOnlyList<string> Books(params string[] ids) => new List<string>(ids);

    // draft：以下配方的工具/条件/材料/数量/经验均为占位草稿，最终由用户调（对标 6 个例子 + 生存造物常识）。
    private static readonly IReadOnlyList<RecipeData> _all = new[]
    {
        // 骨刀：读完《野外生存指南》解锁；削骨即成，无需工具台工具。
        new RecipeData(
            Id: "bone_knife",
            DisplayName: "骨刀",
            Category: RecipeCategory.Misc,
            OutputKey: "bone_knife",
            OutputQuantity: 1,
            MaterialCosts: Cost(("bone", 2), ("cloth", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(WildernessSurvivalGuideBookId),
            WorkMinutes: 45),

        // 粗布背心：读过《裁缝手记》解锁；缝制布甲。
        new RecipeData(
            Id: "cloth_vest",
            DisplayName: "粗布背心",
            Category: RecipeCategory.Tailoring,
            OutputKey: "cloth_vest",
            OutputQuantity: 1,
            MaterialCosts: Cost(("cloth", 4)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(TailorsNotesBookId),
            WorkMinutes: 90),

        // 布夹克：同读《裁缝手记》解锁；比粗布背心多两条袖子（覆盖双臂）、料更足，故材料/工时都翻一档（拟定待调）。
        // 牛仔外套不可制作——厚牛仔布不在材料表，只能靠搜刮（见 camp.json 住宅-衣柜）。
        new RecipeData(
            Id: "cloth_jacket",
            DisplayName: "布夹克",
            Category: RecipeCategory.Tailoring,
            OutputKey: "cloth_jacket",
            OutputQuantity: 1,
            MaterialCosts: Cost(("cloth", 6)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(TailorsNotesBookId),
            WorkMinutes: 150),

        // 板凳（低级椅）：家具梯度最低档——无书门槛、无工具槽，**人人可造、开局即可做**（用户拍板：开局可做板凳，不必做中级木椅）。
        // 材料是木椅的打折版（仅 wood 2、去 nails，拟定待调）。产物 key 不在武器/护甲/材料集 → 走 CraftOutputFactory 家具/杂项分支落地。
        // 座位功能**暂同木椅**（低配版，读书座位无差异）；板凳/木椅/沙发的档次差异（耐久/舒适/加成）待设计。
        new RecipeData(
            Id: "bench",
            DisplayName: "板凳",
            Category: RecipeCategory.Woodwork,
            OutputKey: "bench",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 2)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 60),

        // 椅子：锯片类木工 + 读过《木匠入门》解锁（用户拍板：木椅/自制弓也要读木工书）。
        new RecipeData(
            Id: "chair",
            DisplayName: "木椅",
            Category: RecipeCategory.Woodwork,
            OutputKey: "chair",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 4), ("nails", 2)),
            RequiredTools: Tools(ToolSlot.SawBlade),
            RequiredBookIds: Books(CarpentryBasicsBookId),
            WorkMinutes: 150),

        // ── [批次21·impl-bedrest] 床：养病的物质基础 ──
        // 开局只有 2 张（camp.json 的 床#1/床#2），**第三张起要自己造**。床位稀缺是有意的：
        // 一张床只躺一个人，躺着的那个不站岗不生产（见 BedrestLogic）——玩家得反复决定"这张床今晚归谁"。
        // 走**沙袋那条链**（产出材料 → 库存「摆放」→ 左键落位），不发明新的建造范式。
        // 材料：木料 12（床架）+ 布 4（褥子）+ 钉子 6。比木椅重得多——它是个能让人躺平的大件。
        // 门槛同木椅：锯片 + 读过《木匠入门》（用户拍板"木椅/自制弓也要读木工书"，床是更大的木工活，同理）。
        // 工时 150 分。数值全部拟定待调。拆除走通用规则（SalvageLogic 50% 向下取整 ⇒ 木料 6 + 布 2 + 钉子 3）。
        new RecipeData(
            Id: "bed",
            DisplayName: "床",
            Category: RecipeCategory.Woodwork,
            OutputKey: "bed",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 12), ("cloth", 4), ("nails", 6)),
            RequiredTools: Tools(ToolSlot.SawBlade),
            RequiredBookIds: Books(CarpentryBasicsBookId),
            WorkMinutes: 150),

        // ── [批次20·掩体] 沙袋：**用户拍板"可自由建造摆放"**——项目里第一件玩家能自己往地上摆的防御工事 ──
        // 为什么沙袋能建而**墙不能建**（"墙不能建"是用户为防 kill box 拍的板，别以为规则不一致而"统一"掉）：
        // 沙袋**不阻挡移动、不改变寻路**（SandbagSpec.IsSolid/CarvesNavHole 恒 false）⇒ 敌人照样直线冲过来、
        // 不会被墙的迷宫牵着绕 ⇒ **摆不出 kill box**；它只给 25% 远程无伤，**而且敌人也能蹲在你的沙袋后面用**
        // （CoverLogic 双向对称）。玩家能经营防御位置，但摆不出必胜阵型。完整论证见 SandbagSpec 的类注。
        // 材料：布 2（袋子）+ 石料 4（往里装的东西）。**无书无工具门槛**——往麻袋里铲土不是手艺活，
        // 开局第一夜就该能垒起来。工时 30 分：一个人铲一垛的量。全部数值拟定待调。
        // 拆除走通用规则（SalvageLogic：50% 向下取整 ⇒ 布 1 + 石料 2），不设特例。
        new RecipeData(
            Id: "sandbag",
            DisplayName: "沙袋",
            Category: RecipeCategory.Misc,
            OutputKey: "sandbag",
            OutputQuantity: 1,
            MaterialCosts: Cost(("cloth", 2), ("stone", 4)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 30),

        // ── [批次20·拆除回收] 回收木料：**用户拍板的第三条规则**（4 废木料 + 1 胶水 → 4 木料，需锯片工作台）──
        // 拆一件吃 16 木料的东西，只直接掉回 4 木料 + 4 废木料；这条配方就是那"另外 25%"的赎回券——
        // 走一趟它，木材的总回收率才追平别的材料的 50%，**代价是一份胶水**（见 Materials 的 glue：它吃燃料，稀缺是刻意的）。
        // **不设书门槛**（拆除是"建错了地方"的退出机制，不该被一本还没搜到的书卡死；粘木板是苦力活，不是手艺活）。
        // 工时 40 分：比造把椅子快，但也不是白得——你得站在锯台前把一堆碎料拼齐、刨平、压住。数值拟定待调。
        new RecipeData(
            Id: "wood_from_scrap",
            DisplayName: "回收木料",
            Category: RecipeCategory.Woodwork,
            OutputKey: "wood",
            OutputQuantity: 4,
            MaterialCosts: Cost(("scrap_wood", 4), ("glue", 1)),
            RequiredTools: Tools(ToolSlot.SawBlade),
            RequiredBookIds: Books(),
            WorkMinutes: 40),

        // ── [批次20·拆除回收] 熬骨胶：胶水的**唯一**产出途径（守门测试盯着这一点）──
        // 三重稀缺是设计而非疏漏：① 吃**燃料**——火把/发电机/火药/全部枪弹都在抢同一桶油；
        // ② 吃**骨头**——得先有动物或尸骨；③ 要烧杯槽 + 《土法化学笔记》——开局这两样都没有。
        // ⇒ 前几天你拆错位置的墙，木料**就是回不满**。这正是「胶水税」该有的痛感。
        new RecipeData(
            Id: "glue",
            DisplayName: "熬骨胶",
            Category: RecipeCategory.Chemistry,
            OutputKey: "glue",
            OutputQuantity: 2,
            MaterialCosts: Cost(("bone", 4), ("fuel", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 60),

        // 火药：烧杯类化学 + 读过《土法化学笔记》解锁。
        new RecipeData(
            Id: "gunpowder",
            DisplayName: "火药",
            Category: RecipeCategory.Chemistry,
            OutputKey: "gunpowder",
            OutputQuantity: 2,
            MaterialCosts: Cost(("stone", 1), ("fuel", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 60),

        // 鞣制药水：烧杯类化学 + 读过《土法化学笔记》解锁。
        new RecipeData(
            Id: "tanning_solution",
            DisplayName: "鞣制药水",
            Category: RecipeCategory.Chemistry,
            OutputKey: "tanning_solution",
            OutputQuantity: 2,
            MaterialCosts: Cost(("fuel", 1), ("stone", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 60),

        // 短弓（原名「自制弓」）：卡尺类精工 + 读过《木匠入门》解锁（用户拍板：木椅/弓也要读木工书）。
        // **改名说明**：用户拍板的 5 把弓是「短弓/反曲弓/长弓/竞技复合弓/狩猎弓」，没有「自制弓」——
        // 留着它就是凭空多出第 6 把弓。而「木料 2 + 绳 1」＝一根木头 + 一根弦，本来就是最朴素的短弓，
        // 故这条配方由「短弓」承接（它此前是**悬空引用**：有配方，WeaponTable 却查不到武器数值）。
        // Id / OutputKey 仍是 handmade_bow（内部键，改它会无谓地波及 CraftOutputFactory 与一批既有测试）。
        new RecipeData(
            Id: "handmade_bow",
            DisplayName: "短弓",
            Category: RecipeCategory.Precision,
            OutputKey: "handmade_bow",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 2), ("rope", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(CarpentryBasicsBookId),
            WorkMinutes: 120),

        // 自制猎枪（批次18b，用户拍板新增；旧「土制枪」已删）：唯一**能自己造**的枪。
        // 工具槽取卡尺类精工（同自制弓——枪管/击发机构是精工活）；书门槛取《土法化学笔记》而非木工书：
        // 该书本就解锁"火药"，"懂土法化学 → 能自己攒枪"这条链最自然，且不必新造一本枪匠书（会带出新的投放/掉落缺口）。
        // 材料全部取自现有 Materials 目录（未新造材料）：金属锭 2（枪管）+ 木料 2（枪托）+ 机械零件 2（击发机构）。
        // 工时 240 分＝全表最长（自制弓 120 / 木椅 150），造一把枪本就该比削张弓费事。数值皆拟定待调。
        new RecipeData(
            Id: "improvised_hunting_gun",
            DisplayName: "自制猎枪",
            Category: RecipeCategory.Precision,
            OutputKey: "improvised_hunting_gun",
            OutputQuantity: 1,
            MaterialCosts: Cost(("metal_ingot", 2), ("wood", 2), ("components", 2)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 240),

        // 自制霰弹枪（多弹丸武器）：**全表最好造的枪**——它就是一根钢管 + 击针，没有线膛、没有精密瞄具。
        // 故比自制猎枪更便宜也更快：用**废金属**（而非猎枪的金属锭=需先熔炼提纯）、机械零件只要 1 个（简单击发）、
        // 工时 150 分（猎枪 240）。代价全在数值上：射程最短、衰减最重、扩散最大、对披甲目标几乎无效。
        // 同样需卡尺精工 + 读过《土法化学笔记》（懂火药的人才造得了枪）。材料全取自现有 Materials 目录，未新造。
        // 枪本身不吃火药——火药是**弹药**（鹿弹 ammo_buck）的成本，见下方弹药配方。数值皆拟定待调。
        new RecipeData(
            Id: "improvised_shotgun",
            DisplayName: "自制霰弹枪",
            Category: RecipeCategory.Precision,
            OutputKey: "improvised_shotgun",
            OutputQuantity: 1,
            MaterialCosts: Cost(("scrap_metal", 3), ("wood", 2), ("components", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 150),

        // ── [批次21·T7/T10] 改装台：**武器改造的唯一场所**（用户拍板「在工作台可以制作改装台，在改装台落地武器改造」）──
        // 在**工作台**上造出来 → 进库存 → 由玩家**自己摆到营地里**（同沙袋的"造→摆"两段式）。
        // 它是**实心家具**（挖导航洞、真挡路），故放置受 PlacementRules 约束：**不许贴着围栏/大门**（64px 禁建带）。
        // 这正是用户原话要的东西：「为了防止玩家使用改装台、椅子等家具阻挡寻路，**放置的时候**就不允许贴着大门和围栏」
        // —— 要的是给放置**加约束**，不是取消放置；kill box 由禁建带正面挡住（见 WeaponModLogic.BenchSpec）。
        // 工具槽取**卡尺**（精工：改枪管、装刺刀都是找基准面的活，同自制猎枪/霰弹枪/弓弩那条线）。
        // **不设书门槛**：项目通例是不为一个新系统凭空造新书（会带出新的书籍投放缺口，见上文自制猎枪的同款论证）——
        // 改枪的门槛已经由"先得凑齐料造出这台工作案"承担了。
        // 材料：木料 8（台面/支腿）+ 废金属 4（台钳与卡具）+ 机械零件 2（虎钳丝杠）+ 钉子 6。工时 200 分。
        // RequiredCrafterGates: 一台就够 —— 已有改装台时本配方灰掉（判定委托营地层，见 CampMain 的 crafterGate）。
        // 拆除走通用规则（FurnitureBuildCost["改装台"] 折半返还）。数值皆拟定待调。
        // ⚠ **位置有讲究**：本条**追加在既有卡尺类配方之后**，不插到表头——`CraftingPanelFormat.GroupByTool`
        // 按"工具需求的首次出现顺序"分桶（无工具 → 锯片 → 烧杯 → 卡尺），把一条卡尺配方插到最前面会把整个桶序搅乱。
        new RecipeData(
            Id: "mod_bench",
            DisplayName: "改装台",
            Category: RecipeCategory.Precision,
            OutputKey: "mod_bench",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 8), ("scrap_metal", 4), ("components", 2), ("nails", 6)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(),
            WorkMinutes: 200,
            RequiredCrafterGates: Books(ModBenchAbsentGate)),

        // ══════════════ [批次18] 子弹零件 + 四种子弹（用户拍板）══════════════
        // 产物 key 同时是 Materials 目录项 → CraftOutputFactory 走材料分支落地为可堆叠材料堆。
        //
        // 【稀缺梯度＝用户拍板的制作比】1 个「子弹零件」→ **短 8 / 中 5 / 鹿 4 / 长 2** 发。
        // 同一份原料，能喂手枪 8 次、喂步枪 5 次（而步枪一次扣扳机吞 2 发 → 实际只够 2 次半）、
        // 喂狙击枪 2 次。**越强的枪，同一份料能打的次数越少** —— 这就是"强，但打不起"的算式。
        //
        // 【后勤代价的两条腿】
        //  ① 子弹零件：弹壳/底火/弹头坯——**没法用土办法糊弄的精密件**，主要靠搜刮（见下方那条配方：
        //     能造，但吃机械零件，贵）。它是四种子弹的**唯一共同瓶颈**。
        //  ② 火药：每炉弹药还要 1 包火药，而火药 = 石料1 + **燃料1**（见上面 gunpowder 那条）。
        //     燃料同时是火把/发电机的命根子 → **「多打两枪」和「今晚有没有灯」落进同一个预算。**
        //     这正是用户要的：**不削枪的数值，用后勤代价平衡。**
        //
        // 工具/书门槛四条统一：烧杯类化学 + 《土法化学笔记》（该书本就解锁火药与自制猎枪——
        // 懂土法化学 → 能攒枪、也能攒弹，这条链最自然，不必新造一本枪匠书）。数值皆拟定待调。

        // 子弹零件：**唯一允许新增的材料**（用户点名）。能造，但刻意贵——机械零件是拆精密装置才有的东西。
        // 定位：搜刮为主、制作兜底。搜刮断供时你还能造，但每造一个都在啃别的系统的料。
        new RecipeData(
            Id: "bullet_parts",
            DisplayName: "子弹零件",
            Category: RecipeCategory.Precision,
            OutputKey: "bullet_parts",
            OutputQuantity: 1,
            MaterialCosts: Cost(("scrap_metal", 2), ("components", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 60),

        // 短子弹（手枪/冲锋枪）：1 零件 + 1 火药 → **8 发**。最便宜的枪弹。
        new RecipeData(
            Id: "ammo_short",
            DisplayName: "短子弹",
            Category: RecipeCategory.Chemistry,
            OutputKey: "ammo_short",
            OutputQuantity: 8,
            MaterialCosts: Cost(("bullet_parts", 1), ("gunpowder", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 45),

        // 中子弹（自制猎枪/步枪/栓动猎枪）：1 零件 + 1 火药 → **5 发**。
        // 步枪二连发 → 一炉只够它扣 2 次半扳机。它 93.5% 的命中，代价就在这行。
        new RecipeData(
            Id: "ammo_medium",
            DisplayName: "中子弹",
            Category: RecipeCategory.Chemistry,
            OutputKey: "ammo_medium",
            OutputQuantity: 5,
            MaterialCosts: Cost(("bullet_parts", 1), ("gunpowder", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 45),

        // 鹿弹（自制霰弹枪）：1 零件 + 1 火药 → **4 发**。一发塞 8 颗铅丸，用料本就比中子弹重。
        new RecipeData(
            Id: "ammo_buck",
            DisplayName: "鹿弹",
            Category: RecipeCategory.Chemistry,
            OutputKey: "ammo_buck",
            OutputQuantity: 4,
            MaterialCosts: Cost(("bullet_parts", 1), ("gunpowder", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 45),

        // 长子弹（狙击枪）：1 零件 + 1 火药 → **只有 2 发**。全表最贵的一发子弹。
        // 拿它打丧尸＝用金子砸苍蝇；它存在的意义是"这一枪必须命中一个人"。
        new RecipeData(
            Id: "ammo_long",
            DisplayName: "长子弹",
            Category: RecipeCategory.Chemistry,
            OutputKey: "ammo_long",
            OutputQuantity: 2,
            MaterialCosts: Cost(("bullet_parts", 1), ("gunpowder", 1)),
            RequiredTools: Tools(ToolSlot.Beaker),
            RequiredBookIds: Books(FolkChemistryNotesBookId),
            WorkMinutes: 45),

        // ==================== 箭（3 种可制作；碳纤维箭无配方，只能搜刮） ====================
        //
        // 箭一律**不吃火药、不吃子弹零件** —— 这是弓弩的立身之本。枪弹的原料稀缺且与别的东西竞争；
        // 箭只吃木料/废金属/金属锭，而且**射出去还能捡回来一些**。枪强而打不起，弓弩弱而打得久 —— 两条路子由此分野。
        //
        // ⚠ **但箭绝不便宜**，这是刻意的。用户拍板的回收率是 **25%**（读过《弓与箭之道》才 50%）——
        // 射出四支只捡回一支，箭是**持续消耗品**。若造箭近乎白送，"跑回战场把箭捡回来"就不值得玩家冒一次险，
        // 回收率这条机制也就白设计了。故除了应急用的木箭，**每一支箭都要吃到金属**（废金属 / 金属锭）。
        //
        // 三种箭的门槛梯度（拟定待调）：木箭「什么都不要」→ 自制箭「要卡尺 + 废金属」→ 重头箭「要卡尺 + 金属锭」。
        // 注意**造箭一律不要书**：削一根箭是苦力活，不是手艺活（要读书的是造**弓**；《弓与箭之道》管的是回收率，不是配方）。

        // 削尖的木箭：**便宜好用的主力箭**（用户手改后的定位）。木料 1 → 4 支，无工具槽、无书门槛 —— **开局第一天就能做**。
        // 它不再是"没箭了才用的应急货"：伤害 ×0.75、破甲 ×0.75，样样差一档但都不致残；
        // 代价集中在**射程 ×0.75**（全表最短）——它是新营地唯一撑得起弓手的箭。
        new RecipeData(
            Id: "ammo_arrow_stick",
            DisplayName: "削尖的木箭",
            Category: RecipeCategory.Precision,
            OutputKey: "ammo_arrow_stick",
            OutputQuantity: 3,
            MaterialCosts: Cost(("wood", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 20),

        // 自制箭：**基线**（修正系数全 1.00）。木杆 + 铁头 + 布尾羽，要卡尺找平直度。一批 5 支 / 30 工时。
        new RecipeData(
            Id: "ammo_arrow_handmade",
            DisplayName: "自制箭",
            Category: RecipeCategory.Precision,
            OutputKey: "ammo_arrow_handmade",
            OutputQuantity: 4,
            MaterialCosts: Cost(("wood", 2), ("scrap_metal", 1), ("cloth", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(),
            WorkMinutes: 45),

        // 重头箭：**破甲专精**（用户原话「破甲能力更高，但射程和攻速有所削弱」）。箭头要灌实心金属 → 吃金属锭（贵）。
        // 一批 4 支 / 40 工时。专门留着对付披甲的劫掠者——打丧尸用它是浪费。
        new RecipeData(
            Id: "ammo_arrow_heavy",
            DisplayName: "重头箭",
            Category: RecipeCategory.Precision,
            OutputKey: "ammo_arrow_heavy",
            OutputQuantity: 3,
            MaterialCosts: Cost(("wood", 2), ("metal_ingot", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(),
            WorkMinutes: 60),

        // 碳纤维箭：**没有配方，这是有意的**。工厂早就停工了——它只能搜刮（超市/守林人小屋/金手指帮/河边小屋）。
        // 稀缺是它唯一的、也是足够的代价：四项全优还更准，谁也不该造得出来。

        // ==================== 弓弩（4 把可制作；竞技复合弓/狩猎弓/复合弩无配方，只能搜刮） ====================
        //
        // 「短弓」的配方是既有的 handmade_bow（见上，Id 未动）。以下 4 把是它的进阶：一律**卡尺槽 + 《木匠入门》**
        //（同一本书管全部弓弩——不新造"弩匠书"，那会带出新的书籍投放缺口）。梯度体现在**材料贵贱与工时**上。

        // 反曲弓：标准均衡款。比短弓多一根木料 + 一块皮革（贴片/握把）。
        new RecipeData(
            Id: "recurve_bow",
            DisplayName: "反曲弓",
            Category: RecipeCategory.Precision,
            OutputKey: "recurve_bow",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 3), ("rope", 1), ("leather", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(CarpentryBasicsBookId),
            WorkMinutes: 180),

        // 长弓：射程之王。一张比人还高的弓 → 料最多的**纯木**配方（木料 5 + 绳 2），工时 240。
        new RecipeData(
            Id: "longbow",
            DisplayName: "长弓",
            Category: RecipeCategory.Precision,
            OutputKey: "longbow",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 5), ("rope", 2)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(CarpentryBasicsBookId),
            WorkMinutes: 240),

        // 单手轻弩：弩＝木身 + 弩机。弩机是机械活 → 首次出现 components（机械零件）。
        new RecipeData(
            Id: "light_crossbow",
            DisplayName: "单手轻弩",
            Category: RecipeCategory.Precision,
            OutputKey: "light_crossbow",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 2), ("scrap_metal", 2), ("rope", 1), ("components", 1)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(CarpentryBasicsBookId),
            WorkMinutes: 200),

        // 双手重弩：**全表最贵、最费时的配方**（320 分 ＞ 自制猎枪 240）。钢制弩臂 → 吃金属锭。
        // 它的回报是 65% 穿透（可制作里最高）—— 想打穿板甲，就得先付出这个代价。
        new RecipeData(
            Id: "heavy_crossbow",
            DisplayName: "双手重弩",
            Category: RecipeCategory.Precision,
            OutputKey: "heavy_crossbow",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 4), ("metal_ingot", 2), ("rope", 2), ("components", 2)),
            RequiredTools: Tools(ToolSlot.Calipers),
            RequiredBookIds: Books(CarpentryBasicsBookId),
            WorkMinutes: 320),

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
        new RecipeData(
            Id: CookStation.RecipeId,
            DisplayName: CookStation.PropName,
            Category: RecipeCategory.Misc,
            OutputKey: CookStation.ItemKey,
            OutputQuantity: 1,
            MaterialCosts: Cost(("stone", 8), ("wood", 6), ("scrap_metal", 3), ("nails", 4)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 180,
            RequiredCrafterGates: Books(CookStation.AbsentGate)),

        // 锅：装进烹饪台的槽位 ⇒ 每份饭省 2 点热量（用户拍板）。一口砸扁再敲圆的铁锅。
        // 无书无工具（敲锅不是手艺活），但吃**金属锭**——它是"省料"的投资，本身就得先付一笔料。
        new RecipeData(
            Id: "cooking_pot",
            DisplayName: "锅",
            Category: RecipeCategory.Misc,
            OutputKey: CookStation.PotItemKey,
            OutputQuantity: 1,
            MaterialCosts: Cost(("metal_ingot", 1), ("scrap_metal", 2)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 60),

        // 烤架：同样 -2 点。比锅便宜（几根铁丝架在火上就是烤架），但一样占掉一个槽 ⇒
        // 两个槽都装满 = 每份饭只要 12 点，这是玩家能拿到的**唯一**一档"省料"，且要付两份材料 + 两份工时。
        new RecipeData(
            Id: "cooking_grill",
            DisplayName: "烤架",
            Category: RecipeCategory.Misc,
            OutputKey: CookStation.GrillItemKey,
            OutputQuantity: 1,
            MaterialCosts: Cost(("wire", 4), ("scrap_metal", 2)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 45),

        // 火把（手持光源，批次4 光照）：木棒裹布蘸燃油即成——基础求生造物，无书门槛、无工具槽、开局可做。
        // 产物 key="torch"（对齐 LightSource.TorchKey），经 CraftOutputFactory 落地为 Item.Light（非武器/护甲/材料）。
        // 材料拟定待调：木料 1 + 布 1 + 燃料 1。手电不可制作（拾取/投放获得）。
        new RecipeData(
            Id: "torch",
            DisplayName: "火把",
            Category: RecipeCategory.Misc,
            OutputKey: "torch",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wood", 1), ("cloth", 1), ("fuel", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 20),

        // ── [SPEC-B14] 草药医疗自制药（无书门槛：民间方子人人会；无工具槽；工时制）──────────────
        // 产物 key=herbal_salve / dandelion_tea，同时是 Materials 目录项 → CraftOutputFactory 走材料分支落地为 Item.Material，
        // 据 Key 查 MedicineCatalog 治感染（草药膏 0.45 / 蒲公英茶 0.10 治疗效率）。材料/工时皆拟定待调。

        // 草药膏：蒲公英 1 + 玫瑰果 1 + 老君须 1 捣制，工时 ~40 分。
        new RecipeData(
            Id: "herbal_salve",
            DisplayName: "草药膏",
            Category: RecipeCategory.Misc,
            OutputKey: "herbal_salve",
            OutputQuantity: 1,
            MaterialCosts: Cost(("dandelion", 1), ("rosehip", 1), ("laojunxu", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 40),

        // 蒲公英茶：蒲公英 2 煮制（最简，不引入"水"新资源），工时 ~15 分。
        new RecipeData(
            Id: "dandelion_tea",
            DisplayName: "蒲公英茶",
            Category: RecipeCategory.Misc,
            OutputKey: "dandelion_tea",
            OutputQuantity: 1,
            MaterialCosts: Cost(("dandelion", 2)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 15),

        // [SPEC-B14-补] 草药绷带：老君须 1 + 绷带 1，工时 ~20 分。止血手术供点 25（普通绷带的上位替代，见 SurgeryCatalog）。
        new RecipeData(
            Id: "herbal_bandage",
            DisplayName: "草药绷带",
            Category: RecipeCategory.Misc,
            OutputKey: "herbal_bandage",
            OutputQuantity: 1,
            MaterialCosts: Cost(("laojunxu", 1), ("bandage", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 20),

        // [SPEC-B14-补2] 玫瑰果茶：玫瑰果 2 煮制，工时 ~15 分。饮用后 24 游戏小时伤病恢复速度 +9pp（见 Pawn 恢复加成 buff）。
        new RecipeData(
            Id: "rosehip_tea",
            DisplayName: "玫瑰果茶",
            Category: RecipeCategory.Misc,
            OutputKey: "rosehip_tea",
            OutputQuantity: 1,
            MaterialCosts: Cost(("rosehip", 2)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 15),

        // ── 布鲁斯（狗）装备五件套（批次5，道格 2 级解锁）────────────────────────────
        // 制作者门槛＝道格且羁绊≥2 级（RequiredCrafterGates: DogGearCrafterGate，判定委托营地层）；
        // 无书门槛、无工具槽（道格手工为伙伴打造）。材料/工时/护甲值皆**拟定待调**。
        // 产物 key＝DogGearCatalog 键，经 CraftOutputFactory 落地为 Item.Armor（穿戴走 DogApparelSlots）。

        // 布制狗衣：身体贴身甲（低防·轻）。缝纫小件，布为主。
        new RecipeData(
            Id: "dog_cloth_vest",
            DisplayName: "布制狗衣",
            Category: RecipeCategory.Tailoring,
            OutputKey: "布制狗衣",
            OutputQuantity: 1,
            MaterialCosts: Cost(("cloth", 3)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 50,
            RequiredCrafterGates: Books(DogGearCrafterGate)),

        // 皮制狗衣：身体外套甲（中防·稍重）。皮革为主 + 绳绑带。
        new RecipeData(
            Id: "dog_leather_vest",
            DisplayName: "皮制狗衣",
            Category: RecipeCategory.Tailoring,
            OutputKey: "皮制狗衣",
            OutputQuantity: 1,
            MaterialCosts: Cost(("leather", 2), ("rope", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 70,
            RequiredCrafterGates: Books(DogGearCrafterGate)),

        // 口袋狗衣：身体无甲，缝多口袋给布鲁斯携带容量（探索负重）。布 + 绳（背带）。
        new RecipeData(
            Id: "dog_pocket_vest",
            DisplayName: "口袋狗衣",
            Category: RecipeCategory.Tailoring,
            OutputKey: "口袋狗衣",
            OutputQuantity: 1,
            MaterialCosts: Cost(("cloth", 3), ("rope", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 60,
            RequiredCrafterGates: Books(DogGearCrafterGate)),

        // 铁皮头甲：头部高防甲（重）。废金属敲打成盔 + 皮革内衬。
        new RecipeData(
            Id: "dog_iron_helmet",
            DisplayName: "铁皮头甲",
            Category: RecipeCategory.Misc,
            OutputKey: "铁皮头甲",
            OutputQuantity: 1,
            MaterialCosts: Cost(("scrap_metal", 2), ("leather", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 70,
            RequiredCrafterGates: Books(DogGearCrafterGate)),

        // 铁丝头甲：头部轻便甲（防护弱于铁皮）。铁丝编笼 + 布衬。
        new RecipeData(
            Id: "dog_wire_helmet",
            DisplayName: "铁丝头甲",
            Category: RecipeCategory.Misc,
            OutputKey: "铁丝头甲",
            OutputQuantity: 1,
            MaterialCosts: Cost(("wire", 2), ("cloth", 1)),
            RequiredTools: Tools(),
            RequiredBookIds: Books(),
            WorkMinutes: 55,
            RequiredCrafterGates: Books(DogGearCrafterGate)),
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
