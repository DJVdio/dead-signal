using System;
using System.Linq;
using DeadSignal.Combat;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 弓弩体系 + **组合修正**（「箭的类型反过来修改弓的属性」，用户拍板的核心机制）。
/// <para>
/// 本文件锁定的是**规则形态**，不是数值：谁比谁强、哪个方向变好变坏、哪些武器绝不受影响。
/// 具体数字「拟定待调」，用户会在 <c>docs/weapons-calc.xlsx</c> 里改——改完这些测试仍应全绿，
/// 若某条挂了，说明改的不是数值而是**设计意图**。
/// </para>
/// </summary>
public class ArcheryTests
{
    private static Weapon[] AllArchery() => WeaponTable.ArcheryArsenal().ToArray();

    // ==================== 组合修正：机制本身 ====================

    [Fact]
    public void 组合修正_不吃箭的武器原样返回同一引用_零回归()
    {
        // 这是「既有 Sim 基线零漂移」的机制保证：枪和近战根本不进这条路径。
        // 断言引用相等（不只是值相等）——连一个新对象都不该造出来。
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => !Archery.UsesArrows(w)))
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Assert.Same(w, Archery.Combine(w, arrow));
            }
        }
    }

    [Fact]
    public void 组合修正_近战武器搭箭也纹丝不动()
    {
        Weapon dagger = WeaponTable.Dagger();
        Weapon combined = Archery.Combine(dagger, ArrowTable.Carbon());

        Assert.Same(dagger, combined);
    }

    [Fact]
    public void 组合修正_没搭箭时原样返回()
    {
        Weapon bow = WeaponTable.ShortBow();

        Assert.Same(bow, Archery.Combine(bow, (ArrowDef?)null));
        Assert.Same(bow, Archery.Combine(bow, "根本不存在的箭"));
    }

    [Fact]
    public void 组合修正_自制箭是基线_五项数值一个不变()
    {
        // 自制箭系数全 1 → 有效武器与基础武器在被修正的 5 项上必须完全相同。
        foreach (Weapon bow in AllArchery())
        {
            Weapon eff = Archery.Combine(bow, ArrowTable.Handmade());

            Assert.Equal(bow.DamageMax, eff.DamageMax, 6);
            Assert.Equal(bow.Penetration, eff.Penetration, 6);
            Assert.Equal(bow.AttackInterval, eff.AttackInterval, 6);
            Assert.Equal(bow.BaseSpreadDegrees, eff.BaseSpreadDegrees, 6);
            Assert.Equal(bow.MaxRange!.Value, eff.MaxRange!.Value, 6);
            Assert.Equal(bow.FalloffStart!.Value, eff.FalloffStart!.Value, 6);
        }
    }

    [Fact]
    public void 组合修正_箭改不了的字段一律继承()
    {
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Weapon eff = Archery.Combine(bow, arrow);

                Assert.Equal(bow.DamageType, eff.DamageType);
                Assert.Equal(bow.TwoHanded, eff.TwoHanded);
                Assert.Equal(bow.CanDualWield, eff.CanDualWield);
                Assert.True(eff.IsRanged);
                Assert.Equal(bow.FalloffFloor, eff.FalloffFloor);   // 末端还剩多少劲，与箭种无关
                Assert.Equal(AmmoKeys.Arrow, eff.AmmoKey);          // 有效武器仍然吃箭
                Assert.Equal(1, eff.BurstCount);                    // 弓弩不连发
                Assert.Equal(1, eff.PelletCount);                   // 弓弩不打弹丸

                // **回归钉**：换支箭不会让弓弦变安静。漏抄这一条，弓一搭上箭就变成完全无声
                // ——潜行武器白送一个无敌属性。（这条 Sim 里真的漏过一次，见 Archery.Combine 注释。）
                Assert.Equal(bow.NoiseRadius, eff.NoiseRadius);
                Assert.True(eff.NoiseRadius > 0, $"「{eff.Name}」放箭不该是完全无声的");
            }
        }
    }

    [Fact]
    public void 组合修正_伤害下限恒为1_再好的箭也擦得过去()
    {
        // 近战锐器通则（用户口径「刀可以轻划一下」）：箭是"飞出去的刀"，斜面掠射是常态。
        // 箭的好坏体现在**上限**（能不能扎穿），不在下限。
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Assert.Equal(Archery.DamageFloor, Archery.Combine(bow, arrow).DamageMin);
            }
        }
    }

    // ==================== 组合修正：重头箭（用户唯一明确的一条） ====================

    [Fact]
    public void 重头箭_破甲更高但射程和攻速削弱_用户原话()
    {
        // 用户原话：「重头箭（破甲能力更高，但射程和攻速有所削弱）」——三个方向逐条钉死。
        foreach (Weapon bow in AllArchery())
        {
            Weapon baseline = Archery.Combine(bow, ArrowTable.Handmade());
            Weapon heavy = Archery.Combine(bow, ArrowTable.Heavy());

            Assert.True(heavy.Penetration > baseline.Penetration,
                $"「{bow.Name}」搭重头箭：破甲必须更高（{heavy.Penetration:P1} vs {baseline.Penetration:P1}）");
            Assert.True(heavy.MaxRange < baseline.MaxRange,
                $"「{bow.Name}」搭重头箭：射程必须削弱（{heavy.MaxRange} vs {baseline.MaxRange}）");
            Assert.True(heavy.AttackInterval > baseline.AttackInterval,
                $"「{bow.Name}」搭重头箭：攻速必须削弱＝冷却更长（{heavy.AttackInterval}s vs {baseline.AttackInterval}s）");
        }
    }

    [Fact]
    public void 重头箭_射程削弱时满伤段同步缩_衰减曲线整条挪而非被截断()
    {
        // 只缩 MaxRange 不缩 FalloffStart 的话，射程一短，"满伤段占比"反而变大 = 近距离白得好处。
        // 两者等比缩放才是"这张弓射得近了"，而不是"这张弓变成近战特化"。
        Weapon bow = WeaponTable.Longbow();
        Weapon heavy = Archery.Combine(bow, ArrowTable.Heavy());

        double baseRatio = bow.FalloffStart!.Value / bow.MaxRange!.Value;
        double heavyRatio = heavy.FalloffStart!.Value / heavy.MaxRange!.Value;

        Assert.Equal(baseRatio, heavyRatio, 6);
    }

    // ==================== 组合修正：其余三种箭的方向 ====================

    [Fact]
    public void 削尖的木箭_全面最差_伤害穿透射程精度都不如自制箭()
    {
        foreach (Weapon bow in AllArchery())
        {
            Weapon baseline = Archery.Combine(bow, ArrowTable.Handmade());
            Weapon stick = Archery.Combine(bow, ArrowTable.SharpenedStick());

            Assert.True(stick.DamageMax < baseline.DamageMax, "木箭伤害更低");
            Assert.True(stick.Penetration < baseline.Penetration, "木箭穿透更低");
            Assert.True(stick.MaxRange < baseline.MaxRange, "木箭射程更短");
            Assert.True(stick.BaseSpreadDegrees > baseline.BaseSpreadDegrees, "木箭更不准（散布角更大）");
        }
    }

    [Fact]
    public void 削尖的木箭_唯独不拖慢出手_它轻()
    {
        // 木箭是应急货：差归差，但不该在"手速"上再罚一遍——它比自制箭还轻。
        foreach (Weapon bow in AllArchery())
        {
            Assert.Equal(
                Archery.Combine(bow, ArrowTable.Handmade()).AttackInterval,
                Archery.Combine(bow, ArrowTable.SharpenedStick()).AttackInterval,
                6);
        }
    }

    [Fact]
    public void 碳纤维箭_四项全优且更准_但不可制作()
    {
        foreach (Weapon bow in AllArchery())
        {
            Weapon baseline = Archery.Combine(bow, ArrowTable.Handmade());
            Weapon carbon = Archery.Combine(bow, ArrowTable.Carbon());

            Assert.True(carbon.DamageMax > baseline.DamageMax, "碳纤维箭伤害更高");
            Assert.True(carbon.Penetration > baseline.Penetration, "碳纤维箭穿透更高");
            Assert.True(carbon.MaxRange > baseline.MaxRange, "碳纤维箭射程更远");
            Assert.True(carbon.AttackInterval < baseline.AttackInterval, "碳纤维箭出手更快");
            Assert.True(carbon.BaseSpreadDegrees < baseline.BaseSpreadDegrees, "碳纤维箭更准");
        }

        // 代价：稀缺。它没有配方，只能搜刮——这是它唯一的、也是足够的代价。
        Assert.False(ArrowTable.Carbon().Craftable);
    }

    [Fact]
    public void 箭_只有碳纤维箭不可制作_其余三种都有配方资格()
    {
        Assert.Equal(3, ArrowTable.Craftable().Count());
        Assert.DoesNotContain(ArrowTable.Craftable(), a => a.Key == ArrowKeys.Carbon);
    }

    // ==================== 组合修正：叠乘不失控 ====================

    [Fact]
    public void 叠乘_穿透被clamp住_不许出现无视一切护甲的组合()
    {
        // 最凶的组合：全表最高穿透的弩 × 破甲专精的重头箭。裸乘会冲到 ~99%（= 护甲系统对它不存在）。
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                double pen = Archery.Combine(bow, arrow).Penetration;

                Assert.InRange(pen, 0, Archery.MaxPenetration);
            }
        }
    }

    [Fact]
    public void 叠乘_散布角有下限_没有绝对精准的弓()
    {
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Assert.True(Archery.Combine(bow, arrow).BaseSpreadDegrees >= Archery.MinSpreadDegrees);
            }
        }
    }

    // ==================== 8 把弓弩：规则形态 ====================

    [Fact]
    public void 弓弩_共八把_五弓三弩()
    {
        string[] names = AllArchery().Select(w => w.Name).ToArray();

        Assert.Equal(8, names.Length);
        foreach (string expected in new[]
                 {
                     "短弓", "反曲弓", "长弓", "竞技复合弓", "狩猎弓",
                     "单手轻弩", "双手重弩", "复合弩",
                 })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void 弓弩_全部吃箭_弓与弩共用弹药类型()
    {
        // 用户拍板：「弩和弓共用弹药类型」。
        foreach (Weapon w in AllArchery())
        {
            Assert.Equal(AmmoKeys.Arrow, w.AmmoKey);
            Assert.True(w.UsesAmmo);
            Assert.Equal(1, w.AmmoPerAttack);   // 一次射一支箭
            Assert.True(Archery.UsesArrows(w));
        }
    }

    [Fact]
    public void 弓弩_全是远程锐器_且不连发不打弹丸()
    {
        foreach (Weapon w in AllArchery())
        {
            Assert.True(w.IsRanged);
            Assert.Equal(DamageType.Sharp, w.DamageType);   // 护甲表「挡锐器」列写的就是"刀/箭"
            Assert.Equal(1, w.BurstCount);
            Assert.Equal(1, w.PelletCount);
            Assert.NotNull(w.MaxRange);
        }
    }

    [Fact]
    public void 弓弩_一律无枪托近战兜底_贴脸即死()
    {
        // 所有枪贴脸都能砸（MeleeProfile），弓弩不能——被摸到就只能挨打。
        // 这是远程潜行武器该付的代价，是设计，不是遗漏。
        foreach (Weapon w in AllArchery())
        {
            Assert.False(w.HasMeleeProfile);
            Assert.Null(w.MeleeProfile());
        }

        Assert.True(WeaponTable.Rifle().HasMeleeProfile, "对照组：枪有枪托兜底");
    }

    [Fact]
    public void 弓弩_伤害下限全为1_近战锐器通则()
    {
        foreach (Weapon w in AllArchery())
        {
            Assert.Equal(1, w.DamageMin);
        }

        Assert.True(WeaponTable.ImprovisedHuntingGun().DamageMin > 1, "对照组：枪械下限不压到 1");
    }

    [Fact]
    public void 弓弩_都在Arsenal里_Sim与UI能查到()
    {
        foreach (Weapon w in AllArchery())
        {
            Assert.Contains(WeaponTable.Arsenal(), a => a.Name == w.Name);
            Assert.False(string.IsNullOrWhiteSpace(w.Description));
            Assert.Equal(w.Description, WeaponTable.DescriptionOf(w.Name));
        }
    }

    // ==================== 生态位：证明不是换皮 ====================

    [Fact]
    public void 生态位_任何弓箭组合的DPS都低于步枪_弓弩不是输出武器()
    {
        // 弓弩的价值在潜行/后勤（箭可回收、不吃火药），不在输出。**连最强组合也不该越过主力枪。**
        double rifleDps = Dps(WeaponTable.Rifle());

        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Weapon eff = Archery.Combine(bow, arrow);

                Assert.True(Dps(eff) < rifleDps,
                    $"「{eff.Name}」DPS {Dps(eff):F2} 不该达到步枪 {rifleDps:F2}");
            }
        }
    }

    [Fact]
    public void 生态位_长弓是射程之王_竞技复合弓是精度之王()
    {
        Weapon[] bows = AllArchery();

        Assert.Equal("长弓", bows.OrderByDescending(w => w.MaxRange!.Value).First().Name);
        Assert.Equal("竞技复合弓", bows.OrderBy(w => w.BaseSpreadDegrees).First().Name);
    }

    [Fact]
    public void 生态位_狩猎弓是伤害之王_复合弩是破甲之王()
    {
        Weapon[] bows = AllArchery();

        Assert.Equal("狩猎弓", bows.OrderByDescending(w => w.DamageMax).First().Name);
        Assert.Equal("复合弩", bows.OrderByDescending(w => w.Penetration).First().Name);
    }

    [Fact]
    public void 生态位_短弓是最弱的入门弓_双手重弩是最慢的()
    {
        Weapon[] bows = AllArchery();

        // 短弓与单手轻弩都"弱"，但弱法不同：短弓是伤害最低的弓，轻弩是全表最弱的远程。
        // 这里钉的是"短弓是全部**弓**里最弱的那把"（入门款，木料2+绳1 就能削出来）。
        Assert.Equal("短弓", bows.Where(IsBow).OrderBy(w => w.DamageMax).First().Name);

        Assert.Equal("双手重弩", bows.OrderByDescending(w => w.AttackInterval).First().Name);
    }

    [Fact]
    public void 生态位_单手轻弩是唯一单手弓弩_可双持()
    {
        Weapon[] oneHanded = AllArchery().Where(w => !w.TwoHanded).ToArray();

        Assert.Single(oneHanded);
        Assert.Equal("单手轻弩", oneHanded[0].Name);
        Assert.True(oneHanded[0].CanDualWield, "唯一的单手弓弩应能双持（双持轻弩＝两发之后全是绝望）");
    }

    [Fact]
    public void 生态位_弩比弓穿透高但更慢_不是换皮()
    {
        // 常识差异化：弩靠机械储能 → 穿透高、上弦慢；弓靠人力 → 出手快。
        Weapon[] bows = AllArchery().Where(IsBow).ToArray();
        Weapon[] crossbows = AllArchery().Where(w => !IsBow(w)).ToArray();

        Assert.True(crossbows.Average(w => w.Penetration) > bows.Average(w => w.Penetration),
            "弩的平均穿透应高于弓");
        Assert.True(crossbows.Average(w => w.AttackInterval) > bows.Average(w => w.AttackInterval),
            "弩的平均冷却应长于弓（上弦慢）");
    }

    [Fact]
    public void 生态位_八把弓弩两两不重样_没有换皮()
    {
        // "换皮"的定义：两把武器的 5 项特征（伤害上限/穿透/冷却/射程/散布）全都一样。
        Weapon[] bows = AllArchery();

        for (int i = 0; i < bows.Length; i++)
        {
            for (int j = i + 1; j < bows.Length; j++)
            {
                bool identical =
                    bows[i].DamageMax == bows[j].DamageMax &&
                    bows[i].Penetration == bows[j].Penetration &&
                    bows[i].AttackInterval == bows[j].AttackInterval &&
                    bows[i].MaxRange == bows[j].MaxRange &&
                    bows[i].BaseSpreadDegrees == bows[j].BaseSpreadDegrees;

                Assert.False(identical, $"「{bows[i].Name}」与「{bows[j].Name}」是换皮");
            }
        }
    }

    [Fact]
    public void 生态位_木箭与碳纤维箭差距显著_不是微调()
    {
        // 4 种箭若只差 2~3%，玩家没有理由去区分它们。最差与最好之间应有量级差。
        Weapon bow = WeaponTable.RecurveBow();
        Weapon stick = Archery.Combine(bow, ArrowTable.SharpenedStick());
        Weapon carbon = Archery.Combine(bow, ArrowTable.Carbon());

        Assert.True(carbon.DamageMax >= stick.DamageMax * 1.5, "碳纤维箭伤害应比木箭高 50% 以上");
        Assert.True(carbon.Penetration >= stick.Penetration * 2.0, "碳纤维箭穿透应是木箭的 2 倍以上");
    }


    // ==================== 箭矢回收（用户拍板：25% / 读书后 50%） ====================

    [Fact]
    public void 回收率_基础25pct_读过弓与箭之道后50pct_用户拍板()
    {
        // 用户原话：「箭只有 25% 的几率不被损毁。如果读过《弓与箭之道》，则是 50% 的几率能回收。」
        Assert.Equal(0.25, Archery.BaseArrowRecoveryRate);
        Assert.Equal(0.50, Archery.SkilledArrowRecoveryRate);

        Assert.Equal(0.25, Archery.ArrowRecoveryRate(hasReadArcheryBook: false));
        Assert.Equal(0.50, Archery.ArrowRecoveryRate(hasReadArcheryBook: true));
    }

    [Fact]
    public void 回收率_读书正好翻倍_那本书是弓弩流的硬前置()
    {
        Assert.Equal(2.0, Archery.SkilledArrowRecoveryRate / Archery.BaseArrowRecoveryRate, 6);
    }

    [Fact]
    public void 回收_四支箭基础只捡回一支_弓弩不是免费远程()
    {
        // 25% ⇒ 射四支捡回一支。弓弩的后勤压力小于枪，但**远不是没有**。
        // roll < rate 即回收；给定 [0.1, 0.9, 0.9, 0.9] → 只有第一支落在 25% 内。
        var rng = new SequenceRandomSource(0.10, 0.90, 0.90, 0.90);

        Assert.Equal(1, Archery.RollArrowRecovery(4, hasReadArcheryBook: false, rng));
    }

    [Fact]
    public void 回收_同一组掷点_读过书能多捡回一半()
    {
        // 同样的四次掷点，基础只过 0.10 那一支；读过书后 0.10 与 0.30 两支都过（阈值 0.50）。
        double[] rolls = { 0.10, 0.30, 0.90, 0.60 };

        Assert.Equal(1, Archery.RollArrowRecovery(4, false, new SequenceRandomSource(rolls)));
        Assert.Equal(2, Archery.RollArrowRecovery(4, true, new SequenceRandomSource(rolls)));
    }

    [Fact]
    public void 回收_逐支箭独立掷点_不是一次射击整体判定()
    {
        // 粒度＝逐支独立。全过 → 全捡回；全不过 → 一支不剩。
        Assert.Equal(3, Archery.RollArrowRecovery(3, false, new SequenceRandomSource(0.0, 0.0, 0.0)));
        Assert.Equal(0, Archery.RollArrowRecovery(3, true, new SequenceRandomSource(0.99, 0.99, 0.99)));
    }

    // ==================== 挑箭兜底 ====================

    [Fact]
    public void 挑箭_优先用最差的_好箭留着()
    {
        // 玩家没显式选箭时的兜底：先打光烂箭。省得碳纤维箭被自动打光。
        Assert.Equal(ArrowKeys.SharpenedStick,
            Archery.PickCheapestAvailable(k => k == ArrowKeys.Carbon || k == ArrowKeys.SharpenedStick ? 5 : 0)!.Key);

        Assert.Equal(ArrowKeys.Heavy,
            Archery.PickCheapestAvailable(k => k == ArrowKeys.Heavy || k == ArrowKeys.Carbon ? 3 : 0)!.Key);

        Assert.Null(Archery.PickCheapestAvailable(_ => 0));
    }

    // ==================== 辅助 ====================

    private static bool IsBow(Weapon w) => !w.Name.Contains('弩');

    private static double Dps(Weapon w) =>
        (w.DamageMin + w.DamageMax) / 2.0
        * Math.Max(1, w.BurstCount) * Math.Max(1, w.PelletCount)
        / w.AttackInterval;
}
