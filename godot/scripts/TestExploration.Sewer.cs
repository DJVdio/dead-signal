using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    // ================= [T61] 下水道 =================
    //
    // 🔴 用户原话：「规模小，下水道，**除了某几个拐角可能有一只丧尸，基本没有危险**，
    //    主要靠**黑暗逼仄的环境**和**大量拐角的差视野**，配合滴滴答答的水滴声和脚步声和回声吓人。」
    //
    // ⇒ **这一关的恐怖全部来自"你看不见"，没有一点来自"你打不过"。**
    //    几何（蛇形通道 / 8 个直角弯 / 一条死胡同 / 3 只分散的丧尸）全在纯逻辑 <see cref="ExplorationWalls"/>
    //    的 Sewer* 段里，**并且上了单测**（SewerTests：任何位置最多被 1 只丧尸感知 / 墙不漏也不堵 / 入口看不见最深处）。
    //    这里只做空间执行：把矩形实体化、把点铺出来。**别在这里另写一份几何。**
    //
    // 📌 **黑暗**：本关经 <see cref="ExplorationLighting.IsIndoorsDark"/> 标为室内恒暗（0.10）⇒ 视锥 ~124px / 半角 30°
    //    ⇒ 玩家基本上**必须手持光源**（占一只手 ⇒ 与双手武器互斥）。**不铺任何固定光源** —— 那正是这一关的身份。
    // 📌 **音效**：用户要的"滴滴答答的水滴声/脚步声/回声"**没有承载它的系统**（本项目至今无音效系统）
    //    ⇒ **不在此伪造**，已作为重大缺口上报。当前只能靠搜刮点的环境叙事文字兑现（见 ExplorationCache 的 Sewer* 叙事）。

    /// <summary>
    /// [T61] 下水道：一条**蛇形的窄管**——入口竖井 → 横廊 → 弯 → 竖廊 → 弯（岔出一条死胡同）→ … → 最深处汇流室（**耗子**）。
    /// </summary>
    private void SetupSewer()
    {
        // 地台：污水的墨绿灰（比任何一关都暗——它本来就该是全图最暗的地方）。
        AddZonePad(new Vector2(320, 140), new Vector2(1360, 1300), new Color(0.13f, 0.16f, 0.15f, 0.60f));

        // ——墙：由通道的**补集**自动推出（ExplorationWalls.SewerWalls）⇒ 通道与墙不可能对不上。
        //   同一批矩形三用：碰撞（挡人）/ 导航 obstruction（阻断寻路）/ 墙层射线（**挡视线** —— 拐角就是靠它成立的）。
        var wallC = new Color(0.17f, 0.19f, 0.18f, 0.98f);
        foreach (WallRect w in ExplorationWalls.SewerWalls())
            AddSolidWall(w, wallC, zIndex: -5);

        // ——5 处搜刮点（**很少量**物资：蘑菇、老鼠、几样基础材料。这地方的价值是耗子，不是战利品）——
        var lootC = new Color(0.42f, 0.46f, 0.38f);   // 苔绿
        foreach (SewerCacheSpot spot in ExplorationWalls.SewerCacheSpots)
            AddDiscoveryPoint(spot.Id, new Vector2(spot.X, spot.Y), markerColor: lootC, label: spot.Label);

        // ——最深处：**耗子**（可招募幸存者）。非物资点 ⇒ 不计探索完成度（同护士相遇点口径）。
        //   踏进去 → CampMain.OnDiscovery → RatRecruit.Resolve → 弹招募对话。
        (float rx, float ry) = ExplorationWalls.SewerDeepestPoint;
        AddDiscoveryPoint(
            RatRecruit.MeetDiscoveryId,
            new Vector2(rx, ry),
            markerColor: new Color(0.72f, 0.66f, 0.52f),   // 昏黄：一个活人
            label: RatPerk.RatName);
    }
}
