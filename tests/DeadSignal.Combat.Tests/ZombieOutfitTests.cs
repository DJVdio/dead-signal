using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 丧尸的衣服。两条互不相干的通路：
/// <para>
/// ① <b>日常着装（随机）</b>——用户口径："大部分丧尸应当都是基础的布衣/夹克/长裤/短裤等灾难发生时的日常着装"。
/// 生成时按加权预设抽一套，叠在腐皮之外。锁：预设表/权重、只用日常衣物（**含皮夹克**，护甲件不得混入）、
/// 抽取走 <see cref="IRandomSource"/> 且可复现。
/// </para>
/// <para>
/// ② <b>精英丧尸（authored·具名）</b>——用户口径："只有少部分我人为设定的高难度丧尸会穿护甲"。
/// **不进随机池**，由关卡/Spawn 侧按名字点名。锁：具名可取、精英不污染随机池、只用护甲表现有件。
/// </para>
/// 通用铁律：腐皮与布类同占 <see cref="ArmorSlot.Skin"/> 槽、靠输入顺序定内外 ⇒ <b>腐皮恒为最内层</b>。
/// 防护值一律取 <see cref="ArmorTable"/> 表值（**不做破损折损**，"破损"由部分覆盖表达）。
/// </summary>
public class ZombieOutfitTests
{
    private static readonly HashSet<string> BareParts = new()
    {
        HumanBody.Head, HumanBody.LeftFoot, HumanBody.RightFoot,
    };

    /// <summary>roll 落在某日常预设区间正中的随机值（预设按权重顺序排布在 [0,1) 上）。</summary>
    private static IRandomSource At(int presetIndex)
    {
        double lo = ZombieOutfit.Presets.Take(presetIndex).Sum(p => p.Weight);
        double mid = lo + ZombieOutfit.Presets[presetIndex].Weight / 2;
        return new SequenceRandomSource(mid);
    }

    // ---- ① 日常着装（随机池）----

    [Fact]
    public void DailyPresets_WeightsSumToOne()
    {
        Assert.Equal(1.0, ZombieOutfit.Presets.Sum(p => p.Weight), precision: 6);
        Assert.All(ZombieOutfit.Presets, p => Assert.True(p.Weight > 0));
    }

    [Fact]
    public void DailyPresets_MatchLockedDistribution_IncludingJacket()
    {
        // 用户口径：日常着装 = 布衣 / 夹克 / 长裤 / 短裤（"等" = 含已有的背心、外套）。
        // 夹克按外套层梯度铺开（布夹克 8/4 → 牛仔外套 10/5 → 皮夹克 12/6），**越挡刀的越稀有**。
        var expected = new (string Name, double Weight, string[] Clothes)[]
        {
            ("衣不蔽体", 0.15, System.Array.Empty<string>()),
            ("仅剩长裤", 0.18, new[] { "长裤" }),
            ("仅剩上衣", 0.13, new[] { "长袖布衣" }),
            ("寻常打扮", 0.24, new[] { "长袖布衣", "长裤" }),
            ("穿布夹克上班的", 0.08, new[] { "布夹克", "长袖布衣", "长裤" }),
            ("穿牛仔外套的", 0.06, new[] { "牛仔外套", "长袖布衣", "长裤" }),
            ("穿皮夹克的", 0.04, new[] { "皮夹克", "长袖布衣", "长裤" }),
            ("套着粗布外套", 0.02, new[] { "粗布外套", "长袖布衣", "长裤" }),
            ("夏日打扮", 0.10, new[] { "粗布背心", "短裤" }),
        };

        Assert.Equal(expected.Length, ZombieOutfit.Presets.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            ZombieOutfitPreset actual = ZombieOutfit.Presets[i];
            Assert.Equal(expected[i].Name, actual.Name);
            Assert.Equal(expected[i].Weight, actual.Weight);
            Assert.Equal(expected[i].Clothes, actual.Clothes().Select(a => a.Name).ToArray());
        }
    }

