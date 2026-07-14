using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs / CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 书的底层数据：一本可阅读书的 id / 标题 / 正文 / 已读标记 / 配方产出（本轮留桩）。
// Item(书类) 只用 book id 引用本条目；已读态是运行时可变状态，故本类非不可变（区别于 Item）。

/// <summary>
/// 一本书的底层数据条目。<see cref="Item.Book"/> 用 <see cref="Id"/> 引用它。
/// 已读标记 <see cref="IsRead"/> 由 W3 阅读接入在读完时 <see cref="MarkRead"/> 置位。
/// <see cref="GrantsRecipeStub"/> 是"读完给配方"的**桩**：配方系统后续再接，本轮只挂一个配方 id 占位。
/// </summary>
public sealed class BookData
{
    /// <summary>书 id（稳定键，Item.RefKey 指它）。</summary>
    public string Id { get; }

    /// <summary>标题（作 Item 显示名）。</summary>
    public string Title { get; }

    /// <summary>正文文本（本轮为占位草稿，待用户改）。</summary>
    public string Body { get; }

    /// <summary>读完给的配方 id（**桩**，配方系统后续接；无产出则为 <c>null</c>）。</summary>
    public string? GrantsRecipeStub { get; }

    /// <summary>是否已读（运行时可变；<see cref="MarkRead"/> 置位）。</summary>
    public bool IsRead { get; private set; }

    /// <summary>
    /// 读完本书所需的游戏内小时（<c>draft</c>）：读书为耗时活动，读者按 <see cref="ReadingProgress"/> 累计到此值即读完。
    /// 技术工具书篇幅长（读得久）、纯 lore 日记短（读得快）。默认 12h。
    /// </summary>
    public double ReadHours { get; }

    /// <summary>
    /// **前置书**（通用书籍前置链，可空 = 无前置）：读本书时若读者尚未读完此前置书，**不禁止**阅读，
    /// 但读速 ×<see cref="ReadingSpeed.MissingPrerequisiteMultiplier"/>（即耗时数倍，见 <see cref="ReadingSpeed.PrerequisiteFactor"/>）。
    /// 数据驱动：链上每本书声明各自前置即可（如《进阶木匠技术》←《木匠入门》）。系数 draft 待调。
    /// </summary>
    public string? PrerequisiteBookId { get; }

    public BookData(string id, string title, string body, string? grantsRecipeStub = null, double readHours = 12,
        string? prerequisiteBookId = null)
    {
        Id = id;
        Title = title;
        Body = body;
        GrantsRecipeStub = grantsRecipeStub;
        ReadHours = readHours;
        PrerequisiteBookId = prerequisiteBookId;
    }

    /// <summary>标记为已读（幂等，重复调用无副作用）。W3 阅读结算时调用。</summary>
    public void MarkRead() => IsRead = true;

    /// <summary>由本书造一件对应的库存物品（书类 Item，引用键=本书 id）。</summary>
    public Item ToItem() => Item.Book(Id, Title);
}

/// <summary>
/// 内置书目（占位草稿）。本轮先放两本，正文标 <c>// draft 待用户改</c>，配方产出留桩。
/// W3 阅读接入可从此取 <see cref="BookData"/> 实例（同一实例才能共享已读态）。
/// </summary>
public static class BookLibrary
{
    /// <summary>《野外生存指南》——读完给"野外陷阱"配方（桩）。</summary>
    public static BookData WildernessSurvivalGuide() => new(
        id: "wilderness_survival_guide",
        title: "野外生存指南",
        body: WildernessSurvivalGuideBody,
        grantsRecipeStub: "recipe:wilderness_trap", // 桩：配方系统后续接
        readHours: 24); // draft：技术工具书，读得久

    /// <summary>《农场主的一百个问题》——读完给"作物种植"配方（桩）。</summary>
    public static BookData FarmerHundredQuestions() => new(
        id: "farmer_hundred_questions",
        title: "农场主的一百个问题",
        body: FarmerHundredQuestionsBody,
        grantsRecipeStub: "recipe:crop_planting", // 桩：配方系统后续接
        readHours: 24); // draft：技术工具书，读得久

