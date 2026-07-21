using System;

namespace DeadSignal.Godot;

/// <summary>角色视觉状态；只描述表现，不参与移动、战斗或工时结算。</summary>
public enum ActorAnimationState
{
    Idle,
    Walk,
    Attack,
    Hit,
    Work,
    ReadStanding,
    ReadSeated,
    Sit,
    Lie,
}

/// <summary>实际出手武器对应的动作骨架。改装武器按基础中文名前缀归类。</summary>
public enum WeaponAttackAnimation
{
    Unarmed,
    Bite,
    KnifeThrust,
    SwordSlash,
    HeavySwing,
    PolearmThrust,
    PistolRecoil,
    LongGunRecoil,
    BowShot,
    CrossbowRecoil,
}

/// <summary>剧情/设施临时占用对常规 Role 的视觉覆盖。</summary>
public enum PawnVisualActivity
{
    None,
    Sitting,
    Working,
    Reading,
    Lying,
}

/// <summary>
/// 动画状态与武器动作目录。纯逻辑、无 Godot 类型，供运行时消费并由单测钉死覆盖面。
/// </summary>
public static class ActorAnimationCatalog
{
    public static ActorAnimationState Resolve(
        bool moving,
        bool attacking,
        bool hit,
        PawnRole? role,
        bool stationing,
        bool hasReadingSeat,
        PawnVisualActivity activity)
    {
        // 短促反馈优先于循环动作；移动优先于“尚在走向工位/座位”的角色职责。
        if (hit) return ActorAnimationState.Hit;
        if (attacking) return ActorAnimationState.Attack;
        if (moving) return ActorAnimationState.Walk;
        // 正在赶往工位/座位但因避让短暂停住时，不得提前播放工作/阅读；也不伪造脚步。
        if (stationing) return ActorAnimationState.Idle;

        ActorAnimationState overridden = activity switch
        {
            PawnVisualActivity.Sitting => ActorAnimationState.Sit,
            PawnVisualActivity.Working => ActorAnimationState.Work,
            PawnVisualActivity.Reading => hasReadingSeat
                ? ActorAnimationState.ReadSeated
                : ActorAnimationState.ReadStanding,
            PawnVisualActivity.Lying => ActorAnimationState.Lie,
            _ => ActorAnimationState.Idle,
        };
        if (activity != PawnVisualActivity.None) return overridden;

        return role switch
        {
            PawnRole.Sleeping or PawnRole.Bedrest => ActorAnimationState.Lie,
            PawnRole.Producing => ActorAnimationState.Work,
            PawnRole.Reading when hasReadingSeat => ActorAnimationState.ReadSeated,
            PawnRole.Reading => ActorAnimationState.ReadStanding,
            _ => ActorAnimationState.Idle,
        };
    }

    public static WeaponAttackAnimation AttackFor(string? weaponName)
    {
        string name = BaseName(weaponName);
        return name switch
        {
            "爪击" or "撕咬" => WeaponAttackAnimation.Bite,
            "拳脚" => WeaponAttackAnimation.Unarmed,
            "匕首" or "骨刀" or "刺剑" => WeaponAttackAnimation.KnifeThrust,
            "短剑" or "长剑" => WeaponAttackAnimation.SwordSlash,
            "重剑" or "消防斧" or "棍棒" or "尖头锤" or "破甲锤" => WeaponAttackAnimation.HeavySwing,
            "草叉" => WeaponAttackAnimation.PolearmThrust,
            "手枪" or "自制手枪" or "牙医小手枪" => WeaponAttackAnimation.PistolRecoil,
            "自制猎枪" or "冲锋枪" or "步枪" or "狙击枪" or "自制霰弹枪" => WeaponAttackAnimation.LongGunRecoil,
            "短弓" or "反曲弓" or "长弓" or "竞技复合弓" or "狩猎弓" => WeaponAttackAnimation.BowShot,
            "单手轻弩" or "双手重弩" or "复合弩" => WeaponAttackAnimation.CrossbowRecoil,
            _ => WeaponAttackAnimation.Unarmed,
        };
    }

    public static float DurationSeconds(WeaponAttackAnimation animation) => animation switch
    {
        WeaponAttackAnimation.PistolRecoil => 0.18f,
        WeaponAttackAnimation.LongGunRecoil => 0.24f,
        WeaponAttackAnimation.CrossbowRecoil => 0.28f,
        WeaponAttackAnimation.BowShot => 0.34f,
        WeaponAttackAnimation.HeavySwing => 0.42f,
        WeaponAttackAnimation.SwordSlash => 0.32f,
        WeaponAttackAnimation.KnifeThrust or WeaponAttackAnimation.PolearmThrust => 0.26f,
        WeaponAttackAnimation.Bite => 0.25f,
        _ => 0.22f,
    };

    private static string BaseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "拳脚";
        int variant = name.IndexOf('（');
        int instance = name.IndexOf('#');
        int end = variant < 0 ? name.Length : variant;
        if (instance >= 0) end = Math.Min(end, instance);
        return name[..end];
    }
}
