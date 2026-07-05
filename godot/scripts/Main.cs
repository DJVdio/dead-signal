using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 场景总装 + RimWorld 式操控中枢。
///
/// 负责：搭俯视小地图（地面 + 墙 + 导航烘焙）、昼夜色调（CanvasModulate）、生成幸存者/丧尸、
/// 处理选中/框选/右键指令/时间档位/暂停/debug 快进，并每帧刷新 HUD 与环境色。
/// </summary>
public sealed partial class Main : Node2D
{
    // 地图世界范围（像素）。
    private static readonly Rect2 MapBounds = new(0, 0, 1600, 1200);
    private const float NavInset = 20f;

    private readonly List<Rect2> _wallRects = new();
    private readonly List<Pawn> _survivors = new();
    private readonly List<Zombie> _zombies = new();
    private readonly HashSet<Pawn> _selected = new();

    private CombatEngine _combat = null!;
    private GameClock _clock = null!;
    private CameraController _camera = null!;
    private CanvasModulate _ambient = null!;
    private Hud _hud = null!;
    private Node2D _actorLayer = null!;

    // 寻路连通性自检：跨墙探针（起点/终点分居某堵墙两侧），导航就绪后跑一次。
    private bool _navTested;
    private int _navWarmupFrames;

    // 框选状态。
    private bool _dragging;
    private Vector2 _dragStartScreen;
    private Vector2 _dragStartWorld;
    private const float DragThreshold = 6f;

    public override void _Ready()
    {
        _combat = new CombatEngine();

        BuildWorld();
        BuildNavigation();

        _clock = new GameClock();
        AddChild(_clock);
        _clock.Configure(LoadDayNightConfig());

        _ambient = new CanvasModulate();
        AddChild(_ambient);

        _camera = new CameraController { Position = MapBounds.GetCenter() };
        _camera.SetBounds(new Rect2(MapBounds.Position + new Vector2(120, 120),
            MapBounds.Size - new Vector2(240, 240)));
        AddChild(_camera);
        _camera.MakeCurrent();

        _hud = new Hud();
        AddChild(_hud);

        SpawnActors();
    }

    // ---------------- 建图 ----------------

    private void BuildWorld()
    {
        _actorLayer = new Node2D { Name = "Actors" };

        // 地面。
        var ground = new Polygon2D
        {
            Polygon = RectPoints(MapBounds),
            Color = new Color(0.17f, 0.19f, 0.17f),
            ZIndex = -20,
        };
        AddChild(ground);

        // 简单网格线，增强俯视空间感。
        var grid = new GridLines { Bounds = MapBounds, ZIndex = -19 };
        AddChild(grid);

        // 边界墙（挡住走出地图）。
        float t = 20f;
        AddWall(new Rect2(0, 0, MapBounds.Size.X, t), border: true);
        AddWall(new Rect2(0, MapBounds.Size.Y - t, MapBounds.Size.X, t), border: true);
        AddWall(new Rect2(0, 0, t, MapBounds.Size.Y), border: true);
        AddWall(new Rect2(MapBounds.Size.X - t, 0, t, MapBounds.Size.Y), border: true);

        // 内部障碍（构成寻路绕行）。
        AddWall(new Rect2(400, 280, 40, 420));
        AddWall(new Rect2(400, 280, 480, 40));
        AddWall(new Rect2(900, 720, 40, 380));
        AddWall(new Rect2(1040, 480, 360, 40));
        AddWall(new Rect2(1150, 480, 40, 300));

        AddChild(_actorLayer);
    }

