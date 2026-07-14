using System.Collections.Generic;

namespace DeadSignal.Godot;

// 注意：本文件为**纯 C# 逻辑**，不得引入任何 Godot 类型
// （与 ExplorationWalls.cs / RuinedChurch.cs 一样被 DeadSignal.Combat.Tests 以 Link 方式编入单测）。
//
// ================= [SPEC-T60] 难民营地（后期·中图·高危）=================
//
// 🔴 用户原话（authored 唯一事实源，一字不改）：
//    「难民营地，规模中，临时建起的一片平房，内是大量的小房间，过道狭窄，光线昏暗，视野受限，
//      并且物资分散在每一个房间中，一同在房间中的还有开门跳脸的丧尸。」
//
// 🔴 **这一关是那套光照/视野系统的第二次、也是最直接的一次派上用场。**
//    「光线昏暗」不是美术调色，是 <see cref="VisionLogic.IndoorsDarkAmbient"/>（0.10）——
//    <see cref="ExplorationLighting.IsIndoorsDark"/> 把这一关标成室内恒暗，压过昼夜相位。
//    代入 <see cref="VisionLogic.ConeFor"/>：视距 300 → **约 124**，半角 60° → **33°**。
//    ⇒ **「视野受限」是算出来的，不是画出来的**：你的视野只剩一个 124px 的窄锥，
//      而一条过道有 1200px 长 —— **你看不见过道的另一头，更看不见门后有什么。**
//    ⇒ 手电（<see cref="HeldLightState"/>）在这里第一次成为一个**真选择**，而它是有代价的：
//      占一只手 ⇒ **双手武器与光源互斥**（<c>HeldLightState.BlocksWeaponEquip</c>）⇒ 举着手电你只能用单手武器；
//      且黑暗中持光 ⇒ 被发现的距离按 <c>VisionLogic.ExposureRangeMultiplier</c> 放大。
//      **看得见，还是打得动 —— 二选一。** 这三条规则引擎里早就有了，这一关只是第一次让它们真的疼。
//
// 🔴 **「过道狭窄」是玩家的朋友，不是敌人 —— 这一条是这关设计的核心。**
//    docs/research/2026-07-14-lanchester.md 实测：丧尸与幸存者**零碰撞**，开阔地几何上能围 **16 只**；
//    而围攻是**断崖不是斜坡**：1 只＝胜率 82.6%，2 只＝**16.6%**，3 只＝**0.8%**，4 只起＝**0%**。
//    ⇒ 在旷野被围＝必死。而在这里：
//      · <b>房门宽 <see cref="DoorwayWidth"/>＝48</b> &lt; 2×丧尸直径(26)＝52 ⇒ **门口一次只过得来一只**
//        ⇒ **卡在门口＝把围攻打成 1v1（82.6%）**。这是这一关唯一正确的战术，几何上有保证。
//      · <b>过道宽 <see cref="CorridorWidth"/>＝72</b> &lt; 3×26＝78 ⇒ 过道里**最多两只并排**
//        ⇒ 退到过道里比退到旷野强，但**仍是 16.6% 的死局** ⇒ **别在过道里打，退到门口去打。**
//    「窄」在这里是**战术空间**，不是难度旋钮。
//
// 🔴 **「开门跳脸」是机制，不是脚本。**
//    每扇房门初始**关着**；门＝墙层（<see cref="VisionOcclusion.WallMask"/>）⇒ **它挡视线**。
//    ⇒ 站在门外，到房里那只丧尸的视线**被门板挡死**（可见数 0）；且就算门开着，
//      黑暗中你的视距也只有 124px —— **你没有任何办法提前知道门后有没有东西。**
//    ⇒ 推开门的那一刻，它已经在 <see cref="AmbushDistance"/>（≤90px）之内 ——
//      比丧尸的攻击距离（50px）只远 40px。**这就是"跳脸"。** 两条都上了单测。
//
// 🔴 **物资分散在每一个房间里 ⇒ 代价是"你得开 14 扇门"。**
//    14 处搜刮点分在 **14 个不同的房间**（单测钉死：没有任何一个房间放两处）。
//    每处**量都不大**——难民随身带的东西：吃的、布、绷带、一点白银。**没有枪**。
//    这一关的回报不是某个大堆，是**十四次"要不要推开这扇门"**。

