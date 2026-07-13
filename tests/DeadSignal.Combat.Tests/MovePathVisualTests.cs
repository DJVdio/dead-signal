using System.Numerics;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

// 移动路径线（RimWorld 式「接下来要走的线路」）的纯几何：
//  RemainingPolyline —— 导航路径点 + 推进下标 + 角色位置 → 还没走的那截折线（起点恒在角色脚下 → 边走边缩短）。
//  Dashes           —— 折线 → 虚线短划段（按弧长切、相位全程连续、拐角处拆段贴着折线走）。
// 绘制本身（iso 投影/配色/终点标记）不在此测；路径数据取自 NavigationAgent2D 真实路径，本类不重算寻路。
public class MovePathVisualTests
{
    private static Vector2 V(float x, float y) => new(x, y);

    // ---- ShouldDraw：画谁（己方全员画，不只选中那个；敌方绝不画）----

    [Fact]
    public void ShouldDraw_PlayerUnitWithLivePath()
    {
        Assert.True(MovePathVisual.ShouldDraw(isPlayerUnit: true, alive: true, hasNavPath: true));
    }

    [Fact]
    public void ShouldDraw_NeverForEnemies()
    {
        // 丧尸/劫掠者的路径是作弊级信息 —— 无论他活着、正在赶路，都不画。
        Assert.False(MovePathVisual.ShouldDraw(isPlayerUnit: false, alive: true, hasNavPath: true));
    }

    [Fact]
    public void ShouldDraw_NotForDeadOrPathless()
    {
        Assert.False(MovePathVisual.ShouldDraw(isPlayerUnit: true, alive: false, hasNavPath: true));  // 死人
        Assert.False(MovePathVisual.ShouldDraw(isPlayerUnit: true, alive: true, hasNavPath: false)); // 站着不动/已到达
    }

    // ---- StrokeFor：选中者更醒目，未选中者克制（同屏 N 条也不糊、不盖住地面信息）----

    [Fact]
    public void StrokeFor_SelectedIsBolderAndMoreOpaque()
    {
        MovePathVisual.Stroke sel = MovePathVisual.StrokeFor(selected: true);
        MovePathVisual.Stroke idle = MovePathVisual.StrokeFor(selected: false);

        Assert.True(sel.Width > idle.Width);
        Assert.True(sel.Alpha > idle.Alpha);
        Assert.True(idle.Alpha < 1f);          // 未选中恒半透明
        Assert.True(idle.Width > 0f && sel.Alpha <= 1f);
    }

    // ---- RemainingPolyline ----

    [Fact]
    public void RemainingPolyline_StartsAtActorAndKeepsUnwalkedPoints()
    {
        var path = new[] { V(0, 0), V(100, 0), V(100, 100), V(200, 100) };
        // 已推进到下标 2：路径点 0/1 已走过，只剩 [2],[3]。
        var line = MovePathVisual.RemainingPolyline(path, 2, V(90, 40));

        Assert.Equal(new[] { V(90, 40), V(100, 100), V(200, 100) }, line);
    }

    [Fact]
    public void RemainingPolyline_ShrinksAsActorAdvances_SameTargetSameEnd()
    {
        var path = new[] { V(0, 0), V(100, 0), V(200, 0) };

        var early = MovePathVisual.RemainingPolyline(path, 1, V(10, 0));
        var later = MovePathVisual.RemainingPolyline(path, 2, V(150, 0));

        // 走得越远，剩余折线越短，但终点不变（还是导航终点）。
        Assert.True(MovePathVisual.Length(later) < MovePathVisual.Length(early));
        Assert.Equal(V(200, 0), early[^1]);
        Assert.Equal(V(200, 0), later[^1]);
    }

    [Fact]
    public void RemainingPolyline_MergesPointCoincidingWithActor()
    {
        // 下一个路径点恰在角色脚下（阈值内）→ 不生成零长首段。
        var path = new[] { V(50, 50), V(150, 50) };
        var line = MovePathVisual.RemainingPolyline(path, 0, V(50.5f, 50f));

        Assert.Equal(new[] { V(50.5f, 50f), V(150, 50) }, line);
    }

    [Fact]
    public void RemainingPolyline_EmptyWhenNoPath()
    {
        Assert.Empty(MovePathVisual.RemainingPolyline(null, 0, V(1, 1)));
        Assert.Empty(MovePathVisual.RemainingPolyline(System.Array.Empty<Vector2>(), 0, V(1, 1)));
    }

