using System.Collections.Generic;
using System.Linq;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次21·T7：**枪托近战数值 + 三种近战型态（利爪/创伤/刺刀）+ 改装台设施**。
///
/// <para>
/// 本组测试钉死的是 T7 的三条真规则，以及**改装落地前那三个 P0**（见 journal 里程碑1修订）：
/// <list type="number">
/// <item><b>枪托是"绝望手段"，不是好武器</b>：任何一把枪的枪托 DPS 都必须 ＞ 拳脚、＜ 一把真近战武器。
///       此前步枪枪托 5.33 伤/秒 ＞ 棍棒 4.79 —— 抡枪托比抡棍棒还猛，本测试就是来钉死这条不许再发生。</item>
/// <item><b>三种型态三选一</b>，且刺刀/利爪打出来的**必须真的是锐击**（这正是 P0-C：型态此前一入库就丢）。</item>
/// <item><b>改装 = 改装台 + 材料 + 工时</b>，缺一不可（此前是"点一下白送、随处可做"）。</item>
/// </list>
/// </para>
/// </summary>
[Collection(ModdedWeaponRegistryCollection.Name)]
public class GunModBenchTests
{
    // 全表 8 把枪（弓弩没有枪托，见 WeaponTable：弓的 StockMelee* 恒 null；栓动猎枪已被用户删除）。
    private static IReadOnlyList<Weapon> Firearms() =>
        WeaponTable.Arsenal().Where(w => w.IsRanged && w.HasMeleeProfile).ToList();

    /// <summary>DPS 一律走引擎的 <see cref="WeaponDps"/>（单一事实源），不在测试里另写一遍公式。</summary>
    private static double Dps(Weapon w) => WeaponDps.Single(w);

    // ==================== 1. 枪托近战数值定稿 ====================

    /// <summary>六把枪都得有枪托 profile（不然打空了就只能站着挨打）。弓弩不在此列——它们没有枪托。
    /// （原为七把，栓动猎枪已被用户从数值表删除。）</summary>
    [Fact]
    public void AllFirearms_HaveStockMeleeProfile()
    {
        Assert.Equal(8, Firearms().Count);
        Assert.All(Firearms(), w => Assert.NotNull(w.MeleeProfile()));
    }

    /// <summary>
    /// ✅ <b>[T47] 「记录失衡」的时代结束了 —— 这条已经翻回【正向硬护栏】。</b>
    ///
    /// <para><b>背景</b>：从前这里钉的是"枪托 ＞ 棍棒（记录现状）"，并留话「若此断言变红，说明枪托已被调低，
    /// 请把正向硬护栏加回来」。<b>用户已经把六把枪的枪托全部压下去了</b>（`WeaponTable` 里逐把标着
    /// 「用户拍板压低枪托」），⇒ 现在照约定翻回来。</para>
    ///
    /// <para><b>规则（T7 定稿的原话）</b>：任何一把枪的枪托 DPS 都必须 <b>＜ 棍棒</b>（全表最弱的近战武器）——
    /// 否则"我该不该带把近战武器"根本不成其为一个选择：带把枪就够了，它自带一根比棍棒更好的棍子。</para>
    ///
    /// <para>当前：<c>拳脚 1.67 ＜ 枪托 1.72~1.89 ＜ 棍棒 2.04 ＜ 匕首 2.35</c>。
    /// <b>护栏卡的是最坏一对</b>：最强的枪托（狙击 1.89）对最弱的近战（棍棒 2.04）—— 中间没有洞。</para>
    /// </summary>
    [Fact]
    public void StockMelee_IsWeakerThan_TheWeakestRealMeleeWeapon()
    {
        double club = Dps(WeaponTable.Club());       // 全表最弱近战武器

        foreach (Weapon gun in Firearms())
        {
            double stock = Dps(gun.MeleeProfile()!);
            Assert.True(stock < club,
                $"{gun.Name} 枪托 DPS {stock:F2} 不得 ≥ 棍棒 {club:F2} —— " +
                "抡枪托比抡棍子还猛的话，「要不要带把近战武器」就不成其为一个选择了");
        }
    }

    /// <summary>
    /// ✅ <b>[T47] 同上，翻回正向硬护栏。</b>「比拳头强，但不如一把真正的刀」是 T7 定稿的原话锚点。
    /// <para>护栏卡**最坏一对**：最强的枪托（狙击 1.89）vs 匕首 2.35。</para>
    /// </summary>
    [Fact]
    public void StockMelee_NeverBeats_TheDagger()
    {
        double dagger = Dps(WeaponTable.Dagger());

        foreach (Weapon gun in Firearms())
        {
            double stock = Dps(gun.MeleeProfile()!);
            Assert.True(stock < dagger,
                $"{gun.Name} 枪托 DPS {stock:F2} 不得 ≥ 匕首 {dagger:F2} —— 枪托比拳头强，但不如一把真正的刀");
        }
    }

    /// <summary>**枪托的地板**：枪托 DPS 必须高于拳脚——枪再不济也是一根铁棍，总比空手强。</summary>
    [Fact]
    public void StockMelee_IsBetterThan_BareFists()
    {
        double fists = Dps(WeaponTable.Fists());
        foreach (Weapon gun in Firearms())
        {
            double stock = Dps(gun.MeleeProfile()!);
            Assert.True(stock > fists,
                $"{gun.Name} 的枪托 DPS {stock:F2} 应高于拳脚 {fists:F2}——拿着枪不该比空手还弱");
        }
    }

    /// <summary>枪托恒为**钝击**（默认型态），且穿透极低——没装刺刀的枪砸不穿甲。</summary>
    [Fact]
    public void StockMelee_IsBluntAndBarelyPenetrates_ByDefault()
    {
        foreach (Weapon gun in Firearms())
        {
            Weapon stock = gun.MeleeProfile()!;
            Assert.Equal(DamageType.Blunt, stock.DamageType);
            Assert.True(stock.Penetration <= 0.05, $"{gun.Name} 枪托穿透 {stock.Penetration} 过高");
        }
    }

