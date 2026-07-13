using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat; // Faction（纯 C# 引擎类型，无 Godot 依赖）

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 DoorLogic.cs / LootSession.cs / SalvageLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（弹菜单、派人走过去、逐帧推进）归 Godot 层（CampMain + SiteActionPopup），本文件只出**菜单内容的规则**。

/// <summary>玩家能对一处目标（门/围栏）下达的动作。</summary>
public enum SiteAction
{
    /// <summary>推开（含抬起自家的门闩）。</summary>
    OpenDoor,

    /// <summary>关上（能闩的门顺手闩上）。</summary>
    CloseDoor,

    /// <summary>撬锁：<b>安静、慢、要铁丝</b>。</summary>
    PickLock,

    /// <summary>静默拆除（围栏）：<b>安静、慢</b>。侧面开个洞，绕开正门。</summary>
    SilentDismantle,

    /// <summary>破坏：<b>快、很响</b>。整条街都听得见。</summary>
    Bash,
}

/// <summary>
/// 右键菜单里的一项。<paramref name="Enabled"/>=false 时仍然**列出来**（灰掉 + 写明原因）——
/// 直接把选项藏掉会让玩家以为"这门撬不了"，而真相是"你没带铁丝"。列出来才教得会他。
/// </summary>
public readonly record struct SiteActionOption(
    SiteAction Action,
    string Label,
    string Hint,
    bool Enabled,
    string DisabledReason);

/// <summary>
/// <b>玩家对着一扇门 / 一段围栏，右键能干什么</b>（用户拍板：「右键点击敌营门时选择<b>撬锁/破坏</b>，
/// 点击围栏时<b>静默拆除/破坏</b>」）。
///
/// <para>
/// <b>它让"潜入敌营"闭环</b>：摸到据点 → <b>撬锁</b>（安静、慢，得算准哨兵背对的窗口）/
/// <b>静默拆围栏</b>（侧面开洞，绕开正门）/ <b>破坏</b>（快，但整个据点瞬间警觉，然后他们包抄你）。
/// 三条路是同一个取舍的三张面孔：<b>安静但慢 vs 快但很响</b>。
/// </para>
///
/// <para>
/// <b>⚠️ 核心判断：菜单只在"真有得选"时才存在</b>（<see cref="NeedsMenu"/>）。
/// 一扇没锁的门只有"推开"这一个动作（用户拍板「开门只有一种动作，不分轻推和踹开」）——
/// 给它弹一个只有一项的菜单，是<b>拿仪式感惩罚玩家</b>。≥2 个可用动作才弹菜单，否则右键 = 直接干那一件事，
/// 保持既有的一击直达手感。
/// </para>
///
/// <para>
/// <b>⚠️ 对称性（硬要求）：本类一个数值都不自己定。</b>耗时/噪音/判定<b>全部转发</b>到
/// <see cref="DoorLogic"/> / <see cref="NoiseLogic"/> 的单一真源 —— 玩家砸门和丧尸砸门是同一个 180，
/// 玩家开门和劫掠者开门是同一个 100。<b>不给玩家开后门，也不给 AI 开后门。</b>
/// （已上单测：<c>SiteActionsTests.玩家和AI用同一套噪音_菜单不许自己造一套数值</c>）
/// </para>
/// </summary>
public static class SiteActions
{
    /// <summary>这个动作会发出多大噪音。<b>纯转发</b>，见类注的对称性要求。</summary>
    public static double NoiseOf(SiteAction action) => action switch
    {
        SiteAction.PickLock => NoiseLogic.LockpickNoiseRadius, // 30：比走路还轻，谁也惊动不了
        SiteAction.OpenDoor => NoiseLogic.DoorNoiseRadius,     // 100：一扇旧门被推开的吱呀
        SiteAction.CloseDoor => NoiseLogic.DoorNoiseRadius,    // 同上（开门只有一种动作，关门也一样）
        SiteAction.Bash => NoiseLogic.BreachNoiseRadius,       // 180：全表最响的非枪噪音
        SiteAction.SilentDismantle => SilentDismantleNoise,    // 见下（等 impl-raider-ai 的单一真源）
        _ => 0,
    };

    /// <summary>撬一次锁要多久。<b>纯转发</b> <see cref="DoorLogic.PickSeconds"/>（不给玩家加速）。</summary>
    public static double PickSecondsFor(LockTier tier) => DoorLogic.PickSeconds(tier);

    /// <summary>撬一次锁的成功率。<b>纯转发</b> <see cref="DoorLogic.PickChance"/>。</summary>
    public static double PickChanceFor(LockTier tier) => DoorLogic.PickChance(tier);

