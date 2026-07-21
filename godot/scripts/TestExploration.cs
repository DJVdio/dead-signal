using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration : ExplorationLevel
{
    /// <summary>广播站原位是否仍有关键设备；设备随遗体遗失时为 false，遗体腐烂后恢复为 true。</summary>
    public bool TransmitterAvailableAtOrigin { get; set; } = true;
    // 画布尺寸 per-destination：Initialize 时按 DestinationName 查 ExplorationLevelSize 取值；未登记的目的地回退默认 2400×1600
    // （当前仍走回退的只剩下水道与金手指帮据点，见 ExplorationLevelSize.Overrides）。
    // 🔴 改造前这两个是写死的 const 2400f/1600f。现在是**实例字段** ⇒ 所有实例方法对 LevelW/LevelH 的引用一字不改，
    //    只是从"编译期常量"变成"本关运行时尺寸"。静态上下文只有 3 处（LookoutTelescopePosition/BroadcastTransmitterPosition
    //    两处占位坐标 + LevelWanderRect），它们读不到实例字段：LevelWanderRect 已改为实例方法；两处锚点方向**刻意相反**——
    //    LookoutTelescopePosition 读 SizeFor(城市之巅) 跟随放大，BroadcastTransmitterPosition 是 authored 定点、钉死 (1200,300)
    //    不跟随（各自 summary 有详述）。
    private float LevelW = ExplorationLevelSize.DefaultWidth;
    private float LevelH = ExplorationLevelSize.DefaultHeight;
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

    /// <summary>望远镜占位在瞭望关内的世界坐标（贴北墙，朝正北）。anim-lookout 据此放演出节点/镜头锚点。
    /// <b>静态字段</b>随城市之巅画布同步：读 <see cref="ExplorationLevelSize.SizeFor"/>(城市之巅) 的宽取半 ⇒ 恒为北缘正中，
    /// 该关适度放大到 2800×1900 后 X 自动变 1400（不再是旧 DefaultWidth/2＝1200）。日后调档只改 Overrides，此锚点自动跟随。</summary>
    public static readonly Vector2 LookoutTelescopePosition =
        new(ExplorationLevelSize.SizeFor(ExplorationCache.CityRooftopLookoutName).Width / 2f, 260f);

    // ——广播台：发出设备定点投放契约（RadioMainline 主线消费）——
    /// <summary>
    /// 广播台「发出设备」发现点 id（须与 <see cref="RadioMainline.TransmitterDiscoveryId"/> 一致）。踏入机房发现区即上报此 id。
    /// 挂点在 <c>CampMain.OnExplorationDiscovery</c>：取得设备先记为本趟携带态；活人回营后才由 <see cref="RadioMainline.GrantTransmitter"/> 提交主线。
    /// 定点非随机（用户 D4 拍板：主线关键物资保底/定点投放）。
    /// </summary>
    public const string BroadcastTransmitterDiscoveryId = RadioMainline.TransmitterDiscoveryId;

    /// <summary>发射机占位在广播台关内的世界坐标（机房内，贴发射塔基座）。
    /// 🔴 <b>authored 定点，刻意不跟随画布缩放</b>——广播台已放大到 3200×2200（见 <see cref="ExplorationLevelSize"/> 覆盖行），
    /// 但本锚点仍取 <see cref="ExplorationLevelSize.DefaultWidth"/>/2 ＝ <b>(1200,300) 一字不动</b>：它是 authored 坐标，
    /// 放大时反过来让**机房去框住它**（<c>SetupBroadcastStation</c> 的机房大厅 x[600,1884] 已含此点）。
    /// ⇒ <b>不要</b>把它改成读 <c>SizeFor(广播台).Width/2</c>（那会推到 1600、离开 authored 位）——
    /// 与 <see cref="LookoutTelescopePosition"/>（演出锚点，可同步、已改为跟随）**方向相反**，勿照抄。</summary>
    public static readonly Vector2 BroadcastTransmitterPosition = new(ExplorationLevelSize.DefaultWidth / 2f, 300f);

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

    // [SPEC-T60] 门后特殊丧尸登记：门名 → 锁在其后的丧尸（该门被打开时逐个 Activate）。
    //   一只可挂多扇门（教堂墓地那群锁在后院门/北耳门后，推开任一即唤醒）。开门入口＝ToggleLevelDoor(推)/UnlockLevelDoor(撬)。
    private readonly Dictionary<string, List<Zombie>> _doorLockedZombies = new();
    // [SPEC-B13] 超市骗局：关内敌对幸存者（Raider 阵营）。轻信被诱入内圈伏击、或拒绝后闯内圈时按需生成（见 SpawnSupermarketRaiders）。
    private readonly List<Raider> _levelRaiders = new();
    private readonly List<Rect2> _obstructions = new();
    private Area2D _returnZone = null!;
    private Node2D _actorLayer = null!;
    private Node2D _isoLayer = null!;

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
        public LockTier Lock;   // None＝没上锁（推一下就开）；否则撬开要铁丝，档次决定成功率/耗时
        public StaticBody2D Body = null!;
        public Polygon2D Panel = null!;
    }

    // 视野遮暗（批次4）：探索关全程启用。发现点视觉容器供检测层隐藏（视野外不揭示）。
    private VisionMask? _visionMask;

    // 批次4 光照：探索关固定光源场（预置油灯）。供玩家遮暗渲染（VisionMask.SetSourceProvider）与
    // 关内丧尸感知（ConfigurePerception 的 localLightAt）按位置查询最强光源贡献。位置/盏数拟定待调。
    private readonly LightField _levelLights = new();
    // 动态光源不常驻固定场：幸存者手持光 + 劫掠者战斗火把按帧投影，避免把移动者的位置写死进场景。
    private readonly List<PlacedLight> _dynamicLightsScratch = new();
    private readonly List<(Node2D container, Vector2 pos)> _discoveryVisuals = new();

    // 点击式探索交互共用发现点几何。Proximity 保留 Area2D 踏入；Click 只允许 CampMain 右键寻路到位后
    // 经 TriggerDiscovery 进入同一条 OnDiscovery 路由。Visual 用来阻止玩家点击尚未被视野揭示的点。
    private sealed record DiscoveryTarget(
        string Id, string Label, Rect2 Rect, bool Repeatable, NarrativeTrigger Trigger, Node2D Visual);
    private readonly List<DiscoveryTarget> _discoveryTargets = new();

    // 探索发现点：本关内已触发过的 discoveryId（防同一关内重复上报；跨关持久去重由 CampMain 的 flag 负责）。
    private readonly HashSet<string> _firedDiscoveries = new();

    // 战斗引擎依赖：与营地单位共用同一实例（由 CampMain 在 Initialize 前注入），
    // 关卡新建的丧尸经 Actor.Inject(Combat, Clock) 拿到它——缺则丧尸首帧 Think 解引用 Clock 崩。
    public CombatEngine Combat { get; set; } = null!;

    public override void Initialize()
    {
        // 画布尺寸按目的地取（未登记覆盖的目的地回退默认 2400×1600）。须在任何用到 LevelW/LevelH 的 Setup* 之前。
        (LevelW, LevelH) = ExplorationLevelSize.SizeFor(DestinationName);

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
        else if (DestinationName == ExplorationCache.PoliceStationName)
            SetupPoliceStation();

        SetupFormalEnvironmentArt();

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

    /// <summary>按目的地语义铺正式环境物件。仅表现，不新增碰撞、导航洞、搜刮点或剧情。</summary>
    private void SetupFormalEnvironmentArt()
    {
        if (SetupSiteSpecificEnvironmentArt())
            return;

        int first = DestinationName switch
        {
            string n when n.Contains("医院") || n.Contains("药店") || n.Contains("警察") || n.Contains("消防") => 4,
            string n when n.Contains("加油") || n.Contains("广播") || n.Contains("仓库") || n.Contains("下水") => 8,
            string n when n.Contains("村") || n.Contains("小屋") || n.Contains("庄园") || n.Contains("教堂") || n.Contains("营地") => 12,
            _ => 0,
        };
        Vector2[] points =
        {
            new(LevelW * 0.16f, LevelH * 0.24f),
            new(LevelW * 0.82f, LevelH * 0.28f),
            new(LevelW * 0.20f, LevelH * 0.72f),
            new(LevelW * 0.78f, LevelH * 0.76f),
        };
        for (int i = 0; i < points.Length; i++)
            _isoLayer.AddChild(new ExplorationPropSprite
            {
                CartesianPosition = points[i],
                AtlasIndex = first + i,
                DisplaySize = Mathf.Clamp(Mathf.Min(LevelW, LevelH) * 0.085f, 105f, 190f),
                ZIndex = 1,
            });
    }

    /// <summary>
    /// 六张重点探索图使用地点专属正式道具，不再按「城市/公共/工业/乡村」大类随机撒四件泛化物件。
    /// 坐标贴合既有建筑、搜刮点与实体障碍；节点仍为纯视觉，不改变碰撞、导航、掉落或剧情。
    /// </summary>
    private bool SetupSiteSpecificEnvironmentArt()
    {
        // 村庄的民居与水井在关卡 authored 坐标处逐件铺设；只需阻止末尾再撒泛化四件套。
        if (DestinationName == VillageRescue.DestinationName)
            return true;

        (int Index, Vector2 Position, float Size)[] props = DestinationName switch
        {
            ExplorationCache.GasStationName => new[]
            {
                (0, new Vector2(1400f, 1900f), 160f), // 加油机
                (1, new Vector2(1320f, 1630f), 155f), // 西侧抛锚车
                (1, new Vector2(1820f, 1590f), 155f), // 东侧抛锚车
                (2, new Vector2(1400f, 950f), 180f),  // 油罐车
                (3, new Vector2(820f, 1480f), 145f),  // 便利店柜台
            },
            NurseRecruit.DestinationName => new[]
            {
                (3, new Vector2(1150f, 1380f), 135f), // 店面柜台
                (4, new Vector2(1720f, 1240f), 140f), // 药品货架
                (5, new Vector2(1080f, 900f), 135f),  // 处方柜
                (6, new Vector2(1250f, 900f), 125f),  // 药品冷藏箱
            },
            ExplorationCache.FireStationName => new[]
            {
                (7, new Vector2(760f, 1520f), 210f),  // 消防车
            },
            WorldMapPanel.BroadcastStationName => new[]
            {
                (8, new Vector2(2520f, 420f), 230f),  // 天线塔
                (9, BroadcastTransmitterPosition, 170f),
                (10, new Vector2(880f, 320f), 145f), // 机架
                (11, new Vector2(400f, 1380f), 155f),// 发电机
            },
            ExplorationCache.HarvesterWarehouseName => new[]
            {
                (12, new Vector2(2350f, 1700f), 220f), // 收割机
                (13, new Vector2(1120f, 1350f), 160f), // 货架通道
                (13, new Vector2(1580f, 1350f), 160f),
                (13, new Vector2(2040f, 1350f), 160f),
            },
            _ => System.Array.Empty<(int, Vector2, float)>(),
        };

        foreach ((int index, Vector2 position, float size) in props)
            AddSiteSpecificProp(position, index, size);
        return props.Length > 0;
    }

    private void AddSiteSpecificProp(Vector2 position, int atlasIndex, float displaySize)
        => _isoLayer.AddChild(new ExplorationPropSprite
        {
            CartesianPosition = position,
            AtlasIndex = atlasIndex,
            AtlasPath = ExplorationPropSprite.SiteSpecificAtlasPath,
            DisplaySize = displaySize,
            ZIndex = 2,
        });

    /// <summary>
    /// 装配视野遮暗层（探索关全程启用）：以探索队为观察者、环境光按当前相位（探索=白昼满档）算锥形，
    /// 视野外网格遮暗 + 视野外丧尸/发现点隐藏。判定仍在 cartesian 平面，绘制统一走 faux-iso 投影。
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
            AddIsoBlock(new Rect2(c.x, c.y, c.w, c.h), CoverColor, 2, height: 8f);
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
    /// 返回鼠标点命中的可交互发现点。一次性点已触发后不再命中；物资缓存和战斗尸体
    /// 允许重复命中，以便搜到一半走开后继续搜。门等容器仍由 CampMain 自己查询。
    /// </summary>
    public bool TryGetDiscoveryTarget(Vector2 worldPoint, out string discoveryId, out Rect2 rect,
        out string label, out NarrativeTrigger trigger)
    {
        DiscoveryTarget? best = null;
        float bestDistance = float.MaxValue;
        foreach (DiscoveryTarget target in _discoveryTargets)
        {
            if (!IsInstanceValid(target.Visual) || !target.Visual.Visible)
                continue; // 视野外尚未揭示：既不显示 hover，也不能隔墙下令调查
            if (!target.Rect.Grow(8f).HasPoint(worldPoint))
                continue;
            if (!target.Repeatable && _firedDiscoveries.Contains(target.Id))
                continue;

            float distance = target.Rect.GetCenter().DistanceSquaredTo(worldPoint);
            if (distance < bestDistance)
            {
                best = target;
                bestDistance = distance;
            }
        }

        if (best is { } hit)
        {
            discoveryId = hit.Id;
            rect = hit.Rect;
            label = hit.Label;
            trigger = hit.Trigger;
            return true;
        }

        discoveryId = "";
        rect = default;
        label = "";
        trigger = NarrativeTrigger.Proximity;
        return false;
    }

    /// <summary>兼容只关心几何的调用方。</summary>
    public bool TryGetDiscoveryTarget(Vector2 worldPoint, out string discoveryId, out Rect2 rect)
        => TryGetDiscoveryTarget(worldPoint, out discoveryId, out rect, out _, out _);

    /// <summary>点击/到达后触发发现点，和 Area2D 踏入使用完全相同的去重语义。</summary>
    public bool TriggerDiscovery(string discoveryId, Pawn finder)
    {
        DiscoveryTarget? target = _discoveryTargets.FirstOrDefault(t => t.Id == discoveryId);
        if (target is null)
            return false;
        if (!target.Repeatable && !_firedDiscoveries.Add(discoveryId))
            return false;

        RaiseDiscovery(discoveryId, finder);
        return true;
    }

    private static bool IsRepeatableDiscovery(string discoveryId)
        => ExplorationInteractionLogic.IsRepeatableDiscovery(discoveryId)
           || !string.IsNullOrEmpty(ExplorationCache.FlagForCache(discoveryId));

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

    /// <summary>关内某点合成局部光照 L∈[0,1]（环境光与固定/动态光源取 max），供敌方感知 ConeFor 消费。</summary>
    private float SampleLevelLight(Vector2 pos)
        => VisionLogic.CombineLight(
            VisionLogic.AmbientLight(Clock.CurrentPhase, LevelIndoorsDark),
            _levelLights.StrongestAt(pos.X, pos.Y, CurrentDynamicLights()));

    /// <summary>
    /// 当前场景的移动光源快照：玩家/探索队手持光与交战中的劫掠者火把。
    /// 返回复用缓冲，只在消费方完成当前查询前有效，不向调用方暴露可修改集合。
    /// </summary>
    private IEnumerable<PlacedLight> CurrentDynamicLights()
    {
        _dynamicLightsScratch.Clear();
        foreach (Pawn p in ExpeditionTeam)
        {
            if (p.Alive && p.HeldLight.ActiveHeld is LightProfile lp)
            {
                _dynamicLightsScratch.Add(new PlacedLight(p.GlobalPosition.X, p.GlobalPosition.Y, lp));
            }
        }

        foreach (Raider r in _levelRaiders)
        {
            if (r.Alive && r.ActiveTorchLight is { } torch)
            {
                _dynamicLightsScratch.Add(torch);
            }
        }

        return _dynamicLightsScratch;
    }

    private void SetupVisionMask()
    {
        SetupLevelLights();
        _visionMask = new VisionMask();
        _visionMask.Configure(new Rect2(0, 0, LevelW, LevelH), VisionMask.ProjectionMode.Iso);
        // 观察者＝存活探索队 + 随队布鲁斯（狗随队时也揭示视野，其感知锥同规则）。
        _visionMask.SetViewersProvider(() =>
        {
            IEnumerable<Actor> viewers = ExpeditionTeam.Where(p => p.Alive).Cast<Actor>();
            return CompanionDog is { Alive: true } dog ? viewers.Append(dog) : viewers;
        });
        _visionMask.SetAmbientProvider(() => VisionLogic.AmbientLight(Clock.CurrentPhase, LevelIndoorsDark));
        // 光源场：玩家侧遮暗按局部光照（灯旁视野更远），VisionMask 内部 CombineLight(ambient, 源贡献)。
        _visionMask.SetSourceProvider(pos => _levelLights.StrongestAt(pos.X, pos.Y, CurrentDynamicLights()));
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
    /// <b>隔着墙也看得见</b>。金手指帮的 4 个守备现在也是 Raider（还站岗），漏掉他们等于把"绕开哨兵视野锥摸进去"
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
                    captured.SetVisualHidden(!v);
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
                    captured.SetVisualHidden(!v);
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
        // （既有洞：超市那 4 个 Raider 一直没被清；金手指帮 4 个守备也走这条列表 ⇒ 一并收口。）
        foreach (var r in _levelRaiders)
        {
            if (IsInstanceValid(r))
                r.QueueFree();
        }
        _levelRaiders.Clear();

    }

    private void BuildTerrain()
    {
        _isoLayer = new Node2D { Name = "IsoLayer", YSortEnabled = true };
        _isoLayer.AddToGroup("iso_layer");
        AddChild(_isoLayer);

        AddIsoSurface(new Rect2(0, 0, LevelW, LevelH), new Color(0.22f, 0.25f, 0.20f), -20);

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

        AddChild(body);
        AddIsoBlock(rect, c, -5, height: border ? 28f : 20f);

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
        AddChild(body);
        AddIsoBlock(rect, color, zIndex, height: 24f);

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

        var panel = AddIsoPolygon(Quad(rect.Position, rect.Size), color, -4);
        AddChild(body);

        var inst = new LevelDoorInstance
        {
            Name = spec.Name,
            Rect = rect,
            State = spec.Initial,
            Lock = spec.Lock,
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

    /// <summary>某扇关内门的锁档（不存在则 <see cref="LockTier.None"/>）。撬锁消费层据此掷成功率/耗时。</summary>
    public LockTier LevelDoorLockTier(string name)
    {
        foreach (LevelDoorInstance d in _levelDoors)
        {
            if (d.Name == name)
                return d.Lock;
        }
        return LockTier.None;
    }

    /// <summary>
    /// 撬锁成功后**把这扇锁门打开**（Locked → Open：撤墙层 body + 摘导航洞 + 重烘焙）。
    /// <b>只对锁着的门生效</b>（没锁的门走 <see cref="ToggleLevelDoor"/> 的推/关）。返回是否真的开了。
    /// </summary>
    public bool UnlockLevelDoor(string name)
    {
        foreach (LevelDoorInstance d in _levelDoors)
        {
            if (d.Name != name)
                continue;
            if (d.State != DoorState.Locked)
                return false;
            d.State = DoorState.Open;
            SetLevelDoorBlocking(d, false);
            CombatVfxBurst.SpawnDoor(_isoLayer, d.Rect.GetCenter(), opening: true);
            ActivateDoorLockedZombies(d.Name); // [SPEC-T60] 撬开锁门＝唤醒门后特殊丧尸（警察拘留区那只）
            return true;
        }
        return false;
    }

    /// <summary>
    /// <b>开 / 关</b>一扇关内的门（<b>开门只有一种动作</b>，与营地同口径：开着就关上，关着就推开）。
    /// 绝大多数关内门<b>没上锁</b>（不需要铁丝，推一下就开）；<b>锁着的门推不开</b>——它走撬锁那条路
    /// （消费层 <c>CampMain.ExecuteLevelDoorInteract</c> 先看 <see cref="LevelDoorState"/>，Locked 则去撬，撬开后调
    /// <see cref="UnlockLevelDoor"/>）。故本方法**拒绝推开锁门**（返回 false），否则「推一下就开」会绕过锁。
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

        // 锁着的门推不开（撬锁走 UnlockLevelDoor）——防「推一下就开」绕过锁。
        if (door.State == DoorState.Locked)
        {
            message = "";
            return false;
        }

        bool wasBlocking = DoorLogic.Blocks(door.State);
        if (wasBlocking)
        {
            door.State = DoorState.Open;
            SetLevelDoorBlocking(door, false);
            CombatVfxBurst.SpawnDoor(_isoLayer, door.Rect.GetCenter(), opening: true);
            ActivateDoorLockedZombies(door.Name); // [SPEC-T60] 推开门＝唤醒门后特殊丧尸
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
        CombatVfxBurst.SpawnDoor(_isoLayer, door.Rect.GetCenter(), opening: false);
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
        _camera = new CameraController { Position = Iso.Project(new Vector2(LevelW / 2, LevelH / 2)) };
        _camera.SetBounds(Iso.ProjectBounds(new Rect2(0, 0, LevelW, LevelH)));
        AddChild(_camera);
        _camera.MakeCurrent();
    }

    private void SetupReturnZone()
    {
        // [T61] 下水道：返回区＝那口检修竖井的井底（唯一的出口）。默认那个"地图正下方"的位置在下水道里是**实心墙**。
        // [警察局] 返回区＝门厅内的入口（默认那个"地图正下方"落点在警察局的门厅之外＝实心墙）。
        Vector2 pos = DestinationName == ExplorationCache.SewerName
            ? new Vector2(ExplorationWalls.SewerEntry.X - 40f, ExplorationWalls.SewerEntry.Y - 40f)
            : DestinationName == ExplorationCache.PoliceStationName
                ? new Vector2(ExplorationWalls.PoliceEntry.X - 40f, ExplorationWalls.PoliceEntry.Y - 40f)
                : new Vector2(LevelW / 2 - 40, LevelH - 120);

        AddIsoPolygon(Quad(pos, new Vector2(80, 80)), new Color(0.2f, 0.8f, 0.2f, 0.5f), 10);

        var arrow = new Polygon2D
        {
            Polygon = new Vector2[]
            {
                new(40, -50), new(60, -20), new(48, -20),
                new(48, 0), new(32, 0), new(32, -20),
                new(20, -20),
            },
            Color = new Color(0.3f, 0.9f, 0.3f),
            Position = Iso.Project(pos + new Vector2(40f, 40f)),
            ZIndex = 11,
        };
        _isoLayer.AddChild(arrow);

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
        // [警察局] 入口在门厅（x[300,720]）—— 默认 x=200 那个落点在门厅之外＝墙里；改从门厅内横排落队。
        bool police = DestinationName == ExplorationCache.PoliceStationName;
        Vector2 spawn = sewer
            ? new Vector2(ExplorationWalls.SewerEntry.X - 10f, ExplorationWalls.SewerEntry.Y - 20f)
            : police
                ? new Vector2(ExplorationWalls.PoliceEntry.X - 60f, ExplorationWalls.PoliceEntry.Y)
                : new Vector2(200, LevelH - 200);
        float stepX = sewer ? 0f : 40f;
        float stepY = sewer ? -34f : 0f;
        for (int i = 0; i < ExpeditionTeam.Count; i++)
        {
            Pawn p = ExpeditionTeam[i];
            p.Position = spawn + new Vector2(i * stepX, i * stepY);
            p.Reparent(_actorLayer, keepGlobalTransform: false);
            p.SetPresentationLayer(_isoLayer);
        }

        // 随队布鲁斯（若带上）：放在队伍旁、reparent 进关卡 actor 层。跟随道格/自主缠斗（关内敌对经 CampMain
        // 的敌对 provider 读 LevelHostiles）由其既有 AI 驱动；战斗引擎已在营地 Inject 过（跨关卡复用同一实例）。
        if (CompanionDog is { } dog)
        {
            dog.Position = spawn + new Vector2(ExpeditionTeam.Count * stepX, ExpeditionTeam.Count * stepY);
            dog.Reparent(_actorLayer, keepGlobalTransform: false);
            dog.SetPresentationLayer(_isoLayer);
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
    /// 本关存活敌对单位＝存活丧尸 + 关内敌对幸存者（超市骗局伏击的据点幸存者 / <b>金手指帮的 4 名守备</b>）——
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
        // 🔴 布点被硬不变量钉死（SewerTests：**任何位置最多被 1 只感知**）——依据 sim-lanchester（2026-07-17 全仓重跑值）：
        //    1 只 100%、2 只 84.5%（但已要付 1.14 处永久残缺）、**3 只 22.0%** ⇒ **围攻是断崖**（拐点在 2→3）。
        //    "最多被 1 只感知"这条护栏**不因真值放宽而松动**：1v1 才是唯一无伤的档。
        //    这地方要的是"吓人"，不是"危险"，两者必须焊开。
        //    **要挪/要加，先跑 SewerTests。**
        if (DestinationName == ExplorationCache.SewerName)
        {
            var sewerSpots = new List<Vector2>(ExplorationWalls.SewerZombieSpots.Count);
            foreach ((float x, float y) in ExplorationWalls.SewerZombieSpots)
                sewerSpots.Add(new Vector2(x, y));
            SpawnZombiesAt(sewerSpots);
            return;
        }

        // [警察局] Medium：**4 只，各藏一间房的深角**（比下水道低危的 3 只多一只）。
        // 🔴 布点被硬不变量钉死（PoliceStationTests：**任一可行走点最多被 1 只感知**）——「Medium」是总量更多，
        //    不是同时更多（2 只 84.5% 但付 1.14 处永久残缺、**3 只 22.0%＝断崖**；lanchester.md·2026-07-17 全仓重跑值）。
        //    「室内多拐角」靠房间门洞遮挡，不靠人海。**要挪/要加先跑 PoliceStationTests。**
        if (DestinationName == ExplorationCache.PoliceStationName)
        {
            // [SPEC-T60] 拘留区那只（守着两件甲）＝门后特殊丧尸：冻结在拘留区铁门后，撬开铁门才唤醒。
            //   另 3 间无门开放侧房的那 3 只＝普通丧尸（靠近/视野唤醒）。
            foreach ((float x, float y) in ExplorationWalls.PoliceZombieSpots)
            {
                bool locked = ExplorationWalls.PoliceSpotBehindHoldingDoor((x, y));
                Zombie z = SpawnZombieAt(new Vector2(x, y), LevelWanderRect(), doorLocked: locked);
                if (locked)
                    RegisterDoorLocked(z, new[] { ExplorationWalls.PoliceHoldingDoorName });
            }
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
        // 🔴 **不是让你清完的**（2 只 84.5%，**3 只 22.0%**，4 只 1.6%，5 只起 0%；lanchester.md·2026-07-17 全仓重跑值）——**是让你转身跑、并把门关上。**
        if (DestinationName == RuinedChurch.DestinationName)
        {
            // 教堂本体 3 只＝普通丧尸（视野/靠近唤醒）。
            foreach ((float x, float y) in RuinedChurch.ChurchZombieSpots)
                SpawnZombieAt(new Vector2(x, y), LevelWanderRect());
            // [SPEC-T60] 墓地那 12 只＝门后特殊丧尸：冻结在墓地边界两扇门后，推开后院门/北耳门任一即整片唤醒涌来。
            foreach ((float x, float y) in RuinedChurch.GraveyardZombieSpots)
            {
                Zombie z = SpawnZombieAt(new Vector2(x, y), LevelWanderRect(), doorLocked: true);
                RegisterDoorLocked(z, RuinedChurch.GraveyardWakeDoors);
            }
            return;
        }

        // [SPEC-T60] 难民营地：10 只**开门跳脸**（各锁在一间房的门后）+ 4 只过道游荡。
        // 跳脸的那 10 只徘徊区**只有它自己那间房**：它必须待在门后——它就是那扇门的全部意义。
        // ⚠️ Phase2 起它们**不再要求贴门 ≤90px**：唤醒绑门实体、与像素距离无关（贴门/房中央/最深角落都有）。
        if (DestinationName == RefugeeCamp.DestinationName)
        {
            // [SPEC-T60] 每只伏击丧尸＝门后特殊丧尸：冻结在自己那间房的门后，推开那扇房门才唤醒它（一门唤醒一只）。
            //   徘徊区仍限本房（唤醒后在房内/追出门都由导航说了算）。
            foreach (AmbushZombie a in RefugeeCamp.AmbushZombies)
            {
                WallRect room = RefugeeCamp.Room(a.RoomNumber).Rect;
                const float pad = RefugeeCamp.RoomWallThickness + 20f;
                var box = new Rect2(
                    room.X + pad, room.Y + pad,
                    Mathf.Max(1f, room.Width - pad * 2f), Mathf.Max(1f, room.Height - pad * 2f));
                Zombie z = SpawnZombieAt(new Vector2(a.X, a.Y), box, doorLocked: true);
                RegisterDoorLocked(z, new[] { RefugeeCamp.WakeDoorFor(a) });
            }
            // 过道 4 只＝普通丧尸（视野/噪音/靠近唤醒）。
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

        // 默认散布关也从世界图的 enemyCount 读数；旧关卡的专属 authored 布点在上方
        // 各自分支中保留，不把通用档位硬套到医院/教堂/村庄等特殊地图。
        int configuredCount = WorldMapPanel.Graph.Find(DestinationName)?.EnemyCount ?? -1;
        int count = configuredCount >= 0
            ? Mathf.Clamp(configuredCount, 0, spots.Length)
            : spots.Length;
        for (int i = 0; i < count; i++)
        {
            Vector2 spot = spots[i];
            SpawnZombieAt(spot, wander);
        }
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
    // 实例方法（读本关 LevelW/LevelH 实例字段）：画布尺寸 per-destination 后不能再 static。
    private Rect2 LevelWanderRect()
        => new(WallT + 40, WallT + 40, LevelW - WallT * 2 - 80, LevelH - WallT * 2 - 80);




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
    private Zombie SpawnZombieAt(Vector2 pos, Rect2 wander, bool doorLocked = false)
    {
        var z = Zombie.Create(wander, LevelTargets); // 目标池含随队布鲁斯（可被关内丧尸攻击/杀）
        z.Inject(Combat, Clock); // 与营地单位相同的 combat+clock，务必在入树/首个物理帧 Think 前完成
        z.ConfigurePerception(localLightAt: SampleLevelLight); // 固定光源→局部光照喂给（暴露走目标 CarriedLightIntensity 回落）
        z.Position = pos;
        // [SPEC-T60] 探索关丧尸走威胁模型：普通(doorLocked=false，视野/噪音/靠近唤醒) / 门后特殊(true，冻结待其门被开)。
        //   营地丧尸不经此路（走 SpawnCampZombies），保原昼夜休眠零回归。
        z.MarkExploration(doorLocked);
        _actorLayer.AddChild(z);
        _zombies.Add(z);
        z.SetPresentationLayer(_isoLayer);
        return z;
    }

    /// <summary>[SPEC-T60] 把一只门后特殊丧尸登记到它的**唤醒门集**下（推开其一即唤醒它）。</summary>
    private void RegisterDoorLocked(Zombie z, IEnumerable<string> wakeDoors)
    {
        foreach (string door in wakeDoors)
        {
            if (!_doorLockedZombies.TryGetValue(door, out List<Zombie>? list))
                _doorLockedZombies[door] = list = new List<Zombie>();
            list.Add(z);
        }
    }

    /// <summary>[SPEC-T60] 某扇门被打开（推开/撬开）时，唤醒锁在其后的门后特殊丧尸（转为普通丧尸，此后照常感知追击）。</summary>
    private void ActivateDoorLockedZombies(string doorName)
    {
        if (!_doorLockedZombies.TryGetValue(doorName, out List<Zombie>? list))
            return;
        foreach (Zombie z in list)
        {
            if (IsInstanceValid(z))
                z.Activate();
        }
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

        var tag = new Label { Text = label, Position = Iso.Project(rect.Position) + new Vector2(6, -24), ZIndex = 12 };
        tag.AddThemeFontSizeOverride("font_size", 12);
        tag.AddThemeColorOverride("font_color", new Color(0.85f, 0.8f, 0.65f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        _isoLayer.AddChild(tag);
    }







    /// <summary>[SPEC-B13] 占位分区地台：一片半透明方形地台示意某个区域（纯视觉、无碰撞，同瞭望台/广播台占位口径）。</summary>
    private void AddZonePad(Vector2 topLeft, Vector2 size, Color color)
    {
        AddIsoPolygon(Quad(topLeft, size), color, -10);
    }















    /// <summary>铺一处村庄物资搜刮点（发现点式，踏入即经 ExplorationCache.Resolve 落地掉落+叙事）。棕黄标记区别于剧情点。</summary>
    private void AddCachePoint(string cacheId, Vector2 pos, string label)
        => AddDiscoveryPoint(cacheId, pos, markerColor: new Color(0.55f, 0.48f, 0.36f), label: label);

    /// <summary>画一口正式乡村水井（纯视觉、无碰撞）：村中心与庄园地标。</summary>
    private void DrawWellPlaceholder(Vector2 center)
        => AddSiteSpecificProp(center, 15, 145f);


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
            FloatingText.Spawn(_isoLayer, Iso.Project(VillageDoorPosition) + new Vector2(0f, -46f), VillageRescue.BarkText, new Color(0.95f, 0.9f, 0.55f));
            _villageBarkCooldown = VillageRescue.BarkIntervalSeconds;
        }
    }

    /// <summary>造一个发现点：地面标记 + 文字标签 + 触发 Area2D（踏入一次即上报，本关内不重复）。
    /// 标记+标签挂在一个容器 Node2D 下，登记进 <see cref="_discoveryVisuals"/> 供视野检测层隐藏（视野外不揭示）；
    /// 触发 Area2D 独立不受隐藏影响（视野外踏入仍可发现，"看不见但撞上了"）。</summary>
    private void AddDiscoveryPoint(string discoveryId, Vector2 pos, Color markerColor, string label,
        Vector2? zoneSize = null, NarrativeTrigger trigger = NarrativeTrigger.Proximity)
    {
        // 视觉容器（隐藏用）：标记+标签挂其下，隐藏容器即隐藏两者。
        var visual = new Node2D { Position = Iso.Project(pos) };
        _isoLayer.AddChild(visual);

        var mark = new Polygon2D
        {
            Polygon = ProjectRelativeQuad(pos, new Vector2(28, 28)),
            Color = new Color(markerColor.R, markerColor.G, markerColor.B, 0.6f),
            ZIndex = 8,
        };
        visual.AddChild(mark);

        var tag = new Label
        {
            Text = label,
            Position = new Vector2(-16, -40),
            ZIndex = 12,
        };
        tag.AddThemeFontSizeOverride("font_size", 13);
        tag.AddThemeColorOverride("font_color", new Color(0.9f, 0.85f, 0.7f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        visual.AddChild(tag);

        _discoveryVisuals.Add((visual, pos));

        Vector2 triggerSize = zoneSize ?? new Vector2(70, 70);
        _discoveryTargets.Add(new DiscoveryTarget(
            discoveryId,
            label,
            new Rect2(pos - triggerSize / 2f, triggerSize),
            IsRepeatableDiscovery(discoveryId),
            trigger,
            visual));

        if (ExplorationInteractionLogic.TriggersOnBodyEntered(trigger))
        {
            var zone = new Area2D { Position = pos };
            zone.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = triggerSize } });
            zone.CollisionMask = 0b0001u; // 与返回区一致：只感知玩家 Pawn 所在层
            zone.BodyEntered += body =>
            {
                // 踏进去的那个人要透传出去——物资搜刮点是**他**站在那儿一件件往外掏（逐件搜刮，见 LootSession）。
                if (body is Pawn finder)
                    TriggerDiscovery(discoveryId, finder);
            };
            AddChild(zone);
        }
    }

    /// <summary>
    /// 在<b>关内</b>落一具可搜刮的尸体（<c>CampMain.SpawnLevelCorpse</c> 在敌人倒下时调）——
    /// 「探索关杀敌也要落尸、也要能搜刮」（用户拍板）。
    ///
    /// <para><b>坐标系</b>：探索关玩法仍是<b>平面 cartesian</b>，尸体显示则与全关统一走 faux-iso 投影。
    /// 所以尸体点直接落在死者<b>咽气的那个点</b>上——<paramref name="worldPos"/> 是全局坐标，这里 <c>ToLocal</c>
    /// 换算成关内坐标。营地那套「尸体格 / 不堆叠推挤 / iso 投影」（<see cref="CorpseYard"/>）在这儿一概不适用，
    /// 营地的尸体格仍不硬搬：关内尸体保留原地搜刮语义，只共享显示投影。</para>
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
        var visual = new Node2D { Position = Iso.Project(pos) };
        _isoLayer.AddChild(visual);

        var mark = new Polygon2D
        {
            Polygon = ProjectRelativeQuad(pos, new Vector2(24, 24)),
            Color = new Color(0.45f, 0.16f, 0.16f, 0.65f),   // 暗血红：一具躺着的尸体（区别于物资棕黄/叙事冷青）
            ZIndex = 7,                                       // 压在地面之上、人形(10)之下——人从尸体上走过去
        };
        visual.AddChild(mark);

        var tag = new Label
        {
            Text = containerName,   // 「据点幸存者的尸体 #1」——玩家一眼看见这儿躺着谁
            Position = new Vector2(-16, -38),
            ZIndex = 12,
        };
        tag.AddThemeFontSizeOverride("font_size", 12);
        tag.AddThemeColorOverride("font_color", new Color(0.88f, 0.72f, 0.68f));
        tag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        tag.AddThemeConstantOverride("outline_size", 3);
        visual.AddChild(tag);

        _discoveryVisuals.Add((visual, pos));

        Vector2 triggerSize = new(56, 56);
        _discoveryTargets.Add(new DiscoveryTarget(
            containerName,
            containerName,
            new Rect2(pos - triggerSize / 2f, triggerSize),
            Repeatable: true,
            NarrativeTrigger.Proximity,
            visual));

        var zone = new Area2D { Position = pos };
        zone.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = triggerSize } });
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
    /// 触发式：Proximity 连接 Area2D.BodyEntered；Click 不连接踏入事件，只进入右键命中表，角色寻路到位后触发。
    /// 两者共用标记、视野揭示、去重与 OnDiscovery 解析，但输入语义互不混淆。
    /// </summary>
    private void SetupNarrativeSpots()
    {
        foreach (NarrativeSpot spot in NarrativeSpotRegistry.ForDestination(DestinationName))
        {
            AddDiscoveryPoint(
                spot.Id,
                new Vector2(spot.X, spot.Y),
                markerColor: new Color(0.40f, 0.62f, 0.72f), // 冷青：叙事调查点（区别于物资棕黄/剧情红）
                label: spot.Label,
                trigger: spot.Trigger);
        }
    }

    /// <summary>把 cartesian 多边形投影后挂到探索显示层。只改表现，不参与碰撞/导航。</summary>
    private Polygon2D AddIsoPolygon(Vector2[] cartPolygon, Color color, int zIndex)
    {
        Vector2 anchor = cartPolygon.OrderByDescending(p => p.X + p.Y).First();
        Vector2 projectedAnchor = Iso.Project(anchor);
        var polygon = new Polygon2D
        {
            Position = projectedAnchor,
            Polygon = cartPolygon.Select(p => Iso.Project(p) - projectedAnchor).ToArray(),
            Color = color,
            // 地台固定在人形下方；交互标记可显式放在上层；其余世界物件交给 YSort。
            ZIndex = zIndex <= -10 || zIndex >= 7 ? zIndex : 0,
        };
        _isoLayer.AddChild(polygon);
        return polygon;
    }

    /// <summary>把一块 cartesian footprint 画成 faux-iso 平台/立体块。</summary>
    private void AddIsoSurface(Rect2 rect, Color color, int zIndex, float cell = 96f)
    {
        _isoLayer.AddChild(new IsoTilePanel
        {
            FootprintCart = rect,
            Style = new PixelStyle { color = new double[] { color.R, color.G, color.B }, jitter = 0.035 },
            Seed = 101,
            Cell = cell,
            Height = 0f,
            Facade = false,
            ZIndex = zIndex,
        });
    }

    /// <summary>把一块 cartesian footprint 画成 faux-iso 立体块，并切片参与脚点 YSort。</summary>
    private void AddIsoBlock(Rect2 rect, Color color, int zIndex, float height = 20f, bool facade = true, float cell = 48f)
    {
        int nx = Mathf.Max(1, Mathf.CeilToInt(rect.Size.X / cell));
        int ny = Mathf.Max(1, Mathf.CeilToInt(rect.Size.Y / cell));
        float width = rect.Size.X / nx;
        float depth = rect.Size.Y / ny;
        int worldZ = zIndex <= -10 ? zIndex : 0;

        for (int y = 0; y < ny; y++)
        {
            for (int x = 0; x < nx; x++)
            {
                var sub = new Rect2(rect.Position + new Vector2(x * width, y * depth), new Vector2(width, depth));
                _isoLayer.AddChild(new IsoTilePanel
                {
                    FootprintCart = sub,
                    Style = new PixelStyle { color = new double[] { color.R, color.G, color.B }, jitter = 0.025 },
                    Seed = Mathf.RoundToInt(rect.Position.X * 0.01f + rect.Position.Y * 0.03f) + x * 7 + y * 13,
                    Cell = Mathf.Min(cell, 48f),
                    Height = height,
                    Facade = facade,
                    ZIndex = worldZ,
                });
            }
        }
    }

    /// <summary>以 cartesian 中心为锚，把一块小矩形投影成容器内局部坐标。</summary>
    private static Vector2[] ProjectRelativeQuad(Vector2 center, Vector2 size)
    {
        Vector2 origin = center - size / 2f;
        Vector2 anchor = Iso.Project(center);
        return Quad(origin, size).Select(p => Iso.Project(p) - anchor).ToArray();
    }

    private static Vector2[] Quad(Vector2 pos, Vector2 size) => new[]
    {
        pos,
        new Vector2(pos.X + size.X, pos.Y),
        pos + size,
        new Vector2(pos.X, pos.Y + size.Y),
    };
}
