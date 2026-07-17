using DeadSignal.Combat;
using DeadSignal.Godot;

namespace DeadSignal.Combat.Tests;

/// <summary>
/// 武器改装模型 + 合成逻辑：单改装数值变化、多部位叠加、同部位冲突拒绝、不适用武器拒绝、
/// 锐击枪托型（刺刀/利爪）与钝击型（创伤）枪托改装。数值皆 draft，测试只校验方向/形态，不锁死具体数。
/// </summary>
public sealed class WeaponModTests
{
    // ---- 大类派生 ----

    [Fact]
    public void ClassOf_DerivesFromExistingFields()
    {
        Assert.Equal(WeaponClass.Firearm, WeaponMods.ClassOf(WeaponTable.Rifle()));
        Assert.Equal(WeaponClass.Blade, WeaponMods.ClassOf(WeaponTable.Shortsword()));
        Assert.Equal(WeaponClass.Blunt, WeaponMods.ClassOf(WeaponTable.Club()));
    }

    // ---- 单改装数值变化 ----

    /// <summary>
    /// 锋刃研磨：<b>穿透 ×1.75（乘算）</b>，其余不动，且不碰原 base 对象。
    /// <para>[T47] 用户把它从"加算 +0.05"改成了"**+75%**"（乘算）——短剑 0.12 → 0.21。</para>
    /// </summary>
    [Fact]
    public void SingleMod_ChangesTargetedStats_LeavesBaseUntouched()
    {
        var baseSword = WeaponTable.Shortsword();
        var result = WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.HonedEdge() });

        // 研磨：穿透 +75%（乘算，不是加一个绝对值）
        Assert.Equal(baseSword.Penetration * 1.75, result.Weapon.Penetration, 6);
        // 其余不变
        Assert.Equal(baseSword.DamageMax, result.Weapon.DamageMax, 6);
        Assert.Equal(baseSword.AttackInterval, result.Weapon.AttackInterval, 6);
        // 原 base 未被改动
        Assert.Equal(0.12, baseSword.Penetration, 6);
    }

    /// <summary>
    /// 加重剑柄：伤害 +6%，<b>重量 +18%</b>。
    /// <para>⚠️ [T47] <b>用户删掉了它原有的攻速惩罚</b> —— 它的代价现在**是重量**（重量已真的进负重账）。
    /// 这条改钉新意图：伤害↑、重量↑、<b>攻速一格不动</b>。谁把攻速惩罚加回来，这里会红。</para>
    /// </summary>
    [Fact]
    public void WeightedHandle_RaisesDamage_CostIsWeight_NotAttackSpeed()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.WeightedHandle() });

        Assert.Equal(baseSword.DamageMin * 1.06, r.Weapon.DamageMin, 6);
        Assert.Equal(baseSword.DamageMax * 1.06, r.Weapon.DamageMax, 6);
        Assert.Equal(baseSword.AttackInterval, r.Weapon.AttackInterval, 6);   // 攻速不再是代价
        Assert.Equal(1.18, WeaponModCatalog.WeightedHandle().WeightMultiplier, 6);   // 代价在这
    }

    /// <summary>
    /// 轻质化剑柄：攻速 +3%，<b>重量 −12%</b>。
    /// <para>⚠️ [T47] <b>用户删掉了它原有的伤害惩罚</b> —— 减重本身就是它的收益（负重账已接通）。
    /// 这条改钉新意图：攻速↑、重量↓、<b>伤害一格不动</b>。</para>
    /// </summary>
    [Fact]
    public void LightenedHandle_RaisesAttackSpeed_AndCutsWeight_NoDamagePenalty()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.LightenedHandle() });

        Assert.Equal(baseSword.AttackInterval * 0.97, r.Weapon.AttackInterval, 6);   // 攻速 +3% ⇒ 间隔 ×0.97
        Assert.Equal(baseSword.DamageMax, r.Weapon.DamageMax, 6);                    // 伤害不再是代价
        Assert.Equal(0.88, WeaponModCatalog.LightenedHandle().WeightMultiplier, 6);  // 收益在这（−12%）
    }

    [Fact]
    public void SawnOffBarrel_ShortensRange_WidensSpread()
    {
        var baseGun = WeaponTable.Rifle();
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.SawnOffBarrel() });

        Assert.True(r.Weapon.MaxRange < baseGun.MaxRange);
        Assert.True(r.Weapon.BaseSpreadDegrees > baseGun.BaseSpreadDegrees);
    }

    [Fact]
    public void ExtendedBarrel_LengthensRange_TightensSpread()
    {
        var baseGun = WeaponTable.Rifle();
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.ExtendedBarrel() });

        Assert.True(r.Weapon.MaxRange > baseGun.MaxRange);
        Assert.True(r.Weapon.BaseSpreadDegrees < baseGun.BaseSpreadDegrees);
    }

    /// <summary>
    /// 钉子强化：<b>穿透 +0.03（加算）</b>。
    /// <para>🔴 <b>加算是 CLAUDE.md 乘算铁律的唯一例外，用户亲自点名的</b>：
    /// 「钉子强化：穿透 +0.03 是因为**棍棒原本是 0 穿透**」——乘算在零上永远是零（零陷阱）。
    /// 谁把它改成 <c>Mul</c>，这条改装当场变成废件，本测试红。</para>
    /// <para>⚠️ [T47] 用户把它的**伤害加成删掉了**（改成"25% 几率造成小流血"，那条是引擎新轴、尚未落地）。
    /// 故不再断言伤害↑ —— 它现在只加穿透。</para>
    /// </summary>
    [Fact]
    public void NailStuds_OnClub_AddsPenetration()
    {
        var baseClub = WeaponTable.Club();
        var r = WeaponMods.ApplyMods(baseClub, new[] { WeaponModCatalog.NailStuds() });

        Assert.Equal(0.0, baseClub.Penetration, 9);          // 前提：棍棒穿透本来就是 0（零陷阱的成因）
        Assert.Equal(0.03, r.Weapon.Penetration, 9);         // 加算：0 + 0.03
        Assert.True(r.Weapon.Penetration > baseClub.Penetration, "钉尖必须真的能破一点甲");
    }

    /// <summary>
    /// 🔴 <b>钉子强化与铁丝强化占的是<b>两个不同部位</b>，可以一起装。</b>
    ///
    /// <para><b>用户在 wiki 上定的（wiki＝唯一设计源，代码向它看齐）</b>：
    /// 铁丝强化 part＝<b>棍棒上部</b>（缠在棍身那一段）、钉子强化 part＝<b>棍棒顶端</b>（砸在棍头那一圈）——
    /// 一个缠杆、一个钉头，物理上本来就不打架。用户原话：「钉子和铁丝强化<b>不占用同一个槽，可以一起安装</b>」。</para>
    ///
    /// <para>
    /// ⚠️ <b>旧代码把两者都塞在 <c>WeaponPart.Shaft</c>（"杆/头强化"）里 ⇒ 二选一</b>，
    /// 那是**已作废的设计**。它的后果不是"少一个选项"：铁丝（伤害 +15%）会把钉子（穿透 +0.03 + 25% 小流血）
    /// <b>完全支配</b> —— 实测打长剑手 +15.4pp vs +0.1pp（＝噪声）⇒ 钉子成了永远不会有人选的死配方。
    /// 分槽之后两者不再互相排挤，钉子买的是**流血与破甲**、铁丝买的是**伤害**，各自成立。
    /// </para>
    /// </summary>
    [Fact]
    public void 钉子与铁丝不占同一个槽_可以一起装在同一根棍棒上()
    {
        Weapon baseClub = WeaponTable.Club();

        WeaponMod nails = WeaponModCatalog.NailStuds();
        WeaponMod wire = WeaponModCatalog.WireWrap();

        // ① 两者部位不同 —— 这就是"能一起装"的全部依据（冲突判据走 Part 枚举相等）
        Assert.NotEqual(nails.Part, wire.Part);

        // ② 同时装不抛（旧设计在这里抛 WeaponModException「部位『杆』已有改装」）
        ModdedWeapon r = WeaponMods.ApplyMods(baseClub, new[] { nails, wire });

        // ③ 两者的效果都真的落到了武器上：钉子的穿透（加算·零陷阱）+ 铁丝的伤害（乘算）
        Assert.Equal(0.03, r.Weapon.Penetration, 9);
        Assert.True(r.Weapon.DamageMin > baseClub.DamageMin, "铁丝的伤害加成没落上");
        Assert.True(r.Weapon.BleedOnHitChance > 0.0, "钉子的小流血没落上");

        // ④ 再加防滑缠手（缠手＝第三个部位）⇒ 满改装棍棒＝三件全上，一样不冲突
        Weapon full = WeaponMods.ApplyMods(baseClub, new[] { nails, wire, WeaponModCatalog.GripWrapBlade() }).Weapon;
        Assert.True(full.AttackInterval < baseClub.AttackInterval, "防滑缠手的攻速没落上");
        Assert.Equal(0.03, full.Penetration, 9);
    }

    // ---- 多部位叠加 ----

    [Fact]
    public void MultipleMods_DifferentParts_AllApplied()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, new[]
        {
            WeaponModCatalog.SerratedBlade(),   // 刃
            WeaponModCatalog.WeightedHandle(),  // 柄
            WeaponModCatalog.GripWrapBlade(),   // 缠手
        });

        // 三部位各自的效果都体现
        Assert.True(r.Weapon.DamageMax > baseSword.DamageMax);   // 锯齿 + 加重
        Assert.True(r.Weapon.DamageMin > baseSword.DamageMin);   // 加重
        Assert.Equal(3, r.AppliedMods.Count);
        Assert.Contains("锯齿剑刃", r.Weapon.Name);
        Assert.Contains("防滑缠手", r.Weapon.Name);
    }

    [Fact]
    public void GunMultiPart_Barrel_Muzzle_Stock_Coexist()
    {
        var baseGun = WeaponTable.Rifle();
        // 加长枪管(Barrel) + 刺刀(Muzzle) + 轻质化枪托(Stock)：三个不同部位并存
        var r = WeaponMods.ApplyMods(baseGun, new[]
        {
            WeaponModCatalog.ExtendedBarrel(),
            WeaponModCatalog.Bayonet(),
            WeaponModCatalog.LightenedStock(),
        });

        Assert.Equal(3, r.AppliedMods.Count);
        Assert.True(r.Weapon.MaxRange > baseGun.MaxRange);        // 加长枪管
        // 刺刀＝锐击枪托。型态已**烧进 Weapon 自身**（不再是旁挂 override）——
        // 正因如此，这把枪入库/装备/存档之后，刺刀才还在（旧的旁挂对象一入库就丢，是 P0-C）。
        Assert.Equal(MeleeForm.Bayonet, r.Form);
        Assert.Equal(DamageType.Sharp, r.Weapon.MeleeProfile()!.DamageType);
    }

    // ---- 同部位冲突拒绝 ----

    [Fact]
    public void SamePart_Twice_Throws()
    {
        var baseSword = WeaponTable.Shortsword();
        var ex = Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseSword, new[]
            {
                WeaponModCatalog.WeightedHandle(),   // 柄
                WeaponModCatalog.LightenedHandle(),  // 柄 —— 冲突
            }));
        Assert.Contains("柄", ex.Message);
    }

    [Fact]
    public void SameBladePart_Twice_Throws()
    {
        var baseSword = WeaponTable.Shortsword();
        Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseSword, new[]
            {
                WeaponModCatalog.SerratedBlade(),  // 刃
                WeaponModCatalog.HonedEdge(),      // 刃 —— 冲突
            }));
    }

    // ---- 不适用武器拒绝 ----

    [Fact]
    public void GunMod_OnMeleeWeapon_Throws()
    {
        var baseSword = WeaponTable.Shortsword();
        var ex = Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseSword, new[] { WeaponModCatalog.SawnOffBarrel() }));
        // [SPEC-B21] 报错文案随白名单改了口径：不再说"需某某大类"（用户根本不关心大类），
        // 而是直接告诉你**它到底能装哪几把**——这才是他下一步要用的信息。
        Assert.Contains("装不到", ex.Message);
        Assert.Contains(baseSword.Name, ex.Message);
        Assert.Contains("它只能装", ex.Message);
    }

    [Fact]
    public void BladeMod_OnClub_Throws()
    {
        var baseClub = WeaponTable.Club();
        Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseClub, new[] { WeaponModCatalog.SerratedBlade() }));
    }

    [Fact]
    public void BluntMod_OnFirearm_Throws()
    {
        var baseGun = WeaponTable.Rifle();
        Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.WireWrap() }));
    }

    // ---- 锐击/钝击枪托型改装 ----

    /// <summary>
    /// 刺刀型：枪托近战 = <b>80% 攻速的刺剑</b>（锐击、穿透 25%、捅得快）。
    /// <para>⚠️ [T47] <b>不再断言"单击伤害↑"</b> —— 新口径下刺刀的单击伤害就是刺剑的 2~7，
    /// 与步枪原厂枪托的 4~7 <b>上限持平、下限更低</b>。它赢在**快**（间隔 2.375s vs 3.0s）⇒ 每秒伤害更高。
    /// 拿单击伤害去断言会误报，正确的轴是 <b>DPS</b>。</para>
    /// </summary>
    [Fact]
    public void Bayonet_MakesStockMeleeSharp_HigherDpsAndPenetration()
    {
        var baseGun = WeaponTable.Rifle();
        var defaultMelee = baseGun.MeleeProfile();
        Assert.NotNull(defaultMelee);
        Assert.Equal(DamageType.Blunt, defaultMelee!.DamageType); // 默认枪托钝击

        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.Bayonet() });
        var eff = r.EffectiveMeleeProfile();

        Assert.NotNull(eff);
        Assert.Equal(DamageType.Sharp, eff!.DamageType);                          // 刺刀锐击
        Assert.True(eff.Penetration > defaultMelee.Penetration);                  // 穿透↑（0.25 vs 0.03）
        Assert.True(eff.AttackInterval < defaultMelee.AttackInterval);            // 捅比抡快
        Assert.True(WeaponDps.Single(eff) > WeaponDps.Single(defaultMelee));      // 每秒伤害↑（不看单击）
        Assert.False(eff.IsRanged);                                               // 近战必中
    }

    [Fact]
    public void ClawStock_MakesStockMeleeSharp()
    {
        var baseGun = WeaponTable.Rifle();
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.ClawStock() });
        var eff = r.EffectiveMeleeProfile();

        Assert.NotNull(eff);
        Assert.Equal(DamageType.Sharp, eff!.DamageType);
    }

    [Fact]
    public void TraumaStock_KeepsBluntButHitsHarderAndSlower()
    {
        var baseGun = WeaponTable.Rifle();
        var defaultMelee = baseGun.MeleeProfile()!;
        var r = WeaponMods.ApplyMods(baseGun, new[] { WeaponModCatalog.TraumaStock() });
        var eff = r.EffectiveMeleeProfile();

        Assert.NotNull(eff);
        Assert.Equal(DamageType.Blunt, eff!.DamageType);           // 铁锤仍钝击（型态 Trauma → Blunt）
        Assert.Equal(MeleeForm.Trauma, r.Form);
        Assert.True(eff.DamageMax > defaultMelee.DamageMax);       // 伤↑
        Assert.True(eff.AttackInterval > defaultMelee.AttackInterval); // 更慢
    }

    [Fact]
    public void TwoSharpStockMods_Conflict_Throws()
    {
        // 刺刀(Muzzle)与利爪(Stock)部位不同、可过部位校验，但两者都是**近战型态** → 一把枪只能有一种，拒绝
        var baseGun = WeaponTable.Rifle();
        var ex = Assert.Throws<WeaponModException>(() =>
            WeaponMods.ApplyMods(baseGun, new[]
            {
                WeaponModCatalog.Bayonet(),
                WeaponModCatalog.ClawStock(),
            }));
        Assert.Contains("近战型态", ex.Message);
    }

    // ---- 合成不改动 base、无改装即原样 ----

    [Fact]
    public void EmptyMods_ProducesEquivalentWeapon()
    {
        var baseSword = WeaponTable.Shortsword();
        var r = WeaponMods.ApplyMods(baseSword, System.Array.Empty<WeaponMod>());

        Assert.Equal(baseSword.Name, r.Weapon.Name);
        Assert.Equal(baseSword.DamageMax, r.Weapon.DamageMax, 6);
        Assert.Equal(baseSword.Penetration, r.Weapon.Penetration, 6);
    }

    [Fact]
    public void ImplicitConversion_ToWeapon_Works()
    {
        Weapon w = WeaponMods.ApplyMods(WeaponTable.Shortsword(), new[] { WeaponModCatalog.HonedEdge() });
        Assert.Contains("锋刃研磨", w.Name);
    }

    // ---- 目录 ----

    [Fact]
    public void Catalog_For_ReturnsClassAppropriateMods()
    {
        // [SPEC-B21] 装配约束已从「大类」换成「逐把枪的白名单」：断言改为"步枪在白名单里"
        Assert.All(WeaponModCatalog.For(WeaponTable.Rifle()),
            m => Assert.Contains(WeaponTable.Rifle().Name, m.FitsWeapons));
        Assert.All(WeaponModCatalog.For(WeaponTable.Club()),
            m => Assert.Contains(WeaponTable.Club().Name, m.FitsWeapons));
        Assert.NotEmpty(WeaponModCatalog.All());
    }
}
