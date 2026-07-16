using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 StoryFlags.cs / ChristineRequestLogic.cs / MerchantLineage.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 「dead signal」标题主线的**状态机纯逻辑**（用户拍板 [SPEC-B8] 三条）：
//   电台分层（用户原话）："能回复就是能发出。营地简易电台只能听，到广播台获得发出设备后才能回复/发出。"
//   → 未知情 → 已收听军方广播（营地收音机，主线知情入口） → 持有发出设备（广播台探索点定点投放）
//     → (终态分岔) 已回复军方 | 已呼叫南方营地
//
// 结局衔接（结局矩阵 RESOLVED，见 journal §3 用户总纲；本类只落机制骨架，事件本体/CG/南方营地/考验皆 authored 待用户）：
//   · 已回复军方 → 记录回复日；回复日 +3 天期满触发**军方白天来袭**（结局②，非即时终局：白天屠杀留守者，
//     外出探险队幸存归来见营地覆灭，游戏不强制结束，续走结局①尸潮或③南逃）。本类只在期满置**事件钩子 flag**，
//     军袭事件本体不实装（CampMain 留 TODO 挂点 + 安全 no-op）。
//     ★回复军方后尸潮时限**不冻结**：第 3 天军袭先到、40 天尸潮更晚，无需 EndgameFreezeFlag（此处不置，仅注明）。
//   · 已呼叫南方 → 置**南逃线 flag**（结局③，唯一生路：南方求救→临时开放"前往峡谷的路"→考验→带少量物资南逃，
//     后续 authored）。南逃线亦**不冻结**尸潮时限：南逃须抢在第 40 天尸潮前完成（紧迫感即卖点，用户拍板）。
//
// 两个终态**互斥且不可逆**（终局岔口）：一旦回复军方或呼叫南方，状态机停在该终态，不再推进。
// 状态只存在 StoryFlags 的字符串里（引擎无整数字段）：这里只做"读值→判定→写值"，不碰 Godot、可单测。
// ★广播文本/抉择选项文案皆**草稿，供用户改**；本类只保证状态推进可跑、可测，不发明背叛伏笔（留给用户手写）。

/// <summary>电台主线当前阶段（rank 递增：Unknown&lt;Heard&lt;HasTransmitter&lt;终态；两终态互斥不可逆）。</summary>
public enum RadioMainlineStage
{
    /// <summary>未知情（默认；flag 未设置即此）。尚未在营地收音机听过军方广播。</summary>
    Unknown,

    /// <summary>已收听军方循环广播（营地收音机交互）——主线知情入口。尚无发出设备，只能收听。</summary>
    HeardBroadcast,

    /// <summary>已在广播台取得"发出设备"——电台解锁"回复/发出"，营地收音机交互升级为抉择入口。</summary>
    HasTransmitter,

    /// <summary>终态①：已回复军方（结局②岔口，回复日 +3 触发军方白天来袭）。</summary>
    RepliedMilitary,

    /// <summary>终态②：已呼叫南方营地（结局③岔口，开启南逃线）。</summary>
    CalledSouth,
}

/// <summary>
/// 电台主线状态判定/推进。主状态存于 flag <see cref="StageKey"/>
/// （未设=Unknown / "heard" / "device" / "replied_military" / "called_south"）。
/// 收听经 <see cref="MarkBroadcastHeard"/>；取得发出设备经 <see cref="GrantTransmitter"/>；
/// 抉择经 <see cref="ReplyToMilitary"/> / <see cref="CallSouth"/>（仅 <see cref="RadioMainlineStage.HasTransmitter"/> 可选，选后锁死）。
/// 军袭倒计时经 <see cref="IsMilitaryRaidDue"/> 判定、经 <see cref="TryFireMilitaryRaidHook"/> 一次性触发事件钩子。
/// </summary>
public static class RadioMainline
{
    /// <summary>主状态 flag（未设=Unknown / "heard" / "device" / "replied_military" / "called_south"）。</summary>
    public const string StageKey = "radio_mainline";

    /// <summary>回复军方那天（游戏第几天，字符串承载整数）；军袭期满 = 回复日 + <see cref="MilitaryRaidDelayDays"/>。</summary>
    public const string ReplyDayKey = "radio_reply_day";

    /// <summary>军方白天来袭事件钩子已触发 flag（保证只触发一次，防重复挂事件）。</summary>
    public const string MilitaryRaidFiredKey = "radio_military_raid_fired";

