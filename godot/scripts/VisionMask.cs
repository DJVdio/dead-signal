using System;
using System.Collections.Generic;
using Godot;
using SysVec2 = System.Numerics.Vector2;

namespace DeadSignal.Godot;

/// <summary>
/// 玩家侧视野遮暗渲染 + 视野外揭示/隐藏（批次4，Godot 运行时层）。以受控角色（<see cref="_viewersProvider"/> 供给）的
/// 位置/朝向 + 当前光照下的 <see cref="VisionLogic.VisionCone"/>，结合障碍遮挡（<see cref="VisionOcclusion"/> raycast），
/// 把视野外的画面按网格叠画单档暗四边形；并把视野外的敌人/可交互物节点 <c>Visible=false</c>（不揭示）。
///
/// 判定核=纯逻辑 <see cref="VisionField"/>（逐格心 / 逐目标 <see cref="VisionLogic.CanSee"/>，多观察者取并集）；
/// 本类只做：Godot↔System.Numerics 坐标桥、raycast 遮挡供给、网格暗格直绘、节点隐藏、节流+脏重绘。
///
/// 坐标：物理/网格/raycast 全在 cartesian（观察者 <c>Actor.GlobalPosition</c>/朝向、墙体皆 cartesian）；
/// 仅 <see cref="_Draw"/> 按 <see cref="Projection"/> 投影格四角——探索关 <see cref="ProjectionMode.Cartesian"/> 直绘方格，
/// 营地 <see cref="ProjectionMode.Iso"/> 经 <see cref="Iso.Project"/> 成菱形（<see cref="CanvasItem.DrawColoredPolygon"/> 吃任意四边形）。
///
/// 性能：节流 <see cref="UpdateInterval"/>（真实时钟，不吃 TimeScale）重算，暗格位图变化才 <see cref="CanvasItem.QueueRedraw"/>；
/// 逐格先做廉价锥检（<see cref="VisionField"/> 内），仅落在锥内的格补 raycast（多数格在所有锥外、零 raycast）。
///
/// 刻意不走 Polygon2D + AddOutline 挖洞管线（headless 挖洞失效的既往坑）：本方案只叠画暗格、不做多边形布尔减。
/// </summary>
public sealed partial class VisionMask : Node2D
{
    public enum ProjectionMode
    {
        /// <summary>cartesian 直绘（仅供特殊演出或调试画布）。</summary>
        Cartesian,

        /// <summary>iso 投影（营地与常规探索关统一经 <see cref="Iso.Project"/> 渲染）。</summary>
        Iso,
    }

    // ── 配置（皆拟定待调）──────────────────────────────────────────────────
    /// <summary>网格格边长（世界像素）。越小越细但 raycast/绘制越多。</summary>
    public float CellSize { get; set; } = 48f;

    /// <summary>单档遮暗色（含 alpha）。视野外格叠此色；"半暗记忆 / 全黑未见"的多档留待细化。</summary>
    public Color DarkColor { get; set; } = new(0.02f, 0.02f, 0.05f, 0.82f);

    /// <summary>观察者满档视距 R0（玩家）。传入 <see cref="VisionLogic.ConeFor(float,float)"/> 随光照缩放。</summary>
    public float ViewerBaseRange { get; set; } = VisionLogic.BaseRange;

    /// <summary>重算节流间隔（秒，真实时钟）。</summary>
    public float UpdateInterval { get; set; } = 0.25f;

    /// <summary>绘制投影模式。</summary>
    public ProjectionMode Projection { get; set; } = ProjectionMode.Cartesian;

    // ── 依赖注入（delegate，避免硬绑具体关卡）─────────────────────────────
    private Rect2 _worldBounds;
    private bool _boundsSet;
    private Func<IEnumerable<Actor>>? _viewersProvider;
    private Func<float>? _ambientProvider;
    private Func<Vector2, float>? _sourceProvider;
    private Func<IEnumerable<(Vector2 worldPos, Action<bool> setVisible)>>? _revealablesProvider;
    private Func<Actor, VisionLogic.VisionCone, VisionLogic.VisionCone>? _viewerConeAdjuster;