    /// <summary>
    /// 重量在「单击伤害 ↔ 冷却」之间搬运：**越重的枪，枪托单击越痛、抡得越慢**。
    /// 取两端对照：狙击枪（最重最长）单击 ＞ 手枪（最轻）；且狙击枪抡得更慢。
    /// </summary>
    [Fact]
    public void HeavierGun_HitsHarderButSwingsSlower()
    {
        Weapon pistol = WeaponTable.Pistol().MeleeProfile()!;
        Weapon sniper = WeaponTable.SniperRifle().MeleeProfile()!;

        Assert.True(sniper.DamageMax > pistol.DamageMax, "狙击枪托单击应重于手枪柄");
        Assert.True(sniper.AttackInterval > pistol.AttackInterval, "狙击枪托应抡得更慢");
        Assert.True(sniper.NoiseRadius > pistol.NoiseRadius, "狙击枪托砸下去应更响");
    }

    /// <summary>枪托噪音**不继承枪本体的枪声**（砸不是打），但也不是哑剧：落在近战量级。</summary>
    [Fact]
    public void StockMeleeNoise_IsMeleeTier_NotGunshot()
    {
        foreach (Weapon gun in Firearms())
        {
            Weapon stock = gun.MeleeProfile()!;
            Assert.True(stock.NoiseRadius < gun.NoiseRadius / 2,
                $"{gun.Name} 枪托噪音 {stock.NoiseRadius} 不该接近枪声 {gun.NoiseRadius}");
            Assert.True(stock.NoiseRadius > 0, "抡枪托砸人不是哑剧");
        }
    }

    // ==================== 2. 三种近战型态 ====================

    private static Weapon Rifle() => WeaponTable.Rifle();

    private static ModdedWeapon Mod(Weapon baseWeapon, params WeaponMod[] mods)
        => WeaponMods.ApplyMods(baseWeapon, mods);

    /// <summary>刺刀型 / 利爪型 → 枪托打出来的是**锐击**（P0-C：此前这个型态一入库就丢了）。</summary>
    [Theory]
    [InlineData("刺刀型")]
    [InlineData("利爪型")]
    public void BayonetAndClaw_MakeTheStockStrike_Sharp(string modName)
    {
        WeaponMod mod = WeaponModCatalog.For(WeaponClass.Firearm).First(m => m.Name == modName);
        ModdedWeapon modded = Mod(Rifle(), mod);

        Assert.Equal(DamageType.Sharp, modded.Weapon.MeleeProfile()!.DamageType);
    }

    /// <summary>创伤型 → 仍是**钝击**，只是更重（用户语义：钝伤加重）。</summary>
    [Fact]
    public void Trauma_StaysBlunt_ButHitsHarder()
    {
        WeaponMod trauma = WeaponModCatalog.For(WeaponClass.Firearm).First(m => m.Name == "创伤型");
        Weapon plain = Rifle().MeleeProfile()!;
        Weapon modded = Mod(Rifle(), trauma).Weapon.MeleeProfile()!;

        Assert.Equal(DamageType.Blunt, modded.DamageType);
        Assert.True(modded.DamageMax > plain.DamageMax, "创伤型应把钝击打得更重");
        Assert.True(modded.AttackInterval > plain.AttackInterval, "创伤型应更慢");
    }

    /// <summary>刺刀型 = **全型态最高穿透**（用户语义：刺击穿透）。</summary>
    [Fact]
    public void Bayonet_HasHighestPenetration_OfAllForms()
    {
        var forms = WeaponModCatalog.For(WeaponClass.Firearm).Where(m => m.Form is not null).ToList();
        Assert.Equal(4, forms.Count);   // [T68] 利爪 / 创伤 / 刺刀（重枪）+ 锋刃（短枪），不多不少

        double PenOf(string name) =>
            Mod(Rifle(), forms.First(m => m.Name == name)).Weapon.MeleeProfile()!.Penetration;

        double bayonet = PenOf("刺刀型");
        Assert.True(bayonet > PenOf("利爪型"), "刺刀（突刺）穿透应高于利爪（切割）");
        Assert.True(bayonet > PenOf("创伤型"), "刺刀穿透应高于创伤（钝器）");
    }

    /// <summary>利爪型 = 三型态里**单击伤害最高**（切割挥砍）。</summary>
    [Fact]
    public void Claw_HasHighestPerHitDamage_AmongSharpForms()
    {
        double ClawMax = Mod(Rifle(), Catalog("利爪型")).Weapon.MeleeProfile()!.DamageMax;
        double BayonetMax = Mod(Rifle(), Catalog("刺刀型")).Weapon.MeleeProfile()!.DamageMax;
        Assert.True(ClawMax > BayonetMax, "利爪（挥砍）单击应重于刺刀（突刺）");
    }

    private static WeaponMod Catalog(string name)
        => WeaponModCatalog.For(WeaponClass.Firearm).First(m => m.Name == name);

