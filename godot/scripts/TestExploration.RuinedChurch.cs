using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    // ================= [SPEC-T60] 破败教堂 =================
    //
    // 🔴 用户原话：「破败教堂，规模中，穿过教堂的视野盲区，打开门看到后院墓地中有大量丧尸（突然看到吓一跳的感觉），
    //    在这里可以找到一些军方留下的被烧了一半的忏悔录和一些被军方屠杀的人用血写在墙上的对军方的辱骂。」
    //
    // 🔴 **这一关的每一堵墙都是为挡视线砌的，不是为了挡路。** 几何、门、丧尸布点、"吓一跳"的那两条数字
    //    （门关着＝可见 0 只 / 门一开＝一片同时进锥）全在纯逻辑 <see cref="RuinedChurch"/> 里，并且**上了单测**。
    //    这里只做空间执行：把矩形实体化、把门装上、把点铺出来。**别在这里另写一份几何。**

    /// <summary>
    /// [SPEC-T60] 破败教堂：一关**视野**，不是一关战力。
    /// 分区（南→北，近→深）：门厅(告解亭) → 中殿(长椅/立柱的盲区·墙上的血字) → 圣坛(祭台/圣器室) → **后院墓地(门后那一片)**。
    /// </summary>
    private void SetupRuinedChurch()
    {
        // 分区占位地台（纯视觉）。墓地那一片刻意压暗——但你在推开门之前**根本看不到它**。
        // [Phase2] 画布放大到 3200×2200（ExplorationLevelSize 登记）⇒ 四条地台随 RuinedChurch 的几何重排：
        //   门厅 1700..1934 / 中殿 1112..1700 / 圣坛 712..1100 / 墓地 166..700，东西两侧一律 400..2800。
        //   🔴 别在这里另写一份数：这四条是 Left/Right/Top/Bottom + GraveyardWallY/ScreenY 的投影，几何改了这里要跟着改。
        AddZonePad(new Vector2(400, 1700), new Vector2(2400, 234), new Color(0.24f, 0.22f, 0.24f, 0.55f)); // 门厅
        AddZonePad(new Vector2(400, 1112), new Vector2(2400, 588), new Color(0.22f, 0.21f, 0.25f, 0.55f)); // 中殿
        AddZonePad(new Vector2(400, 712), new Vector2(2400, 388), new Color(0.26f, 0.24f, 0.20f, 0.58f));  // 圣坛（旧金色）
        AddZonePad(new Vector2(400, 166), new Vector2(2400, 534), new Color(0.16f, 0.19f, 0.17f, 0.62f));  // 后院墓地（最暗）

        // ——墙体：外墙 / 墓地边界 / 屏风 / 长椅 / 立柱 / 告解亭 / 祭台 / 圣器室——
        // 同一批矩形三用：碰撞（挡人）/ 导航 obstruction（阻断寻路）/ 墙层射线（**挡视线**）。
        var stoneC = new Color(0.33f, 0.32f, 0.30f, 0.95f);
        foreach (WallRect w in RuinedChurch.Walls())
            AddSolidWall(w, stoneC, zIndex: -5);

        // ——两扇可关的门，**都在墓地边界上，初始关着**——
        // 关着 ⇒ 门在墙层上 ⇒ **它挡视线** ⇒ 你在门这边真的看不见后院。推开它的那一刻，一片丧尸同时进视野。
        var doorC = new Color(0.42f, 0.30f, 0.22f, 0.95f);
        foreach (ExplorationDoor d in RuinedChurch.Doors())
            AddLevelDoor(d, doorC);

        // ——12 处搜刮点（穷：布/木/铁/蜡 + 一点白银，**没有枪没有弹药**）——
        foreach (ChurchCacheSpot s in RuinedChurch.CacheSpots)
            AddDiscoveryPoint(s.Id, new Vector2(s.X, s.Y), markerColor: ChurchZoneColor(s.Zone), label: s.Label);
    }

    /// <summary>教堂分区的标记色（墓地那两处刻意发绿——你看见它们时，多半也看见了别的东西）。</summary>
    private static Color ChurchZoneColor(ChurchZone zone) => zone switch
    {
        ChurchZone.Narthex => new Color(0.52f, 0.48f, 0.44f),
        ChurchZone.Nave => new Color(0.55f, 0.50f, 0.40f),
        ChurchZone.Chancel => new Color(0.62f, 0.55f, 0.36f),   // 圣坛（旧金）
        _ => new Color(0.42f, 0.52f, 0.40f),                     // 墓地（苔绿）
    };
}