/// <summary>难民营地的一间平房（一个房间＝一个盲盒：一扇门、可能一处物资、可能一只贴脸的丧尸）。</summary>
/// <param name="Rect">房间外轮廓。</param>
/// <param name="DoorEdge">唯一那扇门所在的边（**恰好一扇**——房间是死胡同，这正是"卡门口"成立的前提）。</param>
/// <param name="DoorName">门名（关内可交互门的键）。</param>
public readonly record struct RefugeeRoom(int Number, WallRect Rect, RoomEdge DoorEdge, string DoorName)
{
    /// <summary>门洞中心沿该墙轴向的坐标（横墙＝X，竖墙＝Y）。</summary>
    public float DoorCenter => DoorEdge is RoomEdge.Top or RoomEdge.Bottom
        ? Rect.X + Rect.Width / 2f
        : Rect.Y + Rect.Height / 2f;
}

/// <summary>难民营地的一处搜刮点（id 与 <see cref="ExplorationCache"/> 一致；<paramref name="RoomNumber"/> 指明它在哪间房）。</summary>
public readonly record struct RefugeeCacheSpot(string Id, int RoomNumber, float X, float Y, string Label);

/// <summary>一只**开门跳脸**的丧尸：待在 <paramref name="RoomNumber"/> 号房里，门后 <see cref="RefugeeCamp.AmbushDistance"/> 之内。</summary>
public readonly record struct AmbushZombie(int RoomNumber, float X, float Y);

/// <summary>
/// 哪些探索关是「室内恒暗」（压过昼夜相位）。<b>单一事实源</b>——运行时（TestExploration 的环境光/光照采样）
/// 与单测（视野数字护栏）读的是同一个判据，不会各写一份。
/// </summary>
public static class ExplorationLighting
{
    /// <summary>
    /// 该目的地是否室内恒暗（<see cref="VisionLogic.AmbientLight"/> 的 <c>indoorsDark</c> 入参）。
    /// 当前只有<b>难民营地</b>：用户原话「光线昏暗，视野受限」。
    /// <para>
    /// ⚠️ 此前 <c>TestExploration</c> 的三处环境光调用**一律硬编码 <c>indoorsDark: false</c>** ——
    /// 也就是说 <see cref="VisionLogic.IndoorsDarkAmbient"/> 这条常量写好了却**从来没有任何一关用过**。
    /// 这里是它第一次真的接上线。
    /// </para>
    /// </summary>
    public static bool IsIndoorsDark(string? destinationName)
        => destinationName == RefugeeCamp.DestinationName
        // [T61] **下水道**：用户原话「主要靠**黑暗**逼仄的环境…吓人」⇒ 它是本项目第二个室内恒暗关。
        // 恒暗 ⇒ 环境光压到 VisionLogic.IndoorsDarkAmbient(0.10) ⇒ 视锥缩到 ~124px / 半角 30°
        // ⇒ **玩家基本上必须手持光源**（HeldLightState 占一只手 ⇒ 与双手武器互斥 ⇒「要么看得见，要么打得动」）。
        || destinationName == ExplorationCache.SewerName;
}

/// <summary>难民营地的关卡几何（纯逻辑）：一片规整的排屋 + 一条窄脊 + 两条窄横道。</summary>
public static class RefugeeCamp
{
    /// <summary>目的地路由键（须与 world_graph.json / ExplorationCache / WorldMapPanel 一字不差）。</summary>
    public const string DestinationName = "难民营地";

    // ── 尺寸 ──（关卡 2400×1600）
    /// <summary>营区外墙厚。</summary>
    public const float WallThickness = 16f;
    /// <summary>平房墙厚（薄板墙——临时搭的）。</summary>
    public const float RoomWallThickness = 8f;

    /// <summary>
    /// 🔴 <b>房门宽 48</b>。这个数是算出来的，不是拍的：
    /// 丧尸半径 13（<c>Zombie.cs</c>）⇒ 直径 26 ⇒ **48 &lt; 52 ⇒ 门口一次只塞得下一只**
    /// ⇒ 卡门口＝把围攻打成 1v1。下限则是导航体：48 &gt; 2×14＝28 ⇒ 人过得去。
    /// <b>改这个数之前先读 <c>docs/research/2026-07-14-lanchester.md</c>：2 只围攻胜率就只剩 16.6%。</b>
    /// </summary>
    public const float DoorwayWidth = 48f;

    /// <summary>
    /// 🔴 <b>过道宽 72</b>：72 &lt; 3×26＝78 ⇒ 过道里最多**两只并排**（旷野是 16 只）。
    /// 「窄」是玩家的战术空间——但两只仍是 16.6% 的死局，**过道只是比旷野强，不是安全**。
    /// </summary>
    public const float CorridorWidth = 72f;

