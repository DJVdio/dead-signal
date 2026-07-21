using System;
using System.Collections.Generic;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 一名 <see cref="Actor"/> 的等距视觉：正式 8 方向像素图集 + 运行时状态叠加。
/// 挂在 iso_layer（YSortEnabled）下，每帧把 actor 的 cartesian 逻辑位置投到 iso 屏幕坐标
/// （<c>position = Iso.Project(actor.GlobalPosition)</c>，脚点=原点=YSort 深度锚），并读 actor 暴露的
/// 数据自绘。物理/寻路仍在 Actor（cartesian top-down），此处只表现——伪等距解耦。
///
/// 具名角色按 DisplayName 选 authored 图集；布鲁斯使用专属德牧图集；其余幸存者、劫掠者、丧尸和犬类
/// 使用泛用图集。朝向先把 cartesian 面朝方向经 <c>Iso.Project</c> 转到 iso 屏幕空间，再量化为八方向。
/// 阵营环、选中环、真实持械、血条、受击闪烁/抖动仍由运行时状态叠加；素材缺失时回退旧矢量绘制。
/// </summary>
public sealed partial class ActorSprite : Node2D
{
    private const string ActorAtlasPath = "res://assets/world/actor-directions.png";
    private const string NamedActorAtlasAPath = "res://assets/world/named-actors-a.png";
    private const string NamedActorAtlasBPath = "res://assets/world/named-actors-b.png";
    private const string BruceAtlasPath = "res://assets/world/bruce-directions.png";
    private static Texture2D? _actorAtlas;
    private static Texture2D? _namedActorAtlasA;
    private static Texture2D? _namedActorAtlasB;
    private static Texture2D? _bruceAtlas;
    private static readonly Dictionary<string, Texture2D?> EquipmentAtlases = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Texture2D?> AnimationAtlases = new(StringComparer.Ordinal);

    private Actor _actor = null!;
    private bool _bound;
    private float _drawAngle;          // 当前绘制朝向（iso 屏幕弧度），平滑逼近目标
    private bool _angleInit;

    // ---- 重绘脏标记：_Draw 只随 _drawAngle + 这组离散态变化，静止帧跳过 QueueRedraw 省重绘命令 ----
    // （Position 抖动/Modulate 闪色由引擎变换与调色处理，不进 _Draw，故不触发重绘。）
    private bool _drawInit;
    private float _drawnAngle;
    private float _drawnHealth = -1f;
    private bool _drawnSelected;
    private int _drawnRoleKey = -1;
    private bool _drawnSleeping;
    private Color _drawnTint;
    private float _drawnRadius = -1f;
    private Faction _drawnFaction = (Faction)(-1);
    private int _drawnEquipmentHash;
    private ActorAnimationState _drawnAnimationState = (ActorAnimationState)(-1);

    private const float TurnRate = 16f; // 朝向平滑速率（越大越跟手）

    // ---- 三阵营描边（用户拍板：己方白 / 中立蓝 / 敌对红）----
    // 视角固定为玩家营地（Survivor）。敌对与否一律走 Factions.IsHostile（勿散写 faction 相等比较），
    // 中立=非己方且不敌对——神秘商人等未来非敌对第三方挂对应阵营后按此自动变蓝，绘制层不特判。
    // 实现走 _Draw 底色环（复用选中环范式），刻意不用 AddOutline 管线（headless 挖洞失效的既往坑）。
    private const Faction PlayerFaction = Faction.Survivor;
    private static readonly Color FactionSelf = new(0.95f, 0.97f, 1f, 0.95f);     // 己方 白
    private static readonly Color FactionNeutral = new(0.30f, 0.62f, 1f, 0.95f);  // 中立 蓝
    private static readonly Color FactionHostile = new(1f, 0.32f, 0.28f, 0.95f);  // 敌对 红

    // ---- 受击表现（Flash 闪色 / Shake 抖动，由 CombatFeed 订阅驱动）----
    private bool _subscribed;
    private readonly RandomNumberGenerator _rng = new();
    private float _flashTime;           // 剩余闪色时长
    private float _flashDur;            // 本次闪色总时长
    private float _flashPeak;           // 闪色峰值强度 0..1
    private Color _flashColor = Colors.White;
    private float _shakeTime;           // 剩余抖动时长
    private float _shakeDur;            // 本次抖动总时长
    private float _shakeAmp;            // 抖动峰值幅度（iso 屏幕像素）

    // ---- 统一动作状态（身体、穿戴、手持物共用同一变换，纸娃娃不会与角色脱节）----
    private double _animationClock;
    private ulong _lastAttackSequence;
    private float _attackAnimTime;
    private float _attackAnimDuration = 0.22f;
    private WeaponAttackAnimation _attackAnimation = WeaponAttackAnimation.Unarmed;
    private float _hitPoseTime;
    private ActorAnimationState _animationState;

    // ---- 放逐淡出（modulate alpha→0 后自毁；克莉丝汀放逐时她仍 Alive，故不能靠 !Alive 自毁）----
    private bool _fading;
    private float _fadeTime;            // 剩余淡出时长
    private float _fadeDur;             // 本次淡出总时长

    // ---- 脚本 CG 演出接管（CampMain.CinematicCg）----
    // 为 true 时 _Process 整段早退：不再 SyncToActor / 重写 Modulate / 跑抖动闪色，
    // 由 CG 逐帧直接写本节点的 Position/Scale/Modulate/Visible（真实时基，TimeScale=0 下也能动）。
    // 关键：TimeScale=0 时本类 _Process 仍被调用但 delta=0，若不接管则它每帧把 Modulate 刷回 White、
    // 把 Position 拉回 SyncToActor，会与 CG 的直接写入打架。默认 false ⇒ 非 CG 路径零副作用。
    private bool _cinematic;

    /// <summary>本 sprite 当前绑定的 Actor（未绑定返回 null）。供 CampMain 放逐时按 actor 反查其 sprite。</summary>
    public Actor? BoundActor => _bound ? _actor : null;

