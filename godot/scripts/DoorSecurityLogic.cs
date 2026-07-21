namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 DoorLogic.cs / VisionLogic.cs / BreachLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（真去切门的状态、重烘焙导航、查门口有没有人）归 CampMain.SecureCampDoors，本文件只出**纯判定函数**。

/// <summary>
/// <b>自动闩门</b>：营地大门在几个关键时刻**自己闩上**，不必玩家每天手动去点。
///
/// <para>
/// <b>它堵的洞</b>：门闩此前<b>纯靠玩家手动</b>（<see cref="DoorLogic.ClosedRestingState"/>：关门即闩）。
/// 于是玩家派三个人出去探索，<b>营地大门就那么敞着</b>——夜里劫掠者散步进来把营地端了，
/// 而玩家正在地图另一头搜刮，毫不知情。<b>这不是"硬核"，是玩家无法预期的陷阱。</b>
/// </para>
///
/// <para>
/// <b>为什么规则挂在「相位」上，而不是「营里有人就闩上」这条持续规则</b>（要害）：
/// 若"有人留守 → 门自动闩上"每帧成立，那玩家推开门的下一帧它就会自己闩回去——<b>他将永远开不了自家的门</b>。
/// 故自动闩门只发生在**玩家的注意力要离开营地 / 危险将至**的那几个<b>时刻</b>（<see cref="ShouldSecureAt"/>）。
/// 过了那一刻，玩家整个相位内都能自由开门，没有任何东西跟他抢方向盘。
/// <b>——安全的默认值，玩家仍可随时推翻。</b>
/// </para>
///
/// <para>
/// <b>为什么落在营地昼夜流程状态机而不是夜防/守卫调度</b>：本仓的"出门"<b>不是空间动作</b>——探索队是**场景切换**走的
/// （<c>CampMain.LoadExplorationLevel</c> 把队员 reparent 进关卡场景），<b>没有人真的走过大门</b>。
/// 而 <c>NightWatchContest</c> 是"袭营发生时"的对抗结算、<c>ShiftSchedule</c> 是纯函数（谁上白班谁上夜班），
/// 二者都<b>不是时刻、不是事件源</b>。全仓唯一能表达"此刻队伍出发了 / 此刻天黑了"的东西，就是
/// <see cref="DayPhase"/> 内部流程状态机。
/// </para>
/// </summary>
public static class DoorSecurityLogic
{
    /// <summary>
    /// 这个相位**开始**的那一刻，要不要替玩家把营地大门闩上。<b>只有三个时刻</b>：
    /// <list type="bullet">
    /// <item><see cref="DayPhase.DayTravel"/> —— <b>探索队出发</b>。这正是那个洞：人一走，门得带上。
    /// 也是全仓唯一一个能替"出门的人"关门的时机（他们不走门，是场景切换走的）。</item>
    /// <item><see cref="DayPhase.NightPrep"/> —— <b>天黑</b>。睡前检查一遍门锁，是个人都会做。
    /// 夜里是劫掠者和尸潮的时段，一扇忘了闩的门就是全营的命。</item>
    /// <item><see cref="DayPhase.DayReturn"/> —— <b>探索队回营</b>。把夜里可能开着的门重新闩好，回到安全默认。</item>
    /// </list>
    ///
    /// <para>
    /// <b>其余相位一律不管</b>（白天在营、夜里行动中）：玩家想开着门晒太阳、想开门迎战、想把丧尸放进来
    /// 在门口打伏击——都是他的战术自由，他人就在场，看得见后果。自动化只在他<b>看不见</b>的时候兜底。
    /// </para>
    /// </summary>
    public static bool ShouldSecureAt(DayPhase phase) => phase switch
    {
        DayPhase.DayTravel => true,  // 出发：人走了，门带上
        DayPhase.DayReturn => true,  // 回营：门重新闩好
        DayPhase.NightPrep => true,  // 入夜：睡前锁门
        _ => false,
    };

    /// <summary>
    /// 扫到一扇门时，这扇门该不该被<b>自动闩上</b>。
    /// <list type="bullet">
    /// <item><b>只动能闩的门</b>（<paramref name="barrable"/>，即营地大门）。民居的门没有闩——而且这是**零回归的要害**：
    /// 住宅/仓库/牛棚的门默认<b>开着</b>，营内幸存者靠它寻路进屋读书、睡觉、干活；自动闩门若把它们一并关上，
    /// <b>全营的人会卡死在门外</b>。</item>
    /// <item><b>敞着的（<see cref="DoorState.Open"/>）要闩</b>——玩家忘了关的那一扇。</item>
    /// <item><b>只是关着的（<see cref="DoorState.Closed"/>）也要闩</b>——因为<b>劫掠者会开门</b>：
    /// 「关着 + 没锁 + 够得着」三条全中 ⇒ 推门直入，250HP 形同虚设。<b>对会拧门把手的敌人来说，「关着」就等于「没关」。</b></item>
    /// <item><b>已经闩上的不重复动</b>（幂等）——否则每个相位都要白白重烘焙一次导航、刷一条没意义的提示。</item>
    /// <item><b>锁着的（<see cref="DoorState.Locked"/>）不碰</b>——它已经挡着了，而"锁"和"闩"是两种不同的信息
    /// （锁能撬，闩撬不了）；把 Locked 悄悄改写成 Barred 会抹掉"这扇门有锁"这件事。</item>
    /// <item><b>门口站着人（<paramref name="doorwayOccupied"/>）就不闩</b>——否则会把他实心夹在门板里
    /// （与玩家手动关门同一条护栏，见 <c>CampMain.IsDoorwayOccupied</c>）。</item>
    /// </list>
    ///
    /// <para>
    /// <b>【本单拍板】签名里没有"营里还剩几个人"这一维——空营地一样闩。</b>
    /// 决定性理由：<b>回营根本不经过大门</b>——探索队是场景切换回来的（<c>UnloadExplorationLevel</c> 把人
    /// reparent 到营地正中），不是走回来的。故"闩上了，回来的人得自己开门"这个代价，<b>在本仓根本不存在</b>
    /// ⇒ 最后一人出门时闩上是<b>纯收益</b>：没有任何成本，却挡住了"空营地被端"这个最要命的结局。
    /// 一条不影响结果的输入，不该出现在签名里。
    /// </para>
    /// </summary>
    public static bool ShouldAutoBar(DoorState state, bool barrable, bool doorwayOccupied)
    {
        if (!barrable || doorwayOccupied)
        {
            return false;
        }
        return state is DoorState.Open or DoorState.Closed;
    }
}
