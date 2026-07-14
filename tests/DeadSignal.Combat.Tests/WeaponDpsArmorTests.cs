using DeadSignal.Combat;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 「对甲 DPS」的正确性护栏。
///
/// <para>它不是"再写一遍护甲公式然后跟自己对答案"——那样只能证明我抄对了自己。
/// 真正的检验是：<b>拿它去复现设计文档 §5 里那批独立实测出来的涌现数据</b>。
/// 对上了，才说明护甲结算是照引擎真实规则跑的。<b>对不上，就说明我算错了，
/// 而一个错的对甲 DPS 会让用户照着它调平衡 —— 比根本没有这一列更糟。</b></para>
/// </summary>
public class WeaponDpsArmorTests
{
    // 设计文档 §5「多弹丸齐射」的实测涌现数据（自制霰弹枪 8 颗弹丸）：
    //   板甲挡下 69.2% / 皮夹克 34.8% / 布衣 6.7% / 腐皮 1.1%
    //   每齐射到肉的伤害：无甲 24.0 → 重甲 7.1
    // 这些是当初从弹丸级判定里"自己长出来"的数，不是写死的常量 —— 正好当独立锚点。

    /// <summary>
    /// 🔴 <b>锚点用的是「文档当时那把霰弹枪」，不是 <c>WeaponTable</c> 里的现值。</b>
    /// <para>
    /// 因为霰弹枪的数值**后来被用户手改过**（伤害 1~5 → <b>2~6</b>，见 <c>WeaponTable.ImprovisedShotgun</c> 的
    /// "用户手改"注释）。拿现值去撞文档里那批旧实测数，必然对不上 —— 但那**不代表护甲结算错了，
    /// 只代表武器变强了**（实测：四个挡下率全都同方向下降，正是"攻击骰变大 ⇒ 更难被挡"的表现）。
    /// </para>
    /// <para>
    /// 我们要验的是<b>护甲结算逻辑</b>，不是当前数值。所以这里把武器钉回文档当时的样子
    /// （1~5 / 8 颗 / 穿透 10%）—— 这样一旦对上，就证明"三段判定 + 部位覆盖 + 逐颗独立"这套算法是对的。
    /// </para>
    /// </summary>
    private static Weapon DocEraShotgun() => new()
    {
        Name = "自制霰弹枪（文档实测时的数值）",
        DamageMin = 1,          // 文档当时：1~5（今值 2~6，用户手改）
        DamageMax = 5,
        PelletCount = 8,
        Penetration = 0.10,
        DamageType = DamageType.Sharp,
        IsRanged = true,
        TwoHanded = true,
        AttackInterval = 4.0,
    };

    /// <summary>用引擎跑一发霰弹（8 颗），统计"被完全挡下"的弹丸占比 + 到肉伤害。</summary>
    private static (double BlockedRate, double DamagePerVolley) Shotgun(IReadOnlyList<ArmorLayer> armor, int samples = 200000)
    {
        Weapon shotgun = DocEraShotgun();
        var rng = new SystemRandomSource(4242);
        var resolver = new CombatResolver(rng);
        var hit = new VolumeWeightedHitSelector(rng);
        Body body = HumanBody.NewBody();
        var parts = body.Parts.Values.ToList();
        IReadOnlyList<ArmorLayer> layers = CombatResolver.OrderOuterToInner(armor);

        int blocked = 0;
        double damage = 0;
        for (int i = 0; i < samples; i++)
        {
            BodyPart part = hit.Select(parts);
            CombatResult r = resolver.Resolve(shotgun, layers, part);
            if (r.Terminated) blocked++;
            damage += r.FinalDamage;
        }
        int pellets = Math.Max(1, shotgun.PelletCount);
        return (blocked / (double)samples, damage / samples * pellets);
    }

    [Fact]
    public void 交叉验证_无甲每齐射到肉约24点()
    {
        // 霰弹枪 1~5（均值 3.0）× 8 颗 = 24.0 —— 文档写的就是这个数
        (double blocked, double dmg) = Shotgun(System.Array.Empty<ArmorLayer>());
        Assert.Equal(0, blocked, 3);              // 无甲：一颗也挡不下
        Assert.InRange(dmg, 23.5, 24.5);          // 文档：24.0
    }

    [Fact]
    public void 交叉验证_皮夹克挡下约34点8百分比()
    {
        // 🔴 这是最要紧的一条：文档实测 34.8%。它把"打到裸露部位（头/手/脚）根本挡不下"也算进去了，
        //    所以我的采样必须**按真实部位分布抽**才能对得上——只算"打中甲上"会算出高得多的挡下率。
        // 🔴 文档那批数是拿**甲组**测的（外甲 + 长袖布衣），不是单层——Sim/Program.cs:215 白纸黑字
        //    写着"护甲组合（从外到内）：板甲 → 粗布外套 → 长袖布衣"。用单层撞它必然对不上。
        (double blocked, _) = Shotgun(new[] { ArmorTable.LeatherJacket(), ArmorTable.LongSleeveShirt() });
        Assert.InRange(blocked, 0.335, 0.36);     // 文档：34.8%（实算 34.9%）
    }

    [Fact]
    public void 交叉验证_布衣挡下约6点7百分比()
    {
        (double blocked, _) = Shotgun(new[] { ArmorTable.LongSleeveShirt() });
        Assert.InRange(blocked, 0.06, 0.075);     // 文档：6.7%（实算 6.8%）
    }

    [Fact]
    public void 交叉验证_板甲挡下约69点2百分比()
    {
        (double blocked, _) = Shotgun(new[] { ArmorTable.Plate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() });
        Assert.InRange(blocked, 0.68, 0.70);      // 文档：69.2%（实算 69.1%）
    }

    // ── 对皮甲 DPS 的性质护栏 ──

    [Fact]
    public void 对皮甲DPS_必定低于裸DPS_因为甲会挡()
    {
        foreach (Weapon w in WeaponTable.Arsenal())
        {
            double bare = WeaponDps.Single(w);
            double vs = WeaponDps.AgainstLeatherArmor(w);
            Assert.True(vs < bare, $"{w.Name}：对皮甲 {vs:F2} 应低于裸 {bare:F2}");
            Assert.True(vs > 0, $"{w.Name}：对皮甲 DPS 不该归零（头/手/脚是裸露的，总打得进去）");
        }
    }

    [Fact]
    public void 对皮甲DPS_同一把武器算两次必得同值()
    {
        // 固定种子 ⇒ 表里的数不会自己跳来跳去
        Weapon rifle = WeaponTable.Rifle();
        Assert.Equal(WeaponDps.AgainstLeatherArmor(rifle), WeaponDps.AgainstLeatherArmor(rifle), 6);
    }

    [Fact]
    public void 霰弹枪对披甲的跌幅_应远大于步枪()
    {
        // 「霰弹枪对披甲极差」是设计文档的结论（8 颗弹丸逐颗被挡）。这条钉死它。
        static double Drop(Weapon w) => 1 - WeaponDps.AgainstLeatherArmor(w) / WeaponDps.Single(w);

        double shotgun = Drop(WeaponTable.ImprovisedShotgun());
        double rifle = Drop(WeaponTable.Rifle());
        Assert.True(shotgun > rifle,
            $"霰弹枪对甲跌 {shotgun:P1} 应比步枪跌 {rifle:P1} 更狠——多弹丸逐颗被挡是它的死穴");
    }
}
