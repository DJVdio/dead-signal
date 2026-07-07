using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace DeadSignal.Godot;

/// <summary>
/// 幸存者营地主场景（初级地图）：山凹里的农庄，三面环山、一面栅栏+大门，易守难攻。
///
/// 【等距 Batch 1（世界视觉精致化）】在 B0 双层地基上把块状换成精致等距：
///  - LogicLayer（Visible=false）：StaticBody2D 墙 / CharacterBody2D actors / RoofFade Area2D，
///    坐标 = camp.json cartesian，物理/寻路/碰撞原封不动。
///  - IsoLayer（YSortEnabled）：iso 视觉——地面菱形地块、建筑地基+墙立面+屋顶、道具、门框/门槛。
/// 遮挡物用抬起的立体块（顶面平铺 + 前向左右立面做假 3D，见 <see cref="IsoTilePanel"/>），长墙/大建筑
/// 切小块各自 YSort 修 B0 擦身错序。屋顶常态 50% 半透、角色进入 80% 透（<see cref="RoofFade"/>）。
///
/// 角色视觉由 B2 的 ActorSprite 承担（Actor 自行经 "iso_layer" group 挂到 IsoLayer）。
/// 与 <see cref="Main"/> 刻意不抽公共基类（避免动战斗侧）。
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

    // 门缝连通性自检目标（建筑名 + 室内中心 cartesian），首个可用导航帧跑一次。
    private readonly List<(string name, Vector2 target)> _navTargets = new();
    private bool _navTested;

    private CombatEngine _combat = null!;
    private GameClock _clock = null!;
    private ExplorationLevel? _currentLevel;
    private Node? _levelRoot;
    private NavigationRegion2D _campNavRegion = null!;
    private PawnRoleManager _roleManager = null!;
    private CameraController _camera = null!;
    private CanvasModulate _ambient = null!;
    private Hud _hud = null!;
    private CharacterPanel _characterPanel = null!;  // 角色检视面板（挂 HUD CanvasLayer，不随相机移动）

    private WorldMapPanel _worldMapPanel = null!;
    private ExpeditionPanel _expeditionPanel = null!;
    private GuardPanel _guardPanel = null!;
    private ReturnWarningPopup _returnWarningPopup = null!;
    private MealPanel _mealPanel = null!;
    private CampResources _resources = null!;
    private MealBubblePool _bubblePool = null!;
    // 剧情/世界 flag 存储：condition 谓词只读它、气泡 triggers 写它，推动用户手写剧情走向。
    // 内容（哪些 flag、取什么值）全部来自 meal_bubbles.json 里用户填的数据；此处仅持有存储实例。
    private readonly StoryFlags _storyFlags = new StoryFlags();
    // 近期已故名单：死者已从 _survivors 移除，这里另存**死亡当刻拍的快照**供聚餐气泡"死亡反应"读取（dead 谓词命中）。
    // 死时即拍（不持 Pawn 引用，避免节点被回收后 Inspect 失效）；每餐结算末清空——死亡只在紧随其后的一餐被提及（"近期"）。
    private readonly List<PawnSnapshot> _recentlyDeceased = new();
    private string _pendingDestination = "";
    private int _pendingTravelTime;

    private Node2D _logicLayer = null!;  // 物理/导航平面（cartesian，不可见）
    private Node2D _isoLayer = null!;     // 视觉层（iso，YSort）
    private Node2D _actorLayer = null!;   // actors（LogicLayer 下）

    // ---------------- D 守卫防御战 ----------------
    // 已建岗位（含预置 + 调试放置）。哨塔/屋顶=实心结构；暗哨=非碰撞站位标记。
    private readonly List<GuardPostInstance> _guardPosts = new();
    private readonly List<Zombie> _raidZombies = new();  // 当前袭营波次（存活）
    private readonly List<Pawn> _raidGuards = new();       // 本夜上岗守卫（存活）
    private bool _raidActive;
    private float _raidIntensity = 1f;
    private GuardPostKind _debugPlaceKind = GuardPostKind.Watchtower; // 调试放置轮换类型
    private const float BreachRadius = 280f;  // 破防线：丧尸摸进营心此半径内 = 破防（拟定待调）

    // ---------------- 教学关：克莉丝汀反水（第 2 夜脚本人类袭击，自成一路，与丧尸袭营互斥）----------------
    private readonly List<Raider> _tutorialRaiders = new();  // 场上普通劫掠者（不含克莉丝汀）
    private Raider? _christine;                              // 克莉丝汀（Raider 实例；反水前敌、反水后友、招募后转 Pawn 移除）
    private bool _tutorialActive;                            // 教学关战斗进行中（逐帧 UpdateCristineTutorial）
    private bool _christineTurned;                           // 克莉丝汀是否已反水（切 Survivor 阵营）
    private const int TutorialRaiderCount = 2;               // 固定生成 2 个劫掠者（不走 RaidWave 概率）

    /// <summary>单个已建岗位：类型 + 属性 + 守卫驻守站位（cartesian，须可寻路）。</summary>
    private sealed class GuardPostInstance
    {
        public GuardPostKind Kind;
        public GuardPostStats Stats;
        public Vector2 StandPos;
        public string Name = "";
    }

    private CampConfig _cfg = new();
    private Heights _heights;

    // 框选状态。_dragStartWorldIso = 按下时的 iso 屏幕世界坐标（框选用），点选另行反投影。
    private bool _dragging;
    private Vector2 _dragStartScreen;
    private Vector2 _dragStartWorldIso;
    private const float DragThreshold = 6f;

    private static readonly Vector2[] SleepPositions =
    {
        new(450, 410),
        new(1110, 405),
    };

    // z 分层：地面 → 建筑地基/门槛 → 立体遮挡物/角色(YSort) → 屋顶。
    private const int ZGround = -20;
    private const int ZFloor = -18;
    private const int ZThreshold = -16;
    private const int ZWorld = 0;
    private const int ZRoof = 30;

    // 立体块切分粒度（cartesian 像素）——越小 YSort 越准但越碎/越多节点。「拟定待调」
    private const float CellWall = 96f;
    private const float CellMountain = 220f;
    private const float CellFence = 140f;

    public override void _Ready()
    {
        _combat = new CombatEngine();
        _cfg = LoadCampConfig();
        _mapBounds = ToRect(_cfg.mapBounds) ?? _mapBounds;
        _navInset = _cfg.navInset > 0 ? (float)_cfg.navInset : _navInset;
        _cameraCenter = ToVec(_cfg.cameraCenter) ?? _mapBounds.GetCenter();
        _heights = Heights.From(_cfg.heights);

        VerifyIsoRoundtrip();

        _isoLayer = new Node2D { Name = "IsoLayer", YSortEnabled = true };
        // B2 的 Actor 会用 GetTree().GetFirstNodeInGroup("iso_layer") 找到本层挂 ActorSprite。
        _isoLayer.AddToGroup("iso_layer");
        AddChild(_isoLayer);
        _logicLayer = new Node2D { Name = "LogicLayer", Visible = false };
        AddChild(_logicLayer);

        BuildWorld();
        BuildNavigation();

        _clock = new GameClock();
        AddChild(_clock);
        _clock.Configure(LoadDayNightConfig());
        _clock.OnPhaseChanged += OnGamePhaseChanged;

        _worldMapPanel = new WorldMapPanel();
        AddChild(_worldMapPanel);
        _worldMapPanel.Visible = false;
        _worldMapPanel.DestinationSelected += OnWorldMapDestinationSelected;
        _worldMapPanel.Cancelled += () => _worldMapPanel.Visible = false;

        _expeditionPanel = new ExpeditionPanel();
        AddChild(_expeditionPanel);
        _expeditionPanel.Visible = false;
        _expeditionPanel.SelectDestinationRequested += () => _worldMapPanel.Visible = true;
        _expeditionPanel.ExpeditionConfirmed += OnExpeditionConfirmed;

        _guardPanel = new GuardPanel();
        AddChild(_guardPanel);
        _guardPanel.Visible = false;
        _guardPanel.GuardConfirmed += OnGuardConfirmed;

        _returnWarningPopup = new ReturnWarningPopup();
        AddChild(_returnWarningPopup);
        _returnWarningPopup.Visible = false;
        _returnWarningPopup.ReturnNow += OnReturnNow;
        _clock.OnExploreWarning += OnExploreWarning;

        _resources = LoadCampResources();
        _bubblePool = LoadMealBubbles();
        _mealPanel = new MealPanel();
        AddChild(_mealPanel);
        _mealPanel.Visible = false;
        _mealPanel.Continued += OnMealContinued;

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

        // 角色面板挂到 HUD 这层 CanvasLayer（UI 层，不随相机变换）；初始隐藏，关闭按钮 → 收起。
        _characterPanel = GD.Load<PackedScene>("res://scenes/CharacterPanel.tscn").Instantiate<CharacterPanel>();
        _hud.AddChild(_characterPanel);
        _characterPanel.HidePanel();
        _characterPanel.CloseRequested += _characterPanel.HidePanel;

        // 战斗日志面板挂到 HUD 层（E③）：自订阅 CombatFeed，_ExitTree 自退订。
        _hud.AddChild(new CombatLogPanel());

        SpawnActors();

        _roleManager = new PawnRoleManager(_survivors, _clock);
        _roleManager.RolesChanged += OnRolesChanged;

        CallDeferred(nameof(StartFirstDay));
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

        // 地面（iso 平铺菱形地块，恒底层）。仅视觉，无碰撞。
        var groundStyle = _cfg.ground ?? new PixelStyle { color = new[] { 0.20, 0.23, 0.18 }, jitter = 0.05 };
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = _mapBounds,
            Style = groundStyle,
            Seed = 1,
            Cell = 64f,
            ZIndex = ZGround,
        });

        // 地图边界墙（纯碰撞，防走出世界；山体已覆盖，无视觉）。
        float t = 20f;
        AddBorderWall(new Rect2(0, 0, _mapBounds.Size.X, t));
        AddBorderWall(new Rect2(0, _mapBounds.Size.Y - t, _mapBounds.Size.X, t));
        AddBorderWall(new Rect2(0, 0, t, _mapBounds.Size.Y));
        AddBorderWall(new Rect2(_mapBounds.Size.X - t, 0, t, _mapBounds.Size.Y));

        // 三面环山（抬起的立体块，切块 YSort）。
        var mtnStyle = _cfg.mountainStyle ?? new PixelStyle { color = new[] { 0.27, 0.32, 0.27 }, jitter = 0.14 };
        foreach (RectSpec m in _cfg.mountains ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(m.rect) is { } r)
            {
                AddSolid(r, mtnStyle, seed: 7, (float)_heights.mountain, CellMountain);
            }
        }

        // 一面栅栏 + 大门缺口（门柱更高）。
        var fenceStyle = _cfg.fenceStyle ?? new PixelStyle { color = new[] { 0.40, 0.30, 0.19 }, jitter = 0.12 };
        foreach (RectSpec f in _cfg.fences ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(f.rect) is { } r)
            {
                AddSolid(r, fenceStyle, seed: 11, (float)_heights.fence, CellFence);
            }
        }
        foreach (RectSpec p in _cfg.gatePosts ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(p.rect) is { } r)
            {
                AddSolid(r, fenceStyle, seed: 13, (float)_heights.post, CellFence);
            }
        }

        // 建筑（地基 + 墙立面 + 门 + 屋顶淡出）。
        foreach (BuildingSpec b in _cfg.buildings ?? System.Array.Empty<BuildingSpec>())
        {
            BuildBuilding(b);
        }

        // 道具（工作台等实心障碍，矮立体块）。
        foreach (PropSpec pr in _cfg.props ?? System.Array.Empty<PropSpec>())
        {
            if (ToRect(pr.rect) is { } r)
            {
                var style = new PixelStyle { color = pr.color, jitter = pr.jitter };
                AddSolid(r, style, seed: 17, (float)_heights.prop, cell: 200f);
            }
        }

        // 守卫岗位（预置点，读 camp.json guardPosts）。哨塔/屋顶=实心结构进 _navHoles（随首次烘焙生效）；暗哨=非碰撞标记。
        BuildGuardPosts();

        _logicLayer.AddChild(_actorLayer);
    }

    // ---------------- D1 岗位建造/放置 ----------------

    /// <summary>读 camp.json 预置岗位一次性建起（首次导航烘焙前，实心结构随之进 _navHoles）。</summary>
    private void BuildGuardPosts()
    {
        _guardPosts.Clear();
        foreach (GuardPostSpec spec in _cfg.guardPosts ?? System.Array.Empty<GuardPostSpec>())
        {
            if (ToVec(spec.stand) is { } stand)
            {
                PlaceGuardPost(ParseKind(spec.kind), stand, rebake: false);
            }
        }
    }

    /// <summary>
    /// 放置一个岗位：实心类型（哨塔/屋顶平台）在站位南侧（朝大门侧）建实心结构块——挡路+挖导航洞+立体视觉，
    /// 站位本身留空地可寻路；非碰撞类型（暗哨）只落一个矮标记，不挡路。<paramref name="rebake"/> 用于运行时放置后重烘焙。
    /// </summary>
    private void PlaceGuardPost(GuardPostKind kind, Vector2 stand, bool rebake)
    {
        GuardPostStats stats = GuardPostStats.For(kind);
        var post = new GuardPostInstance
        {
            Kind = kind,
            Stats = stats,
            StandPos = stand,
            Name = $"{stats.DisplayName}{Circled(_guardPosts.Count + 1)}",
        };

        if (stats.IsSolid)
        {
            // 结构块偏移到站位南侧，避免困住守卫站位（站位保持可寻路）。
            Rect2 structRect = new(stand.X - 24f, stand.Y + 30f, 48f, 48f);
            var style = new PixelStyle
            {
                color = kind == GuardPostKind.Watchtower
                    ? new[] { 0.34, 0.30, 0.24 }
                    : new[] { 0.30, 0.32, 0.36 },
                jitter = 0.10,
            };
            AddSolid(structRect, style, seed: 53, (float)_heights.wall, cell: 48f);
        }
        else
        {
            AddHiddenPostMarker(stand);
        }

        _guardPosts.Add(post);

        // 运行时放置改了 _navHoles（仅实心岗位）→ 必须重烘焙，否则新障碍不挡寻路（已知隐坑）。
        if (rebake && stats.IsSolid)
        {
            RebakeNavigation();
        }
    }

    /// <summary>暗哨站位标记：非碰撞、不挖洞的矮地面标记（纯视觉，深色小块）。</summary>
    private void AddHiddenPostMarker(Vector2 stand)
    {
        var style = new PixelStyle { color = new[] { 0.22, 0.20, 0.16 }, jitter = 0.15 };
        var marker = new Rect2(stand.X - 16f, stand.Y - 16f, 32f, 32f);
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = marker, Style = style, Seed = 59, Cell = 32f, ZIndex = ZThreshold,
        });
    }

    /// <summary>调试放置：把当前轮换类型的岗位放到鼠标反投影落点，随后类型轮换（B 键触发）。</summary>
    private void PlaceGuardPostAtMouse()
    {
        Vector2 cart = Iso.Unproject(GetGlobalMousePosition());
        PlaceGuardPost(_debugPlaceKind, cart, rebake: true);
        GD.Print($"[Raid] 调试放置岗位 {_debugPlaceKind} @ {cart:0}（下次轮换）。");
        _debugPlaceKind = (GuardPostKind)(((int)_debugPlaceKind + 1) % 3);
    }

    private static GuardPostKind ParseKind(string? s) => s switch
    {
        "roof" or "roofPlatform" => GuardPostKind.RoofPlatform,
        "hidden" or "hiddenPost" => GuardPostKind.HiddenPost,
        _ => GuardPostKind.Watchtower,
    };

    private static string Circled(int n) =>
        n is >= 1 and <= 9 ? ((char)('①' + n - 1)).ToString() : n.ToString();

    /// <summary>建筑：地基菱形 + 四面墙立面（门侧留缺口，室内可寻路）+ 门框门槛 + 屋顶淡出。</summary>
    private void BuildBuilding(BuildingSpec b)
    {
        if (ToRect(b.rect) is not { } foot)
        {
            return;
        }
        float wt = b.wallThickness > 0 ? (float)b.wallThickness : 18f;
        float wallH = b.wallHeight > 0 ? (float)b.wallHeight : (float)_heights.wall;

        // 地基/地板（平铺，比墙暗，屋顶半透时可见）。
        var floorStyle = new PixelStyle { color = ScaleColor(b.wallColor, 0.6f), jitter = 0.04 };
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = foot, Style = floorStyle, Seed = 31, Cell = 56f, ZIndex = ZFloor,
        });

        // 墙立面。
        var wallStyle = new PixelStyle { color = b.wallColor, jitter = 0.10 };
        foreach (Rect2 seg in WallSegments(foot, wt, b.door))
        {
            AddSolid(seg, wallStyle, seed: 23, wallH, CellWall);
        }

        // 门可见：门槛地面色带 + 两侧门柱。
        AddDoorDecor(foot, wt, wallH, b.door, b.wallColor);

        if (b.roof)
        {
            // 屋顶：抬到墙高的顶面（无裙边），恒顶层，RoofFade 目标。
            var roofStyle = new PixelStyle { color = b.roofColor, jitter = 0.09 };
            var roof = new IsoTilePanel
            {
                FootprintCart = foot, Style = roofStyle, Seed = 29, Cell = 44f,
                Height = wallH, Facade = false, ZIndex = ZRoof,
            };
            _isoLayer.AddChild(roof);

            // 淡出触发区（Area2D）在 logic 层，cartesian 平面探测幸存者，跨层淡出 iso 屋顶。
            var fade = new RoofFade { Position = foot.Position + foot.Size / 2 };
            Rect2 interiorLocal = new(
                -foot.Size / 2 + new Vector2(wt, wt),
                foot.Size - new Vector2(wt * 2, wt * 2));
            _logicLayer.AddChild(fade);
            fade.Setup(roof, interiorLocal);
        }

        _navTargets.Add((b.name ?? "建筑", foot.GetCenter()));
    }

    /// <summary>门框门槛：门缝处铺一条区分色地面 + 缝两端各一根门柱（纯视觉，不挡路）。</summary>
    private void AddDoorDecor(Rect2 foot, float wt, float wallH, DoorSpec? door, double[]? wallColor)
    {
        if (door is not { } d || d.gapWidth <= 0)
        {
            return;
        }
        float gapStart = (float)d.gapStart, gapW = (float)d.gapWidth;
        const float postW = 12f;
        Rect2 gap, postA, postB;

        switch (d.side)
        {
            case "south":
                gap = new Rect2(foot.Position.X + gapStart, foot.End.Y - wt, gapW, wt);
                postA = new Rect2(gap.Position.X - postW, gap.Position.Y, postW, wt);
                postB = new Rect2(gap.End.X, gap.Position.Y, postW, wt);
                break;
            case "north":
                gap = new Rect2(foot.Position.X + gapStart, foot.Position.Y, gapW, wt);
                postA = new Rect2(gap.Position.X - postW, gap.Position.Y, postW, wt);
                postB = new Rect2(gap.End.X, gap.Position.Y, postW, wt);
                break;
            case "west":
                gap = new Rect2(foot.Position.X, foot.Position.Y + gapStart, wt, gapW);
                postA = new Rect2(gap.Position.X, gap.Position.Y - postW, wt, postW);
                postB = new Rect2(gap.Position.X, gap.End.Y, wt, postW);
                break;
            case "east":
                gap = new Rect2(foot.End.X - wt, foot.Position.Y + gapStart, wt, gapW);
                postA = new Rect2(gap.Position.X, gap.Position.Y - postW, wt, postW);
                postB = new Rect2(gap.Position.X, gap.End.Y, wt, postW);
                break;
            default:
                return;
        }

        // 门槛地面色带（醒目木色），略高于地基。
        var sill = new PixelStyle { color = new[] { 0.62, 0.50, 0.30 }, jitter = 0.06 };
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = gap, Style = sill, Seed = 41, Cell = 40f, ZIndex = ZThreshold,
        });

        // 门柱（矮立体块，纯视觉；深木色）。
        var postStyle = new PixelStyle { color = ScaleColor(wallColor, 0.5f), jitter = 0.05 };
        _isoLayer.AddChild(new IsoTilePanel { FootprintCart = postA, Style = postStyle, Seed = 43, Cell = 48f, Height = wallH, Facade = true, ZIndex = ZWorld });
        _isoLayer.AddChild(new IsoTilePanel { FootprintCart = postB, Style = postStyle, Seed = 47, Cell = 48f, Height = wallH, Facade = true, ZIndex = ZWorld });
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
    /// 加一堵实心矩形：LogicLayer 放 StaticBody2D（墙层，cartesian，挡角色 + 导航挖洞，整块一体），
    /// IsoLayer 放对应的**切块抬起立体块**视觉（逐块 YSort）。物理与视觉解耦、坐标各自平面。
    /// </summary>
    private void AddSolid(Rect2 rect, PixelStyle style, int seed, float height, float cell)
    {
        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100; // 层 3 = 墙（Actor 只与此层碰撞）
        body.CollisionMask = 0;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        _logicLayer.AddChild(body);

        AddOccluderVisual(rect, style, seed, height, cell);
        _navHoles.Add(rect);
    }

    /// <summary>把一个 cartesian 矩形切成 ≤cell 的小块，每块一个抬起立体块节点，逐块 YSort。</summary>
    private void AddOccluderVisual(Rect2 rect, PixelStyle style, int seed, float height, float cell)
    {
        int nx = Mathf.Max(1, Mathf.CeilToInt(rect.Size.X / cell));
        int ny = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y / cell));
        float cw = rect.Size.X / nx, ch = rect.Size.Y / ny;

        for (int iy = 0; iy < ny; iy++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                var sub = new Rect2(rect.Position.X + ix * cw, rect.Position.Y + iy * ch, cw, ch);
                _isoLayer.AddChild(new IsoTilePanel
                {
                    FootprintCart = sub,
                    Style = style,
                    Seed = seed + ix * 7 + iy * 13,
                    Cell = Mathf.Min(cell, 48f),
                    Height = height,
                    Facade = true,
                    ZIndex = ZWorld,
                });
            }
        }
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
        region.NavigationPolygon = BakeNavPoly();
        // 导航区直接挂根节点（cartesian 平面），避开 LogicLayer 不可见性的任何耦合顾虑。
        AddChild(region);
        _campNavRegion = region;
    }

    /// <summary>由当前 _navHoles 烘焙一张导航网格（挖墙洞）。初次建图与运行时放置岗位后重烘焙共用。</summary>
    private NavigationPolygon BakeNavPoly()
    {
        var navPoly = new NavigationPolygon { AgentRadius = 14f };

        Rect2 outer = new(
            _mapBounds.Position + new Vector2(_navInset, _navInset),
            _mapBounds.Size - new Vector2(_navInset * 2, _navInset * 2));

        // 门缝修复根因：B0（与原 Main.cs）用 navPoly.AddOutline + 空 source geometry 烘焙——
        // 这套**根本不挖洞**（AddOutline 的洞被 BakeFromSourceGeometryData 忽略），于是整张地图是一块
        // 无洞网格，寻路走直线穿墙、agent 撞 StaticBody 卡死在墙角找不到门。
        // 正解：把可行走区作 traversable outline、每堵墙作 obstruction outline 喂给 source geometry，
        // 再烘焙——真正在网格里挖出墙洞，门缝成为唯一通路。
        var src = new NavigationMeshSourceGeometryData2D();
        src.AddTraversableOutline(RectPoints(outer));
        foreach (Rect2 hole in _navHoles)
        {
            // 外扩仅 2px（B0 若用 4px + gapWidth 64 会把门缝吃到寻路进不去；本轮 gapWidth 已加宽到 100）。
            src.AddObstructionOutline(RectPoints(hole.Grow(2f)));
        }

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, src);
        return navPoly;
    }

    /// <summary>
    /// 运行时放置实心岗位改了 _navHoles 后重烘焙导航（已知隐坑：不重烘焙则新障碍不挡寻路）。
    /// nav region 同步有一帧滞后，_navTested 复位以在下个就绪帧重跑连通性自检。
    /// </summary>
    private void RebakeNavigation()
    {
        _campNavRegion.NavigationPolygon = BakeNavPoly();
        _navTested = false;
    }

    /// <summary>
    /// 门缝连通性自检：从营地中心到各建筑室内求路。既查终点能否抵达室内，又沿路径采样验证
    /// **不穿墙**（穿墙 = 导航洞没生效 → 寻路走直线撞墙卡死，正是用户反馈的 bug）。
    /// </summary>
    private void VerifyNavConnectivity()
    {
        Rid map = GetWorld2D().NavigationMap;
        Vector2 start = _cameraCenter;
        foreach ((string name, Vector2 target) in _navTargets)
        {
            Vector2[] path = NavigationServer2D.MapGetPath(map, start, target, true);
            float endDist = path.Length > 0 ? path[^1].DistanceTo(target) : -1f;
            bool reaches = path.Length > 0 && endDist < 40f;
            bool crossesWall = PathCrossesWall(path);
            bool ok = reaches && !crossesWall;
            GD.Print($"[Nav] 门缝连通 {name}: {(ok ? "OK" : "FAIL")}  终点距室内 {endDist:0.0}px，" +
                     $"路径点 {path.Length}，穿墙 {(crossesWall ? "是(导航洞失效!)" : "否")}");
        }
    }

    /// <summary>沿折线密集采样，任一采样点落在墙矩形内 → 路径穿墙（导航洞未生效）。</summary>
    private bool PathCrossesWall(Vector2[] path)
    {
        for (int i = 0; i + 1 < path.Length; i++)
        {
            Vector2 a = path[i], b = path[i + 1];
            int steps = Mathf.Max(1, (int)(a.DistanceTo(b) / 8f));
            for (int s = 0; s <= steps; s++)
            {
                Vector2 pt = a.Lerp(b, (float)s / steps);
                foreach (Rect2 w in _navHoles)
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
        }
    }

    private void AddActor(Actor actor)
    {
        actor.Inject(_combat, _clock);
        actor.Died += OnActorDied;
        _actorLayer.AddChild(actor); // LogicLayer 下（不可见，仅物理/寻路）
        // 角色视觉（iso 层的人形 ActorSprite）由 B2 接管：Actor 自行经 "iso_layer" group 挂载。
    }

    private void OnActorDied(Actor actor)
    {
        if (actor is Pawn p)
        {
            _survivors.Remove(p);
            _selected.Remove(p);
            _raidGuards.Remove(p); // 守卫阵亡：移出上岗名单（结算据存活数判守卫全倒）
            // 只**追加**一份死亡当刻快照供下一餐"死亡反应"气泡；不改上面 _survivors/_selected/_raidGuards 的既有清理语义。
            _recentlyDeceased.Add(PawnSnapshot.FromInspection(p.Inspect()));
        }
    }

    // ---------------- 每帧刷新 ----------------

    public override void _Process(double delta)
    {
        _ambient.Color = _clock.CurrentAmbientColor();
        bool exploring = _currentLevel != null;
        _hud.SetStatus(
            $"{(exploring ? "探索" : "营地")}  第 {_clock.Day} 天  {_clock.ClockString()}  [{_clock.CurrentPhase}]   速度 {_clock.SpeedLabel()}   " +
            $"幸存者 {_survivors.Count(s => s.Alive)}");

        if (_returnWarningPopup.Visible && _clock.CurrentPhase == DayPhase.DayExplore)
            _returnWarningPopup.SetRemainingTime(_clock.GetExploreTimeRemaining());

        if (_raidActive)
            UpdateRaid(delta);

        if (_tutorialActive)
            UpdateCristineTutorial(delta);

        // 导航图首帧就绪后跑一次门缝连通性自检。
        if (!_navTested)
        {
            Rid map = GetWorld2D().NavigationMap;
            if (NavigationServer2D.MapGetIterationId(map) != 0)
            {
                VerifyNavConnectivity();
                _navTested = true;
            }
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
            case Key.B:
                // 调试：在鼠标落点放置岗位（类型轮换 哨塔→屋顶平台→暗哨），实心岗位自动重烘焙导航。
                PlaceGuardPostAtMouse();
                break;
            case Key.R:
                // 调试：手动触发一波袭营（仅 NightAct 生效），把防御战跑通；正式由叙事事件调 TriggerRaid。
                TriggerRaid(_raidIntensity);
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
            foreach (Pawn p in _survivors.Where(p => p.IsControllable && boxIso.HasPoint(Iso.Project(p.GlobalPosition))))
            {
                Select(p);
            }
            _characterPanel.HidePanel();  // 框选（含多选）不看单人检视，收起面板。
        }
        else
        {
            // 点选：落点反投影回 cartesian，和 actor 逻辑位置按半径比对。
            Vector2 cart = Iso.Unproject(_dragStartWorldIso);
            Pawn? hit = _survivors
                .Where(p => p.IsControllable && p.GlobalPosition.DistanceTo(cart) <= p.Radius + 8)
                .OrderBy(p => p.GlobalPosition.DistanceTo(cart))
                .FirstOrDefault();
            if (hit != null)
            {
                Select(hit);
                // 命中单人 → 打开/刷新面板（重复调用即切换）；附装假肢入口：面板空槽装假肢即时恢复能力。
                _characterPanel.ShowFor(hit.Inspect(), (region, grade) => hit.EquipProsthetic(region, grade));
            }
            else
            {
                _characterPanel.HidePanel();  // 点空白未命中 → 收起。
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

    private void OnRolesChanged()
    {
        foreach (Pawn p in _selected.Where(p => !p.IsControllable).ToList())
        {
            p.Selected = false;
            _selected.Remove(p);
        }
        if (_selected.Count == 0)
            _characterPanel.HidePanel();
    }

    private async void StartSleepTransition()
    {
        var sleepers = _survivors.Where(p => p.Role == PawnRole.Expedition).ToList();
        if (sleepers.Count == 0)
        {
            _clock.TransitionTo(DayPhase.NightPrep);
            return;
        }

        Engine.TimeScale = 1;

        for (int i = 0; i < sleepers.Count; i++)
        {
            Vector2 spot = SleepPositions[i % SleepPositions.Length];
            sleepers[i].CommandMoveTo(spot);
        }

        while (sleepers.Any(p => !p.IsNavigationFinished()))
        {
            await ToSignal(GetTree().CreateTimer(0.25f), "timeout");
        }

        foreach (var p in sleepers)
        {
            p.Role = PawnRole.Sleeping;
            p.SetSleeping(true);
        }

        _clock.TransitionTo(DayPhase.NightPrep);
    }

    // ---------------- 聚餐 ----------------

    private void EnterDuskMeal() => _clock.TransitionTo(DayPhase.DuskMeal);

    /// <summary>
    /// 一次聚餐（发生在昼夜相位切换点，一天两次）：全员用餐扣食物（不足则士气下降）+ 触发气泡，弹出模态面板。
    /// 饥饿模型（净零）：本次切换全员饥饿刻度无条件 -1；吃到饭者 +1（clamp 到各自上限）——吃满两餐即维持。
    /// 结算后：按各存活者刻度累加饥饿士气下降；刻度归 0 者饿死（走统一死亡路径）。
    /// </summary>
    private void RunMeal(string title, string phaseTag)
    {
        var diners = _survivors.Where(s => s.Alive).ToList();
        MealOutcome outcome = _resources.ConsumeMeal(diners.Count);

        // 昼夜切换净结算：一次性施加"无条件 -1，吃到再 +1"（吃满两餐净零维持）。
        // 用 ResolvePhase 一步算净变化 + clamp，避免旧两步"1→0 途中 Feed 被短路"的跨 0 误杀。
        var hungerNotes = new List<string>();
        for (int i = 0; i < diners.Count; i++)
        {
            bool ate = i < outcome.Served;
            diners[i].ResolveHungerPhase(ate);
            if (!ate && diners[i].Hunger.Level < HungerLevel.Sated)
            {
                hungerNotes.Add($"{diners[i].DisplayName}（{diners[i].Hunger.Level.Label()}）");
            }
        }

        // 饥饿士气下降（越饿越重，阶梯见 HungerState.MoraleFor）：按结算后各存活者刻度累加一次扣减。
        double hungerMorale = diners.Sum(d => d.Hunger.MoralePenaltyPerPhase);
        if (hungerMorale > 0)
        {
            _resources.ApplyHungerMorale(hungerMorale);
        }

        // 饿死：刻度归 0 者走统一死亡路径（Died 事件会改 _survivors，先收集再逐个处理）。
        foreach (var starved in diners.Where(d => d.IsStarvedToDeath).ToList())
        {
            starved.StarveToDeath();
        }

        // 构造"世界只读快照"喂条件驱动选择器：相位 + 当前 flags + 存活者真实状态 + 食物/士气。
        // 角色状态只读引擎真实状态（经 Inspect→PawnSnapshot），不发明新状态、不做关系/性格。
        // 在场存活者 + 近期已故者的快照都放进 Pawns：前者供伤/饥饿谓词，后者供 dead 死亡反应谓词。
        var pawnSnapshots = _survivors.Where(s => s.Alive)
                                      .Select(s => PawnSnapshot.FromInspection(s.Inspect()))
                                      .Concat(_recentlyDeceased) // 近期已故快照（死时已拍好）
                                      .ToList();
        var context = new MealWorldContext
        {
            Phase = phaseTag,
            Flags = _storyFlags,
            Pawns = pawnSnapshots,
            Food = _resources.Food,
            Morale = _resources.Morale,
        };
        var bubbles = _bubblePool.Pick(context, 3);
        // 应用选中气泡的 triggers（改 flags）——推动剧情；选择器不隐式改 flag，故独立成步。
        MealBubblePool.ApplyTriggers(bubbles, _storyFlags);
        _recentlyDeceased.Clear(); // 死亡只在紧随其后的一餐被提及，之后归入历史不再复播

        _mealPanel.ShowMeal(title, outcome, bubbles, hungerNotes);
    }

    private void OnMealContinued()
    {
        _mealPanel.Visible = false;
        switch (_clock.CurrentPhase)
        {
            case DayPhase.DawnMeal:
                _clock.TransitionTo(DayPhase.DayPrep);
                break;
            case DayPhase.DuskMeal:
                // 黄昏聚餐结束，交回原睡眠过渡（探险队走到床位就寝 → NightPrep）。
                CallDeferred(nameof(StartSleepTransition));
                break;
        }
    }

    // ---------------- 面板状态机 ----------------

    private void StartFirstDay() => _clock.TransitionTo(DayPhase.DayPrep);

    private void OnGamePhaseChanged(DayPhase phase)
    {
        _expeditionPanel.Visible = false;
        _worldMapPanel.Visible = false;
        _guardPanel.Visible = false;
        _returnWarningPopup.Visible = false;
        _mealPanel.Visible = false;

        switch (phase)
        {
            case DayPhase.DawnMeal:
                // 黎明聚餐：全员已在 NightAct 起唤醒并度过实时夜晚，此处结算食物 + 气泡交流，结束进 DayPrep。
                EndRaid(); // 夜晚结束：清残留丧尸、守卫下岗
                RunMeal("黎明聚餐", "dawn");
                break;
            case DayPhase.DayPrep:
                foreach (var p in _survivors)
                    p.SetSleeping(false);
                PopulateExpeditionPanel();
                _expeditionPanel.Visible = true;
                break;
            case DayPhase.DayTravel:
                break;
            case DayPhase.DayExplore:
                LoadExplorationLevel(_pendingDestination);
                break;
            case DayPhase.DayReturn:
                // 探索队已返回，卸载关卡后进入黄昏聚餐（睡眠过渡推迟到聚餐结束）。
                _returnWarningPopup.CancelDelay();
                _returnWarningPopup.Visible = false;
                UnloadExplorationLevel();
                CallDeferred(nameof(EnterDuskMeal));
                break;
            case DayPhase.DuskMeal:
                RunMeal("黄昏聚餐", "dusk");
                break;
            case DayPhase.NightPrep:
                PopulateGuardPanel();
                _guardPanel.Visible = true;
                break;
            case DayPhase.NightAct:
                // 夜晚开始：唤醒全员（含白天留守者），守卫上岗由 PawnRoleManager 按站岗分配置 Guard。
                // 之后夜晚由 GameClock 实时流逝 NightLengthSeconds，到时自动 → DawnMeal（不再瞬跳）。
                foreach (var p in _survivors)
                    p.SetSleeping(false);
                StationGuards(); // D2：守卫走向各自岗位站位并挂上岗位加成
                // 教学关：第 2 夜一次性触发克莉丝汀反水关（StoryFlag 防重入）。这一晚是脚本人类袭击，
                // 不叠加丧尸袭营（TriggerRaid 会因 _tutorialActive 早退）。
                if (_clock.Day == 2 && !_storyFlags.Has("tutorial_raider_started"))
                    BeginCristineTutorial();
                break;
        }
    }

    private void PopulateExpeditionPanel()
    {
        var entries = new List<ExpeditionPanel.PawnEntry>();
        for (int i = 0; i < _survivors.Count; i++)
        {
            var insp = _survivors[i].Inspect();
            entries.Add(new ExpeditionPanel.PawnEntry
            {
                Id = i,
                Name = insp.DisplayName,
                WeaponSummary = insp.Weapon?.Name ?? "徒手",
                ArmorSummary = string.Join(", ", insp.Armor.Select(a => a.Name)),
            });
        }
        _expeditionPanel.SetPawns(entries);
        if (!string.IsNullOrEmpty(_pendingDestination))
            _expeditionPanel.SetDestination(_pendingDestination, _pendingTravelTime);
    }

    private void PopulateGuardPanel()
    {
        // 岗位来自已建岗位（预置 + 调试放置）。PostId = 岗位在 _guardPosts 中的下标。
        var posts = new List<GuardPanel.GuardPostDef>();
        for (int i = 0; i < _guardPosts.Count; i++)
        {
            posts.Add(new GuardPanel.GuardPostDef { PostId = i, Name = _guardPosts[i].Name });
        }
        _guardPanel.SetupPosts(posts);

        var pawnOptions = new List<GuardPanel.PawnOption>();
        for (int i = 0; i < _survivors.Count; i++)
        {
            var insp = _survivors[i].Inspect();
            pawnOptions.Add(new GuardPanel.PawnOption
            {
                Id = i,
                Name = insp.DisplayName,
                EquipmentSummary = insp.Weapon?.Name ?? "徒手",
            });
        }
        _guardPanel.SetPawns(pawnOptions);
    }

    private void OnExpeditionConfirmed(int[] pawnIds, string destination)
    {
        var ids = new HashSet<int>();
        foreach (int idx in pawnIds)
        {
            if (idx >= 0 && idx < _survivors.Count)
                ids.Add(_survivors[idx].Id);
        }
        _roleManager.SetExpeditionIds(ids);
        _clock.TransitionTo(DayPhase.DayTravel);
    }

    private void OnWorldMapDestinationSelected(string name, int travelTime)
    {
        _pendingDestination = name;
        _pendingTravelTime = travelTime;
        _worldMapPanel.Visible = false;
        _expeditionPanel.SetDestination(name, travelTime);
    }

    private void OnExploreWarning()
    {
        if (_clock.CurrentPhase != DayPhase.DayExplore)
            return;
        _returnWarningPopup.ResetWarning();
        _returnWarningPopup.Visible = true;
    }

    private void OnReturnNow()
    {
        _returnWarningPopup.CancelDelay();
        _returnWarningPopup.Visible = false;
        _clock.TransitionTo(DayPhase.DayReturn);
    }

    private void OnExplorationReturn()
    {
        if (_clock.CurrentPhase == DayPhase.DayExplore)
            _clock.TransitionTo(DayPhase.DayReturn);
    }

    private void LoadExplorationLevel(string destinationName)
    {
        if (_levelRoot != null)
            return;

        var levelScene = GD.Load<PackedScene>("res://scenes/TestExploration.tscn");
        _levelRoot = levelScene.Instantiate();
        _currentLevel = (ExplorationLevel)_levelRoot;

        _currentLevel.Clock = _clock;
        // 关卡里新建的丧尸要拿到与营地单位相同的战斗引擎实例（否则 Inject 缺失、首帧 Think 崩）。
        if (_currentLevel is TestExploration testLevel)
            testLevel.Combat = _combat;
        _currentLevel.ExpeditionTeam = _survivors.Where(s => s.Role == PawnRole.Expedition).ToList();

        _currentLevel.OnReturnToCamp += OnExplorationReturn;

        ClearSelection();
        _campNavRegion.Enabled = false;

        GetTree().Root.AddChild(_levelRoot);
        SetCampVisible(false);

        _currentLevel.Initialize();
    }

    private void UnloadExplorationLevel()
    {
        if (_levelRoot == null)
            return;

        _currentLevel!.OnReturnToCamp -= OnExplorationReturn;
        _currentLevel!.Cleanup();

        foreach (Pawn p in _survivors.Where(s => s.Role == PawnRole.Expedition))
        {
            if (_levelRoot.IsAncestorOf(p))
            {
                p.Reparent(_actorLayer, keepGlobalTransform: false);
                p.Position = _cameraCenter;
            }
        }

        _currentLevel = null;
        GetTree().Root.RemoveChild(_levelRoot);
        _levelRoot.QueueFree();
        _levelRoot = null;

        _campNavRegion.Enabled = true;
        _camera.MakeCurrent();
        SetCampVisible(true);
    }

    private void SetCampVisible(bool visible)
    {
        _isoLayer.Visible = visible;
    }

    private void OnGuardConfirmed(Dictionary<int, int> posts)
    {
        var assignments = new Dictionary<int, int>();
        foreach (var kv in posts)
        {
            if (kv.Value >= 0 && kv.Value < _survivors.Count)
                assignments[kv.Key] = _survivors[kv.Value].Id;
        }
        _roleManager.SetGuardAssignments(assignments);
        _clock.TransitionTo(DayPhase.NightAct);
    }

    // ---------------- D2 驻守 AI + 岗位加成 ----------------

    /// <summary>
    /// 夜晚上岗：按 PawnRoleManager 的岗位分配（postId→pawnId），让每个守卫走向岗位站位并挂上岗位加成。
    /// 走向岗位靠 Stationing 令 Pawn.Think Guard 分支放行移动令；抵达即回原地驻守。
    /// </summary>
    private void StationGuards()
    {
        _raidGuards.Clear();
        foreach (var kv in _roleManager.GuardAssignments)
        {
            int postId = kv.Key, pawnId = kv.Value;
            if (postId < 0 || postId >= _guardPosts.Count)
                continue;
            Pawn? guard = _survivors.FirstOrDefault(p => p.Id == pawnId && p.Alive);
            if (guard == null)
                continue;

            GuardPostInstance post = _guardPosts[postId];
            guard.ApplyGuardPost(post.Stats);
            guard.Stationing = true;
            guard.CommandMoveTo(post.StandPos);
            _raidGuards.Add(guard);
        }
    }

    // ---------------- D3 袭营触发/生成 ----------------

    /// <summary>
    /// 公共袭营触发钩子——供未来叙事事件脚本调用（如某夜剧情事件 <c>TriggerRaid(1.5f)</c>）。
    /// 仅 NightAct 生效；波次规模随天数/营地规模递增（<see cref="RaidWave"/>），乘强度系数。
    /// </summary>
    public void TriggerRaid(float intensity = 1f)
    {
        if (_clock.CurrentPhase != DayPhase.NightAct)
        {
            GD.Print("[Raid] 非 NightAct 相位，忽略袭营触发。");
            return;
        }
        if (_raidActive)
            return;
        if (_tutorialActive)
        {
            GD.Print("[Raid] 教学关（克莉丝汀反水）进行中，忽略丧尸袭营，避免两种袭击叠加。");
            return;
        }

        _raidIntensity = intensity;
        int campSize = _survivors.Count(s => s.Alive);
        int count = RaidWave.ZombieCount(_clock.Day, campSize, intensity);
        SpawnCampZombies(count);
        _raidActive = true;
        GD.Print($"[Raid] 第 {_clock.Day} 天袭营：强度 {intensity:0.0}，{count} 只丧尸自大门涌入，上岗守卫 {_raidGuards.Count} 人。");
    }

    /// <summary>照 TestExploration 模板在大门缺口外生成一波丧尸：Inject(_combat,_clock)、挂 _actorLayer、涌入营地。</summary>
    private void SpawnCampZombies(int count)
    {
        // 游荡范围 = 营地内部（丧尸从门外向营心推进，被守卫/破防线拦截）。
        Rect2 wander = new(
            _mapBounds.Position + new Vector2(200, 200),
            _mapBounds.Size - new Vector2(400, 400));

        for (int i = 0; i < count; i++)
        {
            // 门外错峰生成：大门缺口 x∈[730,870]，y 在栅栏(1120)与地图下边界(1180)之间。
            float gx = 730f + (i * 37f) % 140f;
            float gy = 1152f + (i % 3) * 12f;

            var z = Zombie.Create(wander, () => _survivors.Where(a => a.Alive).Cast<Actor>());
            z.Inject(_combat, _clock); // 与营地单位同一 combat+clock，务必首帧 Think 前完成
            z.Position = new Vector2(gx, gy);
            _actorLayer.AddChild(z);
            z.Died += OnRaidZombieDied;
            _raidZombies.Add(z);
        }
    }

    private void OnRaidZombieDied(Actor a)
    {
        if (a is Zombie z)
            _raidZombies.Remove(z);
    }

    // ---------------- D4 结算 ----------------

    /// <summary>
    /// 袭营实时推进（仅 _raidActive 时逐帧调用）：守卫巡防锁敌（暗哨触发一次性首发）+ 破防/胜负检测。
    /// </summary>
    private void UpdateRaid(double delta)
    {
        // 守卫巡防：无目标时取侦测半径内最近丧尸交战（是否真正开火由 Pawn.Think Guard 的射程判定裁决）。
        foreach (Pawn g in _raidGuards)
        {
            if (!g.Alive || g.HasActiveTarget)
                continue;
            Zombie? nearest = null;
            float best = g.GuardSightRadius;
            foreach (Zombie z in _raidZombies)
            {
                if (!z.Alive)
                    continue;
                float d = g.GlobalPosition.DistanceTo(z.GlobalPosition);
                if (d < best)
                {
                    best = d;
                    nearest = z;
                }
            }
            if (nearest != null)
            {
                g.TryFirstStrike(nearest); // 暗哨首发（一次性）
                g.CommandAttack(nearest);
            }
        }

        // 破防线：任一丧尸摸进营心 BreachRadius 内 = 破防。
        bool breached = _raidZombies.Any(z => z.Alive && z.GlobalPosition.DistanceTo(_cameraCenter) < BreachRadius);
        int zombiesRemaining = _raidZombies.Count(z => z.Alive);
        int guardsAlive = _raidGuards.Count(g => g.Alive);

        RaidEvaluation eval = RaidResolution.Evaluate(zombiesRemaining, guardsAlive, breached);
        if (eval.State != RaidState.Ongoing)
            FinishRaid(eval);
    }

    /// <summary>结算收口：守住无损失；被攻破按 <see cref="RaidResolution.ConsequenceFor"/> 扣食物/士气。</summary>
    private void FinishRaid(RaidEvaluation eval)
    {
        _raidActive = false;
        RaidConsequence cons = RaidResolution.ConsequenceFor(eval);
        if (eval.State == RaidState.Overrun)
        {
            _resources.ApplyRaidLoss(cons.FoodLoss, cons.MoraleLoss);
            GD.Print($"[Raid] 防御战失败（{eval.Reason}）：损食物 {cons.FoodLoss}、士气 {cons.MoraleLoss}。伤亡在实时战斗中自然产生。");
        }
        else
        {
            GD.Print("[Raid] 防御战守住：丧尸全灭。");
        }
        // 残留丧尸（破防/守卫全倒时仍在场者）清场，避免拖到黎明。
        foreach (Zombie z in _raidZombies)
        {
            if (IsInstanceValid(z))
                z.QueueFree();
        }
        _raidZombies.Clear();
    }

    /// <summary>夜晚结束：清残留丧尸、守卫下岗撤销加成（DawnMeal 调用）。</summary>
    private void EndRaid()
    {
        _raidActive = false;
        foreach (Zombie z in _raidZombies)
        {
            if (IsInstanceValid(z))
                z.QueueFree();
        }
        _raidZombies.Clear();
        foreach (Pawn g in _raidGuards)
        {
            if (IsInstanceValid(g) && g.Alive)
            {
                g.ClearGuardPost();
                g.Stationing = false;
            }
        }
        _raidGuards.Clear();

        // 极端情况：夜晚耗尽仍未分胜负（教学关战斗未收口）→ 天亮统一清场，防止拖入白天。
        if (_tutorialActive || _tutorialRaiders.Count > 0 || _christine != null)
            CleanupCristineTutorial();
    }

    // ---------------- 教学关：克莉丝汀反水编排 ----------------

    /// <summary>
    /// 第 2 夜脚本化开场：门外固定生成 2 个劫掠者 + 克莉丝汀（皆 Raider 阵营、起手打幸存者）。
    /// 不走 <see cref="RaidWave"/> 概率。生成模板沿用 <see cref="SpawnCampZombies"/>（门缝错峰、Inject、挂 _actorLayer）。
    /// </summary>
    private void BeginCristineTutorial()
    {
        _storyFlags.Set("tutorial_raider_started", "true");
        _tutorialActive = true;
        _christineTurned = false;
        _tutorialRaiders.Clear();

        // 无目标时的门外游荡范围（与丧尸同款：向营心推进被守卫/破防线拦截）。
        Rect2 wander = new(
            _mapBounds.Position + new Vector2(200, 200),
            _mapBounds.Size - new Vector2(400, 400));

        // 2 个普通劫掠者：目标池含幸存者 + 克莉丝汀（反水后克莉丝汀变 Survivor，IsHostile 自动让劫掠者也打她）。
        for (int i = 0; i < TutorialRaiderCount; i++)
        {
            var r = Raider.Create(wander, TutorialRaiderTargets, usePistol: true);
            r.Inject(_combat, _clock); // 首帧 Think 前完成注入
            r.Position = new Vector2(760f + i * 48f, 1152f + (i % 2) * 10f);
            _actorLayer.AddChild(r);
            r.Died += OnTutorialRaiderDied;
            _tutorialRaiders.Add(r);
        }

        // 克莉丝汀：起手 Faction=Raider、targetProvider=幸存者池（打幸存者），与两名同伙一致。
        _christine = Raider.Create(
            wander,
            () => _survivors.Where(a => a.Alive).Cast<Actor>(),
            usePistol: true,
            displayName: "克莉丝汀");
        _christine.Inject(_combat, _clock);
        _christine.Position = new Vector2(830f, 1160f);
        _actorLayer.AddChild(_christine);
        _christine.Died += OnChristineDied;

        GD.Print($"[教学关] 第 {_clock.Day} 夜：{TutorialRaiderCount} 名劫掠者 + 克莉丝汀自大门袭击营地。");
    }

    /// <summary>劫掠者的择敌候选池：存活幸存者 + 克莉丝汀（反水后她变敌对，无需另改劫掠者）。</summary>
    private IEnumerable<Actor> TutorialRaiderTargets()
    {
        foreach (Pawn s in _survivors)
            if (s.Alive)
                yield return s;
        if (_christine is { Alive: true })
            yield return _christine;
    }

    private void OnTutorialRaiderDied(Actor a)
    {
        if (a is Raider r)
            _tutorialRaiders.Remove(r);
    }

    private void OnChristineDied(Actor a)
    {
        // 克莉丝汀战死（Actor.Die 会随后 QueueFree 本节点）：立即置空引用，避免后续帧 use-after-free。
        // 若死在结算前 → 支线不触发（黑暗向，有意为之），FinishCristineTutorial 据 _christine==null 不弹抉择。
        _christine = null;
        GD.Print("[教学关] 克莉丝汀战死。");
    }

    /// <summary>
    /// 逐帧推进教学关：反水监测（未反水时）+ 胜负复用判定。仅 <see cref="_tutorialActive"/> 时调用。
    /// </summary>
    private void UpdateCristineTutorial(double delta)
    {
        // ① 反水监测：任一劫掠者受伤较重 或 克莉丝汀自己受伤 → 翻转。
        if (!_christineTurned && _christine is { Alive: true })
        {
            IEnumerable<float> raiderHps = _tutorialRaiders.Where(r => r.Alive).Select(r => r.HealthFraction);
            if (TutorialRaidLogic.ShouldTurncoat(raiderHps, _christine.HealthFraction))
                TurnChristine();
        }

        // ② 胜负：复用 RaidResolution，把"敌方剩余数"（劫掠者 + 未反水的克莉丝汀）当"敌人数"喂入。
        //    守卫全倒沿用"幸存者存活数"（全灭 = 负）；破防线沿用 BreachRadius。
        bool christineHostile = _christine is { Alive: true } && !_christineTurned;
        int raidersAlive = _tutorialRaiders.Count(r => r.Alive);
        int enemiesRemaining = TutorialRaidLogic.EnemiesRemaining(raidersAlive, christineHostile);
        int defendersAlive = _survivors.Count(s => s.Alive);
        bool breached = _tutorialRaiders.Any(r => r.Alive && r.GlobalPosition.DistanceTo(_cameraCenter) < BreachRadius)
                        || (christineHostile && _christine!.GlobalPosition.DistanceTo(_cameraCenter) < BreachRadius);

        RaidEvaluation eval = RaidResolution.Evaluate(enemiesRemaining, defendersAlive, breached);
        if (eval.State != RaidState.Ongoing)
            FinishCristineTutorial(eval);
    }

    /// <summary>
    /// 反水：头顶飘台词 + 切 Survivor 阵营 + 换目标池为劫掠者 → 她变**盟友 AI**（自动打劫掠者，玩家不可控）。
    /// 劫掠者目标池（<see cref="TutorialRaiderTargets"/>）已含她，切阵营后 IsHostile 自动令双方互为敌。
    /// </summary>
    private void TurnChristine()
    {
        _christineTurned = true;
        FloatingText.Spawn(_actorLayer, _christine!.GlobalPosition,
            "杀死这些劫掠者！我是好人！", new Color(1f, 0.9f, 0.4f));
        _christine.SetFaction(Faction.Survivor);
        _christine.SetTargetProvider(() => _tutorialRaiders.Where(r => r.Alive).Cast<Actor>());
        _christine.CancelOrders(); // 立即丢弃"打幸存者"的旧指令，下一帧 Think 重新对劫掠者择敌
        GD.Print("[教学关] 克莉丝汀反水：切换 Survivor 阵营，转而攻击劫掠者。");
    }

    /// <summary>
    /// 战斗收口：清残留劫掠者、置 done flag、被攻破按 RaidResolution 后果扣资源。
    /// **仅击退(Defended)且克莉丝汀存活**才弹抉择面板；失利(Overrun，含她独活)或她战死 → 支线不触发，直接收尾。
    /// </summary>
    private void FinishCristineTutorial(RaidEvaluation eval)
    {
        _tutorialActive = false;
        foreach (Raider r in _tutorialRaiders)
            if (IsInstanceValid(r))
                r.QueueFree();
        _tutorialRaiders.Clear();
        _storyFlags.Set("tutorial_raider_done", "true");

        if (eval.State == RaidState.Overrun)
        {
            RaidConsequence cons = RaidResolution.ConsequenceFor(eval);
            _resources.ApplyRaidLoss(cons.FoodLoss, cons.MoraleLoss);
            GD.Print($"[教学关] 袭击失利（{eval.Reason}）：损食物 {cons.FoodLoss}、士气 {cons.MoraleLoss}。");
        }
        else
        {
            GD.Print("[教学关] 击退劫掠者。");
        }

        // 仅"击退劫掠者(Defended) 且 克莉丝汀存活"才弹抉择。失利(Overrun，向空营/败局收留无意义)
        // 或她战死 → 不弹，直接收尾。
        if (eval.State == RaidState.Defended && _christine is { Alive: true })
        {
            PromptCristineChoice();
        }
        else
        {
            // 失利或战死：支线不触发（黑暗向）。她若失利仍存活，清掉残留节点，不留孤儿单位。
            if (_christine != null && IsInstanceValid(_christine))
                _christine.QueueFree();
            _christine = null;
        }
    }

    /// <summary>战后暂停（TimeScale=0）弹收留/放逐/处决三选一。</summary>
    private void PromptCristineChoice()
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        panel.ForCristine(
            "劫掠者已被清剿。克莉丝汀瘫坐在血泊边，抬头望向你：\n" +
            "「我不是他们一伙的……是他们逼我带路的。求你，让我留下——我能帮上忙。」");
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            HandleCristineChoice((CristineChoice)v);
            panel.QueueFree();
        };
    }

    private void HandleCristineChoice(CristineChoice choice)
    {
        _storyFlags.Set("tutorial_raider_done", "true");
        if (_christine == null || !IsInstanceValid(_christine))
        {
            _christine = null;
            return;
        }

        switch (choice)
        {
            case CristineChoice.Recruit:
                RecruitChristine();
                break;
            case CristineChoice.Exile:
                _christine.QueueFree();
                _christine = null;
                GD.Print("[教学关] 放逐克莉丝汀：她离开了营地。");
                break;
            case CristineChoice.Execute:
                _christine.QueueFree();
                _christine = null;
                GD.Print("[教学关] 处决克莉丝汀。");
                break;
        }
    }

    /// <summary>
    /// 收留：把克莉丝汀从 Raider-AI 实例转成玩家可控 Pawn 入营——在她原位移除 Raider、
    /// <see cref="Pawn.Create"/> 一个同名 Pawn，走 <see cref="AddActor"/>（Inject + Died 订阅 + 挂 _actorLayer）+ 加入 _survivors。
    /// 置 <c>christine_recruited</c> flag，供气泡框架接后续请求台词（本单不做那部分）。
    /// </summary>
    private void RecruitChristine()
    {
        Vector2 pos = _christine!.GlobalPosition;
        _christine.QueueFree();
        _christine = null;

        var pawn = Pawn.Create("克莉丝汀", usePistol: true, new Color(0.85f, 0.55f, 0.75f));
        pawn.Position = pos; // cartesian，原地入营
        AddActor(pawn);
        _survivors.Add(pawn);

        _storyFlags.Set("christine_recruited", "true");
        GD.Print("[教学关] 收留克莉丝汀：转为可控幸存者入营。");
    }

    /// <summary>教学关未收口时的天亮兜底清场：移除双方残留、复位标志、置 done flag。</summary>
    private void CleanupCristineTutorial()
    {
        _tutorialActive = false;
        foreach (Raider r in _tutorialRaiders)
            if (IsInstanceValid(r))
                r.QueueFree();
        _tutorialRaiders.Clear();
        if (_christine != null && IsInstanceValid(_christine))
            _christine.QueueFree();
        _christine = null;
        _storyFlags.Set("tutorial_raider_done", "true");
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

    // 颜色数组等比缩放（做暗色地基/门柱），保留原数组不越界。
    private static double[]? ScaleColor(double[]? c, float mul) =>
        c is { Length: >= 3 } ? new[] { c[0] * mul, c[1] * mul, c[2] * mul } : c;

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
            TravelTimeSeconds = raw.travelTimeSeconds,
            WarningBufferSeconds = raw.warningBufferSeconds,
        };
    }

    private CampResources LoadCampResources()
    {
        CampResourcesRaw raw = CampResourcesRaw.Default();
        const string path = "res://data/camp_resources.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    raw = JsonSerializer.Deserialize<CampResourcesRaw>(f.GetAsText());
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"camp_resources.json 解析失败，用默认值：{e.Message}");
                }
            }
        }
        return new CampResources(raw.initialFood, raw.initialMorale, raw.moralePenaltyPerMissingMeal, raw.moraleMax);
    }

    private MealBubblePool LoadMealBubbles()
    {
        MealBubble[]? bubbles = null;
        const string path = "res://data/meal_bubbles.json";
        if (FileAccess.FileExists(path))
        {
            using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                try
                {
                    bubbles = JsonSerializer.Deserialize<MealBubble[]>(f.GetAsText());
                }
                catch (JsonException e)
                {
                    GD.PushWarning($"meal_bubbles.json 解析失败，气泡池为空：{e.Message}");
                }
            }
        }
        return new MealBubblePool(bubbles);
    }

    // ---------------- JSON 映射（字段名对应 camp.json/daynight.json，故意小写） ----------------

    private sealed class CampConfig
    {
        public double[]? mapBounds { get; set; }
        public double navInset { get; set; }
        public double[]? cameraCenter { get; set; }
        public Heights? heights { get; set; }
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
        public GuardPostSpec[]? guardPosts { get; set; }
    }

    /// <summary>预置守卫岗位：kind = watchtower/roofPlatform/hidden；stand = 守卫驻守站位 [x,y]（cartesian，须可寻路）。</summary>
    private struct GuardPostSpec
    {
        public string? kind { get; set; }
        public double[]? stand { get; set; }
    }

    /// <summary>iso 立面/屋顶抬升高度（屏幕像素）。缺省用向后兼容默认值。</summary>
    private struct Heights
    {
        public double wall { get; set; }
        public double mountain { get; set; }
        public double fence { get; set; }
        public double post { get; set; }
        public double prop { get; set; }

        public static Heights From(Heights? c)
        {
            Heights h = c ?? default;
            return new Heights
            {
                wall = h.wall > 0 ? h.wall : 46,
                mountain = h.mountain > 0 ? h.mountain : 96,
                fence = h.fence > 0 ? h.fence : 26,
                post = h.post > 0 ? h.post : 40,
                prop = h.prop > 0 ? h.prop : 20,
            };
        }
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
        public double wallHeight { get; set; }
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

    private struct CampResourcesRaw
    {
        public int initialFood { get; set; }
        public double initialMorale { get; set; }
        public double moraleMax { get; set; }
        public double moralePenaltyPerMissingMeal { get; set; }

        public static CampResourcesRaw Default() => new()
        {
            initialFood = 12,
            initialMorale = 80,
            moraleMax = 100,
            moralePenaltyPerMissingMeal = 4,
        };
    }

    private struct DayNightRaw
    {
        public double dayLengthSeconds { get; set; }
        public double nightLengthSeconds { get; set; }
        public double travelTimeSeconds { get; set; }
        public double warningBufferSeconds { get; set; }
        public bool startAtNight { get; set; }
        public double[]? dayColor { get; set; }
        public double[]? nightColor { get; set; }
        public double twilightFraction { get; set; }

        public static DayNightRaw Default() => new()
        {
            dayLengthSeconds = 720,
            nightLengthSeconds = 480,
            travelTimeSeconds = 120,
            warningBufferSeconds = 300,
            startAtNight = false,
            twilightFraction = 0.12,
        };
    }
}
