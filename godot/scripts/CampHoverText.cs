using System;

namespace DeadSignal.Godot;

// 注意：本文件为纯 C# 文案拼装，不引入 Godot 类型；由消费层（CampMain）调用，
// 同时 Link 进单测，保证家具/结构目录里已经写好的 authored 简介不会只停留在数据表。

/// <summary>
/// 营地 hover 文案的最小拼装器。
/// <para>
/// 目录（<see cref="FurnitureBuildCost"/> / <see cref="CampStructureTable"/>）是简介的唯一真源；
/// 本类不复制任何文案，只负责把它们和当前状态拼进玩家能看到的一行。
/// </para>
/// </summary>
public static class CampHoverText
{
    /// <summary>把家具 authored 简介接到已有的交互提示后面；未知/无简介时保持原提示。</summary>
    public static string AppendFurnitureDescription(string furnitureKey, string baseHint)
    {
        string baseText = baseHint ?? string.Empty;
        string? description = FurnitureBuildCost.Description(furnitureKey);
        return string.IsNullOrWhiteSpace(description)
            ? baseText
            : $"{baseText} · {description}";
    }

    /// <summary>
    /// 组装结构 hover：等级风味 + 当前耐久；已毁结构只报缺口，不把 0/上限伪装成仍可砸的墙。
    /// </summary>
    public static string Structure(string displayName, StructureTier tier, double hp, bool destroyed)
    {
        string name = displayName ?? string.Empty;
        string blurb = CampStructureTable.Blurb(tier);
        if (destroyed)
        {
            return string.IsNullOrWhiteSpace(blurb)
                ? $"{name} · 已毁（缺口）"
                : $"{name} · {blurb} · 已毁（缺口）";
        }

        int maxHp = CampStructureTable.MaxHp(tier);
        double shownHp = Math.Clamp(hp, 0d, maxHp);
        string durability = $"耐久 {shownHp:0.##}/{maxHp}";
        return string.IsNullOrWhiteSpace(blurb)
            ? $"{name} · {durability}"
            : $"{name} · {blurb} · {durability}";
    }
}
