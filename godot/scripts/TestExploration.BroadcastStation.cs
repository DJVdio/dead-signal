using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// 广播台（「dead signal」主线中后期探索点，用户 [SPEC-B8] 拍板）：在此**定点**取得「发出设备」（非随机，落实 D4 主线关键物资保底）。
    /// <para>
    /// [SPEC-T60·中图放大] 画布放大到 3200×2200（中·≈3天量级，见 <see cref="ExplorationLevelSize"/> 覆盖行），
    /// 从纯占位地台升级为**真广播站结构**：主体<b>播音楼</b>（含北端<b>机房</b>大厅 + 沿中央脊廊分布的播音室/办公室/资料室/食堂等房间）、
    /// 院内五座**外屋**（更衣室/储藏间/发电机房/备件仓库/屋顶天线基座）、以及东北角的<b>天线塔</b>基座剪影。
    /// 墙体一律走 <see cref="AddSolidWall"/>（挡路 + 断寻路 + 挡视线三用），房间轮廓复用 <see cref="ExplorationWalls.RoomOutlineWalls"/>；
    /// 玩家自南侧院子入场，须穿过院子外屋 + 播音楼脊廊才够得到北端机房的发射机 —— 步行/搜刮量按 3 天量级铺开（数值拟定待调）。
    /// </para>
    /// <para>
    /// 🔴 authored 调查点（播音台 / 走廊照片墙，<see cref="NarrativeSpot"/>）文本与坐标一字不动：播音室仍框住 (900,500)、照片墙仍框住 (1500,700)、
    /// 发射机仍钉在 <see cref="BroadcastTransmitterPosition"/>(1200,300)。本次只放大机制层的地形/物资/点位分布，不碰任何 authored 叙事。
    /// </para>
    /// 取设备/推进状态/叙事接线由 <c>CampMain.OnExplorationDiscovery</c> 的挂点补齐（<see cref="RadioMainline.GrantTransmitter"/> + 取设备叙事）。
    /// 十处普通物资搜刮点接 <see cref="ExplorationCache"/>。占位美术：机房/院子地台 + 天线塔基座剪影 + 发射机标记；正式关卡空间/美术待后续。
    /// </summary>
    private void SetupBroadcastStation()
    {
        var wallC = new Color(0.30f, 0.30f, 0.33f, 0.95f);   // 混凝土/砖墙
        var yardC = new Color(0.19f, 0.20f, 0.18f, 0.55f);   // 院子地台
        var floorC = new Color(0.21f, 0.22f, 0.25f, 0.72f);  // 室内地台

        // ——院子地台（纯视觉，示意站区外的开阔地/停车场；玩家自南侧入场）——
        AddZonePad(new Vector2(60, 60), new Vector2(3080, 2080), yardC);

        // ═══════════════ 播音楼（主体建筑，x[600,1884] y[160,1460]，南墙留脊廊入口）═══════════════
        //   北端＝机房大厅（发射机在此）；南墙缺口 x[1150,1280] 正对中央脊廊，脊廊两侧挂各房间。
        const float bLeft = 600f, bRight = 1884f, bTop = 160f, bBot = 1460f;
        const float spineL = 1150f, spineR = 1280f;   // 中央脊廊（开阔，不砌墙）
        AddZonePad(new Vector2(bLeft, bTop), new Vector2(bRight - bLeft, bBot - bTop), floorC);

        const float ew = 16f; // 外墙厚
        AddSolidWall(new WallRect(bLeft, bTop, bRight - bLeft, ew), wallC, zIndex: -5);            // 北墙
        AddSolidWall(new WallRect(bLeft, bTop, ew, bBot - bTop), wallC, zIndex: -5);               // 西墙
        AddSolidWall(new WallRect(bRight - ew, bTop, ew, bBot - bTop), wallC, zIndex: -5);         // 东墙
        // 南墙：中间留脊廊入口缺口 x[spineL,spineR]
        AddSolidWall(new WallRect(bLeft, bBot - ew, spineL - bLeft, ew), wallC, zIndex: -5);       // 南墙·西段
        AddSolidWall(new WallRect(spineR, bBot - ew, bRight - spineR, ew), wallC, zIndex: -5);     // 南墙·东段

        // —— 机房大厅（北端横贯：发射机 + 机架间；门朝南开向脊廊）——
        BuildBroadcastRoom(new Rect2(bLeft, 180f, bRight - bLeft, 300f), wallC, RoomEdge.Bottom);

        // —— 脊廊西侧房间（门朝右／开向脊廊）——
        BuildBroadcastRoom(new Rect2(620f, 486f, spineL - 620f, 300f), wallC, RoomEdge.Right);   // 播音室（框住 播音台 900,500，顶墙下移让 y=500 落房内）
        BuildBroadcastRoom(new Rect2(620f, 810f, spineL - 620f, 210f), wallC, RoomEdge.Right);   // 台长办公室
        BuildBroadcastRoom(new Rect2(620f, 1050f, spineL - 620f, 380f), wallC, RoomEdge.Right);  // 值班室茶水间

        // —— 脊廊东侧房间（门朝左／开向脊廊）——
        BuildBroadcastRoom(new Rect2(spineR, 560f, 1810f - spineR, 340f), wallC, RoomEdge.Left);   // 照片墙走廊房（框住 照片墙 1500,700）
        BuildBroadcastRoom(new Rect2(spineR, 930f, 1810f - spineR, 250f), wallC, RoomEdge.Left);   // 资料室
        BuildBroadcastRoom(new Rect2(spineR, 1210f, 1810f - spineR, 220f), wallC, RoomEdge.Left);  // 食堂后厨

        // ═══════════════ 院内外屋（freestanding，散在院子里逼玩家绕远）═══════════════
        BuildBroadcastRoom(new Rect2(240f, 900f, 320f, 260f), wallC, RoomEdge.Right);    // 员工更衣室（西）
        BuildBroadcastRoom(new Rect2(240f, 1250f, 320f, 280f), wallC, RoomEdge.Top);     // 发电机房（西南）
        BuildBroadcastRoom(new Rect2(1980f, 900f, 340f, 260f), wallC, RoomEdge.Left);    // 杂物储藏间（东）
        BuildBroadcastRoom(new Rect2(2200f, 420f, 340f, 280f), wallC, RoomEdge.Bottom);  // 屋顶天线基座（东北，天线塔下）
        BuildBroadcastRoom(new Rect2(2500f, 1300f, 360f, 320f), wallC, RoomEdge.Left);   // 备件仓库（东南最深角）
        // —— [SPEC-T60·补物资] 再铺 6 座外屋/侧间，把中图物资抬到 16 处（band 10~30 内，站区随纵深铺开）——
        BuildBroadcastRoom(new Rect2(300f, 1650f, 260f, 200f), wallC, RoomEdge.Right);    // 门卫室（南入口旁·先遇到）
        BuildBroadcastRoom(new Rect2(240f, 540f, 280f, 240f), wallC, RoomEdge.Right);     // 锅炉房（西·管道碎铁）
        BuildBroadcastRoom(new Rect2(1980f, 420f, 220f, 220f), wallC, RoomEdge.Left);     // 控制室（东北·发射控制台）
        BuildBroadcastRoom(new Rect2(2600f, 420f, 260f, 240f), wallC, RoomEdge.Left);     // 天线机房（东北·调谐间，天线塔旁）
        BuildBroadcastRoom(new Rect2(1900f, 1250f, 260f, 250f), wallC, RoomEdge.Left);    // 磁带库（东·成架磁带/遮尘布）
        BuildBroadcastRoom(new Rect2(2000f, 1750f, 360f, 230f), wallC, RoomEdge.Left);    // 车库（东南·台里的车与备用油）

        // ═══════════════ 天线塔（东北角·纯视觉：塔基剪影 + 拉线示意）═══════════════
        AddZonePad(new Vector2(2360f, 200f), new Vector2(320f, 320f), new Color(0.17f, 0.18f, 0.20f, 0.6f));
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(2480f, 240f), new Vector2(80f, 220f)),
            Color = new Color(0.34f, 0.30f, 0.24f, 0.9f),
            ZIndex = 6,
        });

        // ═══════════════ 机房占位美术：发射机地台 + 塔基剪影（原口径保留，锚点不动）═══════════════
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(BroadcastTransmitterPosition.X - 200f, BroadcastTransmitterPosition.Y - 120f), new Vector2(400f, 260f)),
            Color = new Color(0.20f, 0.21f, 0.24f, 0.85f),
            ZIndex = 5,
        });
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(BroadcastTransmitterPosition.X - 34f, BroadcastTransmitterPosition.Y - 110f), new Vector2(68f, 56f)),
            Color = new Color(0.34f, 0.30f, 0.24f, 0.9f),
            ZIndex = 6,
        });

        // ═══════════════ 发射机可交互占位（踏入发现区即上报 transmitter id → 推进主线）═══════════════
        AddDiscoveryPoint(
            BroadcastTransmitterDiscoveryId,
            BroadcastTransmitterPosition,
            markerColor: new Color(0.40f, 0.65f, 0.55f),
            label: "发射机");

        // ═══════════════ 十处普通物资搜刮点（接 ExplorationCache；沿机房→脊廊→院内外屋铺开，近→深）═══════════════
        var lootC = new Color(0.5f, 0.48f, 0.44f);
        // 机房大厅内
        AddDiscoveryPoint(ExplorationCache.BroadcastServerRackId, new Vector2(880, 320), markerColor: lootC, label: "机架间");
        // 播音楼脊廊·西侧
        AddDiscoveryPoint(ExplorationCache.BroadcastOfficeId, new Vector2(870, 900), markerColor: lootC, label: "台长办公室");
        AddDiscoveryPoint(ExplorationCache.BroadcastBreakRoomId, new Vector2(870, 1230), markerColor: new Color(0.55f, 0.5f, 0.4f), label: "值班室茶水间");
        // 播音楼脊廊·东侧
        AddDiscoveryPoint(ExplorationCache.BroadcastArchiveId, new Vector2(1540, 1045), markerColor: lootC, label: "资料室");
        AddDiscoveryPoint(ExplorationCache.BroadcastCanteenId, new Vector2(1540, 1310), markerColor: lootC, label: "食堂后厨");
        // 院内外屋
        AddDiscoveryPoint(ExplorationCache.BroadcastLockersId, new Vector2(400, 1030), markerColor: lootC, label: "员工更衣室");
        AddDiscoveryPoint(ExplorationCache.BroadcastGeneratorId, new Vector2(400, 1380), markerColor: lootC, label: "发电机房");
        AddDiscoveryPoint(ExplorationCache.BroadcastStoreroomId, new Vector2(2150, 1030), markerColor: lootC, label: "杂物储藏间");
        AddDiscoveryPoint(ExplorationCache.BroadcastRoofAntennaId, new Vector2(2370, 560), markerColor: lootC, label: "屋顶天线基座");
        AddDiscoveryPoint(ExplorationCache.BroadcastPartsStoreId, new Vector2(2680, 1460), markerColor: lootC, label: "备件仓库");
        // [SPEC-T60·补物资] 6 处新点（与 ExplorationCache 广播台段登记一一对应）
        AddDiscoveryPoint(ExplorationCache.BroadcastGuardBoothId, new Vector2(430, 1750), markerColor: lootC, label: "门卫室");
        AddDiscoveryPoint(ExplorationCache.BroadcastBoilerRoomId, new Vector2(380, 660), markerColor: lootC, label: "锅炉房");
        AddDiscoveryPoint(ExplorationCache.BroadcastControlRoomId, new Vector2(2090, 530), markerColor: lootC, label: "控制室");
        AddDiscoveryPoint(ExplorationCache.BroadcastAntennaShedId, new Vector2(2730, 540), markerColor: lootC, label: "天线机房");
        AddDiscoveryPoint(ExplorationCache.BroadcastTapeLibraryId, new Vector2(2030, 1375), markerColor: lootC, label: "磁带库");
        AddDiscoveryPoint(ExplorationCache.BroadcastGarageId, new Vector2(2180, 1865), markerColor: lootC, label: "车库");
    }

    /// <summary>广播站的一间占位房间：四条细墙轮廓（复用 <see cref="ExplorationWalls.RoomOutlineWalls"/>），<paramref name="doorEdges"/> 上各留一处门洞。</summary>
    private void BuildBroadcastRoom(Rect2 rect, Color color, params RoomEdge[] doorEdges)
    {
        var room = new WallRect(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
        foreach (WallRect w in ExplorationWalls.RoomOutlineWalls(room, doorEdges))
            AddSolidWall(w, color, zIndex: 6);
    }
}
