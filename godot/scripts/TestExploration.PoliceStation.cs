using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    // ================= [警察局] 警察局 =================
    //
    // 🔴 用户拍板：前中期 · 规模小 · **室内多拐角** · 危险 Medium，前置挂消防站/河边小屋之后。
    //   「室内多拐角」＝中央脊廊 + 侧向 loot 房间，靠房间门洞遮挡制造盲区（不是靠黑暗——本关**不**标室内恒暗）。
    //   几何/丧尸布点/搜刮点坐标全在纯逻辑 <see cref="ExplorationWalls"/>（PoliceRooms/PoliceWalls/PoliceZombieSpots/PoliceCacheSpots），
    //   并上了单测（<c>PoliceStationTests</c>：任一可行走点感知≤1 / 墙补集不漏 / 路通 / 规模小）。**别在这里另写一份几何。**
    //   这里只做空间执行：把矩形实体化、把搜刮点铺出来。掉落/叙事在 <see cref="ExplorationCache.Resolve"/> + <see cref="SetupNarrativeSpots"/>。

    /// <summary>
    /// [警察局] 一座小警局的室内：门厅(入口) → 办公区/证物室/更衣室(三间侧房) → 禁闭区(最深·**防暴头盔+防弹背心**)。
    /// 丧尸 4 只各藏一房深角（Medium），玩家沿脊廊一间间摸过去，一次只撞见一间房里的东西。
    /// </summary>
    private void SetupPoliceStation()
    {
        // 地台（纯视觉）：室内水泥灰。禁闭区那一片压一层冷蓝——最深、也是两件甲所在。
        // [SPEC-T60·Phase2] 随画布 2800×1900 重排：铺满放大后的建筑 bbox（房间 x[300,2340] y[200,1740] + 20 边距）。
        AddZonePad(new Vector2(280, 180), new Vector2(2080, 1580), new Color(0.24f, 0.25f, 0.27f, 0.55f));
        AddZonePad(new Vector2(1420, 180), new Vector2(600, 280), new Color(0.20f, 0.24f, 0.30f, 0.55f)); // 禁闭区

        // ——墙：由可行走矩形的**补集**自动推出（ExplorationWalls.PoliceWalls）⇒ 房间与墙不可能对不上。
        //   同一批矩形三用：碰撞（挡人）/ 导航 obstruction（阻断寻路）/ 墙层射线（**挡视线** —— 多拐角的盲区就是靠它成立的）。
        var wallC = new Color(0.30f, 0.31f, 0.33f, 0.98f);
        foreach (WallRect w in ExplorationWalls.PoliceWalls())
            AddSolidWall(w, wallC, zIndex: -5);

        // ——拘留区那道**锁死的铁门**（禁闭区唯一入口·全项目第一扇真锁门）——
        //   初始 Locked ⇒ 挡人/挡视线/断寻路三件事一起生效（DoorLogic.Blocks(Locked)=true）⇒ 禁闭区够不着。
        //   撬开（消耗铁丝·按 LockTier 掷成功率）才可达两件甲——撬锁接线在消费层 CampMain.ExecuteLevelDoorInteract。
        var doorC = new Color(0.36f, 0.38f, 0.42f, 0.98f); // 铁灰：一眼看得出是道正经的锁门
        foreach (ExplorationDoor d in ExplorationWalls.PoliceDoors())
            AddLevelDoor(d, doorC);

        // ——5 处搜刮点（近→深）。禁闭室那处用**钢青**标出来：它是这一关最值钱的地方（两件甲）。——
        var lootC = new Color(0.52f, 0.55f, 0.60f);   // 警局灰蓝
        var armorC = new Color(0.42f, 0.60f, 0.72f);  // 钢青：防护装备（禁闭室）
        foreach (PoliceCacheSpot spot in ExplorationWalls.PoliceCacheSpots)
        {
            bool isCell = spot.Id == ExplorationCache.PoliceHoldingCellId;
            AddDiscoveryPoint(spot.Id, new Vector2(spot.X, spot.Y),
                markerColor: isCell ? armorC : lootC, label: spot.Label);
        }
    }
}
