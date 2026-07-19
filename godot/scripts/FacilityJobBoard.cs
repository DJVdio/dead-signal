using System;
using System.Collections.Generic;
using System.Linq;

namespace DeadSignal.Godot;

// 纯 C#：不引 Godot 类型。空间层只负责回答“工人是否已到对应设施前”。

/// <summary>生产设施稳定槽键。实例 id 必须来自存档稳定名，不能用场景对象引用。</summary>
public static class FacilityJobKeys
{
    public const string MainWorkbench = "workbench:main";
    public const string MainWeaponBench = "weaponbench:main";
    public const string MainModBench = "modbench:main";
    public const string MainCookStation = "cookstation:main";
    public const string MainButcherStation = "butcher:main";

    public static string For(string facilityKind, string stableInstanceId)
    {
        if (string.IsNullOrWhiteSpace(facilityKind))
            throw new ArgumentException("设施类别不可空", nameof(facilityKind));
        if (string.IsNullOrWhiteSpace(stableInstanceId))
            throw new ArgumentException("设施稳定名不可空", nameof(stableInstanceId));
        return $"{facilityKind.Trim()}:{stableInstanceId.Trim()}";
    }

    /// <summary>
    /// 旧版全营单任务没有保存设施引用，只能按任务 id 做确定性归槽。
    /// 普通配方归旧工作台；新存档下单时由调用方使用真实设施稳定名，不再走此方法。
    /// </summary>
    public static string ForLegacyRecipe(string recipeId)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
            throw new ArgumentException("任务 id 不可空", nameof(recipeId));

        if (recipeId.StartsWith("cook:", StringComparison.Ordinal))
            return MainCookStation;
        if (recipeId.StartsWith("butcher:", StringComparison.Ordinal))
            return MainButcherStation;
        if (recipeId.StartsWith("plant:", StringComparison.Ordinal))
        {
            string plot = recipeId["plant:".Length..];
            return For("cropplot", string.IsNullOrWhiteSpace(plot) ? "main" : plot);
        }
        if (recipeId.StartsWith("weaponmod:", StringComparison.Ordinal))
            return MainModBench;
        if (recipeId.StartsWith("salvage:", StringComparison.Ordinal))
        {
            string target = recipeId["salvage:".Length..];
            // 旧任务只有世界实体拆除才需要在目标前做；库存物品拆解原本就在工作台。
            // prop#/door# 是旧运行时生成的世界稳定目标前缀，其余一律保守归工作台，避免读档后找不到 rect 永久卡死。
            if (target.StartsWith("prop#", StringComparison.Ordinal)
                || target.StartsWith("door#", StringComparison.Ordinal))
                return For("worksite", target);
            return MainWorkbench;
        }
        return MainWorkbench;
    }
}

public enum FacilityJobStartFailure
{
    None,
    WorkerUnavailable,
    FacilityBusy,
    WorkerAlreadyAssigned,
}

public sealed record FacilityJobStartResult(bool Started, FacilityJobStartFailure Failure)
{
    public static readonly FacilityJobStartResult Success = new(true, FacilityJobStartFailure.None);
}

/// <summary>一座设施的一条在制任务；同一设施同时至多一条。</summary>
public sealed record FacilityJobSlot(string SlotKey, CraftingJob Job, int WorkerId);

/// <summary>
/// 全营生产槽板：设施槽与工人占用双向唯一。它不碰库存、寻路或角色状态，
/// 只在调用方确认“到位 + 可生产相位 + 未参战”后推进工时。
/// </summary>
public sealed class FacilityJobBoard
{
    private readonly Dictionary<string, FacilityJobSlot> _bySlot = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _slotByWorker = new();

    public int Count => _bySlot.Count;

    /// <summary>按稳定槽键排序的快照，保证存档输出确定性。</summary>
    public IReadOnlyList<FacilityJobSlot> Jobs => _bySlot.Values
        .OrderBy(x => x.SlotKey, StringComparer.Ordinal)
        .ToArray();

    public FacilityJobSlot? FindBySlot(string slotKey)
        => slotKey is not null && _bySlot.TryGetValue(slotKey, out FacilityJobSlot? slot) ? slot : null;