    [Fact]
    public void DailyPresets_MostZombiesWearSomething()
    {
        // 用户口径「**大部分**丧尸都是日常着装」。
        double dressed = ZombieOutfit.Presets.Where(p => p.Clothes().Count > 0).Sum(p => p.Weight);
        Assert.Equal(0.85, dressed, precision: 6);
        Assert.True(dressed > 0.5, "大部分丧尸应当穿着日常着装");
    }

    [Fact]
    public void DailyPresets_UseEverydayWear_NeverBodyArmor()
    {
        // 日常着装可以有皮夹克（灾难当天身上本来就穿着）；但**护甲件**（皮革胸甲/皮甲/板甲）是精英丧尸的
        // authored 范畴，绝不能混进随机池——否则"少部分人为设定的高难度丧尸才穿护甲"就被随机破坏了。
        var everyday = new HashSet<string>
        {
            "长袖布衣", "长裤", "短裤", "粗布背心", "粗布外套", "布夹克", "牛仔外套", "皮夹克",
        };
        var bodyArmor = new HashSet<string> { "皮革胸甲", "皮甲", "板甲" };

        foreach (ArmorLayer a in ZombieOutfit.Presets.SelectMany(p => p.Clothes()))
        {
            Assert.Contains(a.Name, everyday);
            Assert.DoesNotContain(a.Name, bodyArmor);
            Assert.NotEqual(ArmorSlot.Plate, a.Slot); // 随机池里不得出现装甲层（含头盔——它们同为 Plate 层）
        }
    }

    [Fact]
    public void DailyPresets_OuterLayer_FollowsRealWorldFrequency()
    {
        // 外套层四件互斥（一具丧尸只穿一件）。频率按"灾难当天现实里谁穿得多"排：
        // 布夹克/牛仔外套是最常见的日常外套 → 皮夹克次之 → 粗布外套（自制感、末世产物）最少见。
        // 顺带满足"越挡刀的越稀有"：布夹克(8/4) > 牛仔外套(10/5) > 皮夹克(12/6)。
        double Rate(string outer) => ZombieOutfit.Presets
            .Where(p => p.Clothes().Any(a => a.Name == outer))
            .Sum(p => p.Weight);

        double cloth = Rate("布夹克");     // 8/4
        double denim = Rate("牛仔外套");   // 10/5
        double leather = Rate("皮夹克");   // 12/6
        double coarse = Rate("粗布外套");  // 6/3

        Assert.Equal(0.20, cloth + denim + leather + coarse, precision: 6); // 外套层合计 20%
        Assert.Equal(0.18, cloth + denim + leather, precision: 6);          // 其中夹克类 18%
        Assert.True(cloth > denim && denim > leather && leather > coarse,
            "布夹克 > 牛仔外套 > 皮夹克 > 粗布外套（自制感的最少见）");
    }

    [Fact]
    public void DailyPresets_OuterLayerPieces_AreMutuallyExclusive()
    {
        // 四件外套同占 ArmorSlot.Outer → 任一套装束里最多只能有一件。
        foreach (ZombieOutfitPreset p in ZombieOutfit.Presets)
        {
            Assert.True(p.Clothes().Count(a => a.Slot == ArmorSlot.Outer) <= 1,
                $"「{p.Name}」同时穿了多件外套");
        }
    }

