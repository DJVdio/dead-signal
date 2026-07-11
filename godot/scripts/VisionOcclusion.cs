using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 视野遮挡的空间查询工具（Godot 运行时层）：向 2D 物理世界打一条射线，问"两点之间是否被障碍（墙层）挡住"。
/// 这是 <see cref="VisionLogic.CanSee"/> 的 <c>occluded</c> 入参的**唯一权威来源**——纯逻辑只做视距+半角，
/// 遮挡一律经此射线判定。批次4 遮暗渲染（<see cref="VisionMask"/>）与敌方感知（Zombie/Raider）共用同一工具，
/// 保证"障碍物遮蔽视野"的口径统一。
///
/// 坐标：物理恒在 cartesian 世界（iso 只是渲染投影），故 from/to 一律传 cartesian 世界坐标（如
/// <c>Actor.GlobalPosition</c>）；射线独立于任何节点变换。<see cref="WallMask"/> 默认=墙层 0b0100
/// （对齐 <c>Actor</c>/<c>AddWall</c> 的墙碰撞层）。
/// </summary>
public static class VisionOcclusion
{
    /// <summary>墙碰撞层（Actor.LayerWall / TestExploration.AddWall 同源）。</summary>
    public const uint WallMask = 0b0100u;

    // 池化的射线查询参数（复用同一实例，逐次只改 From/To/CollisionMask）——避免每次调用 new 一个
    // PhysicsRayQueryParameters2D 的分配（tech-review P0：本工具随无限尸潮波次线性放大，是最大分配热点之一）。
    // 物理查询恒在主线程物理帧内串行调用（Actor 感知 / VisionMask 重算皆如此），无重入，静态复用安全。
    // 同批次1 Projectile._query 复用先例。IntersectRay 返回的 Dictionary 仍每次分配（Godot C# API 无免分配重载），
    // 此处只消除 query 参数对象的 churn。
    private static PhysicsRayQueryParameters2D? _query;

    /// <summary>
    /// <paramref name="from"/> 与 <paramref name="to"/> 之间是否被墙遮挡。<paramref name="space"/> 为目标世界的
    /// <see cref="PhysicsDirectSpaceState2D"/>（须在物理帧内取用：<c>GetWorld2D().DirectSpaceState</c>）。
    /// 两点重合（间距≈0）视为不遮挡。命中任一墙体即遮挡。
    /// </summary>
    public static bool IsOccluded(PhysicsDirectSpaceState2D space, Vector2 from, Vector2 to, uint wallMask = WallMask)
    {
        if (space == null || from.DistanceSquaredTo(to) < 1f)
            return false;

        PhysicsRayQueryParameters2D query = _query ??= new PhysicsRayQueryParameters2D
        {
            CollideWithAreas = false,
            CollideWithBodies = true,
        };
        query.From = from;
        query.To = to;
        query.CollisionMask = wallMask;
        global::Godot.Collections.Dictionary hit = space.IntersectRay(query);
        return hit.Count > 0;
    }
}