    /// <summary>
    /// 南逃线开启 flag（呼叫南方后置）。后续南逃流程（南方求救→峡谷路→考验→南逃）读它，authored 待用户设计。
    /// </summary>
    public const string SouthEscapeOpenFlag = "south_escape_open";

    /// <summary>
    /// 南方已回绝 flag（三问失败后置，[SPEC-B11] 新矩阵）。置后不可再呼叫南方（一次性机会），
    /// 但 <see cref="ReopenAfterSouthFailure"/> 会把状态退回 <see cref="RadioMainlineStage.HasTransmitter"/>，
    /// 让回复军方重新可达（坏结局军袭因此可达；40 天尸潮照常）。
    /// </summary>
    public const string SouthRefusedFlag = "south_refused";

    /// <summary>
    /// 回复军方后到军方来袭的间隔天数（用户拍板："回复日 + 2 的白天"军袭到期 → 南逃谢幕序列）。
    /// 数值真源已外置至 <c>military.json</c>（<see cref="MilitaryConfig.MilitaryRaidDelayDays"/>）；本属性委托到 catalog 段。
    /// </summary>
    public static int MilitaryRaidDelayDays => GameConfigCatalog.Section<MilitaryConfig>().MilitaryRaidDelayDays;

    /// <summary>广播台"发出设备"定点投放的发现点 id，须与 <c>TestExploration</c> 铺设的 Area2D 一致。</summary>
    public const string TransmitterDiscoveryId = "discovery_broadcast_transmitter";

    private const string HeardValue = "heard";
    private const string DeviceValue = "device";
    private const string RepliedValue = "replied_military";
    private const string CalledSouthValue = "called_south";

    // —— 状态读取 ——

    /// <summary>当前阶段（flag 未设/无法识别 → <see cref="RadioMainlineStage.Unknown"/>）。</summary>
    public static RadioMainlineStage Stage(StoryFlags flags)
    {
        string? v = flags?.Get(StageKey);
        if (string.Equals(v, CalledSouthValue, StringComparison.OrdinalIgnoreCase)) return RadioMainlineStage.CalledSouth;
        if (string.Equals(v, RepliedValue, StringComparison.OrdinalIgnoreCase)) return RadioMainlineStage.RepliedMilitary;
        if (string.Equals(v, DeviceValue, StringComparison.OrdinalIgnoreCase)) return RadioMainlineStage.HasTransmitter;
        if (string.Equals(v, HeardValue, StringComparison.OrdinalIgnoreCase)) return RadioMainlineStage.HeardBroadcast;
        return RadioMainlineStage.Unknown;
    }

    /// <summary>是否已收听过军方广播（含更后阶段——持械/终态都隐含听过）。</summary>
    public static bool HasHeardBroadcast(StoryFlags flags) => Stage(flags) >= RadioMainlineStage.HeardBroadcast;

    /// <summary>是否已持有发出设备（含终态——回复/呼叫都隐含持有过设备）。</summary>
    public static bool HasTransmitter(StoryFlags flags) => Stage(flags) >= RadioMainlineStage.HasTransmitter;

    /// <summary>是否已做出不可逆终局抉择（回复军方或呼叫南方）。</summary>
    public static bool HasChosenEnding(StoryFlags flags)
    {
        var s = Stage(flags);
        return s == RadioMainlineStage.RepliedMilitary || s == RadioMainlineStage.CalledSouth;
    }

    /// <summary>营地收音机交互是否应弹**抉择入口**（回复/呼叫/暂不）：已持设备且尚未做终局抉择。</summary>
    public static bool IsDecisionAvailable(StoryFlags flags) => Stage(flags) == RadioMainlineStage.HasTransmitter;

    // —— 状态推进（幂等，不降级；终态锁死）——

    /// <summary>
    /// 在营地收音机首次收听军方广播时开线：Unknown → HeardBroadcast。
    /// 已在更后阶段则无操作（不降级）。返回是否发生了推进（首次收听）。
    /// </summary>
    public static bool MarkBroadcastHeard(StoryFlags flags)
    {
        if (flags == null || Stage(flags) != RadioMainlineStage.Unknown) return false;
        flags.Set(StageKey, HeardValue);
        return true;
    }

    /// <summary>
    /// 在广播台取得发出设备：推进到 HasTransmitter（rank 跳升，即使未先收听也可——设备由探索取得）。
    /// 已持设备或已进终态则无操作（不降级、不覆盖终局抉择）。返回是否发生了推进（首次取得）。
    /// </summary>
    public static bool GrantTransmitter(StoryFlags flags)
    {
        if (flags == null || Stage(flags) >= RadioMainlineStage.HasTransmitter) return false;
        flags.Set(StageKey, DeviceValue);
        return true;
    }

