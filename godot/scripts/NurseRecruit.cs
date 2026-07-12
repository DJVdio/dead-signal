namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 VillageRescue.cs / ExplorationCache.cs / StoryFlags.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「南丁格尔的小药店」探索点·可招募护士角色的纯逻辑（用户 [SPEC-B13] 口径）：
//   南丁格尔的小药店里遇到一个可招募的护士角色。招募机制参照**道格正史入队先例**（VillageRescue：
//   发现点触发→叙事→置旗→回营注入），但与道格有一处关键差异——
//   **护士清醒、可对话**（道格是饿昏迷被动救援）：相遇不是自动救援，而是弹一段**招募对话**
//   （邀请入队 / 暂不），玩家选择：
//     · 接受 → 置 <see cref="AgreedFlag"/>（待回营注入标记 + 对话去重）→ 探索队回营时注入护士 Pawn。
//     · 婉拒 → **不置旗**，可再访药店再谈（<see cref="ShouldOfferRecruitment"/> 未答应前恒可再触发对话）。
//
// 本类只负责脱 Godot 可测的判定：
//   · 相遇解析（同 VillageRescue.Resolve / ExplorationCache.Resolve 模式）：<see cref="Resolve"/>——
//     踏入护士警戒区上报 <see cref="MeetDiscoveryId"/>，返回 <see cref="NurseRecruitOffer"/> 则由 CampMain
//     冻结时标 + 弹 ChoicePanel 招募对话；已答应/已入队返回 null（不再弹）。本类不写 flag（调用方写）。
//   · 招募门控：<see cref="ShouldOfferRecruitment"/>——未答应且未入队 → 可弹对话（拒绝不置旗，故可反复触发直到答应）。
//   · 入队时机（延迟到回营）：<see cref="ShouldEnlistOnReturn"/>——已答应且未注入过 → 回营注入。
//     注入延到回营的理由同道格：探索队出发时名单已定，不在关内临时增员战斗单位；接线更稳、与 VillageRescue 一致。
// 空间执行（药店布局、护士占位、警戒区 Area2D）落 Godot 层（TestExploration）。
// 姓名"南丁格尔"为**占位关联名**（draft，用户后改）；护士性格/正式姓名/台词=用户手写，本类叙事为 draft。
// 医疗特长见 <see cref="NightingalePerk"/>（护士三级 authored 专属效果，[SPEC-B13-补]/[SPEC-B13-补2] 用户拍板）。

/// <summary>
/// 一次护士招募邀约的对话内容：情境标题 + 招募问话 + 接受/婉拒两个选项文案。
/// 供 CampMain 装配 <c>ChoicePanel</c>（接受＝值 1，婉拒＝值 0）。全为 draft 草稿，最终由用户细化。
/// </summary>
public readonly record struct NurseRecruitOffer(
    string Title, string Prompt,
    string AcceptLabel, string AcceptDescription,
    string DeclineLabel, string DeclineDescription);

public static class NurseRecruit
{
    /// <summary>
    /// 目的地**内部路由键**（与 <c>WorldMapPanel</c> 的 Destination.Name 一致：仍是"药店"，务必同步）。
    /// 正名只改显示名（<see cref="DisplayName"/>），路由键/flag 不变——同守林人小屋正名先例。本类脱 Godot 单测故持副本。
    /// </summary>
    public const string DestinationName = "药店";

    /// <summary>显示名（正名"药店→南丁格尔的小药店"）：地图/关卡只改显示，内部键仍用 <see cref="DestinationName"/>。</summary>
    public const string DisplayName = "南丁格尔的小药店";

    /// <summary>
    /// 护士相遇发现点 id（药店内踏入护士警戒区 Area2D 上报；CampMain.OnExplorationDiscovery 据此走 <see cref="Resolve"/>）。
    /// </summary>
    public const string MeetDiscoveryId = "discovery_pharmacy_nurse_meet";

    /// <summary>
    /// 护士已答应入队旗标（招募对话选"邀请入队"即置，跨关持久）：既作"待回营注入"挂起标记
    /// （回营时 <see cref="ShouldEnlistOnReturn"/> 读它决定是否注入），又作相遇对话去重
    /// （置后 <see cref="ShouldOfferRecruitment"/> 返回 false，不再弹对话）。
    /// </summary>
    public const string AgreedFlag = "pharmacy_nurse_agreed";

    /// <summary>
    /// 护士已正史入队旗标（真正注入营地那一刻置，永久）：作"注入一次"硬守卫——
    /// 即便护士日后身故，也不因 <see cref="ShouldEnlistOnReturn"/> 再次注入。
    /// </summary>
    public const string EnlistedFlag = "pharmacy_nurse_enlisted";

    /// <summary>
    /// 南丁格尔 L3「永续遗产」旗标（她本人累计手术台数达 <see cref="SurvivorPerks"/> 的 L3 阈值那一刻置，**永久**）：
    /// 承载 3级效果的"她死/离营依旧生效"语义（[SPEC-B13-补]）——全营手术基础点 +5、全营感染率再 −10% 读它，
    /// 与她的存活 perk 实例解耦（她身故后 perk 实例可能已随 Pawn 移除，遗产靠本旗标存续）。
    /// </summary>
    public const string L3LegacyFlag = "nightingale_l3_legacy";

