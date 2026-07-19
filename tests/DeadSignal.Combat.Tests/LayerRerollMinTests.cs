using DeadSignal.Combat;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 【T59】逐层结算的攻方 roll 口径 —— **重掷 + 取较小者**（用户拍板，方案 E）。
///
/// <para><b>修掉的缺陷</b>：旧口径「第二层起攻方在 <c>[0, 上一层结算后的伤害值]</c> 内 roll」使得
/// <b>期望伤害每多一层就 ×0.5</b>，且与那一层的防御力、武器、伤害类型、穿透 <b>全部无关</b> ——
/// 连一层**防御为 0** 的破布也照样把伤害砍半（防御 0 ⇒ 必判 Full ⇒ carried 原样带下，
/// 但下一层仍在 <c>[0, carried]</c> 重掷 ⇒ 白送 50%）。实测：三片零防御布片 = 25%，
/// 比全表最强的板甲（41%）还抗揍。</para>
///
/// <para><b>新口径（用户原话）</b>：「每一层都要重新 roll 攻击值，和上一层穿透过来的伤害值
/// 取较小的那个作为这一层的实际值」：
/// <code>
/// rolled = Range(武器.DamageMin, 武器.DamageMax)   // 区间【不再收缩】，永远是武器的原始区间
/// atk    = min(rolled, 上一层结算后的伤害)          // 上一层带下来的伤害是【上限】
/// </code>
/// ⇒ 掷高了被上层的 carried 卡住（**伤害不会越穿越大**）；掷低了才吃亏。
/// ⇒ **层数不再无条件减半**；衰减有下限（趋向武器伤害区间下界）。</para>
/// </summary>
public class LayerRerollMinTests
{
    private static readonly BodyPart Chest = new() { Name = "胸部", VolumeWeight = 40 };

    private static ArmorLayer Nil(ArmorSlot slot) => new()
    {
        Name = "零防御布片", Slot = slot, SharpDefense = 0, BluntDefense = 0,
    };

    /// <summary>
    /// 🔴 核心：第二层的 roll 区间是**武器的原始区间**，不是收缩后的 <c>[0, carried]</c>。
    /// 本例第二层掷出 18 —— 它**大于**上一层带下来的 15。
    /// <b>旧实现下这一行会直接抛异常</b>（SequenceRandomSource 校验 18 ∉ [0,15]）⇒ 精确打红。
    /// 新实现：18 被 carried=15 卡住 ⇒ 取 15 ⇒ **不衰减**。
    /// </summary>
    [Fact]
    public void SecondLayer_RollsFullWeaponRange_AndIsCappedByCarried()
    {
        var weapon = new Weapon { Name = "重剑", DamageMin = 10, DamageMax = 20, Penetration = 0, DamageType = DamageType.Sharp };
        var layers = new[] { Nil(ArmorSlot.Plate), Nil(ArmorSlot.Skin) };

        // atk1=15（零防御必 Full → carried 15）、def1=0、atk2=18（∈[10,20]，但 >carried）、def2=0
        var rng = new SequenceRandomSource(15, 0, 18, 0);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.Equal(2, r.LayersPenetrated);
        Assert.Equal(15, r.Layers[1].AttackRoll, 9);      // 取 min(18, 15) = 15
        Assert.Equal(15, r.Layers[1].DamageAfterLayer, 9);
        Assert.Equal(15, r.FinalDamage, 9);               // **一点没衰减**：零防御层不再白送减伤
        Assert.Equal(0, rng.Remaining);
    }

    /// <summary>掷低了才吃亏：rolled(11) &lt; carried(15) ⇒ 取 11。</summary>
    [Fact]
    public void SecondLayer_TakesRoll_WhenRollIsLowerThanCarried()
    {
        var weapon = new Weapon { Name = "重剑", DamageMin = 10, DamageMax = 20, Penetration = 0, DamageType = DamageType.Sharp };
        var layers = new[] { Nil(ArmorSlot.Plate), Nil(ArmorSlot.Skin) };

        var rng = new SequenceRandomSource(15, 0, 11, 0);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.Equal(11, r.Layers[1].AttackRoll, 9);
        Assert.Equal(11, r.FinalDamage, 9);
    }

