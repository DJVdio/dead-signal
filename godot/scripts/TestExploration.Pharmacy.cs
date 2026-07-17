using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// 南丁格尔的小药店（[SPEC-B13]，小点 5 物资点 + 护士相遇招募点 + 1 叙事调查点）：小店面 + 后屋药房 + 阁楼，小而有层次。
    /// 关内核心＝**可招募护士**（柜台后守店的清醒 NPC，踏入其警戒区弹 ChoicePanel 招募对话，见 <see cref="NurseRecruit"/> 与
    /// CampMain.PromptNurseRecruit）。物资＝基础药品/绷带为主但量薄（大头药品在医院），投放/叙事见 <see cref="ExplorationCache"/>。
    /// 叙事调查点（柜台留言板）由 <see cref="SetupNarrativeSpots"/> 按目的地自动铺（NarrativeSpotRegistry），此处不重复铺。
    /// </summary>
    private void SetupNightingalePharmacy()
    {
        // —— 小店面（临街）+ 后屋药房（暗间）+ 阁楼：小而有层次 ——
        // 小店面须开**两处**门洞：南＝临街外门，北＝通后屋药房。后屋药房的南门正对小店面的北墙，
        // 墙实体化后若北墙不开洞，后屋药房就三面实心 + 一门顶死＝玩家永远进不去（既有布局的通行性缺陷）。
        AddRoomOutline(new Rect2(900, 700, 500, 340), new Color(0.30f, 0.32f, 0.34f, 0.95f), "南丁格尔的小药店", RoomEdge.Bottom, RoomEdge.Top);
        AddRoomOutline(new Rect2(1000, 480, 320, 220), new Color(0.26f, 0.28f, 0.30f, 0.95f), "后屋药房", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(1440, 500, 240, 200), new Color(0.24f, 0.25f, 0.27f, 0.95f), "阁楼", RoomEdge.Left);

        // 柜台（纯视觉占位）：护士就守在它后头。
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(980, 820), new Vector2(320, 24)),
            Color = new Color(0.35f, 0.30f, 0.24f, 0.95f),
            ZIndex = 5,
        });

        // —— 护士相遇招募点（柜台后，NPC 非物资；踏入弹招募对话）——
        AddDiscoveryPoint(
            NurseRecruit.MeetDiscoveryId,
            new Vector2(1150, 850),
            markerColor: new Color(0.40f, 0.72f, 0.66f), // 青绿＝友方 NPC，与褐色搜刮点区分
            label: "护士");

        // —— 5 物资搜刮点：小店面(近) → 后屋药房(深) → 阁楼(最深)。量薄（小药店） ——
        AddDiscoveryPoint(ExplorationCache.PharmacyCounterId, new Vector2(1000, 950),
            markerColor: new Color(0.55f, 0.5f, 0.42f), label: "收银台");
        AddDiscoveryPoint(ExplorationCache.PharmacyShelfId, new Vector2(1330, 950),
            markerColor: new Color(0.55f, 0.5f, 0.42f), label: "货架");
        AddDiscoveryPoint(ExplorationCache.PharmacyDispensaryId, new Vector2(1080, 560),
            markerColor: new Color(0.5f, 0.46f, 0.38f), label: "处方柜");
        AddDiscoveryPoint(ExplorationCache.PharmacyColdBoxId, new Vector2(1240, 560),
            markerColor: new Color(0.5f, 0.46f, 0.38f), label: "冷藏箱");
        AddDiscoveryPoint(ExplorationCache.PharmacyAtticId, new Vector2(1560, 590),
            markerColor: new Color(0.5f, 0.44f, 0.36f), label: "阁楼杂物");
    }
}
