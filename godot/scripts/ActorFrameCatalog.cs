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

/// <summary>七类持械动作 × 每类三帧 × 八方向；只供能真正持械的人类角色使用。</summary>
public static class ActorAttackFrameCatalog
{
    public const int Actions = 7;
    public const int FramesPerAction = 3;
    public const int Columns = Actions * FramesPerAction;
    public const int Rows = 8;
    public const string Root = "res://assets/world/attacks";

    public static string PathFor(string? displayName, string actorKind) => displayName switch
    {
        "山姆" => $"{Root}/sam.png",
        "诺蒂" => $"{Root}/notty.png",
        "克莉丝汀" => $"{Root}/christine.png",
        "耗子" => $"{Root}/rat.png",
        "道格" => $"{Root}/doug.png",
        "南丁格尔" => $"{Root}/nightingale.png",
        "皮特" => $"{Root}/pete.png",
        _ => $"{Root}/{actorKind}.png",
    };

    public static int RowForDirection(int directionColumn)
        => Math.Clamp(directionColumn, 0, Rows - 1);

    public static int ActionFor(WeaponAttackPose pose) => pose switch
    {
        WeaponAttackPose.OneHandSwing => 0,
        WeaponAttackPose.OneHandThrust => 1,
        WeaponAttackPose.OneHandShot => 2,
        WeaponAttackPose.TwoHandSwing => 3,
        WeaponAttackPose.TwoHandThrust => 4,
        WeaponAttackPose.TwoHandShot => 5,
        WeaponAttackPose.BowShot => 6,
        _ => 0,
    };

    public static int FrameFor(float progress)
    {
        float normalized = Math.Clamp(progress, 0f, 1f);
        if (normalized < 0.28f) return 0;
        if (normalized < 0.62f) return 1;
        return 2;
    }

    public static int ColumnFor(WeaponAttackPose pose, float progress)
        => ActionFor(pose) * FramesPerAction + FrameFor(progress);
}
