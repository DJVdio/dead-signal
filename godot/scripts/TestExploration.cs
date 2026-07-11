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
            SetupGordonHangedDiscovery();
        else if (DestinationName == ExplorationCache.RiversideCabinName)
            SetupRiversideCabinCaches();
        else if (DestinationName == ExplorationCache.HarvesterWarehouseName)
            SetupHarvesterWarehouseCaches();
        else if (DestinationName == WorldMapPanel.CityRooftopLookoutName)
            SetupCityRooftopLookout();
        else if (DestinationName == WorldMapPanel.BroadcastStationName)
            SetupBroadcastStation();

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
        {
            var z = Zombie.Create(wander, LevelTargets); // 目标池含随队布鲁斯（可被关内丧尸攻击/杀）
            z.Inject(Combat, Clock); // 与营地单位相同的 combat+clock，务必在入树/首个物理帧 Think 前完成
            z.ConfigurePerception(localLightAt: SampleLevelLight); // 固定光源→局部光照喂给（暴露走目标 CarriedLightIntensity 回落）
            z.Position = spot;
            _actorLayer.AddChild(z);
            _zombies.Add(z);
            _markers[z] = CreateActorMarker(z, new Color(0.45f, 0.6f, 0.35f));
        }
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

    /// <summary>守望者森林小屋：哥顿上吊尸（门口树上）+ 日记B 发现点。与金手指帮根据地异地，独立一处。</summary>
    private void SetupGordonHangedDiscovery()
    {
        AddDiscoveryPoint(
            GoldfingerDiscovery.GordonHangedId,
            new Vector2(2000, 1220),
            markerColor: new Color(0.55f, 0.5f, 0.45f),
            label: "上吊尸");
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

        AddDiscoveryPoint(
            ExplorationCache.RiversideBedChestId,
            new Vector2(1850, 420),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "床底木箱");
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

        AddDiscoveryPoint(
            ExplorationCache.BroadcastPartsStoreId,
            new Vector2(1980, 360),
            markerColor: new Color(0.5f, 0.48f, 0.44f),
            label: "备件仓库");
    }

    /// <summary>造一个发现点：地面标记 + 文字标签 + 触发 Area2D（踏入一次即上报，本关内不重复）。
    /// 标记+标签挂在一个容器 Node2D 下，登记进 <see cref="_discoveryVisuals"/> 供视野检测层隐藏（视野外不揭示）；
    /// 触发 Area2D 独立不受隐藏影响（视野外踏入仍可发现，"看不见但撞上了"）。</summary>
    private void AddDiscoveryPoint(string discoveryId, Vector2 pos, Color markerColor, string label)
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
        zone.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(70, 70) } });
        zone.CollisionMask = 0b0001u; // 与返回区一致：只感知玩家 Pawn 所在层
        zone.BodyEntered += body =>
        {
            if (body is Pawn && _firedDiscoveries.Add(discoveryId))
                RaiseDiscovery(discoveryId);
        };
        AddChild(zone);
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
