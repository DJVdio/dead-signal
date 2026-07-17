using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// 河边小屋（前中期探索点，用户拍板）：五处搜刮点（发现点式，踏入即入库+弹环境叙事，投放/叙事见 <see cref="ExplorationCache"/>）。
    /// · 枪柜（← 自制猎枪 + 弹药/箭；原栓动猎枪随该武器删除而撤下，用户拍板改掉自制猎枪填缺口）铺在靠近入口处（近入口＝易得）；· 床底木箱（通用搜刮）位置更深。
    /// 触发链路复用现有 <see cref="AddDiscoveryPoint"/>；掉落解析在 CampMain.OnExplorationDiscovery 走 <see cref="ExplorationCache.Resolve"/>。
    /// <para>
    /// [小图适度放大] 画布 2800×1900（<see cref="ExplorationLevelSize"/>）。身份＝河滩/栈道/杂物：北缘一条河（视觉水面地台）+ 一段沿岸**栈道**（护栏
    /// 逼出绕行动线）+ 一座**河边小屋**（含屋后菜窖棚）+ 岸边**杂物**堆两处（实体挡路制造绕路）。搜刮点随纵深从岸边→屋内→屋后铺开，
    /// 不新增 id（<see cref="ExplorationCache"/> 五处不变 ⇒ *CacheTests 恒绿；小图不必堆到 3 天量级）。地台/护栏为占位视觉，坐标拟定待调。
    /// </para>
    /// </summary>
    private void SetupRiversideCabinCaches()
    {
        // —— 河滩/河（北缘水面占位地台，纯视觉无碰撞）——
        AddZonePad(new Vector2(0, 0), new Vector2(2800, 300), new Color(0.20f, 0.34f, 0.42f, 0.55f)); // 河水
        AddZonePad(new Vector2(0, 300), new Vector2(2800, 140), new Color(0.42f, 0.40f, 0.30f, 0.45f)); // 河滩湿泥

        // —— 沿岸栈道（两道护栏＝实体墙，之间是走道；逼玩家从西侧上栈道再折向小屋，制造绕行）——
        var railC = new Color(0.34f, 0.26f, 0.18f, 0.95f);
        AddSolidWall(new WallRect(560f, 470f, 900f, 30f), railC, zIndex: 6);   // 栈道·北护栏
        AddSolidWall(new WallRect(760f, 640f, 900f, 30f), railC, zIndex: 6);   // 栈道·南护栏（与北护栏错位＝S 形走道）

        // —— 河边小屋（实体墙房间，门朝南＝正对下方来路）+ 屋后菜窖棚 ——
        AddRoomOutline(new Rect2(1560, 780, 640, 480), new Color(0.32f, 0.28f, 0.22f, 0.95f), "河边小屋", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(2260, 980, 300, 280), new Color(0.26f, 0.23f, 0.18f, 0.95f), "屋后菜窖", RoomEdge.Left);

        // —— 岸边杂物堆两处（实体挡路，逼绕行）——
        var junkC = new Color(0.38f, 0.34f, 0.28f, 0.95f);
        AddSolidWall(new WallRect(900f, 1180f, 220f, 60f), junkC, zIndex: 6);   // 破船/木料堆
        AddSolidWall(new WallRect(1300f, 1440f, 60f, 220f), junkC, zIndex: 6);  // 渔网桩/杂物垛

        // —— 五处搜刮点：岸边渔具(近) → 枪柜/灶膛(小屋入口/屋内) → 床底木箱(屋内深) → 屋后菜窖(最深) ——
        AddDiscoveryPoint(
            ExplorationCache.RiversideFishingId,
            new Vector2(680, 560),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "屋檐渔具箱");
        AddDiscoveryPoint(
            ExplorationCache.RiversideGunCabinetId,
            new Vector2(1500, 1360),
            markerColor: new Color(0.55f, 0.42f, 0.28f),
            label: "枪柜");
        AddDiscoveryPoint(
            ExplorationCache.RiversideHearthId,
            new Vector2(1720, 960),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "灶膛橱柜");
        AddDiscoveryPoint(
            ExplorationCache.RiversideBedChestId,
            new Vector2(2040, 960),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "床底木箱");
        AddDiscoveryPoint(
            ExplorationCache.RiversideCellarId,
            new Vector2(2410, 1110),
            markerColor: new Color(0.5f, 0.45f, 0.3f),
            label: "屋后菜窖");
    }
}