    /// <summary>进入脚本 CG 演出：让 _Process 让位，CG 独占本 sprite 的 Transform/Modulate/Visible。</summary>
    public void EnterCinematic() => _cinematic = true;

    /// <summary>退出脚本 CG 演出：交还 _Process（下一帧起恢复常规同步/受击表现）。</summary>
    public void ExitCinematic() => _cinematic = false;

    /// <summary>
    /// 放逐淡出：接管 modulate，alpha 在 <paramref name="seconds"/> 内补间到 0 后 QueueFree 自身。
    /// 走独立的 _Process 分支——不受"每帧重写 Modulate"与"!Alive 即自毁"打断（放逐时 actor 仍 Alive）。
    /// 淡出期间仍同步脚点+重绘，让她边走出边淡去。
    /// </summary>
    public void FadeOutAndFree(float seconds)
    {
        _fadeDur = Mathf.Max(0.05f, seconds);
        _fadeTime = _fadeDur;
        _fading = true;
    }

    /// <summary>绑定所表现的 Actor，并把绘制朝向初始化到其当前朝向（避免出生瞬间甩头）。</summary>
    public void Bind(Actor actor)
    {
        _actor = actor;
        _bound = true;
        ZIndex = 0;
        TextureFilter = TextureFilterEnum.Nearest;
        _actorAtlas ??= GD.Load<Texture2D>(ActorAtlasPath);
        _namedActorAtlasA ??= GD.Load<Texture2D>(NamedActorAtlasAPath);
        _namedActorAtlasB ??= GD.Load<Texture2D>(NamedActorAtlasBPath);
        _bruceAtlas ??= GD.Load<Texture2D>(BruceAtlasPath);
        _lastAttackSequence = actor.VisualAttackSequence;
        SyncToActor();
        QueueRedraw();

        // 订阅受击总线：只处理"自己是承伤方"的命中。总线是 static event——
        // 必须在 _ExitTree 退订（E 地基硬约定），否则悬挂已 QueueFree 的死节点。
        if (!_subscribed)
        {
            CombatFeed.Published += OnCombatEvent;
            _subscribed = true;
        }
    }

    public override void _ExitTree()
    {
        if (_subscribed)
        {
            CombatFeed.Published -= OnCombatEvent;
            _subscribed = false;
        }
    }

    /// <summary>受击闪色：modulate 向 <paramref name="color"/> 偏移、按 <paramref name="peak"/> 强度，随后 tween 恢复。</summary>
    public void Flash(Color color, float peak, float duration = 0.32f)
    {
        _flashColor = color;
        _flashPeak = Mathf.Clamp(peak, 0f, 1f);
        _flashDur = duration;
        _flashTime = duration;
    }

    /// <summary>受击抖动：精灵短促抖动 <paramref name="amplitude"/> 像素，衰减回原位。</summary>
    public void Shake(float amplitude, float duration = 0.22f)
    {
        _shakeAmp = amplitude;
        _shakeDur = duration;
        _shakeTime = duration;
    }

    /// <summary>
    /// 受击总线回调：仅当本 sprite 所属 Actor 是承伤方时反馈。命中越重（伤害大/断肢）闪抖越猛、
    /// 溅血越浓；被甲挡下走轻微中性反馈且不溅血。承伤方本帧可能已致死——用 IsInstanceValid 守一手。
    /// </summary>
    private void OnCombatEvent(CombatFeed.Event e)
    {
        if (!_bound || !GodotObject.IsInstanceValid(_actor) || e.Target != _actor)
        {
            return;
        }

        AttackOutcome hit = e.Hit;
        Node parent = GetParent();

        if (hit.Blocked || hit.Damage <= 0)
        {
            // 被甲挡下：偏白的清脆"叮"闪 + 极轻抖，不溅血。
            Flash(new Color(0.92f, 0.96f, 1f), 0.45f, 0.18f);
            Shake(2f, 0.14f);
            _hitPoseTime = 0.14f;
            CombatVfxBurst.SpawnImpact(parent as Node2D,
                Position + new Vector2(0f, -_actor.Radius * 2.45f),
                ImpactVfxKind.Armor, 0.65f);
            return;
        }

        // 伤害强度归一（拟定待调）：常规伤害约 0~30 铺满 0..1，断肢直接拉满。
        float sev = Mathf.Clamp((float)(hit.Damage / 30.0), 0f, 1f);
        if (hit.Severed)
        {
            sev = 1f;
        }

        // 断肢=偏白骨裂闪，普通=血红闪；强度随 sev。
        Color flashCol = hit.Severed ? new Color(1f, 0.9f, 0.9f) : new Color(1f, 0.25f, 0.22f);
        Flash(flashCol, 0.45f + 0.55f * sev);
        Shake(3f + 9f * sev);
        _hitPoseTime = 0.22f;
        CombatVfxBurst.SpawnImpact(parent as Node2D,
            Position + new Vector2(0f, -_actor.Radius * 2.45f),
            CombatVfxCatalog.ImpactFor(hit), 0.65f + sev * 0.75f);

        // 溅血：脚点（本 sprite 的 node 位置）落血贴花；断肢/大流血更浓（heavy）。
        if (parent != null)
        {
            float bloodSev = hit.Bled ? Mathf.Max(sev, 0.6f) : sev;
            BloodDecal.Spawn(parent, Position, bloodSev, hit.Severed || hit.Bled);
        }
    }