    /// <summary>
    /// 🔴 <b>用户拍板：「一把枪械只能进行一种近战改装」—— 三种型态两两互斥，三选一。</b>
    ///
    /// <para><b>这不只是平衡，是【语义必要条件】</b>：三种型态各自 <c>Set</c> 掉这把枪的整个近战 profile
    /// （刺刀 = 80% 攻速的刺剑 / 利爪 = 消防斧 / 创伤 = 尖头锤）。若能同时装，同一把枪的近战模式就有
    /// <b>三个互相覆盖的定义</b>，最终数值取决于**遍历顺序** —— 那是个隐藏的顺序依赖 bug，不是"叠了个 buff"。</para>
    ///
    /// <para>🔴 <b>护栏必须卡最坏一对。</b> 部位安排让这条规则有**两道**闸：
    /// <list type="bullet">
    /// <item><b>利爪 + 创伤</b> 同占「枪托」⇒ **部位冲突**先拦下 —— 这一对<b>就算把型态规则整个删掉也照样报错</b>，
    ///       拿它当护栏是**假绿**（旧测试正是只测了这一对 + 刺刀利爪）。</item>
    /// <item><b>刺刀（枪口）+ 利爪/创伤（枪托）</b> 部位**不冲突** ⇒ **只有型态规则挡得住**。
    ///       其中 <b>「刺刀 + 创伤」此前一条测试都没有</b> —— 正是"中间塌了测不出来"的那种洞。</item>
    /// </list>
    /// 故这里**三对全测、且两种施加顺序都测**（顺序依赖正是要防的东西），外加"三个一起装"。</para>
    /// </summary>
    [Fact]
    public void OnlyOneMeleeForm_PerWeapon_AllPairs_BothOrders()
    {
        string[] forms = { "刺刀型", "利爪型", "创伤型" };

        // 三对 × 两种顺序 —— 一对都不许漏（含只靠型态规则挡住的「刺刀+创伤」）
        foreach (string a in forms)
        {
            foreach (string b in forms.Where(x => x != a))
            {
                WeaponModException ex = Assert.Throws<WeaponModException>(
                    () => Mod(Rifle(), Catalog(a), Catalog(b)));

                // 报错必须说人话（这条会原样显示给玩家，见 CraftingPanel 的 Blocks）
                Assert.True(ex.Message.Contains("近战型态") || ex.Message.Contains("部位"),
                    $"{a}+{b} 的拒绝理由玩家看不懂：{ex.Message}");
            }
        }

        // 三个一起装：无论如何都得拒绝
        Assert.Throws<WeaponModException>(
            () => Mod(Rifle(), Catalog("刺刀型"), Catalog("利爪型"), Catalog("创伤型")));

        // 而**单独一个**型态照常能装（别把互斥写成"一个都不许装"）
        foreach (string only in forms)
        {
            Assert.NotNull(Mod(Rifle(), Catalog(only)).Weapon.MeleeProfile());
            Assert.Equal(1, Mod(Rifle(), Catalog(only)).AppliedMods.Count(m => m.Form is not null));
        }
    }

    /// <summary>
    /// **只有型态规则挡得住的那两对**，单独再钉一次 —— 并且断言拒绝理由**确实来自型态规则**（不是部位）。
    /// <para>
    /// 这条是**变异探针**：谁把 <c>WeaponMods.ApplyMods</c> 里那段"至多一条带近战型态的改装"删掉，
    /// 部位闸拦不住这两对（枪口 vs 枪托），它们会**默默合成成功** ⇒ 本条立刻红。
    /// 上一条测试里"利爪+创伤"那一对是拦不住这个变异的（它有部位闸兜底），故必须有这一条。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("刺刀型", "利爪型")]
    [InlineData("刺刀型", "创伤型")]   // ← 此前完全没测过的那一对
    public void MeleeFormRule_IsTheOnlyThingStopping_MuzzlePlusStock(string muzzleForm, string stockForm)
    {
        // 前提：这两条改装占的是**不同部位**（所以部位闸帮不上忙）
        Assert.NotEqual(Catalog(muzzleForm).Part, Catalog(stockForm).Part);

        WeaponModException ex = Assert.Throws<WeaponModException>(
            () => Mod(Rifle(), Catalog(muzzleForm), Catalog(stockForm)));

        Assert.Contains("近战型态", ex.Message);   // 拒绝理由必须是型态规则本身
    }

    // ══════════════════════════ [T68] 锋刃型（短枪第 4 种近战型态）══════════════════════════
    //
    // 🔴 派单的担忧：「impl-weaponmod 的三选一只覆盖了长枪三型态，可能不知道有锋刃型 ⇒ 手枪能同时装锋刃型+刺刀型」。
    // 核实结论（见下三条测试）：
    //   ① 型态互斥规则**本就是通用的**（认任何带 Form 的改装，非硬编码三种）⇒ 锋刃型给了 Form 就自动进组。
    //   ② "手枪同时装锋刃型+刺刀型"**在白名单层就不可能**：刺刀/利爪/创伤的白名单 = 4 把重枪，**根本不含手枪**
    //      ⇒ 往手枪上装刺刀，在 FitsWeapons 闸就被拒（"装不到...上"），够不着型态闸。
    // ⚠️ **假绿警告（派单点名的坑）**：锋刃型占**枪托**，与利爪/创伤同部位。若拿"锋刃型+利爪型"当型态护栏，
    //     部位闸会先拦下 ⇒ 删掉型态规则它照样红 = 假绿。**且这一对在任何真枪上都装不上**（白名单不相交），
    //     故本簇**不用它当护栏**，改用①的结构断言 + ②的白名单断言。

    private static WeaponMod GunMod(string name)
        => WeaponModCatalog.For(WeaponClass.Firearm).First(m => m.Name == name);

    /// <summary>① 锋刃型带 <see cref="MeleeForm"/> ⇒ 结构上就在"至多一种型态"互斥组里（那条规则认任何 Form）。</summary>
    [Fact]
    public void 锋刃型_是第四种近战型态_带Form进互斥组()
    {
        Assert.NotNull(GunMod("锋刃型").Form);
        Assert.Equal(MeleeForm.Blade, GunMod("锋刃型").Form);
    }