    /// <summary>
    /// 🔴 用户点名要小心的 case ①：Half 分支的 <c>carriedDamage = atk / 2</c> 里的 <c>atk</c>
    /// 必须是**取 min 之后的实际值**，不是原始 rolled。
    /// 本例第二层 rolled=20、carried=10 ⇒ atk=10 ⇒ 半伤 = **5**（若误用 rolled 会得 10）。
    /// </summary>
    [Fact]
    public void HalfBranch_HalvesThePostMinValue_NotTheRawRoll()
    {
        var weapon = new Weapon { Name = "重剑", DamageMin = 10, DamageMax = 20, Penetration = 0, DamageType = DamageType.Sharp };
        var layers = new[]
        {
            Nil(ArmorSlot.Plate),
            new ArmorLayer { Name = "内层", Slot = ArmorSlot.Skin, SharpDefense = 20, BluntDefense = 10 },
        };

        // 层1：atk=10、零防御 → Full → carried 10
        // 层2：rolled=20 → min(20, 10) = 10；def=16 → 10 >= 8 → Half → carried = 10/2 = 5、转钝
        var rng = new SequenceRandomSource(10, 0, 20, 16);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.Equal(10, r.Layers[1].AttackRoll, 9);           // 记的是 min 之后的值
        Assert.Equal(LayerOutcome.Half, r.Layers[1].Outcome);
        Assert.Equal(5, r.Layers[1].DamageAfterLayer, 9);      // 10/2，不是 20/2
        Assert.Equal(5, r.FinalDamage, 9);
        Assert.True(r.Layers[1].ConvertedToBlunt);
    }

    /// <summary>
    /// 🔴 用户点名要小心的 case ②：**锐转钝之后，后续层重掷用的仍是「武器的原始伤害区间」**。
    /// 转换改的是 <see cref="DamageType"/> 与穿透（归零），**不改武器的伤害区间**
    /// （<see cref="Weapon"/> 上只有一组 DamageMin/DamageMax，本就与伤害类型无关）。
    /// 本例：层1 半伤转钝 carried=5；层2 在 [10,10] 重掷得 10，被 carried 卡到 5 ⇒ 钝伤 5、穿透 0、用钝防。
    /// </summary>
    [Fact]
    public void AfterSharpToBlunt_NextLayerStillRerollsWeaponsOriginalRange()
    {
        var weapon = new Weapon { Name = "长剑", DamageMin = 10, DamageMax = 10, Penetration = 0.5, DamageType = DamageType.Sharp };
        var layers = new[]
        {
            new ArmorLayer { Name = "外", Slot = ArmorSlot.Plate, SharpDefense = 40, BluntDefense = 20 },
            new ArmorLayer { Name = "内", Slot = ArmorSlot.Skin, SharpDefense = 40, BluntDefense = 20 },
        };

        // 层1：锐防40、穿透0.5 → defMax=20；atk=10 vs def=15 → 10>=7.5 → Half → carried=5、转钝、穿透归零
        // 层2：rolled=10（武器原始区间 [10,10]）→ min(10, 5) = 5；钝防20、穿透0 → defMax=20；def=2 → Full → 5
        var rng = new SequenceRandomSource(10, 15, 10, 2);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.True(r.Layers[0].ConvertedToBlunt);
        Assert.Equal(5, r.Layers[1].AttackRoll, 9);        // min(10, 5)：carried 才是上限
        Assert.Equal(0, r.Layers[1].PenetrationUsed, 9);   // 转钝 ⇒ 穿透归零（不变）
        Assert.Equal(20, r.Layers[1].ApplicableDefense);   // 转钝 ⇒ 用钝防（不变）
        Assert.Equal(DamageType.Blunt, r.FinalDamageType);
        Assert.Equal(5, r.FinalDamage, 9);
    }

