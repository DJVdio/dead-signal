using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 改装的装配约束：**从「武器大类」换成「逐把枪的白名单」**（用户拍板）。
///
/// <para><b>为什么要换</b>：按大类卡，用户没法表达"这个改装只能装步枪和霰弹枪"。
/// 白名单让约束的粒度落到**具体武器**上，他就能在 wiki 上逐把勾。</para>
///
/// <para><b>🔴 迁移的硬要求是「行为零变化」</b>：把每条改装原来的大类**展开成等价的武器名清单**。
/// 老存档里的改装枪靠 <c>ModdedWeaponRegistry</c> 用**当前规则**重算 —— 规则一旦收严，老组合就变非法。
/// 所以迁移这一步**一个组合都不许少**，收窄留给用户自己在 wiki 上做。</para>
///
/// <para><b>🔴 [T29] 收窄已发生</b>（用户拍板）：弓弩被划出 6 条枪械改装的白名单——这修的是
/// <c>ClassOf</c> 拿 <c>IsRanged</c> 当"枪械"的**真 bug**（短弓能装截短枪管），不是平衡调整。
/// 老档里因此变非法的组合**不会静默失效**：<c>ModdedWeaponRegistry.RebuildOrBase</c> 把它
/// **回落成基础武器**（弓还在，改装没了）。下面的迁移护栏已相应改钉新意图。</para>
///
/// <para><b>🔴 [T47] 白名单已成为「用户逐格勾的数据」</b>：用户在 wiki 上把每条改装的可装武器
/// 逐格勾过了，而且**各不相同**（截短枪管划掉冲锋枪、三型态划掉手枪+冲锋枪、锯齿/镂空划掉刺剑、
/// 防滑缠手锐器+钝器合并成一条）。⇒ **再拿 <c>AllOfClass</c> 去派生就会覆盖掉用户的手勾**。
/// 下面的"迁移零变化"两条已相应改钉**新意图**（不是删掉）。</para>
/// </summary>
/// <remarks>
/// ⚠️ <b>本类必须挂 <c>[Collection]</c></b>：它碰 <c>ModdedWeaponRegistry</c> 的 <c>Clear/Restore</c>
/// （进程级静态注册表）。此前漏挂 ⇒ 与 <c>GunModBenchTests</c>/<c>ModdedWeaponEquipTests</c> 并行时
/// 互相擦掉对方刚登记的枪，**随机红一条**（既有 flaky，[T47] 顺手修掉）。见 <see cref="ModdedWeaponRegistryCollection"/>。
/// </remarks>
[Collection(ModdedWeaponRegistryCollection.Name)]
public class WeaponModWhitelistTests
{
    // ── 白名单的基本语义 ──

    [Fact]
    public void 白名单里有这把枪_才装得上()
    {
        WeaponMod mod = WeaponModCatalog.LightenedStock();
        Assert.Contains(WeaponTable.Rifle().Name, mod.FitsWeapons);

        // 不在白名单里的（长剑）→ 装不上
        Assert.DoesNotContain(WeaponTable.Longsword().Name, mod.FitsWeapons);
        Assert.Throws<WeaponModException>(
            () => WeaponMods.ApplyMods(WeaponTable.Longsword(), new[] { mod }));
    }

    [Fact]
    public void 目录按武器名过滤_不再按大类()
    {
        // 步枪拿得到枪械改装
        var forRifle = WeaponModCatalog.For(WeaponTable.Rifle()).Select(m => m.Name).ToList();
        Assert.Contains("轻质化枪托", forRifle);

        // 长剑拿不到枪械改装，只拿得到刃类的
        var forSword = WeaponModCatalog.For(WeaponTable.Longsword()).Select(m => m.Name).ToList();
        Assert.DoesNotContain("轻质化枪托", forSword);
        Assert.NotEmpty(forSword);
    }

    // ── 🔴 [T29] 迁移期已过：用户主动收窄，弓弩不再吃枪械改装 ──
    //
    // 下面两条**原先钉的是"迁移零变化"**（新白名单 ≡ 旧大类，一个组合都不许少）。
    // 那个意图**已经完成它的使命并被用户推翻**：
    //   · 迁移零变化是**一次性的过渡要求**——目的是"换约束模型时别顺手改行为"，它当时成立、现已过期。
    //   · 用户随后明令：把 8 把弓弩从 6 条枪械改装里**全部划掉**。
    //   · 这修的是**真 bug**（ClassOf 拿 IsRanged 当"枪械" ⇒ 短弓能装截短枪管），不是平衡调整。
    // ⇒ 故两条改钉**新口径**（枪械改装只装真枪），而不是删掉：它们仍在守同一件事的另一面——
    //   白名单与引擎判定必须一致，任何人再把弓弩混进枪械改装都会当场变红。

