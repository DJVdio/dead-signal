using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ContainerLoot.cs / CraftingJob.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 逐件搜刮（《三角洲行动》式）——用户拍板原话：
//   「可搜刮点交互，物品需要和三角洲行动一样，**一件一件转出来**，
//     **防止玩家在危险中快速交互完就跑**，**每个物品搜刮速度可以一样，不用做分级**。」
//
// 设计目的：搜刮 = **一段暴露时间**。你站着不动的每一秒都在丧尸的视野锥里，
// 门外啃围栏的那位不会等你翻完最后一格抽屉。于是玩家真正要做的决策是：**拿到第几件就跑？**
//
// 与既有两道约束的咬合（方向不同，缺一不可）：
//   · 负重上限（ExpeditionBag/CarryCapacity）限制你**能带走**多少；
//   · 逐件搜刮限制你**有时间拿**多少。
//   ⇒ "背得动但来不及拿" 是本作独有的紧张感。**故耗时不得为了流畅而调到可忽略。**
//
// 与 CraftingJob（工时制在制品）的关键区别 —— 形态像，语义不同，故不复用那个类：
//   · CraftingJob：中断＝暂停，**已投入工时不丢**（半成品还在工作台上，明天接着做）。
//   · LootSession：中断＝**当前这件的进度作废**（手伸进抽屉一半被咬了，那件东西还在抽屉里）。
//     已经**完整取出**的件已经进包了，谁也拿不走。这正是"拿到第几件就跑"这个决策的赌注所在。
//
// 一"件" = 藏物清单里的**一个条目**（LootItem），不是一个单位：8 发子弹是一堆、一次转出，
// 同《三角洲》口径。故耗时按条目数算，不按数量/重量/价值——用户明说"不用做分级"。

/// <summary>
/// 一次逐件搜刮会话（可变：进度逐帧累积）。承载"哪件正在转出来、转了多久、还剩几件、还要等多久"。
/// 只做**计时与出件**，不碰库存、不碰容器登记——出件后由调用方去 <see cref="ContainerLoot.TakeNext"/> 实扣、
/// 去 <c>CollectLoot</c> 实收（同 <c>CraftingLogic</c>「判定与结算分离」通则）。
/// <para>
/// <b>这是一个「派下去的活」，不是一个「打开的界面」</b>（用户拍板：「允许玩家控制一个角色去搜刮转物品，
/// <b>然后控制另一个角色</b>」）。所以：一人一份会话、无静态态 ⇒ <b>多人同时各搜各的天然成立</b>；
/// 玩家把镜头/控制权切给别人，这份会话照样在后台推进。<b>绝不做成模态面板</b>——那会让
/// "一个人蹲着掏尸体、另一个人在门口盯着围栏外的动静"这个分工当场归零。
/// </para>
/// </summary>
public sealed class LootSession
{
    /// <summary>
    /// 每件基础搜刮耗时（**实时秒**，拟定待调）。所有物品**一律相同**——用户明令不做分级。
    /// <para>
    /// 校准依据（daynight.json：白天 720s、单程 travelTime 120s ⇒ 一趟白天**现场可用 ≈ 480 秒**）：
    /// 大点南林村庄 68 件 → 204s 纯站桩＝吃掉现场时间的 <b>42%</b>，再算上关内跑动与战斗，
    /// **一趟绝对搜不完一个大点**（正是要的"疼"）；小点 13~19 件 → 39~57s，一趟能清干净（不无聊）。
    /// 单个容器平均 2.3 件 ⇒ 约 <b>7 秒</b>站着不动——够一只丧尸从视野边缘扑到你脸上。
    /// </para>
    /// </summary>
    public const float DefaultSecondsPerItem = 3.0f;

    // 出件判定的容差（秒）。逐帧 delta 是 float，30 帧 × 0.1s 累加起来**够不到** 3.0——
    // 不留容差的话，最后一件会永远差一丁点转不出来（进度条卡在 99% 那种 bug）。
    // 内部累加器另用 double，把漂移压到这个容差之下。
    private const double CompletionEpsilon = 1e-4;

    private readonly List<LootItem> _remaining;

    // 当前这件已投入的秒数（内部 double：见 CompletionEpsilon 的注释）。
    private double _itemElapsed;

    /// <summary>本会话搜的容器名（＝ <see cref="ContainerLoot"/> 的登记键；探索点则为 cacheId）。</summary>
    public string Container { get; }

    /// <summary>
    /// 每件的**基础工作秒数**（对所有人、所有物品都一样 —— 用户明令物品之间不分级）。
    /// 一个人实际要站多久 = 这个数 ÷ 他的工作效率，见 <see cref="EffectiveSecondsPerItem"/>。
    /// </summary>
    public float SecondsPerItem { get; }

    /// <summary>当前这件已投入的秒数（0 ~ <see cref="SecondsPerItem"/>）。中断即清零。</summary>
    public float ItemElapsedSeconds => (float)_itemElapsed;

    /// <summary>本会话已完整取出的件数（这些已经进包了，中断也带得走）。</summary>
    public int TakenCount { get; private set; }

    public LootSession(string container, IEnumerable<LootItem> remaining, float secondsPerItem = DefaultSecondsPerItem)
    {
        Container = container ?? "";
        _remaining = remaining?.ToList() ?? new List<LootItem>();
        SecondsPerItem = secondsPerItem <= 0f ? 0f : secondsPerItem;
    }