    /// <summary>
    /// 🔴 <b>跳脸距离上限 90px</b>：推开门时丧尸离你不超过这么远。
    /// 参照：丧尸攻击距离 50px（24+13+13）；黑暗中你的视距只有约 124px（见类注）。
    /// ⇒ 90 &lt; 124 ⇒ **门一开，它当场就在你视野里**；90 − 50 = 40px ⇒ **它一步就够着你**。
    /// </summary>
    public const float AmbushDistance = 90f;

    private const float Left = 260f;
    private const float Right = 2140f;
    private const float Top = 140f;
    private const float Bottom = 1420f;

    // ── 过道（一条竖脊 + 两条横道；宽度一律 CorridorWidth）──
    /// <summary>竖脊（主过道）西缘 X。</summary>
    public const float SpineLeft = 1164f;
    /// <summary>竖脊东缘 X。</summary>
    public const float SpineRight = SpineLeft + CorridorWidth; // 1236

    /// <summary>北横道（上缘 Y）。</summary>
    public const float CorridorNorthY = 500f;
    /// <summary>南横道（上缘 Y）。</summary>
    public const float CorridorSouthY = 950f;

    // ── 入口（**两个都关不上** ⇒ 退路永远在）──
    /// <summary>营门（南墙，正对竖脊）。</summary>
    public const float GateX = 1200f;
    /// <summary>西侧铁丝网豁口（西墙，正对北横道）。</summary>
    public const float WestBreachY = CorridorNorthY + CorridorWidth / 2f; // 536

    /// <summary>
    /// 营区外墙的两个入口，<b>两个都是关不上的洞</b>：营门（南·正对竖脊）与西侧铁丝网豁口（西·正对北横道）。
    /// 关内 18 扇门**全是房门**（死胡同），一扇也不在退路上 ⇒ 无论你把多少扇门关死，都走得出去。
    /// </summary>
    public static IReadOnlyList<HospitalEntrance> Entrances() => new[]
    {
        new HospitalEntrance("营门", null),
        new HospitalEntrance("西侧豁口", null),
    };

    /// <summary>营门内侧站位（进关第一步）。</summary>
    public static readonly (float X, float Y) GatePoint = (GateX, 1350f);

    /// <summary>
    /// 18 间平房。三排（南→北＝近→深）× 六列（竖脊两侧各三）。**每间恰好一扇门**，且门一律开向过道。
    /// <para>南排门朝北（开向南横道）、中排门朝南/北交替、北排门朝南（开向北横道）——过道是唯一的路。</para>
    /// </summary>
    public static readonly IReadOnlyList<RefugeeRoom> Rooms = BuildRooms();

    private static IReadOnlyList<RefugeeRoom> BuildRooms()
    {
        var rooms = new List<RefugeeRoom>(18);
        // 六列的左缘（竖脊两侧各三，列宽 296）
        float[] colX = { 276f, 572f, 868f, 1236f, 1532f, 1828f };
        const float colW = 296f;

        int n = 1;

        // 南排（近，y 1022..1404）：门朝北（开向南横道）
        foreach (float x in colX)
            rooms.Add(new RefugeeRoom(n, new WallRect(x, 1022f, colW, 382f), RoomEdge.Top, DoorNameOf(n++)));

        // 中排（y 572..950）：门朝北/朝南交替 —— 两条横道都得走
        for (int i = 0; i < colX.Length; i++)
        {
            RoomEdge edge = i % 2 == 0 ? RoomEdge.Bottom : RoomEdge.Top;
            rooms.Add(new RefugeeRoom(n, new WallRect(colX[i], 572f, colW, 378f), edge, DoorNameOf(n++)));
        }

        // 北排（最深，y 156..500）：门朝南（开向北横道）
        foreach (float x in colX)
            rooms.Add(new RefugeeRoom(n, new WallRect(x, 156f, colW, 344f), RoomEdge.Bottom, DoorNameOf(n++)));

        return rooms;
    }

    /// <summary>N 号房门的门名（关内可交互门的键）。</summary>
    public static string DoorNameOf(int roomNumber) => $"{roomNumber} 号房门";

