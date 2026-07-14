using System.Collections.Generic;
using System.Numerics;
using DeadSignal.Godot;
using Xunit;
using Box = DeadSignal.Godot.PlacementRules.Box;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「**家具不许贴着大门和围栏放**」的钉死测试（用户原话：「为了防止玩家使用改装台、椅子等家具阻挡寻路，
/// 放置的时候就不允许贴着大门和围栏」）。
///
/// <para>
/// <b>为什么这条规则必须存在</b>：在它之前，营地里唯一能被玩家放下的东西是沙袋，而沙袋<b>恒不挡路</b>
/// （<see cref="SandbagSpec.IsSolid"/> = false）—— 所以"玩家摆件堵死寻路"这件事在物理上就不可能发生。
/// 床（impl-bedrest）与改装台（impl-gunmod）是**头两件玩家可放置的实心家具**：它们进 <c>_navHoles</c>、
/// 挖导航洞、真的挡路。kill box 的门从这一刻起第一次被推开了一条缝，而本规则就是把它按回去的东西
/// （与「墙不能建」同一个动机，见 <see cref="StructureBuildCost"/> / <see cref="FenceUpgradeLogic"/> 的类注）。
/// </para>
/// </summary>
public class PlacementRulesTests
{
    // 一座 1000×1000 的测试营地，南墙是一段真围栏（厚 22，与 camp.json 一致）。
    private static readonly Box Bounds = new(0f, 0f, 1000f, 1000f);
    private static readonly Box SouthFence = new(0f, 500f, 400f, 22f);

    private static readonly IReadOnlyList<Box> Defenses = new[] { SouthFence };
    private static readonly IReadOnlyList<Box> NoSolids = System.Array.Empty<Box>();
    private static readonly IReadOnlyList<Box> NoFurniture = System.Array.Empty<Box>();

    /// <summary>一张改装台（实心、受本规则约束——默认值即"受约束"）。</summary>
    private static readonly PlaceableSpec Bench = new("改装台", 120f, 74f, IsSolid: true);

    private static PlacementVerdict Place(PlaceableSpec spec, float x, float y) =>
        PlacementRules.CanPlace(spec, new Vector2(x, y), Bounds, Defenses, NoSolids, NoFurniture);

    // ---------- 核心：贴着围栏放 = 拒 ----------

    /// <summary>紧贴围栏北面（家具边缘正好吻在墙皮上）——正是用户要禁的那一手。</summary>
    [Fact]
    public void Flush_against_fence_is_rejected()
    {
        // 改装台高 74 ⇒ 中心 y = 500 - 37 = 463 时，它的南边缘正好 = 围栏北沿 500。
        Assert.Equal(PlacementVerdict.TooCloseToDefenses, Place(Bench, 200f, 463f));
    }

    /// <summary>压在围栏身体里——同样归"太靠近防线"（比"那儿有东西"更说明问题）。</summary>
    [Fact]
    public void Overlapping_the_fence_is_rejected()
    {
        Assert.Equal(PlacementVerdict.TooCloseToDefenses, Place(Bench, 200f, 505f));
    }

    /// <summary>
    /// <b>缓冲带的边界是精确的</b>：差 1px 进带内 ⇒ 拒；正好退到带外 ⇒ 放行。
    /// （<see cref="PlacementRules.KeepOut"/> = 64px，量的是家具边缘到防线边缘的距离。）
    /// </summary>
    [Fact]
    public void KeepOut_band_boundary_is_exact()
    {
        // 南边缘距围栏北沿正好 KeepOut ⇒ 放行；再往南挪 1px 就进带内 ⇒ 拒。
        float clearY = 500f - PlacementRules.KeepOut - Bench.Height / 2f;
        Assert.Equal(PlacementVerdict.Ok, Place(Bench, 200f, clearY));
        Assert.Equal(PlacementVerdict.TooCloseToDefenses, Place(Bench, 200f, clearY + 1f));
    }

    /// <summary>离防线足够远的空地 ⇒ 随便放。</summary>
    [Fact]
    public void Far_from_defenses_is_ok()
    {
        Assert.Equal(PlacementVerdict.Ok, Place(Bench, 200f, 200f));
    }

    /// <summary>缓冲带是**四面**的：围栏南侧（营外一侧）同样禁——绕到墙背面堵路也是堵路。</summary>
    [Fact]
    public void KeepOut_band_applies_on_every_side_of_the_fence()
    {
        Assert.Equal(PlacementVerdict.TooCloseToDefenses, Place(Bench, 200f, 560f));
    }

    /// <summary>大门与门体和围栏一视同仁（三者都是 <see cref="CampStructureKind"/>，都是"必须保持通畅的关口"）。</summary>
    [Fact]
    public void Gate_gets_the_same_keep_out_band()
    {
        var gate = new Box(400f, 500f, 200f, 22f);
        var defenses = new[] { gate };
        PlacementVerdict v = PlacementRules.CanPlace(
            Bench, new Vector2(500f, 463f), Bounds, defenses, NoSolids, NoFurniture);
        Assert.Equal(PlacementVerdict.TooCloseToDefenses, v);
    }

    // ---------- 类型级豁免 ----------

