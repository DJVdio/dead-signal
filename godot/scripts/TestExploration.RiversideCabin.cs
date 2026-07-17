using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
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
}