    /// <summary>
    /// 某人实际每件要站多久（秒）＝ <b>基础工作秒数 ÷ 工作效率</b>（用户拍板："搜刮速度要受操作能力影响"）。
    /// <para>
    /// <b>乘算，不是加算</b>（项目通则）。效率取调用方那条既有乘子链（<c>CampMain.WorkEfficiencyOf</c> ＝
    /// 操作能力 × 山姆光环 …，与制作/挖废墟同源，别另立一套）。山姆缺两指 ⇒ 0.86 ⇒ 3.0s → <b>3.49s/件</b>，慢约 16%。
    /// </para>
    /// <para>
    /// ⚠️ <b>效率 ≤ 0 ⇒ 无穷大：断了双手的人翻不了箱子。</b> 这是乘算的必然结果，也是对的——
    /// 绝不能给一个下限兜底，否则"没有手的人"会凭空获得搜刮速度。
    /// </para>
    /// </summary>
    public static float EffectiveSecondsPerItem(double workEfficiency, float baseSeconds = DefaultSecondsPerItem)
        => workEfficiency <= 0d ? float.PositiveInfinity : (float)(baseSeconds / workEfficiency);

    /// <summary>还留在容器里的件（含正在转出来的那件——**没转完就还是它的**）。</summary>
    public IReadOnlyList<LootItem> Remaining => _remaining;

    /// <summary>还剩几件（含当前正在转的那件）。</summary>
    public int RemainingCount => _remaining.Count;

    /// <summary>搜空了（没得拿了）。</summary>
    public bool IsComplete => _remaining.Count == 0;

    /// <summary>正在转出来的那件（搜空则 <c>null</c>）。</summary>
    public LootItem? CurrentItem => _remaining.Count > 0 ? _remaining[0] : null;

    /// <summary>当前这件的进度 [0,1]（搜空则 1；零耗时视为即完）。</summary>
    public float ItemProgress => _remaining.Count == 0
        ? 1f
        : SecondsPerItem <= 0f
            ? 1f
            : Math.Clamp((float)(_itemElapsed / SecondsPerItem), 0f, 1f);

    /// <summary>
    /// 全部搜完还剩多少**工作秒**（＝ 效率 1.0 的人要站多久）。给 <see cref="RemainingRealSeconds"/> 换算用。
    /// ＝ 当前这件的剩余 + 后面那些件的整份基础耗时。
    /// </summary>
    public float RemainingWorkSeconds => _remaining.Count == 0
        ? 0f
        : (float)(Math.Max(0d, SecondsPerItem - _itemElapsed) + (_remaining.Count - 1) * (double)SecondsPerItem);

    /// <summary>
    /// <b>这个人</b>全部搜完还要站多少**实时秒** —— 玩家做"要不要撤"决策的唯一依据，UI 必须显示它。
    /// 效率 ≤ 0（断手）⇒ 无穷大：他永远搜不完。
    /// </summary>
    public float RemainingRealSeconds(double workEfficiency)
        => _remaining.Count == 0
            ? 0f
            : workEfficiency <= 0d ? float.PositiveInfinity : (float)(RemainingWorkSeconds / workEfficiency);

    /// <summary>
    /// 推进搜刮（只在"这个人正站在容器旁翻"的帧调）。本帧投入的工作秒 = <paramref name="deltaSeconds"/> ×
    /// <paramref name="workEfficiency"/>（**乘算通则**：效率 0 ⇒ 一点都不动，断手的人翻不了箱子）。
    /// 累计满 <see cref="SecondsPerItem"/> 即**转出一件**，余数滚进下一件（一帧 delta 很大时可能一次转出多件）。
    /// 返回本次转出来的件（按取出顺序）。
    /// <para>调用方拿到返回值后须去 <see cref="ContainerLoot.TakeNext"/> 实扣、并把这几件真正收进背包/库存。</para>
    /// <para><b>每个搜刮者持有自己的一份 LootSession</b>（本类无静态态）⇒ 多人同时各搜各的天然成立。</para>
    /// </summary>
    /// <param name="deltaSeconds">本帧流逝的实时秒（≤0 或已搜空 → 什么都不发生）。</param>
    /// <param name="workEfficiency">
    /// 这个人的工作效率（<c>CampMain.WorkEfficiencyOf</c> ＝ 操作能力 × 山姆光环…，与制作/挖废墟**同一条乘子链**）。
    /// 健全饱食者 = 1.0 ⇒ 基础耗时原样。≤0 ⇒ 不推进。
    /// </param>
    public IReadOnlyList<LootItem> Advance(double deltaSeconds, double workEfficiency = 1d)
    {
        if (deltaSeconds <= 0d || workEfficiency <= 0d || IsComplete)
        {
            return Array.Empty<LootItem>();
        }

        _itemElapsed += deltaSeconds * workEfficiency;

        List<LootItem>? taken = null;
        while (_remaining.Count > 0 && _itemElapsed >= SecondsPerItem - CompletionEpsilon)
        {
            _itemElapsed = Math.Max(0d, _itemElapsed - SecondsPerItem);
            (taken ??= new List<LootItem>()).Add(_remaining[0]);
            _remaining.RemoveAt(0);
            TakenCount++;

            if (SecondsPerItem <= 0f)
            {
                break; // 零耗时：一帧只转一件，防死循环
            }
        }

        if (_remaining.Count == 0)
        {
            _itemElapsed = 0d; // 搜空：余数不留
        }

        return (IReadOnlyList<LootItem>?)taken ?? Array.Empty<LootItem>();
    }

    /// <summary>
    /// 中断（走开 / 挨打 / 玩家喊停）：<b>当前这件的进度作废</b>，它还在容器里；
    /// 已完整取出的件不受影响（已经进包了）。会话本身不销毁——回来还能接着搜，但**这件得从头翻起**。
    /// </summary>
    public void Interrupt() => _itemElapsed = 0d;
}
