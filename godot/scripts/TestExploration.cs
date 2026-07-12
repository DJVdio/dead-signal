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
        BuildNavigation();
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
        else if (DestinationName == WorldMapPanel.BroadcastStationName)
            SetupBroadcastStation();
        else if (DestinationName == VillageRescue.DestinationName)
            SetupSouthForestVillage();

        // 叙事调查点（极乐迪斯科式，[SPEC-B12]）：按目的地迭代注册表铺 Area2D（与物资/主线点并存，命名空间隔离）。
        // 须在 SetupVisionMask 之前（视觉容器进 _discoveryVisuals，供视野外隐藏）。
        SetupNarrativeSpots();

        // 视野遮暗（批次4）：探索关全程启用。发现点须在此之前铺好（进 _discoveryVisuals）。
        SetupVisionMask();
    }

    /// <summary>
    /// 装配视野遮暗层（探索关全程启用）：以探索队为观察者、环境光按当前相位（探索=白昼满档）算锥形，
    /// 视野外网格遮暗 + 视野外丧尸/发现点隐藏。遮罩覆盖全关、cartesian 直绘（探索关本就 top-down cartesian）。
    /// </summary>
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

    /// <summary>本关存活敌对单位＝存活丧尸（供随队布鲁斯经 CampMain 敌对 provider 自主缠斗）。</summary>
    public override IEnumerable<Actor> LevelHostiles()
    {
        foreach (Zombie z in _zombies)
            if (z.Alive)
                yield return z;
    }

    private void SpawnZombies()
    {
        // 南林村庄：围困锁屋的一圈丧尸（[SPEC-B11]），取代默认散布——见 SpawnVillageSiegeZombies。
        if (DestinationName == VillageRescue.DestinationName)
        {
            SpawnVillageSiegeZombies();
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

    /// <summary>造一只关内丧尸（与营地同 combat/clock、含随队布鲁斯的目标池、局部光照感知）并登记标记。</summary>
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
        AddRoomOutline(new Rect2(980, 520, 480, 380), new Color(0.34f, 0.30f, 0.24f, 0.95f), RoomEdge.Bottom, "守林人小屋");
        AddRoomOutline(new Rect2(1180, 630, 210, 200), new Color(0.28f, 0.25f, 0.21f, 0.95f), RoomEdge.Top, "里屋（暗间）");

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

    /// <summary>房间占位轮廓的门洞所在边。</summary>
    private enum RoomEdge { Top, Bottom, Left, Right }

    /// <summary>画一圈房间占位轮廓（纯视觉、无碰撞、不进导航）：四条细墙边，<paramref name="doorEdge"/> 那条中段留门洞，附房间名标签。</summary>
    private void AddRoomOutline(Rect2 rect, Color color, RoomEdge doorEdge, string label)
    {
        const float t = 8f;    // 墙厚
        const float door = 64f; // 门洞宽
        AddWallStrip(rect.Position, new Vector2(rect.Size.X, t), color, doorEdge == RoomEdge.Top, door);
        AddWallStrip(new Vector2(rect.Position.X, rect.End.Y - t), new Vector2(rect.Size.X, t), color, doorEdge == RoomEdge.Bottom, door);
        AddWallStrip(rect.Position, new Vector2(t, rect.Size.Y), color, doorEdge == RoomEdge.Left, door);
        AddWallStrip(new Vector2(rect.End.X - t, rect.Position.Y), new Vector2(t, rect.Size.Y), color, doorEdge == RoomEdge.Right, door);

        var tag = new Label { Text = label, Position = rect.Position + new Vector2(6, -20), ZIndex = 12 };
        tag.AddThemeFontSizeOverride("font_size", 12);
        tag.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        AddChild(tag);
    }

    /// <summary>一条墙边占位（纯视觉）：横竖由 size 长边判断；<paramref name="withDoor"/> 则中段留门洞、画成两段。</summary>
    private void AddWallStrip(Vector2 pos, Vector2 size, Color color, bool withDoor, float doorWidth)
    {
        if (!withDoor)
        {
            AddChild(new Polygon2D { Polygon = Quad(pos, size), Color = color, ZIndex = 6 });
            return;
        }
        bool horizontal = size.X >= size.Y;
        if (horizontal)
        {
            float seg = (size.X - doorWidth) / 2f;
            AddChild(new Polygon2D { Polygon = Quad(pos, new Vector2(seg, size.Y)), Color = color, ZIndex = 6 });
            AddChild(new Polygon2D { Polygon = Quad(new Vector2(pos.X + seg + doorWidth, pos.Y), new Vector2(seg, size.Y)), Color = color, ZIndex = 6 });
        }
        else
        {
            float seg = (size.Y - doorWidth) / 2f;
            AddChild(new Polygon2D { Polygon = Quad(pos, new Vector2(size.X, seg)), Color = color, ZIndex = 6 });
            AddChild(new Polygon2D { Polygon = Quad(new Vector2(pos.X, pos.Y + seg + doorWidth), new Vector2(size.X, seg)), Color = color, ZIndex = 6 });
        }
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
    /// 画上锁的屋子：四面视觉墙（纯 Polygon2D、无碰撞），南墙留一处门缺口（<see cref="VillageDoorPosition"/> 处）。
    /// 墙不挡路（碰撞+导航重烘焙为遗留）；"上锁/被困"靠门发现区 + 屋内占位 + 围困丧尸表达。
    /// </summary>
    private void DrawLockedHouse()
    {
        Vector2 c = VillageHouseCenter;
        float hw = VillageHouseHalfW, hh = VillageHouseHalfH, t = 16f;
        var wallColor = new Color(0.34f, 0.30f, 0.25f, 0.95f);

        // 北墙（整条）
        AddWallVisual(new Rect2(c.X - hw, c.Y - hh - t, hw * 2f + t, t), wallColor);
        // 西墙、东墙（整条）
        AddWallVisual(new Rect2(c.X - hw - t, c.Y - hh, t, hh * 2f), wallColor);
        AddWallVisual(new Rect2(c.X + hw, c.Y - hh, t, hh * 2f + t), wallColor);
        // 南墙：中间留 90px 门缺口，拆两段
        const float doorHalf = 45f;
        float southY = c.Y + hh;
        AddWallVisual(new Rect2(c.X - hw, southY, (c.X - doorHalf) - (c.X - hw), t), wallColor); // 门左段
        AddWallVisual(new Rect2(c.X + doorHalf, southY, (c.X + hw) - (c.X + doorHalf), t), wallColor); // 门右段

        // 地板底色（示意室内），ZIndex 低于占位标记。
        var floor = new Polygon2D
        {
            Polygon = Quad(new Vector2(c.X - hw, c.Y - hh), new Vector2(hw * 2f, hh * 2f)),
            Color = new Color(0.24f, 0.22f, 0.19f, 0.8f),
            ZIndex = 3,
        };
        AddChild(floor);
    }

    /// <summary>纯视觉墙段（无碰撞 StaticBody，区别于 <see cref="AddWall"/>）：占位屋墙用。</summary>
    private void AddWallVisual(Rect2 rect, Color color)
    {
        var vis = new Polygon2D
        {
            Polygon = Quad(rect.Position, rect.Size),
            Color = color,
            ZIndex = 4,
        };
        AddChild(vis);
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
            if (body is Pawn && _firedDiscoveries.Add(discoveryId))
                RaiseDiscovery(discoveryId);
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
