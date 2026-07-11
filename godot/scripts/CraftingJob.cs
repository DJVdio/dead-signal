using System;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 CraftingLogic.cs / CraftingService.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 工时制在制品模型：夜间生产不再"点击即得"，而是一件件推进工时。
//   开工时材料已由 CraftingService.StartJob 即扣（锁定，防重复下单）；本模型只承载**工时进度**——
//   仅当"人在工作台 且 处可生产相位"（由调用方合成为一个 bool）时 Advance 推进；
//   中断（工人被袭营拉走 / 相位切到非生产）= 传 canWork=false → 停推进、不丢已有进度；
//   进度满（IsComplete）后由调用方走 CraftingService.CompleteJob 出产物入库。
// 工时单位=游戏分钟（对齐 daynight 时标）；每工作台单在制任务（多任务队列列 TODO 待确认）。

/// <summary>一件在制品的工时进度载体（可变：进度逐帧累积）。材料已在开工时扣除，本类不碰库存。</summary>
public sealed class CraftingJob
{
    /// <summary>配方 id（对齐 <see cref="RecipeData.Id"/>；完工时调用方据此查配方出产物）。</summary>
    public string RecipeId { get; }

    /// <summary>批量倍数（≥1；产物与总工时按它放大，开工时已定）。</summary>
    public int Times { get; }

    /// <summary>本任务总工时（游戏分钟，= 配方 WorkMinutes × Times，拟定待调）。</summary>
    public int TotalWorkMinutes { get; }

    /// <summary>已投入工时（游戏分钟，封顶 <see cref="TotalWorkMinutes"/>）。</summary>
    public int ElapsedWorkMinutes { get; private set; }

    public CraftingJob(string recipeId, int totalWorkMinutes, int times = 1)
    {
        if (string.IsNullOrEmpty(recipeId)) throw new ArgumentException("配方 id 不可空", nameof(recipeId));
        RecipeId = recipeId;
        Times = times < 1 ? 1 : times;
        TotalWorkMinutes = totalWorkMinutes < 0 ? 0 : totalWorkMinutes;
        ElapsedWorkMinutes = 0;
    }

    /// <summary>已完工（累计工时 ≥ 总工时；零工时任务视为即完）。</summary>
    public bool IsComplete => ElapsedWorkMinutes >= TotalWorkMinutes;

    /// <summary>剩余工时（游戏分钟），下限 0。</summary>
    public int RemainingWorkMinutes => Math.Max(0, TotalWorkMinutes - ElapsedWorkMinutes);

    /// <summary>进度 [0,1]（总工时为 0 时恒 1）。</summary>
    public float Progress => TotalWorkMinutes <= 0
        ? 1f
        : Math.Clamp((float)ElapsedWorkMinutes / TotalWorkMinutes, 0f, 1f);

    /// <summary>
    /// 推进工时：仅当 <paramref name="canWork"/>（= 人在工作台 且 处可生产相位，由调用方合成）为真、
    /// <paramref name="minutes"/>&gt;0、且尚未完工时才累加，封顶总工时。返回本次实际推进的分钟数（暂停/已完/无效均为 0）。
    /// </summary>
    /// <param name="minutes">本次流逝的游戏分钟。</param>
    /// <param name="canWork">调用方合成：工人在该工作台 且 当前相位允许生产（夜班生产相位）。为 false=中断，不丢进度。</param>
    public int Advance(int minutes, bool canWork)
    {
        if (!canWork || minutes <= 0 || IsComplete) return 0;
        int before = ElapsedWorkMinutes;
        ElapsedWorkMinutes = Math.Min(TotalWorkMinutes, ElapsedWorkMinutes + minutes);
        return ElapsedWorkMinutes - before;
    }
}
