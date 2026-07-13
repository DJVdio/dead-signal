using System;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 DoorLogic.cs / NoiseLogic.cs / BreachLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（找门/找围栏格/施伤/摧毁/寻路）归 Godot 实时层（Raider.cs / CampMain.cs），本文件只出**纯判定函数**。

/// <summary>
/// 劫掠者的<b>入侵手段</b>。
/// <para>
/// ⚠️ <b>这是一条反退化设计</b>（用户口径：「劫掠者会花一段时间撬锁，或者轻声拆除围栏进入，
/// 以避免玩家不派任何岗哨只等着敌人砸门」）。
/// </para>
/// <para>
/// <b>没有安静入侵时，游戏是坏的</b>：劫掠者只会砸门（噪音 180），一砸就把你吵醒 ⇒
/// <b>玩家的最优解变成"根本不派守夜人"</b>（反正敌人来了会自己敲锣打鼓通知你）⇒ 整套岗哨/夜防系统全白搭。
/// </para>
/// <para>
/// 有了安静入侵之后：<b>你不派人看着，他们就无声无息地进来了</b>。守夜从"可选的保险"变成<b>"不派就死"</b>。
/// </para>
/// </summary>
public enum IntrusionMethod
{
    /// <summary>没有可入侵的目标（门和围栏都不挡路了 —— 大概已经进来了）→ 交回常规追击。</summary>
    None,

    /// <summary><b>撬锁</b>（安静 30、快、但要工具、还可能撬断）。复用 <see cref="DoorLogic.TryPick"/>。</summary>
    PickLock,

    /// <summary><b>轻声拆除围栏</b>（安静 30、<b>很慢</b>、不需要工具）。慢就是它的代价。</summary>
    QuietDismantle,

    /// <summary><b>砸</b>（噪音 180，快，把全场都招来）。只有已经被发现 / 撬不开 / 时间来不及了才砸。</summary>
    Bash,
}

/// <summary>入侵决策的输入快照。</summary>
public readonly record struct IntrusionSituation
{
    /// <summary>已经被发现了（交火中 / 守夜人已拉警报）。<b>都打起来了还安静个什么劲</b> → 砸。</summary>
    public bool Detected { get; init; }
    /// <summary>身上有撬锁工具（铁丝）。没有就撬不了锁。</summary>
    public bool HasLockpicks { get; init; }
    /// <summary>附近有锁着的门。</summary>
    public bool LockedDoorNearby { get; init; }
    /// <summary>那扇门的锁档（决定撬锁耗时与成功率，走 <see cref="DoorLogic"/>）。</summary>
    public LockTier DoorLock { get; init; }
    /// <summary>附近有围栏格可拆。</summary>
    public bool FenceNearby { get; init; }
    /// <summary>那格围栏的档次（决定拆除耗时）。</summary>
    public StructureTier FenceTier { get; init; }
    /// <summary>天亮前还剩多少秒（<b>时间紧迫就没法慢慢来</b>）。</summary>
    public double SecondsUntilDawn { get; init; }
    /// <summary>已经撬断了几次工具/失败几次。撬不开就只能抡锤子了。</summary>
    public int FailedPickAttempts { get; init; }
}

/// <summary>入侵参数（拟定待调，数据驱动：随 <c>raider_tactics.json</c> 一起配）。</summary>
public sealed record IntrusionParams
{
    /// <summary>撬锁失败几次就放弃、改砸（撬锁每次失败都断一根铁丝）。</summary>
    public int MaxPickAttempts { get; init; } = 2;

    /// <summary>
    /// 静默拆除的数值 —— <b>直接持有通用参数，AI 不另存一份</b>。
    /// <b>玩家和劫掠者拆同一格围栏，花的是同一个 45 秒、发的是同一个 35 噪音</b>
    /// （<see cref="SilentDismantleLogic"/>；对称性靠签名保证，那些函数根本不接受"谁在拆"）。
    /// </summary>
    public SilentDismantleParams Dismantle { get; init; } = SilentDismantleParams.Default;

    /// <summary>
    /// 时间余量系数：剩余夜晚时间 &lt; 所需时间 × 本系数 ⇒ <b>急了，改砸</b>。
    /// 没这条，天快亮了他还蹲在那儿慢慢拆。
    /// </summary>
    public double TimePressureMargin { get; init; } = 1.5;

    public static IntrusionParams Default { get; } = new();
}

