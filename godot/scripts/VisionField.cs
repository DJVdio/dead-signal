using System;
using System.Collections.Generic;
using System.Numerics;

namespace DeadSignal.Godot;

/// <summary>
/// 视野场纯逻辑（零 Godot 依赖，Link 进 <c>DeadSignal.Combat.Tests</c>）。批次4 遮暗渲染 / 视野外隐藏的判定核。
///
/// 模型：一批观察者（位置 + 朝向 + 视野锥），加一个"两点间是否被障碍遮蔽"的谓词（由 Godot 层 raycast 供给），
/// 回答两个问题：
///  1. <see cref="IsPointVisible"/>：世界某点是否被任一观察者看见（供检测层揭示/隐藏敌人/可交互物）；
///  2. <see cref="ComputeDarkCells"/>：把包围盒切成网格，逐格心判可见，产出"该遮暗"的格位图（供遮暗渲染层直绘暗格）。
///
/// 判定复用 <see cref="VisionLogic.CanSee"/>（视距+半角+遮挡三重与）。遮挡谓词只在"格心/目标已落在某观察者锥内"
/// 时才被调用（先做 <see cref="VisionLogic.CanSee"/> 的 <c>occluded:false</c> 廉价锥检，再补 raycast）——绝大多数格
/// 在所有锥外、直接跳过 raycast，把昂贵的空间查询压到最小。空间执行（raycast/遮暗直绘/节点隐藏）归 Godot 运行时层。
///
/// 坐标用 <see cref="System.Numerics.Vector2"/>（对齐 <see cref="VisionLogic"/> 的零依赖口径；消费方从 Godot.Vector2 转换）。
/// </summary>
public static class VisionField
{
    /// <summary>一名观察者：世界位置 + 朝向向量（无需归一化）+ 其当前光照下的视野锥。</summary>
    public readonly struct Viewer
    {
        public Vector2 Position { get; }
        public Vector2 Facing { get; }
        public VisionLogic.VisionCone Cone { get; }

        public Viewer(Vector2 position, Vector2 facing, VisionLogic.VisionCone cone)
        {
            Position = position;
            Facing = facing;
            Cone = cone;
        }
    }

    /// <summary>
    /// 世界某点是否被任一观察者看见。对每个观察者先做廉价锥检（视距+半角，<c>occluded:false</c>）；
    /// 仅当落在锥内才调用 <paramref name="occludedBetween"/>（观察者→点是否被障碍遮蔽），未被遮蔽即可见。
    /// 无观察者 / 全被遮蔽或全在锥外 → 不可见。
    /// </summary>
    public static bool IsPointVisible(
        IReadOnlyList<Viewer> viewers, Vector2 point, Func<Vector2, Vector2, bool> occludedBetween)
    {
        for (int i = 0; i < viewers.Count; i++)
        {
            Viewer v = viewers[i];
            // 廉价锥检：先不管遮挡，问"若无遮挡是否在视距+半角内"。落在锥外的点（占绝大多数）直接跳过 raycast。
            if (!VisionLogic.CanSee(v.Position, v.Facing, point, v.Cone, occluded: false))
                continue;
            if (!occludedBetween(v.Position, point))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 把 [<paramref name="boundsMin"/>, <paramref name="boundsMax"/>] 包围盒按 <paramref name="cellSize"/> 切成网格，
    /// 逐格心判可见，产出行主序（row-major，index = r*cols + c）的"该遮暗"位图：格心不可见 → <c>true</c>（遮暗）。
    /// <paramref name="cols"/>/<paramref name="rows"/> 回传网格尺寸，供调用方按 index 反算格的世界矩形去绘制。
    /// 包围盒非法（宽/高 ≤0）时回退 1×1，避免空数组。
    /// </summary>
    public static bool[] ComputeDarkCells(
        Vector2 boundsMin, Vector2 boundsMax, float cellSize,
        IReadOnlyList<Viewer> viewers, Func<Vector2, Vector2, bool> occludedBetween,
        out int cols, out int rows)
    {
        bool[]? buffer = null;
        ComputeDarkCells(boundsMin, boundsMax, cellSize, viewers, occludedBetween, ref buffer, out cols, out rows);
        return buffer!;
    }

    /// <summary>
    /// 复用缓冲区版：把结果写进调用方持有的 <paramref name="buffer"/>（长度不足/为 null 时才重新分配，尺寸不变即原地覆写），
    /// 消除每次重算 <c>new bool[cols*rows]</c> 的逐帧托管分配（营地约 1900 格、4Hz）。语义与分配版完全一致。
    /// </summary>
    public static void ComputeDarkCells(
        Vector2 boundsMin, Vector2 boundsMax, float cellSize,
        IReadOnlyList<Viewer> viewers, Func<Vector2, Vector2, bool> occludedBetween,
        ref bool[]? buffer, out int cols, out int rows)
    {
        float size = cellSize > 0f ? cellSize : 1f;
        cols = Math.Max(1, (int)MathF.Ceiling((boundsMax.X - boundsMin.X) / size));
        rows = Math.Max(1, (int)MathF.Ceiling((boundsMax.Y - boundsMin.Y) / size));

        int count = cols * rows;
        if (buffer is null || buffer.Length != count)
        {
            buffer = new bool[count];
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                Vector2 center = new(
                    boundsMin.X + (c + 0.5f) * size,
                    boundsMin.Y + (r + 0.5f) * size);
                buffer[r * cols + c] = !IsPointVisible(viewers, center, occludedBetween);
            }
        }
    }
}
