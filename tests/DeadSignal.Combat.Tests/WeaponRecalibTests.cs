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
    // ---- ① 近战锐器区间：剑类由用户在数值表上手改（T21 同步） ----

    /// <summary>
    /// 逐把锁死近战锐器的伤害区间与穿透。<b>四把剑是用户在数值表上手改的值</b>（T21 同步回代码）；
    /// 匕首与草叉用户没动，保持批次18 的下移值。
    ///
    /// ⚠ <b>批次18 的两条旧规则已被用户的新值推翻，不要再往回改</b>：
    /// ① 「近战锐器下限恒为 1」——**不再成立**。剑类下限现在是一条阶梯 1/2/2/3/5：
    ///    越大的剑越不可能"只是划破皮"，重剑最低也要划掉 5 点。
    /// ② 「区间下移＝宽度不变」——**不再成立**。剑类区间被<b>收窄</b>了（短剑宽 14→7、长剑 20→9），
    ///    从"高方差、掷 1 挠痒 / 掷满爆发"变成"稳定可靠"，方差换成了穿透（见下面的穿透阶梯）。
    /// </summary>
    [Theory]
    [InlineData("匕首", 1, 7, 0.09)]      // 用户未动（批次18 原案锚点）
    [InlineData("刺剑", 2, 8, 0.16)]      // 用户手改（原 1~12 / 0.15）
    [InlineData("短剑", 2, 9, 0.12)]      // 用户手改（原 1~15；穿透未动）
    [InlineData("长剑", 3, 12, 0.25)]     // 用户手改（原 1~21 / 0.18）
    [InlineData("草叉", 4, 8, 0.16)]      // 用户手改（原 1~18；穿透未动）——区间大幅收窄，同剑类取向
    [InlineData("重剑", 5, 20, 0.40)]     // 用户手改（原 1~27 / 0.24）
    public void MeleeSharpWeapon_MatchesUserTunedTable(string name, int expectedMin, int expectedMax, double expectedPen)
    {
        Weapon w = WeaponTable.Arsenal().Single(x => x.Name == name);
        Assert.Equal(DamageType.Sharp, w.DamageType);
        Assert.False(w.IsRanged);
        Assert.Equal(expectedMin, w.DamageMin);
        Assert.Equal(expectedMax, w.DamageMax);
        Assert.Equal(expectedPen, w.Penetration, 6);
    }

    /// <summary>
    /// 剑类下限阶梯（用户新值的立意）：匕首 ≤ 刺剑 ≤ 短剑 ＜ 长剑 ＜ 重剑。
    /// 「越大的剑，最差的一下也越狠」——护栏：不许有人把某把剑的下限又压回 1。
    /// </summary>
    [Fact]
    public void SwordFamily_DamageMin_FormsAscendingLadder()
    {
        Assert.True(WeaponTable.Dagger().DamageMin <= WeaponTable.Rapier().DamageMin);
        Assert.True(WeaponTable.Rapier().DamageMin <= WeaponTable.Shortsword().DamageMin);
        Assert.True(WeaponTable.Shortsword().DamageMin < WeaponTable.Longsword().DamageMin);
        Assert.True(WeaponTable.Longsword().DamageMin < WeaponTable.Greatsword().DamageMin);
    }

    /// <summary>
    /// 剑类穿透阶梯（用户新值的立意）：短剑 12% ＜ 刺剑 16% ＜ 长剑 25% ＜ 重剑 40%。
    /// 用户收窄伤害区间的同时把穿透拉开——剑的成长曲线从"伤害方差"改挂到"吃甲能力"上。
    /// </summary>
    [Fact]
    public void SwordFamily_Penetration_FormsAscendingLadder()
    {
        Assert.True(WeaponTable.Shortsword().Penetration < WeaponTable.Rapier().Penetration);
        Assert.True(WeaponTable.Rapier().Penetration < WeaponTable.Longsword().Penetration);
        Assert.True(WeaponTable.Longsword().Penetration < WeaponTable.Greatsword().Penetration);
    }

    // ---- ② 枪械：下限仍远高于 1，但区间已由用户重调（T21 同步） ----

    /// <summary>
    /// 逐把锁死枪械的伤害区间（<b>T21：用户在数值表上重调过一轮</b>）。
    ///
    /// ⚠️ 旧断言名 <c>Gun_KeepsOriginalDamageRange_NotShiftedDown</c> 编码了<b>两层</b>意图，用户只推翻了其中一层：
    /// ① 「枪不适用『下限压到 1』的下移公式——子弹没有"擦破皮"这一说」→ <b>仍然成立</b>：
    ///    新下限 手枪 8 / 冲锋枪 6 / 步枪 10 / 栓动 16 / 狙击 20，全都远高于 1。
    ///    该护栏由 <see cref="NoSingleSlugFirearm_HasDamageMinOfOne"/> 独立钉住，未受影响。
    /// ② 「枪保持<b>原始</b>区间不动」→ <b>已被用户推翻</b>：他主动削了枪（步枪 20~35 → 10~24、
    ///    狙击下限 40 → 20、冲锋枪下限 10 → 6），把枪从"贴脸即秒"拉回"仍强但没那么绝对"。
    ///    故本测试改为钉<b>用户的新区间</b>，不再宣称"保持原始"。
    /// </summary>
    [Theory]
    [InlineData("手枪", 8, 14)]        // 用户未动
    [InlineData("冲锋枪", 6, 18)]      // 用户手改下限（10 → 6）
    [InlineData("步枪", 10, 24)]       // 用户手改（20~35 → 10~24）
    [InlineData("栓动猎枪", 16, 28)]   // 用户未动伤害（只改了穿透）
    [InlineData("狙击枪", 20, 70)]     // 用户手改下限（40 → 20）：区间拉宽
    public void Gun_MatchesUserTunedDamageRange(string name, int expectedMin, int expectedMax)
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
    /// 下移的目的（回归锚）：低护甲值真的能挡下轻刃的低掷点。
    /// 门槛 = 护甲值×(1−穿透)/2；匕首穿透 9%、长袖布衣锐防 6 → 门槛 2.73 ＞ 下限 1，故存在挡下带。
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

    /// <summary>
    /// 用户新值划出的分界线（T21）：<b>布衣只挡得住轻刃，挡不住大剑</b>。
    ///
    /// 批次18 的旧意图是"全体近战锐器都存在被布衣挡下的低掷点带"（靠把下限统一压到 1 实现）。
    /// 用户把大剑的下限抬起来（长剑 3、重剑 5）、穿透拉上去（25%/40%）之后，
    /// 这两把的<b>最差一击也已经越过布衣门槛</b> ⇒ 旧意图对它们不再成立，且这正是新值想要的：
    /// 一件长袖布衫本来就不该指望能挡下一记重剑。
    ///
    /// 所以本测试钉的是<b>分界线本身</b>：轻刃（匕首/短剑/刺剑）保留挡下带，大剑（长剑/重剑）没有。
    /// </summary>
    [Fact]
    public void ClothArmor_BlocksLightBlades_ButNeverBigSwords()
    {
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();
        double Threshold(Weapon w) => shirt.SharpDefense * (1 - w.Penetration) / 2.0;

        // 轻刃：门槛高于下限 ⇒ 低掷点会被布衣完全吃掉
        foreach (Weapon w in new[] { WeaponTable.Dagger(), WeaponTable.Shortsword(), WeaponTable.Rapier() })
        {
            Assert.True(Threshold(w) > w.DamageMin,
                $"{w.Name} 是轻刃，布衣门槛 {Threshold(w):F2} 应高于其下限 {w.DamageMin}（保留挡下带）");
        }

        // 大剑：最差一击也越过门槛 ⇒ 布衣一下都挡不住
        foreach (Weapon w in new[] { WeaponTable.Longsword(), WeaponTable.Greatsword() })
        {
            Assert.True(Threshold(w) <= w.DamageMin,
                $"{w.Name} 是大剑，布衣门槛 {Threshold(w):F2} 不该拦得住它的下限 {w.DamageMin}");
        }
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
    /// 棍棒（道格开局武器）＝ <b>6~8</b>（T21 用户手改）。
    ///
    /// ⚠️ 旧断言名 <c>Club_BaseDamageRaised</c> 钉的是批次18 的「钝器提伤」（7~9 → 10~13）——
    /// <b>那个意图已被用户自己回调</b>：他把棍棒压到 6~8，比批次18 之前的 7~9 还低。
    /// 棍棒是道格的开局武器，压低它＝把开局难度拉回来。故本测试改钉新值，不再宣称"提伤"。
    /// （"钝器整体高于 1 点下限"的护栏仍在 <see cref="BluntWeapons_KeepHighDamageMin"/>。）
    /// </summary>
    [Fact]
    public void Club_MatchesUserTunedTable()
    {
        Weapon club = WeaponTable.Club();
        Assert.Equal(6, club.DamageMin);
        Assert.Equal(8, club.DamageMax);
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

    // ---- ④ 破甲锤：批次18 说"不动"，T21 用户自己动了下限 ----

    /// <summary>
    /// 破甲锤 <b>16~28</b>、穿透 35%（T21 用户手改下限 20 → 16）。
    ///
    /// ⚠️ 旧断言名 <c>Warhammer_Untouched</c> 钉的是批次18 的用户拍板「破甲锤不动」——
    /// <b>那条已被用户自己推翻</b>（他这轮把下限从 20 压到 16）。上限 28 与穿透 35% 仍未动，
    /// 「破甲锤＝全表最高钝器穿透」的定位不变，故只改数字、不改定位。
    /// </summary>
    [Fact]
    public void Warhammer_MatchesUserTunedTable()
    {
        Weapon wh = WeaponTable.Warhammer();
        Assert.Equal(16, wh.DamageMin);
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

    /// <summary>
    /// 步枪穿透 40%：军用步枪＝高穿透，专啃多层甲。冷却 2.8s 未动。
    /// <b>伤害区间 T21 由用户改为 10~24</b>（原 20~35）——穿透与冷却是这条测试的主张，伤害只是附带钉值。
    /// </summary>
    [Fact]
    public void Rifle_Penetration40Percent_CooldownUntouched()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.Equal(0.40, rifle.Penetration, 6);
        Assert.Equal(10, rifle.DamageMin);   // T21 用户手改（20 → 10）
        Assert.Equal(24, rifle.DamageMax);   // T21 用户手改（35 → 24）
        Assert.Equal(2.8, rifle.AttackInterval, 6);
    }

    /// <summary>
    /// 步枪二连发（用户拍板：「先直接做成二连发，数值以后再在表格里手调」）。
    /// 照冲锋枪三连发的既有机制走（BurstCount + BurstInterval）。
    ///
    /// 本测试的意图是<b>护栏</b>：不许<b>我们</b>为了平衡二连发而偷偷回调数值——用户明说"平衡以后自己在表里调"。
    /// 这个意图<b>依然成立</b>：伤害 20~35 → 10~24 正是<b>用户自己在表里调的</b>（T21），不是我们代偿。
    /// 冷却 2.8s 与穿透 40% 至今未被任何人动过——那才是这条护栏真正盯的东西。
    /// </summary>
    [Fact]
    public void Rifle_IsTwoRoundBurst_NoAgentSideBalanceCompensation()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.Equal(2, rifle.BurstCount);
        Assert.True(rifle.BurstInterval > 0, "二连发必须有连发内间隔（照冲锋枪机制）");

        // 冷却/穿透：自二连发落地起从未被动过（护栏——不许为平衡二连发偷偷拉冷却/削穿透）
        Assert.Equal(2.8, rifle.AttackInterval, 6);
        Assert.Equal(0.40, rifle.Penetration, 6);
        // 伤害：用户本人在数值表上调的值（非我方代偿）
        Assert.Equal(10, rifle.DamageMin);
        Assert.Equal(24, rifle.DamageMax);
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
