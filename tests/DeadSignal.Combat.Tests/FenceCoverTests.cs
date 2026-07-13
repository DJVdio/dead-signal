using System.Numerics;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 围栏 = 半身掩体（用户拍板：「围栏是半身掩体，因为围栏中间有网格空洞」）。
/// ⇒ 能看穿、能射穿；贴着它的**双方**都享 25% 远程无伤；但**不允许隔着围栏近战**。
///
/// 营地北墙围栏实测几何：rect [300,300,800,22] —— 厚 22px，营内在 y&gt;322 一侧。
/// </summary>
public class FenceCoverTests
{
    /// <summary>一段北墙围栏（挡近战）。</summary>
    private static HalfCover Fence() =>
        HalfCover.FromRect(300, 300, 800, 22, CoverLogic.DefaultCoverChance, blocksMelee: true);

    /// <summary>一垛沙袋（不挡近战）。</summary>
    private static HalfCover Sandbag() =>
        HalfCover.FromRect(1090, 386, 92, 26, CoverLogic.DefaultCoverChance, blocksMelee: false);

    // ── 围栏对双方都是掩体 ──────────────────────────────────────

    [Fact]
    public void 玩家贴着围栏内侧_朝墙外丧尸开枪_丧尸贴着围栏啃_玩家受掩体保护()
    {
        Vector2 player = new(700, 334);  // 贴围栏内侧（离墙面 12px）
        Vector2 zombie = new(700, 288);  // 贴围栏外侧啃墙

        Assert.True(CoverLogic.Protects(Fence(), shooter: zombie, target: player));
    }

    /// <summary>
    /// 用户点名的那条推论：「丧尸打围栏的时候，射击他们是视作他们在掩体后的」。
    /// **这必须是自动涌现的**——紧贴 + 方向性 + 双向对称三条通用规则一摆，它自然成立。
    /// 若这条不成立，说明通用规则有洞（不许为它写特例）。
    /// </summary>
    [Fact]
    public void 丧尸贴着围栏啃_玩家从内侧射它_它享有百分之二十五无效()
    {
        Vector2 zombie = new(700, 288);  // 贴外侧啃墙（离墙面 12px）
        Vector2 player = new(700, 334);  // 玩家在内侧开枪

        // 同一个纯函数、同一套几何——没有任何一行"如果是丧尸/如果在啃墙"的特例代码。
        Assert.True(CoverLogic.Protects(Fence(), shooter: player, target: zombie));

        var covers = new[] { Fence() };
        Assert.Equal(0.25f, CoverLogic.CoverChanceFor(covers, player, zombie), 3);

        var rng = new SequenceRandomSource(new[] { 0.10 });
        Assert.True(CoverLogic.Negates(ranged: true, CoverLogic.CoverChanceFor(covers, player, zombie), rng));
    }

    [Fact]
    public void 中间那层网谁都占不到便宜_隔栏对射双方同为百分之二十五()
    {
        var covers = new[] { Fence() };
        Vector2 inside = new(700, 334), outside = new(700, 288);

        Assert.Equal(0.25f, CoverLogic.CoverChanceFor(covers, outside, inside), 3); // 外打内
        Assert.Equal(0.25f, CoverLogic.CoverChanceFor(covers, inside, outside), 3); // 内打外
    }

    // ── 紧贴才生效（用户拍板）───────────────────────────────────

    [Fact]
    public void 站在院子中央不算有掩体_必须走到围栏边上()
    {
        var covers = new[] { Fence() };
        Vector2 zombie = new(700, 288); // 墙外的丧尸

        // 离围栏 60px（约两个身位）——虽然子弹确实要穿过围栏，但人没贴着它 ⇒ 不算掩体。
        Vector2 farFromWall = new(700, 382);
        Assert.Equal(0f, CoverLogic.CoverChanceFor(covers, zombie, farFromWall), 3);
        Assert.Null(CoverLogic.AdjacentCover(covers, farFromWall));

        // 走到墙边贴住（离墙面 12px）⇒ 生效。掩体是个**位置决策**。
        Vector2 atWall = new(700, 334);
        Assert.Equal(0.25f, CoverLogic.CoverChanceFor(covers, zombie, atWall), 3);
        Assert.NotNull(CoverLogic.AdjacentCover(covers, atWall));
    }

    [Fact]
    public void 紧贴阈值是二十四像素_刚好贴住生效_一个身位外失效()
    {
        var covers = new[] { Fence() };
        Vector2 zombie = new(700, 200); // 正北，墙外远处

        // 墙面在 y=322。距墙 20px（y=342）→ 在阈值 24 内 ⇒ 生效。
        Assert.Equal(0.25f, CoverLogic.CoverChanceFor(covers, zombie, new Vector2(700, 342)), 3);
        // 距墙 30px（y=352）→ 超阈值 ⇒ 不生效。
        Assert.Equal(0f, CoverLogic.CoverChanceFor(covers, zombie, new Vector2(700, 352)), 3);
    }

    // ── 不允许隔着围栏近战（用户拍板）────────────────────────────

    [Fact]
    public void 不能隔着围栏近战_丧尸咬不到墙内的你()
    {
        var covers = new[] { Fence() };
        // 实测几何：丧尸(R=13,AttackRange=24)够得着 49px；隔栏两边贴住时中心距仅 12+22+13=47 < 49
        // ⇒ 光靠碰撞体挡不住，必须显式拦。
        Vector2 zombieOutside = new(700, 309 - 13);  // 贴外墙面
        Vector2 playerInside = new(700, 322 + 12);   // 贴内墙面

        Assert.True(CoverLogic.MeleeBlocked(covers, zombieOutside, playerInside));
        Assert.True(CoverLogic.MeleeBlocked(covers, playerInside, zombieOutside)); // 反向同理：长矛也捅不出去
    }

    [Fact]
    public void 同侧近战不受阻断_围栏只拦隔着它的那一下()
    {
        var covers = new[] { Fence() };
        // 两人都在院内，围栏在他们身后 → 照常互砍。
        Assert.False(CoverLogic.MeleeBlocked(covers, new Vector2(700, 340), new Vector2(730, 350)));
    }

    [Fact]
    public void 沙袋桌椅不挡近战_矮物绕过去就能砍()
    {
        var covers = new[] { Sandbag() };
        // 一南一北隔着沙袋——沙袋是矮物，跨过去/绕过去就能贴身砍，不该拦近战。
        Vector2 north = new(1136, 370), south = new(1136, 430);
        Assert.False(CoverLogic.MeleeBlocked(covers, north, south));
        // 但它照样提供 25% 远程无效（贴着 + 方向对）。
        Assert.True(CoverLogic.Protects(Sandbag(), shooter: north, target: south));
    }

    // ── 围栏被啃穿后，掩体也得跟着没 ─────────────────────────────

    [Fact]
    public void 围栏被啃穿_掩体随之消失_不能对着空洞白享二十五()
    {
        var field = new CoverField();
        field.Add(300, 300, 800, 22, CoverLogic.DefaultCoverChance, blocksMelee: true);

        Vector2 zombie = new(700, 288), player = new(700, 334);
        Assert.Equal(0.25f, field.ChanceFor(zombie, player), 3);
        Assert.True(field.MeleeBlockedBetween(zombie, player));

        // 这段墙被啃穿 → 移除。
        Assert.True(field.RemoveRect(300, 300, 800, 22));

        Assert.Equal(0f, field.ChanceFor(zombie, player), 3);      // 掩体没了
        Assert.False(field.MeleeBlockedBetween(zombie, player));   // 缺口处也咬得到了
    }
}
