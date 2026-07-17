using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>[SPEC-B13·拟设定待确认] 东部新村游荡丧尸布点（中等 7 只，band 6~8）：散在工地/老屋分区之间，远离南侧排屋入口，数量/布点拟定待调（归 param-calibration）。
    /// [放大·中3天] 画布 3200×2200 后随三分区纵深重排，最深两只守老屋区（含高价值药箱）。</summary>
    private static readonly Vector2[] EastNewVillageZombieSpots =
    {
        new(900f, 1250f),  // 工地·料场附近
        new(1400f, 1300f), // 工地·脚手架下
        new(2050f, 1350f), // 工地·工具棚附近
        new(1150f, 1000f), // 工地·钢筋料区（偏北）
        new(900f, 700f),   // 老屋区西（守老屋一号）
        new(1650f, 560f),  // 老屋区中（守老屋二号）
        new(2550f, 620f),  // 老屋药箱深处（守着高价值柜）
    };

    /// <summary>
    /// [SPEC-B13-补3·拟设定待确认] 东部新村（正名，内部路由键「住宅区」）：末日前在建的迁建安置区——半建成铁皮排屋 + 工地料场 + 已入住的几户老屋。
    /// 用户拍板"物资种类分散、量小，住宅区物资不单一不集中"→**30 处·杂而薄**（每点 1~2 件、品类混杂），戒掉"建材大户"单一身份。三分区近→深，一户户翻：
    ///   排屋区(南/近，11·每户厨房/衣柜/床底/阳台各一小点) → 工地区(中，8·维持偏建材) → 老屋区(北/深，11·含最深药箱)。
    ///
    /// <para>
    /// [放大·中3天·拟定待调] 画布覆盖 3200×2200（见 <see cref="ExplorationLevelSize.Overrides"/>）后，把三分区在纵深上拉满：
    /// 南排是**一排带门排屋**（<see cref="AddRoomOutline"/> 一户一屋、门朝街＝"一户户翻"须逐个进门，制造绕路+遮挡）；
    /// 中段工地散着料垛/脚手架（<see cref="AddSolidWall"/> 实体垛）逼玩家绕行；北深是**两座老宅院**（带门房间，最深一户藏药箱）。
    /// 30 点随纵深铺开＋逐屋进门的步行，把工作量抬到"≈3天量级"（数值拟定待调，精调归 param-calibration；点位<b>数量/杂而薄配比</b>
    /// 维持 30 处不新增＝叙事 draft 归用户）。
    /// </para>
    /// 占位美术：三分区地台 + 排屋/老宅墙体 + 搜刮点标记（正式空间/美术待后续，同瞭望台/广播台占位口径）；掉落/叙事在 <see cref="ExplorationCache.Resolve"/>。
    /// 敌对布防见 <see cref="EastNewVillageZombieSpots"/>（游荡中等 7 只）；叙事调查点（乔迁对联/工地打卡板）见 NarrativeSpotRegistry。
    /// 铺点序＝ExplorationCache.CacheIdsFor(住宅区) 的近→深序；坐标皆拟定待调（点密，间距≥70px 避触发歧义）。
    /// </summary>
    private void SetupEastNewVillage()
    {
        // 分区占位地台（纯视觉）：排屋(南/暖灰)、工地(中/黄褐)、老屋(北/冷褐)。30 点加密后放宽覆盖范围。
        AddZonePad(new Vector2(380, 1680), new Vector2(2200, 440), new Color(0.30f, 0.29f, 0.27f, 0.55f));  // 排屋区
        AddZonePad(new Vector2(560, 960), new Vector2(2200, 640), new Color(0.34f, 0.30f, 0.20f, 0.55f));   // 工地区
        AddZonePad(new Vector2(560, 360), new Vector2(2100, 560), new Color(0.26f, 0.24f, 0.24f, 0.55f));   // 老屋区

        var near = new Color(0.55f, 0.5f, 0.4f);   // 排屋/近入口
        var mid = new Color(0.52f, 0.48f, 0.40f);  // 工地中段
        var deep = new Color(0.5f, 0.46f, 0.42f);  // 老屋深处
        var houseC = new Color(0.30f, 0.28f, 0.25f, 0.95f);  // 排屋墙体
        var oldHouseC = new Color(0.28f, 0.26f, 0.26f, 0.95f); // 老宅墙体（更冷）
        var pileC = new Color(0.32f, 0.29f, 0.22f, 0.95f);   // 工地料垛（实体·挡路+断视线）

        // 排屋区（南/近，11·一排带门排屋，门朝街＝一户户进门翻）
        AddRoomOutline(new Rect2(420f, 1720f, 460f, 340f), houseC, "A户/样板间", RoomEdge.Bottom);
        AddDiscoveryPoint(ExplorationCache.NewVillageShowroomId, new Vector2(520, 1950), markerColor: near, label: "样板间客厅");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowKitchenId, new Vector2(770, 1960), markerColor: near, label: "A户厨房");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowAWardrobeId, new Vector2(560, 1790), markerColor: near, label: "A户衣柜");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowAUnderbedId, new Vector2(780, 1790), markerColor: near, label: "A户床底");
        AddRoomOutline(new Rect2(1000f, 1720f, 420f, 340f), houseC, "B户", RoomEdge.Bottom);
        AddDiscoveryPoint(ExplorationCache.NewVillageRowBKitchenId, new Vector2(1100, 1960), markerColor: near, label: "B户厨房");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowBBalconyId, new Vector2(1100, 1790), markerColor: near, label: "B户阳台");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowBClosetId, new Vector2(1320, 1950), markerColor: near, label: "B户储物间");
        AddDiscoveryPoint(ExplorationCache.NewVillageUnfinishedId, new Vector2(1700, 1950), markerColor: near, label: "半成品单元");
        AddRoomOutline(new Rect2(2000f, 1700f, 520f, 360f), houseC, "C户", RoomEdge.Bottom, RoomEdge.Left);
        AddDiscoveryPoint(ExplorationCache.NewVillageRowCShoeCabId, new Vector2(2120, 1850), markerColor: near, label: "C户玄关鞋柜");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowCBathId, new Vector2(2350, 1960), markerColor: near, label: "C户卫生间");
        AddDiscoveryPoint(ExplorationCache.NewVillageRowDBalconyId, new Vector2(1600, 1720), markerColor: near, label: "D户阳台杂物");

        // 工地区（中，8·维持偏建材；料垛/脚手架实体制造绕路，工具棚/项目部为带门工棚）
        AddSolidWall(new WallRect(780f, 1280f, 180f, 50f), pileC, zIndex: 6);   // 料场木料垛
        AddSolidWall(new WallRect(1300f, 1230f, 180f, 50f), pileC, zIndex: 6);  // 水泥垛
        AddSolidWall(new WallRect(1650f, 1180f, 180f, 50f), pileC, zIndex: 6);  // 钢筋垛
        AddDiscoveryPoint(ExplorationCache.NewVillageLumberYardId, new Vector2(700, 1400), markerColor: mid, label: "料场木料垛");
        AddDiscoveryPoint(ExplorationCache.NewVillageScaffoldId, new Vector2(1150, 1350), markerColor: mid, label: "脚手架下");
        AddRoomOutline(new Rect2(1620f, 1280f, 320f, 280f), pileC, "工具棚", RoomEdge.Bottom);
        AddDiscoveryPoint(ExplorationCache.NewVillageToolShedId, new Vector2(1750, 1400), markerColor: mid, label: "工地工具棚");
        AddDiscoveryPoint(ExplorationCache.NewVillageRebarPileId, new Vector2(900, 1150), markerColor: mid, label: "钢筋碎料堆");
        AddRoomOutline(new Rect2(2020f, 1130f, 340f, 300f), pileC, "项目部工棚", RoomEdge.Bottom, RoomEdge.Left);
        AddDiscoveryPoint(ExplorationCache.NewVillageSiteOfficeId, new Vector2(2150, 1250), markerColor: mid, label: "项目部工棚");
        AddDiscoveryPoint(ExplorationCache.NewVillageCementPileId, new Vector2(1400, 1150), markerColor: mid, label: "水泥料堆");
        AddDiscoveryPoint(ExplorationCache.NewVillageElectricalBoxId, new Vector2(1950, 1100), markerColor: mid, label: "临时配电箱");
        AddDiscoveryPoint(ExplorationCache.NewVillageForemanLockerId, new Vector2(2650, 1000), markerColor: mid, label: "工头储物柜");

        // 老屋区（北/深，11·两座带门老宅院，最深一户藏药箱）
        AddRoomOutline(new Rect2(600f, 520f, 620f, 420f), oldHouseC, "老屋一号", RoomEdge.Bottom, RoomEdge.Right);
        AddDiscoveryPoint(ExplorationCache.NewVillageOldKitchenId, new Vector2(700, 800), markerColor: deep, label: "老屋灶间");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldWardrobeId, new Vector2(700, 600), markerColor: deep, label: "老屋卧室衣柜");
        AddDiscoveryPoint(ExplorationCache.NewVillageRootCellarId, new Vector2(900, 450), markerColor: deep, label: "老屋菜窖");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldHallId, new Vector2(950, 750), markerColor: deep, label: "老屋堂屋");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldUnderbedId, new Vector2(1150, 600), markerColor: deep, label: "老屋床底");
        AddDiscoveryPoint(ExplorationCache.NewVillageOldAtticId, new Vector2(1150, 850), markerColor: deep, label: "老屋阁楼");
        AddRoomOutline(new Rect2(1540f, 400f, 520f, 400f), oldHouseC, "老屋二号", RoomEdge.Bottom, RoomEdge.Left);
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2KitchenId, new Vector2(1650, 700), markerColor: deep, label: "二号老屋厨房");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2WoodshedId, new Vector2(1650, 480), markerColor: deep, label: "二号老屋柴房");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2YardId, new Vector2(1950, 620), markerColor: deep, label: "二号老屋院子");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2ShrineId, new Vector2(2250, 470), markerColor: deep, label: "老屋神龛");
        AddDiscoveryPoint(ExplorationCache.NewVillageOld2MedCabId, new Vector2(2550, 650), markerColor: new Color(0.56f, 0.5f, 0.42f), label: "老屋药箱");
    }
}