    /// <summary>
    /// 抉择：回复军方（结局②岔口）。仅 <see cref="RadioMainlineStage.HasTransmitter"/> 可选（须持设备、未做过终局抉择）。
    /// 成功则置终态 RepliedMilitary 并记录回复日 <paramref name="currentDay"/>（军袭期满 = 回复日 + <see cref="MilitaryRaidDelayDays"/>）。
    /// 返回是否成功推进（非持设备态/已抉择 → false，什么都不做）。★不置 EndgameFreezeFlag：尸潮时限照走（军袭先到）。
    /// </summary>
    public static bool ReplyToMilitary(StoryFlags flags, int currentDay)
    {
        if (flags == null || Stage(flags) != RadioMainlineStage.HasTransmitter) return false;
        flags.Set(StageKey, RepliedValue);
        flags.Set(ReplyDayKey, currentDay.ToString());
        return true;
    }

    /// <summary>
    /// 抉择：呼叫南方营地（结局③岔口，唯一生路）。仅 <see cref="RadioMainlineStage.HasTransmitter"/> 可选。
    /// 成功则置终态 CalledSouth + 开启南逃线 <see cref="SouthEscapeOpenFlag"/>（后续 authored 流程读它）。
    /// 返回是否成功推进。★不置 EndgameFreezeFlag：南逃须抢在第 40 天尸潮前完成。
    /// </summary>
    public static bool CallSouth(StoryFlags flags)
    {
        if (flags == null || Stage(flags) != RadioMainlineStage.HasTransmitter) return false;
        if (flags.Has(SouthRefusedFlag)) return false; // 南方已拒（三问失败过）：不可再呼叫南方
        flags.Set(StageKey, CalledSouthValue);
        flags.Set(SouthEscapeOpenFlag, "true");
        return true;
    }

    /// <summary>
    /// 南方三问失败后重开电台（[SPEC-B11] 新矩阵）：CalledSouth 终态 → 退回
    /// <see cref="RadioMainlineStage.HasTransmitter"/>（回复军方重新可达），并置 <see cref="SouthRefusedFlag"/>
    /// （南方已拒，不可再呼叫南方）+ 清 <see cref="SouthEscapeOpenFlag"/>（南逃线关闭）。
    /// 仅 CalledSouth 可退回（非该终态 → false，什么都不做）。返回是否发生退回。
    /// ★不结束游戏：调用方失败后继续游戏，玩家可回复军方（→军袭坏结局）或死守（→第 40 天尸潮）。
    /// </summary>
    public static bool ReopenAfterSouthFailure(StoryFlags flags)
    {
        if (flags == null || Stage(flags) != RadioMainlineStage.CalledSouth) return false;
        flags.Set(StageKey, DeviceValue);        // 退回持设备态，抉择入口重新可用
        flags.Set(SouthRefusedFlag, "true");     // 南方已拒，CallSouth 从此被 guard 挡下
        flags.Set(SouthEscapeOpenFlag, null);    // 南逃线关闭（清 flag）
        return true;
    }

    /// <summary>南方是否已回绝过（三问失败）；供消费层隐藏"呼叫南方"选项。</summary>
    public static bool IsSouthRefused(StoryFlags flags) => flags != null && flags.Has(SouthRefusedFlag);

    // —— 军方来袭倒计时（结局②，回复军方后）——

    /// <summary>已记录的回复日（游戏第几天）；未回复/无法解析 → null。</summary>
    public static int? ReplyDay(StoryFlags flags)
        => flags != null && int.TryParse(flags.Get(ReplyDayKey), out int d) ? d : (int?)null;

    /// <summary>纯算术：给定回复日与当前天，军袭是否期满（当前天 ≥ 回复日 + 间隔）。</summary>
    public static bool MilitaryRaidDue(int replyDay, int currentDay) => currentDay >= replyDay + MilitaryRaidDelayDays;

    /// <summary>
    /// 军方白天来袭是否已期满：处于 RepliedMilitary 且当前天 ≥ 回复日 + <see cref="MilitaryRaidDelayDays"/>。
    /// 未回复军方或未到期 → false。
    /// </summary>
    public static bool IsMilitaryRaidDue(StoryFlags flags, int currentDay)
    {
        if (Stage(flags) != RadioMainlineStage.RepliedMilitary) return false;
        int? day = ReplyDay(flags);
        return day.HasValue && MilitaryRaidDue(day.Value, currentDay);
    }