    /// <summary>
    /// <b>沙袋豁免</b>（类型级，与围栏那边"类型级防建墙"同构）：它恒不挡路
    /// （<see cref="SandbagSpec.IsSolid"/> = false），而它的<b>本职就是垒在防线后面</b>让人蹲着挨枪。
    /// 把它赶离围栏 64px = 废掉掩体这个机制，且与用户的动机（防"阻挡寻路"）毫无关系。
    /// </summary>
    [Fact]
    public void Sandbag_is_exempt_and_may_hug_the_fence()
    {
        var sandbag = new PlaceableSpec(
            "沙袋", SandbagSpec.Width, SandbagSpec.Height, IsSolid: false, AllowedAgainstDefenses: true);
        Assert.Equal(PlacementVerdict.Ok, PlacementRules.CanPlace(
            sandbag, new Vector2(200f, 486f), Bounds, Defenses, NoSolids, NoFurniture));
    }

    /// <summary>
    /// <b>忘了填豁免标记 = 受约束</b>（fail-safe）。新家具的作者不写 <c>AllowedAgainstDefenses</c>
    /// 时拿到的是**安全**的那一侧，而不是把 kill box 的门漏开。
    /// </summary>
    [Fact]
    public void Default_spec_is_subject_to_the_rule()
    {
        var careless = new PlaceableSpec("某件新家具", 60f, 60f, IsSolid: true);
        Assert.False(careless.AllowedAgainstDefenses);
        Assert.Equal(PlacementVerdict.TooCloseToDefenses, Place(careless, 200f, 460f));
    }

    // ---------- 其余放置合法性（与沙袋那条老路同口径） ----------

    [Fact]
    public void Out_of_bounds_is_rejected()
    {
        Assert.Equal(PlacementVerdict.OutOfBounds, Place(Bench, 10f, 200f));
    }

    [Fact]
    public void Blocked_by_solid_is_rejected()
    {
        var wall = new[] { new Box(150f, 150f, 100f, 100f) };
        PlacementVerdict v = PlacementRules.CanPlace(
            Bench, new Vector2(200f, 200f), Bounds, Defenses, wall, NoFurniture);
        Assert.Equal(PlacementVerdict.BlockedBySolid, v);
    }

    [Fact]
    public void Overlapping_existing_furniture_is_rejected()
    {
        var placed = new[] { new Box(150f, 150f, 100f, 100f) };
        PlacementVerdict v = PlacementRules.CanPlace(
            Bench, new Vector2(200f, 200f), Bounds, NoSolids, NoSolids, placed);
        Assert.Equal(PlacementVerdict.OverlapsFurniture, v);
    }

    // ---------- 缓冲带宽度不是拍脑袋来的 ----------

    /// <summary>
    /// <b>64px 的两条硬约束</b>（改这个数之前先看这里）：
    /// <list type="number">
    /// <item>&gt; <b>32px</b>：导航烘焙 <c>AgentRadius=14</c> + 障碍外扩 2px ⇒ 人能挤过去的最窄走廊 = 2×(14+2)。
    ///       缓冲带若窄于此，家具与围栏之间的缝**根本走不了人** —— 那就等于砌了一堵墙，本规则也就白写了。</item>
    /// <item>&gt; <b>40px</b>：砌墙工要站进 <c>seg.Rect.Grow(Pawn.Radius 12 + PendingArriveMargin 28)</c> 才算"到位"
    ///       （CampMain.cs:3280）。缓冲带必须把这条**施工站位带**整个让出来，否则玩家会把自己的围栏
    ///       升级/修复入口用家具堵死 —— 一个远比 kill box 更难查的坑。</item>
    /// </list>
    /// </summary>
    [Fact]
    public void KeepOut_clears_both_the_nav_corridor_and_the_wall_build_stand_lane()
    {
        Assert.True(PlacementRules.KeepOut > PlacementRules.NavCorridorFloor,
            "缓冲带必须宽过人能挤过去的最窄走廊，否则家具与墙之间的缝会变成一堵新墙");
        Assert.True(PlacementRules.KeepOut > PlacementRules.WallBuildStandLane,
            "缓冲带必须让出砌墙工的站位带，否则围栏会被家具堵得没法升级/修复");
    }

    /// <summary>拒绝理由必须是**给玩家看的中文**，不能把英文枚举名漏上屏（项目铁律：禁代码腔）。</summary>
    [Theory]
    [InlineData(PlacementVerdict.OutOfBounds)]
    [InlineData(PlacementVerdict.TooCloseToDefenses)]
    [InlineData(PlacementVerdict.BlockedBySolid)]
    [InlineData(PlacementVerdict.OverlapsFurniture)]
    public void Rejection_text_is_player_facing_chinese(PlacementVerdict verdict)
    {
        string text = PlacementRules.RejectionText(verdict);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain(verdict.ToString(), text);
    }

    /// <summary>放行时没有拒绝理由（别对着一次成功的放置弹一条错误提示）。</summary>
    [Fact]
    public void Ok_has_no_rejection_text()
    {
        Assert.Equal("", PlacementRules.RejectionText(PlacementVerdict.Ok));
    }
}
