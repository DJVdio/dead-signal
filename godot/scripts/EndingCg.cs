using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 RadioMainline.cs / GameOverCondition.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 三条正史结局的 **CG 文本定稿**（结局矩阵 RESOLVED，见 journal §3 用户总纲；[SPEC-B11] 用户拍板"三结局 CG 直接做"）：
//   · CG①=第 40 天尸潮全灭（悲壮终局，呼应瞭望台所见）——GameOverCondition 全灭 且 _siegeActive 时替代普通败北文本。
//   · CG②【已推翻旧设计·重写为「南逃谢幕」两幕 CG】=回复军方 → 第 n+2 天白天军袭：军人带顶级装备屠尽全营，
//     **随机一名幸存者半残南逃**（不再是"全员在营才全灭"，而是**无条件强制终局序列**——见 SouthEscapeEnding）。
//     旧「全员在营被屠尽 → EndingCg.ForGameOver 选 CG②」路由作废（新序列不经全灭判定，序列末尾直接 EndingPanel.Show）。
//     新 CG 分两幕：CG-A（<see cref="MilitaryRaidMassacre"/>）=屠营+半残南逃，接玩家操作段单线南逃；
//     CG-B（<see cref="SouthEscapeFarewell"/>）=峡谷前大桥未落、两哨兵冷眼看着 → 黑屏谢幕。**军方动机保持不解释**（[SPEC-B11] 留白）。
//   · CG③=南逃成功（唯一生路，留悬念不圆满）——南逃启程完成时播（见 SouthTrial.EscapeCg 组装启程旁白+本 CG）。
//
// CG 文本以 drafts-authored.md §4 底稿升正式（用户拍板"直接做"，微调润色，正史事实不改）。分段承载：每个 string 为一屏（EndingPanel 逐段渐显）。
// 结局路由 EndingRouting.ForGameOver 为纯函数，供 CampMain 全灭时选 CG，亦可单测。

/// <summary>全灭时应播放的结局种类（据全灭成因路由）。</summary>
public enum EndingKind
{
    /// <summary>普通全灭（非尸潮、非军袭）：保留原 GameOverPanel「营地无人生还」行为。</summary>
    Normal,

    /// <summary>第 40 天尸潮围攻全灭（CG①·悲壮终局）。</summary>
    HordeSiege,

    /// <summary>
    /// 回复军方后军袭 → 「南逃谢幕」序列（CG②）。
    /// ★已推翻旧「全员在营才全灭」设计：军袭现为**无条件强制终局**（随机一名半残幸存者南逃），
    ///   不经全灭判定；此枚举与 <see cref="ForGameOver"/> 保留供**遗留路由/测试**引用（运行时不再走全灭路由触发）。
    /// </summary>
    MilitaryWipe,
}

/// <summary>三条正史结局 CG 的**分段文本定稿**（drafts §4 升正式）+ 全灭结局路由纯函数。</summary>
public static class EndingCg
{
    /// <summary>
    /// 全灭结局路由：军袭全灭上下文优先，其次尸潮围攻，否则普通全灭。纯函数，供 CampMain 在
    /// <see cref="GameOverCondition.AllSurvivorsDead(int)"/> 判真时选 CG。
    /// ★军袭已改为**强制终局南逃谢幕序列**（<see cref="SouthEscapeEnding"/>），不再经此全灭路由触发；
    ///   本函数与 <paramref name="militaryRaidWipe"/> 分支保留为遗留/兜底（运行时 <c>_militaryRaidWipeContext</c> 恒 false）。
    /// </summary>
    /// <param name="siegeActive">当前是否处于第 40 天尸潮无限围攻（_siegeActive）。</param>
    /// <param name="militaryRaidWipe">遗留：全灭是否由军方来袭致全员在营被屠尽（运行时不再置位）。</param>
    public static EndingKind ForGameOver(bool siegeActive, bool militaryRaidWipe)
    {
        if (militaryRaidWipe) return EndingKind.MilitaryWipe;
        if (siegeActive) return EndingKind.HordeSiege;
        return EndingKind.Normal;
    }

    // —— CG①：第 40 天尸潮全灭（悲壮终局）——

    /// <summary>CG① 标题（画面顶部，可空则不显）。</summary>
    public const string HordeSiegeTitle = "";

    /// <summary>CG① 分段文本（每段一屏，逐段渐显）。呼应 LookoutSighting《正北方，天际线尽头》那片"阴云"。</summary>
    public static readonly IReadOnlyList<string> HordeSiege = new[]
    {
        "望远镜里的那片阴云，终于漫到了脚下。",
        "它不是云。你们早就知道。",
        "一波，又一波。你们把门顶了一次又一次，把还能开火的人一个个补到缺口上。",
        "没有援军。从来没有。",
        "一个接一个，名字从你心里的那份名单上划去，再没能添回来。",
        "你守住了这里，直到最后一个人。",
        "这样做，是对的吗？",
    };