    /// <summary>
    /// 静默拆除的噪音（35）。<b>纯转发</b> <see cref="SilentDismantleLogic.NoiseRadius"/>。
    /// <para>
    /// ⚠️ <b>直接调「通用机制层」<see cref="SilentDismantleLogic"/>，不绕道 <c>IntrusionLogic</c></b>：
    /// 后者是<b>劫掠者的决策层</b>（"我现在该撬、该拆、还是该砸"）——那是 AI 的心思，玩家不该从它那里取数。
    /// 两边都从**同一个通用机制层**取数，才是对称性的正确形状（impl-raider-ai 的分层批评是对的）。
    /// </para>
    /// <b>硬约束</b>：它<b>必须 &lt; 丧尸嗅觉 70</b>，否则"静默"二字不成立——那就只是个"慢速版破坏"
    /// （又慢又招人，没有任何人会选它），整条机制失去存在意义。
    /// </summary>
    public static double SilentDismantleNoise => SilentDismantleLogic.NoiseRadius;

    /// <summary>静默拆一段围栏要多久（基础 45s，每升一档 +20s）。<b>纯转发</b> <see cref="SilentDismantleLogic.SecondsFor"/>。</summary>
    public static double DismantleSecondsFor(StructureTier tier)
        => SilentDismantleLogic.SecondsFor(tier, SilentDismantleParams.Default);

    // ---------------- 门 ----------------

    /// <summary>
    /// 对着一扇门，这个人此刻能干什么。<paramref name="lockpickCount"/> = 身上的铁丝数
    /// （<see cref="DoorLogic.LockpickMaterialKey"/>）。
    /// <para>
    /// 不能操作门的（丧尸 / 狗）返回**空列表** —— 丧尸只会砸，而那是 AI 的破防路径（<c>BreachController</c>），
    /// 不走玩家菜单；狗没有手。
    /// </para>
    /// </summary>
    public static IReadOnlyList<SiteActionOption> ForDoor(
        DoorState state, LockTier lockTier, Faction faction, bool isAnimal, int lockpickCount)
    {
        var opts = new List<SiteActionOption>();
        if (!DoorLogic.CanOperateDoors(faction, isAnimal))
        {
            return opts; // 丧尸/狗：菜单是空的
        }

        // 推开（含抬起自家门闩）
        if (DoorLogic.CanOpen(state, faction, isAnimal))
        {
            string label = state == DoorState.Barred ? "抬闩开门" : "推开";
            opts.Add(new SiteActionOption(SiteAction.OpenDoor, label,
                $"一下就开，但有动静（{NoiseOf(SiteAction.OpenDoor):0} 半径，附近的东西听得见）", true, ""));
        }

        // 关上（能闩的顺手闩上）
        if (DoorLogic.CanClose(state, faction, isAnimal))
        {
            opts.Add(new SiteActionOption(SiteAction.CloseDoor, "关上",
                "把它关上（能闩的门顺手闩好）", true, ""));
        }

        // 撬锁：**只有锁着的门**（闩不是锁——横木在门内侧，撬锁的手艺没有用武之地）
        if (state == DoorState.Locked)
        {
            bool hasWire = lockpickCount > 0;
            double sec = PickSecondsFor(lockTier);
            double pct = PickChanceFor(lockTier) * 100;
            opts.Add(new SiteActionOption(SiteAction.PickLock, "撬锁",
                $"安静（{NoiseOf(SiteAction.PickLock):0} 半径，什么都惊动不了）· 每次 {sec:0} 秒 · 成功率 {pct:0}% · 失败断一根铁丝（还有 {lockpickCount} 根）",
                hasWire,
                hasWire ? "" : "没有铁丝——撬不了，只能砸（很响）"));
        }

        // 破坏：挡路的才有得砸（CanBash 恒等于 Blocks，这条铁律不能在菜单层被绕过）——
        // **但只在"你打不开它"的时候才提供**。
        //
        // ⚠️ 这一条是设计，不是省事：**砸一扇你本来就推得开的门，是被严格支配的选项**
        //    （开门更快、更安静 100 < 180、还不毁掉门）——没有任何人会选它。把它摆在菜单上，
        //    只会让每一次开门都变成一次无意义的二选一。**破坏是暴力解，不是并列项**：
        //    门锁着 / 闩着（你开不了）的时候，它才是一条真正的出路。
        //
        // 不需要任何工具 ⇒ 玩家**绝不会被一扇门永久卡死**（没铁丝也总能砸开，只是很响）。
        if (DoorLogic.CanBash(state) && !DoorLogic.CanOpen(state, faction, isAnimal))
        {
            opts.Add(new SiteActionOption(SiteAction.Bash, "破坏",
                $"快，但**很响**（{NoiseOf(SiteAction.Bash):0} 半径 —— 半条街都听得见，他们会围过来）", true, ""));
        }

        return opts;
    }