    /// <summary>
    /// 一次性触发军方来袭事件钩子：到期（<see cref="IsMilitaryRaidDue"/>）且钩子未触发过则置
    /// <see cref="MilitaryRaidFiredKey"/> 并返回 true（首次），此后恒 false。军袭事件本体不实装——
    /// CampMain 在 true 分支留 TODO 挂点 + 安全 no-op（authored 待用户）。
    /// </summary>
    public static bool TryFireMilitaryRaidHook(StoryFlags flags, int currentDay)
    {
        if (flags == null || !IsMilitaryRaidDue(flags, currentDay)) return false;
        if (flags.Has(MilitaryRaidFiredKey)) return false;
        flags.Set(MilitaryRaidFiredKey, "true");
        return true;
    }

    // —— 文案草稿（供用户改；本类只承载 draft，运行时层薄接线）——

    /// <summary>营地收音机循环广播文本（**草稿供用户改**）。军方救援频段循环播报 + 杂音；不写死背叛伏笔（留给用户）。</summary>
    public const string MilitaryBroadcastLoop =
        "「……滋——这里是国民警卫队第七救援频段，向所有仍在收听的幸存者广播。……滋啦——" +
        "请保持冷静，就地固守，避免暴露行踪。我们正在重建收容区，逐步恢复对各区域的接应。……嗞——" +
        "如你们持有可发出的通讯设备，请在听到本讯息后回复坐标，我们会派人前来。……滋滋——" +
        "重复：保持冷静，就地固守，等待接应。……」\n\n" +
        "（讯息到此中断，隔了几秒又从头开始，一遍遍地循环。你手里这台简易收音机只能收，发不出去。）";

    /// <summary>抉择面板标题（**草稿供用户改**）。持发出设备后同一交互升级为此抉择。</summary>
    public const string DecisionPrompt =
        "发出设备已就位，电台能发得出去了。频道那头，军方的循环讯息仍在一遍遍重复。\n\n你要怎么做？";

    /// <summary>抉择面板标题·南方已回绝版（**草稿供用户改**）。三问失败后，南方选项已不在，只剩回复军方/暂不。</summary>
    public const string DecisionPromptSouthRefused =
        "南边那扇门已经关上了。频道那头，只剩军方的循环讯息还在一遍遍重复。\n\n你要怎么做？";

    /// <summary>抉择选项：回复军方（**草稿供用户改**）。</summary>
    public const string ReplyOptionLabel = "回复军方，报出我们的坐标";

    /// <summary>抉择选项：呼叫南方营地（**草稿供用户改**）。</summary>
    public const string CallSouthOptionLabel = "改呼南边的营地，试着求一条生路";

    /// <summary>抉择选项：暂不（**草稿供用户改**）。不可逆终局岔口，故默认给"再想想"的退出。</summary>
    public const string DeferOptionLabel = "先不急，再想想";

    /// <summary>回复军方的二次确认文案（**草稿供用户改**）。不可逆，故二次确认。</summary>
    public const string ReplyConfirmPrompt =
        "一旦报出坐标，就收不回来了。你确定要回复军方吗？";

    /// <summary>呼叫南方的二次确认文案（**草稿供用户改**）。不可逆，故二次确认。</summary>
    public const string CallSouthConfirmPrompt =
        "改呼南边，就等于把命押在他们肯不肯开门上。你确定要呼叫南方营地吗？";

    /// <summary>广播台取得发出设备的发现叙事标题（**草稿供用户改**）。</summary>
    public const string TransmitterPickupTitle = "机房角落，那台还能用的发射机";

    /// <summary>广播台取得发出设备的发现叙事正文（**草稿供用户改**）。只描述取得设备，不写死后续走向。</summary>
    public const string TransmitterPickupNarrative =
        "广播台的机房积了厚厚一层灰，大半设备早被搬空或砸烂了。可靠墙那台老式发射机竟还立着——" +
        "指示灯在你拨动电源时迟疑地亮了一下，又稳住了。\n\n" +
        "你拆下它的核心组件和天线接口，小心地收进背包。有了这东西，营地那台只能收听的破收音机，" +
        "终于能发得出声音了。\n\n" +
        "回去以后，你得决定：这一声，要喊给谁听。";
}
