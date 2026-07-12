namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 StoryFlags.cs / BookData.cs / ContainerLoot.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 探索「发现点」的纯逻辑：把探索队走到某个发现点触发的 discoveryId，
// 解析成 (剧情 flag / 掉落书 id / 环境叙事标题+正文)。已发现（flag 已置）则返回 null 不重复。
// 金手指帮根据地有**两具尸体**（用户拍板，设计文档 §8.7）：
//   · 帮众尸体（GangMemberCorpse）：被克莉丝汀反杀的金手指帮成员，时间线早于探索、各分支都可见，配日记A；
//   · 克莉丝汀本人尸体（ChristineCorpse）：仅复仇线（她三拒后独走复仇而死）才有，点名叙事、不再另给书。
// 哥顿上吊尸（后院树上）+日记B 在异地的守林人小屋（内部路由键仍＝守望者森林小屋 WatchersCabinName，显示名正名为「守林人小屋」；与金手指帮根据地异地，用户拍板）。
// 本类按 discoveryId 解析、与目的地无关（目的地→发现点的铺设/门控在 TestExploration；克莉丝汀本人尸体点仅复仇线铺出，本类亦守卫返回 null）。
// 叙事为 draft 草稿，最终由用户优化；本类只保证"读值→判定→出叙事"可跑、可测，不碰 Godot、不写 flag。

/// <summary>一次发现的落地结果：置哪个 flag、给哪本书、弹什么环境叙事（标题 + 正文）。</summary>
public readonly record struct DiscoveryResult(string StoryFlag, string BookId, string Title, string Narrative);

/// <summary>
/// 探索发现点解析（金手指帮根据地 / 守望者森林小屋两地共用）。<see cref="Resolve"/> 由 CampMain 在探索队触发发现点时调用：
/// 返回 <see cref="DiscoveryResult"/> 则 CampMain 负责置 flag、经 <c>LootApplication</c> 入库该书、弹叙事面板；
/// 返回 <c>null</c> 表示未知 id 或已发现过（flag 已置），什么都不做。
/// </summary>
public static class GoldfingerDiscovery
{
    /// <summary>帮众尸体发现点 id（被克莉丝汀反杀的金手指帮成员；各分支都可见）。</summary>
    public const string GangMemberCorpseId = "discovery_goldfinger_member_corpse";

    /// <summary>克莉丝汀本人尸体发现点 id（仅复仇线铺出；探索关内 Area2D 触发时上报）。</summary>
    public const string ChristineCorpseId = "discovery_christine_corpse";

    /// <summary>哥顿上吊尸发现点 id。</summary>
    public const string GordonHangedId = "discovery_gordon_hanged";

    /// <summary>已发现帮众尸体（防重复）。</summary>
    public const string GangMemberCorpseFlag = "found_goldfinger_member_corpse";

    /// <summary>已发现克莉丝汀本人尸体（防重复）。</summary>
    public const string ChristineCorpseFlag = "found_christine_corpse";

    /// <summary>已发现哥顿上吊尸（防重复）。</summary>
    public const string GordonHangedFlag = "found_gordon_hanged";

    /// <summary>日记A（帮众尸旁，帮众自白）书 id，须与 <c>BookLibrary.GoldfingerDiaryA</c> 一致。</summary>
    public const string DiaryABookId = "goldfinger_diary_a";

    /// <summary>日记B（哥顿尸旁）书 id，须与 <c>BookLibrary.GoldfingerDiaryB</c> 一致。</summary>
    public const string DiaryBBookId = "goldfinger_diary_b";

    /// <summary>
    /// 克莉丝汀"三度回绝后独自去复仇"离营时置此 flag（<c>CampMain.ChristineLeaveVoluntary</c> 写）。
    /// 决定金手指帮根据地是否**另有**克莉丝汀本人的尸体（帮众尸体与之无关、恒在）。
    /// </summary>
    public const string ChristineLeftForRevengeFlag = "christine_left_for_revenge";

