using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 尸体不堆叠推挤（RimWorld 式）：尸体<b>无碰撞体积</b>（活人/丧尸从尸体上走过去），但<b>同一「尸体格」只放一具</b>——
/// 新尸体落在已占格上时自动挤到旁边最近的空格。本作是连续像素坐标（非格子制），故「一格」= 尸体专用概念格
/// <see cref="CorpseField.CellSize"/>，只用于落位去重，<b>不参与寻路/物理</b>。
/// </summary>
public class CorpsePushTests
{
    private const float Cell = CorpseField.CellSize;

    /// <summary>处处可通行（无墙）。</summary>
    private static bool Anywhere(Vector2 _) => true;

    /// <summary>格中心（用格坐标直接取世界点，避免测试里手算像素）。</summary>
    private static Vector2 Center(int cx, int cy) => CorpseField.CenterOf(new CorpseCell(cx, cy));

    // ── 换算：连续坐标 → 概念格（负坐标必须 floor，不能截断）─────────────────

    [Theory]
    [InlineData(0f, 0f, 0, 0)]
    [InlineData(31.9f, 31.9f, 0, 0)]
    [InlineData(32f, 0f, 1, 0)]
    [InlineData(-1f, -1f, -1, -1)]     // 截断会错成 (0,0)
    [InlineData(-32f, -33f, -1, -2)]
    public void CellOf_FloorsIncludingNegatives(float x, float y, int cx, int cy)
    {
        Assert.Equal(new CorpseCell(cx, cy), CorpseField.CellOf(new Vector2(x, y)));
    }

    [Fact]
    public void CenterOf_RoundTripsBackToSameCell()
    {
        var c = new CorpseCell(-3, 7);
        Assert.Equal(c, CorpseField.CellOf(CorpseField.CenterOf(c)));
    }

    // ── 基本落位 ────────────────────────────────────────────────────────────

    [Fact]
    public void FirstCorpse_LandsOnItsOwnCell_NotDisplaced()
    {
        var field = new CorpseField();
        var p = field.Place(new Vector2(100f, 100f), Anywhere);

        Assert.Equal(CorpseField.CellOf(new Vector2(100f, 100f)), p.Cell);
        Assert.False(p.Displaced);
        Assert.False(p.Stacked);
        Assert.Equal(CorpseField.CenterOf(p.Cell), p.Position);
        Assert.Equal(1, field.Count);
    }

    [Fact]
    public void SecondCorpseOnSameCell_IsPushedToAdjacentCell()
    {
        var field = new CorpseField();
        var home = field.Place(Center(5, 5), Anywhere);
        var second = field.Place(Center(5, 5), Anywhere);   // 同一格再死一个

        Assert.NotEqual(home.Cell, second.Cell);
        Assert.True(second.Displaced);
        Assert.False(second.Stacked);
        // 挤到「旁边」= 切比雪夫距离恰好 1（第一环）
        Assert.Equal(1, Math.Max(Math.Abs(second.Cell.X - 5), Math.Abs(second.Cell.Y - 5)));
        Assert.Equal(2, field.Count);
    }

    [Fact]
    public void PushPrefersNearestEmptyCell_OrthogonalBeforeDiagonal()
    {
        var field = new CorpseField();
        // 家格 + 四个正交邻居全占满 → 只剩对角可用（对角距离 √2 > 1，必须排在正交之后）
        field.Place(Center(0, 0), Anywhere);
        field.Place(Center(1, 0), Anywhere);
        field.Place(Center(-1, 0), Anywhere);
        field.Place(Center(0, 1), Anywhere);
        field.Place(Center(0, -1), Anywhere);

        var p = field.Place(Center(0, 0), Anywhere);

        Assert.True(p.Displaced);
        Assert.Equal(1, Math.Abs(p.Cell.X));   // 对角
        Assert.Equal(1, Math.Abs(p.Cell.Y));
    }

    [Fact]
    public void RingOne_FullyOccupied_PushesToSecondRing()
    {
        var field = new CorpseField();
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            field.Place(Center(dx, dy), Anywhere);   // 3x3 全满

        var p = field.Place(Center(0, 0), Anywhere);

        Assert.True(p.Displaced);
        Assert.Equal(2, Math.Max(Math.Abs(p.Cell.X), Math.Abs(p.Cell.Y)));  // 第二环
        Assert.False(p.Stacked);
    }

    // ── 不可通行区（墙/水）：绝不把尸体挤进去 ───────────────────────────────

    [Fact]
    public void NeverPushedIntoImpassableCell()
    {
        var field = new CorpseField();
        // 只有 x>=0 一侧可通行（x<0 是墙）
        bool Passable(Vector2 w) => w.X >= 0f;

        field.Place(Center(0, 0), Passable);
        for (int i = 0; i < 12; i++)
        {
            var p = field.Place(Center(0, 0), Passable);
            Assert.True(p.Position.X >= 0f, $"尸体被挤进墙里: {p.Cell}");
            Assert.True(p.Cell.X >= 0);
        }
    }

    [Fact]
    public void DeathInsideImpassableCell_IsPushedOutToPassableGround()
    {
        var field = new CorpseField();
        bool Passable(Vector2 w) => w.X >= 0f;   // 左半边是墙

        // 死在墙格里（如贴墙被打死、坐标被推入墙体）→ 也要挤到可通行地面上
        var p = field.Place(Center(-1, 3), Passable);

        Assert.True(p.Displaced);
        Assert.True(p.Cell.X >= 0);
    }