    [Fact]
    public void Clothes_UseUnmodifiedTableValues_NoWearAndTearDiscount()
    {
        // 「破损」不打折——表值即最终值（破损由"只剩几件 + 头脚全裸"的部分覆盖表达）。
        var table = new[]
        {
            ArmorTable.LongSleeveShirt(), ArmorTable.Trousers(), ArmorTable.Shorts(),
            ArmorTable.CoarseClothVest(), ArmorTable.CoarseClothCoat(),
            ArmorTable.ClothJacket(), ArmorTable.DenimJacket(), ArmorTable.LeatherJacket(),
            ArmorTable.ChestPlate(), ArmorTable.Leather(), ArmorTable.Plate(), ArmorTable.WorkGloves(),
            // 精英专属头盔（[SPEC-B19]）：只在精英预设里出现，日常池永远抽不到。
            ArmorTable.MilitaryHelmet(), ArmorTable.RiotHelmet(),
        }.ToDictionary(a => a.Name);

        IEnumerable<ArmorLayer> all = ZombieOutfit.Presets.SelectMany(p => p.Clothes())
            .Concat(ZombieOutfit.ElitePresets.SelectMany(p => p.Clothes()));

        foreach (ArmorLayer a in all)
        {
            ArmorLayer t = table[a.Name];
            Assert.Equal(t.SharpDefense, a.SharpDefense);
            Assert.Equal(t.BluntDefense, a.BluntDefense);
            Assert.Equal(t.Slot, a.Slot);
        }
    }

    [Fact]
    public void DailyClothes_NeverCover_HeadOrFeet()
    {
        // 与用户定稿的护甲表对称：日常着装没有帽子和鞋 → 丧尸的头/脚裸露。
        foreach (ArmorLayer a in ZombieOutfit.Presets.SelectMany(p => p.Clothes()))
        {
            Assert.NotNull(a.CoversParts);
            Assert.Empty(a.CoversParts!.Intersect(BareParts));
        }
    }

    [Fact]
    public void RollArmor_AlwaysEndsWithHide_SoOrderOuterToInnerKeepsHideInnermost()
    {
        for (int i = 0; i < ZombieOutfit.Presets.Count; i++)
        {
            IReadOnlyList<ArmorLayer> armor = ZombieOutfit.RollArmor(At(i));

            Assert.Equal("腐皮", armor[^1].Name);
            Assert.Single(armor.Where(a => a.Name == "腐皮"));
            Assert.Equal("腐皮", CombatResolver.OrderOuterToInner(armor)[^1].Name);
        }
    }

    [Fact]
    public void RollArmor_NakedPreset_IsHideOnly()
    {
        IReadOnlyList<ArmorLayer> armor = ZombieOutfit.RollArmor(At(0)); // 「衣不蔽体」
        Assert.Equal("腐皮", Assert.Single(armor).Name);
    }

    [Fact]
    public void RollArmor_PicksPresetByWeightedRoll_Reproducible()
    {
        // 累积区间：衣不蔽体[0,.15) 仅剩长裤[.15,.33) 仅剩上衣[.33,.46) 寻常打扮[.46,.70)
        //           穿布夹克上班的[.70,.78) 穿牛仔外套的[.78,.84) 穿皮夹克的[.84,.88)
        //           套着粗布外套[.88,.90) 夏日打扮[.90,1)
        IReadOnlyList<ArmorLayer> armor = ZombieOutfit.RollArmor(new SequenceRandomSource(0.75));
        Assert.Equal(new[] { "布夹克", "长袖布衣", "长裤", "腐皮" }, armor.Select(a => a.Name).ToArray());

        IReadOnlyList<ArmorLayer> again = ZombieOutfit.RollArmor(new SequenceRandomSource(0.75));
        Assert.Equal(armor.Select(a => a.Name), again.Select(a => a.Name));

        // 最强的那件夹克要抽到 [.84,.88) 才有——稀有。
        Assert.Equal(
            new[] { "皮夹克", "长袖布衣", "长裤", "腐皮" },
            ZombieOutfit.RollArmor(new SequenceRandomSource(0.86)).Select(a => a.Name).ToArray());
    }

    [Fact]
    public void RollArmor_ConsumesExactlyOneRoll()
    {
        var rng = new SequenceRandomSource(0.6, 0.1);
        ZombieOutfit.RollArmor(rng);
        Assert.Equal(1, rng.Remaining); // 一只丧尸只抽一次装束
    }