/// <summary>
/// 劫掠者<b>安静入侵</b>的纯逻辑：撬锁 / 轻声拆围栏 / 什么时候改砸 / 守夜人有几次机会发现。
/// </summary>
public static class IntrusionLogic
{
    /// <summary>
    /// <b>安静入侵的噪音半径</b> = 撬锁的那个 30（<see cref="NoiseLogic.LockpickNoiseRadius"/>）。
    /// <para>
    /// <b>必须低于丧尸嗅觉 70、甚至低于走路 40</b> —— 否则"安静"入侵会自己把东西招来，
    /// 那它就只是"慢速版砸门"，又慢又要工具还照样惊动全场，<b>没有任何劫掠者会选它</b>，
    /// 这条反退化设计就整个作废了。
    /// </para>
    /// <para>对照另一端：<b>砸 180</b>（<see cref="NoiseLogic.BreachNoiseRadius"/>），快，但把全场都招来。</para>
    /// </summary>
    public const double QuietNoiseRadius = SilentDismantleLogic.NoiseRadius;

    /// <summary>
    /// 该用哪种手段进来。<b>默认走安静路线；只有"已经被发现 / 撬不开 / 来不及了"才砸。</b>
    /// <para>
    /// 优先撬锁（快，只给守夜人 1 次机会），撬不了才拆围栏（慢，给守夜人 3 次机会）——
    /// 但撬锁<b>要工具、会失败</b>，拆围栏<b>不要工具</b>。这就是两条路的取舍。
    /// </para>
    /// </summary>
    public static IntrusionMethod Choose(in IntrusionSituation s, IntrusionParams p)
    {
        bool anyTarget = s.LockedDoorNearby || s.FenceNearby;
        if (!anyTarget)
        {
            return IntrusionMethod.None; // 没门没围栏挡路了 → 交回常规追击
        }

        if (s.Detected)
        {
            return IntrusionMethod.Bash; // 都打起来了，还安静个什么劲
        }

        if (s.FailedPickAttempts < p.MaxPickAttempts
            && s.LockedDoorNearby
            && s.HasLockpicks
            && HasTimeFor(DoorLogic.PickSeconds(s.DoorLock), s, p))
        {
            return IntrusionMethod.PickLock;
        }

        if (s.FenceNearby && HasTimeFor(DismantleSeconds(s.FenceTier, p), s, p))
        {
            return IntrusionMethod.QuietDismantle;
        }

        return IntrusionMethod.Bash; // 有东西挡路，但没工具/撬断了/来不及了 → 抡锤子（并把全场招来）
    }

    /// <summary>天亮前的时间够不够慢慢来。</summary>
    public static bool HasTimeFor(double needSeconds, in IntrusionSituation s, IntrusionParams p)
        => s.SecondsUntilDawn >= needSeconds * p.TimePressureMargin;

    /// <summary>
    /// 轻声拆一格围栏要多久。<b>转发给通用 API</b>（<see cref="SilentDismantleLogic.SecondsFor"/>）——
    /// <b>AI 绝不在这里另算一套</b>，否则玩家和劫掠者的行为会悄无声息地不对称。
    /// </summary>
    public static double DismantleSeconds(StructureTier tier, IntrusionParams p)
        => SilentDismantleLogic.SecondsFor(tier, p.Dismantle);

    /// <summary>拆除完成时的伤害（整格满血 = 一个真的洞）。转发给通用 API。</summary>
    public static double DismantleDamage(StructureTier tier)
        => SilentDismantleLogic.DamageFor(tier);

    /// <summary>安静作业给守夜人几次发现机会。转发给通用 API。</summary>
    public static int DetectionRolls(double seconds, IntrusionParams p)
        => SilentDismantleLogic.DetectionRolls(seconds, p.Dismantle);

    /// <summary>
    /// <paramref name="rolls"/> 次独立机会下的<b>累计发现率</b>：1 − (1 − 单次)^次数。
    /// <para>
    /// ⚠️ <b>单次发现率为 0（＝营地没派任何守夜人）⇒ 累计恒为 0</b>：
    /// 拆到天亮也没人看见。<b>这正是用户要的"不派岗哨就死"</b> —— 这条不是巧合，是这整套东西的目的。
    /// </para>
    /// </summary>
    public static double CumulativeDetection(double singleRollChance, int rolls)
        => SilentDismantleLogic.CumulativeDetection(singleRollChance, rolls);
}