    public override void _Process(double delta)
    {
        if (!_bound)
        {
            return;
        }

        // 脚本 CG 演出接管：整段让位，CG 逐帧直接驱动本节点（真实时基）。放最前，优先于淡出/自毁分支。
        if (_cinematic)
        {
            return;
        }

        // 放逐淡出：接管 modulate 直至 alpha→0，然后自毁（不走下方 !Alive 自毁）。
        if (_fading)
        {
            _fadeTime -= (float)delta;
            if (GodotObject.IsInstanceValid(_actor))
            {
                SyncToActor();          // 边淡边走出
            }
            float a = Mathf.Clamp(_fadeTime / _fadeDur, 0f, 1f);
            Modulate = new Color(1f, 1f, 1f, a);
            QueueRedraw();
            if (_fadeTime <= 0f)
            {
                QueueFree();
            }
            return;
        }

        // Actor 死亡即 QueueFree 自身，或将被回收——同步销毁视觉，避免悬挂。
        if (!GodotObject.IsInstanceValid(_actor) || !_actor.Alive)
        {
            QueueFree();
            return;
        }

        SyncToActor();

        if (_actor.VisualAttackSequence != _lastAttackSequence)
        {
            _lastAttackSequence = _actor.VisualAttackSequence;
            _attackAnimation = _actor.VisualAttackKind;
            _attackAnimDuration = Mathf.Max(0.08f, _actor.VisualAttackDurationSeconds);
            _attackAnimTime = _attackAnimDuration;
        }
        if (_attackAnimTime > 0f) _attackAnimTime -= (float)delta;
        if (_hitPoseTime > 0f) _hitPoseTime -= (float)delta;

        _animationState = ResolveAnimationState();
        if (_animationState != ActorAnimationState.Idle)
            _animationClock += delta;

        // 目标朝向：cartesian 面朝向量经 iso 投影（线性、原点→原点）后取屏幕角，再指数平滑。
        Vector2 fScreen = Iso.Project(Vector2.FromAngle(_actor.FacingAngle));
        if (fScreen != Vector2.Zero)
        {
            float target = fScreen.Angle();
            if (!_angleInit)
            {
                _drawAngle = target;
                _angleInit = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-TurnRate * (float)delta); // 帧率无关
                _drawAngle = Mathf.LerpAngle(_drawAngle, target, t);
            }
        }

