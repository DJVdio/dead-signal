using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationCache.cs / GoldfingerDiscovery.cs / StoryFlags.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「南林村庄」探索点·道格与布鲁斯正史入队的纯逻辑（用户 [SPEC-B11] 口径）：
//   道格和布鲁斯被困在一个上锁的、被丧尸包围的屋子里；调查团靠近到**中距离**时布鲁斯开始吠叫，
//   吸引调查团过去；玩家清理/绕过丧尸→开锁→屋内发现**饿昏迷的道格**+布鲁斯→救援→二人一狗正史入队
//   （入队时道格饥饿极低、布鲁斯亦极低，接 HungerState/DogHungerState 低档，"饿昏迷"语义）。
//
// 本类只负责脱 Godot 可测的判定：
//   · 触发距离（中距离吠叫）：<see cref="ShouldStartBarking"/>——空间由 Godot 层做距离轮询喂入。
//   · 救援解析（同 ExplorationCache.Resolve 模式）：<see cref="Resolve"/>——踏入锁屋门即上报 <see cref="RescueDiscoveryId"/>，
//     返回 <see cref="RescueOutcome"/> 则由 CampMain 置 <see cref="RescuedFlag"/> + 弹救援叙事；已救过返回 null。
//   · 入队时机（延迟到回营）：<see cref="ShouldEnlistOnReturn"/>——救援发生在关内、道格饿昏迷无法作战，
//     叙事为"架回营地"，故真正的道格/布鲁斯注入延到探索队回营时做（避免跨场景 Bruce 敌对寻路，接线更稳）。
// 空间执行（村庄布局、锁屋、围困丧尸生成、吠叫飘字、开锁发现区）落 Godot 层（TestExploration）。
// 叙事为 draft 草稿，最终由用户优化；本类只保证"读值→判定→出叙事/入队条件"可跑、可测。

/// <summary>一次村庄救援落地结果：置哪个 flag、弹什么救援叙事（标题 + 正文）。</summary>
public readonly record struct RescueOutcome(string StoryFlag, string Title, string Narrative);

public static class VillageRescue
{
    /// <summary>目的地名（与 <c>WorldMapPanel</c> 的 Destination.Name 一致，务必同步；本类脱 Godot 单测故本地持有副本）。</summary>
    public const string DestinationName = "南林村庄";

    /// <summary>救援发现点 id（探索关内锁屋门 Area2D 踏入时上报；CampMain.OnExplorationDiscovery 据此走 <see cref="Resolve"/>）。</summary>
    public const string RescueDiscoveryId = "discovery_south_forest_rescue";

    /// <summary>
    /// 救援已发生旗标（踏入锁屋门即置，跨关持久）：既作救援叙事去重（<see cref="Resolve"/> 门控），
    /// 又作"待回营注入"的挂起标记（回营时 <see cref="ShouldEnlistOnReturn"/> 读它决定是否注入道格布鲁斯）。
    /// </summary>
    public const string RescuedFlag = "village_doug_rescued";

    /// <summary>
    /// 道格布鲁斯已正史入队旗标（真正注入营地那一刻置，永久）：作"注入一次"硬守卫——
    /// 即便道格日后身故（_doug 置 null），也不因 <see cref="ShouldEnlistOnReturn"/> 再次注入。
    /// 与既有气泡门控 bruce_present 等平行（注入时由营地层自然置起）。
    /// </summary>
    public const string EnlistedFlag = "doug_enlisted";

    /// <summary>围困锁屋的丧尸数（用户拟定 4~6，取中值；数值待调）。</summary>
    public const int SiegeZombieCount = 5;

    /// <summary>
    /// 布鲁斯吠叫触发的"中距离"半径（关内世界坐标 px，拟定待调）：调查团任一成员距锁屋 ≤ 此值即触发吠叫。
    /// 中距离＝比一眼看见远、比擦身而过近，给玩家"听见狗叫→循声找过去"的引导空间。
    /// </summary>
    public const float BarkTriggerRadius = 520f;

    /// <summary>重复吠叫飘字的间隔秒数（"重复的叫声提示"，拟定待调）。</summary>
    public const float BarkIntervalSeconds = 1.3f;

    /// <summary>
    /// 入队时道格的饥饿低档（"饿昏迷"语义）：营养不良（1，濒临饿死、能力惩罚 0.45）。
    /// 注入后压到此档（<see cref="HungerState.DrainTo"/>），玩家须靠聚餐把他喂回来。数值待调。
    /// </summary>
    public const int DougEnlistHunger = (int)HungerLevel.Malnourished; // 1

