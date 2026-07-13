using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;   // RecipeBook / Materials / CraftOutputFactory（配方与产物落地在消费层）
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次18 武器数值重标定（用户拍板）：
/// ① <b>近战</b>锐器区间整体下移——下限压到 1，上限 = 原上限 −(原下限 −1)（宽度不变、平均下降）。
///    设计取向：让布甲/腐皮这类低护甲值真的能挡下低掷点（门槛 = 护甲值×(1−穿透)/2），代价是 PvE 变慢变磨。
/// ② <b>枪械不降下限</b>（用户拍板，[DECISION] 已关闭）：刀可以"轻划一下"（1 点伤害说得通），
///    <b>子弹没有"擦破皮"这一说——打中就是打中</b>。故 6 把枪保留原高下限，也保住枪对近战的压制力。
/// ③ 丧尸爪击 3~9 → 1~5（用户手定值，非公式推导：不是平移，是砍半平均）。
/// ④ 钝器提基础伤害（吃不到"降下限"红利：钝防门槛低于其下限），幅度在新环境下用 Sim 重校准。
/// ⑤ 破甲锤不动。
/// </summary>
public class WeaponRecalibTests
{
    // ---- ① 近战锐器：下限压到 1 ----

    /// <summary>近战锐器（非远程）的伤害下限都必须是 1。这是规则，不是个案。</summary>
    [Fact]
    public void AllMeleeSharpWeapons_HaveDamageMinOfOne()
    {
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.DamageType == DamageType.Sharp && !w.IsRanged))
        {
            Assert.Equal(1, w.DamageMin);
        }
    }

    /// <summary>区间整体下移＝宽度不变。逐把锁死近战锐器的新上限（= 原上限 − 原下限 + 1）。</summary>
    [Theory]
    [InlineData("匕首", 7)]        // 原 4~10（用户原案锚点）
    [InlineData("短剑", 15)]       // 原 6~20
    [InlineData("刺剑", 12)]       // 原 7~18
    [InlineData("长剑", 21)]       // 原 10~30
    [InlineData("草叉", 18)]       // 原 9~26
    [InlineData("重剑", 27)]       // 原 14~40
    public void MeleeSharpWeapon_IntervalShiftedDown_WidthPreserved(string name, int expectedMax)
    {
        Weapon w = WeaponTable.Arsenal().Single(x => x.Name == name);
        Assert.Equal(DamageType.Sharp, w.DamageType);
        Assert.False(w.IsRanged);
        Assert.Equal(1, w.DamageMin);
        Assert.Equal(expectedMax, w.DamageMax);
    }

    // ---- ② 枪械：不降下限，保持原区间（用户拍板回滚） ----

    /// <summary>
    /// 6 把枪保持<b>原始</b>伤害区间——下移公式不适用于枪械。
    /// 「子弹没有擦破皮这一说：打中就是打中」，故枪不能有 1 点伤害的低掷点。
    /// </summary>
    [Theory]
    [InlineData("手枪", 8, 14)]
    [InlineData("冲锋枪", 10, 18)]
    [InlineData("步枪", 20, 35)]
    [InlineData("栓动猎枪", 16, 28)]
    [InlineData("狙击枪", 40, 70)]
    public void Gun_KeepsOriginalDamageRange_NotShiftedDown(string name, int expectedMin, int expectedMax)
    {
        Weapon w = WeaponTable.Arsenal().Single(x => x.Name == name);
        Assert.True(w.IsRanged);
        Assert.Equal(expectedMin, w.DamageMin);
        Assert.Equal(expectedMax, w.DamageMax);
    }

    /// <summary>
    /// 「单发实心弹」枪械——用户"枪不降下限"规则的适用范围。
    /// ⚠ 不是所有 <c>IsRanged</c> 都算：<b>自制霰弹枪</b>按<b>弹丸</b>计（单颗 1~5，一次打 8 颗），
    /// <b>自制弓</b>射的是箭矢——一颗霰弹丸/一支箭「擦到」是说得通的，只有<b>子弹</b>没有"擦破皮"。
    /// 故这两把不适用本规则（它们分属 impl-shotgun / impl-bow，数值由其各自拍板）。
    /// </summary>
    private static readonly string[] SingleSlugFirearms =
        { "自制猎枪", "手枪", "冲锋枪", "步枪", "栓动猎枪", "狙击枪" };

    /// <summary>回归护栏：单发实心弹枪械的下限都不许被压到 1（防止有人把近战下移公式误推到枪械上）。</summary>
    [Fact]
    public void NoSingleSlugFirearm_HasDamageMinOfOne()
    {
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => SingleSlugFirearms.Contains(w.Name)))
        {
            Assert.True(w.DamageMin > 1, $"{w.Name} 是单发实心弹枪械，下限不该被压到 1（子弹没有\"擦破皮\"）");
        }
    }

    /// <summary>下移不改变锐器之间的强弱序（宽度不变 → 平均值序不变）。</summary>
    [Fact]
    public void SharpWeapons_KeepRelativeOrdering()
    {
        double Avg(Weapon w) => (w.DamageMin + w.DamageMax) / 2.0;

        Assert.True(Avg(WeaponTable.Dagger()) < Avg(WeaponTable.Rapier()));
        Assert.True(Avg(WeaponTable.Rapier()) < Avg(WeaponTable.Shortsword()));
        Assert.True(Avg(WeaponTable.Shortsword()) < Avg(WeaponTable.Longsword()));
        Assert.True(Avg(WeaponTable.Longsword()) < Avg(WeaponTable.Greatsword()));
        Assert.True(Avg(WeaponTable.Pistol()) < Avg(WeaponTable.Rifle()));
        Assert.True(Avg(WeaponTable.Rifle()) < Avg(WeaponTable.SniperRifle()));
    }

    /// <summary>
    /// 下移的目的（回归锚）：低护甲值现在真的能挡下低掷点。
    /// 门槛 = 护甲值×(1−穿透)/2；匕首穿透 9%、长袖布衣锐防 6 → 门槛 2.73 ＞ 下限 1，故存在挡下带。
    /// 现值下限 4 时门槛恒低于任何掷点 → 挡下率 0%。
    /// </summary>
    [Fact]
    public void LoweredMin_MakesClothArmorAbleToBlockLowRolls()
    {
        Weapon dagger = WeaponTable.Dagger();
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();
        double threshold = shirt.SharpDefense * (1 - dagger.Penetration) / 2.0;

        Assert.True(threshold > dagger.DamageMin,
            $"布衣门槛 {threshold:F2} 必须高于匕首下限 {dagger.DamageMin}，否则永远挡不下任何一击");
    }

    // ---- ② 丧尸爪击：1~5（用户手定） ----

    /// <summary>爪击 3~9 → 1~5：平均 6 → 3（砍半）。非公式平移（那会得出 1~7），是用户手定值。</summary>
    [Fact]
    public void ZombieClaw_IsOneToFive()
    {
        Weapon claw = WeaponTable.ZombieClaw();
        Assert.Equal(1, claw.DamageMin);
        Assert.Equal(5, claw.DamageMax);
        Assert.Equal(DamageType.Sharp, claw.DamageType);
    }

    // ---- ③ 钝器：提基础伤害（破甲锤除外） ----

    /// <summary>
    /// 棍棒（道格开局武器）提伤：7~9 → 10~13。
    /// 旧环境下 weapon-calib 推荐 +30~50%；新环境（锐器砍 43%、爪击砍半）下重校准取 +44% 平均。
    /// </summary>
    [Fact]
    public void Club_BaseDamageRaised()
    {
        Weapon club = WeaponTable.Club();
        Assert.Equal(10, club.DamageMin);
        Assert.Equal(13, club.DamageMax);
        Assert.Equal(DamageType.Blunt, club.DamageType);
    }

    /// <summary>尖头锤提伤：12~16 → 15~20（+25% 平均）。</summary>
    [Fact]
    public void SpikeHammer_BaseDamageRaised()
    {
        Weapon hammer = WeaponTable.SpikeHammer();
        Assert.Equal(15, hammer.DamageMin);
        Assert.Equal(20, hammer.DamageMax);
        Assert.Equal(DamageType.Blunt, hammer.DamageType);
    }

    // ---- ④ 破甲锤不动 ----

    /// <summary>破甲锤维持 20~28（用户拍板：不动）。</summary>
    [Fact]
    public void Warhammer_Untouched()
    {
        Weapon wh = WeaponTable.Warhammer();
        Assert.Equal(20, wh.DamageMin);
        Assert.Equal(28, wh.DamageMax);
        Assert.Equal(0.35, wh.Penetration, 6);
    }

    /// <summary>
    /// 钝器不参与"下限压到 1"（它们吃不到布甲红利，且降下限会让钝器变挠痒）。
    /// 回归护栏：钝器下限必须仍显著高于 1，否则说明有人把公式误用到了钝器上。
    /// </summary>
    [Fact]
    public void BluntWeapons_KeepHighDamageMin()
    {
        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.DamageType == DamageType.Blunt))
        {
            Assert.True(w.DamageMin > 1, $"{w.Name} 的下限不该被压到 1（钝器不适用下移规则）");
        }
    }

    /// <summary>钝器强弱序：棍棒 ＜ 尖头锤 ＜ 破甲锤（提伤后仍成立，不许棍棒反超）。</summary>
    [Fact]
    public void BluntWeapons_KeepRelativeOrdering()
    {
        double Avg(Weapon w) => (w.DamageMin + w.DamageMax) / 2.0;

        Assert.True(Avg(WeaponTable.Club()) < Avg(WeaponTable.SpikeHammer()));
        Assert.True(Avg(WeaponTable.SpikeHammer()) < Avg(WeaponTable.Warhammer()));
    }

    // ---- ③ 步枪穿透 21% → 40%（用户拍板；伤害/冷却不动） ----

    /// <summary>步枪穿透提到 40%：军用步枪＝高穿透，专啃多层甲。伤害区间 20~35 与冷却 2.8s 不动。</summary>
    [Fact]
    public void Rifle_PenetrationRaisedTo40Percent_DamageAndCooldownUntouched()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.Equal(0.40, rifle.Penetration, 6);
        Assert.Equal(20, rifle.DamageMin);
        Assert.Equal(35, rifle.DamageMax);
        Assert.Equal(2.8, rifle.AttackInterval, 6);
    }

    /// <summary>
    /// 步枪二连发（用户拍板：「先直接做成二连发，数值以后再在表格里手调」）。
    /// 照冲锋枪三连发的既有机制走（BurstCount + BurstInterval），<b>数值一概不动</b>：
    /// 伤害 20~35、冷却 2.8s、穿透 40% 全部保持——用户明说平衡以后自己在表里调，不许为二连发擅自回调。
    /// </summary>
    [Fact]
    public void Rifle_IsTwoRoundBurst_WithNoBalanceCompensation()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.Equal(2, rifle.BurstCount);
        Assert.True(rifle.BurstInterval > 0, "二连发必须有连发内间隔（照冲锋枪机制）");

        // 数值不动（护栏：防止有人为了平衡偷偷拉冷却/削伤害）
        Assert.Equal(20, rifle.DamageMin);
        Assert.Equal(35, rifle.DamageMax);
        Assert.Equal(2.8, rifle.AttackInterval, 6);
        Assert.Equal(0.40, rifle.Penetration, 6);
    }

    /// <summary>连发是军用枪的特征：冲锋枪 3 发、步枪 2 发；民用/自制枪一律单发。</summary>
    [Fact]
    public void OnlyMilitaryGuns_HaveBurst()
    {
        Assert.Equal(3, WeaponTable.Smg().BurstCount);
        Assert.Equal(2, WeaponTable.Rifle().BurstCount);

        Assert.Equal(1, WeaponTable.Pistol().BurstCount);
        Assert.Equal(1, WeaponTable.ImprovisedHuntingGun().BurstCount);
        Assert.Equal(1, WeaponTable.BoltActionHuntingRifle().BurstCount);
        Assert.Equal(1, WeaponTable.SniperRifle().BurstCount);
    }

    /// <summary>
    /// 穿透序（<b>仅限枪械</b>）：步枪 40% 在枪里仅次于狙击 70%，已高于破甲锤 35%；自制猎枪 25% 低于步枪。
    /// ⚠ 全表第二不再成立——<b>自制弓穿透 45%</b>（impl-bow 落地）已超步枪，故本断言只在枪械内部比较。
    /// </summary>
    [Fact]
    public void Rifle_IsSecondHighestPenetration_AmongFirearms()
    {
        var gunsByPen = WeaponTable.Arsenal()
            .Where(w => SingleSlugFirearms.Contains(w.Name))
            .OrderByDescending(w => w.Penetration)
            .ToList();
        Assert.Equal("狙击枪", gunsByPen[0].Name);
        Assert.Equal("步枪", gunsByPen[1].Name);
        Assert.True(WeaponTable.ImprovisedHuntingGun().Penetration < WeaponTable.Rifle().Penetration);
    }
}

