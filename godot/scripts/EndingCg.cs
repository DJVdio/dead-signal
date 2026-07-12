using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 RadioMainline.cs / GameOverCondition.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 三条正史结局的 **CG 文本定稿**（结局矩阵 RESOLVED，见 journal §3 用户总纲；[SPEC-B11] 用户拍板"三结局 CG 直接做"）：
//   · CG①=第 40 天尸潮全灭（悲壮终局，呼应瞭望台所见）——GameOverCondition 全灭 且 _siegeActive 时替代普通败北文本。
//   · CG②=回复军方后军袭导致全员在营全灭（背叛·全灭变体）——**军方动机保持不解释**（[SPEC-B11] 军方动机留白定稿）。
//     军袭事件本体未实装（RadioMainline 军袭钩子 no-op），故此路由为**预留分支**：待军袭实装、若军袭时全员在营被屠尽，
//     由军袭事件置全灭上下文 → 走此 CG（见 CampMain._militaryRaidWipeContext / TryTriggerMilitaryRaid TODO）。
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

    /// <summary>回复军方后军袭致全员在营全灭（CG②·背叛全灭变体，预留）。</summary>
    MilitaryWipe,
}

/// <summary>三条正史结局 CG 的**分段文本定稿**（drafts §4 升正式）+ 全灭结局路由纯函数。</summary>
public static class EndingCg
{
    /// <summary>
    /// 全灭结局路由：军袭全灭上下文优先（预留，军袭本体待实装），其次尸潮围攻，否则普通全灭。
    /// 纯函数，供 CampMain 在 <see cref="GameOverCondition.AllSurvivorsDead(int)"/> 判真时选 CG。
    /// </summary>
    /// <param name="siegeActive">当前是否处于第 40 天尸潮无限围攻（_siegeActive）。</param>
    /// <param name="militaryRaidWipe">全灭是否由军方白天来袭致全员在营被屠尽（军袭事件本体置位，预留）。</param>
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

    // —— CG②：回复军方后军袭全员在营全灭（背叛·全灭变体，军方动机不解释）——

    /// <summary>CG② 标题。</summary>
    public const string MilitaryWipeTitle = "";

    /// <summary>
    /// CG② 分段文本。成因＝你自己按下的求救键招来的人（仅军袭时全员在营、无人外出＝全灭）。
    /// **军方为何屠杀，全篇不解释**（[SPEC-B11] 动机留白）——只留收音机循环广播的反讽。
    /// </summary>
    public static readonly IReadOnlyList<string> MilitaryWipe = new[]
    {
        "你按响了求救的信号。",
        "他们真的来了。",
        "（收音机还在一遍遍地循环：「……请保持冷静，就地固守……等待接应……」）",
        "没有人在外面。这一次，没有人回得来。",
        "一个接一个，他们伏在自己的位子上，再没有起身。",
        "你等来的，是你亲手呼叫的那一方。",
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