    /// <summary>
    /// 天然钝器每层保留自身穿透（不进转换分支）—— 方案 E 不得破坏这条既有口径。
    /// </summary>
    [Fact]
    public void NaturalBlunt_KeepsOwnPenetration_UnderRerollMin()
    {
        var weapon = new Weapon { Name = "破甲锤", DamageMin = 10, DamageMax = 10, Penetration = 0.2, DamageType = DamageType.Blunt };
        var layers = new[]
        {
            new ArmorLayer { Name = "外", Slot = ArmorSlot.Plate, SharpDefense = 40, BluntDefense = 20 },
            new ArmorLayer { Name = "内", Slot = ArmorSlot.Skin, SharpDefense = 20, BluntDefense = 10 },
        };

        // 层1：钝防20 穿透0.2 → defMax=16；atk=10 vs def=15 → Half → carried=5、**不转换**、穿透仍 0.2
        // 层2：rolled=10 → min(10,5)=5；钝防10 穿透0.2 → defMax=8；def=2 → Full → 5
        var rng = new SequenceRandomSource(10, 15, 10, 2);
        var r = new CombatResolver(rng).Resolve(weapon, layers, Chest);

        Assert.False(r.Layers[0].ConvertedToBlunt);
        Assert.Equal(0.2, r.Layers[1].PenetrationUsed, 9);
        Assert.Equal(5, r.FinalDamage, 9);
    }

    /// <summary>真实人体的胸部（<see cref="ArmorTable"/> 的 CoversParts 按真实部位名过滤，用假部位会被全部滤掉）。</summary>
    private static BodyPart RealChest() => HumanBody.NewBody().Parts[HumanBody.Chest];

