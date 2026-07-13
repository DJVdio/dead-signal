using System.Collections.Generic;
using System.Linq;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 探索关墙体几何（<see cref="ExplorationWalls"/>）：锁屋 / 房间轮廓的墙段矩形。
/// 这些矩形是 TestExploration 里 StaticBody2D+RectangleShape2D 的**唯一几何源**，
/// 同一批矩形同时充当：① 碰撞体（挡路）② 导航 obstruction（阻断寻路）③ 墙层 0b0100 射线遮挡（挡视线）。
/// 故「线段穿不穿墙」的纯逻辑断言＝对这三件事的共同证明（射线遮挡与碰撞几何同源）。
/// </summary>
public class ExplorationWallsTests
{
    // 南林村庄锁屋的实参（与 TestExploration.VillageHouseCenter/HalfW/HalfH 对齐）
    private const float Cx = 1200f, Cy = 520f, Hw = 170f, Hh = 130f;

    private static IReadOnlyList<WallRect> LockedHouse()
        => ExplorationWalls.LockedHouseWalls(Cx, Cy, Hw, Hh);

    // ── 锁屋：四面墙 + 南墙一处门洞 ──────────────────────────────────────────

    [Fact]
    public void LockedHouse_WallsAreNonDegenerate()
    {
        IReadOnlyList<WallRect> walls = LockedHouse();
        Assert.NotEmpty(walls);
        Assert.All(walls, w =>
        {
            Assert.True(w.Width > 0f, "墙段宽须为正");
            Assert.True(w.Height > 0f, "墙段高须为正");
        });
    }