    /// <summary>
    /// 护士占位姓名（draft·用户后改）：<c>Pawn.Create</c> 按此名授予护士专属 perk
    /// （<see cref="SurvivorPerks.GrantNurse"/>，同诺蒂"书虫"按名授予先例）。
    /// </summary>
    public const string NurseName = "南丁格尔";

    // ——招募对话（draft·"清醒可对话"情境，护士警觉但讲理；接受/婉拒两支；最终由用户细化）——
    public const string MeetTitle = "南丁格尔的小药店·柜台后";

    // 注：本文案在 ChoicePanel 顶部提示框显示（高度有限），故控制在数行内；更丰的相遇细节走接受/婉拒叙事。
    public const string MeetPrompt =
        "柜台后头站着个女人，护士服洗得发白，手里攥着把手术剪——剪尖对着你们，人却没退。\n" +
        "“……你们不是那帮抢药的，也不是死人。”她打量片刻，剪子慢慢放低，“这店我一个人守了快一个月了。”\n\n" +
        "要不要邀请她加入营地？";

    public const string AcceptLabel = "邀请她加入营地";
    public const string AcceptDescription = "多一双手，尤其是一双会治伤的手";

    public const string DeclineLabel = "暂不";
    public const string DeclineDescription = "先不开口，日后可再来药店找她";

    /// <summary>接受招募后的落地叙事（draft·她答应、收拾药品随行；最终由用户细化）。</summary>
    public const string AcceptTitle = "她收起了剪刀";
    public const string AcceptNarrative =
        "女人沉默了几秒，终于把手术剪插回围裙口袋。\n" +
        "“……好。反正这儿也守不了多久了。”她转身，从柜台底下拖出一个塞满绷带和药瓶的帆布包，" +
        "利落地挎上肩，“我叫南丁格尔——别笑，我妈起的。”\n" +
        "“有伤员就交给我。我知道该怎么做。”\n\n" +
        "（她会在你们返回营地后加入。）";

    /// <summary>婉拒招募后的落地叙事（draft·她理解、留在店里，可再来；最终由用户细化）。</summary>
    public const string DeclineTitle = "改天再说";
    public const string DeclineNarrative =
        "你们没有开口。女人也没多问，只是重新握紧了那把剪刀，退回柜台后的阴影里。\n" +
        "“……行。”她说，“我哪也不去。想好了，再来找我。”\n\n" +
        "（她仍守在药店里。日后可再来相谈。）";

    /// <summary>
    /// 是否可弹招募对话（纯门控，供 <see cref="Resolve"/> 与 Godot 层消费）：未答应（<see cref="AgreedFlag"/> 未置）
    /// 且未入队（<see cref="EnlistedFlag"/> 未置）→ true。婉拒不置任何旗标，故未答应前每次踏入均可再弹对话。
    /// </summary>
    public static bool ShouldOfferRecruitment(StoryFlags flags)
        => flags != null && !flags.Has(AgreedFlag) && !flags.Has(EnlistedFlag);

    /// <summary>
    /// 相遇解析（同 <c>VillageRescue.Resolve</c> 模式）：踏入护士警戒区上报 <see cref="MeetDiscoveryId"/> 时调用。
    /// 返回 <see cref="NurseRecruitOffer"/> 则 CampMain 冻结时标 + 弹 ChoicePanel 招募对话；
    /// 返回 null 表示未知 id、或已答应（<see cref="AgreedFlag"/> 已置，等回营注入）、或已入队——什么都不弹。
    /// 本函数不写 flag（接受时由调用方置 <see cref="AgreedFlag"/>）。
    /// </summary>
    public static NurseRecruitOffer? Resolve(string discoveryId, StoryFlags flags)
    {
        if (discoveryId != MeetDiscoveryId)
            return null;
        if (!ShouldOfferRecruitment(flags))
            return null; // 已答应（待回营注入）或已入队：不再弹对话
        return new NurseRecruitOffer(
            MeetTitle, MeetPrompt,
            AcceptLabel, AcceptDescription,
            DeclineLabel, DeclineDescription);
    }

    /// <summary>
    /// 回营时是否应注入（正史入队）护士（纯判定，供 CampMain 回营钩子消费）：
    /// 已答应（<see cref="AgreedFlag"/> 已置）且尚未注入过（<see cref="EnlistedFlag"/> 未置）→ true。
    /// 注入延到回营是因为探索队出发时名单已定、不在关内临时增员战斗单位（与 VillageRescue 一致）。
    /// <see cref="EnlistedFlag"/> 作"注入一次"硬守卫，护士日后身故也不会因本判定重复注入。
    /// </summary>
    public static bool ShouldEnlistOnReturn(StoryFlags flags)
        => flags != null && flags.Has(AgreedFlag) && !flags.Has(EnlistedFlag);
}
