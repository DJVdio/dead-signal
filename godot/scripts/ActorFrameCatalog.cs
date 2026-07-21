using System;

namespace DeadSignal.Godot;

/// <summary>逐帧角色图集目录。12 列动作 × 8 行方向，与站立图集方向顺序一致。</summary>
public static class ActorFrameCatalog
{
    public const int Columns = 12;
    public const int Rows = 8;
    public const string Root = "res://assets/world/animations";

    public static string PathFor(string? displayName, string actorKind) => displayName switch
    {
        "山姆" => $"{Root}/sam.png",
        "诺蒂" => $"{Root}/notty.png",
        "克莉丝汀" => $"{Root}/christine.png",
        "耗子" => $"{Root}/rat.png",
        "道格" => $"{Root}/doug.png",
        "南丁格尔" => $"{Root}/nightingale.png",
        "皮特" => $"{Root}/pete.png",
        "布鲁斯" => $"{Root}/bruce.png",
        _ => $"{Root}/{actorKind}.png",
    };

    public static int RowForDirection(int directionColumn)
        => Math.Clamp(directionColumn, 0, Rows - 1);

    /// <summary>东南行由西南整行镜像而来；整行镜像会反转动作列，取样时反向映射回来。</summary>
    public static int SourceColumnForDirection(int directionColumn, int actionColumn)
        => directionColumn == Rows - 1 ? Columns - 1 - actionColumn : actionColumn;

    public static int ColumnFor(ActorAnimationState state, double clock, float attackProgress) => state switch
    {
        ActorAnimationState.Walk => 1 + ((int)Math.Floor(clock * 7.5) % 3 + 3) % 3,
        ActorAnimationState.Attack => attackProgress < 0.48f ? 4 : 5,
        ActorAnimationState.Hit => 6,
        ActorAnimationState.Work => 7,
        ActorAnimationState.ReadStanding => 8,
        ActorAnimationState.ReadSeated => 9,
        ActorAnimationState.Sit => 10,
        ActorAnimationState.Lie => 11,
        _ => 0,
    };
}