    // ── 运行态 ────────────────────────────────────────────────────────────
    private bool[] _darkCells = Array.Empty<bool>();
    private bool[]? _darkScratch;                       // 双 buffer：重算写此、与 _darkCells 比较，变了才互换（去逐帧 new bool[]）
    private readonly List<VisionField.Viewer> _viewerBuffer = new(); // 复用观察者快照列表（去 per-recompute new List）
    private int _cols;
    private int _rows;
    private Vector2 _gridMin;      // 网格原点（cartesian，= worldBounds.Position）
    private ulong _lastTick;
    private bool _hasComputed;

    // 观察者位姿脏检：观察者位置/朝向/锥（含光照）都没变 → 暗格必然不变（墙体静态），跳过整轮 ComputeDarkCells。
    // 站着不动的夜里从 4Hz 全格重算降到零重算（DriveReveal 仍每次跑，因可揭示物会移动）。
    private int _lastPoseKey;
    private bool _hasPoseKey;
    private bool _poseDirty;      // 外部世界变更（如结构被破坏改变遮挡）可置真强制下次重算，见 MarkDirty()

    // 夜间开关：营地白天全可见豁免 → Enabled=false（不遮暗、揭示一切）；夜间/探索关全程 → true。
    private bool _enabled = true;
    private bool _appliedDisabled;   // 已执行过"禁用即揭示一切+清暗格"，避免每帧重复

    /// <summary>装配：覆盖的世界包围盒（cartesian）+ 绘制投影模式。ZIndex 拉高盖住世界（HUD 在独立 CanvasLayer 之上不受影响）。</summary>
    public void Configure(Rect2 worldBounds, ProjectionMode projection)
    {
        _worldBounds = worldBounds;
        _gridMin = worldBounds.Position;
        _boundsSet = true;
        Projection = projection;
        ZIndex = 4000; // 盖住世界内容（发现点标签 ZIndex≈12，角色 ZIndex≈10），仍在 HUD CanvasLayer 之下
    }

    /// <summary>受控观察者供给（探索队/夜间营地在岗幸存者）。取其 <see cref="Actor.GlobalPosition"/>/<see cref="Actor.FacingAngle"/>。</summary>
    public void SetViewersProvider(Func<IEnumerable<Actor>> provider) => _viewersProvider = provider;

    /// <summary>环境光 L∈[0,1] 供给（探索白天=满档、营地夜=<see cref="VisionLogic.NightAmbient"/>，随相位）。锥形据此缩放。</summary>
    public void SetAmbientProvider(Func<float> provider) => _ambientProvider = provider;

    /// <summary>
    /// 光源贡献供给（可选）：某世界位置处的最强光源贡献 [0,1]（如营地 <c>CampLights.StrongestAt</c>）。
    /// 观察者局部光照 = <see cref="VisionLogic.CombineLight"/>(环境光, 该位置最强贡献) → 灯/火堆旁视野更远更宽。
    /// 不设=纯环境光。
    /// </summary>
    public void SetSourceProvider(Func<Vector2, float> provider) => _sourceProvider = provider;

    /// <summary>
    /// 可揭示物供给（敌人/发现点等）：返回每个物的 cartesian 世界位置 + 一个"设可见"回调。视野内→<c>setVisible(true)</c>，
    /// 视野外→<c>setVisible(false)</c>。用回调而非节点，是为兼容营地敌人的视觉 <see cref="ActorSprite"/> 挂在
    /// iso_layer（非 Actor 子节点，隐 Actor 隐不掉它）——调用方自定"隐什么"。
    /// </summary>
    public void SetRevealablesProvider(Func<IEnumerable<(Vector2 worldPos, Action<bool> setVisible)>> provider)
        => _revealablesProvider = provider;

    /// <summary>
    /// 逐观察者视锥后处理（可选）：光照/基准 R0 出的基础锥再叠角色个体系数（如道格 +10% 视角、布鲁斯 +10% 视距/视角）。
    /// 回调收 (观察者 Actor, 基础锥)，返回缩放后的锥（不缩放者原样返回）。不设=全观察者只吃光照锥。
    /// synergy-wiring 用它把 <c>DougBruceBond</c> 视野系数经 <see cref="VisionLogic.VisionCone.Scaled"/> 施到道格/布鲁斯。
    /// </summary>
    public void SetViewerConeAdjuster(Func<Actor, VisionLogic.VisionCone, VisionLogic.VisionCone> adjuster)
        => _viewerConeAdjuster = adjuster;