    [Fact]
    public void RollArmor_RollAtUpperBound_StillYieldsLastPreset_NoOutOfRange()
    {
        IReadOnlyList<ArmorLayer> armor = ZombieOutfit.RollArmor(new SequenceRandomSource(1.0));
        Assert.Equal(new[] { "粗布背心", "短裤", "腐皮" }, armor.Select(a => a.Name).ToArray());
    }

    // ---- ② 精英丧尸（authored·具名，不进随机池）----

    [Fact]
    public void ElitePresets_AreNotInTheRandomPool()
    {
        // 铁律：精英丧尸是**用户人为设定**的，只能被点名，绝不能被随机抽到。
        var dailyNames = ZombieOutfit.Presets.Select(p => p.Name).ToHashSet();
        Assert.NotEmpty(ZombieOutfit.ElitePresets);

        foreach (ZombieOutfitPreset elite in ZombieOutfit.ElitePresets)
        {
            Assert.DoesNotContain(elite.Name, dailyNames);
            Assert.Equal(0, elite.Weight); // 权重 0 = 不参与加权抽取
        }

        // 把随机池抽穿也抽不出护甲件。
        var rng = new SystemRandomSource(20260713);
        for (int i = 0; i < 20_000; i++)
        {
            Assert.DoesNotContain(ZombieOutfit.RollArmor(rng), a => a.Slot == ArmorSlot.Plate);
        }
    }

    [Fact]
    public void ElitePresets_AreDraft_AwaitingUserAuthoring()
    {
        // 样板预设是 draft（待用户定稿），不是最终设定——标志位在此锁住，防止被当成既定内容。
        Assert.All(ZombieOutfit.ElitePresets, p => Assert.True(p.IsDraft));
    }

    [Fact]
    public void ElitePresets_WearRealBodyArmor_FromTheArmorTable()
    {
        foreach (ZombieOutfitPreset elite in ZombieOutfit.ElitePresets)
        {
            Assert.Contains(elite.Clothes(), a => a.Slot == ArmorSlot.Plate); // 精英 = 真的穿护甲
        }
    }

    [Fact]
    public void ArmorOf_NamedPreset_IsDeterministic_AndHideStaysInnermost()
    {
        IReadOnlyList<ArmorLayer> a1 = ZombieOutfit.ArmorOf("防暴警察丧尸");
        IReadOnlyList<ArmorLayer> a2 = ZombieOutfit.ArmorOf("防暴警察丧尸");

        Assert.Equal(a1.Select(x => x.Name), a2.Select(x => x.Name)); // 具名 = 确定性，不掷骰
        Assert.Contains(a1, x => x.Name == "板甲");
        Assert.Equal("腐皮", a1[^1].Name);
        Assert.Equal("腐皮", CombatResolver.OrderOuterToInner(a1)[^1].Name);
    }

    [Fact]
    public void ArmorOf_AlsoResolvesDailyPresetsByName()
    {
        // 关卡侧也能点名一套日常装（如剧情要一只"只剩长裤"的丧尸）。
        IReadOnlyList<ArmorLayer> armor = ZombieOutfit.ArmorOf("仅剩长裤");
        Assert.Equal(new[] { "长裤", "腐皮" }, armor.Select(a => a.Name).ToArray());
    }

