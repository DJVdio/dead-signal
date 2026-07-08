using System;

namespace DeadSignal.Godot;

/// <summary>
/// 营地物资最小模型：食物份数。聚餐规则（每人每餐扣 1 份食物）的落点。
/// 纯 C# 无 Godot 依赖——初值由 <c>camp_resources.json</c> 经 CampMain 注入。
/// 数值全部"拟定待调"，用于走通流程；正式数值待策划。
/// </summary>
public sealed class CampResources
{
    /// <summary>剩余食物份数（1 份 = 1 人 1 餐）。</summary>
    public int Food { get; private set; }

    public CampResources(int food)
    {
        Food = Math.Max(0, food);
    }

    /// <summary>
    /// 一次聚餐结算：<paramref name="diners"/> 名幸存者各扣 1 份食物；食物不足时聚餐仍发生，
    /// 未吃上饭的人数记为缺口（其饥饿状态另在 Pawn 侧加深）。返回本餐明细供 UI 展示。
    /// </summary>
    public MealOutcome ConsumeMeal(int diners)
    {
        diners = Math.Max(0, diners);
        int served = Math.Min(diners, Food);
        int missing = diners - served;
        Food -= served;

        return new MealOutcome(diners, served, missing, Food);
    }

    /// <summary>
    /// 入库食物份数（营地搜刮落点）：搜到的食物不长留库存，直接累加到食物份数（clamp 到 ≥0，负数当 0）。
    /// </summary>
    public void AddFood(int portions)
    {
        Food = Math.Max(0, Food + Math.Max(0, portions));
    }

    /// <summary>
    /// 防御战失败后果落点（D 守卫防御战）：扣食物份数（不越界）。
    /// 数值由 <see cref="RaidResolution.ConsequenceFor"/> 给出"拟定待调"建议值。
    /// </summary>
    public void ApplyRaidLoss(int foodLoss)
    {
        Food = Math.Max(0, Food - Math.Max(0, foodLoss));
    }
}

/// <summary>单次聚餐结算明细（只读快照）。</summary>
public readonly record struct MealOutcome(
    int Diners,
    int Served,
    int Missing,
    int FoodRemaining);
