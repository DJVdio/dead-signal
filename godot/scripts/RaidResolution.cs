namespace DeadSignal.Godot;

/// <summary>防御战即时状态：进行中 / 已守住 / 被攻破。</summary>
public enum RaidState
{
    Ongoing,
    Defended,
    Overrun,
}

/// <summary>被攻破的原因（仅 <see cref="RaidState.Overrun"/> 有意义）。</summary>
public enum OverrunReason
{
    None,
    Breached,     // 丧尸突入营地纵深（越过防线，摸到住所）
    GuardsFell,   // 守卫全倒，营防无人
}

/// <summary>一次结算评估的结果（纯数据）。</summary>
public readonly struct RaidEvaluation
{
    public RaidState State { get; init; }
    public OverrunReason Reason { get; init; }
}

/// <summary>
/// 防御战后果（拟定待调占位）：损失食物/士气/伤亡数。守住则全 0。
/// 由 CampMain 施加到 <c>CampResources</c>；伤亡在实时战斗里自然产生，此处只记建议值。
/// </summary>
public readonly struct RaidConsequence
{
    public int FoodLoss { get; init; }
    public double MoraleLoss { get; init; }
}

/// <summary>
/// 防御战胜负与后果判定（纯逻辑、无 Godot 依赖，可 Link 进单测）。
/// 优先级：破防入营 &gt; 丧尸全灭守住 &gt; 守卫全倒。数值"拟定待调"。
/// </summary>
public static class RaidResolution
{
    /// <summary>
    /// 即时评估防御战状态。
    /// </summary>
    /// <param name="zombiesRemaining">场上存活丧尸数。</param>
    /// <param name="guardsAlive">存活守卫数。</param>
    /// <param name="breached">是否已有丧尸突入营地纵深。</param>
    public static RaidEvaluation Evaluate(int zombiesRemaining, int guardsAlive, bool breached)
    {
        // 破防最优先：一旦丧尸摸进住所即判损失（即便随后被清光，人已受害）。
        if (breached)
            return new RaidEvaluation { State = RaidState.Overrun, Reason = OverrunReason.Breached };

        // 丧尸全灭 = 守住。
        if (zombiesRemaining <= 0)
            return new RaidEvaluation { State = RaidState.Defended, Reason = OverrunReason.None };

        // 还有丧尸但守卫全倒 = 营防崩溃。
        if (guardsAlive <= 0)
            return new RaidEvaluation { State = RaidState.Overrun, Reason = OverrunReason.GuardsFell };

        return new RaidEvaluation { State = RaidState.Ongoing, Reason = OverrunReason.None };
    }

    /// <summary>由结算状态给出后果建议值（拟定待调）。守住无损失。</summary>
    public static RaidConsequence ConsequenceFor(RaidEvaluation eval)
    {
        if (eval.State != RaidState.Overrun)
            return new RaidConsequence { FoodLoss = 0, MoraleLoss = 0 };

        return eval.Reason switch
        {
            // 破防：丧尸翻箱倒柜 + 全营惊惧，损失更重。
            OverrunReason.Breached => new RaidConsequence { FoodLoss = 4, MoraleLoss = 20 },
            // 守卫全倒：防线失守，士气重挫。
            OverrunReason.GuardsFell => new RaidConsequence { FoodLoss = 2, MoraleLoss = 15 },
            _ => new RaidConsequence { FoodLoss = 2, MoraleLoss = 10 },
        };
    }
}
