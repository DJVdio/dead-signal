using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// faux-iso 战斗与交互瞬时特效。全部输入都是表现层坐标/方向；不碰碰撞、伤害、库存或随机结算。
/// </summary>
public sealed partial class CombatVfxBurst : Node2D
{
    private enum BurstKind
    {
        MuzzleGun,
        MuzzleBow,
        MuzzleCrossbow,
        Melee,
        Impact,
        Death,
        Loot,
        Door,
        WorkDust,
    }

    private BurstKind _kind;
    private ProjectileVfxKind _projectileKind;
    private ImpactVfxKind _impactKind;
    private WeaponAttackAnimation _attackKind;
    private Vector2 _dir = Vector2.Right;
    private Color _color = Colors.White;
    private float _strength = 1f;
    private float _age;
    private float _lifetime = 0.3f;
    private float _phase;

    public static void SpawnMuzzle(Node2D? layer, Vector2 cartOrigin, Vector2 cartDirection,
        ProjectileVfxKind projectileKind, WeaponAttackAnimation attackKind)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return;
        Vector2 screenDir = Iso.Project(cartDirection).Normalized();
        BurstKind kind = attackKind switch
        {
            WeaponAttackAnimation.BowShot => BurstKind.MuzzleBow,
            WeaponAttackAnimation.CrossbowRecoil => BurstKind.MuzzleCrossbow,
            _ => BurstKind.MuzzleGun,
        };
        var vfx = New(layer, Iso.Project(cartOrigin), kind, 0.20f);
        vfx._projectileKind = projectileKind;
        vfx._attackKind = attackKind;
        vfx._dir = screenDir == Vector2.Zero ? Vector2.Right : screenDir;
        vfx._strength = attackKind == WeaponAttackAnimation.LongGunRecoil ? 1.25f : 1f;
        GameAudioRuntime.PlayWorld(GameAudioCatalog.MuzzleCue(projectileKind, attackKind), cartOrigin);
    }

    public static void SpawnMelee(Node2D? layer, Vector2 attackerCart, Vector2 targetCart,
        WeaponAttackAnimation attackKind)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return;
        Vector2 from = Iso.Project(attackerCart);
        Vector2 to = Iso.Project(targetCart);
        Vector2 direction = (to - from).Normalized();
        var vfx = New(layer, from.Lerp(to, 0.48f) + new Vector2(0f, -18f), BurstKind.Melee,
            attackKind == WeaponAttackAnimation.HeavySwing ? 0.34f : 0.25f);
        vfx._dir = direction == Vector2.Zero ? Vector2.Right : direction;
        vfx._attackKind = attackKind;
        vfx._strength = attackKind == WeaponAttackAnimation.HeavySwing ? 1.3f : 1f;
        GameAudioRuntime.PlayWorld(GameAudioCatalog.MeleeCue(attackKind), attackerCart.Lerp(targetCart, 0.48f));
    }

    public static void SpawnImpact(Node2D? layer, Vector2 isoPosition, ImpactVfxKind kind, float strength)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return;
        var vfx = New(layer, isoPosition, BurstKind.Impact, kind == ImpactVfxKind.Fatal ? 0.48f : 0.30f);
        vfx._impactKind = kind;
        vfx._strength = Mathf.Clamp(strength, 0.45f, 1.6f);
        // 同类命中不要像盖章一样每次都朝同一方向炸开；位置决定相位，既有变化又不引入玩法随机源。
        vfx._phase = Mathf.PosMod(isoPosition.X * 0.037f + isoPosition.Y * 0.019f, Mathf.Tau);
        vfx._color = kind switch
        {
            ImpactVfxKind.Armor => new Color(0.88f, 0.94f, 1f),
            ImpactVfxKind.FleshSharp => new Color(0.72f, 0.04f, 0.05f),
            ImpactVfxKind.FleshBlunt => new Color(0.72f, 0.30f, 0.12f),
            ImpactVfxKind.Fatal => new Color(0.48f, 0.02f, 0.03f),
            ImpactVfxKind.Wall => new Color(0.62f, 0.58f, 0.50f),
            _ => new Color(0.48f, 0.42f, 0.32f),
        };
        if (kind != ImpactVfxKind.Miss)
            GameAudioRuntime.PlayIso(GameAudioCatalog.ImpactCue(kind), isoPosition, (vfx._strength - 1f) * 2.5f);
    }

    public static void SpawnDeath(Node2D? layer, Vector2 cartPosition, Color bodyColor, float radius)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return;
        var vfx = New(layer, Iso.Project(cartPosition), BurstKind.Death, 0.58f);
        vfx._color = bodyColor.Darkened(0.28f);
        vfx._strength = Mathf.Clamp(radius / 12f, 0.75f, 1.5f);
        GameAudioRuntime.PlayWorld(AudioCue.Death, cartPosition);
    }

    public static void SpawnLoot(Node2D? layer, Vector2 cartPosition)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return;
        New(layer, Iso.Project(cartPosition) + new Vector2(0f, -24f), BurstKind.Loot, 0.46f);
        GameAudioRuntime.PlayWorld(AudioCue.Loot, cartPosition);
    }

    public static void SpawnDoor(Node2D? layer, Vector2 cartCenter, bool opening)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return;
        var vfx = New(layer, Iso.Project(cartCenter), BurstKind.Door, 0.34f);
        vfx._strength = opening ? 1f : 0.78f;
        vfx._phase = opening ? 1f : -1f;
        GameAudioRuntime.PlayWorld(opening ? AudioCue.DoorOpen : AudioCue.DoorClose, cartCenter);
    }

    public static void SpawnWorkDust(Node2D? layer, Vector2 cartPosition, float strength = 1f)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return;
        var vfx = New(layer, Iso.Project(cartPosition), BurstKind.WorkDust, 0.42f);
        vfx._strength = strength;
        GameAudioRuntime.PlayWorld(AudioCue.Work, cartPosition, (strength - 1f) * 2f);
    }

    private static CombatVfxBurst New(Node2D layer, Vector2 isoPosition, BurstKind kind, float lifetime)
    {
        var vfx = new CombatVfxBurst
        {
            Position = isoPosition,
            _kind = kind,
            _lifetime = lifetime,
            ZIndex = 120,
        };
        layer.AddChild(vfx);
        vfx.QueueRedraw();
        return vfx;
    }

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= _lifetime)
        {
            QueueFree();
            return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float t = Mathf.Clamp(_age / _lifetime, 0f, 1f);
        float fade = 1f - t;
        switch (_kind)
        {
            case BurstKind.MuzzleGun:
                DrawGunMuzzle(t, fade);
                break;
            case BurstKind.MuzzleBow:
                DrawBowRelease(t, fade, crossbow: false);
                break;
            case BurstKind.MuzzleCrossbow:
                DrawBowRelease(t, fade, crossbow: true);
                break;
            case BurstKind.Melee:
                DrawMelee(t, fade);
                break;
            case BurstKind.Impact:
                DrawImpact(t, fade);
                break;
            case BurstKind.Death:
                DrawDeath(t, fade);
                break;
            case BurstKind.Loot:
                DrawLoot(t, fade);
                break;
            case BurstKind.Door:
                DrawDoor(t, fade);
                break;
            case BurstKind.WorkDust:
                DrawWorkDust(t, fade);
                break;
        }
    }

    private void DrawGunMuzzle(float t, float fade)
    {
        Vector2 p = new(-_dir.Y, _dir.X);
        float len = (16f + 15f * _strength) * (1f - t * 0.55f);
        Color hot = new(1f, 0.92f, 0.46f, fade);
        Color orange = new(1f, 0.38f, 0.08f, fade * 0.9f);
        Vector2[] flame =
        {
            -p * 3f,
            _dir * len * 0.55f - p * 6f,
            _dir * len,
            _dir * len * 0.55f + p * 6f,
            p * 3f,
        };
        DrawColoredPolygon(flame, orange);
        DrawLine(Vector2.Zero, _dir * len * 0.72f, hot, 4f * _strength);
        DrawCircle(Vector2.Zero, 5f * _strength * fade, hot);
        for (int i = 0; i < 3; i++)
        {
            float side = i - 1f;
            Vector2 smoke = -_dir * (4f + t * (8f + i * 3f)) + p * side * (3f + t * 5f) + new Vector2(0f, -t * 7f);
            DrawCircle(smoke, (2f + t * 4f) * _strength, new Color(0.45f, 0.46f, 0.48f, fade * 0.42f));
        }
        // 枪械抛壳：黄铜小壳从枪身侧后上抛；弓弩不会走本分支。
        Vector2 casing = -_dir * (2f + t * 7f) + p * (7f + t * 13f) + new Vector2(0f, -Mathf.Sin(t * Mathf.Pi) * 8f);
        DrawLine(casing - _dir * 2.5f, casing + _dir * 2.5f, new Color(0.84f, 0.63f, 0.20f, fade), 2f);
    }

    private void DrawBowRelease(float t, float fade, bool crossbow)
    {
        Vector2 p = new(-_dir.Y, _dir.X);
        Color air = new(0.78f, 0.88f, 0.82f, fade * 0.7f);
        float reach = (crossbow ? 18f : 24f) * (0.35f + t);
        DrawLine(-_dir * 3f + p * 8f, _dir * reach + p * (10f + t * 5f), air, 1.5f);
        DrawLine(-_dir * 3f - p * 8f, _dir * reach - p * (10f + t * 5f), air, 1.5f);
        if (crossbow)
            DrawCircle(Vector2.Zero, 5f + t * 4f, new Color(0.66f, 0.58f, 0.43f, fade * 0.35f));
    }

    private void DrawMelee(float t, float fade)
    {
        float angle = _dir.Angle();
        float start = angle - (_attackKind is WeaponAttackAnimation.KnifeThrust or WeaponAttackAnimation.PolearmThrust ? 0.16f : 1.18f);
        float end = angle + (_attackKind is WeaponAttackAnimation.KnifeThrust or WeaponAttackAnimation.PolearmThrust ? 0.16f : 0.82f);
        float radius = (_attackKind == WeaponAttackAnimation.HeavySwing ? 28f : 22f) * _strength * (0.72f + 0.28f * t);
        Color c = _attackKind switch
        {
            WeaponAttackAnimation.HeavySwing => new Color(0.88f, 0.62f, 0.25f, fade),
            WeaponAttackAnimation.Bite => new Color(0.70f, 0.08f, 0.08f, fade),
            WeaponAttackAnimation.Unarmed => new Color(0.82f, 0.78f, 0.68f, fade),
            _ => new Color(0.88f, 0.94f, 1f, fade),
        };
        if (_attackKind is WeaponAttackAnimation.KnifeThrust or WeaponAttackAnimation.PolearmThrust)
        {
            DrawLine(-_dir * radius * 0.28f, _dir * radius, c, _attackKind == WeaponAttackAnimation.PolearmThrust ? 4f : 3f);
            DrawLine(_dir * radius, _dir * radius * 0.72f + new Vector2(-_dir.Y, _dir.X) * 4f, c.Lightened(0.25f), 2f);
        }
        else
        {
            DrawArc(Vector2.Zero, radius, start, end, 18, c, _attackKind == WeaponAttackAnimation.HeavySwing ? 6f : 3.5f);
            DrawArc(Vector2.Zero, radius * 0.78f, start + 0.15f, end - 0.12f, 15, new Color(c.R, c.G, c.B, fade * 0.35f), 2f);
        }
    }

    private void DrawImpact(float t, float fade)
    {
        float reach = (8f + 24f * t) * _strength;
        int rays = _impactKind == ImpactVfxKind.Fatal ? 11 : 7;
        for (int i = 0; i < rays; i++)
        {
            float a = i * 2.399963f + _phase;
            Vector2 d = Vector2.FromAngle(a);
            float wobble = 0.72f + (i % 3) * 0.14f;
            Color c = new(_color.R, _color.G, _color.B, fade * (0.65f + (i % 2) * 0.25f));
            if (_impactKind == ImpactVfxKind.Armor)
                DrawLine(d * reach * 0.28f, d * reach * wobble, c, 2f);
            else
                DrawCircle(d * reach * wobble, (2.1f + _strength) * fade, c);
        }
        if (_impactKind == ImpactVfxKind.FleshBlunt)
            DrawArc(Vector2.Zero, reach * 0.55f, 0f, Mathf.Tau, 20, new Color(_color.R, _color.G, _color.B, fade * 0.55f), 3f);
        if (_impactKind == ImpactVfxKind.Armor)
            DrawCircle(Vector2.Zero, 4f * fade, new Color(1f, 0.92f, 0.46f, fade));
    }

    private void DrawDeath(float t, float fade)
    {
        float radius = (10f + t * 30f) * _strength;
        DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 28, new Color(_color.R, _color.G, _color.B, fade * 0.52f), 4f);
        for (int i = 0; i < 7; i++)
        {
            Vector2 d = Vector2.FromAngle(i * 0.897f + 0.3f);
            Vector2 pos = d * radius * (0.45f + (i % 3) * 0.18f) + new Vector2(0f, -t * 10f);
            DrawCircle(pos, (4f + i % 2 * 2f) * fade * _strength, new Color(_color.R, _color.G, _color.B, fade * 0.38f));
        }
    }

    private void DrawLoot(float t, float fade)
    {
        Color gold = new(1f, 0.84f, 0.32f, fade);
        for (int i = 0; i < 4; i++)
        {
            float x = (i - 1.5f) * 7f;
            Vector2 c = new(x, -t * (18f + i * 3f));
            DrawLine(c + new Vector2(-3f, 0), c + new Vector2(3f, 0), gold, 2f);
            DrawLine(c + new Vector2(0, -3f), c + new Vector2(0, 3f), gold, 2f);
        }
    }

    private void DrawDoor(float t, float fade)
    {
        Color dust = new(0.60f, 0.52f, 0.40f, fade * 0.52f);
        for (int i = 0; i < 6; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            Vector2 p = new(side * (5f + t * (10f + i * 2f)), -2f - t * (4f + i));
            DrawCircle(p, (2.5f + t * 3f) * _strength, dust);
        }
        DrawArc(Vector2.Zero, 12f + t * 7f, _phase > 0 ? -1.2f : 1.9f, _phase > 0 ? 0.2f : 3.3f, 12,
            new Color(0.82f, 0.74f, 0.60f, fade * 0.55f), 2f);
    }

    private void DrawWorkDust(float t, float fade)
    {
        for (int i = 0; i < 7; i++)
        {
            float a = i * 0.87f;
            Vector2 p = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f) * (6f + t * 22f) * _strength;
            DrawCircle(p, (3f + t * 4f) * fade, new Color(0.58f, 0.52f, 0.44f, fade * 0.55f));
        }
    }
}