    /// <summary>按房号取房间（不存在则抛——房号是代码常量，取不到就是写错了）。</summary>
    public static RefugeeRoom Room(int number)
    {
        foreach (RefugeeRoom r in Rooms)
        {
            if (r.Number == number)
                return r;
        }
        throw new KeyNotFoundException($"难民营地没有 {number} 号房");
    }

    /// <summary>
    /// 营区全部墙段（外墙 + 18 间平房的轮廓）。<b>不含门板</b>——门板是 <see cref="Doors"/>。
    /// 过道不用砌墙：它就是房间与房间之间剩下的那条缝（宽 <see cref="CorridorWidth"/>）。
    /// </summary>
    public static IReadOnlyList<WallRect> Walls()
    {
        const float t = WallThickness;
        var walls = new List<WallRect>(80);

        // ── 外墙（两个入口处断开）──
        walls.Add(new WallRect(Left, Top, Right - Left, t));                                   // 北墙（实心）
        walls.Add(new WallRect(Right - t, Top, t, Bottom - Top));                              // 东墙（实心）
        AddSplit(walls, new WallRect(Left, Top, t, Bottom - Top), false, WestBreachY);         // 西墙：铁丝网豁口
        AddSplit(walls, new WallRect(Left, Bottom - t, Right - Left, t), true, GateX);         // 南墙：营门

        // ── 18 间平房的轮廓（各留一处 48px 的门洞）──
        foreach (RefugeeRoom r in Rooms)
        {
            foreach (WallRect w in ExplorationWalls.RoomOutlineWalls(
                         r.Rect, RoomWallThickness, DoorwayWidth, r.DoorEdge))
            {
                walls.Add(w);
            }
        }

        return walls;
    }

    /// <summary>
    /// 18 扇房门，**初始全部关着**。这是"开门跳脸"成立的前提：门＝墙层 ⇒ 门挡视线 ⇒
    /// **你没有任何办法提前知道门后有什么**（黑暗里视距只有 124px，也帮不了你）。
    /// <para>门**都没上锁**：进了房间也出得来。</para>
    /// </summary>
    public static IReadOnlyList<ExplorationDoor> Doors()
    {
        const float t = RoomWallThickness;
        const float w = DoorwayWidth;
        var doors = new List<ExplorationDoor>(Rooms.Count);

        foreach (RefugeeRoom r in Rooms)
        {
            WallRect panel = r.DoorEdge switch
            {
                RoomEdge.Top => new WallRect(r.DoorCenter - w / 2f, r.Rect.Y, w, t),
                RoomEdge.Bottom => new WallRect(r.DoorCenter - w / 2f, r.Rect.Bottom - t, w, t),
                RoomEdge.Left => new WallRect(r.Rect.X, r.DoorCenter - w / 2f, t, w),
                _ => new WallRect(r.Rect.Right - t, r.DoorCenter - w / 2f, t, w),
            };
            doors.Add(new ExplorationDoor(r.DoorName, panel, DoorState.Closed));
        }

        return doors;
    }

    /// <summary>某房间**门外**（过道一侧）的站位：推开这扇门之前，你就站在这儿。</summary>
    public static (float X, float Y) OutsideDoorPoint(RefugeeRoom r, float standoff = 26f) => r.DoorEdge switch
    {
        RoomEdge.Top => (r.DoorCenter, r.Rect.Y - standoff),
        RoomEdge.Bottom => (r.DoorCenter, r.Rect.Bottom + standoff),
        RoomEdge.Left => (r.Rect.X - standoff, r.DoorCenter),
        _ => (r.Rect.Right + standoff, r.DoorCenter),
    };

    /// <summary>某房间门外站位的**朝向**（面向房间；VisionLogic 的锥形要它）。</summary>
    public static (float X, float Y) FacingIntoRoom(RefugeeRoom r) => r.DoorEdge switch
    {
        RoomEdge.Top => (0f, 1f),
        RoomEdge.Bottom => (0f, -1f),
        RoomEdge.Left => (1f, 0f),
        _ => (-1f, 0f),
    };

