using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// 城市之巅瞭望观景台（前中期调查点，用户口径："用望远镜看到正北方黑压压上百万尸潮向南移动"）。
    /// 骨架＝复用本测试关地形，仅在北缘铺一处**望远镜可交互占位**（发现点式，踏入即上报 <see cref="LookoutTelescopeDiscoveryId"/>）。
    /// 演出/剧情/旗标接线由兄弟系统在 <c>CampMain.OnExplorationDiscovery</c> 的 TODO 挂点补齐（见 <see cref="LookoutTelescopeDiscoveryId"/> 注释）。
    /// 另铺两处同址物资搜刮点（游客服务台/瞭望员值班室，接 loot-story，物资/叙事见 <see cref="ExplorationCache"/>）。
    /// 占位美术：北缘一段观景护栏 + 望远镜标记；正式高层观景台空间布局/美术待后续。
    /// </summary>
    private void SetupCityRooftopLookout()
    {
        // [小图适度放大] 画布 2800×1900（<see cref="ExplorationLevelSize"/>·占位地台图无 authored 固定像素不变量）。
        //   身份＝高层天台观景台：北缘望远镜（瞭望正北尸潮·flag 式）+ 天台机房/瞭望员值班室两栋设备房 + 南侧游客服务楼 +
        //   通风管道/水箱台/设备排（实体挡路制造绕行）。5 搜刮点随纵深从南侧服务区→天台深处铺开，id 不新增（*CacheTests 计数恒绿）。
        //   望远镜锚点 LookoutTelescopePosition 已随画布同步到北缘正中(1400,260)。地台/护栏为占位视觉，坐标拟定待调。
        // —— 天台观景平台占位地台（纯视觉，示意开阔天台面）——
        AddZonePad(new Vector2(300, 360), new Vector2(2200, 640), new Color(0.24f, 0.25f, 0.28f, 0.4f));

        // 占位护栏：贴北墙一段横栏，示意"高层观景台面朝正北"（纯视觉，无碰撞；随望远镜锚点，放大后已居北缘正中）。
        var railing = new Polygon2D
        {
            Polygon = Quad(new Vector2(LookoutTelescopePosition.X - 240f, LookoutTelescopePosition.Y - 34f), new Vector2(480f, 10f)),
            Color = new Color(0.32f, 0.30f, 0.26f, 0.9f),
            ZIndex = 6,
        };
        AddChild(railing);

        // —— 天台机房 + 瞭望员值班室（东北设备房，门朝南＝正对天台开阔面）+ 南侧游客服务楼（近入口，门朝北通天台）——
        AddRoomOutline(new Rect2(1560, 560, 460, 340), new Color(0.28f, 0.30f, 0.32f, 0.95f), "天台机房", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(2120, 380, 340, 300), new Color(0.26f, 0.28f, 0.30f, 0.95f), "瞭望员值班室", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(480, 1120, 420, 320), new Color(0.30f, 0.31f, 0.33f, 0.95f), "游客服务楼", RoomEdge.Top);

        // —— 天台设备（实体挡路，逼绕行）：通风管道 / 水箱台 / 设备排 ——
        var equipC = new Color(0.34f, 0.34f, 0.36f, 0.95f);
        AddSolidWall(new WallRect(1040f, 720f, 60f, 300f), equipC, zIndex: 6);   // 通风管道（竖）
        AddSolidWall(new WallRect(1180f, 1080f, 320f, 60f), equipC, zIndex: 6);  // 水箱台（横）
        AddSolidWall(new WallRect(2000f, 980f, 60f, 340f), equipC, zIndex: 6);   // 设备排（竖）

        // 望远镜可交互占位：踏入发现区即上报 telescope id（挂点见常量注释；anim-lookout 可用同坐标叠演出节点）。
        AddDiscoveryPoint(
            LookoutTelescopeDiscoveryId,
            LookoutTelescopePosition,
            markerColor: new Color(0.30f, 0.55f, 0.70f),
            label: "望远镜");

        // 同址物资搜刮点（接 loot-story HANDOFF；物资/叙事在 ExplorationCache.Resolve，落地走 CampMain.OnExplorationDiscovery→ExplorationCache）：
        //   · 游客服务台（浅/近入口，南侧服务楼，先被遇到）；· 瞭望员值班室（藏深，关内北侧远角，与望远镜同处高空值守区）。
        AddDiscoveryPoint(
            ExplorationCache.LookoutGiftShopId,
            new Vector2(680, 1280),
            markerColor: new Color(0.55f, 0.5f, 0.4f),
            label: "游客服务台");

        // [SPEC-B12] 小点扩至 5：贩卖机/员工储物柜(近中)、天台机房(深)。
        AddDiscoveryPoint(ExplorationCache.LookoutVendingId, new Vector2(1050, 1350), markerColor: new Color(0.5f, 0.48f, 0.42f), label: "自动贩卖机");
        AddDiscoveryPoint(ExplorationCache.LookoutStaffLockerId, new Vector2(1500, 960), markerColor: new Color(0.5f, 0.48f, 0.42f), label: "员工储物柜");
        AddDiscoveryPoint(ExplorationCache.LookoutMachineRoomId, new Vector2(1720, 720), markerColor: new Color(0.5f, 0.48f, 0.42f), label: "天台机房");

        AddDiscoveryPoint(
            ExplorationCache.LookoutWardensRoomId,
            new Vector2(2290, 520),
            markerColor: new Color(0.5f, 0.48f, 0.44f),
            label: "瞭望员值班室");
    }
}
