using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// 守林人小屋（小点样板，用户 [SPEC-B11-补] 拍板；内部路由键＝守望者森林小屋 <see cref="WorldMapPanel.WatchersCabinName"/>，显示名正名为「守林人小屋」）：
    /// 屋子（含**屋中屋**——里屋一道内门/暗间，小点也有层次）+ **后院老树上哥顿上吊尸**（发现点）+ 日记B + 两处物资搜刮点（里屋碗柜/后院柴房，小点量级）。
    /// 哥顿一致性（设计文档 §3/§8.7）：帮主哥顿早已独自走进林中孤屋、上吊自杀于树上——此处按用户最新口径落**后院**老树（原文档为门口，已随正名同步）。
    /// 屋/树为占位视觉（无碰撞、不进导航；正式空间/美术待后续，同瞭望台/广播台占位口径）；发现点/搜刮点走既有 <see cref="AddDiscoveryPoint"/> 链路，
    /// 掉落解析在 CampMain.OnExplorationDiscovery（哥顿走 <see cref="GoldfingerDiscovery.Resolve"/>、两搜刮点走 <see cref="ExplorationCache.Resolve"/>）。
    /// </summary>
    private void SetupRangersCabin()
    {
        // [小图适度放大] 画布 2800×1900（<see cref="ExplorationLevelSize"/>）。身份＝林间小屋 + 柴棚 + 林下：
        //   保「屋中屋」层次，补一间**柴棚**（后院），采集点铺到更开阔的**林下**。搜刮点/采集点 id 一律不动
        //   （<see cref="ExplorationCache"/> 五处 + <see cref="ForageLogic"/> 两处不变 ⇒ *CacheTests / ForageFarmingButcheryTests 恒绿），
        //   只随放大后空间重排坐标。哥顿上吊尸随老树移到后院深处（authored 后院口径不变）。地台/树为占位视觉，坐标拟定待调。
        // —— 林下开阔地占位地台（纯视觉）——
        AddZonePad(new Vector2(1400, 1240), new Vector2(1100, 620), new Color(0.20f, 0.26f, 0.16f, 0.4f)); // 后院林下

        // —— 屋子（外屋）+ 里屋（暗间）占位轮廓：小点也有「屋中屋」层次 ——
        AddRoomOutline(new Rect2(1000, 760, 560, 440), new Color(0.34f, 0.30f, 0.24f, 0.95f), "守林人小屋", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(1240, 900, 260, 240), new Color(0.28f, 0.25f, 0.21f, 0.95f), "里屋（暗间）", RoomEdge.Top);
        // —— 柴棚（后院一间，门朝西＝正对林下开阔地）：柴房搜刮点在其内 ——
        AddRoomOutline(new Rect2(1900, 1180, 320, 300), new Color(0.26f, 0.22f, 0.17f, 0.95f), "柴棚", RoomEdge.Left);

        // 里屋碗柜（暗间内、路径较浅）：小点日常储粮/急救小物。
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinPantryId,
            new Vector2(1360, 1020),
            markerColor: new Color(0.55f, 0.5f, 0.4f),
            label: "碗柜");

        // [SPEC-B12] 小点扩至 5（band 5~10 下限；仍是"阁楼/床底/门廊"一类小点，不破坏内容稀薄氛围）。
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinUnderbedId,
            new Vector2(1120, 900),
            markerColor: new Color(0.5f, 0.46f, 0.36f),
            label: "床底铁盒");
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinPorchId,
            new Vector2(1180, 1320),
            markerColor: new Color(0.5f, 0.46f, 0.36f),
            label: "门廊工具架");
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinAtticId,
            new Vector2(1460, 880),
            markerColor: new Color(0.5f, 0.46f, 0.36f),
            label: "阁楼杂物");

        // —— 后院：老树 + 哥顿上吊尸占位 + 柴房搜刮点（柴棚内，藏深）——
        AddBackyardTree(new Vector2(2100, 760));
        AddBackyardTree(new Vector2(1560, 1560)); // 林下另一株老树（占位氛围）
        // 哥顿上吊尸（后院老树横枝）+ 日记B 发现点。与金手指帮根据地异地，独立一处。
        AddDiscoveryPoint(
            GoldfingerDiscovery.GordonHangedId,
            new Vector2(2100, 830),
            markerColor: new Color(0.55f, 0.5f, 0.45f),
            label: "上吊尸");
        // 后院柴棚·柴房（藏深）：木料/绳/钉。
        AddDiscoveryPoint(
            ExplorationCache.RangersCabinShedId,
            new Vector2(2060, 1330),
            markerColor: new Color(0.5f, 0.44f, 0.34f),
            label: "柴房");

        // [T67] 两处**采集点**（不是搜刮箱——弯腰在地上薅蘑菇，走 ForageLogic）：林下腐叶 + 柴堆背阴。
        //   读过《野外生存指南》采得更多（×1.5）。绿色标记区别于搜刮点的暖色。**id 不动**（护栏），仅随放大后林下空间重排。
        var forageC = new Color(0.42f, 0.62f, 0.36f);
        AddDiscoveryPoint(ForageLogic.RangersCabinMushroomId, new Vector2(1700, 1560), markerColor: forageC, label: "林下蘑菇");
        AddDiscoveryPoint(ForageLogic.RangersCabinWoodpileMushroomId, new Vector2(1840, 1340), markerColor: forageC, label: "柴堆背阴");
    }

    /// <summary>后院老树占位（纯视觉）：树干 + 树冠 + 一段吊绳，示意哥顿上吊处。<paramref name="basePos"/>＝树根位置。</summary>
    private void AddBackyardTree(Vector2 basePos)
    {
        AddChild(new Polygon2D // 树干
        {
            Polygon = Quad(basePos + new Vector2(-10, -60), new Vector2(20, 90)),
            Color = new Color(0.30f, 0.22f, 0.14f),
            ZIndex = 4,
        });
        AddChild(new Polygon2D // 树冠（粗略多边形）
        {
            Polygon = new Vector2[]
            {
                basePos + new Vector2(-72, -60), basePos + new Vector2(-42, -132),
                basePos + new Vector2(28, -152), basePos + new Vector2(82, -104),
                basePos + new Vector2(58, -52),
            },
            Color = new Color(0.20f, 0.30f, 0.18f, 0.95f),
            ZIndex = 5,
        });
        AddChild(new Polygon2D // 横枝垂下的吊绳（示意上吊处，正上方即哥顿发现点）
        {
            Polygon = Quad(basePos + new Vector2(-2, -96), new Vector2(3, 58)),
            Color = new Color(0.55f, 0.5f, 0.4f),
            ZIndex = 5,
        });
    }
}