    [Fact]
    public void 枪械改装_一把弓弩都装不上()
    {
        var archery = WeaponModCatalog.AllModdableWeapons()
            .Where(WeaponModCatalog.IsArchery).Select(w => w.Name).ToList();

        Assert.Equal(8, archery.Count);   // 5 弓 + 3 弩

        foreach (WeaponMod mod in WeaponModCatalog.All()
                     .Where(m => WeaponModCatalog.LegacyClassOf(m) == WeaponClass.Firearm))
        {
            Assert.NotEmpty(mod.FitsWeapons);
            foreach (string bow in archery)
            {
                Assert.DoesNotContain(bow, mod.FitsWeapons);
            }
        }
    }

    /// <summary>
    /// 🔴 <b>[T47/T68] 15 条改装的白名单 = 用户在 wiki 上逐格勾出来的那张表，逐条钉死</b>（[T68] 新增锋刃型）。
    ///
    /// <para>这条**取代了**原先"白名单 ≡ 旧大类"的两条迁移护栏 —— 那个意图已经完成使命并被用户推翻：
    /// 他把每条改装的可装武器**改成各不相同**了（截短枪管划掉冲锋枪、三型态划掉手枪+冲锋枪、
    /// 锯齿/镂空划掉刺剑、防滑缠手锐器+钝器合并成一条），再拿大类去派生就会**覆盖掉他的手勾**。</para>
    ///
    /// <para>把整张表写死在这里，是为了让"谁又拿 <c>AllOfClass</c> 一把梭回去"当场变红。
    /// 用户在 wiki 上改了勾选，来同步的人改这张表即可 —— <b>它就是那张表</b>。</para>
    /// </summary>
    [Fact]
    public void 十五条改装的白名单_逐条等于用户在wiki上勾的那张表()
    {
        string[] guns6 = { "自制猎枪", "手枪", "冲锋枪", "步枪", "狙击枪", "自制霰弹枪" };
        string[] gunsSawn = { "自制猎枪", "手枪", "步枪", "狙击枪", "自制霰弹枪" };          // 用户划掉冲锋枪
        string[] gunsForm = { "自制猎枪", "步枪", "狙击枪", "自制霰弹枪" };                  // 用户划掉手枪+冲锋枪
        string[] gunsBladeForm = { "手枪", "冲锋枪" };                                       // [T68] 锋刃型＝短枪专属（与 gunsForm 不相交）
        // 🔴 消防斧已按用户拍板勾进锐器改装（「和长剑同档」）—— **唯独镂空剑刃不勾**
        //    （镂空会挖掉消防斧赖以成立的头部质量，见 消防斧按与长剑同档的口径拿到五条锐器改装_唯独镂空剑刃不勾）
        string[] blades6WithAxe = { "匕首", "短剑", "刺剑", "长剑", "草叉", "重剑", "消防斧" };
        string[] serratedFits = { "匕首", "短剑", "长剑", "草叉", "重剑", "消防斧" };            // 划掉刺剑，含消防斧
        string[] fullerFits = { "匕首", "短剑", "长剑", "草叉", "重剑" };                      // 划掉刺剑，**不含消防斧**
        string[] bladesAndBlunts = { "匕首", "短剑", "刺剑", "长剑", "草叉", "重剑", "消防斧", "棍棒", "尖头锤", "破甲锤" };
        string[] clubOnly = { "棍棒" };

        var expected = new Dictionary<string, string[]>
        {
            ["lightened_stock"] = guns6,
            ["sawn_off_barrel"] = gunsSawn,
            ["extended_barrel"] = guns6,
            ["bayonet"] = gunsForm,
            ["claw_stock"] = gunsForm,
            ["trauma_stock"] = gunsForm,
            ["blade_stock"] = gunsBladeForm,   // [T68] 锋刃型（手枪/冲锋枪）
            ["serrated_blade"] = serratedFits,
            ["honed_edge"] = blades6WithAxe,
            ["fuller_blade"] = fullerFits,
            ["weighted_handle"] = blades6WithAxe,
            ["lightened_handle"] = blades6WithAxe,
            ["grip_wrap_blade"] = bladesAndBlunts,   // 用户把原来同名的锐器/钝器两条合并成了一条
            ["wire_wrap"] = clubOnly,
            ["nail_studs"] = clubOnly,
        };

        IReadOnlyList<WeaponMod> actual = WeaponModCatalog.All();
        Assert.Equal(15, actual.Count);   // [T68] 新增锋刃型（14 → 15）

        foreach (WeaponMod mod in actual)
        {
            Assert.True(expected.ContainsKey(mod.Id), $"目录里多了一条 wiki 表上没有的改装：{mod.Id}");
            Assert.Equal(expected[mod.Id].OrderBy(s => s), mod.FitsWeapons.OrderBy(s => s));
        }
        Assert.Equal(expected.Keys.OrderBy(s => s), actual.Select(m => m.Id).OrderBy(s => s));
    }