    /// <summary>克莉丝汀本人尸体点不再另给书（日记A 归帮众尸体），此处以空串表示"无书"。</summary>
    public const string NoBookId = "";

    /// <summary>
    /// 解析一次发现。未知 id 或对应 flag 已置（已发现）返回 <c>null</c>。
    /// 克莉丝汀本人尸体点仅在复仇线（<see cref="ChristineLeftForRevengeFlag"/> 已置）成立，否则返回 <c>null</c>。
    /// 本方法**不写** flag（无副作用）；置 flag 由调用方在弹叙事后进行。
    /// </summary>
    public static DiscoveryResult? Resolve(string discoveryId, StoryFlags flags)
    {
        if (discoveryId == GangMemberCorpseId)
        {
            // 帮众尸体：被克莉丝汀反杀的金手指帮成员，时间线早于探索、各分支都可见，配日记A。
            if (flags != null && flags.Has(GangMemberCorpseFlag))
                return null;
            return new DiscoveryResult(
                GangMemberCorpseFlag, DiaryABookId, GangMemberCorpseTitle, GangMemberNarrative);
        }

        if (discoveryId == ChristineCorpseId)
        {
            // 克莉丝汀本人尸体：仅复仇线成立（非复仇线关卡不铺此点，Resolve 亦守卫），不再另给书。
            if (flags == null || !flags.Has(ChristineLeftForRevengeFlag))
                return null;
            if (flags.Has(ChristineCorpseFlag))
                return null;
            return new DiscoveryResult(
                ChristineCorpseFlag, NoBookId, ChristineCorpseTitle, ChristineNarrative);
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

    // draft 待用户改 —— 帮众尸体点标题
    private const string GangMemberCorpseTitle = "墙角的帮众尸体";

    // draft 待用户改 —— 克莉丝汀本人尸体点标题
    private const string ChristineCorpseTitle = "废墟深处的一具遗体";

    // draft 待用户改
    private const string GordonHangedTitle = "小屋后院，树上的尸体";

    // draft 待用户改 —— 帮众尸体环境叙事：被克莉丝汀反杀的金手指帮成员（男性帮众；各分支都可见）
    private const string GangMemberNarrative =
        "墙角蜷着一具男尸，一身金手指帮的行头。杀死他的不是丧尸——" +
        "颈侧一道利器捅出的伤口深可见骨，是被人贴身反手捅进去的。\n\n" +
        "翻倒的桌椅、地上拖出的长长血痕，都在说：这里曾有人拼死反抗过，" +
        "而且，那个被他们当成\"规矩\"随意糟践的人，竟真让其中一个再没能站起来。\n\n" +
        "遗体旁散落着一本卷了边的日记。";

    // 克莉丝汀本人尸体环境叙事（仅复仇线）：维持原文案；仅去掉原末句"遗体旁散落着一本卷了边的日记"
    //（日记A 现归恒在的帮众尸体，避免此处再声称有一本却不给书——已在回报中标注此一处偏离）。
    private const string ChristineNarrative =
        "你认得这件外套——是克莉丝汀离营那天穿走的。\n\n" +
        "她终究还是一个人来了，也一个人留在了这里。" +
        "尸体的姿态、身上的痕迹，无声地讲完了她没能讲给你听的那段过去。\n\n" +
        "她想要的从来不是你们的兵，只是有人肯陪她走这一趟。";

    // draft 待用户改 —— 哥顿上吊尸环境叙事（守林人小屋，后院老树上）
    private const string GordonNarrative =
        "林子深处，一栋孤零零的守林人小屋。绕到后院，那棵老树的横枝上，吊着一具男尸——" +
        "绳子早已勒进发黑的皮肉，风一过，尸身便轻轻转上半圈。他在这儿挂了有些日子了。\n\n" +
        "没有挣扎的痕迹，没有被丧尸啃噬的伤口。脚下没有可供踩踏的东西——" +
        "他是自己爬上去，自己松的手。\n\n" +
        "树根旁的草丛里，一本硬壳笔记本摊开着，被露水泡得发胀，最后一页的字迹却格外用力。";
}
