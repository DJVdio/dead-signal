using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    // ================= 联合收割机仓库（中图·放大到 ≈3 天量级）=================
    //
    // 用户拍板：前中期探索点，工业材料为主。放大三步：
    //   ① 画布 3200×2200（ExplorationLevelSize.Overrides，拟定待调）；
    //   ② 真地形：南侧装卸区(入口) → 中央货架/堆垛通道 → 西办公间/东油料库 → 北阁楼(最深)；
    //   ③ 10 处搜刮点在放大空间里铺开（near→deep 梯度不变，工业料为主，食物/医疗仅休息角一处）。
    //
    // 🔴 点数被 ExplorationCacheTests.CacheIdsFor_MapsDestinations 钉死＝10（band 10~30 下限），不增不减；
    //    "3 天量级"靠更大画布(更多步行) + 货架/堆垛制造绕路 + 点位铺开，而非加点。物资量拟定待调。
    //    地形墙无单测覆盖（单测只测纯逻辑 cache id / 完成度），只需编译通过 + 布局合理。

    /// <summary>
    /// 联合收割机仓库：建货架/堆垛/装卸区/通道的真地形 + 10 处搜刮点（发现点式，投放/叙事见 <see cref="ExplorationCache"/>）。
    /// · 工具柜（←《木匠入门》，易得）铺在靠近入口处；· 阁楼铁皮箱（←《进阶木匠技术》，藏深）在最北的阁楼里，难度梯度 draft 拟定待调。
    /// </summary>
    private void SetupHarvesterWarehouseCaches()
    {
        BuildWarehouseTerrain();

        var floorC = new Color(0.48f, 0.44f, 0.36f); // 主库区（棕黄，工业料）
        var loftC = new Color(0.5f, 0.46f, 0.4f);    // 阁楼（更亮，藏深）

        // 近入口（南，装卸区一带，3）
        AddDiscoveryPoint(ExplorationCache.WarehouseToolCabinetId, new Vector2(1200, 1780), markerColor: floorC, label: "工具柜");
        AddDiscoveryPoint(ExplorationCache.WarehouseCombineCabId, new Vector2(2350, 1700), markerColor: floorC, label: "收割机驾驶室");
        AddDiscoveryPoint(ExplorationCache.WarehouseScrapPileId, new Vector2(650, 1720), markerColor: floorC, label: "废铁堆");

        // 中区（货架/堆垛通道之间，5）
        AddDiscoveryPoint(ExplorationCache.WarehouseWorkbenchId, new Vector2(1550, 1380), markerColor: floorC, label: "工作台");
        AddDiscoveryPoint(ExplorationCache.WarehouseBreakCornerId, new Vector2(780, 1150), markerColor: floorC, label: "休息角");
        AddDiscoveryPoint(ExplorationCache.WarehousePartsBinId, new Vector2(2100, 1300), markerColor: floorC, label: "零件料架");
        AddDiscoveryPoint(ExplorationCache.WarehouseFuelDrumId, new Vector2(2650, 950), markerColor: floorC, label: "油料桶区");
        AddDiscoveryPoint(ExplorationCache.WarehouseLumberRackId, new Vector2(1200, 920), markerColor: floorC, label: "木料架");

        // 深处（北，阁楼，2）——阁楼铁皮箱最里头（进阶木匠书）。
        AddDiscoveryPoint(ExplorationCache.WarehouseHayLoftId, new Vector2(2200, 620), markerColor: loftC, label: "草料阁");
        AddDiscoveryPoint(ExplorationCache.WarehouseAtticChestId, new Vector2(1650, 420), markerColor: loftC, label: "阁楼铁皮箱");
    }

    /// <summary>
    /// 仓库真地形：装卸区(南)分区地台 + 三处房间(阁楼/办公间/油料库，占位墙带门洞) + 中央货架/堆垛（实体墙，制造通道绕路）。
    /// <para>货架/堆垛之间留宽通道，不封死从南入口(≈1600,2080)到各点的路；地形不进单测，只在实机成型。</para>
    /// </summary>
    private void BuildWarehouseTerrain()
    {
        var padDock = new Color(0.24f, 0.26f, 0.22f, 0.55f);  // 装卸区（水泥灰绿）
        var padFloor = new Color(0.26f, 0.24f, 0.19f, 0.5f);  // 主库区地台
        var wallC = new Color(0.33f, 0.30f, 0.24f, 0.95f);    // 占位墙
        var rackC = new Color(0.40f, 0.35f, 0.27f);           // 货架/料架（矮实体）
        var stackC = new Color(0.44f, 0.38f, 0.28f);          // 托盘堆垛

        // 分区地台（纯视觉）：南侧装卸区、主库区。
        AddZonePad(new Vector2(1200, 1850), new Vector2(1200, 300), padDock);  // 装卸区（贴南入口）
        AddZonePad(new Vector2(420, 480), new Vector2(2360, 1300), padFloor);  // 主库区

        // 三处房间（占位墙 + 门洞）：北阁楼(深)、西办公间、东油料库。
        AddRoomOutline(new Rect2(1400, 320, 1040, 520), wallC, "阁楼", RoomEdge.Bottom);   // 深处，含草料阁/铁皮箱
        AddRoomOutline(new Rect2(560, 980, 480, 400), wallC, "办公间", RoomEdge.Right);      // 含休息角
        AddRoomOutline(new Rect2(2420, 800, 560, 500), wallC, "油料库", RoomEdge.Left);      // 含油料桶区

        // 中央货架（长条实体，纵向排布 ⇒ 分出通道）：三排，之间与两端留 ≈300px 通道。
        AddSolidWall(new WallRect(1100, 1120, 44, 460), rackC, zIndex: 6);
        AddSolidWall(new WallRect(1560, 1120, 44, 460), rackC, zIndex: 6);
        AddSolidWall(new WallRect(2020, 1120, 44, 460), rackC, zIndex: 6);
        // 横向料架（上中段），与纵向货架交错成 gauntlet。
        AddSolidWall(new WallRect(760, 760, 520, 40), rackC, zIndex: 6);
        AddSolidWall(new WallRect(1720, 760, 520, 40), rackC, zIndex: 6);

        // 托盘堆垛（方块实体，散布主库区中段）——迫使绕行、拉长在关步行。
        (float x, float y)[] stacks =
        {
            (1360, 1500), (1980, 1520), (900, 1440), (2280, 1440), (1620, 980),
        };
        foreach ((float x, float y) in stacks)
            AddSolidWall(new WallRect(x, y, 130, 130), stackC, zIndex: 6);
    }
}