        // 受击抖动：在基准脚点位置上叠加衰减随机偏移，衰减尽头精确回原位。
        if (_shakeTime > 0f)
        {
            _shakeTime -= (float)delta;
            float k = Mathf.Clamp(_shakeTime / _shakeDur, 0f, 1f);
            Position += new Vector2(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)) * (_shakeAmp * k);
        }

        RedrawIfChanged();

        // 基准染色：睡眠半透，其余全白；受击闪色在其上叠加（向闪色 Lerp，随时间衰减回基准）。
        Color baseMod = _actor is Pawn { Role: PawnRole.Sleeping }
            ? new Color(1, 1, 1, 0.35f)
            : Colors.White;
        if (_flashTime > 0f)
        {
            _flashTime -= (float)delta;
            float k = Mathf.Clamp(_flashTime / _flashDur, 0f, 1f) * _flashPeak;
            baseMod = baseMod.Lerp(_flashColor, k);
        }
        Modulate = baseMod;
    }

    private void SyncToActor() => Position = Iso.Project(_actor.GlobalPosition);

    private ActorAnimationState ResolveAnimationState()
    {
        if (_actor is not Pawn pawn)
        {
            return ActorAnimationCatalog.Resolve(
                _actor.Velocity.LengthSquared() > 4f,
                _attackAnimTime > 0f,
                _hitPoseTime > 0f,
                null, false, false, PawnVisualActivity.None);
        }

        bool stationing = pawn.Role == PawnRole.Producing
            ? pawn.ProducingStationing
            : pawn.Stationing;
        return ActorAnimationCatalog.Resolve(
            pawn.Velocity.LengthSquared() > 4f,
            _attackAnimTime > 0f,
            _hitPoseTime > 0f,
            pawn.Role,
            stationing,
            pawn.ReadingSeat.HasValue,
            pawn.VisualActivity);
    }

    // 仅在 _Draw 的任一输入变化时请求重绘：朝向平滑期间每帧微动→持续重绘，settle 后停；
    // 血量/选中/角色/睡眠/染色/半径任一变化也重绘。静止且无状态变更的帧完全跳过。
    private void RedrawIfChanged()
    {
        float health = _actor.HealthFraction;
        bool selected = _actor.Selected;
        bool sleeping = _actor is Pawn { SleepingVisual: true };
        int roleKey = _actor is Pawn pw ? (int)pw.Role : -1;
        Color tint = _actor.BodyTint;
        float radius = _actor.Radius;
        Faction faction = _actor.Faction;
        int equipmentHash = EquipmentStateHash();

        if (_drawInit
            && _animationState == ActorAnimationState.Idle
            && _drawnAnimationState == _animationState
            && Mathf.Abs(_drawAngle - _drawnAngle) <= 0.0005f
            && health == _drawnHealth
            && selected == _drawnSelected
            && sleeping == _drawnSleeping
            && roleKey == _drawnRoleKey
            && tint == _drawnTint
            && radius == _drawnRadius
            && faction == _drawnFaction
            && equipmentHash == _drawnEquipmentHash)
        {
            return;
        }

        _drawInit = true;
        _drawnAngle = _drawAngle;
        _drawnHealth = health;
        _drawnSelected = selected;
        _drawnSleeping = sleeping;
        _drawnRoleKey = roleKey;
        _drawnTint = tint;
        _drawnRadius = radius;
        _drawnFaction = faction;
        _drawnEquipmentHash = equipmentHash;
        _drawnAnimationState = _animationState;
        QueueRedraw();
    }

    /// <summary>穿脱/换手/光源耗尽都必须触发重绘。</summary>
    private int EquipmentStateHash()
    {
        var hash = new HashCode();
        if (_actor is Pawn pawn)
        {
            hash.Add(pawn.WeaponInHand(Hand.Left)?.Name);
            hash.Add(pawn.WeaponInHand(Hand.Right)?.Name);
            hash.Add(pawn.Grip);
            hash.Add(pawn.HeldLight.Held?.Key);
            hash.Add(pawn.HeldLight.HandUsed);
            hash.Add(pawn.HeldLight.IsLit);
            foreach (EquipSlot slot in Enum.GetValues<EquipSlot>())
                hash.Add(pawn.ApparelAt(slot));
        }
        else if (_actor is Dog dog)
        {
            foreach (DogEquipSlot slot in Enum.GetValues<DogEquipSlot>())
                hash.Add(dog.Apparel.ItemAt(slot));
        }
        return hash.ToHashCode();
    }

    public override void _Draw()
    {
        if (!_bound || !GodotObject.IsInstanceValid(_actor))
        {
            return;
        }

        float r = _actor.Radius;
        Vector2 f = Vector2.FromAngle(_drawAngle);
        var p = new Vector2(-f.Y, f.X);

        Color tint = _actor.BodyTint;

        float headCY = -r * 2.78f;
        float headR = r * 0.62f;

        DrawColoredPolygon(Ellipse(new Vector2(0, -r * 0.12f), r * 1.2f, r * 0.55f), new Color(0, 0, 0, 0.28f));

        // 阵营描边：脚下底色环（己方白/中立蓝/敌对红），始终绘制。
        Vector2[] facRing = Ellipse(new Vector2(0, -r * 0.12f), r * 1.30f, r * 0.58f, 28);
        DrawPolyline(Close(facRing), FactionOutlineColor(_actor.Faction), 2.5f, true);

        // 选中态与阵营描边并存（保守方案，标待确认）：选中绿环画在阵营环之外形成双环，
        // 层级上选中高亮压过阵营边（外层更醒目），阵营色仍以内环恒示。
        if (_actor.Selected)
        {
            Vector2[] ring = Ellipse(new Vector2(0, -r * 0.12f), r * 1.52f, r * 0.70f, 28);
            DrawPolyline(Close(ring), new Color(0.4f, 1f, 0.55f), 2f, true);
        }

        int directionColumn = DirectionColumn();
        PoseTransform pose = AnimationTransform(r);
        DrawSetTransform(pose.Offset, pose.Rotation, pose.Scale);

        DrawHeldEquipmentPass(r, directionColumn, behindBody: true);

        bool animatedFrame = DrawAnimationFrame(r, tint);
        bool atlasStanding = false;
        if (animatedFrame || DrawAtlasStanding(r, tint))
        {
            atlasStanding = true;
        }
        else
        {
            DrawStanding(r, tint, f, p);
        }

        if (atlasStanding)
            DrawWornEquipment(r, directionColumn);

        DrawHeldEquipmentPass(r, directionColumn, behindBody: false);
        if (!animatedFrame)
            DrawActivityProp(r);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

        if (_animationState == ActorAnimationState.Lie)
            DrawSleepBreath(r);

        float barW = r * 2.4f;
        float barH = 4f;
        float barY = _animationState == ActorAnimationState.Lie
            ? -r * 2.35f
            : atlasStanding ? -r * 5.15f - 6f : headCY - headR - 8f;
        var barPos = new Vector2(-barW / 2f, barY);
        float frac = _actor.HealthFraction;
        DrawRect(new Rect2(barPos, new Vector2(barW, barH)), new Color(0.1f, 0.1f, 0.1f, 0.85f));
        Color hp = frac > 0.5f ? new Color(0.4f, 0.85f, 0.4f)
            : frac > 0.25f ? new Color(0.9f, 0.8f, 0.3f)
            : new Color(0.9f, 0.35f, 0.3f);
        DrawRect(new Rect2(barPos, new Vector2(barW * frac, barH)), hp);

        if (_actor is Pawn rp && rp.Role != PawnRole.Idle)
        {
            Color dot = rp.Role switch
            {
                PawnRole.Sleeping => new Color(0.3f, 0.5f, 0.9f, 0.7f),
                PawnRole.Guard => new Color(0.9f, 0.7f, 0.2f, 0.8f),
                PawnRole.Expedition => new Color(0.3f, 0.8f, 0.4f, 0.8f),
                _ => Colors.Transparent,
            };
            DrawCircle(new Vector2(r * 1.5f, barY + 2f), 3f, dot);
        }
    }

    private readonly record struct PoseTransform(Vector2 Offset, float Rotation, Vector2 Scale);

    /// <summary>只变换角色本体及全部纸娃娃层；脚下环和血条保持稳定、可读。</summary>
    private PoseTransform AnimationTransform(float r)
    {
        float loop = (float)_animationClock;
        Vector2 facing = Vector2.FromAngle(_drawAngle);
        float attackProgress = _attackAnimDuration <= 0f
            ? 1f
            : 1f - Mathf.Clamp(_attackAnimTime / _attackAnimDuration, 0f, 1f);
        float pulse = Mathf.Sin(attackProgress * Mathf.Pi);
        float gait = Mathf.Sin(loop * 9f);

        return _animationState switch
        {
            ActorAnimationState.Walk => new PoseTransform(
                new Vector2(gait * r * 0.10f, -Mathf.Abs(gait) * r * 0.13f),
                gait * 0.035f,
                new Vector2(1f + Mathf.Abs(gait) * 0.025f, 1f - Mathf.Abs(gait) * 0.025f)),
            ActorAnimationState.Hit => new PoseTransform(-facing * r * 0.22f, -Mathf.Sign(facing.X) * 0.13f, new Vector2(1.04f, 0.94f)),
            ActorAnimationState.Attack => AttackTransform(r, facing, pulse, attackProgress),
            ActorAnimationState.Work => new PoseTransform(
                new Vector2(0f, Mathf.Sin(loop * 6f) * r * 0.08f),
                Mathf.Sin(loop * 3f) * 0.055f,
                new Vector2(1f, 1f - Mathf.Max(0f, Mathf.Sin(loop * 6f)) * 0.035f)),
            ActorAnimationState.ReadStanding or ActorAnimationState.ReadSeated or ActorAnimationState.Sit
                => new PoseTransform(Vector2.Zero, 0f, Vector2.One),
            ActorAnimationState.Lie => new PoseTransform(
                new Vector2(0f, Mathf.Sin(loop * 1.7f) * r * 0.025f), 0f,
                new Vector2(1f + Mathf.Sin(loop * 1.7f) * 0.006f, 1f)),
            _ => new PoseTransform(Vector2.Zero, 0f, Vector2.One),
        };
    }

    /// <summary>从每名角色/泛用种类的 12 列逐帧图集画当前动作，不再用站立帧冒充坐卧和工作。</summary>
    private bool DrawAnimationFrame(float r, Color tint)
    {
        string kind = _actor switch
        {
            Raider => "raider",
            Zombie => "zombie",
            Dog => "dog",
            _ => "survivor",
        };
        string? displayName = _actor switch
        {
            Pawn pawn => pawn.DisplayName,
            Raider raider => raider.DisplayName,
            Dog dog => dog.DisplayName,
            _ => null,
        };
        string path = ActorFrameCatalog.PathFor(displayName, kind);
        if (!AnimationAtlases.TryGetValue(path, out Texture2D? atlas))
        {
            atlas = GD.Load<Texture2D>(path);
            AnimationAtlases[path] = atlas;
        }
        if (atlas is null) return false;

        float progress = _attackAnimDuration <= 0f
            ? 1f
            : 1f - Mathf.Clamp(_attackAnimTime / _attackAnimDuration, 0f, 1f);
        int col = ActorFrameCatalog.ColumnFor(_animationState, _animationClock, progress);
        int row = ActorFrameCatalog.RowForDirection(DirectionColumn());
        float cellW = atlas.GetWidth() / (float)ActorFrameCatalog.Columns;
        float cellH = atlas.GetHeight() / (float)ActorFrameCatalog.Rows;
        var source = new Rect2(col * cellW, row * cellH, cellW, cellH);

        float height = r * (_actor is Dog ? 5.0f : 5.5f);
        float width = height * (cellW / cellH);
        var destination = new Rect2(-width / 2f, -height, width, height);
        Color authoredTint = Colors.White.Lerp(tint.Lightened(0.55f), 0.05f);
        DrawTextureRectRegion(atlas, destination, source, authoredTint);
        return true;
    }

    private PoseTransform AttackTransform(float r, Vector2 facing, float pulse, float progress)
    {
        float side = Mathf.Cos(_drawAngle) >= 0f ? 1f : -1f;
        return _attackAnimation switch
        {
            WeaponAttackAnimation.KnifeThrust => new PoseTransform(facing * r * 0.30f * pulse, -side * 0.05f * pulse, new Vector2(1f, 0.97f)),
            WeaponAttackAnimation.PolearmThrust => new PoseTransform(facing * r * 0.40f * pulse, -side * 0.035f * pulse, new Vector2(1.03f, 0.95f)),
            WeaponAttackAnimation.SwordSlash => new PoseTransform(facing * r * 0.12f * pulse, side * (progress - 0.5f) * 0.34f * pulse, Vector2.One),
            WeaponAttackAnimation.HeavySwing => new PoseTransform(facing * r * 0.10f * pulse, side * (progress - 0.45f) * 0.55f * pulse, new Vector2(1.04f, 0.94f)),
            WeaponAttackAnimation.PistolRecoil => new PoseTransform(-facing * r * 0.20f * pulse, side * 0.045f * pulse, new Vector2(1.02f, 0.98f)),
            WeaponAttackAnimation.LongGunRecoil => new PoseTransform(-facing * r * 0.32f * pulse, side * 0.035f * pulse, new Vector2(1.04f, 0.96f)),
            WeaponAttackAnimation.BowShot => new PoseTransform(-facing * r * 0.12f * pulse, -side * 0.08f * pulse, new Vector2(1f + pulse * 0.035f, 1f)),
            WeaponAttackAnimation.CrossbowRecoil => new PoseTransform(-facing * r * 0.25f * pulse, side * 0.04f * pulse, new Vector2(1.03f, 0.97f)),
            WeaponAttackAnimation.Bite => new PoseTransform(facing * r * 0.34f * pulse, side * 0.06f * pulse, new Vector2(1.06f, 0.93f)),
            _ => new PoseTransform(facing * r * 0.25f * pulse, side * 0.08f * pulse, new Vector2(1.03f, 0.96f)),
        };
    }

    /// <summary>活动道具与动作同步绘制，避免只靠状态点表达“工作/读书/吃饭”。</summary>
    private void DrawActivityProp(float r)
    {
        float loop = (float)_animationClock;
        if (_animationState is ActorAnimationState.ReadStanding or ActorAnimationState.ReadSeated)
        {
            float y = _animationState == ActorAnimationState.ReadSeated ? -r * 2.15f : -r * 2.35f;
            Vector2 c = new(0f, y);
            Color paper = new(0.82f, 0.72f, 0.52f);
            Color edge = new(0.18f, 0.13f, 0.10f);
            Vector2[] left =
            {
                c,
                c + new Vector2(-r * 0.78f, -r * 0.20f),
                c + new Vector2(-r * 0.72f, r * 0.48f),
                c + new Vector2(0f, r * 0.28f),
            };
            Vector2[] right =
            {
                c,
                c + new Vector2(r * 0.78f, -r * 0.20f),
                c + new Vector2(r * 0.72f, r * 0.48f),
                c + new Vector2(0f, r * 0.28f),
            };
            DrawColoredPolygon(left, paper);
            DrawColoredPolygon(right, paper.Lightened(0.10f));
            DrawPolyline(Close(left), edge, 1.5f);
            DrawPolyline(Close(right), edge, 1.5f);
            if (Mathf.PosMod(loop, 2.8f) > 2.45f)
                DrawLine(c, c + new Vector2(r * 0.55f, -r * 0.30f), new Color(0.95f, 0.88f, 0.68f), 2f);
        }
        else if (_animationState == ActorAnimationState.Work)
        {
            float swing = Mathf.Sin(loop * 6f);
            Vector2 grip = new(r * 0.48f, -r * 2.25f);
            Vector2 tip = grip + Vector2.FromAngle(-1.0f + swing * 0.65f) * r * 1.25f;
            DrawLine(grip, tip, new Color(0.40f, 0.24f, 0.12f), r * 0.16f);
            DrawLine(tip + new Vector2(-r * 0.28f, 0), tip + new Vector2(r * 0.28f, 0), new Color(0.58f, 0.62f, 0.65f), r * 0.24f);
            if (swing > 0.88f)
            {
                Color spark = new(1f, 0.72f, 0.20f, 0.9f);
                DrawLine(tip, tip + new Vector2(r * 0.38f, -r * 0.18f), spark, 2f);
                DrawLine(tip, tip + new Vector2(-r * 0.30f, -r * 0.28f), spark, 2f);
            }
        }
        else if (_animationState == ActorAnimationState.Sit)
        {
            Vector2 c = new(0f, -r * 1.90f);
            DrawColoredPolygon(Ellipse(c, r * 0.72f, r * 0.28f, 18), new Color(0.50f, 0.32f, 0.18f));
            DrawArc(c, r * 0.55f, 0f, Mathf.Pi, 12, new Color(0.82f, 0.70f, 0.45f), 2f);
        }
    }

    private void DrawSleepBreath(float r)
    {
        float a = 0.35f + 0.35f * (0.5f + 0.5f * Mathf.Sin((float)_animationClock * 1.7f));
        Color c = new(0.72f, 0.82f, 1f, a);
        DrawArc(new Vector2(r * 1.8f, -r * 1.25f), r * 0.38f, -1.4f, 0.55f, 10, c, 2f);
        DrawArc(new Vector2(r * 2.35f, -r * 1.70f), r * 0.27f, -1.4f, 0.55f, 10, c, 2f);
    }

    /// <summary>正式 8 方向角色图集。具名角色优先，失败时回退泛用图集，再失败才回退旧矢量人形。</summary>
    private bool DrawAtlasStanding(float r, Color tint)
    {
        Texture2D? atlas;
        int row;
        bool authored;
        if (_actor is Pawn pawn && TryNamedPawnAtlas(pawn.DisplayName, out atlas, out row))
        {
            authored = true;
        }
        else if (_actor is Dog { DisplayName: "布鲁斯" } && _bruceAtlas is not null)
        {
            atlas = _bruceAtlas;
            row = 0;
            authored = true;
        }
        else
        {
            atlas = _actorAtlas;
            row = _actor switch
            {
                Raider => 1,
                Zombie => 2,
                Dog => 3,
                _ => 0,
            };
            authored = false;
        }

        if (atlas is null)
            return false;

        int col = DirectionColumn();
        const float sourceW = 64f;
        const float sourceH = 96f;
        var source = new Rect2(col * sourceW, row * sourceH, sourceW, sourceH);

        float height = r * (_actor is Dog ? 5.0f : 5.5f);
        float width = height * (sourceW / sourceH);
        var destination = new Rect2(-width / 2f, -height, width, height);
        Color authoredTint = authored ? Colors.White : Colors.White.Lerp(tint.Lightened(0.55f), 0.14f);
        DrawTextureRectRegion(atlas, destination, source, authoredTint);
        return true;
    }

    private static bool TryNamedPawnAtlas(string displayName, out Texture2D? atlas, out int row)
    {
        (atlas, row) = displayName switch
        {
            "山姆" => (_namedActorAtlasA, 0),
            "诺蒂" => (_namedActorAtlasA, 1),
            "克莉丝汀" => (_namedActorAtlasA, 2),
            "耗子" => (_namedActorAtlasA, 3),
            "道格" => (_namedActorAtlasB, 0),
            "南丁格尔" => (_namedActorAtlasB, 1),
            "皮特" => (_namedActorAtlasB, 2),
            _ => (null, -1),
        };
        return atlas is not null;
    }

    private int DirectionColumn()
    {
        float sector = Mathf.PosMod(_drawAngle - Mathf.Pi * 0.5f, Mathf.Tau) / (Mathf.Tau / 8f);
        return Mathf.RoundToInt(sector) % 8;
    }

    /// <summary>
    /// 只读 Pawn 的真实左右手与持光状态绘制。背向三方向先画、其余后画；双手同一实例只画一次，双持各画一件。
    /// 未进入本批图集的武器保留轮廓线回退，避免“有数值但手上什么都没有”的静默失败。
    /// </summary>
    private void DrawHeldEquipmentPass(float r, int directionColumn, bool behindBody)
    {
        if (_actor is not Pawn pawn
            || EquipmentVisualCatalog.DrawHeldBehindBody(directionColumn) != behindBody)
            return;

        Weapon? left = pawn.WeaponInHand(Hand.Left);
        Weapon? right = pawn.WeaponInHand(Hand.Right);
        if (pawn.Grip == GripMode.TwoHanded && pawn.PrimaryWeapon is Weapon twoHanded)
        {
            DrawHeldWeaponVisual(twoHanded, Hand.Right, r, directionColumn, twoHandGrip: true);
        }
        else
        {
            if (left is not null)
                DrawHeldWeaponVisual(left, Hand.Left, r, directionColumn, twoHandGrip: false);
            if (right is not null)
                DrawHeldWeaponVisual(right, Hand.Right, r, directionColumn, twoHandGrip: false);
        }

        if (pawn.HeldLight.HandUsed is Hand lightHand
            && pawn.HeldLight.Held is LightProfile light)
        {
            // 熄灭火把仍占手，但不能继续显示火焰；以无焰木杆回退。手电耗尽只灭光束，实体不变。
            if (light.Key == LightSource.TorchKey && !pawn.HeldLight.IsLit)
                DrawFallbackHeldItem(r, lightHand, ranged: false, twoHandGrip: false, new Color(0.34f, 0.20f, 0.10f));
            else if (EquipmentVisualCatalog.ResolveLight(light.Key) is not { } lightVisual
                     || !DrawEquipmentCell(lightVisual, lightHand, r, directionColumn, twoHandGrip: false))
                DrawFallbackHeldItem(r, lightHand, ranged: false, twoHandGrip: false, new Color(0.70f, 0.58f, 0.32f));
        }
    }

    private void DrawHeldWeaponVisual(Weapon weapon, Hand hand, float r, int directionColumn, bool twoHandGrip)
    {
        if (EquipmentVisualCatalog.ResolveWeapon(weapon.Name) is not { } visual
            || !DrawEquipmentCell(visual, hand, r, directionColumn, twoHandGrip))
            DrawFallbackHeldItem(r, hand, weapon.IsRanged, twoHandGrip,
                weapon.IsRanged ? new Color(0.18f, 0.20f, 0.23f) : new Color(0.72f, 0.75f, 0.82f));
    }

    private bool DrawEquipmentCell(EquipmentVisualDef visual, Hand hand, float r, int directionColumn, bool twoHandGrip)
    {
        Texture2D? atlas = EquipmentAtlas(visual.AtlasPath);
        if (atlas is null)
            return false;

        var source = new Rect2(
            directionColumn * visual.CellWidth,
            visual.AtlasRow * visual.CellHeight,
            visual.CellWidth,
            visual.CellHeight);
        Vector2 f = Vector2.FromAngle(_drawAngle);
        var p = new Vector2(-f.Y, f.X);
        float handSide = hand == Hand.Left ? -1f : 1f;
        Vector2 center = new Vector2(0f, -r * 2.20f) + f * r * 0.22f;
        if (!twoHandGrip)
            center += p * handSide * r * 0.50f;

        float size = r * 4.9f * visual.DisplayScale;
        var destination = new Rect2(center - new Vector2(size, size) / 2f, new Vector2(size, size));
        DrawTextureRectRegion(atlas, destination, source);
        return true;
    }

    private static Texture2D? EquipmentAtlas(string path)
    {
        if (!EquipmentAtlases.TryGetValue(path, out Texture2D? atlas))
        {
            atlas = GD.Load<Texture2D>(path);
            EquipmentAtlases[path] = atlas;
        }
        return atlas;
    }

    /// <summary>
    /// 只读真实穿戴槽绘制纸娃娃。多槽装备只画一次；成对手套/鞋按左右槽各画一件，单只穿戴不会冒出一整双。
    /// 面具/眼镜固定走 Face 锚点，绝不覆盖角色发型；板甲整件走 Plate 层并自带腿甲，但不画手脚。
    /// </summary>
    private void DrawWornEquipment(float r, int directionColumn)
    {
        if (_actor is Pawn pawn)
        {
            PaperDollLayer[] layers =
            {
                PaperDollLayer.Skin,
                PaperDollLayer.Pants,
                PaperDollLayer.Feet,
                PaperDollLayer.Outer,
                PaperDollLayer.Plate,
                PaperDollLayer.Hands,
                PaperDollLayer.Head,
                PaperDollLayer.Eyes,
                PaperDollLayer.Face,
            };
            var drawn = new HashSet<string>(StringComparer.Ordinal);
            foreach (PaperDollLayer layer in layers)
            {
                foreach (EquipSlot slot in Enum.GetValues<EquipSlot>())
                {
                    string? item = pawn.ApparelAt(slot);
                    if (EquipmentVisualCatalog.ResolveApparel(item) is not { } visual || visual.Layer != layer)
                        continue;

                    bool sideSpecific = visual.Anchor is EquipmentVisualAnchor.Hand or EquipmentVisualAnchor.Foot;
                    if (!sideSpecific && !drawn.Add(visual.ItemKey))
                        continue;
                    DrawWornCell(visual, r, directionColumn, slot);
                }
            }
        }
        else if (_actor is Dog dog)
        {
            foreach (DogEquipSlot slot in new[] { DogEquipSlot.Body, DogEquipSlot.Head })
            {
                if (EquipmentVisualCatalog.ResolveDogApparel(dog.Apparel.ItemAt(slot)) is { } visual)
                    DrawWornCell(visual, r, directionColumn, null);
            }
        }
    }

    private void DrawWornCell(EquipmentVisualDef visual, float r, int directionColumn, EquipSlot? slot)
    {
        Texture2D? atlas = EquipmentAtlas(visual.AtlasPath);
        if (atlas is null)
            return;

        var source = new Rect2(
            directionColumn * visual.CellWidth,
            visual.AtlasRow * visual.CellHeight,
            visual.CellWidth,
            visual.CellHeight);
        Rect2 destination;
        switch (visual.Anchor)
        {
            case EquipmentVisualAnchor.Body:
            case EquipmentVisualAnchor.DogBody:
            {
                float height = r * (visual.Anchor == EquipmentVisualAnchor.DogBody ? 5.0f : 5.5f);
                float width = height * (64f / 96f);
                destination = new Rect2(-width / 2f, -height, width, height);
                break;
            }
            case EquipmentVisualAnchor.Head:
            {
                float size = r * 2.15f * visual.DisplayScale;
                Vector2 center = new(0f, -r * 4.48f);
                destination = new Rect2(center - Vector2.One * size / 2f, Vector2.One * size);
                break;
            }
            case EquipmentVisualAnchor.Face:
            {
                // 比 Head 锚点低：恐怖面具/墨镜只能盖脸，不得压住发际线与发型。
                float size = r * 1.72f * visual.DisplayScale;
                Vector2 center = new(0f, -r * 4.18f);
                destination = new Rect2(center - Vector2.One * size / 2f, Vector2.One * size);
                break;
            }
            case EquipmentVisualAnchor.Hand:
            {
                Vector2 f = Vector2.FromAngle(_drawAngle);
                Vector2 p = new(-f.Y, f.X);
                float side = slot == EquipSlot.LeftHand ? -1f : 1f;
                Vector2 center = new Vector2(0f, -r * 2.42f) + p * side * r * 0.62f + f * r * 0.10f;
                float size = r * 1.22f * visual.DisplayScale;
                destination = new Rect2(center - Vector2.One * size / 2f, Vector2.One * size);
                break;
            }
            case EquipmentVisualAnchor.Foot:
            {
                Vector2 f = Vector2.FromAngle(_drawAngle);
                Vector2 p = new(-f.Y, f.X);
                float side = slot == EquipSlot.LeftFoot ? -1f : 1f;
                Vector2 center = new Vector2(0f, -r * 0.34f) + p * side * r * 0.30f + f * r * 0.05f;
                float size = r * 1.36f * visual.DisplayScale;
                destination = new Rect2(center - Vector2.One * size / 2f, Vector2.One * size);
                break;
            }
            default:
                return;
        }
        DrawTextureRectRegion(atlas, destination, source);
    }

    private void DrawFallbackHeldItem(float r, Hand hand, bool ranged, bool twoHandGrip, Color color)
    {
        Vector2 f = Vector2.FromAngle(_drawAngle);
        var p = new Vector2(-f.Y, f.X);
        float handSide = hand == Hand.Left ? -1f : 1f;
        Vector2 grip = new Vector2(0f, -r * 2.0f) + f * r * 0.28f;
        if (!twoHandGrip)
            grip += p * handSide * r * 0.38f;
        DrawLine(grip, grip + f * r * (ranged ? 1.55f : 0.88f), color, r * (ranged ? 0.24f : 0.15f));
    }

    private void DrawStanding(float r, Color tint, Vector2 f, Vector2 p)
    {
        Color torso = tint;
        Color torsoDark = tint.Darkened(0.42f);
        Color torsoLight = tint.Lightened(0.28f);
        Color legCol = tint.Darkened(0.58f);
        Color headCol = tint.Lightened(0.40f);
        Color headShade = headCol.Darkened(0.35f);
        var outline = new Color(0.05f, 0.05f, 0.07f, 0.9f);

        float feetY = 0f;
        float hipY = -r * 1.15f;
        float torsoCY = -r * 1.72f;
        float shoulderY = -r * 2.25f;
        float headCY = -r * 2.78f;
        float headR = r * 0.62f;

        Vector2 hipL = new Vector2(0, hipY) + p * r * 0.28f;
        Vector2 hipR = new Vector2(0, hipY) - p * r * 0.28f;
        Vector2 footL = p * r * 0.42f + f * r * 0.10f + new Vector2(0, feetY);
        Vector2 footR = -p * r * 0.42f - f * r * 0.10f + new Vector2(0, feetY);
        DrawLine(hipL, footL, outline, r * 0.62f);
        DrawLine(hipR, footR, outline, r * 0.62f);
        DrawLine(hipL, footL, legCol, r * 0.42f);
        DrawLine(hipR, footR, legCol, r * 0.42f);

        Vector2 torsoC = new Vector2(0, torsoCY);
        DrawColoredPolygon(Ellipse(torsoC, r * 0.78f + 1.2f, r * 1.05f + 1.2f), outline);
        DrawColoredPolygon(Ellipse(torsoC, r * 0.78f, r * 1.05f), torso);
        DrawColoredPolygon(Ellipse(torsoC + new Vector2(0, r * 0.35f), r * 0.6f, r * 0.7f), torsoDark);
        DrawColoredPolygon(Ellipse(torsoC + f * r * 0.18f - new Vector2(0, r * 0.32f), r * 0.42f, r * 0.5f), torsoLight);

        Vector2 shoulderC = new Vector2(0, shoulderY);
        Vector2 shoulderL = shoulderC + p * r * 0.72f;
        Vector2 shoulderR = shoulderC - p * r * 0.72f;
        Vector2 handOff = new Vector2(0, r * 0.95f);
        Vector2 handL = shoulderL + handOff + f * r * 0.18f;
        Vector2 handR = shoulderR + new Vector2(0, r * 0.78f) + f * r * 0.62f;
        DrawLine(shoulderL, handL, torsoDark, r * 0.34f);
        DrawLine(shoulderR, handR, torsoDark, r * 0.34f);

        DrawLine(shoulderL, shoulderR, torsoDark, r * 0.30f);

        DrawColoredPolygon(Ellipse(new Vector2(0, headCY), headR + 1.2f, headR + 1.2f), outline);
        DrawColoredPolygon(Ellipse(new Vector2(0, headCY), headR, headR), headCol);
        DrawColoredPolygon(Ellipse(new Vector2(0, headCY) + f * headR * 0.5f, headR * 0.42f, headR * 0.42f), headShade);

        if (_actor.RangedArmed)
        {
            var gun = new Color(0.2f, 0.22f, 0.26f);
            DrawLine(handR, handR + f * r * 1.35f, gun, r * 0.26f);
        }
        else
        {
            var blade = new Color(0.72f, 0.75f, 0.82f);
            DrawLine(handR, handR + f * r * 0.82f, blade, r * 0.16f);
        }
    }

    private void DrawSleeping(float r, Color tint)
    {
        Color headCol = tint.Lightened(0.40f);
        var outline = new Color(0.05f, 0.05f, 0.07f, 0.9f);

        float bodyLen = r * 2.2f;
        float bodyH = r * 0.65f;
        Vector2 bodyC = new Vector2(0, -r * 1.1f);
        DrawColoredPolygon(Ellipse(bodyC, bodyLen / 2, bodyH), outline);
        DrawColoredPolygon(Ellipse(bodyC, bodyLen / 2 - 1, bodyH - 1), tint);

        Vector2 headC = bodyC + new Vector2(bodyLen / 2 + r * 0.25f, -r * 0.08f);
        float hr = r * 0.48f;
        DrawColoredPolygon(Ellipse(headC, hr + 1, hr + 1), outline);
        DrawColoredPolygon(Ellipse(headC, hr, hr), headCol);
    }

    /// <summary>
    /// 从阵营推导描边色（视角=玩家营地 <see cref="PlayerFaction"/>）：己方白、对己方敌对红、其余中立蓝。
    /// 敌对判定统一走 <see cref="Factions.IsHostile"/>；中立分支覆盖未来非敌对第三方（如神秘商人），无需在此特判。
    /// </summary>
    private static Color FactionOutlineColor(Faction faction)
    {
        if (faction == PlayerFaction)
        {
            return FactionSelf;
        }
        return Factions.IsHostile(PlayerFaction, faction) ? FactionHostile : FactionNeutral;
    }

    private static Vector2[] Ellipse(Vector2 c, float rx, float ry, int seg = 22)
    {
        var pts = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = Mathf.Tau * i / seg;
            pts[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return pts;
    }

    /// <summary>把多边形点集闭合（首点补到尾）用于 DrawPolyline 画成环。</summary>
    private static Vector2[] Close(Vector2[] pts)
    {
        var closed = new Vector2[pts.Length + 1];
        pts.CopyTo(closed, 0);
        closed[pts.Length] = pts[0];
        return closed;
    }
}
