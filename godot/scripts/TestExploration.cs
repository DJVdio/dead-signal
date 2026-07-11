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

    private CameraController _camera = null!;
    private readonly List<Zombie> _zombies = new();
    private readonly Dictionary<Actor, Node2D> _markers = new();
    private readonly List<Rect2> _obstructions = new();
    private Area2D _returnZone = null!;
    private Node2D _actorLayer = null!;

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
            var z = Zombie.Create(wander, () => ExpeditionTeam.Where(a => a.Alive).Cast<Actor>());
            z.Inject(Combat, Clock); // 与营地单位相同的 combat+clock，务必在入树/首个物理帧 Think 前完成
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

    /// <summary>造一个发现点：地面标记 + 文字标签 + 触发 Area2D（踏入一次即上报，本关内不重复）。</summary>
    private void AddDiscoveryPoint(string discoveryId, Vector2 pos, Color markerColor, string label)
    {
        var mark = new Polygon2D
        {
            Polygon = Quad(new Vector2(-14, -14), new Vector2(28, 28)),
            Color = new Color(markerColor.R, markerColor.G, markerColor.B, 0.6f),
            Position = pos,
            ZIndex = 8,
        };
        AddChild(mark);

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
        AddChild(tag);

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