/// <summary>
/// 批次18b：删除「土制枪」、新增可制作的「自制猎枪」（用户拍板：土制枪删了，自制猎枪是新的一把）。
/// </summary>
public class ImprovisedHuntingGunTests
{
    /// <summary>「土制枪」已从武器全集删除——不许再出现（含掉落/制作/UI，它们全部派生自 Arsenal）。</summary>
    [Fact]
    public void Zipgun_IsDeleted_NotInArsenal()
    {
        Assert.DoesNotContain(WeaponTable.Arsenal(), w => w.Name == "土制枪");
    }

    /// <summary>自制猎枪：远程锐器、<b>双手</b>长枪、伤害 4~16、穿透 25%、带枪托钝击。</summary>
    [Fact]
    public void ImprovisedHuntingGun_HasUserDecidedSpec()
    {
        Weapon g = WeaponTable.ImprovisedHuntingGun();
        Assert.Equal("自制猎枪", g.Name);
        Assert.Contains(WeaponTable.Arsenal(), w => w.Name == "自制猎枪");

        Assert.Equal(4, g.DamageMin);            // 用户拍板
        Assert.Equal(16, g.DamageMax);           // 用户拍板
        Assert.Equal(0.25, g.Penetration, 6);    // 用户拍板
        Assert.Equal(DamageType.Sharp, g.DamageType);
        Assert.True(g.IsRanged);
        Assert.True(g.TwoHanded);                // 长枪＝双手（我方拍板）
        Assert.False(g.CanDualWield);
        Assert.True(g.HasMeleeProfile);          // 枪托可贴脸钝击
        Assert.Equal(DamageType.Blunt, g.MeleeProfile()!.DamageType);
    }

