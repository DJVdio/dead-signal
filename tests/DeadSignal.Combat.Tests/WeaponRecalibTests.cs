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
    [InlineData("匕首", 1, 7, 0.06)]      // T29 用户手改穿透（0.09 → 0.06）
    [InlineData("刺剑", 2, 7, 0.25)]      // T29 用户手改（上限 8 → 7；穿透 0.16 → 0.25）
    [InlineData("短剑", 3, 8, 0.12)]      // T29 用户手改（2~9 → 3~8；穿透未动）
    [InlineData("长剑", 3, 15, 0.24)]     // T29 用户手改（上限 12 → 15；穿透 0.25 → 0.24）
    [InlineData("草叉", 4, 9, 0.20)]      // T29 用户手改（上限 8 → 9；穿透 0.16 → 0.20）
    [InlineData("重剑", 6, 20, 0.40)]     // T29 用户手改下限（5 → 6）；穿透未动，仍是全近战最高
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
    /// 剑类下限阶梯：匕首 1 ≤ 刺剑 2 ≤ 短剑 3 ≤ 长剑 3 ＜ 重剑 6。
    /// 「越大的剑，最差的一下也越狠」——护栏：不许有人把某把剑的下限又压回 1。
    ///
    /// ⚠ T29：短剑→长剑之间的<b>严格</b>递增已被用户的新值抹平（他把短剑下限提到 3，与长剑持平）。
    /// 阶梯的立意（大剑不该"轻轻划一下"）没变，变的是它现在是<b>非严格</b>递增。
    /// 故断言由 &lt; 放宽到 ≤，只在两端保留严格性：匕首必须最低、重剑必须严格最高。
    /// </summary>
    [Fact]
    public void SwordFamily_DamageMin_FormsAscendingLadder()
    {
        Assert.True(WeaponTable.Dagger().DamageMin <= WeaponTable.Rapier().DamageMin);
        Assert.True(WeaponTable.Rapier().DamageMin <= WeaponTable.Shortsword().DamageMin);
        Assert.True(WeaponTable.Shortsword().DamageMin <= WeaponTable.Longsword().DamageMin);
        Assert.True(WeaponTable.Longsword().DamageMin < WeaponTable.Greatsword().DamageMin);
        Assert.True(WeaponTable.Dagger().DamageMin < WeaponTable.Greatsword().DamageMin);
    }

    /// <summary>
    /// 剑类穿透阶梯：短剑 12% ＜ 长剑 24% ＜ 刺剑 25% ＜ 重剑 40%。
    /// 用户收窄伤害区间的同时把穿透拉开——剑的成长曲线从"伤害方差"改挂到"吃甲能力"上。
    ///
    /// ⚠ T29 <b>阶梯的次序被用户改了</b>：旧序是「刺剑 16% ＜ 长剑 25%」，即"剑越大越吃甲"。
    /// 他这轮把刺剑提到 25%、长剑压到 24% ⇒ <b>刺剑反超长剑</b>，成为仅次于重剑的第二吃甲近战。
    /// 这不是倒挂，是生态位改写且说得通：刺剑是<b>突刺</b>武器（找甲缝钻），长剑是劈砍——
    /// "尖的比宽的更吃甲"物理上本就成立。故本测试改钉<b>新的次序</b>，而不是把长剑改回去。
    /// 立意仍在：穿透是一条有序阶梯，重剑封顶。
    /// </summary>
    [Fact]
    public void SwordFamily_Penetration_FormsAscendingLadder()
    {
        Assert.True(WeaponTable.Shortsword().Penetration < WeaponTable.Longsword().Penetration);
        Assert.True(WeaponTable.Longsword().Penetration < WeaponTable.Rapier().Penetration);
        Assert.True(WeaponTable.Rapier().Penetration < WeaponTable.Greatsword().Penetration);
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
    [InlineData("手枪", 4, 14)]        // T29 用户手改下限（8 → 4）
    [InlineData("冲锋枪", 6, 18)]      // 用户未动
    [InlineData("步枪", 10, 24)]       // 用户未动
    [InlineData("狙击枪", 20, 70)]     // 用户未动：区间已是拉宽后的
    // 「栓动猎枪」原在此列（16~28），T29 用户把整把武器从数值表删除 ⇒ 一并撤下。
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
        { "自制猎枪", "手枪", "冲锋枪", "步枪", "狙击枪" };   // 栓动猎枪已被用户删除

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
    /// 所以本测试钉的是<b>分界线本身</b>：有挡下带的（匕首/刺剑）vs 没有的（短剑/长剑/重剑）。
    ///
    /// ⚠ T29 <b>分界线往下挪了一格</b>：用户把短剑下限提到 3（原 2），而布衣对短剑的门槛只有
    /// 6×(1−0.12)/2 = 2.64 ⇒ <b>短剑的最差一击也已越过布衣门槛</b>，它不再有挡下带，站到了大剑那一侧。
    /// 现在还留着挡下带的只剩匕首（门槛 2.82 ＞ 下限 1）与刺剑（2.25 ＞ 2）。
    /// 立意未变（"一件布衫挡得住小刀，挡不住剑"），变的是短剑被用户划到了"剑"那边。
    /// </summary>
    [Fact]
    public void ClothArmor_BlocksLightBlades_ButNeverBigSwords()
    {
        ArmorLayer shirt = ArmorTable.LongSleeveShirt();
        double Threshold(Weapon w) => shirt.SharpDefense * (1 - w.Penetration) / 2.0;

        // 有挡下带：门槛高于下限 ⇒ 低掷点会被布衣完全吃掉
        foreach (Weapon w in new[] { WeaponTable.Dagger(), WeaponTable.Rapier() })
        {
            Assert.True(Threshold(w) > w.DamageMin,
                $"{w.Name} 是轻刃，布衣门槛 {Threshold(w):F2} 应高于其下限 {w.DamageMin}（保留挡下带）");
        }

        // 无挡下带：最差一击也越过门槛 ⇒ 布衣一下都挡不住
        foreach (Weapon w in new[] { WeaponTable.Shortsword(), WeaponTable.Longsword(), WeaponTable.Greatsword() })
        {
            Assert.True(Threshold(w) <= w.DamageMin,
                $"{w.Name} 的下限 {w.DamageMin} 已越过布衣门槛 {Threshold(w):F2}，布衣不该拦得住它");
        }
    }

    // ---- ② 丧尸爪击：1~3 + 冷却 1.4s + 穿透 0.03（用户在 wiki 表上定稿）----

    /// <summary>
    /// 爪击 <b>1~3 / 1.4s / 穿透 0.03</b>（wiki 表定稿，表赢代码；历程：1~5·2.3s → 1~3·1.2s → 今值）。
    /// <para>
    /// 🔴 <b>旧口径已被用户推翻</b>：本测试原先钉的是 1.2s，并写着「DPS 1.30 → 1.67（+28%）⇒ 丧尸整体是<b>变强</b>了」。
    /// 用户随后明确反向拍板：「丧尸虽然伤害低了，但是丧尸本就是<b>以量取胜</b>，三打一的战力比是九比一，
    /// 所以<b>单一丧尸不该很强</b>。」⇒ 冷却回到 <b>1.4s</b>、穿透压到 <b>0.03</b>，DPS <b>1.67 → 1.43</b>，
    /// 对穿皮夹克的目标更是只剩 <b>0.74</b>。这是<b>有意的削弱</b>，不是回归——别再把它调回去。
    /// </para>
    /// <para>丧尸的强度不在单只身上：兰彻斯特平方律已在引擎里实证（<c>docs/research/2026-07-14-lanchester.md</c>）——
    /// 2 只围攻就把胜率从 82.6% 打到 16.6%，3 只 0.8%。</para>
    /// </summary>
    [Fact]
    public void ZombieClaw_IsOneToThree_Slower_AndBarelyPenetrates()
    {
        Weapon claw = WeaponTable.ZombieClaw();
        Assert.Equal(1, claw.DamageMin);
        Assert.Equal(3, claw.DamageMax);
        Assert.Equal(1.4, claw.AttackInterval, 6);
        Assert.Equal(0.03, claw.Penetration, 6);
        Assert.Equal(DamageType.Sharp, claw.DamageType);
    }

    // ---- ③ 钝器：提基础伤害（破甲锤除外） ----

    /// <summary>
    /// 棍棒（道格开局武器）＝ <b>4~7</b> / 穿透 <b>0</b> / 冷却 <b>2.7s</b> / <b>可双持</b>（T29 用户手改，原 6~8 / 0.03 / 2.4s / 单持）。
    ///
    /// ⚠️ 批次18 的「钝器提伤」已被用户<b>连续两轮回调</b>：10~13 → 6~8 → 4~7，如今比批次18 之前的 7~9 还低一档。
    /// 穿透归零后它与拳脚同列（全表仅这两个 0 穿透）。每秒伤害 2.04 ⇒ <b>全表倒数第二</b>，仅高于拳脚 1.67。
    /// 棍棒是道格的开局武器，压低它＝把开局难度拉回来——这是用户明知的取向，别当平衡问题"修"回去。
    /// （"钝器整体高于 1 点下限"的护栏仍在 <see cref="BluntWeapons_KeepHighDamageMin"/>。）
    /// </summary>
    [Fact]
    public void Club_MatchesUserTunedTable()
    {
        Weapon club = WeaponTable.Club();
        Assert.Equal(4, club.DamageMin);
        Assert.Equal(7, club.DamageMax);
        Assert.Equal(0, club.Penetration, 6);
        Assert.Equal(2.7, club.AttackInterval, 6);
        Assert.True(club.CanDualWield);
        Assert.Equal(DamageType.Blunt, club.DamageType);
    }

    /// <summary>
    /// 尖头锤＝ <b>6~14</b> / 冷却 <b>3.5s</b> / <b>强制双手</b>（T29 用户手改，原 15~20 / 3.7s / 单手）。
    ///
    /// ⚠️ 旧断言名 <c>SpikeHammer_BaseDamageRaised</c> 钉的是批次18 的「钝器提伤」（12~16 → 15~20）——
    /// <b>那个意图已被用户推翻</b>：他把它压到 6~14（平均 17.5 → 10，−43%），比批次18 之前的 12~16 还低。
    /// 同轮它被改成双手武器 ⇒ 定位从"单手重锤"变成"要两只手才抡得动的家伙"。故改钉新值，不再宣称"提伤"。
    /// </summary>
    [Fact]
    public void SpikeHammer_MatchesUserTunedTable()
    {
        Weapon hammer = WeaponTable.SpikeHammer();
        Assert.Equal(6, hammer.DamageMin);
        Assert.Equal(14, hammer.DamageMax);
        Assert.Equal(3.5, hammer.AttackInterval, 6);
        Assert.True(hammer.TwoHanded);
        Assert.Equal(DamageType.Blunt, hammer.DamageType);
    }

    // ---- ④ 破甲锤：批次18 说"不动"，T21 用户自己动了下限 ----

    /// <summary>
    /// 破甲锤 <b>10~18</b>、穿透 <b>15%</b>（T29 用户手改：原 16~28 / 35%）。
    ///
    /// ⚠️ <b>「破甲锤＝破甲专精」这个定位本身已被用户撤销</b>，不只是数字变了：
    /// 穿透 35% → 15% 之后，它在近战里<b>低于</b>重剑 40% / 刺剑 25% / 长剑 24% / 草叉 20%——
    /// 一把叫"破甲锤"的武器，如今是全表倒数第二不吃甲的近战（只比棍棒 0% 强）。
    /// 它剩下的立身之本是<b>钝伤本身</b>（震荡/骨折用初始武器 roll、不吃护甲）与<b>全表最强砸墙 2.0</b>。
    /// 名字与破门能力仍对得上"铁皮罐头也照开不误"，<b>穿甲杀伤则不再</b>。
    /// 这是用户在数值表上的明确取向，别当平衡问题"修"回去；若要它名副其实，需用户重新拍板穿透。
    /// </summary>
    [Fact]
    public void Warhammer_MatchesUserTunedTable()
    {
        Weapon wh = WeaponTable.Warhammer();
        Assert.Equal(10, wh.DamageMin);
        Assert.Equal(18, wh.DamageMax);
        Assert.Equal(0.15, wh.Penetration, 6);

        // 破甲专精已易主：重剑才是全近战最高穿透。
        Assert.True(wh.Penetration < WeaponTable.Greatsword().Penetration);
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

    // ---- ③ 步枪穿透：21% → 40%（批次18b）→ **70%**（T29 用户手改） ----

    /// <summary>
    /// 步枪穿透 <b>70%</b>、冷却 <b>3.0s</b>（T29 用户手改：原 40% / 2.8s）——军用步枪＝高穿透，专啃多层甲。
    /// 70% 正是狙击枪的旧值（狙击同轮被抬到 95%）⇒ 步枪的吃甲能力现在与"旧狙击枪"同档，
    /// 代价是冷却又慢了 0.2s。伤害区间 10~24 未动。
    /// </summary>
    [Fact]
    public void Rifle_Penetration70Percent_SlowerCooldown()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.Equal(0.70, rifle.Penetration, 6);
        Assert.Equal(10, rifle.DamageMin);
        Assert.Equal(24, rifle.DamageMax);
        Assert.Equal(3.0, rifle.AttackInterval, 6);
    }

    /// <summary>
    /// 步枪二连发（用户拍板：「先直接做成二连发，数值以后再在表格里手调」）。
    /// 照冲锋枪三连发的既有机制走（BurstCount + BurstInterval）。
    ///
    /// 本测试的意图是<b>护栏</b>：不许<b>我们</b>为了平衡二连发而偷偷回调数值——用户明说"平衡以后自己在表里调"。
    /// 这个意图<b>依然成立</b>，且这一轮尤其能说明问题：伤害 10~24 / 冷却 3.0s / 穿透 70% 三个数
    /// <b>全是用户自己在表里调的</b>（T21 削伤害、T29 抬穿透+拉冷却），没有一处是我方代偿。
    /// 本测试钉的是"这些数与数值表逐格一致"，而不是"它们不许变"。
    /// </summary>
    [Fact]
    public void Rifle_IsTwoRoundBurst_NoAgentSideBalanceCompensation()
    {
        Weapon rifle = WeaponTable.Rifle();
        Assert.Equal(2, rifle.BurstCount);
        Assert.True(rifle.BurstInterval > 0, "二连发必须有连发内间隔（照冲锋枪机制）");

        // 以下四个数一律以数值表为准（用户手改），代码只负责与表逐格对上。
        Assert.Equal(3.0, rifle.AttackInterval, 6);
        Assert.Equal(0.70, rifle.Penetration, 6);
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
        Assert.Equal(1, WeaponTable.SniperRifle().BurstCount);   // 栓动猎枪原也在此列，已被用户删除
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

    /// <summary>
    /// 自制猎枪：远程锐器、<b>双手</b>长枪、伤害 <b>6~20</b>、穿透 <b>40%</b>、带枪托钝击（T29 用户手改：原 4~16 / 25%）。
    /// <para>⚠ 它的旧定位注脚「穿透 25% 低于步枪 40%，故不是穿甲专精」已再次被推翻：
    /// 用户这轮把它抬到 40%、同时把步枪抬到 70% ⇒ 它仍低于步枪，结论不变，只是数字全变了。
    /// 它的身份始终是<b>唯一能自己造的枪</b>（不靠掉落），不是穿甲专精。</para>
    /// </summary>
    [Fact]
    public void ImprovisedHuntingGun_HasUserDecidedSpec()
    {
        Weapon g = WeaponTable.ImprovisedHuntingGun();
        Assert.Equal("自制猎枪", g.Name);
        Assert.Contains(WeaponTable.Arsenal(), w => w.Name == "自制猎枪");

        Assert.Equal(6, g.DamageMin);            // T29 用户手改（4 → 6）
        Assert.Equal(20, g.DamageMax);           // T29 用户手改（16 → 20）
        Assert.Equal(0.40, g.Penetration, 6);    // T29 用户手改（0.25 → 0.40）
        Assert.True(g.Penetration < WeaponTable.Rifle().Penetration, "仍低于军用步枪——它不是穿甲专精");
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
