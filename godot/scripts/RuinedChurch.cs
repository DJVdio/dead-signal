using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationWalls.cs / StuartManor.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
// 空间执行（墙体实体化 / 门的开关 / 导航重烘焙 / 视野遮罩）归 TestExploration 运行时层。
//
// ================= [SPEC-T60] 破败教堂（后期·中图·高危）=================
//
// 🔴 用户原话（authored 唯一事实源，一字不改）：
//    「破败教堂，规模中，穿过教堂的视野盲区，打开门看到后院墓地中有大量丧尸（突然看到吓一跳的感觉），
//      在这里可以找到一些军方留下的被烧了一半的忏悔录和一些被军方屠杀的人用血写在墙上的对军方的辱骂。」
//
// 🔴 **这是一关"视野"，不是一关"战力"。**
//    项目里那套光照与锥形视野（VisionLogic / VisionOcclusion / VisionMask）一直在，却从没有一关拿它当主轴。
//    这一关的每一堵墙都是为**遮挡视线**而砌的，不是为了挡路：
//      · **长椅**（5 排横贯的实心长条）——把中殿切成一条条**东西向的窄槽**：你只看得见自己所在的那一条。
//      · **立柱**（3 根，在中央走道里**左右交错**）——中央走道因此**是折的**，站在门厅望不到头。
//      · **告解亭**（2 座封闭小间）——真正的盲盒：不迈进去，你不知道里头有什么。
//      · **屏风**（中殿↔圣坛的横墙，两处拱门）——**你走完整个中殿都看不见后院那扇门**。
//    ⇒ 「穿过教堂的视野盲区」是**几何做出来的**，不是脚本演的。
//
// 🔴 **"吓一跳"是机制，不是演出。**
//    后院门**初始关着**。门在本仓的口径里＝墙层（VisionOcclusion.WallMask）⇒ **它挡视线**。
//    ⇒ 站在门前（<see cref="DoorApproachPoint"/>），到墓地里 12 只丧尸的**每一条视线都被挡死，可见数 = 0**。
//    ⇒ 推开门、迈进门洞（<see cref="DoorwayPoint"/>）的那一刻，门板从墙层里摘掉，
//      **一整片丧尸同时进入视野锥** —— 信息量在一帧里爆炸。这就是"突然看到吓一跳"。
//    两条都上了单测（<c>RuinedChurchTests</c>），谁改坏了几何当场红。
//
// 🔴 **推开门 = 转身就跑，不是清场。**（docs/research/2026-07-14-lanchester.md）
//    2 只丧尸围攻＝胜率 16.6%，3 只＝0.8%，4 只起＝**0%**。围攻是**断崖不是斜坡**。
//    12 只 ⇒ 站着打＝必死。**设计意图就是让你转身跑，并把门关上。**
//    为此这一关有两条**互相拉扯、且都必须成立**的硬不变量（单测钉死）：
//      **A. 墓地关得死**：墓地边界上的**每一个洞都装了门** —— 与医院**恰恰相反**（医院的不变量是
//         "每道边界必留一个关不上的洞"，防的是玩家把自己反锁在里面）。这里反过来：
//         **两扇门全关上 ⇒ 墓地与教堂真的断开** ⇒ "关门隔开丧尸"是真的，不是安慰。
//      **B. 玩家跑得掉**：外墙的**两个入口永远敞着**（关不上）⇒ 就算把关内的门全关死，
//         从教堂里任意一点仍走得到关外。A 与 B 同时成立，靠的是"能关的门全在墓地那道边界上、
//         一扇也不在退路上"。<see cref="LevelReachability"/> 的 BFS 把这两条都跑成了测试。
//    门**都没上锁** ⇒ 万一你人在墓地里，照样推得开门回来（绝不会被一扇门永久卡死）。
//
// 🔴 **这一关最值钱的东西不是银烛台，是那两页残纸。**（「高风险不是永远高回报」是用户拍板的通则）
//    12 处搜刮点全是布/木/铁/蜡和一点白银——**没有枪，没有弹药**（教堂本来就不该有）。
//    真正的回报是 <see cref="NarrativeSpotRegistry"/> 里那两处 authored 证据：
//    **烧了一半的忏悔录**（告解亭）与**墙上的血字**（中殿）——「军方干了什么」的第一手证据。
//    而广播台那条主线的岔口正是**"回复军方 / 呼叫南方"**（RadioMainline）⇒ 这条线索链直通结局。
//    🔴 **框架只负责"按条件播放"**：正文是用户手写的 authored 内容，代码一个字都不许编（见 NarrativeSpot.cs 的占位）。

