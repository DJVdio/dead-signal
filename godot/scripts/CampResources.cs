using System;

namespace DeadSignal.Godot;

/// <summary>
/// 营地物资最小模型：食物份数 + 士气。聚餐规则（每人每餐扣 1 份食物、食物不足则士气下降）的落点。
/// 纯 C# 无 Godot 依赖——初值/惩罚由 <c>camp_resources.json</c> 经 CampMain 注入。
/// 数值全部"拟定待调"，用于走通流程；正式数值待策划。
/// </summary>
public sealed class CampResources
{
    /// <summary>剩余食物份数（1 份 = 1 人 1 餐）。</summary>
    public int Food { get; private set; }

    /// <summary>士气（0~<see cref="_moraleMax"/>）。食物短缺时按缺额下降。</summary>
    public double Morale { get; private set; }

    private readonly double _moralePenaltyPerMissingMeal; // 每缺一份口粮扣的士气
    private readonly double _moraleMax;

    public CampResources(int food, double morale, double moralePenaltyPerMissingMeal, double moraleMax = 100)
    {
        Food = Math.Max(0, food);
        _moraleMax = moraleMax <= 0 ? 100 : moraleMax;
        Morale = Math.Clamp(morale, 0, _moraleMax);
        _moralePenaltyPerMissingMeal = moralePenaltyPerMissingMeal;
    }

    /// <summary>
    /// 一次聚餐结算：<paramref name="diners"/> 名幸存者各扣 1 份食物；食物不足时聚餐仍发生，
    /// 但按未吃上饭的人数扣士气。返回本餐明细供 UI 展示。
    /// </summary>
    public MealOutcome ConsumeMeal(int diners)
    {
        diners = Math.Max(0, diners);
        int served = Math.Min(diners, Food);
        int missing = diners - served;
        Food -= served;

        double moraleDelta = missing > 0 ? -missing * _moralePenaltyPerMissingMeal : 0;
        Morale = Math.Clamp(Morale + moraleDelta, 0, _moraleMax);

        return new MealOutcome(diners, served, missing, moraleDelta, Food, Morale);
    }

    /// <summary>
    /// 饥饿士气下降落点：本次昼夜切换全员饥饿造成的士气总扣减（越饿越重，阶梯见 <see cref="HungerState.MoraleFor"/>）。
    /// <paramref name="penalty"/> 为各存活者当前刻度士气惩罚之和；非正数视为 0（clamp 不越界）。返回结算后士气。
    /// </summary>
    public double ApplyHungerMorale(double penalty)
    {
        Morale = Math.Clamp(Morale - Math.Max(0, penalty), 0, _moraleMax);
        return Morale;
    }

    /// <summary>
    /// 防御战失败后果落点（D 守卫防御战）：扣食物份数 + 扣士气（各自不越界）。
    /// 数值由 <see cref="RaidResolution.ConsequenceFor"/> 给出"拟定待调"建议值。
    /// </summary>
    public void ApplyRaidLoss(int foodLoss, double moraleLoss)
    {
        Food = Math.Max(0, Food - Math.Max(0, foodLoss));
        Morale = Math.Clamp(Morale - Math.Max(0, moraleLoss), 0, _moraleMax);
    }
}

/// <summary>单次聚餐结算明细（只读快照）。</summary>
public readonly record struct MealOutcome(
    int Diners,
    int Served,
    int Missing,
    double MoraleDelta,
    int FoodRemaining,
    double Morale);
