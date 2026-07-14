using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// <b>钝器的存在理由 = 砸建筑</b>（用户拍板：「钝器对建筑伤害应该要远高于锐器，这应当是钝器的一个优势」）。
///
/// <para>
/// 钝器对<b>人</b>是弱的（棍棒打丧尸胜率全表垫底），这是<b>有意的代价</b>；回报就在这里——**要破门就得带把锤子**。
/// 本文件把这条设计意图钉成硬护栏：以后谁把钝器的砸墙系数调回去、或者把锐器抬上来，测试当场红。
/// </para>
///
/// <para>
/// <b>为什么护栏要按「最弱钝器 vs 最强锐器」立</b>：既有 <see cref="StructureDamageTests"/> 比的是
/// 「破甲锤 vs 匕首」（&gt;5 倍），那是最强钝器打最弱锐器——它<b>掩盖了真正的洞</b>：改动前最弱的钝器（棍棒 2.44 点/秒）
/// 只比最强的锐器（重剑 1.63 点/秒）快 50%，拿重剑的玩家根本没理由换锤子。护栏必须卡在**最坏一对**上。
/// </para>
///
/// <para>
/// <b>「不许波及」也是护栏</b>：丧尸砸墙用爪击、劫掠者砸墙用匕首/枪托——调钝器时一旦顺手动了它们，
/// 尸潮与夜袭的破防难度就被无声改掉了。故本文件同时钉死这三条的砸墙系数（见 §不许波及）。
/// </para>
/// </summary>
public class BluntStructureAdvantageTests
{
    /// <summary>钝器要「远」高于锐器——远的下限：破墙效率至少 3 倍。</summary>
    private const double RequiredAdvantage = 3.0;

    private static IEnumerable<Weapon> MeleeArsenal()
        => WeaponTable.Arsenal().Where(w => !w.IsRanged);

    private static IEnumerable<Weapon> BluntMelee()
        => MeleeArsenal().Where(w => w.DamageType == DamageType.Blunt);

    private static IEnumerable<Weapon> SharpMelee()
        => MeleeArsenal().Where(w => w.DamageType == DamageType.Sharp);

    /// <summary>
    /// <b>消防斧是这条通则唯一的具名例外，这是设计，不是漏洞。</b>（[批次25·T44]）
    ///
    /// <para>
    /// 「锐器砸墙无用」的立论是「<b>刃口的杀伤全建立在切开血肉上，木头铁皮不吃这一套</b>」——
    /// 而<b>劈门恰恰是斧子的正经用途</b>：它不是一把用来切开血肉的刃，它是一把用来把东西劈成两半的工具。
    /// 一个"锐器 ⇒ 砸墙必定无用"的通则，代价是让消防斧这把武器在语义上说不通。
    /// </para>
    ///
    /// <para>
    /// 故三段梯度在消防斧这里变成<b>四段</b>，两侧都有硬护栏（见 <c>AxeTests</c>）：
    /// <c>其余锐器（≤0.98） ＜ 枪托（<b>1.72~1.89</b>） ＜ 【消防斧 3.35】 ＜ 钝器（3.67~12.44）</c>
    /// </para>
    ///
    /// <para>
    /// 📝 <b>枪托那一档已随「压低枪托」下移</b>（[T48]，2.08~2.84 → <b>1.72~1.89</b>）：六把枪的
    /// <c>StructureFactor</c> 全 = 1.0 ⇒ 枪托砸墙点/秒 <b>恒等于</b>其近战 DPS ⇒ 压低对人伤害必然连带压低砸墙。
    /// <b>四段梯度不破</b>——枪托只是在自己那一档里往下缩，上下两个邻档（锐器 0.98 / 消防斧 3.35）都没碰到。
    /// 方向也是对的：<b>枪托本来就不该是破拆工具</b>。（玩家感知：拿枪托砸金属门 78s → <b>120s</b>。）
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>例外只开给消防斧这一把，不许扩大</b>：钝器打<b>人</b>是弱的（棍棒打丧尸 47.8% 全表垫底），
    /// 破拆是它们<b>唯一</b>的回报。消防斧没有进钝器档（砸金属门 68s vs 破甲锤 18s，仍是量级差），
    /// 「要破金属门就得带把锤子」原样成立。再往这个集合里加第二把"能砸墙的锐器"之前，先想清楚钝器还剩什么。
    /// </para>
    /// </summary>
    private static IEnumerable<Weapon> SharpMeleeExceptTheAxe()
        => SharpMelee().Where(w => w.Name != WeaponTable.Axe().Name);

    // ---- 设计意图：钝器 >> 锐器 ----

    [Fact]
    public void 任意钝器的砸墙系数_都高于任意锐器()
    {
        // ⚠️ 这一条**故意把消防斧也算进来**（不走 SharpMeleeExceptTheAxe）：消防斧在**系数**这一层仍必须低于
        // 最弱的钝器（1.2 ＜ 棍棒 1.8）。它的例外只发生在**效率**（点/秒）那一层——它砍得更狠、也更慢，
        // 净效率越过了枪托档。系数这条底线一破，消防斧就真的变成一把"锐器外形的钝器"了。
        double weakestBlunt = BluntMelee().Min(StructureDamage.FactorFor);
        double strongestSharp = SharpMelee().Max(StructureDamage.FactorFor);

        Assert.True(weakestBlunt > strongestSharp,
            $"最弱钝器的砸墙系数 ×{weakestBlunt:0.0#} 必须高于最强锐器 ×{strongestSharp:0.0#}——" +
            "钝器砸建筑强是它唯一的不可替代场景，这条塌了钝器就没有存在理由了");
    }