    public FacilityJobSlot? FindByWorker(int workerId)
        => _slotByWorker.TryGetValue(workerId, out string? key) ? _bySlot[key] : null;

    /// <param name="workerMayProduce">调用方必须传角色当前可生产资格；Guard/Reading/Bedrest 等一律 false。</param>
    public FacilityJobStartResult TryStart(
        string slotKey,
        CraftingJob job,
        int workerId,
        bool workerMayProduce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
        ArgumentNullException.ThrowIfNull(job);

        if (!workerMayProduce || workerId < 0)
            return new(false, FacilityJobStartFailure.WorkerUnavailable);
        if (_bySlot.ContainsKey(slotKey))
            return new(false, FacilityJobStartFailure.FacilityBusy);
        if (_slotByWorker.ContainsKey(workerId))
            return new(false, FacilityJobStartFailure.WorkerAlreadyAssigned);

        AddUnchecked(new FacilityJobSlot(slotKey, job, workerId));
        return FacilityJobStartResult.Success;
    }

    /// <summary>读档专用；仍校验槽与工人唯一，不依赖读档瞬间的角色 Role。</summary>
    public FacilityJobStartResult TryRestore(FacilityJobSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot.SlotKey);
        ArgumentNullException.ThrowIfNull(slot.Job);
        if (slot.WorkerId < 0)
            return new(false, FacilityJobStartFailure.WorkerUnavailable);
        if (_bySlot.ContainsKey(slot.SlotKey))
            return new(false, FacilityJobStartFailure.FacilityBusy);
        if (_slotByWorker.ContainsKey(slot.WorkerId))
            return new(false, FacilityJobStartFailure.WorkerAlreadyAssigned);
        AddUnchecked(slot);
        return FacilityJobStartResult.Success;
    }

    /// <summary>离台或参战只暂停，不清任务、不释放已投入工时。</summary>
    public int Advance(
        string slotKey,
        int minutes,
        bool workerAtAssignedFacility,
        bool productionPhaseAllowsWork,
        bool workerInCombat)
    {
        FacilityJobSlot? slot = FindBySlot(slotKey);
        if (slot is null) return 0;
        bool canWork = workerAtAssignedFacility && productionPhaseAllowsWork && !workerInCombat;
        return slot.Job.Advance(minutes, canWork);
    }

    /// <summary>仅完工任务可取走结算；取走同时释放设施和工人。</summary>
    public FacilityJobSlot? TakeCompleted(string slotKey)
    {
        FacilityJobSlot? slot = FindBySlot(slotKey);
        return slot?.Job.IsComplete == true ? Remove(slot) : null;
    }

    /// <summary>取消任务并释放设施和工人。材料退不退由库存消费层决定。</summary>
    public FacilityJobSlot? Cancel(string slotKey)
    {
        FacilityJobSlot? slot = FindBySlot(slotKey);
        return slot is null ? null : Remove(slot);
    }

    /// <summary>把旧版单任务确定性迁成一项；null 或无效 worker 表示没有可迁任务。</summary>
    public static FacilityJobBoard FromLegacySingleJob(
        CraftingJob? legacyJob,
        int legacyWorkerId,
        Func<string, string>? resolveSlotKey = null)
    {
        var board = new FacilityJobBoard();
        if (legacyJob is null || legacyWorkerId < 0) return board;
        string key = (resolveSlotKey ?? FacilityJobKeys.ForLegacyRecipe)(legacyJob.RecipeId);
        FacilityJobStartResult restored = board.TryRestore(new FacilityJobSlot(key, legacyJob, legacyWorkerId));
        if (!restored.Started)
            throw new InvalidOperationException($"旧生产任务迁移失败：{restored.Failure}");
        return board;
    }

    private void AddUnchecked(FacilityJobSlot slot)
    {
        _bySlot.Add(slot.SlotKey, slot);
        _slotByWorker.Add(slot.WorkerId, slot.SlotKey);
    }

    private FacilityJobSlot Remove(FacilityJobSlot slot)
    {
        _bySlot.Remove(slot.SlotKey);
        _slotByWorker.Remove(slot.WorkerId);
        return slot;
    }
}