    // ── 周围全满：允许最后堆叠，但绝不落进墙、绝不丢尸 ───────────────────────

    [Fact]
    public void AllRingsFull_StacksOnNearestPassableCell_RatherThanVanishing()
    {
        var field = new CorpseField();
        int r = CorpseField.MaxSearchRing;
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
            field.Place(Center(dx, dy), Anywhere);

        int before = field.Count;
        var p = field.Place(Center(0, 0), Anywhere);

        Assert.True(p.Stacked);                       // 搜索半径内确实无空位 → 退化为堆叠
        Assert.Equal(new CorpseCell(0, 0), p.Cell);   // 堆在自己脚下（最近的可通行格）
        Assert.Equal(before, field.Count);            // 已占格不重复计数
    }

    [Fact]
    public void HomeImpassableAndAllRingsFull_StacksOnNearestPassableCell_NotInsideWall()
    {
        var field = new CorpseField();
        bool Passable(Vector2 w) => w.X >= 0f;
        int r = CorpseField.MaxSearchRing;
        for (int dx = 0; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
            field.Place(Center(dx, dy), Passable);    // 可通行的半边全占满

        var p = field.Place(Center(-1, 0), Passable); // 死在墙里且外面没空位

        Assert.True(p.Stacked);
        Assert.True(p.Cell.X >= 0, "宁可堆叠也不能落进墙里");
    }

    // ── 确定性（同输入 → 同输出；无随机）─────────────────────────────────────

    [Fact]
    public void Placement_IsDeterministic_SameInputSameOutput()
    {
        List<CorpseCell> Run()
        {
            var field = new CorpseField();
            var seq = new List<CorpseCell>();
            for (int i = 0; i < 40; i++)
                seq.Add(field.Place(Center(3, 3), Anywhere).Cell);   // 全部死在同一格
            return seq;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Resolve_IsPure_DoesNotRegisterOccupancy()
    {
        var field = new CorpseField();
        var a = field.Resolve(Center(2, 2), Anywhere);
        var b = field.Resolve(Center(2, 2), Anywhere);

        Assert.Equal(a.Cell, b.Cell);      // 只算不登记 → 两次同解
        Assert.Equal(0, field.Count);
    }

    // ── 尸体清理：释放格位（焚烧/搬走后原地可以再躺人）──────────────────────

    [Fact]
    public void Remove_FreesCellForReuse()
    {
        var field = new CorpseField();
        var first = field.Place(Center(9, 9), Anywhere);
        field.Remove(first.Cell);

        var again = field.Place(Center(9, 9), Anywhere);
        Assert.Equal(first.Cell, again.Cell);
        Assert.False(again.Displaced);
    }

    // ── 尸体无碰撞体积：CorpseField 只管「尸体格」，不提供任何通行/阻挡查询 ──
    // 通行性是**注入**的（passable 由 Godot 导航层给），CorpseField 从不写它——
    // 这是「尸体不阻挡移动」在纯逻辑层的结构性保证。

    [Fact]
    public void CorpseField_ExposesNoBlockingQuery_CorpsesAreNotObstacles()
    {
        var names = typeof(CorpseField).GetMembers()
            .Select(m => m.Name)
            .Where(n => n.Contains("Block", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Solid", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Obstacle", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Passable", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(names);   // 尸体格只影响尸体落位，任何人都能从尸体上走过去
    }

    // ── 性能：落一具尸体只看家格周围的环，绝不遍历全场尸体 ────────────────────

    [Fact]
    public void Place_DoesNotScanWholeField_ProbeCountBoundedByRingArea()
    {
        var field = new CorpseField();
        // 先铺 800 具（尸潮后满地是尸体的量级），散布在一片区域
        var rnd = new Random(1234);
        for (int i = 0; i < 800; i++)
            field.Place(new Vector2(rnd.Next(0, 2000), rnd.Next(0, 2000)), Anywhere);

        int probes = 0;
        bool Counting(Vector2 _) { probes++; return true; }
        field.Place(new Vector2(1000f, 1000f), Counting);

        int r = CorpseField.MaxSearchRing;
        int worstCase = (2 * r + 1) * (2 * r + 1);   // 搜索窗上限，与场上尸体总数无关
        Assert.InRange(probes, 1, worstCase);
        Assert.True(field.Count >= 700);             // 场上确实一大堆尸体
    }

    [Fact]
    public void ManyCorpsesInOneSpot_AllDistinctCells_AndFast()
    {
        var field = new CorpseField();
        var cells = new HashSet<CorpseCell>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 最坏形态：500 只丧尸全死在同一个门口（每次都要外扩找空位）
        for (int i = 0; i < 500; i++)
            cells.Add(field.Place(Center(20, 20), Anywhere).Cell);

        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 200, $"500 具堆门口耗时 {sw.ElapsedMilliseconds}ms");
        // 搜索窗 (2r+1)^2 内的格位被填满后开始堆叠，故不同格数 = 搜索窗容量上限
        int r = CorpseField.MaxSearchRing;
        Assert.Equal((2 * r + 1) * (2 * r + 1), cells.Count);
    }
}
