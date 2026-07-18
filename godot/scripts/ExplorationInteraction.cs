using System;
using System.Numerics;

namespace DeadSignal.Godot;

/// <summary>
/// 探索期输入与威胁配置的纯逻辑契约。
///
/// 探索关是 top-down cartesian 平面，营地才需要 Iso.Project/Unproject。Godot
/// 消费层必须通过这里判断角色能否接收探索指令，不能把营地的 Role=Idle 闸门
/// 直接复用到 Role=Expedition 的远征队员上。
/// </summary>
public static class ExplorationInteractionLogic
{
    /// <summary>探索中只允许本次远征队员接收新指令；营地沿用 Idle。</summary>
    public static bool CanControl(PawnRole role, bool inExploration)
        => inExploration ? role == PawnRole.Expedition : role == PawnRole.Idle;

    /// <summary>
    /// 将屏幕/画布点转换成世界 cartesian 点：探索平面保持原值，营地由调用方注入
    /// faux-iso 反投影。用委托避免把 Godot 类型拖入纯逻辑测试。
    /// </summary>
    public static Vector2 WorldPoint(Vector2 point, bool inExploration, Func<Vector2, Vector2> campInverse)
    {
        ArgumentNullException.ThrowIfNull(campInverse);
        return inExploration ? point : campInverse(point);
    }

    /// <summary>物资缓存/战斗尸体可重复点击接着搜；叙事/主线/采集点仍由 flag 去重。</summary>
    public static bool IsRepeatableDiscovery(string discoveryId)
        => !string.IsNullOrEmpty(discoveryId)
           && (discoveryId.StartsWith("cache:", StringComparison.Ordinal)
               || discoveryId.Contains("的尸体 #", StringComparison.Ordinal));
}

/// <summary>
/// 地图威胁档位的兜底带宽。具体关卡可在 world_graph.json 以 enemyCount 覆盖；
/// 这张表只负责新目的地漏配时的确定性回退，不改变旧关卡 authored 数量。
/// </summary>
public static class ExplorationThreatProfile
{
    public static int EnemyCountFor(DangerTier danger) => danger switch
    {
        DangerTier.Low => 3,
        DangerTier.Medium => 6,
        DangerTier.High => 10,
        _ => 0,
    };
}