/// <summary>破败教堂的四个分区（南→北＝浅→深）。</summary>
public enum ChurchZone
{
    /// <summary>门厅（南·近）：告解亭在此——烧了一半的忏悔录也在此。</summary>
    Narthex,

    /// <summary>中殿（中）：长椅与立柱的视野盲区；墙上的血字在此。</summary>
    Nave,

    /// <summary>圣坛区（深）：祭台、圣器室；后院那两扇门就在它的北墙上。</summary>
    Chancel,

    /// <summary>后院墓地（最深·门后那一片）。</summary>
    Graveyard,
}

/// <summary>破败教堂的一处搜刮点（id 与 <see cref="ExplorationCache"/> 一致）。</summary>
public readonly record struct ChurchCacheSpot(string Id, float X, float Y, string Label, ChurchZone Zone);

/// <summary>
/// 破败教堂的关卡几何（纯逻辑）。同一批矩形三用：碰撞（挡路）/ 导航 obstruction（阻断寻路）/ 墙层射线（挡视线）。
/// </summary>
public static class RuinedChurch
{
    /// <summary>目的地路由键（须与 world_graph.json / ExplorationCache / WorldMapPanel 一字不差）。</summary>
    public const string DestinationName = "破败教堂";

    // ── 尺寸 ──（关卡 2400×1600，南面 y>1420 留返回区）
    /// <summary>外墙厚。</summary>
    public const float WallThickness = 16f;
    /// <summary>内墙（隔墙/屏风）厚。</summary>
    public const float PartitionThickness = 12f;
    /// <summary>门洞宽（须 &gt; 2×<see cref="ExplorationWalls.NavAgentRadius"/>）。</summary>
    public const float DoorwayWidth = 72f;

    private const float Left = 300f;
    private const float Right = 2100f;
    private const float Top = 120f;
    private const float Bottom = 1420f;

    /// <summary>墓地／教堂的分界墙 y（＝教堂北墙。**这一条线就是这一关的全部**）。</summary>
    public const float GraveyardWallY = 500f;

    /// <summary>屏风（中殿↔圣坛）y。走完整个中殿都看不见后院门，靠的就是它。</summary>
    public const float ScreenY = 780f;

    // ── 门（authored 名字；与 TestExploration 的可交互门一一对应）──
    /// <summary>后院门（中轴，正对祭台后方）：**推开它的那一刻就是这一关的全部**。</summary>
    public const string BackyardDoor = "后院门";
    /// <summary>北耳门（圣坛东侧的边门；墓地的第二个洞，一样装了门——墓地必须关得死）。</summary>
    public const string NorthSideDoor = "北耳门";

    /// <summary>后院门的门洞中心 X。</summary>
    public const float BackyardDoorX = 1100f;
    /// <summary>北耳门的门洞中心 X。</summary>
    public const float NorthSideDoorX = 1560f;

    /// <summary>**门前**站位（后院门南侧，教堂这一边）：在这里，墓地的可见丧尸数**必须是 0**。</summary>
    public static readonly (float X, float Y) DoorApproachPoint = (BackyardDoorX, 566f);

    /// <summary>**门洞**站位（推开门、迈进去的那一帧）：在这里，一整片丧尸同时进视野。</summary>
    public static readonly (float X, float Y) DoorwayPoint = (BackyardDoorX, GraveyardWallY + PartitionThickness / 2f);

    /// <summary>正门内侧站位（进关第一步；从这里望出去，中殿深处什么都看不见——盲区测试的观察点之一）。</summary>
    public static readonly (float X, float Y) FrontDoorPoint = (1100f, 1360f);

    /// <summary>
    /// 外墙的两个入口。<b>两个都是关不上的洞</b>（DoorName = null）——这是不变量 B（玩家跑得掉）的全部依据：
    /// 关内可关的门一扇也不在退路上。
    /// </summary>
    public static IReadOnlyList<HospitalEntrance> Entrances() => new[]
    {
        new HospitalEntrance("正门", null),   // 南墙正中，返回区正对着它
        new HospitalEntrance("侧门", null),   // 西墙，绕开门厅直入中殿
    };

    /// <summary>正门（南墙）的洞中心 X。</summary>
    public const float FrontDoorX = 1100f;
    /// <summary>侧门（西墙）的洞中心 Y。</summary>
    public const float SideDoorY = 1150f;

