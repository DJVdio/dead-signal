using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace DeadSignal.Godot;

public sealed partial class TestExploration
{
    /// <summary>
    /// [T49] <b>废弃医院</b>（用户原话：「有医疗物资和大量丧尸，大地图，中危」）。
    /// 全游戏**手术与治疗的补给来源**：医疗物资集中投放于药房/手术层（打破全域"禁医疗灌水"的例外点，正是医院身份）。
    /// 分区近→深：门诊/急诊大厅(南) → 住院部 → 药房(医疗集中) → 手术层(手术耗材+高价值医疗)。
    ///
    /// <para>
    /// 🔴 <b>「大量丧尸」+「中危」这两句，只有在能绕过去时才同时成立。</b>
    /// 改造前这里是**一片开阔地**（一堵墙都没有）：14 只丧尸共享同一片视野，进门就被全楼看见，
    /// 既躲不掉也隔不开——那不是中危，是死亡陷阱（连场战斗的代价见 <c>docs/research/2026-07-14-combat-cost.md</c>：
    /// 单场 68% 胜率、不治疗连打，能撑过第 3 个的只剩 3.5%）。
    /// </para>
    ///
    /// <para>
    /// 所以医院现在是一栋**建筑**（几何全部取自纯逻辑 <see cref="ExplorationWalls.HospitalWalls"/>，已上单测）：
    /// <list type="bullet">
    /// <item><b>三个入口</b>：正门 / 急诊入口 / 员工侧门（西面，**跳过大厅直插住院部**的捷径——更短，也更没退路）。</item>
    /// <item><b>每道分区边界多个门洞</b>：一条走廊挤满丧尸时，还有第二条。</item>
    /// <item><b>可关的门</b>（防火门/安全门/卷帘门，初始<b>关着</b>）：关上＝把追你的丧尸挡在门后，
    ///       它得绕到这道边界的另一个门洞去。**每道边界都留了一个关不上的洞**，故你永远关不死自己（单测钉死）。</item>
    /// <item>隔墙同时<b>挡视线</b>（<see cref="VisionOcclusion"/> 打的就是这批矩形）⇒ 丧尸不再一次性全员发现你。</item>
    /// </list>
    /// <b>噪音是这一关的主轴</b>：推门 100、走路 40，而开一枪是 350（手枪）~600（步枪）——足以横穿两三个分区，
    /// 等于**把整层楼叫醒**。医院的正解是近战/弓（70）、关门、绕路，而不是站着清 14 只。
    /// </para>
    /// </summary>
    private void SetupHospital()
    {
        // 分区占位地台（纯视觉）：门诊/急诊(南/近)、住院部(中)、药房(深)、手术层(最深)。越深越"洁净"色调、也越危险。
        // [大图放大] 画布 2400×1600 → 4200×2800，四片地台位置/尺寸一律 ×1.75 跟着放大（保比例）。
        AddZonePad(new Vector2(560, 1890), new Vector2(3080, 665), new Color(0.24f, 0.24f, 0.26f, 0.55f)); // 门诊/急诊大厅
        AddZonePad(new Vector2(525, 1085), new Vector2(3185, 735), new Color(0.22f, 0.25f, 0.28f, 0.55f));  // 住院部
        AddZonePad(new Vector2(700, 665), new Vector2(3115, 385), new Color(0.20f, 0.28f, 0.24f, 0.58f));   // 药房（医疗集中，偏药绿）
        AddZonePad(new Vector2(525, 210), new Vector2(3325, 420), new Color(0.28f, 0.30f, 0.32f, 0.60f));   // 手术层（无菌灰白，最深）

        // ——楼层平面：外墙（三个入口处断开）+ 三道分区隔墙（各留多个门洞）——
        // 同一批矩形三用：碰撞（挡人）/ 导航 obstruction（阻断寻路）/ 墙层射线（挡视线）。
        var wallC = new Color(0.30f, 0.31f, 0.33f, 0.95f);
        foreach (WallRect w in ExplorationWalls.HospitalWalls())
            AddSolidWall(w, wallC, zIndex: -5);

        // ——可关的门（初始关着：医院的防火门本就是关着的，深区的丧尸因此不会在你进门那一刻全员涌来）——
        var doorC = new Color(0.55f, 0.42f, 0.28f, 0.95f); // 门板：木色，和灰墙区分得开
        foreach (ExplorationDoor d in ExplorationWalls.HospitalDoors())
            AddLevelDoor(d, doorC);

        // ——30 处搜刮点（坐标/文案/分区皆取自纯逻辑，与墙体同源，故不会有点被砌进墙里——单测钉死）——
        foreach (HospitalCacheSpot s in ExplorationWalls.HospitalCacheSpots)
        {
            AddDiscoveryPoint(
                s.Id,
                new Vector2(s.X, s.Y),
                markerColor: HospitalZoneColor(s.Zone),
                label: s.Label);
        }
    }

    /// <summary>医院分区的标记色：越深越"洁净"（药房药绿 / 手术层无菌灰白），呼应"越深越危险、也越值钱"。</summary>
    private static Color HospitalZoneColor(HospitalZone zone) => zone switch
    {
        HospitalZone.Lobby => new Color(0.5f, 0.5f, 0.5f),           // 门诊/急诊（非医疗为主）
        HospitalZone.Ward => new Color(0.5f, 0.54f, 0.56f),          // 住院部
        HospitalZone.Pharmacy => new Color(0.42f, 0.62f, 0.44f),     // 药房（医疗，药绿）
        _ => new Color(0.62f, 0.66f, 0.62f),                          // 手术层（无菌灰白·高价值）
    };
}