    /// <summary>《裁缝手记》（纺织书，draft）——读过它的制作者解锁粗布背心一类缝纫配方。</summary>
    public static BookData TailorsNotes() => new(
        id: "tailors_notes",
        title: "裁缝手记",
        body: TailorsNotesBody,
        grantsRecipeStub: "recipe:cloth_vest", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 20); // draft：技术手记，读得较久

    /// <summary>《土法化学笔记》（化学书，draft）——读过它的制作者解锁火药 / 鞣制药水一类化学配方。</summary>
    public static BookData FolkChemistryNotes() => new(
        id: "folk_chemistry_notes",
        title: "土法化学笔记",
        body: FolkChemistryNotesBody,
        grantsRecipeStub: "recipe:gunpowder", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 20); // draft：技术笔记，读得较久

    /// <summary>《木匠入门》（木工书，draft）——读过它的制作者解锁木椅 / 自制弓一类木工配方（一本管两条，同构土法化学笔记）。</summary>
    public static BookData CarpentryBasics() => new(
        id: "carpentry_basics",
        title: "木匠入门",
        body: CarpentryBasicsBody,
        grantsRecipeStub: "recipe:chair", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 20); // draft：技术工具书，读得较久

    /// <summary>
    /// 《进阶木匠技术》（木工进阶书，draft）——**前置**《木匠入门》：没读完前置照样能读，但读速极慢（×0.2）。
    /// 读完解锁什么**待用户指定**（暂作占位、不挂配方产出）。
    /// </summary>
    public static BookData AdvancedCarpentry() => new(
        id: "advanced_carpentry",
        title: "进阶木匠技术",
        body: AdvancedCarpentryBody,
        grantsRecipeStub: null, // 解锁效果待用户指定（占位书）
        readHours: 28, // draft：进阶技术书，篇幅更长
        prerequisiteBookId: "carpentry_basics"); // 前置链首条数据：没读入门读得极慢

    /// <summary>
    /// 日记A（金手指帮根据地，克莉丝汀尸旁）——两个普通帮众视角：灾后互助、参与暴行、"金手指帮"命名由来。
    /// 纯叙事物品，无配方产出（桩留空）。正文为占位草稿，最终由用户手写。
    /// </summary>
    public static BookData GoldfingerDiaryA() => new(
        id: "goldfinger_diary_a",
        title: "一本卷边的日记（其一）",
        body: GoldfingerDiaryABody,
        grantsRecipeStub: null, // 无配方，纯 lore
        readHours: 6); // draft：纯 lore 日记，读得快

    /// <summary>
    /// 日记B（哥顿上吊尸旁，守林人小屋，与日记A 异地）——金手指帮文化起源 + 帮主哥顿身世/自杀。
    /// 纯叙事物品，无配方产出（桩留空）。正文为 draft 草稿，最终由用户优化。
    /// </summary>
    public static BookData GoldfingerDiaryB() => new(
        id: "goldfinger_diary_b",
        title: "一本硬壳笔记（其二）",
        body: GoldfingerDiaryBBody,
        grantsRecipeStub: null, // 无配方，纯 lore
        readHours: 6); // draft：纯 lore 日记，读得快

    /// <summary>《弓与箭之道》书 id（稳定键）。它给的是**被动加成**（四项，见下），不解锁配方。</summary>
    public const string WayOfBowAndArrowId = "way_of_bow_and_arrow";

