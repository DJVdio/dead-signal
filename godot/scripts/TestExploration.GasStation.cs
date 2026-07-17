using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>[SPEC-B13·拟设定待确认] 加油站游荡丧尸布点（中低 5 只，band 4~6）：散在便利店/修车棚/油罐区，远离南侧加油区入口，数量/布点拟定待调（归 param-calibration）。
    /// [放大·中3天] 画布 3200×2200 后随分区重排，最北一只守着地下储油间（高价值）。</summary>
    private static readonly Vector2[] GasStationZombieSpots =
    {
        new(750f, 1400f),  // 便利店·里屋附近
        new(2400f, 1450f), // 修车棚·工位
        new(2550f, 1350f), // 修车棚·零件区
        new(1400f, 900f),  // 油罐区·油罐车旁
        new(1600f, 530f),  // 地下储油间深处（守着高价值燃油）
    };

    /// <summary>
    /// [SPEC-B13·拟设定待确认] 加油站：公路加油站——加油区 + 便利店 + 修车棚 + 油罐区（地下储油间）。
    /// **燃油大户**（fuel 为火堆/油灯燃料的主要产出来源，呼应"固定光源耗燃油"点子；投放量拟定待调），
    /// 便利店食品少量 + 修车棚工具零件；中点下限 10 处搜刮，近→深：加油区(南/近) → 便利店(中) → 修车棚(中) → 油罐区(北/深·高价值)。
    ///
    /// <para>
    /// [放大·中3天·拟定待调] 画布覆盖 3200×2200（见 <see cref="ExplorationLevelSize.Overrides"/>）后，把四个分区在纵深上拉开：
    /// 南侧加油罩棚(近) → 西侧便利店 / 东侧修车棚两座**带门建筑**（<see cref="AddRoomOutline"/>，进店要走门洞＝绕路+遮挡）→
    /// 最北的**地下储油间**（带门深房，燃油大户的高价值终点，最深一只丧尸守着）。前场散着抛锚车/油桶垛（<see cref="AddSolidWall"/> 实体）
    /// 逼玩家从入口蛇形绕到深区。步行/搜刮工作量随纵深与绕路抬到"≈3天量级"（数值拟定待调，精调归 param-calibration；
    /// 点位<b>数量</b>维持 10 处、fuel 多点身份不变＝叙事 draft 归用户，不新增点位）。
    /// </para>
    /// 占位美术：四片分区地台 + 建筑墙体 + 搜刮点标记（正式空间/美术待后续）；掉落/叙事在 <see cref="ExplorationCache.Resolve"/>。
    /// 敌对布防见 <see cref="GasStationZombieSpots"/>（中低 5 只）；叙事调查点（公路车龙/立柱油价牌）见 NarrativeSpotRegistry。
    /// </summary>
    private void SetupGasStation()
    {
        // 分区占位地台（纯视觉）：加油区(南/罩棚)、便利店(西/暖光)、修车棚(东/油污)、油罐区(北/警戒黄)。
        AddZonePad(new Vector2(1150, 1780), new Vector2(900, 340), new Color(0.22f, 0.23f, 0.25f, 0.6f));   // 加油区（罩棚）
        AddZonePad(new Vector2(450, 1250), new Vector2(650, 420), new Color(0.30f, 0.28f, 0.22f, 0.55f));   // 便利店
        AddZonePad(new Vector2(2100, 1250), new Vector2(700, 420), new Color(0.24f, 0.23f, 0.20f, 0.6f));   // 修车棚
        AddZonePad(new Vector2(1150, 320), new Vector2(1000, 420), new Color(0.34f, 0.30f, 0.14f, 0.55f));  // 油罐区

        var near = new Color(0.5f, 0.52f, 0.5f);
        var storeC = new Color(0.55f, 0.5f, 0.4f);
        var repairC = new Color(0.5f, 0.48f, 0.44f);
        var fuelC = new Color(0.62f, 0.56f, 0.30f); // 燃油区偏暖黄，凸显燃油大户身份
        var carC = new Color(0.28f, 0.26f, 0.24f, 0.95f); // 抛锚车/油桶垛（实体·挡路+断视线）

        // 前场障碍（实体）：抛锚车横在加油区、油桶垛压在油罐区入口 ⇒ 玩家从南入口须蛇形绕过才能上深区。
        AddSolidWall(new WallRect(1200f, 1600f, 240f, 60f), carC, zIndex: 6);  // 加油区·抛锚车（西）
        AddSolidWall(new WallRect(1700f, 1560f, 240f, 60f), carC, zIndex: 6);  // 加油区·抛锚车（东，与西错开留过道）
        AddSolidWall(new WallRect(1350f, 1140f, 220f, 60f), carC, zIndex: 6);  // 通往油罐区的油桶垛

        // 加油区（南/近）
        AddDiscoveryPoint(ExplorationCache.GasPumpIslandId, new Vector2(1400, 1900), markerColor: fuelC, label: "加油岛");
        AddDiscoveryPoint(ExplorationCache.GasKioskId, new Vector2(1750, 1950), markerColor: near, label: "收银亭");
        // 便利店（西/带门建筑，食品少量）
        AddRoomOutline(new Rect2(450f, 1250f, 650f, 420f), new Color(0.30f, 0.27f, 0.21f, 0.95f), "便利店", RoomEdge.Bottom, RoomEdge.Right);
        AddDiscoveryPoint(ExplorationCache.GasStoreSnacksId, new Vector2(750, 1520), markerColor: storeC, label: "便利店零食货架");
        AddDiscoveryPoint(ExplorationCache.GasStoreDrinksId, new Vector2(950, 1450), markerColor: storeC, label: "冷饮柜");
        AddDiscoveryPoint(ExplorationCache.GasStoreBackroomId, new Vector2(600, 1350), markerColor: storeC, label: "便利店里屋");
        // 修车棚（东/带门建筑，工具零件）
        AddRoomOutline(new Rect2(2100f, 1250f, 700f, 420f), new Color(0.26f, 0.24f, 0.20f, 0.95f), "修车棚", RoomEdge.Bottom, RoomEdge.Left);
        AddDiscoveryPoint(ExplorationCache.GasRepairBayId, new Vector2(2350, 1500), markerColor: repairC, label: "修车工位");
        AddDiscoveryPoint(ExplorationCache.GasPartsShelfId, new Vector2(2600, 1420), markerColor: repairC, label: "零件货架");
        AddDiscoveryPoint(ExplorationCache.GasOilRackId, new Vector2(2500, 1320), markerColor: fuelC, label: "机油货架");
        // 油罐区（北/深，燃油大户高价值；地下储油间＝带门深房，最深一只丧尸守着）
        AddDiscoveryPoint(ExplorationCache.GasTankerId, new Vector2(1400, 950), markerColor: fuelC, label: "油罐车");
        AddRoomOutline(new Rect2(1350f, 350f, 520f, 360f), new Color(0.34f, 0.30f, 0.16f, 0.95f), "地下储油间", RoomEdge.Bottom);
        AddDiscoveryPoint(ExplorationCache.GasUndergroundTankId, new Vector2(1600, 500), markerColor: fuelC, label: "地下储油间");
    }
}