    /// <summary>屋外四向射向屋心，除南门方向外一律穿墙（＝墙真的围住了屋子）。</summary>
    [Fact]
    public void LockedHouse_BlocksLineFromOutsideOnEverySideButDoor()
    {
        IReadOnlyList<WallRect> walls = LockedHouse();

        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, Cx, Cy - Hh - 200f, Cx, Cy), "北面射入应被北墙挡住");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, Cx - Hw - 200f, Cy, Cx, Cy), "西面射入应被西墙挡住");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, Cx + Hw + 200f, Cy, Cx, Cy), "东面射入应被东墙挡住");
        // 南面：门洞左右两侧的墙段仍要挡（取门洞外侧一段横坐标）
        float offDoorX = Cx - ExplorationWalls.LockedHouseDoorHalfWidth - 40f;
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, offDoorX, Cy + Hh + 200f, offDoorX, Cy), "南墙门洞外侧应被挡住");
    }

    /// <summary>门洞是唯一通路：正对门缺口射入屋心，不碰任何墙。</summary>
    [Fact]
    public void LockedHouse_DoorGapIsTheOnlyOpening()
    {
        IReadOnlyList<WallRect> walls = LockedHouse();
        Assert.False(ExplorationWalls.SegmentHitsAnyWall(walls, Cx, Cy + Hh + 200f, Cx, Cy), "门洞正中射入不应被挡");
    }

    /// <summary>四角不得有对角漏洞（旧几何西北/西南角各漏一个 t×t 方孔，丧尸可斜穿）。</summary>
    [Fact]
    public void LockedHouse_CornersAreSealed()
    {
        IReadOnlyList<WallRect> walls = LockedHouse();
        const float t = ExplorationWalls.LockedHouseWallThickness;

        // 四条对角线：从角外侧斜穿向屋内角，必被墙挡
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, Cx - Hw - t - 30f, Cy - Hh - t - 30f, Cx - Hw + 30f, Cy - Hh + 30f), "西北角不得漏");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, Cx - Hw - t - 30f, Cy + Hh + t + 30f, Cx - Hw + 30f, Cy + Hh - 30f), "西南角不得漏");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, Cx + Hw + t + 30f, Cy - Hh - t - 30f, Cx + Hw - 30f, Cy - Hh + 30f), "东北角不得漏");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, Cx + Hw + t + 30f, Cy + Hh + t + 30f, Cx + Hw - 30f, Cy + Hh - 30f), "东南角不得漏");
    }

    /// <summary>门洞宽度＝2×半宽（导航 AgentRadius 14 下仍须能过人）。</summary>
    [Fact]
    public void LockedHouse_DoorGapWideEnoughForNavAgent()
    {
        float gap = ExplorationWalls.LockedHouseDoorHalfWidth * 2f;
        Assert.True(gap - ExplorationWalls.NavAgentRadius * 2f > 0f, "门洞减去两侧 agent 半径后须仍有通道");
    }

    // ── 房间轮廓：四边内嵌，门边留洞 ────────────────────────────────────────

    [Fact]
    public void RoomOutline_DoorEdgeIsSplitAndOthersAreSolid()
    {
        var room = new WallRect(900f, 700f, 500f, 340f);
        IReadOnlyList<WallRect> walls = ExplorationWalls.RoomOutlineWalls(room, RoomEdge.Bottom);

        float cx = room.X + room.Width / 2f;
        // 南（门）边正中射入 → 通
        Assert.False(ExplorationWalls.SegmentHitsAnyWall(walls, cx, room.Bottom + 100f, cx, room.Y + room.Height / 2f), "门洞应通");
        // 其余三边射入 → 挡
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, cx, room.Y - 100f, cx, room.Y + room.Height / 2f), "北墙应挡");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, room.X - 100f, room.Y + room.Height / 2f, cx, room.Y + room.Height / 2f), "西墙应挡");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, room.Right + 100f, room.Y + room.Height / 2f, cx, room.Y + room.Height / 2f), "东墙应挡");
        // 门边偏离门洞处仍挡
        float offDoorX = room.X + 40f;
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, offDoorX, room.Bottom + 100f, offDoorX, room.Y + room.Height / 2f), "门边非门洞段应挡");
    }

    /// <summary>可多门边：南丁格尔小药店须 Bottom(临街外门) + Top(通后屋药房) 双洞，否则后屋被封死。</summary>
    [Fact]
    public void RoomOutline_SupportsMultipleDoorEdges()
    {
        var shop = new WallRect(900f, 700f, 500f, 340f);
        IReadOnlyList<WallRect> walls = ExplorationWalls.RoomOutlineWalls(shop, RoomEdge.Bottom, RoomEdge.Top);

        float cx = shop.X + shop.Width / 2f;
        float inside = shop.Y + shop.Height / 2f;
        Assert.False(ExplorationWalls.SegmentHitsAnyWall(walls, cx, shop.Y - 100f, cx, inside), "北门洞应通（通后屋药房）");
        Assert.False(ExplorationWalls.SegmentHitsAnyWall(walls, cx, shop.Bottom + 100f, cx, inside), "南门洞应通（临街）");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(walls, shop.X - 100f, inside, cx, inside), "西墙仍应挡");
    }

    /// <summary>
    /// 药店实际布局回归：后屋药房(南门) → 小药店(北门) 的两处门洞须**横向重叠**且宽到能过导航体，
    /// 否则后屋药房三面实心 + 南门顶死小药店北墙 = 玩家永远进不去（既有布局的真实通行性缺陷）。
    /// </summary>
    [Fact]
    public void Pharmacy_BackRoomRemainsReachableThroughAlignedDoorGaps()
    {
        var shop = new WallRect(900f, 700f, 500f, 340f);
        var backRoom = new WallRect(1000f, 480f, 320f, 220f);

        IReadOnlyList<WallRect> shopWalls = ExplorationWalls.RoomOutlineWalls(shop, RoomEdge.Bottom, RoomEdge.Top);
        IReadOnlyList<WallRect> backWalls = ExplorationWalls.RoomOutlineWalls(backRoom, RoomEdge.Bottom);
        List<WallRect> all = shopWalls.Concat(backWalls).ToList();

        // 两洞的横向重叠区（后屋门洞 x 中心 1160、小药店门洞 x 中心 1150，各宽 64 → 重叠 [1128,1182]）
        float lo = System.Math.Max(backRoom.X + backRoom.Width / 2f, shop.X + shop.Width / 2f) - ExplorationWalls.RoomDoorWidth / 2f;
        float hi = System.Math.Min(backRoom.X + backRoom.Width / 2f, shop.X + shop.Width / 2f) + ExplorationWalls.RoomDoorWidth / 2f;
        Assert.True(hi - lo > ExplorationWalls.NavAgentRadius * 2f, "两处门洞的重叠通道须宽于导航体直径");

        // 沿重叠区中线，从小药店内部走进后屋药房内部：全程不穿墙
        float lane = (lo + hi) / 2f;
        Assert.False(
            ExplorationWalls.SegmentHitsAnyWall(all, lane, shop.Y + 60f, lane, backRoom.Y + backRoom.Height - 60f),
            "小药店 → 后屋药房 应有一条不穿墙的通路");
    }

    /// <summary>守林人小屋的「屋中屋」：里屋北门开在外屋内部，外屋南门临街 → 两跳可达里屋。</summary>
    [Fact]
    public void RangersCabin_InnerRoomReachableFromOuterRoom()
    {
        var cabin = new WallRect(980f, 520f, 480f, 380f);
        var inner = new WallRect(1180f, 630f, 210f, 200f);

        List<WallRect> all = ExplorationWalls.RoomOutlineWalls(cabin, RoomEdge.Bottom)
            .Concat(ExplorationWalls.RoomOutlineWalls(inner, RoomEdge.Top))
            .ToList();

        float innerCx = inner.X + inner.Width / 2f;
        // 里屋北门正上方（外屋内部）→ 里屋内部：不穿墙
        Assert.False(
            ExplorationWalls.SegmentHitsAnyWall(all, innerCx, inner.Y - 60f, innerCx, inner.Y + inner.Height / 2f),
            "外屋内部 → 里屋 应可通过里屋北门");
        // 里屋南面（外屋内部）射入里屋 → 被里屋南墙挡
        Assert.True(
            ExplorationWalls.SegmentHitsAnyWall(all, innerCx, inner.Bottom + 30f, innerCx, inner.Y + inner.Height / 2f),
            "里屋南墙应挡");
    }

    // ── 线段-矩形相交工具本身 ───────────────────────────────────────────────

    [Fact]
    public void SegmentHitsAnyWall_BasicCases()
    {
        var wall = new[] { new WallRect(0f, 0f, 10f, 100f) };

        Assert.True(ExplorationWalls.SegmentHitsAnyWall(wall, -20f, 50f, 20f, 50f), "横穿应命中");
        Assert.False(ExplorationWalls.SegmentHitsAnyWall(wall, -20f, 50f, -15f, 50f), "线段止于墙外应不命中");
        Assert.False(ExplorationWalls.SegmentHitsAnyWall(wall, -20f, 150f, 20f, 150f), "从墙下方绕过应不命中");
        Assert.True(ExplorationWalls.SegmentHitsAnyWall(wall, 5f, 50f, 5f, 60f), "整段在墙内应命中");
        Assert.False(ExplorationWalls.SegmentHitsAnyWall(System.Array.Empty<WallRect>(), -20f, 50f, 20f, 50f), "无墙则不命中");
    }
}