    // ---------------- 围栏（静默拆除 / 破坏） ----------------

    /// <summary>
    /// 对着一段围栏，这个人此刻能干什么：<b>静默拆除</b>（安静、慢）/ <b>破坏</b>（快、很响）。
    /// <para>
    /// ⚠️ 静默拆除的**耗时/判定**等 <c>impl-raider-ai</c> 的底层通用逻辑（玩家与 AI 共用，见 <see cref="SilentDismantleNoise"/>）。
    /// 本函数只出**菜单内容**；真正"拆得动吗、拆多久"由那套逻辑裁定，调用方接线时传入。
    /// </para>
    /// </summary>
    /// <param name="kind">结构种类。<b>只有围栏拆得动</b>（<see cref="SilentDismantleLogic.CanDismantle"/> 对门恒 false：
    /// 门已经有撬(30)/砸(180)两条完整的路，再给它加一条"静默拆"是**重复机制**）。</param>
    /// <param name="tier">这段围栏的档次（决定静默拆的耗时：基础 45s，每升一档 +20s）。</param>
    /// <param name="destroyed">已经拆没了的不用再拆。</param>
    public static IReadOnlyList<SiteActionOption> ForFence(
        Faction faction, bool isAnimal, CampStructureKind kind, StructureTier tier, bool destroyed = false)
    {
        var opts = new List<SiteActionOption>();
        if (!DoorLogic.CanOperateDoors(faction, isAnimal))
        {
            return opts; // 丧尸/狗：不走玩家菜单（丧尸砸围栏是 BreachController 的事）
        }

        // 能不能静默拆，由**通用机制层**裁定（门恒 false）——不在菜单层自己判"这是不是围栏"。
        if (SilentDismantleLogic.CanDismantle(kind, destroyed))
        {
            double sec = DismantleSecondsFor(tier);
            // ⚠️ **不显示成功率** —— 静默拆除**不掷骰**（没有 IRandomSource）：花够时间就一定拆开。
            // 它的取舍是「**时间 + 被撞见的风险**」，不是运气。写"约 45 秒"比写"70% 成功率"诚实得多，
            // 也正好和撬锁（会失败、会断铁丝）形成对照——两条安静路子，一条赌运气，一条赌时间。
            opts.Add(new SiteActionOption(SiteAction.SilentDismantle, "静默拆除",
                $"安静（{NoiseOf(SiteAction.SilentDismantle):0} 半径，什么都惊动不了）· 需要 {sec:0} 秒 · 侧面开个洞，绕开正门",
                true, ""));
        }

        opts.Add(new SiteActionOption(SiteAction.Bash, "破坏",
            $"快，但**很响**（{NoiseOf(SiteAction.Bash):0} 半径 —— 整个据点会瞬间警觉，然后他们包抄你）", true, ""));

        return opts;
    }

    // ---------------- 弹不弹菜单 ----------------

    /// <summary>
    /// 要不要弹菜单：<b>≥2 个「可用」动作才弹</b>。
    /// <para>
    /// 一扇没锁的门只有"推开"这一个动作（用户拍板「开门只有一种动作」）——给它弹一个只有一项的菜单，
    /// 是<b>拿仪式感惩罚玩家</b>。此时右键 = 直接干那一件事，保持既有的一击直达手感。
    /// </para>
    /// <para>
    /// <b>按「可用」数算，不按「列出」数</b>：一扇锁着的门、而玩家没带铁丝 ⇒ 撬锁是灰的、只有"破坏"能按
    /// ⇒ <b>仍然弹菜单</b>（因为玩家需要看见那条灰掉的"撬锁——没有铁丝"，否则他学不到下次该带铁丝）。
    /// 这是唯一一处"1 个可用动作也弹菜单"的情形，故判据是 <b>列出 ≥2 且 可用 ≥1</b>。
    /// </para>
    /// </summary>
    public static bool NeedsMenu(IReadOnlyList<SiteActionOption> options)
        => options.Count >= 2 && options.Any(o => o.Enabled);

    /// <summary>菜单不弹时，右键该直接干的那一件事（<c>null</c> = 什么都干不了）。</summary>
    public static SiteAction? SoleAction(IReadOnlyList<SiteActionOption> options)
    {
        List<SiteActionOption> usable = options.Where(o => o.Enabled).ToList();
        return usable.Count == 1 ? usable[0].Action : null;
    }
}
