using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using DeadSignal.Combat;

namespace DeadSignal.Godot;

/// <summary>
/// 幸存者营地主场景（初级地图）：大平地上的农庄，四面围栏围合、正南+正北各一扇大门。围栏/大门为带 HP 的可破坏结构。
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
    private Rect2 _mapBounds = new(0, 0, 2400, 1800);
    private float _navInset = 20f;
    private Vector2 _cameraCenter = new(1200, 900);

    // 所有实心矩形（山体/栅栏/建筑墙/道具）——既是碰撞，也作导航挖洞。
    private readonly List<Rect2> _navHoles = new();

    // 可破坏结构（围栏/大门）：HP + 碰撞体/视觉块/rect。HP→0 摧毁时逐一清场并重烘焙导航开出缺口。
    private readonly List<CampStructureInstance> _structures = new();
    private readonly List<Pawn> _survivors = new();
    private readonly HashSet<Pawn> _selected = new();
    private bool _gameOver; // 全灭防重入：game-over 只触发一次。

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

    // ---------------- 营地搜刮 / 共享库存 / 阅读（W3a） ----------------
    // 背包=营地共享库存（用户口径）：所有搜到的武器/护甲/书进这里，食物进 _resources.Food。
    private readonly InventoryStore _inventory = new();
    // loot 容器（衣柜/展示柜/草垛）的一次性搜刮登记；storage 容器（柜子）不入此表，其藏物开局即入库存。
    private readonly ContainerLoot _containerLoot = new();
    // 书 id → 同一 BookData 实例（阅读面板 MarkRead 后已读态共享，故必须持同一实例）。
    private readonly Dictionary<string, BookData> _bookRegistry = new();
    private Func<string, BookData?> _bookResolver = _ => null;
    // 场上可点击容器（cartesian rect + 角色 + 藏物），点击命中据此存取/搜刮。
    private readonly List<ContainerRef> _containers = new();
    private StashPanel _stashPanel = null!;
    private ReaderPanel _readerPanel = null!;
    private bool _stashOpen;             // 库存面板是否开着（时标冻结的唯一持有者）
    private double _prevStashTimeScale;  // 开库存前的时标（关闭时恢复）

    // ---------------- 配方 / 制作（工作台接入） ----------------
    // 全营共享一台工作台的工具装配态（field 初始化，早于 ApplyStorageInitialStock 就绪，供工具搜到即装）。
    private readonly WorkbenchState _workbench = new();
    private CraftingPanel _craftingPanel = null!;
    private bool _craftingOpen;             // 制作面板是否开着（与库存互斥地持有时标冻结）
    private double _prevCraftingTimeScale;   // 开制作面板前的时标（关闭时恢复）
    private CampToast _campToast = null!;    // 制作/搜刮的一行瞬时提示（HUD 顶部，手动显隐——时标冻结下计时器不走）
    private DiscoveryPanel _discoveryPanel = null!;  // 探索发现环境叙事面板（模态，弹出时冻结时标）
    private double _prevDiscoveryTimeScale;          // 弹发现叙事前的时标（关闭时恢复）

    /// <summary>场上一个可点击容器：名字（稳定标识）+ cartesian 命中矩形 + 角色（storage/loot）+ 藏物清单。</summary>
    private sealed class ContainerRef
    {
        public string Name = "";
        public Rect2 Rect;
        public string Role = "";
        public List<LootItem> Loot = new();
    }
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
    private const float BreachRadius = 420f;  // 破防线：丧尸摸进营心此半径内 = 破防（随 2400×1800 地图放大调，拟定待调）

    // ---------------- 教学关：克莉丝汀反水（第 2 夜脚本人类袭击，自成一路，与丧尸袭营互斥）----------------
    private readonly List<Raider> _tutorialRaiders = new();  // 场上普通劫掠者（不含克莉丝汀）
    private Raider? _christine;                              // 克莉丝汀（Raider 实例；反水前敌、反水后友、招募后转 Pawn 移除）
    private bool _tutorialActive;                            // 教学关战斗进行中（逐帧 UpdateCristineTutorial）
    private bool _christineTurned;                           // 克莉丝汀是否已反水（切 Survivor 阵营）
    private const int TutorialRaiderCount = 2;               // 固定生成 2 个劫掠者（不走 RaidWave 概率）
    private const string ChristineName = "克莉丝汀";          // 招募后作为 Pawn 的显示名（请求线据此识别她）

    /// <summary>
    /// 一处可破坏结构（围栏/大门）的运行时实例：血量状态 + cartesian rect + 碰撞体/视觉块。
    /// Blocking=true（围栏）建实心碰撞 + 导航洞；Blocking=false（大门）为可通行关口（无碰撞、不入导航洞，门控后续）。
    /// </summary>
    private sealed class CampStructureInstance
    {
        public CampStructureState State = null!;
        public Rect2 Rect;
        public bool Blocking;                       // true=围栏(碰撞+导航洞)；false=可通行大门
        public StaticBody2D? Body;                  // 仅 Blocking 时存在
        public readonly List<Node2D> Visuals = new();
        public bool Removed;                        // 已摧毁并清场（防重复摧毁/重烘焙）
    }

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

    // 睡眠点（住宅室内中心 / 仓库室内中心，须跟随 camp.json buildings 放大后的 rect）。
    private static readonly Vector2[] SleepPositions =
    {
        new(630, 600),
        new(1610, 550),
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

        // 营地搜刮/库存/阅读（W3a）。书解析器取 BookLibrary 的**单一快照实例**（每 id 一份，已读态共享）。
        var bookSnapshot = BookLibrary.All().ToDictionary(b => b.Id);
        _bookResolver = id => bookSnapshot.TryGetValue(id, out BookData? b) ? b : null;

        _stashPanel = new StashPanel { Layer = 20 };
        AddChild(_stashPanel);
        _stashPanel.Visible = false;
        _stashPanel.BookOpenRequested += OnBookOpenRequested;
        _stashPanel.EquipRequested += OnStashEquipRequested;
        _stashPanel.Closed += CloseStash;

        _readerPanel = new ReaderPanel { Layer = 21 }; // 叠在库存面板之上
        AddChild(_readerPanel);
        _readerPanel.Visible = false;
        _readerPanel.Closed += OnReaderClosed;

        _discoveryPanel = new DiscoveryPanel { Layer = 22 }; // 探索发现叙事，独立于库存链路
        AddChild(_discoveryPanel);
        _discoveryPanel.Visible = false;
        _discoveryPanel.Continued += OnDiscoveryContinued;

        // 工作台制作面板（点营地工作台打开；与库存面板一样冻结时标）。事件接 CraftingService 实扣实产。
        _craftingPanel = new CraftingPanel { Layer = 20 };
        AddChild(_craftingPanel);
        _craftingPanel.Visible = false;
        _craftingPanel.CraftRequested += OnCraftRequested;
        _craftingPanel.ModApplyRequested += OnModApplyRequested;
        _craftingPanel.Closed += CloseCrafting;

        // 制作/搜刮一行瞬时提示（HUD 之上，独立高层，时标冻结下靠手动显隐）。
        _campToast = new CampToast { Layer = 26 };
        AddChild(_campToast);

        // storage 容器（住宅柜子）的开局藏物：食物入 _resources.Food、书/武器/护甲入共享库存、材料入库存、工具装工作台。
        ApplyStorageInitialStock();

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

        // 山体（可选，抬起的立体块切块 YSort）。当前大平地布局 camp.json 无 mountains，此循环空跑；保留以便日后地图复用。
        var mtnStyle = _cfg.mountainStyle ?? new PixelStyle { color = new[] { 0.27, 0.32, 0.27 }, jitter = 0.14 };
        foreach (RectSpec m in _cfg.mountains ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(m.rect) is { } r)
            {
                AddSolid(r, mtnStyle, seed: 7, (float)_heights.mountain, CellMountain);
            }
        }

        // 四面围栏（可破坏结构：实心碰撞 + 导航洞 + HP）+ 南北大门缺口（门柱更高，非破坏锚点）。
        var fenceStyle = _cfg.fenceStyle ?? new PixelStyle { color = new[] { 0.40, 0.30, 0.19 }, jitter = 0.12 };
        foreach (RectSpec f in _cfg.fences ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(f.rect) is { } r)
            {
                AddStructure(r, fenceStyle, seed: 11, (float)_heights.fence, CellFence,
                    StructureTier.FenceBasic, blocking: true);
            }
        }
        foreach (RectSpec p in _cfg.gatePosts ?? System.Array.Empty<RectSpec>())
        {
            if (ToRect(p.rect) is { } r)
            {
                AddSolid(r, fenceStyle, seed: 13, (float)_heights.post, CellFence);
            }
        }
        // 南北大门（可破坏结构：HP=250；**默认关闭**——实心碰撞+挖导航洞+门槛视觉，敌人须砸门破防。门控后续）。
        foreach (GateSpec g in _cfg.gates ?? System.Array.Empty<GateSpec>())
        {
            if (ToRect(g.rect) is { } r)
            {
                AddGate(r);
            }
        }

        // 建筑（地基 + 墙立面 + 门 + 屋顶淡出）。
        foreach (BuildingSpec b in _cfg.buildings ?? System.Array.Empty<BuildingSpec>())
        {
            BuildBuilding(b);
        }

        // 道具（工作台等实心障碍，矮立体块）。带 role 的道具（storage/loot）另登记为可点击容器。
        foreach (PropSpec pr in _cfg.props ?? System.Array.Empty<PropSpec>())
        {
            if (ToRect(pr.rect) is { } r)
            {
                var style = new PixelStyle { color = pr.color, jitter = pr.jitter };
                AddSolid(r, style, seed: 17, (float)_heights.prop, cell: 200f);
                RegisterContainer(pr, r);
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

    /// <summary>把一个 cartesian 矩形切成 ≤cell 的小块，每块一个抬起立体块节点，逐块 YSort。
    /// <paramref name="collect"/> 非空时把每块视觉节点登记进去（供可破坏结构摧毁时逐块移除）。</summary>
    private void AddOccluderVisual(Rect2 rect, PixelStyle style, int seed, float height, float cell,
        List<Node2D>? collect = null)
    {
        int nx = Mathf.Max(1, Mathf.CeilToInt(rect.Size.X / cell));
        int ny = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y / cell));
        float cw = rect.Size.X / nx, ch = rect.Size.Y / ny;

        for (int iy = 0; iy < ny; iy++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                var sub = new Rect2(rect.Position.X + ix * cw, rect.Position.Y + iy * ch, cw, ch);
                var panel = new IsoTilePanel
                {
                    FootprintCart = sub,
                    Style = style,
                    Seed = seed + ix * 7 + iy * 13,
                    Cell = Mathf.Min(cell, 48f),
                    Height = height,
                    Facade = true,
                    ZIndex = ZWorld,
                };
                _isoLayer.AddChild(panel);
                collect?.Add(panel);
            }
        }
    }

    // ---------------- 可破坏结构（围栏/大门）：HP + 摧毁开口 ----------------

    /// <summary>
    /// 加一处可破坏结构（围栏）：与 <see cref="AddSolid"/> 同构建实心碰撞 + 切块立体视觉 + 导航洞，
    /// 但把碰撞体/视觉块/rect 记进 <see cref="CampStructureInstance"/>（含 HP 状态），供 HP→0 摧毁时逐一清场并重烘焙开口。
    /// </summary>
    private CampStructureInstance AddStructure(Rect2 rect, PixelStyle style, int seed, float height, float cell,
        StructureTier tier, bool blocking)
    {
        var inst = new CampStructureInstance
        {
            State = new CampStructureState(tier),
            Rect = rect,
            Blocking = blocking,
        };

        if (blocking)
        {
            var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
            body.CollisionLayer = 0b0100; // 层 3 = 墙（Actor 只与此层碰撞）
            body.CollisionMask = 0;
            body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
            _logicLayer.AddChild(body);
            inst.Body = body;
            _navHoles.Add(rect);
        }

        AddOccluderVisual(rect, style, seed, height, cell, inst.Visuals);
        _structures.Add(inst);
        return inst;
    }

    /// <summary>
    /// 加一扇大门（可破坏结构，HP=基础大门 250）：**默认关闭**——和围栏一样建成实心屏障
    /// （StaticBody2D 碰撞 + 挖导航洞 + 切块立体视觉），纳入 <see cref="CampStructureInstance"/> 体系，
    /// 可被攻击摧毁（HP→0 移碰撞 + 重烘焙开出缺口，敌人穿破口涌入）。门下先铺一条门槛地面色带作视觉区分。
    /// 门控（花料/交互开关门）机制后续做。
    /// </summary>
    private void AddGate(Rect2 rect)
    {
        // 门槛地面色带（纯视觉，铺在门体之下，随门体摧毁一并清场）。
        double[] sillColor = _cfg.gateMarker?.color ?? new[] { 0.35, 0.28, 0.18 };
        var sill = new IsoTilePanel
        {
            FootprintCart = rect,
            Style = new PixelStyle { color = sillColor, jitter = 0.06 },
            Seed = 61,
            Cell = 40f,
            ZIndex = ZThreshold,
        };
        _isoLayer.AddChild(sill);

        // 门体：实心可破坏结构（用门体色，比围栏略高一点以示区分）。
        var gateStyle = new PixelStyle { color = sillColor, jitter = 0.08 };
        CampStructureInstance inst = AddStructure(
            rect, gateStyle, seed: 61, (float)_heights.post, CellFence,
            StructureTier.GateBasic, blocking: true);
        inst.Visuals.Add(sill); // 摧毁时门槛随门体一并 QueueFree
    }

    /// <summary>
    /// 结构攻击接口（供后续袭营/守卫块让敌人打围栏/大门）：对某结构施加伤害，HP→0 摧毁并开口。
    /// 返回该击是否摧毁了结构。敌人主动攻击结构的 AI 属袭营块（后续），本块只提供承伤入口 + 摧毁开口实现。
    /// </summary>
    private bool DamageStructure(CampStructureInstance s, int amount)
    {
        if (s.Removed)
        {
            return false;
        }
        bool destroyed = s.State.TakeDamage(amount);
        if (destroyed)
        {
            DestroyStructure(s);
        }
        return destroyed;
    }

    /// <summary>结构摧毁：移除碰撞体 + 视觉块 + 导航洞并重烘焙（仅阻挡型动导航），开出缺口供敌人穿过破口。</summary>
    private void DestroyStructure(CampStructureInstance s)
    {
        if (s.Removed)
        {
            return;
        }
        s.Removed = true;

        if (s.Body != null && IsInstanceValid(s.Body))
        {
            s.Body.QueueFree();
        }
        foreach (Node2D v in s.Visuals)
        {
            if (IsInstanceValid(v))
            {
                v.QueueFree();
            }
        }
        s.Visuals.Clear();

        if (s.Blocking)
        {
            _navHoles.Remove(s.Rect); // Rect2 值相等；移除后重烘焙在此处补出缺口
            RebakeNavigation();
        }
        GD.Print($"[结构] {s.State.Kind} 被摧毁 @ {s.Rect.Position:0}，已开出缺口（敌人可穿过）。");
    }

    /// <summary>就近取一处未摧毁结构（供敌人选定攻击目标 / 调试打击）：中心距落点最近且在半径内者。</summary>
    private CampStructureInstance? NearestStructure(Vector2 cart, float radius)
    {
        CampStructureInstance? best = null;
        float bestD = radius;
        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed)
            {
                continue;
            }
            float d = s.Rect.GetCenter().DistanceTo(cart);
            if (d < bestD)
            {
                bestD = d;
                best = s;
            }
        }
        return best;
    }

    /// <summary>
    /// 破防 AI 用：按**边缘距离**（非中心）在 <paramref name="radius"/> 内取离 <paramref name="from"/> 最近的未毁结构，
    /// 输出其最近边缘点与边缘距离。长围栏中心可能很远但边缘就在敌人脸上——故用边缘距离（<see cref="BreachLogic"/>）。
    /// 委托给 <see cref="BreachController"/>（结构类型不外泄）。
    /// </summary>
    private bool TryFindBreachTarget(Vector2 from, float radius, out Vector2 edgePoint, out float edgeDistance)
    {
        edgePoint = from;
        edgeDistance = float.PositiveInfinity;
        CampStructureInstance? best = NearestStructureByEdge(from, radius, out float bestDist, out Vector2 bestEdge);
        if (best == null)
        {
            return false;
        }
        edgePoint = bestEdge;
        edgeDistance = bestDist;
        return true;
    }

    /// <summary>破防 AI 用：对 <paramref name="from"/> 附近 <paramref name="radius"/> 内边缘最近的未毁结构施伤，返回是否本击摧毁。</summary>
    private bool DamageNearestStructureAt(Vector2 from, float radius, int amount)
    {
        CampStructureInstance? s = NearestStructureByEdge(from, radius, out _, out _);
        return s != null && DamageStructure(s, amount);
    }

    /// <summary>按边缘距离取最近未毁结构 + 其最近边缘点（供破防择目标/施伤共用）。</summary>
    private CampStructureInstance? NearestStructureByEdge(Vector2 from, float radius, out float bestDist, out Vector2 bestEdge)
    {
        CampStructureInstance? best = null;
        bestDist = radius;
        bestEdge = from;
        foreach (CampStructureInstance s in _structures)
        {
            if (s.Removed)
            {
                continue;
            }
            (double ex, double ey) = BreachLogic.NearestPointOnRect(
                from.X, from.Y, s.Rect.Position.X, s.Rect.Position.Y, s.Rect.Size.X, s.Rect.Size.Y);
            float d = (float)BreachLogic.Distance(from.X, from.Y, ex, ey);
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
                bestEdge = new Vector2((float)ex, (float)ey);
            }
        }
        return best;
    }

    /// <summary>调试（F 键）：对鼠标落点最近的结构打 50 伤害，验证承伤/摧毁→开口→重烘焙链路。</summary>
    private void DebugDamageStructureAtMouse()
    {
        Vector2 cart = Iso.Unproject(GetGlobalMousePosition());
        CampStructureInstance? s = NearestStructure(cart, 300f);
        if (s == null)
        {
            GD.Print("[结构] 鼠标附近无可破坏结构。");
            return;
        }
        bool destroyed = DamageStructure(s, 50);
        GD.Print($"[结构] 调试打击 {s.State.Kind} -50 → HP {s.State.Hp}/{s.State.MaxHp}" +
                 (destroyed ? "（摧毁！已开口）" : ""));
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

            // 克莉丝汀若在请求线走完前身故：清空该支线全部 flag，彻底停播请求/离开（她已不在场）。
            if (p.DisplayName == ChristineName)
                ChristineRequestLogic.Abort(_storyFlags);

            // 玩家幸存者移出名单**之后**判全灭：无一存活 → game-over（只触发一次）。
            // 只玩家幸存者（_survivors 里的 Pawn）算数——盟友反水者/劫掠者/丧尸不进此判定。
            if (!_gameOver && GameOverCondition.AllSurvivorsDead(_survivors.Count(s => s.Alive)))
            {
                _gameOver = true;
                GameOverPanel.Show(_hud);
            }
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
            case Key.F:
                // 调试：打击鼠标落点最近的围栏/大门，验证承伤→摧毁→开口→重烘焙链路（敌人打结构 AI 属袭营块，后续）。
                DebugDamageStructureAtMouse();
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

        // 单击（非框选）优先判容器命中：命中即存取/搜刮，保留当前选中不动，不落到选人/收面板逻辑。
        if (!isBox && TryOpenContainerAt(Iso.Unproject(_dragStartWorldIso)))
        {
            return;
        }

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
                // 命中单人 → 打开/刷新面板（重复调用即切换）；附装假肢入口 + 装备态快照（11 槽/持械/握持）。
                ShowInspect(hit);
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

    /// <summary>分粮策略：默认"先喂最饿"——库存不足时把口粮压在濒死者身上，最大化少死人（策略/数值拟定待调）。</summary>
    private RationStrategy _rationStrategy = RationStrategy.HungriestFirst;

    /// <summary>缺口趋势预警阈值（昼夜）：存货全员吃饱撑不过这么多昼夜时红字告警，逼玩家搜刮。拟定待调。</summary>
    private const int FoodShortfallWarnDays = 2;

    private void EnterDuskMeal() => _clock.TransitionTo(DayPhase.DuskMeal);

    /// <summary>
    /// 一次聚餐（发生在昼夜相位切换点，一天两次）：全员用餐扣食物（不足则未进食者饥饿加深）+ 触发气泡，弹出模态面板。
    /// 饥饿模型（净零）：本次切换全员饥饿刻度无条件 -1；吃到饭者 +1（clamp 到各自上限）——吃满两餐即维持。
    /// 结算后：刻度归 0 者饿死（走统一死亡路径）。
    /// </summary>
    private void RunMeal(string title, string phaseTag)
    {
        var living = _survivors.Where(s => s.Alive).ToList();

        // 分粮：库存不足喂饱全员时，按策略（默认先喂最饿=少死人）决定谁吃到、谁挨饿。
        var dinerInputs = living.Select(ToDiner).ToList();
        RationOutcome ration = FoodEconomy.Allocate(_resources.Food, dinerInputs, _rationStrategy);

        // 食物扣减：续用 ConsumeMeal——每份=1 时其消耗/缺口与分粮结算完全一致，
        // 仅"谁吃到"从原序改由 ration.Fed 决定（见下逐人喂食）。
        MealOutcome outcome = _resources.ConsumeMeal(living.Count);

        // 昼夜切换净结算：一次性施加"无条件 -1，吃到再 +1"（吃满两餐净零维持）。
        // 用 ResolvePhase 一步算净变化 + clamp，避免旧两步"1→0 途中 Feed 被短路"的跨 0 误杀。
        // 谁吃到由分粮策略给出的 ration.Fed[i]（原序对齐 living[i]）决定，而非先到先吃。
        var hungerNotes = new List<string>();
        for (int i = 0; i < living.Count; i++)
        {
            bool ate = ration.Fed[i];
            living[i].ResolveHungerPhase(ate);
            if (!ate && living[i].Hunger.Level < HungerLevel.Sated)
            {
                hungerNotes.Add($"{living[i].DisplayName}（{living[i].Hunger.Level.Label()}）");
            }
        }

        // 饿死：刻度归 0 者走统一死亡路径（Died 事件会改 _survivors，先收集再逐个处理）。
        foreach (var starved in living.Where(d => d.IsStarvedToDeath).ToList())
        {
            starved.StarveToDeath();
        }

        // 构造"世界只读快照"喂条件驱动选择器：相位 + 当前 flags + 存活者真实状态 + 食物。
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
        };
        var bubbles = _bubblePool.Pick(context, 3);
        // 应用选中气泡的 triggers（改 flags）——推动剧情；选择器不隐式改 flag，故独立成步。
        MealBubblePool.ApplyTriggers(bubbles, _storyFlags);
        _recentlyDeceased.Clear(); // 死亡只在紧随其后的一餐被提及，之后归入历史不再复播

        _mealPanel.ShowMeal(title, outcome, bubbles, hungerNotes);

        // 缺口预警：本餐有人挨饿→急告；否则按存货趋势提醒还能撑几昼夜，逼玩家搜刮补给。
        WarnFoodShortfall(ration, _survivors.Count(s => s.Alive));
    }

    /// <summary>把存活幸存者映射成分粮输入：饥饿刻度 + 是否伤员（急性伤：昏迷/出血/骨折需养伤，供 WoundedFirst 优先）。</summary>
    private static FoodDiner ToDiner(Pawn p)
    {
        var insp = p.Inspect();
        bool wounded = insp.IsUnconscious
            || insp.Parts.Any(part => part.IsBleeding || part.IsFractured);
        return new FoodDiner(p.Hunger.Value, wounded);
    }

    /// <summary>缺口预警（HUD 顶栏红字，手动显隐持久到下次覆盖）：本餐挨饿→急告；否则存货撑不过阈值昼夜→趋势告警。</summary>
    private void WarnFoodShortfall(RationOutcome ration, int livingCount)
    {
        if (ration.StarvedCount > 0)
        {
            _campToast.Show($"食物告罄：{ration.StarvedCount} 人挨饿，尽快搜刮补给", CampToast.Bad);
            return;
        }

        int days = FoodEconomy.DaysUntilShortfall(_resources.Food, livingCount);
        if (days != int.MaxValue && days <= FoodShortfallWarnDays)
        {
            _campToast.Show($"食物见底：存货约撑 {days} 昼夜，尽快搜刮补给", CampToast.Bad);
        }
    }

    private void OnMealContinued()
    {
        _mealPanel.Visible = false;

        // 本餐若播了克莉丝汀的请求气泡（trigger 置了 pending）→ 先弹抉择面板，
        // 相位推进推迟到玩家选完（AdvanceAfterMeal）。仅当她仍是在营存活幸存者时才逼问；
        // 否则（已亡故/离场）静默清线，照常推进。
        if (ChristineRequestLogic.HasPendingRequest(_storyFlags))
        {
            if (ChristinePawn() != null)
            {
                PromptChristineHelpChoice();
                return;
            }
            ChristineRequestLogic.Abort(_storyFlags);
        }

        AdvanceAfterMeal();
    }

    /// <summary>聚餐结束后的相位推进（黎明→白天备战；黄昏→睡眠过渡）。抉择面板延迟场景亦复用它收尾。</summary>
    private void AdvanceAfterMeal()
    {
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

        // 克莉丝汀累计 3 次"暂不"后不立即走：排期到下一次昼夜交替（相位切进聚餐）时自行离开。
        // 置于结算前，使她不再计入本餐用餐者。走"自愿离开"清理（非 Died，不触发全灭判定）。
        if ((phase == DayPhase.DawnMeal || phase == DayPhase.DuskMeal)
            && ChristineRequestLogic.ConsumeLeaving(_storyFlags))
        {
            ChristineLeaveVoluntary();
        }

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

    /// <summary>
    /// 探索队踏入金手指帮根据地一处发现点：置 flag（防重复）、把对应日记经 <c>LootApplication</c> 入共享库存、
    /// 冻结时标弹环境叙事。日记回营后在库存点开经 ReaderPanel 细读。
    /// </summary>
    private void OnExplorationDiscovery(string discoveryId)
    {
        DiscoveryResult? r = GoldfingerDiscovery.Resolve(discoveryId, _storyFlags);
        if (r == null)
            return; // 未知 id 或已发现过

        DiscoveryResult d = r.Value;
        _storyFlags.Set(d.StoryFlag, "true"); // 持久去重：本 flag 已置则下次 Resolve 返回 null

        // 日记入共享库存（同一 BookData 实例登记进 registry，回营阅读共享已读态）。
        LootApplication.Apply(
            new[] { LootItem.Book(d.BookId) }, _inventory, _bookRegistry, _bookResolver);

        _prevDiscoveryTimeScale = Engine.TimeScale;
        Engine.TimeScale = 0; // 冻结探索实时层，专注读叙事
        _discoveryPanel.Show(d.Title, d.Narrative);
        _discoveryPanel.Visible = true;
    }

    /// <summary>关发现叙事面板，恢复时标（冻结中打开的则回 1）。</summary>
    private void OnDiscoveryContinued()
    {
        _discoveryPanel.Visible = false;
        Engine.TimeScale = _prevDiscoveryTimeScale <= 0 ? 1 : _prevDiscoveryTimeScale;
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
        _currentLevel.DestinationName = destinationName; // 关卡据此决定是否铺发现点（金手指帮根据地）

        _currentLevel.OnReturnToCamp += OnExplorationReturn;
        _currentLevel.OnDiscovery += OnExplorationDiscovery;

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
        _currentLevel!.OnDiscovery -= OnExplorationDiscovery;
        _currentLevel!.Cleanup();

        // 卸载关卡时若发现叙事面板仍开着，收起并恢复时标，避免带回营地。
        if (_discoveryPanel.Visible)
            OnDiscoveryContinued();

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
            // 门外错峰生成：南北两门缺口 x∈[1100,1300]，交替从南门(y>1500)、北门(y<300)外涌入。
            bool south = (i & 1) == 0;
            float gx = 1110f + (i * 43f) % 180f;
            float gy = south ? 1540f + (i % 3) * 14f : 260f - (i % 3) * 14f;

            var z = Zombie.Create(wander, () => _survivors.Where(a => a.Alive).Cast<Actor>());
            z.Inject(_combat, _clock); // 与营地单位同一 combat+clock，务必首帧 Think 前完成
            z.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter); // 门关闭→砸墙破防
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

    /// <summary>结算收口：守住无损失；被攻破按 <see cref="RaidResolution.ConsequenceFor"/> 扣食物。</summary>
    private void FinishRaid(RaidEvaluation eval)
    {
        _raidActive = false;
        RaidConsequence cons = RaidResolution.ConsequenceFor(eval);
        if (eval.State == RaidState.Overrun)
        {
            _resources.ApplyRaidLoss(cons.FoodLoss);
            GD.Print($"[Raid] 防御战失败（{eval.Reason}）：损食物 {cons.FoodLoss}。伤亡在实时战斗中自然产生。");
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
            r.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter); // 门关闭→砸墙破防
            r.Position = new Vector2(1120f + i * 60f, 1540f + (i % 2) * 12f); // 南门外错峰
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
        _christine.ConfigureBreach(TryFindBreachTarget, DamageNearestStructureAt, _cameraCenter); // 门关闭→砸墙破防
        _christine.Position = new Vector2(1200f, 1560f); // 南门外
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
            _resources.ApplyRaidLoss(cons.FoodLoss);
            GD.Print($"[教学关] 袭击失利（{eval.Reason}）：损食物 {cons.FoodLoss}。");
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
                // 放逐：让她走向门外后消失（活着离开，不留尸不流血）。
                WalkOutAndDespawn(_christine);
                _christine = null;
                GD.Print("[教学关] 放逐克莉丝汀：她走向门外，消失在营地外。");
                break;
            case CristineChoice.Execute:
                // 处决：脚下留一摊浓血后当场消失（不做倒地尸体视觉）。
                SpawnDeathBlood(_christine);
                _christine.QueueFree();
                _christine = null;
                GD.Print("[教学关] 处决克莉丝汀：地上留下一摊血。");
                break;
        }
    }

    // ---------------- 克莉丝汀请求线：请求出兵清剿金手指帮 ----------------

    /// <summary>当前在营存活的克莉丝汀 Pawn（招募后）；不在场（未招募/已亡故/已离开）返回 null。</summary>
    private Pawn? ChristinePawn() =>
        _survivors.FirstOrDefault(p => p.Alive && p.DisplayName == ChristineName);

    /// <summary>
    /// 弹出「答应出兵清剿 / 暂不」抉择面板（复用通用 <see cref="ChoicePanel"/>，非教学三选一）。
    /// 冻结时标、选完恢复：答应→请求线永久停播；暂不→计一次，满 3 次排期离开。收尾均走 <see cref="AdvanceAfterMeal"/>。
    /// </summary>
    private void PromptChristineHelpChoice()
    {
        double prevScale = Engine.TimeScale;
        Engine.TimeScale = 0;

        var panel = new ChoicePanel();
        AddChild(panel);
        // draft 待用户改：三条请求台词见 meal_bubbles.json；此处为抉择面板提示与选项占位。
        panel.Setup(
            "克莉丝汀把你拉到一旁，声音压得很低：\n" +
            "「金手指帮……我夜里一闭眼就是他们的脸。带上人，跟我去把那个据点端了——求你了。」",
            new List<ChoicePanel.ChoiceOption>
            {
                new() { Value = 1, Label = "答应出兵清剿",
                        Description = "等探明他们的据点，就动手", Accent = new Color(0.62f, 0.25f, 0.22f) },
                new() { Value = 0, Label = "暂不",
                        Description = "现在还不是时候", Accent = new Color(0.45f, 0.42f, 0.4f) },
            });
        panel.Confirmed += v =>
        {
            Engine.TimeScale = prevScale <= 0 ? 1 : prevScale;
            bool agreed = v == 1;
            ChristineRequestLogic.Resolve(_storyFlags, agreed);
            GD.Print(agreed
                ? "[克莉丝汀] 答应出兵清剿金手指帮（请求线停播，待后续探索兑现）。"
                : $"[克莉丝汀] 暂不出兵（累计回绝 {ChristineRequestLogic.DeclineCount(_storyFlags)}/{ChristineRequestLogic.DeclinesToLeave}）。");
            panel.QueueFree();
            AdvanceAfterMeal(); // 抉择完成，接回被推迟的聚餐后相位推进
        };
    }

    /// <summary>
    /// 克莉丝汀累计 3 次回绝后自行离开：**不走 Died 事件**（避免触发全灭判定/记 _recentlyDeceased），
    /// 手动做与 <see cref="OnActorDied"/> 平行的名单清理（移出 _survivors/_selected/_raidGuards），再走向门外消失。
    /// </summary>
    private void ChristineLeaveVoluntary()
    {
        var christine = ChristinePawn();
        if (christine == null)
        {
            return; // 已不在场（如中途身故，Abort 已清 flag）——静默跳过
        }
        _survivors.Remove(christine);
        _selected.Remove(christine);
        _raidGuards.Remove(christine);
        // draft 待用户改：三拒后独走复仇的离别台词（决绝，呼应反水时"我是好人！"）。
        FloatingText.Spawn(_actorLayer, christine.GlobalPosition,
            "杀死这些劫掠者……这一次，我一个人也去。", new Color(0.9f, 0.5f, 0.4f));
        WalkOutAndDespawn(christine);
        // 置"独自去复仇"flag：日后在金手指帮根据地发现其尸体时，环境叙事据此点名措辞（衔接 §7 时限失败态）。
        _storyFlags.Set(GoldfingerDiscovery.ChristineLeftForRevengeFlag, "true");
        GD.Print("[克莉丝汀] 三度回绝后，她收拾东西，独自走出了营地大门。");
    }

    /// <summary>
    /// 让某单位走向营地大门外并在淡出后从场上抹除（放逐/自愿离开共用；活着离开、无倒地尸体）。
    /// 朝"背离营地中心"方向走出（不依赖精确门坐标，任何地图都读作离开），同时令其 ActorSprite 真 alpha 淡出——
    /// 放逐时该单位仍 Alive，故 sprite 走 <see cref="ActorSprite.FadeOutAndFree"/> 独立分支自淡出/自毁（不被每帧重写 Modulate 打断）。
    /// 淡出结束后再 QueueFree actor 本体（sprite 已自毁；状态图标条随 actor 一并消失）。
    /// </summary>
    private void WalkOutAndDespawn(Actor who)
    {
        if (who == null || !IsInstanceValid(who))
        {
            return;
        }
        Vector2 outward = who.GlobalPosition - _cameraCenter;
        outward = outward.LengthSquared() > 1f ? outward.Normalized() : Vector2.Down;
        who.CommandMoveTo(who.GlobalPosition + outward * 420f);

        const float fadeSeconds = 2.0f;
        FindActorSprite(who)?.FadeOutAndFree(fadeSeconds);
        GetTree().CreateTimer(fadeSeconds + 0.2f).Timeout += () =>
        {
            if (IsInstanceValid(who))
            {
                who.QueueFree();
            }
        };
    }

    /// <summary>在 iso 层子节点里按绑定的 Actor 反查其 ActorSprite（放逐淡出用）；未挂载/未找到返回 null。</summary>
    private ActorSprite? FindActorSprite(Actor who)
    {
        foreach (Node child in _isoLayer.GetChildren())
        {
            if (child is ActorSprite s && s.BoundActor == who)
            {
                return s;
            }
        }
        return null;
    }

    /// <summary>处决表现：在单位脚点投一摊浓血（heavy）到 iso 层（与受击溅血同源），随后由调用方 QueueFree。</summary>
    private void SpawnDeathBlood(Actor who)
    {
        if (who == null || !IsInstanceValid(who))
        {
            return;
        }
        BloodDecal.Spawn(_isoLayer, Iso.Project(who.GlobalPosition), severity: 1f, heavy: true);
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

        var pawn = Pawn.Create(ChristineName, usePistol: true, new Color(0.85f, 0.55f, 0.75f));
        pawn.Position = pos; // cartesian，原地入营
        AddActor(pawn);
        _survivors.Add(pawn);

        _storyFlags.Set("christine_recruited", "true");
        ChristineRequestLogic.Begin(_storyFlags); // 开启"请求出兵清剿金手指帮"支线（聚餐里递进请求）
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

    // ---------------- 营地搜刮 / 共享库存 / 阅读（W3a） ----------------

    /// <summary>把一个带 role 的道具登记为可点击容器：loot 类进 <see cref="_containerLoot"/>（一次性搜刮），storage 类留藏物待开局入库。</summary>
    private void RegisterContainer(PropSpec pr, Rect2 rect)
    {
        if (string.IsNullOrEmpty(pr.role) || string.IsNullOrEmpty(pr.name))
        {
            return;
        }
        List<LootItem> loot = ParseLoot(pr.loot);
        _containers.Add(new ContainerRef { Name = pr.name!, Rect = rect, Role = pr.role!, Loot = loot });
        if (pr.role == "loot")
        {
            _containerLoot.Register(pr.name!, loot);
        }
    }

    /// <summary>camp.json 的 loot 规格 → 纯逻辑 <see cref="LootItem"/> 清单（未知 kind / 缺引用键的条目忽略）。</summary>
    private static List<LootItem> ParseLoot(LootSpec[]? specs)
    {
        var list = new List<LootItem>();
        foreach (LootSpec s in specs ?? System.Array.Empty<LootSpec>())
        {
            switch (s.kind)
            {
                case "food":
                    list.Add(LootItem.Food(s.qty > 0 ? s.qty : 1));
                    break;
                case "book" when !string.IsNullOrEmpty(s.id):
                    list.Add(LootItem.Book(s.id!));
                    break;
                case "weapon" when !string.IsNullOrEmpty(s.id):
                    list.Add(LootItem.Weapon(s.id!));
                    break;
                case "armor" when !string.IsNullOrEmpty(s.id):
                    list.Add(LootItem.Armor(s.id!));
                    break;
            }
        }
        return list;
    }

    /// <summary>建好库存/面板/时标钩子后，把 storage 容器（住宅柜子）的开局藏物一次性落地到共享库存/食物/工作台。</summary>
    private void ApplyStorageInitialStock()
    {
        foreach (ContainerRef c in _containers.Where(c => c.Role == "storage"))
        {
            var tools = new List<ToolSlot>();
            int food = LootApplication.Apply(c.Loot, _inventory, _bookRegistry, _bookResolver, tools);
            if (food > 0)
            {
                _resources.AddFood(food);
            }
            InstallFoundTools(tools);
        }
    }

    /// <summary>把搜到的工具装进营地共享工作台对应槽（幂等；解锁该类配方）。返回本次新装上的工具中文名。</summary>
    private List<string> InstallFoundTools(IEnumerable<ToolSlot> tools)
    {
        var installed = new List<string>();
        foreach (ToolSlot t in tools)
        {
            if (_workbench.InstallTool(t))
            {
                installed.Add(t.Label());
                GD.Print($"[工作台] 装上工具「{t.Label()}」，解锁对应类配方。");
            }
        }
        return installed;
    }

    /// <summary>
    /// 点击落点（cartesian）命中某容器则处理：storage→开库存面板；loot→未搜过则搜出入库 + 反馈，随后开库存面板看结果。
    /// 命中返回 <c>true</c>（调用方据此不再走选人/收面板逻辑）。
    /// </summary>
    private bool TryOpenContainerAt(Vector2 cart)
    {
        ContainerRef? hit = _containers.FirstOrDefault(c => c.Rect.Grow(8f).HasPoint(cart));
        if (hit == null)
        {
            return false;
        }

        if (hit.Role == "workbench")
        {
            OpenCrafting();
            return true;
        }

        if (hit.Role == "storage")
        {
            OpenStash(null);
            return true;
        }

        // loot 容器：一次性搜刮。
        if (_containerLoot.IsSearched(hit.Name))
        {
            OpenStash($"{hit.Name}：已经搜过了。");
            return true;
        }
        IReadOnlyList<LootItem> loot = _containerLoot.Search(hit.Name);
        var tools = new List<ToolSlot>();
        int food = LootApplication.Apply(loot, _inventory, _bookRegistry, _bookResolver, tools);
        if (food > 0)
        {
            _resources.AddFood(food);
        }
        List<string> installedTools = InstallFoundTools(tools);
        // 物品件数：入库存的非食物件（武器/护甲/书/材料）。工具进的是工作台不算库存件，另在提示里点名。
        int itemCount = loot.Count(l => l.Kind is not LootKind.Food and not LootKind.Tool);
        string toolNote = installedTools.Count > 0 ? $"，装上 {string.Join("、", installedTools)}" : "";
        string notice = loot.Count == 0
            ? $"{hit.Name}：空空如也。"
            : food > 0
                ? $"在{hit.Name}搜到 {itemCount} 件物品、{food} 份食物{toolNote}。"
                : $"在{hit.Name}搜到 {itemCount} 件物品{toolNote}。";
        GD.Print($"[搜刮] {notice}");
        OpenStash(notice);
        return true;
    }

    /// <summary>打开（或刷新）库存面板：首次打开冻结时标；<paramref name="notice"/> 为可空的一行搜刮反馈。</summary>
    private void OpenStash(string? notice)
    {
        if (!_stashOpen)
        {
            _prevStashTimeScale = Engine.TimeScale;
            Engine.TimeScale = 0;
            _stashOpen = true;
        }
        _stashPanel.ShowStash(_inventory, _resources.Food, notice, IsBookRead);
        _stashPanel.Visible = true;
    }

    /// <summary>
    /// 库存面板「装备」→ 装到当前选中的幸存者（单选取一，无选中则提示）。武器默认装右手（EquipToHand
    /// 会把双手武器自动分流占两手）；护甲走 EquipApparel。成功则从库存移除该件、刷新面板（含开着的角色面板）。
    /// </summary>
    private void OnStashEquipRequested(string refKey)
    {
        Pawn? target = _selected.FirstOrDefault(p => p.IsControllable);
        if (target == null)
        {
            OpenStash("请先选中一个幸存者，再点「装备」。");
            return;
        }

        Item? item = _inventory.Equippable.FirstOrDefault(i => i.RefKey == refKey);
        if (item == null)
        {
            return; // 库存里已无此件（并发/重复点），静默
        }

        // 顶替回库：装备前记下目标已占的武器/护甲名，装成功后把被顶替下来的旧件回库存（绝不静默丢）。
        bool isWeapon = item.Category == ItemCategory.Weapon;
        List<string> before = isWeapon ? HeldWeaponNames(target) : target.EquippedApparel.ToList();

        bool ok = isWeapon
            ? target.EquipWeapon(refKey, Hand.Right)  // 双手武器 EquipToHand 自动占两手
            : target.EquipApparel(refKey);
        if (!ok)
        {
            OpenStash($"{item.DisplayName} 无法装备（断肢禁槽/持握冲突/不适用）。");
            return;
        }

        _inventory.Remove(item);
        // 被顶替下来的旧件回库存（本次刚装上的 refKey 从 after 抵掉一份，不算顶替；同名替换也不丢件）。
        List<string> after = isWeapon ? HeldWeaponNames(target) : target.EquippedApparel.ToList();
        foreach (string displaced in DisplacedNames(before, after, refKey))
        {
            _inventory.Add(isWeapon ? Item.Weapon(displaced) : Item.Armor(displaced));
        }

        if (_characterPanel.IsShown)
        {
            ShowInspect(target); // 面板开着 → 刷新装备态
        }
        OpenStash($"已为 {target.DisplayName} 装备 {item.DisplayName}。");
    }

    /// <summary>卸某手武器 → 回库存 + 刷面板/库存列表（供角色面板「卸下」按钮回调）。</summary>
    private void UnequipWeaponToStash(Pawn p, Hand hand)
    {
        List<string> before = HeldWeaponNames(p);
        p.UnequipWeapon(hand);
        List<string> after = HeldWeaponNames(p);
        foreach (string name in DisplacedNames(before, after, null))
        {
            _inventory.Add(Item.Weapon(name));
        }
        RefreshAfterEquipmentChange(p);
    }

    /// <summary>卸某件穿戴品（按名）→ 回库存 + 刷面板/库存列表（供角色面板「卸下」按钮回调）。</summary>
    private void UnequipApparelToStash(Pawn p, string apparelName)
    {
        List<string> before = p.EquippedApparel.ToList();
        p.UnequipApparel(apparelName);
        List<string> after = p.EquippedApparel.ToList();
        foreach (string name in DisplacedNames(before, after, null))
        {
            _inventory.Add(Item.Armor(name));
        }
        RefreshAfterEquipmentChange(p);
    }

    /// <summary>装备变更后刷新：角色面板开着则重拍装备态，库存面板开着则重列（反映回库的旧件）。</summary>
    private void RefreshAfterEquipmentChange(Pawn p)
    {
        if (_characterPanel.IsShown)
        {
            ShowInspect(p);
        }
        if (_stashOpen)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);
        }
    }

    /// <summary>
    /// 目标当前实际持有的武器名（物理件计数）：双手握一把 → 只记一件（两手同一把，靠 <see cref="GripMode.TwoHanded"/>
    /// 判定，不能用引用比较——武器目录是共享实例，双持两把同名武器亦引用相等）。
    /// </summary>
    private static List<string> HeldWeaponNames(Pawn p)
    {
        var list = new List<string>();
        var left = p.WeaponInHand(Hand.Left);
        var right = p.WeaponInHand(Hand.Right);
        if (p.Grip == GripMode.TwoHanded)
        {
            var w = right ?? left;
            if (w is not null) list.Add(w.Name);
        }
        else
        {
            if (left is not null) list.Add(left.Name);
            if (right is not null) list.Add(right.Name);
        }
        return list;
    }

    /// <summary>
    /// 顶替/卸下后应回库存的旧件名（多重集差 before − after）：<paramref name="newlyEquipped"/> 为本次刚装上的件，
    /// 从 after 里先抵掉一份（它已由库存扣减记账，不算顶替），从而同名替换也能正确回收旧件；卸下场景传 null。
    /// </summary>
    private static IEnumerable<string> DisplacedNames(List<string> before, List<string> after, string? newlyEquipped)
    {
        var afterCounts = after.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        if (newlyEquipped is not null && afterCounts.TryGetValue(newlyEquipped, out int nc) && nc > 0)
        {
            afterCounts[newlyEquipped] = nc - 1;
        }
        foreach (string name in before)
        {
            if (afterCounts.TryGetValue(name, out int c) && c > 0)
            {
                afterCounts[name] = c - 1; // 仍在装备上的同名件，抵消
            }
            else
            {
                yield return name; // 顶替/卸下下来的
            }
        }
    }

    /// <summary>打开/刷新角色检视面板：喂只读健康快照 + 装备态快照 + 装假肢/卸下入口（皆死数据/闭包，面板不持 live Pawn）。</summary>
    private void ShowInspect(Pawn p)
        => _characterPanel.ShowFor(
            p.Inspect(),
            SnapshotEquipment(p),
            (region, grade) => p.EquipProsthetic(region, grade),
            hand => UnequipWeaponToStash(p, hand),
            name => UnequipApparelToStash(p, name));

    /// <summary>由 live Pawn 的只读装备 API 拍一份纯数据装备快照（11 穿戴槽 + 左右手持械 + 握持态）。</summary>
    private static EquipmentSnapshot SnapshotEquipment(Pawn p)
    {
        IReadOnlySet<EquipSlot> disabled = p.DisabledApparelSlots;
        var slots = Enum.GetValues<EquipSlot>()
            .Select(s => new ApparelSlotStatus { Slot = s, ItemName = p.ApparelAt(s), IsDisabled = disabled.Contains(s) })
            .ToList();
        return new EquipmentSnapshot
        {
            Slots = slots,
            LeftHandWeapon = WeaponInfo.From(p.WeaponInHand(Hand.Left)),
            RightHandWeapon = WeaponInfo.From(p.WeaponInHand(Hand.Right)),
            Grip = p.Grip,
        };
    }

    /// <summary>关库存面板：连带关阅读面板、恢复时标（暂停中打开的则回 1，避免冻死）。</summary>
    private void CloseStash()
    {
        _stashPanel.Visible = false;
        _readerPanel.Visible = false;
        _stashOpen = false;
        Engine.TimeScale = _prevStashTimeScale <= 0 ? 1 : _prevStashTimeScale;
    }

    /// <summary>
    /// 库存里点某本书的「阅读」：把这次阅读**归属到某个读者幸存者**（标其个人已读集，配方书门槛按此判定），
    /// 同时置全局已读（库存"已读"标记等营地视角仍读全局），弹阅读面板（叠在库存之上）。
    /// 读者 = 当前选中的可控幸存者（同"装备"入口的选人口径）；无选中时退化到首个可控幸存者，保证阅读始终可用。
    /// </summary>
    private void OnBookOpenRequested(string bookId)
    {
        if (!_bookRegistry.TryGetValue(bookId, out BookData? bd))
        {
            return;
        }
        // 归属读者：优先当前选中的可控幸存者，否则首个可控幸存者（MVP：库存无"读者槽"，复用选中口径）。
        Pawn? reader = _selected.FirstOrDefault(p => p.IsControllable)
            ?? _survivors.FirstOrDefault(p => p.Alive && p.IsControllable);
        reader?.MarkBookRead(bookId);
        bd.MarkRead(); // 全局已读保留（库存已读标记 / 其它营地视角消费点仍读它）
        if (bd.GrantsRecipeStub != null)
        {
            GD.Print($"[阅读] 读完《{bd.Title}》，获得配方（桩，配方系统后续接）：{bd.GrantsRecipeStub}");
        }
        _readerPanel.ShowBook(bd.Title, bd.Body);
        _readerPanel.Visible = true;
    }

    /// <summary>关阅读面板回到库存：刷新库存（反映刚置的「已读」标记），时标仍由库存面板持有。</summary>
    private void OnReaderClosed()
    {
        _readerPanel.Visible = false;
        if (_stashOpen)
        {
            _stashPanel.ShowStash(_inventory, _resources.Food, null, IsBookRead);
        }
    }

    private bool IsBookRead(string bookId) =>
        _bookRegistry.TryGetValue(bookId, out BookData? b) && b.IsRead;

    // ---------------- 配方 / 制作（工作台接入） ----------------

    /// <summary>打开（或刷新）工作台制作面板：首次打开冻结时标。制作者=当前可控幸存者；书门槛按制作者本人已读（<see cref="Pawn.HasReadBook"/>）。</summary>
    private void OpenCrafting()
    {
        if (!_craftingOpen)
        {
            _prevCraftingTimeScale = Engine.TimeScale;
            Engine.TimeScale = 0;
            _craftingOpen = true;
        }
        RefreshCrafting();
        _craftingPanel.Visible = true;
    }

    /// <summary>重刷制作面板数据（制作/改装后调，反映扣掉的材料、新入库产物、升级后的技能）。</summary>
    private void RefreshCrafting()
        => _craftingPanel.ShowFor(_workbench, ControllableCrafters(), _inventory,
            (pawn, id) => pawn.HasReadBook(id)); // 书门槛按制作者本人已读（非营地全局）

    /// <summary>当前可作制作者的幸存者（存活且空闲可控）。</summary>
    private List<Pawn> ControllableCrafters()
        => _survivors.Where(p => p.Alive && p.IsControllable).ToList();

    /// <summary>关制作面板：恢复时标、清掉瞬时提示（与库存面板互斥地持有时标）。</summary>
    private void CloseCrafting()
    {
        _craftingPanel.Visible = false;
        _craftingOpen = false;
        _campToast.Hide();
        Engine.TimeScale = _prevCraftingTimeScale <= 0 ? 1 : _prevCraftingTimeScale;
    }

    /// <summary>
    /// 面板「制作」→ 查配方 → 走 <see cref="CraftingService.Craft"/> 实扣实产（产物按 <see cref="CraftOutputFactory"/> 分类）。
    /// 成功：提示产物 + 技能升级，刷新面板；失败：提示 <see cref="CraftBlock.Detail"/> 中文缺项。
    /// </summary>
    private void OnCraftRequested(string recipeId, Pawn crafter)
    {
        RecipeData? recipe = RecipeBook.Find(recipeId);
        if (recipe is null)
        {
            _campToast.Show($"未知配方：{recipeId}", CampToast.Bad);
            return;
        }

        CraftResult result = CraftingService.Craft(
            recipe, id => crafter.HasReadBook(id), // 书门槛按制作者本人已读
            _workbench, _inventory, 1, CraftOutputFactory.Create);

        if (!result.Success)
        {
            string reason = string.Join("；", result.Blocks.Select(b => b.Detail));
            _campToast.Show($"做不了「{recipe.DisplayName}」：{reason}", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        string products = string.Join("、", result.Produced.Select(p => p.DisplayName));
        _campToast.Show($"{crafter.DisplayName} 制作了 {products}", CampToast.Ok);
        GD.Print($"[制作] {crafter.DisplayName} 制作 {recipe.DisplayName} → {products}");
        RefreshCrafting();
    }

    /// <summary>面板「改装」→ 走 <see cref="CraftingService.ApplyWeaponMod"/> 消耗基础武器落地变体入库。成功/失败均提示 + 刷新。</summary>
    private void OnModApplyRequested(string baseWeaponRefKey, IReadOnlyList<string> modNames, Pawn crafter)
    {
        WeaponModResult result = CraftingService.ApplyWeaponMod(baseWeaponRefKey, modNames, _inventory);
        if (!result.Success)
        {
            _campToast.Show($"改装失败：{result.FailureReason}", CampToast.Bad);
            RefreshCrafting();
            return;
        }

        string name = result.Produced?.DisplayName ?? "改装武器";
        _campToast.Show($"{crafter.DisplayName} 改装出 {name}", CampToast.Ok);
        GD.Print($"[改装] {baseWeaponRefKey} → {name}");
        RefreshCrafting();
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
        return new CampResources(raw.initialFood);
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
        public GateSpec[]? gates { get; set; }
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

    /// <summary>大门规格：rect = cartesian [x,y,w,h]；side = 朝向（north/south，供门槛/门控用）。</summary>
    private struct GateSpec
    {
        public double[]? rect { get; set; }
        public string? side { get; set; }
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
        // W3a 搜刮：role = storage（共享库存存取）/ loot（一次性搜刮）；loot = 藏物清单（storage 类即开局库存）。
        public string? role { get; set; }
        public LootSpec[]? loot { get; set; }
    }

    /// <summary>容器藏物一条：kind = food/book/weapon/armor；qty = 食物份数；id = 书 id / 武器名 / 护甲名。</summary>
    private struct LootSpec
    {
        public string? kind { get; set; }
        public int qty { get; set; }
        public string? id { get; set; }
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

        public static CampResourcesRaw Default() => new()
        {
            initialFood = 12,
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