    /// <summary>
    /// ② 🔴 <b>这才是"手枪不可能同时挂两种近战型态"的真正防线</b>：白名单不相交。
    /// 手枪装得上锋刃型；但刺刀/利爪/创伤的 <c>FitsWeapons</c> 里**根本没有手枪** ⇒ 往手枪上装它们，
    /// 在 FitsWeapons 闸就被拒（够不着型态闸）。反向亦然：步枪装不上锋刃型。
    /// </summary>
    [Fact]
    public void 短枪型态与重枪型态白名单严格不相交_故手枪装不了刺刀利爪创伤()
    {
        Weapon pistol = WeaponTable.Pistol();

        // 手枪装得上锋刃型
        Assert.NotNull(Mod(pistol, GunMod("锋刃型")).Weapon.MeleeProfile());

        // 但装不上任何重枪型态——且拒绝理由是**白名单**（"装不到"），不是型态闸
        foreach (string heavy in new[] { "刺刀型", "利爪型", "创伤型" })
        {
            WeaponModException ex = Assert.Throws<WeaponModException>(() => Mod(pistol, GunMod(heavy)));
            Assert.Contains("装不到", ex.Message);
        }

        // 反向：步枪装不上锋刃型（同样白名单闸）
        WeaponModException rev = Assert.Throws<WeaponModException>(() => Mod(Rifle(), GunMod("锋刃型")));
        Assert.Contains("装不到", rev.Message);

        // 结构断言：两组白名单交集为空（未来谁把手枪塞进重枪型态白名单，这条立刻红）
        var bladeGuns = GunMod("锋刃型").FitsWeapons;
        foreach (string heavy in new[] { "刺刀型", "利爪型", "创伤型" })
            Assert.Empty(bladeGuns.Intersect(GunMod(heavy).FitsWeapons));
    }

    /// <summary>③ 锋刃型单独能装（别把互斥写成"一个都不许装"），且它进 AppliedMods 时算作一种型态。</summary>
    [Fact]
    public void 锋刃型_单独装在手枪上_成立且算一种型态()
    {
        ModdedWeapon r = Mod(WeaponTable.Pistol(), GunMod("锋刃型"));
        Assert.NotNull(r.Weapon.MeleeProfile());
        Assert.Equal(DamageType.Sharp, r.Weapon.MeleeProfile()!.DamageType);   // 匕首＝锐击
        Assert.Equal(1, r.AppliedMods.Count(m => m.Form is not null));
    }

    /// <summary>
    /// 🔴 <b>存档兼容</b>：老档里若真存着一把"同时装了两个近战型态"的枪（本不该存在——
    /// <c>ApplyMods</c> 从来就拒绝这种组合，故正常游戏流程造不出来；但手改存档/未来数据变更可能造出来），
    /// <b>读档必须走 <c>impl-sync-all</c> 定下的降级范式：回落成基础武器，不静默失效、不吞材料、不崩。</b>
    /// <para>存档版本仍是 <b>v3</b>，本条不需要任何迁移代码（<c>Rebuild</c> 用**当前**规则重算，非法即 null）。</para>
    /// </summary>
    [Fact]
    public void 存档_老档里同时装了两个近战型态的枪_载入回落成基础枪_不崩不消失()
    {
        var 非法档 = new ModdedWeaponSpec("步枪（刺刀型・创伤型）", "步枪", new[] { "刺刀型", "创伤型" });

        // 严格版（"这组合合不合法"的判据，纯函数）⇒ null
        Assert.Null(ModdedWeaponRegistry.Rebuild(非法档));

        // 载入路径 ⇒ 回落成基础步枪：枪还在，改装没了
        Weapon? fallen = ModdedWeaponRegistry.RebuildOrBase(非法档);
        Assert.NotNull(fallen);
        Assert.Equal("步枪", fallen!.Name);
        Assert.Equal(DamageType.Blunt, fallen.MeleeProfile()!.DamageType);   // 原厂枪托，不是刺刀/铁锤
    }

    /// <summary>
    /// ✅ <b>[T47] 翻回正向硬护栏 —— 这是本单最重要的一条平衡断言。</b>
    ///
    /// <para><b>规则（用户的新口径）</b>：改装后的枪托必须
    /// <b>① 强于原厂枪托</b>（否则没人会去花 240 工时改它）、
    /// <b>② 弱于匕首</b>（改装能让你"打空了还能打"，但**不能让你不必带近战武器**）。</para>
    ///
    /// <para><b>上界为什么取匕首（2.353）而不是棍棒（2.04）</b> —— 用户拍板：
    /// 付了最大代价的型态（创伤型：重量 +50%、材料最贵、240 工时）近战**超过棍棒是应得的**
    /// ——棍棒是全表最弱的近战武器，一把加装了铁锤头的步枪打不过一根木棍，那才荒唐。
    /// 但它绝不能超过**一把真正的刀**。<b>创伤型 2.286 ＞ 棍棒 2.04 是有意为之，不是待修的 bug。</b></para>
    ///
    /// <para>🔴 <b>[T68] 上界从"匕首"松成"该型态克隆的那把武器本体"</b>。原设计（80% 攻速时）
    /// 恰好让最猛的型态（创伤 2.286）也压在匕首 2.353 之下，于是拿匕首当上界。
    /// 用户把攻速抬到 <b>85%</b> 后，利爪（=消防斧×0.85）/ 创伤（=尖头锤×0.85）会**略微超过最弱的匕首**
    /// （利爪 ≈2.375 ＞ 匕首 ≈2.353，富余仅 ~0.02）——这是 85% 的直接后果，不是 bug：
    /// <b>它们本就是"85% 的消防斧 / 尖头锤"，而消防斧/尖头锤远强于匕首，克隆件略过匕首是应得的。</b>
    /// 设计意图「改装不取代真近战武器」仍成立的**结构性保证**：每个型态严格 ＜ 它克隆的那把武器本体（85% ＜ 100%），
    /// 且远不及中高档真武器（长剑/重剑）——你要认真砍人还是会带把好刀。
    /// ⚠️ "利爪/创伤 略过匕首"已上抛协调者确认（属 85% 的既知后果）。</para>
    /// </summary>
    [Fact]
    public void ModdedStock_IsStrongerThanPlain_ButNeverBeatsItsSourceWeapon()
    {
        var refByForm = new Dictionary<string, Weapon>
        {
            ["刺刀型"] = WeaponTable.Arsenal().First(w => w.Name == "刺剑"),
            ["利爪型"] = WeaponTable.Arsenal().First(w => w.Name == "消防斧"),
            ["创伤型"] = WeaponTable.Arsenal().First(w => w.Name == "尖头锤"),
        };

        // ⚠️ 只有 4 把重枪能装近战型态（用户已把手枪/冲锋枪从白名单划掉）——逐枪逐型态全扫，不取样。
        foreach (Weapon gun in Firearms())
        {
            double plain = Dps(gun.MeleeProfile()!);

            foreach (var (form, source) in refByForm)
            {
                WeaponMod mod = Catalog(form);
                if (!mod.FitsWeapons.Contains(gun.Name)) continue;   // 手枪/冲锋枪装不了型态

                double modded = Dps(Mod(gun, mod).Weapon.MeleeProfile()!);

                Assert.True(modded > plain,
                    $"{gun.Name}·{form} DPS {modded:F3} 必须强于原厂枪托 {plain:F3} —— 否则没人会去改");
                // [T68] 上界 = 克隆源本体（85% 攻速 ⇒ 型态永远严格弱于它模仿的那把武器）
                Assert.True(modded < Dps(source),
                    $"{gun.Name}·{form} DPS {modded:F3} 不得达到其克隆源「{source.Name}」{Dps(source):F3} —— " +
                    "型态是 85% 攻速的克隆，永远不该反超本体");
            }
        }
    }