    /// <summary>
    /// 14 处搜刮点，**分在 14 个不同的房间里**（单测钉死：没有任何一间房放两处）。
    /// 🔴 每处量都不大——难民随身带的东西：吃的、布、绷带、一点白银。**没有枪，没有护甲。**
    /// 这一关的回报不是某个大堆，是**十四次"要不要推开这扇门"**。
    /// </summary>
    public static readonly IReadOnlyList<RefugeeCacheSpot> CacheSpots = new[]
    {
        // 南排（近，5）—— 1~6 号房
        new RefugeeCacheSpot(ExplorationCache.RefugeeCotRowId, 1, 424f, 1240f, "行军床铺"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeLuggagePileId, 2, 720f, 1240f, "行李堆"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeStoveId, 3, 1016f, 1250f, "煤油炉"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeWaterDrumId, 4, 1384f, 1240f, "水桶"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeRationCrateId, 5, 1680f, 1250f, "配给箱"),

        // 中排（中，5）—— 7~12 号房
        new RefugeeCacheSpot(ExplorationCache.RefugeeSickRoomId, 7, 424f, 760f, "隔离房"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeChildRoomId, 8, 720f, 770f, "孩子的房间"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeToolCornerId, 9, 1016f, 760f, "工具角"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeClothesLineId, 10, 1384f, 770f, "晾衣绳"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeSuitcaseId, 11, 1680f, 760f, "摞起来的手提箱"),

        // 北排（最深，4）—— 13~18 号房
        new RefugeeCacheSpot(ExplorationCache.RefugeeRegistryDeskId, 13, 424f, 330f, "登记台"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeStorageRoomId, 15, 1016f, 330f, "物资库房"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeGeneratorId, 16, 1384f, 340f, "发电机房"),
        new RefugeeCacheSpot(ExplorationCache.RefugeeGuardPostId, 17, 1680f, 330f, "值守间"),
    };

    /// <summary>
    /// 🔴 **10 只开门跳脸的丧尸** —— 每只都待在一间房里、门后 <see cref="AmbushDistance"/> 之内。
    /// 门关着时你看不见它（门挡视线，单测钉死可见数 0）；门一开，它已经在你脸上（≤90px，单测钉死）。
    /// <para>刻意**不是每间房都有**（18 间房 10 只）——如果每扇门后都有，玩家会学会"开门即后撤"，
    /// 那就又变回脚本了。**不知道哪扇门后有**，才是这一关的全部。</para>
    /// </summary>
    public static readonly IReadOnlyList<AmbushZombie> AmbushZombies = new[]
    {
        // 南排 1/3/5/6 号房：门朝北 ⇒ 丧尸贴在房间**北侧**（就在门内）
        new AmbushZombie(1, 424f, 1080f),
        new AmbushZombie(3, 1016f, 1078f),
        new AmbushZombie(5, 1680f, 1076f),
        new AmbushZombie(6, 1976f, 1082f),   // 6 号房**没有物资**——纯粹的一扇门，后面只有它
        // 中排 7/9 号房：门朝南 ⇒ 丧尸贴在房间**南侧**
        new AmbushZombie(7, 424f, 894f),
        new AmbushZombie(9, 1016f, 892f),
        // 中排 10/12 号房：门朝北
        new AmbushZombie(10, 1384f, 630f),
        new AmbushZombie(12, 1976f, 628f),   // 12 号房也没有物资
        // 北排 13/16 号房：门朝南
        new AmbushZombie(13, 424f, 444f),
        new AmbushZombie(16, 1384f, 442f),
    };

    /// <summary>过道里游荡的 4 只（**别在过道里跟它们打**：过道能并排两只＝16.6% 的死局。退到门口去）。</summary>
    public static readonly IReadOnlyList<(float X, float Y)> CorridorZombieSpots = new[]
    {
        (1200f, 1180f),   // 竖脊·南段
        (620f, 536f),     // 北横道·西段
        (1900f, 986f),    // 南横道·东段
        (1200f, 320f),    // 竖脊·北段（最深）
    };

    /// <summary>一条边按一处门洞中心断开（外墙用）。</summary>
    private static void AddSplit(List<WallRect> walls, WallRect edge, bool horizontal, float center)
    {
        float half = CorridorWidth / 2f;
        if (horizontal)
        {
            float segA = (center - half) - edge.X;
            if (segA > 0f)
                walls.Add(new WallRect(edge.X, edge.Y, segA, edge.Height));
            float bStart = center + half;
            if (edge.Right > bStart)
                walls.Add(new WallRect(bStart, edge.Y, edge.Right - bStart, edge.Height));
        }
        else
        {
            float segA = (center - half) - edge.Y;
            if (segA > 0f)
                walls.Add(new WallRect(edge.X, edge.Y, edge.Width, segA));
            float bStart = center + half;
            if (edge.Bottom > bStart)
                walls.Add(new WallRect(edge.X, bStart, edge.Width, edge.Bottom - bStart));
        }
    }
}
