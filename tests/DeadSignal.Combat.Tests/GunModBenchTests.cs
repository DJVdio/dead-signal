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
    // 全表 7 把枪（弓弩没有枪托，见 WeaponTable：弓的 StockMelee* 恒 null）。
    private static IReadOnlyList<Weapon> Firearms() =>
        WeaponTable.Arsenal().Where(w => w.IsRanged && w.HasMeleeProfile).ToList();

    private static double Dps(Weapon w) => (w.DamageMin + w.DamageMax) / 2.0 / w.AttackInterval;

    // ==================== 1. 枪托近战数值定稿 ====================

    /// <summary>七把枪都得有枪托 profile（不然打空了就只能站着挨打）。弓弩不在此列——它们没有枪托。</summary>
    [Fact]
    public void AllSevenFirearms_HaveStockMeleeProfile()
    {
        Assert.Equal(7, Firearms().Count);
        Assert.All(Firearms(), w => Assert.NotNull(w.MeleeProfile()));
    }

    /// <summary>
    /// **枪托的天花板**：任何一把枪的枪托 DPS 都必须**严格低于全表最弱的真近战武器（匕首）**。
    /// 抡枪托绝不该比拿一把真家伙更划算——枪托是"你打空了"的意思，不是一条武器路线。
    /// </summary>
    [Fact]
    public void StockMelee_IsStrictlyWorseThan_TheWeakestRealMeleeWeapon()
    {
        double dagger = Dps(WeaponTable.Dagger());   // 全表最弱近战武器
        double club = Dps(WeaponTable.Club());       // 最弱钝器

        foreach (Weapon gun in Firearms())
        {
            double stock = Dps(gun.MeleeProfile()!);
            Assert.True(stock < dagger,
                $"{gun.Name} 的枪托 DPS {stock:F2} 应低于匕首 {dagger:F2}——抡枪托不该比拿刀强");
            Assert.True(stock < club,
                $"{gun.Name} 的枪托 DPS {stock:F2} 应低于棍棒 {club:F2}——抡枪托不该比抡棍棒强");
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
        Assert.Equal(3, forms.Count);   // 利爪 / 创伤 / 刺刀，不多不少

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

    /// <summary>**一把枪只能有一种近战型态**（三选一）。刺刀在枪口、利爪在枪托——部位不同，故必须靠型态规则挡住。</summary>
    [Fact]
    public void OnlyOneMeleeForm_PerWeapon()
    {
        // 刺刀(枪口) + 利爪(枪托)：部位不冲突，但型态冲突 ⇒ 必须拒绝
        WeaponModException ex = Assert.Throws<WeaponModException>(
            () => Mod(Rifle(), Catalog("刺刀型"), Catalog("利爪型")));
        Assert.Contains("近战型态", ex.Message);

        // 利爪 + 创伤：同占枪托 ⇒ 部位冲突先拦下
        Assert.Throws<WeaponModException>(() => Mod(Rifle(), Catalog("利爪型"), Catalog("创伤型")));
    }

    /// <summary>
    /// **改装后的枪托仍够不到长剑/尖头锤那一档**：型态能把"绝望手段"抬成"能打"，
    /// 但一把枪不该同时是全场最好的近战武器。上限锚在棍棒。
    /// </summary>
    [Fact]
    public void ModdedStock_StaysBelow_RealMeleeCeiling()
    {
        double club = Dps(WeaponTable.Club());
        foreach (string form in new[] { "刺刀型", "利爪型", "创伤型" })
        {
            double dps = Dps(Mod(Rifle(), Catalog(form)).Weapon.MeleeProfile()!);
            Assert.True(dps <= club,
                $"{form} 改装后枪托 DPS {dps:F2} 不该超过棍棒 {club:F2}");
            Assert.True(dps > Dps(Rifle().MeleeProfile()!),
                $"{form} 总得比没改装强，否则没人会去改");
        }
    }

    /// <summary>型态用**乘算**缩放：重枪改出来的近战件就该更重（不是给每把枪加同样的固定值）。</summary>
    [Fact]
    public void Forms_ScaleMultiplicatively_WithGunWeight()
    {
        WeaponMod claw = Catalog("利爪型");
        double pistolGain = Mod(WeaponTable.Pistol(), claw).Weapon.MeleeProfile()!.DamageMax
                            - WeaponTable.Pistol().MeleeProfile()!.DamageMax;
        double sniperGain = Mod(WeaponTable.SniperRifle(), claw).Weapon.MeleeProfile()!.DamageMax
                            - WeaponTable.SniperRifle().MeleeProfile()!.DamageMax;

        Assert.True(sniperGain > pistolGain,
            "同一条利爪型改装，装在重枪上的增量应大于轻枪（乘算而非加算）");
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
        WeaponModAvailability r = Check(bench: false,
            ("metal_ingot", 9), ("scrap_metal", 9), ("rope", 9));

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
        WeaponModAvailability r = Check(bench: true,
            ("metal_ingot", 9), ("scrap_metal", 9), ("rope", 9));

        Assert.True(r.CanApply);
        Assert.Empty(r.Blocks);
    }

    /// <summary>每条改装都得有材料成本和工时——**没有白送的改装**。</summary>
    [Fact]
    public void EveryMod_CostsMaterialsAndTime()
    {
        Assert.All(WeaponModCatalog.All(), m =>
        {
            Assert.True(m.MaterialCosts.Count > 0, $"改装「{m.Name}」没有材料成本");
            Assert.True(m.WorkMinutes > 0, $"改装「{m.Name}」没有工时");
        });
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

    /// <summary>多选改装时，材料与工时**累加**。</summary>
    [Fact]
    public void MultipleMods_SumCostAndWorkMinutes()
    {
        var mods = new[] { Catalog("刺刀型"), Catalog("加长枪管") };
        int expected = Catalog("刺刀型").WorkMinutes + Catalog("加长枪管").WorkMinutes;

        Assert.Equal(expected, WeaponModLogic.TotalWorkMinutes(mods));
        // 两条都吃 scrap_metal（2 + 1）与 metal_ingot（1 + 1）⇒ 必须累加而非覆盖
        Assert.Equal(3, WeaponModLogic.TotalCost(mods)["scrap_metal"]);
        Assert.Equal(2, WeaponModLogic.TotalCost(mods)["metal_ingot"]);
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
