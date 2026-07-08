namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 StoryFlags.cs / BookData.cs / ContainerLoot.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 金手指帮根据地「发现点」的纯逻辑：把探索队走到某个发现点触发的 discoveryId，
// 解析成 (剧情 flag / 掉落书 id / 环境叙事标题+正文)。已发现（flag 已置）则返回 null 不重复。
// 克莉丝汀尸体的叙事措辞按其支线 flag 分支（去复仇而死 vs 通用），衔接 §7 时限失败态。
// ★台词/叙事皆占位草稿，最终由用户手写；本类只保证"读值→判定→出叙事"可跑、可测，不碰 Godot、不写 flag。

/// <summary>一次发现的落地结果：置哪个 flag、给哪本书、弹什么环境叙事（标题 + 正文）。</summary>
public readonly record struct DiscoveryResult(string StoryFlag, string BookId, string Title, string Narrative);

/// <summary>
/// 金手指帮根据地发现点解析。<see cref="Resolve"/> 由 CampMain 在探索队触发发现点时调用：
/// 返回 <see cref="DiscoveryResult"/> 则 CampMain 负责置 flag、经 <c>LootApplication</c> 入库该书、弹叙事面板；
/// 返回 <c>null</c> 表示未知 id 或已发现过（flag 已置），什么都不做。
/// </summary>
public static class GoldfingerDiscovery
{
    /// <summary>克莉丝汀尸体发现点 id（探索关内 Area2D 触发时上报）。</summary>
    public const string ChristineCorpseId = "discovery_christine_corpse";

    /// <summary>哥顿上吊尸发现点 id。</summary>
    public const string GordonHangedId = "discovery_gordon_hanged";

    /// <summary>已发现克莉丝汀尸体（防重复）。</summary>
    public const string ChristineCorpseFlag = "found_christine_corpse";

    /// <summary>已发现哥顿上吊尸（防重复）。</summary>
    public const string GordonHangedFlag = "found_gordon_hanged";

    /// <summary>日记A（克莉丝汀尸旁）书 id，须与 <c>BookLibrary.GoldfingerDiaryA</c> 一致。</summary>
    public const string DiaryABookId = "goldfinger_diary_a";

    /// <summary>日记B（哥顿尸旁）书 id，须与 <c>BookLibrary.GoldfingerDiaryB</c> 一致。</summary>
    public const string DiaryBBookId = "goldfinger_diary_b";

    /// <summary>
    /// 克莉丝汀"三度回绝后独自去复仇"离营时置此 flag（<c>CampMain.ChristineLeaveVoluntary</c> 写）。
    /// 尸体叙事据此在"点名她去复仇而死"与"通用无名遗体"两种措辞间分支。
    /// </summary>
    public const string ChristineLeftForRevengeFlag = "christine_left_for_revenge";

    /// <summary>
    /// 解析一次发现。未知 id 或对应 flag 已置（已发现）返回 <c>null</c>。
    /// 本方法**不写** flag（无副作用）；置 flag 由调用方在弹叙事后进行。
    /// </summary>
    public static DiscoveryResult? Resolve(string discoveryId, StoryFlags flags)
    {
        if (discoveryId == ChristineCorpseId)
        {
            if (flags != null && flags.Has(ChristineCorpseFlag))
                return null;
            return new DiscoveryResult(
                ChristineCorpseFlag, DiaryABookId, ChristineCorpseTitle,
                ChristineNarrative(flags));
        }

        if (discoveryId == GordonHangedId)
        {
            if (flags != null && flags.Has(GordonHangedFlag))
                return null;
            return new DiscoveryResult(
                GordonHangedFlag, DiaryBBookId, GordonHangedTitle, GordonNarrative);
        }

        return null;
    }

    // draft 待用户改
    private const string ChristineCorpseTitle = "废墟深处的一具遗体";

    // draft 待用户改
    private const string GordonHangedTitle = "梁上的尸体";

    /// <summary>克莉丝汀尸体环境叙事：她去复仇而死→点名措辞；否则通用无名遗体措辞。</summary>
    private static string ChristineNarrative(StoryFlags? flags)
    {
        bool leftForRevenge = flags != null && flags.Has(ChristineLeftForRevengeFlag);
        if (leftForRevenge)
        {
            // draft 待用户改
            return
                "你认得这件外套——是克莉丝汀离营那天穿走的。\n\n" +
                "她终究还是一个人来了，也一个人留在了这里。" +
                "尸体的姿态、身上的痕迹，无声地讲完了她没能讲给你听的那段过去。\n\n" +
                "她想要的从来不是你们的兵，只是有人肯陪她走这一趟。\n\n" +
                "遗体旁散落着一本卷了边的日记。";
        }

        // draft 待用户改 —— 通用措辞（未收留 / 未去复仇等）
        return
            "一具年轻女性的遗体，蜷在墙角。死状凄惨，身上的痕迹指向的不是丧尸，" +
            "而是活人——是某种被当成\"规矩\"反复施加的暴行。\n\n" +
            "无论她是谁，都不该这样死去。\n\n" +
            "遗体旁散落着一本卷了边的日记。";
    }

    // draft 待用户改 —— 哥顿上吊尸环境叙事
    private const string GordonNarrative =
        "一具男尸吊在房梁上，绳子早已勒进发黑的皮肉，看样子死了有些日子了。\n\n" +
        "没有挣扎的痕迹，没有被丧尸啃噬的伤口。他是自己走上去的。\n\n" +
        "脚边的桌上，一本硬壳笔记本摊开着，最后一页的字迹格外用力。";
}
