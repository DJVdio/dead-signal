using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 幸存者营地主场景（初级地图）：山凹里的农庄，三面环山、一面栅栏+大门，易守难攻。
///
/// 【等距重构 Batch 0（伪等距 / faux-iso，PZ 做法）】
/// 逻辑/物理/寻路/碰撞/camp.json 全不动，只在渲染层做投影 + YSort。场景拆两个兄弟层：
///  - <b>LogicLayer</b>（Visible=false，无视觉）：全部 StaticBody2D 墙、CharacterBody2D actors、
///    RoofFade Area2D，坐标 = camp.json 的 cartesian 像素，物理/导航全在此平面，原封不动。
///  - <b>IsoLayer</b>（YSortEnabled）：全部视觉节点，坐标 = <see cref="Iso.Project"/>(cartesian)。
/// 静态世界投影一次；每帧把 actor 的 cartesian GlobalPosition 推给 IsoLayer 里的占位标记。
/// 鼠标：<see cref="Godot.Node2D.GetGlobalMousePosition"/> 现返回 iso 屏幕坐标，右键/点选先
/// <see cref="Iso.Unproject"/> 回 cartesian 再和 actor 逻辑位置比对；框选反过来把 actor 投到屏幕比。
///
/// 角色 B0 只用占位菱形标记（人形留 B2）。与 <see cref="Main"/> 刻意不抽公共基类（避免动战斗侧）。
/// </summary>
public sealed partial class CampMain : Node2D
{
    private Rect2 _mapBounds = new(0, 0, 1600, 1200);
    private float _navInset = 20f;
    private Vector2 _cameraCenter = new(800, 650);

    // 所有实心矩形（山体/栅栏/建筑墙/道具）——既是碰撞，也作导航挖洞。
    private readonly List<Rect2> _navHoles = new();
    private readonly List<Pawn> _survivors = new();
    private readonly HashSet<Pawn> _selected = new();

    // actor → iso 占位标记（body 菱形 + 选中环），每帧同步位置。
    private readonly Dictionary<Pawn, (Node2D node, Line2D ring)> _markers = new();

    private CombatEngine _combat = null!;
    private GameClock _clock = null!;
    private CameraController _camera = null!;
    private CanvasModulate _ambient = null!;
    private Hud _hud = null!;

    private Node2D _logicLayer = null!;  // 物理/导航平面（cartesian，不可见）
    private Node2D _isoLayer = null!;     // 视觉层（iso，YSort）
    private Node2D _actorLayer = null!;   // actors（LogicLayer 下）

    private CampConfig _cfg = new();

    // 框选状态。_dragStartWorldIso = 按下时的 iso 屏幕世界坐标（框选用），点选另行反投影。
    private bool _dragging;
    private Vector2 _dragStartScreen;
    private Vector2 _dragStartWorldIso;
    private const float DragThreshold = 6f;

    // z 分层：地面恒底 → 世界遮挡物/角色同层交给 YSort → 屋顶恒顶。
    private const int ZGround = -10;
    private const int ZWorld = 0;
    private const int ZRoof = 20;

    public override void _Ready()
    {
        _combat = new CombatEngine();
        _cfg = LoadCampConfig();
        _mapBounds = ToRect(_cfg.mapBounds) ?? _mapBounds;
        _navInset = _cfg.navInset > 0 ? (float)_cfg.navInset : _navInset;
        _cameraCenter = ToVec(_cfg.cameraCenter) ?? _mapBounds.GetCenter();

        VerifyIsoRoundtrip();

        _isoLayer = new Node2D { Name = "IsoLayer", YSortEnabled = true };
        AddChild(_isoLayer);
        _logicLayer = new Node2D { Name = "LogicLayer", Visible = false };
        AddChild(_logicLayer);

        BuildWorld();
        BuildNavigation();

        _clock = new GameClock();
        AddChild(_clock);
        _clock.Configure(LoadDayNightConfig());

        _ambient = new CanvasModulate();
        AddChild(_ambient);

        // 相机在 iso 屏幕空间平移/缩放：中心与边界都投影到 iso。
        Rect2 isoBounds = Iso.ProjectBounds(_mapBounds);
        _camera = new CameraController { Position = Iso.Project(_cameraCenter) };
        _camera.SetBounds(isoBounds.Grow(-120));
        AddChild(_camera);
        _camera.MakeCurrent();

        _hud = new Hud();
        AddChild(_hud);

        SpawnActors();
    }

