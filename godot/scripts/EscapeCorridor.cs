using System;
using System.Collections.Generic;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 「南逃谢幕」序列的**玩家操作段**关卡（<see cref="ExplorationLevel"/> 子类）：一条**单线密林窄廊道**，
/// 只有一条路往南（伪选择、无别的选择空间），终点是**峡谷前**。南逃者（<see cref="ExplorationLevel.ExpeditionTeam"/>[0]）
/// 由玩家右键操作向南走（复用 CampMain 既有选中+移动机制，level 通用）；踏入终点区触发 <see cref="OnReachedCanyon"/>，
/// 由 CampMain 播 CG-B（峡谷前谢幕）→ 黑屏谢幕。
///
/// <para>
/// 🔴 REUSABLE：军袭结局 + 将来「40 天无限尸潮」结局共用本关（走廊几何与谢幕通路与触发上下文无关）。
/// 美术为**占位**（用户日后 author）：密林＝两侧深色障碍带夹出中央窄道；峡谷＝终点区，含占位「未落下的大桥」+「两个哨兵」。
/// 自包含（仿 <see cref="TestExploration"/>）：自建地形/相机（跟随南逃者）/导航/终点区，不铺发现点、不放敌人、不铺战斗。
/// </para>
/// </summary>
public sealed partial class EscapeCorridor : ExplorationLevel
{
    // —— 廊道几何（占位待调）：窄而长（约一屏半），单线往南 ——
    private const float LevelW = 900f;      // 关卡总宽
    private const float LevelH = 2200f;     // 关卡总高（南北纵深，约 1.5 屏）
    private const float LaneHalfW = 130f;   // 中央可走窄道半宽（两侧密林夹出）
    private const float WallT = 40f;        // 边界墙厚
    private static readonly float LaneCx = LevelW / 2f; // 窄道中心线 x

    private static readonly Vector2 SpawnPoint = new(LaneCx, 220f);              // 南逃者出生（北端）
    private static readonly Vector2 CanyonZonePos = new(LaneCx, LevelH - 200f);  // 峡谷终点区（南端）
    private const float CanyonZoneSize = 150f;

    private CameraController _camera = null!;
    private NavigationRegion2D _navRegion = null!;
    private Node2D _actorLayer = null!;
    private Area2D _canyonZone = null!;
    private Pawn? _escapee;
    private bool _reached; // 终点只触发一次

    private readonly List<Rect2> _obstructions = new();
    private readonly Dictionary<Actor, Node2D> _markers = new();

    /// <summary>南逃者踏入峡谷终点区（谢幕触发点）。CampMain 订阅它 → 播 CG-B + EndingPanel。只触发一次。</summary>
    public event Action? OnReachedCanyon;

    public override void Initialize()
    {
        BuildTerrain();
        BuildNavigation();
        SetupCamera();
        SetupCanyonEndpoint();
        PlaceEscapee();
    }

    public override void _Process(double delta)
    {
        // 相机跟随南逃者（廊道比屏高，须跟随；CG 接管期间 CameraController 自行让位）。
        if (_escapee is { } e && IsInstanceValid(e) && _camera != null && IsInstanceValid(_camera))
            _camera.FocusOn(e.GlobalPosition);
    }

    public override void _ExitTree()
    {
        foreach (var kv in _markers)
            if (IsInstanceValid(kv.Value))
                kv.Value.QueueFree();
        _markers.Clear();
    }

    public override void Cleanup()
    {
        // 南逃谢幕是强制终局，本关不"归营"卸载；EndingPanel 接管后唯一出口是重开/退出。留空实现即可。
    }

    // —— 地形：边界墙 + 两侧密林障碍带（夹出中央单线窄道）——
    private void BuildTerrain()
    {
        _actorLayer = new Node2D { Name = "Actors" };

        var ground = new Polygon2D
        {
            Polygon = Quad(Vector2.Zero, new Vector2(LevelW, LevelH)),
            Color = new Color(0.09f, 0.11f, 0.09f), // 深林地表（占位）
            ZIndex = -20,
        };
        AddChild(ground);

        // 四面边界墙。
        AddWall(new Rect2(0, 0, LevelW, WallT), border: true);
        AddWall(new Rect2(0, LevelH - WallT, LevelW, WallT), border: true);
        AddWall(new Rect2(0, 0, WallT, LevelH), border: true);
        AddWall(new Rect2(LevelW - WallT, 0, WallT, LevelH), border: true);

        // 两侧密林障碍带（深色）——把可走区夹成中央 x∈[LaneCx-LaneHalfW, LaneCx+LaneHalfW] 的单线窄道。
        Color forest = new(0.06f, 0.14f, 0.08f);
        float leftW = (LaneCx - LaneHalfW) - WallT;
        float rightX = LaneCx + LaneHalfW;
        float rightW = (LevelW - WallT) - rightX;
        // 终点区（南端峡谷）之前才夹密林——留出终点开阔区。
        float forestBottom = CanyonZonePos.Y - 260f;
        AddWall(new Rect2(WallT, WallT, leftW, forestBottom - WallT), border: false, color: forest);
        AddWall(new Rect2(rightX, WallT, rightW, forestBottom - WallT), border: false, color: forest);

        AddChild(_actorLayer);

        BuildCanyonProps();
    }

