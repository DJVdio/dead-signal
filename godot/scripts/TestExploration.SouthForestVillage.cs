using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// 村庄区域游荡丧尸数（大点区域危险，锁屋 5 围困之外散布在各分区间，拟定待调）。
    /// [放大·≈5天量级] 画布 4200×2800 后区域近 3 倍，游荡数按放大比例上调 4→9（散布不成潮·多为 1~2 只遭遇；拟定待调）。
    /// </summary>
    private const int VillageWanderZombieCount = 9;

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

        // 村庄区域游荡丧尸：散布在各分区之间（远离南入口，避免刚进关就贴脸），村域大徘徊区（自随画布放大）。
        var villageWander = new Rect2(WallT + 60, WallT + 60, LevelW - WallT * 2 - 120, LevelH - WallT * 2 - 120);
        Vector2[] wanderSpots =
        {
            new(950f, 1900f),   // 民居区
            new(1500f, 2100f),  // 民居—村中心之间
            new(2300f, 1850f),  // 村中心
            new(2750f, 2100f),  // 村中心东
            new(3300f, 1700f),  // 村尾
            new(3550f, 1100f),  // 祠堂/村尾深处
            new(2000f, 780f),   // 后山口
            new(1650f, 1250f),  // 锁屋以南空地
            new(520f, 1450f),   // 河滩
        };
        for (int i = 0; i < VillageWanderZombieCount && i < wanderSpots.Length; i++)
            SpawnZombieAt(wanderSpots[i], villageWander);
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

        // ——空间分区（大点，[SPEC-B11-补]"5天+探索量"）：村口 → 民居区 → 村中心 → 村尾/藏深 → 后山 → 河滩 → 果园，锁屋救援只是其中一区——
        // 搜刮点/叙事在 ExplorationCache，落地走 CampMain.OnExplorationDiscovery→ExplorationCache.Resolve；authored 叙事调查点见 NarrativeSpot（本次未动其文本）。

        // [放大·≈5天量级] 画布 4200×2800：先立村落骨架（真实院墙 + 巷道，逼出蛇形绕路，别一片开阔），
        //   再把 42 处搜刮点沿"村口→民居→村中心→村尾→后山→河滩→果园（NE 远角·最深）"由近及深铺满全图。
        //   搜刮点均落在开阔/巷道地面（不封进院墙）＝可达；院墙只作绕行障碍。坐标/密度拟定待调，实机校准。
        DrawVillageCompounds();

        // ——村口/杂物区（南入口侧，先被遇到；4）：皮卡 / 岗亭 / 废三轮 / 道班房——
        DrawHousePlaceholder(new Vector2(1200f, 2450f), new Vector2(200f, 150f)); // 村口小屋
        AddCachePoint(ExplorationCache.VillageRoadsideCarId, new Vector2(900f, 2480f), "皮卡");
        DrawHousePlaceholder(new Vector2(700f, 2300f), new Vector2(130f, 110f));  // 岗亭
        AddCachePoint(ExplorationCache.VillageGatePostId, new Vector2(700f, 2300f), "岗亭");
        AddCachePoint(ExplorationCache.VillageTrikeId, new Vector2(1150f, 2200f), "废三轮");
        DrawHousePlaceholder(new Vector2(1600f, 2450f), new Vector2(160f, 120f)); // 道班房
        AddCachePoint(ExplorationCache.VillageRoadHutId, new Vector2(1600f, 2450f), "道班房");

        // ——民居区（西/中南，多户人家；11）：厨房 / 米缸 / 衣柜 / 梳妆台 / 菜畦 / 鸡窝 / 储藏 / 阁楼 / 柴垛 / 老井灶间 / 晒场棚——
        AddCachePoint(ExplorationCache.VillageKitchenId, new Vector2(700f, 1900f), "厨房碗柜");
        AddCachePoint(ExplorationCache.VillagePantry2Id, new Vector2(820f, 2010f), "米缸");
        AddCachePoint(ExplorationCache.VillageWardrobeId, new Vector2(1160f, 1950f), "卧室衣柜");
        AddCachePoint(ExplorationCache.VillageBedroom2Id, new Vector2(1260f, 1870f), "梳妆台");
        AddCachePoint(ExplorationCache.VillageCourtyardId, new Vector2(940f, 1680f), "院子菜畦");
        AddCachePoint(ExplorationCache.VillageCoopId, new Vector2(1080f, 1560f), "鸡窝棚");
        AddCachePoint(ExplorationCache.VillageBackRoomId, new Vector2(600f, 1640f), "储藏间");
        AddCachePoint(ExplorationCache.VillageLoftId, new Vector2(520f, 1780f), "阁楼");
        AddCachePoint(ExplorationCache.VillageWoodpileId, new Vector2(770f, 1500f), "柴垛");
        DrawHousePlaceholder(new Vector2(1450f, 1750f), new Vector2(180f, 150f)); // 老井人家
        AddCachePoint(ExplorationCache.VillageOldWellHouseId, new Vector2(1450f, 1750f), "老井人家灶间");
        DrawHousePlaceholder(new Vector2(1560f, 2010f), new Vector2(150f, 120f)); // 晒场杂物棚
        AddCachePoint(ExplorationCache.VillageDryingShedId, new Vector2(1560f, 2010f), "晒场杂物棚");

        // ——村中心（中部，公共设施；8）：小卖部 / 供销社仓 / 候车棚 / 村小 / 水井工具箱 / 铁匠铺 / 邮电所 / 信用社——
        DrawHousePlaceholder(new Vector2(2200f, 1900f), new Vector2(240f, 170f)); // 小卖部
        AddCachePoint(ExplorationCache.VillageShopShelfId, new Vector2(2200f, 1900f), "小卖部");
        DrawHousePlaceholder(new Vector2(2000f, 2060f), new Vector2(180f, 150f)); // 供销社仓
        AddCachePoint(ExplorationCache.VillageCoopStoreId, new Vector2(2000f, 2060f), "供销社仓");
        AddCachePoint(ExplorationCache.VillageBusStopId, new Vector2(2500f, 2160f), "候车棚");
        DrawHousePlaceholder(new Vector2(2620f, 1760f), new Vector2(230f, 180f)); // 村小
        AddCachePoint(ExplorationCache.VillageSchoolId, new Vector2(2620f, 1760f), "村小教室");
        DrawWellPlaceholder(new Vector2(2150f, 1600f));
        AddCachePoint(ExplorationCache.VillageWellToolboxId, new Vector2(2150f, 1600f), "水井工具箱");
        DrawHousePlaceholder(new Vector2(2760f, 2000f), new Vector2(150f, 140f)); // 铁匠铺
        AddCachePoint(ExplorationCache.VillageForgeId, new Vector2(2760f, 2000f), "铁匠铺");
        DrawHousePlaceholder(new Vector2(2400f, 1500f), new Vector2(170f, 140f)); // 邮电所
        AddCachePoint(ExplorationCache.VillagePostOfficeId, new Vector2(2400f, 1500f), "邮电所");
        DrawHousePlaceholder(new Vector2(1950f, 1650f), new Vector2(150f, 130f)); // 信用社
        AddCachePoint(ExplorationCache.VillageCreditCoopId, new Vector2(1950f, 1650f), "信用社铁柜");

        // ——村尾/藏深（东/北远角，难度梯度尾；8）：农具棚 / 谷仓 / 养蜂棚 / 坟场看守屋 / 祠堂 / 卫生所 / 打谷场谷堆 / 石碾房——
        DrawHousePlaceholder(new Vector2(3400f, 2050f), new Vector2(250f, 170f)); // 农具棚
        AddCachePoint(ExplorationCache.VillageToolShedId, new Vector2(3400f, 2050f), "农具棚");
        DrawHousePlaceholder(new Vector2(3150f, 1850f), new Vector2(220f, 180f)); // 谷仓
        AddCachePoint(ExplorationCache.VillageBarnId, new Vector2(3150f, 1850f), "打谷场谷仓");
        AddCachePoint(ExplorationCache.VillageBeehiveId, new Vector2(3650f, 1650f), "养蜂棚");
        DrawHousePlaceholder(new Vector2(3300f, 1150f), new Vector2(150f, 140f));  // 坟场看守屋
        AddCachePoint(ExplorationCache.VillageGraveHutId, new Vector2(3300f, 1150f), "坟场看守屋");
        DrawHousePlaceholder(new Vector2(3600f, 900f), new Vector2(230f, 200f));   // 祠堂
        AddCachePoint(ExplorationCache.VillageShrineId, new Vector2(3600f, 900f), "祠堂");
        DrawHousePlaceholder(new Vector2(3300f, 1450f), new Vector2(210f, 180f));  // 卫生所
        AddCachePoint(ExplorationCache.VillageClinicId, new Vector2(3300f, 1450f), "卫生所");
        AddCachePoint(ExplorationCache.VillageThreshingId, new Vector2(3050f, 1560f), "打谷场谷堆");
        DrawHousePlaceholder(new Vector2(3500f, 1900f), new Vector2(150f, 130f));  // 石碾房
        AddCachePoint(ExplorationCache.VillageStoneMillId, new Vector2(3500f, 1900f), "石碾房");

        // ——后山（最北，藏深；山洞暗格＝医疗深藏奖励；5）：猎人窝棚 / 炭窑 / 山洞暗格 / 采药人石屋 / 兽夹套子——
        DrawHousePlaceholder(new Vector2(1600f, 700f), new Vector2(140f, 120f));   // 猎人窝棚
        AddCachePoint(ExplorationCache.VillageBackhillBlindId, new Vector2(1600f, 700f), "猎人窝棚");
        AddCachePoint(ExplorationCache.VillageBackhillKilnId, new Vector2(2000f, 500f), "炭窑");
        AddCachePoint(ExplorationCache.VillageBackhillCaveId, new Vector2(2450f, 350f), "山洞暗格");
        DrawHousePlaceholder(new Vector2(1750f, 420f), new Vector2(130f, 110f));   // 采药人石屋
        AddCachePoint(ExplorationCache.VillageBackhillHerbHutId, new Vector2(1750f, 420f), "采药人石屋");
        AddCachePoint(ExplorationCache.VillageBackhillSnareId, new Vector2(2250f, 650f), "兽夹套子");

        // ——河滩（最西，沿河带；4）：搁浅小船 / 晒鱼棚 / 抽水泵房 / 水磨坊——
        AddCachePoint(ExplorationCache.VillageRiverbankBoatId, new Vector2(300f, 1600f), "搁浅小船");
        DrawHousePlaceholder(new Vector2(320f, 1300f), new Vector2(130f, 110f));   // 晒鱼棚
        AddCachePoint(ExplorationCache.VillageRiverbankShackId, new Vector2(320f, 1300f), "晒鱼棚");
        DrawHousePlaceholder(new Vector2(300f, 1000f), new Vector2(130f, 120f));   // 抽水泵房
        AddCachePoint(ExplorationCache.VillageRiverbankPumpId, new Vector2(300f, 1000f), "抽水泵房");
        DrawHousePlaceholder(new Vector2(360f, 1950f), new Vector2(140f, 120f));   // 水磨坊
        AddCachePoint(ExplorationCache.VillageWatermillId, new Vector2(360f, 1950f), "水磨坊");

        // ——新分区·果园梯田（NE 远角，最深步行；2）：果窖 / 看园棚——
        DrawHousePlaceholder(new Vector2(3700f, 450f), new Vector2(150f, 120f));   // 果窖
        AddCachePoint(ExplorationCache.VillageOrchardCellarId, new Vector2(3700f, 450f), "果窖");
        DrawHousePlaceholder(new Vector2(3400f, 650f), new Vector2(130f, 110f));   // 看园棚
        AddCachePoint(ExplorationCache.VillageOrchardShedId, new Vector2(3400f, 650f), "看园棚");

        // ——核心区：上锁的屋子（道格布鲁斯被困，西北部）——
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

    /// <summary>
    /// [放大·≈5天量级] 立起村落骨架：几片<b>实体</b>农家院墙（<see cref="AddRoomOutline"/>，四面墙各留门洞）
    /// + 两道带缺口的长院墙（<see cref="AddSolidWall"/>）——把开阔地切成巷道、逼出蛇形绕路（"别一片开阔"），
    /// 把步行工作量往 5 天锚点抬。院墙一律落在搜刮点之间的空地上作绕行障碍、不封住任何搜刮点＝点位恒可达；
    /// 上锁救援屋另见 <see cref="DrawLockedHouse"/>。布局/尺寸拟定待调，实机校准。
    /// </summary>
    private void DrawVillageCompounds()
    {
        var yardColor = new Color(0.31f, 0.28f, 0.23f, 0.95f);

        // 空地上的农家院（障碍·各留门洞，村落质感）——落在搜刮点簇之间的空隙，不压任何点位：
        AddRoomOutline(new Rect2(1850f, 2280f, 300f, 240f), yardColor, "打谷院", RoomEdge.Bottom, RoomEdge.Right);
        AddRoomOutline(new Rect2(1520f, 1330f, 300f, 260f), yardColor, "李家院", RoomEdge.Left, RoomEdge.Bottom);
        AddRoomOutline(new Rect2(2820f, 1240f, 300f, 280f), yardColor, "王家院", RoomEdge.Top, RoomEdge.Left);
        AddRoomOutline(new Rect2(2000f, 920f, 320f, 260f), yardColor, "后山口院", RoomEdge.Bottom, RoomEdge.Right);
        AddRoomOutline(new Rect2(2240f, 2000f, 250f, 210f), yardColor, "供销院", RoomEdge.Top, RoomEdge.Right);
        AddRoomOutline(new Rect2(2600f, 700f, 280f, 240f), yardColor, "后山东院", RoomEdge.Bottom, RoomEdge.Left);

        // 两道带缺口的长院墙，把南侧入口与北侧村落切成巷道（缺口＝唯一通路，funnel）：
        AddSolidWall(new WallRect(950f, 2250f, 520f, 18f), yardColor, zIndex: 4);   // 南横墙·西段
        AddSolidWall(new WallRect(1700f, 2250f, 620f, 18f), yardColor, zIndex: 4);  // 南横墙·东段（中留缺口）
        AddSolidWall(new WallRect(2900f, 1600f, 18f, 420f), yardColor, zIndex: 4);  // 中竖墙·上段
        AddSolidWall(new WallRect(2900f, 2150f, 18f, 400f), yardColor, zIndex: 4);  // 中竖墙·下段（中留缺口）
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
}
