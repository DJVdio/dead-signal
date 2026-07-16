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
/// 🔴 REUSABLE：军袭/40 天尸潮**坏结局**（单人半残南逃）+ **举家南逃 WIN 好结局**（全营列队向南）共用本关——
/// 走廊几何完全一致，只按 <see cref="FamilyMode"/> 分叉**演出**（放置人数/相机取景/终点触发条件/峡谷美术）：
///   · 单人坏结局（默认 FamilyMode=false）：放 <see cref="ExplorationLevel.ExpeditionTeam"/>[0] 一人、相机跟单人、
///     首个 Pawn 踏入终点即触发、峡谷＝「未落下的大桥」+「两个哨兵冷眼」（<see cref="BuildCanyonProps"/> dark 分支）。
///   · 举家 WIN（FamilyMode=true）：放**全员**（列成一列）、跟随者自动跟排头、相机取景**全队**、**全员到齐**才触发、
///     峡谷＝「大桥落下」+「迎接者」（<see cref="BuildCanyonProps"/> WIN 分支）。
/// 美术为**占位**（用户日后 author）：密林＝两侧深色障碍带夹出中央窄道。
/// 自包含（仿 <see cref="TestExploration"/>）：自建地形/相机/导航/终点区，不铺发现点、不放敌人、不铺战斗。
/// </para>
/// </summary>
public sealed partial class EscapeCorridor : ExplorationLevel
{
    /// <summary>
    /// 全员行军模式（举家南逃 WIN）：放全员、跟随者自动跟排头、相机取景全队、**全员到齐**才触发终点。
    /// 默认 false＝单人坏结局（放 [0]、跟单人、首个到达即触发）——CampMain 在 Initialize 前置位。
    /// </summary>
    public bool FamilyMode;
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
    private Pawn? _escapee;                       // 单人坏结局：南逃者 / 全员 WIN：排头（玩家操控者）
    private readonly List<Pawn> _followers = new();// 全员 WIN：跟随者（自动跟排头）
    private readonly List<Pawn> _team = new();     // 全员 WIN：全队（含排头，用于取景/到齐判定）
    private bool _reached; // 终点只触发一次
    private double _followThrottle; // 跟随者重发 CommandMoveTo 的节流累加（避免每帧抖 nav agent）

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
        if (_camera == null || !IsInstanceValid(_camera))
            return;

        if (!FamilyMode)
        {
            // 单人坏结局：相机跟随南逃者（廊道比屏高，须跟随；CG 接管期间 CameraController 自行让位）。
            if (_escapee is { } e && IsInstanceValid(e))
                _camera.FocusOn(e.GlobalPosition);
            return;
        }

        // —— 全员 WIN：跟随者自动跟排头 + 相机取景全队 + 全员到齐判定 ——
        var living = LivingTeam();
        if (living.Count == 0)
            return;

        // 相机取景全队：跟随全队质心（占位口径，全员集中在窄道里，质心即队伍中心）。
        Vector2 centroid = Vector2.Zero;
        foreach (Pawn p in living)
            centroid += p.GlobalPosition;
        centroid /= living.Count;
        _camera.FocusOn(centroid);

        // 跟随者跟排头（节流重发路径令，避免每帧抖 nav agent）。排头由玩家右键操控。
        _followThrottle += delta;
        if (_followThrottle >= 0.35 && _escapee is { } lead && IsInstanceValid(lead))
        {
            _followThrottle = 0;
            for (int i = 0; i < _followers.Count; i++)
            {
                Pawn f = _followers[i];
                if (f == null || !IsInstanceValid(f) || !f.Alive)
                    continue;
                // 跟到排头身后（北侧）一列错位点——排头往南走，跟随者鱼贯而随。
                Vector2 slot = lead.GlobalPosition + new Vector2(((i % 2) * 2 - 1) * 34f, -60f - (i / 2) * 46f);
                f.CommandMoveTo(slot);
            }
        }

