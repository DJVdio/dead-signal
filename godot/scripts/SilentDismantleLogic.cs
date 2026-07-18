using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
//（与 DoorLogic.cs / NoiseLogic.cs / IntrusionLogic.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（移除碰撞 / 挖导航洞 / 重烘焙 / 表现）归 Godot 实时层（CampMain），本文件只出**判定 + 数值**。

/// <summary>静默拆除的参数（拟定待调）。</summary>
public sealed record SilentDismantleParams
{
    /// <summary>
    /// 静默拆一格<b>基础围栏</b>要多久（秒）。
    /// 具体时长以 Wiki 配置为准；<b>慢就是静默的代价</b>，也是留给对面发现你的窗口。
    /// </summary>
    public double SecondsBase { get; init; } = 45.0;

    /// <summary>围栏每升一档，静默拆除多花的秒数（更结实 = 更难无声拆掉）。</summary>
    public double SecondsPerTier { get; init; } = 20.0;

    /// <summary>
    /// 作业期间，<b>每隔多少秒给对面一次发现机会</b>（守夜人 / 敌营哨兵，走 <see cref="NightWatchContest"/>）。
    /// <b>这是核心平衡旋钮</b>：慢到让称职的看守<b>有机会</b>发现，但不至于"只要有人看着就必然被抓"。
    /// </summary>
    public double DetectionRollInterval { get; init; } = 15.0;

    public static SilentDismantleParams Default { get; } = new();
}

/// <summary>
/// <b>静默拆除</b>（围栏一格）的通用纯逻辑 —— <b>玩家和 AI 用的是同一套</b>。
/// <para>
/// 用户口径两条，一体两面：
/// <list type="bullet">
///   <item>「劫掠者会……<b>轻声拆除围栏</b>进入，以避免玩家不派任何岗哨只等着敌人砸门」</item>
///   <item>「<b>同样的，玩家也可以</b>控制角色……点击围栏时<b>静默拆除</b>/破坏」</item>
/// </list>
/// </para>
/// <para>
/// ⚠️ <b>对称性是靠签名保证的，不是靠自觉</b>：本类的函数<b>根本不接受"谁在拆"这个参数</b>。
    /// 玩家潜入敌营开侧洞，和劫掠者夜里摸进你家，走同一套 Wiki 配置。
/// 任何一方开后门（AI 拆得特别快 / 玩家拆得特别安静）都是设计失败。
/// </para>
/// <para>
/// <b>与 <c>SalvageLogic</c> 严格分家</b>：那个是"拆自己营地的东西回收 50% 材料"（<b>建造经济</b>）；
/// 这个是"把一格围栏从场上抹掉"（<b>潜入手段</b>）。<b>静默拆除不返任何材料</b> ——
/// 你是去潜入的，不是去拆迁的（见 <see cref="YieldsMaterials"/>）。
/// </para>
/// </summary>
public static class SilentDismantleLogic
{
    /// <summary>
    /// 静默拆除噪音半径；具体值以 Wiki 配置为准。
    /// <para>
    /// 必须低于丧尸感知门槛，否则"静默"拆除会自己把东西招来，那它就只是"又慢又招人的破坏"——
    /// <b>没有任何人（玩家或 AI）会选它</b>，这条机制就整个作废了。
    /// </para>
    /// </summary>
    public const double NoiseRadius = NoiseLogic.SilentDismantleNoiseRadius;

    /// <summary>
    /// <b>静默拆除不返材料</b>。想回收材料请走 <c>SalvageLogic</c>（拆自己营地的东西，返还规则以 Wiki 配置为准）——
    /// 那是建造经济，不是潜入。混在一起会变成"拆敌营围栏还能顺走木料"的荒诞。
    /// </summary>
    public const bool YieldsMaterials = false;

    /// <summary>
    /// 什么能静默拆：<b>只有围栏</b>，且还没被拆没。
    /// <para>
    /// <b>门不能</b>——门有它自己的撬锁/破坏路径（见 <see cref="DoorLogic"/>）。
    /// 给门再开一条"静默拆"是重复机制。
    /// </para>
    /// </summary>
    public static bool CanDismantle(CampStructureKind kind, bool destroyed)
        => !destroyed && kind == CampStructureKind.Fence;

    /// <summary>静默拆一格围栏要多久（秒）。<b>不接受"谁在拆"——玩家和 AI 同一个数。</b></summary>
    public static double SecondsFor(StructureTier tier, SilentDismantleParams p)
        => p.SecondsBase + p.SecondsPerTier * TierStep(tier);

    /// <summary>
    /// 拆除完成时对那一格施加的伤害 = <b>整格的满血</b>。
    /// <b>拆掉一格就是一个货真价实的洞</b>（复用围栏分格 + 既有摧毁链路，不另造"洞"的概念）。
    /// <para>⚠️ 洞是<b>永久</b>的：要补得走<b>升级围栏</b>那条路（用户拍板"墙不能建，只能升级开局自带的围栏"）。</para>
    /// </summary>
    public static double DamageFor(StructureTier tier)
        => CampStructureTable.MaxHp(tier);

    /// <summary>
    /// 一次耗时 <paramref name="seconds"/> 的静默作业，<b>给看守方几次发现机会</b>。至少 1 次
    /// （再快的活也得让人有一次撞见你的可能）。<b>对玩家和 AI 同样成立</b>：
    /// 你潜入敌营时被哨兵撞见，用的是同一个函数。
    /// </summary>
    public static int DetectionRolls(double seconds, SilentDismantleParams p)
    {
        double interval = Math.Max(0.1, p.DetectionRollInterval);
        return Math.Max(1, (int)(Math.Max(0, seconds) / interval));
    }

    /// <summary>
    /// <paramref name="rolls"/> 次独立机会下的<b>累计发现率</b>：1 − (1 − 单次)^次数。
    /// <para>
    /// ⚠️ <b>单次发现率为 0（没人看着）⇒ 累计恒为 0</b>。这条<b>双向</b>成立：
    /// 你营地没派守夜人 ⇒ 劫掠者随便拆；敌营没哨兵 ⇒ 你随便拆。<b>这就是对称。</b>
    /// </para>
    /// </summary>
    public static double CumulativeDetection(double singleRollChance, int rolls)
    {
        double p = Math.Clamp(singleRollChance, 0, 1);
        return 1.0 - Math.Pow(1.0 - p, Math.Max(0, rolls));
    }

    /// <summary>围栏档次 → 升级级数（基础=0）。</summary>
    private static int TierStep(StructureTier tier) => tier switch
    {
        StructureTier.FenceBasic      => 0,
        StructureTier.FenceReinforced => 1,
        StructureTier.FenceSheetMetal => 2,
        StructureTier.FenceFullMetal  => 3,
        _ => 0,
    };
}