    private static double AvgDamage(Weapon weapon, IReadOnlyList<ArmorLayer> layers, int seed, int n = 40_000)
    {
        var resolver = new CombatResolver(new SystemRandomSource(seed));
        var ordered = CombatResolver.OrderOuterToInner(layers);
        BodyPart chest = RealChest();
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += resolver.Resolve(weapon, ordered, chest).FinalDamage;
        }
        return sum / n;
    }

    /// <summary>
    /// 🔴 缺陷的验收判据（统计）：**零防御的层不再把伤害砍半**。
    ///
    /// <para>旧口径：1 层 100%、2 层 **50.0%**、3 层 **25.0%**（精确，且与武器无关 —— 白送）。</para>
    /// <para>新口径：衰减纯粹来自「重掷 k 次取最小值」这个统计效应，**下限被武器伤害区间的下界兜住**：
    /// 长剑 3~15 ⇒ E[min of k] = 3 + 12/(k+1) ⇒ 1 层 9.0、2 层 7.0、3 层 **6.0（67%）**，
    /// **永远掉不到 DamageMin=3 以下**（旧口径三层已经砍到 2.25 ＜ 3，物理上讲不通）。</para>
    /// </summary>
    [Fact]
    public void ZeroDefenseLayers_NoLongerHalveDamage()
    {
        Weapon weapon = WeaponTable.Longsword();

        double naked = AvgDamage(weapon, System.Array.Empty<ArmorLayer>(), 4242);
        double two = AvgDamage(weapon, new[] { Nil(ArmorSlot.Outer), Nil(ArmorSlot.Skin) }, 4242);
        double three = AvgDamage(weapon, new[] { Nil(ArmorSlot.Plate), Nil(ArmorSlot.Outer), Nil(ArmorSlot.Skin) }, 4242);

        // 旧实现：two/naked = 0.500、three/naked = 0.250 ⇒ 两条都精确打红。
        Assert.True(two / naked > 0.70, $"两层零防御：裸身 {naked:F2} → {two:F2}（{two / naked:P1}）");
        Assert.True(three / naked > 0.60, $"三层零防御：裸身 {naked:F2} → {three:F2}（{three / naked:P1}）");

        // 衰减有下限：无论叠多少层零防御布片，都掉不到武器伤害下界以下。
        Assert.True(three > weapon.DamageMin, $"三层零防御 {three:F2} 不应低于武器伤害下界 {weapon.DamageMin}");
    }

    /// <summary>
    /// 🔴 「好甲必须强过烂甲」的验收判据：**一件板甲必须明显强于三件垃圾布**。
    /// 护甲数值均从 Wiki 配置读取。
    /// </summary>
    [Fact]
    public void OnePlate_BeatsThreeRags()
    {
        Weapon weapon = WeaponTable.Longsword();

        double plate = AvgDamage(weapon, new[] { ArmorTable.Plate() }, 777);
        double rags = AvgDamage(
            weapon,
            new[] { ArmorTable.ChestPlate(), ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() },
            777);

        Assert.True(plate < rags,
            $"板甲必须比三件垃圾布更抗揍：板甲 {plate:F2} vs 三件套 {rags:F2}");
    }

    /// <summary>
    /// 破甲锤 vs 两件破布：**"多穿一件破布就减半"这个白送已经消失**（50% → 约 90%）。
    ///
    /// <para>🔴 <b>但这里还剩一个【护甲表数值】问题，机制层修不了，已上抛用户</b>：
    /// 皮甲钝防 9 × (1 − 破甲锤穿透 0.15) = <b>7.65</b>，而破甲锤的**最低**伤害是 <b>10</b>
    /// ⇒ 攻 roll 恒 &gt; 防 roll ⇒ **结构性地永远判 Full、永远 0 减伤**。
    /// ⇒ 修复后皮甲对破甲锤仍是 14.01（裸身 14.01），两件破布 12.66 —— 破布**仍然**略强于皮甲。
    /// 这是**数值**（皮甲钝防太低，够不着破甲锤的伤害下界），不是**规则**。
    /// 护甲表由用户定夺，本单不擅自改数值。</para>
    /// </summary>
    [Fact]
    public void Warhammer_TwoRags_NoLongerHalveDamage()
    {
        Weapon hammer = WeaponTable.Warhammer();

        double naked = AvgDamage(hammer, System.Array.Empty<ArmorLayer>(), 31337);
        double rags = AvgDamage(hammer, new[] { ArmorTable.CoarseClothCoat(), ArmorTable.LongSleeveShirt() }, 31337);

        // 旧实现：rags/naked = 0.496 ⇒ 精确打红。新实现：≈0.90（纯 min-of-2 的统计衰减）。
        Assert.True(rags / naked > 0.80,
            $"两件破布不该再把破甲锤减半：裸身 {naked:F2} → 两件破布 {rags:F2}（{rags / naked:P1}）");
    }

    /// <summary>
    /// **零漂移自检**：方案 E 只影响**第 2 层起**的攻方 roll ⇒ 0 层与 1 层的结算路径
    /// 一个字节都不该变（第一层永远是武器原始区间的直掷、且没有"上一层"可取 min）。
    /// 这条若红，说明实现动错了地方。
    /// </summary>
    [Fact]
    public void ZeroAndOneLayer_PathsUnchanged()
    {
        var weapon = new Weapon { Name = "重剑", DamageMin = 10, DamageMax = 20, Penetration = 0, DamageType = DamageType.Sharp };

        var naked = new CombatResolver(new SequenceRandomSource(14))
            .Resolve(weapon, System.Array.Empty<ArmorLayer>(), Chest);
        Assert.Equal(14, naked.FinalDamage, 9);

        // 单层：攻 roll 14、防 roll 只能是 0（零防御 ⇒ defMax=0）⇒ Full ⇒ 14，与旧实现逐位相同。
        var one = new CombatResolver(new SequenceRandomSource(14, 0))
            .Resolve(weapon, new[] { Nil(ArmorSlot.Skin) }, Chest);
        Assert.Equal(14, one.Layers[0].AttackRoll, 9);
        Assert.Equal(14, one.FinalDamage, 9);
    }
}