    /// <summary>
    /// 🔴 <b>[T47] 口径变更：型态【不再随枪身缩放】。</b>
    ///
    /// <para><b>旧口径（已作废）</b>：型态是"在这把枪自己的枪托数值上乘一个系数" ⇒ 重枪改出来的更猛
    /// （从前这里钉的是"狙击枪的利爪增量 ＞ 手枪的利爪增量"）。</para>
    ///
    /// <para><b>新口径（用户写在 wiki 上）</b>：「近战模式**等同于 80% 攻速的〈某把近战武器〉**」
    /// ⇒ <b>覆盖</b>，不是缩放 ⇒ <b>所有枪的同一型态，枪托数值完全一样</b>。
    /// 一句话：<b>你捅人用的是那把刺刀，不是那把枪</b>。差异全部搬到了**重量代价**上（+10%/+30%/+50%）。</para>
    ///
    /// <para>这条改钉新意图（不是删掉）：谁要是把"随枪身缩放"改回来，四把枪的同型态数值会立刻分叉，这里当场红。</para>
    /// </summary>
    [Fact]
    public void Forms_NoLongerScaleWithTheGun_AllGunsGetIdenticalStockStats()
    {
        foreach (string form in new[] { "刺刀型", "利爪型", "创伤型" })
        {
            WeaponMod mod = Catalog(form);
            var profiles = Firearms()
                .Where(g => mod.FitsWeapons.Contains(g.Name))
                .Select(g => Mod(g, mod).Weapon.MeleeProfile()!)
                .ToList();

            Assert.Equal(4, profiles.Count);   // 四把重枪

            foreach (Weapon p in profiles)
            {
                Assert.Equal(profiles[0].DamageMin, p.DamageMin, 9);
                Assert.Equal(profiles[0].DamageMax, p.DamageMax, 9);
                Assert.Equal(profiles[0].Penetration, p.Penetration, 9);
                Assert.Equal(profiles[0].AttackInterval, p.AttackInterval, 9);
                Assert.Equal(profiles[0].NoiseRadius, p.NoiseRadius, 9);
            }
        }
    }

    /// <summary>
    /// 型态的枪托 = <b>[T68] 85% 攻速的〈刺剑 / 消防斧 / 尖头锤 / 匕首〉</b> —— 逐字段对着 <c>WeaponTable</c> 核。
    /// <para>数值**从武器表读、不抄数字** ⇒ 用户日后调那几把武器，型态自动跟着变。这条钉死这层联动 + 85% 攻速系数。</para>
    /// </summary>
    [Theory]
    [InlineData("刺刀型", "刺剑")]
    [InlineData("利爪型", "消防斧")]
    [InlineData("创伤型", "尖头锤")]
    public void Forms_AreExactlyTheReferenceWeapon_At85PercentSpeed(string form, string referenceName)
    {
        Weapon reference = WeaponTable.Arsenal().First(w => w.Name == referenceName);
        Weapon stock = Mod(Rifle(), Catalog(form)).Weapon.MeleeProfile()!;

        Assert.Equal(reference.DamageMin, stock.DamageMin, 9);
        Assert.Equal(reference.DamageMax, stock.DamageMax, 9);
        Assert.Equal(reference.Penetration, stock.Penetration, 9);
        Assert.Equal(reference.NoiseRadius, stock.NoiseRadius, 9);
        Assert.Equal(reference.AttackInterval / 0.85, stock.AttackInterval, 9);   // [T68] 85% 攻速 ⇒ 间隔 ÷0.85
        Assert.Equal(Dps(reference) * 0.85, Dps(stock), 9);
    }