    // —— 峡谷终点占位美术：未落下的大桥 + 两个哨兵（authored 占位，用户日后换真美术）——
    private void BuildCanyonProps()
    {
        // 峡谷裂隙（横贯的深渊带）。
        float chasmY = CanyonZonePos.Y + 40f;
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(WallT, chasmY), new Vector2(LevelW - WallT * 2, 90f)),
            Color = new Color(0.02f, 0.02f, 0.03f),
            ZIndex = -8,
        });

        // 「大桥没有落下」：对岸一截吊起的桥板（占位，斜置在裂隙对岸之上，未搭到近岸）。
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(LaneCx - 70f, chasmY - 120f), new Vector2(140f, 60f)),
            Color = new Color(0.32f, 0.26f, 0.18f),
            ZIndex = -6,
        });

        // 两个哨兵（对岸桥头·冷眼看着·占位深色人形）。
        foreach (float dx in new[] { -80f, 80f })
            AddChild(new Polygon2D
            {
                Polygon = new Vector2[] { new(0, -18), new(9, 0), new(0, 18), new(-9, 0) },
                Color = new Color(0.18f, 0.2f, 0.24f),
                Position = new Vector2(LaneCx + dx, chasmY - 150f),
                ZIndex = -5,
            });
    }

    private void AddWall(Rect2 rect, bool border, Color? color = null)
    {
        Color c = color ?? new Color(0.08f, 0.09f, 0.10f);
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100u; // 墙层：挡移动 + 导航 obstruction（同 TestExploration 口径）
        body.CollisionMask = 0u;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        body.AddChild(new Polygon2D { Polygon = Quad(-rect.Size / 2, rect.Size), Color = c, ZIndex = -5 });
        AddChild(body);
        if (!border)
            _obstructions.Add(rect);
    }

    private void BuildNavigation()
    {
        _navRegion = new NavigationRegion2D { NavigationPolygon = BakeNavPoly() };
        AddChild(_navRegion);
    }

    private NavigationPolygon BakeNavPoly()
    {
        var navPoly = new NavigationPolygon { AgentRadius = ExplorationWalls.NavAgentRadius };
        var src = new NavigationMeshSourceGeometryData2D();
        float inset = 22f;
        src.AddTraversableOutline(Quad(new Vector2(inset, inset), new Vector2(LevelW - inset * 2, LevelH - inset * 2)));
        foreach (Rect2 obs in _obstructions)
            src.AddObstructionOutline(Quad(obs.Position, obs.Size));
        NavigationServer2D.BakeFromSourceGeometryData(navPoly, src);
        return navPoly;
    }

    private void SetupCamera()
    {
        _camera = new CameraController { Position = SpawnPoint };
        _camera.SetBounds(new Rect2(0, 0, LevelW, LevelH));
        AddChild(_camera);
        _camera.MakeCurrent();
    }

    private void SetupCanyonEndpoint()
    {
        // 终点区视觉提示（南端·占位）。
        AddChild(new Polygon2D
        {
            Polygon = Quad(Vector2.Zero, new Vector2(CanyonZoneSize, CanyonZoneSize)),
            Color = new Color(0.5f, 0.45f, 0.2f, 0.28f),
            Position = CanyonZonePos - new Vector2(CanyonZoneSize / 2f, CanyonZoneSize / 2f),
            ZIndex = 9,
        });

        _canyonZone = new Area2D { Position = CanyonZonePos };
        _canyonZone.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(CanyonZoneSize, CanyonZoneSize) } });
        _canyonZone.CollisionMask = 0b0001u; // 侦测 Pawn（同 TestExploration 返回区口径）
        AddChild(_canyonZone);
        _canyonZone.BodyEntered += OnCanyonBodyEntered;
    }

    private void OnCanyonBodyEntered(Node2D body)
    {
        if (_reached || body is not Pawn)
            return;
        _reached = true;
        Callable.From(() => OnReachedCanyon?.Invoke()).CallDeferred();
    }

    private void PlaceEscapee()
    {
        if (ExpeditionTeam == null || ExpeditionTeam.Count == 0)
            return;
        _escapee = ExpeditionTeam[0];
        _escapee.Position = SpawnPoint;
        _escapee.Reparent(_actorLayer, keepGlobalTransform: false);
        _markers[_escapee] = CreateActorMarker(_escapee, _escapee.BodyTint);
    }

    private static Node2D CreateActorMarker(Actor actor, Color color)
    {
        var marker = new Polygon2D
        {
            Polygon = new Vector2[] { new(0, -10), new(10, 0), new(0, 10), new(-10, 0) },
            Color = color,
            ZIndex = 10,
        };
        actor.AddChild(marker);
        return marker;
    }

    private static Vector2[] Quad(Vector2 pos, Vector2 size) => new[]
    {
        pos,
        new Vector2(pos.X + size.X, pos.Y),
        pos + size,
        new Vector2(pos.X, pos.Y + size.Y),
    };
}