    /// <summary>
    /// 《弓与箭之道》——**项目里第一本"给被动加成"而非"解锁配方"的书**（用户拍板）。
    /// <para>
    /// <b>四项效果</b>（用户写在数值表『书籍』页「效果」列）：
    /// <list type="bullet">
    /// <item>箭矢回收率 <b>25% → 50%</b>（<see cref="Archery.ArrowRecoveryRate"/>）。</item>
    /// <item>弓弩<b>射程 +10%</b>（<see cref="Archery.BookRangeMult"/>）。</item>
    /// <item>弓弩<b>锥形角 −10%</b>（散布收窄＝更准，<see cref="Archery.BookSpreadMult"/>）。</item>
    /// <item>弓弩<b>攻速 +2%</b>（<see cref="Archery.BookAttackSpeedMult"/>；折到出手间隔上是 ×1/1.02）。</item>
    /// </list>
    /// 后三项在 <see cref="Archery.Combine"/> 里与**箭的同轴系数连乘**（乘算不加算，CLAUDE.md 铁律），
    /// 且只碰弓弩——读了射艺书不会让你的步枪打得更远。
    /// </para>
    /// <para>
    /// 四项都是射手**本人**读完才算数（判据＝其 <see cref="ReadBookSet"/>，与配方书门槛同一个对象）：
    /// 加成属于**人**，不属于箭，也不属于营地。
    /// </para>
    /// <para>
    /// 这不是新架构：<c>MedicalBookPoints</c>（读过的医疗书 → 手术加点）早就确立了同一套模式——
    /// 引擎只吃一个值，"读没读过"由调用方从读者的已读书集里取。本书照抄该模式。
    /// </para>
    /// <para>
    /// <b>不可制作</b>（无配方，只能搜刮）：书就该是捡的。基础 25% 的回收率意味着弓弩养起来很吃力，
    /// 而这本书正好把它减半——于是它是**弓弩流的硬前置**：找不到它，你就养不起一个弓手。
    /// </para>
    /// </summary>
    public static BookData WayOfBowAndArrow() => new(
        id: WayOfBowAndArrowId,
        title: "弓与箭之道",
        body: WayOfBowAndArrowBody,
        grantsRecipeStub: null, // 刻意为空：它不解锁配方，给的是被动加成（回收率翻倍）
        readHours: 18);         // draft：技艺书，比纯 lore 长、比工程手册短

    /// <summary>《机械之美》书 id（弩的解锁书；用户拍板的书名）。</summary>
    public const string MechanicalBeautyId = "mechanical_beauty";

    /// <summary>
    /// 《机械之美》（[SPEC-B21·T26 追加] <b>书名是用户给的</b>：「《机械之美》用武器零件造」）——
    /// <b>解锁：单手轻弩 / 双手重弩</b>（从《木匠入门》挪来）。
    ///
    /// <para>
    /// 🔴 <b>正文＝占位，待用户 authored。</b> 用户只给了书名，没给正文。剧情/文案是 authored 内容
    /// （CLAUDE.md 铁律：代码不做程序化引申），故这里只放一句<b>最中性的功能性描述</b>，
    /// <b>不编世界观、不编作者、不编来历</b>。用户写好后整段替换 <see cref="MechanicalBeautyBody"/> 即可，代码不用动。
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>本书目前没有任何投放点 ⇒ 弩暂时造不出来。</b>
    /// 单手轻弩/双手重弩<b>搜刮不到</b>（全图 0 处投放，只能造）⇒ 书拿不到，这两把弩就<b>在游戏里不存在</b>。
    /// <b>"这本书从哪来"是设计决策（搜刮书？搜哪个点？开局有？），已 [DECISION] 上抛用户，代码不许自己塞。</b>
    /// 用户点头后，往 <c>ExplorationCache</c>（或商人货架）加一条投放即可 —— 一行的事。
    /// （对照：复合弩<b>有</b> 3 处投放，故"弩"这个武器类本身不会消失，消失的是**可制作的那两把**。）
    /// </para>
    ///
    /// <para><b>不设前置书</b>：用户没说它要接在哪本后面（对照《进阶木匠技术》前置《木匠入门》）。</para>
    /// </summary>
    public static BookData MechanicalBeauty() => new(
        id: MechanicalBeautyId,
        title: "机械之美",
        body: MechanicalBeautyBody,
        grantsRecipeStub: null, // 书门槛已实装（RecipeBook.RequiredBookIds），此桩仅作叙事标记，无须占位
        readHours: 24);         // draft：机械工程手册，与《野外生存指南》同档（技术工具书，读得久）

