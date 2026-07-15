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

    /// <summary>导航区（<see cref="BuildNavigation"/> 建；开关门后 <see cref="RebakeNavigation"/> 换掉它的 polygon）。</summary>
    private NavigationRegion2D _navRegion = null!;

    /// <summary>
    /// 关内**可关的门**（当前只有废弃医院有：防火门/安全门/卷帘门）。
    /// 门的"挡 / 不挡"和营地是<b>同一个开关</b>：切墙层 <see cref="VisionOcclusion.WallMask"/> ⇒
    /// 挡人（碰撞）+ 挡视线（射线打的就是这一层）+ 断寻路（进 <see cref="_obstructions"/> 后重烘焙）三件事一起生效。
    /// </summary>
    private readonly List<LevelDoorInstance> _levelDoors = new();

    /// <summary>一扇关内门的运行时实例（门板矩形 + 状态 + 碰撞体/视觉）。</summary>
    private sealed class LevelDoorInstance
    {
        public string Name = "";
        public Rect2 Rect;
        public DoorState State;
        public StaticBody2D Body = null!;
        public Polygon2D Panel = null!;
    }

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
        else if (DestinationName == StuartManor.DestinationName)
            SetupStuartManor();
        else if (DestinationName == ExplorationCache.FireStationName)
            SetupFireStation();
        else if (DestinationName == RuinedChurch.DestinationName)
            SetupRuinedChurch();
        else if (DestinationName == RefugeeCamp.DestinationName)
            SetupRefugeeCamp();
        else if (DestinationName == ExplorationCache.SewerName)
            SetupSewer();

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

    /// <summary>
    /// 本关是否<b>室内恒暗</b>（压过昼夜相位，环境光锁死在 <see cref="VisionLogic.IndoorsDarkAmbient"/>＝0.10）。
    /// 判据在纯逻辑 <see cref="ExplorationLighting.IsIndoorsDark"/>（单一事实源，单测与运行时读同一个）。
    /// <para>
    /// 🔴 [T60] <b>此前这三处环境光调用一律硬编码 <c>indoorsDark: false</c></b> —— 也就是说
    /// <c>VisionLogic.IndoorsDarkAmbient</c> 这条常量写好了却<b>从来没有任何一关用过</b>。
    /// 难民营地（用户原话「光线昏暗，视野受限」）是它第一次真的接上线。
    /// </para>
    /// </summary>
    private bool LevelIndoorsDark => ExplorationLighting.IsIndoorsDark(DestinationName);

    /// <summary>
    /// 探索关预置固定光源（油灯示例，拟定待调）：入口附近 + 关内中段各一盏。
    /// <para>
    /// 🔴 <b>室内恒暗的关卡一盏都不预置</b>：难民营地的「昏暗」是这一关的<b>主轴</b>——
    /// 摆两盏常亮的油灯进去，等于把用户要的东西直接删掉。那里的光只有一个来源：<b>你自己带的</b>
    /// （<see cref="HeldLightState"/>：占一只手 ⇒ 双手武器与光源互斥 ⇒ <b>看得见还是打得动，二选一</b>）。
    /// </para>
    /// </summary>
    private void SetupLevelLights()
    {
        _levelLights.Clear();
        if (LevelIndoorsDark)
            return;
        _levelLights.AddFixed(LightSource.LampKey, LevelW / 2f, LevelH - 200f); // 入口/返回区附近
        _levelLights.AddFixed(LightSource.LampKey, LevelW * 0.5f, LevelH * 0.4f); // 关内中段
    }

    /// <summary>关内某点合成局部光照 L∈[0,1]（环境光与固定光源取 max），供丧尸感知 ConeFor 消费。</summary>
    private float SampleLevelLight(Vector2 pos)
        => VisionLogic.CombineLight(
            VisionLogic.AmbientLight(Clock.CurrentPhase, LevelIndoorsDark),
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
        _visionMask.SetAmbientProvider(() => VisionLogic.AmbientLight(Clock.CurrentPhase, LevelIndoorsDark));
        // 光源场：玩家侧遮暗按局部光照（灯旁视野更远），VisionMask 内部 CombineLight(ambient, 源贡献)。
        _visionMask.SetSourceProvider(pos => _levelLights.StrongestAt(pos.X, pos.Y));
        _visionMask.SetRevealablesProvider(Revealables);
        // 羁绊视野系数（道格锥角/布鲁斯视距·锥角按等级缩放）：CampMain 注入 BondScaleCone，与营地侧同口径，
        // 使道格带布鲁斯出探索的视野技能端到端生效。未注入（无羁绊上下文）则不缩放。
        if (ViewerConeAdjuster != null)
            _visionMask.SetViewerConeAdjuster(ViewerConeAdjuster);
        AddChild(_visionMask);
    }

    /// <summary>
    /// 视野检测层的可揭示物：存活丧尸 + <b>存活的敌对幸存者</b>（隐 Actor 节点即隐其地面标记）+ 发现点视觉容器。
    /// <para>
    /// 🔴 Raider 此前<b>不在这个列表里</b>（既有洞，超市那 4 个据点幸存者一直中招）⇒ 他们不受视野遮罩管辖、
    /// <b>隔着墙也看得见</b>。金手指帮的 8 个守备现在也是 Raider（还站岗），漏掉他们等于把"绕开哨兵视野锥摸进去"
    /// 直接废掉——玩家一进关就把全据点的红点数清楚了，还潜入什么。
    /// </para>
    /// </summary>
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

        foreach (Raider r in _levelRaiders)
        {
            if (!IsInstanceValid(r) || !r.Alive)
                continue;
            Raider captured = r;
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

        // 敌对幸存者同样要拆干净：不 Clear 的话，下一趟探索的 LevelHostiles 会遍历到上一关已释放的节点。
        // （既有洞：超市那 4 个 Raider 一直没被清；金手指帮 8 个守备也走这条列表 ⇒ 一并收口。）
        foreach (var r in _levelRaiders)
        {
            if (IsInstanceValid(r))
                r.QueueFree();
        }
        _levelRaiders.Clear();

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
        _navRegion = new NavigationRegion2D { NavigationPolygon = BakeNavPoly() };
        AddChild(_navRegion);
    }

    /// <summary>照当前的 <see cref="_obstructions"/> 烘一张导航网格（墙 + 关着的门都是障碍）。</summary>
    private NavigationPolygon BakeNavPoly()
    {
        var navPoly = new NavigationPolygon { AgentRadius = ExplorationWalls.NavAgentRadius };
        var src = new NavigationMeshSourceGeometryData2D();

        float inset = 22f;
        Rect2 outer = new(inset, inset, LevelW - inset * 2, LevelH - inset * 2);
        src.AddTraversableOutline(Quad(outer.Position, outer.Size));

        foreach (Rect2 obs in _obstructions)
            src.AddObstructionOutline(Quad(obs.Position, obs.Size));

        NavigationServer2D.BakeFromSourceGeometryData(navPoly, src);
        return navPoly;
    }

    /// <summary>
    /// 开关门后重烘焙导航（门是障碍的增删）。
    /// <para>
    /// ⚠️ <b>nav region 同步滞后（本仓已知隐坑）</b>：换掉 polygon 之后，NavigationServer 要到**下一次服务器同步**
    /// 才让新网格生效。营地那边为此专门留了破防 AI 的宽限期（<c>CampMain.DoorNavSyncGraceMs</c>）——
    /// <b>探索关不需要</b>：关内没有砸门的 AI（<c>BreachController</c> 是营地专属），
    /// 唯一会踩这一帧的场景（"AI 刚开了门却被告知走不通，于是转身砸自己刚开的门"）在这里根本不存在。
    /// 玩家侧从"推开门"到"下一条移动指令"至少隔着一次输入事件，那时早已同步完毕。
    /// </para>
    /// </summary>
    private void RebakeNavigation()
    {
        if (_navRegion is null || !IsInstanceValid(_navRegion))
            return;
        _navRegion.NavigationPolygon = BakeNavPoly();
    }

    // ---- 关内可关的门（当前只有废弃医院；玩家交互入口在 CampMain 的容器体系，见 LevelDoorTargets/ToggleLevelDoor）----

    /// <summary>
    /// 建一扇关内的门：门板 = <see cref="StaticBody2D"/>（墙层）+ 可见的门板多边形 + 导航洞。
    /// 三样随开关一起增删（见 <see cref="SetLevelDoorBlocking"/>）—— 与营地门同一口径：**一处开关，三件事同时生效**。
    /// </summary>
    private void AddLevelDoor(ExplorationDoor spec, Color color)
    {
        var rect = new Rect2(spec.Rect.X, spec.Rect.Y, spec.Rect.Width, spec.Rect.Height);

        var body = new StaticBody2D { Position = rect.Position + rect.Size / 2 };
        body.CollisionMask = 0u;
        body.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = rect.Size } });

        var panel = new Polygon2D
        {
            Polygon = Quad(-rect.Size / 2, rect.Size),
            Color = color,
            ZIndex = -4, // 压在墙(-5)之上：关着的门看得出是"门"，不是墙
        };
        body.AddChild(panel);
        AddChild(body);

        var inst = new LevelDoorInstance
        {
            Name = spec.Name,
            Rect = rect,
            State = spec.Initial,
            Body = body,
            Panel = panel,
        };
        _levelDoors.Add(inst);

        // 建图期：直接按初始态设好挡/不挡，**不重烘焙**（BuildNavigation 还没跑，等它一次性烘）。
        ApplyLevelDoorBlocking(inst, DoorLogic.Blocks(inst.State));
    }

    /// <summary>门的「挡 / 不挡」落地：碰撞层（=挡人+挡视线）、门板视觉、导航洞，三样一起切。<b>不</b>重烘焙。</summary>
    private void ApplyLevelDoorBlocking(LevelDoorInstance door, bool blocking)
    {
        if (IsInstanceValid(door.Body))
            door.Body.CollisionLayer = blocking ? VisionOcclusion.WallMask : 0u;
        if (IsInstanceValid(door.Panel))
            door.Panel.Visible = blocking; // 开着＝你看到的是一个洞

        if (blocking)
        {
            if (!_obstructions.Contains(door.Rect))
                _obstructions.Add(door.Rect);
        }
        else
        {
            _obstructions.Remove(door.Rect); // Rect2 值相等
        }
    }

    /// <summary>切一扇门的挡/不挡并重烘焙导航（运行时开关门走这条）。</summary>
    private void SetLevelDoorBlocking(LevelDoorInstance door, bool blocking)
    {
        ApplyLevelDoorBlocking(door, blocking);
        RebakeNavigation();
    }

    /// <summary>关内可交互的门（名字 + 门板矩形）。CampMain 据此把它们登记进容器体系（右键前往 → 到达开关门）。</summary>
    public IReadOnlyList<(string Name, Rect2 Rect)> LevelDoorTargets()
    {
        var list = new List<(string, Rect2)>(_levelDoors.Count);
        foreach (LevelDoorInstance d in _levelDoors)
            list.Add((d.Name, d.Rect));
        return list;
    }

    /// <summary>某扇关内门当前的状态（不存在则 null）。</summary>
    public DoorState? LevelDoorState(string name)
    {
        foreach (LevelDoorInstance d in _levelDoors)
        {
            if (d.Name == name)
                return d.State;
        }
        return null;
    }

    /// <summary>
    /// <b>开 / 关</b>一扇关内的门（<b>开门只有一种动作</b>，与营地同口径：开着就关上，关着就推开）。
    /// 关内的门<b>都没上锁</b>（不需要铁丝，玩家绝不会被一扇门永久卡死）。
    /// <para>
    /// 返回是否真的动了门；<paramref name="message"/> 给调用方出提示。关门时若门缝里站着东西则关不上
    /// （否则会把它实心夹进门板里）。
    /// </para>
    /// </summary>
    public bool ToggleLevelDoor(string name, out string message)
    {
        LevelDoorInstance? door = null;
        foreach (LevelDoorInstance d in _levelDoors)
        {
            if (d.Name == name) { door = d; break; }
        }
        if (door is null)
        {
            message = "";
            return false;
        }

        bool wasBlocking = DoorLogic.Blocks(door.State);
        if (wasBlocking)
        {
            door.State = DoorState.Open;
            SetLevelDoorBlocking(door, false);
            message = $"推开了{door.Name}。";
            return true;
        }

        if (LevelDoorwayOccupied(door))
        {
            message = $"{door.Name}关不上——门口还站着东西。";
            return false;
        }

        door.State = DoorState.Closed;
        SetLevelDoorBlocking(door, true);
        message = $"关上了{door.Name}。——门后的东西暂时过不来了。";
        return true;
    }

    /// <summary>门缝里站着人/狗/丧尸吗（关门前必查，否则会把它实心夹进门板里）。</summary>
    private bool LevelDoorwayOccupied(LevelDoorInstance door)
    {
        Rect2 gap = door.Rect.Grow(10f);

        foreach (Zombie z in _zombies)
        {
            if (IsInstanceValid(z) && z.Alive && gap.HasPoint(z.Position))
                return true;
        }
        foreach (Raider r in _levelRaiders)
        {
            if (IsInstanceValid(r) && r.Alive && gap.HasPoint(r.Position))
                return true;
        }
        foreach (Pawn p in ExpeditionTeam)
        {
            if (IsInstanceValid(p) && p.Alive && gap.HasPoint(p.Position))
                return true;
        }
        if (CompanionDog is { } dog && IsInstanceValid(dog) && dog.Alive && gap.HasPoint(dog.Position))
            return true;

        return false;
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
        // [T61] 下水道：返回区＝那口检修竖井的井底（唯一的出口）。默认那个"地图正下方"的位置在下水道里是**实心墙**。
        Vector2 pos = DestinationName == ExplorationCache.SewerName
            ? new Vector2(ExplorationWalls.SewerEntry.X - 40f, ExplorationWalls.SewerEntry.Y - 40f)
            : new Vector2(LevelW / 2 - 40, LevelH - 120);

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
        // [T61] 下水道的入口是一口**竖井**（通道宽 140）——默认那个横排落点会把人放进墙里。
        // 故这一关**沿竖井纵向排队**下来（x 不动，y 逐个上移），与 ExplorationWalls.SewerEntry 同源。
        bool sewer = DestinationName == ExplorationCache.SewerName;
        Vector2 spawn = sewer
            ? new Vector2(ExplorationWalls.SewerEntry.X - 10f, ExplorationWalls.SewerEntry.Y - 20f)
            : new Vector2(200, LevelH - 200);
        float stepX = sewer ? 0f : 40f;
        float stepY = sewer ? -34f : 0f;
        for (int i = 0; i < ExpeditionTeam.Count; i++)
        {
            Pawn p = ExpeditionTeam[i];
            p.Position = spawn + new Vector2(i * stepX, i * stepY);
            p.Reparent(_actorLayer, keepGlobalTransform: false);
            _markers[p] = CreateActorMarker(p, p.BodyTint);
        }

        // 随队布鲁斯（若带上）：放在队伍旁、reparent 进关卡 actor 层。跟随道格/自主缠斗（关内敌对经 CampMain
        // 的敌对 provider 读 LevelHostiles）由其既有 AI 驱动；战斗引擎已在营地 Inject 过（跨关卡复用同一实例）。
        if (CompanionDog is { } dog)
        {
            dog.Position = spawn + new Vector2(ExpeditionTeam.Count * stepX, ExpeditionTeam.Count * stepY);
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

    /// <summary>
    /// 本关存活敌对单位＝存活丧尸 + 关内敌对幸存者（超市骗局伏击的据点幸存者 / <b>金手指帮的 8 名守备</b>）——
    /// 供随队布鲁斯经 CampMain 敌对 provider 自主缠斗、视野揭示。
    /// </summary>
    public override IEnumerable<Actor> LevelHostiles()
    {
        foreach (Zombie z in _zombies)
            if (IsInstanceValid(z) && z.Alive)
                yield return z;
        foreach (Raider r in _levelRaiders)
            if (IsInstanceValid(r) && r.Alive)
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

        // [T49] 废弃医院：全图最高丧尸密度（14 只，band 12~16），向住院部/药房/手术层深区扎堆——"大量丧尸"是其身份。
        // 🔴 但这 14 只**不是让你清完的**（连场战斗的代价见 docs/research/2026-07-14-combat-cost.md）：
        // 分区隔墙断了视线、每道边界有多个门洞、防火门可以关上 ⇒ 正解是**绕、关门、别开枪**。布点/数值拟定待调。
        if (DestinationName == ExplorationCache.HospitalName)
        {
            var hospitalSpots = new List<Vector2>(ExplorationWalls.HospitalZombieSpots.Count);
            foreach ((float x, float y) in ExplorationWalls.HospitalZombieSpots)
                hospitalSpots.Add(new Vector2(x, y));
            SpawnZombiesAt(hospitalSpots);
            return;
        }

        // [T61] 下水道：**3 只，各蹲一个拐角**（用户原话「除了某几个拐角可能有**一只**丧尸，**基本没有危险**」）。
        // 🔴 布点被硬不变量钉死（SewerTests：**任何位置最多被 1 只感知**）——依据 sim-lanchester：
        //    2 只围攻胜率 16.6%、3 只 0.8% ⇒ **围攻是断崖**。这地方要的是"吓人"，不是"危险"，两者必须焊开。
        //    **要挪/要加，先跑 SewerTests。**
        if (DestinationName == ExplorationCache.SewerName)
        {
            var sewerSpots = new List<Vector2>(ExplorationWalls.SewerZombieSpots.Count);
            foreach ((float x, float y) in ExplorationWalls.SewerZombieSpots)
                sewerSpots.Add(new Vector2(x, y));
            SpawnZombiesAt(sewerSpots);
            return;
        }

        // [SPEC-T51] 斯图尔特家族庄园：**盘踞的是劫掠者，不是丧尸**——庄园里一只游荡丧尸都不铺。
        // 唯一的丧尸在**门口**：用户原话「男性尸体吊挂在门口**喂丧尸**」⇒ 吊尸把丧尸引在了大门那儿，成了一道活的护城河。
        if (DestinationName == StuartManor.DestinationName)
        {
            SpawnStuartManorRaiders();
            SpawnStuartGateZombies();
            return;
        }

        // [批次25·T50] 消防站：用户口径「**低危**」⇒ **全图最少的丧尸（3 只，band 2~4）**，且全部铺在深处
        //   （器材间/值班室/后院），**入口的车库一只都没有** —— 玩家一进门就能安安稳稳把消防斧摘下来。
        //   这是三个新点里最容易的那个，也是开局最该去的那个。数量/布点拟定待调（归 param-calibration）。
        if (DestinationName == ExplorationCache.FireStationName)
        {
            SpawnZombiesAt(FireStationZombieSpots);
            return;
        }

        // [SPEC-T60] 破败教堂：教堂本体 3 只（稀——进关不会当场被淹）+ **后院墓地 12 只**（「大量」）。
        // 墓地那 12 只在关着的门后：它们看不见你，你也看不见它们。推开门的那一刻，一片同时进你的视野。
        // 🔴 **不是让你清完的**（2 只围攻＝16.6%，3 只＝0.8%，4 只起＝0%）——**是让你转身跑、并把门关上。**
        if (DestinationName == RuinedChurch.DestinationName)
        {
            foreach ((float x, float y) in RuinedChurch.ChurchZombieSpots)
                SpawnZombieAt(new Vector2(x, y), LevelWanderRect());
            foreach ((float x, float y) in RuinedChurch.GraveyardZombieSpots)
                SpawnZombieAt(new Vector2(x, y), LevelWanderRect());
            return;
        }

        // [SPEC-T60] 难民营地：10 只**开门跳脸**（各锁在一间房里，就贴在门后 ≤90px）+ 4 只过道游荡。
        // 跳脸的那 10 只徘徊区**只有它自己那间房**：它必须待在门后——它就是那扇门的全部意义。
        if (DestinationName == RefugeeCamp.DestinationName)
        {
            foreach (AmbushZombie a in RefugeeCamp.AmbushZombies)
            {
                WallRect room = RefugeeCamp.Room(a.RoomNumber).Rect;
                const float pad = RefugeeCamp.RoomWallThickness + 20f;
                var box = new Rect2(
                    room.X + pad, room.Y + pad,
                    Mathf.Max(1f, room.Width - pad * 2f), Mathf.Max(1f, room.Height - pad * 2f));
                SpawnZombieAt(new Vector2(a.X, a.Y), box);
            }
            foreach ((float x, float y) in RefugeeCamp.CorridorZombieSpots)
                SpawnZombieAt(new Vector2(x, y), LevelWanderRect());
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
        Rect2 wander = LevelWanderRect();
        foreach (Vector2 spot in spots)
            SpawnZombieAt(spot, wander);
    }

    /// <summary>
    /// 覆盖全关的徘徊区。<b>注意它只是"想去哪"，不是"去得了哪"</b>——真正的通行由导航网格说了算，
    /// 而关着的门是导航障碍 ⇒ 被门关住的丧尸（教堂后院那 12 只、难民营地各房里那 10 只）**出不来**。
    /// "关门隔开丧尸"因此是真的，不是靠给它们画一个小圈。
    /// </summary>
    private static Rect2 LevelWanderRect()
        => new(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);

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

    /// <summary>
    /// [批次25·T50] 消防站游荡丧尸布点（<b>低危·全图最少：3 只，band 2~4</b>）。用户原话「低危」。
    /// <para>
    /// 参照：加油站 5（中低）/ 东部新村 7（中）/ 医院 14（丧尸巢）⇒ 消防站 3 是**全图最低**，名副其实。
    /// 三只**全在深处**（器材间外 / 值班室 / 后院训练塔），队伍出生点（200,1400）所在的**车库入口区一只都没有**——
    /// 玩家进门就能把器材墙上那把消防斧摘下来，不必先打一架。这正是"开局友好"的意思。
    /// </para>
    /// 数量/布点拟定待调（归 param-calibration）。
    /// </summary>
    private static readonly Vector2[] FireStationZombieSpots =
    {
        new(1120f, 940f),  // 器材间门外（挡在急救柜那条路上）
        new(1700f, 1130f), // 值班室附近
        new(1880f, 640f),  // 后院·训练塔下（最深）
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

    /// <summary>
    /// 金手指帮守备布点：读 <see cref="GoldfingerGang.Posts"/>（相对坐标）投到本关尺寸。
    /// <b>布点的真源在那张表，不在这里</b>——因为"开一枪会招来几个人"是纯几何、Sim 要算它
    /// （<see cref="GoldfingerGang.AlertedBy"/>），而 Sim 够不着 Godot 场景层。两处各写一份就会漂移。
    /// </summary>
    private Vector2[] GoldfingerGuardSpots() => GoldfingerGang.Posts
        .Select(p => new Vector2((float)(LevelW * p.X), (float)(LevelH * p.Y)))
        .ToArray();

    /// <summary>据点入口方位（关内世界坐标，南侧）：哨兵的扫视中心朝这儿——他们防的是从这个方向进来的人。</summary>
    private Vector2 GoldfingerEntrance => new(LevelW * 0.5f, LevelH * 0.95f);

    /// <summary>
    /// 金手指帮根据地守备布防。
    ///
    /// <para>🔴 <b>他们是人，不是丧尸</b>（用户澄清：「金手指帮是人，不是丧尸，不过他们刚经历完异常战斗，
    /// 大家的状态都不是巅峰」）。此前这 8 个"守备"生成的是 <c>Zombie</c> ⇒ 丧尸不持械 ⇒
    /// <b>打赢金手指帮一把武器都捡不到</b>，而这本该是玩家最重要的装备通道。</para>
    ///
    /// <para>改成 <see cref="Raider"/> 后三样东西<b>全部白拿、零新规则</b>：
    /// ① <b>持械</b> ⇒ 杀了能扒（<c>CorpseLoot.Strip</c> 必掉零掷骰，走 <c>SpawnLevelCorpse</c> 落尸通道）；
    /// ② <b>会站岗</b>（<c>Raider.ConfigureSentry</c> 早已实现、此前<b>全项目零调用点</b>——设计文档 §5 的
    /// 敌营岗哨/三角波扫视、TODO 里记的"岗哨没有调用点"，就是卡在"守备是丧尸，而丧尸不会站岗"上）；
    /// ③ <b>会开门/会砸墙/听得见动静</b>（Raider 本就有这些，丧尸只会砸）。</para>
    ///
    /// <para><b>编制、持械、伤情全在 <see cref="GoldfingerGang.Roster"/>（authored 表）</b>，本方法只管把它摆到地图上——
    /// 数值改动去改那张表，别改这里。</para>
    /// </summary>
    private void SpawnGoldfingerGuards()
    {
        Vector2[] spots = GoldfingerGuardSpots();
        var wander = new Rect2(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);
        IReadOnlyList<GangGuard> roster = GoldfingerGang.Roster;

        for (int i = 0; i < roster.Count && i < spots.Length; i++)
        {
            GangGuard guard = roster[i];
            Vector2 pos = spots[i];

            var r = Raider.Create(
                wander, LevelTargets,
                displayName: guard.DisplayName,
                weapon: GoldfingerGang.WeaponFor(guard.Arm));
            r.Inject(Combat, Clock);
            r.ConfigurePerception(localLightAt: SampleLevelLight);
            r.ApplyInjury(guard.Injury); // 「刚经历完异常战斗」＝预置部位伤 + 骨折（不登记出血，否则他们会自己流血死）
            r.Position = pos;

            if (guard.IsSentry)
            {
                // 钉在岗位上、绕"朝向入口"的中心有规律地左右扫视 ⇒ 玩家可以蹲着数拍子、算准背对的那几秒摸过去。
                // 深处两个走懈怠档（扫得慢、端点发呆久＝好绕）——伤兵守内院；近入口那个走警觉档（难绕）。
                float facing = (GoldfingerEntrance - pos).Angle();
                bool deep = pos.Y < LevelH * 0.5f;
                r.ConfigureSentry(pos, facing, deep ? SentrySweep.Slack : SentrySweep.Alert);
            }

            _actorLayer.AddChild(r);
            _levelRaiders.Add(r);
            _markers[r] = CreateActorMarker(r, new Color(0.72f, 0.26f, 0.22f)); // 暗红：敌对幸存者（与丧尸绿/己方一眼区分）
        }
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

        // [T67] 两处**采集点**（不是搜刮箱——弯腰在地上薅蘑菇，走 ForageLogic）：林下腐叶 + 柴堆背阴。
        //   读过《野外生存指南》采得更多（×1.5）。绿色标记区别于搜刮点的暖色。
        var forageC = new Color(0.42f, 0.62f, 0.36f);
        AddDiscoveryPoint(ForageLogic.RangersCabinMushroomId, new Vector2(1520, 840), markerColor: forageC, label: "林下蘑菇");
        AddDiscoveryPoint(ForageLogic.RangersCabinWoodpileMushroomId, new Vector2(1660, 1000), markerColor: forageC, label: "柴堆背阴");
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
    /// · 枪柜（← 自制猎枪 + 弹药/箭；原栓动猎枪随该武器删除而撤下，用户拍板改掉自制猎枪填缺口）铺在靠近入口处（近入口＝易得）；· 床底木箱（通用搜刮）位置更深。
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
    /// [T49] <b>废弃医院</b>（用户原话：「有医疗物资和大量丧尸，大地图，中危」）。
    /// 全游戏**手术与治疗的补给来源**：医疗物资集中投放于药房/手术层（打破全域"禁医疗灌水"的例外点，正是医院身份）。
    /// 分区近→深：门诊/急诊大厅(南) → 住院部 → 药房(医疗集中) → 手术层(手术耗材+高价值医疗)。
    ///
    /// <para>
    /// 🔴 <b>「大量丧尸」+「中危」这两句，只有在能绕过去时才同时成立。</b>
    /// 改造前这里是**一片开阔地**（一堵墙都没有）：14 只丧尸共享同一片视野，进门就被全楼看见，
    /// 既躲不掉也隔不开——那不是中危，是死亡陷阱（连场战斗的代价见 <c>docs/research/2026-07-14-combat-cost.md</c>：
    /// 单场 68% 胜率、不治疗连打，能撑过第 3 个的只剩 3.5%）。
    /// </para>
    ///
    /// <para>
    /// 所以医院现在是一栋**建筑**（几何全部取自纯逻辑 <see cref="ExplorationWalls.HospitalWalls"/>，已上单测）：
    /// <list type="bullet">
    /// <item><b>三个入口</b>：正门 / 急诊入口 / 员工侧门（西面，**跳过大厅直插住院部**的捷径——更短，也更没退路）。</item>
    /// <item><b>每道分区边界多个门洞</b>：一条走廊挤满丧尸时，还有第二条。</item>
    /// <item><b>可关的门</b>（防火门/安全门/卷帘门，初始<b>关着</b>）：关上＝把追你的丧尸挡在门后，
    ///       它得绕到这道边界的另一个门洞去。**每道边界都留了一个关不上的洞**，故你永远关不死自己（单测钉死）。</item>
    /// <item>隔墙同时<b>挡视线</b>（<see cref="VisionOcclusion"/> 打的就是这批矩形）⇒ 丧尸不再一次性全员发现你。</item>
    /// </list>
    /// <b>噪音是这一关的主轴</b>：推门 100、走路 40，而开一枪是 350（手枪）~600（步枪）——足以横穿两三个分区，
    /// 等于**把整层楼叫醒**。医院的正解是近战/弓（70）、关门、绕路，而不是站着清 14 只。
    /// </para>
    /// </summary>
    private void SetupHospital()
    {
        // 分区占位地台（纯视觉）：门诊/急诊(南/近)、住院部(中)、药房(深)、手术层(最深)。越深越"洁净"色调、也越危险。
        AddZonePad(new Vector2(320, 1080), new Vector2(1760, 380), new Color(0.24f, 0.24f, 0.26f, 0.55f)); // 门诊/急诊大厅
        AddZonePad(new Vector2(300, 620), new Vector2(1820, 420), new Color(0.22f, 0.25f, 0.28f, 0.55f));  // 住院部
        AddZonePad(new Vector2(400, 380), new Vector2(1780, 220), new Color(0.20f, 0.28f, 0.24f, 0.58f));  // 药房（医疗集中，偏药绿）
        AddZonePad(new Vector2(300, 120), new Vector2(1900, 240), new Color(0.28f, 0.30f, 0.32f, 0.60f));  // 手术层（无菌灰白，最深）

        // ——楼层平面：外墙（三个入口处断开）+ 三道分区隔墙（各留多个门洞）——
        // 同一批矩形三用：碰撞（挡人）/ 导航 obstruction（阻断寻路）/ 墙层射线（挡视线）。
        var wallC = new Color(0.30f, 0.31f, 0.33f, 0.95f);
        foreach (WallRect w in ExplorationWalls.HospitalWalls())
            AddSolidWall(w, wallC, zIndex: -5);

        // ——可关的门（初始关着：医院的防火门本就是关着的，深区的丧尸因此不会在你进门那一刻全员涌来）——
        var doorC = new Color(0.55f, 0.42f, 0.28f, 0.95f); // 门板：木色，和灰墙区分得开
        foreach (ExplorationDoor d in ExplorationWalls.HospitalDoors())
            AddLevelDoor(d, doorC);

        // ——30 处搜刮点（坐标/文案/分区皆取自纯逻辑，与墙体同源，故不会有点被砌进墙里——单测钉死）——
        foreach (HospitalCacheSpot s in ExplorationWalls.HospitalCacheSpots)
        {
            AddDiscoveryPoint(
                s.Id,
                new Vector2(s.X, s.Y),
                markerColor: HospitalZoneColor(s.Zone),
                label: s.Label);
        }
    }

    /// <summary>医院分区的标记色：越深越"洁净"（药房药绿 / 手术层无菌灰白），呼应"越深越危险、也越值钱"。</summary>
    private static Color HospitalZoneColor(HospitalZone zone) => zone switch
    {
        HospitalZone.Lobby => new Color(0.5f, 0.5f, 0.5f),           // 门诊/急诊（非医疗为主）
        HospitalZone.Ward => new Color(0.5f, 0.54f, 0.56f),          // 住院部
        HospitalZone.Pharmacy => new Color(0.42f, 0.62f, 0.44f),     // 药房（医疗，药绿）
        _ => new Color(0.62f, 0.66f, 0.62f),                          // 手术层（无菌灰白·高价值）
    };

    // ================= [T61] 下水道 =================
    //
    // 🔴 用户原话：「规模小，下水道，**除了某几个拐角可能有一只丧尸，基本没有危险**，
    //    主要靠**黑暗逼仄的环境**和**大量拐角的差视野**，配合滴滴答答的水滴声和脚步声和回声吓人。」
    //
    // ⇒ **这一关的恐怖全部来自"你看不见"，没有一点来自"你打不过"。**
    //    几何（蛇形通道 / 8 个直角弯 / 一条死胡同 / 3 只分散的丧尸）全在纯逻辑 <see cref="ExplorationWalls"/>
    //    的 Sewer* 段里，**并且上了单测**（SewerTests：任何位置最多被 1 只丧尸感知 / 墙不漏也不堵 / 入口看不见最深处）。
    //    这里只做空间执行：把矩形实体化、把点铺出来。**别在这里另写一份几何。**
    //
    // 📌 **黑暗**：本关经 <see cref="ExplorationLighting.IsIndoorsDark"/> 标为室内恒暗（0.10）⇒ 视锥 ~124px / 半角 30°
    //    ⇒ 玩家基本上**必须手持光源**（占一只手 ⇒ 与双手武器互斥）。**不铺任何固定光源** —— 那正是这一关的身份。
    // 📌 **音效**：用户要的"滴滴答答的水滴声/脚步声/回声"**没有承载它的系统**（本项目至今无音效系统）
    //    ⇒ **不在此伪造**，已作为重大缺口上报。当前只能靠搜刮点的环境叙事文字兑现（见 ExplorationCache 的 Sewer* 叙事）。

    /// <summary>
    /// [T61] 下水道：一条**蛇形的窄管**——入口竖井 → 横廊 → 弯 → 竖廊 → 弯（岔出一条死胡同）→ … → 最深处汇流室（**耗子**）。
    /// </summary>
    private void SetupSewer()
    {
        // 地台：污水的墨绿灰（比任何一关都暗——它本来就该是全图最暗的地方）。
        AddZonePad(new Vector2(320, 140), new Vector2(1360, 1300), new Color(0.13f, 0.16f, 0.15f, 0.60f));

        // ——墙：由通道的**补集**自动推出（ExplorationWalls.SewerWalls）⇒ 通道与墙不可能对不上。
        //   同一批矩形三用：碰撞（挡人）/ 导航 obstruction（阻断寻路）/ 墙层射线（**挡视线** —— 拐角就是靠它成立的）。
        var wallC = new Color(0.17f, 0.19f, 0.18f, 0.98f);
        foreach (WallRect w in ExplorationWalls.SewerWalls())
            AddSolidWall(w, wallC, zIndex: -5);

        // ——5 处搜刮点（**很少量**物资：蘑菇、老鼠、几样基础材料。这地方的价值是耗子，不是战利品）——
        var lootC = new Color(0.42f, 0.46f, 0.38f);   // 苔绿
        foreach (SewerCacheSpot spot in ExplorationWalls.SewerCacheSpots)
            AddDiscoveryPoint(spot.Id, new Vector2(spot.X, spot.Y), markerColor: lootC, label: spot.Label);

        // ——最深处：**耗子**（可招募幸存者）。非物资点 ⇒ 不计探索完成度（同护士相遇点口径）。
        //   踏进去 → CampMain.OnDiscovery → RatRecruit.Resolve → 弹招募对话。
        (float rx, float ry) = ExplorationWalls.SewerDeepestPoint;
        AddDiscoveryPoint(
            RatRecruit.MeetDiscoveryId,
            new Vector2(rx, ry),
            markerColor: new Color(0.72f, 0.66f, 0.52f),   // 昏黄：一个活人
            label: RatPerk.RatName);
    }

    // ================= [SPEC-T60] 破败教堂 =================
    //
    // 🔴 用户原话：「破败教堂，规模中，穿过教堂的视野盲区，打开门看到后院墓地中有大量丧尸（突然看到吓一跳的感觉），
    //    在这里可以找到一些军方留下的被烧了一半的忏悔录和一些被军方屠杀的人用血写在墙上的对军方的辱骂。」
    //
    // 🔴 **这一关的每一堵墙都是为挡视线砌的，不是为了挡路。** 几何、门、丧尸布点、"吓一跳"的那两条数字
    //    （门关着＝可见 0 只 / 门一开＝一片同时进锥）全在纯逻辑 <see cref="RuinedChurch"/> 里，并且**上了单测**。
    //    这里只做空间执行：把矩形实体化、把门装上、把点铺出来。**别在这里另写一份几何。**

    /// <summary>
    /// [SPEC-T60] 破败教堂：一关**视野**，不是一关战力。
    /// 分区（南→北，近→深）：门厅(告解亭) → 中殿(长椅/立柱的盲区·墙上的血字) → 圣坛(祭台/圣器室) → **后院墓地(门后那一片)**。
    /// </summary>
    private void SetupRuinedChurch()
    {
        // 分区占位地台（纯视觉）。墓地那一片刻意压暗——但你在推开门之前**根本看不到它**。
        AddZonePad(new Vector2(300, 1240), new Vector2(1800, 164), new Color(0.24f, 0.22f, 0.24f, 0.55f)); // 门厅
        AddZonePad(new Vector2(300, 792), new Vector2(1800, 448), new Color(0.22f, 0.21f, 0.25f, 0.55f));  // 中殿
        AddZonePad(new Vector2(300, 512), new Vector2(1800, 268), new Color(0.26f, 0.24f, 0.20f, 0.58f));  // 圣坛（旧金色）
        AddZonePad(new Vector2(300, 136), new Vector2(1800, 364), new Color(0.16f, 0.19f, 0.17f, 0.62f));  // 后院墓地（最暗）

        // ——墙体：外墙 / 墓地边界 / 屏风 / 长椅 / 立柱 / 告解亭 / 祭台 / 圣器室——
        // 同一批矩形三用：碰撞（挡人）/ 导航 obstruction（阻断寻路）/ 墙层射线（**挡视线**）。
        var stoneC = new Color(0.33f, 0.32f, 0.30f, 0.95f);
        foreach (WallRect w in RuinedChurch.Walls())
            AddSolidWall(w, stoneC, zIndex: -5);

        // ——两扇可关的门，**都在墓地边界上，初始关着**——
        // 关着 ⇒ 门在墙层上 ⇒ **它挡视线** ⇒ 你在门这边真的看不见后院。推开它的那一刻，一片丧尸同时进视野。
        var doorC = new Color(0.42f, 0.30f, 0.22f, 0.95f);
        foreach (ExplorationDoor d in RuinedChurch.Doors())
            AddLevelDoor(d, doorC);

        // ——12 处搜刮点（穷：布/木/铁/蜡 + 一点白银，**没有枪没有弹药**）——
        foreach (ChurchCacheSpot s in RuinedChurch.CacheSpots)
            AddDiscoveryPoint(s.Id, new Vector2(s.X, s.Y), markerColor: ChurchZoneColor(s.Zone), label: s.Label);
    }

    /// <summary>教堂分区的标记色（墓地那两处刻意发绿——你看见它们时，多半也看见了别的东西）。</summary>
    private static Color ChurchZoneColor(ChurchZone zone) => zone switch
    {
        ChurchZone.Narthex => new Color(0.52f, 0.48f, 0.44f),
        ChurchZone.Nave => new Color(0.55f, 0.50f, 0.40f),
        ChurchZone.Chancel => new Color(0.62f, 0.55f, 0.36f),   // 圣坛（旧金）
        _ => new Color(0.42f, 0.52f, 0.40f),                     // 墓地（苔绿）
    };

    // ================= [SPEC-T60] 难民营地 =================
    //
    // 🔴 用户原话：「难民营地，规模中，临时建起的一片平房，内是大量的小房间，过道狭窄，光线昏暗，视野受限，
    //    并且物资分散在每一个房间中，一同在房间中的还有开门跳脸的丧尸。」
    //
    // 🔴 **昏暗＝ <see cref="ExplorationLighting.IsIndoorsDark"/>（环境光 0.10）** ⇒ 视距 300→约 124、半角 60°→33°。
    //    「视野受限」是算出来的，不是画出来的。**一盏固定光源都不预置**（见 <see cref="SetupLevelLights"/>）。
    // 🔴 **窄是玩家的朋友**：房门 48px ⇒ 门口一次只过得来一只丧尸 ⇒ **卡门口＝把围攻打成 1v1**。
    //    几何、门、跳脸布点、那三个数（48/72/90）全在纯逻辑 <see cref="RefugeeCamp"/> 里并上了单测。

    /// <summary>
    /// [SPEC-T60] 难民营地：一片临时排屋——18 间小房、18 扇关着的门、两条窄过道、**没有一盏灯**。
    /// </summary>
    private void SetupRefugeeCamp()
    {
        // 地台（纯视觉）：整片营区一块暗地台。这一关不靠地台分区——**它靠门**。
        AddZonePad(new Vector2(276, 156), new Vector2(1848, 1248), new Color(0.17f, 0.16f, 0.15f, 0.60f));

        // ——墙体：营区外墙（两个**关不上**的入口）+ 18 间平房的轮廓（各一处 48px 门洞）——
        var shackC = new Color(0.30f, 0.27f, 0.23f, 0.95f);
        foreach (WallRect w in RefugeeCamp.Walls())
            AddSolidWall(w, shackC, zIndex: -5);

        // ——18 扇房门，初始全部关着。门＝墙层 ⇒ 挡视线 ⇒ **你没有任何办法提前知道门后有什么**——
        var doorC = new Color(0.45f, 0.35f, 0.26f, 0.95f);
        foreach (ExplorationDoor d in RefugeeCamp.Doors())
            AddLevelDoor(d, doorC);

        // ——14 处搜刮点，分在 14 个不同的房间里（"物资分散在每一个房间中"）——
        var lootC = new Color(0.52f, 0.47f, 0.38f);
        foreach (RefugeeCacheSpot s in RefugeeCamp.CacheSpots)
            AddDiscoveryPoint(s.Id, new Vector2(s.X, s.Y), markerColor: lootC, label: s.Label);
    }

    // ================= [SPEC-T51] 斯图尔特家族庄园 =================
    //
    // 🔴 用户原话（authored 唯一事实源，一字不改）：「斯图尔特家族庄园（农庄，并不是很富裕，中地图，
    //    有盘踞的劫掠者和岗哨，高危，高风险不是永远高回报，这个调查点最富裕的地方是劫掠者们的装备和衣服，
    //    并且这里会有斯图尔特家的一些剧情，讲述了他们好心收留一些流浪者，结果被背刺，女儿妻子被奸杀，
    //    男性尸体吊挂在门口喂丧尸，在枯井底有抱着婴儿饿死的女性尸体）」
    //
    // 🔴 **别把这一关"平衡"掉**：
    //    · 农庄**是穷的**（10 处搜刮点全是布/木头/土豆，见 ExplorationCache）——那正是「高风险不是永远高回报」；
    //    · 回报**长在人身上**（7 个劫掠者的武器与衣服，见 StuartManor）——**先打赢，才有得扒**；
    //    · 而「打赢劫掠者白捡一身装备」这个场景**不存在**（docs/research/2026-07-14-combat-cost.md：
    //      持棍棒的胜率 96.5% 却 66% 留下骨折、持破甲锤 70.8% 且 13% 断肢、持手枪只有 26.2%；
    //      不治疗连打，能撑过第 3 个的只剩 3.5%）⇒ **玩家现实里会清掉两三个就撤，或者干脆绕过去。**
    //      **允许他不打**（潜行绕过 / 只清边缘 / 撤退）是这一关的正确玩法之一，不是设计失败。
    //
    // 🔴 **噪音是这一关的核心机制**：哨位间距是照着枪声半径设计的（见 StuartManor.Posts / AlertedBy）——
    //    从庭院中央动手：弓(70)→0 人 / 匕首(90)→1 人 / **手枪(350)→3 人 / 步枪(600)→6 人（整座庄园）**。
    //    ⇒ 「枪纸面最强，但**一开枪就没有『逐个清哨』了**」。真正的通关手段是弓弩 + 哨兵扫视的空窗。

    /// <summary>庄园大门（关内世界坐标，南侧）：探索队自此进关，**一进来就看见门口横梁上的人**。哨兵的扫视中心朝这儿。</summary>
    private static readonly Vector2 StuartGate = new(
        (float)(StuartManor.Entrance.X * LevelW), (float)(StuartManor.Entrance.Y * LevelH));

    /// <summary>门口吊尸的位置（叙事点锚点；丧尸就聚在这一小片）。与 <c>NarrativeSpotRegistry</c> 里那处 (700,1300) 一致。</summary>
    private static readonly Vector2 StuartGallows = new(700f, 1300f);

    /// <summary>
    /// [SPEC-T51] 斯图尔特家族庄园：一座被劫掠者盘踞的<b>穷农庄</b>。
    /// 分区（南→北，近→深）：大门/前院晒场 → 谷仓/畜栏(东) → 主屋(西·里屋在最里头) → 后院枯井(东北·最深)。
    /// <para>占位美术：分区地台 + 主屋/谷仓占位墙体（<see cref="AddRoomOutline"/>）+ 枯井占位 + 10 处搜刮点标记；
    /// 掉落/叙事在 <see cref="ExplorationCache.Resolve"/>，四处 authored 叙事点由 <see cref="SetupNarrativeSpots"/> 按注册表自动铺（此处不重复铺）。</para>
    /// </summary>
    private void SetupStuartManor()
    {
        // 分区占位地台（纯视觉）：前院/晒场(南)、谷仓/畜栏(东中)、主屋(西北)、后院(东北·枯井)。
        AddZonePad(new Vector2(420, 1080), new Vector2(1160, 400), new Color(0.26f, 0.24f, 0.18f, 0.55f)); // 前院/晒谷场（土黄）
        AddZonePad(new Vector2(1380, 840), new Vector2(560, 380), new Color(0.24f, 0.21f, 0.16f, 0.58f));  // 谷仓/畜栏
        AddZonePad(new Vector2(1780, 220), new Vector2(500, 520), new Color(0.20f, 0.22f, 0.19f, 0.55f));  // 后院（枯井在此）

        // 主屋（含**里屋**——那扇从外头钉了闩的门在最里头）：占位墙体，南墙留门。
        AddRoomOutline(new Rect2(560, 380, 720, 440), new Color(0.34f, 0.30f, 0.24f, 0.95f), "主屋", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(920, 380, 300, 180), new Color(0.28f, 0.25f, 0.21f, 0.95f), "里屋", RoomEdge.Bottom);
        // 谷仓：占位墙体，西墙留门（从晒场那侧进）。
        AddRoomOutline(new Rect2(1440, 880, 420, 300), new Color(0.32f, 0.27f, 0.20f, 0.95f), "谷仓", RoomEdge.Left);

        // 后院的枯井（占位视觉；authored 叙事点「枯井」由注册表铺在 2050,330）。
        DrawWellPlaceholder(new Vector2(2050f, 330f));
        // 门口的横梁（占位视觉：两根门柱 + 一道横梁；authored 叙事点「门口」由注册表铺在 700,1300）。
        DrawStuartGallowsPlaceholder();

        var yardC = new Color(0.55f, 0.48f, 0.36f);   // 前院/晒场（棕黄，物资）
        var houseC = new Color(0.52f, 0.46f, 0.38f);  // 主屋
        var barnC = new Color(0.50f, 0.44f, 0.32f);   // 谷仓/农具

        // 前院/晒场（近，3）
        AddDiscoveryPoint(ExplorationCache.StuartGateCartId, new Vector2(900, 1350), markerColor: yardC, label: "门前板车");
        AddDiscoveryPoint(ExplorationCache.StuartThreshingYardId, new Vector2(980, 1150), markerColor: yardC, label: "晒谷场");
        AddDiscoveryPoint(ExplorationCache.StuartChickenCoopId, new Vector2(1250, 1300), markerColor: yardC, label: "鸡舍");

        // 主屋（中，4）
        AddDiscoveryPoint(ExplorationCache.StuartKitchenId, new Vector2(640, 760), markerColor: houseC, label: "灶间");
        AddDiscoveryPoint(ExplorationCache.StuartHallCupboardId, new Vector2(1080, 700), markerColor: houseC, label: "堂屋碗柜");
        AddDiscoveryPoint(ExplorationCache.StuartWardrobeId, new Vector2(860, 470), markerColor: houseC, label: "卧室衣柜");
        AddDiscoveryPoint(ExplorationCache.StuartPantryId, new Vector2(1200, 560), markerColor: houseC, label: "储藏间");

        // 谷仓/农具（中，2）
        AddDiscoveryPoint(ExplorationCache.StuartHayLoftId, new Vector2(1560, 980), markerColor: barnC, label: "草料阁");
        AddDiscoveryPoint(ExplorationCache.StuartToolShedId, new Vector2(1750, 1120), markerColor: barnC, label: "农具棚");

        // 后院菜窖（最深，1）——翻到底，也不过是一窖发芽的土豆和一卷绷带。
        AddDiscoveryPoint(ExplorationCache.StuartRootCellarId, new Vector2(2020, 600), markerColor: barnC, label: "后院菜窖");

        // [T67] 两处**采集点**：那家人的菜地还在，只是没人回来收了（在地上刨土豆，走 ForageLogic）。
        var forageC = new Color(0.42f, 0.62f, 0.36f);
        AddDiscoveryPoint(ForageLogic.StuartGardenPotatoId, new Vector2(1900, 720), markerColor: forageC, label: "菜地土豆");
        AddDiscoveryPoint(ForageLogic.StuartFurrowPotatoId, new Vector2(1980, 840), markerColor: forageC, label: "垄尾漏刨");
    }

    /// <summary>
    /// [批次25·T50] 消防站：街区消防站——车库（卷帘门大开）+ 值班室 + 器材间 + 后院训练塔。
    /// 用户原话：「消防站（一些基础物资和消防斧，<b>小地图</b>，<b>低危</b>）」。
    /// <para>
    /// <b>小地图</b>：5 处搜刮点（小点 band），空间也铺得紧凑——车库占南半场（队伍出生点 200,1400 就在它西南），
    /// 两间小屋（值班室/器材间）+ 一片后院，走两步就到，不做迷宫。
    /// <b>低危</b>：3 只游荡丧尸（<see cref="FireStationZombieSpots"/>，全图最少），且**入口车库区一只都没有**。
    /// </para>
    /// <para>
    /// 近→深：车库(南/近·消防车+<b>器材墙上的消防斧</b>) → 值班室(东/中) → 器材间(北/深·急救柜) → 后院(东北/最深·杂物棚)。
    /// 两间小屋用 <see cref="AddRoomOutline"/> 实体墙，门洞**都朝南**（正对车库那片开阔地）⇒ 从出生点一路向北，
    /// 每间屋都是正面进，不存在"门顶死在别人墙上"的通行陷阱（那是 <c>SetupNightingalePharmacy</c> 记下的教训）。
    /// 掉落/叙事在 <see cref="ExplorationCache.Resolve"/>；叙事调查点（车库出车记录板）由 <see cref="SetupNarrativeSpots"/> 按目的地自动铺。
    /// </para>
    /// </summary>
    private void SetupFireStation()
    {
        // 分区占位地台（纯视觉）：车库(南/近·水泥灰)、后院训练场(东北/最深·土黄)。
        AddZonePad(new Vector2(360, 1060), new Vector2(900, 420), new Color(0.26f, 0.27f, 0.29f, 0.6f));  // 车库（卷帘门大开）
        AddZonePad(new Vector2(1500, 400), new Vector2(700, 460), new Color(0.30f, 0.28f, 0.22f, 0.55f)); // 后院·训练场

        // 两间小屋（实体墙，门洞都朝南＝正对车库开阔地，见类注）。
        AddRoomOutline(new Rect2(1420, 1080, 380, 300), new Color(0.30f, 0.32f, 0.34f, 0.95f), "值班室", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(900, 620, 420, 300), new Color(0.28f, 0.30f, 0.32f, 0.95f), "器材间", RoomEdge.Bottom);

        // 消防车（纯视觉占位）：车头朝外停在车库里，器材箱那一侧就是搜刮点。
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(560, 1240), new Vector2(300, 110)),
            Color = new Color(0.46f, 0.16f, 0.14f, 0.95f), // 消防红（褪色）
            ZIndex = 5,
        });

        var bayC = new Color(0.55f, 0.5f, 0.42f);   // 车库（近）
        var gearC = new Color(0.62f, 0.42f, 0.30f); // 器材墙：偏红，全站唯一的武器（消防斧）就挂在这儿
        var roomC = new Color(0.5f, 0.46f, 0.38f);  // 值班室/器材间（中·深）
        var medC = new Color(0.42f, 0.62f, 0.44f);  // 急救柜（药绿，与药店/医院同色语义）

        // 车库（南/近，2）
        AddDiscoveryPoint(ExplorationCache.FireStationEngineBayId, new Vector2(700, 1300), markerColor: bayC, label: "消防车器材箱");
        AddDiscoveryPoint(ExplorationCache.FireStationGearWallId, new Vector2(1120, 1150), markerColor: gearC, label: "器材墙");
        // 值班室（东/中，1）
        AddDiscoveryPoint(ExplorationCache.FireStationDutyRoomId, new Vector2(1600, 1250), markerColor: roomC, label: "值班室铺位");
        // 器材间（北/深，1·唯一急救包）
        AddDiscoveryPoint(ExplorationCache.FireStationMedCabinetId, new Vector2(1110, 790), markerColor: medC, label: "急救柜");
        // 后院（东北/最深，1）
        AddDiscoveryPoint(ExplorationCache.FireStationBackyardShedId, new Vector2(1900, 620), markerColor: roomC, label: "杂物棚");
    }

    /// <summary>门口横梁的占位视觉（两根门柱 + 一道横梁 + 几条垂下的绳）：纯 Polygon2D，无碰撞。</summary>
    private void DrawStuartGallowsPlaceholder()
    {
        var wood = new Color(0.30f, 0.25f, 0.19f, 0.95f);
        Vector2 g = StuartGallows;

        // 横梁
        AddChild(new Polygon2D
        {
            Polygon = Quad(g + new Vector2(-150f, -14f), new Vector2(300f, 14f)),
            Color = wood,
            ZIndex = 5,
        });
        // 两根门柱
        AddChild(new Polygon2D
        {
            Polygon = Quad(g + new Vector2(-160f, -14f), new Vector2(16f, 90f)),
            Color = wood,
            ZIndex = 5,
        });
        AddChild(new Polygon2D
        {
            Polygon = Quad(g + new Vector2(144f, -14f), new Vector2(16f, 90f)),
            Color = wood,
            ZIndex = 5,
        });
        // 垂下的绳（四条，间隔匀——"这个高度是有人算过的"）
        for (int i = 0; i < 4; i++)
        {
            AddChild(new Polygon2D
            {
                Polygon = Quad(g + new Vector2(-108f + (i * 72f), 0f), new Vector2(3f, 46f)),
                Color = new Color(0.42f, 0.38f, 0.30f, 0.95f),
                ZIndex = 6,
            });
        }
    }

    /// <summary>
    /// [SPEC-T51] 庄园劫掠者布防：编制/持械/着装/是否站岗<b>全在 <see cref="StuartManor.Roster"/>（authored 表）</b>，
    /// 布点在 <see cref="StuartManor.Posts"/>（同一张表，与噪音几何共用）——本方法只管把它们摆到地图上。
    /// <b>数值改动去改那张表，别改这里。</b>
    ///
    /// <para><b>照 <c>SpawnGoldfingerGuards</c> 的范式做，没另起炉灶</b>：<see cref="Raider.Create"/> 点名持械 + 点名着装
    /// ⇒ 杀了能扒（<c>CorpseLoot.Strip</c> 必掉零掷骰，走 <c>SpawnLevelCorpse</c> 落尸通道，<b>零新规则</b>）；
    /// <see cref="Raider.ConfigureSentry"/> ⇒ 会站岗（扫视<b>周期固定、可观察、可预测</b>，玩家能数着拍子摸过去）。</para>
    ///
    /// <para><b>三个哨兵的扫视档</b>：近入口的大门哨走<b>警觉</b>档（难绕——他就是来防你从这个方向进来的）；
    /// 深处的谷仓/主屋哨走<b>懈怠</b>档（扫得慢、端点发呆久＝好绕）——<b>越深越好摸</b>，逼玩家在
    /// 「从门口硬啃」和「绕开大门、摸进深处」之间做取舍。</para>
    /// </summary>
    private void SpawnStuartManorRaiders()
    {
        var wander = new Rect2(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);
        IReadOnlyList<ManorRaider> roster = StuartManor.Roster;

        for (int i = 0; i < roster.Count && i < StuartManor.Posts.Count; i++)
        {
            ManorRaider m = roster[i];
            (double px, double py) = StuartManor.Posts[i];
            var pos = new Vector2((float)(px * LevelW), (float)(py * LevelH));

            var r = Raider.Create(
                wander, LevelTargets,
                displayName: m.DisplayName,
                weapon: StuartManor.WeaponFor(m.Arm),
                outfit: StuartManor.ApparelFor(m.Outfit)); // 🔴 穿什么＝掉什么：这一关的回报就长在这一行上
            r.Inject(Combat, Clock);
            r.ConfigurePerception(localLightAt: SampleLevelLight);
            r.Position = pos;

            if (m.IsSentry)
            {
                float facing = (StuartGate - pos).Angle();          // 扫视中心朝大门——他们防的是从南边进来的人
                bool deep = pos.Y < LevelH * 0.62f;                  // 主屋/谷仓一线以北＝深处
                r.ConfigureSentry(pos, facing, deep ? SentrySweep.Slack : SentrySweep.Alert);
            }

            _actorLayer.AddChild(r);
            _levelRaiders.Add(r);
            _markers[r] = CreateActorMarker(r, new Color(0.72f, 0.26f, 0.22f)); // 暗红：敌对幸存者（与丧尸绿/己方一眼区分）
        }
    }

    /// <summary>
    /// [SPEC-T51] 门口的丧尸（<b>用户原话「男性尸体吊挂在门口<b>喂丧尸</b>」的空间落地</b>）：
    /// 4 只聚在大门横梁底下、给一个贴着门口的<b>紧凑</b>徘徊区 ⇒ 它们不会散进庄园，就在那儿仰着脸够。
    ///
    /// <para><b>它们和劫掠者互不攻击</b>——不是特判，是既有结构：关内敌对单位的目标池 <see cref="LevelTargets"/>
    /// 只有探索队（+随队布鲁斯）⇒ 丧尸看不见劫掠者、劫掠者也不理丧尸。落到玩法上恰好就是那句话的意思：
    /// <b>这道尸群是劫掠者给自己养的护城河，进门要先过它。</b></para>
    ///
    /// <para>⚠️ 在门口打这一架<b>不会惊动庄园</b>（近战噪音 90~150px，而最近的哨位在 250px 开外——见
    /// <see cref="StuartManor.AlertedBy"/>）：这是有意的<b>喘息位</b>——门口这一架是关卡的"学费"，不是死刑。
    /// 数量/布点拟定待调（归 param-calibration）。</para>
    /// </summary>
    private void SpawnStuartGateZombies()
    {
        // 贴着门口横梁的紧凑徘徊区（它们被吊尸拴在这儿，不往庄园里散）。
        var gateWander = new Rect2(StuartGallows.X - 220f, StuartGallows.Y - 170f, 440f, 300f);

        Vector2[] spots =
        {
            new(StuartGallows.X - 60f, StuartGallows.Y - 70f),
            new(StuartGallows.X + 80f, StuartGallows.Y - 60f),
            new(StuartGallows.X - 100f, StuartGallows.Y + 50f),
            new(StuartGallows.X + 120f, StuartGallows.Y + 30f),
        };
        foreach (Vector2 spot in spots)
            SpawnZombieAt(spot, gateWander);
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
    /// 在<b>关内</b>落一具可搜刮的尸体（<c>CampMain.SpawnLevelCorpse</c> 在敌人倒下时调）——
    /// 「探索关杀敌也要落尸、也要能搜刮」（用户拍板）。
    ///
    /// <para><b>坐标系</b>：探索关是<b>平面 cartesian</b>（与营地的 faux-iso 无关，见 docs/TODO.md「探索关正式化专项」②），
    /// 所以尸体点直接落在死者<b>咽气的那个点</b>上——<paramref name="worldPos"/> 是全局坐标，这里 <c>ToLocal</c>
    /// 换算成关内坐标。营地那套「尸体格 / 不堆叠推挤 / iso 投影」（<see cref="CorpseYard"/>）在这儿一概不适用，
    /// 也<b>不该</b>硬搬：关内没有 iso 人形层可挂。</para>
    ///
    /// <para><b>不发明新交互</b>：完全复用本关既有的搜刮范式（<see cref="AddDiscoveryPoint"/> 的
    /// marker + 标签 + 踏入 Area2D）——关内一切搜刮点都是"踏进去开搜"，尸体只是<b>又一个可搜刮容器</b>。
    /// 上报的 id ＝ 容器名本身（<see cref="CorpseNaming"/>，如「据点幸存者的尸体 #1」），营地层据此路由到逐件搜刮。</para>
    ///
    /// <para>⚠️ <b>与 authored 发现点的两处刻意不同</b>：
    /// <list type="number">
    /// <item><b>可重复踏入</b>（<b>不</b>进 <see cref="_firedDiscoveries"/>）：尸体一次掏不完是常态
    /// （被咬了要跑），跑开还得能回来接着掏。authored 发现点是一次性的，尸体不是。</item>
    /// <item><b>动态、无注册表</b>：现杀现落，id 是运行时生成的，不在 <see cref="ExplorationCache"/> 里。</item>
    /// </list></para>
    ///
    /// <para>[HANDOFF] 探索关正式化专项（TODO#10 ③）：届时搜刮点改"点击→寻路到位→开搜"，
    /// <b>尸体点要跟着一起改</b>（它走的就是这条 AddDiscoveryPoint 范式，改一处即可）。</para>
    /// </summary>
    public void AddCorpseSearchPoint(string containerName, Vector2 worldPos)
    {
        Vector2 pos = ToLocal(worldPos);

        // 视觉容器（同发现点：进 _discoveryVisuals ⇒ 视野外不揭示——看不见的尸体不该在雾里发光）。
        var visual = new Node2D();
        AddChild(visual);

        var mark = new Polygon2D
        {
            Polygon = Quad(new Vector2(-12, -12), new Vector2(24, 24)),
            Color = new Color(0.45f, 0.16f, 0.16f, 0.65f),   // 暗血红：一具躺着的尸体（区别于物资棕黄/叙事冷青）
            Position = pos,
            ZIndex = 7,                                       // 压在地面之上、人形(10)之下——人从尸体上走过去
        };
        visual.AddChild(mark);

        var tag = new Label
        {
            Text = containerName,   // 「据点幸存者的尸体 #1」——玩家一眼看见这儿躺着谁
            Position = pos + new Vector2(-16, -38),
            ZIndex = 12,
        };
        tag.AddThemeFontSizeOverride("font_size", 12);
        tag.AddThemeColorOverride("font_color", new Color(0.88f, 0.72f, 0.68f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        visual.AddChild(tag);

        _discoveryVisuals.Add((visual, pos));

        var zone = new Area2D { Position = pos };
        zone.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(56, 56) } });
        zone.CollisionMask = 0b0001u;   // 同发现点/返回区：只感知玩家 Pawn 所在层
        zone.BodyEntered += body =>
        {
            // **不去重**（见上）：走开了还能回来接着掏。搜空之后营地层自会回一句"已经搜过了"。
            if (body is Pawn finder)
                RaiseDiscovery(containerName, finder);
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