    /// <summary>
    /// 墓地边界（教堂北墙）上的门洞。
    /// <para>
    /// 🔴 <b>硬不变量 A（与医院恰恰相反，是刻意的）</b>：这道边界上**每一个洞都装了门**（无 null）。
    /// 医院怕的是"把自己反锁在里面"，故每道边界必留一个关不上的洞；这一关怕的是**关了门也挡不住**——
    /// 「推开门看到一片丧尸 ⇒ 转身就跑 ⇒ 把门关上」这条玩法，只有在**墓地真的关得死**时才成立。
    /// 退路不靠这道边界，靠外墙那两个永远敞着的入口（<see cref="Entrances"/>）。
    /// </para>
    /// </summary>
    public static IReadOnlyList<Doorway> GraveyardDoorways() => new[]
    {
        new Doorway(BackyardDoorX, BackyardDoor),
        new Doorway(NorthSideDoorX, NorthSideDoor),
    };

    /// <summary>
    /// 屏风（中殿↔圣坛）上的两处拱门：**都关不上**（教堂的屏风本就没有门）。
    /// 它只挡视线、不挡路——这正是"视野盲区"想要的：你走得过去，但你**看不过去**。
    /// </summary>
    public static IReadOnlyList<Doorway> ScreenDoorways() => new[]
    {
        new Doorway(700f, null),
        new Doorway(1500f, null),
    };

    /// <summary>
    /// 教堂的全部墙段（外墙 + 墓地边界 + 屏风 + 长椅 + 立柱 + 告解亭 + 祭台 + 圣器室）。
    /// <b>不含门板</b>——门板是 <see cref="Doors"/>，随开关动态增删。
    /// <para>长椅/立柱/告解亭/祭台**都在墙层上**：它们的职责是**挡视线**（挡路只是副作用）。</para>
    /// </summary>
    public static IReadOnlyList<WallRect> Walls()
    {
        const float t = WallThickness;
        const float p = PartitionThickness;
        const float w = DoorwayWidth;
        var walls = new List<WallRect>(40);

        // ── 外墙 ──
        walls.Add(new WallRect(Left, Top, Right - Left, t));                       // 北墙（墓地后墙，实心）
        walls.Add(new WallRect(Right - t, Top, t, Bottom - Top));                  // 东墙（实心）
        AddSplit(walls, new WallRect(Left, Top, t, Bottom - Top), false, new[] { SideDoorY }, w);          // 西墙：侧门
        AddSplit(walls, new WallRect(Left, Bottom - t, Right - Left, t), true, new[] { FrontDoorX }, w);   // 南墙：正门

        // ── 墓地边界（教堂北墙）：两个洞，两扇门 ──
        var gy = new List<float>();
        foreach (Doorway d in GraveyardDoorways())
            gy.Add(d.Center);
        AddSplit(walls, new WallRect(Left + t, GraveyardWallY, (Right - t) - (Left + t), p), true, gy, w);

        // ── 屏风（中殿↔圣坛）：两处拱门，关不上 ──
        var sc = new List<float>();
        foreach (Doorway d in ScreenDoorways())
            sc.Add(d.Center);
        AddSplit(walls, new WallRect(Left + t, ScreenY, (Right - t) - (Left + t), p), true, sc, w);

        // ── 圣坛区（y 512..780）──
        // 祭台：横在圣坛正中，挡住从屏风拱门望向后院门的那条线。
        walls.Add(new WallRect(950f, 600f, 300f, 60f));
        // 圣器室：封闭小间，西墙留门（从圣坛进）。里头一个搜刮点。
        foreach (WallRect r in ExplorationWalls.RoomOutlineWalls(
                     new WallRect(1740f, 530f, 340f, 240f), RoomEdge.Left))
            walls.Add(r);

        // ── 中殿（y 792..1240）：5 排长椅 ──
        // 每排＝两条实心长条（中央走道在 1030..1170）。排距 90、条厚 26 ⇒ 排间留 64px 的**东西向窄槽**
        // （净宽 64−2×14＝36 &gt; 0 ⇒ 走得进去）。站在槽里，你**只看得见这一条槽**。
        foreach (float y in PewRowY)
        {
            walls.Add(new WallRect(380f, y, 650f, PewThickness));    // 西半排
            walls.Add(new WallRect(1170f, y, 850f, PewThickness));   // 东半排
        }

        // ── 中殿：3 根立柱，在中央走道里**左右交错** ⇒ 中央走道是折的，从门厅望不到头 ──
        foreach (WallRect c in Columns)
            walls.Add(c);

        // ── 门厅（y 1240..1404）：2 座告解亭（封闭小间，门朝北开向门厅）──
        foreach (WallRect box in ConfessionalBoxes)
        {
            foreach (WallRect r in ExplorationWalls.RoomOutlineWalls(box, RoomEdge.Top))
                walls.Add(r);
        }

        return walls;
    }

