using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 一名 <see cref="Actor"/> 的等距视觉：程序化（纯矢量、无素材）精致人形。
/// 挂在 iso_layer（YSortEnabled）下，每帧把 actor 的 cartesian 逻辑位置投到 iso 屏幕坐标
/// （<c>position = Iso.Project(actor.GlobalPosition)</c>，脚点=原点=YSort 深度锚），并读 actor 暴露的
/// 数据自绘。物理/寻路仍在 Actor（cartesian top-down），此处只表现——伪等距解耦。
///
/// 人形分层画：投影阴影 → 选中环 → 双腿 → 躯干（明暗）→ 双臂 → 肩线 → 头（含朝向标）→ 持械指示 → 血条。
/// 朝向为**自由角度**（用户拍板：矢量绘制、无离散帧）：把 actor 的 cartesian 面朝方向经
/// <c>Iso.Project</c> 转到 iso 屏幕空间取角，指数平滑旋转当前绘制角，肩线/头标/持械手随之转。
/// </summary>
public sealed partial class ActorSprite : Node2D
{
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

        if (_drawInit
            && Mathf.Abs(_drawAngle - _drawnAngle) <= 0.0005f
            && health == _drawnHealth
            && selected == _drawnSelected
            && sleeping == _drawnSleeping
            && roleKey == _drawnRoleKey
            && tint == _drawnTint
            && radius == _drawnRadius
            && faction == _drawnFaction)
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
        QueueRedraw();
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

        if (_actor is Pawn { SleepingVisual: true })
        {
            DrawSleeping(r, tint);
        }
        else
        {
            DrawStanding(r, tint, f, p);
        }

        float barW = r * 2.4f;
        float barH = 4f;
        var barPos = new Vector2(-barW / 2f, headCY - headR - 8f);
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
            DrawCircle(new Vector2(r * 1.5f, headCY - headR - 4f), 3f, dot);
        }
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
