using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 金手指帮守备编制表（<see cref="GoldfingerGang"/>）。
///
/// <para>本类钉死三件事，任何一件被回退都必须红：
/// <list type="number">
/// <item><b>他们是持械的人</b> —— 8 个人手里的东西<b>全部扒得走</b>（此前是丧尸 ⇒ 打赢一把枪都捡不到）。</item>
/// <item><b>他们没拿着自己看守的军火</b> —— 冲锋枪在军械柜里，是奖赏，不是守备的手牌。</item>
/// <item><b>「状态不是巅峰」既真伤又不白送</b> —— 致死余量确实掉了，但没人半死不活。</item>
/// </list></para>
/// </summary>
public class GoldfingerGangTests
{
    private static double VitalHp(Body b) =>
        b.HpOf(HumanBody.Chest) + b.HpOf(HumanBody.Abdomen) + b.HpOf(HumanBody.Head);

    private static double VitalMaxHp(Body b) =>
        b.MaxHpOf(HumanBody.Chest) + b.MaxHpOf(HumanBody.Abdomen) + b.MaxHpOf(HumanBody.Head);

    // ── 一、他们是人，持械，杀了能扒 ──────────────────────────────────────

    [Fact]
    public void 守备编制为八人()
    {
        Assert.Equal(8, GoldfingerGang.Roster.Count);
    }