    /// <summary>全部内置书的全新实例（每次调用新建，已读态不共享）。</summary>
    public static IReadOnlyList<BookData> All() => new[]
    {
        WildernessSurvivalGuide(),
        FarmerHundredQuestions(),
        TailorsNotes(),
        FolkChemistryNotes(),
        CarpentryBasics(),
        AdvancedCarpentry(),
        WayOfBowAndArrow(),
        MechanicalBeauty(),
        GoldfingerDiaryA(),
        GoldfingerDiaryB(),
    };

    // 🔴 **占位正文 —— 待用户 authored，别替他写。**
    // 用户只给了书名《机械之美》和一句「用武器零件造」。这里只陈述这本书**在机制上是什么**
    // （读完解锁两把弩），不编作者、不编来历、不编世界观 —— 那些是 authored 内容，只有用户能写。
    // 别的书的正文都是有文风的散文；这一段刻意保持干巴巴，正是为了让人一眼看出"它还没写"。
    private const string MechanicalBeautyBody =
        "（正文待补）一本讲机括与传动的书。读完它的人能用零件装出单手轻弩与双手重弩。";

    // draft 待用户改 —— 技艺书《弓与箭之道》：不解锁配方，读完把箭矢回收率 25% → 50%
    private const string WayOfBowAndArrowBody =
        "一本薄薄的射艺小册，封面上是一张画得极认真的弓。扉页题着一句话：\n\n" +
        "\"射出去的箭，一半的功夫在把它捡回来。\"\n\n" +
        "书里没怎么讲怎么瞄准——作者显然觉得那是各人的事。整整三章都在讲别的：" +
        "怎么挑不劈的木头，怎么让箭杆经得起一次次撞击，怎么从尸体、树干和泥里把箭起出来而不折断它，" +
        "以及最要紧的——射之前先想好，这一箭你捡不捡得回来。\n\n" +
        "\"新手一天能射光一筒箭。老手一筒箭能用一个月。\"\n" +
        "\"区别不在准头。在于老手每射一箭之前，都已经知道那支箭会落在哪里。\"";

    // 用户手写终稿（T21 从数值表『书籍』页正文列同步，逐字照抄——authored 内容，勿润色）
    private const string WildernessSurvivalGuideBody =
        "\"活下去不靠运气，靠的是在天黑前把该做的都做完。\"\n" +
        "第一章 取火。第二章 净水。第三章 用一根铁丝和一点耐心，让林子替你狩猎——书里画着一套简陋却致命的陷阱，只要照着做，晚上就不必空着肚子睡。";

    // 用户手写终稿（T21 同步，逐字照抄）
    private const string FarmerHundredQuestionsBody =
        "\"问：土豆几时下种？答：清明前后，看你的地，也看你的胆量。\"\n" +
        "\"问：一块地能养活几口人？答：伺候得好，比你想的多；伺候不好，一个都不剩。\"\n" +
        "一百个问题，一百个答案，藏着从一粒种子到一顿饱饭的全部门道。";

    // draft 待用户改 —— 纺织书《裁缝手记》：解锁粗布背心一类缝纫配方
    private const string TailorsNotesBody =
        "一本用粗线装订的手记，纸页间还夹着几缕褪色的棉线。\n\n" +
        "\"针脚要密，密了才挡风；线头要藏，藏了才耐磨。\"\n\n" +
        "从量体、裁片到缝合，作者把一件挡身的粗布背心拆成了十几道工序，" +
        "一笔一画描得清清楚楚。照着做，几块破布也能变成能穿的东西。";

