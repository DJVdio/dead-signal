using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 GameOverCondition.cs / RaidWave.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 承载「尸潮时限」规则：游戏从第 1 天起隐性倒计时，到期尸潮抵达 → 无限波次围攻直至全灭。
// 计时不依赖玩家是否发现——「城市之巅瞭望观景台」望远镜交互只置旗标 SightedFlag（解锁 UI 知情/显示），
// 不影响时限推进。到期后是**无生还的终局**（有意为之的黑暗设定，不当平衡问题软化）。
// 只出纯判定/调度，弹窗/生成/施伤归 Godot 运行时层（CampMain）。

/// <summary>尸潮相位：未发现(隐性计时中) / 已望见(解锁显示，仍在计时) / 已抵达(终局无限围攻)。</summary>
public enum HordePhase
{
    Hidden,
    Sighted,
    Arrived,
}

/// <summary>一次围攻波次调度决策（纯数据）：是否该投下一波、投多少。</summary>
public readonly struct SiegeWave
{
    public bool ShouldSpawn { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// 尸潮时限与到期终局围攻的纯逻辑（无 Godot 依赖，可 Link 进单测）。
/// 数值全部"拟定待调"占位——用 Sim/试玩校准，规则形态才需用户拍板。
/// </summary>
public static class HordeTimeline
{
    // ---------------- 时限 ----------------

    /// <summary>时限天数（拟定待调）：day &gt;= 此值 → 尸潮抵达终局围攻。</summary>
    public const int DeadlineDay = 40;

    /// <summary>望见尸潮的剧情旗标键（瞭望台望远镜交互置位；ui-countdown/loot-story 等只消费）。</summary>
    public const string SightedFlag = "horde_sighted";

    /// <summary>尸潮已抵达（终局围攻已启动）的剧情旗标键（CampMain 触发终局时置位）。</summary>
    public const string ArrivedFlag = "horde_arrived";

    /// <summary>
    /// 终局冻结旗标键：主线推进到终局抉择点后由结局流程置位 → 时限被结局流程接管，尸潮围攻不再触发
    /// （<see cref="ShouldTriggerSiege"/> 冻结分支）。置位方留待主线/结局系统（本批未实装），此处只做门控钩子。
    /// </summary>
    public const string EndgameFreezeFlag = "endgame_freeze";

    /// <summary>
    /// 由当前天数与「是否已望见」求尸潮相位。
    /// 到期(day&gt;=DeadlineDay)一律 Arrived——不依赖发现；未到期时发现只区分 Sighted/Hidden。
    /// </summary>
    public static HordePhase Evaluate(int day, bool sighted)
    {
        if (day >= DeadlineDay)
            return HordePhase.Arrived;
        return sighted ? HordePhase.Sighted : HordePhase.Hidden;
    }

    /// <summary>距尸潮抵达还剩几天（到期及以后为 0）。第 1 天为 DeadlineDay-1，第 DeadlineDay 天为 0。</summary>
    public static int DaysRemaining(int day)
    {
        return Math.Max(0, DeadlineDay - day);
    }

    /// <summary>
    /// 到期尸潮围攻是否应触发（运行时触发判据）：到期(<see cref="Evaluate"/>==Arrived) 且未被终局流程冻结。
    /// 终局冻结优先——主线推进到终局抉择点后 <paramref name="endgameFrozen"/> 为真，即便已过时限也不再触发开放世界围攻
    /// （结局由主线流程接管）。<see cref="Evaluate"/> 签名不变，本方法为附加门控。
    /// </summary>
    public static bool ShouldTriggerSiege(int day, bool sighted, bool endgameFrozen)
    {
        if (endgameFrozen)
            return false;
        return Evaluate(day, sighted) == HordePhase.Arrived;
    }

    // ---------------- 到期终局：无限围攻波次调度 ----------------
    //
    // 「无限量」的运行时安全实现：每波是有限批（可渲染，不一次性生成上百万实例把 Godot 撑爆），
    // 但波次**永不停轮**且规模逐波递增——残敌被压到阈值以下、或超过最长间隔即补下一波。
    // 无「守住」出口：唯一终止是 GameOverCondition 全灭。CampMain 逐帧喂当前残敌数/距上波时间，
    // 本函数只出「该不该来、来多少」。

    /// <summary>首波规模（拟定待调，比常规袭营 RaidWave.Base 大，压迫感）。</summary>
    public const float WaveBase = 8f;

    /// <summary>每波规模递增（拟定待调）。</summary>
    public const float WaveGrowth = 2f;

    /// <summary>波次随在营人数微增（拟定待调；营地越大越招祸，与 RaidWave 同调性）。</summary>
    public const float WaveCampFactor = 0.5f;

    /// <summary>单波渲染上限（拟定待调，防 Godot 实例爆炸；封顶不封"无限轮次"）。</summary>
    public const int WaveCap = 60;

    /// <summary>强制下一波的最长间隔秒（拟定待调）：即便残敌仍多，超此即补投，不给喘息。</summary>
    public const double WaveInterval = 12.0;

    /// <summary>残敌降到此数(含)及以下即补下一波（拟定待调，不必全清就压上来）。</summary>
    public const int WaveClearThreshold = 4;

    /// <summary>
    /// 场上丧尸并发硬上限（拟定待调）：围攻波次投放前先按此把本波规模 clamp 到 <c>上限−残敌</c>，
    /// 达上限则本波不投（等玩家清出空间再压上来）。**不软化"无生还终局"语义**——波次仍不停轮询，
    /// 只是清不动时不让 Godot 实体真无界堆积（防 day40 数百节点逐帧 _Process/物理体崩帧，同时封住敌方感知 raycast 的分母）。
    /// </summary>
    public const int MaxConcurrentSiege = 80;

    /// <summary>
    /// 下一波是否该来 + 规模。
    /// </summary>
    /// <param name="waveIndex">已投放波次序号（0=首波，立即投）。</param>
    /// <param name="zombiesAlive">场上存活丧尸数。</param>
    /// <param name="secondsSinceLastWave">距上一波投放已过秒数。</param>
    /// <param name="campSize">在营幸存者数。</param>
    /// <param name="maxConcurrent">场上并发硬上限：本波规模 clamp 到 <c>上限−残敌</c>，凑不出正数即本波不投
    /// （<see cref="ShouldSpawn"/>=false，波次照常下次再判）。默认 <see cref="int.MaxValue"/>=不限（保持既有调用行为）。</param>
    public static SiegeWave NextWave(int waveIndex, int zombiesAlive, double secondsSinceLastWave, int campSize, int maxConcurrent = int.MaxValue)
    {
        bool due = waveIndex <= 0
            || zombiesAlive <= WaveClearThreshold
            || secondsSinceLastWave >= WaveInterval;

        if (!due)
            return new SiegeWave { ShouldSpawn = false, Count = 0 };

        int count = WaveSize(waveIndex, campSize);
        // 在场并发上限：达上限则本波缩减/跳过，封 day40 无界实体堆积（不改"波次不停轮"语义）。
        int headroom = maxConcurrent - Math.Max(0, zombiesAlive);
        if (count > headroom)
            count = Math.Max(0, headroom);
        if (count <= 0)
            return new SiegeWave { ShouldSpawn = false, Count = 0 };

        return new SiegeWave { ShouldSpawn = true, Count = count };
    }

    /// <summary>第 waveIndex 波的丧尸数：基数 + 逐波递增 + 随营地微增，封顶 WaveCap、保底 1。</summary>
    public static int WaveSize(int waveIndex, int campSize)
    {
        waveIndex = Math.Max(0, waveIndex);
        campSize = Math.Max(0, campSize);

        float raw = WaveBase + waveIndex * WaveGrowth + campSize * WaveCampFactor;
        int count = (int)Math.Ceiling(raw);
        return Math.Clamp(count, 1, WaveCap);
    }
}
