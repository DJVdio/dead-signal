namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 NurseRecruit.cs / VillageRescue.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// [T61] **耗子** —— 下水道最深处遇到的可招募幸存者。招募链路照抄 NurseRecruit（相遇点 → 弹对话 →
// 答应置 AgreedFlag → 回营注入置 EnlistedFlag），一格没另起炉灶。
//
// 🔴🔴 **authored 纪律：用户给的关于她的全部信息，一个字都没有多**：
//   ①「走到最深处，会看到一个**浑身恶臭穿着潮湿破布夹克的女人**」
//   ②「他是一名**可招募的幸存者**」
//   ③「**没有名字，叫"耗子"**」
//   ④ 三级专属效果 + 升级方式（见 RatPerk）
// **就这些。** 她的前史 / 性格 / 为什么在下水道 / 和谁认识 —— 用户**一个字都没写**，
// **代码不许编造**（CLAUDE.md：「剧情/角色关系/性格…是用户手写的 authored 内容，代码只做"按条件播放"的框架，
// 不做程序化引申」）。故下方**招募对话正文全是明确标注的占位**，只写死用户给的那四条事实（恶臭、潮湿破布夹克、
// 女人、无名、叫耗子、可招募），**其余一律留白等用户手写**。见 journal 的「待用户手写的正文清单」。

/// <summary>
/// 耗子招募对话的一次要约（同 <see cref="NurseRecruitOffer"/> 形态）：标题 + 正文 + 两个选项的标签与说明。
/// </summary>
public readonly record struct RatRecruitOffer(
    string Title,
    string Prompt,
    string AcceptLabel,
    string AcceptDescription,
    string DeclineLabel,
    string DeclineDescription);

/// <summary>
/// **耗子**的招募（[T61]，纯逻辑）。链路与 <see cref="NurseRecruit"/> 完全同构：
/// 探索队走到下水道**最深处**踏到 <see cref="MeetDiscoveryId"/> → <see cref="Resolve"/> 出要约 → 玩家答应则置
/// <see cref="AgreedFlag"/> → 回营时 <see cref="ShouldEnlistOnReturn"/> 为真则真正注入 Pawn 并置 <see cref="EnlistedFlag"/>。
/// <para>
/// **为什么入队延到回营**：探索队出发时名单已定，不在关内临时增员战斗单位（与 <see cref="NurseRecruit"/> /
/// <c>VillageRescue</c> 同一条既有口径，不为她破例）。
/// </para>
/// <para>她的**专属效果**不在本类，在 <see cref="RatPerk"/>（身份标记 <see cref="SurvivorPerks.IsRat"/>，
/// 由 <c>Pawn.Create</c> 按名 <see cref="RatPerk.RatName"/> 授予）。</para>
/// </summary>
public static class RatRecruit
{
    /// <summary>下水道的目的地名（世界图路由键＝显示名；世界图上的位置/前置归 <c>world_graph.json</c>，不在本类）。</summary>
    public const string DestinationName = "下水道";

    /// <summary>她的名字（转发 <see cref="RatPerk.RatName"/>，单一真源在那边）。用户原话：「没有名字，叫"耗子"」。</summary>
    public const string RatName = RatPerk.RatName;

    /// <summary>相遇点 discoveryId（下水道**最深处**）。**非物资搜刮点** ⇒ 不计探索完成度（同护士相遇点口径）。</summary>
    public const string MeetDiscoveryId = "discovery_sewer_rat_meet";

    /// <summary>已答应加入（关内置；待回营注入 + 相遇对话去重）。婉拒**不置任何旗标** ⇒ 日后再来还能再谈。</summary>
    public const string AgreedFlag = "sewer_rat_agreed";

    /// <summary>已注入入队（回营置；"注入一次"硬守卫，她日后身故也不复注入）。</summary>
    public const string EnlistedFlag = "sewer_rat_enlisted";

    // ==================== authored 正文（🔴 待用户手写，下方全是占位） ====================
    //
    // ⚠️ 下面每一段都**只**由用户给的四条事实拼成，**没有任何引申**。
    //    用户没说她怎么说话、说什么、为什么在这儿、见到人是什么反应 ⇒ **我们不知道，所以不写**。
    //    请用户手写替换。替换时**不需要改任何代码**——只改这几个 const 的字符串。

