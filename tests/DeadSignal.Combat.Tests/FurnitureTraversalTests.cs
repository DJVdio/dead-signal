using System.Numerics;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「<b>家具可跨越，跨过时 −25% 移速</b>」的钉死测试（用户原话：「椅子之类的别的家具都可以跨过，
/// 但是跨过时会减少 25% 的移动速度」；「改装台、烹饪台不允许跨越」）。
///
/// <para>
/// 本组测试守着三条<b>会被人不小心改坏</b>的东西：
/// <list type="number">
/// <item><b>乘算，不是加算</b>（项目铁律）—— 加算会让"断腿 + 跨椅子"算出一个凭空多出来的惩罚。</item>
/// <item><b>缺省可跨越</b>（fail-safe）—— 忘登记只该导致"这件家具没那么碍事"，绝不该凭空造出一堵挡路的墙。</item>
/// <item><b>作业台不可跨</b> —— 改装台/烹饪台是用户点名的实心固定设施。</item>
/// </list>
/// </para>
/// </summary>
public class FurnitureTraversalTests
{
    // ---------- 类型级：谁能跨、谁不能 ----------

    /// <summary>椅子/床/柜子这类家具：<b>能跨</b>（用户点名的那一类）。</summary>
    [Theory]
    [InlineData("床")]
    [InlineData("床#2")]          // 带流水号的实例名要能归一到类型
    [InlineData("住宅-柜子")]
    [InlineData("住宅-衣柜")]
    [InlineData("住宅-展示柜")]
    [InlineData("沙袋")]
    [InlineData("沙袋#3")]
    public void Ordinary_furniture_is_traversable(string key)
    {
        Assert.True(FurnitureTraversal.IsTraversable(key));
        Assert.Equal(FurnitureTraversal.CrossingSpeedMultiplier, FurnitureTraversal.SpeedMultiplierOf(key));
    }

    /// <summary><b>用户点名不可跨的两台</b> + 同类的工作台：实心固定设施，跨不过去。</summary>
    [Theory]
    [InlineData("改装台")]
    [InlineData("烹饪台")]
    [InlineData("工作台")]
    public void Workstations_are_not_traversable(string key)
    {
        Assert.False(FurnitureTraversal.IsTraversable(key));
        // 它是实心的，人压根站不上去 ⇒ 不该给它一个"踩上去的减速"（那会是死代码，还会误导后人以为能踩）。
        Assert.Equal(FurnitureTraversal.NoSlowdown, FurnitureTraversal.SpeedMultiplierOf(key));
    }

    /// <summary>
    /// <b>fail-safe 缺省 = 可跨越</b>：没登记过的新家具**不挡路**。
    /// 反过来设默认（忘填 = 实心）意味着一次疏漏就能在营地里凭空造出一堵墙、把人卡死在门外。
    /// </summary>
    [Fact]
    public void Unregistered_furniture_defaults_to_traversable()
    {
        Assert.True(FurnitureTraversal.IsTraversable("某件还没人登记过的新家具"));
    }

    // ---------- 减速：乘算，禁加算 ----------

    /// <summary>跨过家具 = ×0.75，即 −25%。</summary>
    [Fact]
    public void Crossing_costs_25_percent()
    {
        Assert.Equal(0.75, FurnitureTraversal.CrossingSpeedMultiplier);
    }

    /// <summary>
    /// <b>与其它移速修正连乘，不是加算</b>（项目铁律：<c>0.86 × 1.03</c>，不是 <c>0.89</c>）。
    /// 一个断了腿（×0.7）的人跨过椅子 = <b>0.7 × 0.75 = 0.525</b>；
    /// 加算会算成 <c>1 − 0.30 − 0.25 = 0.45</c> —— 凭空多惩罚了 7.5 个点。
    /// </summary>
    [Fact]
    public void Stacks_multiplicatively_with_other_move_penalties()
    {
        const double legFracture = 0.7;   // 腿骨折（未治疗）
        double combined = legFracture * FurnitureTraversal.CrossingSpeedMultiplier;

        Assert.Equal(0.525, combined, 6);
        Assert.NotEqual(1.0 - 0.30 - 0.25, combined, 6);   // 加算的错误答案 0.45
    }

