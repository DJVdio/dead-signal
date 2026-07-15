using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DeadSignal.Combat;
using DeadSignal.Godot;
using Xunit;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 批次25·T47：**消耗型改装（锋刃研磨：穿透 +75%，攻击三次后失去）** + **重量作为核心代价** + **穿透 100% 上限**。
///
/// <para>
/// 这一组钉死的是这单最难的一块：**给改装加状态层，而不是把纯函数改成有状态的**。
/// <list type="number">
/// <item><b><c>Rebuild(spec)</c> 保持纯函数</b>：它是"这组合当前还合不合法"的判据，砍了三刀不该改变它的答案。</item>
/// <item><b>次数是武器【实例】上的状态</b>：两把研磨过的短剑必须能分辨谁砍了几下
///       （靠**唯一实例名** —— 变体名就是实例 id，见 <c>ModdedWeaponRegistry.Register</c>）。</item>
/// <item><b>存进存档 v3</b>（往 <c>impl-iron</c> 开好的 payload 里加字段，**没撞版本号**），
///       且**老档缺字段 ⇒ 补满次数、不是 0**。</item>
/// <item><b>用光即脱落且玩家看得见</b>：<c>ConsumeUse</c> 返回换成哪把武器 + 哪条改装掉了。</item>
/// </list>
/// </para>
/// </summary>
[Collection(ModdedWeaponRegistryCollection.Name)]
public class WeaponModWearTests
{
    private static WeaponMod Honed() => WeaponModCatalog.HonedEdge();
    private static WeaponMod GripWrap() => WeaponModCatalog.GripWrapBlade();
    private static Weapon Shortsword() => WeaponTable.Shortsword();

    private static ModdedWeapon Mod(Weapon w, params WeaponMod[] mods) => WeaponMods.ApplyMods(w, mods);

    // ═══════════════════ 1. 目录：锋刃研磨是全表唯一的消耗型改装 ═══════════════════

    /// <summary>
    /// 锋刃研磨 = **穿透 +75%（乘算）、攻击三次后失去**。数值逐格钉死 —— 用户写在 wiki 上的就是这个。
    /// <para>⚠️ 派单书里写的是"+25%"，**以 wiki 为准**（表赢代码）。</para>
    /// </summary>
    [Fact]
    public void 锋刃研磨_穿透乘算加百分之七十五_且三次后失效()
    {
        WeaponMod honed = Honed();

        Assert.Equal(3, honed.UsesBeforeBreak);
        Assert.True(honed.IsConsumable);

        // 穿透是**乘算**的（用户口径：「穿透 -10% 指在原本数值上 -该数值的 10%，例如 20% 变成 18%」）
        StatMod pen = Assert.Single(honed.Stats, s => s.Stat == WeaponStat.Penetration);
        Assert.Equal(StatOp.Mul, pen.Op);
        Assert.Equal(1.75, pen.Value, 6);

        // 端到端：短剑穿透 × 1.75
        Weapon plain = Shortsword();
        Weapon sharp = Mod(plain, honed).Weapon;
        Assert.Equal(plain.Penetration * 1.75, sharp.Penetration, 6);
        Assert.True(sharp.Penetration > plain.Penetration, "研磨过的刀必须更能破甲");
    }

    /// <summary>全表**只有**锋刃研磨一条是消耗型 —— 谁又加了一条，这里会红，提醒他去接耐久层/存档/UI。</summary>
    [Fact]
    public void 全表只有锋刃研磨一条是消耗型改装()
    {
        var consumables = WeaponModCatalog.All().Where(m => m.IsConsumable).Select(m => m.Id).ToList();
        Assert.Equal(new[] { "honed_edge" }, consumables);

        // 其余 13 条恒为永久（UsesBeforeBreak == null）
        Assert.All(WeaponModCatalog.All().Where(m => m.Id != "honed_edge"),
            m => Assert.Null(m.UsesBeforeBreak));
    }

    // ═══════════════════ 2. 实例身份：两把研磨过的刀必须能分辨 ═══════════════════

