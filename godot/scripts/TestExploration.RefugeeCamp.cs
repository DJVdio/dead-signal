using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    // ================= [SPEC-T60] 难民营地 =================
    //
    // 🔴 用户原话：「难民营地，规模中，临时建起的一片平房，内是大量的小房间，过道狭窄，光线昏暗，视野受限，
    //    并且物资分散在每一个房间中，一同在房间中的还有开门跳脸的丧尸。」
    //
    // 🔴 **昏暗＝ <see cref="ExplorationLighting.IsIndoorsDark"/>（环境光 0.10）** ⇒ 视距 300→约 124、半角 60°→33°。
    //    「视野受限」是算出来的，不是画出来的。**一盏固定光源都不预置**（见 <see cref="SetupLevelLights"/>）。
    // 🔴 **窄是玩家的朋友**：房门 48px ⇒ 门口一次只过得来一只丧尸 ⇒ **卡门口＝把围攻打成 1v1**。
    //    几何、门、跳脸布点、那三个数（48/72/90）全在纯逻辑 <see cref="RefugeeCamp"/> 里并上了单测。

    /// <summary>
    /// [SPEC-T60] 难民营地：一片临时排屋——18 间小房、18 扇关着的门、两条窄过道、**没有一盏灯**。
    /// </summary>
    private void SetupRefugeeCamp()
    {
        // 地台（纯视觉）：整片营区一块暗地台。这一关不靠地台分区——**它靠门**。
        // 范围读 RefugeeCamp.Interior（**同一个事实源**）：Phase2 放大到 3200×2200 时这里自动跟，不用两头对数。
        WallRect pad = RefugeeCamp.Interior;
        AddZonePad(new Vector2(pad.X, pad.Y), new Vector2(pad.Width, pad.Height), new Color(0.17f, 0.16f, 0.15f, 0.60f));

        // ——墙体：营区外墙（两个**关不上**的入口）+ 18 间平房的轮廓（各一处 48px 门洞）——
        var shackC = new Color(0.30f, 0.27f, 0.23f, 0.95f);
        foreach (WallRect w in RefugeeCamp.Walls())
            AddSolidWall(w, shackC, zIndex: -5);

        // ——18 扇房门，初始全部关着。门＝墙层 ⇒ 挡视线 ⇒ **你没有任何办法提前知道门后有什么**——
        var doorC = new Color(0.45f, 0.35f, 0.26f, 0.95f);
        foreach (ExplorationDoor d in RefugeeCamp.Doors())
            AddLevelDoor(d, doorC);

        // ——14 处搜刮点，分在 14 个不同的房间里（"物资分散在每一个房间中"）——
        var lootC = new Color(0.52f, 0.47f, 0.38f);
        foreach (RefugeeCacheSpot s in RefugeeCamp.CacheSpots)
            AddDiscoveryPoint(s.Id, new Vector2(s.X, s.Y), markerColor: lootC, label: s.Label);
    }
}
