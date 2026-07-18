using System;
using System.Collections.Generic;
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
    public void 组合修正_搭箭后保留弓弩各自下限_不再拍回1()
    {
        // 🔴 用户拍板（[DECISION] impl-archery-redo, journal 2026-07-15）：xlsx 退役、以 wiki 为准、
        // **「箭下限恒 1」机制整条退役**。从前 Combine 把任何 弓×箭 的下限拍回 1（"箭是飞出去的刀，擦得过去"）；
        // 现在弓弩按各自 wiki 值有自己的下限（短弓 2 … 复合弩 12），搭箭只改**上限**（DamageMult 作用于 DamageMax），
        // 下限原样保留、不被拍回 1。
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Assert.Equal(bow.DamageMin, Archery.Combine(bow, arrow).DamageMin, 6);
            }
        }
    }

    // ==================== 用户手改的数值：逐格对账 ====================

    [Fact]
    public void 箭矢系数_与用户手改的数值表逐格一致()
    {
        // 这条不是设计断言，是**对账单**：用户在数值表里亲手改过的格子，必须原样躺在代码里。
        // 数值仍「拟定待调」——再调时连这里一起改；它挂了只说明代码与用户的表脱钩了。
        static void 对账(ArrowDef a, double 伤害, double 破甲, double 射程, double 冷却, double 散布)
        {
            Assert.Equal(伤害, a.DamageMult, 6);
            Assert.Equal(破甲, a.PenetrationMult, 6);
            Assert.Equal(射程, a.RangeMult, 6);
            Assert.Equal(冷却, a.CooldownMult, 6);
            Assert.Equal(散布, a.SpreadMult, 6);
        }

        对账(ArrowTable.SharpenedStick(), 0.75, 0.75, 0.75, 1.00, 1.10);
        对账(ArrowTable.Handmade(), 1.00, 1.00, 1.00, 1.00, 1.00);
        // T29 用户手改重头箭：伤害 1.35 → 1.25、破甲 1.45 → 1.50。
        // 用户又在 wiki 弹药表上继续加码破甲、松一点冷却：破甲 1.50 → **1.75**、冷却 1.15 → **1.1**
        // （"破甲专精就该在破甲轴上兑现，别拿伤害喂它"的路线走到底——见 Archery.Heavy 注释）。
        对账(ArrowTable.Heavy(), 1.25, 1.75, 0.75, 1.1, 1.25);
        对账(ArrowTable.Carbon(), 1.25, 1.25, 1.20, 0.90, 0.70);
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

    /// <summary>
    /// 🔴 [T56 终结] <b>「弓弩下限恒 1 / 近战锐器通则」整条退役</b>——用户拍板（[DECISION] impl-archery-redo,
    /// journal 2026-07-15）：**xlsx 已退役、以 wiki 为准**。全表 8 把弓弩的下限改为各自的 wiki 值
    /// （下面逐把对账），不再统一压到 1。<see cref="Archery.Combine"/> 也同步退役「搭箭拍回 1」那条。
    /// </summary>
    [Fact]
    public void 弓弩_伤害下限按各自wiki值_下限恒1通则已退役()
    {
        var expected = new Dictionary<string, double>
        {
            ["短弓"] = 2, ["反曲弓"] = 3, ["长弓"] = 4, ["竞技复合弓"] = 4, ["狩猎弓"] = 3.5,
            ["单手轻弩"] = 4, ["双手重弩"] = 6, ["复合弩"] = 12,
        };

        foreach (Weapon w in AllArchery())
        {
            Assert.Equal(expected[w.Name], w.DamageMin, 6);
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
    public void 生态位_复合弩是射程与精度之王()
    {
        // 🔴 用户拍板（[DECISION] impl-archery-redo, journal 2026-07-15）：以 wiki 为准。
        // wiki 接受值下复合弩同时拥有全表最远射程 550px 与最小散布 1.8°。
        Weapon[] bows = AllArchery();

        double maxRange = bows.Max(w => w.MaxRange!.Value);
        Assert.Equal(550, maxRange);
        Assert.Equal("复合弩", bows.OrderByDescending(w => w.MaxRange!.Value).First().Name);
        Assert.Equal("复合弩", bows.OrderBy(w => w.BaseSpreadDegrees).First().Name);
    }

    /// <summary>
    /// ⚠️ [T56] <b>「狩猎弓是伤害之王」这条已被用户推翻，故从本测试中删除</b>。
    /// <para>
    /// 用户重设了两把弓的生态位（原话）：「竞技复合弓和狩猎弓是<b>同级别武器</b>，区别是
    /// <b>竞技复合弓远而准，狩猎弓快</b>」——狩猎弓从"慢、重、狠的伤害之王"改成了
    /// <b>全表最快的弓</b>（冷却 1.6s、伤害仅 3.5~9.75）。它<b>不再</b>是伤害之王，这是<b>设计意图</b>，不是回归。
    /// 狩猎弓的新人设（最快 + 近而糙）由 <c>BoneKnifeAndHuntingBowTests</c> 钉死。
    /// </para>
    /// <para>「复合弩是破甲之王」不受影响，保留。</para>
    /// </summary>
    [Fact]
    public void 生态位_复合弩是破甲之王()
    {
        Weapon[] bows = AllArchery();

        Assert.Equal("复合弩", bows.OrderByDescending(w => w.Penetration).First().Name);
    }

    [Fact]
    public void 生态位_短弓是最弱的入门弓_复合弩是最慢的()
    {
        Weapon[] bows = AllArchery();

        // 🔴 [T56 终结] 全表已同步到 wiki 新值（[DECISION] impl-archery-redo, journal 2026-07-15），
        // 不再有"新旧代"之分——直接按 **DPS** 比全部**弓**（含狩猎弓）：短弓 DPS 最低（入门款，木料2+绳1 就能削出来）。
        Assert.Equal("短弓", bows.Where(IsBow).OrderBy(Dps).First().Name);

        // 🔴 「最慢」由双手重弩（wiki 降到 5.0s）易主为**复合弩（6.2s，全表最慢）**——复合弩「最慢」生态位以 wiki 为新事实源。
        Assert.Equal("复合弩", bows.OrderByDescending(w => w.AttackInterval).First().Name);
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
        //
        // ⚠ 门槛按用户手改后的数值放宽过一次：木箭破甲 0.55 → 0.75（不再"遇甲即废"），
        //   碳纤维/木箭的穿透比从 2.27× 收窄到 1.67×。**收窄是用户的意图**——木箭被扶正成
        //   "便宜好用的主力箭"，两端本就该靠近。这里改钉「三轴各拉开 ≥50%」，量级差仍在，
        //   同时把**精度**也纳进来：它现在是木箭与碳纤维箭之间最诚实的一条鸿沟。
        Weapon bow = WeaponTable.RecurveBow();
        Weapon stick = Archery.Combine(bow, ArrowTable.SharpenedStick());
        Weapon carbon = Archery.Combine(bow, ArrowTable.Carbon());

        Assert.True(carbon.DamageMax >= stick.DamageMax * 1.5, "碳纤维箭伤害应比木箭高 50% 以上");
        Assert.True(carbon.Penetration >= stick.Penetration * 1.5, "碳纤维箭穿透应比木箭高 50% 以上");
        Assert.True(carbon.BaseSpreadDegrees <= stick.BaseSpreadDegrees * 0.75, "碳纤维箭散布应比木箭小 25% 以上");
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

    // ==================== 《弓与箭之道》的三项被动（用户在数值表『书籍』页写下） ====================
    //
    // 用户原话（旧）：「弓箭射程+10%，锥形角-10%，攻速+2%」；[T68] 射程+10% 已换成弹道速度+20%（挂起的引擎新轴）⇒ 现存两项都**只作用于弓弩**（"弓箭"），
    // 且都是**射手本人读过书**才吃到（判据＝其 ReadBookSet，与回收率 25%→50% 同一条口径）。

    [Fact]
    public void 书_三项加成的方向与幅度_用户口径()
    {
        // 🔴 [T68] 用户把「射程 +10%」换成了「弹道速度 +20%」（引擎新轴，未落地）⇒ 射程加成已中和为 ×1.0。
        Assert.Equal(1.0, Archery.BookRangeMult);           // [T68] 射程加成删除（原 +10%，换成弹道速度+20%＝挂起的新轴）
        Assert.Equal(0.90, Archery.BookSpreadMult);         // 锥形角 −10%（散布收窄＝更准）
        Assert.Equal(1.02, Archery.BookAttackSpeedMult);    // 攻速 +2%

        // 攻速是"每秒出手数"，冷却是"两次出手的间隔"——互为倒数，不是同一个数。
        Assert.Equal(1.0 / 1.02, Archery.BookCooldownMult, 12);
        Assert.True(Archery.BookCooldownMult < 1.0, "攻速+2% ⇒ 出手间隔必须变短");
    }

    [Fact]
    public void 书_未读时逐字段与旧行为一致_零回归()
    {
        // 默认参数 false ＝ 原样。这条护栏保证：这次加书不会碰到任何"没读书的人"的既有数值，
        // Sim 的既有基线（一律走默认重载）不可能漂移。
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Weapon oldPath = Archery.Combine(bow, arrow);
                Weapon unread = Archery.Combine(bow, arrow, hasReadArcheryBook: false);

                Assert.Equal(oldPath.DamageMax, unread.DamageMax, 12);
                Assert.Equal(oldPath.Penetration, unread.Penetration, 12);
                Assert.Equal(oldPath.AttackInterval, unread.AttackInterval, 12);
                Assert.Equal(oldPath.BaseSpreadDegrees, unread.BaseSpreadDegrees, 12);
                Assert.Equal(oldPath.MaxRange, unread.MaxRange);
                Assert.Equal(oldPath.FalloffStart, unread.FalloffStart);
            }
        }
    }

    [Fact]
    public void 书_只作用于弓弩_枪与近战一个字段都不碰()
    {
        // "弓箭射程+10%"是弓箭的事。读了射艺书不会让你的步枪打得更远、匕首挥得更快。
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => !Archery.UsesArrows(w)))
        {
            Assert.Same(w, Archery.Combine(w, ArrowTable.Handmade(), hasReadArcheryBook: true));
            Assert.Same(w, Archery.Combine(w, (ArrowDef?)null, hasReadArcheryBook: true));
        }
    }

    [Fact]
    public void 书_散布与冷却两项_对弓与弩都生效()
    {
        // 弩也吃（用户说的"弓箭"＝这套弹药体系，弓与弩共用箭，见 ArrowKeys 注释）。
        // 🔴 [T68] **射程加成已删除**（用户换成弹道速度+20%＝引擎新轴，挂起）⇒ 读书后射程**不再变化**，只剩散布/攻速两项。
        foreach (Weapon bow in AllArchery())
        {
            Weapon unread = Archery.Combine(bow, ArrowTable.Handmade());
            Weapon read = Archery.Combine(bow, ArrowTable.Handmade(), hasReadArcheryBook: true);

            Assert.Equal(unread.MaxRange!.Value, read.MaxRange!.Value, 12);   // [T68] 射程不再被书改动（加成已中和为 ×1.0）
            Assert.True(read.BaseSpreadDegrees < unread.BaseSpreadDegrees, $"{bow.Name}：读过书应更准");
            Assert.True(read.AttackInterval < unread.AttackInterval, $"{bow.Name}：读过书出手应更快");

            // 伤害/穿透**不在里面**——书教的是射得准、抽箭快，不是把箭头磨利。
            Assert.Equal(unread.DamageMax, read.DamageMax, 12);
            Assert.Equal(unread.Penetration, read.Penetration, 12);
        }
    }

    [Fact]
    public void 书_与箭的同轴系数一律连乘_不是加算()
    {
        // CLAUDE.md 铁律：百分比一律乘算。射程 = 弓基础 × 箭系数 × 1.10，
        // **不是** 弓基础 × (箭系数 + 0.10)。用重头箭（射程 ×0.75、冷却 ×1.10、散布 ×1.25）验三条轴。
        Weapon bow = WeaponTable.Longbow();
        ArrowDef heavy = ArrowTable.Heavy();
        Weapon read = Archery.Combine(bow, heavy, hasReadArcheryBook: true);

        Assert.Equal(bow.MaxRange!.Value * heavy.RangeMult * Archery.BookRangeMult, read.MaxRange!.Value, 9);
        Assert.Equal(bow.FalloffStart!.Value * heavy.RangeMult * Archery.BookRangeMult, read.FalloffStart!.Value, 9);
        Assert.Equal(bow.AttackInterval * heavy.CooldownMult * Archery.BookCooldownMult, read.AttackInterval, 9);
        Assert.Equal(bow.BaseSpreadDegrees * heavy.SpreadMult * Archery.BookSpreadMult, read.BaseSpreadDegrees, 9);

        // 加算会得到别的数（射程 0.75+0.10=0.85 ≠ 0.75×1.10=0.825）——钉死它不是加算。
        Assert.NotEqual(bow.MaxRange.Value * (heavy.RangeMult + 0.10), read.MaxRange.Value, 3);
    }

    [Fact]
    public void 书_攻速正好加2pct_不多不少()
    {
        // 攻速 +2% 的定义：每秒出手数 ×1.02 ⇔ 间隔 ÷1.02。
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                double unread = Archery.Combine(bow, arrow).AttackInterval;
                double read = Archery.Combine(bow, arrow, hasReadArcheryBook: true).AttackInterval;

                Assert.Equal(1.02, unread / read, 9);   // 出手频率之比 ＝ 攻速加成
            }
        }
    }

    [Fact]
    public void 书_加成后仍受穿透上限与散布下限的钳制()
    {
        // 书把散布再压 10%，仍不许任何组合变成"绝对精准"——弓总有手抖。
        foreach (Weapon bow in AllArchery())
        {
            foreach (ArrowDef arrow in ArrowTable.All)
            {
                Weapon read = Archery.Combine(bow, arrow, hasReadArcheryBook: true);

                Assert.True(read.BaseSpreadDegrees >= Archery.MinSpreadDegrees);
                Assert.True(read.Penetration <= Archery.MaxPenetration);
            }
        }
    }

    [Fact]
    public void 书_Combine仍是纯函数_不改入参也可重复调用()
    {
        Weapon bow = WeaponTable.RecurveBow();
        double range0 = bow.MaxRange!.Value;
        double interval0 = bow.AttackInterval;

        Weapon a = Archery.Combine(bow, ArrowTable.Carbon(), hasReadArcheryBook: true);
        Weapon b = Archery.Combine(bow, ArrowTable.Carbon(), hasReadArcheryBook: true);

        Assert.Equal(a.MaxRange, b.MaxRange);
        Assert.Equal(a.AttackInterval, b.AttackInterval, 12);
        Assert.Equal(range0, bow.MaxRange!.Value);          // 入参弓一个字段都没被改
        Assert.Equal(interval0, bow.AttackInterval, 12);
    }

    [Fact]
    public void 书_按材料键搭箭的重载也吃这三项()
    {
        Weapon bow = WeaponTable.ShortBow();

        Weapon byKey = Archery.Combine(bow, ArrowKeys.Handmade, hasReadArcheryBook: true);
        Weapon byDef = Archery.Combine(bow, ArrowTable.Handmade(), hasReadArcheryBook: true);

        Assert.Equal(byDef.MaxRange, byKey.MaxRange);
        Assert.Equal(byDef.AttackInterval, byKey.AttackInterval, 12);
        Assert.Equal(byDef.BaseSpreadDegrees, byKey.BaseSpreadDegrees, 12);
    }

    [Fact]
    public void 书_加成属于射手而非箭_空箭壶时也照样改写弓()
    {
        // 三项加成是**人**的本事（挂在读者身上），不是箭的属性。故即便没搭箭，弓的这三项也已是加成后的样子。
        // （实战里没箭就射不出去，这条只是把"加成归属于谁"钉死，防止将来有人把它挪进 ArrowDef。）
        Weapon bow = WeaponTable.HuntingBow();
        Weapon read = Archery.Combine(bow, (ArrowDef?)null, hasReadArcheryBook: true);

        Assert.Equal(bow.MaxRange!.Value * Archery.BookRangeMult, read.MaxRange!.Value, 9);
        Assert.Equal(bow.BaseSpreadDegrees * Archery.BookSpreadMult, read.BaseSpreadDegrees, 9);
        Assert.Equal(bow.AttackInterval * Archery.BookCooldownMult, read.AttackInterval, 9);
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