    /// <summary>
    /// 🔴 <b>这单最核心的一条。</b> 两把「短剑（锋刃研磨）」若共用一个变体名，就会**共用一个次数计数器** ——
    /// 砍了三下，两把一起钝。故带消耗型改装的武器登记时必须拿到**唯一实例名**。
    /// </summary>
    [Fact]
    public void 两把研磨过的短剑_是两个独立实例_各砍各的()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            string a = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed()));
            string b = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed()));

            Assert.NotEqual(a, b);   // ← 唯一实例名。共用名字 = 共用计数器 = 这条改装做不成

            // 砍 a 两下，b 一下都没动
            ModdedWeaponRegistry.ConsumeUse(a);
            ModdedWeaponRegistry.ConsumeUse(a);

            Assert.Equal(1, ModdedWeaponRegistry.RemainingUses(a, "锋刃研磨"));
            Assert.Equal(3, ModdedWeaponRegistry.RemainingUses(b, "锋刃研磨"));
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>
    /// <b>永久改装（其余 13 条）行为完全不变</b>：名字照旧是"步枪（刺刀型）"，**不带实例后缀**、可共享。
    /// 这条是**零回归护栏** —— 实例名机制若泄漏到永久改装上，既有存档/UI/测试会一起塌。
    /// </summary>
    [Fact]
    public void 永久改装不带实例后缀_行为与从前逐字相同()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            string a = ModdedWeaponRegistry.Register(
                WeaponMods.ApplyMods(WeaponTable.Rifle(), new[] { WeaponModCatalog.Bayonet() }));
            string b = ModdedWeaponRegistry.Register(
                WeaponMods.ApplyMods(WeaponTable.Rifle(), new[] { WeaponModCatalog.Bayonet() }));

            Assert.Equal("步枪（刺刀型）", a);
            Assert.Equal(a, b);                 // 同名幂等覆盖（从前就是这样）
            Assert.DoesNotContain('#', a);      // 没有实例后缀泄漏出来
            Assert.Empty(ModdedWeaponRegistry.RemainingUsesOf(a));   // 也没有耐久条目
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>
    /// <c>Item.RefKey == Weapon.Name</c> 是全项目的**隐含不变式**（库存/装备/负重/存档全靠它）。
    /// 实例名机制不许把它捅破：按实例名查出来的那把枪，自己也得叫这个名字。
    /// </summary>
    [Fact]
    public void 实例名与武器自己的名字必须一致_否则库存与装备会错位()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            string name = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed()));
            Weapon? w = ModdedWeaponRegistry.WeaponByName(name);

            Assert.NotNull(w);
            Assert.Equal(name, w!.Name);
            Assert.Equal("短剑", ModdedWeaponRegistry.BaseNameOf(name));   // 负重表按基础武器名索引，别落进"未登记武器"
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    // ═══════════════════ 3. Rebuild 仍是纯函数（语义没被耐久污染） ═══════════════════

    /// <summary>
    /// 🔴 <b><c>Rebuild</c> 是"这组合当前还合不合法"的判据 —— 砍了三刀不该改变它的答案。</b>
    /// 耐久是**另一层**的事。这条钉死两者没有互相污染。
    /// </summary>
    [Fact]
    public void Rebuild保持纯函数_砍到脱落也不改变它的答案()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            string name = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed()));
            var spec = new ModdedWeaponSpec(name, "短剑", new[] { "锋刃研磨" });

            Weapon? before = ModdedWeaponRegistry.Rebuild(spec);
            Assert.NotNull(before);

            // 砍到脱落
            ModdedWeaponRegistry.ConsumeUse(name);
            ModdedWeaponRegistry.ConsumeUse(name);
            ModdedWeaponRegistry.ConsumeUse(name);

            // 同一条 spec，Rebuild 给出的答案**逐位相同**（纯函数：同入参同出参）
            Weapon? after = ModdedWeaponRegistry.Rebuild(spec);
            Assert.NotNull(after);
            Assert.Equal(before!.Penetration, after!.Penetration, 9);
            Assert.Equal(before.Name, after.Name);
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    // ═══════════════════ 4. 用光即脱落，且玩家看得见 ═══════════════════

    /// <summary>
    /// 砍满三下 ⇒ 锋刃研磨**脱落**，武器回落成一把干干净净的**短剑**（改装没了，刀还在），
    /// 并且把"哪条改装掉了"报出来 —— <b>不能静默失效</b>。
    /// </summary>
    [Fact]
    public void 砍满三下_锋刃研磨脱落_武器回落成基础短剑_且报给玩家()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            string name = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed()));

            ModWearResult r1 = ModdedWeaponRegistry.ConsumeUse(name);
            ModWearResult r2 = ModdedWeaponRegistry.ConsumeUse(name);
            Assert.False(r1.Changed);   // 前两下：武器没变，调用方什么都不用做
            Assert.False(r2.Changed);
            Assert.Equal(1, ModdedWeaponRegistry.RemainingUses(name, "锋刃研磨"));

            ModWearResult r3 = ModdedWeaponRegistry.ConsumeUse(name);
            Assert.True(r3.Changed);                              // 第三下：变了
            Assert.Equal("短剑", r3.WeaponName);                  // 回落成基础武器（不是凭空消失）
            Assert.Equal(new[] { "锋刃研磨" }, r3.BrokenModNames); // 玩家看得见：是这条改装磨没了

            // 旧实例名当场注销（再没有持有者，留着只会让存档滚大）
            Assert.Null(ModdedWeaponRegistry.WeaponByName(name));
            Assert.Empty(ModdedWeaponRegistry.Specs);

            // 回落到的"短剑"是原厂武器，本来就在 WeaponTable 里 ⇒ 拿得到，穿透回到原值
            Weapon? plain = ModdedWeaponRegistry.WeaponByName("短剑");
            Assert.NotNull(plain);
            Assert.Equal(WeaponTable.Shortsword().Penetration, plain!.Penetration, 9);
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>
    /// **还剩别的改装**时，脱落只摘掉用光的那一条 —— 武器变成"只带防滑缠手的短剑"，攻速加成还在。
    /// （这是"回落成基础武器"之外的另一条分支，最容易写漏。）
    /// </summary>
    [Fact]
    public void 脱落只摘掉用光的那一条_其余改装原样保留()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            // 锋刃研磨（刃）+ 防滑缠手（缠手）—— 不同部位，可共存
            string name = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed(), GripWrap()));

            ModdedWeaponRegistry.ConsumeUse(name);
            ModdedWeaponRegistry.ConsumeUse(name);
            ModWearResult broke = ModdedWeaponRegistry.ConsumeUse(name);

            Assert.True(broke.Changed);
            Assert.Equal(new[] { "锋刃研磨" }, broke.BrokenModNames);
            Assert.NotEqual("短剑", broke.WeaponName);   // 还剩防滑缠手 ⇒ 仍是个变体，不是基础武器

            Weapon? now = ModdedWeaponRegistry.WeaponByName(broke.WeaponName);
            Assert.NotNull(now);
            // 穿透回到原值（研磨没了），但攻速加成还在（缠手还在）
            Assert.Equal(WeaponTable.Shortsword().Penetration, now!.Penetration, 9);
            Assert.Equal(WeaponTable.Shortsword().AttackInterval * 0.95, now.AttackInterval, 9);
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>没有消耗型改装的武器，<c>ConsumeUse</c> 是**彻底的空操作**（永久改装砍一万下也不掉）。</summary>
    [Fact]
    public void 永久改装砍多少下都不掉()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            string name = ModdedWeaponRegistry.Register(
                WeaponMods.ApplyMods(WeaponTable.Rifle(), new[] { WeaponModCatalog.TraumaStock() }));

            for (int i = 0; i < 100; i++)
            {
                Assert.False(ModdedWeaponRegistry.ConsumeUse(name).Changed);
            }
            Assert.NotNull(ModdedWeaponRegistry.WeaponByName(name));
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>原厂武器名 / 根本没登记过的名字喂进来 ⇒ 不崩、不改任何东西。</summary>
    [Fact]
    public void ConsumeUse对原厂武器与未知名字是空操作()
    {
        ModdedWeaponRegistry.Clear();
        Assert.False(ModdedWeaponRegistry.ConsumeUse("短剑").Changed);
        Assert.False(ModdedWeaponRegistry.ConsumeUse("根本不存在的东西").Changed);
        Assert.False(ModdedWeaponRegistry.ConsumeUse(null).Changed);
    }

    // ═══════════════════ 5. 存档 v3：剩余次数往返 ═══════════════════

    /// <summary>
    /// **存档往返**：砍过一下的刀，读档回来还剩 2 次（不是回满 3 次 —— 那等于免费续刀）。
    /// 走的是真存档结构（<c>SaveMapper.CaptureModdedWeapons</c> / <c>RestoreModdedWeapons</c>），不是内存直传。
    /// </summary>
    [Fact]
    public void 存档往返_剩余次数原样带回来_不回满()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            string name = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed()));
            ModdedWeaponRegistry.ConsumeUse(name);   // 砍了一下 ⇒ 还剩 2

            List<ModdedWeaponSave> saved = SaveMapper.CaptureModdedWeapons();
            Assert.Single(saved);
            Assert.Equal(2, saved[0].RemainingUses!["锋刃研磨"]);

            // 退出 → 读档
            ModdedWeaponRegistry.Clear();
            SaveMapper.RestoreModdedWeapons(saved);

            Assert.Equal(2, ModdedWeaponRegistry.RemainingUses(name, "锋刃研磨"));
            Weapon? w = ModdedWeaponRegistry.WeaponByName(name);
            Assert.NotNull(w);
            Assert.Equal(WeaponTable.Shortsword().Penetration * 1.75, w!.Penetration, 6);   // 研磨还在

            // 读档后再砍两下 ⇒ 正好脱落（而不是还能砍三下）
            Assert.False(ModdedWeaponRegistry.ConsumeUse(name).Changed);
            Assert.True(ModdedWeaponRegistry.ConsumeUse(name).Changed);
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>
    /// 🔴 <b>老存档兼容</b>：v3 之前根本没有消耗型改装 ⇒ 老档里 <c>RemainingUses</c> 是 <c>null</c>。
    /// **必须补成满次数，不是 0** —— 默认 0 会让老档一读进来，所有研磨过的刀当场全部脱落（凭空没收玩家的东西）。
    /// </summary>
    [Fact]
    public void 老存档没有剩余次数字段_补成满次数而不是零()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            // 老档长这样：三个字符串，没有 RemainingUses
            var old = new List<ModdedWeaponSave>
            {
                new() { VariantName = "短剑（锋刃研磨）#1", BaseWeaponName = "短剑", ModNames = new() { "锋刃研磨" } },
            };
            Assert.Null(old[0].RemainingUses);

            SaveMapper.RestoreModdedWeapons(old);

            Assert.Equal(3, ModdedWeaponRegistry.RemainingUses("短剑（锋刃研磨）#1", "锋刃研磨"));
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>
    /// 🔴 <b>读档后新造的刀不许顶掉存档里的刀</b>（实例序号要往前推）。
    /// 这与 <c>impl-traps</c> 踩过的 <c>_trapSeq</c> 是同一个坑：读旧档后新家具撞名顶掉旧的。
    /// </summary>
    [Fact]
    public void 读档后新磨的刀_不会撞名顶掉存档里已有的刀()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            SaveMapper.RestoreModdedWeapons(new List<ModdedWeaponSave>
            {
                new() { VariantName = "短剑（锋刃研磨）#1", BaseWeaponName = "短剑", ModNames = new() { "锋刃研磨" } },
                new() { VariantName = "短剑（锋刃研磨）#2", BaseWeaponName = "短剑", ModNames = new() { "锋刃研磨" } },
            });

            string fresh = ModdedWeaponRegistry.Register(Mod(Shortsword(), Honed()));

            Assert.NotEqual("短剑（锋刃研磨）#1", fresh);
            Assert.NotEqual("短剑（锋刃研磨）#2", fresh);
            Assert.Equal(3, ModdedWeaponRegistry.Specs.Count);            // 三把刀都在，一把没被顶掉
            Assert.NotNull(ModdedWeaponRegistry.WeaponByName("短剑（锋刃研磨）#1"));
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    // ═══════════════════ 6. 重量：改装的增减重必须真的进负重账 ═══════════════════

    /// <summary>
    /// 🔴 用户原话：「<b>我希望重量在改装中是一个重要的因素</b>」。
    /// 这条钉死重量**不是一个只存不用的 flavor 字段** —— 它真的乘进武器重量。
    /// </summary>
    [Fact]
    public void 改装的重量真的改武器实重_而不是只存不用()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            double plainRifle = ItemWeights.WeaponKg("步枪");
            Assert.Equal(7.5, plainRifle, 6);   // [carryweight2] 步枪基础重 4.0→7.5（用户 wiki 翻倍）

            // 创伤型 +50%
            string trauma = ModdedWeaponRegistry.Register(
                WeaponMods.ApplyMods(WeaponTable.Rifle(), new[] { WeaponModCatalog.TraumaStock() }));
            Assert.Equal(plainRifle * 1.50, ItemWeights.WeaponKg(trauma), 6);

            // 轻质化枪托 −15%（减重是它唯一的收益，必须真的减）
            string light = ModdedWeaponRegistry.Register(
                WeaponMods.ApplyMods(WeaponTable.Rifle(), new[] { WeaponModCatalog.LightenedStock() }));
            Assert.Equal(plainRifle * 0.85, ItemWeights.WeaponKg(light), 6);
            Assert.True(ItemWeights.WeaponKg(light) < plainRifle, "轻质化枪托必须真的让枪变轻");
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>
    /// 多条改装的重量**连乘**（百分比一律乘算，CLAUDE.md 铁律）—— [carryweight2·枪重翻倍后] 满改装步枪 <b>7.5 → 15.1875kg</b>。
    ///
    /// <para>
    /// 🔴 <b>改装的代价形态 = 「它吃掉你的搜刮余量」，不是"出门即 debuff"</b>（用户拍板：三条线就是 30/50/80，不改）：
    /// 满改装步枪出门（中期配置 24.99kg）**仍在 30kg 免罚线下、一分不罚**（余量只剩 5kg）；硬余量从 62.7 掉到 55.0kg
    /// ⇒ 枪重翻倍后原厂中期步枪已搬不空最大点位（66kg），满改装再多留 7.69kg。「要么带甲带枪、要么带货」由此成立。
    /// </para>
    /// <para>
    /// 增重乘子沿用创伤型枪托 +50% × 加长枪管 +35%（wiki）；步枪基础重由 4.0 翻倍到 7.5（用户 wiki 手改，
    /// carryweight2 落地）⇒ 满改装实重 = 7.5 × 1.5 × 1.35 = <b>15.1875kg</b>。
    /// </para>
    /// </summary>
    [Fact]
    public void 多条改装的重量连乘_满改装步枪十五点一九公斤()
    {
        ModdedWeaponRegistry.Clear();
        try
        {
            // 最重的合法组合：创伤型（枪托 +50%）× 加长枪管（枪管 +35%）
            // 刺刀占枪口，但它的近战型态与创伤型冲突 ⇒ 一把枪只能有一种型态，故这就是上限。
            string heavy = ModdedWeaponRegistry.Register(WeaponMods.ApplyMods(
                WeaponTable.Rifle(),
                new[] { WeaponModCatalog.TraumaStock(), WeaponModCatalog.ExtendedBarrel() }));

            Assert.Equal(7.5 * 1.50 * 1.35, ItemWeights.WeaponKg(heavy), 6);   // = 15.1875kg
            Assert.Equal(15.1875, ItemWeights.WeaponKg(heavy), 6);
            Assert.True(ItemWeights.WeaponKg(heavy) > ItemWeights.WeaponKg("步枪") * 2 * 0.9,
                "满改装步枪应该重到玩家能感觉出来——这正是用户要的核心代价");
        }
        finally { ModdedWeaponRegistry.Clear(); }
    }

    /// <summary>原厂武器 / 未登记名 ⇒ 重量倍率恒 1.0（既有负重算式**零变化**）。</summary>
    [Fact]
    public void 原厂武器的重量倍率恒为一_既有负重零回归()
    {
        ModdedWeaponRegistry.Clear();
        Assert.Equal(1.0, ModdedWeaponRegistry.WeightMultiplierOf("步枪"), 9);
        Assert.Equal(1.0, ModdedWeaponRegistry.WeightMultiplierOf("短剑"), 9);
        Assert.Equal(1.0, ModdedWeaponRegistry.WeightMultiplierOf("没听说过的东西"), 9);
        Assert.Equal(1.0, ModdedWeaponRegistry.WeightMultiplierOf(null), 9);

        Assert.Equal(7.5, ItemWeights.WeaponKg("步枪"), 6);   // [carryweight2] 步枪基础重 4.0→7.5
        Assert.Equal(1.6, ItemWeights.WeaponKg("短剑"), 6);   // wiki 同步（1.2 → 1.6）
    }

    /// <summary>
    /// 全表 14 条改装的重量倍率必须落在 wiki 写的区间 <b>[0.75, 1.50]</b> 内，且**必须是正数**
    /// （0 或负数会把武器变成失重的，负重账当场失真）。
    /// </summary>
    [Fact]
    public void 全表改装的重量倍率都在合法区间内()
    {
        foreach (WeaponMod m in WeaponModCatalog.All())
        {
            Assert.True(m.WeightMultiplier >= 0.75 && m.WeightMultiplier <= 1.50,
                $"改装「{m.Name}」的重量倍率 {m.WeightMultiplier} 超出了用户表上的区间 [0.75, 1.50]");
        }
    }

    // ═══════════════════ 7. 🔴 覆盖自检：接线源码扫描（反"纯逻辑绿≠功能生效"）═══════════════════

    /// <summary>
    /// 🔴 <b>本项目有一个反复发作的失效模式：纯逻辑全绿、消费层从没接线 ⇒ 功能根本不存在。</b>
    /// （改装枪曾经装不上身，因为 <c>Pawn</c> 里的 <c>WeaponCatalog</c> 只含原厂武器 —— 而单测全绿。）
    ///
    /// <para>
    /// 消耗型改装的整条链有**三段在 Godot 类型里**（<c>Actor</c> / <c>Pawn</c> / <c>CampMain</c> 都引了 Godot、
    /// 进不了单测）：<b>打了一下 → 掉一次 → 脱落 → 换武器 + 报给玩家</b>。
    /// 任何一段没接，上面那 16 条纯逻辑测试**照样全绿**，而游戏里的刀永远磨不钝。
    /// </para>
    /// <para>
    /// 所以这里做**源码级接线扫描**（沿用 <c>ModdedWeaponEquipTests.Pawn源码不得再自建原厂武器字典</c> 的范式）：
    /// 谁把这三段里的任何一段删掉/改名，这条立刻红。
    /// </para>
    /// </summary>
    [Fact]
    public void 接线自检_消耗型改装的三段消费层都真的接上了()
    {
        string actor = CodeOf("Actor.cs");
        string pawn = CodeOf("Pawn.cs");
        string camp = CodeOf("CampMain.cs");

        // ① Actor：攻击真的打出去之后，通知子类（钩子必须**被调用**，不能只是声明了一个空方法）
        Assert.Contains("OnAttackDelivered(AttackWeapon)", actor);
        Assert.Contains("protected virtual void OnAttackDelivered", actor);

        // ② Pawn：覆写钩子 → 真的去扣次数 → 脱落时换掉手上的武器 + 抛事件
        Assert.Contains("protected override void OnAttackDelivered", pawn);
        Assert.Contains("ModdedWeaponRegistry.ConsumeUse", pawn);
        Assert.Contains("WeaponModBroken", pawn);

        // ③ CampMain：真的订阅了那个事件（不订阅 ⇒ 改装静默脱落，玩家一脸问号）
        Assert.Contains("WeaponModBroken += OnWeaponModBroken", camp);
        Assert.Contains("private void OnWeaponModBroken", camp);
        // 且真的报给玩家看（"不能静默失效"是这条设计的硬要求）
        Assert.Contains("_campToast.Show", camp[camp.IndexOf("private void OnWeaponModBroken", System.StringComparison.Ordinal)..]);
    }

    /// <summary>读一个 godot/scripts 源文件，**剔除注释行**——注释里提到某个名字不算"接线"。</summary>
    private static string CodeOf(string fileName)
    {
        string path = GodotScript(fileName);
        return string.Join("\n", File.ReadAllLines(path).Where(l =>
        {
            string t = l.TrimStart();
            return !t.StartsWith("//") && !t.StartsWith("*") && !t.StartsWith("///");
        }));
    }

    private static string GodotScript(string fileName, [CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "godot", "scripts", fileName)))
        {
            dir = dir.Parent;
        }
        Assert.True(dir is not null, $"找不到 godot/scripts/{fileName}");
        return Path.Combine(dir!.FullName, "godot", "scripts", fileName);
    }
}