    /// <summary>[T68] 锋刃型（短枪）的枪托 = 85% 攻速的匕首——单独核一遍（它装在手枪，不在上面 Theory 的 Rifle 上）。</summary>
    [Fact]
    public void BladeForm_IsExactlyTheDagger_At85PercentSpeed()
    {
        Weapon dagger = WeaponTable.Arsenal().First(w => w.Name == "匕首");
        Weapon stock = Mod(WeaponTable.Pistol(), GunMod("锋刃型")).Weapon.MeleeProfile()!;

        Assert.Equal(dagger.DamageMin, stock.DamageMin, 9);
        Assert.Equal(dagger.DamageMax, stock.DamageMax, 9);
        Assert.Equal(dagger.Penetration, stock.Penetration, 9);
        Assert.Equal(dagger.AttackInterval / 0.85, stock.AttackInterval, 9);
    }

    // ==================== 3. 改装台：设施 + 材料 + 工时 ====================

    private static WeaponModAvailability Check(bool bench, params (string Key, int Qty)[] stock)
    {
        var have = stock.ToDictionary(s => s.Key, s => s.Qty);
        return WeaponModLogic.CanApply(
            Rifle(),
            new[] { Catalog("刺刀型") },
            key => have.GetValueOrDefault(key),
            hasModBench: bench);
    }

    /// <summary>**没有改装台就改不了枪**（用户拍板：武器改造只能在改装台上做）。</summary>
    [Fact]
    public void WithoutModBench_CannotModify()
    {
        WeaponModAvailability r = Check(bench: false, ("iron", 9), ("rope", 9));

        Assert.False(r.CanApply);
        Assert.Contains(r.Blocks, b => b.Reason == WeaponModBlockReason.NoModBench);
    }

    /// <summary>材料不够也改不了（改装**不再是白送的**）。</summary>
    [Fact]
    public void WithoutMaterials_CannotModify()
    {
        WeaponModAvailability r = Check(bench: true);   // 有台子，但库存空空

        Assert.False(r.CanApply);
        Assert.Contains(r.Blocks, b => b.Reason == WeaponModBlockReason.InsufficientMaterial);
    }

    /// <summary>改装台 + 材料齐 ⇒ 放行。</summary>
    [Fact]
    public void WithBenchAndMaterials_CanModify()
    {
        WeaponModAvailability r = Check(bench: true, ("iron", 9), ("rope", 9));

        Assert.True(r.CanApply);
        Assert.Empty(r.Blocks);
    }

    /// <summary>
    /// **没有白送的改装** —— 每条至少要付**工时**（改装不是点击即得，得有人站在改装台前把活干完）。
    ///
    /// <para>
    /// [T47] <b>材料可以为空</b>（用户在 wiki 上把三条的材料清掉了），而且**语义是对的**：
    /// 截短枪管 = 锯掉一截，不消耗任何东西；锋刃研磨 = 一块磨刀石反复用；锯齿剑刃 = 在刃上开齿。
    /// 它们付的是**时间**（60 / 60 / 240 分钟），不是物资。
    /// </para>
    /// <para>
    /// ⚠️ 白名单写死这三条：**第四条零材料的改装冒出来时这里会红** —— 逼来人确认那到底是设计还是漏填。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryMod_CostsTime_AndOnlyThreeAreMaterialFree()
    {
        string[] materialFree = { "sawn_off_barrel", "serrated_blade", "honed_edge" };

        foreach (WeaponMod m in WeaponModCatalog.All())
        {
            Assert.True(m.WorkMinutes > 0, $"改装「{m.Name}」没有工时 —— 改装不是点击即得");

            if (materialFree.Contains(m.Id))
            {
                Assert.Empty(m.MaterialCosts);
            }
            else
            {
                Assert.True(m.MaterialCosts.Count > 0,
                    $"改装「{m.Name}」没有材料成本 —— 若这是有意的（像锯短枪管那样「不消耗东西」），" +
                    "把它加进本测试的 materialFree 白名单，并写清理由");
            }
        }
    }

    /// <summary>
    /// 每条改装都得有**稳定内部 id**，且全表唯一、只用 ASCII 蛇形。
    /// <para>
    /// 用户在**本地 wiki** 上调改装数值，改完由 agent 靠这个 id 同步回 catalog。
    /// 中文名当不了 id：「防滑缠手」跨锐器/钝器**同名两条**（按名索引会撞），而且中文名随时可能改。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryMod_HasStableAsciiId_UniqueAcrossCatalog()
    {
        var ids = WeaponModCatalog.All().Select(m => m.Id).ToList();

        Assert.All(ids, id => Assert.Matches("^[a-z0-9_]+$", id));
        Assert.Equal(ids.Count, ids.Distinct().Count());   // 唯一（含跨类同名的两条「防滑缠手」）

        // 三种近战型态的 id 逐字钉死——wiki 回写靠它定位，改了就对不上了。
        Assert.Equal("bayonet", Catalog("刺刀型").Id);
        Assert.Equal("claw_stock", Catalog("利爪型").Id);
        Assert.Equal("trauma_stock", Catalog("创伤型").Id);
    }

    /// <summary>
    /// 多选改装时，材料与工时**累加**（不是后一条覆盖前一条 —— 那会让多选改装白嫖材料）。
    ///
    /// <para>
    /// ⚠️ [T46] 本测试从前钉的是「<c>scrap_metal</c> 与 <c>metal_ingot</c> 同时出现时必须累加」。
    /// 废金属 + 金属锭已被合并成**铁**（<c>impl-iron</c>）⇒ 那两个 key 变成了同一个，**原来的立意塌了**
    /// （它测的是"两个不同的 key 各自累加"）。故换一对材料重写：
    /// <b>「铁」测同 key 累加（4 + 3 = 7），「绳子」测只有一条改装吃的材料不会被漏掉。</b>
    /// </para>
    /// </summary>
    [Fact]
    public void MultipleMods_SumCostAndWorkMinutes()
    {
        var mods = new[] { Catalog("刺刀型"), Catalog("加长枪管") };
        IReadOnlyDictionary<string, int> cost = WeaponModLogic.TotalCost(mods);

        Assert.Equal(Catalog("刺刀型").WorkMinutes + Catalog("加长枪管").WorkMinutes,
            WeaponModLogic.TotalWorkMinutes(mods));

        // 两条都吃「铁」（刺刀 4 + 长管 3）⇒ 必须累加而非覆盖
        Assert.Equal(Catalog("刺刀型").MaterialCosts["iron"] + Catalog("加长枪管").MaterialCosts["iron"],
            cost["iron"]);
        Assert.Equal(7, cost["iron"]);

        // 只有刺刀吃「绳子」⇒ 也不能在合并时被另一条的成本冲掉
        Assert.Equal(1, cost["rope"]);
    }