    /// <summary>枪械通则：下限不为 1（子弹没有"擦破皮"）。自制猎枪下限 4。</summary>
    [Fact]
    public void ImprovisedHuntingGun_DamageMinIsNotOne()
    {
        Assert.True(WeaponTable.ImprovisedHuntingGun().DamageMin > 1);
    }

    /// <summary>自制猎枪必须<b>可制作</b>（它是"自制"武器，不能只靠掉落）：配方存在、产物名与 WeaponTable 对得上。</summary>
    [Fact]
    public void ImprovisedHuntingGun_IsCraftable_AndRecipeOutputMatchesWeaponName()
    {
        RecipeData r = RecipeBook.All.Single(x => x.Id == "improvised_hunting_gun");

        // 产物显示名必须与 WeaponTable 的武器名逐字一致，否则造出来的物品装备不上（消费层按名查 Arsenal）。
        Assert.Equal(WeaponTable.ImprovisedHuntingGun().Name, r.DisplayName);
        Assert.Equal(1, r.OutputQuantity);

        // 材料全部取自现有 Materials 目录（不许新造材料）。
        Assert.NotEmpty(r.MaterialCosts);
        foreach (string key in r.MaterialCosts.Keys)
        {
            Assert.True(Materials.Has(key), $"配方用了不存在的材料 {key}（不许新造材料）");
        }
    }

    /// <summary>配方产物落地时被判为「武器」类物品（否则会掉进杂项分支，玩家拿到一个装备不了的东西）。</summary>
    [Fact]
    public void ImprovisedHuntingGun_CraftOutput_IsWeaponItem()
    {
        var items = CraftOutputFactory.Create("improvised_hunting_gun", 1).ToList();
        Assert.Single(items);
        Assert.Equal("自制猎枪", items[0].RefKey);
    }
}
