using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 「望远镜瞭望尸潮」全屏演出：正式俯瞰背景 + 程序化移动尸群与双筒遮罩。
///
/// 语义：瞭望台关卡内与望远镜交互 → 弹出本演出。透过望远镜（双圆遮罩+暗角）远眺正北——
/// 地平线上黑压压的「上百万」尸潮缓缓向镜头（南）蠕动：远处压缩成一堵蠕动的暗墙，越近个体越大越疏，
/// 前景零星掉队者剪影给出尺度参照。氛围克制压抑（黑暗向有意为之）。可跳过（任意键/点击）。
///
/// 时序全走真实时钟（<see cref="Time.GetTicksMsec"/>），**不吃 <c>Engine.TimeScale</c>/暂停**——
/// 演出期间世界是否冻结都照播（<see cref="Node.ProcessModeEnum.Always"/>）。播完（或跳过）淡出→回调。
///
/// 骨架：屏幕空间 <see cref="CanvasLayer"/>（高 Layer 压一切、不随 Camera2D 移动）承载一个满屏
/// <see cref="HordeVista"/>（<see cref="Control"/>）做全部绘制/计时/输入。收尾接口 <see cref="Play"/> 的
/// <c>onFinished</c> 回调——约定：**本演出先播，播完再由调用方（loot-story）弹剧情文本+置旗标**。
/// </summary>
public sealed partial class HordeLookoutCinematic : CanvasLayer
{
    /// <summary>压在所有 HUD/模态之上（GameOver 等用默认 0/1 层，这里取高层确保盖住）。</summary>
    public const int CinematicLayer = 128;

    private Action? _onFinished;
    private bool _done;

    /// <summary>
    /// 便捷入口（仿 <see cref="GameOverPanel.Show"/>）：挂到 <paramref name="host"/> 下并开始播放，
    /// 播完（或被跳过）后自动回调 <paramref name="onFinished"/> 并自毁。返回实例（调用方一般无需持有）。
    /// </summary>
    public static HordeLookoutCinematic Show(Node host, Action onFinished)
    {
        var cinematic = new HordeLookoutCinematic();
        host.AddChild(cinematic);
        cinematic.Play(onFinished);
        return cinematic;
    }

    /// <summary>
    /// 开始播放（须已在场景树内——<see cref="Show"/> 已代为 AddChild）。演出结束或被跳过时调用一次
    /// <paramref name="onFinished"/>，随后本节点自毁。<paramref name="onFinished"/> 即「播完回调」挂点：
    /// 交给剧情文本+置旗标 HordeSighted（归 loot-story，接线见 journal 的 [HANDOFF] anim-lookout → loot-story）。
    /// </summary>
    public void Play(Action onFinished)
    {
        _onFinished = onFinished;
        Layer = CinematicLayer;
        ProcessMode = ProcessModeEnum.Always; // 世界即便暂停/TimeScale=0 也照播

        var vista = new HordeVista();
        vista.Finished += Finish;
        AddChild(vista);
    }

    /// <summary>演出收尾：仅回调一次（跳过与自然播完共用此路），回调后自毁整层。</summary>
    private void Finish()
    {
        if (_done)
        {
            return;
        }
        _done = true;

        Action? cb = _onFinished;
        _onFinished = null;
        cb?.Invoke();
        QueueFree();
    }
}

/// <summary>
/// 望远镜视野本体（满屏 <see cref="Control"/>，全部 <see cref="_Draw"/> 自绘）。由 <see cref="HordeLookoutCinematic"/>
/// 创建、驱动，结束时触发 <see cref="Finished"/>。分镜（约 11s，拟定待调）：
/// 0.0–0.5s 黑场淡入 → 全程 双圆望远镜遮罩内远眺正北，尸潮暗墙压地平线、个体缓缓南下变大、
/// 前景掉队者剪影蹒跚 + 镜头轻微漂移呼吸 → 末段 0.6s 淡出黑场（干净交棒给后续剧情面板）。
/// </summary>
public sealed partial class HordeVista : Control
{
    public const string OverviewTexturePath = "res://assets/world/cinematics/horde-overview.png";