    /// <summary>相遇对话标题。<b>🔴 占位·待用户手写。</b></summary>
    public const string MeetTitle = "下水道·最深处";

    /// <summary>
    /// 相遇对话正文。<b>🔴 占位·待用户手写。</b>
    /// <para>当前只写死用户明确给的：**浑身恶臭**、**潮湿的破布夹克**、**女人**、**没有名字**、**叫"耗子"**。
    /// 她的来历/性格/开口说什么，用户没写 ⇒ **此处一个字都不许编**。</para>
    /// </summary>
    public const string MeetPrompt =
        "手电的光圈扫过最后一个拐角。\n" +
        "一个女人站在那里。潮湿的破布夹克贴在身上，那股恶臭先于她本人到达。\n" +
        "她没有名字。她说，叫她耗子。\n" +
        "\n" +
        "〔🔴 占位：本段待用户手写。她的来历、性格、见到人的第一句话，用户尚未提供，故此处不作任何引申。〕";

    /// <summary>接受选项的标签。<b>🔴 占位·待用户手写。</b></summary>
    public const string AcceptLabel = "邀请她加入营地";

    /// <summary>接受选项的说明。<b>🔴 占位·待用户手写。</b></summary>
    public const string AcceptDescription = "在这底下活到今天的人，知道怎么在废墟里找东西";

    /// <summary>婉拒选项的标签。<b>🔴 占位·待用户手写。</b></summary>
    public const string DeclineLabel = "暂不";

    /// <summary>婉拒选项的说明。<b>🔴 占位·待用户手写。</b>（不置任何旗标 ⇒ 日后再下来还能再谈。）</summary>
    public const string DeclineDescription = "先不开口，日后可再下来找她";

    /// <summary>接受后的叙事标题。<b>🔴 占位·待用户手写。</b></summary>
    public const string AcceptTitle = "她跟上来了";

    /// <summary>接受后的叙事正文。<b>🔴 占位·待用户手写。</b></summary>
    public const string AcceptNarrative =
        "〔🔴 占位：本段待用户手写。〕\n" +
        "她跟在队伍最后，脚步声轻得几乎不存在。";

    /// <summary>婉拒后的叙事标题。<b>🔴 占位·待用户手写。</b></summary>
    public const string DeclineTitle = "改天再说";

    /// <summary>婉拒后的叙事正文。<b>🔴 占位·待用户手写。</b></summary>
    public const string DeclineNarrative =
        "〔🔴 占位：本段待用户手写。〕\n" +
        "（她仍在下水道底下。日后可再下来相谈。）";

    // ==================== 纯判定（与 NurseRecruit 逐行同构） ====================

    /// <summary>
    /// 是否可弹招募对话：未答应（<see cref="AgreedFlag"/> 未置）且未入队（<see cref="EnlistedFlag"/> 未置）→ true。
    /// 婉拒不置任何旗标，故未答应前每次走到最深处都可再弹。
    /// </summary>
    public static bool ShouldOfferRecruitment(StoryFlags flags)
        => flags != null && !flags.Has(AgreedFlag) && !flags.Has(EnlistedFlag);

    /// <summary>
    /// 相遇解析：踏到 <see cref="MeetDiscoveryId"/> 时调用。返回要约 ⇒ 上层冻结时标 + 弹 ChoicePanel；
    /// 返回 null ⇒ 未知 id / 已答应（等回营注入）/ 已入队，什么都不弹。**本函数不写 flag**（接受时由调用方置）。
    /// </summary>
    public static RatRecruitOffer? Resolve(string discoveryId, StoryFlags flags)
    {
        if (discoveryId != MeetDiscoveryId)
        {
            return null;
        }
        if (!ShouldOfferRecruitment(flags))
        {
            return null;
        }
        return new RatRecruitOffer(
            MeetTitle, MeetPrompt,
            AcceptLabel, AcceptDescription,
            DeclineLabel, DeclineDescription);
    }

    /// <summary>
    /// 回营时是否应注入（正史入队）耗子：已答应且尚未注入过 → true。
    /// <see cref="EnlistedFlag"/> 作"注入一次"硬守卫。
    /// </summary>
    public static bool ShouldEnlistOnReturn(StoryFlags flags)
        => flags != null && flags.Has(AgreedFlag) && !flags.Has(EnlistedFlag);
}