    // —— CG②·第一幕（CG-A）：军袭屠营开场旁白（接玩家操作段·南逃）——
    // ⚠️ 文案为**占位草稿**（忠实用户 authored 剧情节拍：军人带顶级装备屠尽全营、随机一人半残南逃、军方动机不解释），
    //   待用户润色定稿。CG-A 主体是**冻结脚本演出**（军人 spawn 屠杀，见 CampMain.SouthEscape.cs），本段作开场字幕叠层。

    /// <summary>CG-A 标题。</summary>
    public const string MilitaryRaidMassacreTitle = "";

    /// <summary>
    /// CG-A 分段文本（军袭屠营开场旁白·占位草稿）。成因＝你自己按下的求救键招来的人。
    /// **军方为何屠杀，全篇不解释**（[SPEC-B11] 动机留白）——只留收音机循环广播的反讽。
    /// </summary>
    public static readonly IReadOnlyList<string> MilitaryRaidMassacre = new[]
    {
        "你按响了求救的信号。",
        "他们真的来了——成队的军人，武装到牙齿。",
        "（收音机还在一遍遍地循环：「……请保持冷静，就地固守……等待接应……」）",
        "枪声很短，很整齐。一个接一个，他们伏在自己的位子上，再没有起身。",
        "你等来的，是你亲手呼叫的那一方。",
        "混乱里，只有一个人还在动——朝着南边，一步一拐地跑了出去。",
    };

    /// <summary>
    /// 旧名保留：<see cref="EndingKind.MilitaryWipe"/> 路由 / 遗留测试仍引用此字段，指向同一 CG-A 屠营文本。
    /// （旧"全员在营被屠尽·无人回得来"文本已按新 authored 剧情作废重写为屠营+半残南逃。）
    /// </summary>
    public static readonly IReadOnlyList<string> MilitaryWipe = MilitaryRaidMassacre;

    /// <summary>旧名保留（遗留引用）。</summary>
    public const string MilitaryWipeTitle = "";

    // —— CG②·第二幕（CG-B）：峡谷前谢幕（🔴 REUSABLE：军袭 + 将来 40 天尸潮结局共用此谢幕）——
    // ⚠️ 文案为**占位草稿**（忠实用户 authored 节拍：南逃到峡谷前、对方大桥没有落下、只有两个哨兵冷眼看着 → 黑屏谢幕），
    //   待用户润色定稿。EndingPanel 播完即终局（重新开始/退出）。

    /// <summary>CG-B 标题。</summary>
    public const string SouthEscapeFarewellTitle = "";

    /// <summary>
    /// CG-B 分段文本（峡谷前谢幕·占位草稿）。玩家操作段单线南逃到峡谷前后播出，播完黑屏谢幕（游戏结束）。
    /// 南逃者是保留身份的桥梁角色（见 <see cref="SouthEscapeEnding"/>），此处只谢幕、第二幕「峡谷营地」很久远排期。
    /// </summary>
    public static readonly IReadOnlyList<string> SouthEscapeFarewell = new[]
    {
        "一路向南，密林尽头是那道峡谷。",
        "对岸就是活路。可是那座桥，没有落下来。",
        "桥头站着两个哨兵。他们看着你，冷冷地，没有一个人动。",
        "他们不会放下那座桥。你也过不去。",
        "风从峡谷里灌上来。你停在原地，退无可退，也进无可进。",
    };

    // —— CG③：南逃成功（唯一生路，留悬念不圆满）——

    /// <summary>CG③ 标题。</summary>
    public const string SouthEscapeTitle = "";

    /// <summary>
    /// CG③ 分段文本（南逃成功的结尾段）。不是胜利，是"还没死"——留悬念、开放，一丝微光，为二号大地图伏笔。
    /// 完整播放序列＝启程旁白（<see cref="SouthTrial.DepartureNarration"/>，含三问变体一句）＋本段，见 <see cref="SouthTrial.EscapeCg"/>。
    /// </summary>
    public static readonly IReadOnlyList<string> SouthEscape = new[]
    {
        "你们逃出来了。",
        "活下来的没剩几个。带走的物资，撑不了多久。",
        "峡谷的风，比城里干净一些。前面是什么，没人知道。",
        "至少，信号还在——",
        "只是这一次，是你们自己，往前走。",
    };

    /// <summary>据结局种类取 CG 分段文本（<see cref="EndingKind.Normal"/> 无 CG → 空列表，调用方回落 GameOverPanel）。</summary>
    public static IReadOnlyList<string> ForKind(EndingKind kind) => kind switch
    {
        EndingKind.HordeSiege => HordeSiege,
        EndingKind.MilitaryWipe => MilitaryWipe,
        _ => System.Array.Empty<string>(),
    };
}