    /// <summary>
    /// 🔴 <b>用户拍板的互斥：「钉子强化是棍棒独有的，而棍棒不能锋刃研磨」。</b>
    /// <para>两边都要钉 —— 只测一边（比如只测"钉子强化只装棍棒"）挡不住真正的失败姿态：
    /// <b>棍棒既能钉钉子、又能开刃</b>。那要两条断言合起来才拦得下。</para>
    /// </summary>
    [Fact]
    public void 钉子强化是棍棒独有_而棍棒不能锋刃研磨()
    {
        Weapon club = WeaponTable.Club();

        // ① 钉子强化（与铁丝强化）= 棍棒独有：全表**只有**棍棒装得上，别的武器装了就抛
        foreach (WeaponMod shaft in new[] { WeaponModCatalog.NailStuds(), WeaponModCatalog.WireWrap() })
        {
            Assert.Equal(new[] { "棍棒" }, shaft.FitsWeapons.ToArray());

            foreach (Weapon w in WeaponModCatalog.AllModdableWeapons().Where(w => w.Name != "棍棒"))
            {
                Assert.DoesNotContain(w.Name, shaft.FitsWeapons);
                Assert.Throws<WeaponModException>(() => WeaponMods.ApplyMods(w, new[] { shaft }));
            }
        }

        // ② 棍棒**不能**锋刃研磨（钝器没有"刃"可开）—— 装了就抛
        WeaponMod honed = WeaponModCatalog.HonedEdge();
        Assert.DoesNotContain("棍棒", honed.FitsWeapons);
        Assert.Throws<WeaponModException>(() => WeaponMods.ApplyMods(club, new[] { honed }));

        // ③ 棍棒能拿到的改装**恰好**是三条：铁丝强化 / 钉子强化 / 防滑缠手 —— 一条不多、一条不少
        Assert.Equal(
            new[] { "钉子强化", "铁丝强化", "防滑缠手" }.OrderBy(s => s),
            WeaponModCatalog.For(club).Select(m => m.Name).OrderBy(s => s));
    }

    /// <summary>
    /// ✅ <b>[用户拍板] 消防斧按「和长剑同档」的口径勾进锐器改装 —— 6 条里拿到 5 条。</b>
    ///
    /// <para>依据（口径）：消防斧原先 DPS <b>2.79</b> ≈ 长剑 <b>2.81</b>（同档）。用户原话是要「**和长剑同档的口径**」，
    /// 不是"一个字不差照抄" ⇒ 逐条过了语义，**只跳掉「镂空剑刃」一条**。
    /// （⚠ [weapon-finalize] 消防斧已升到 6.5~14＝DPS 3.01，略高于长剑——但**改装白名单是用户拍板的口径归属**，
    /// 不随 DPS 微调重算；这条测试钉的是"哪些改装装得上"，与具体 DPS 值无关，故不受升伤影响。）</para>
    ///
    /// <para>🔴 <b>为什么单单跳镂空剑刃</b>（这是本条测试真正要钉住的判断）：
    /// 理由**不是**"消防斧没有'剑刃'" —— 加重剑柄/轻质化剑柄/锯齿剑刃也都叫"剑X"，按名字否决会把 4 条一起误杀。
    /// 真正的理由是**功能上自相矛盾**：<b>消防斧的杀伤力就是它的头部质量</b>（头重杆轻，靠惯性劈开东西）。
    /// 镂空 = 开血槽减重 −25%、攻速 +15%、伤害 −9% ⇒ 把消防斧赖以成立的那个东西挖掉，
    /// 换来一把"更快但更轻更软的消防斧" —— 那不是消防斧，那是一把很差的剑。</para>
    ///
    /// <para>（本条**取代了**先前那条"消防斧拿不到任何改装_待用户在 wiki 上勾选" —— 用户已经勾了。改钉新意图，不是删。）</para>
    /// </summary>
    [Fact]
    public void 消防斧按与长剑同档的口径拿到五条锐器改装_唯独镂空剑刃不勾()
    {
        Weapon axe = WeaponTable.Axe();
        Weapon longsword = WeaponTable.Longsword();

        var axeMods = WeaponModCatalog.For(axe).Select(m => m.Id).OrderBy(s => s).ToList();
        var swordMods = WeaponModCatalog.For(longsword).Select(m => m.Id).OrderBy(s => s).ToList();

        // 长剑吃满 6 条锐器改装（作为"同档"的参照系；它要是变了，这条也该重新想）
        Assert.Equal(
            new[] { "fuller_blade", "grip_wrap_blade", "honed_edge", "lightened_handle", "serrated_blade", "weighted_handle" },
            swordMods);

        // 消防斧 = 长剑的那 6 条**减去镂空剑刃**
        Assert.Equal(swordMods.Where(id => id != "fuller_blade"), axeMods);
        Assert.Equal(5, axeMods.Count);

        // 逐条正向：这 5 条是真的装得上（不是白名单写了却合成失败）
        foreach (WeaponMod m in WeaponModCatalog.For(axe))
        {
            WeaponMods.ApplyMods(axe, new[] { m });   // 抛异常即红
        }

        // 🔴 镂空剑刃**装不上消防斧**（白名单没勾 ⇒ 合成当场拒绝）
        Assert.DoesNotContain("消防斧", WeaponModCatalog.FullerBlade().FitsWeapons);
        Assert.Throws<WeaponModException>(
            () => WeaponMods.ApplyMods(axe, new[] { WeaponModCatalog.FullerBlade() }));

        // 消防斧当然也拿不到枪械/钝器改装（它是锐器）
        Assert.DoesNotContain("消防斧", WeaponModCatalog.NailStuds().FitsWeapons);
        Assert.DoesNotContain("消防斧", WeaponModCatalog.Bayonet().FitsWeapons);
    }