    /// <summary>反投影正确性自检：一批 cartesian 点 project→unproject 应精确还原（误差 ~0）。</summary>
    private void VerifyIsoRoundtrip()
    {
        Vector2[] samples =
        {
            new(0, 0), _mapBounds.End, _cameraCenter,
            new(560, 720), new(320, 300), new(1259, 999), new(801, 1119),
        };
        float maxErr = 0f;
        foreach (Vector2 s in samples)
        {
            maxErr = Mathf.Max(maxErr, s.DistanceTo(Iso.Unproject(Iso.Project(s))));
        }
        GD.Print($"[Iso] project↔unproject 往返最大误差 = {maxErr:0.######} px（期望 ~0，验证鼠标反投影正确性）");
    }

    // ---------------- 建图 ----------------

    private void BuildWorld()
    {
        _actorLayer = new Node2D { Name = "Actors" };

        // 地面（iso 菱形块，恒底层）。仅视觉，无碰撞。
        var groundStyle = _cfg.ground ?? new PixelStyle { color = new[] { 0.20, 0.23, 0.18 }, jitter = 0.05 };
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = _mapBounds,
            Style = groundStyle,
            Seed = 1,
            Cell = 96f,
            ZIndex = ZGround,
        });

        // 地图边界墙（纯碰撞，防走出世界；山体已覆盖，无视觉）。
        float t = 20f;
        AddBorderWall(new Rect2(0, 0, _mapBounds.Size.X, t));
        AddBorderWall(new Rect2(0, _mapBounds.Size.Y - t, _mapBounds.Size.X, t));
        AddBorderWall(new Rect2(0, 0, t, _mapBounds.Size.Y));
        AddBorderWall(new Rect2(_mapBounds.Size.X - t, 0, t, _mapBounds.Size.Y));

        // 三面环山。
        var mtnStyle = _cfg.mountainStyle ?? new PixelStyle { color = new[] { 0.27, 0.32, 0.27 }, jitter = 0.14 };
        foreach (RectSpec m in _cfg.mountains ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(m.rect) is { } r)
            {
                AddSolid(r, mtnStyle, seed: 7);
            }
        }

        // 一面栅栏 + 大门缺口（门柱亦实心）。
        var fenceStyle = _cfg.fenceStyle ?? new PixelStyle { color = new[] { 0.40, 0.30, 0.19 }, jitter = 0.12 };
        foreach (RectSpec f in _cfg.fences ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(f.rect) is { } r)
            {
                AddSolid(r, fenceStyle, seed: 11);
            }
        }
        foreach (RectSpec p in _cfg.gatePosts ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(p.rect) is { } r)
            {
                AddSolid(r, fenceStyle, seed: 13);
            }
        }

        // 建筑（墙留门洞 + 屋顶淡出）。
        foreach (BuildingSpec b in _cfg.buildings ?? System.Array.Empty<BuildingSpec>())
        {
            BuildBuilding(b);
        }

        // 道具（工作台等实心障碍）。
        foreach (PropSpec pr in _cfg.props ?? System.Array.Empty<PropSpec>())
        {
            if (ToRect(pr.rect) is { } r)
            {
                var style = new PixelStyle { color = pr.color, jitter = pr.jitter };
                AddSolid(r, style, seed: 17);
            }
        }

        _logicLayer.AddChild(_actorLayer);
    }

    /// <summary>建筑：沿 footprint 生成四面墙（门侧留缺口），室内可寻路进入；带屋顶则加淡出触发。</summary>
    private void BuildBuilding(BuildingSpec b)
    {
        if (ToRect(b.rect) is not { } foot)
        {
            return;
        }
        float wt = b.wallThickness > 0 ? (float)b.wallThickness : 18f;
        var wallStyle = new PixelStyle { color = b.wallColor, jitter = 0.10 };

        foreach (Rect2 seg in WallSegments(foot, wt, b.door))
        {
            AddSolid(seg, wallStyle, seed: 23);
        }

        if (b.roof)
        {
            // 屋顶视觉在 iso 层（恒顶层，盖住角色）。
            var roofStyle = new PixelStyle { color = b.roofColor, jitter = 0.09 };
            var roof = new IsoTilePanel { FootprintCart = foot, Style = roofStyle, Seed = 29, Cell = 48f, ZIndex = ZRoof };
            _isoLayer.AddChild(roof);

            // 淡出触发区（Area2D）在 logic 层，cartesian 平面探测幸存者进入，跨层淡出 iso 屋顶。
            var fade = new RoofFade { Position = foot.Position + foot.Size / 2 };
            Rect2 interiorLocal = new(
                -foot.Size / 2 + new Vector2(wt, wt),
                foot.Size - new Vector2(wt * 2, wt * 2));
            _logicLayer.AddChild(fade);
            fade.Setup(roof, interiorLocal);
        }
    }

    /// <summary>由 footprint + 门规格生成四面墙矩形，门所在边裂成两段留出缺口。</summary>
    private static List<Rect2> WallSegments(Rect2 foot, float t, DoorSpec? door)
    {
        string side = door?.side ?? "";
        float gapStart = door is { } d ? (float)d.gapStart : 0f;
        float gapW = door is { } d2 ? (float)d2.gapWidth : 0f;

        float x = foot.Position.X, y = foot.Position.Y, w = foot.Size.X, h = foot.Size.Y;
        var segs = new List<Rect2>();

        // 北 / 南（水平边），门在该边时沿 X 裂成左右两段。
        AddHWall(segs, new Rect2(x, y, w, t), side == "north" ? gapStart : -1, gapW, x);
        AddHWall(segs, new Rect2(x, y + h - t, w, t), side == "south" ? gapStart : -1, gapW, x);

        // 西 / 东（竖直边，去掉与横墙重叠的上下角），门在该边时沿 Y 裂成上下两段。
        AddVWall(segs, new Rect2(x, y + t, t, h - 2 * t), side == "west" ? gapStart : -1, gapW, y + t);
        AddVWall(segs, new Rect2(x + w - t, y + t, t, h - 2 * t), side == "east" ? gapStart : -1, gapW, y + t);

        return segs;
    }

    // 水平墙：gapStart<0 表示无门（整段）；否则从 footX+gapStart 起裂成左右两段。
    private static void AddHWall(List<Rect2> segs, Rect2 wall, float gapStart, float gapW, float footX)
    {
        if (gapStart < 0 || gapW <= 0)
        {
            segs.Add(wall);
            return;
        }
        float doorX = footX + gapStart;
        var left = new Rect2(wall.Position.X, wall.Position.Y, doorX - wall.Position.X, wall.Size.Y);
        var right = new Rect2(doorX + gapW, wall.Position.Y, wall.End.X - (doorX + gapW), wall.Size.Y);
        if (left.Size.X > 1) segs.Add(left);
        if (right.Size.X > 1) segs.Add(right);
    }

    // 竖直墙：gapStart<0 表示无门；否则从 footY+gapStart 起裂成上下两段。
    private static void AddVWall(List<Rect2> segs, Rect2 wall, float gapStart, float gapW, float footY)
    {
        if (gapStart < 0 || gapW <= 0)
        {
            segs.Add(wall);
            return;
        }
        float doorY = footY + gapStart;
        var top = new Rect2(wall.Position.X, wall.Position.Y, wall.Size.X, doorY - wall.Position.Y);
        var bottom = new Rect2(wall.Position.X, doorY + gapW, wall.Size.X, wall.End.Y - (doorY + gapW));
        if (top.Size.Y > 1) segs.Add(top);
        if (bottom.Size.Y > 1) segs.Add(bottom);
    }

    /// <summary>
    /// 加一堵实心矩形：LogicLayer 里放 StaticBody2D（墙层，cartesian，挡角色 + 导航挖洞），
    /// IsoLayer 里放对应的 iso 菱形块视觉。二者解耦、坐标各自平面。
    /// </summary>
    private void AddSolid(Rect2 rect, PixelStyle style, int seed)
    {
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100; // 层 3 = 墙（Actor 只与此层碰撞）
        body.CollisionMask = 0;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        _logicLayer.AddChild(body);

        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = rect,
            Style = style,
            Seed = seed,
            Cell = 48f,
            ZIndex = ZWorld,
        });

        _navHoles.Add(rect);
    }

    /// <summary>纯碰撞的边界墙（LogicLayer，无视觉、不登记导航洞——压在地图边缘，山体已覆盖）。</summary>
    private void AddBorderWall(Rect2 rect)
    {
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100;
        body.CollisionMask = 0;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        _logicLayer.AddChild(body);
    }

    private void BuildNavigation()
    {
        var region = new NavigationRegion2D();
        var navPoly = new NavigationPolygon { AgentRadius = 14f };

        Rect2 outer = new(
            _mapBounds.Position + new Vector2(_navInset, _navInset),
            _mapBounds.Size - new Vector2(_navInset * 2, _navInset * 2));
        navPoly.AddOutline(RectPoints(outer));

        foreach (Rect2 hole in _navHoles)
        {
            navPoly.AddOutline(RectPoints(hole.Grow(4f)));
        }

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, new NavigationMeshSourceGeometryData2D());
        region.NavigationPolygon = navPoly;
        // 导航区直接挂根节点（cartesian 平面），避开 LogicLayer 不可见性的任何耦合顾虑。
        AddChild(region);
    }

    // ---------------- 生成单位 ----------------

    private void SpawnActors()
    {
        foreach (SpawnSpec s in _cfg.spawns ?? System.Array.Empty<SpawnSpec>())
        {
            Color color = ToColor(s.color, new Color(0.5f, 0.7f, 0.9f));
            var p = Pawn.Create(s.name ?? "幸存者", s.pistol, color);
            if (ToVec(s.pos) is { } pos)
            {
                p.Position = pos; // cartesian
            }
            AddActor(p);
            _survivors.Add(p);
            AddMarker(p, color);
        }
    }

    private void AddActor(Actor actor)
    {
        actor.Inject(_combat, _clock);
        actor.Died += OnActorDied;
        _actorLayer.AddChild(actor); // LogicLayer 下（不可见，仅物理/寻路）
    }

    /// <summary>为一名幸存者建 iso 占位标记：菱形身体 + 选中环（B2 换人形）。</summary>
    private void AddMarker(Pawn p, Color color)
    {
        var node = new Node2D { ZIndex = ZWorld };

        var body = new Polygon2D { Polygon = IsoDiamond(p.Radius), Color = color };
        node.AddChild(body);
        var outline = new Line2D
        {
            Points = IsoDiamondLoop(p.Radius),
            Width = 1.5f,
            DefaultColor = new Color(0, 0, 0, 0.6f),
        };
        node.AddChild(outline);

        var ring = new Line2D
        {
            Points = IsoDiamondLoop(p.Radius + 5f),
            Width = 2f,
            DefaultColor = new Color(0.4f, 1f, 0.5f),
            Visible = false,
        };
        node.AddChild(ring);

        node.Position = Iso.Project(p.GlobalPosition);
        _isoLayer.AddChild(node);
        _markers[p] = (node, ring);
    }

    // 以 cartesian 圆（半径 r）的四个基点投影出的 iso 菱形。
    private static Vector2[] IsoDiamond(float r) => new[]
    {
        Iso.Project(new Vector2(0, -r)),
        Iso.Project(new Vector2(r, 0)),
        Iso.Project(new Vector2(0, r)),
        Iso.Project(new Vector2(-r, 0)),
    };

    // 闭合折线（首点重复）用于描边/选中环。
    private static Vector2[] IsoDiamondLoop(float r)
    {
        Vector2[] d = IsoDiamond(r);
        return new[] { d[0], d[1], d[2], d[3], d[0] };
    }

    private void OnActorDied(Actor actor)
    {
        if (actor is Pawn p)
        {
            _survivors.Remove(p);
            _selected.Remove(p);
            if (_markers.Remove(p, out var m))
            {
                m.node.QueueFree();
            }
        }
    }

    // ---------------- 每帧刷新 ----------------

    public override void _Process(double delta)
    {
        _ambient.Color = _clock.CurrentAmbientColor();
        string phase = _clock.IsNight ? "夜" : "昼";
        _hud.SetStatus(
            $"营地[等距]  第 {_clock.Day} 天  {_clock.ClockString()}  [{phase}]   速度 {_clock.SpeedLabel()}   " +
            $"幸存者 {_survivors.Count}");

        // 把 actor 的 cartesian 逻辑位置每帧投影同步到 iso 占位标记。
        foreach ((Pawn p, (Node2D node, Line2D ring)) in _markers)
        {
            if (!GodotObject.IsInstanceValid(p))
            {
                continue;
            }
            node.Position = Iso.Project(p.GlobalPosition);
            ring.Visible = _selected.Contains(p);
        }
    }

    // ---------------- 输入：选中 / 指令 / 时间 ----------------

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;
            case InputEventMouseButton mb:
                HandleMouseButton(mb);
                break;
            case InputEventMouseMotion when _dragging:
                UpdateDrag();
                break;
        }
    }

    private void HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Space:
                _clock.TogglePause();
                break;
            case Key.Key1:
                _clock.SetSpeedIndex(0);
                break;
            case Key.Key2:
                _clock.SetSpeedIndex(1);
                break;
            case Key.Key3:
                _clock.SetSpeedIndex(2);
                break;
            case Key.T:
                _clock.DebugSkipToPhaseEnd();
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mb)
    {
        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                _dragging = true;
                _dragStartScreen = mb.Position;
                _dragStartWorldIso = GetGlobalMousePosition(); // iso 屏幕世界坐标
            }
            else if (_dragging)
            {
                _dragging = false;
                _hud.HideSelectionRect();
                FinishSelection(mb.Position);
            }
        }
        else if (mb is { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            // 右键落点在 iso 地面 → 反投影回 cartesian 再下移动指令。
            IssueMove(Iso.Unproject(GetGlobalMousePosition()));
        }
    }

    private void UpdateDrag()
    {
        Vector2 cur = GetViewport().GetMousePosition();
        if (_dragStartScreen.DistanceTo(cur) < DragThreshold)
        {
            _hud.HideSelectionRect();
            return;
        }
        Vector2 topLeft = new(Mathf.Min(_dragStartScreen.X, cur.X), Mathf.Min(_dragStartScreen.Y, cur.Y));
        Vector2 size = (cur - _dragStartScreen).Abs();
        _hud.ShowSelectionRect(new Rect2(topLeft, size));
    }

    private void FinishSelection(Vector2 releaseScreen)
    {
        bool isBox = _dragStartScreen.DistanceTo(releaseScreen) >= DragThreshold;
        ClearSelection();

        if (isBox)
        {
            // 框选：拖拽矩形在 iso 屏幕空间；把每个 actor 的 cartesian 位置 Project 到 iso 再判包含。
            Vector2 endIso = GetGlobalMousePosition();
            Rect2 boxIso = RectFrom(_dragStartWorldIso, endIso);
            foreach (Pawn p in _survivors.Where(p => boxIso.HasPoint(Iso.Project(p.GlobalPosition))))
            {
                Select(p);
            }
        }
        else
        {
            // 点选：落点反投影回 cartesian，和 actor 逻辑位置按半径比对。
            Vector2 cart = Iso.Unproject(_dragStartWorldIso);
            Pawn? hit = _survivors
                .Where(p => p.GlobalPosition.DistanceTo(cart) <= p.Radius + 8)
                .OrderBy(p => p.GlobalPosition.DistanceTo(cart))
                .FirstOrDefault();
            if (hit != null)
            {
                Select(hit);
            }
        }
    }

    private void IssueMove(Vector2 cartPos)
    {
        if (_selected.Count == 0)
        {
            return;
        }
        // 多人移动时环形散开，避免堆叠（cartesian 平面）。
        var list = _selected.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            Vector2 offset = Vector2.Zero;
            if (list.Count > 1)
            {
                float ang = Mathf.Tau * i / list.Count;
                offset = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 26f;
            }
            list[i].CommandMoveTo(cartPos + offset);
        }
    }

    private void Select(Pawn p)
    {
        _selected.Add(p);
        p.Selected = true; // actor 自身 _Draw 不可见，选中环由 iso 标记表现
    }

    private void ClearSelection()
    {
        foreach (Pawn p in _selected)
        {
            p.Selected = false;
        }
        _selected.Clear();
    }

    // ---------------- 工具 ----------------

    private static Vector2[] RectPoints(Rect2 r) => new[]
    {
        r.Position,
        new Vector2(r.End.X, r.Position.Y),
        r.End,
        new Vector2(r.Position.X, r.End.Y),
    };

    private static Rect2 RectFrom(Vector2 a, Vector2 b)
    {
        Vector2 tl = new(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
        return new Rect2(tl, (b - a).Abs());
    }

    private static Rect2? ToRect(double[]? r) =>
        r is { Length: >= 4 } ? new Rect2((float)r[0], (float)r[1], (float)r[2], (float)r[3]) : null;

    private static Vector2? ToVec(double[]? v) =>
        v is { Length: >= 2 } ? new Vector2((float)v[0], (float)v[1]) : null;

    private static Color ToColor(double[]? c, Color fallback) =>
        c is { Length: >= 3 } ? new Color((float)c[0], (float)c[1], (float)c[2]) : fallback;

    // ---------------- 配置加载 ----------------

    private CampConfig LoadCampConfig()
    {
        const string path = "res://data/camp.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    return JsonSerializer.Deserialize<CampConfig>(f.GetAsText()) ?? new CampConfig();
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"camp.json 解析失败，用空营地：{e.Message}");
                }
            }
        }
        return new CampConfig();
    }

    private GameClock.Config LoadDayNightConfig()
    {
        DayNightRaw raw = DayNightRaw.Default();
        const string path = "res://data/daynight.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    raw = JsonSerializer.Deserialize<DayNightRaw>(f.GetAsText());
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"daynight.json 解析失败，用默认值：{e.Message}");
                }
            }
        }

        return new GameClock.Config
        {
            DayLengthSeconds = raw.dayLengthSeconds,
            NightLengthSeconds = raw.nightLengthSeconds,
            StartAtNight = raw.startAtNight,
            DayColor = ToColor(raw.dayColor, new Color(1, 0.98f, 0.92f)),
            NightColor = ToColor(raw.nightColor, new Color(0.18f, 0.22f, 0.38f)),
            TwilightFraction = raw.twilightFraction <= 0 ? 0.12 : raw.twilightFraction,
        };
    }

    // ---------------- JSON 映射（字段名对应 camp.json/daynight.json，故意小写） ----------------

    private sealed class CampConfig
    {
        public double[]? mapBounds { get; set; }
        public double navInset { get; set; }
        public double[]? cameraCenter { get; set; }
        public PixelStyle? ground { get; set; }
        public RectSpec[]? mountains { get; set; }
        public PixelStyle? mountainStyle { get; set; }
        public RectSpec[]? fences { get; set; }
        public RectSpec[]? gatePosts { get; set; }
        public GateMarker? gateMarker { get; set; }
        public PixelStyle? fenceStyle { get; set; }
        public BuildingSpec[]? buildings { get; set; }
        public PropSpec[]? props { get; set; }
        public SpawnSpec[]? spawns { get; set; }
    }

    private struct RectSpec
    {
        public double[]? rect { get; set; }
    }

    private struct GateMarker
    {
        public double[]? rect { get; set; }
        public double[]? color { get; set; }
    }

    private struct DoorSpec
    {
        public string? side { get; set; }
        public double gapStart { get; set; }
        public double gapWidth { get; set; }
    }

    private sealed class BuildingSpec
    {
        public string? name { get; set; }
        public double[]? rect { get; set; }
        public double wallThickness { get; set; }
        public DoorSpec? door { get; set; }
        public double[]? wallColor { get; set; }
        public bool roof { get; set; }
        public double[]? roofColor { get; set; }
    }

    private struct PropSpec
    {
        public string? name { get; set; }
        public double[]? rect { get; set; }
        public double[]? color { get; set; }
        public double tile { get; set; }
        public double jitter { get; set; }
    }

    private struct SpawnSpec
    {
        public string? name { get; set; }
        public double[]? pos { get; set; }
        public bool pistol { get; set; }
        public double[]? color { get; set; }
    }

    private struct DayNightRaw
    {
        public double dayLengthSeconds { get; set; }
        public double nightLengthSeconds { get; set; }
        public bool startAtNight { get; set; }
        public double[]? dayColor { get; set; }
        public double[]? nightColor { get; set; }
        public double twilightFraction { get; set; }

        public static DayNightRaw Default() => new()
        {
            dayLengthSeconds = 720,
            nightLengthSeconds = 480,
            startAtNight = false,
            twilightFraction = 0.12,
        };
    }
}
