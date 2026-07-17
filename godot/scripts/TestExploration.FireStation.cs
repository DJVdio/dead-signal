using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// [批次25·T50] 消防站游荡丧尸布点（<b>低危·全图最少：3 只，band 2~4</b>）。用户原话「低危」。
    /// <para>
    /// 参照：加油站 5（中低）/ 东部新村 7（中）/ 医院 14（丧尸巢）⇒ 消防站 3 是**全图最低**，名副其实。
    /// 三只**全在深处**（器材间外 / 值班室 / 后院训练塔），队伍出生点（200,1400）所在的**车库入口区一只都没有**——
    /// 玩家进门就能把器材墙上那把消防斧摘下来，不必先打一架。这正是"开局友好"的意思。
    /// </para>
    /// 数量/布点拟定待调（归 param-calibration）。
    /// </summary>
    private static readonly Vector2[] FireStationZombieSpots =
    {
        new(1120f, 940f),  // 器材间门外（挡在急救柜那条路上）
        new(1700f, 1130f), // 值班室附近
        new(1880f, 640f),  // 后院·训练塔下（最深）
    };

    /// <summary>
    /// [批次25·T50] 消防站：街区消防站——车库（卷帘门大开）+ 值班室 + 器材间 + 后院训练塔。
    /// 用户原话：「消防站（一些基础物资和消防斧，<b>小地图</b>，<b>低危</b>）」。
    /// <para>
    /// <b>小地图</b>：5 处搜刮点（小点 band），空间也铺得紧凑——车库占南半场（队伍出生点 200,1400 就在它西南），
    /// 两间小屋（值班室/器材间）+ 一片后院，走两步就到，不做迷宫。
    /// <b>低危</b>：3 只游荡丧尸（<see cref="FireStationZombieSpots"/>，全图最少），且**入口车库区一只都没有**。
    /// </para>
    /// <para>
    /// 近→深：车库(南/近·消防车+<b>器材墙上的消防斧</b>) → 值班室(东/中) → 器材间(北/深·急救柜) → 后院(东北/最深·杂物棚)。
    /// 两间小屋用 <see cref="AddRoomOutline"/> 实体墙，门洞**都朝南**（正对车库那片开阔地）⇒ 从出生点一路向北，
    /// 每间屋都是正面进，不存在"门顶死在别人墙上"的通行陷阱（那是 <c>SetupNightingalePharmacy</c> 记下的教训）。
    /// 掉落/叙事在 <see cref="ExplorationCache.Resolve"/>；叙事调查点（车库出车记录板）由 <see cref="SetupNarrativeSpots"/> 按目的地自动铺。
    /// </para>
    /// </summary>
    private void SetupFireStation()
    {
        // 分区占位地台（纯视觉）：车库(南/近·水泥灰)、后院训练场(东北/最深·土黄)。
        AddZonePad(new Vector2(360, 1060), new Vector2(900, 420), new Color(0.26f, 0.27f, 0.29f, 0.6f));  // 车库（卷帘门大开）
        AddZonePad(new Vector2(1500, 400), new Vector2(700, 460), new Color(0.30f, 0.28f, 0.22f, 0.55f)); // 后院·训练场

        // 两间小屋（实体墙，门洞都朝南＝正对车库开阔地，见类注）。
        AddRoomOutline(new Rect2(1420, 1080, 380, 300), new Color(0.30f, 0.32f, 0.34f, 0.95f), "值班室", RoomEdge.Bottom);
        AddRoomOutline(new Rect2(900, 620, 420, 300), new Color(0.28f, 0.30f, 0.32f, 0.95f), "器材间", RoomEdge.Bottom);

        // 消防车（纯视觉占位）：车头朝外停在车库里，器材箱那一侧就是搜刮点。
        AddChild(new Polygon2D
        {
            Polygon = Quad(new Vector2(560, 1240), new Vector2(300, 110)),
            Color = new Color(0.46f, 0.16f, 0.14f, 0.95f), // 消防红（褪色）
            ZIndex = 5,
        });

        var bayC = new Color(0.55f, 0.5f, 0.42f);   // 车库（近）
        var gearC = new Color(0.62f, 0.42f, 0.30f); // 器材墙：偏红，全站唯一的武器（消防斧）就挂在这儿
        var roomC = new Color(0.5f, 0.46f, 0.38f);  // 值班室/器材间（中·深）
        var medC = new Color(0.42f, 0.62f, 0.44f);  // 急救柜（药绿，与药店/医院同色语义）

        // 车库（南/近，2）
        AddDiscoveryPoint(ExplorationCache.FireStationEngineBayId, new Vector2(700, 1300), markerColor: bayC, label: "消防车器材箱");
        AddDiscoveryPoint(ExplorationCache.FireStationGearWallId, new Vector2(1120, 1150), markerColor: gearC, label: "器材墙");
        // 值班室（东/中，1）
        AddDiscoveryPoint(ExplorationCache.FireStationDutyRoomId, new Vector2(1600, 1250), markerColor: roomC, label: "值班室铺位");
        // 器材间（北/深，1·唯一急救包）
        AddDiscoveryPoint(ExplorationCache.FireStationMedCabinetId, new Vector2(1110, 790), markerColor: medC, label: "急救柜");
        // 后院（东北/最深，1）
        AddDiscoveryPoint(ExplorationCache.FireStationBackyardShedId, new Vector2(1900, 620), markerColor: roomC, label: "杂物棚");
    }
}
