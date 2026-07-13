using System.Collections.Generic;
using System.Numerics;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「隔着围栏够不着人 ⇒ 去啃围栏，而不是站着空挥」（impl-cover 的 HANDOFF）。
///
/// <para>
/// <b>这个 bug 是怎么来的</b>：impl-cover 落地了「围栏是半身掩体」——看得穿、射得穿，但**近战捅不过去**（用户拍板）。
/// 而围栏只有 <b>22px 厚</b>，丧尸的够到距离是 <b>24 + 13 + 13 ≈ 50px</b> ⇒ <b>它跨得过围栏够到你</b>。
/// 于是丧尸会：看见墙内的你 → 贴到栏上 → <b>停下来进入攻击姿态</b>（距离够）→ 每一次出手都被围栏几何拦掉
/// ⇒ <b>站在那里永远空挥</b>，既不咬到人，也不啃墙。
/// </para>
/// <para>
/// <b>修法</b>：把"该不该转砸墙"从"导航到不了目标"扩成 <see cref="BreachLogic.ShouldBreach"/> ——
/// <b>导航到不了 或 近战被围栏挡住</b>，两者任一即砸墙。后者是关键：大门开着时导航<b>是通的</b>
/// （绕一圈能进来），可丧尸不绕路，它就贴在栏上——此时必须让它啃墙。
/// </para>
/// </summary>
public class FenceMeleeStallTests
{
    // 营地南墙真实几何：一格围栏 100×22（CampMain.SplitFence 切出来的），外侧是 y > 1500。
    private static readonly HalfCover FenceTile =
        HalfCover.FromRect(1300, 1478, 100, 22, chance: 0.25f, blocksMelee: true);

    private static readonly List<HalfCover> Covers = new() { FenceTile };

    [Fact]
    public void 丧尸隔着围栏够不着人_转入砸围栏_而不是空挥()
    {
        var zombie = new Vector2(1350, 1508);    // 栏外，贴着围栏
        var survivor = new Vector2(1350, 1470);  // 栏内，贴着围栏

        // 前提①：它俩离得**够近**——38px，远在丧尸 50px 的够到距离之内。
        // 这正是空挥的根源：距离判定说"能打"，几何判定说"打不过去"。
        Assert.True(Vector2.Distance(zombie, survivor) < 50f);

        // 前提②：中间隔着围栏 ⇒ 近战打不出去（impl-cover 的规则）。
        bool meleeStalled = CoverLogic.MeleeBlocked(Covers, zombie, survivor);
        Assert.True(meleeStalled);

        // 前提③：**导航是通的**（大门开着，绕一圈进得来）—— 所以旧的"到不了才砸墙"判据在这里是 false。
        // 若只看导航，丧尸就会一直站着空挥。
        bool navBlocked = false;

        // 断言：它必须转去砸围栏。
        Assert.True(BreachLogic.ShouldBreach(navBlocked, meleeStalled));
    }

    [Fact]
    public void 它啃的是挡在中间的那格围栏_不是远处的大门()
    {
        // 被围栏卡住的丧尸认领攻击位时，就近选中面前这格围栏（大门在 200px 开外）。
        var cands = new List<BreachCandidate>
        {
            new(0, 1100, 1478, 200, 22, BreachSlots.Capacity(200, 22, BreachSlots.DefaultFootprint)), // 大门
            new(1, 1300, 1478, 100, 22, BreachSlots.Capacity(100, 22, BreachSlots.DefaultFootprint)), // 面前这格
        };
        var book = new BreachSlotBook();

        int target = BreachSlots.ChooseTarget(1350, 1508, cands, book, attacker: 1, radius: 320,
            out _, out _, out _);

        Assert.Equal(1, target); // 面前那格围栏
    }

    [Fact]
    public void 没有围栏挡着就正常咬人_不去砸墙()
    {
        // 同一侧（都在栏外）：够得着，就该咬人——不能因为附近有围栏就跑去啃墙。
        var zombie = new Vector2(1350, 1520);
        var survivor = new Vector2(1360, 1530);

        bool meleeStalled = CoverLogic.MeleeBlocked(Covers, zombie, survivor);
        Assert.False(meleeStalled);
        Assert.False(BreachLogic.ShouldBreach(navBlocked: false, meleeStalledByFence: meleeStalled));
    }

    [Fact]
    public void 导航到不了目标仍然砸墙_老判据不能丢()
    {
        // 零回归：原本"被结构阻隔 ⇒ 砸墙"的路径必须原样保留（门闩着、四面围合）。
        Assert.True(BreachLogic.ShouldBreach(navBlocked: true, meleeStalledByFence: false));
    }

    [Fact]
    public void 远程不受影响_能射穿围栏()
    {
        // 围栏挡近战、不挡远程（网格空洞，射得穿）。ShouldBreach 的第二个判据只喂近战被卡的情形，
        // 远程攻击者传 false ⇒ 不会因为面前有围栏就丢下枪去砸墙。
        Assert.False(BreachLogic.ShouldBreach(navBlocked: false, meleeStalledByFence: false));
    }
}