    /// <summary>
    /// 夜间开关。<c>false</c>=营地白天全可见豁免：不遮暗、揭示一切候选物；<c>true</c>=启用视野系统（探索关全程/营地夜间）。
    /// 关时立即揭示一切并清暗格（下次 Recompute 生效）。
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
            return;
        _enabled = enabled;
        _appliedDisabled = false; // 状态翻转，允许禁用分支或启用分支各跑一次
        _poseDirty = true;        // 翻转后强制下次全量重算（不吃位姿脏检短路）
        Recompute();
    }

    /// <summary>
    /// 强制下次 <see cref="Recompute"/> 全量重算暗格（绕过观察者位姿脏检）。供"非观察者移动但遮挡改变"的场景调用——
    /// 如围攻中围栏/大门被破坏改变了视线通路；观察者站着不动时位姿脏检本会跳过重算，此钩子确保结构变更后暗格及时更新。
    /// （当前遮暗为 0.25s 节流的观感层，未在结构破坏处接线；留作显式接缝，避免"墙没了但暗格没更新"的静默失真。）
    /// </summary>
    public void MarkDirty() => _poseDirty = true;

    public override void _Ready()
    {
        _lastTick = Time.GetTicksMsec();
        // 首帧立即算一次（否则开局有一帧全亮）。
        Recompute();
    }

    public override void _PhysicsProcess(double delta)
    {
        ulong now = Time.GetTicksMsec();
        if ((now - _lastTick) / 1000f < UpdateInterval)
            return;
        _lastTick = now;
        Recompute();
    }

    /// <summary>取观察者快照 → 算暗格位图（变化才重绘）+ 驱动揭示/隐藏。须在物理帧内调用（raycast 取 DirectSpaceState）。</summary>
    private void Recompute()
    {
        if (!_boundsSet || _viewersProvider == null || !IsInsideTree())
            return;

        // 白天全可见豁免：揭示一切候选物、清空暗格。只在刚翻转到禁用时跑一次。
        if (!_enabled)
        {
            if (!_appliedDisabled)
            {
                RevealAll();
                if (_hasComputed || _darkCells.Length > 0)
                {
                    _darkCells = Array.Empty<bool>();
                    _cols = _rows = 0;
                    _hasComputed = false;
                    QueueRedraw();
                }
                _appliedDisabled = true;
            }
            return;
        }

        List<VisionField.Viewer> viewers = SnapshotViewers();

        PhysicsDirectSpaceState2D space = GetWorld2D().DirectSpaceState;
        bool OccludedBetween(SysVec2 from, SysVec2 to) =>
            VisionOcclusion.IsOccluded(space, new Vector2(from.X, from.Y), new Vector2(to.X, to.Y));

        // 观察者位姿脏检：位置/朝向/锥全未变且已算过 → 暗格必然不变（墙体静态），跳过整轮 ComputeDarkCells。
        int poseKey = PoseKey(viewers);
        bool posesChanged = _poseDirty || !_hasPoseKey || poseKey != _lastPoseKey || !_hasComputed;
        if (posesChanged)
        {
            SysVec2 min = new(_worldBounds.Position.X, _worldBounds.Position.Y);
            SysVec2 max = new(_worldBounds.End.X, _worldBounds.End.Y);
            // 双 buffer：算进 _darkScratch，与当前 _darkCells 比较，变了才互换引用（去逐帧 new bool[]）。
            VisionField.ComputeDarkCells(min, max, CellSize, viewers, OccludedBetween, ref _darkScratch, out int cols, out int rows);

            if (!_hasComputed || cols != _cols || rows != _rows || !SameBits(_darkScratch!, _darkCells))
            {
                (_darkCells, _darkScratch) = (_darkScratch!, _darkCells); // 互换：新结果成显示 buffer，旧的留作下次 scratch
                _cols = cols;
                _rows = rows;
                _hasComputed = true;
                QueueRedraw();
            }

            _lastPoseKey = poseKey;
            _hasPoseKey = true;
            _poseDirty = false;
        }

        // 揭示/隐藏：可揭示物（敌人等）会移动，即便观察者未动也须每次驱动。
        DriveReveal(viewers, OccludedBetween);
    }

    /// <summary>观察者位姿指纹：数量 + 每人位置/朝向/锥（含光照缩放）。全等即暗格必等（墙体静态），据此短路重算。</summary>
    private static int PoseKey(List<VisionField.Viewer> viewers)
    {
        var hc = new HashCode();
        hc.Add(viewers.Count);
        foreach (VisionField.Viewer v in viewers)
        {
            hc.Add(v.Position.X);
            hc.Add(v.Position.Y);
            hc.Add(v.Facing.X);
            hc.Add(v.Facing.Y);
            hc.Add(v.Cone.Range);
            hc.Add(v.Cone.HalfAngleDeg);
        }
        return hc.ToHashCode();
    }

    private List<VisionField.Viewer> SnapshotViewers()
    {
        float ambient = _ambientProvider?.Invoke() ?? VisionLogic.DaylightAmbient;
        // 逐观察者局部光照 = max(环境光, 该处最强光源贡献)。无光源供给（探索关）时退化为纯环境光、全员同锥（省重复算）。
        VisionLogic.VisionCone sharedCone = _sourceProvider == null
            ? VisionLogic.ConeFor(ambient, ViewerBaseRange)
            : default;

        _viewerBuffer.Clear(); // 复用缓冲（去 per-recompute new List）
        foreach (Actor a in _viewersProvider!.Invoke())
        {
            if (a == null || !IsInstanceValid(a) || !a.Alive)
                continue;
            Vector2 p = a.GlobalPosition;
            Vector2 f = Vector2.FromAngle(a.FacingAngle);
            VisionLogic.VisionCone cone = _sourceProvider == null
                ? sharedCone
                : VisionLogic.ConeFor(VisionLogic.CombineLight(ambient, _sourceProvider(p)), ViewerBaseRange);
            // 角色个体系数（道格/布鲁斯羁绊视野）：基础光照锥再叠 .Scaled(...)。无 adjuster/非技能角色原样。
            if (_viewerConeAdjuster != null)
                cone = _viewerConeAdjuster(a, cone);
            _viewerBuffer.Add(new VisionField.Viewer(new SysVec2(p.X, p.Y), new SysVec2(f.X, f.Y), cone));
        }
        return _viewerBuffer;
    }

    private void DriveReveal(List<VisionField.Viewer> viewers, Func<SysVec2, SysVec2, bool> occludedBetween)
    {
        if (_revealablesProvider == null)
            return;
        foreach ((Vector2 worldPos, Action<bool> setVisible) in _revealablesProvider.Invoke())
        {
            bool visible = VisionField.IsPointVisible(viewers, new SysVec2(worldPos.X, worldPos.Y), occludedBetween);
            setVisible(visible);
        }
    }

    /// <summary>禁用（白天豁免）时揭示一切候选物。</summary>
    private void RevealAll()
    {
        if (_revealablesProvider == null)
            return;
        foreach ((Vector2 _, Action<bool> setVisible) in _revealablesProvider.Invoke())
            setVisible(true);
    }

    private static bool SameBits(bool[] a, bool[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    public override void _Draw()
    {
        if (!_hasComputed || _cols == 0 || _rows == 0)
            return;

        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                if (!_darkCells[r * _cols + c])
                    continue;

                // 该格 cartesian 四角。
                float x0 = _gridMin.X + c * CellSize;
                float y0 = _gridMin.Y + r * CellSize;
                float x1 = x0 + CellSize;
                float y1 = y0 + CellSize;

                Vector2 a = Project(new Vector2(x0, y0));
                Vector2 b = Project(new Vector2(x1, y0));
                Vector2 d = Project(new Vector2(x1, y1));
                Vector2 e = Project(new Vector2(x0, y1));
                DrawColoredPolygon(new[] { a, b, d, e }, DarkColor);
            }
        }
    }

    private Vector2 Project(Vector2 cart) =>
        Projection == ProjectionMode.Iso ? Iso.Project(cart) : cart;
}