        // 全员到齐：所有存活队员都进了终点区 → 触发一次谢幕。
        if (!_reached && AllInCanyonZone(living))
        {
            _reached = true;
            Callable.From(() => OnReachedCanyon?.Invoke()).CallDeferred();
        }
    }

    private List<Pawn> LivingTeam()
    {
        var living = new List<Pawn>(_team.Count);
        foreach (Pawn p in _team)
            if (p != null && IsInstanceValid(p) && p.Alive)
                living.Add(p);
        return living;
    }

    private static bool AllInCanyonZone(IReadOnlyList<Pawn> living)
    {
        float r = CanyonZoneSize * 0.5f;
        foreach (Pawn p in living)
            if (Mathf.Abs(p.GlobalPosition.X - CanyonZonePos.X) > r || Mathf.Abs(p.GlobalPosition.Y - CanyonZonePos.Y) > r)
                return false;
        return true;
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

    // —— 峡谷终点占位美术（authored 占位，用户日后换真美术）——
    //   坏结局 dark：未落下的大桥 + 两个哨兵冷眼；举家 WIN：大桥落下（搭到近岸）+ 迎接者（暖色·招手）。
    private void BuildCanyonProps()
    {
        // 峡谷裂隙（横贯的深渊带·两分支共用）。
        float chasmY = CanyonZonePos.Y + 40f;
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(WallT, chasmY), new Vector2(LevelW - WallT * 2, 90f)),
            Color = new Color(0.02f, 0.02f, 0.03f),
            ZIndex = -8,
        });

        if (FamilyMode)
        {
            // 举家 WIN：大桥**落下**——桥板横搭裂隙、连通近岸对岸（占位）。
            AddChild(new Polygon2D
            {
                Polygon = Quad(new Vector2(LaneCx - 70f, chasmY - 6f), new Vector2(140f, 102f)),
                Color = new Color(0.42f, 0.34f, 0.22f),
                ZIndex = -6,
            });
            // 迎接者（对岸桥头·暖色人形·占位，示意有人来迎）。
            foreach (float dx in new[] { -70f, 0f, 70f })
                AddChild(new Polygon2D
                {
                    Polygon = new Vector2[] { new(0, -18), new(9, 0), new(0, 18), new(-9, 0) },
                    Color = new Color(0.72f, 0.6f, 0.32f),
                    Position = new Vector2(LaneCx + dx, chasmY - 150f),
                    ZIndex = -5,
                });
            return;
        }

        // 坏结局：「大桥没有落下」——对岸一截吊起的桥板（占位，斜置对岸之上，未搭到近岸）。
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
        // 全员 WIN：终点触发改由 _Process「全员到齐」判定，Area2D 单体 BodyEntered 不作数（否则首个到达即误触）。
        if (FamilyMode || _reached || body is not Pawn)
            return;
        _reached = true;
        Callable.From(() => OnReachedCanyon?.Invoke()).CallDeferred();
    }

    private void PlaceEscapee()
    {
        if (ExpeditionTeam == null || ExpeditionTeam.Count == 0)
            return;

        if (!FamilyMode)
        {
            // 单人坏结局：放 [0]、跟单人。
            _escapee = ExpeditionTeam[0];
            _escapee.Position = SpawnPoint;
            _escapee.Reparent(_actorLayer, keepGlobalTransform: false);
            _markers[_escapee] = CreateActorMarker(_escapee, _escapee.BodyTint);
            return;
        }

        // 全员 WIN：放全员，列成一列（排头在南端 SpawnPoint，其余鱼贯在北侧错位）。
        _team.Clear();
        _followers.Clear();
        for (int i = 0; i < ExpeditionTeam.Count; i++)
        {
            Pawn p = ExpeditionTeam[i];
            if (p == null || !IsInstanceValid(p))
                continue;
            // 排头 [0] 在 SpawnPoint；跟随者往北错位排列（窄道内 x 左右交替）。
            p.Position = i == 0
                ? SpawnPoint
                : SpawnPoint + new Vector2(((i % 2) * 2 - 1) * 34f, -60f - ((i - 1) / 2) * 46f);
            p.Reparent(_actorLayer, keepGlobalTransform: false);
            _markers[p] = CreateActorMarker(p, p.BodyTint);
            _team.Add(p);
            if (i == 0) _escapee = p; else _followers.Add(p);
        }
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
