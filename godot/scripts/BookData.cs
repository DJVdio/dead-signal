using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs / CampResources.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 书的底层数据：一本可阅读书的 id / 标题 / 正文 / 已读标记 / 配方产出（本轮留桩）。
// Item(书类) 只用 book id 引用本条目；已读态是运行时可变状态，故本类非不可变（区别于 Item）。

/// <summary>
/// [T59] <b>「书」与「日记」是两种东西，不是两个数值档位</b>（用户拍板）：
/// <list type="bullet">
/// <item><b>书</b>给<b>角色</b>读 —— 花的是角色的时间，换来的是能力（解锁配方）。</item>
/// <item><b>日记</b>给<b>玩家</b>读 —— 一个点开就能看全文的<b>道具</b>，游戏冻结着看，不花任何角色的时间，只讲故事。</item>
/// </list>
/// </summary>
public enum BookKind
{
    /// <summary>技术工具书：角色坐下来读，耗 <see cref="BookData.ReadHours"/>，读完解锁配方/被动。</summary>
    Manual,

    /// <summary>日记/笔记：<b>道具</b>。玩家点开即看全文，**无阅读工时**，不解锁任何东西。</summary>
    Diary,
}

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

    /// <summary>
    /// 玩家在游戏里看到的**短简介**（一句 flavor，区别于 <see cref="Body"/> 的整段正文）。
    /// 与武器/护甲的 <c>Description</c> 同一角色：抽取器把它同步进 wiki『书籍』表的「简介」列。
    /// 日记不填（<see cref="Diary"/> 恒空）。
    /// </summary>
    public string Description { get; }

    /// <summary>读完给的配方 id（**桩**，配方系统后续接；无产出则为 <c>null</c>）。</summary>
    public string? GrantsRecipeStub { get; }

    /// <summary>是否已读（运行时可变；<see cref="MarkRead"/> 置位）。</summary>
    public bool IsRead { get; private set; }

    /// <summary>
    /// 读完本书所需的游戏内小时：读书为耗时活动，读者按 <see cref="ReadingProgress"/> 累计到此值即读完。
    /// <para>🔴 <b>这是"书"独有的字段 —— 它就是书的代价</b>（读书的那个角色整夜占着座位，不能站岗、不能干活）。
    /// <b>日记恒为 0</b>：日记是给玩家看的道具，不由角色去读，**根本没有"工时"这回事**（见 <see cref="Kind"/>）。</para>
    /// </summary>
    public double ReadHours { get; }

    /// <summary>
    /// **前置书**（通用书籍前置链，可空 = 无前置）：读本书时若读者尚未读完此前置书，**不禁止**阅读，
    /// 但读速 ×<see cref="ReadingSpeed.MissingPrerequisiteMultiplier"/>（即耗时数倍，见 <see cref="ReadingSpeed.PrerequisiteFactor"/>）。
    /// 数据驱动：链上每本书声明各自前置即可（如《进阶木匠技术》←《木匠入门》）。系数 draft 待调。
    /// </summary>
    public string? PrerequisiteBookId { get; }

    /// <summary>
    /// 🔴 <b>[T59] 这一条是"书"还是"日记"</b>——用户拍板的分类，**不是两个数值档位，是两种东西**：
    /// <list type="bullet">
    /// <item><b>书</b>（<see cref="BookKind.Manual"/>）：<b>角色</b>读。代价＝**角色的时间**
    ///       （整夜、占座位、读书的人不能站岗也不能干活）⇒ <see cref="ReadHours"/> 就是它的代价。作用＝解锁配方/能力。</item>
    /// <item><b>日记</b>（<see cref="BookKind.Diary"/>）：<b>玩家</b>读。是**道具**，点开就能看全文，
    ///       看的时候游戏是**冻结**的（库存面板持有时标）⇒ **零角色时间**。作用＝纯叙事。
    ///       ⇒ <b>它根本不该有"阅读工时"</b>（<see cref="ReadHours"/> 恒 0，见 <see cref="Diary"/>）。</item>
    /// </list>
    /// <para><b>此前是个真 bug</b>：日记被做成了 <c>readHours: 6</c> 的书，会出现在**夜间读书指派**列表里 ——
    /// 于是玩家可以派一个幸存者<b>整夜坐着读一本给玩家看的叙事文本</b>（那一夜他不能站岗、不能干活），
    /// 而日记 <see cref="GrantsRecipeStub"/> 为 null ⇒ <b>什么也换不到</b>。**给玩家看的文本被做成了角色的劳动。**
    /// 现已按用户口径修正：日记不进读书指派（见 <c>CampMain.PopulateReadingPanel</c>）。</para>
    /// </summary>
    public BookKind Kind { get; }

    /// <summary>是不是日记（＝道具，玩家点开看，不吃角色时间）。</summary>
    public bool IsDiary => Kind == BookKind.Diary;

    public BookData(string id, string title, string body, string? grantsRecipeStub = null, double readHours = 12,
        string? prerequisiteBookId = null, BookKind kind = BookKind.Manual, string description = "")
    {
        Id = id;
        Title = title;
        Body = body;
        GrantsRecipeStub = grantsRecipeStub;
        ReadHours = readHours;
        PrerequisiteBookId = prerequisiteBookId;
        Kind = kind;
        Description = description;
    }

    /// <summary>
    /// 造一本<b>日记</b>（道具，非书）：<b>没有阅读工时</b>（恒 0）、不解锁任何配方、无前置。
    /// <para>玩家在库存里点开它就能看全文（游戏冻结），**不消耗任何角色的时间**。</para>
    /// </summary>
    public static BookData Diary(string id, string title, string body) => new(
        id: id,
        title: title,
        body: body,
        grantsRecipeStub: null,   // 日记不解锁任何东西：它是叙事，不是能力
        readHours: 0,             // 🔴 不适用——日记不由角色去"读"，故没有工时这个字段
        prerequisiteBookId: null,
        kind: BookKind.Diary);

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
        grantsRecipeStub: "recipe:snare_trap", // 桩：配方系统后续接（对齐真配方 id snare_trap；本字段仅叙事标记，门槛真源是 RecipeBook.RequiredBookIds）
        readHours: 4, // [T59] 用户在 wiki 上定的（原 24h）
        description: "书里教你分辨哪种蘑菇能吃。至于分辨不出的那些——总有人先替你试过了。");

    /// <summary>
    /// 《农场主的一百个问题》——[T67] <b>桩已换成真的</b>：读完解锁 <b>捕鸟陷阱</b> 与 <b>菜园</b>
    /// （门槛真源是 <see cref="RecipeBook"/> 里那两条配方的 <c>RequiredBookIds</c>，本字段只是叙事标记）。
    /// <para>
    /// 🔴 <b>它是整条弓箭线的第一块砖</b>：捕鸟陷阱 → 鸟 →【宰杀】→ 鸟肉 + <b>羽毛</b> → <b>三种箭</b>。
    /// 没读这本书 ⇒ 拿不到鸟 ⇒ 拿不到羽毛 ⇒ <b>一支箭都造不出来</b>。
    /// 它<b>开局就在营地共享库存里</b>（<c>camp.json</c> 住宅·柜子），与《野外生存指南》同架——
    /// 于是"开局先读哪一本"成了个真选择：<b>野外生存指南给你弓，农场主给你箭</b>；阅读工时以 Wiki 配置为准。
    /// </para>
    /// </summary>
    public static BookData FarmerHundredQuestions() => new(
        id: "farmer_hundred_questions",
        title: "农场主的一百个问题",
        body: FarmerHundredQuestionsBody,
        grantsRecipeStub: "recipe:" + BirdTrapSpec.RecipeId + "," + CropPlotSpec.RecipeId, // [T67] 桩换成真配方 id
        readHours: 4, // [T59] 用户在 wiki 上定的（原 24h）
        description: "一百个问题，现在只剩一个还要紧：这块地，撑不撑得到收成。");

    /// <summary>《裁缝手记》（纺织书，draft）——读过它的制作者解锁粗布背心一类缝纫配方。</summary>
    public static BookData TailorsNotes() => new(
        id: "tailors_notes",
        title: "裁缝手记",
        body: TailorsNotesBody,
        grantsRecipeStub: "recipe:cloth_vest", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 8, // [T59] 用户在 wiki 上定的（原 20h）
        description: "针脚密一点，能多挡一阵风。挡不挡得住牙，书里没写——写它的时候还不需要写。");

    /// <summary>《土法化学笔记》（化学书，draft）——读过它的制作者解锁火药 / 鞣制药水一类化学配方。</summary>
    public static BookData FolkChemistryNotes() => new(
        id: "folk_chemistry_notes",
        title: "土法化学笔记",
        body: FolkChemistryNotesBody,
        grantsRecipeStub: "recipe:gunpowder", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 8, // [T59] 用户在 wiki 上定的（原 20h）
        description: "配比错一分是废料，错一钱是要命。这本书最实在的一句，是叫你动手前先把窗户打开。");

    /// <summary>《木匠入门》（木工书，draft）——读过它的制作者解锁木椅 / 自制弓一类木工配方（一本管两条，同构土法化学笔记）。</summary>
    public static BookData CarpentryBasics() => new(
        id: "carpentry_basics",
        title: "从零到一学会木匠", // [T59] 用户在 wiki 书籍表把书名从「木匠入门」改成此名（id 不变）
        body: CarpentryBasicsBody,
        grantsRecipeStub: "recipe:chair", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 8, // [T59] 用户在 wiki 上定的（原 20h）
        description: "量两遍，锯一遍。做椅子的耐心和活下去的耐心，原来是同一种。");

    /// <summary>
    /// 《进阶木匠技术》（木工进阶书，draft）——**前置**《木匠入门》：没读完前置照样能读，但读速按配置衰减。
    /// 读完解锁沙发、加固门/围栏，并给消防斧攻速加成（具体效果由对应消费层登记）。
    /// </summary>
    public static BookData AdvancedCarpentry() => new(
        id: "advanced_carpentry",
        title: "进阶木匠技术",
        body: AdvancedCarpentryBody,
        grantsRecipeStub: "recipe:" + SofaSpec.RecipeId, // 桩：配方门槛已实装（RecipeBook.RequiredBookIds）
        readHours: 12, // [T59] 用户在 wiki 上定的（原 28h）
        prerequisiteBookId: "carpentry_basics", // 前置链首条数据：没读入门读得极慢
        description: "它先教你把家里的门加固一道，再顺手教你把消防斧抡得更快。没写的是：这两样，迟早要在同一个晚上用上。");

    /// <summary>
    /// 日记A（金手指帮根据地，克莉丝汀尸旁）——两个普通帮众视角：灾后互助、参与暴行、"金手指帮"命名由来。
    /// 纯叙事物品，无配方产出（桩留空）。正文为占位草稿，最终由用户手写。
    /// </summary>
    public static BookData GoldfingerDiaryA() => BookData.Diary(
        id: "goldfinger_diary_a",
        title: "一本卷边的日记（其一）",
        body: GoldfingerDiaryABody);   // [T59] 道具，非书：无工时、不吃角色时间

    /// <summary>
    /// 日记B（哥顿上吊尸旁，守林人小屋，与日记A 异地）——金手指帮文化起源 + 帮主哥顿身世/自杀。
    /// 纯叙事物品，无配方产出（桩留空）。正文为 draft 草稿，最终由用户优化。
    /// </summary>
    public static BookData GoldfingerDiaryB() => BookData.Diary(
        id: "goldfinger_diary_b",
        title: "一本硬壳笔记（其二）",
        body: GoldfingerDiaryBBody);   // [T59] 道具，非书：无工时、不吃角色时间

    /// <summary>《弓与箭之道》书 id（稳定键）。它给的是**被动加成**（四项，见下），不解锁配方。</summary>
    public const string WayOfBowAndArrowId = "way_of_bow_and_arrow";

    /// <summary>
    /// 《弓与箭之道》——**项目里第一本"给被动加成"而非"解锁配方"的书**（用户拍板）。
    /// <para>
    /// <b>效果</b>（用户写在数值表『书籍』页「效果」列）：
    /// <list type="bullet">
    /// <item>箭矢回收、弹道速度、散布与攻速效果以 Wiki 配置为准。</item>
    /// </list>
    /// 散布/攻速两项在 <see cref="Archery.Combine"/> 里与**箭的同轴系数连乘**（乘算不加算，CLAUDE.md 铁律），
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
    /// <b>不可制作</b>（无配方，只能搜刮）：书就该是捡的。基础回收率以 Wiki 配置为准，
    /// 而这本书正好把它减半——于是它是**弓弩流的硬前置**：找不到它，你就养不起一个弓手。
    /// </para>
    /// </summary>
    public static BookData WayOfBowAndArrow() => new(
        id: WayOfBowAndArrowId,
        title: "弓与箭之道",
        body: WayOfBowAndArrowBody,
        grantsRecipeStub: null, // 刻意为空：它不解锁配方，给的是被动加成（回收率翻倍）
        readHours: 12,          // [T59] 用户在 wiki 上定的（原 18h）
        description: "新手一天射光一筒箭，老手一筒箭用一个月。差的不是准头——是他射出去之前，就知道那支箭捡不捡得回来。");

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
        readHours: 8,          // [T59] 用户在 wiki 上定的（原 24h）
        description: "一本讲机括与传动的书。文明留下的最后一点巧劲，如今刚好够你用零件扣响一次扳机。");

    /// <summary>全部内置书的全新实例（每次调用新建，已读态不共享）。</summary>
    /// <summary>《弓制作指南》书 id（稳定键）。</summary>
    public const string BowCraftingGuideId = "bow_crafting_guide";

    /// <summary>
    /// 《弓制作指南》—— [T59] <b>用户在 wiki 上新加的一本书</b>（他写的效果：
    /// 「解锁短工、反曲弓、长弓、自制箭。解锁弓改装。」——「短工」应为「短弓」笔误）。
    ///
    /// <para><b>它做的是一次"解锁图重构"</b>：把**造弓**这件事从《进阶木匠技术》里拆出来，单独成一本。
    /// 于是《进阶木匠技术》回归它的本行（营地木工 + 消防斧），造弓另有师承。</para>
    ///
    /// <para>🔴 <b>本轮只挪《进阶木匠技术》名下的两把弓（反曲弓 / 长弓）</b>，**保守取最小改动面**：
    /// <list type="bullet">
    /// <item><b>短弓</b>留在《野外生存指南》—— 用户**没有**把它从那本书的效果列里删掉（两本书都写了它）。
    ///       书门槛是 <b>AND</b>（<c>CraftingLogic</c> 逐本校验），挪过来＝改变行为，故不动。</item>
    /// <item><b>自制箭</b>留在《弓与箭之道》—— 同理，用户也没从那本里删掉它。</item>
    /// <item><b>两把弩</b>留在《机械之美》—— 用户的新书文案里**一个"弩"字都没有**，不擅自并进来。</item>
    /// <item><b>「解锁弓改装」</b>没做 —— 弓弩改装的白名单已被 <c>impl-sync-all</c> 按用户口径**收窄掉**了
    ///       （弓弩不吃枪械改装），"用一本书把它解锁回来"是**引擎新轴**（书 → 改装白名单，目前不存在这条路）。</item>
    /// </list>
    /// 这几处的重叠/含糊已 [DECISION-RESOLVED] 记档，用户回来一句话就能再挪。</para>
    ///
    /// <para>🔴 <b>必须有投放点，否则弓当场变成造不出来的死物品</b>：反曲弓/长弓此前挂在《进阶木匠技术》上，
    /// 而那本书**有**投放（联合收割机仓库·阁楼）。把它们挪到一本**没人能捡到**的新书上，
    /// 等于把两把弓从游戏里删掉（《机械之美》当初就踩过这个坑）。
    /// ⇒ 投在<b>守林人小屋·阁楼</b>：那里本就是"弓箭的家"（箱底压着他的狩猎弓与箭，床底是他的《弓与箭之道》）——
    /// 一个自己做弓的护林员，书就该在他手边。<b>消防斧与《进阶木匠技术》仍同馆</b>（联合收割机仓库），那条设计一格没动。</para>
    ///
    /// <para>正文＝**占位**（authored 待用户写，同《机械之美》的处置：只陈述机制，不编作者/来历/世界观）。
    /// 阅读时长 <b>8h</b> 由我拟定 —— 与同为技术工具书的《裁缝手记》《土法化学笔记》《木匠入门》同档（用户没给这个数）。</para>
    /// </summary>
    public static BookData BowCraftingGuide() => new(
        id: BowCraftingGuideId,
        title: "弓制作指南",
        body: BowCraftingGuideBody,
        grantsRecipeStub: "recipe:recurve_bow", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 8,                           // 拟定：技术工具书，与裁缝/化学/木匠入门同档
        description: "选材、开背、上弦、调力——把一根木头变成一张弓。做弓的人从不问你拿它打猎，还是打人。");

    // 🔴 **占位正文 —— 待用户 authored，别替他写。**（同 MechanicalBeautyBody 的口径）
    private const string BowCraftingGuideBody =
        "一本讲怎么把一根木头变成一张弓的手册：选材、开背、上弦、调力。\n"
        + "弓背要顺着木纹开，弦槽不能伤到木芯；拉力不是越大越好，能稳定回到原位才算一张能用的弓。\n\n"
        + "读完它，你就能自己做反曲弓和长弓了。";

    /// <summary>《尖峰时刻》书 id（稳定键）。它是一本滑雪极限运动书，读完解锁「自制简易墨镜」。</summary>
    public const string PeakHourId = "peak_hour";

    /// <summary>
    /// [T71]《尖峰时刻》—— <b>用户在 wiki 上新加的一本书</b>（滑雪极限运动题材，末世基调）。
    /// <para>
    /// <b>解锁「自制简易墨镜」</b>（木缝雪镜；门槛真源在 <c>RecipeBook</c> 的 <c>snow_goggles</c> 配方
    /// <c>RequiredBookIds</c>，<see cref="GrantsRecipeStub"/> 只是叙事标记）。用户既 authored 了这本书，
    /// 又 authored 了它解锁的护甲，本条把书籍效果与该装备接起来；护甲数值以 Wiki 配置为准。
    /// </para>
    /// <para>
    /// 🔴 <b>正文＝用户明确授权代笔</b>（本会话罕见的 authored 授权：用户点名让我方补这一本的正文）。
    /// 主题滑雪极限运动、末世基调、克制不跳戏；末句是刻意的钩子（把"极限运动"接向"末世求生"，
    /// 不点破，留给发现现场的玩家自己体会）。<b>阅读 6h</b> 为用户在 wiki 上定的值。
    /// </para>
    /// <para>
    /// 🔴 <b>必须有投放点</b>（否则解锁它的雪镜就成了造不出的死物品，同《机械之美》踩过的坑）：
    /// 投在 <b>城市之巅瞭望观景台·瞭望员值班室</b>（<c>ExplorationCache.LookoutWardensRoomId</c>）——
    /// 一个在城市之巅高处常年值守的人，手边压着一本讲登顶与陡坡的书，题名与地名（"之巅"／"尖峰"）也应和。
    /// </para>
    /// </summary>
    public static BookData PeakHour() => new(
        id: PeakHourId,
        title: "尖峰时刻",
        body: PeakHourBody,
        grantsRecipeStub: "recipe:snow_goggles", // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记
        readHours: 6,                             // [T71] 用户在 wiki 上定的
        description: "一个滑雪的人，写他怎么在崩塌的雪坡上让自己不慌。他管这叫全世界最没用的本事——直到世界真的开始崩塌。");

    /// <summary>《尖峰时刻·二》——读完解锁厚重裤子与厚重披风。</summary>
    public const string PeakHourTwoId = "peak_hour_2";

    public static BookData PeakHourTwo() => new(
        id: PeakHourTwoId,
        title: "尖峰时刻·二",
        body: PeakHourTwoBody,
        grantsRecipeStub: "recipe:heavy_trousers,heavy_cape",
        readHours: 6,
        description: "卧槽，他还敢去第二次！");

    /// <summary>《尖峰时刻·三》——读完辨认葛根、大黄，并解锁雪地靴。</summary>
    public const string PeakHourThreeId = "peak_hour_3";

    public static BookData PeakHourThree() => new(
        id: PeakHourThreeId,
        title: "尖峰时刻·三",
        body: PeakHourThreeBody,
        grantsRecipeStub: "recipe:snow_boots",
        readHours: 6,
        description: "红牛赞助，他出钱，你出命。");

    // [T71] 用户明确授权代笔的正文（滑雪极限运动·末世基调）。末句「那时候我以为这是全世界最没用的本事」
    // 是刻意的钩子——把极限运动接向末世求生，不点破，保留"发现现场自己体会"的处理。
    private const string PeakHourBody =
        "没有人能征服一座山。你只是在它允许的那几秒里，从它身上滑过去。\n\n" +
        "陡坡教给我的第一课，是恐惧不会害死你——冻住的肌肉才会。雪面在脚下崩塌的时候，" +
        "你唯一能做的就是滑得比崩塌更快。停下，就是被埋。\n\n" +
        "我摔断过锁骨、两根肋骨、右手三根指头。每一次躺在雪里等救援，我都发誓再也不来了。" +
        "可开春我又站在了索道顶端。\n\n" +
        "后来我才想明白：我不是在跟山较劲。我是在练一件事——当脚下的一切都开始失控，怎么让自己不慌。\n\n" +
        "那时候我以为这是全世界最没用的本事。";

    private const string PeakHourTwoBody =
        "第二次上山，我带的是一条厚裤子和一块能挡风的旧布。\n\n" +
        "雪不会因为你穿得像个英雄就少落一片，但它会从裤脚、腰背和袖口一点点把热量拿走。" +
        "保暖不是舒服，是让你的腿在下一次迈步时还听你的话。\n\n" +
        "我把几层布料叠起来，用绳结把重量分散到腰上和肩上。披风挡不住牙，" +
        "却能让你在风里多站一会儿。山上没有观众，能走回来就是唯一的奖牌。";

    private const string PeakHourThreeBody =
        "雪线以下也有东西活着，只是它们不肯把名字写在叶子上。\n\n" +
        "葛根的藤和大黄的叶子，各有各的样子。认错一株，最轻的后果是白费力气，" +
        "更糟的后果不会在书里替你负责。先看形，再看生长的位置，最后才伸手。\n\n" +
        "至于靴子，先护住脚趾。脚趾冻僵以后，最短的路也会变成一场远征。";

    /// <summary>《枪械维修指南》书 id（稳定键）。</summary>
    public const string GunsmithRepairGuideId = "gunsmith_repair_guide";

    /// <summary>
    /// 《枪械维修指南》——神秘商人独家货源，与损坏的狙击枪互斥刷新。读完解锁狙击枪修复配方。
    /// <para>
    /// 正文＝占位，待用户 authored。只陈述机制，不编作者/来历/世界观。
    /// 阅读时长拟定 8h（同《裁缝手记》/《土法化学笔记》/《木匠入门》等技术工具书）。
    /// </para>
    /// <para>
    /// 投放：神秘商人（与损坏的狙击枪互斥刷新，不能同次/同一轮一起出现）。
    /// </para>
    /// </summary>
    public static BookData GunsmithRepairGuide() => new(
        id: GunsmithRepairGuideId,
        title: "枪械维修指南",
        body: GunsmithRepairGuideBody,
        grantsRecipeStub: "recipe:repair_sniper_rifle,rifle,pistol",
        readHours: 12,
        description: "一支枪，和一根烧火棍的区别，往往只差几个螺丝。这本书教你分清哪个该拧紧，哪个该扔掉。");

    // 🔴 占位正文 —— 待用户 authored（同 MechanicalBeautyBody / BowCraftingGuideBody 口径）
    private const string GunsmithRepairGuideBody =
        "不是每一杆老枪都能被你修好——但知道怎么判断它值不值得修，本身就是一门手艺。\n\n" +
        "先看枪管：膛线磨平了就是一根铁管子，救不回来。再看枪机：卡死的不一定废，" +
        "多半是某个簧片断了，换掉就能再响。至于瞄具——裂几道镜片不要紧，" +
        "你只要还能把准星和目标的影子叠在一起，它就还算得上一支枪。\n\n" +
        "读完它，你就能把一杆卡死的狙击枪重新修响——前提是你找得到零件。";

    // Wiki 是设计真源：用户新增行的内部稳定键就是 new_book_1..16。不可另造一套语义 id，
    // 否则抽取器会同时保留两行同名书，逐角色已读与配方门槛也会各认各的。
    public const string StreetFightingGuideId = "new_book_1";
    public const string MedicalFacilityStandardsId = "new_book_2";
    public const string EssenceOfSalesId = "new_book_3";
    public const string HundredFamilyRecipesId = "new_book_4";
    public const string LittleMouseDigsHolesId = "new_book_5";
    public const string ItalianChronicleId = "new_book_6";
    public const string FamilyFirstAidManualId = "new_book_7";
    public const string SharpesAutobiographyId = "new_book_8";
    public const string SpanishChronicleId = "new_book_9";
    public const string DjangoUnchainedId = "new_book_10";
    public const string IrishChronicleId = "new_book_11";
    public const string BritishChronicleId = "new_book_12";
    public const string VietnameseChronicleId = "new_book_13";
    public const string StoneBreakingGuideId = "new_book_14";
    public const string MalayChronicleId = "new_book_15";
    public const string TurkishChronicleId = "new_book_16";

    public static BookData StreetFightingGuide() => new(
        StreetFightingGuideId, "街头格斗指南",
        "街上没有裁判，也没有人等你摆好架势。\n\n" +
        "棍棒要抢中线，匕首要藏在视线下面。别追着四肢浪费力气：先打躯干让人喘不上气，再找头部结束争斗。" +
        "书页边上反复写着同一句话——能跑就跑；跑不了，第一下就别留情。",
        readHours: 6,
        description: "没有擂台、没有回合，也没有胜者采访。它教的只有一件事：在狭窄的街上，怎么用最短的一下让对方倒下。");

    public static BookData MedicalFacilityStandards() => new(
        MedicalFacilityStandardsId, "医疗单位建筑规范",
        "病菌喜欢接缝、裂缝和扫帚够不到的直角。墙角改成弧面，地面向排水口留一点坡度，床与床之间留出清洁通道。\n\n" +
        "这些要求从前看起来像官僚主义：尺寸精确到厘米，清洁流程精确到分钟。如今没有检查员了，" +
        "可少一处积血、少一块发霉的纱布，仍可能让整间营地少抬出去一个人。",
        readHours: 12,
        description: "简单地将墙角改成弧线，居然能少藏这么多污物。文明有时只是一群人认真讨论该怎么拖地。");

    public static BookData EssenceOfSales() => new(
        EssenceOfSalesId, "销售的本质",
        "价格不是写在货物上的数字，而是两个人对同一件东西有多着急。\n\n" +
        "先问，后报；先让对方说出缺什么，再告诉他你手里的东西为什么刚好值这个价。别急着填补沉默——" +
        "很多让步，都是卖家为了结束安静自己送出去的。末页只留了一行：成交之后还能再见面，才叫买卖。",
        readHours: 6,
        description: "教你把一句“就这些”说得像最后的库存。末世没有市场部，但讨价还价从未失业。");

    public static BookData HundredFamilyRecipes() => new(
        HundredFamilyRecipesId, "百家菜谱",
        "这不是名厨的菜谱，是一百户人家写下的省粮办法。剩饭怎么回锅，硬面包怎么泡软，骨头煮到第几遍还肯交出一点味道。\n\n" +
        "每页的分量都很小，办法却很细：先切碎，再慢煮；先让香味出来，再让饥饿相信碗里比看上去更多。" +
        "做饭的人省下的每一点，都够另一个人把今天熬过去。",
        readHours: 6,
        description: "一百户人家的锅底经验。");

    public static BookData LittleMouseDigsHoles() => new(
        LittleMouseDigsHolesId, "小老鼠爱打洞",
        "小老鼠为什么沿着墙根跑？因为开阔地上有猫头鹰。它为什么总钻同一个洞？因为熟悉的路最快。\n\n" +
        "彩色插图把洞口、粪粒、啃痕和脚印画得清清楚楚。给孩子看的问题都很简单，答案却正好能告诉大人：" +
        "套索该放在哪条路上，才不会只抓到一夜的风。",
        readHours: 3,
        description: "一本儿童科普读物。");

    public static BookData ItalianChronicle() => new(
        ItalianChronicleId, "意大利编年史",
        "城邦彼此戒备的年代，弩手需要在装填时活过对面的第一轮箭雨。工匠于是把小盾固定在弩身上，" +
        "让武器既能发射，也能遮住最要命的正面。\n\n" +
        "图稿详列盾面的弧度、重心与固定点。盾做得太大，弩便抬不起来；做得太小，只够替死人挡雨。",
        grantsRecipeStub: "mod:crossbow_shield", readHours: 6,
        description: "历史会重复，敌袭的方向也差不多。");

    public static BookData FamilyFirstAidManual() => new(
        FamilyFirstAidManualId, "家庭急救手册",
        "先确认呼吸，再止血，再处理那些看起来更吓人的伤。把器械按使用顺序摆好，动手前就决定下一步，" +
        "不要让病人躺在那儿等你翻目录。\n\n" +
        "书里的手术并不高明，只讲清洁、准备和熟练。可在时间与感染一同追上来的时候，少一次犹豫就多一分活路。",
        readHours: 8,
        description: "写给普通家庭的急救书。它不能让你成为医生，但能让你的手在必须动刀时少抖一会儿。");

    public static BookData SharpesAutobiography() => new(
        SharpesAutobiographyId, "夏普斯自传",
        "长刃需要空间，短剑和刺剑只需要一道缝。夏普斯写自己如何盯住对手的肩膀，而不是盯剑尖：肩先动，剑才到。\n\n" +
        "他从不把躲闪写成后退。侧半步，让开锋刃，再把自己的剑留在能反击的位置。活下来的秘诀并不光彩——" +
        "只是每次都比死人早看见一点。",
        readHours: 6,
        description: "如水一般。");

    public static BookData SpanishChronicle() => new(
        SpanishChronicleId, "西班牙编年史",
        "帝国缺枪的时候，乡镇工坊就用水管、弹簧和旧枪托拼出能响的东西。图纸不漂亮，公差也不统一，" +
        "但每个零件都能用手边的工具修出来。\n\n" +
        "猎枪、霰弹枪与短手枪各占一章。共同的警告写在每章开头：第一次试射时，把脸放远一点。",
        grantsRecipeStub: "recipe:improvised_hunting_gun,improvised_shotgun,improvised_pistol", readHours: 8,
        description: "粗糙、危险，但比赤手空拳多一个选择。");

    public static BookData DjangoUnchained() => new(
        DjangoUnchainedId, "被解救的姜戈",
        "一册被翻得起毛的通俗故事。主角戴着夸张的帽子，穿一双硬得能踢开门的马靴，袖口里藏着一把小得可笑的手枪。\n\n" +
        "故事把复仇写得痛快，把准备工作写得很短。可夹页上的服装尺寸、枪械剖面和改装笔记倒很认真——" +
        "上一位读者显然不只把它当故事看。",
        grantsRecipeStub: "recipe:dentist_pistol,cowboy_hat,riding_boots", readHours: 20,
        description: "被解救的只有姜戈。");

    public static BookData IrishChronicle() => new(
        IrishChronicleId, "爱尔兰编年史",
        "坏天气、贫瘠土和太多张等着吃饭的嘴，让人学会把土豆种得比日历更可靠。先催芽，再切块，" +
        "让每一块都带着芽眼；覆土不要一次压死，苗长一截就再培一层。\n\n" +
        "书里没有让作物凭空长快的奇迹，只有一套把等待提前做完的办法。十二小时不算多，挨饿时却很长。",
        readHours: 8,
        description: "一部饥荒与土地的旧记录。");

    public static BookData BritishChronicle() => new(
        BritishChronicleId, "英国编年史",
        "紫杉木要顺纹劈开，弓背留白木，弓腹留心木。一张长弓的力量不是来自粗，而是来自两层木质在拉伸与压缩之间各守本分。\n\n" +
        "编年史写国王和战役，附录却写弓匠、弓弦与练习场。那些没有留下名字的人，造出了能越过整片泥地的箭。",
        grantsRecipeStub: "recipe:longbow", readHours: 4,
        description: "从这里学会了竖中指。");

    public static BookData VietnameseChronicle() => new(
        VietnameseChronicleId, "越南编年史",
        "没有足够钢板，就把小片金属、厚布与能找到的硬材料一层层叠起来。护甲不必挡住所有东西，" +
        "只要让最先碰到你的那一下慢一点、偏一点。\n\n" +
        "书中反复强调重量要分散，接缝要错开。穿得动的防护才叫装甲；把人压在原地的，只是一口还没合盖的棺材。",
        grantsRecipeStub: "recipe:simple_armor", readHours: 3,
        description: "越南最爱藤甲兵。");

    public static BookData StoneBreakingGuide() => new(
        StoneBreakingGuideId, "胸口碎大石：从入门到入土",
        "第一章讲呼吸，第二章讲绷紧肌肉，第三章讲怎么挑一块看起来吓人、实际容易裂的石头。\n\n" +
        "真正挨下重击时，诀窍只剩两个：别把气吐光，别让力量全落在一点。作者在页脚郑重声明，" +
        "本书不能保证读者活着学完；书名已经尽到告知义务。",
        readHours: 4,
        description: "万一，我是说万一，他不是江湖骗子呢？");

    public static BookData MalayChronicle() => new(
        MalayChronicleId, "马来编年史",
        "甲板窄、舱门低，长兵器施展不开。水手把两把短刃分在左右手：一把逼开对方的刀，一把从空隙里进去。\n\n" +
        "双持不是两只手各打各的。肩、胯和脚步必须共用同一个节奏，否则第二把刀只会拖慢第一把。",
        readHours: 6,
        description: "海峡与甲板上的近战记录。");

    public static BookData TurkishChronicle() => new(
        TurkishChronicleId, "土耳其编年史",
        "一手持短枪，一手留着刀，旧骑兵把换武器的时间省成了一个动作。枪负责逼人停步，刀负责处理已经靠近的麻烦。\n\n" +
        "诀窍在于两件武器互不争抢身体：射击时刀手压住摆动，近战时枪手替突刺稳住肩线。各让出一点，反而都更听话。",
        readHours: 6,
        description: "刀刀烈火，枪枪好运！");

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
        BowCraftingGuide(),   // [T59] 用户新加
        PeakHour(),           // [T71] 用户新加（滑雪极限运动·解锁自制简易墨镜）
        PeakHourTwo(),        // Wiki 新增：厚重裤子 / 厚重披风
        PeakHourThree(),      // Wiki 新增：葛根 / 大黄识别与雪地靴
        GoldfingerDiaryA(),
        GoldfingerDiaryB(),
        GunsmithRepairGuide(), // [wiki-character-sync] 神秘商人独家货品
        StreetFightingGuide(),
        MedicalFacilityStandards(),
        EssenceOfSales(),
        HundredFamilyRecipes(),
        LittleMouseDigsHoles(),
        ItalianChronicle(),
        FamilyFirstAidManual(),
        SharpesAutobiography(),
        SpanishChronicle(),
        DjangoUnchained(),
        IrishChronicle(),
        BritishChronicle(),
        VietnameseChronicle(),
        StoneBreakingGuide(),
        MalayChronicle(),
        TurkishChronicle(),
    };

    /// <summary>
    /// <b>真正的"书"</b>（角色坐下来读、耗工时、读完解锁能力）—— **不含日记**。
    /// <para>🔴 <b>凡是"派谁去读什么"的地方，都必须用这个，而不是 <see cref="All"/></b>
    /// （<c>CampMain.PopulateReadingPanel</c> 就是拿它筛的）。用 All 会把日记也摆进夜间读书指派列表 ——
    /// 那正是 [T59] 修掉的 bug：玩家可以派人整夜读一本什么也不给的叙事文本。</para>
    /// </summary>
    public static IReadOnlyList<BookData> Manuals()
        => All().Where(b => !b.IsDiary).ToList();

    /// <summary>
    /// <b>日记</b>（道具：玩家在库存里点开就能看全文，游戏冻结，**零角色时间**）。
    /// <para>它们仍是库存里的 <see cref="Item.Book"/>（掉落/存档/图标全都照旧走书那条线），
    /// 变的只是**它不再是一件"可以派人去干"的活**。</para>
    /// </summary>
    public static IReadOnlyList<BookData> Diaries()
        => All().Where(b => b.IsDiary).ToList();

    private const string MechanicalBeautyBody =
        "机括不是魔法，是一连串互相咬合、彼此借力的零件。\n\n" +
        "先把弩臂固定，再让弦经过滑轮与卡扣；每一处松动，都会把力送回你的手指。" +
        "武器零件不是越多越好，关键是让它们在扣下扳机的那一刻同时到位。\n\n" +
        "照着图装，你能做出单手轻弩，也能做出需要双手稳住的重弩。";

    // 技艺书《弓与箭之道》：不解锁配方，读完后的箭矢效果以 Wiki 配置为准。
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