    /// <summary>长椅条厚。</summary>
    public const float PewThickness = 26f;

    /// <summary>5 排长椅的 y（排距 90 ⇒ 排间 64px 的东西向窄槽，走得进去、看不出去）。</summary>
    public static readonly IReadOnlyList<float> PewRowY = new[] { 830f, 920f, 1010f, 1100f, 1190f };

    /// <summary>
    /// 中央走道（x 1030..1170，宽 140）里的 3 根立柱，**左右交错**（西/东/西）：
    /// 每根占走道的一半（70），把直的走道折成 Z 字 ⇒ 站在门厅望不到圣坛，站在圣坛望不到门厅。
    /// </summary>
    public static readonly IReadOnlyList<WallRect> Columns = new[]
    {
        new WallRect(1030f, 1140f, 70f, 70f), // 西半，近门厅
        new WallRect(1100f, 960f, 70f, 70f),  // 东半，居中
        new WallRect(1030f, 800f, 70f, 70f),  // 西半，近屏风
    };

    /// <summary>门厅里的 2 座告解亭（封闭小间，门朝北）。**烧了一半的忏悔录在西侧那一座里。**</summary>
    public static readonly IReadOnlyList<WallRect> ConfessionalBoxes = new[]
    {
        new WallRect(420f, 1250f, 160f, 150f),  // 西侧告解亭 ← 忏悔录
        new WallRect(640f, 1250f, 160f, 150f),  // 东侧告解亭
    };

    /// <summary>
    /// 教堂的两扇**可关的门**（都在墓地边界上）。初始 <see cref="DoorState.Closed"/>——
    /// **这一关成立的前提**：门关着 ⇒ 门挡视线 ⇒ 你在门这边**真的看不见后院**。
    /// <para>门**没有上锁**：人在墓地里也推得开，绝不会被一扇门永久卡死。</para>
    /// </summary>
    public static IReadOnlyList<ExplorationDoor> Doors()
    {
        const float p = PartitionThickness;
        const float w = DoorwayWidth;
        var doors = new List<ExplorationDoor>(2);
        foreach (Doorway d in GraveyardDoorways())
        {
            if (d.DoorName is null)
                continue;
            doors.Add(new ExplorationDoor(
                d.DoorName,
                new WallRect(d.Center - w / 2f, GraveyardWallY, w, p),
                DoorState.Closed));
        }
        return doors;
    }

    /// <summary>
    /// 12 处搜刮点（中图 band 10~16）。
    /// 🔴 **穷**：布/木/铁/蜡 + 一点白银，**没有枪，没有弹药**。「高风险不是永远高回报」——
    /// 这一关的回报是墙上那些字，不是烛台。
    /// </summary>
    public static readonly IReadOnlyList<ChurchCacheSpot> CacheSpots = new[]
    {
        // 门厅（南·近，3）
        new ChurchCacheSpot(ExplorationCache.ChurchOfferingBoxId, 1400f, 1330f, "奉献箱", ChurchZone.Narthex),
        new ChurchCacheSpot(ExplorationCache.ChurchCloakroomId, 1760f, 1330f, "衣帽间", ChurchZone.Narthex),
        new ChurchCacheSpot(ExplorationCache.ChurchHymnalRackId, 950f, 1300f, "圣诗集架", ChurchZone.Narthex),

        // 中殿（中，4）——全在长椅切出来的窄槽/侧廊里，得钻进去才够得着
        new ChurchCacheSpot(ExplorationCache.ChurchPewUnderId, 700f, 1155f, "长椅底下", ChurchZone.Nave),
        new ChurchCacheSpot(ExplorationCache.ChurchCandleStandId, 345f, 975f, "侧廊烛台", ChurchZone.Nave),
        new ChurchCacheSpot(ExplorationCache.ChurchOrganLoftId, 2050f, 880f, "风琴台", ChurchZone.Nave),
        new ChurchCacheSpot(ExplorationCache.ChurchFontId, 1600f, 1155f, "洗礼池", ChurchZone.Nave),

        // 圣坛区（深，3）
        new ChurchCacheSpot(ExplorationCache.ChurchAltarId, 1100f, 700f, "祭台", ChurchZone.Chancel),
        new ChurchCacheSpot(ExplorationCache.ChurchSacristyCabinetId, 1900f, 660f, "圣器室橱柜", ChurchZone.Chancel),
        new ChurchCacheSpot(ExplorationCache.ChurchChoirLockerId, 560f, 620f, "唱诗席储物柜", ChurchZone.Chancel),

        // 后院墓地（最深·门后那一片，2）——**这两处就是"要不要迈进去"的赌注**
        new ChurchCacheSpot(ExplorationCache.ChurchGravediggerShedId, 480f, 260f, "掘墓人工棚", ChurchZone.Graveyard),
        new ChurchCacheSpot(ExplorationCache.ChurchCryptId, 1850f, 240f, "石棺墓室", ChurchZone.Graveyard),
    };

