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

    // ── 五、画布尺寸：Sim 与关卡读同一个数 ────────────────────────────────

    /// <summary>
    /// 🔴 <b>把 <see cref="GoldfingerGang.LevelW"/>/<see cref="GoldfingerGang.LevelH"/> 焊死在
    /// <see cref="ExplorationLevelSize"/> 上</b>——本关画布的<b>单一事实源是 ExplorationLevelSize</b>，
    /// <c>GoldfingerGang</c> 里那两个 const 只是<b>脱 Godot 的副本</b>（<see cref="GoldfingerCalibration"/> 在 Sim 里要读它，
    /// 而 <c>ExplorationLevelSize</c> <b>刻意不被 Sim Link</b>——那是"既有 Sim 战斗基线结构性零漂移"论证的基础，不能为了取个尺寸就动摇它）。
    /// <para>
    /// <b>这条测试就是那道焊缝。</b>此前 Sim 里写的是<b>硬编码字面量</b> <c>2400, 1600</c>，与 ExplorationLevelSize 之间
    /// <b>没有任何东西保证一致</b> ⇒ 谁哪天给金手指加一行 <c>Overrides</c>，<b>游戏里的招怪变了、Sim 报告还在印旧数</b>，
    /// 而且<b>不会有任何测试红</b>（实测漏修的后果：报告会把 短剑 0→1 / 长剑 0→1 / 手枪 2→4 / 步枪 5→7，
    /// 把"潜行清哨可行"这条设计红线在报告里<b>当场写反</b>）。现在改画布必须同步这两个 const，否则本测试当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void 画布尺寸与ExplorationLevelSize焊死_改了那边这边必须跟着改()
    {
        // 真源＝ExplorationLevelSize（下面把 const 摆在 expected 位只为满足 xUnit2000 分析器；语义上是"副本必须等于真源"）。
        (float w, float h) = ExplorationLevelSize.SizeFor(ExplorationCache.GoldfingerBaseName);

        Assert.Equal(GoldfingerGang.LevelW, w, 6);
        Assert.Equal(GoldfingerGang.LevelH, h, 6);
    }

    /// <summary>
    /// 🔴 <b>authored 噪音红线</b>（用户裁决 C 明示保住的那条）：「<b>枪一响还是死</b>」。
    /// <para>
    /// 半径一律读<b>真武器表</b>（<see cref="WeaponTable"/> 的 NoiseRadius），不自造常数——与 <c>StuartManorTests</c> 同规矩。
    /// 噪音源＝<b>中段</b>（0.55, 0.40，玩家推进必经），与 <see cref="GoldfingerCalibration"/> 报告口径一致。
    /// </para>
    /// <para>
    /// 这几个数是 <c>GoldfingerGang.cs</c> 的 [T57] 拍板<b>前提</b>（那段注释明写"噪音设计<b>一格没动</b>"才敢降难度）：
    /// <b>弓/匕首叫醒 0 人 ⇒ 逐个清哨可行；手枪 2 人 ⇒ 招来一小撮；步枪 5 人 ⇒ 半个据点扑上来。</b>
    /// 此前<b>全项目没有任何测试钉它</b>（招怪表只活在 Sim 生成的 research 文档里）⇒ 悄悄改布点/画布不会有人发现。
    /// </para>
    /// </summary>
    [Fact]
    public void 枪一响还是死_弓与匕首叫醒零人而枪招来一片()
    {
        int Alerted(Weapon w) => GoldfingerGang.AlertedBy(
            GoldfingerGang.NoiseProbeX, GoldfingerGang.NoiseProbeY,
            w.NoiseRadius, GoldfingerGang.LevelW, GoldfingerGang.LevelH);

        Assert.Equal(0, Alerted(WeaponTable.ShortBow()));   // 70  —— 潜行清哨可行的全部依据
        Assert.Equal(0, Alerted(WeaponTable.Dagger()));     // 90
        Assert.Equal(0, Alerted(WeaponTable.Shortsword())); // 95
        Assert.Equal(2, Alerted(WeaponTable.Pistol()));     // 350 —— 招来一小撮
        Assert.Equal(5, Alerted(WeaponTable.Rifle()));      // 600 —— 半个据点
    }

    /// <summary>越响叫醒的人越多（单调不减）——这是几何本身，任何布点/画布改动都不该打破它。</summary>
    [Fact]
    public void 越响叫醒的人越多()
    {
        int prev = -1;
        foreach (double radius in new[] { 50.0, 70.0, 90.0, 150.0, 350.0, 500.0, 600.0, 700.0, 5000.0 })
        {
            int n = GoldfingerGang.AlertedBy(
                GoldfingerGang.NoiseProbeX, GoldfingerGang.NoiseProbeY,
                radius, GoldfingerGang.LevelW, GoldfingerGang.LevelH);
            Assert.True(n >= prev, $"半径 {radius} 反而叫醒得更少");
            prev = n;
        }
        Assert.Equal(GoldfingerGang.Roster.Count, prev); // 半径够大 ⇒ 全据点
    }
}
