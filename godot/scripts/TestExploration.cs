using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration : ExplorationLevel
{
    private const float LevelW = 2400f;
    private const float LevelH = 1600f;
    private const float WallT = 20f;

    // ——城市之巅瞭望观景台：望远镜交互占位契约（兄弟系统消费）——
    /// <summary>
    /// 望远镜发现点 id。踏入望远镜发现区即经 <see cref="ExplorationLevel.OnDiscovery"/> → <c>CampMain.OnExplorationDiscovery(discoveryId)</c> 上报此 id。
    /// 挂点在 <c>CampMain.OnExplorationDiscovery</c>（已留 TODO 分支）：
    ///   · anim-lookout：在此 id 触发望远镜瞭望演出（正北黑压压上百万尸潮向南移动），演出锚点＝ <see cref="LookoutTelescopePosition"/>（关内世界坐标）。
    ///   · loot-story：在此 id 出剧情文本（环境叙事，复用 ShowDiscoveryNarrative）。
    ///   · core-timer：演出/文本落地后置 HordeSighted 旗标（解锁尸潮倒计时 HUD）。
    /// 现状：此 id 在 CampMain.OnExplorationDiscovery 走两个既有 Resolve 均返回 null（安全 no-op，不崩），待兄弟接入。
    /// </summary>
    public const string LookoutTelescopeDiscoveryId = "discovery_lookout_telescope";

    /// <summary>望远镜占位在瞭望关内的世界坐标（贴北墙，朝正北）。anim-lookout 据此放演出节点/镜头锚点。</summary>
    public static readonly Vector2 LookoutTelescopePosition = new(LevelW / 2f, 260f);

    // ——广播台：发出设备定点投放契约（RadioMainline 主线消费）——
    /// <summary>
    /// 广播台「发出设备」发现点 id（须与 <see cref="RadioMainline.TransmitterDiscoveryId"/> 一致）。踏入机房发现区即上报此 id。
    /// 挂点在 <c>CampMain.OnExplorationDiscovery</c>：取得发出设备 → <see cref="RadioMainline.GrantTransmitter"/> 推进状态 + 弹取设备叙事（<see cref="RadioMainline.TransmitterPickupNarrative"/>）。
    /// 定点非随机（用户 D4 拍板：主线关键物资保底/定点投放）。
    /// </summary>
    public const string BroadcastTransmitterDiscoveryId = RadioMainline.TransmitterDiscoveryId;

    /// <summary>发射机占位在广播台关内的世界坐标（机房内，贴发射塔基座）。</summary>
    public static readonly Vector2 BroadcastTransmitterPosition = new(LevelW / 2f, 300f);

    // ——南林村庄：上锁的屋子 / 围困 / 吠叫锚点（[SPEC-B11]，见 VillageRescue）——
    /// <summary>锁屋中心（关内世界坐标）：道格布鲁斯被困于此，屋内放二人一狗占位。</summary>
    private static readonly Vector2 VillageHouseCenter = new(1200f, 520f);
    /// <summary>锁屋南墙门缺口位（救援发现区锚点 + 布鲁斯吠叫飘字锚点；队伍自南侧入关循此而来）。</summary>
    private static readonly Vector2 VillageDoorPosition = new(1200f, 650f);
    private const float VillageHouseHalfW = 170f;
    private const float VillageHouseHalfH = 130f;

    private bool _villageActive;         // 本关＝南林村庄（决定 _Process 是否跑吠叫轮询）
    private bool _villageBarking;        // 布鲁斯当前是否在吠叫（"起吠"一次性，离中距离即清）
    private double _villageBarkCooldown;  // 距下次"汪！"飘字的剩余秒数

    private CameraController _camera = null!;
    private readonly List<Zombie> _zombies = new();
    // [SPEC-B13] 超市骗局：关内敌对幸存者（Raider 阵营）。轻信被诱入内圈伏击、或拒绝后闯内圈时按需生成（见 SpawnSupermarketRaiders）。
    private readonly List<Raider> _levelRaiders = new();
    private readonly Dictionary<Actor, Node2D> _markers = new();
    private readonly List<Rect2> _obstructions = new();
    private Area2D _returnZone = null!;
    private Node2D _actorLayer = null!;

    // 视野遮暗（批次4）：探索关全程启用。发现点视觉容器供检测层隐藏（视野外不揭示）。
    private VisionMask? _visionMask;

    // 批次4 光照：探索关固定光源场（预置油灯）。供玩家遮暗渲染（VisionMask.SetSourceProvider）与
    // 关内丧尸感知（ConfigurePerception 的 localLightAt）按位置查询最强光源贡献。位置/盏数拟定待调。
    private readonly LightField _levelLights = new();
    private readonly List<(Node2D container, Vector2 pos)> _discoveryVisuals = new();

    // 探索发现点：本关内已触发过的 discoveryId（防同一关内重复上报；跨关持久去重由 CampMain 的 flag 负责）。
    private readonly HashSet<string> _firedDiscoveries = new();

    // 战斗引擎依赖：与营地单位共用同一实例（由 CampMain 在 Initialize 前注入），
    // 关卡新建的丧尸经 Actor.Inject(Combat, Clock) 拿到它——缺则丧尸首帧 Think 解引用 Clock 崩。
    public CombatEngine Combat { get; set; } = null!;

    public override void Initialize()
    {
        BuildTerrain();
        SetupCamera();
        SetupReturnZone();
        PlaceTeam();
        SpawnZombies();

        // 按目的地叠加对应发现点（MVP 复用本测试关场景，仅换触发点；其余目的地无发现点，行为不变）：
        //   金手指帮根据地 → 帮众尸体 + 日记A（恒在）、复仇线另加克莉丝汀本人尸体；守望者森林小屋 → 哥顿上吊尸 + 日记B。
        // 哥顿与克莉丝汀异地（用户拍板），不再同关叠出。
        if (DestinationName == WorldMapPanel.GoldfingerBaseName)
            SetupGoldfingerCorpseDiscoveries();
        else if (DestinationName == WorldMapPanel.WatchersCabinName)
            SetupRangersCabin();
        else if (DestinationName == ExplorationCache.RiversideCabinName)
            SetupRiversideCabinCaches();
        else if (DestinationName == ExplorationCache.HarvesterWarehouseName)
            SetupHarvesterWarehouseCaches();
        else if (DestinationName == WorldMapPanel.CityRooftopLookoutName)
            SetupCityRooftopLookout();
        else if (DestinationName == NurseRecruit.DestinationName)
            SetupNightingalePharmacy();
        else if (DestinationName == WorldMapPanel.BroadcastStationName)
            SetupBroadcastStation();
        else if (DestinationName == VillageRescue.DestinationName)
            SetupSouthForestVillage();
        else if (DestinationName == ExplorationCache.EastNewVillageName)
            SetupEastNewVillage();
        else if (DestinationName == ExplorationCache.GasStationName)
            SetupGasStation();
        else if (DestinationName == ExplorationCache.SupermarketName)
            SetupSupermarket();
        else if (DestinationName == ExplorationCache.HospitalName)
            SetupHospital();

        // 叙事调查点（极乐迪斯科式，[SPEC-B12]）：按目的地迭代注册表铺 Area2D（与物资/主线点并存，命名空间隔离）。
        // 须在 SetupVisionMask 之前（视觉容器进 _discoveryVisuals，供视野外隐藏）。
        SetupNarrativeSpots();

        // 半身掩体（桌子/货架/沙袋）：非实心矮物，不进 _obstructions（不挡路/不挡视线/不挡子弹），
        // 只登记进掩体场——躲其后挨的远程攻击 25% 整发无效。须在导航烘焙前后皆可（本就不参与导航）。
        SetupCovers();

        // 视野遮暗（批次4）：探索关全程启用。发现点须在此之前铺好（进 _discoveryVisuals）。
        SetupVisionMask();

        // ⚠️ 导航必须**最后**烘焙：上面每个目的地的 Setup* 都会往 _obstructions 里加房间/锁屋的墙
        // （见 AddSolidWall）。旧代码在 BuildTerrain 之后就烘焙，后建的墙一律没进导航——AI 会照直穿墙寻路。
        // 这里一次性烘焙全部墙体，不做 NavigationRegion 的增量重烘焙（那条路要等 NavigationServer 同步，
        // headless 下尤其容易读到滞后的旧 map）。
        BuildNavigation();
    }

    /// <summary>
    /// 装配视野遮暗层（探索关全程启用）：以探索队为观察者、环境光按当前相位（探索=白昼满档）算锥形，
    /// 视野外网格遮暗 + 视野外丧尸/发现点隐藏。遮罩覆盖全关、cartesian 直绘（探索关本就 top-down cartesian）。
    /// </summary>
    // ---- 半身掩体（桌子/货架/沙袋）：远程 25% 整发无效 ----

    /// <summary>关内半身掩体场（非实心；承伤入口经 static <c>Actor.Covers</c> 查询）。</summary>
    private readonly CoverField _coverField = new();

    /// <summary>半身掩体的视觉色（翻倒的桌子/货架/沙袋，土褐；原型占位，非美术）。</summary>
    private static readonly Color CoverColor = new(0.50f, 0.44f, 0.32f);

    /// <summary>
    /// 铺关内半身掩体：<b>非实心</b>矮物——不建碰撞体、不进 <c>_obstructions</c>（故不挡路、不挖导航洞、
    /// 不断视线、不挡子弹），只登记进 <see cref="_coverField"/>：躲其后挨的<b>远程</b>攻击按 25% 整发判无效
    /// （方向性——敌人绕后即失效；双向对称——躲在其后的劫掠者/丧尸同样受保护）。
    ///
    /// <para>摆位（全为<b>拟定待调</b>占位）：撒在关内开阔地与入口回撤路线上，让遭遇战有可用的战术地形——
    /// 没有掩体的旷野交火就是纯站桩对射。<b>刻意不摆在墙边</b>（贴墙的掩体没意义：墙本就断视线）。</para>
    /// </summary>
    private void SetupCovers()
    {
        _coverField.Clear();

        // 关内散布的翻倒桌子/货架/沙袋（cartesian [x,y,w,h]，拟定待调）。
        (float x, float y, float w, float h)[] covers =
        {
            (1040f, 1300f, 110f, 30f),  // 入口内侧：回撤时可依托
            (1260f, 1300f, 110f, 30f),
            (700f, 820f, 120f, 32f),    // 中段西侧开阔地
            (1620f, 800f, 120f, 32f),   // 中段东侧开阔地
            (1130f, 540f, 130f, 30f),   // 深处（接近目标区）
            (420f, 1080f, 30f, 120f),   // 西翼竖排货架
            (1980f, 1080f, 30f, 120f),  // 东翼竖排货架
        };

        foreach ((float x, float y, float w, float h) c in covers)
        {
            _coverField.Add(c.x, c.y, c.w, c.h);
            AddChild(new Polygon2D
            {
                Polygon = Quad(new Vector2(c.x, c.y), new Vector2(c.w, c.h)),
                Color = CoverColor,
                ZIndex = 2, // 贴地（低于墙/人形），一眼看出是矮物而非墙
            });
        }

        // 场级接线：一切 Actor 的承伤入口经此查询（双向对称）。退场置 null，见 _ExitTree。
        Actor.Covers = _coverField;
    }

    public override void _ExitTree()
    {
        // 掩体场是 static：不清会把本关的桌椅货架带回营地场景。
        if (Actor.Covers == _coverField)
        {
            Actor.Covers = null;
        }
    }

    /// <summary>探索关预置固定光源（油灯示例，拟定待调）：入口附近 + 关内中段各一盏。</summary>
    private void SetupLevelLights()
    {
        _levelLights.Clear();
        _levelLights.AddFixed(LightSource.LampKey, LevelW / 2f, LevelH - 200f); // 入口/返回区附近
        _levelLights.AddFixed(LightSource.LampKey, LevelW * 0.5f, LevelH * 0.4f); // 关内中段
    }

    /// <summary>关内某点合成局部光照 L∈[0,1]（环境光与固定光源取 max），供丧尸感知 ConeFor 消费。</summary>
    private float SampleLevelLight(Vector2 pos)
        => VisionLogic.CombineLight(
            VisionLogic.AmbientLight(Clock.CurrentPhase, indoorsDark: false),
            _levelLights.StrongestAt(pos.X, pos.Y));

    private void SetupVisionMask()
    {
        SetupLevelLights();
        _visionMask = new VisionMask();
        _visionMask.Configure(new Rect2(0, 0, LevelW, LevelH), VisionMask.ProjectionMode.Cartesian);
        // 观察者＝存活探索队 + 随队布鲁斯（狗随队时也揭示视野，其感知锥同规则）。
        _visionMask.SetViewersProvider(() =>
        {
            IEnumerable<Actor> viewers = ExpeditionTeam.Where(p => p.Alive).Cast<Actor>();
            return CompanionDog is { Alive: true } dog ? viewers.Append(dog) : viewers;
        });
        _visionMask.SetAmbientProvider(() => VisionLogic.AmbientLight(Clock.CurrentPhase, indoorsDark: false));
        // 光源场：玩家侧遮暗按局部光照（灯旁视野更远），VisionMask 内部 CombineLight(ambient, 源贡献)。
        _visionMask.SetSourceProvider(pos => _levelLights.StrongestAt(pos.X, pos.Y));
        _visionMask.SetRevealablesProvider(Revealables);
        // 羁绊视野系数（道格锥角/布鲁斯视距·锥角按等级缩放）：CampMain 注入 BondScaleCone，与营地侧同口径，
        // 使道格带布鲁斯出探索的视野技能端到端生效。未注入（无羁绊上下文）则不缩放。
        if (ViewerConeAdjuster != null)
            _visionMask.SetViewerConeAdjuster(ViewerConeAdjuster);
        AddChild(_visionMask);
    }

    /// <summary>视野检测层的可揭示物：存活丧尸（隐 Actor 节点即隐其地面标记）+ 发现点视觉容器。</summary>
    private IEnumerable<(Vector2 worldPos, Action<bool> setVisible)> Revealables()
    {
        foreach (Zombie z in _zombies)
        {
            if (!IsInstanceValid(z) || !z.Alive)
                continue;
            Zombie captured = z;
            yield return (captured.GlobalPosition, v =>
            {
                if (IsInstanceValid(captured))
                    captured.Visible = v;
            });
        }

        foreach ((Node2D container, Vector2 pos) in _discoveryVisuals)
        {
            Node2D captured = container;
            yield return (pos, v =>
            {
                if (IsInstanceValid(captured))
                    captured.Visible = v;
            });
        }
    }

    public override void Cleanup()
    {
        foreach (var z in _zombies)
        {
            if (IsInstanceValid(z))
                z.QueueFree();
        }
        _zombies.Clear();

        foreach (var kv in _markers)
        {
            if (IsInstanceValid(kv.Value))
                kv.Value.QueueFree();
        }
        _markers.Clear();
    }

    private void BuildTerrain()
    {
        var ground = new Polygon2D
        {
            Polygon = Quad(Vector2.Zero, new Vector2(LevelW, LevelH)),
            Color = new Color(0.22f, 0.25f, 0.20f),
            ZIndex = -20,
        };
        AddChild(ground);

        AddWall(new Rect2(0, 0, LevelW, WallT), border: true);
        AddWall(new Rect2(0, LevelH - WallT, LevelW, WallT), border: true);
        AddWall(new Rect2(0, 0, WallT, LevelH), border: true);
        AddWall(new Rect2(LevelW - WallT, 0, WallT, LevelH), border: true);

        (Vector2 pos, Vector2 size)[] boxes =
        {
            (new Vector2(400, 300), new Vector2(80, 80)),
            (new Vector2(800, 600), new Vector2(100, 60)),
            (new Vector2(1400, 400), new Vector2(70, 90)),
            (new Vector2(1800, 700), new Vector2(90, 70)),
            (new Vector2(600, 1000), new Vector2(80, 80)),
            (new Vector2(1600, 1100), new Vector2(100, 60)),
        };
        foreach (var (pos, sz) in boxes)
            AddWall(new Rect2(pos, sz), border: false, color: new Color(0.38f, 0.34f, 0.26f));

        _actorLayer = new Node2D { Name = "Actors" };
        AddChild(_actorLayer);
    }

    private void AddWall(Rect2 rect, bool border, Color? color = null)
    {
        Color c = color ?? (border ? new Color(0.10f, 0.11f, 0.12f) : new Color(0.38f, 0.34f, 0.26f));

        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = 0b0100u;
        body.CollisionMask = 0u;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });

        var vis = new Polygon2D
        {
            Polygon = Quad(-rect.Size / 2, rect.Size),
            Color = c,
            ZIndex = -5,
        };
        body.AddChild(vis);
        AddChild(body);

        if (!border)
            _obstructions.Add(rect);
    }

    /// <summary>
    /// 实体墙段（关内建筑用）：<see cref="StaticBody2D"/> + 矩形碰撞（不逐点多边形）+ 进 <see cref="_obstructions"/> 供导航烘焙。
    /// 碰撞层＝<see cref="VisionOcclusion.WallMask"/>（0b0100），这一层同时被三方消费：
    /// 移动碰撞（挡路）、<see cref="BuildNavigation"/> 的 obstruction outline（阻断寻路）、
    /// <see cref="VisionOcclusion.IsOccluded"/> 射线（挡视线 → VisionMask 遮暗 + 丧尸/劫掠者感知）。
    /// 几何一律取自纯逻辑 <see cref="ExplorationWalls"/>（同一批矩形喂三处，不会各写一份）。
    /// <paramref name="zIndex"/> 保持各建筑原有的绘制层，外观与旧的纯视觉墙一致。
    /// </summary>
    private void AddSolidWall(WallRect wall, Color color, int zIndex)
    {
        var rect = new Rect2(wall.X, wall.Y, wall.Width, wall.Height);

        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionLayer = VisionOcclusion.WallMask;
        body.CollisionMask = 0u;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });
        body.AddChild(new Polygon2D
        {
            Polygon = Quad(-rect.Size / 2, rect.Size),
            Color = color,
            ZIndex = zIndex,
        });
        AddChild(body);

        _obstructions.Add(rect);
    }

    private void BuildNavigation()
    {
        var region = new NavigationRegion2D();
        var navPoly = new NavigationPolygon { AgentRadius = 14f };
        var src = new NavigationMeshSourceGeometryData2D();

        float inset = 22f;
        Rect2 outer = new(inset, inset, LevelW - inset * 2, LevelH - inset * 2);
        src.AddTraversableOutline(Quad(outer.Position, outer.Size));

        foreach (Rect2 obs in _obstructions)
            src.AddObstructionOutline(Quad(obs.Position, obs.Size));

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, src);
        region.NavigationPolygon = navPoly;
        AddChild(region);
    }

    private void SetupCamera()
    {
        _camera = new CameraController { Position = new Vector2(LevelW / 2, LevelH / 2) };
        _camera.SetBounds(new Rect2(0, 0, LevelW, LevelH));
        AddChild(_camera);
        _camera.MakeCurrent();
    }

    private void SetupReturnZone()
    {
        Vector2 pos = new(LevelW / 2 - 40, LevelH - 120);

        var visual = new Polygon2D
        {
            Polygon = Quad(Vector2.Zero, new Vector2(80, 80)),
            Color = new Color(0.2f, 0.8f, 0.2f, 0.5f),
            Position = pos,
            ZIndex = 10,
        };
        AddChild(visual);

        var arrow = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new(40, -50), new(60, -20), new(48, -20),
                new(48, 0), new(32, 0), new(32, -20),
                new(20, -20),
            },
            Color = new Color(0.3f, 0.9f, 0.3f),
            Position = pos,
            ZIndex = 11,
        };
        AddChild(arrow);

        _returnZone = new Area2D { Position = pos + new Vector2(40, 40) };
        var shape = new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(80, 80) } };
        _returnZone.AddChild(shape);
        _returnZone.CollisionMask = 0b0001u;
        AddChild(_returnZone);
        _returnZone.BodyEntered += OnReturnZoneBodyEntered;
    }

    private void OnReturnZoneBodyEntered(Node2D body)
    {
        if (body is Pawn)
            Callable.From(ReturnToCamp).CallDeferred();
    }

    private void PlaceTeam()
    {
        Vector2 spawn = new(200, LevelH - 200);
        float stepX = 40f;
        for (int i = 0; i < ExpeditionTeam.Count; i++)
        {
            Pawn p = ExpeditionTeam[i];
            p.Position = spawn + new Vector2(i * stepX, 0);
            p.Reparent(_actorLayer, keepGlobalTransform: false);
            _markers[p] = CreateActorMarker(p, p.BodyTint);
        }

        // 随队布鲁斯（若带上）：放在队伍旁、reparent 进关卡 actor 层。跟随道格/自主缠斗（关内敌对经 CampMain
        // 的敌对 provider 读 LevelHostiles）由其既有 AI 驱动；战斗引擎已在营地 Inject 过（跨关卡复用同一实例）。
        if (CompanionDog is { } dog)
        {
            dog.Position = spawn + new Vector2(ExpeditionTeam.Count * stepX, 0);
            dog.Reparent(_actorLayer, keepGlobalTransform: false);
            _markers[dog] = CreateActorMarker(dog, dog.BodyTint);
        }
    }

    /// <summary>关内丧尸的目标池＝存活探索队 + 随队布鲁斯（狗也可被咬/被杀）。</summary>
    private IEnumerable<Actor> LevelTargets()
    {
        foreach (Pawn p in ExpeditionTeam)
            if (p.Alive)
                yield return p;
        if (CompanionDog is { Alive: true } dog)
            yield return dog;
    }

    /// <summary>本关存活敌对单位＝存活丧尸 + 关内敌对幸存者（超市骗局伏击的 Raider）——供随队布鲁斯经 CampMain 敌对 provider 自主缠斗、视野揭示。</summary>
    public override IEnumerable<Actor> LevelHostiles()
    {
        foreach (Zombie z in _zombies)
            if (z.Alive)
                yield return z;
        foreach (Raider r in _levelRaiders)
            if (r.Alive)
                yield return r;
    }

    private void SpawnZombies()
    {
        // 南林村庄：围困锁屋的一圈丧尸（[SPEC-B11]），取代默认散布——见 SpawnVillageSiegeZombies。
        if (DestinationName == VillageRescue.DestinationName)
        {
            SpawnVillageSiegeZombies();
            return;
        }

        // 金手指帮根据地（[SPEC-B12-补] 中型·以战斗为主）：敌对密度较默认散布加强，向 gauntlet 中段与深处 loot 区（北侧）加权，
        // 逼玩家"打过才拿"深处的军械柜/头目区。数量/布点拟定待调，归 param-calibration 校准（勿铺成安全搜刮关）。
        if (DestinationName == WorldMapPanel.GoldfingerBaseName)
        {
            SpawnGoldfingerGuards();
            return;
        }

        // [SPEC-B13·拟设定待确认] 东部新村：游荡丧尸中等（7 只，band 6~8），散布在工地/老屋区，避开南侧排屋入口。
        if (DestinationName == ExplorationCache.EastNewVillageName)
        {
            SpawnZombiesAt(EastNewVillageZombieSpots);
            return;
        }

        // [SPEC-B13·拟设定待确认] 加油站：中低密度游荡丧尸（5 只，band 4~6），散布在便利店/修车棚/油罐区，避开南侧加油区入口。
        if (DestinationName == ExplorationCache.GasStationName)
        {
            SpawnZombiesAt(GasStationZombieSpots);
            return;
        }

        // [SPEC-B13] 超市：幸存者据点，无丧尸——威胁来自那伙人（骗局伏击的 Raider），不铺游荡丧尸。
        if (DestinationName == ExplorationCache.SupermarketName)
            return;

        // [SPEC-B13] 医院：丧尸巢废墟，密度显著高于他图（14 只，band 12~16），向住院部/药房/手术层深区扎堆——"大量丧尸占据"是其身份，数值拟定待调（归 param-calibration）。
        if (DestinationName == ExplorationCache.HospitalName)
        {
            SpawnZombiesAt(HospitalZombieSpots);
            return;
        }

        Vector2[] spots =
        {
            new(LevelW * 0.25f, LevelH * 0.3f),
            new(LevelW * 0.5f, LevelH * 0.25f),
            new(LevelW * 0.75f, LevelH * 0.3f),
            new(LevelW * 0.35f, LevelH * 0.6f),
            new(LevelW * 0.7f, LevelH * 0.55f),
        };
        var wander = new Rect2(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);

        foreach (Vector2 spot in spots)
            SpawnZombieAt(spot, wander);
    }

    /// <summary>[SPEC-B13] 在给定点位各造一只丧尸、共享一个覆盖全关的徘徊区（用于东部新村/加油站等按分区手铺敌对的关卡）。</summary>
    private void SpawnZombiesAt(IReadOnlyList<Vector2> spots)
    {
        var wander = new Rect2(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);
        foreach (Vector2 spot in spots)
            SpawnZombieAt(spot, wander);
    }

    /// <summary>[SPEC-B13·拟设定待确认] 东部新村游荡丧尸布点（中等 7 只，band 6~8）：散在工地/老屋分区之间，远离南侧排屋入口，数量/布点拟定待调（归 param-calibration）。</summary>
    private static readonly Vector2[] EastNewVillageZombieSpots =
    {
        new(720f, 980f),   // 工地·料场附近
        new(1080f, 900f),  // 工地·脚手架下
        new(1450f, 960f),  // 工地·工具棚附近
        new(900f, 720f),   // 工地·钢筋料区
        new(700f, 520f),   // 老屋区西
        new(1150f, 440f),  // 老屋区中
        new(1900f, 400f),  // 工头储物柜深处（守着高价值柜）
    };

    /// <summary>[SPEC-B13·拟设定待确认] 加油站游荡丧尸布点（中低 5 只，band 4~6）：散在便利店/修车棚/油罐区，远离南侧加油区入口，数量/布点拟定待调（归 param-calibration）。</summary>
    private static readonly Vector2[] GasStationZombieSpots =
    {
        new(1080f, 1050f), // 便利店·冷饮柜附近
        new(1020f, 720f),  // 修车棚·工位
        new(1480f, 760f),  // 修车棚·零件区
        new(1150f, 420f),  // 油罐区·油罐车旁
        new(1980f, 360f),  // 地下储油间深处（守着高价值燃油）
    };

    /// <summary>[SPEC-B13] 医院游荡丧尸布点（丧尸巢·高密度 14 只，band 12~16）：显著高于他图，向住院部/药房/手术层深区扎堆，"大量丧尸占据"是其身份。数量/布点拟定待调（归 param-calibration）。</summary>
    private static readonly Vector2[] HospitalZombieSpots =
    {
        // 门诊/急诊大厅（南·近，2·稀）
        new(700f, 1150f), new(1500f, 1200f),
        // 住院部（中，4）
        new(600f, 850f), new(1100f, 780f), new(1600f, 900f), new(900f, 650f),
        // 药房（北·深，4·扎堆守医疗）
        new(1200f, 450f), new(700f, 400f), new(1600f, 420f), new(2000f, 500f),
        // 手术层（最北·最深，4·扎堆守高价值医疗）
        new(1000f, 220f), new(1400f, 200f), new(1800f, 240f), new(500f, 260f),
    };

    /// <summary>造一只关内丧尸（与营地同 combat/clock、含随队布鲁斯的目标池、局部光照感知）并登记标记。</summary>
    /// <summary>
    /// 铺一只普通丧尸（随机日常着装：布衣/夹克/长裤/短裤…）。
    /// <para>
    /// TODO（authored·待用户设定）：要在某处摆一只**高难度精英丧尸**（穿护甲的），传 outfitName 点名即可——
    /// <c>Zombie.Create(wander, LevelTargets, outfitName: "防暴警察丧尸")</c>（板甲）或 <c>"军人丧尸"</c>（皮甲）。
    /// 精英预设不在随机池里，只会出现在被点名的地方。可用名字见 <see cref="ZombieOutfit.ElitePresets"/>
    /// （当前两套是 IsDraft 样板，等用户定稿）。**本 agent 未在任何关卡实际摆放精英丧尸**——那是 authored 工作。
    /// </para>
    /// </summary>
    private void SpawnZombieAt(Vector2 pos, Rect2 wander)
    {
        var z = Zombie.Create(wander, LevelTargets); // 目标池含随队布鲁斯（可被关内丧尸攻击/杀）
        z.Inject(Combat, Clock); // 与营地单位相同的 combat+clock，务必在入树/首个物理帧 Think 前完成
        z.ConfigurePerception(localLightAt: SampleLevelLight); // 固定光源→局部光照喂给（暴露走目标 CarriedLightIntensity 回落）
        z.Position = pos;
        _actorLayer.AddChild(z);
        _zombies.Add(z);
        _markers[z] = CreateActorMarker(z, new Color(0.45f, 0.6f, 0.35f));
    }

    // ——超市骗局伏击（[SPEC-B13]）——
    /// <summary>超市幸存者据点内圈中心（关内世界坐标）：接触点在门口、内圈房间在此、伏击/闯入的 Raider 在此周围生成。</summary>
    private static readonly Vector2 SupermarketDenCenter = new(1200f, 380f);

    /// <summary>
    /// [SPEC-B13] 生成超市骗局的敌对幸存者（Raider 阵营，近战匕首＝"背刺"语义），并可选施加潜行先手一击。
    /// CampMain 在玩家「轻信跟随」被诱入内圈（<paramref name="preemptiveStrike"/>=true）、或「拒绝」后闯入内圈抢物资（false，公平战）时调用。
    /// 先手一击复用 <see cref="NightWatchContest.PreemptiveStrikeMultiplier"/>(1.5x)、走既有承伤管道（<c>ReceiveAttack(damageFactor)</c>），不改战斗规则。
    /// 去重由 CampMain 侧 <see cref="SupermarketAmbush.AmbushSprungFlag"/> 负责（同一趟探索不重复刷敌）。
    /// </summary>
    public void SpawnSupermarketRaiders(int count, bool preemptiveStrike)
    {
        Vector2 center = SupermarketDenCenter;
        // 紧凑徘徊区（贴内圈房间），使他们围着玩家打转、堵住退路。
        var wander = new Rect2(center.X - 300f, center.Y - 220f, 600f, 440f);
        Raider? first = null;
        for (int i = 0; i < count; i++)
        {
            float ang = Mathf.Tau * i / System.Math.Max(1, count) - Mathf.Pi / 2f;
            Vector2 pos = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 90f;
            var r = Raider.Create(wander, LevelTargets, usePistol: false, displayName: "据点幸存者");
            r.Inject(Combat, Clock);
            r.ConfigurePerception(localLightAt: SampleLevelLight);
            r.Position = pos;
            _actorLayer.AddChild(r);
            _levelRaiders.Add(r);
            _markers[r] = CreateActorMarker(r, new Color(0.72f, 0.26f, 0.22f)); // 暗红：敌对幸存者（同 Raider.Create 体色，与丧尸绿/己方一眼区分）
            first ??= r;
        }

        if (preemptiveStrike && first is { } ambusher)
            ApplySupermarketPreemptiveStrike(ambusher, center);
    }

    /// <summary>潜行先手一击：以匕首对最近探索队员施一次 1.5x 承伤（背刺）。走既有承伤管道，不改战斗规则。</summary>
    private void ApplySupermarketPreemptiveStrike(Raider ambusher, Vector2 center)
    {
        Pawn? victim = null;
        float best = float.MaxValue;
        foreach (Pawn p in ExpeditionTeam)
        {
            if (!p.Alive)
                continue;
            float d = p.GlobalPosition.DistanceSquaredTo(center);
            if (d < best) { best = d; victim = p; }
        }
        if (victim is null)
            return;
        victim.ReceiveAttack(ambusher, CombatData.Dagger(), Combat, damageFactor: NightWatchContest.PreemptiveStrikeMultiplier);
        GD.Print($"[Supermarket] 背刺先手 ×{NightWatchContest.PreemptiveStrikeMultiplier:0.0} 命中 {victim.DisplayName}。");
    }

    /// <summary>金手指帮根据地守备丧尸数（中型·战斗为主，较默认 5 加强，拟定待调，归 param-calibration 校准）。</summary>
    private const int GoldfingerGuardZombieCount = 8;

    /// <summary>
    /// 金手指帮根据地守备布防（[SPEC-B12-补]）：<see cref="GoldfingerGuardZombieCount"/> 只丧尸，向根据地深处（北侧远角，
    /// 军械柜/头目区所在）与中段 gauntlet 加权布点，使深处 loot 必须打穿才能取。数量/布点拟定待调。
    /// </summary>
    private void SpawnGoldfingerGuards()
    {
        // 深处/中段加权：多数落在关卡上半（y 小＝北，深处 loot 区），少数在中段与近入口，逼出"打过才拿"。
        Vector2[] spots =
        {
            new(LevelW * 0.78f, LevelH * 0.18f), // 深·头目/银库区
            new(LevelW * 0.90f, LevelH * 0.15f), // 深·银库暗格侧
            new(LevelW * 0.70f, LevelH * 0.24f), // 深·军械柜侧
            new(LevelW * 0.55f, LevelH * 0.32f), // 中深·修械/弹药区
            new(LevelW * 0.62f, LevelH * 0.45f), // 中·皮件/gauntlet
            new(LevelW * 0.38f, LevelH * 0.40f), // 中·铺位/油料区
            new(LevelW * 0.30f, LevelH * 0.62f), // 中前·前院
            new(LevelW * 0.50f, LevelH * 0.72f), // 近入口·岗哨侧
        };
        var wander = new Rect2(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);
        for (int i = 0; i < GoldfingerGuardZombieCount && i < spots.Length; i++)
            SpawnZombieAt(spots[i], wander);
    }

    /// <summary>村庄区域游荡丧尸数（大点区域危险，锁屋 5 围困之外散布在各分区间，拟定待调）。</summary>
    private const int VillageWanderZombieCount = 4;

    /// <summary>
    /// 南林村庄：<see cref="VillageRescue.SiegeZombieCount"/> 只丧尸围困锁屋（[SPEC-B11]"被丧尸包围的屋子"）
    /// + <see cref="VillageWanderZombieCount"/> 只在村庄各区间游荡（大点区域危险，[SPEC-B11-补]）——
    /// 围困沿锁屋外围一圈布点、给一个贴着屋子的紧凑徘徊区，让它们在屋周打转＝围困本身即"丧尸向屋子聚集"的空间体现
    /// （用户口径"吠叫吸引丧尸向屋子聚集"由初始围困布局承载；吠叫仅作引导玩家的叙事飘字，不额外驱赶丧尸，避免过度惩罚）。
    /// 数量/半径拟定待调。
    /// </summary>
    private void SpawnVillageSiegeZombies()
    {
        // 贴着锁屋外围的紧凑徘徊区（屋子四周一圈），使丧尸在屋周打转、堵住入口。
        var siegeWander = new Rect2(
            VillageHouseCenter.X - VillageHouseHalfW - 160f,
            VillageHouseCenter.Y - VillageHouseHalfH - 160f,
            (VillageHouseHalfW + 160f) * 2f,
            (VillageHouseHalfH + 160f) * 2f);

        // 沿锁屋外围一圈均匀布点（含门口方向，堵住玩家开锁路径）。
        float ringRadius = VillageHouseHalfW + 90f;
        for (int i = 0; i < VillageRescue.SiegeZombieCount; i++)
        {
            float ang = Mathf.Tau * i / VillageRescue.SiegeZombieCount - Mathf.Pi / 2f; // 从正上方起，均分一圈
            var spot = VillageHouseCenter + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ringRadius;
            SpawnZombieAt(spot, siegeWander);
        }

        // 村庄区域游荡丧尸：散布在各分区之间（远离南入口，避免刚进关就贴脸），村域大徘徊区。
        var villageWander = new Rect2(WallT + 60, WallT + 60, LevelW - WallT * 2 - 120, LevelH - WallT * 2 - 120);
        Vector2[] wanderSpots =
        {
            new(700f, 780f),   // 民居区北
            new(1500f, 1150f), // 村中心南—村尾东南之间
            new(1750f, 560f),  // 村尾/卫生所—祠堂之间
            new(1000f, 460f),  // 锁屋以西空地
        };
        for (int i = 0; i < VillageWanderZombieCount && i < wanderSpots.Length; i++)
            SpawnZombieAt(wanderSpots[i], villageWander);
    }

    // ---------------- 探索发现点 ----------------

    /// <summary>
    /// 金手指帮根据地：两具尸体发现点（用户拍板，设计文档 §8.7）。
    /// · 帮众尸体（被克莉丝汀反杀，恒在、各分支可见）铺在靠近入口处，先被遇到；
    /// · 克莉丝汀本人尸体仅复仇线（<see cref="ExplorationLevel.ChristineLeftForRevenge"/>）铺出，位置更深，叙事递进（先帮众、再她本人）。
    /// 触发链路复用现有 AddDiscoveryPoint / DiscoveryPanel / GoldfingerDiscovery.Resolve。
    /// </summary>
    private void SetupGoldfingerCorpseDiscoveries()
    {
        AddDiscoveryPoint(
            GoldfingerDiscovery.GangMemberCorpseId,
            new Vector2(1150, 350),
            markerColor: new Color(0.7f, 0.2f, 0.2f),
            label: "遗体");

        if (ChristineLeftForRevenge)
            AddDiscoveryPoint(
                GoldfingerDiscovery.ChristineCorpseId,
                new Vector2(1950, 380),
                markerColor: new Color(0.78f, 0.15f, 0.28f),
                label: "遗体");

        // [SPEC-B12-补] 中型·战斗为主：11 处帮派储备物资点（发现点式；掉落/叙事在 ExplorationCache.Resolve）。
        // "打过才拿"——近入口(南侧)少、gauntlet 中段与根据地深处(北侧远角)多；与上方两具尸体发现点命名空间独立不冲突。
        // 近入口(2)：岗哨/前院。
        AddCachePoint(ExplorationCache.GoldfingerCheckpointId, new Vector2(600f, 1200f), "岗哨掩体");
        AddCachePoint(ExplorationCache.GoldfingerYardWreckId, new Vector2(900f, 1090f), "前院废车堆");
        // 中区 gauntlet(5)。
        AddCachePoint(ExplorationCache.GoldfingerBunksId, new Vector2(780f, 850f), "帮众铺位");
        AddCachePoint(ExplorationCache.GoldfingerAmmoCrateId, new Vector2(1220f, 780f), "弹药箱");
        AddCachePoint(ExplorationCache.GoldfingerGunBenchId, new Vector2(1050f, 650f), "修械台");
        AddCachePoint(ExplorationCache.GoldfingerHidePileId, new Vector2(1420f, 900f), "皮件堆");
        AddCachePoint(ExplorationCache.GoldfingerFuelStashId, new Vector2(700f, 600f), "油料桶");
        // 深处(4，根据地北侧远角，打穿才拿)：军械柜/头目保险柜/银库暗格/头目急救箱。
        AddCachePoint(ExplorationCache.GoldfingerArmoryId, new Vector2(1600f, 300f), "军械柜");
        AddCachePoint(ExplorationCache.GoldfingerBossSafeId, new Vector2(1850f, 250f), "头目保险柜");
        AddCachePoint(ExplorationCache.GoldfingerSilverCacheId, new Vector2(2010f, 180f), "银库暗格");
        AddCachePoint(ExplorationCache.GoldfingerBossMedkitId, new Vector2(1750f, 480f), "头目急救箱");
    }

    /// <summary>
    /// 守林人小屋（小点样板，用户 [SPEC-B11-补] 拍板；内部路由键＝守望者森林小屋 <see cref="WorldMapPanel.WatchersCabinName"/>，显示名正名为「守林人小屋」）：
    /// 屋子（含**屋中屋**——里屋一道内门/暗间，小点也有层次）+ **后院老树上哥顿上吊尸**（发现点）+ 日记B + 两处物资搜刮点（里屋碗柜/后院柴房，小点量级）。
    /// 哥顿一致性（设计文档 §3/§8.7）：帮主哥顿早已独自走进林中孤屋、上吊自杀于树上——此处按用户最新口径落**后院**老树（原文档为门口，已随正名同步）。
    /// 屋/树为占位视觉（无碰撞、不进导航；正式空间/美术待后续，同瞭望台/广播台占位口径）；发现点/搜刮点走既有 <see cref="AddDiscoveryPoint"/> 链路，
    /// 掉落解析在 CampMain.OnExplorationDiscovery（哥顿走 <see cref="GoldfingerDiscovery.Resolve"/>、两搜刮点走 <see cref="ExplorationCache.Resolve"/>）。
    /// </summary>
    private void SetupRangersCabin()
    {
        // —— 屋子（外屋）+ 里屋（暗间）占位轮廓：小点也有「屋中屋」层次 ——
        AddRoomOutline(new Rect2(980, 520, 480, 380), new Color(0.34f, 0.30f, 0.24f, 0.95f), "守林人小屋", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(1180, 630, 210, 200), new Color(0.28f, 0.25f, 0.21f, 0.95f), "里屋（暗间）", RoomEdge.Top);

        // 里屋碗柜（暗间内、路径较浅）：小点日常储粮/急救小物。
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinPantryId,
            new Vector2(1285, 730),
            markerColor: new Color(0.55f, 0.5f, 0.4f),
            label: "碗柜");

        // [SPEC-B12] 小点扩至 5（band 5~10 下限；仍是"阁楼/床底/门廊"一类小点，不破坏内容稀薄氛围）。
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinUnderbedId,
            new Vector2(1080, 640),
            markerColor: new Color(0.5f, 0.46f, 0.36f),
            label: "床底铁盒");
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinPorchId,
            new Vector2(1200, 960),
            markerColor: new Color(0.5f, 0.46f, 0.36f),
            label: "门廊工具架");
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinAtticId,
            new Vector2(1420, 560),
            markerColor: new Color(0.5f, 0.46f, 0.36f),
            label: "阁楼杂物");

        // —— 后院：老树 + 哥顿上吊尸占位 + 柴房搜刮点 ——
        AddBackyardTree(new Vector2(1720, 700));
        // 哥顿上吊尸（后院老树横枝）+ 日记B 发现点。与金手指帮根据地异地，独立一处。
        AddDiscoveryPoint(
            GoldfingerDiscovery.GordonHangedId,
            new Vector2(1720, 760),
            markerColor: new Color(0.55f, 0.5f, 0.45f),
            label: "上吊尸");
        // 后院柴房（藏深）：木料/绳/钉。
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinShedId,
            new Vector2(1720, 920),
            markerColor: new Color(0.5f, 0.44f, 0.34f),
            label: "柴房");
    }

    /// <summary>
    /// 画一圈房间占位轮廓：四条细墙边（<b>实体</b>——挡路 + 阻断寻路 + 挡视线，见 <see cref="AddSolidWall"/>），
    /// <paramref name="doorEdges"/> 上的每条边中段留门洞，附房间名标签。几何取自 <see cref="ExplorationWalls.RoomOutlineWalls"/>。
    /// </summary>
    private void AddRoomOutline(Rect2 rect, Color color, string label, params RoomEdge[] doorEdges)
    {
        var room = new WallRect(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
        foreach (WallRect wall in ExplorationWalls.RoomOutlineWalls(room, doorEdges))
            AddSolidWall(wall, color, zIndex: 6);

        var tag = new Label { Text = label, Position = rect.Position + new Vector2(6, -20), ZIndex = 12 };
        tag.AddThemeFontSizeOverride("font_size", 12);
        tag.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        AddChild(tag);
    }

    /// <summary>后院老树占位（纯视觉）：树干 + 树冠 + 一段吊绳，示意哥顿上吊处。<paramref name="basePos"/>＝树根位置。</summary>
    private void AddBackyardTree(Vector2 basePos)
    {
        AddChild(new Polygon2D // 树干
        {
            Polygon = Quad(basePos + new Vector2(-10, -60), new Vector2(20, 90)),
            Color = new Color(0.30f, 0.22f, 0.14f),
            ZIndex = 4,
        });
        AddChild(new Polygon2D // 树冠（粗略多边形）
        {
            Polygon = new Vector2[]
            {
                basePos + new Vector2(-72, -60), basePos + new Vector2(-42, -132),
                basePos + new Vector2(28, -152), basePos + new Vector2(82, -104),
                basePos + new Vector2(58, -52),
            },
            Color = new Color(0.20f, 0.30f, 0.18f, 0.95f),
            ZIndex = 5,
        });
        AddChild(new Polygon2D // 横枝垂下的吊绳（示意上吊处，正上方即哥顿发现点）
        {
            Polygon = Quad(basePos + new Vector2(-2, -96), new Vector2(3, 58)),
            Color = new Color(0.55f, 0.5f, 0.4f),
            ZIndex = 5,
        });
    }

    /// <summary>
    /// 南丁格尔的小药店（[SPEC-B13]，小点 5 物资点 + 护士相遇招募点 + 1 叙事调查点）：小店面 + 后屋药房 + 阁楼，小而有层次。
    /// 关内核心＝**可招募护士**（柜台后守店的清醒 NPC，踏入其警戒区弹 ChoicePanel 招募对话，见 <see cref="NurseRecruit"/> 与
    /// CampMain.PromptNurseRecruit）。物资＝基础药品/绷带为主但量薄（大头药品在医院），投放/叙事见 <see cref="ExplorationCache"/>。
    /// 叙事调查点（柜台留言板）由 <see cref="SetupNarrativeSpots"/> 按目的地自动铺（NarrativeSpotRegistry），此处不重复铺。
    /// </summary>
    private void SetupNightingalePharmacy()
    {
        // —— 小店面（临街）+ 后屋药房（暗间）+ 阁楼：小而有层次 ——
        // 小店面须开**两处**门洞：南＝临街外门，北＝通后屋药房。后屋药房的南门正对小店面的北墙，
        // 墙实体化后若北墙不开洞，后屋药房就三面实心 + 一门顶死＝玩家永远进不去（既有布局的通行性缺陷）。
        AddRoomOutline(new Rect2(900, 700, 500, 340), new Color(0.30f, 0.32f, 0.34f, 0.95f), "南丁格尔的小药店", RoomEdge.Bottom, RoomEdge.Top);
        AddRoomOutline(new Rect2(1000, 480, 320, 220), new Color(0.26f, 0.28f, 0.30f, 0.95f), "后屋药房", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(1440, 500, 240, 200), new Color(0.24f, 0.25f, 0.27f, 0.95f), "阁楼", RoomEdge.Left);

        // 柜台（纯视觉占位）：护士就守在它后头。
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(980, 820), new Vector2(320, 24)),
            Color = new Color(0.35f, 0.30f, 0.24f, 0.95f),
            ZIndex = 5,
        });

        // —— 护士相遇招募点（柜台后，NPC 非物资；踏入弹招募对话）——
        AddDiscoveryPoint(
            NurseRecruit.MeetDiscoveryId,
            new Vector2(1150, 850),
            markerColor: new Color(0.40f, 0.72f, 0.66f), // 青绿＝友方 NPC，与褐色搜刮点区分
            label: "护士");

        // —— 5 物资搜刮点：小店面(近) → 后屋药房(深) → 阁楼(最深)。量薄（小药店） ——
        AddDiscoveryPoint(ExplorationCache.PharmacyCounterId, new Vector2(1000, 950),
            markerColor: new Color(0.55f, 0.5f, 0.42f), label: "收银台");
        AddDiscoveryPoint(ExplorationCache.PharmacyShelfId, new Vector2(1330, 950),
            markerColor: new Color(0.55f, 0.5f, 0.42f), label: "货架");
        AddDiscoveryPoint(ExplorationCache.PharmacyDispensaryId, new Vector2(1080, 560),
            markerColor: new Color(0.5f, 0.46f, 0.38f), label: "处方柜");
        AddDiscoveryPoint(ExplorationCache.PharmacyColdBoxId, new Vector2(1240, 560),
            markerColor: new Color(0.5f, 0.46f, 0.38f), label: "冷藏箱");
        AddDiscoveryPoint(ExplorationCache.PharmacyAtticId, new Vector2(1560, 590),
            markerColor: new Color(0.5f, 0.44f, 0.36f), label: "阁楼杂物");
    }

    /// <summary>
    /// 河边小屋（前中期探索点，用户拍板）：两处搜刮点（发现点式，踏入即入库+弹环境叙事，投放/叙事见 <see cref="ExplorationCache"/>）。
    /// · 枪柜（← 栓动猎枪）铺在靠近入口处（近入口＝易得）；· 床底木箱（通用搜刮）位置更深。
    /// 触发链路复用现有 <see cref="AddDiscoveryPoint"/>；掉落解析在 CampMain.OnExplorationDiscovery 走 <see cref="ExplorationCache.Resolve"/>。
    /// </summary>
    private void SetupRiversideCabinCaches()
    {
        AddDiscoveryPoint(
            ExplorationCache.RiversideGunCabinetId,
            new Vector2(1100, 380),
            markerColor: new Color(0.55f, 0.42f, 0.28f),
            label: "枪柜");

        // [SPEC-B12] 小点扩至 5：灶膛(近)/渔具(近)、菜窖(深)。
        AddDiscoveryPoint(
            ExplorationCache.RiversideHearthId,
            new Vector2(1320, 420),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "灶膛橱柜");
        AddDiscoveryPoint(
            ExplorationCache.RiversideFishingId,
            new Vector2(920, 520),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "屋檐渔具箱");

        AddDiscoveryPoint(
            ExplorationCache.RiversideBedChestId,
            new Vector2(1850, 420),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "床底木箱");
        AddDiscoveryPoint(
            ExplorationCache.RiversideCellarId,
            new Vector2(2020, 660),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "屋后菜窖");
    }

    /// <summary>
    /// 联合收割机仓库（前中期探索点，用户拍板）：两处搜刮点（发现点式，投放/叙事见 <see cref="ExplorationCache"/>）。
    /// · 工具柜（←《木匠入门》，易得）铺在靠近入口处；· 阁楼铁皮箱（←《进阶木匠技术》，藏深）位置最深，难度梯度 draft 拟定待调。
    /// </summary>
    private void SetupHarvesterWarehouseCaches()
    {
        AddDiscoveryPoint(
            ExplorationCache.WarehouseToolCabinetId,
            new Vector2(1080, 400),
            markerColor: new Color(0.48f, 0.44f, 0.36f),
            label: "工具柜");

        // [SPEC-B12] 中点扩至 10（band 10~30 下限）：工业材料为主，近→深散布；食物/医疗仅休息角一处。
        AddDiscoveryPoint(ExplorationCache.WarehouseWorkbenchId, new Vector2(1300, 420), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "工作台");
        AddDiscoveryPoint(ExplorationCache.WarehouseBreakCornerId, new Vector2(880, 700), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "休息角");
        AddDiscoveryPoint(ExplorationCache.WarehousePartsBinId, new Vector2(1520, 620), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "零件料架");
        AddDiscoveryPoint(ExplorationCache.WarehouseFuelDrumId, new Vector2(1760, 460), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "油料桶区");
        AddDiscoveryPoint(ExplorationCache.WarehouseLumberRackId, new Vector2(1180, 920), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "木料架");
        AddDiscoveryPoint(ExplorationCache.WarehouseHayLoftId, new Vector2(1600, 1000), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "草料阁");
        AddDiscoveryPoint(ExplorationCache.WarehouseScrapPileId, new Vector2(720, 1080), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "废铁堆");
        AddDiscoveryPoint(ExplorationCache.WarehouseCombineCabId, new Vector2(1900, 860), markerColor: new Color(0.48f, 0.44f, 0.36f), label: "收割机驾驶室");

        AddDiscoveryPoint(
            ExplorationCache.WarehouseAtticChestId,
            new Vector2(2050, 1180),
            markerColor: new Color(0.5f, 0.46f, 0.4f),
            label: "阁楼铁皮箱");
    }

    /// <summary>
    /// 城市之巅瞭望观景台（前中期调查点，用户口径："用望远镜看到正北方黑压压上百万尸潮向南移动"）。
    /// 骨架＝复用本测试关地形，仅在北缘铺一处**望远镜可交互占位**（发现点式，踏入即上报 <see cref="LookoutTelescopeDiscoveryId"/>）。
    /// 演出/剧情/旗标接线由兄弟系统在 <c>CampMain.OnExplorationDiscovery</c> 的 TODO 挂点补齐（见 <see cref="LookoutTelescopeDiscoveryId"/> 注释）。
    /// 另铺两处同址物资搜刮点（游客服务台/瞭望员值班室，接 loot-story，物资/叙事见 <see cref="ExplorationCache"/>）。
    /// 占位美术：北缘一段观景护栏 + 望远镜标记；正式高层观景台空间布局/美术待后续。
    /// </summary>
    private void SetupCityRooftopLookout()
    {
        // 占位护栏：贴北墙一段横栏，示意"高层观景台面朝正北"（纯视觉，无碰撞）。
        var railing = new Polygon2D
        {
            Polygon = Quad(new Vector2(LookoutTelescopePosition.X - 240f, LookoutTelescopePosition.Y - 34f), new Vector2(480f, 10f)),
            Color = new Color(0.32f, 0.30f, 0.26f, 0.9f),
            ZIndex = 6,
        };
        AddChild(railing);

        // 望远镜可交互占位：踏入发现区即上报 telescope id（挂点见常量注释；anim-lookout 可用同坐标叠演出节点）。
        AddDiscoveryPoint(
            LookoutTelescopeDiscoveryId,
            LookoutTelescopePosition,
            markerColor: new Color(0.30f, 0.55f, 0.70f),
            label: "望远镜");

        // 同址物资搜刮点（接 loot-story HANDOFF；物资/叙事在 ExplorationCache.Resolve，落地走 CampMain.OnExplorationDiscovery→ExplorationCache）：
        //   · 游客服务台（浅/近入口，贴南入口侧，先被遇到）；· 瞭望员值班室（藏深，关内北侧远角，与望远镜同处高空值守区）。
        AddDiscoveryPoint(
            ExplorationCache.LookoutGiftShopId,
            new Vector2(600, 1250),
            markerColor: new Color(0.55f, 0.5f, 0.4f),
            label: "游客服务台");

        // [SPEC-B12] 小点扩至 5：贩卖机/员工储物柜(近中)、天台机房(深)。
        AddDiscoveryPoint(ExplorationCache.LookoutVendingId, new Vector2(900, 1150), markerColor: new Color(0.5f, 0.48f, 0.42f), label: "自动贩卖机");
        AddDiscoveryPoint(ExplorationCache.LookoutStaffLockerId, new Vector2(1300, 900), markerColor: new Color(0.5f, 0.48f, 0.42f), label: "员工储物柜");
        AddDiscoveryPoint(ExplorationCache.LookoutMachineRoomId, new Vector2(1750, 480), markerColor: new Color(0.5f, 0.48f, 0.42f), label: "天台机房");

        AddDiscoveryPoint(
            ExplorationCache.LookoutWardensRoomId,
            new Vector2(2050, 320),
            markerColor: new Color(0.5f, 0.48f, 0.44f),
            label: "瞭望员值班室");
    }

    /// <summary>
    /// 广播台（「dead signal」主线中后期探索点，用户 [SPEC-B8] 拍板）：在此**定点**取得「发出设备」（非随机，落实 D4 主线关键物资保底）。
    /// 骨架＝复用本测试关地形，在关内铺一处**发射机可交互占位**（发现点式，踏入即上报 <see cref="BroadcastTransmitterDiscoveryId"/>）+ 发射塔/机房占位视觉。
    /// 取设备/推进状态/叙事接线由 <c>CampMain.OnExplorationDiscovery</c> 的挂点补齐（<see cref="RadioMainline.GrantTransmitter"/> + 取设备叙事）。
    /// 另铺两处普通物资搜刮点（值班室茶水间/备件仓库，接 <see cref="ExplorationCache"/>）。
    /// 占位美术：机房地台 + 发射塔基座剪影 + 发射机标记；正式关卡空间/美术待后续。
    /// </summary>
    private void SetupBroadcastStation()
    {
        // 占位机房地台：发射机所在的一片方形地台（纯视觉，无碰撞）。
        var floor = new Polygon2D
        {
            Polygon = Quad(new Vector2(BroadcastTransmitterPosition.X - 220f, BroadcastTransmitterPosition.Y - 140f), new Vector2(440f, 300f)),
            Color = new Color(0.20f, 0.21f, 0.24f, 0.85f),
            ZIndex = 5,
        };
        AddChild(floor);

        // 占位发射塔基座：机房上方一个窄高的塔基剪影，示意"通讯发射塔"（纯视觉）。
        var towerBase = new Polygon2D
        {
            Polygon = Quad(new Vector2(BroadcastTransmitterPosition.X - 34f, BroadcastTransmitterPosition.Y - 120f), new Vector2(68f, 60f)),
            Color = new Color(0.34f, 0.30f, 0.24f, 0.9f),
            ZIndex = 6,
        };
        AddChild(towerBase);

        // 发射机可交互占位：踏入发现区即上报 transmitter id（挂点见常量注释；取得发出设备→推进主线状态）。
        AddDiscoveryPoint(
            BroadcastTransmitterDiscoveryId,
            BroadcastTransmitterPosition,
            markerColor: new Color(0.40f, 0.65f, 0.55f),
            label: "发射机");

        // 同址普通物资搜刮点（接 ExplorationCache；落地走 CampMain.OnExplorationDiscovery→ExplorationCache.Resolve）：
        //   · 值班室茶水间（浅/近入口，贴南入口侧，先被遇到）；· 备件仓库（藏深，关内北侧远角）。
        AddDiscoveryPoint(
            ExplorationCache.BroadcastBreakRoomId,
            new Vector2(650, 1230),
            markerColor: new Color(0.55f, 0.5f, 0.4f),
            label: "值班室茶水间");

        // [SPEC-B12] 中点扩至 10（band 10~30 下限）：电子/线材为主，近→深散布；食物仅食堂、医疗仅更衣室各一处。
        AddDiscoveryPoint(ExplorationCache.BroadcastOfficeId, new Vector2(920, 1000), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "台长办公室");
        AddDiscoveryPoint(ExplorationCache.BroadcastLockersId, new Vector2(1200, 1150), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "员工更衣室");
        AddDiscoveryPoint(ExplorationCache.BroadcastCanteenId, new Vector2(720, 900), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "食堂后厨");
        AddDiscoveryPoint(ExplorationCache.BroadcastStoreroomId, new Vector2(1420, 900), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "杂物储藏间");
        AddDiscoveryPoint(ExplorationCache.BroadcastGeneratorId, new Vector2(1020, 620), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "发电机房");
        AddDiscoveryPoint(ExplorationCache.BroadcastServerRackId, new Vector2(1520, 520), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "机架间");
        AddDiscoveryPoint(ExplorationCache.BroadcastArchiveId, new Vector2(1760, 720), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "资料室");
        AddDiscoveryPoint(ExplorationCache.BroadcastRoofAntennaId, new Vector2(1900, 940), markerColor: new Color(0.5f, 0.48f, 0.44f), label: "屋顶天线基座");

        AddDiscoveryPoint(
            ExplorationCache.BroadcastPartsStoreId,
            new Vector2(1980, 360),
            markerColor: new Color(0.5f, 0.48f, 0.44f),
            label: "备件仓库");
    }

    /// <summary>[SPEC-B13] 占位分区地台：一片半透明方形地台示意某个区域（纯视觉、无碰撞，同瞭望台/广播台占位口径）。</summary>
    private void AddZonePad(Vector2 topLeft, Vector2 size, Color color)
    {
        var pad = new Polygon2D
        {
            Polygon = Quad(topLeft, size),
            Color = color,
            ZIndex = 4,
        };
        AddChild(pad);
    }

    /// <summary>
    /// [SPEC-B13-补3·拟设定待确认] 东部新村（正名，内部路由键「住宅区」）：末日前在建的迁建安置区——半建成铁皮排屋 + 工地料场 + 已入住的几户老屋。
    /// 用户拍板"物资种类分散、量小，住宅区物资不单一不集中"→**30 处·杂而薄**（每点 1~2 件、品类混杂），戒掉"建材大户"单一身份。三分区近→深，一户户翻：
    ///   排屋区(南/近，11·每户厨房/衣柜/床底/阳台各一小点) → 工地区(中，8·维持偏建材) → 老屋区(北/深，11·含最深药箱)。
    /// 占位美术：三片分区地台 + 搜刮点标记（正式空间/美术待后续，同瞭望台/广播台占位口径）；掉落/叙事在 <see cref="ExplorationCache.Resolve"/>。
    /// 敌对布防见 <see cref="EastNewVillageZombieSpots"/>（游荡中等 7 只）；叙事调查点（乔迁对联/工地打卡板）见 NarrativeSpotRegistry。
    /// 铺点序＝ExplorationCache.CacheIdsFor(住宅区) 的近→深序；坐标皆拟定待调（点密，间距≥70px 避触发歧义）。
    /// </summary>
    private void SetupEastNewVillage()
    {
        // 分区占位地台（纯视觉）：排屋(南/暖灰)、工地(中/黄褐)、老屋(北/冷褐)。30 点加密后放宽覆盖范围。
        AddZonePad(new Vector2(420, 1010), new Vector2(1400, 360), new Color(0.30f, 0.29f, 0.27f, 0.55f));  // 排屋区
        AddZonePad(new Vector2(540, 560), new Vector2(1540, 480), new Color(0.34f, 0.30f, 0.20f, 0.55f));   // 工地区
        AddZonePad(new Vector2(540, 240), new Vector2(1460, 380), new Color(0.26f, 0.24f, 0.24f, 0.55f));   // 老屋区

        var near = new Color(0.55f, 0.5f, 0.4f);   // 排屋/近入口
        var mid = new Color(0.52f, 0.48f, 0.40f);  // 工地中段
        var deep = new Color(0.5f, 0.46f, 0.42f);  // 老屋深处

        // 排屋区（南/近，11·一户户翻：A/B/C/D 户各厨房/衣柜/床底/阳台等）
        AddDiscoveryPoint(ExplorationCache.NewVillageShowroomId, new Vector2(500, 1280), markerColor: near, label: "样板间客厅");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowKitchenId, new Vector2(760, 1280), markerColor: near, label: "A户厨房");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowAWardrobeId, new Vector2(760, 1140), markerColor: near, label: "A户衣柜");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowAUnderbedId, new Vector2(600, 1170), markerColor: near, label: "A户床底");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowBKitchenId, new Vector2(1000, 1300), markerColor: near, label: "B户厨房");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowBBalconyId, new Vector2(1000, 1160), markerColor: near, label: "B户阳台");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowBClosetId, new Vector2(1180, 1290), markerColor: near, label: "B户储物间");
        AddDiscoveryPoint(ExplorationCache.NewVillageUnfinishedId, new Vector2(1390, 1280), markerColor: near, label: "半成品单元");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowCShoeCabId, new Vector2(1580, 1200), markerColor: near, label: "C户玄关鞋柜");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowCBathId, new Vector2(1700, 1300), markerColor: near, label: "C户卫生间");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowDBalconyId, new Vector2(1160, 1040), markerColor: near, label: "D户阳台杂物");
        // 工地区（中，8·维持偏建材）
        AddDiscoveryPoint(ExplorationCache.NewVillageLumberYardId, new Vector2(620, 940), markerColor: mid, label: "料场木料垛");
        AddDiscoveryPoint(ExplorationCache.NewVillageScaffoldId, new Vector2(920, 880), markerColor: mid, label: "脚手架下");
        AddDiscoveryPoint(ExplorationCache.NewVillageToolShedId, new Vector2(1300, 940), markerColor: mid, label: "工地工具棚");
        AddDiscoveryPoint(ExplorationCache.NewVillageRebarPileId, new Vector2(760, 720), markerColor: mid, label: "钢筋碎料堆");
        AddDiscoveryPoint(ExplorationCache.NewVillageSiteOfficeId, new Vector2(1560, 800), markerColor: mid, label: "项目部工棚");
        AddDiscoveryPoint(ExplorationCache.NewVillageCementPileId, new Vector2(1080, 700), markerColor: mid, label: "水泥料堆");
        AddDiscoveryPoint(ExplorationCache.NewVillageElectricalBoxId, new Vector2(1400, 720), markerColor: mid, label: "临时配电箱");
        AddDiscoveryPoint(ExplorationCache.NewVillageForemanLockerId, new Vector2(1950, 620), markerColor: mid, label: "工头储物柜");
        // 老屋区（北/深，11·一户户翻，最深药箱）
        AddDiscoveryPoint(ExplorationCache.NewVillageOldKitchenId, new Vector2(620, 520), markerColor: deep, label: "老屋灶间");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldWardrobeId, new Vector2(620, 380), markerColor: deep, label: "老屋卧室衣柜");
        AddDiscoveryPoint(ExplorationCache.NewVillageRootCellarId, new Vector2(800, 300), markerColor: deep, label: "老屋菜窖");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldHallId, new Vector2(860, 480), markerColor: deep, label: "老屋堂屋");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldUnderbedId, new Vector2(1000, 400), markerColor: deep, label: "老屋床底");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldAtticId, new Vector2(1040, 560), markerColor: deep, label: "老屋阁楼");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2KitchenId, new Vector2(1280, 460), markerColor: deep, label: "二号老屋厨房");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2WoodshedId, new Vector2(1280, 320), markerColor: deep, label: "二号老屋柴房");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2YardId, new Vector2(1480, 420), markerColor: deep, label: "二号老屋院子");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2ShrineId, new Vector2(1680, 320), markerColor: deep, label: "老屋神龛");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2MedCabId, new Vector2(1880, 440), markerColor: new Color(0.56f, 0.5f, 0.42f), label: "老屋药箱");
    }

    /// <summary>
    /// [SPEC-B13·拟设定待确认] 加油站：公路加油站——加油区 + 便利店 + 修车棚 + 油罐区（地下储油间）。
    /// **燃油大户**（fuel 为火堆/油灯燃料的主要产出来源，呼应"固定光源耗燃油"点子；投放量拟定待调），
    /// 便利店食品少量 + 修车棚工具零件；中点下限 10 处搜刮，近→深：加油区(南/近) → 便利店(中) → 修车棚(中) → 油罐区(北/深·高价值)。
    /// 占位美术：四片分区地台 + 搜刮点标记（正式空间/美术待后续）；掉落/叙事在 <see cref="ExplorationCache.Resolve"/>。
    /// 敌对布防见 <see cref="GasStationZombieSpots"/>（中低 5 只）；叙事调查点（公路车龙/立柱油价牌）见 NarrativeSpotRegistry。
    /// </summary>
    private void SetupGasStation()
    {
        // 分区占位地台（纯视觉）：加油区(南/深灰罩棚)、便利店(中/暖光)、修车棚(中/油污)、油罐区(北/警戒黄)。
        AddZonePad(new Vector2(480, 1120), new Vector2(760, 340), new Color(0.22f, 0.23f, 0.25f, 0.6f));    // 加油区（罩棚）
        AddZonePad(new Vector2(560, 840), new Vector2(880, 300), new Color(0.30f, 0.28f, 0.22f, 0.55f));    // 便利店
        AddZonePad(new Vector2(880, 560), new Vector2(1080, 320), new Color(0.24f, 0.23f, 0.20f, 0.6f));    // 修车棚
        AddZonePad(new Vector2(980, 260), new Vector2(1180, 320), new Color(0.34f, 0.30f, 0.14f, 0.55f));   // 油罐区

        var near = new Color(0.5f, 0.52f, 0.5f);
        var storeC = new Color(0.55f, 0.5f, 0.4f);
        var repairC = new Color(0.5f, 0.48f, 0.44f);
        var fuelC = new Color(0.62f, 0.56f, 0.30f); // 燃油区偏暖黄，凸显燃油大户身份

        // 加油区（南/近）
        AddDiscoveryPoint(ExplorationCache.GasPumpIslandId, new Vector2(650, 1230), markerColor: fuelC, label: "加油岛");
        AddDiscoveryPoint(ExplorationCache.GasKioskId, new Vector2(980, 1180), markerColor: near, label: "收银亭");
        // 便利店（中，食品少量）
        AddDiscoveryPoint(ExplorationCache.GasStoreSnacksId, new Vector2(720, 1000), markerColor: storeC, label: "便利店零食货架");
        AddDiscoveryPoint(ExplorationCache.GasStoreDrinksId, new Vector2(1080, 1050), markerColor: storeC, label: "冷饮柜");
        AddDiscoveryPoint(ExplorationCache.GasStoreBackroomId, new Vector2(1250, 900), markerColor: storeC, label: "便利店里屋");
        // 修车棚（中，工具零件）
        AddDiscoveryPoint(ExplorationCache.GasRepairBayId, new Vector2(1020, 720), markerColor: repairC, label: "修车工位");
        AddDiscoveryPoint(ExplorationCache.GasPartsShelfId, new Vector2(1480, 760), markerColor: repairC, label: "零件货架");
        AddDiscoveryPoint(ExplorationCache.GasOilRackId, new Vector2(1750, 680), markerColor: fuelC, label: "机油货架");
        // 油罐区（北/深，燃油大户高价值）
        AddDiscoveryPoint(ExplorationCache.GasTankerId, new Vector2(1150, 420), markerColor: fuelC, label: "油罐车");
        AddDiscoveryPoint(ExplorationCache.GasUndergroundTankId, new Vector2(1980, 340), markerColor: fuelC, label: "地下储油间");
    }

    /// <summary>
    /// [SPEC-B13] 超市：一伙幸存者据守的卖场——外围卖场/仓储/后巷可搜刮（货架残余，食物身份但单点薄），内圈是他们的据点囤货。
    /// 骗局（用户原话"轻信会被骗进密闭小房间背刺围攻"）：门口接触点弹 <see cref="ChoicePanel"/> 二选一——
    ///   · 轻信跟随 → 被诱入内圈密室（<see cref="AddRoomOutline"/> 占位房，"走到房间触发"语义）→ 背刺围攻（<see cref="SpawnSupermarketRaiders"/> 施 1.5x 先手）；
    ///   · 不轻信 → 警告后可搜外围，内圈物资被占——踏入内圈闯入点即公平开战抢货。
    /// 分支时序/文本/去重旗标由 <see cref="SupermarketAmbush"/>（纯逻辑）+ CampMain.OnExplorationDiscovery 驱动；本方法只铺空间。
    /// 占位美术：分区地台 + 据点密室墙体 + 搜刮点标记（正式空间/美术待后续）。骗局后果细节 draft 待确认。
    /// </summary>
    private void SetupSupermarket()
    {
        // 分区占位地台（纯视觉）：卖场(南/主)、仓储(东北)、后巷(西北)。据点内圈另用密室墙体（AddRoomOutline）示意。
        AddZonePad(new Vector2(360, 860), new Vector2(1680, 560), new Color(0.24f, 0.25f, 0.27f, 0.55f)); // 卖场
        AddZonePad(new Vector2(1720, 540), new Vector2(560, 380), new Color(0.22f, 0.21f, 0.19f, 0.6f));  // 仓储区
        AddZonePad(new Vector2(240, 360), new Vector2(380, 380), new Color(0.18f, 0.19f, 0.18f, 0.62f));  // 后巷卸货区

        var shelfC = new Color(0.55f, 0.48f, 0.36f);  // 外围货架残余（棕黄，物资）
        var hoardC = new Color(0.60f, 0.50f, 0.34f);  // 内圈幸存者囤货（略暖，缴获感）
        var dangerC = new Color(0.78f, 0.30f, 0.24f); // 据点接触/闯入点（暗红：这里有人，危险）

        // 外围（近→中，7 处，货架残余）
        AddDiscoveryPoint(ExplorationCache.SupermarketCheckoutId, new Vector2(700, 1250), markerColor: shelfC, label: "收银台前区");
        AddDiscoveryPoint(ExplorationCache.SupermarketSnackAisleId, new Vector2(1000, 1050), markerColor: shelfC, label: "零食货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketCannedAisleId, new Vector2(1400, 1050), markerColor: shelfC, label: "罐头货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketHouseholdId, new Vector2(1700, 900), markerColor: shelfC, label: "日用百货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketHardwareId, new Vector2(600, 850), markerColor: shelfC, label: "五金杂货角");
        AddDiscoveryPoint(ExplorationCache.SupermarketStockroomId, new Vector2(2000, 700), markerColor: shelfC, label: "仓储区货架");
        AddDiscoveryPoint(ExplorationCache.SupermarketBackAlleyId, new Vector2(400, 500), markerColor: shelfC, label: "后巷卸货区");

        // 内圈·幸存者据点密室（占位墙体，南墙留门＝"密闭小房间"；囤货点落其中，打赢/闯入后可搜）。
        var den = SupermarketDenCenter; // (1200, 380)
        AddRoomOutline(new Rect2(den.X - 200f, den.Y - 150f, 400f, 300f), new Color(0.34f, 0.30f, 0.26f, 0.95f), "里屋", RoomEdge.Bottom);
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardFoodId, new Vector2(den.X - 100f, den.Y - 60f), markerColor: hoardC, label: "他们的囤粮");
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardMedsId, new Vector2(den.X + 100f, den.Y - 40f), markerColor: hoardC, label: "他们的药箱");
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardGearId, new Vector2(den.X - 80f, den.Y + 50f), markerColor: hoardC, label: "缴获装备堆");
        AddDiscoveryPoint(ExplorationCache.SupermarketHoardStashId, new Vector2(den.X + 90f, den.Y + 50f), markerColor: hoardC, label: "头目私囤");

        // 骗局接触点（门口，靠内圈南侧）：踏入弹接触对话（CampMain 走 SupermarketAmbush）。zone 略大稳稳接住。
        AddDiscoveryPoint(SupermarketAmbush.ContactDiscoveryId, new Vector2(den.X, den.Y + 240f), markerColor: dangerC, label: "有人招呼", zoneSize: new Vector2(150f, 130f));
        // 内圈闯入点（门槛处，介于接触点与囤货之间）：拒绝招呼后踏入即公平开战抢被占物资。
        AddDiscoveryPoint(SupermarketAmbush.InnerRingDiscoveryId, new Vector2(den.X, den.Y + 120f), markerColor: dangerC, label: "里屋（有人把守）", zoneSize: new Vector2(140f, 90f));
    }

    /// <summary>
    /// [SPEC-B13] 医院：被大量丧尸占据的废墟（丧尸密度显著高于他图，见 <see cref="HospitalZombieSpots"/>），高风险高收益——
    /// 医疗物资集中投放于药房/手术层（打破全域"禁医疗灌水"的例外点，正是医院身份）。分区近→深：
    ///   门诊/急诊大厅(南/近) → 住院部(中) → 药房(北/深·医疗集中) → 手术层(最北/最深·手术耗材+高价值医疗)。
    /// 占位美术：四片分区地台 + 30 处搜刮点标记；掉落/叙事在 <see cref="ExplorationCache.Resolve"/>。叙事调查点（分诊台公告/住院部病房）见 NarrativeSpotRegistry。
    /// </summary>
    private void SetupHospital()
    {
        // 分区占位地台（纯视觉）：门诊/急诊(南/近)、住院部(中)、药房(深)、手术层(最深)。越深越"洁净"色调、也越危险。
        AddZonePad(new Vector2(320, 1080), new Vector2(1760, 380), new Color(0.24f, 0.24f, 0.26f, 0.55f)); // 门诊/急诊大厅
        AddZonePad(new Vector2(300, 620), new Vector2(1820, 420), new Color(0.22f, 0.25f, 0.28f, 0.55f));  // 住院部
        AddZonePad(new Vector2(400, 380), new Vector2(1780, 220), new Color(0.20f, 0.28f, 0.24f, 0.58f));  // 药房（医疗集中，偏药绿）
        AddZonePad(new Vector2(300, 120), new Vector2(1900, 240), new Color(0.28f, 0.30f, 0.32f, 0.60f));  // 手术层（无菌灰白，最深）

        var lobbyC = new Color(0.5f, 0.5f, 0.5f);      // 门诊/急诊（非医疗为主）
        var wardC = new Color(0.5f, 0.54f, 0.56f);     // 住院部
        var pharmC = new Color(0.42f, 0.62f, 0.44f);   // 药房（医疗，药绿）
        var orC = new Color(0.62f, 0.66f, 0.62f);      // 手术层（无菌灰白·高价值）

        // 门诊/急诊大厅（近，7·非医疗为主）
        AddDiscoveryPoint(ExplorationCache.HospitalReceptionId, new Vector2(700, 1300), markerColor: lobbyC, label: "挂号台");
        AddDiscoveryPoint(ExplorationCache.HospitalTriageId, new Vector2(900, 1150), markerColor: lobbyC, label: "分诊台");
        AddDiscoveryPoint(ExplorationCache.HospitalWaitingRoomId, new Vector2(1200, 1250), markerColor: lobbyC, label: "候诊区");
        AddDiscoveryPoint(ExplorationCache.HospitalVendingId, new Vector2(1500, 1300), markerColor: lobbyC, label: "自动贩卖机");
        AddDiscoveryPoint(ExplorationCache.HospitalErTrolleyId, new Vector2(1750, 1150), markerColor: lobbyC, label: "急诊抢救推车");
        AddDiscoveryPoint(ExplorationCache.HospitalSecurityId, new Vector2(400, 1150), markerColor: lobbyC, label: "保安室");
        AddDiscoveryPoint(ExplorationCache.HospitalCafeteriaId, new Vector2(2000, 1250), markerColor: lobbyC, label: "食堂");

        // 住院部（中，8）
        AddDiscoveryPoint(ExplorationCache.HospitalWardLinenId, new Vector2(600, 900), markerColor: wardC, label: "病房布草间");
        AddDiscoveryPoint(ExplorationCache.HospitalWardLockerId, new Vector2(900, 850), markerColor: wardC, label: "病床储物柜");
        AddDiscoveryPoint(ExplorationCache.HospitalNurseStationId, new Vector2(1200, 900), markerColor: pharmC, label: "护士站");
        AddDiscoveryPoint(ExplorationCache.HospitalDoctorOfficeId, new Vector2(1600, 850), markerColor: wardC, label: "医生办公室");
        AddDiscoveryPoint(ExplorationCache.HospitalDirtyUtilityId, new Vector2(1900, 950), markerColor: wardC, label: "污物处置间");
        AddDiscoveryPoint(ExplorationCache.HospitalKitchenetteId, new Vector2(700, 680), markerColor: wardC, label: "配餐间");
        AddDiscoveryPoint(ExplorationCache.HospitalFloorStoreId, new Vector2(2050, 700), markerColor: wardC, label: "楼层库房");
        AddDiscoveryPoint(ExplorationCache.HospitalMorgueId, new Vector2(350, 700), markerColor: new Color(0.42f, 0.44f, 0.5f), label: "太平间");

        // 药房（深，7·医疗集中——高价值）
        AddDiscoveryPoint(ExplorationCache.HospitalPharmacyCounterId, new Vector2(700, 520), markerColor: pharmC, label: "药房前台");
        AddDiscoveryPoint(ExplorationCache.HospitalPharmacyShelfId, new Vector2(1000, 470), markerColor: pharmC, label: "处方药架");
        AddDiscoveryPoint(ExplorationCache.HospitalPharmacyFridgeId, new Vector2(1300, 500), markerColor: pharmC, label: "冷藏药柜");
        AddDiscoveryPoint(ExplorationCache.HospitalPharmacyBackId, new Vector2(1600, 460), markerColor: pharmC, label: "药库后间");
        AddDiscoveryPoint(ExplorationCache.HospitalNarcoticsCabinetId, new Vector2(1900, 520), markerColor: pharmC, label: "管制药柜");
        AddDiscoveryPoint(ExplorationCache.HospitalDispensaryId, new Vector2(500, 420), markerColor: pharmC, label: "配药室");
        AddDiscoveryPoint(ExplorationCache.HospitalMedSupplyRoomId, new Vector2(2100, 460), markerColor: pharmC, label: "医材库");

        // 手术层（最深，8·手术耗材+高价值医疗）
        AddDiscoveryPoint(ExplorationCache.HospitalOrScrubId, new Vector2(600, 300), markerColor: orC, label: "刷手准备间");
        AddDiscoveryPoint(ExplorationCache.HospitalOrTheatreId, new Vector2(900, 240), markerColor: orC, label: "手术室");
        AddDiscoveryPoint(ExplorationCache.HospitalSterileStoreId, new Vector2(1200, 300), markerColor: orC, label: "无菌耗材库");
        AddDiscoveryPoint(ExplorationCache.HospitalIcuId, new Vector2(1500, 240), markerColor: orC, label: "ICU 重症监护");
        AddDiscoveryPoint(ExplorationCache.HospitalBloodBankId, new Vector2(1800, 300), markerColor: orC, label: "血库");
        AddDiscoveryPoint(ExplorationCache.HospitalAnesthesiaId, new Vector2(2050, 240), markerColor: orC, label: "麻醉科");
        AddDiscoveryPoint(ExplorationCache.HospitalSterilizerId, new Vector2(350, 280), markerColor: orC, label: "器械灭菌室");
        AddDiscoveryPoint(ExplorationCache.HospitalChiefSafeId, new Vector2(1250, 150), markerColor: new Color(0.72f, 0.68f, 0.5f), label: "主任药品保险柜");
    }

    /// <summary>
    /// 南林村庄（道格与布鲁斯正史入队地，用户 [SPEC-B11]）：几栋占位民居 + 一栋**上锁的屋子**（道格布鲁斯被困其中）。
    /// 触发链（时序）：调查团自南侧入关 → 靠近锁屋**中距离**（<see cref="VillageRescue.BarkTriggerRadius"/>）→
    /// 布鲁斯**吠叫**（_Process 距离轮询 + "汪！"飘字，引导玩家循声找过去；围困丧尸由 <see cref="SpawnVillageSiegeZombies"/> 布好）→
    /// 玩家清/绕丧尸 → 踏入锁屋门（救援发现区）→ 上报 <see cref="VillageRescue.RescueDiscoveryId"/> →
    /// CampMain 走 <see cref="VillageRescue.Resolve"/> 出救援叙事 + 置 rescued 旗标 → 回营正史注入道格布鲁斯（饿昏迷低档）。
    /// 占位美术：视觉墙体 + 门缺口 + 二人一狗屋内占位；碰撞实体化墙 + 导航重烘焙为遗留精修（当前墙纯视觉、不挡路，
    /// 救援触发靠门发现区，队伍自南门自然进屋即触发）。
    /// </summary>
    private void SetupSouthForestVillage()
    {
        _villageActive = true; // 开 _Process 吠叫轮询

        // ——空间分区（大点，[SPEC-B11-补]"5天+探索量"）：村口 → 民居区 → 村中心 → 村尾/藏深，锁屋救援只是其中一区——
        // 各区占位民居（纯视觉，示意聚落；正式布局/美术待后续）。搜刮点/叙事在 ExplorationCache，落地走 CampMain.OnExplorationDiscovery→ExplorationCache.Resolve。

        // [SPEC-B12] 大点 9→30（band 30+ 硬口径）：既有四分区加密 + 新增后山/河滩两分区；
        // 单点掉落调薄（食物散布 7 处、医疗集中候车棚 1 + 后山洞深藏 1），总量对齐经济压力口径（拟定待调）。

        // 村口/杂物区（南入口侧，先被遇到）：皮卡 + 岗亭 + 废三轮。
        DrawHousePlaceholder(new Vector2(700f, 1300f), new Vector2(160f, 120f)); // 村口小屋
        AddCachePoint(ExplorationCache.VillageRoadsideCarId, new Vector2(450f, 1300f), "皮卡");
        DrawHousePlaceholder(new Vector2(300f, 1180f), new Vector2(110f, 100f));  // 岗亭
        AddCachePoint(ExplorationCache.VillageGatePostId, new Vector2(300f, 1180f), "岗亭");
        AddCachePoint(ExplorationCache.VillageTrikeId, new Vector2(620f, 1150f), "废三轮");

        // 民居区（西/中南，多户人家）：厨房 / 卧室衣柜 / 主卧梳妆台 / 院子菜畦 / 鸡窝棚 / 灶房米缸 / 阁楼 / 柴垛 / 储藏间。
        DrawHousePlaceholder(new Vector2(500f, 900f), new Vector2(230f, 180f));
        AddCachePoint(ExplorationCache.VillageKitchenId, new Vector2(500f, 900f), "厨房碗柜");
        AddCachePoint(ExplorationCache.VillagePantry2Id, new Vector2(560f, 980f), "米缸");
        DrawHousePlaceholder(new Vector2(870f, 1080f), new Vector2(210f, 170f));
        AddCachePoint(ExplorationCache.VillageWardrobeId, new Vector2(870f, 1080f), "卧室衣柜");
        AddCachePoint(ExplorationCache.VillageBedroom2Id, new Vector2(930f, 1010f), "梳妆台");
        DrawHousePlaceholder(new Vector2(690f, 720f), new Vector2(200f, 170f));
        AddCachePoint(ExplorationCache.VillageCourtyardId, new Vector2(690f, 720f), "院子菜畦");
        AddCachePoint(ExplorationCache.VillageCoopId, new Vector2(760f, 640f), "鸡窝棚");
        DrawHousePlaceholder(new Vector2(470f, 560f), new Vector2(200f, 170f));
        AddCachePoint(ExplorationCache.VillageBackRoomId, new Vector2(470f, 560f), "储藏间");
        AddCachePoint(ExplorationCache.VillageLoftId, new Vector2(400f, 620f), "阁楼");
        AddCachePoint(ExplorationCache.VillageWoodpileId, new Vector2(560f, 490f), "柴垛");

        // 村中心（中部，公共设施）：小卖部 / 供销社仓 / 候车棚 / 村小 / 水井工具箱 / 铁匠铺。
        DrawHousePlaceholder(new Vector2(1200f, 1080f), new Vector2(240f, 170f)); // 小卖部
        AddCachePoint(ExplorationCache.VillageShopShelfId, new Vector2(1200f, 1080f), "小卖部");
        DrawHousePlaceholder(new Vector2(1000f, 1120f), new Vector2(180f, 150f)); // 供销社仓
        AddCachePoint(ExplorationCache.VillageCoopStoreId, new Vector2(1000f, 1120f), "供销社仓");
        AddCachePoint(ExplorationCache.VillageBusStopId, new Vector2(1350f, 1180f), "候车棚");
        DrawHousePlaceholder(new Vector2(1400f, 900f), new Vector2(230f, 180f));  // 村小
        AddCachePoint(ExplorationCache.VillageSchoolId, new Vector2(1400f, 900f), "村小教室");
        DrawWellPlaceholder(new Vector2(1120f, 860f));
        AddCachePoint(ExplorationCache.VillageWellToolboxId, new Vector2(1120f, 860f), "水井工具箱");
        DrawHousePlaceholder(new Vector2(1500f, 1120f), new Vector2(150f, 140f)); // 铁匠铺
        AddCachePoint(ExplorationCache.VillageForgeId, new Vector2(1500f, 1120f), "铁匠铺");

        // 村尾/藏深（东/北远角，难度梯度尾）：农具棚 / 谷仓 / 养蜂棚 / 坟场看守屋 / 祠堂 / 卫生所。
        DrawHousePlaceholder(new Vector2(1950f, 1180f), new Vector2(250f, 170f)); // 农具棚
        AddCachePoint(ExplorationCache.VillageToolShedId, new Vector2(1950f, 1180f), "农具棚");
        DrawHousePlaceholder(new Vector2(1680f, 1120f), new Vector2(220f, 180f)); // 谷仓
        AddCachePoint(ExplorationCache.VillageBarnId, new Vector2(1680f, 1120f), "打谷场谷仓");
        AddCachePoint(ExplorationCache.VillageBeehiveId, new Vector2(2000f, 980f), "养蜂棚");
        DrawHousePlaceholder(new Vector2(1650f, 520f), new Vector2(150f, 140f));  // 坟场看守屋
        AddCachePoint(ExplorationCache.VillageGraveHutId, new Vector2(1650f, 520f), "坟场看守屋");
        DrawHousePlaceholder(new Vector2(2000f, 420f), new Vector2(220f, 190f)); // 祠堂
        AddCachePoint(ExplorationCache.VillageShrineId, new Vector2(2000f, 420f), "祠堂");
        DrawHousePlaceholder(new Vector2(1850f, 760f), new Vector2(210f, 180f)); // 卫生所
        AddCachePoint(ExplorationCache.VillageClinicId, new Vector2(1850f, 760f), "卫生所");

        // 新分区·后山（最北，藏深；山洞暗格＝医疗深藏奖励）：猎人窝棚 / 炭窑 / 山洞暗格。
        DrawHousePlaceholder(new Vector2(1100f, 220f), new Vector2(140f, 120f)); // 猎人窝棚
        AddCachePoint(ExplorationCache.VillageBackhillBlindId, new Vector2(1100f, 220f), "猎人窝棚");
        AddCachePoint(ExplorationCache.VillageBackhillKilnId, new Vector2(1450f, 260f), "炭窑");
        AddCachePoint(ExplorationCache.VillageBackhillCaveId, new Vector2(1750f, 180f), "山洞暗格");

        // 新分区·河滩（最西，沿河带）：搁浅小船 / 晒鱼棚 / 抽水泵房。
        AddCachePoint(ExplorationCache.VillageRiverbankBoatId, new Vector2(200f, 900f), "搁浅小船");
        DrawHousePlaceholder(new Vector2(230f, 720f), new Vector2(130f, 110f)); // 晒鱼棚
        AddCachePoint(ExplorationCache.VillageRiverbankShackId, new Vector2(230f, 720f), "晒鱼棚");
        DrawHousePlaceholder(new Vector2(210f, 460f), new Vector2(130f, 120f)); // 抽水泵房
        AddCachePoint(ExplorationCache.VillageRiverbankPumpId, new Vector2(210f, 460f), "抽水泵房");

        // ——核心区：上锁的屋子（道格布鲁斯被困，中北部）——
        // 四面视觉墙 + 南墙门缺口。
        DrawLockedHouse();

        // 屋内被困的道格 + 布鲁斯占位标记（真正的 Pawn/Dog 于回营时由 CampMain 正史注入，此处仅示意"饿昏迷的人 + 守着他的狗"）。
        DrawTrappedPlaceholders();

        // 上锁的门＝救援发现点：踏入门缺口即上报救援 id（zone 略大于门口，稳稳接住"撬门进屋"这一刻）。
        // 挂点：CampMain.OnExplorationDiscovery → VillageRescue.Resolve（出救援叙事 + 置 rescued 旗标）；真正入队延到回营。
        // 救援为主线入队触发，**不计入**物资完成度 X/Y（同瞭望台望远镜口径，见 ExplorationProgress）。
        // 破锁"耗时/交互进度条"为遗留可选（当前＝直接开，最小侵入）。
        AddDiscoveryPoint(
            VillageRescue.RescueDiscoveryId,
            VillageDoorPosition,
            markerColor: new Color(0.78f, 0.66f, 0.42f),
            label: "门（上锁）",
            zoneSize: new Vector2(130f, 130f));
    }

    /// <summary>铺一处村庄物资搜刮点（发现点式，踏入即经 ExplorationCache.Resolve 落地掉落+叙事）。棕黄标记区别于剧情点。</summary>
    private void AddCachePoint(string cacheId, Vector2 pos, string label)
        => AddDiscoveryPoint(cacheId, pos, markerColor: new Color(0.55f, 0.48f, 0.36f), label: label);

    /// <summary>画一口占位水井（纯视觉：石圈 + 井口深色，无碰撞）：村中心地标。</summary>
    private void DrawWellPlaceholder(Vector2 center)
    {
        var ring = new Polygon2D
        {
            Polygon = Quad(center - new Vector2(40f, 40f), new Vector2(80f, 80f)),
            Color = new Color(0.36f, 0.34f, 0.30f, 0.95f),
            ZIndex = 4,
        };
        AddChild(ring);
        var mouth = new Polygon2D
        {
            Polygon = Quad(center - new Vector2(26f, 26f), new Vector2(52f, 52f)),
            Color = new Color(0.08f, 0.09f, 0.10f, 0.95f),
            ZIndex = 5,
        };
        AddChild(mouth);
    }

    /// <summary>画一栋占位民居（纯视觉方框 + 描边，无碰撞）：示意村庄里的其他房屋。</summary>
    private void DrawHousePlaceholder(Vector2 center, Vector2 size)
    {
        var body = new Polygon2D
        {
            Polygon = Quad(center - size / 2f, size),
            Color = new Color(0.30f, 0.27f, 0.23f, 0.85f),
            ZIndex = 4,
        };
        AddChild(body);
        var roof = new Polygon2D
        {
            Polygon = Quad(center - size / 2f - new Vector2(6f, 6f), new Vector2(size.X + 12f, 10f)),
            Color = new Color(0.22f, 0.19f, 0.16f, 0.9f),
            ZIndex = 5,
        };
        AddChild(roof);
    }

    /// <summary>
    /// 画上锁的屋子：四面<b>实体</b>墙围合（挡路 + 阻断寻路 + 挡视线，见 <see cref="AddSolidWall"/>），
    /// 南墙留一处门缺口（<see cref="VillageDoorPosition"/> 处）＝唯一通路——"被困"由此在空间上真正成立
    /// （旧版四面墙是纯 Polygon2D，丧尸/玩家可径直穿墙而过，围困形同虚设）。
    /// 几何取自 <see cref="ExplorationWalls.LockedHouseWalls"/>（含西侧两角的补角：旧几何漏了两个 t×t 对角洞）。
    /// </summary>
    private void DrawLockedHouse()
    {
        Vector2 c = VillageHouseCenter;
        float hw = VillageHouseHalfW, hh = VillageHouseHalfH;
        var wallColor = new Color(0.34f, 0.30f, 0.25f, 0.95f);

        foreach (WallRect wall in ExplorationWalls.LockedHouseWalls(c.X, c.Y, hw, hh))
            AddSolidWall(wall, wallColor, zIndex: 4);

        // 地板底色（示意室内），ZIndex 低于占位标记。
        var floor = new Polygon2D
        {
            Polygon = Quad(new Vector2(c.X - hw, c.Y - hh), new Vector2(hw * 2f, hh * 2f)),
            Color = new Color(0.24f, 0.22f, 0.19f, 0.8f),
            ZIndex = 3,
        };
        AddChild(floor);
    }

    /// <summary>屋内占位：饿昏迷的道格（横卧色块）+ 守在身边的布鲁斯（小色块），纯视觉示意。</summary>
    private void DrawTrappedPlaceholders()
    {
        // 道格：屋内靠里，横卧姿态（宽扁色块）示意"倒地昏迷"。
        var doug = new Polygon2D
        {
            Polygon = Quad(VillageHouseCenter + new Vector2(-46f, -30f), new Vector2(52f, 22f)),
            Color = new Color(0.62f, 0.56f, 0.42f, 0.95f),
            ZIndex = 7,
        };
        AddChild(doug);
        // 布鲁斯：守在道格身侧的小色块。
        var bruce = new Polygon2D
        {
            Polygon = Quad(VillageHouseCenter + new Vector2(24f, -14f), new Vector2(26f, 16f)),
            Color = new Color(0.45f, 0.38f, 0.30f, 0.95f),
            ZIndex = 7,
        };
        AddChild(bruce);

        var tag = new Label
        {
            Text = "？",
            Position = VillageHouseCenter + new Vector2(-8f, -64f),
            ZIndex = 12,
        };
        tag.AddThemeFontSizeOverride("font_size", 15);
        tag.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        AddChild(tag);
    }

    /// <summary>
    /// 每帧：南林村庄的布鲁斯吠叫轮询（[SPEC-B11]"调查团靠近中距离→布鲁斯开始叫"）。
    /// 取存活探索队到锁屋门的最近距离喂 <see cref="VillageRescue.ShouldStartBarking"/>/<see cref="VillageRescue.InBarkRange"/>：
    /// 进中距离即起吠、每 <see cref="VillageRescue.BarkIntervalSeconds"/> 秒飘一声"汪！"（音频无资产的占位提示）；
    /// 离开中距离即停吠；救援已触发（本关内已上报救援 id）即静音（人被救走）。
    /// 吠叫仅引导玩家、不额外驱赶丧尸（围困已由初始布局承载，避免过度惩罚，见 SpawnVillageSiegeZombies）。
    /// </summary>
    public override void _Process(double delta)
    {
        if (!_villageActive)
            return;
        if (_firedDiscoveries.Contains(VillageRescue.RescueDiscoveryId))
        {
            _villageBarking = false; // 已救援：静音
            return;
        }

        float nearest = float.MaxValue;
        foreach (Pawn p in ExpeditionTeam)
            if (p.Alive)
                nearest = Mathf.Min(nearest, p.GlobalPosition.DistanceTo(VillageDoorPosition));

        if (!VillageRescue.InBarkRange(nearest))
        {
            _villageBarking = false; // 离开中距离：停吠（下次再进重新起吠）
            return;
        }

        if (VillageRescue.ShouldStartBarking(nearest, _villageBarking))
        {
            _villageBarking = true;
            _villageBarkCooldown = 0.0; // 进范围立即先叫一声
        }

        _villageBarkCooldown -= delta;
        if (_villageBarkCooldown <= 0.0)
        {
            FloatingText.Spawn(this, VillageDoorPosition + new Vector2(0f, -46f), VillageRescue.BarkText, new Color(0.95f, 0.9f, 0.55f));
            _villageBarkCooldown = VillageRescue.BarkIntervalSeconds;
        }
    }

    /// <summary>造一个发现点：地面标记 + 文字标签 + 触发 Area2D（踏入一次即上报，本关内不重复）。
    /// 标记+标签挂在一个容器 Node2D 下，登记进 <see cref="_discoveryVisuals"/> 供视野检测层隐藏（视野外不揭示）；
    /// 触发 Area2D 独立不受隐藏影响（视野外踏入仍可发现，"看不见但撞上了"）。</summary>
    private void AddDiscoveryPoint(string discoveryId, Vector2 pos, Color markerColor, string label, Vector2? zoneSize = null)
    {
        // 视觉容器（隐藏用）：标记+标签挂其下，隐藏容器即隐藏两者。
        var visual = new Node2D();
        AddChild(visual);

        var mark = new Polygon2D
        {
            Polygon = Quad(new Vector2(-14, -14), new Vector2(28, 28)),
            Color = new Color(markerColor.R, markerColor.G, markerColor.B, 0.6f),
            Position = pos,
            ZIndex = 8,
        };
        visual.AddChild(mark);

        var tag = new Label
        {
            Text = label,
            Position = pos + new Vector2(-16, -40),
            ZIndex = 12,
        };
        tag.AddThemeFontSizeOverride("font_size", 13);
        tag.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        visual.AddChild(tag);

        _discoveryVisuals.Add((visual, pos));

        var zone = new Area2D { Position = pos };
        zone.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = zoneSize ?? new Vector2(70, 70) } });
        zone.CollisionMask = 0b0001u; // 与返回区一致：只感知玩家 Pawn 所在层
        zone.BodyEntered += body =>
        {
            // 踏进去的那个人要透传出去——物资搜刮点是**他**站在那儿一件件往外掏（逐件搜刮，见 LootSession）。
            if (body is Pawn finder && _firedDiscoveries.Add(discoveryId))
                RaiseDiscovery(discoveryId, finder);
        };
        AddChild(zone);
    }

    /// <summary>
    /// 叙事调查点铺设（[SPEC-B12] 极乐迪斯科式）：按目的地迭代 <see cref="NarrativeSpotRegistry"/> 铺触发区，
    /// 与物资搜刮点/主线触发点并存（命名空间隔离，id 前缀 narrative_）。复用 <see cref="AddDiscoveryPoint"/>
    /// （marker + label + Area2D → RaiseDiscovery），叙事点用**冷青**标记区别于物资(棕黄)/剧情尸体(红)。
    /// 触发解析在 <c>CampMain.OnExplorationDiscovery</c> 走 <see cref="NarrativeSpotRegistry.Resolve"/>（分页弹叙事、冻结时标＝不走时间）。
    ///
    /// 触发式：Proximity（靠近即触发）已落地＝walk-in。Click（点击调查物→角色走近后触发）探索关**无拾取先例**——
    /// [HANDOFF] 探索关正式化专项（TODO#8）：届时把 <c>spot.Trigger == NarrativeTrigger.Click</c> 的点改为鼠标拾取 + 寻路到达触发；
    /// 当前统一渲染为 proximity（walk-in 即可达、可测），Trigger 字段已在注册表如实标注，切换时读它即可。
    /// </summary>
    private void SetupNarrativeSpots()
    {
        foreach (NarrativeSpot spot in NarrativeSpotRegistry.ForDestination(DestinationName))
        {
            AddDiscoveryPoint(
                spot.Id,
                new Vector2(spot.X, spot.Y),
                markerColor: new Color(0.40f, 0.62f, 0.72f), // 冷青：叙事调查点（区别于物资棕黄/剧情红）
                label: spot.Label);
        }
    }

    private static Node2D CreateActorMarker(Actor actor, Color color)
    {
        var marker = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new(0, -10), new(10, 0), new(0, 10), new(-10, 0),
            },
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
