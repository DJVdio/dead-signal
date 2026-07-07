using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型（与 RaidResolution.cs 一样被
// DeadSignal.Combat.Tests 以 Link 编入单测）。编排（脚本化生成/飘字/切阵营换目标/抉择面板/
// 收留转 Pawn）在 CampMain 的 Godot 层，本处只承载可脱 Godot 复现的两处决策。

/// <summary>
/// 教学关"克莉丝汀反水"的纯判定逻辑。反水由 CampMain 每帧调用 <see cref="ShouldTurncoat"/> 监测，
/// 触发后运行时 <c>christine.SetFaction(Survivor)</c> + <c>SetTargetProvider(劫掠者池)</c> 整套改边。
/// 胜负复用 <see cref="RaidResolution.Evaluate"/>，把 <see cref="EnemiesRemaining"/> 当"敌人数"喂入。
/// 数值"拟定待调"占位。
/// </summary>
public static class TutorialRaidLogic
{
    /// <summary>劫掠者血量比低于此值即判"受伤较重"，触发克莉丝汀反水。</summary>
    public const float RaiderWoundedThreshold = 0.5f;

    /// <summary>克莉丝汀血量比低于此值即判"受到伤害"（=满血，任意掉血即触发）。</summary>
    public const float ChristineHurtThreshold = 1f;

    /// <summary>
    /// 反水触发判定：**任一劫掠者 HP 低于阈值** 或 **克莉丝汀受到任意伤害**，先到者即翻转。
    /// </summary>
    /// <param name="raiderHealthFractions">场上存活劫掠者的血量比（0..1）。</param>
    /// <param name="christineHealthFraction">克莉丝汀自身血量比（0..1）。</param>
    public static bool ShouldTurncoat(IEnumerable<float> raiderHealthFractions, float christineHealthFraction)
    {
        if (christineHealthFraction < ChristineHurtThreshold)
        {
            return true;
        }
        foreach (float hp in raiderHealthFractions)
        {
            if (hp < RaiderWoundedThreshold)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 喂给 <see cref="RaidResolution.Evaluate"/> 的"敌方剩余数"：存活劫掠者数 +（克莉丝汀未反水且存活时算 1）。
    /// 保证"劫掠者清空但克莉丝汀仍敌对"不会被误判为守住。
    /// </summary>
    public static int EnemiesRemaining(int raidersAlive, bool christineAliveAndHostile)
    {
        return raidersAlive + (christineAliveAndHostile ? 1 : 0);
    }
}
