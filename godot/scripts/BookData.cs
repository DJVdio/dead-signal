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

    /// <summary>两本内置书的全新实例（每次调用新建，已读态不共享）。</summary>
    public static IReadOnlyList<BookData> All() => new[]
    {
        WildernessSurvivalGuide(),
        FarmerHundredQuestions(),
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
}