    // —— 时长/淡入淡出（秒，拟定待调，落在派单的 8~15s 区间）——
    private const float DurationSec = 11f;
    private const float FadeInSec = 0.5f;
    private const float FadeOutSec = 0.6f;

    // —— 尸潮场规模 —— 千余个体（近横向铺开、远处压缩成暗墙）营造「上百万」观感。纯视觉。
    private const int DotCount = 1400;

    /// <summary>演出结束（自然播完或被跳过）——<see cref="HordeLookoutCinematic"/> 据此回调+自毁。</summary>
    public event Action? Finished;

    // 真实时钟（不吃 TimeScale/暂停）。
    private ulong _lastMs;
    private float _elapsed;
    private bool _ending;
    private float _fadeT; // 收尾黑场 0→1
    private bool _emitted;

    // 版面几何（随 Size 变化重算）：双圆中心/半径、地平线 y。
    private Vector2 _size;
    private float _cx1, _cx2, _cy, _radius, _horizonY;

    // 望远镜黑框：满屏挖掉双圆后的黑色补集（扫描线切条，静态、Size 变才重建）。
    private readonly List<Rect2> _maskRects = new();

    // 尸潮个体场：归一化横向位、深度 u（0=地平线远处，1=近前景）、相位、尺寸抖动。纯视觉 RNG（同 BloodDecal，不走引擎 IRandomSource）。
    private float[] _nx = Array.Empty<float>();
    private float[] _u = Array.Empty<float>();
    private float[] _phase = Array.Empty<float>();
    private float[] _sizeJit = Array.Empty<float>();
    private float[] _speedJit = Array.Empty<float>();

    // 前景零星掉队者（尺度参照，个体剪影）：横向位、深度、相位。
    private float[] _sx = Array.Empty<float>();
    private float[] _su = Array.Empty<float>();
    private float[] _sphase = Array.Empty<float>();

    private readonly RandomNumberGenerator _rng = new();
    private Label _skipHint = null!;
    private Texture2D? _overviewTexture;