    [Fact]
    public void 弓弩装不上枪械改装_装了就抛()
    {
        // 曾经的 bug：ClassOf 把弓弩算作 Firearm ⇒ 截短枪管真能装到短弓上。用户已明令划掉。
        WeaponMod barrel = WeaponModCatalog.SawnOffBarrel();

        Assert.DoesNotContain(WeaponTable.ShortBow().Name, barrel.FitsWeapons);
        Assert.DoesNotContain(WeaponTable.HeavyCrossbow().Name, barrel.FitsWeapons);

        Assert.Throws<WeaponModException>(
            () => WeaponMods.ApplyMods(WeaponTable.ShortBow(), new[] { barrel }));
        Assert.Throws<WeaponModException>(
            () => WeaponMods.ApplyMods(WeaponTable.CompoundCrossbow(), new[] { WeaponModCatalog.Bayonet() }));
    }

    /// <summary>
    /// 弓弩现在<b>一条改装都拿不到</b>（枪械改装被划掉、刃类/钝类本就不含它们）——
    /// 这是收窄的直接后果，写成断言是为了让它**可见**：若用户日后想给弓弩单开一类改装
    /// （弓弦/箭台/瞄具…），这条会红，提醒来更新口径。
    /// </summary>
    [Fact]
    public void 弓弩当前拿不到任何改装()
    {
        foreach (Weapon bow in WeaponModCatalog.AllModdableWeapons().Where(WeaponModCatalog.IsArchery))
        {
            Assert.Empty(WeaponModCatalog.For(bow));
        }
    }

    // ── 存档兼容 ──

    [Fact]
    public void 存档_白名单收窄后_老改装枪还原不出来_但不崩()
    {
        // 这是"收窄"的代价，必须心里有数：Rebuild 拿当前规则重算，组合非法就返回 null。
        // 引擎本来就为这种情况留了退路（不抛异常、不崩）。
        WeaponMod mod = WeaponModCatalog.LightenedStock();
        Weapon rifle = WeaponTable.Rifle();

        // 正常：能还原
        var spec = new ModdedWeaponSpec("测试变体", rifle.Name, new[] { mod.Name });
        Assert.NotNull(ModdedWeaponRegistry.Rebuild(spec));

        // 基础武器根本不存在 ⇒ 还原不出来，但返回 null 而不是抛
        var bad = new ModdedWeaponSpec("坏变体", "不存在的枪", new[] { mod.Name });
        Assert.Null(ModdedWeaponRegistry.Rebuild(bad));
    }