    [Fact]
    public void 钝器的破墙效率_至少是锐器的三倍_按最坏一对算()
    {
        // 消防斧除外（见 SharpMeleeExceptTheAxe 的类注）：它是唯一一把设计上就该劈得开门的锐器，
        // 拿它当"锐器天花板"来卡这条通则，等于用一把工具去否定另一把工具的存在理由。
        // 消防斧自己的上下界由 AxeTests 两侧钉死，一格都没放松。
        Weapon worstBlunt = BluntMelee().OrderBy(StructureDamage.PerSecond).First();
        Weapon bestSharp = SharpMeleeExceptTheAxe().OrderByDescending(StructureDamage.PerSecond).First();

        double blunt = StructureDamage.PerSecond(worstBlunt);
        double sharp = StructureDamage.PerSecond(bestSharp);

        Assert.True(blunt >= sharp * RequiredAdvantage,
            $"最弱钝器「{worstBlunt.Name}」{blunt:F2} 点/秒 必须至少是最强锐器「{bestSharp.Name}」" +
            $"{sharp:F2} 点/秒 的 {RequiredAdvantage} 倍（实为 {blunt / sharp:F1} 倍）——" +
            "用户要的是「远」高于；只差几成玩家感觉不到，也就不会为破门去换武器");
    }

    [Fact]
    public void 破甲锤是全表最强破拆武器_连丧尸爪击也不例外()
    {
        Weapon hammer = WeaponTable.Warhammer();
        double best = StructureDamage.PerSecond(hammer);

        foreach (Weapon w in WeaponTable.Arsenal().Where(w => w.Name != hammer.Name))
        {
            Assert.True(best > StructureDamage.PerSecond(w),
                $"破甲锤 {best:F2} 点/秒 应强于 {w.Name} {StructureDamage.PerSecond(w):F2} 点/秒");
        }

        // 它的砸墙系数还要压过丧尸的「撕扯」——「铁皮罐头也照开不误」这句介绍得有数值兜底：
        // 破甲 + 破门 = 专业破拆武器，这是它的定位。
        Assert.True(StructureDamage.FactorFor(hammer) > StructureDamage.FactorFor(WeaponTable.ZombieClaw()),
            "破甲锤的砸墙系数应是全表最高——它是专业破拆工具，不该输给一只丧尸的爪子");
    }

    [Fact]
    public void 砸墙三段梯度_锤子强于枪托_枪托强于刀刃()
    {
        // 消防斧除外（见 SharpMeleeExceptTheAxe）：它是**第四段**，正卡在枪托与钝器之间——
        // 「抡枪托比抡斧子劈门快」和「抡斧子比抡锤子破门快」两条荒唐都由 AxeTests 挡住。
        double weakestBlunt = BluntMelee().Min(StructureDamage.PerSecond);
        double strongestSharp = SharpMeleeExceptTheAxe().Max(StructureDamage.PerSecond);
        // HasMeleeProfile 正好把「枪」与「弓弩」分开：弓没有枪托可抡。
        Weapon[] guns = WeaponTable.Arsenal().Where(w => w.HasMeleeProfile).ToArray();

        Assert.NotEmpty(guns);
        foreach (Weapon gun in guns)
        {
            double stock = StructureDamage.PerSecond(gun);

            // 枪托是钝的金属块，比刀刃管用……
            Assert.True(stock > strongestSharp,
                $"抡「{gun.Name}」的枪托 {stock:F2} 点/秒 应强于最强锐器 {strongestSharp:F2} 点/秒");

            // ……但它终究不是破拆工具：拿枪去砸门，光荣但笨拙。
            Assert.True(stock < weakestBlunt,
                $"抡「{gun.Name}」的枪托 {stock:F2} 点/秒 不该赶上最弱的钝器 {weakestBlunt:F2} 点/秒——" +
                "「抡枪托比抡棍子猛」这个 bug 已经复活过两次了");
        }
    }

    [Fact]
    public void 要破门就得带把锤子_金属门前锤与剑是量级差()
    {
        double hp = CampStructureTable.MaxHp(StructureTier.DoorMetal);
        double hammer = StructureDamage.SecondsToBreach(WeaponTable.Warhammer(), hp);
        double sword = StructureDamage.SecondsToBreach(WeaponTable.Greatsword(), hp);

        // 这条是玩家真正感知到的东西：不是"系数高一点"，而是"拿剑砸这扇门要砸到天亮"。
        Assert.True(sword > hammer * 5,
            $"砸穿金属门：破甲锤 {hammer:F0}s vs 重剑 {sword:F0}s——差距不到 5 倍，" +
            "玩家就不会为了破门专门带一把锤子，钝器的场景也就不存在");
    }

    // ---- 不许波及：调钝器不得改动尸潮/夜袭的破防难度 ----

    [Fact]
    public void 丧尸爪击的砸墙系数_不被钝器调整波及()
    {
        // 丧尸砸墙走爪击的「撕扯」系数（Zombie.cs:67 AttackWeapon = ZombieClaw）。
        // 它一旦被顺手改动，尸潮破围栏的时间就变了——那是另一条轴的平衡，不属于本单。
        Assert.Equal(2.5, StructureDamage.FactorFor(WeaponTable.ZombieClaw()), 6);
    }

    [Fact]
    public void 劫掠者的砸墙武器_不被钝器调整波及()
    {
        // 劫掠者只有两种手牌（Raider.cs:73/80）：手枪（砸墙 = 抡枪托）与匕首。
        // 两者都不是钝器 ⇒ 本单提高钝器系数，夜袭的破墙速度必须一格不动。
        Assert.Equal(0.4, StructureDamage.FactorFor(WeaponTable.Dagger()), 6);
        Assert.Equal(1.0, StructureDamage.FactorFor(WeaponTable.Pistol()), 6);
    }
}