    /// <summary>改装台在**工作台**上造得出来，且有成本/工时（用户原话：在工作台可以制作改装台）。</summary>
    [Fact]
    public void ModBench_IsCraftableAtWorkbench()
    {
        RecipeData? bench = RecipeBook.Find(WeaponModLogic.BenchRecipeId);
        Assert.NotNull(bench);
        Assert.Equal("改装台", bench!.DisplayName);
        Assert.True(bench.MaterialCosts.Count > 0);
        Assert.True(bench.WorkMinutes > 0);
        // 造一台案子是精工活：要卡尺
        Assert.Contains(ToolSlot.Calipers, bench.RequiredTools);
    }

    /// <summary>
    /// **改装台的固定锚点不许侵入防线禁建带**（用户拍板：改装台是营地内**固定位置**，在车间＝空牛棚）。
    /// <para>
    /// 固定位置**比可摆放更需要这条自检**：它实心、挖导航洞、不可跨越，而玩家**摆不了也挪不动**——
    /// 锚点若压进禁建带，就是一条玩家永远无法纠正的死路（守卫走不到墙根、砌墙的人站不进施工位）。
    /// </para>
    /// </summary>
    [Fact]
    public void ModBenchAnchor_DoesNotIntrude_DefenseKeepOutBand()
    {
        // 规格本身：实心、且**不豁免**禁建带（沙袋才有豁免，因为它恒不挡路）
        Assert.True(WeaponModLogic.BenchSpec.IsSolid);
        Assert.False(WeaponModLogic.BenchSpec.AllowedAgainstDefenses);

        // 锚点落在**空牛棚（车间）**内：[1480,980,420,320]
        float x = WeaponModLogic.BenchAnchorX, y = WeaponModLogic.BenchAnchorY;
        float w = WeaponModLogic.BenchWidth, h = WeaponModLogic.BenchHeight;

        Assert.True(x >= 1480 && x + w <= 1900, "改装台锚点应在空牛棚（车间）的 x 范围内");
        Assert.True(y >= 980 && y + h <= 1300, "改装台锚点应在空牛棚（车间）的 y 范围内");

        // 与牛棚现有道具「草垛A」[1540,1020,86,72] 不重叠
        bool overlapsHay = x < 1540 + 86 && 1540 < x + w && y < 1020 + 72 && 1020 < y + h;
        Assert.False(overlapsHay, "改装台锚点不该压在牛棚草垛上");
    }

    /// <summary>改装台可拆（返还一半），且拆除表与配方**同成本**——两处不同步就是数值 bug。</summary>
    [Fact]
    public void ModBench_SalvageCost_MatchesRecipeCost()
    {
        RecipeData bench = RecipeBook.Find(WeaponModLogic.BenchRecipeId)!;
        IReadOnlyDictionary<string, int>? furniture = FurnitureBuildCost.Of(WeaponModLogic.BenchFurnitureKey);

        Assert.NotNull(furniture);
        Assert.Equal(bench.MaterialCosts.OrderBy(k => k.Key), furniture!.OrderBy(k => k.Key));
    }

    /// <summary>改装任务 id 能原样解回（基础武器 + 改装列表）——工时任务靠它在完工时还原要做的事。</summary>
    [Fact]
    public void ModJobId_RoundTrips()
    {
        string jobId = WeaponModLogic.JobIdFor("步枪", new[] { "刺刀型", "加长枪管" });

        var target = WeaponModLogic.TargetOf(jobId);
        Assert.NotNull(target);
        Assert.Equal("步枪", target!.Value.BaseWeaponKey);
        Assert.Equal(new[] { "刺刀型", "加长枪管" }, target.Value.ModNames);

        // 不是改装任务的 id 不该被误认（拆解任务共用同一个 CraftingJob 队列）
        Assert.Null(WeaponModLogic.TargetOf("salvage:prop#工作台"));
        Assert.Null(WeaponModLogic.TargetOf("bone_knife"));
    }

    /// <summary>
    /// **改装型态必须走 `Unarmed.MeleeFor` 那条唯一的近战路径**（不许在 Actor 里另起 if 分支，
    /// 否则"持弓=空手"与"改装枪托"会各走各的、互相盖掉）。
    /// <para>
    /// 之所以**不需要**给 <c>MeleeFor</c> 加 override 入参：型态已经烧进 <see cref="Weapon.StockMeleeDamageType"/>，
    /// 改装武器**就是一把普通 Weapon** ⇒ <c>MeleeFor(改装枪)</c> 自然返回锐击枪托，一行都不用改。
    /// </para>
    /// </summary>
    [Fact]
    public void ModdedStock_FlowsThrough_UnarmedMeleeFor_NoSpecialCasing()
    {
        Weapon plainRifle = Rifle();
        Weapon bayonetRifle = Mod(Rifle(), Catalog("刺刀型")).Weapon;

        // 原厂枪：抡枪托 = 钝击
        Assert.Equal(DamageType.Blunt, Unarmed.MeleeFor(plainRifle).DamageType);
        // 装了刺刀的同一把枪：同一条路径，打出来是锐击突刺
        Weapon melee = Unarmed.MeleeFor(bayonetRifle);
        Assert.Equal(DamageType.Sharp, melee.DamageType);
        Assert.True(melee.Penetration > Unarmed.MeleeFor(plainRifle).Penetration);

        // 两者都不算"空手近战"（拿着枪就不是空手）
        Assert.False(Unarmed.IsUnarmedMelee(bayonetRifle));
    }