    // —— 调色（暗、冷、脏，克制压抑）——
    private static readonly Color SkyTop = new(0.02f, 0.03f, 0.045f);
    private static readonly Color SkyHorizon = new(0.17f, 0.17f, 0.15f);
    private static readonly Color GroundFar = new(0.12f, 0.13f, 0.11f);
    private static readonly Color GroundNear = new(0.045f, 0.05f, 0.045f);
    private static readonly Color HordeCol = new(0.06f, 0.065f, 0.06f);
    private static readonly Color LensRim = new(0.30f, 0.33f, 0.30f, 0.55f);
    private static readonly Color Reticle = new(0.45f, 0.48f, 0.45f, 0.14f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;   // 吞掉背景点击，别漏到探索世界
        ProcessMode = ProcessModeEnum.Always; // 与父层一致：暂停下也计时/绘制/收输入

        _rng.Randomize();
        BuildDotField();
        BuildStragglers();
        _overviewTexture = GD.Load<Texture2D>(OverviewTexturePath);

        _skipHint = new Label { Text = "按任意键跳过" };
        _skipHint.AddThemeFontSizeOverride("font_size", 13);
        _skipHint.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.66f, 0.6f));
        _skipHint.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        _skipHint.AddThemeConstantOverride("outline_size", 3);
        _skipHint.SetAnchorsPreset(LayoutPreset.BottomRight);
        _skipHint.GrowHorizontal = GrowDirection.Begin;
        _skipHint.GrowVertical = GrowDirection.Begin;
        _skipHint.Position = new Vector2(-140, -34);
        AddChild(_skipHint);

        _lastMs = Time.GetTicksMsec();
    }

    private void BuildDotField()
    {
        _nx = new float[DotCount];
        _u = new float[DotCount];
        _phase = new float[DotCount];
        _sizeJit = new float[DotCount];
        _speedJit = new float[DotCount];
        for (int i = 0; i < DotCount; i++)
        {
            _nx[i] = _rng.RandfRange(-1f, 1f);
            _u[i] = _rng.Randf();
            _phase[i] = _rng.RandfRange(0f, Mathf.Tau);
            _sizeJit[i] = _rng.RandfRange(0.7f, 1.35f);
            _speedJit[i] = _rng.RandfRange(0.8f, 1.25f);
        }
    }

    private void BuildStragglers()
    {
        const int n = 4;
        _sx = new float[n];
        _su = new float[n];
        _sphase = new float[n];
        for (int i = 0; i < n; i++)
        {
            _sx[i] = _rng.RandfRange(-0.55f, 0.55f);
            _su[i] = _rng.RandfRange(0.55f, 0.95f); // 都在近前景
            _sphase[i] = _rng.RandfRange(0f, Mathf.Tau);
        }
    }

    public override void _Process(double _)
    {
        // 真实 delta：不受 Engine.TimeScale 影响，演出期间世界冻结也照走。
        ulong now = Time.GetTicksMsec();
        float dt = (now - _lastMs) / 1000f;
        _lastMs = now;
        if (dt > 0.1f)
        {
            dt = 0.1f;
        }
        _elapsed += dt;

        if (_size != Size && Size.X > 1 && Size.Y > 1)
        {
            ComputeLayout();
        }

        // 尸潮南下（向镜头）：深度 u 递增，越过 1 回收到地平线（换新横向位）。近处走得略快（透视）。
        for (int i = 0; i < _u.Length; i++)
        {
            _u[i] += dt * 0.055f * _speedJit[i] * Mathf.Lerp(0.6f, 1.5f, _u[i]);
            if (_u[i] > 1f)
            {
                _u[i] -= 1f;
                _nx[i] = _rng.RandfRange(-1f, 1f);
            }
        }
        for (int i = 0; i < _su.Length; i++)
        {
            _su[i] += dt * 0.045f;
            if (_su[i] > 1f)
            {
                _su[i] = 0.55f;
                _sx[i] = _rng.RandfRange(-0.55f, 0.55f);
            }
        }

        if (!_ending && _elapsed >= DurationSec)
        {
            _ending = true;
        }
        if (_ending)
        {
            _fadeT += dt / FadeOutSec;
            if (_fadeT >= 1f && !_emitted)
            {
                _emitted = true;
                Finished?.Invoke();
                return;
            }
        }

        QueueRedraw();
    }

    /// <summary>任意键/鼠标按下即跳过（吞掉事件不漏给世界）。已在收尾则忽略。</summary>
    public override void _Input(InputEvent @event)
    {
        if (_ending)
        {
            return;
        }
        bool skip = (@event is InputEventKey k && k.Pressed && !k.Echo)
                    || (@event is InputEventMouseButton mb && mb.Pressed);
        if (skip)
        {
            _ending = true;
            GetViewport().SetInputAsHandled();
        }
    }

    private void ComputeLayout()
    {
        _size = Size;
        // 双圆望远镜：两圆水平并置、略重叠成经典「双筒」轮廓。半径受屏高/屏宽双向约束防越界。
        _radius = Mathf.Min(_size.Y * 0.46f, _size.X * 0.29f);
        _cy = _size.Y * 0.5f;
        float mid = _size.X * 0.5f;
        float sep = _radius * 0.84f; // < r ⇒ 两圆重叠
        _cx1 = mid - sep;
        _cx2 = mid + sep;
        // 地平线略高于中线，给近处尸潮/掉队者留出行进空间。
        _horizonY = _cy - _radius * 0.16f;
        BuildMask();
    }

    /// <summary>扫描线切条：满屏减去双圆并集＝望远镜黑框。静态一次算好，逐帧只 DrawRect。</summary>
    private void BuildMask()
    {
        _maskRects.Clear();
        const float step = 4f;
        for (float y = 0; y < _size.Y; y += step)
        {
            float h = Mathf.Min(step, _size.Y - y);
            float ymid = y + h * 0.5f;

            // 本行两圆的 x 覆盖区间（可能 0/1/2 段），合并重叠。
            var spans = new List<(float A, float B)>(2);
            AddSpan(spans, _cx1, ymid);
            AddSpan(spans, _cx2, ymid);

            if (spans.Count == 0)
            {
                _maskRects.Add(new Rect2(0, y, _size.X, h)); // 整行都在圆外→全黑
                continue;
            }
            spans.Sort((p, q) => p.A.CompareTo(q.A));
            // 合并 + 填补集为黑。
            float cursor = 0f;
            float openEnd = float.NegativeInfinity;
            foreach (var (a, b) in spans)
            {
                if (a > openEnd)
                {
                    // 新洞开始前的黑段。
                    if (a > cursor)
                    {
                        _maskRects.Add(new Rect2(cursor, y, a - cursor, h));
                    }
                    cursor = Mathf.Max(cursor, b);
                    openEnd = b;
                }
                else
                {
                    // 与上一洞重叠，延伸洞右缘。
                    if (b > openEnd)
                    {
                        openEnd = b;
                        cursor = Mathf.Max(cursor, b);
                    }
                }
            }
            if (cursor < _size.X)
            {
                _maskRects.Add(new Rect2(cursor, y, _size.X - cursor, h));
            }
        }
    }

    private void AddSpan(List<(float, float)> spans, float cx, float y)
    {
        float dy = y - _cy;
        if (Mathf.Abs(dy) >= _radius)
        {
            return;
        }
        float half = Mathf.Sqrt(_radius * _radius - dy * dy);
        spans.Add((cx - half, cx + half));
    }

    public override void _Draw()
    {
        if (_size.X <= 1 || _size.Y <= 1)
        {
            return;
        }

        // 先在双筒近景中观察；随后镜头缓慢升高，中央裁切逐渐扩为完整俯瞰，双筒边框同时退出。
        float overview = CinematicOverview.EasedProgress(
            _elapsed,
            CinematicOverview.HordeRiseStartSeconds,
            CinematicOverview.HordeRiseDurationSeconds);
        float breathX = Mathf.Sin(_elapsed * 0.55f) * 3.5f;
        float breathY = Mathf.Cos(_elapsed * 0.4f) * 2.2f;
        float pulse = 1f + Mathf.Sin(_elapsed * 0.5f) * 0.03f;
        float hy = _horizonY + breathY;

        if (_overviewTexture is not null)
            DrawOverviewBackground(overview);
        else
            DrawSkyAndGround(hy);
        float movingOverlayAlpha = Mathf.Lerp(1f, 0.28f, overview);
        DrawHordeMass(hy, breathX, pulse, movingOverlayAlpha);
        DrawStragglers(hy, breathX, pulse, movingOverlayAlpha);
        float lensAlpha = 1f - overview;
        DrawVignette(lensAlpha);
        DrawMask(lensAlpha);
        DrawReticle(lensAlpha);
        DrawLensRim(lensAlpha);
        DrawFadeCover();
    }

    private void DrawOverviewBackground(float overview)
    {
        if (_overviewTexture is null)
            return;
        float w = _overviewTexture.GetWidth();
        float h = _overviewTexture.GetHeight();
        float cropScale = Mathf.Lerp(0.52f, 1f, overview);
        var cropSize = new Vector2(w * cropScale, h * cropScale);
        // 近景稍偏下，先看见尸群个体；升高后回到整张全局构图。
        var center = new Vector2(w * 0.5f, Mathf.Lerp(h * 0.63f, h * 0.5f, overview));
        var source = new Rect2(center - cropSize / 2f, cropSize);
        source.Position = new Vector2(
            Mathf.Clamp(source.Position.X, 0f, w - source.Size.X),
            Mathf.Clamp(source.Position.Y, 0f, h - source.Size.Y));
        DrawTextureRectRegion(_overviewTexture, new Rect2(Vector2.Zero, _size), source);
    }

    private void DrawSkyAndGround(float hy)
    {
        const float strip = 4f;
        // 天空：顶→地平线渐亮（脏黄灰）。
        for (float y = 0; y < hy; y += strip)
        {
            float t = y / Mathf.Max(hy, 1f);
            DrawRect(new Rect2(0, y, _size.X, strip + 1f), SkyTop.Lerp(SkyHorizon, t));
        }
        // 地面：地平线→底部渐暗。
        float gh = _size.Y - hy;
        for (float y = hy; y < _size.Y; y += strip)
        {
            float t = (y - hy) / Mathf.Max(gh, 1f);
            DrawRect(new Rect2(0, y, _size.X, strip + 1f), GroundFar.Lerp(GroundNear, t));
        }
        // 地平线薄雾：一抹淡带让远处暗墙浮出轮廓。
        DrawRect(new Rect2(0, hy - _radius * 0.05f, _size.X, _radius * 0.1f),
            new Color(0.22f, 0.22f, 0.2f, 0.25f));
    }

    /// <summary>远处压缩成一堵蠕动暗墙（地平线密带），近处个体渐大渐疏——「上百万」尸潮向镜头缓移。</summary>
    private void DrawHordeMass(float hy, float breathX, float pulse, float alphaMultiplier)
    {
        float mid = _size.X * 0.5f;
        float bottom = _size.Y * 0.98f;

        // 地平线暗墙：贴地平线一条被无数远尸压实的暗带（密度上重下轻）。
        DrawRect(new Rect2(0, hy, _size.X, _radius * 0.16f),
            new Color(HordeCol.R, HordeCol.G, HordeCol.B, 0.92f * alphaMultiplier));
        DrawRect(new Rect2(0, hy + _radius * 0.16f, _size.X, _radius * 0.1f),
            new Color(HordeCol.R, HordeCol.G, HordeCol.B, 0.5f * alphaMultiplier));

        for (int i = 0; i < _u.Length; i++)
        {
            float u = _u[i];
            // 透视压缩：深度平方映射，绝大多数个体挤在地平线附近。
            float y = hy + (bottom - hy) * (u * u);
            float ext = Mathf.Lerp(_size.X * 0.56f, _size.X * 0.78f, u);
            float sway = Mathf.Sin(_elapsed * 0.7f + _phase[i]) * Mathf.Lerp(1.5f, 7f, u);
            float x = mid + _nx[i] * ext + sway + breathX;

            float sz = Mathf.Lerp(1.1f, 4.4f, u) * _sizeJit[i] * pulse;
            float a = Mathf.Lerp(0.5f, 0.95f, u);
            Color c = HordeCol.Lerp(new Color(0.09f, 0.1f, 0.09f), u);
            c.A = a * alphaMultiplier;
            // 站立个体：略高于宽的暗块（像素风）。
            DrawRect(new Rect2(x - sz * 0.5f, y - sz * 0.7f, sz, sz * 1.4f), c);
        }
    }

    /// <summary>前景零星掉队者剪影（头+躯干+分腿），给出尺度参照，蹒跚南下。</summary>
    private void DrawStragglers(float hy, float breathX, float pulse, float alphaMultiplier)
    {
        float mid = _size.X * 0.5f;
        float bottom = _size.Y * 1.02f;
        for (int i = 0; i < _su.Length; i++)
        {
            float u = _su[i];
            float y = hy + (bottom - hy) * (u * u);
            float ext = _size.X * 0.42f;
            float sway = Mathf.Sin(_elapsed * 0.9f + _sphase[i]) * 5f;
            float x = mid + _sx[i] * ext + sway + breathX;
            float scale = Mathf.Lerp(6f, 15f, u) * pulse;
            DrawSilhouette(new Vector2(x, y), scale, _elapsed + _sphase[i], alphaMultiplier);
        }
    }

    private void DrawSilhouette(Vector2 foot, float s, float t, float alphaMultiplier)
    {
        Color body = new(0.03f, 0.035f, 0.03f, 0.96f * alphaMultiplier);
        float lurch = Mathf.Sin(t * 2.4f) * s * 0.12f; // 蹒跚左右倾
        // 躯干（压扁椭圆近似成矩形叠头）。
        DrawRect(new Rect2(foot.X - s * 0.28f + lurch, foot.Y - s * 1.7f, s * 0.56f, s * 1.25f), body);
        // 头。
        DrawCircle(new Vector2(foot.X + lurch * 1.3f, foot.Y - s * 1.85f), s * 0.28f, body);
        // 双腿（分开一点）。
        DrawRect(new Rect2(foot.X - s * 0.22f, foot.Y - s * 0.55f, s * 0.18f, s * 0.55f), body);
        DrawRect(new Rect2(foot.X + s * 0.04f, foot.Y - s * 0.55f, s * 0.18f, s * 0.55f), body);
    }

    /// <summary>圆内暗角：由内向外叠黑环，越近镜缘越暗（望远镜观感）。</summary>
    private void DrawVignette(float alphaMultiplier)
    {
        const int rings = 14;
        foreach (float cx in new[] { _cx1, _cx2 })
        {
            var center = new Vector2(cx, _cy);
            for (int i = 0; i < rings; i++)
            {
                float f = i / (float)(rings - 1);
                float rr = Mathf.Lerp(_radius * 0.52f, _radius, f);
                float a = Mathf.Lerp(0f, 0.55f, f * f) * alphaMultiplier;
                DrawArc(center, rr, 0f, Mathf.Tau, 56, new Color(0, 0, 0, a),
                    _radius * 0.5f / rings * 2.2f, false);
            }
        }
    }

    private void DrawMask(float alphaMultiplier)
    {
        if (alphaMultiplier <= 0.001f)
            return;
        var black = new Color(0, 0, 0, alphaMultiplier);
        foreach (Rect2 r in _maskRects)
        {
            DrawRect(r, black);
        }
    }

    /// <summary>望远镜准星：中央十字丝 + 短刻度（低透明，克制）。</summary>
    private void DrawReticle(float alphaMultiplier)
    {
        Color reticle = new(Reticle.R, Reticle.G, Reticle.B, Reticle.A * alphaMultiplier);
        float mid = _size.X * 0.5f;
        float span = _radius * 0.68f;
        DrawLine(new Vector2(_cx1 - span, _cy), new Vector2(_cx2 + span, _cy), reticle, 1f);
        DrawLine(new Vector2(mid, _cy - span), new Vector2(mid, _cy + span), reticle, 1f);
        for (int i = -3; i <= 3; i++)
        {
            if (i == 0)
            {
                continue;
            }
            float x = mid + i * span * 0.22f;
            DrawLine(new Vector2(x, _cy - 4f), new Vector2(x, _cy + 4f), reticle, 1f);
        }
    }

    private void DrawLensRim(float alphaMultiplier)
    {
        Color rim = new(LensRim.R, LensRim.G, LensRim.B, LensRim.A * alphaMultiplier);
        DrawArc(new Vector2(_cx1, _cy), _radius, 0f, Mathf.Tau, 72, rim, 2f, true);
        DrawArc(new Vector2(_cx2, _cy), _radius, 0f, Mathf.Tau, 72, rim, 2f, true);
    }

    /// <summary>首尾黑场：淡入(1→0)/淡出(0→1) 一层全屏黑，收尾全黑时干净交棒后续剧情面板。</summary>
    private void DrawFadeCover()
    {
        float cover = 0f;
        if (_elapsed < FadeInSec)
        {
            cover = 1f - _elapsed / FadeInSec;
        }
        if (_ending)
        {
            cover = Mathf.Clamp(_fadeT, 0f, 1f);
        }
        if (cover > 0.001f)
        {
            DrawRect(new Rect2(0, 0, _size.X, _size.Y), new Color(0, 0, 0, cover));
        }
    }
}