    // draft 待用户改 —— 化学书《土法化学笔记》：解锁火药 / 鞣制药水一类化学配方
    private const string FolkChemistryNotesBody =
        "封面被药水浸出几块焦黄，翻开一股刺鼻的酸味似乎还没散尽。\n\n" +
        "\"配比错一分，是废料；错一钱，是要命。动手前先把窗户打开。\"\n\n" +
        "笔记里记满了土法配方——如何把硝石、木炭和硫磺研成火药，" +
        "如何调一锅鞣制生皮的药水。字迹潦草，却每一步都标着分量与火候。";

    // draft 待用户改 —— 木工书《木匠入门》：解锁木椅 / 自制弓一类木工配方
    private const string CarpentryBasicsBody =
        "一本封皮沾满木屑的旧册子，翻开时还簌簌往下掉。\n\n" +
        "\"量两遍，锯一遍。急着下刀的人，做出来的东西也急着散架。\"\n\n" +
        "从认木料、开榫到打磨收边，作者把一把结实的椅子、一张能拉满的弓，" +
        "拆成一道道看得见摸得着的工序。照着做，几根木头也能拼成能坐能用的东西。";

    // draft 待用户改 —— 木工进阶书《进阶木匠技术》：前置《木匠入门》；解锁效果待用户指定
    private const string AdvancedCarpentryBody =
        "纸页泛黄，字里行间满是术语与受力草图，像是写给已经会做椅子的人看的。\n\n" +
        "\"没摸熟基本功就翻到这一页的，先回去把《木匠入门》读透——否则这里每一句你都得多琢磨半天。\"\n\n" +
        "开卯连接、层压弯曲、承重结构……门道比入门深了不止一层，" +
        "读起来也慢得多。但真吃透了，能造的就不只是能用的东西了。";

    // 用户手写终稿（T21 同步，逐字照抄）—— 日记A：两个普通帮众视角（互助求生 / 参与暴行 / "金手指帮"命名由来）
    private const string GoldfingerDiaryABody =
        "字迹潦草，像是就着火光匆匆写下的。\n" +
        "灾变头一个月，是老陈拉了我一把。我们俩背靠背，从加油站一路抢到城郊，谁也没抛下谁——那时候我还觉得，能活下来的都是好人。\n" +
        "后来我们撞上了这伙人。有吃的，有墙，有武器。代价是，你得跟他们一样。\n" +
        "第一次把那女人拖进屋的时候，我手在抖。老大说，就该如此拿在手里，拿不住的，不配活。老陈先动的手，我跟上了。往后就不抖了。\n" +
        "他们管自己叫\"金手指\"。老大说，指头是男人身上最诚实的东西——它按住扳机，也按住女人，它做过的事，脑子可以不认，指头认。\n" +
        "所以入伙那天，每个人都要用手指在她们身上留下印子。这就是规矩。";

    // 用户手写终稿（T21 同步，逐字照抄）—— 日记B：哥顿身世/以暴掩懦/看透后自杀。
    // ⚠️ 第 4 行句末无标点是用户原样，勿"修正"。
    private const string GoldfingerDiaryBBody =
        "硬壳笔记本，扉页只写着一个名字：哥顿。字迹清秀得似是小女生字样。\n" +
        "我母亲从不许我父亲说完一句话。父亲低着头吃饭、低着头挨骂、低着头老去。我恨他的软弱，也害怕自己就是他。\n" +
        "变尸的那天，是母亲先咬穿了父亲的喉咙。他没有反抗，甚至没有后退——他就那样看着她扑上来，像是终于等到了什么。我躲在门后，一动没动。\n" +
        "我用稿子砸穿了母亲的头，比我想象得简单得多，我早该这么做了\n" +
        "后来我立了规矩，教弟兄们如何拿捏一个女人。\n" +
        "可这些天我总在想——把她们折磨到死，我究竟证明了什么？\n" +
        "我意识到，我不过是那个躲在门后、连喊都不敢喊的孩子。\n" +
        "什么都没有。只有我自己那点没用的怕。\n" +
        "守林人小屋后院那棵老树的横枝够粗，绳子我也试过了。\n" +
        "没人认得我，也没人替我说话——很好，再见了这个操蛋的世界，我很荣幸让它变得更加操蛋。";
}
