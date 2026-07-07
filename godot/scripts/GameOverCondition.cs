using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 HungerState.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 承载「游戏结束」的判定规则：全体玩家幸存者死亡即全灭。
// 只判定，不含暂停/弹窗等表现——那些归 Godot 运行时层（GameOverPanel + CampMain）。

/// <summary>
/// 「营地全灭」判定（游戏结束最小版）。
/// 口径：玩家幸存者（CampMain 的 _survivors 里的 Pawn）**无一存活**即全灭。
/// 克莉丝汀反水盟友、劫掠者、丧尸都不是幸存者，不进本判定的输入。
/// 纯函数，无副作用，供 CampMain 在 Pawn 死亡、移出名单**之后**调用。
/// </summary>
public static class GameOverCondition
{
    /// <summary>
    /// 存活幸存者数为 0（或负，兜底）即全灭。空营地视为全灭。
    /// </summary>
    /// <param name="aliveSurvivorCount">当前仍存活的玩家幸存者数量。</param>
    public static bool AllSurvivorsDead(int aliveSurvivorCount) => aliveSurvivorCount <= 0;

    /// <summary>
    /// 名单快照重载：传每个幸存者「是否存活」的布尔序列，无一为真（含空序列）即全灭。
    /// </summary>
    /// <param name="survivorAliveFlags">各幸存者存活标志的快照。</param>
    public static bool AllSurvivorsDead(IEnumerable<bool> survivorAliveFlags)
    {
        foreach (bool alive in survivorAliveFlags)
        {
            if (alive)
            {
                return false;
            }
        }
        return true;
    }
}