    /// <summary>
    /// 教堂本体的游荡丧尸（3 只·稀）：进关不会当场被淹。真正的一片在门后。
    /// </summary>
    public static readonly IReadOnlyList<(float X, float Y)> ChurchZombieSpots = new[]
    {
        (1600f, 1330f),  // 门厅东侧
        (350f, 1080f),   // 西侧廊
        (2050f, 1050f),  // 东侧廊
    };

    /// <summary>
    /// 🔴 **后院墓地的 12 只丧尸 —— 「大量」就是它的身份。**
    /// 全部扎在后院门正北的扇面里：推开门的那一刻，它们**同时**进入视野锥（单测钉死 ≥8 只）。
    /// <para>
    /// **这 12 只不是让你清完的。** 2 只围攻＝16.6%，3 只＝0.8%，4 只起＝0%
    /// （docs/research/2026-07-14-lanchester.md）。**设计意图是让你转身跑，并把门关上。**
    /// </para>
    /// </summary>
    /// <remarks>
    /// 布点是**按视野锥算出来的**，不是撒的：以门洞站位（<see cref="DoorwayPoint"/>）为原点，
    /// 白昼锥＝视距 300 / 半角 60° ⇒ 前 10 只落在锥内（推开门那一瞬**同时**出现），
    /// 剩 2 只是更远的散兵（东、西各一，第一眼看不到——它们会自己朝你来）。
    /// 挪动坐标之前先跑 <c>RuinedChurchTests</c> 那两条：可见数会当场告诉你惊吓还在不在。
    /// </remarks>
    public static readonly IReadOnlyList<(float X, float Y)> GraveyardZombieSpots = new[]
    {
        // 锥内（推开门 ⇒ 一眼看到这一片）
        (1010f, 430f), (1190f, 425f), (1100f, 355f), (950f, 375f), (1265f, 370f),
        (1090f, 265f), (890f, 300f), (1290f, 300f), (1150f, 235f), (1010f, 250f),
        // 锥外的两只散兵（东/西）——第一眼看不见，但它们听得见你
        (1420f, 430f), (760f, 420f),
    };

    /// <summary>一条边按若干门洞中心断开成多段（同 <see cref="ExplorationWalls"/> 的口径）。</summary>
    private static void AddSplit(
        List<WallRect> walls, WallRect edge, bool horizontal, IReadOnlyList<float> centers, float doorWidth)
    {
        var sorted = new List<float>(centers);
        sorted.Sort();

        float cursor = horizontal ? edge.X : edge.Y;
        float end = horizontal ? edge.Right : edge.Bottom;

        foreach (float c in sorted)
        {
            float gapStart = c - doorWidth / 2f;
            float gapEnd = c + doorWidth / 2f;
            if (gapStart > cursor)
            {
                walls.Add(horizontal
                    ? new WallRect(cursor, edge.Y, gapStart - cursor, edge.Height)
                    : new WallRect(edge.X, cursor, edge.Width, gapStart - cursor));
            }
            if (gapEnd > cursor)
                cursor = gapEnd;
        }

        if (end > cursor)
        {
            walls.Add(horizontal
                ? new WallRect(cursor, edge.Y, end - cursor, edge.Height)
                : new WallRect(edge.X, cursor, edge.Width, end - cursor));
        }
    }
}
