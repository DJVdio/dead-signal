using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>弹丸在 faux-iso 表现层的外形；物理弹道仍由 Projectile 独立负责。</summary>
public enum ProjectileVfxKind
{
    Bullet,
    Pellet,
    Arrow,
    Bolt,
}

public enum ImpactVfxKind
{
    Armor,
    FleshSharp,
    FleshBlunt,
    Fatal,
    Wall,
    Miss,
}

/// <summary>战斗特效分类纯逻辑。新增远程武器时由测试强制要求明确落入一种外形。</summary>
public static class CombatVfxCatalog
{
    public static ProjectileVfxKind ProjectileFor(Weapon weapon, string ammoKey)
    {
        if (weapon.Name.Contains('弩')) return ProjectileVfxKind.Bolt;
        if (ArrowTable.IsArrow(ammoKey) || weapon.Name.Contains('弓')) return ProjectileVfxKind.Arrow;
        if (weapon.PelletCount > 1) return ProjectileVfxKind.Pellet;
        return ProjectileVfxKind.Bullet;
    }

    public static ImpactVfxKind ImpactFor(AttackOutcome hit)
    {
        if (hit.Died || hit.Severed) return ImpactVfxKind.Fatal;
        if (hit.Blocked || hit.Damage <= 0) return ImpactVfxKind.Armor;
        return hit.FinalType == DamageType.Sharp
            ? ImpactVfxKind.FleshSharp
            : ImpactVfxKind.FleshBlunt;
    }
}