    [Fact]
    public void RemainingPolyline_EmptyWhenArrived()
    {
        var path = new[] { V(0, 0), V(100, 0) };
        // 下标越界（走完）→ 只剩角色位置一个点 → 不可画，返回空（调用方据此隐藏路径线）。
        Assert.Empty(MovePathVisual.RemainingPolyline(path, 2, V(100, 0)));
        // 终点就在脚下（合并）→ 同样退化为空。
        Assert.Empty(MovePathVisual.RemainingPolyline(path, 1, V(100, 0)));
    }

    [Fact]
    public void RemainingPolyline_NegativeIndexClampsToStart()
    {
        var path = new[] { V(0, 0), V(100, 0) };
        var line = MovePathVisual.RemainingPolyline(path, -3, V(0, 0));

        Assert.Equal(new[] { V(0, 0), V(100, 0) }, line); // 首点与角色重合被合并
    }

    // ---- Length ----

    [Fact]
    public void Length_SumsSegments()
    {
        var line = new[] { V(0, 0), V(30, 40), V(30, 140) }; // 50 + 100
        Assert.Equal(150f, MovePathVisual.Length(line), 3);
        Assert.Equal(0f, MovePathVisual.Length(new[] { V(5, 5) }));
        Assert.Equal(0f, MovePathVisual.Length(null));
    }

    // ---- Dashes ----

    [Fact]
    public void Dashes_StraightLine_CutsByArcLength()
    {
        var line = new[] { V(0, 0), V(100, 0) };
        var dashes = MovePathVisual.Dashes(line, dash: 20f, gap: 20f);

        // 周期 40：0-20、40-60、80-100 三段。
        Assert.Equal(3, dashes.Count);
        Assert.Equal(V(0, 0), dashes[0].A);
        Assert.Equal(V(20, 0), dashes[0].B);
        Assert.Equal(V(40, 0), dashes[1].A);
        Assert.Equal(V(60, 0), dashes[1].B);
        Assert.Equal(V(80, 0), dashes[2].A);
        Assert.Equal(V(100, 0), dashes[2].B);
    }

    [Fact]
    public void Dashes_PhaseIsContinuousAcrossCorner_SplitsInsteadOfCuttingCorner()
    {
        // 拐角：(0,0)→(10,0)→(10,10)，总长 20；短划 15 + 间隙 5 → 第一划横跨拐角。
        var line = new[] { V(0, 0), V(10, 0), V(10, 10) };
        var dashes = MovePathVisual.Dashes(line, dash: 15f, gap: 5f);

        // 该划被拆成两段，各自贴着折线（不是从 (0,0) 直连到 (10,5) 切角）。
        Assert.Equal(2, dashes.Count);
        Assert.Equal(V(0, 0), dashes[0].A);
        Assert.Equal(V(10, 0), dashes[0].B);   // 第一段到拐角为止
        Assert.Equal(V(10, 0), dashes[1].A);   // 第二段从拐角续上
        Assert.Equal(V(10, 5), dashes[1].B);   // 弧长 15 落在竖段 5 处
        // 尾部 15..20 是间隙 → 不再有段。
    }

    [Fact]
    public void Dashes_TotalDashedLengthNeverExceedsPolyline()
    {
        var line = new[] { V(0, 0), V(60, 0), V(60, 45) }; // 60 + 45 = 105
        var dashes = MovePathVisual.Dashes(line, MovePathVisual.DashLength, MovePathVisual.GapLength);

        float dashed = 0f;
        foreach (var (a, b) in dashes)
        {
            dashed += Vector2.Distance(a, b);
        }
        Assert.True(dashed <= MovePathVisual.Length(line) + 0.01f);
        Assert.True(dashed > 0f);
    }

    [Fact]
    public void Dashes_ShorterThanOneDash_YieldsSingleShortSegment()
    {
        var line = new[] { V(0, 0), V(6, 0) };
        var dashes = MovePathVisual.Dashes(line, dash: 20f, gap: 10f);

        Assert.Single(dashes);
        Assert.Equal(V(0, 0), dashes[0].A);
        Assert.Equal(V(6, 0), dashes[0].B);
    }

    [Fact]
    public void Dashes_NonPositiveDashOrGap_FallsBackToSolidSegments()
    {
        var line = new[] { V(0, 0), V(10, 0), V(10, 10) };

        Assert.Equal(2, MovePathVisual.Dashes(line, dash: 0f, gap: 10f).Count);
        Assert.Equal(2, MovePathVisual.Dashes(line, dash: 10f, gap: 0f).Count);
    }

    [Fact]
    public void Dashes_EmptyForDegeneratePolyline()
    {
        Assert.Empty(MovePathVisual.Dashes(null, 10f, 5f));
        Assert.Empty(MovePathVisual.Dashes(new[] { V(1, 1) }, 10f, 5f));
        Assert.Empty(MovePathVisual.Dashes(new[] { V(1, 1), V(1, 1) }, 10f, 5f)); // 零长段跳过
    }
}