    /// <summary>三重修正（骨折 × 命中减速 × 家具）照样连乘 —— 乘算链没有例外。</summary>
    [Fact]
    public void Three_modifiers_all_multiply()
    {
        const double legFracture = 0.7;
        const double stagger = 0.6;       // 命中减速
        double combined = legFracture * stagger * FurnitureTraversal.CrossingSpeedMultiplier;

        Assert.Equal(0.315, combined, 6);
    }

    // ---------- 减速场：站上去慢，走下来恢复 ----------

    private static TraversalField FieldWithChairAt(float x, float y, float w, float h)
    {
        var f = new TraversalField();
        f.Add(x, y, w, h);
        return f;
    }

    /// <summary>踩在家具上 ⇒ ×0.75。</summary>
    [Fact]
    public void Standing_on_furniture_slows()
    {
        TraversalField field = FieldWithChairAt(100f, 100f, 40f, 40f);
        Assert.Equal(0.75, field.MultiplierAt(new Vector2(120f, 120f)), 6);
    }

    /// <summary><b>走下来就恢复原速</b>（减速是"踩着才有"，不是一个会赖着不走的 debuff）。</summary>
    [Fact]
    public void Leaving_furniture_restores_full_speed()
    {
        TraversalField field = FieldWithChairAt(100f, 100f, 40f, 40f);
        Assert.Equal(0.75, field.MultiplierAt(new Vector2(120f, 120f)), 6);   // 站上去
        Assert.Equal(1.0, field.MultiplierAt(new Vector2(300f, 300f)), 6);    // 走下来
    }

    /// <summary>空场 / 没踩着任何东西 ⇒ 原速（别给站在空地上的人也来一刀）。</summary>
    [Fact]
    public void Empty_field_is_full_speed()
    {
        Assert.Equal(1.0, new TraversalField().MultiplierAt(new Vector2(50f, 50f)), 6);
    }

    /// <summary>
    /// 重叠的两块家具 ⇒ <b>连乘</b>（0.75 × 0.75 = 0.5625），不是取最小值、更不是加算。
    /// 放置规则本就禁止家具重叠，实战里碰不到；但规则本身按连乘写，免得日后允许重叠时
    /// 这里悄悄变成一条加算的例外。
    /// </summary>
    [Fact]
    public void Overlapping_furniture_multiplies()
    {
        var field = new TraversalField();
        field.Add(100f, 100f, 40f, 40f);
        field.Add(120f, 120f, 40f, 40f);   // 与前一块重叠

        Assert.Equal(0.5625, field.MultiplierAt(new Vector2(130f, 130f)), 6);
    }

    /// <summary>家具被拆走 ⇒ 那块地恢复原速。</summary>
    [Fact]
    public void Removing_furniture_clears_its_slowdown()
    {
        TraversalField field = FieldWithChairAt(100f, 100f, 40f, 40f);
        Assert.True(field.RemoveRect(100f, 100f, 40f, 40f));
        Assert.Equal(1.0, field.MultiplierAt(new Vector2(120f, 120f)), 6);
        Assert.Equal(0, field.Count);
    }

    /// <summary>
    /// <b>减速对所有角色一视同仁</b>：场只认坐标、<b>不认谁站在上面</b> —— 丧尸/劫掠者/布鲁斯跨过家具
    /// 与玩家慢得一模一样。这是结构性的（减速挂在 Actor 的移速链上，而他们全都是 Actor），
    /// 本测试钉死"场里没有任何按阵营/角色分叉的入口"。
    /// </summary>
    [Fact]
    public void Slowdown_is_faction_blind()
    {
        TraversalField field = FieldWithChairAt(100f, 100f, 40f, 40f);
        var sameSpot = new Vector2(120f, 120f);

        // 同一个点问两次（"玩家问"和"丧尸问"），签名里根本没有区分谁的余地 ⇒ 必然同值。
        Assert.Equal(field.MultiplierAt(sameSpot), field.MultiplierAt(sameSpot), 6);
        Assert.Equal(0.75, field.MultiplierAt(sameSpot), 6);
    }
}
