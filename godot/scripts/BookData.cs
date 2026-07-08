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

    public BookData(string id, string title, string body, string? grantsRecipeStub = null)
    {
        Id = id;
        Title = title;
        Body = body;
        GrantsRecipeStub = grantsRecipeStub;
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
        grantsRecipeStub: "recipe:wilderness_trap"); // 桩：配方系统后续接

    /// <summary>《农场主的一百个问题》——读完给"作物种植"配方（桩）。</summary>
    public static BookData FarmerHundredQuestions() => new(
        id: "farmer_hundred_questions",
        title: "农场主的一百个问题",
        body: FarmerHundredQuestionsBody,
        grantsRecipeStub: "recipe:crop_planting"); // 桩：配方系统后续接

    /// <summary>《裁缝手记》（纺织书，draft）——读过它的制作者解锁粗布背心一类缝纫配方。</summary>
    public static BookData TailorsNotes() => new(
        id: "tailors_notes",
        title: "裁缝手记",
        body: TailorsNotesBody,
        grantsRecipeStub: "recipe:cloth_vest"); // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记

    /// <summary>《土法化学笔记》（化学书，draft）——读过它的制作者解锁火药 / 鞣制药水一类化学配方。</summary>
    public static BookData FolkChemistryNotes() => new(
        id: "folk_chemistry_notes",
        title: "土法化学笔记",
        body: FolkChemistryNotesBody,
        grantsRecipeStub: "recipe:gunpowder"); // 桩：书门槛已实装（RecipeBook.RequiredBookIds），此仅作叙事标记

    /// <summary>
    /// 日记A（金手指帮根据地，克莉丝汀尸旁）——两个普通帮众视角：灾后互助、参与暴行、"金手指帮"命名由来。
    /// 纯叙事物品，无配方产出（桩留空）。正文为占位草稿，最终由用户手写。
    /// </summary>
    public static BookData GoldfingerDiaryA() => new(
        id: "goldfinger_diary_a",
        title: "一本卷边的日记（其一）",
        body: GoldfingerDiaryABody,
        grantsRecipeStub: null); // 无配方，纯 lore

    /// <summary>
    /// 日记B（哥顿上吊尸旁，守望者森林小屋，与日记A 异地）——金手指帮文化起源 + 帮主哥顿身世/自杀。
    /// 纯叙事物品，无配方产出（桩留空）。正文为 draft 草稿，最终由用户优化。
    /// </summary>
    public static BookData GoldfingerDiaryB() => new(
        id: "goldfinger_diary_b",
        title: "一本硬壳笔记（其二）",
        body: GoldfingerDiaryBBody,
        grantsRecipeStub: null); // 无配方，纯 lore

    /// <summary>全部内置书的全新实例（每次调用新建，已读态不共享）。</summary>
    public static IReadOnlyList<BookData> All() => new[]
    {
        WildernessSurvivalGuide(),
        FarmerHundredQuestions(),
        TailorsNotes(),
        FolkChemistryNotes(),
        GoldfingerDiaryA(),
        GoldfingerDiaryB(),
    };

    // draft 待用户改
    private const string WildernessSurvivalGuideBody =
        "封皮磨得发白，边角卷起。翻开第一页，是一行褪色的钢笔字：\n" +
        "\"活下去不靠运气，靠的是在天黑前把该做的都做完。\"\n\n" +
        "第一章 取火。第二章 净水。第三章 用一根铁丝和一点耐心，让林子替你狩猎——" +
        "书里画着一套简陋却致命的陷阱，只要照着做，晚上就不必空着肚子睡。";

    // draft 待用户改
    private const string FarmerHundredQuestionsBody =
        "一本被翻烂的问答手册，扉页盖着某个已经不存在的农业合作社的红章。\n\n" +
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

    // draft 待用户改 —— 日记A：两个普通帮众视角（互助求生 / 参与暴行 / "金手指帮"命名由来）
    private const string GoldfingerDiaryABody =
        "字迹潦草，像是就着火光匆匆写下的。\n\n" +
        "灾变头一个月，是老陈拉了我一把。我们俩背靠背，从加油站一路抢到城郊，" +
        "谁也没抛下谁——那时候我还觉得，能活下来的都是好人。\n\n" +
        "后来我们撞上了这伙人。有吃的，有墙，有枪。代价是，你得跟他们一样。\n\n" +
        "第一次把那女人拖进屋的时候，我手在抖。哥顿说，男人就该拿在手里，" +
        "拿不住的，不配活。老陈先动的手，我跟上了。往后就不抖了。\n\n" +
        "他们管自己叫\"金手指\"。头目说，指头是男人身上最诚实的东西——" +
        "它按住扳机，也按住女人，它做过的事，脑子可以不认，指头认。\n" +
        "所以入伙那天，每个人都要用手指在她们身上留下印子。这就是规矩。\n\n" +
        "我写下这些，不是想被原谅。我只是怕哪天连自己都忘了，我曾经也算个人。";

    // draft 待用户改 —— 日记B：金手指帮文化起源 + 哥顿身世/以暴掩懦/看透后自杀
    private const string GoldfingerDiaryBBody =
        "硬壳笔记本，扉页只写着一个名字：哥顿。字迹工整得不像个恶人。\n\n" +
        "我母亲从不许我父亲说完一句话。她的声音、她的手，压着这个家二十年。" +
        "父亲低着头吃饭、低着头挨骂、低着头老去。我恨他的软弱，也害怕自己就是他。\n\n" +
        "变尸的那天，是母亲先咬穿了父亲的喉咙。他没有反抗，甚至没有后退——" +
        "他就那样看着她扑上来，像是终于等到了什么。我躲在门后，一动没动。\n\n" +
        "后来我立了规矩，教弟兄们如何拿捏一个女人，如何让恐惧替我说话。" +
        "他们怕我，就没人看得见：我不过是那个躲在门后、连喊都不敢喊的孩子。\n\n" +
        "可这些天我总在想——把她们折磨到死，我究竟证明了什么？\n" +
        "什么都没有。指头按下去，按住的从来不是别人，是我自己那点没用的怕。\n\n" +
        "我一个人来了这片林子。守望者小屋门口那棵老树的横枝够粗，绳子我也试过了。\n" +
        "没人认得我，也没人替我说话——很好。这一次，我不必再装了。";
}