    /// <summary>
    /// 🔴 <b>[T29 收窄的存档降级姿态 —— 用户拍板：回落，不是丢弃]</b>
    ///
    /// <para>老存档里若真存在「短弓（截短枪管）」，收窄后这条组合已非法：
    /// <list type="bullet">
    /// <item><see cref="ModdedWeaponRegistry.Rebuild"/>（严格版，合法性判据）⇒ 仍返回 <c>null</c>。</item>
    /// <item><see cref="ModdedWeaponRegistry.RebuildOrBase"/>（载入路径）⇒ <b>回落成基础短弓</b>：
    ///       <b>弓还在，只是改装没了</b>，不崩、不返回 null、玩家不会平白少一把武器。</item>
    /// </list>
    /// 这不是"版本升级作废旧档"，是**数据收窄**——让一把弓凭空消失是糟糕的体验，而回落只要一行。</para>
    /// </summary>
    [Fact]
    public void 存档_老档里装了枪管的短弓_载入后回落成基础弓_不崩不消失()
    {
        var 老档里的弓 = new ModdedWeaponSpec(
            "短弓（截短枪管）", WeaponTable.ShortBow().Name, new[] { WeaponModCatalog.SawnOffBarrel().Name });

        // 严格版：这条组合当前非法 ⇒ null（它是"合不合法"的判据，语义不变）
        Assert.Null(ModdedWeaponRegistry.Rebuild(老档里的弓));

        // 载入路径：回落成基础短弓 —— 武器还在，改装没了
        Weapon? fallen = ModdedWeaponRegistry.RebuildOrBase(老档里的弓);
        Assert.NotNull(fallen);
        Assert.Equal(WeaponTable.ShortBow().Name, fallen!.Name);
        Assert.Equal(WeaponTable.ShortBow().MaxRange, fallen.MaxRange);          // 枪管带来的射程加成没了
        Assert.Equal(WeaponTable.ShortBow().AttackInterval, fallen.AttackInterval, 6);
    }

    /// <summary>读档全链路：非法变体经 <c>Restore</c> 之后，按变体名仍查得到武器（回落后的基础弓），而不是查不到。</summary>
    [Fact]
    public void 存档_Restore后按变体名仍查得到弓_只是拿到的是基础弓()
    {
        try
        {
            ModdedWeaponRegistry.Restore(new[]
            {
                new ModdedWeaponSpec("短弓（截短枪管）", WeaponTable.ShortBow().Name,
                    new[] { WeaponModCatalog.SawnOffBarrel().Name }),
            });

            Weapon? w = ModdedWeaponRegistry.WeaponByName("短弓（截短枪管）");
            Assert.NotNull(w);                                        // 没有凭空消失
            Assert.Equal(WeaponTable.ShortBow().Name, w!.Name);       // 拿到的是基础短弓

            // 负重表照旧按基础武器名索引（回落不该让它变成"未登记武器"）
            Assert.Equal(WeaponTable.ShortBow().Name, ModdedWeaponRegistry.BaseNameOf("短弓（截短枪管）"));
        }
        finally
        {
            ModdedWeaponRegistry.Clear();   // 静态注册表：不清会漏进别的测试
        }
    }

    /// <summary>
    /// 回落有底线：<b>基础武器本身</b>都不存在了（如被删除的栓动猎枪）⇒ 无处可落 ⇒ 仍返回 null。
    /// 这两种失败是不同的事，别混为一谈。
    /// </summary>
    [Fact]
    public void 存档_基础武器都被删了_无处可落_仍返回null()
    {
        var 没了的枪 = new ModdedWeaponSpec(
            "栓动猎枪（刺刀型）", "栓动猎枪", new[] { WeaponModCatalog.Bayonet().Name });

        Assert.Null(ModdedWeaponRegistry.RebuildOrBase(没了的枪));
    }

    // ── 数据完整性 ──

    [Fact]
    public void 全表改装_白名单都不为空()
    {
        foreach (WeaponMod mod in WeaponModCatalog.All())
        {
            Assert.True(mod.FitsWeapons.Count > 0,
                $"改装「{mod.Name}」的白名单是空的 —— 那它哪把武器都装不上，等于废件");
        }
    }

    [Fact]
    public void 全表改装_白名单里的名字_都是真实存在的武器()
    {
        var known = WeaponModCatalog.AllModdableWeapons().Select(w => w.Name).ToHashSet();
        foreach (WeaponMod mod in WeaponModCatalog.All())
        {
            foreach (string name in mod.FitsWeapons)
            {
                Assert.True(known.Contains(name),
                    $"改装「{mod.Name}」的白名单里有「{name}」，但武器表里没有这把武器（写错了名字？）");
            }
        }
    }
}