    // ==================== 4. 改装武器的身份（P0-A / P0-B） ====================

    /// <summary>
    /// **P0-A 的真正端到端护栏：改装出来的枪，必须真的能拿到手里、并且打出锐击。**
    /// <para>
    /// <b>这条测试为什么必须存在</b>：此前 <c>Pawn.EquipWeapon</c> 查的是一张
    /// <c>WeaponTable.Arsenal().ToDictionary()</c> 的静态字典（只含**原厂**武器），
    /// 而改装变体是**运行时注册**的 ⇒ 回查落空 ⇒ **装备静默返 false ⇒ 玩家花了材料+工时改出来的枪永远拿不起来**，
    /// 三种枪托型态也就永远进不了战斗。而当时的测试**只断言了注册表查得到**，没碰真正的消费方 ⇒ 绿灯给了假信心。
    /// </para>
    /// <para>
    /// <c>Pawn</c> 是 Godot 节点、单测里造不出来；但**装备的真实结算在纯引擎的
    /// <see cref="WeaponLoadout"/> 里**（Pawn 只是"按名解析 → 交给它"）。所以这里把那条链原样跑一遍：
    /// **按名解析（唯一入口）→ 上手 → 贴脸打出来的是锐击**。解析入口一旦回退成只查原厂表，这条立刻红。
    /// </para>
    /// </summary>
    [Fact]
    public void ModdedVariant_CanActuallyBeEquipped_AndStrikesSharp()
    {
        ModdedWeaponRegistry.Clear();
        string variantName = ModdedWeaponRegistry.Register(Mod(Rifle(), Catalog("刺刀型")));

        // ① 按名解析——这正是 Pawn.EquipWeapon 现在走的唯一入口
        Weapon? resolved = ModdedWeaponRegistry.WeaponByName(variantName);
        Assert.NotNull(resolved);

        // ② 真的上手（双手长枪）
        var loadout = new WeaponLoadout();
        Assert.True(loadout.EquipTwoHanded(resolved!), "改装出来的枪必须装得上——装不上这单就白做了");

        // ③ 拿在手里贴脸打，打出来的是**锐击突刺**（型态真的进了战斗，不是装饰）
        Weapon inHand = loadout.RightHand!;
        Weapon melee = Unarmed.MeleeFor(inHand);
        Assert.Equal(DamageType.Sharp, melee.DamageType);
        Assert.True(melee.Penetration > Rifle().MeleeProfile()!.Penetration);

        // ④ **回归护栏**：原厂那张 Arsenal 字典**根本查不到**这个名字——
        //    这就是当初 EquipWeapon 失败的原因。谁把解析入口退回去，②③ 立刻红。
        Assert.DoesNotContain(WeaponTable.Arsenal(), w => w.Name == variantName);
    }

    /// <summary>
    /// **P0-A/B**：改装出来的变体必须能**按名回查**——否则装备不上（Pawn.EquipWeapon 查不到）、
    /// 存档即蒸发（SaveMapper 查不到）。注册后 <see cref="ModdedWeaponRegistry.WeaponByName"/> 必须找得到它。
    /// </summary>
    [Fact]
    public void ModdedVariant_IsResolvableByName_AfterRegistration()
    {
        ModdedWeaponRegistry.Clear();
        ModdedWeapon modded = Mod(Rifle(), Catalog("刺刀型"));
        string name = ModdedWeaponRegistry.Register(modded);

        Weapon? found = ModdedWeaponRegistry.WeaponByName(name);
        Assert.NotNull(found);
        Assert.Equal(DamageType.Sharp, found!.MeleeProfile()!.DamageType);   // 型态没在回查中丢失

        // 原厂武器仍然查得到（回查先走 WeaponTable）
        Assert.NotNull(ModdedWeaponRegistry.WeaponByName("步枪"));
        // 没登记过的名字查不到
        Assert.Null(ModdedWeaponRegistry.WeaponByName("步枪（不存在的改装）"));
    }

    /// <summary>
    /// **P0-B 存档往返**：只存 (变体名, 基础武器名, 改装名) 三个字符串，读档按当前规则重合成 ——
    /// 数值不入档，故日后调了改装数值，老存档里的枪自动跟着改，不会腐化成旧数值。
    /// </summary>
    [Fact]
    public void ModdedVariant_SurvivesSaveLoadRoundTrip()
    {
        ModdedWeaponRegistry.Clear();
        ModdedWeapon modded = Mod(Rifle(), Catalog("利爪型"));
        string name = ModdedWeaponRegistry.Register(modded);
        double maxBefore = ModdedWeaponRegistry.WeaponByName(name)!.MeleeProfile()!.DamageMax;

        IReadOnlyList<ModdedWeaponSpec> saved = ModdedWeaponRegistry.Specs;
        Assert.Single(saved);
        Assert.Equal("步枪", saved[0].BaseWeaponName);
        Assert.Equal(new[] { "利爪型" }, saved[0].ModNames);

        // 模拟退出 → 读档
        ModdedWeaponRegistry.Clear();
        Assert.Null(ModdedWeaponRegistry.WeaponByName(name));   // 清干净了
        ModdedWeaponRegistry.Restore(saved);

        Weapon? restored = ModdedWeaponRegistry.WeaponByName(name);
        Assert.NotNull(restored);
        Assert.Equal(maxBefore, restored!.MeleeProfile()!.DamageMax);
        Assert.Equal(DamageType.Sharp, restored.MeleeProfile()!.DamageType);
    }
}
