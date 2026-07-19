using DeadSignal.Combat;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 NightRaidLogic.cs / ShiftSchedule.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// 承载「夜袭事件调度」规则：入夜先掷人类夜袭 12.5%，命中则在夜晚前 3 游戏小时均匀选触发点；
// 仅人袭未命中才掷丧尸夜袭 22.5%，命中则在前 5 游戏小时均匀选触发点；二者互斥且到点仅触发一次。
// ScriptNight / HordeNight 保留枚举槽位留给后续 CampMain 集成。

public enum NightEventKind
{
    None,
    HumanRaid,
    ZombieRaid,
    ScriptNight,
    HordeNight,
}

/// <summary>
/// 夜袭事件调度纯逻辑（无 Godot 依赖，可 Link 进单测）。
/// 入夜时 <see cref="Roll"/> 生成一次调度，之后按 <see cref="ShouldTrigger"/> 判定触发时机。
/// <see cref="Fired"/> 标记已触发，读档恢复时直接构造本结构（不重掷）。
/// </summary>
public readonly struct NightEventSchedule
{
    /// <summary>人类夜袭触发概率 12.5%。</summary>
    public const double HumanRaidChance = 0.125;

    /// <summary>丧尸夜袭触发概率 22.5%（仅人袭未命中才掷）。</summary>
    public const double ZombieRaidChance = 0.225;

    /// <summary>人类夜袭触发窗口：夜晚前 3 游戏小时。拟定待调。</summary>
    public const double HumanRaidWindowHours = 3.0;

    /// <summary>丧尸夜袭触发窗口：夜晚前 5 游戏小时。拟定待调。</summary>
    public const double ZombieRaidWindowHours = 5.0;

    public NightEventKind EventKind { get; }
    public double TriggerGameHour { get; }
    public bool Fired { get; }

    /// <summary>无事件（默认）。</summary>
    public static NightEventSchedule None => new(NightEventKind.None, 0.0, false);

    private NightEventSchedule(NightEventKind kind, double triggerHour, bool fired)
    {
        EventKind = kind;
        TriggerGameHour = triggerHour;
        Fired = fired;
    }

    /// <summary>
    /// 入夜时执行一次掷点：人袭 12.5%（前 3h）→ 未命中则丧尸 22.5%（前 5h）→ 均未命中则 None。
    /// </summary>
    public static NightEventSchedule Roll(IRandomSource rng)
    {
        double roll1 = rng.Range(0.0, 1.0);
        if (roll1 < HumanRaidChance)
        {
            double hour = rng.Range(0.0, HumanRaidWindowHours);
            return new NightEventSchedule(NightEventKind.HumanRaid, hour, false);
        }

        double roll2 = rng.Range(0.0, 1.0);
        if (roll2 < ZombieRaidChance)
        {
            double hour = rng.Range(0.0, ZombieRaidWindowHours);
            return new NightEventSchedule(NightEventKind.ZombieRaid, hour, false);
        }

        return None;
    }

    /// <summary>
    /// 当前游戏小时（从入夜起算 ≥ <see cref="TriggerGameHour"/>）且未触发时，返回 true。
    /// None 恒返回 false。
    /// </summary>
    public bool ShouldTrigger(double currentNightGameHour)
    {
        if (EventKind == NightEventKind.None || Fired)
            return false;
        return currentNightGameHour >= TriggerGameHour;
    }

    /// <summary>返回标记已触发的副本（读档恢复时已触发的事件保持 fired=true）。</summary>
    public NightEventSchedule WithFired()
        => new NightEventSchedule(EventKind, TriggerGameHour, true);

    /// <summary>从存档数据恢复（不执行任何掷点）。</summary>
    public static NightEventSchedule Restore(NightEventKind kind, double triggerGameHour, bool fired)
        => new NightEventSchedule(kind, triggerGameHour, fired);
}