/// <summary>逻辑层 Projectile 的可见伴生节点：每帧只接收投影位置，不参与射线或命中。</summary>
public sealed partial class ProjectileVfx : Node2D
{
    private ProjectileVfxKind _kind;
    private Vector2 _dir = Vector2.Right;
    private float _pulse;

    public static ProjectileVfx? Spawn(Node2D? layer, ProjectileVfxKind kind, Vector2 cartPosition, Vector2 cartDirection)
    {
        if (layer is null || !GodotObject.IsInstanceValid(layer)) return null;
        var visual = new ProjectileVfx { _kind = kind, ZIndex = 100 };
        layer.AddChild(visual);
        visual.UpdateCartesian(cartPosition, cartDirection);
        return visual;
    }

    public void UpdateCartesian(Vector2 cartPosition, Vector2 cartDirection)
    {
        Position = Iso.Project(cartPosition);
        Vector2 projected = Iso.Project(cartDirection);
        _dir = projected == Vector2.Zero ? Vector2.Right : projected.Normalized();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _pulse += (float)delta;
        if (_kind is ProjectileVfxKind.Arrow or ProjectileVfxKind.Bolt)
            QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 p = new(-_dir.Y, _dir.X);
        switch (_kind)
        {
            case ProjectileVfxKind.Bullet:
                DrawLine(-_dir * 16f, _dir * 3f, new Color(1f, 0.72f, 0.22f, 0.38f), 3f);
                DrawLine(-_dir * 8f, _dir * 2f, new Color(1f, 0.95f, 0.62f), 2f);
                DrawCircle(Vector2.Zero, 2.2f, new Color(1f, 0.98f, 0.78f));
                break;
            case ProjectileVfxKind.Pellet:
                DrawLine(-_dir * 7f, _dir * 2f, new Color(1f, 0.68f, 0.22f, 0.65f), 1.6f);
                DrawCircle(Vector2.Zero, 1.6f, new Color(1f, 0.90f, 0.55f));
                break;
            case ProjectileVfxKind.Arrow:
            {
                float bob = Mathf.Sin(_pulse * 18f) * 0.5f;
                DrawLine(-_dir * 12f + p * bob, _dir * 7f + p * bob, new Color(0.46f, 0.28f, 0.12f), 2f);
                DrawLine(_dir * 7f, _dir * 11f, new Color(0.72f, 0.75f, 0.76f), 2f);
                DrawLine(-_dir * 10f, -_dir * 13f + p * 3f, new Color(0.68f, 0.18f, 0.12f), 1.5f);
                DrawLine(-_dir * 10f, -_dir * 13f - p * 3f, new Color(0.68f, 0.18f, 0.12f), 1.5f);
                break;
            }
            case ProjectileVfxKind.Bolt:
                DrawLine(-_dir * 8f, _dir * 7f, new Color(0.34f, 0.22f, 0.12f), 3f);
                DrawLine(_dir * 7f, _dir * 10f, new Color(0.78f, 0.80f, 0.82f), 2.5f);
                DrawLine(-_dir * 7f - p * 3f, -_dir * 7f + p * 3f, new Color(0.62f, 0.18f, 0.10f), 2f);
                break;
        }
    }
}