    /// <summary>入队时布鲁斯的饥饿低档（与道格同为"饿昏迷"，压到 <see cref="DogHungerState.DrainTo"/> 此档）。数值待调。</summary>
    public const int BruceEnlistHunger = (int)HungerLevel.Malnourished; // 1

    // ——救援叙事（draft·"饿昏迷"情境，道格初见虚弱、话极少；最终由用户细化）——
    public const string RescueTitle = "南林村庄·上锁的屋子";

    public const string RescueNarrative =
        "门从里面闩死了。撬开的一瞬，一条狗从阴影里窜出来，龇着牙低吼，死死挡在里屋门口——却连站都站不稳，四条腿在打颤。\n" +
        "屋角蜷着个人。嘴唇干裂脱皮，眼睛半睁半合，认出你们不是那东西，抬了抬手，喉咙里却只挤得出一点气音。\n" +
        "\n" +
        "男人（气若游丝）：“……布鲁斯……别叫了……是活人……”\n" +
        "那狗听见名字，喉咙里的吼一下子散了，一瘸一拐退回他身边，用鼻子拱他的手背。\n" +
        "男人（几乎听不见）：“……水……有水么……”\n" +
        "\n" +
        "你们把他和狗一起架起来。他没再说话——饿得连道谢的力气都没剩下。\n" +
        "带回去再说。";

    /// <summary>吠叫飘字文本（音频无资产时的占位提示，见 TestExploration 注明）。</summary>
    public const string BarkText = "汪！";

    /// <summary>
    /// 中距离吠叫触发判定（纯函数，供 Godot 层距离轮询消费）：调查团**任一成员**距锁屋的最近距离 ≤
    /// <see cref="BarkTriggerRadius"/> 且尚未开始吠叫 → 开始吠叫。已在吠叫（<paramref name="alreadyBarking"/>）则不重复起吠。
    /// </summary>
    /// <param name="nearestMemberDistance">调查团存活成员距锁屋的最近距离（关内世界坐标 px，由 Godot 层算）。</param>
    /// <param name="alreadyBarking">当前是否已处于吠叫态。</param>
    public static bool ShouldStartBarking(float nearestMemberDistance, bool alreadyBarking)
        => !alreadyBarking && nearestMemberDistance <= BarkTriggerRadius;

    /// <summary>
    /// 距离是否仍在吠叫范围内（供 Godot 层判断"离开中距离即停吠"）：最近距离 ≤ 半径即仍在范围。
    /// 与 <see cref="ShouldStartBarking"/> 分开，让"起吠"（需未吠过）与"续吠/停吠"（只看距离）语义清晰。
    /// </summary>
    public static bool InBarkRange(float nearestMemberDistance)
        => nearestMemberDistance <= BarkTriggerRadius;

    /// <summary>
    /// 救援解析（同 <c>ExplorationCache.Resolve</c> 模式）：踏入锁屋门上报 <see cref="RescueDiscoveryId"/> 时调用。
    /// 返回 <see cref="RescueOutcome"/> 则 CampMain 置 <see cref="RescuedFlag"/> + 弹救援叙事面板；
    /// 返回 null 表示未知 id 或**已救过**（<see cref="RescuedFlag"/> 已置），什么都不做。本函数不写 flag（调用方写）。
    /// </summary>
    public static RescueOutcome? Resolve(string discoveryId, StoryFlags flags)
    {
        if (discoveryId != RescueDiscoveryId)
            return null;
        if (flags == null || flags.Has(RescuedFlag))
            return null; // 已救过：去重
        return new RescueOutcome(RescuedFlag, RescueTitle, RescueNarrative);
    }

    /// <summary>
    /// 回营时是否应注入（正史入队）道格布鲁斯（纯判定，供 CampMain.OnExplorationReturn 消费）：
    /// 已在关内救过（<see cref="RescuedFlag"/> 已置）且尚未注入过（<see cref="EnlistedFlag"/> 未置）→ true。
    /// 注入延到回营是因为救援发生在探索关内、道格饿昏迷无法作战（叙事＝架回营地），
    /// 且避免在关内把营地态的布鲁斯注入后跨场景追敌寻路。<see cref="EnlistedFlag"/> 作"注入一次"硬守卫，
    /// 道格日后身故（_doug 置 null）也不会因本判定重复注入。
    /// </summary>
    public static bool ShouldEnlistOnReturn(StoryFlags flags)
        => flags != null && flags.Has(RescuedFlag) && !flags.Has(EnlistedFlag);
}