    private void AddWall(Rect2 rect, bool border = false)
    {
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100; // 层 3 = 墙
        body.CollisionMask = 0;
        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = rect.Size },
        };
        body.AddChild(shape);

        var vis = new Polygon2D
        {
            Polygon = RectPoints(new Rect2(-rect.Size / 2, rect.Size)),
            Color = border ? new Color(0.1f, 0.11f, 0.12f) : new Color(0.3f, 0.31f, 0.34f),
            ZIndex = -5,
        };
        body.AddChild(vis);

        AddChild(body);
        if (!border)
        {
            _wallRects.Add(rect);
        }
    }

    private void BuildNavigation()
    {
        var region = new NavigationRegion2D();
        var navPoly = new NavigationPolygon { AgentRadius = 14f };

        // 外框（可行走区域，从边界内缩进）。
        Rect2 outer = new(
            MapBounds.Position + new Vector2(NavInset, NavInset),
            MapBounds.Size - new Vector2(NavInset * 2, NavInset * 2));

        // 导航洞修复（与 CampMain/B1 同款根因）：旧代码用 navPoly.AddOutline(墙洞) + 空 source geometry
        // 烘焙——**AddOutline 的洞被 BakeFromSourceGeometryData 忽略**，整张地图是一块无洞网格，
        // 寻路走直线穿墙、agent 撞 StaticBody 卡死。正解：把可行走区作 traversable outline、
        // 每堵墙作 obstruction outline 喂给 source geometry，再烘焙——真正在网格里挖出墙洞。
        var src = new NavigationMeshSourceGeometryData2D();
        src.AddTraversableOutline(RectPoints(outer));
        foreach (Rect2 w in _wallRects)
        {
            src.AddObstructionOutline(RectPoints(w.Grow(4f))); // 外扩 4px 缓冲，agent 不贴墙
        }

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, src);
        region.NavigationPolygon = navPoly;
        AddChild(region);
    }

    /// <summary>
    /// 跨墙寻路连通性自检：几组起点/终点分居某堵内墙两侧，直线必穿墙。求路后既查能否抵达终点，
    /// 又沿路径密集采样验证**不穿墙**。穿墙 = 导航洞没生效（寻路走直线撞墙卡死，正是 B1 那个 bug）。
    /// </summary>
    private void VerifyNavConnectivity()
    {
        // 探针：(名称, 起点, 终点)，均在可行走区内、分居某堵内墙两侧（直线连线必穿该墙）。
        (string Name, Vector2 Start, Vector2 End)[] probes =
        {
            ("竖墙#1(400,280,40,420)", new Vector2(300, 500), new Vector2(600, 500)),
            ("竖墙#3(900,720,40,380)", new Vector2(850, 900), new Vector2(1050, 900)),
            ("竖墙#5(1150,480,40,300)", new Vector2(1080, 600), new Vector2(1300, 600)),
        };

        Rid map = GetWorld2D().NavigationMap;
        foreach ((string name, Vector2 start, Vector2 end) in probes)
        {
            Vector2[] path = NavigationServer2D.MapGetPath(map, start, end, true);
            float endDist = path.Length > 0 ? path[^1].DistanceTo(end) : -1f;
            bool reaches = path.Length > 0 && endDist < 40f;
            bool crossesWall = PathCrossesWall(path);
            bool ok = reaches && !crossesWall;
            GD.Print($"[Nav] 跨墙连通 {name}: {(ok ? "OK" : "FAIL")}  终点距 {endDist:0.0}px，" +
                     $"路径点 {path.Length}，穿墙 {(crossesWall ? "是(导航洞失效!)" : "否")}");
        }
    }

    /// <summary>沿折线密集采样，任一采样点落在内墙矩形内 → 路径穿墙（导航洞未生效）。</summary>
    private bool PathCrossesWall(Vector2[] path)
    {
        for (int i = 0; i + 1 < path.Length; i++)
        {
            Vector2 a = path[i], b = path[i + 1];
            int steps = Mathf.Max(1, (int)(a.DistanceTo(b) / 8f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 pt = a.Lerp(b, (float)s / steps);
                foreach (Rect2 w in _wallRects)
                {
                    if (w.HasPoint(pt))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // ---------------- 生成单位 ----------------

    private void SpawnActors()
    {
        var a = Pawn.Create("阿枪", usePistol: true, new Color(0.4f, 0.65f, 0.95f));
        a.Position = new Vector2(200, 900);
        AddActor(a);
        _survivors.Add(a);

        var b = Pawn.Create("阿匕", usePistol: false, new Color(0.55f, 0.8f, 0.95f));
        b.Position = new Vector2(260, 960);
        AddActor(b);
        _survivors.Add(b);

        Vector2[] zpos =
        {
            new(1300, 300),
            new(1200, 950),
            new(700, 200),
        };
        Rect2 wanderBounds = new(
            MapBounds.Position + new Vector2(80, 80),
            MapBounds.Size - new Vector2(160, 160));
        foreach (Vector2 p in zpos)
        {
            var z = Zombie.Create(wanderBounds, () => _survivors.Cast<Actor>());
            z.Position = p;
            AddActor(z);
            _zombies.Add(z);
        }
    }

    private void AddActor(Actor actor)
    {
        actor.Inject(_combat, _clock);
        actor.Died += OnActorDied;
        _actorLayer.AddChild(actor);
    }

    private void OnActorDied(Actor actor)
    {
        if (actor is Pawn p)
        {
            _survivors.Remove(p);
            _selected.Remove(p);
        }
        else if (actor is Zombie z)
        {
            _zombies.Remove(z);
        }
    }

    // ---------------- 每帧刷新 ----------------

    // 导航烘焙完成后跑一次跨墙连通性自检（走物理帧：headless 下 idle _Process 可能不被 pump）。
    public override void _PhysicsProcess(double delta)
    {
        if (_navTested)
        {
            return;
        }
        Rid map = GetWorld2D().NavigationMap;
        if (NavigationServer2D.MapGetIterationId(map) != 0)
        {
            // 迭代 id 非 0 后再多等几帧，确保 region 网格已同步进 map（否则求路会得空路径）。
            if (++_navWarmupFrames < 5)
            {
                return;
            }
            VerifyNavConnectivity();
            _navTested = true;
        }
    }

    public override void _Process(double delta)
    {
        _ambient.Color = _clock.CurrentAmbientColor();
        int alive = _zombies.Count(z => z.Alive);
        string phase = _clock.IsNight ? "夜" : "昼";
        _hud.SetStatus(
            $"第 {_clock.Day} 天  {_clock.ClockString()}  [{phase}]   速度 {_clock.SpeedLabel()}   " +
            $"幸存者 {_survivors.Count}  丧尸 {alive}");
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
                _dragStartWorld = GetGlobalMousePosition();
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
            IssueCommand(GetGlobalMousePosition());
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
            Vector2 endWorld = GetGlobalMousePosition();
            Rect2 world = RectFrom(_dragStartWorld, endWorld);
            foreach (Pawn p in _survivors.Where(p => world.HasPoint(p.GlobalPosition)))
            {
                Select(p);
            }
        }
        else
        {
            Pawn? hit = _survivors
                .Where(p => p.GlobalPosition.DistanceTo(_dragStartWorld) <= p.Radius + 6)
                .OrderBy(p => p.GlobalPosition.DistanceTo(_dragStartWorld))
                .FirstOrDefault();
            if (hit != null)
            {
                Select(hit);
            }
        }
    }

    private void IssueCommand(Vector2 worldPos)
    {
        if (_selected.Count == 0)
        {
            return;
        }

        // 右键点到丧尸 → 攻击；否则移动。
        Zombie? enemy = _zombies
            .Where(z => z.Alive && z.GlobalPosition.DistanceTo(worldPos) <= z.Radius + 8)
            .OrderBy(z => z.GlobalPosition.DistanceTo(worldPos))
            .FirstOrDefault();

        if (enemy != null)
        {
            foreach (Pawn p in _selected)
            {
                p.CommandAttack(enemy);
            }
            return;
        }

        // 多人移动时环形散开，避免堆叠。
        var list = _selected.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            Vector2 offset = Vector2.Zero;
            if (list.Count > 1)
            {
                float ang = Mathf.Tau * i / list.Count;
                offset = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 26f;
            }
            list[i].CommandMoveTo(worldPos + offset);
        }
    }

    private void Select(Pawn p)
    {
        _selected.Add(p);
        p.Selected = true;
        p.QueueRedraw();
    }

    private void ClearSelection()
    {
        foreach (Pawn p in _selected)
        {
            p.Selected = false;
            p.QueueRedraw();
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

    private GameClock.Config LoadDayNightConfig()
    {
        DayNightRaw raw = default;
        bool loaded = false;
        const string path = "res://data/daynight.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    raw = JsonSerializer.Deserialize<DayNightRaw>(f.GetAsText());
                    loaded = true;
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"daynight.json 解析失败，用默认值：{e.Message}");
                }
            }
        }
        if (!loaded)
        {
            raw = DayNightRaw.Default();
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

    private static Color ToColor(double[]? c, Color fallback) =>
        c is { Length: >= 3 } ? new Color((float)c[0], (float)c[1], (float)c[2]) : fallback;

    // JSON 映射（字段名对应 daynight.json，故意小写）。
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