    [Fact]
    public void 每个守备手里的武器都扒得走()
    {
        // 这条就是本单的存在理由：他们此前是丧尸（爪击＝天生武器，CorpseLoot 结构性排除）⇒ 打赢一件武器都掉不出来。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Weapon w = GoldfingerGang.WeaponFor(g.Arm);
            Assert.True(CorpseLoot.IsSalvageable(w), $"{g.DisplayName} 的 {w.Name} 扒不走");
        }
    }

    [Fact]
    public void 守备不拿他们自己看守的军火()
    {
        // 全图唯一的冲锋枪锁在他们的军械柜里（ExplorationCache 最深处）＝打赢的奖赏。
        // 但凡有一个守备端着它，这仗就从"硬仗"变成"必死局"。步枪/狙击枪/霰弹枪同理。
        var forbidden = new[]
        {
            WeaponTable.Smg().Name, WeaponTable.Rifle().Name,
            WeaponTable.SniperRifle().Name, WeaponTable.ImprovisedShotgun().Name,
        };
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Assert.DoesNotContain(GoldfingerGang.WeaponFor(g.Arm).Name, forbidden);
        }
    }

    [Fact]
    public void 打赢的战利品是四短剑四匕首_枪不在他们手上()
    {
        // 经济钉死：打赢金手指帮 = 一次可观但**有限**的装备跃迁。
        //  · 长剑 / 重剑 / 破甲锤 / 步枪一把都没有 ⇒ 好武器仍然只能出门找。
        // 这张表改了（用户拍板换方案），本条必须跟着改——它是经济后果的看门断言。
        //
        // 🔴 [T57] 用户拍板：**手枪从守备手里全撤**（原案 2 手枪 + 2 短剑 + 4 匕首 → 现 4 短剑 + 4 匕首）。
        //    这一关被重排到**中期**，原案在中期是"赢了但全队残废"（潜行清哨全身而退仅 2%、3.26 处永久残缺）。
        //    ⚠️ **枪没有从这一关消失** —— 它们躺在枪械台/军械柜里（见 GoldfingerCacheTests）：
        //    「弹药打光了，空枪扔回枪械台，抄起短剑守着」。玩家照样捡得到枪，只是从尸体上扒变成柜子里翻。
        Dictionary<GangArm, int> loot = GoldfingerGang.Roster
            .GroupBy(g => g.Arm)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.False(loot.ContainsKey(GangArm.Pistol), "守备手里不该再有枪——枪在柜子里，不在人身上");
        Assert.Equal(4, loot[GangArm.Shortsword]);
        Assert.Equal(4, loot[GangArm.Dagger]);
    }

    // ── 二、「状态不是巅峰」：真带伤 ────────────────────────────────────────

    [Fact]
    public void 没有一个守备是满状态()
    {
        // 用户原话：「他们刚经历完异常战斗，大家的状态都不是巅峰。」——一个满血的都不许有。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Body body = GoldfingerGang.NewInjuredBody(g.Injury);
            bool hurt = VitalHp(body) < VitalMaxHp(body) || body.FracturedParts.Count > 0;
            Assert.True(hurt, $"{g.Injury.Name} 竟是满状态");
        }
    }

    [Fact]
    public void 伤扣在致死池上而不只是四肢挂彩()
    {
        // 只扣四肢＝没伤：四肢归零只致残、不致死，挨打余量一点没变。要"更容易被打死"就得扣胸/腹。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Body body = GoldfingerGang.NewInjuredBody(g.Injury);
            Assert.True(VitalHp(body) < VitalMaxHp(body), $"{g.Injury.Name} 的致死余量一点没掉");
        }
    }

    [Fact]
    public void 多数守备带着真降战力的骨折()
    {
        // 骨折是唯一"直接降战力"的轴（手 ×0.70 操作 / 腿 ×0.70 移动），运行时与 Sim 都真消费。
        int fractured = GoldfingerGang.Roster.Count(g => g.Injury.Fractures.Count > 0);
        Assert.True(fractured >= 5, $"只有 {fractured} 人骨折——「大家的状态都不是巅峰」没落到实处");
    }

    [Fact]
    public void 手骨折真降操作能力()
    {
        Body body = GoldfingerGang.NewInjuredBody(GoldfingerGang.ModerateHand);
        EffectConfig cfg = EffectConfig.Default();
        double factor = body.HandFractureOperationFactor(
            cfg.HandFractureOperationMult, cfg.HandFractureHealedOperationMult, cfg.FractureCapabilityFloor);
        Assert.True(factor < 1.0, "手骨折没降操作能力＝这处伤白带了");
    }

    [Fact]
    public void 腿骨折真降移动能力()
    {
        Body body = GoldfingerGang.NewInjuredBody(GoldfingerGang.ModerateLeg);
        EffectConfig cfg = EffectConfig.Default();
        double factor = body.LegFractureMobilityFactor(
            cfg.LegFractureMobilityMult, cfg.LegFractureHealedMobilityMult, cfg.FractureCapabilityFloor);
        Assert.True(factor < 1.0, "腿骨折没降移动能力＝这处伤白带了");
    }

    // ── 三、但别把他们调成白送 ──────────────────────────────────────────────

    [Fact]
    public void 没有一个守备半死不活()
    {
        // 用户原话是「状态不是巅峰」，不是「他们快死了」。最重的那两个也得站得住、打得动。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Body body = GoldfingerGang.NewInjuredBody(g.Injury);
            Assert.False(body.IsDead, $"{g.Injury.Name} 一生成就是死的");
            Assert.False(body.IsUnconscious, $"{g.Injury.Name} 一生成就昏迷");
            Assert.True(VitalHp(body) >= VitalMaxHp(body) * 0.4,
                $"{g.Injury.Name} 致死余量只剩 {VitalHp(body):0.#}/{VitalMaxHp(body):0.#}——白送了");
        }
    }

    [Fact]
    public void 没有守备被预置成缺手断脚或失去持械能力()
    {
        // 断手 = 拿不住武器（Body.RecalculatePenalties）⇒ 那就不是"带伤"，是"废了"，还会连带掉不出武器。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Body body = GoldfingerGang.NewInjuredBody(g.Injury);
            Assert.Empty(body.Parts.Keys.Where(body.IsGone));
            Assert.True(body.DisabilityModifiers.OperationPenalty < 1.0, $"{g.Injury.Name} 已完全无法出手");
        }
    }

    [Fact]
    public void 预置伤不出血否则他们会在玩家赶到前自己流血死()
    {
        // 战斗内失血是 1.5/s·处（BleedModel），三处大伤口 ~15.6s 放干。预置伤若登记出血，
        // 这 8 个人会在关卡加载后的十几秒内自己躺平——玩家推门进来看到一地尸体。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Body body = GoldfingerGang.NewInjuredBody(g.Injury);
            Assert.Equal(0, body.BleedingWoundCount);
            Assert.False(body.BledOut);
        }
    }

    [Fact]
    public void 预置伤不动储血量_因为那条轴在运行时与Sim会对不上账()
    {
        // 见 GoldfingerGang 类注释：SetBloodMax 会把血回满（Sim 在身体工厂之后调它 ⇒ 预扣的失血被静默擦掉），
        // 且失血分级的攻速惩罚只有 Sim 消费、运行时的 Actor 只拿 BloodRatio 画血条。
        // 这条测试钉死"我们没走那条轴"，免得后人"顺手补个失血"把 Sim 和实机搞得对不上。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Body body = GoldfingerGang.NewInjuredBody(g.Injury);
            Assert.Equal(BloodLossTier.None, body.BloodTier);
        }
    }

    // ── 四、Sim 与运行时读同一张表 ─────────────────────────────────────────

    [Fact]
    public void 身体工厂与就地施伤等价()
    {
        // Sim 的身体工厂走 NewInjuredBody，运行时的 Raider 走 ApplyInjuries(现成的 Body)。两条路必须同一个结果，
        // 否则 Sim 算出来的胜率与玩家真正碰上的那 8 个人对不上。
        foreach (GangGuard g in GoldfingerGang.Roster)
        {
            Body viaFactory = GoldfingerGang.NewInjuredBody(g.Injury);

            Body viaApply = HumanBody.NewBody();
            GoldfingerGang.ApplyInjuries(viaApply, g.Injury);

            foreach (string part in viaFactory.Parts.Keys)
            {
                Assert.Equal(viaFactory.HpOf(part), viaApply.HpOf(part), 6);
            }
            Assert.Equal(
                viaFactory.FracturedParts.OrderBy(p => p),
                viaApply.FracturedParts.OrderBy(p => p));
        }
    }
}