    [Fact]
    public void ArmorOf_UnknownName_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => ZombieOutfit.ArmorOf("不存在的丧尸"));
    }

    [Fact]
    public void Fixed_PluggableInto_DuelFighterArmorFactory_IgnoringRng()
    {
        // 具名预设要能塞进 DuelFighter.ArmorFactory（Sim 侧点名精英丧尸），且**不消耗随机源**。
        System.Func<IRandomSource, IReadOnlyList<ArmorLayer>> factory = ZombieOutfit.Fixed("军人丧尸");
        var rng = new SequenceRandomSource(0.5);

        IReadOnlyList<ArmorLayer> armor = factory(rng);

        Assert.Equal(1, rng.Remaining); // 一次 roll 都没花
        Assert.Contains(armor, a => a.Slot == ArmorSlot.Plate);
        Assert.Equal("腐皮", armor[^1].Name);
    }

    [Fact]
    public void EliteZombie_IsMuchHarderToHurt_ThanDailyZombie()
    {
        // 精英丧尸存在的意义：板甲（锐防 50）把挡下门槛抬到 25，寻常武器基本破不了躯干。
        var blade = new Weapon
        {
            Name = "测试锐器", DamageMin = 2.5, DamageMax = 2.5, Penetration = 0, DamageType = DamageType.Sharp,
        };
        BodyPart chest = HumanBody.NewBody().Parts[HumanBody.Chest];

        IReadOnlyList<ArmorLayer> elite =
            CombatResolver.OrderOuterToInner(ZombieOutfit.ArmorOf("防暴警察丧尸"));

        // 装甲层在最外。[SPEC-B19] 起精英还戴头盔（同为 Plate 层，按输入序排在板甲之前）——头盔不覆盖胸，
        // 故对这一击而言，**胸口最外的一层仍是板甲**。
        Assert.Equal(ArmorSlot.Plate, elite[0].Slot);
        Assert.Equal(
            "板甲",
            elite.First(l => l.CoversParts is null || l.CoversParts.Contains(HumanBody.Chest)).Name);
        // 板甲最外层：def 掷 5.1 就已 > 2×2.5 → 挡下（日常布衣要掷到 6 才挡得下；板甲上限 50，几乎必挡）。
        CombatResult r = new CombatResolver(new SequenceRandomSource(2.5, 5.1)).Resolve(blade, elite, chest);
        Assert.Equal(0, r.FinalDamage);
    }

    [Fact]
    public void ClothedZombie_CanBlock_WhereNakedHideMathematicallyNever_Could()
    {
        // 本机制的存在理由。逐层结算：atk ∈ [伤害下限,上限]、def ∈ [0, 护甲值×(1−穿透)]，atk < def/2 才算挡下
        // → **单层甲有可能挡下一击 ⟺ 护甲值×(1−穿透)/2 > 武器伤害下限**。
        // 腐皮锐防 3 → 门槛 1.5；长袖布衣锐防 6 → 门槛 3.0。取一把伤害恒为 2.5 的锐器（不吃 WeaponTable
        // 的在途改动）夹在两个门槛之间：腐皮**即使掷出满防也挡不下**，布衣则挡得下。
        var blade = new Weapon
        {
            Name = "测试锐器", DamageMin = 2.5, DamageMax = 2.5, Penetration = 0, DamageType = DamageType.Sharp,
        };
        BodyPart chest = HumanBody.NewBody().Parts[HumanBody.Chest];

        IReadOnlyList<ArmorLayer> hideOnly = CombatResolver.OrderOuterToInner(ZombieOutfit.RollArmor(At(0)));
        CombatResult naked = new CombatResolver(new SequenceRandomSource(2.5, 3.0)).Resolve(blade, hideOnly, chest);
        Assert.Equal("腐皮", Assert.Single(hideOnly).Name);
        Assert.True(naked.FinalDamage > 0, "腐皮：掷出满防仍挡不下（数学上恒不可能挡下）");

        // 「寻常打扮」（index 3）：命中胸过长袖布衣（长裤不覆盖胸，被引擎按部位过滤掉）。
        IReadOnlyList<ArmorLayer> dressed = CombatResolver.OrderOuterToInner(ZombieOutfit.RollArmor(At(3)));
        CombatResult clothed = new CombatResolver(new SequenceRandomSource(2.5, 6.0)).Resolve(blade, dressed, chest);
        Assert.Equal(0, clothed.FinalDamage);
    }
}
